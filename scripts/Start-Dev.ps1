<#
.SYNOPSIS
    Starts the Apperture platform in development mode (convenience entry point).
.DESCRIPTION
    Calls scripts\dev\up.ps1 to launch the Management API and UI Server.
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
