#Requires -Version 7.2
<#
.SYNOPSIS
    Compares two sets of TRX files by test identity and outcome. Reads files only.
.DESCRIPTION
    Matches tests by TRX test id (stable across runs of the same assembly), then by exact display name for what is left, so a theory
    row whose data changed shows as removed + added, and rows that share a display name are not collapsed (a name-set comparison
    loses them). Reports totals, added / removed / outcome-changed tests with a per-method roll-up, skipped and not-executed tests,
    per-class counts, the isolation-mode (rcsi) split, the slowest tests, and SqlServerTests cases that passed implausibly fast
    (a soft-skip signature).

    An input that cannot be trusted is not compared: a file that is not a TRX, is truncated, has counters that disagree with its own
    results, holds no results, or is the same file on both sides is an input error (exit 2), never a clean comparison.

    Exit codes: 0 = compared, expectations (if any) met, nothing flagged under -Strict; 1 = findings under -Strict or an expectation
    was not met; 2 = input error.
.PARAMETER Baseline / Candidate  One or more TRX files (or folders holding .trx files; searched recursively). A side with several files is merged.
.PARAMETER ExpectAdded           The number of tests that must be new in the candidate.
.PARAMETER ExpectFailed          Substrings separated by '|'; the candidate's failed tests must be exactly the tests matching these (each substring at least one test).
.PARAMETER ExpectRcsiAdded       Added tests per isolation mode, e.g. 'True=18,False=18'. Modes not named must have none added.
.PARAMETER Strict                Flag removed tests, outcome changes away from Passed, new failures, skipped / not-executed tests and soft-skip suspects as findings.
.PARAMETER Json                  Write the full result as JSON to this path.
.PARAMETER PassThru              Return the result object.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$Baseline,
    [Parameter(Mandatory)][string[]]$Candidate,
    [Nullable[int]]$ExpectAdded,
    [string[]]$ExpectFailed,
    [string]$ExpectRcsiAdded,
    [switch]$Strict,
    [int]$Top = 10,
    [int]$SoftSkipMs = 100,
    [string]$Json,
    [switch]$PassThru,
    [switch]$Quiet
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ValidationCommon.ps1')
$Baseline = Expand-List $Baseline; $Candidate = Expand-List $Candidate; $ExpectFailed = Expand-List $ExpectFailed '|'

function Get-TrxSide([string[]]$Inputs, [string]$Label) {
    $files = foreach ($i in $Inputs) {
        if (Test-Path -LiteralPath $i -PathType Container) { Get-ChildItem -LiteralPath $i -Filter *.trx -Recurse -File | Sort-Object FullName | ForEach-Object FullName } else { $i }
    }
    $files = @($files); $errors = @(); $parsed = @()
    if ($files.Count -eq 0) { $errors += "${Label}: no TRX file given or found" }
    foreach ($f in $files) {
        $t = Read-Trx $f
        if (-not $t.Valid) { $errors += "${Label} $f : $($t.Errors -join '; ')" } else { $parsed += $t }
    }
    $all = @($parsed | ForEach-Object { $_.Results })
    $dupIds = @($all | Group-Object TestId | Where-Object Count -gt 1)
    if ($dupIds.Count -gt 0) { $errors += "${Label}: $($dupIds.Count) test ids occur more than once across the files (overlapping inputs)" }
    [pscustomobject]@{ Label = $Label; Files = $files; Parsed = $parsed; Results = $all; Errors = $errors }
}

function Get-ClassOf($r) { if ($r.Class) { $r.Class } else { ($r.Name -replace '\(.*$', '') -replace '\.[^.]+$', '' } }
function Get-MethodKey($r) { ($r.Name -replace '\(.*$', '') }
function Get-OutcomeCounts($results) {
    $h = [ordered]@{ Total = @($results).Count; Passed = 0; Failed = 0; NotExecuted = 0; Other = 0 }
    foreach ($r in $results) { switch ($r.Outcome) { 'Passed' { $h.Passed++ } 'Failed' { $h.Failed++ } 'NotExecuted' { $h.NotExecuted++ } default { $h.Other++ } } }
    [pscustomobject]$h
}
function Get-RcsiSplit($results) {
    $h = [ordered]@{ True = 0; False = 0; none = 0 }
    foreach ($r in $results) { $h[(Get-RcsiMode $r.Name)]++ }
    [pscustomobject]$h
}

$base = Get-TrxSide $Baseline 'baseline'; $cand = Get-TrxSide $Candidate 'candidate'
$errors = @($base.Errors) + @($cand.Errors)
if ($errors.Count -eq 0) {
    $bh = @($base.Parsed.Sha256) ; $ch = @($cand.Parsed.Sha256)
    if (@($bh | Where-Object { $_ -in $ch }).Count -gt 0) { $errors += 'baseline and candidate include the same file (identical content): nothing to compare' }
}
if ($errors.Count -gt 0) {
    if (-not $Quiet) { $errors | ForEach-Object { Write-Host "INPUT ERROR: $_" -ForegroundColor Red } }
    $res = [pscustomobject]@{ InputOk = $false; Errors = $errors; Findings = @('input error'); ExitCode = 2 }
    if ($Json) { Write-TextLf $Json ($res | ConvertTo-Json -Depth 6) }
    if ($PassThru) { $res }; exit 2
}

# --- match by id, then by exact name
$bById = @{}; foreach ($r in $base.Results) { $bById[$r.TestId] = $r }
$cById = @{}; foreach ($r in $cand.Results) { $cById[$r.TestId] = $r }
$pairs = New-Object System.Collections.Generic.List[object]
$bLeft = New-Object System.Collections.Generic.List[object]; $cLeft = New-Object System.Collections.Generic.List[object]
foreach ($r in $base.Results) { if ($cById.ContainsKey($r.TestId)) { $pairs.Add([pscustomobject]@{ B = $r; C = $cById[$r.TestId]; By = 'id' }) } else { $bLeft.Add($r) } }
foreach ($r in $cand.Results) { if (-not $bById.ContainsKey($r.TestId)) { $cLeft.Add($r) } }
$bByName = @{}; foreach ($r in ($bLeft | Sort-Object TestId)) { if (-not $bByName.ContainsKey($r.Name)) { $bByName[$r.Name] = New-Object System.Collections.Generic.Queue[object] }; $bByName[$r.Name].Enqueue($r) }
$added = New-Object System.Collections.Generic.List[object]; $matchedBase = New-Object System.Collections.Generic.HashSet[string]
foreach ($r in ($cLeft | Sort-Object TestId)) {
    if ($bByName.ContainsKey($r.Name) -and $bByName[$r.Name].Count -gt 0) { $b = $bByName[$r.Name].Dequeue(); [void]$matchedBase.Add($b.TestId); $pairs.Add([pscustomobject]@{ B = $b; C = $r; By = 'name' }) }
    else { $added.Add($r) }
}
$removed = @($bLeft | Where-Object { -not $matchedBase.Contains($_.TestId) })
$changed = @($pairs | Where-Object { $_.B.Outcome -ne $_.C.Outcome })
$matchedByName = @($pairs | Where-Object By -eq 'name').Count

# --- derived reports
$bCounts = Get-OutcomeCounts $base.Results; $cCounts = Get-OutcomeCounts $cand.Results
$bRc = Get-RcsiSplit $base.Results; $cRc = Get-RcsiSplit $cand.Results; $aRc = Get-RcsiSplit $added; $rRc = Get-RcsiSplit $removed
$candFailed = @($cand.Results | Where-Object Outcome -eq 'Failed')
$candNotRun = @($cand.Results | Where-Object { $_.Outcome -ne 'Passed' -and $_.Outcome -ne 'Failed' })
$baseFailedIds = [Collections.Generic.HashSet[string]]::new([string[]]@($base.Results | Where-Object Outcome -eq 'Failed' | ForEach-Object TestId))
$newFailures = @($candFailed | Where-Object { -not $baseFailedIds.Contains($_.TestId) })
# A passed SqlServerTests case that took under SoftSkipMs is only a count (many are pure-function cases). It becomes a suspect when the
# same test took SoftSkipMs or longer in the baseline: a test that used to talk to SQL Server and now returns at once.
$fastSql = @($cand.Results | Where-Object { $_.Outcome -eq 'Passed' -and (Get-ClassOf $_) -match 'SqlServer' -and (ConvertTo-Seconds $_.Duration) * 1000 -lt $SoftSkipMs })
$softSkip = @($fastSql | Where-Object { $bById.ContainsKey($_.TestId) -and (ConvertTo-Seconds $bById[$_.TestId].Duration) * 1000 -ge $SoftSkipMs })
$methodRollup = @(@($added | ForEach-Object { [pscustomobject]@{ Dir = 'added'; Method = Get-MethodKey $_ } }) + @($removed | ForEach-Object { [pscustomobject]@{ Dir = 'removed'; Method = Get-MethodKey $_ } }) |
        Group-Object Dir, Method | ForEach-Object { [pscustomobject]@{ Direction = $_.Group[0].Dir; Method = $_.Group[0].Method; Rows = $_.Count } } | Sort-Object Direction, Method)
$classes = @(@($base.Results | ForEach-Object { Get-ClassOf $_ }) + @($cand.Results | ForEach-Object { Get-ClassOf $_ }) | Sort-Object -Unique)
$bByClass = @($base.Results | Group-Object { Get-ClassOf $_ } -AsHashTable -AsString); $cByClass = @($cand.Results | Group-Object { Get-ClassOf $_ } -AsHashTable -AsString)
$bc = if ($bByClass.Count) { $bByClass[0] } else { @{} }; $cc = if ($cByClass.Count) { $cByClass[0] } else { @{} }
$classTable = foreach ($c in $classes) {
    $b = if ($bc.ContainsKey($c)) { @($bc[$c]).Count } else { 0 }; $n = if ($cc.ContainsKey($c)) { @($cc[$c]).Count } else { 0 }
    if ($b -ne $n) { [pscustomobject]@{ Class = $c; Baseline = $b; Candidate = $n; Delta = $n - $b } }
}
$slowest = @($cand.Results | Sort-Object { ConvertTo-Seconds $_.Duration } -Descending | Select-Object -First $Top | ForEach-Object { [pscustomobject]@{ Seconds = [Math]::Round((ConvertTo-Seconds $_.Duration), 1); Name = $_.Name } })

# --- findings and expectations
$findings = New-Object System.Collections.Generic.List[string]
if ($Strict) {
    if ($removed.Count) { $findings.Add("$($removed.Count) tests removed") }
    if ($changed | Where-Object { $_.C.Outcome -ne 'Passed' }) { $findings.Add("$(@($changed | Where-Object { $_.C.Outcome -ne 'Passed' }).Count) tests changed outcome away from Passed") }
    if ($newFailures.Count) { $findings.Add("$($newFailures.Count) new failures") }
    if ($candNotRun.Count) { $findings.Add("$($candNotRun.Count) skipped / not-executed tests in the candidate") }
    if ($softSkip.Count) { $findings.Add("$($softSkip.Count) SqlServerTests cases that took $SoftSkipMs ms or more in the baseline now pass in under $SoftSkipMs ms (possible soft skip)") }
}
if ($null -ne $ExpectAdded -and $added.Count -ne $ExpectAdded) { $findings.Add("expected $ExpectAdded added tests, found $($added.Count)") }
if ($ExpectFailed) {
    foreach ($e in $ExpectFailed) { if (-not ($candFailed | Where-Object { $_.Name.Contains($e) })) { $findings.Add("expected a failing test matching '$e', found none") } }
    $unexpected = @($candFailed | Where-Object { $n = $_.Name; -not ($ExpectFailed | Where-Object { $n.Contains($_) }) })
    if ($unexpected.Count) { $findings.Add("$($unexpected.Count) failing tests match none of the expected patterns, e.g. $($unexpected[0].Name)") }
}
if ($ExpectRcsiAdded) {
    $want = @{}; foreach ($p in ($ExpectRcsiAdded -split ',')) { $k, $v = $p -split '=', 2; $want[$k.Trim()] = [int]$v }
    foreach ($m in 'True', 'False', 'none') { $w = if ($want.ContainsKey($m)) { $want[$m] } else { 0 }; if ($aRc.$m -ne $w) { $findings.Add("added rcsi $m : expected $w, found $($aRc.$m)") } }
}
$exit = if ($findings.Count) { 1 } else { 0 }

$res = [pscustomobject]@{
    InputOk = $true; Baseline = $base.Files; Candidate = $cand.Files
    BaselineCounts = $bCounts; CandidateCounts = $cCounts; BaselineRcsi = $bRc; CandidateRcsi = $cRc; AddedRcsi = $aRc; RemovedRcsi = $rRc
    Added = @($added | ForEach-Object { $_.Name }); Removed = @($removed | ForEach-Object { $_.Name })
    OutcomeChanged = @($changed | ForEach-Object { [pscustomobject]@{ Name = $_.C.Name; From = $_.B.Outcome; To = $_.C.Outcome } })
    MatchedById = @($pairs | Where-Object By -eq 'id').Count; MatchedByName = $matchedByName
    CandidateFailed = @($candFailed | ForEach-Object { $_.Name }); NewFailures = @($newFailures | ForEach-Object { $_.Name })
    CandidateNotExecuted = @($candNotRun | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Outcome = $_.Outcome } })
    SoftSkipSuspects = @($softSkip | ForEach-Object { $_.Name }); FastSqlCases = $fastSql.Count; MethodRollup = $methodRollup; ClassDeltas = @($classTable); Slowest = $slowest
    DuplicateNamesBaseline = @($base.Results | Group-Object Name | Where-Object Count -gt 1).Count; DuplicateNamesCandidate = @($cand.Results | Group-Object Name | Where-Object Count -gt 1).Count
    Findings = @($findings); ExitCode = $exit
}
if ($Json) { Write-TextLf $Json ($res | ConvertTo-Json -Depth 6) }
if (-not $Quiet) {
    $fmt = { param($c) "total $($c.Total): passed $($c.Passed), failed $($c.Failed), not executed $($c.NotExecuted), other $($c.Other)" }
    'TRX comparison'
    "  baseline : $(& $fmt $bCounts)   rcsi False/True/none $($bRc.False)/$($bRc.True)/$($bRc.none)"
    "  candidate: $(& $fmt $cCounts)   rcsi False/True/none $($cRc.False)/$($cRc.True)/$($cRc.none)"
    "  matched by id $($res.MatchedById), by name $matchedByName; added $($added.Count) (rcsi False/True/none $($aRc.False)/$($aRc.True)/$($aRc.none)); removed $($removed.Count); outcome changed $($changed.Count)"
    "  tests sharing a display name: baseline $($res.DuplicateNamesBaseline) names, candidate $($res.DuplicateNamesCandidate) names (compared by id, not collapsed)"
    if ($candFailed.Count) { '  failed in candidate:'; $candFailed | ForEach-Object { "    $($_.Name)" } }
    if ($candNotRun.Count) { '  skipped / not executed in candidate:'; $candNotRun | Select-Object -First 20 | ForEach-Object { "    [$($_.Outcome)] $($_.Name)" } }
    if ($methodRollup.Count) { '  added / removed rows by method:'; $methodRollup | Select-Object -First 40 | ForEach-Object { "    $($_.Direction) x$($_.Rows)  $($_.Method)" } }
    if ($changed.Count) { '  outcome changes:'; $changed | Select-Object -First 20 | ForEach-Object { "    $($_.B.Outcome) -> $($_.C.Outcome)  $($_.C.Name)" } }
    if ($classTable) { '  classes whose test count differs:'; $classTable | Sort-Object Delta -Descending | Select-Object -First 30 | ForEach-Object { "    $($_.Delta.ToString('+0;-0'))  $($_.Class) ($($_.Baseline) -> $($_.Candidate))" } }
    "  SqlServerTests cases passing in under $SoftSkipMs ms: $($fastSql.Count) (informational); of those, slow in the baseline (soft-skip suspects): $($softSkip.Count)"
    $softSkip | Select-Object -First 10 | ForEach-Object { "    suspect: $($_.Name)" }
    "  slowest $Top :"; $slowest | ForEach-Object { "    $($_.Seconds)s  $($_.Name)" }
    if ($findings.Count) { 'FINDINGS:'; $findings | ForEach-Object { "  - $_" } } else { 'No findings.' }
}
if ($PassThru) { $res }
exit $exit
