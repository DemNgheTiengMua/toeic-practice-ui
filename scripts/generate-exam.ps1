<#
.SYNOPSIS
Administrative CLI tool to generate and ingest standardized exams from a blueprint JSON (Issue #88).
.EXAMPLE
.\scripts\generate-exam.ps1 -Blueprint docs/samples/sample-blueprint-toeic-reading.json
#>
param (
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Blueprint,

    [Parameter(Mandatory = $false)]
    [int]$AdminId = 1
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Blueprint)) {
    Write-Error "Blueprint file not found at path: $Blueprint"
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    dotnet run --project src/WebAPI -- generate-exam (Resolve-Path $Blueprint).Path --admin-id $AdminId
}
finally {
    Pop-Location
}
