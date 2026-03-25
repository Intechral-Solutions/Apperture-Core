<#
.SYNOPSIS
    Runs Apperture tests (unit, integration, arch, e2e).
.DESCRIPTION
    Executes dotnet test for the solution. Use -Filter to run a subset.
    For Management Host governance + other SqlServer integration tests locally, set APPERTURE_TEST_SQL
    and MSSQL_SA_PASSWORD (see tests/Apperture.IntegrationTests/README-ManagementHostGovernance.md).
    GitHub Actions sets these via the SQL Server service container in .github/workflows/ci.yml.
.EXAMPLE
    .\test.ps1
    .\test.ps1 -Filter "UnitTests"
    .\test.ps1 -NoBuild -Verbose
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$Filter,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$ResolveScript = Join-Path $RepoRoot "scripts\dev\Get-DevDotNetSdk.ps1"
$sdk = & $ResolveScript -RepoRoot $RepoRoot
$env:DOTNET_ROOT = $sdk.DotNetRoot
if ($env:PATH -notlike "*$($sdk.DotNetRoot)*") {
    $env:PATH = "$($sdk.DotNetRoot);$env:PATH"
}
$Solution = Join-Path $RepoRoot "Apperture.slnx"

if (-not (Test-Path $Solution)) {
    Write-Error "Solution not found: $Solution"
}

$testArgs = @("test", $Solution, "-c", $Configuration)
if ($NoBuild) { $testArgs += "--no-build" }
if ($Filter) { $testArgs += "--filter", "FullyQualifiedName~$Filter" }

Push-Location $RepoRoot
try {
    Write-Host "Running tests..." -ForegroundColor Cyan
    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
    Write-Host "Tests passed." -ForegroundColor Green
} finally {
    Pop-Location
}
