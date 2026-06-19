# Local INI settings for Manage.ps1 (scripts/.runtime/script-settings.ini).
Set-StrictMode -Version Latest

function Get-ScriptSettingsPath {
    return (Join-Path (Get-AppertureRuntimeRoot) 'script-settings.ini')
}

function Read-ScriptSettingsIni {
    $path = Get-ScriptSettingsPath
    $sections = @{}
    if (-not (Test-Path -LiteralPath $path)) {
        return $sections
    }

    $currentSection = 'defaults'
    foreach ($line in Get-Content -LiteralPath $path) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#') -or $trimmed.StartsWith(';')) {
            continue
        }
        if ($trimmed -match '^\[(.+)\]$') {
            $currentSection = $matches[1].ToLowerInvariant()
            if (-not $sections.ContainsKey($currentSection)) {
                $sections[$currentSection] = @{}
            }
            continue
        }
        if ($trimmed -match '^([^=]+)=(.*)$') {
            $key = $matches[1].Trim().ToLowerInvariant()
            $value = $matches[2].Trim()
            if (-not $sections.ContainsKey($currentSection)) {
                $sections[$currentSection] = @{}
            }
            $sections[$currentSection][$key] = $value
        }
    }
    return $sections
}

function Write-ScriptSettingsIni {
    param(
        [Parameter(Mandatory)]
        [hashtable]$Sections
    )

    $path = Get-ScriptSettingsPath
    $lines = New-Object System.Collections.Generic.List[string]
    $orderedSections = @('defaults', 'dev', 'deployment') + ($Sections.Keys | Where-Object { $_ -notin @('defaults', 'dev', 'deployment') } | Sort-Object)

    $seen = @{}
    foreach ($sectionName in $orderedSections) {
        if (-not $Sections.ContainsKey($sectionName)) { continue }
        if ($seen.ContainsKey($sectionName)) { continue }
        $seen[$sectionName] = $true

        $lines.Add("[$sectionName]")
        $keys = $Sections[$sectionName].Keys | Sort-Object
        foreach ($key in $keys) {
            $value = $Sections[$sectionName][$key]
            if ($null -ne $value -and $value -ne '') {
                $lines.Add("$key=$value")
            }
        }
        $lines.Add('')
    }

    $content = ($lines -join [Environment]::NewLine).TrimEnd()
    if ($content) { $content += [Environment]::NewLine }
    [System.IO.File]::WriteAllText($path, $content)
}

function Get-ScriptSetting {
    param(
        [Parameter(Mandatory)][string]$Key,
        [string]$Area = '',
        [string]$Default = ''
    )

    $sections = Read-ScriptSettingsIni
    $keyLower = $Key.ToLowerInvariant()

    if ($Area) {
        $areaLower = $Area.ToLowerInvariant()
        if ($sections.ContainsKey($areaLower) -and $sections[$areaLower].ContainsKey($keyLower)) {
            return $sections[$areaLower][$keyLower]
        }
    }

    if ($sections.ContainsKey('defaults') -and $sections['defaults'].ContainsKey($keyLower)) {
        return $sections['defaults'][$keyLower]
    }

    return $Default
}

function Set-ScriptSetting {
    param(
        [Parameter(Mandatory)][string]$Key,
        [Parameter(Mandatory)][string]$Value,
        [string]$Section = 'defaults'
    )

    $sections = Read-ScriptSettingsIni
    $sectionLower = $Section.ToLowerInvariant()
    $keyLower = $Key.ToLowerInvariant()

    if (-not $sections.ContainsKey($sectionLower)) {
        $sections[$sectionLower] = @{}
    }
    $sections[$sectionLower][$keyLower] = $Value
    Write-ScriptSettingsIni -Sections $sections
}

function Get-BuiltInManageDefault {
    param(
        [Parameter(Mandatory)][string]$Name,
        [string]$Area = '',
        [string]$Command = ''
    )

    switch ($Name) {
        'Mode' { return 'native' }
        'Service' { return 'all' }
        'Configuration' {
            if ($Area -eq 'deployment' -or $Command -in @('publish', 'deploy')) {
                return 'Release'
            }
            return 'Debug'
        }
        default { return '' }
    }
}

function Resolve-ManageParameterValue {
    param(
        [Parameter(Mandatory)][string]$Name,
        [string]$CliValue = '',
        [string]$Area = '',
        [string]$Command = ''
    )

    if (-not [string]::IsNullOrWhiteSpace($CliValue)) {
        return $CliValue
    }

    $iniKey = switch ($Name) {
        'Area' { 'area' }
        'Mode' { 'mode' }
        'Target' { 'target' }
        'Service' { 'service' }
        'Configuration' { 'configuration' }
        default { $Name.ToLowerInvariant() }
    }

    if ($Area) {
        $areaValue = Get-ScriptSetting -Key $iniKey -Area $Area
        if (-not [string]::IsNullOrWhiteSpace($areaValue)) {
            return $areaValue
        }
        if ($Name -eq 'Service') {
            $lastService = Get-ScriptSetting -Key 'lastservice' -Area $Area
            if (-not [string]::IsNullOrWhiteSpace($lastService)) {
                return $lastService
            }
        }
    }

    $defaultValue = Get-ScriptSetting -Key $iniKey -Area ''
    if (-not [string]::IsNullOrWhiteSpace($defaultValue)) {
        return $defaultValue
    }

    return (Get-BuiltInManageDefault -Name $Name -Area $Area -Command $Command)
}

function Get-ResolvedManageParameters {
    param(
        [string]$Area = '',
        [string]$Command = '',
        [string]$Mode = '',
        [string]$Service = '',
        [string]$Target = '',
        [string]$Configuration = ''
    )

    $resolvedArea = Resolve-ManageParameterValue -Name 'Area' -CliValue $Area -Area $Area -Command $Command
    $resolvedMode = Resolve-ManageParameterValue -Name 'Mode' -CliValue $Mode -Area $(if ($resolvedArea) { $resolvedArea } else { 'dev' }) -Command $Command
    $resolvedService = Resolve-ManageParameterValue -Name 'Service' -CliValue $Service -Area $(if ($resolvedArea) { $resolvedArea } else { 'dev' }) -Command $Command
    $resolvedTarget = Resolve-ManageParameterValue -Name 'Target' -CliValue $Target -Area $(if ($resolvedArea) { $resolvedArea } else { 'deployment' }) -Command $Command
    $resolvedConfiguration = Resolve-ManageParameterValue -Name 'Configuration' -CliValue $Configuration -Area $resolvedArea -Command $Command

    return [pscustomobject]@{
        Area          = $resolvedArea
        Command       = $Command
        Mode          = $resolvedMode
        Service       = $resolvedService
        Target        = $resolvedTarget
        Configuration = $resolvedConfiguration
    }
}

function Save-ManageParametersToSettings {
    param(
        [Parameter(Mandatory)]
        $Resolved
    )

    if ($Resolved.Area) {
        Set-ScriptSetting -Key 'area' -Value $Resolved.Area -Section 'defaults'
    }

    if ($Resolved.Area -eq 'dev') {
        if ($Resolved.Mode) {
            Set-ScriptSetting -Key 'mode' -Value $Resolved.Mode -Section 'dev'
            Set-ScriptSetting -Key 'mode' -Value $Resolved.Mode -Section 'defaults'
        }
        if ($Resolved.Service -and $Resolved.Service -ne 'all') {
            Set-ScriptSetting -Key 'service' -Value $Resolved.Service -Section 'defaults'
            Set-ScriptSetting -Key 'lastservice' -Value $Resolved.Service -Section 'dev'
        }
    }

    if ($Resolved.Area -eq 'deployment') {
        if ($Resolved.Target) {
            Set-ScriptSetting -Key 'target' -Value $Resolved.Target -Section 'deployment'
            Set-ScriptSetting -Key 'target' -Value $Resolved.Target -Section 'defaults'
        }
        if ($Resolved.Configuration) {
            Set-ScriptSetting -Key 'configuration' -Value $Resolved.Configuration -Section 'deployment'
        }
    }
}

function Show-ScriptSettings {
    $path = Get-ScriptSettingsPath
    Write-Host 'Saved script defaults' -ForegroundColor Cyan
    Write-Host "  File: $path" -ForegroundColor DarkGray
    Write-Host ''

    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host '  (no saved settings — using built-in defaults)' -ForegroundColor Yellow
        Write-Host '  Built-in Mode: native' -ForegroundColor DarkGray
        Write-Host '  Built-in Service: all' -ForegroundColor DarkGray
        return
    }

    Get-Content -LiteralPath $path | ForEach-Object { Write-Host "  $_" }
    Write-Host ''
    Write-Host 'Built-in fallbacks when a key is missing:' -ForegroundColor DarkGray
    Write-Host '  Mode=native, Service=all, Configuration=Debug (dev) / Release (deployment publish/deploy)'
}

function Clear-ScriptSettings {
    param([switch]$Force)

    $path = Get-ScriptSettingsPath
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host 'No saved settings to clear.' -ForegroundColor Yellow
        return
    }

    if (-not $Force) {
        $response = Read-Host "Delete saved settings at $path? [y/N]"
        if ($response -notmatch '^[yY]') {
            Write-Host 'Cancelled.' -ForegroundColor Yellow
            return
        }
    }

    Remove-Item -LiteralPath $path -Force
    Write-Host 'Saved settings cleared.' -ForegroundColor Green
}
