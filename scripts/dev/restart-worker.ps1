[CmdletBinding()]
param(
    [switch]$Headless
)

$ErrorActionPreference = "Stop"
$startScript = Join-Path $PSScriptRoot "start-worker.ps1"
& $startScript -Restart -Headless:$Headless
