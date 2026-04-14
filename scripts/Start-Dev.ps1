<#
.SYNOPSIS
    Starts the Apperture platform in development mode (convenience entry point).
.DESCRIPTION
    With no service switches: calls scripts\dev\up.ps1 to launch SQL Server, Management API, UI, and worker.
    A prior run is stopped first (ports 9000/9001) so rebuild does not fail on locked DLLs.

    With one or more of -Workers, -Host, -Ui, -Db: runs only the matching scripts\dev\restart-*.ps1
    scripts so you can start or restart any combination of dev processes/containers without a full up.ps1.

    This script does not use [CmdletBinding()] so that -Db can be used: advanced functions reserve -db for -Debug.
.PARAMETER NoBrowser
    Passed to up.ps1 (default path) or restart-ui.ps1 when -Ui is used: do not auto-open the UI in the browser.
.PARAMETER Workers
    Run scripts\dev\restart-worker.ps1.
.PARAMETER RestartHost
    Run scripts\dev\restart-host.ps1. Use -Host (alias) on the command line; a parameter named Host is not used
    because it conflicts with PowerShell's automatic $Host variable.
.PARAMETER Ui
    Run scripts\dev\restart-ui.ps1.
.PARAMETER Db
    Run scripts\dev\restart-db.ps1 (SQL Server container).
.EXAMPLE
    .\Start-Dev.ps1
    .\Start-Dev.ps1 -NoBrowser
.EXAMPLE
    .\Start-Dev.ps1 -Ui
    .\Start-Dev.ps1 -Host -Ui -NoBrowser
.EXAMPLE
    .\Start-Dev.ps1 -Workers -Db
#>
param(
    [switch]$NoBrowser,
    [switch]$Workers,
    [Alias('Host')]
    [switch]$RestartHost,
    [switch]$Ui,
    [switch]$Db
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$DevDir = Join-Path $ScriptDir "dev"

$selectiveRestart = $Workers -or $RestartHost -or $Ui -or $Db
if (-not $selectiveRestart) {
    $upParams = @{}
    if ($PSBoundParameters.ContainsKey('NoBrowser')) {
        $upParams['NoBrowser'] = $NoBrowser
    }
    & (Join-Path $DevDir "up.ps1") @upParams
    return
}

$uiParams = @{}
if ($PSBoundParameters.ContainsKey('NoBrowser')) {
    $uiParams['NoBrowser'] = $NoBrowser
}

if ($RestartHost) {
    & (Join-Path $DevDir "restart-host.ps1")
}
if ($Workers) {
    & (Join-Path $DevDir "restart-worker.ps1")
}
if ($Ui) {
    & (Join-Path $DevDir "restart-ui.ps1") @uiParams
}
if ($Db) {
    & (Join-Path $DevDir "restart-db.ps1")
}
