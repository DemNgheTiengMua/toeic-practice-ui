# Signs in and returns a bearer token. The response field is `token`, not `accessToken`.
# Usage: $t = Get-EeToken -Email admin@demo.local ; iex (Get-EeToken ...) style helpers below.
param(
    [string]$Email = "admin@demo.local",
    [string]$Password = "Demo1234!",
    [string]$BaseUrl = "http://localhost:5001"
)

$body = @{ email = $Email; password = $Password } | ConvertTo-Json -Compress
$file = Join-Path $env:TEMP "ee-signin-$([guid]::NewGuid().ToString('N')).json"
$body | Out-File -FilePath $file -Encoding ascii -NoNewline
try {
    $resp = curl.exe -s -X POST "$BaseUrl/api/auth/sign-in" -H "Content-Type: application/json" --data-binary "@$file" | ConvertFrom-Json
} finally {
    Remove-Item $file -ErrorAction SilentlyContinue
}
if (-not $resp.token) { throw "sign-in failed for $Email" }
$resp.token
