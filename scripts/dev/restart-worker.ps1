<#
.SYNOPSIS
    Restarts the Apperture testing worker runtime via local dotnet run (not Docker).
.DESCRIPTION
    Delegates to scripts\dev\start-worker.ps1 -Restart: stops existing WorkerHost dotnet processes, then starts
    a new instance in a new console window (same model as scripts\dev\up.ps1). Docker is not used for the worker here.
.NOTES
    Roadmap: This entry point will evolve into a general "restart workers" capability that can
    restart all workers or specific workers by name/id, aligned with dynamic worker management
    across dev and operational workflows. Until then, it only restarts the single local
    WorkerHost process used in scripts\dev\up.ps1.
#>
[CmdletBinding()]
param(
    [switch]$Headless
)

$ErrorActionPreference = "Stop"
$startScript = Join-Path $PSScriptRoot "start-worker.ps1"
& $startScript -Restart -Headless:$Headless
