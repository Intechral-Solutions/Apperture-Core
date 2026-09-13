[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\lib\Import-DeploymentCommand.ps1')
Initialize-DeploymentCommandContext

Write-Host 'Deployment status (scaffold)' -ForegroundColor Cyan
Write-Host "  Repository: $script:RepoRoot" -ForegroundColor Gray
Write-Host '  Targets planned: windows-service, azure, docker, local' -ForegroundColor Gray
Write-DeploymentScaffoldMessage -CommandName 'status'
