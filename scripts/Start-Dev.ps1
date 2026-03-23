<#
.SYNOPSIS
    Starts the Apperture platform in development mode (convenience entry point).
.DESCRIPTION
    Calls scripts\dev\up.ps1 to launch the Management API and UI Server. A prior run is stopped first
    (ports 9000/9001) so rebuild does not fail on locked DLLs.
.EXAMPLE
    .\Start-Dev.ps1
    .\Start-Dev.ps1 -NoBrowser
#>
[CmdletBinding()]
param(
    [switch]$NoBrowser
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $ScriptDir "dev\up.ps1") @PSBoundParameters
