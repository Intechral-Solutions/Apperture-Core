[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\lib\Import-DevCommand.ps1')
Initialize-DevCommandContext

Write-Host 'Apperture dev status' -ForegroundColor Cyan
Write-Host ''

$modeState = Read-DevRuntimeMode
if ($modeState) {
    Write-Host "Runtime mode: $($modeState.mode) (started $($modeState.startedUtc))" -ForegroundColor Gray
}
else {
    Write-Host 'Runtime mode: (none recorded)' -ForegroundColor Gray
}

Write-Host ''
Write-Host 'Port listeners:' -ForegroundColor Yellow
Get-DevPortStatus -Ports @($script:DevPaths.ApiPort, $script:DevPaths.UiPort) | Format-Table -AutoSize

Write-Host 'Saved PIDs:' -ForegroundColor Yellow
foreach ($name in @('api', 'ui', 'worker', 'aspire')) {
    $pidValue = Read-DevServicePid -ServiceName $name
    if ($pidValue) {
        $proc = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
        $status = if ($proc) { "running ($($proc.ProcessName))" } else { 'not running (stale pid file)' }
        Write-Host ('  {0}: PID {1} - {2}' -f $name, $pidValue, $status) -ForegroundColor Gray
    }
    else {
        Write-Host ('  {0}: (no pid file)' -f $name) -ForegroundColor DarkGray
    }
}

Write-Host ''
Write-Host 'SQL container:' -ForegroundColor Yellow
if (Test-DevSqlContainerRunning) {
    Write-Host '  apperture-sqlserver: running' -ForegroundColor Gray
}
else {
    Write-Host '  apperture-sqlserver: not running' -ForegroundColor DarkGray
}

$workerStatePath = Get-DevWorkerStatePath
if (Test-Path -LiteralPath $workerStatePath) {
    Write-Host ''
    Write-Host 'Worker state:' -ForegroundColor Yellow
    Get-Content -LiteralPath $workerStatePath -Raw | Write-Host -ForegroundColor Gray
}

Write-Host ''
Write-Host "Logs directory: $(Get-AppertureDevLogDir)" -ForegroundColor DarkGray
