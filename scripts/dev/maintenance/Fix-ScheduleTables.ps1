<#
.SYNOPSIS
    Creates the Schedule tables in the Apperture database if they don't exist.
.DESCRIPTION
    Use when the database was migrated with an empty AddSchedules migration.
    Safe to run multiple times (uses IF NOT EXISTS).
.EXAMPLE
    .\Fix-ScheduleTables.ps1
#>
$ErrorActionPreference = "Stop"
$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$SyncScript = Join-Path $PSScriptRoot "Sync-EnvFromUserSecrets.ps1"
& $SyncScript
if (-not $?) { throw "Failed to sync User Secrets to .env" }
$envPath = Join-Path $RepoRoot ".env"
$envLine = (Get-Content -Path $envPath | Select-String -Pattern "^MSSQL_SA_PASSWORD=").Line
if (-not $envLine) { throw "MSSQL_SA_PASSWORD not found in .env" }
$saPassword = $envLine.Split("=", 2)[1]

$sql = @"
IF OBJECT_ID(N'dbo.ScheduleDefinitions', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ScheduleDefinitions] (
        [Id] nvarchar(64) NOT NULL,
        [Name] nvarchar(256) NOT NULL,
        [RecurrenceExpression] nvarchar(256) NOT NULL,
        [TimeZone] nvarchar(128) NOT NULL,
        [Enabled] bit NOT NULL,
        [TargetJobType] int NOT NULL,
        [TargetJobConfigurationJson] nvarchar(max) NULL,
        [ExecutionPolicyRef] nvarchar(256) NULL,
        [Version] int NOT NULL,
        CONSTRAINT [PK_ScheduleDefinitions] PRIMARY KEY ([Id])
    );
END
IF OBJECT_ID(N'dbo.ScheduleAssignments', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ScheduleAssignments] (
        [Id] nvarchar(64) NOT NULL,
        [ScheduleId] nvarchar(64) NOT NULL,
        [RuntimePlaneId] nvarchar(128) NOT NULL,
        [PolicyOverrides] nvarchar(max) NULL,
        [DesiredVersion] int NOT NULL,
        [AppliedVersion] int NOT NULL,
        CONSTRAINT [PK_ScheduleAssignments] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_ScheduleAssignments_RuntimePlaneId] ON [dbo].[ScheduleAssignments] ([RuntimePlaneId]);
END
IF OBJECT_ID(N'dbo.ScheduleRuns', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ScheduleRuns] (
        [Id] bigint IDENTITY(1,1) NOT NULL,
        [ScheduleId] nvarchar(64) NOT NULL,
        [AssignmentId] nvarchar(64) NOT NULL,
        [FireTimeUtc] datetimeoffset NOT NULL,
        [StartedAtUtc] datetimeoffset NOT NULL,
        [CompletedAtUtc] datetimeoffset NULL,
        [Outcome] nvarchar(64) NOT NULL,
        [OutcomeReason] nvarchar(2048) NULL,
        [RunId] nvarchar(64) NULL,
        CONSTRAINT [PK_ScheduleRuns] PRIMARY KEY ([Id])
    );
    CREATE UNIQUE INDEX [IX_ScheduleRuns_ScheduleId_FireTimeUtc] ON [dbo].[ScheduleRuns] ([ScheduleId], [FireTimeUtc]);
    CREATE INDEX [IX_ScheduleRuns_AssignmentId] ON [dbo].[ScheduleRuns] ([AssignmentId]);
END
IF OBJECT_ID(N'dbo.ScheduleStates', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ScheduleStates] (
        [AssignmentId] nvarchar(64) NOT NULL,
        [NextDueUtc] datetimeoffset NULL,
        [LastAttemptedUtc] datetimeoffset NULL,
        [LastOutcome] nvarchar(64) NULL,
        [LastOutcomeReason] nvarchar(2048) NULL,
        [LastSuccessfulDispatchUtc] datetimeoffset NULL,
        CONSTRAINT [PK_ScheduleStates] PRIMARY KEY ([AssignmentId])
    );
END
"@

Write-Host "Creating Schedule tables if they don't exist..." -ForegroundColor Cyan
$sqlFile = Join-Path $env:TEMP "fix-schedule-tables.sql"
$sql | Out-File -FilePath $sqlFile -Encoding utf8
# Copy SQL into container and run (sqlcmd -i requires a path inside the container)
docker cp $sqlFile apperture-sqlserver:/tmp/fix-schedule-tables.sql
docker exec apperture-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$saPassword" -C -d Apperture -i /tmp/fix-schedule-tables.sql
if ($LASTEXITCODE -ne 0) { throw "Failed to run SQL. Is Docker SQL Server running?" }
docker exec apperture-sqlserver rm -f /tmp/fix-schedule-tables.sql 2>$null
Remove-Item $sqlFile -ErrorAction SilentlyContinue
Write-Host "Done." -ForegroundColor Green
