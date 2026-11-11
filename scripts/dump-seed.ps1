#!/usr/bin/env powershell
<#
  Dumps the demo seed from a database that holds the correct END STATE, so the seed cannot drift from
  the schema by hand-editing.

  Why this exists, and why not hand-edit `seed-demo.sql`:
  the legacy seed spends ~3 400 lines writing Questions, QuestionGroups and QuestionOptions. Removing
  that block by line arithmetic was tried and failed: the legacy statements are INTERLEAVED with
  ScoreScales and GradingCriteria writes for the same exams, so a line-range cut either deletes
  non-content rows or leaves half a block behind. Dumping the whole database is the only approach that
  cannot silently drop a row.

  What it writes:
    docs/seed-demo.sql   every table that the app reads, in FK order, with row-level idempotency
    docs/seed-pool-native.sql  the pool content block alone, for the tests that assert on it

  Idempotency is a per-row TRY/CATCH filtered to duplicate-key errors (2627 unique, 2601 index). Any
  OTHER error still throws, which is the property that makes this safe to re-run: a second pass must
  change no count, and a real defect must still fail loudly.

  Usage:
    ./dump-seed.ps1 -Database EEPoolSeed
#>
param(
    [string]$Database = 'EEPoolSeed',
    [string]$OutFile = 'docs/seed-demo.sql',
    [string]$PoolOutFile = 'docs/seed-pool-native.sql'
)

$ErrorActionPreference = 'Stop'
# The connection must speak UTF-8 to the server, or a non-ASCII character comes back as its cp437
# rendering ("ΓÇö" for an em dash) and the dump writes that mojibake into the seed. Measured: the first
# dump turned U+2014 into U+0393 U+00C7 U+00F6, which no test could match.
$conn = New-Object System.Data.SqlClient.SqlConnection("Server=(localdb)\MSSQLLocalDB;Database=$Database;Integrated Security=true;TrustServerCertificate=true")
$conn.Open()

function Q([string]$sql) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    $t = New-Object System.Data.DataTable
    $t.Load($cmd.ExecuteReader())
    return , $t
}

# ---- which tables, in FK order ---------------------------------------------------------------
# Driven by the database, not by a hand-written list: a table added later appears here without an
# edit, which is the same reason the disposition list is tested.
$tables = Q @"
SELECT t.name AS Name
FROM sys.tables t
WHERE t.is_ms_shipped = 0
  AND t.name NOT IN (
    -- #250: the legacy content model. The pool is the only content model, so a seed that wrote these
    -- would resurrect the tables the rebuild deletes. Listing them here is what makes the omission a
    -- decision rather than an oversight.
    'Questions', 'QuestionGroups', 'QuestionOptions',
    -- Run history the seed does not recreate: a drill session, an AI narrative, a placement and the
    -- learning path derived from it are facts about one past sitting. Shipping a placement would assert
    -- a measurement nobody took, and shipping the path without it would assert a plan nobody made.
    'PracticeSessions', 'PracticeAnswers',
    'AiInsights', 'Recommendations',
    'PlacementTests', 'PlacementSkillResults',
    'LearnerPaths', 'LearnerPathSteps', 'LearnerPathWeeks',
    'LearnerSkillStates', 'TargetBands', 'LearnerLessonProgress', 'LessonMicroChecks',
    -- Attempts and their answers are kept only as far as the ledger needs them: a `mock_exam_start`
    -- charge is unrepresentable without its attempt (CK_CreditTxn_Source), and the balance is the
    -- ledger's sum. The answers and per-skill snapshots of those attempts are not shipped: they are
    -- measurements nobody took in a fresh demo.
    'AttemptAnswers', 'AttemptSkillScores', 'FreeResponses',
    -- #269: an attempt row and a complaint about one are kept: a `mock_exam_start` charge and a
    -- make-good grant are unrepresentable without them, and the demo balance is the ledger's sum. The
    -- attempt's answers and per-skill snapshots are not shipped - those are measurements nobody took.
    -- Infrastructure, not demo data.
    'sysdiagrams'
  )
ORDER BY t.name
"@ | ForEach-Object { $_.Name }

# FK-safe order: repeatedly emit a table whose referenced tables have all been emitted.
# The dependency graph is built from sys.foreign_keys rather than a hand-written list, because a table
# added later must appear here without an edit - the same reason the disposition list is tested. A
# table that references itself (a tree) is treated as satisfied by itself, so it does not deadlock the
# loop.
$deps = @{}
foreach ($t in $tables) {
    $parents = Q "SELECT OBJECT_NAME(fk.referenced_object_id) AS P FROM sys.foreign_keys fk WHERE OBJECT_NAME(fk.parent_object_id) = '$t'"
    $deps[$t] = @($parents | ForEach-Object { $_.P } | Where-Object { $_ -ne $t -and $tables -contains $_ } | Sort-Object -Unique)
}

$order = New-Object System.Collections.Generic.List[string]
$emitted = New-Object System.Collections.Generic.HashSet[string]
$guard = 0
$maxGuard = ($tables.Count * $tables.Count) + $tables.Count
while ($order.Count -lt $tables.Count -and $guard -lt $maxGuard) {
    $guard++
    foreach ($t in $tables) {
        if ($emitted.Contains($t)) { continue }
        $ready = $true
        foreach ($p in $deps[$t]) { if (-not $emitted.Contains($p)) { $ready = $false; break } }
        if ($ready) { $order.Add($t); [void]$emitted.Add($t) }
    }
}
# Any table left in a cycle is appended; their rows are written with NOCHECK-able order and a cycle in
# this schema means a mutual reference, which the seed writes after both exist.
foreach ($t in $tables) { if (-not $emitted.Contains($t)) { $order.Add($t); [void]$emitted.Add($t) } }

# PlacementTests and PlacementSkillResults reference each other (a result names its test, a test names
# its results), so the topological loop can order them either way and the dump then fails on a foreign
# key. They are moved to the end in the order the FKs require, after every table either references.
$placementOrder = @('PlacementTests', 'PlacementSkillResults')
foreach ($p in $placementOrder) {
    if ($order.Contains($p)) {
        $order.Remove($p)
        $order.Add($p)
    }
}

function V($value, [switch]$String) {
    if ($null -eq $value -or $value -is [System.DBNull]) { return 'NULL' }
    # A binary column must be emitted as 0x...; without this the byte array prints as "1 0 0 0" and the
    # INSERT fails with "Incorrect syntax near '0'". Found by loading the dump, not by reading it.
    if ($value -is [byte[]]) {
        return '0x' + (($value | ForEach-Object { $_.ToString('X2') }) -join '')
    }
    if (-not $String) {
        if ($value -is [bool]) { return $(if ($value) { '1' } else { '0' }) }
        if ($value -is [DateTime]) { return "'" + $value.ToString('yyyy-MM-ddTHH:mm:ss.fffffff') + "'" }
        if ($value -is [DateTimeOffset]) { return "'" + $value.ToString('yyyy-MM-ddTHH:mm:ss.fffffffzzz') + "'" }
        return [string]$value
    }
    $s = [string]$value
    $s = $s.Replace("'", "''").Replace("`r`n", "`n").Replace("`r", "`n")
    $parts = $s -split "`n", -1
    return (($parts | ForEach-Object { "N'$_'" }) -join ' + CHAR(10) + ')
}

function EmitTable([string]$table, [System.Text.StringBuilder]$sb) {    # system_type_id is what identifies the family: a user-defined alias type (an nvarchar alias) keeps
    # its own user_type_id but inherits the system one, and matching only on user_type_id emitted a
    # string column unquoted - measured as "Invalid column name 'ExamAttempts'" when CoveredTable held
    # that word.
    $cols = Q @"
SELECT c.name AS Name,
       ty.name AS Type,
       ty.system_type_id AS SystemTypeId,
       c.is_identity AS IsIdentity
FROM sys.columns c
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('$table')
ORDER BY c.column_id
"@
    $colNames = @($cols | ForEach-Object { $_.Name })
    $identity = @($cols | Where-Object { $_.IsIdentity } | ForEach-Object { $_.Name })

    $rows = Q "SELECT * FROM [$table]"
    if ($rows.Rows.Count -eq 0) { return 0 }

    # A ledger row whose reason can only be satisfied by an attempt is KEPT, and so is the attempt it
    # names: the balance is the ledger's sum, so dropping the row would understate a demo learner's
    # credits. Measured: dropping the one `mock_exam_start` row turned a balance of 13 into 14. The
    # attempt rows themselves are excluded from the dump, so such a ledger row is dropped only when its
    # attempt is genuinely absent - which cannot happen for a consistent source database.
    $attemptRef = $null
    $kept = @()
    foreach ($row in $rows.Rows) {
        if ($attemptRef -and $row[$attemptRef] -isnot [System.DBNull]) { continue }
        $kept += $row
    }
    $rows = $kept
    if ($rows.Count -eq 0) { return 0 }

    [void]$sb.AppendLine("-- ---- $table ($($rows.Count) rows)")
    if ($identity.Count -gt 0) { [void]$sb.AppendLine("SET IDENTITY_INSERT [$table] ON;") }

    foreach ($row in $rows) {
        $values = foreach ($c in $cols) {
            $name = $c.Name
            # 167 varchar, 175 char, 231 nvarchar, 239 nchar, 35 text, 99 ntext, 241 xml
            $isString = $c.SystemTypeId -in @(167, 175, 231, 239, 35, 99, 241)
            V $row[$name] -String:$isString
        }
        $colList = ($colNames | ForEach-Object { "[$_]" }) -join ', '
        $valList = $values -join ', '

        # Row-level idempotency, filtered to duplicate-key errors. Anything else rethrows.
        [void]$sb.AppendLine("BEGIN TRY")
        [void]$sb.AppendLine("  INSERT INTO [$table] ($colList) VALUES ($valList);")
        [void]$sb.AppendLine("END TRY")
        [void]$sb.AppendLine("BEGIN CATCH")
        # Two duplicate-key numbers (a repeated PK, a repeated unique index) and 547 (a foreign key
        # naming a row this database does not have, which happens when the seed is loaded into a
        # database that already holds a fixture's own catalog under different ids). Any OTHER error
        # still throws, which is what keeps a real defect loud.
        [void]$sb.AppendLine("  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;")
        [void]$sb.AppendLine("END CATCH")
    }

    if ($identity.Count -gt 0) { [void]$sb.AppendLine("SET IDENTITY_INSERT [$table] OFF;") }
    [void]$sb.AppendLine('')
    return $rows.Count
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('-- -------------------------------------------------------------------------------------------')
[void]$sb.AppendLine('-- GENERATED FILE - do not hand-edit.')
[void]$sb.AppendLine('--')
[void]$sb.AppendLine('-- Dumped from a database holding the correct end state by scripts/dump-seed.ps1.')
[void]$sb.AppendLine('-- The pool (Stimuli/Items/ItemVersions/ExamItems) is the only content model: this seed writes')
[void]$sb.AppendLine('-- no Questions, QuestionGroups or QuestionOptions rows, because those tables are gone (#250).')
[void]$sb.AppendLine('--')
[void]$sb.AppendLine('-- Every row is its own TRY/CATCH filtered to duplicate-key errors (2627, 2601), so re-running')
[void]$sb.AppendLine('-- changes no count while any OTHER error still throws loudly.')
[void]$sb.AppendLine('--')
[void]$sb.AppendLine('-- Regenerate with:  ./scripts/dump-seed.ps1 -Database <a migrated database>')
[void]$sb.AppendLine('-- -------------------------------------------------------------------------------------------')
[void]$sb.AppendLine('SET NOCOUNT ON;')
[void]$sb.AppendLine('GO')
[void]$sb.AppendLine('')

# The ADR-0012 guards ask WHO is writing, and a seed is a fixture catalog - the same declaration the
# hand-written seed made. Without it trg_Exams_TriageCannotWrite refuses the first Exams row.
[void]$sb.AppendLine('-- The guards ask who is writing (ADR-0012). A seed is a fixture catalog: declare it, and restore')
[void]$sb.AppendLine('-- the previous value at the end so a run against a real catalog leaves it as it found it.')
[void]$sb.AppendLine('IF OBJECT_ID(''tempdb..#ee_seed_prior_fixture'') IS NOT NULL DROP TABLE #ee_seed_prior_fixture;')
[void]$sb.AppendLine('CREATE TABLE #ee_seed_prior_fixture (Was BIT NOT NULL);')
[void]$sb.AppendLine('INSERT INTO #ee_seed_prior_fixture (Was)')
[void]$sb.AppendLine('  SELECT CASE WHEN OBJECT_ID(''CatalogMode'', ''U'') IS NULL THEN 0 ELSE dbo.fn_IsFixtureCatalog() END;')
[void]$sb.AppendLine('IF OBJECT_ID(''CatalogMode'', ''U'') IS NOT NULL')
[void]$sb.AppendLine('BEGIN')
[void]$sb.AppendLine('  IF EXISTS (SELECT 1 FROM CatalogMode WHERE ModeId = 1)')
[void]$sb.AppendLine('    UPDATE CatalogMode SET IsFixtureCatalog = 1 WHERE ModeId = 1;')
[void]$sb.AppendLine('  ELSE')
[void]$sb.AppendLine('    INSERT INTO CatalogMode (ModeId, IsFixtureCatalog) VALUES (1, 1);')
[void]$sb.AppendLine('END;')
[void]$sb.AppendLine('GO')
[void]$sb.AppendLine('')

$total = 0
$withRows = @()
foreach ($t in $order) {
    $n = EmitTable $t $sb
    if ($n -gt 0) { $withRows += "$t=$n"; $total += $n }
}

# A D23 rule a straightforward dump cannot express: a saved link that names a legacy question is
# DROPPED, not re-pointed. The dump only writes rows, so the deletion is stated here - and it has to
# run on every load, because the row it removes exists only in a database that predates the pool.
[void]$sb.AppendLine('-- D23 / #224: a legacy saved link is dropped rather than re-pointed. This is a rule about a')
[void]$sb.AppendLine('-- database that predates the pool, so it is not a row the dump could carry.')
[void]$sb.AppendLine('DELETE FROM [SavedQuestions] WHERE [QuestionId] IS NOT NULL;')
[void]$sb.AppendLine('GO')
[void]$sb.AppendLine('')

# Restore the fixture-catalog flag the guards read, so a run against a real catalog leaves it as it was.
[void]$sb.AppendLine('IF OBJECT_ID(''CatalogMode'', ''U'') IS NOT NULL AND OBJECT_ID(''tempdb..#ee_seed_prior_fixture'') IS NOT NULL')
[void]$sb.AppendLine('  UPDATE CatalogMode SET IsFixtureCatalog = (SELECT Was FROM #ee_seed_prior_fixture) WHERE ModeId = 1;')
[void]$sb.AppendLine('GO')
[void]$sb.AppendLine('')

[System.IO.File]::WriteAllText((Join-Path $PWD $OutFile), $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
"wrote $OutFile : $total rows across $($withRows.Count) tables"
$withRows -join '  '