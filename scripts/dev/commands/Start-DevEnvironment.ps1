# Starts the native or Aspire dev environment.
[CmdletBinding()]
param(
    [ValidateSet('native', 'aspire')][string]$Mode = 'native',
    [switch]$Build,
    [switch]$NoBrowser
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\lib\Import-DevCommand.ps1')
Initialize-DevCommandContext

Test-DevRuntimeModeMismatch -ExpectedMode $Mode

if ($Mode -eq 'aspire') {
    $listeners = @(Get-DevTcpListeners -Port $script:DevPaths.ApiPort) + @(Get-DevTcpListeners -Port $script:DevPaths.UiPort)
    if ($listeners.Count -gt 0) {
        Write-Host 'Warning: processes are still listening on ports 9000/9001. Stop native dev before starting Aspire.' -ForegroundColor Yellow
    }

    if ($Build) {
        Invoke-AppertureDotNetBuild -RepoRoot $script:RepoRoot -DotNetExe $script:Sdk.DotNetExe -Configuration Debug
    }

    Start-DevAspireAppHost -RepoRoot $script:RepoRoot -DotNetExe $script:Sdk.DotNetExe -AppHostProject $script:DevPaths.AppHostProject
    Write-DevRuntimeMode -Mode 'aspire'
    Write-Host 'Aspire dev environment started. Use the dashboard URL printed in the AppHost terminal.' -ForegroundColor Green
    Write-Host '  API: http://localhost:9000' -ForegroundColor Gray
    Write-Host '  UI:  http://localhost:9001' -ForegroundColor Gray
    return
}

Write-Host 'Starting Apperture dev environment (native / docker-compose SQL)...' -ForegroundColor Cyan
$saPassword = Start-DevDockerSql -RepoRoot $script:RepoRoot -DotNetExe $script:Sdk.DotNetExe

Write-Host 'Stopping any prior native dev API / UI / worker...' -ForegroundColor Cyan
Stop-DevNativeStack -Paths $script:DevPaths
Wait-DevListenPortsFree -Ports @($script:DevPaths.ApiPort, $script:DevPaths.UiPort)

try {
    Start-DevNativeApi -Paths $script:DevPaths -DotNetExe $script:Sdk.DotNetExe -Build:$Build
    Start-DevNativeUi -Paths $script:DevPaths -DotNetExe $script:Sdk.DotNetExe -Build:$Build -NoBrowser:$NoBrowser
    Start-DevNativeWorker -Paths $script:DevPaths -DotNetExe $script:Sdk.DotNetExe -SaPassword $saPassword
}
catch {
    if ($_.Exception.Message -match 'MSB1009|does not exist|Build FAILED|could not find') {
        Write-Host ''
        Write-Host 'Hint: run ./scripts/Manage.ps1 -Area dev -Command build (or start -Build) before starting without a build.' -ForegroundColor Yellow
    }
    throw
}

Write-DevRuntimeMode -Mode 'native'
Write-Host 'Native dev environment started.' -ForegroundColor Green
Write-Host '  SQL: localhost:1433' -ForegroundColor Gray
Write-Host '  API: http://localhost:9000' -ForegroundColor Gray
Write-Host '  UI:  http://localhost:9001' -ForegroundColor Gray
Write-Host 'Stop with: ./scripts/Manage.ps1 -Area dev -Command stop' -ForegroundColor DarkGray
