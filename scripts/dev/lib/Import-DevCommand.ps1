# Initializes shared context for dev command scripts.
Set-StrictMode -Version Latest

$script:ImportDevCommandScriptsRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scriptsRoot = $script:ImportDevCommandScriptsRoot

. (Join-Path $scriptsRoot 'lib\Apperture.ScriptRoot.ps1')
. (Join-Path $scriptsRoot 'lib\Apperture.DotNet.ps1')
. (Join-Path $scriptsRoot 'lib\Apperture.DotNetSolution.ps1')
. (Join-Path $scriptsRoot 'lib\Apperture.UiAssets.ps1')
. (Join-Path $scriptsRoot 'dev\lib\DevProjectPaths.ps1')
. (Join-Path $scriptsRoot 'dev\lib\DevRuntimeState.ps1')
. (Join-Path $scriptsRoot 'dev\lib\DevProcess.ps1')
. (Join-Path $scriptsRoot 'dev\lib\DevDockerSql.ps1')
. (Join-Path $scriptsRoot 'dev\lib\DevDotNetLaunch.ps1')
. (Join-Path $scriptsRoot 'dev\lib\DevServiceLaunch.ps1')

function Initialize-DevCommandContext {
    $script:RepoRoot = Get-AppertureRepoRoot
    $script:Sdk = Initialize-AppertureDotNetSession -RepoRoot $script:RepoRoot
    $script:DevPaths = Get-DevProjectPaths -RepoRoot $script:RepoRoot
    Test-DevProjectPaths -Paths $script:DevPaths
}
