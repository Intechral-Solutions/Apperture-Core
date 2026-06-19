<#
.SYNOPSIS
    Back up development SQL Server databases to local files.
.DESCRIPTION
    Creates timestamped .bak files for Apperture, Apperture_IntegrationTests,
    and Integrations using the Docker SQL Server container.
.EXAMPLE
    .\backup-databases.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$BackupDirectory = Join-Path $RepoRoot "backups"
$ContainerBackupDirectory = "/var/opt/mssql/backups"
$SqlContainerName = "apperture-sqlserver"
$Databases = @("Apperture", "Apperture_IntegrationTests", "Integrations")
$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

if (-not (Test-Path $BackupDirectory)) {
    New-Item -Path $BackupDirectory -ItemType Directory | Out-Null
}

$envPath = Join-Path $RepoRoot ".env"
if (-not (Test-Path $envPath)) { throw ".env not found at $envPath" }

$envLine = (Get-Content -Path $envPath | Select-String -Pattern "^MSSQL_SA_PASSWORD=").Line
if (-not $envLine) { throw "MSSQL_SA_PASSWORD not found in .env" }
$saPassword = $envLine.Split("=", 2)[1]

Write-Host "Backing up databases to $BackupDirectory" -ForegroundColor Cyan

foreach ($database in $Databases) {
    $fileName = "$database-$Timestamp.bak"
    $containerPath = "$ContainerBackupDirectory/$fileName"
    $localPath = Join-Path $BackupDirectory $fileName

    Write-Host "Creating backup for $database..." -ForegroundColor Cyan
    $backupQuery = "BACKUP DATABASE [$database] TO DISK = N'$containerPath' WITH INIT, COMPRESSION, CHECKSUM, STATS = 10;"
    docker exec $SqlContainerName /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$saPassword" -C -Q "$backupQuery" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to back up database $database" }

    docker cp "${SqlContainerName}:$containerPath" "$localPath" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to copy backup file for $database to local backup folder" }

    docker exec $SqlContainerName /bin/sh -c "rm -f '$containerPath'" | Out-Null
    Write-Host "Saved $localPath" -ForegroundColor Green
}

Write-Host "Database backups complete." -ForegroundColor Green
