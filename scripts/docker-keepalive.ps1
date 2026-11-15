# Docker Desktop dies repeatedly on this machine (exit 137 / memory pressure).
# Every call site restarts it, waits for the engine, then runs the real command.
# Usage: . .\scripts\docker-keepalive.ps1; Assert-Docker
function Assert-Docker {
    param([int]$MaxWaitSec = 180)

    $probe = { docker info 2>&1 | Select-String -Pattern "Server Version" }
    if (& $probe) { return }

    Write-Host "[docker-keepalive] engine is down; starting Docker Desktop"
    Start-Process "C:\Users\Nguoi Viet Chim Tay\AppData\Local\Programs\DockerDesktop\Docker Desktop.exe"

    $deadline = (Get-Date).AddSeconds($MaxWaitSec)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 4
        if (& $probe) {
            Write-Host "[docker-keepalive] engine is up"
            return
        }
    }

    throw "[docker-keepalive] engine did not come up within $MaxWaitSec s"
}
