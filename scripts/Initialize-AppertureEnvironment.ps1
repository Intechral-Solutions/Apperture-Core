<#
.SYNOPSIS
    One-time local development environment initialization for Apperture Core.
.DESCRIPTION
    Copies the docker-dev appsettings template when missing, optionally initializes
    User Secrets for the docker-compose SQL path, and prints preferred startup commands.

    Does not replace AppHost or Start-Dev.ps1. Does not create production bootstrap
    configuration.json (Windows/on-prem bootstrap store is separate from local dev).
.EXAMPLE
    .\scripts\Initialize-AppertureEnvironment.ps1
.EXAMPLE
    .\scripts\Initialize-AppertureEnvironment.ps1 -SkipUserSecrets
#>
[CmdletBinding()]
param(
    [switch]$SkipUserSecrets
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$templatePath = Join-Path $repoRoot 'deploy\config\examples\appsettings.docker-dev.json.example'
$devSettingsPath = Join-Path $repoRoot 'src\Apperture.ControlPlane\Apperture.Management.Host\appsettings.Development.json'
$hostProject = Join-Path $repoRoot 'src\Apperture.ControlPlane\Apperture.Management.Host\Apperture.Management.Host.csproj'

Write-Host 'Apperture local environment initialization' -ForegroundColor Cyan
Write-Host ''

if (-not (Test-Path $templatePath)) {
    throw "Template not found: $templatePath"
}

if (-not (Test-Path $devSettingsPath)) {
    Copy-Item -Path $templatePath -Destination $devSettingsPath
    Write-Host "Created appsettings.Development.json from docker-dev template." -ForegroundColor Green
} else {
    Write-Host "appsettings.Development.json already exists; left unchanged." -ForegroundColor Yellow
}

if (-not $SkipUserSecrets) {
  if (-not (Test-Path $hostProject)) {
      Write-Warning "Management.Host project not found; skipping User Secrets check."
  } else {
      $secretsList = dotnet user-secrets list --project $hostProject 2>$null
      if ($LASTEXITCODE -ne 0) {
          Write-Warning "Could not read User Secrets for Management.Host."
      } elseif ($secretsList -match 'SqlServer:Password') {
          Write-Host "User Secrets already contains SqlServer:Password; left unchanged." -ForegroundColor Yellow
      } else {
          Write-Host ''
          Write-Host 'Optional: set SqlServer:Password for the docker-compose / Start-Dev.ps1 path only.' -ForegroundColor DarkYellow
          Write-Host 'Aspire AppHost injects ConnectionStrings:DefaultConnection and does not require this for SQL.' -ForegroundColor DarkYellow
          $response = Read-Host 'Set SqlServer:Password in User Secrets now? [y/N]'
          if ($response -match '^[yY]') {
              $secure = Read-Host -AsSecureString -Prompt 'Enter SQL password (docker-compose sa password)'
              $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
              try {
                  $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
                  dotnet user-secrets set "SqlServer:Password" $plain --project $hostProject | Out-Null
                  Write-Host 'SqlServer:Password stored in User Secrets (not written to appsettings).' -ForegroundColor Green
              } finally {
                  [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
              }
          }
      }
  }
}

Write-Host ''
Write-Host 'Startup options (choose one; do not run both at once):' -ForegroundColor Cyan
Write-Host ''
Write-Host '  Preferred (Aspire):' -ForegroundColor Green
Write-Host '    dotnet run --project src/Apperture.AppHost/Apperture.AppHost.csproj'
Write-Host '    Management UI:  http://localhost:9001'
Write-Host '    Management API: http://localhost:9000'
Write-Host '    Dashboard:      use tokenized login URL printed by AppHost'
Write-Host ''
Write-Host '  Alternate (scripts / docker-compose SQL):' -ForegroundColor Green
Write-Host '    .\scripts\Start-Dev.ps1'
Write-Host ''
Write-Host 'SQL volumes are intentionally separate:' -ForegroundColor DarkYellow
Write-Host '  - Aspire:          apperture-aspire-sql (dynamic port under AppHost)'
Write-Host '  - docker-compose:  apperture-sqlserver (localhost:1433)'
Write-Host 'Users and data are not shared between these paths.' -ForegroundColor DarkYellow
Write-Host ''
Write-Host 'Production/on-prem bootstrap state uses %ProgramData%\Apperture\bootstrap\configuration.json' -ForegroundColor DarkGray
Write-Host '(override with APPERTURE_BOOTSTRAP_CONFIG_PATH). Not required for Aspire local dev.' -ForegroundColor DarkGray
