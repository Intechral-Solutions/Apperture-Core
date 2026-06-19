[CmdletBinding()]
param(
    [ValidateSet('native', 'aspire')][string]$Mode = 'native'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\lib\Import-DevCommand.ps1')
Initialize-DevCommandContext
Test-DevRuntimeModeMismatch -ExpectedMode $Mode

if ($Mode -eq 'aspire') {
    Stop-DevAspireProcesses
    Clear-DevServicePid -ServiceName 'aspire'
    Clear-DevRuntimeMode
    Write-Host 'Aspire dev environment stopped.' -ForegroundColor Green
    return
}

Stop-DevNativeStack -Paths $script:DevPaths
Write-Host 'Native dev environment stopped (SQL container left running).' -ForegroundColor Green
