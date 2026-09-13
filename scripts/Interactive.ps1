<#
.SYNOPSIS
    Interactive menu for Apperture script commands (delegates to Manage.ps1).
.EXAMPLE
    ./scripts/Interactive.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptsRoot = $PSScriptRoot
$ManageScript = Join-Path $ScriptsRoot 'Manage.ps1'

. (Join-Path $ScriptsRoot 'lib\Apperture.ScriptRoot.ps1')
. (Join-Path $ScriptsRoot 'lib\Apperture.ScriptSettings.ps1')

function Read-MenuChoice {
    param(
        [Parameter(Mandatory)][string]$Prompt,
        [Parameter(Mandatory)][hashtable]$Options,
        [string]$DefaultKey = ''
    )

    Write-Host $Prompt -ForegroundColor Cyan
    foreach ($key in ($Options.Keys | Sort-Object)) {
        $label = $Options[$key]
        $mark = if ($key -eq $DefaultKey) { ' (default)' } else { '' }
        Write-Host "  [$key] $label$mark"
    }

    $input = Read-Host 'Choice'
    if ([string]::IsNullOrWhiteSpace($input) -and $DefaultKey) {
        return $DefaultKey
    }
    if ($Options.ContainsKey($input)) {
        return $input
    }
    throw "Invalid choice: $input"
}

function Invoke-ManageFromMenu {
    param([hashtable]$Params)

    $parts = @('./scripts/Manage.ps1')
    foreach ($key in $Params.Keys) {
        $value = $Params[$key]
        if ($null -eq $value) { continue }
        if ($value -is [switch] -and $value) {
            $parts += "-$key"
        }
        elseif ($value -is [string] -and $value -ne '') {
            $parts += "-$key"
            $parts += $value
        }
    }

    $display = $parts -join ' '
    Write-Host ''
    Write-Host "Running: $display" -ForegroundColor DarkGray
    Write-Host ''

    & $ManageScript @Params
}

Write-Host 'Apperture interactive script manager' -ForegroundColor Cyan
Write-Host ''

$savedArea = Get-ScriptSetting -Key 'area' -Default 'dev'
$areaChoice = Read-MenuChoice -Prompt 'Select area:' -Options @{
    '1' = 'Development (dev)'
    '2' = 'Deployment'
} -DefaultKey $(if ($savedArea -eq 'deployment') { '2' } else { '1' })

$area = if ($areaChoice -eq '2') { 'deployment' } else { 'dev' }

if ($area -eq 'dev') {
    $savedMode = Get-ScriptSetting -Key 'mode' -Area 'dev' -Default (Get-BuiltInManageDefault -Name 'Mode')
    $commandChoice = Read-MenuChoice -Prompt 'Select dev command:' -Options @{
        '1'  = 'Start environment'
        '2'  = 'Stop environment'
        '3'  = 'Restart environment'
        '4'  = 'Restart service'
        '5'  = 'Show status'
        '6'  = 'Restore .NET projects'
        '7'  = 'Build .NET projects'
        '8'  = 'Rebuild .NET projects'
        '9'  = 'Clean .NET projects'
        '10' = 'Build UI assets'
        '11' = 'Watch UI assets'
        '12' = 'Show saved defaults'
        '13' = 'Reset saved defaults'
    } -DefaultKey '1'

    $commandMap = @{
        '1' = 'start'; '2' = 'stop'; '3' = 'restart'; '4' = 'restart-service'
        '5' = 'status'; '6' = 'restore'; '7' = 'build'; '8' = 'rebuild'
        '9' = 'clean'; '10' = 'build-ui'; '11' = 'watch-ui'
        '12' = 'defaults'; '13' = 'reset-defaults'
    }
    $command = $commandMap[$commandChoice]

    $params = @{ Area = $area; Command = $command }

    if ($command -in @('start', 'stop', 'restart', 'restart-service')) {
        $modeChoice = Read-MenuChoice -Prompt 'Select dev mode:' -Options @{
            '1' = 'Native (docker-compose SQL + dotnet)'
            '2' = 'Aspire (AppHost)'
        } -DefaultKey $(if ($savedMode -eq 'aspire') { '2' } else { '1' })
        $params['Mode'] = if ($modeChoice -eq '2') { 'aspire' } else { 'native' }
    }

    if ($command -eq 'restart-service') {
        $savedService = Get-ScriptSetting -Key 'lastservice' -Area 'dev' -Default 'api'
        $serviceChoice = Read-MenuChoice -Prompt 'Select service:' -Options @{
            '1' = 'API (Management.Host)'
            '2' = 'UI (Management.Ui.Server)'
            '3' = 'Worker'
            '4' = 'Database (Docker sqlserver)'
            '5' = 'All services'
        } -DefaultKey $(switch ($savedService) {
                'ui' { '2' } 'worker' { '3' } 'db' { '4' } 'all' { '5' } default { '1' }
            })
        $params['Service'] = switch ($serviceChoice) {
            '2' { 'ui' } '3' { 'worker' } '4' { 'db' } '5' { 'all' } default { 'api' }
        }
    }

    if ($command -in @('start', 'restart', 'restart-service')) {
        $buildAnswer = Read-Host 'Build before start/restart? [y/N]'
        if ($buildAnswer -match '^[yY]') { $params['Build'] = $true }

        if ($command -in @('start', 'restart-service') -and $params['Mode'] -ne 'aspire' -and ($params['Service'] -in @('ui', 'all', $null) -or $command -eq 'start')) {
            $browserAnswer = Read-Host 'Open browser for UI? [Y/n]'
            if ($browserAnswer -match '^[nN]') { $params['NoBrowser'] = $true }
        }
    }

    Invoke-ManageFromMenu -Params $params
}
else {
    $savedTarget = Get-ScriptSetting -Key 'target' -Area 'deployment'
    $commandChoice = Read-MenuChoice -Prompt 'Select deployment command:' -Options @{
        '1' = 'Show status'
        '2' = 'Restore .NET projects'
        '3' = 'Build .NET projects'
        '4' = 'Publish .NET projects'
        '5' = 'Deploy environment'
        '6' = 'Build UI assets'
        '7' = 'Show saved defaults'
        '8' = 'Reset saved defaults'
    } -DefaultKey '1'

    $commandMap = @{
        '1' = 'status'; '2' = 'restore'; '3' = 'build'; '4' = 'publish'
        '5' = 'deploy'; '6' = 'build-ui'; '7' = 'defaults'; '8' = 'reset-defaults'
    }
    $command = $commandMap[$commandChoice]
    $params = @{ Area = $area; Command = $command }

    if ($command -eq 'deploy') {
        $targetChoice = Read-MenuChoice -Prompt 'Select deploy target:' -Options @{
            '1' = 'Windows Service'
            '2' = 'Azure'
            '3' = 'Docker'
            '4' = 'Local'
        } -DefaultKey $(switch ($savedTarget) {
                'azure' { '2' } 'docker' { '3' } 'local' { '4' } default { '1' }
            })
        $params['Target'] = switch ($targetChoice) {
            '2' { 'azure' } '3' { 'docker' } '4' { 'local' } default { 'windows-service' }
        }
    }

    Invoke-ManageFromMenu -Params $params
}
