# Initializes shared context for deployment command scripts.
Set-StrictMode -Version Latest

$script:ImportDeploymentCommandScriptsRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scriptsRoot = $script:ImportDeploymentCommandScriptsRoot

. (Join-Path $scriptsRoot 'lib\Apperture.ScriptRoot.ps1')
. (Join-Path $scriptsRoot 'lib\Apperture.DotNet.ps1')
. (Join-Path $scriptsRoot 'lib\Apperture.DotNetSolution.ps1')
. (Join-Path $scriptsRoot 'lib\Apperture.UiAssets.ps1')
. (Join-Path $scriptsRoot 'dev\lib\DevProjectPaths.ps1')

function Initialize-DeploymentCommandContext {
    $script:RepoRoot = Get-AppertureRepoRoot
    $script:Sdk = Initialize-AppertureDotNetSession -RepoRoot $script:RepoRoot
    $script:DevPaths = Get-DevProjectPaths -RepoRoot $script:RepoRoot
}

function Write-DeploymentScaffoldMessage {
    param(
        [Parameter(Mandatory)][string]$CommandName,
        [string]$Detail = ''
    )

    Write-Host "Deployment command '$CommandName' is not fully implemented yet." -ForegroundColor Yellow
    if ($Detail) {
        Write-Host "  $Detail" -ForegroundColor DarkGray
    }
    Write-Host '  TODO: implement packaging and environment-specific deployment workflow.' -ForegroundColor DarkGray
}
