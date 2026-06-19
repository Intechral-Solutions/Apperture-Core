# Deployment command dispatcher (scaffold).
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Command,
    [string]$Target = '',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$commandsDir = Join-Path $PSScriptRoot 'commands'
$map = @{
    'status'    = 'Show-DeploymentStatus.ps1'
    'restore'   = 'Restore-DotNetProjects.ps1'
    'build'     = 'Build-DotNetProjects.ps1'
    'publish'   = 'Publish-DotNetProjects.ps1'
    'deploy'    = 'Deploy-Environment.ps1'
    'build-ui'  = 'Build-UiAssets.ps1'
}

if (-not $map.ContainsKey($Command)) {
    throw "Unknown deployment command: $Command"
}

$scriptPath = Join-Path $commandsDir $map[$Command]
$params = @{}
if ($Target) { $params['Target'] = $Target }
if ($Command -in @('restore', 'build', 'publish', 'deploy', 'build-ui')) {
    $params['Configuration'] = $Configuration
}

& $scriptPath @params
if (-not $?) { exit 1 }
