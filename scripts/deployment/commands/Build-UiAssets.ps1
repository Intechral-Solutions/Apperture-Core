[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\lib\Import-DeploymentCommand.ps1')
Initialize-DeploymentCommandContext
Invoke-UiAssetBuild -RepoRoot $script:RepoRoot
