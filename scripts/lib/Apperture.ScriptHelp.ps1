# Help and usage output for Manage.ps1.
Set-StrictMode -Version Latest

function Show-ManageHelp {
    param([string]$ErrorMessage = '')

    if ($ErrorMessage) {
        Write-Host $ErrorMessage -ForegroundColor Red
        Write-Host ''
    }

    Write-Host 'Apperture script manager' -ForegroundColor Cyan
    Write-Host ''
    Write-Host 'Usage:' -ForegroundColor Yellow
    Write-Host '  ./scripts/Manage.ps1 -Area dev|deployment -Command <command> [options]'
    Write-Host '  ./scripts/Manage.ps1 -Command defaults|reset-defaults'
    Write-Host ''
    Write-Host 'Parameter resolution (Area, Mode, Target, Service, Configuration):' -ForegroundColor Yellow
    Write-Host '  1. Explicit command-line value'
    Write-Host '  2. Saved value in scripts/.runtime/script-settings.ini'
    Write-Host '  3. Built-in default (Mode defaults to native when unset)'
    Write-Host ''
    Write-Host 'Dev commands:' -ForegroundColor Yellow
    Write-Host '  start, stop, restart, restart-service, status'
    Write-Host '  restore, build, rebuild, clean, build-ui, watch-ui'
    Write-Host ''
    Write-Host 'Deployment commands (scaffolded):' -ForegroundColor Yellow
    Write-Host '  status, restore, build, publish, deploy, build-ui'
    Write-Host ''
    Write-Host 'Settings:' -ForegroundColor Yellow
    Write-Host '  defaults, reset-defaults'
    Write-Host ''
    Write-Host 'Examples:' -ForegroundColor Yellow
    Write-Host '  ./scripts/Manage.ps1 -Area dev -Command start'
    Write-Host '  ./scripts/Manage.ps1 -Area dev -Command start -Mode aspire'
    Write-Host '  ./scripts/Manage.ps1 -Area dev -Command start -Build'
    Write-Host '  ./scripts/Manage.ps1 -Area dev -Command restart-service -Service api'
    Write-Host '  ./scripts/Manage.ps1 -Command restart'
    Write-Host '  ./scripts/Manage.ps1 -Command defaults'
    Write-Host '  ./scripts/Interactive.ps1'
    Write-Host ''
}

function Write-ManageValidationError {
    param(
        [Parameter(Mandatory)][string]$Message
    )
    Show-ManageHelp -ErrorMessage $Message
    exit 1
}
