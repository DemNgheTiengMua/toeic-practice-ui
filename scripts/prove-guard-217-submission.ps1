# #217 guard proofs: break one guard at a time, watch the test it protects go red, restore.
#
# The rule from the ops notes: never `git checkout` a file you have edited. Read it into a variable,
# mutate, run, write the original back from the variable.

$ErrorActionPreference = "Stop"
$root = "C:\Users\Nguoi Viet Chim Tay\.local\share\opencode\worktree\f5d847\cefr-217-placement-submit"
Set-Location $root

$results = @()

function Prove-Guard {
    param([string]$Name, [string]$File, [string]$Old, [string]$New, [string]$TestCase)

    $path = Join-Path $root $File
    $original = Get-Content -Raw $path
    if (-not $original.Contains($Old)) {
        Write-Host "SKIP $Name : mutation anchor not found" -ForegroundColor Yellow
        $script:results += [pscustomobject]@{ Guard = $Name; Result = "ANCHOR MISSING" }
        return
    }

    try {
        Set-Content -Path $path -Value ($original.Replace($Old, $New)) -NoNewline
        dotnet build --no-incremental 2>&1 | Out-Null
        # dotnet writes test failures to stderr, which PowerShell surfaces as a native error under
        # ErrorActionPreference=Stop. Redirect inside the command and treat the exit code as the signal.
        $run = (cmd /c "dotnet test tests/Services.Tests --no-build --filter `"FullyQualifiedName~PlacementSubmissionTests`" 2>&1") | Out-String
        $failed = $run -match "Failed!"
        $verdict = if ($failed) { "RED (good)" } else { "GREEN (guard not load-bearing)" }
        Write-Host "$Name -> $verdict" -ForegroundColor $(if ($failed) { "Green" } else { "Red" })
        $script:results += [pscustomobject]@{ Guard = $Name; Result = $verdict }
    }
    finally {
        Set-Content -Path $path -Value $original -NoNewline
    }
}

# 1. PLC-18: mark a blank incorrect (the wrong-but-passing input) -> the blank's IsCorrect assertion.
Prove-Guard -Name "blank stays unjudged" `
    -File "src/Services/Attempts/AttemptEngine.Submission.cs" `
    -Old "ans.IsCorrect = IsAnswered(ans) ? EvaluateAnswer(ans, pinnedVersions, optionsByQuestion) : null;" `
    -New "ans.IsCorrect = EvaluateAnswer(ans, pinnedVersions, optionsByQuestion);"

# 2. PLC-19: keep the untouched skill's zero score row -> the "excluded from the overall" assertion.
Prove-Guard -Name "untouched placement skill excluded" `
    -File "src/Services/Attempts/AttemptEngine.Submission.cs" `
    -Old ".Where(group => !isPlacement || group.Any(IsAnswered))" `
    -New ".Where(group => true)"

# 3. PLC-19: leave an abandoned placement's snapshot alone -> its null-score assertion.
Prove-Guard -Name "abandoned carries no total" `
    -File "src/Services/Attempts/AttemptEngine.Submission.cs" `
    -Old "var measuredNothing = isPlacement && newSkillScores.Count == 0;" `
    -New "var measuredNothing = false;"

# 4. PLC-19: mark nothing-measured `done` instead of abandoned -> the abandoned assertion.
Prove-Guard -Name "nothing measured is abandoned" `
    -File "src/Services/Scoring/PlacementResultWriter.cs" `
    -Old "placementRow.Status = PlacementStatus.Abandoned.ToCode();" `
    -New "placementRow.Status = PlacementStatus.Done.ToCode();"

# 5. PLC-34: bounce a grading sitting back as unfinished -> the progressive-result assertion.
Prove-Guard -Name "objective half visible while grading" `
    -File "src/Services/PlacementService.cs" `
    -Old "var attemptSubmitted = placement.ExamAttempt is { } attempt" `
    -New "var attemptSubmitted = false && placement.ExamAttempt is { } attempt"

# 6. PLC-35: give a failed grading a level -> the "neither zero nor A1" assertion.
Prove-Guard -Name "failed grading has no level" `
    -File "src/Services/Scoring/PlacementResultWriter.cs" `
    -Old "            if (measuredNow)" `
    -New "            if (measuredNow || status == PlacementResultStatus.NotMeasured)"

# 7. PLC-35: have the processor leave a failed response pending_ai -> the attempt never settles.
Prove-Guard -Name "a failed task is settled, not queued forever" `
    -File "src/Services/AiGradingProcessor.cs" `
    -Old "                await SettleFreeResponseAsync(fr.FreeResponseId, cancellationToken);" `
    -New "                // mutated: the task stays pending_ai"

# 8. PLC-35: "the retry writes no ledger row" cannot be mutated through a ledger INSERT - the schema's
# own triggers refuse a charge against a placement attempt (trg_CreditTransactions_ChargeMatchesExamPurpose),
# so the wrong write is refused before any assertion runs. That is a stronger guard than this test, and
# the test's job is the SERVICE half: it asserts the row count and the wallet balance are unchanged after
# a real retry, which is what catches a future retry that reaches the ledger by a legal shape.

Write-Host ""
$results | Format-Table -AutoSize
