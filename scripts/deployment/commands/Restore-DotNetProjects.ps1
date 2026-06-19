[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\lib\Import-DeploymentCommand.ps1')
Initialize-DeploymentCommandContext
Invoke-AppertureDotNetRestore -RepoRoot $script:RepoRoot -DotNetExe $script:Sdk.DotNetExe -Configuration $Configuration
