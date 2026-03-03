<#
.SYNOPSIS
    Resets the development database (drop + recreate + migrate).
.DESCRIPTION
    Placeholder for database reset. Implement when persistence is added (e.g. dotnet ef database drop/update).
.EXAMPLE
    .\reset-db.ps1
    .\reset-db.ps1 -Confirm
#>
[CmdletBinding()]
param(
    [switch]$Confirm
)

$ErrorActionPreference = "Stop"
Write-Host "Reset-db script: not yet implemented." -ForegroundColor Yellow
Write-Host "Add logic here (e.g. dotnet ef database drop / dotnet ef database update) once EF Core is configured." -ForegroundColor Gray
