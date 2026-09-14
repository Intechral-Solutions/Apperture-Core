<#
.SYNOPSIS
    Back up the Aspire-managed dev SQL databases to local .bak files.
.DESCRIPTION
    Targets the Aspire dev SQL container (identified by the persistent `apperture-aspire-sql` volume it
    mounts, not by its randomly-suffixed container name - the same identity approach SqlDevPortGuard
    uses) rather than the docker-compose/native `apperture-sqlserver` container that
    backup-databases.ps1 targets.

    Only backs up "Apperture" by default - the real dev database with data worth preserving. The
    Apperture_* test catalogs (IntegrationTests, BootstrapHttpTests, ManagementGovernanceTests,
    IntegrationSandbox) are intentionally excluded: test fixtures recreate/reseed them idempotently on
    every run, so there's nothing there worth backing up. Pass -Databases to override.
.EXAMPLE
    .\backup-aspire-databases.ps1
    .\backup-aspire-databases.ps1 -Databases Apperture,Apperture_IntegrationTests
#>
[CmdletBinding()]
param(
    [string[]]$Databases = @('Apperture')
)

$ErrorActionPreference = "Stop"

$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.Parent.FullName }
$DataVolumeName = 'apperture-aspire-sql'
$BackupDirectory = Join-Path $RepoRoot 'backups'
$ContainerBackupDirectory = '/var/opt/mssql/backups'
$AppHostProject = Join-Path $RepoRoot 'src\Apperture.AppHost\Apperture.AppHost.csproj'
$Timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'

function Resolve-AspireSqlPassword {
    param([Parameter(Mandatory)][string]$AppHostProject)

    # Precedence matches AppHost's own builder.AddParameter("sql-password", secret: true): User
    # Secrets override appsettings.json.
    $secretsOutput = & dotnet user-secrets list --project $AppHostProject 2>$null
    if ($LASTEXITCODE -eq 0) {
        $line = $secretsOutput | Where-Object { $_ -match '^Parameters:sql-password\s*=\s*(.+)$' } | Select-Object -First 1
        if ($line -and $Matches[1]) {
            return $Matches[1].Trim()
        }
    }

    $appsettingsPath = Join-Path (Split-Path $AppHostProject -Parent) 'appsettings.json'
    if (-not (Test-Path -LiteralPath $appsettingsPath)) {
        throw "Cannot resolve sql-password: $appsettingsPath not found and no User Secrets override."
    }
    $password = (Get-Content -LiteralPath $appsettingsPath -Raw | ConvertFrom-Json).Parameters.'sql-password'
    if ([string]::IsNullOrWhiteSpace($password)) {
        throw "Cannot resolve sql-password: Parameters:sql-password missing from $appsettingsPath and no User Secrets override."
    }
    return $password
}

$containerId = (docker ps --filter "volume=$DataVolumeName" --filter 'status=running' --format '{{.ID}}' | Select-Object -First 1)
if (-not $containerId) {
    throw "No running container mounts volume '$DataVolumeName'. Is the Aspire AppHost running?"
}

$saPassword = Resolve-AspireSqlPassword -AppHostProject $AppHostProject

if (-not (Test-Path $BackupDirectory)) {
    New-Item -Path $BackupDirectory -ItemType Directory | Out-Null
}

Write-Host "Backing up Aspire SQL container $containerId to $BackupDirectory" -ForegroundColor Cyan

foreach ($database in $Databases) {
    $fileName = "$database-$Timestamp.bak"
    $containerPath = "$ContainerBackupDirectory/$fileName"
    $localPath = Join-Path $BackupDirectory $fileName

    Write-Host "Creating backup for $database..." -ForegroundColor Cyan
    $backupQuery = "BACKUP DATABASE [$database] TO DISK = N'$containerPath' WITH INIT, COMPRESSION, CHECKSUM, STATS = 10;"
    docker exec $containerId /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$saPassword" -C -Q "$backupQuery" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to back up database $database" }

    docker cp "${containerId}:$containerPath" "$localPath" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to copy backup file for $database to local backup folder" }

    docker exec $containerId /bin/sh -c "rm -f '$containerPath'" | Out-Null
    Write-Host "Saved $localPath" -ForegroundColor Green
}

Write-Host "Aspire database backups complete." -ForegroundColor Green
