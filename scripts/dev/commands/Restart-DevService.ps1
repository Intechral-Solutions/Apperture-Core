[CmdletBinding()]
param(
    [ValidateSet('native', 'aspire')][string]$Mode = 'native',
    [ValidateSet('api', 'ui', 'worker', 'db', 'all')][string]$Service = 'all',
    [switch]$Build,
    [switch]$NoBrowser
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\lib\Import-DevCommand.ps1')
Initialize-DevCommandContext

if ($Mode -eq 'aspire') {
    Write-Host 'Service-specific restart is only supported in native mode.' -ForegroundColor Yellow
    Write-Host 'Use: ./scripts/Manage.ps1 -Area dev -Command restart -Mode aspire' -ForegroundColor Gray
    Write-Host 'Or restart individual resources from the Aspire dashboard.' -ForegroundColor Gray
    exit 0
}

if ($Service -eq 'all') {
    $restartScript = Join-Path $PSScriptRoot 'Restart-DevEnvironment.ps1'
    & $restartScript -Mode native -Build:$Build -NoBrowser:$NoBrowser
    return
}

if ($Service -eq 'db') {
    Restart-DevDockerSqlService -RepoRoot $script:RepoRoot -DotNetExe $script:Sdk.DotNetExe
    return
}

$saPassword = $null
$envPath = Join-Path $script:RepoRoot '.env'
if (Test-Path -LiteralPath $envPath) {
    $envLine = (Get-Content -Path $envPath | Select-String -Pattern '^MSSQL_SA_PASSWORD=').Line
    if ($envLine) { $saPassword = $envLine.Split('=', 2)[1] }
}

Restart-DevNativeService -Paths $script:DevPaths -DotNetExe $script:Sdk.DotNetExe -Service $Service -Build:$Build -NoBrowser:$NoBrowser -SaPassword $saPassword
Write-Host "Service '$Service' restarted." -ForegroundColor Green
