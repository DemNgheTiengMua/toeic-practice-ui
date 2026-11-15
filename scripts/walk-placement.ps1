param(
    [string]$Email = "learner.fresh@demo.local",
    [string]$BaseUrl = "http://localhost:5001"
)

# End-to-end placement walk over the real API: start, answer every position with its own
# option id, draft the writing task, submit, read the result, then read the review.
# Proves the loop a demo walks, not a unit of it.

$ErrorActionPreference = "Stop"
$token = & "$PSScriptRoot/ee-token.ps1" -Email $Email
$h = @{ Authorization = "Bearer $token" }

function Post([string]$path, $body) {
    $file = Join-Path $env:TEMP "ee-post-$([guid]::NewGuid().ToString('N')).json"
    ($body | ConvertTo-Json -Compress -Depth 5) | Out-File $file -Encoding ascii -NoNewline
    try {
        $raw = curl.exe -s -w "`nHTTP:%{http_code}" -X POST "$BaseUrl$path" -H "Content-Type: application/json" -H "Authorization: Bearer $token" --data-binary "@$file"
    } finally { Remove-Item $file -ErrorAction SilentlyContinue }
    $status = ($raw -split "`n" | Select-Object -Last 1) -replace "HTTP:", ""
    $json = ($raw -split "`n" | Select-Object -First 1) | ConvertFrom-Json
    return [pscustomobject]@{ Status = $status; Body = $json }
}

function Get([string]$path) {
    $raw = curl.exe -s -w "`nHTTP:%{http_code}" "$BaseUrl$path" -H "Authorization: Bearer $token"
    $status = ($raw -split "`n" | Select-Object -Last 1) -replace "HTTP:", ""
    $json = ($raw -split "`n" | Select-Object -First 1) | ConvertFrom-Json
    return [pscustomobject]@{ Status = $status; Body = $json }
}

# ---- 1. catalog -------------------------------------------------------------------------------
$catalog = Get "/api/learner/placement"
Write-Host "catalog: isRetake=$($catalog.Body.isRetake) exams=$($catalog.Body.exams.Count)"

# ---- 2. start ---------------------------------------------------------------------------------
$examId = $catalog.Body.exams[0].examId
$started = Post "/api/learner/placement/start" @{ examId = $examId }
Write-Host "start: HTTP $($started.Status) placement=$($started.Body.placementId) charged=$($started.Body.creditsCharged)"
$placementId = $started.Body.placementId
if (-not $placementId) { throw "start returned no placementId: $($started.Body | ConvertTo-Json -Compress)" }

# ---- 3. preflight, then read the paper ---------------------------------------------------------
# A sitting refuses to open until equipment verification is recorded; that is the real flow, not a
# workaround, so a walk has to complete it the way the browser does.
$attemptId = $started.Body.attemptId
$pre = Post "/api/learner/attempts/$attemptId/preflight/verify" @{}
Write-Host "preflight: HTTP $($pre.Status) verified=$($pre.Body.isVerified)"

$take = Get "/api/learner/placement/take/$placementId"
$questions = $take.Body.questions
Write-Host "take: HTTP $($take.Status) questions=$($questions.Count)"

$answered = 0
$failed = @()
foreach ($q in $questions) {
    if ($q.questionType -eq "free_response") { continue }
    $optionId = $q.options[0].optionId
    $r = Post "/api/learner/placement/take/$placementId/answers/$($q.questionId)" @{ selectedOptionId = $optionId; isFlagged = $false }
    if ($r.Status -eq "200" -and $r.Body.selectedOptionId -eq $optionId) { $answered++ } else { $failed += "q$($q.questionId): HTTP $($r.Status) selected=$($r.Body.selectedOptionId)" }
}
Write-Host "answered: $answered stored, $($failed.Count) refused"
$failed | ForEach-Object { Write-Host "   $_" }

# ---- 4. the writing task ----------------------------------------------------------------------

if (-not $writing) { $writing = $questions | Where-Object { $_.questionType -eq "free_response" } | Select-Object -First 1 }
if ($writing) {
    $text = "Both views have real merit. Technology connects people across distance, makes distributed work possible, and gives isolated learners access to teaching they could not otherwise reach. Critics answer that constant connection erodes attention and replaces depth with volume. In my view the decisive variable is deliberate use rather than the tool: the same network that shortens a conversation can carry a lecture to a village. Education, not prohibition, is what turns one into the other."
    $draft = Post "/api/learner/placement/take/$placementId/draft" @{ questionId = $writing.questionId; responseText = $text }
    Write-Host "draft: HTTP $($draft.Status) words=$($draft.Body.wordCount)"
}

# ---- 5. submit --------------------------------------------------------------------------------
$submitted = Post "/api/learner/placement/take/$placementId/submit" @{}
Write-Host "submit: HTTP $($submitted.Status) band='$($submitted.Body.resultBandCode)' score=$($submitted.Body.totalScore) skills=$($submitted.Body.skillScores.Count)"

# ---- 6. result and review ---------------------------------------------------------------------
$result = Get "/api/learner/placement/result/$placementId"
Write-Host "result: HTTP $($result.Status) band='$($result.Body.resultBandCode)' weaker='$($result.Body.weakerSkillName)' graded=$($result.Body.isGraded)"
$result.Body.skillResults | ForEach-Object { Write-Host "   $($_.skillName): $($_.status)" }

if ($result.Body.isGraded) {
    $review = Get "/api/learner/placement/result/$placementId/review"
    Write-Host "review: HTTP $($review.Status) questions=$($review.Body.questions.Count)"
} else {
    Write-Host "review: not open yet - free responses are still grading (that is the designed order)"
}

