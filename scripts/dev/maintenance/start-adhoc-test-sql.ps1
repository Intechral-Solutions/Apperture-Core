<#
.SYNOPSIS
    Starts a throwaway SQL Server container for manual integration-test runs, on a dynamically
    allocated host port.
.DESCRIPTION
    CTX-3A incident: a manually-run `docker run --name apperture-integration-sql-14333 -p 14333:1433 ...`
    container was left running for 36+ hours and silently squatted on host port 14333 - the port
    reserved for the Aspire dev SQL container (see CONTRIBUTING.md). When AppHost next started, Docker
    failed to publish 14333 for the real Aspire SQL container, but Management Host/Worker were still
    handed that port in their connection string and connected straight through to this leftover
    container instead - a fresh, empty database, wrong Users/Entra data, no obvious error.

    This script is the replacement pattern: it always asks Docker for a random free host port
    (`-p 127.0.0.1::1433`) instead of a fixed one, so it can never collide with 14333 (or anything
    else). The container name does not encode a port number, since that would be a lie the moment
    Docker reassigns it. The container is intentionally ephemeral - re-running this script removes any
    previous instance rather than accumulating leaked containers.

    For automated integration tests, prefer Aspire's own dynamic port via
    APPERTURE_TEST_SQL_CONNECTION_STRING / ConnectionStrings__DefaultConnection (see
    tests/Apperture.IntegrationTests/README-ManagementHostGovernance.md); reach for this script only
    for a manual one-off run against a real container without Aspire running.
.EXAMPLE
    .\start-adhoc-test-sql.ps1
    .\start-adhoc-test-sql.ps1 -SaPassword "MyStrong!Pass1"
#>
[CmdletBinding()]
param(
    [string]$SaPassword = "AdhocTest_$(Get-Random -Maximum 99999)!",
    [string]$ContainerName = "apperture-adhoc-test-sql"
)

$ErrorActionPreference = "Stop"

$reservedPort = 14333
Write-Host "Reserved for Aspire dev SQL - never used here: host port $reservedPort." -ForegroundColor DarkGray

$existing = docker ps -aq --filter "name=^/$ContainerName`$"
if ($existing) {
    Write-Host "Removing previous ad-hoc test SQL container '$ContainerName'..." -ForegroundColor Yellow
    docker rm -f $ContainerName | Out-Null
}

Write-Host "Starting ad-hoc test SQL Server on a dynamically allocated host port..." -ForegroundColor Cyan
docker run --name $ContainerName `
    -e "ACCEPT_EULA=Y" `
    -e "MSSQL_SA_PASSWORD=$SaPassword" `
    -p "127.0.0.1::1433" `
    -d mcr.microsoft.com/mssql/server:2022-latest | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to start container '$ContainerName'." }

Write-Host "Waiting for SQL Server to accept connections..." -ForegroundColor Cyan
$deadline = (Get-Date).AddSeconds(60)
$hostPort = $null
while ((Get-Date) -lt $deadline) {
    $portOutput = docker port $ContainerName 1433/tcp 2>$null
    if ($LASTEXITCODE -eq 0 -and $portOutput) {
        $hostPort = ($portOutput -split ':')[-1].Trim()
        break
    }
    Start-Sleep -Milliseconds 500
}
if (-not $hostPort) { throw "Timed out waiting for '$ContainerName' to publish its host port." }
if ([int]$hostPort -eq $reservedPort) {
    # Vanishingly unlikely (Docker picks from the ephemeral range) but check anyway, since the whole
    # point of this script is to guarantee no collision with the reserved dev port.
    throw "Docker assigned the reserved dev port $reservedPort by coincidence - re-run this script."
}

$connectionString = "Server=127.0.0.1,$hostPort;User Id=sa;Password=$SaPassword;TrustServerCertificate=True;Initial Catalog=Apperture;"

Write-Host ""
Write-Host "Ad-hoc test SQL Server ready: 127.0.0.1,$hostPort (container '$ContainerName')" -ForegroundColor Green
Write-Host "Set this before running tests:" -ForegroundColor Gray
Write-Host "  `$env:APPERTURE_TEST_SQL_CONNECTION_STRING = `"$connectionString`"" -ForegroundColor Gray
Write-Host "Stop and remove when done:" -ForegroundColor Gray
Write-Host "  docker rm -f $ContainerName" -ForegroundColor Gray
