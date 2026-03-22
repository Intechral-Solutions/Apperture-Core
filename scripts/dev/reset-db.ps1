<#
.SYNOPSIS
    Resets the development database (drop + recreate + migrate).
.DESCRIPTION
    Drops the Apperture database in Docker SQL Server, recreates it, and applies
    EF Core migrations. Use when the database is in an inconsistent state.
.EXAMPLE
    .\reset-db.ps1
    .\reset-db.ps1 -Confirm
#>
[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = "Stop"
$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$SyncScript = Join-Path $PSScriptRoot "Sync-EnvFromUserSecrets.ps1"
$PersistenceProject = Join-Path $RepoRoot "src\Apperture.Persistence\Apperture.Persistence.SqlServer\Apperture.Persistence.SqlServer.csproj"
$HostProject = Join-Path $RepoRoot "src\Apperture.ControlPlane\Apperture.Management.Host\Apperture.Management.Host.csproj"

if (-not (Test-Path $PersistenceProject)) { throw "Persistence project not found: $PersistenceProject" }
if (-not (Test-Path $HostProject)) { throw "Host project not found: $HostProject" }

# Sync User Secrets -> .env
& $SyncScript
if (-not $?) { throw "Failed to sync User Secrets to .env" }

$envPath = Join-Path $RepoRoot ".env"
$envLine = (Get-Content -Path $envPath | Select-String -Pattern "^MSSQL_SA_PASSWORD=").Line
if (-not $envLine) { throw "MSSQL_SA_PASSWORD not found in .env" }
$saPassword = $envLine.Split("=", 2)[1]

if ($PSCmdlet.ShouldProcess("Apperture database", "Drop and recreate")) {
    Write-Host "Dropping and recreating Apperture database..." -ForegroundColor Cyan
    docker exec apperture-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$saPassword" -C -Q "IF DB_ID(N'Apperture') IS NOT NULL DROP DATABASE [Apperture]; CREATE DATABASE [Apperture];" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to reset database via sqlcmd. Is Docker SQL Server running?" }

    Write-Host "Applying EF Core migrations..." -ForegroundColor Cyan
    $connectionString = "Server=localhost,1433;Database=Apperture;User Id=sa;Password=$saPassword;TrustServerCertificate=True;"
    $env:Apperture__ConnectionString = $connectionString
    Push-Location $RepoRoot
    try {
        dotnet ef database update --project $PersistenceProject --startup-project $HostProject
        if ($LASTEXITCODE -ne 0) { throw "EF database update failed." }
    } finally {
        Pop-Location
        Remove-Item Env:\Apperture__ConnectionString -ErrorAction SilentlyContinue
    }

    Write-Host "Database reset complete." -ForegroundColor Green
}
