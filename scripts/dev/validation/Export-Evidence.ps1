#Requires -Version 7.2
<#
.SYNOPSIS
    Copies a finished evidence folder to a dated, verified archive on another local drive. Uploads nothing.
.DESCRIPTION
    The source must verify against its own SHA256SUMS.txt first. The copy goes to <DestinationRoot>/<yyyy-MM-dd>/<Name> (never an
    existing folder, never inside the repository or the source), is re-hashed from the destination, must hold exactly the same files with
    the same hashes, and receives ARCHIVE-VERIFIED.txt. With -Restrict (Windows) inheritance is removed and only the current user may read
    the copy.

    WHAT THIS IS NOT. A second drive in the same machine protects against the failure of one drive. It does not protect against theft, fire,
    ransomware, a stolen laptop or an account compromise, so it is NOT an off-machine backup. Evidence whose loss would matter (accepted
    security packages, review records) also needs an independent off-machine copy; that is a separate, owner-run step with its own access
    control, and nothing in this toolkit sends evidence to a network location. ARCHIVE-VERIFIED.txt says so, so that a later reader does not
    mistake the copy for more than it is.

    Exit codes: 0 = copied and verified; 1 = copied but verification failed (the copy is left in place for inspection); 2 = refused.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$DestinationRoot,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,80}$')][string]$Name,
    [string]$Repo,
    [switch]$Restrict,
    [switch]$PassThru
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ValidationCommon.ps1')

function Stop-Refused([string]$Message) { Write-Host "REFUSED: $Message" -ForegroundColor Red; exit 2 }
try {
    if (-not $Repo) { $Repo = (Invoke-Git $PSScriptRoot @('rev-parse', '--show-toplevel')).Text }
    $src = [IO.Path]::GetFullPath($Source).TrimEnd('\', '/'); $root = [IO.Path]::GetFullPath($DestinationRoot).TrimEnd('\', '/')
    if (-not (Test-Path -LiteralPath $src -PathType Container)) { Stop-Refused "source folder not found: $src" }
    $v = Test-ChecksumFile $src
    if (-not $v.Ok) { Stop-Refused "the source does not verify against its SHA256SUMS.txt (checked $($v.Checked); bad $($v.Bad -join ', '); missing $($v.Missing -join ', '); unlisted $($v.Extra -join ', '); $($v.Errors -join '; '))" }
    $dest = Join-Path (Join-Path $root (Get-Date -Format 'yyyy-MM-dd')) $Name
    foreach ($p in @($Repo, $src)) { if ((Test-PathUnder $dest $p) -or (Test-PathUnder $p $dest)) { Stop-Refused "the destination '$dest' overlaps '$p'" } }
    if (Test-Path -LiteralPath $dest) { Stop-Refused "destination exists: $dest (an archive is never overwritten)" }
    if (Test-HasReparsePoint $dest ([IO.Path]::GetPathRoot($dest))) { Stop-Refused 'the destination passes through a link' }
    $sameVolume = [IO.Path]::GetPathRoot($src) -eq [IO.Path]::GetPathRoot($dest)

    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item -Path (Join-Path $src '*') -Destination $dest -Recurse -Force
    $restricted = $false
    if ($Restrict -and $IsWindows) {
        & icacls $dest /inheritance:r /grant:r "${env:USERNAME}:(OI)(CI)F" | Out-Null
        $restricted = ($LASTEXITCODE -eq 0)
        if (-not $restricted) { Write-Host 'WARNING: could not restrict the archive folder' -ForegroundColor Yellow }
    }
    elseif ($Restrict) { Write-Host 'WARNING: -Restrict is implemented for Windows only; the archive folder keeps its inherited access' -ForegroundColor Yellow }
    $check = Test-ChecksumFile $dest
    $prov = Join-Path $src 'provenance.json'
    $commit = if (Test-Path -LiteralPath $prov) { try { (Get-Content -LiteralPath $prov -Raw | ConvertFrom-Json).Tree.Head } catch { 'unreadable' } } else { 'no provenance.json' }
    $verdict = if ($check.Ok -and $check.Checked -eq $v.Checked) { 'VERIFIED' } else { 'FAILED' }
    $note = @(
        "ARCHIVE $verdict $((Get-Date).ToUniversalTime().ToString('o'))",
        "name: $Name", "source commit: $commit", "files checked in the copy: $($check.Checked) of $($v.Checked) in the source",
        "bad: $($check.Bad -join ', ')", "missing: $($check.Missing -join ', ')", "unlisted: $($check.Extra -join ', ')",
        "restricted to the current user: $restricted",
        "same volume as the source: $sameVolume$(if ($sameVolume) { ' (this copy does not even survive the loss of that drive)' })",
        'This is a copy on a local drive. It is NOT an off-machine backup: it does not survive theft, fire, ransomware or loss of this machine.',
        'Independent off-machine archival of evidence that must survive those is a separate step, run by its owner with its own access control.'
    )
    Write-TextLf (Join-Path $dest 'ARCHIVE-VERIFIED.txt') (($note -join "`n") + "`n")
    Write-Host "Archive $verdict at $dest ($($check.Checked) files)"
    if ($sameVolume) { Write-Host 'WARNING: the destination is on the same volume as the source' -ForegroundColor Yellow }
    Write-Host 'Reminder: this is not an off-machine backup (see ARCHIVE-VERIFIED.txt).'
    if ($PassThru) { [pscustomobject]@{ Destination = $dest; Verified = ($verdict -eq 'VERIFIED'); Files = $check.Checked } }
    exit $(if ($verdict -eq 'VERIFIED') { 0 } else { 1 })
}
catch { Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red; exit 2 }
