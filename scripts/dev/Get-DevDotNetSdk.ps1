# Resolves dotnet.exe + install root for repo global.json (used by up.ps1 and ci/test.ps1).
param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot
)

$ErrorActionPreference = "Stop"

$globalJsonPath = Join-Path $RepoRoot "global.json"
if (-not (Test-Path -LiteralPath $globalJsonPath)) {
    throw "global.json not found: $globalJsonPath"
}

$gj = Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json
$requiredVersion = $gj.sdk.version
$rollForward = $gj.sdk.rollForward

function Test-DotNetHasMajor {
    param(
        [string]$DotNetExe,
        [int]$MajorVersion
    )
    if (-not (Test-Path -LiteralPath $DotNetExe)) { return $false }
    $list = & $DotNetExe --list-sdks 2>$null
    if (-not $list) { return $false }
    foreach ($line in $list) {
        if ($line -match '^(\d+)\.') {
            if ([int]$matches[1] -eq $MajorVersion) { return $true }
        }
    }
    return $false
}

function ReadSdkInfoFromDotNetPath {
    param([string]$DotNetExePath)
    if (-not (Test-Path -LiteralPath $DotNetExePath)) { return $null }
    $requiredMajor = 10
    if ($requiredVersion -match '^(\d+)') {
        $requiredMajor = [int]$matches[1]
    }
    if (-not (Test-DotNetHasMajor -DotNetExe $DotNetExePath -MajorVersion $requiredMajor)) {
        return $null
    }
    # Avoid Split-Path -LiteralPath -Parent (ambiguous parameter sets on some Windows PowerShell builds).
    $root = [System.IO.Path]::GetDirectoryName($DotNetExePath)
    $verOut = (& $DotNetExePath --version 2>$null | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($verOut)) { $verOut = "unknown" }
    return [pscustomobject]@{
        DotNetRoot = $root
        DotNetExe  = $DotNetExePath
        Version    = $verOut.Trim()
    }
}

$candidates = @(
    (Join-Path $env:USERPROFILE "dotnet\dotnet.exe"),
    (Join-Path ([Environment]::GetFolderPath('ProgramFiles')) "dotnet\dotnet.exe")
)
$pf86 = [Environment]::GetFolderPath('ProgramFilesX86')
if (-not [string]::IsNullOrWhiteSpace($pf86)) {
    $candidates += (Join-Path $pf86 "dotnet\dotnet.exe")
}

foreach ($candidate in $candidates) {
    $resolved = ReadSdkInfoFromDotNetPath -DotNetExePath $candidate
    if ($null -ne $resolved) {
        return $resolved
    }
}

$major = if ($requiredVersion -match '^(\d+)') { $matches[1] } else { "10" }
$msg = @"
No .NET $major SDK found for this repo. global.json requires SDK $requiredVersion (rollForward: $rollForward).

Install the matching SDK from https://dotnet.microsoft.com/download, or use a user-local install:

  Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile `$env:TEMP\dotnet-install.ps1
  & `$env:TEMP\dotnet-install.ps1 -Channel 10.0 -Quality ga -InstallDir `$env:USERPROFILE\dotnet

Then re-run Start-Dev.ps1 (or add $env:USERPROFILE\dotnet to PATH).
"@
throw $msg
