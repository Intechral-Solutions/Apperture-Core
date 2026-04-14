<#
.SYNOPSIS
    Restarts the Apperture testing worker runtime (delegates to start-worker.ps1 -Restart).
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
