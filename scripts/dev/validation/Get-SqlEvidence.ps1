#Requires -Version 7.2
<#
.SYNOPSIS
    Read-only proof that SQL Server was really used by a test run: per-catalog write markers, or catalog start-ups in a time window.
.DESCRIPTION
    Markers : for every online catalog named Apperture_*, the last user write and the number of user updates since the server started.
              Take one before and one after a run: a SQL test run that wrote nothing did not exercise SQL Server.
    Window  : the number of 'Starting up database' entries in the SQL Server error log between -FromUtc and -ToUtc (each throw-away test
              catalog logs one), with the first and last entry.
    Both run sqlcmd inside the test SQL container through `docker exec`. The password reaches sqlcmd only as the SQLCMDPASSWORD environment
    variable (never on a command line, never printed, never written), and any error text is passed through Protect-Text.
    The connection comes from APPERTURE_TEST_SQL_CONNECTION_STRING (process environment, then the repository .env unless
    -ProcessEnvironmentOnly). It must name a catalog starting Apperture_ and must not use port 14333 (the development server).

    Exit codes: 0 = collected; 2 = could not collect (no configuration, unsafe target, docker or sqlcmd failure). There is no 1: a
    collection either happened or it did not, and a missing proof must not be mistaken for an empty one.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Markers', 'Window')][string]$Mode,
    [string]$Container = 'apperture-test-sql',
    [datetime]$FromUtc,
    [datetime]$ToUtc,
    [string]$Repo,
    [switch]$ProcessEnvironmentOnly,
    [string]$EnvFile,
    [string]$DockerCommand = 'docker',
    [string]$Label,
    [string]$Json,
    [switch]$PassThru
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ValidationCommon.ps1')

function Stop-Collection([string]$Message) { Write-Host "NOT COLLECTED: $(Protect-Text $Message)" -ForegroundColor Red; exit 2 }

if (-not $Repo) { $Repo = (Invoke-Git $PSScriptRoot @('rev-parse', '--show-toplevel')).Text }
if ($Mode -eq 'Window' -and (-not $FromUtc -or -not $ToUtc -or $ToUtc -le $FromUtc)) { Stop-Collection '-FromUtc and a later -ToUtc are required for the Window mode' }
$sql = Resolve-SqlTarget $Repo -ProcessEnvironmentOnly:$ProcessEnvironmentOnly -EnvFile $EnvFile
if (-not $sql.Configured) { Stop-Collection 'APPERTURE_TEST_SQL_CONNECTION_STRING is not configured' }
$i = $sql.Info
if ($i.Port -eq 14333) { Stop-Collection 'the configured port is the development SQL server' }
if ($i.Catalog -notmatch '^Apperture_.+') { Stop-Collection "catalog '$($i.Catalog)' is not a disposable test catalog" }
if (-not $i.HasPassword) { Stop-Collection 'the connection string carries no password (integrated security is not supported here)' }
$pw = if ($sql.Raw -match '(?i)(?:password|pwd)\s*=\s*([^;]+)') { $Matches[1].Trim('"', "'") } else { Stop-Collection 'no password in the connection string' }
$user = if ($i.User) { $i.User } else { 'sa' }

$query = if ($Mode -eq 'Markers') {
    @"
SET NOCOUNT ON;
DECLARE @r TABLE (db sysname, last_write datetime, writes bigint);
DECLARE @n sysname;
DECLARE c CURSOR FOR SELECT name FROM sys.databases WHERE name LIKE 'Apperture[_]%' AND state_desc = 'ONLINE';
OPEN c; FETCH NEXT FROM c INTO @n;
WHILE @@FETCH_STATUS = 0
BEGIN
  DECLARE @sql nvarchar(max) = N'USE ' + QUOTENAME(@n) + N'; SELECT DB_NAME(), MAX(last_user_update), SUM(CAST(user_updates AS bigint)) FROM sys.dm_db_index_usage_stats WHERE database_id = DB_ID();';
  INSERT @r (db, last_write, writes) EXEC (@sql);
  FETCH NEXT FROM c INTO @n;
END
CLOSE c; DEALLOCATE c;
SELECT 'server_utc=' + CONVERT(varchar(30), SYSUTCDATETIME(), 126);
SELECT 'catalog|' + db + '|' + ISNULL(CONVERT(varchar(30), last_write, 126), 'null') + '|' + ISNULL(CAST(writes AS varchar(30)), '0') FROM @r ORDER BY db;
"@
}
else {
    $from = $FromUtc.ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss'); $to = $ToUtc.ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss')
    @"
SET NOCOUNT ON;
CREATE TABLE #l (LogDate datetime, ProcessInfo nvarchar(64), Txt nvarchar(max));
INSERT #l EXEC xp_readerrorlog 0, 1, N'Starting up database', NULL, '$from', '$to';
SELECT 'server_utc=' + CONVERT(varchar(30), SYSUTCDATETIME(), 126);
SELECT 'startups=' + CAST(COUNT(*) AS varchar(20)) FROM #l;
SELECT 'first=' + ISNULL(CONVERT(varchar(30), MIN(LogDate), 126), 'none') + '|last=' + ISNULL(CONVERT(varchar(30), MAX(LogDate), 126), 'none') FROM #l;
DROP TABLE #l;
"@
}

$env:SQLCMDPASSWORD = $pw
try {
    # `-e SQLCMDPASSWORD` (name only) forwards the variable from this process to the container: the value is on no command line.
    $out = & $DockerCommand exec -i -e SQLCMDPASSWORD $Container /opt/mssql-tools18/bin/sqlcmd -S localhost -U $user -C -h -1 -W -Q $query 2>&1 | ForEach-Object { [string]$_ }
    $code = $LASTEXITCODE
}
catch { Stop-Collection "docker could not be run: $($_.Exception.Message)" }
finally { Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue }
if ($code -ne 0) { Stop-Collection "docker exec/sqlcmd failed (exit $code): $(($out | Select-Object -First 3) -join ' ')" }
$lines = @($out | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if (-not ($lines | Where-Object { $_ -like 'server_utc=*' })) { Stop-Collection "the answer holds no server time: $(($lines | Select-Object -First 3) -join ' ')" }

$result = [ordered]@{ Mode = $Mode; Label = $Label; Container = $Container; Target = "$($i.Server) / $($i.Catalog)"; CollectedUtc = (Get-Date).ToUniversalTime().ToString('o') }
$result.ServerUtc = (($lines | Where-Object { $_ -like 'server_utc=*' } | Select-Object -First 1) -replace '^server_utc=', '')
if ($Mode -eq 'Markers') {
    $cats = @($lines | Where-Object { $_ -like 'catalog|*' } | ForEach-Object { $p = $_ -split '\|'; [pscustomobject]@{ Catalog = $p[1]; LastWrite = $p[2]; UserUpdates = [int64]$p[3] } })
    $result.Catalogs = $cats; $result.CatalogCount = $cats.Count; $result.TotalUserUpdates = ($cats | Measure-Object UserUpdates -Sum).Sum
}
else {
    $startups = $lines | Where-Object { $_ -like 'startups=*' } | Select-Object -First 1
    if (-not $startups) { Stop-Collection 'the error-log answer holds no start-up count' }
    $result.Startups = [int]($startups -replace '^startups=', ''); $result.FromUtc = $FromUtc.ToUniversalTime().ToString('o'); $result.ToUtc = $ToUtc.ToUniversalTime().ToString('o')
    $fl = $lines | Where-Object { $_ -like 'first=*' } | Select-Object -First 1; $result.FirstLast = [string]$fl
}
$obj = [pscustomobject]$result
if ($Json) { Write-TextLf $Json ($obj | ConvertTo-Json -Depth 5) }
if (-not $PassThru) { $obj | ConvertTo-Json -Depth 5 }
if ($PassThru) { $obj }
exit 0
