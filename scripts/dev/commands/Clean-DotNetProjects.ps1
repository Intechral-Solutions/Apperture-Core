[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\lib\Import-DevCommand.ps1')
Initialize-DevCommandContext
Invoke-AppertureDotNetClean -RepoRoot $script:RepoRoot -DotNetExe $script:Sdk.DotNetExe -Configuration $Configuration
