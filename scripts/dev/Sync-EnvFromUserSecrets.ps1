<#
.SYNOPSIS
    Syncs the SQL Server password from User Secrets to .env for Docker Compose.
.DESCRIPTION
    Reads SqlServer:Password from Management.Host User Secrets and writes MSSQL_SA_PASSWORD
    to .env at repo root. Docker Compose uses .env; the .NET app reads directly from User Secrets.
    Run this before docker compose when starting the dev environment.
.EXAMPLE
    .\Sync-EnvFromUserSecrets.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$HostProject = Join-Path $RepoRoot "src\Apperture.ControlPlane\Apperture.Management.Host\Apperture.Management.Host.csproj"
$UserSecretsId = "a1b2c3d4-5e6f-4a5b-8c9d-0e1f2a3b4c5d"
$SecretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$UserSecretsId\secrets.json"
$EnvPath = Join-Path $RepoRoot ".env"

if (-not (Test-Path $HostProject)) {
    Write-Error "Management.Host project not found: $HostProject"
}

if (-not (Test-Path $SecretsPath)) {
    Write-Host "User Secrets not initialized. Setting SqlServer:Password..." -ForegroundColor Yellow
    dotnet user-secrets set "SqlServer:Password" "YourStrong!Passw0rd" --project $HostProject | Out-Null
    Write-Host "  Set SqlServer:Password to default. Change with: dotnet user-secrets set `"SqlServer:Password`" `"<password>`" --project src/Apperture.ControlPlane/Apperture.Management.Host" -ForegroundColor Gray
}

$secretsJson = Get-Content -Path $SecretsPath -Raw
$secrets = $secretsJson | ConvertFrom-Json
$password = $secrets."SqlServer:Password"

if ([string]::IsNullOrWhiteSpace($password)) {
    Write-Error "SqlServer:Password not found in User Secrets. Run: dotnet user-secrets set `"SqlServer:Password`" `"<password>`" --project src/Apperture.ControlPlane/Apperture.Management.Host"
}

$envContent = "MSSQL_SA_PASSWORD=$password"
[System.IO.File]::WriteAllText($EnvPath, $envContent)
Write-Host "Synced .env from User Secrets (SqlServer:Password -> MSSQL_SA_PASSWORD)" -ForegroundColor Green
