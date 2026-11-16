# Creates one graded writing attempt so the review page can be shown with real rubric marks.
#
# The seeded placement's writing answer is refused by the grader (it does not answer the task),
# which is correct behaviour but proves only the refusal path. A demo needs both: a paper that was
# scored, with marks per criterion, and a paper that was refused, with the reason. This drives the
# writing-practice flow, which is the shortest path to a scored free response.
param(
    [string]$Email = "learner@demo.local",
    [string]$BaseUrl = "http://localhost:5001"
)

$ErrorActionPreference = "Stop"
$token = & "$PSScriptRoot/ee-token.ps1" -Email $Email

function Call([string]$method, [string]$path, $body) {
    $args = @("-s", "-w", "`nHTTP:%{http_code}", "-X", $method, "$BaseUrl$path",
              "-H", "Authorization: Bearer $token")
    if ($null -ne $body) {
        $file = Join-Path $env:TEMP "ee-wr-$([guid]::NewGuid().ToString('N')).json"
        ($body | ConvertTo-Json -Compress -Depth 5) | Out-File $file -Encoding ascii -NoNewline
        $args += @("-H", "Content-Type: application/json", "--data-binary", "@$file")
    }
    $raw = & curl.exe @args
    if ($null -ne $body) { Remove-Item $file -ErrorAction SilentlyContinue }
    $status = ($raw -split "`n" | Select-Object -Last 1) -replace "HTTP:", ""
    $json = ($raw -split "`n" | Select-Object -First 1)
    return [pscustomobject]@{ Status = $status; Raw = $json }
}

# ---- the writing practice paper this certificate publishes -------------------------------------
$list = Call "GET" "/api/learner/skill-practice/writing" $null
$exam = ($list.Raw | ConvertFrom-Json).exams[0]
if (-not $exam) { throw "no writing practice paper for this certificate: $($list.Raw)" }
Write-Host "paper: $($exam.examId) '$($exam.name)' $($exam.creditCost) cr"

$start = Call "POST" "/api/learner/writing/$($exam.examId)/start" @{}
Write-Host "start: HTTP $($start.Status)"
$attemptId = ($start.Raw | ConvertFrom-Json).attemptId
if (-not $attemptId) { throw "start returned no attemptId: $($start.Raw)" }

$take = Call "GET" "/api/learner/writing/attempts/$attemptId" $null
$task = ($take.Raw | ConvertFrom-Json)
$questionId = $task.currentQuestionId
Write-Host "take: HTTP $($take.Status) question=$questionId"

# A real answer to the actual prompt: an IELTS Task 1 chart report, 160+ words, with an overview,
# selective figures and comparisons - which is what the rubric asks for.
$essay = @"
The chart compares the percentage of households with internet access in three European countries
between 2010 and 2020. Overall, access rose in all three nations, but the gap between them narrowed
markedly over the decade.

In 2010, the Nordic country led clearly, with roughly nine in ten households connected, while the
southern country stood at only about four in ten and the central country at around six in ten. The
Nordic figure then climbed steadily to near saturation by 2020, leaving little room for further
growth.

The most striking change occurred in the southern country, where access more than doubled, rising
from approximately 40 per cent to just over 80 per cent. Growth was fastest between 2014 and 2018,
when the line steepens noticeably. The central country followed a similar but gentler path, moving
from about 60 per cent to roughly 85 per cent.

As a result, the initial spread of some fifty percentage points shrank to less than fifteen by the
end of the period. In short, the three countries converged towards near-universal access, although
the southern country remained marginally behind the other two in 2020.
"@

$draft = Call "POST" "/api/learner/writing/attempts/$attemptId/draft" @{ questionId = $questionId; responseText = $essay.Trim() }
Write-Host "draft: HTTP $($draft.Status)"

$submit = Call "POST" "/api/learner/writing/attempts/$attemptId/submit" $null
Write-Host "submit: HTTP $($submit.Status) status=$(($submit.Raw | ConvertFrom-Json).status)"

# ---- wait for the grader (worker polls every 2s) ----------------------------------------------
$deadline = (Get-Date).AddSeconds(90)
$final = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    $review = Call "GET" "/api/learner/writing/attempts/$attemptId/review" $null
    if ($review.Status -eq "200") {
        $final = $review.Raw | ConvertFrom-Json
        break
    }
    Write-Host "  waiting for the grader (HTTP $($review.Status))..."
}
if (-not $final) { throw "grading did not finish in 90s" }

Write-Host ""
Write-Host "attempt $attemptId graded:"
$final.criteria | ForEach-Object { Write-Host ("  {0}: {1}/{2}" -f $_.criterionName, $_.score, $_.maxScore) }
Write-Host "  overall: $($final.overallScore)"
Write-Host "  priority fix: $($final.priorityFix)"
Write-Host "  review URL: http://localhost:5000/writing/result/$attemptId"
