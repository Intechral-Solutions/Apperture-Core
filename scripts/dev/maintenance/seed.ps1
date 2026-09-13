<#
.SYNOPSIS
    Seeds the development database with sample data.
.DESCRIPTION
    Placeholder for optional sample-data seeding. EF persistence and dev identity bootstrap
    already run on Management.Host startup (migrations + DevTestUser / BootstrapPlatformAdmin).
.EXAMPLE
    .\seed.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Write-Host "Seed script: not yet implemented (sample data only)." -ForegroundColor Yellow
Write-Host "Identity and schema seeding use Management.Host startup (migrations + appsettings.Development.json bootstrap)." -ForegroundColor Gray
