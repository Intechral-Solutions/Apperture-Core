<#
.SYNOPSIS
    Generates OpenAPI/Swagger document or client from the Management API.
.DESCRIPTION
    Placeholder for OpenAPI generation (e.g. NSwag, Swashbuckle CLI, or dotnet-openapi).
    Configure once the API exposes OpenAPI and you want to generate client or spec file.
.EXAMPLE
    .\generate-openapi.ps1
    .\generate-openapi.ps1 -OutputPath .\openapi.json
#>
[CmdletBinding()]
param(
    [string]$OutputPath = "openapi.json",
    [string]$ProjectPath
)

$ErrorActionPreference = "Stop"
$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$ApiProject = $ProjectPath ?? (Join-Path $RepoRoot "src\Apperture.ControlPlane\Apperture.Management.Api\Apperture.Management.Api.csproj")

Write-Host "OpenAPI generation: not yet implemented." -ForegroundColor Yellow
Write-Host "Options: add Swashbuckle/NSwag to the API, then use dotnet-openapi or nswag to generate spec/client to: $OutputPath" -ForegroundColor Gray
