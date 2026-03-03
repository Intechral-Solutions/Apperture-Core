<#
.SYNOPSIS
    Packs NuGet packages for publishable projects.
.DESCRIPTION
    Runs dotnet pack on the solution. Use -Configuration Release for production packages.
.EXAMPLE
    .\pack.ps1
    .\pack.ps1 -Configuration Release -OutputPath .\nupkgs
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputPath,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$Solution = Join-Path $RepoRoot "Apperture.slnx"

if (-not (Test-Path $Solution)) {
    Write-Error "Solution not found: $Solution"
}

$packArgs = @("pack", $Solution, "-c", $Configuration)
if ($NoBuild) { $packArgs += "--no-build" }
if ($OutputPath) {
    $outDir = if ([System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $RepoRoot $OutputPath }
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    $packArgs += "-o", $outDir
}

Push-Location $RepoRoot
try {
    Write-Host "Packing ($Configuration)..." -ForegroundColor Cyan
    & dotnet @packArgs
    if ($LASTEXITCODE -ne 0) { throw "Pack failed." }
    Write-Host "Pack succeeded." -ForegroundColor Green
} finally {
    Pop-Location
}
