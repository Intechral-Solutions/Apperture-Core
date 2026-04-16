<#
.SYNOPSIS
    Restarts the development SQL Server container (compose service sqlserver).
.DESCRIPTION
    This script is only for the database engine in Docker. Management.Host, Management UI, and the WorkerHost
    runtime are local dotnet processes in dev (see up.ps1 and restart-host / restart-ui / restart-worker)—
    they are not started or restarted here.

    Syncs User Secrets to .env (same as other dev scripts that use Docker), then runs
    `docker compose restart sqlserver` from the repo root. Data in the sqlserver volume is preserved.
.EXAMPLE
    .\restart-db.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$SyncScript = Join-Path $PSScriptRoot "Sync-EnvFromUserSecrets.ps1"

& $SyncScript
if (-not $?) { throw "Failed to sync User Secrets to .env" }

$serviceName = "sqlserver"
Write-Host "Restarting Docker service '$serviceName'..." -ForegroundColor Cyan
Push-Location $RepoRoot
try {
    $definedServices = @(& docker compose config --services)
    if ($LASTEXITCODE -ne 0) { throw "docker compose config --services failed" }
    if ($serviceName -notin $definedServices) {
        $known = ($definedServices | Sort-Object) -join ", "
        throw "Compose service '$serviceName' is not defined. Known services: $known."
    }

    & docker compose restart $serviceName
    if ($LASTEXITCODE -ne 0) { throw "docker compose restart failed for service '$serviceName'" }
}
finally {
    Pop-Location
}

Write-Host "SQL Server container restarted (localhost:1433)." -ForegroundColor Green
