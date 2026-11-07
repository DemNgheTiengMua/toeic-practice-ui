# Proves each #219 guard is load-bearing: break it, run the named test, confirm RED, restore.
# Run from the worktree root. NEVER while another dotnet test is going.
#
# Every write goes through [System.IO.File] and is verified by byte length both before and after the
# restore, because a mangled write here once produced a 1.2 GB source file. Line endings are
# normalised to LF for matching only; the file is written back with its own endings.
$ErrorActionPreference = "Continue"
$root = "C:\Users\Nguoi Viet Chim Tay\.local\share\opencode\worktree\f5d847\cefr-219-placement-topics"
Set-Location $root

$writer  = "src\Services\Scoring\PlacementResultWriter.cs"
$planner = "src\BusinessObjects\GuidedLearning\LearningPathPlanner.cs"
$gen     = "src\Services\GuidedLearning\LearningPathService.Generation.cs"
$profile = "src\BusinessObjects\Placement\PlacementCriterionProfile.cs"

function Test-Red {
    param([string]$File, [string]$Old, [string]$New, [string]$Label, [string]$Test)
    $path = (Resolve-Path -LiteralPath $File).Path
    $orig = [System.IO.File]::ReadAllText($path)
    $origLen = $orig.Length
    $lf = $orig.Replace("`r`n", "`n")
    $oldLf = $Old.Replace("`r`n", "`n")
    $newLf = $New.Replace("`r`n", "`n")
    if (-not $lf.Contains($oldLf)) { Write-Host "INVALID            $Label (patch target not found)"; return }

    $mutated = $lf.Replace($oldLf, $newLf)
    if ($orig.Contains("`r`n")) { $mutated = $mutated.Replace("`n", "`r`n") }
    [System.IO.File]::WriteAllText($path, $mutated)
    if ([System.IO.File]::ReadAllText($path) -ne $mutated) {
        [System.IO.File]::WriteAllText($path, $orig)
        Write-Host "ABORTED            $Label (the mutated write did not land as written; restored)"
        return
    }

    dotnet build --no-incremental 2>&1 | Out-Null
    $out = dotnet test tests/Services.Tests --filter "FullyQualifiedName~$Test" --no-build 2>&1 | Out-String

    [System.IO.File]::WriteAllText($path, $orig)
    if ([System.IO.File]::ReadAllText($path) -ne $orig) {
        Write-Host "RESTORE FAILED     $Label (the file did not come back byte-for-byte)"
        return
    }

    if ($out -match "Failed!") { Write-Host "RED                $Label" }
    elseif ($out -match "Passed!") { Write-Host "NOT LOAD-BEARING   $Label" }
    else { Write-Host "UNEXPECTED         $Label" }
}

# 1. band: every topic reported as a constant instead of the band its items sat at.
Test-Red $writer 'Band = i.CefrLevel.Code' 'Band = "B1"' `
    "topic band is the ITEM's band, not the skill's" "Each_touched_topic"

# 2. an answered-but-unjudged item is not a topic seen: a productive answer stores IsCorrect null.
Test-Red $writer '            .Where(a => a.IsCorrect.HasValue)
' '' `
    "an unjudged answer is not counted as a topic seen" "An_answered_but_unjudged_item"

# 3. signal: call every topic on_level.
Test-Red $writer 'correct * 100 >= seen * 70' 'true' `
    "the routing signal actually reads correctness" "Each_touched_topic"

# 4. weakest: take the first criterion instead of the lowest ratio.
Test-Red $profile '            .OrderBy(s => s.MaxScore <= 0 ? decimal.MaxValue : s.Score / s.MaxScore)
            .ThenBy(s => s.CriterionId)
            .First();' '            .First();' `
    "weakest is the lowest ratio, not the first scored" "The_productive_half_records"

# 5. ratio vs raw score: order by Score instead of the proportion.
Test-Red $profile '.OrderBy(s => s.MaxScore <= 0 ? decimal.MaxValue : s.Score / s.MaxScore)' `
    '.OrderBy(s => s.Score)' `
    "weakest compares ratios, so a capped criterion is not misread" "The_weakest_criterion_is_the_lowest_ratio"

# 6. mastery isolation: write a mastery row from the placement.
Test-Red $writer '                row.TopicPriorsJson = PlacementTopicPrior.Serialize(' `
    '                db.UserCategoryMasteries.Add(new BusinessObjects.Entities.UserCategoryMastery { UserId = placement.UserId, CertId = placement.CertId, CategoryId = 1, State = "learning" });
                row.TopicPriorsJson = PlacementTopicPrior.Serialize(' `
    "a placement creates no mastery record" "A_placement_creates_no_mastery_record"

# 7. service wiring: ignore the hint the writer stored.
Test-Red $gen 'var weakAtLevel = hints.TryGetValue((skill.SkillId, level), out var weak) ? weak : [];' `
    'var weakAtLevel = (IReadOnlyList<int>)[];' `
    "the generated path reads the placement's hint" "A_generated_path_drills_the_topic"

# 8. hint is a permutation, not an addition.
Test-Red $planner '        return block.TopicIds
            .OrderByDescending(weak.Contains)
            .ThenBy(t => position[t])
            .ToList();' `
    '        return weak.Concat(block.TopicIds).Distinct().ToList();' `
    "a hint never adds a topic the block cannot drill" "A_hint_never_adds"

# 9. hints are week one only.
Test-Red $planner '            var topicId = week == 1
                ? FirstWeekTopicOrder(block)[0]
                : block.TopicIds[(week - 1) % block.TopicIds.Count];' `
    '            var topicId = FirstWeekTopicOrder(block)[0];' `
    "only the first week is guided by the hint" "The_first_week_starts_on"

# The carried-row case has no mutation point: the branch runs only where the sitting measured nothing,
# so the row is either new (null by construction) or written by the not-measured branch, which nulls
# both signals. A_carried_skill_records_no_topic_hint asserts the behaviour a refactor could break.

# control
dotnet build --no-incremental 2>&1 | Out-Null
$control = dotnet test tests/Services.Tests --filter "FullyQualifiedName~PlacementTopicSignal" --no-build 2>&1 | Out-String
if ($control -match "Failed!") { Write-Host "NOT GREEN          control - the suite is red before any break" }
else { Write-Host "GREEN              control (unbroken code passes)" }
