# Deployment-specific interactive menu (delegates to Manage.ps1).
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$manageScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'Manage.ps1'
& $manageScript -Area deployment -Command status
