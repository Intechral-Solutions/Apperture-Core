# Dev command dispatcher for Manage.ps1.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Command,
    [string]$Mode = 'native',
    [string]$Service = 'all',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [switch]$Build,
    [switch]$NoBrowser,
    [switch]$SkipUiBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$commandsDir = Join-Path $PSScriptRoot 'commands'
$map = @{
    'start'           = 'Start-DevEnvironment.ps1'
    'stop'            = 'Stop-DevEnvironment.ps1'
    'restart'         = 'Restart-DevEnvironment.ps1'
    'restart-service' = 'Restart-DevService.ps1'
    'status'          = 'Show-DevStatus.ps1'
    'restore'         = 'Restore-DotNetProjects.ps1'
    'build'           = 'Build-DotNetProjects.ps1'
    'clean'           = 'Clean-DotNetProjects.ps1'
    'rebuild'         = 'Rebuild-DotNetProjects.ps1'
    'build-ui'        = 'Build-UiAssets.ps1'
    'watch-ui'        = 'Start-UiAssetWatch.ps1'
}

if (-not $map.ContainsKey($Command)) {
    throw "Unknown dev command: $Command"
}

$scriptPath = Join-Path $commandsDir $map[$Command]
$params = @{}

switch ($Command) {
    { $_ -in @('start', 'stop', 'restart', 'restart-service') } {
        $params['Mode'] = $Mode
    }
    'restart-service' { $params['Service'] = $Service }
    { $_ -in @('start', 'restart', 'restart-service') } {
        if ($Build) { $params['Build'] = $true }
        if ($NoBrowser) { $params['NoBrowser'] = $true }
    }
    { $_ -in @('restore', 'build', 'clean', 'rebuild') } {
        $params['Configuration'] = $Configuration
    }
}

if ($SkipUiBuild) {
    Write-Host 'SkipUiBuild is reserved for future UI asset integration.' -ForegroundColor DarkGray
}

& $scriptPath @params
if (-not $?) { exit 1 }
