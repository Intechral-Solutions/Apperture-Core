<#
.SYNOPSIS
    Top-level Apperture script command surface.
.EXAMPLE
    ./scripts/Manage.ps1 -Area dev -Command start
    ./scripts/Manage.ps1 -Area dev -Command start -Mode aspire
    ./scripts/Manage.ps1 -Command restart
    ./scripts/Manage.ps1 -Command defaults
#>
[CmdletBinding()]
param(
    [ValidateSet('dev', 'deployment')]
    [string]$Area,

    [ValidateSet('start', 'stop', 'restart', 'restart-service', 'status',
        'restore', 'build', 'rebuild', 'clean',
        'build-ui', 'watch-ui', 'publish', 'deploy',
        'defaults', 'reset-defaults')]
    [string]$Command,

    [ValidateSet('native', 'aspire')]
    [string]$Mode,

    [ValidateSet('api', 'ui', 'worker', 'db', 'all')]
    [string]$Service,

    [ValidateSet('windows-service', 'azure', 'docker', 'local')]
    [string]$Target,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration,

    [switch]$Build,
    [switch]$NoBrowser,
    [switch]$SkipUiBuild,
    [switch]$Force,
    [switch]$Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptsRoot = $PSScriptRoot
. (Join-Path $ScriptsRoot 'lib\Apperture.ScriptRoot.ps1')
. (Join-Path $ScriptsRoot 'lib\Apperture.ScriptHelp.ps1')
. (Join-Path $ScriptsRoot 'lib\Apperture.ScriptSettings.ps1')

if ($Help -or [string]::IsNullOrWhiteSpace($Command)) {
    Show-ManageHelp
    exit 0
}

if ($Command -eq 'defaults') {
    Show-ScriptSettings
    exit 0
}

if ($Command -eq 'reset-defaults') {
    Clear-ScriptSettings -Force:$Force
    exit 0
}

$resolved = Get-ResolvedManageParameters -Area $Area -Command $Command -Mode $Mode -Service $Service -Target $Target -Configuration $Configuration

$devCommands = @('start', 'stop', 'restart', 'restart-service', 'status', 'restore', 'build', 'rebuild', 'clean', 'build-ui', 'watch-ui')
$deploymentCommands = @('status', 'restore', 'build', 'publish', 'deploy', 'build-ui')

if ([string]::IsNullOrWhiteSpace($resolved.Area)) {
    if ($Command -in $devCommands) { $resolved.Area = 'dev' }
    elseif ($Command -in $deploymentCommands) { $resolved.Area = 'deployment' }
}

if ([string]::IsNullOrWhiteSpace($resolved.Area)) {
    Write-ManageValidationError "Area is required for command '$Command'. Use -Area dev or -Area deployment, or save a default with a prior successful command."
}

if ($resolved.Area -eq 'dev' -and $Command -notin $devCommands) {
    Write-ManageValidationError "Command '$Command' is not valid for -Area dev."
}

if ($resolved.Area -eq 'deployment' -and $Command -notin $deploymentCommands) {
    Write-ManageValidationError "Command '$Command' is not valid for -Area deployment."
}

if ($Command -eq 'deploy' -and [string]::IsNullOrWhiteSpace($resolved.Target)) {
    Write-ManageValidationError 'Deploy requires -Target windows-service|azure|docker|local (or a saved deployment target).'
}

$exitCode = 0
try {
    if ($resolved.Area -eq 'dev') {
        $invokeParams = @{
            Command = $Command
            Mode    = $resolved.Mode
        }
        if ($Command -eq 'restart-service') { $invokeParams['Service'] = $resolved.Service }
        if ($Command -in @('restore', 'build', 'clean', 'rebuild') -and $resolved.Configuration) {
            $invokeParams['Configuration'] = $resolved.Configuration
        }
        if ($Build) { $invokeParams['Build'] = $true }
        if ($NoBrowser) { $invokeParams['NoBrowser'] = $true }
        if ($SkipUiBuild) { $invokeParams['SkipUiBuild'] = $true }

        & (Join-Path $ScriptsRoot 'dev\Invoke-Dev.ps1') @invokeParams
    }
    else {
        $invokeParams = @{
            Command = $Command
        }
        if ($resolved.Target) { $invokeParams['Target'] = $resolved.Target }
        if ($Command -in @('restore', 'build', 'publish', 'deploy', 'build-ui') -and $resolved.Configuration) {
            $invokeParams['Configuration'] = $resolved.Configuration
        }

        & (Join-Path $ScriptsRoot 'deployment\Invoke-Deployment.ps1') @invokeParams
    }

    if (-not $? -or $LASTEXITCODE -ne 0) {
        $exitCode = if ($LASTEXITCODE -ne 0) { $LASTEXITCODE } else { 1 }
    }
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    $exitCode = 1
}

if ($exitCode -eq 0) {
    Save-ManageParametersToSettings -Resolved $resolved
}

exit $exitCode
