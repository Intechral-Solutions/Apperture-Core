[CmdletBinding()]
param(
    [ValidateSet('native', 'aspire')][string]$Mode = 'native',
    [switch]$Build,
    [switch]$NoBrowser
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$stopScript = Join-Path $PSScriptRoot 'Stop-DevEnvironment.ps1'
$startScript = Join-Path $PSScriptRoot 'Start-DevEnvironment.ps1'

& $stopScript -Mode $Mode
& $startScript -Mode $Mode -Build:$Build -NoBrowser:$NoBrowser
