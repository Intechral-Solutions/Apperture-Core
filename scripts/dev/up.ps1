<#
.SYNOPSIS
    Starts the Apperture platform in development mode (Management API + UI Server).
.DESCRIPTION
    Launches the Management API (port 9000) and Management UI Server (port 9001) in separate
    console windows. Use down.ps1 to stop all dev processes.
.EXAMPLE
    .\up.ps1
    .\up.ps1 -NoBrowser
#>
[CmdletBinding()]
param(
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"
$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$ApiProject = Join-Path $RepoRoot "src\Apperture.ControlPlane\Apperture.Management.Api\Apperture.Management.Api.csproj"
$UiServerProject = Join-Path $RepoRoot "src\Apperture.ControlPlane\Apperture.Management.Ui.Server\Apperture.Management.Ui.Server.csproj"

if (-not (Test-Path $ApiProject)) {
    Write-Error "Management API project not found: $ApiProject"
}
if (-not (Test-Path $UiServerProject)) {
    Write-Error "Management UI Server project not found: $UiServerProject"
}

Write-Host "Starting Apperture dev environment..." -ForegroundColor Cyan
Write-Host "  API:        http://localhost:9000" -ForegroundColor Gray
Write-Host "  UI:         http://localhost:9001" -ForegroundColor Gray
Write-Host ""

$apiArgs = @("run", "--project", $ApiProject, "--launch-profile", "http")
$uiArgs = @("run", "--project", $UiServerProject, "--launch-profile", "Ui.Server")
if ($NoBrowser) {
    $uiArgs = @("run", "--project", $UiServerProject, "--no-launch-profile")
    $uiArgs += "--urls", "http://localhost:9001"
}

Start-Process -FilePath "dotnet" -ArgumentList $apiArgs -WorkingDirectory $RepoRoot -WindowStyle Normal
Start-Sleep -Seconds 2
Start-Process -FilePath "dotnet" -ArgumentList $uiArgs -WorkingDirectory $RepoRoot -WindowStyle Normal

Write-Host "Dev servers started. Close their windows or run scripts\dev\down.ps1 to stop." -ForegroundColor Green
