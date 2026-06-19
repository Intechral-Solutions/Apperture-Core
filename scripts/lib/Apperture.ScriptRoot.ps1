# Shared repo/script path resolution for Apperture PowerShell scripts.
Set-StrictMode -Version Latest

function Get-AppertureRepoRoot {
    if ($env:APPERTURE_REPO_ROOT) {
        return (Resolve-Path -LiteralPath $env:APPERTURE_REPO_ROOT).Path
    }

    $scriptsRoot = Get-AppertureScriptsRoot
    return (Resolve-Path -LiteralPath (Join-Path $scriptsRoot '..')).Path
}

function Get-AppertureScriptsRoot {
    if (Get-Variable -Name AppertureScriptsRoot -Scope Script -ErrorAction SilentlyContinue) {
        if ($script:AppertureScriptsRoot) {
            return $script:AppertureScriptsRoot
        }
    }

    # Prefer the directory containing this file (scripts/lib).
    $libDir = $PSScriptRoot
    if ($libDir -and (Split-Path -Leaf $libDir) -eq 'lib') {
        $script:AppertureScriptsRoot = (Resolve-Path -LiteralPath (Join-Path $libDir '..')).Path
        return $script:AppertureScriptsRoot
    }

    throw 'Could not resolve Apperture scripts root. Set APPERTURE_REPO_ROOT or dot-source from scripts/lib/.'
}

function Get-AppertureRuntimeRoot {
    $root = Join-Path (Get-AppertureScriptsRoot) '.runtime'
    if (-not (Test-Path -LiteralPath $root)) {
        New-Item -ItemType Directory -Path $root -Force | Out-Null
    }
    return $root
}

function Get-AppertureDevRuntimeRoot {
    $root = Join-Path (Get-AppertureRuntimeRoot) 'dev'
    if (-not (Test-Path -LiteralPath $root)) {
        New-Item -ItemType Directory -Path $root -Force | Out-Null
    }
    return $root
}

function Get-AppertureDevLogDir {
    $dir = Join-Path (Get-AppertureRepoRoot) 'artifacts\devlogs'
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    return $dir
}

function Import-AppertureScriptLibrary {
    param([Parameter(Mandatory)][string[]]$Names)

    $libDir = Join-Path (Get-AppertureScriptsRoot) 'lib'
    foreach ($name in $Names) {
        $path = Join-Path $libDir $name
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Script library not found: $path"
        }
        . $path
    }
}
