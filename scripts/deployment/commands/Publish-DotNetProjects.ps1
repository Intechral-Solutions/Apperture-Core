[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\lib\Import-DeploymentCommand.ps1')
Initialize-DeploymentCommandContext

$outputRoot = Join-Path $script:RepoRoot 'artifacts\publish'
Write-Host "Publishing Management.Host to $outputRoot\management-host (partial scaffold)..." -ForegroundColor Cyan
Invoke-AppertureDotNetPublish -RepoRoot $script:RepoRoot -DotNetExe $script:Sdk.DotNetExe `
    -ProjectPath $script:DevPaths.HostProject -Configuration $Configuration `
    -OutputPath (Join-Path $outputRoot 'management-host')

Write-DeploymentScaffoldMessage -CommandName 'publish' -Detail 'WorkerHost publish layout and packaging are TODO.'
