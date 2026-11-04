#Requires -Version 7.2
<#
.SYNOPSIS
    Reproduces the three historical validation controls of the DEV-1.3A acceptance from preserved evidence. Read-only.
.DESCRIPTION
    1. WP4B-B mutation evidence: Invoke-Mutation -ReplaySummary over the preserved summary must classify 16 mutations as 16 caught, 0 escaped,
       with every per-mutation failing-test count equal to the WP4B-B record's table (wp4b-b-mutations.expected.json).
    2. WP4B-D versus WP4B-B TRX: Compare-Trx over the two focused-integration TRX files must report 36 added tests (18 per isolation mode, none
       without a mode), the two ManagementHostStartupOrderSqlServerTests failures, and counts 1,361 -> 1,397 (rcsi 556/556 -> 574/574, 249 without).
    3. WP4B-A test selection: Select-Tests over the WP4B-A diff (5a77e24..b10a657), reading Git objects at b10a657, must name the six classes the
       original focused filter lacked. The selector may name more; any extra is listed and not hidden.
    The inputs are the git-ignored evidence folders of the primary checkout (-ArtifactsRoot) and Git objects of the IAM branch. Nothing is run
    from, or written to, the IAM checkout or branch. If an input is missing the control is reported UNAVAILABLE and the exit code is 3; no
    expected number is relaxed or invented.
    Exit codes: 0 = all three reproduced; 1 = a control did not reproduce; 3 = an input was unavailable.
.PARAMETER ArtifactsRoot   The artifacts folder holding wp4b-b-logs, wp4b-d-logs and wp4ba-logs (default: ../Apperture-Core/artifacts next to the repository).
.PARAMETER Out             Folder for the three result files (default: a temporary folder under artifacts/).
#>
[CmdletBinding()]
param(
    [string]$ArtifactsRoot,
    [string]$Repo,
    [string]$Out
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$val = Split-Path $PSScriptRoot -Parent
. (Join-Path $val 'lib/ValidationCommon.ps1')
if (-not $Repo) { $Repo = (Invoke-Git $PSScriptRoot @('rev-parse', '--show-toplevel')).Text }
if (-not $ArtifactsRoot) { $ArtifactsRoot = Join-Path (Split-Path $Repo -Parent) 'Apperture-Core/artifacts' }
if (-not $Out) { $Out = Join-Path $Repo ("artifacts/acceptance-controls/{0}" -f (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')) }
New-Item -ItemType Directory -Force $Out | Out-Null
$pwshExe = (Get-Process -Id $PID).Path
$results = New-Object System.Collections.Generic.List[object]
function Add-Result([string]$Name, [string]$Status, [string]$Detail) { $results.Add([pscustomobject]@{ Control = $Name; Status = $Status; Detail = $Detail }); Write-Host ("{0,-12} {1}: {2}" -f $Status, $Name, $Detail) }
function Get-MissingFile([string[]]$Paths) { $missing = @($Paths | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }); $missing }

# ---- 1. WP4B-B mutation evidence
$summary = Join-Path $ArtifactsRoot 'wp4b-b-logs/mutations-summary.txt'
$missing = Get-MissingFile @($summary)
if ($missing) { Add-Result '1 mutation' 'UNAVAILABLE' "missing: $($missing -join ', ')" }
else {
    $o = & $pwshExe -NoProfile -File (Join-Path $val 'Invoke-Mutation.ps1') -ReplaySummary $summary -ExpectedPerMutation (Join-Path $PSScriptRoot 'wp4b-b-mutations.expected.json') -ExpectCaught 16 -ExpectEscaped 0 -ExpectCount 16 -Out (Join-Path $Out 'mutation') 2>&1
    $code = $LASTEXITCODE
    Add-Result '1 mutation' $(if ($code -eq 0) { 'REPRODUCED' } else { 'NOT REPRODUCED' }) ($o | Select-Object -First 1)
}

# ---- 2. WP4B-D vs WP4B-B TRX
$b = Join-Path $ArtifactsRoot 'wp4b-d-logs/results/IntegrationFocused.trx'; $c = Join-Path $ArtifactsRoot 'wp4b-b-logs/results/IntegrationFocused.trx'
$missing = Get-MissingFile @($b, $c)
if ($missing) { Add-Result '2 trx' 'UNAVAILABLE' "missing: $($missing -join ', ')" }
else {
    $json = Join-Path $Out 'trx-compare.json'
    $o = & $pwshExe -NoProfile -File (Join-Path $val 'Compare-Trx.ps1') -Baseline $b -Candidate $c -ExpectAdded 36 -ExpectRcsiAdded 'True=18,False=18' -ExpectFailed 'ManagementHostStartupOrderSqlServerTests' -Json $json -Quiet 2>&1
    $code = $LASTEXITCODE; $j = Get-Content $json -Raw | ConvertFrom-Json
    $counts = ($j.BaselineCounts.Total -eq 1361 -and $j.CandidateCounts.Total -eq 1397 -and $j.BaselineRcsi.False -eq 556 -and $j.BaselineRcsi.True -eq 556 -and $j.BaselineRcsi.none -eq 249 -and $j.CandidateRcsi.False -eq 574 -and $j.CandidateRcsi.True -eq 574 -and $j.CandidateRcsi.none -eq 249 -and $j.CandidateFailed.Count -eq 2 -and $j.Removed.Count -eq 0)
    Add-Result '2 trx' $(if ($code -eq 0 -and $counts) { 'REPRODUCED' } else { 'NOT REPRODUCED' }) "added $($j.Added.Count), failed $($j.CandidateFailed.Count), totals $($j.BaselineCounts.Total) -> $($j.CandidateCounts.Total), rcsi False/True/none $($j.BaselineRcsi.False)/$($j.BaselineRcsi.True)/$($j.BaselineRcsi.none) -> $($j.CandidateRcsi.False)/$($j.CandidateRcsi.True)/$($j.CandidateRcsi.none), removed $($j.Removed.Count)"
}

# ---- 3. WP4B-A test selection
$filter = Join-Path $ArtifactsRoot 'wp4ba-logs/final-int-filter.txt'
$missing = Get-MissingFile @($filter)
$haveObjects = ((Invoke-Git $Repo @('cat-file', '-t', 'b10a657')).Exit -eq 0) -and ((Invoke-Git $Repo @('cat-file', '-t', '5a77e24')).Exit -eq 0)
if ($missing -or -not $haveObjects) { Add-Result '3 selection' 'UNAVAILABLE' "$(if ($missing) { "missing: $($missing -join ', ')" })$(if (-not $haveObjects) { ' Git objects 5a77e24/b10a657 are not in this repository' })" }
else {
    $json = Join-Path $Out 'selection.json'
    $o = & $pwshExe -NoProfile -File (Join-Path $val 'Select-Tests.ps1') -Repo $Repo -Base 5a77e24 -Head b10a657 -TreeRev b10a657 -CurrentFilter $filter -CurrentFilterProject IntegrationTests -Json $json -Quiet 2>&1
    $j = Get-Content $json -Raw | ConvertFrom-Json
    $six = 'AuthenticationAuditSqlServerTests', 'ManagementHostAccountHttpTests', 'ManagementHostAccountProfileHttpTests', 'ManagementHostBootstrapHttpTests', 'ManagementHostBreakGlassHttpTests', 'UsernameFoundationSqlServerTests'
    $found = @($six | Where-Object { $_ -in $j.MissedByCurrentFilter }); $extra = @($j.MissedByCurrentFilter | Where-Object { $_ -notin $six })
    Add-Result '3 selection' $(if ($found.Count -eq 6) { 'REPRODUCED' } else { 'NOT REPRODUCED' }) "$($found.Count) of the 6 classes the review added are named as missed by the original $(@($j.CurrentFilterClasses).Count)-class filter; also named (not in the record): $(if ($extra) { $extra -join ', ' } else { 'none' }); unmapped production files: $(@($j.Unmapped).Count)"
}

$results | ConvertTo-Json | ForEach-Object { Write-TextLf (Join-Path $Out 'acceptance-controls.json') $_ }
if (@($results | Where-Object Status -eq 'NOT REPRODUCED').Count) { exit 1 }
if (@($results | Where-Object Status -eq 'UNAVAILABLE').Count) { exit 3 }
exit 0
