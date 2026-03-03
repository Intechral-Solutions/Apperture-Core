<#
.SYNOPSIS
    Builds the Apperture solution.
.DESCRIPTION
    Restores and builds the solution. Use -Configuration Release for production builds.
.EXAMPLE
    .\build.ps1
    .\build.ps1 -Configuration Release -NoRestore
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$Solution = Join-Path $RepoRoot "Apperture.slnx"

if (-not (Test-Path $Solution)) {
    Write-Error "Solution not found: $Solution"
}

Push-Location $RepoRoot
try {
    if (-not $NoRestore) {
        Write-Host "Restoring..." -ForegroundColor Cyan
        dotnet restore $Solution
        if ($LASTEXITCODE -ne 0) { throw "Restore failed." }
    }
    Write-Host "Building ($Configuration)..." -ForegroundColor Cyan
    dotnet build $Solution -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
    Write-Host "Build succeeded." -ForegroundColor Green
} finally {
    Pop-Location
}
