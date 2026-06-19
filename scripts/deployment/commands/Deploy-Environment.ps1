[CmdletBinding()]
param(
    [ValidateSet('windows-service', 'azure', 'docker', 'local')][string]$Target = '',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\lib\Import-DeploymentCommand.ps1')
Initialize-DeploymentCommandContext

if ([string]::IsNullOrWhiteSpace($Target)) {
    throw 'Deploy requires -Target windows-service|azure|docker|local'
}

Write-Host "Deploy target: $Target ($Configuration)" -ForegroundColor Cyan
Write-DeploymentScaffoldMessage -CommandName 'deploy' -Detail "Target '$Target' workflow is not implemented."
