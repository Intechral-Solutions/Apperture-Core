<#
.SYNOPSIS
    Controls for scripts/ci/test.ps1, run against a stand-in `dotnet` (no build, no SQL, no network).
.DESCRIPTION
    Each control runs test.ps1 in a child PowerShell with a fake `dotnet` first on PATH, in a throw-away repository root
    (APPERTURE_REPO_ROOT), and asserts the exit code and output. What they protect:

      * a failing test run is a failing exit code (including a host that exits 0 but whose TRX records a failure);
      * a run that executed nothing (a -Filter that matches no test, all tests skipped, no TRX) is never a pass;
      * executed tests that neither passed nor failed (inconclusive and similar) are never a pass;
      * a result file that is missing, truncated, empty, incomplete (a missing or non-numeric count), reports a run outcome
        other than Completed, or whose result records disagree with its counters is never a pass;
      * several result files: an all-skipped project is allowed and named, a project that selected nothing fails an
        unfiltered run and is only reported under -Filter;
      * the -Filter text cannot carry other filter operators and the test host is not called when it is rejected;
      * a missing SDK or solution is an error, and the removed SDK helper is not referenced again;
      * the script reads no environment file and no credential and never turns an unavailable SQL server into a pass;
      * a result file from an earlier run cannot stand in for this run.

    -ScriptPath lets the same controls run against a mutated copy of test.ps1: every mutation of a protection must fail
    at least one control (that is how the controls were shown to be able to fail; see the DEV-1.3B record).
    The run uses the same PowerShell host that started it (pwsh or Windows PowerShell). Only Windows was exercised;
    the Unix shim is written but unverified.
    Exit code: 0 all controls passed, 1 a control failed.
.PARAMETER ScriptPath
    The test.ps1 to check. Default: ../test.ps1 next to this folder.
.EXAMPLE
    pwsh scripts/ci/tests/Test-TestEntryPoint.ps1
    powershell -File scripts\ci\tests\Test-TestEntryPoint.ps1
#>
[CmdletBinding()]
param(
    [string]$ScriptPath = ''
)

$ErrorActionPreference = 'Stop'
# Resolved here, not in the parameter default: Windows PowerShell 5.1 leaves $PSScriptRoot empty inside a param block.
if (-not $ScriptPath) { $ScriptPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'test.ps1' }
$ScriptPath = (Resolve-Path -LiteralPath $ScriptPath).Path
$hostExe = (Get-Process -Id $PID).Path
$onWindows = [IO.Path]::DirectorySeparatorChar -eq '\'

# The scratch root sits inside the repository's ignored artifacts/ folder (never a system temp folder).
$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.Parent.FullName
$scratch = Join-Path (Join-Path (Join-Path $repoRoot 'artifacts') 'test-entrypoint-selftest') ([Guid]::NewGuid().ToString('N'))
$fakeRoot = Join-Path $scratch 'repo'
$fakeBin = Join-Path $scratch 'bin'
$emptyBin = Join-Path $scratch 'empty'
New-Item -ItemType Directory -Force -Path $fakeRoot, $fakeBin, $emptyBin | Out-Null
Set-Content -LiteralPath (Join-Path $fakeRoot 'Apperture.slnx') -Value '<Solution />'

# ---- the stand-in dotnet: logs its arguments, writes a TRX with the requested counters, exits as told ----
$fakePs1 = Join-Path $fakeBin 'fake-dotnet.ps1'
Set-Content -LiteralPath $fakePs1 -Value @'
Add-Content -LiteralPath $env:FAKE_DOTNET_LOG -Value ($args -join "`t")
function Get-Knob([string]$Name, [string]$Default) {
    $v = [Environment]::GetEnvironmentVariable($Name)
    if ($null -ne $v -and $v -ne '') { $v } else { $Default }
}
$i = [array]::IndexOf($args, '--results-directory')
if ($env:FAKE_DOTNET_TRX -ne 'none' -and $i -ge 0) {
    $dir = $args[$i + 1]
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $n = [int](Get-Knob 'FAKE_DOTNET_TRXFILES' '1')
    for ($k = 1; $k -le $n; $k++) {
        $c = [ordered]@{
            total = Get-Knob 'FAKE_DOTNET_TOTAL' '0'; executed = Get-Knob 'FAKE_DOTNET_EXECUTED' '0'
            passed = Get-Knob 'FAKE_DOTNET_PASSED' '0'; failed = Get-Knob 'FAKE_DOTNET_FAILED' '0'
            error = Get-Knob 'FAKE_DOTNET_ERROR' '0'; timeout = Get-Knob 'FAKE_DOTNET_TIMEOUT' '0'; aborted = Get-Knob 'FAKE_DOTNET_ABORTED' '0'
            inconclusive = Get-Knob 'FAKE_DOTNET_INCONCLUSIVE' '0'
        }
        if ($k -eq 1 -and $env:FAKE_DOTNET_FIRST_FAILED) { $c.failed = $env:FAKE_DOTNET_FIRST_FAILED }
        if ($k -eq 1 -and $env:FAKE_DOTNET_ERROR_FIRST) { $c.error = $env:FAKE_DOTNET_ERROR_FIRST }
        if ($k -eq $n -and $env:FAKE_DOTNET_LAST_EMPTY -eq '1') { foreach ($key in @($c.Keys)) { $c[$key] = '0' } }
        # A last assembly whose three tests were all skipped (as real TRX files record it: executed 0, notExecuted 0).
        if ($k -eq $n -and $env:FAKE_DOTNET_LAST_SKIPPED -eq '1') { foreach ($key in @($c.Keys)) { $c[$key] = '0' }; $c.total = '3' }
        # A last assembly that executed two tests, neither of which passed or failed.
        if ($k -eq $n -and $env:FAKE_DOTNET_LAST_INCONCLUSIVE -eq '1') { foreach ($key in @($c.Keys)) { $c[$key] = '0' }; $c.total = '2'; $c.executed = '2'; $c.inconclusive = '2' }

        # Per-file counts for a multi-file run: 'total,executed,passed,failed;total,executed,passed,failed;...'
        if ($env:FAKE_DOTNET_FILES) {
            $row = ($env:FAKE_DOTNET_FILES -split ';')[$k - 1] -split ','
            $c.total = $row[0]; $c.executed = $row[1]; $c.passed = $row[2]; $c.failed = $row[3]
        }

        # The result records, written the way the counters describe them unless a knob makes the two disagree.
        $recPassed = [int](Get-Knob 'FAKE_DOTNET_REC_PASSED' $c.passed)
        $skipCount = [int](Get-Knob 'FAKE_DOTNET_REC_SKIPPED' ([Math]::Max(0, [int]$c.total - [int]$c.executed)))
        $recs = New-Object System.Text.StringBuilder
        foreach ($pair in @(@('Passed', $recPassed), @('Failed', [int]$c.failed), @('Error', [int]$c.error), @('Timeout', [int]$c.timeout), @('Aborted', [int]$c.aborted),
                @('Inconclusive', [int]$c.inconclusive), @((Get-Knob 'FAKE_DOTNET_REC_SKIP_AS' 'NotExecuted'), $skipCount), @('Failed', [int](Get-Knob 'FAKE_DOTNET_REC_EXTRA_FAILED' '0')))) {
            for ($j = 0; $j -lt $pair[1]; $j++) { [void]$recs.Append('<UnitTestResult testId="t" outcome="' + $pair[0] + '" />') }
        }
        $defs = if ([int]$c.total -gt 0) { '<UnitTest name="m" id="t"><TestMethod codeBase="C:\fake\Apperture.Fake' + $k + '.Tests.dll" className="C" name="m" /></UnitTest>' } else { '' }

        $drop = $env:FAKE_DOTNET_DROP
        $setName, $setValue = if ($env:FAKE_DOTNET_SET) { $env:FAKE_DOTNET_SET -split '=', 2 } else { '', '' }
        $attrs = (@($c.Keys) + 'notExecuted' | Where-Object { $_ -ne $drop } | ForEach-Object {
                $value = if ($_ -eq $setName) { $setValue } elseif ($_ -eq 'notExecuted') { '0' } else { $c[$_] }
                $_ + '="' + $value + '"'
            }) -join ' '
        $outcome = Get-Knob 'FAKE_DOTNET_OUTCOME' 'Completed'
        $outcomeAttr = if ($outcome -eq '-') { '' } else { ' outcome="' + $outcome + '"' }
        # One extra verdict bucket ('name=value'), and optionally a duplicated Counters or ResultSummary element.
        $bucketName, $bucketValue = if ($env:FAKE_DOTNET_BUCKET) { $env:FAKE_DOTNET_BUCKET -split '=', 2 } else { '', '' }
        $bucketAttr = if ($bucketName) { ' ' + $bucketName + '="' + $bucketValue + '"' } else { '' }
        $countersXml = '<Counters ' + $attrs + $bucketAttr + ' />'
        if ($env:FAKE_DOTNET_DUP -eq 'counters') { $countersXml += $countersXml }
        $summaryXml = '<ResultSummary' + $outcomeAttr + '>' + $countersXml + '</ResultSummary>'
        if ($env:FAKE_DOTNET_DUP -eq 'summary') { $summaryXml += '<ResultSummary outcome="Completed" />' }   # no counters: only the summary count differs
        $xml = '<?xml version="1.0" encoding="UTF-8"?><TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">' + $summaryXml + '<Results>' + $recs + '</Results><TestDefinitions>' + $defs + '</TestDefinitions></TestRun>'
        $path = Join-Path $dir ("fake_$k.trx")
        if ($k -eq $n -and $env:FAKE_DOTNET_CORRUPT_LAST -eq '1') { $xml = '<TestRun><ResultSummary outcome="Completed" /></TestRun>' }
        if ($k -eq $n -and $env:FAKE_DOTNET_CORRUPT_LAST -eq 'xml') { $xml = '<TestRun><ResultSummary outcome="Completed"><Counters total="5" executed="5" passed="5"' }
        if ($k -eq $n -and $env:FAKE_DOTNET_CORRUPT_LAST -eq 'empty') { $xml = '' }
        [IO.File]::WriteAllText($path, $xml)
    }
}
exit ([int]$env:FAKE_DOTNET_EXIT)
'@
if ($onWindows) {
    Set-Content -LiteralPath (Join-Path $fakeBin 'dotnet.cmd') -Value "@echo off`r`n`"$hostExe`" -NoProfile -File `"%~dp0fake-dotnet.ps1`" %*`r`nexit /b %ERRORLEVEL%" -Encoding ASCII
} else {
    $shim = Join-Path $fakeBin 'dotnet'
    Set-Content -LiteralPath $shim -Value "#!/bin/sh`nexec `"$hostExe`" -NoProfile -File `"`$(dirname `"`$0`")/fake-dotnet.ps1`" `"`$@`""
    & chmod +x $shim
}

$script:pass = 0; $script:fail = 0
function Assert-Control([string]$Name, [bool]$Condition, [string]$Detail) {
    if ($Condition) { $script:pass++; Write-Host "  PASS  $Name" -ForegroundColor Green }
    else { $script:fail++; Write-Host "  FAIL  $Name  ($Detail)" -ForegroundColor Red }
}

function Invoke-Entry {
    param(
        [string[]]$Arguments = @(),
        [hashtable]$Fake = @{},
        [string]$PathOverride = '',
        [string]$Root = $fakeRoot
    )
    $log = Join-Path $scratch ("calls-" + [Guid]::NewGuid().ToString('N') + '.log')
    $vars = @{
        APPERTURE_REPO_ROOT = $Root; FAKE_DOTNET_LOG = $log; FAKE_DOTNET_EXIT = '0'; FAKE_DOTNET_TRX = 'counts'
        FAKE_DOTNET_TOTAL = '5'; FAKE_DOTNET_EXECUTED = '5'; FAKE_DOTNET_PASSED = '5'; FAKE_DOTNET_FAILED = '0'; FAKE_DOTNET_TRXFILES = '1'
    }
    foreach ($k in $Fake.Keys) { $vars[$k] = [string]$Fake[$k] }
    $saved = @{}
    foreach ($k in @($vars.Keys) + 'PATH') { $saved[$k] = [Environment]::GetEnvironmentVariable($k) }
    try {
        foreach ($k in $vars.Keys) { [Environment]::SetEnvironmentVariable($k, $vars[$k]) }
        $sep = [IO.Path]::PathSeparator
        $pathValue = if ($PathOverride) { $PathOverride } else { $fakeBin + $sep + $saved['PATH'] }
        [Environment]::SetEnvironmentVariable('PATH', $pathValue)
        # Windows PowerShell 5.1 turns a child's stderr into error records, which 'Stop' would make terminating.
        $ErrorActionPreference = 'Continue'
        $out = (& $hostExe -NoProfile -File $ScriptPath @Arguments *>&1 | Out-String)
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = 'Stop'
        foreach ($k in $saved.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k]) }
    }
    # The outer @() matters: an `if` expression unrolls a one-element array, and Calls[0] would then be its first character.
    $calls = @(if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log })
    [pscustomobject]@{ Code = $code; Output = $out; Calls = $calls }
}

try {
    Write-Host "Controls for $ScriptPath (host: $hostExe)"

    $r = Invoke-Entry
    Assert-Control 'passing run exits 0 and reports the counts' ($r.Code -eq 0 -and $r.Output -match 'Tests passed: 5 passed, 0 skipped or not executed, 0 failed') "exit $($r.Code): $($r.Output)"
    Assert-Control 'dotnet is called once, for the solution, Debug, with a TRX logger and a results folder under artifacts/' ($r.Calls.Count -eq 1 -and $r.Calls[0] -match "^test`t.*Apperture\.slnx`t-c`tDebug" -and $r.Calls[0] -match "--logger`ttrx`t--results-directory`t.*artifacts.test-results") ("exit $($r.Code) n=$($r.Calls.Count): " + ($r.Calls -join ' | '))
    Assert-Control 'default run does not skip the build and sends no filter' ($r.Calls[0] -notmatch '--no-build' -and $r.Calls[0] -notmatch '--filter') ("exit $($r.Code) n=$($r.Calls.Count): " + ($r.Calls -join ' | '))

    $r = Invoke-Entry -Arguments @('-Configuration', 'Release', '-NoBuild')
    Assert-Control '-Configuration Release -NoBuild reaches dotnet' ($r.Code -eq 0 -and $r.Calls[0] -match "-c`tRelease" -and $r.Calls[0] -match '--no-build') ("exit $($r.Code) n=$($r.Calls.Count): " + ($r.Calls -join ' | '))

    $r = Invoke-Entry -Arguments @('-Filter', 'PasswordPolicyEvaluatorTests')
    Assert-Control '-Filter becomes FullyQualifiedName~<fragment> and says it is iteration only' ($r.Code -eq 0 -and $r.Calls[0] -match "--filter`tFullyQualifiedName~PasswordPolicyEvaluatorTests" -and $r.Output -match 'not evidence') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_EXIT = 1; FAKE_DOTNET_FAILED = 2; FAKE_DOTNET_PASSED = 3 }
    Assert-Control 'a failing test run is a failing exit code' ($r.Code -ne 0 -and $r.Output -match 'Tests failed \(dotnet test exit code 1\)' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_EXIT = 137 }
    Assert-Control 'a crashed test host (exit 137) is a failing exit code' ($r.Code -ne 0 -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_EXIT = 0; FAKE_DOTNET_FAILED = 1; FAKE_DOTNET_PASSED = 4 }
    Assert-Control 'host exit 0 but the TRX records a failure is a failing exit code' ($r.Code -ne 0 -and $r.Output -match '1 failed' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Arguments @('-Filter', 'NoSuchClass') -Fake @{ FAKE_DOTNET_TRX = 'none' }
    Assert-Control 'a filter that matches nothing (exit 0, no TRX) is a failing exit code, not a pass' ($r.Code -ne 0 -and $r.Output -match 'No test was executed' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TOTAL = 3; FAKE_DOTNET_EXECUTED = 0; FAKE_DOTNET_PASSED = 0 }
    Assert-Control 'a run in which every test was skipped (executed 0) is a failing exit code' ($r.Code -ne 0 -and $r.Output -match 'No test was executed' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TOTAL = 6; FAKE_DOTNET_EXECUTED = 4; FAKE_DOTNET_PASSED = 4 }
    Assert-Control 'skipped tests are reported, not hidden, and do not fail a run that executed others' ($r.Code -eq 0 -and $r.Output -match '4 passed, 2 skipped or not executed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TRXFILES = 3; FAKE_DOTNET_TOTAL = 2; FAKE_DOTNET_EXECUTED = 2; FAKE_DOTNET_PASSED = 2 }
    Assert-Control 'counts are summed over every test assembly (3 files x 2 tests)' ($r.Code -eq 0 -and $r.Output -match 'Tests passed: 6 passed' -and $r.Output -match '3 test assemblies') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_EXIT = -1073741819 }
    Assert-Control 'a crashed Windows test host (negative exit code) with a passing TRX is a failing exit code' ($r.Code -ne 0 -and $r.Output -match 'Tests failed' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TRXFILES = 2; FAKE_DOTNET_FIRST_FAILED = 1 }
    Assert-Control 'a failure recorded in an earlier TRX file is a failing exit code (last file clean)' ($r.Code -ne 0 -and $r.Output -match '1 failed' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TRXFILES = 2; FAKE_DOTNET_CORRUPT_LAST = 1 }
    Assert-Control 'an unreadable TRX file fails the run instead of being skipped' ($r.Code -ne 0 -and $r.Output -match 'Cannot read the test counters' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    # Policy for several result files (one per test project). Under -Filter every project but the one holding the matching tests
    # selects nothing, so that is expected and reported. Without -Filter a project that selected nothing is a scope that never ran.
    $r = Invoke-Entry -Arguments @('-Filter', 'SomeClass') -Fake @{ FAKE_DOTNET_TRXFILES = 2; FAKE_DOTNET_LAST_EMPTY = 1 }
    Assert-Control 'under -Filter an assembly that selected no test does not fail a run in which another assembly executed tests, and is reported' ($r.Code -eq 0 -and $r.Output -match 'Tests passed: 5 passed' -and $r.Output -match '1 result file\(s\) selected no test') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TRXFILES = 2; FAKE_DOTNET_LAST_EMPTY = 1 }
    Assert-Control 'without -Filter an assembly that selected no test fails the run, although another assembly executed tests' ($r.Code -ne 0 -and $r.Output -match 'selected no test' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TRXFILES = 3; FAKE_DOTNET_TOTAL = 2; FAKE_DOTNET_EXECUTED = 2; FAKE_DOTNET_PASSED = 2; FAKE_DOTNET_LAST_SKIPPED = 1 }
    Assert-Control 'an assembly whose tests were all skipped is allowed beside passing assemblies, is named, and is classed as not authoritative' ($r.Code -eq 0 -and $r.Output -match 'Apperture\.Fake3\.Tests\.dll executed no test' -and $r.Output -match 'not authoritative' -and $r.Output -match 'Tests passed: 4 passed, 3 skipped or not executed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TRXFILES = 2; FAKE_DOTNET_LAST_INCONCLUSIVE = 1 }
    Assert-Control 'executed tests with no passing verdict in a later TRX file fail the run although the earlier file passed' ($r.Code -ne 0 -and $r.Output -match 'without a passing verdict' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    # A selected run in which tests were executed but none passed and none failed must not be reported as a pass.
    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TOTAL = 5; FAKE_DOTNET_EXECUTED = 5; FAKE_DOTNET_PASSED = 0; FAKE_DOTNET_FAILED = 0 }
    Assert-Control 'executed 5, passed 0, failed 0 (no verdict recorded) is a failing exit code' ($r.Code -ne 0 -and $r.Output -match 'without a passing verdict' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TOTAL = 5; FAKE_DOTNET_EXECUTED = 5; FAKE_DOTNET_PASSED = 0; FAKE_DOTNET_FAILED = 0; FAKE_DOTNET_INCONCLUSIVE = 5 }
    Assert-Control 'executed 5, passed 0, failed 0, inconclusive 5 is a failing exit code' ($r.Code -ne 0 -and $r.Output -match 'without a passing verdict' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TOTAL = 5; FAKE_DOTNET_EXECUTED = 5; FAKE_DOTNET_PASSED = 4; FAKE_DOTNET_FAILED = 0; FAKE_DOTNET_INCONCLUSIVE = 1 }
    Assert-Control 'one inconclusive test among four passing ones is a failing exit code' ($r.Code -ne 0 -and $r.Output -match 'without a passing verdict' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    # An inconclusive test that the executed count leaves out (all five executed tests passed, one more is inconclusive).
    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TOTAL = 5; FAKE_DOTNET_EXECUTED = 5; FAKE_DOTNET_PASSED = 5; FAKE_DOTNET_INCONCLUSIVE = 1 }
    Assert-Control 'an inconclusive count that the executed count omits still fails the run, with the verdict message' ($r.Code -ne 0 -and $r.Output -match 'without a passing verdict' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    # Missing, incomplete or malformed result records.
    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_DROP = 'passed' }
    Assert-Control 'a counters element without a passed count fails instead of reading it as 0' ($r.Code -ne 0 -and $r.Output -match "Cannot read the test counters: the 'passed' count is missing" -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_SET = 'passed=abc' }
    Assert-Control 'a count that is not a number fails with the file named' ($r.Code -ne 0 -and $r.Output -match "Cannot read the test counters: 'passed' is not a whole number" -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_SET = 'failed=-1' }
    Assert-Control 'a negative count cannot cancel a failure and fails the run' ($r.Code -ne 0 -and $r.Output -match "Cannot read the test counters: 'failed' is not a whole number" -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TRXFILES = 2; FAKE_DOTNET_CORRUPT_LAST = 'xml' }
    Assert-Control 'a truncated (ill-formed) TRX file fails the run' ($r.Code -ne 0 -and $r.Output -match 'Cannot read the test counters' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TRXFILES = 2; FAKE_DOTNET_CORRUPT_LAST = 'empty' }
    Assert-Control 'a zero-byte TRX file fails the run' ($r.Code -ne 0 -and $r.Output -match 'Cannot read the test counters' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_OUTCOME = 'Aborted' }
    Assert-Control 'a TRX whose run outcome is not Completed fails the run although its counters look clean' ($r.Code -ne 0 -and $r.Output -match "run outcome 'Aborted'" -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_OUTCOME = '-' }
    Assert-Control 'a TRX without a run outcome fails the run' ($r.Code -ne 0 -and $r.Output -match 'run outcome' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_REC_EXTRA_FAILED = 1 }
    Assert-Control 'a result record that is neither passed nor skipped fails the run although the counters look clean' ($r.Code -ne 0 -and $r.Output -match 'do not match the counters' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_REC_PASSED = 0 }
    Assert-Control 'counters that claim passes the result records do not contain fail the run' ($r.Code -ne 0 -and $r.Output -match 'do not match the counters' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    # A Failed record in place of a Passed one: the record count still adds up, only the outcome shows it.
    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_REC_PASSED = 4; FAKE_DOTNET_REC_EXTRA_FAILED = 1 }
    Assert-Control 'a Failed record in place of one of the Passed records the counters claim fails the run' ($r.Code -ne 0 -and $r.Output -match 'do not match the counters' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    # The records are judged per file and by outcome: a Failed or Error record cannot stand in for a skip, a missing skip record
    # is a mismatch, and one file cannot make up for another.
    foreach ($as in @('Failed', 'Error', 'Inconclusive')) {
        $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TOTAL = 6; FAKE_DOTNET_EXECUTED = 5; FAKE_DOTNET_PASSED = 5; FAKE_DOTNET_REC_SKIP_AS = $as }
        Assert-Control "a '$as' record where the counters say one test was skipped fails the run" ($r.Code -ne 0 -and $r.Output -match 'do not match the counters' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"
    }

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TOTAL = 6; FAKE_DOTNET_EXECUTED = 5; FAKE_DOTNET_PASSED = 5; FAKE_DOTNET_REC_SKIPPED = 0 }
    Assert-Control 'a skipped test with no result record fails the run' ($r.Code -ne 0 -and $r.Output -match 'do not match the counters' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TOTAL = 5; FAKE_DOTNET_EXECUTED = 5; FAKE_DOTNET_PASSED = 6 }
    Assert-Control 'a file that counts more passes than tests it executed fails the run' ($r.Code -ne 0 -and $r.Output -match 'without a passing verdict' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TRXFILES = 2; FAKE_DOTNET_FILES = '5,5,6,0;6,5,4,0'; FAKE_DOTNET_REC_SKIP_AS = 'Failed' }
    Assert-Control 'one file claiming more passes than it executed cannot offset another file that executed a test without a pass' ($r.Code -ne 0 -and $r.Output -match 'without a passing verdict' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TRXFILES = 2; FAKE_DOTNET_FILES = '4,4,2,0;3,0,2,0'; FAKE_DOTNET_REC_SKIP_AS = 'Failed' }
    Assert-Control 'an all-skipped file that claims passes cannot hide failed records and fails the run' ($r.Code -ne 0 -and $r.Output -match 'without a passing verdict' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_DUP = 'counters' }
    Assert-Control 'a result file with two counters elements fails the run' ($r.Code -ne 0 -and $r.Output -match 'Cannot read the test counters' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_DUP = 'summary' }
    Assert-Control 'a result file with two run summaries fails the run' ($r.Code -ne 0 -and $r.Output -match 'run outcome' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"

    # Every verdict bucket other than passed, failed and errored counts against a pass (inconclusive has its own controls above).
    foreach ($bucket in @('passedButRunAborted', 'notRunnable', 'disconnected', 'warning', 'pending', 'inProgress', 'completed')) {
        $r = Invoke-Entry -Fake @{ FAKE_DOTNET_BUCKET = "$bucket=1" }
        Assert-Control "a result file with $bucket = 1 fails the run although every counted test passed" ($r.Code -ne 0 -and $r.Output -match 'without a passing verdict' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"
    }

    foreach ($case in @(
            @{ Name = 'errored'; Fake = @{ FAKE_DOTNET_ERROR = 1 } },
            @{ Name = 'timed-out'; Fake = @{ FAKE_DOTNET_TIMEOUT = 1 } },
            @{ Name = 'aborted'; Fake = @{ FAKE_DOTNET_ABORTED = 1 } },
            @{ Name = 'errored in the first of two TRX files'; Fake = @{ FAKE_DOTNET_TRXFILES = 2; FAKE_DOTNET_ERROR_FIRST = 1 } })) {
        $r = Invoke-Entry -Fake $case.Fake
        Assert-Control "$($case.Name) tests in the TRX fail the run even when the host exited 0" ($r.Code -ne 0 -and $r.Output -match 'errored, timed-out or aborted' -and $r.Output -notmatch 'Tests passed') "exit $($r.Code): $($r.Output)"
    }

    # The printed path must be the solution of the root the run was redirected to, not the script's own checkout.
    $r = Invoke-Entry
    $expectedLine = 'Testing: ' + (Join-Path $fakeRoot 'Apperture.slnx')
    Assert-Control 'the solution about to be tested is printed exactly, and an APPERTURE_REPO_ROOT redirect is announced' ($r.Output.Contains($expectedLine) -and $r.Output -match 'APPERTURE_REPO_ROOT is set') $r.Output

    # 'A' + LF: `$` in a .NET regex also matches before a final newline, so the pattern ends in \z.
    foreach ($bad in @('A|B', 'A&B', 'A B', 'A=B', 'A~B', '!A', '(A)', "A`n")) {
        $r = Invoke-Entry -Arguments @('-Filter', $bad)
        Assert-Control "-Filter '$($bad -replace "`n", '<LF>')' is rejected before dotnet is called" ($r.Code -ne 0 -and $r.Calls.Count -eq 0) "exit $($r.Code), calls $($r.Calls.Count): $($r.Output)"
    }

    $r = Invoke-Entry -PathOverride $emptyBin
    Assert-Control 'no dotnet on PATH is a clear error' ($r.Code -ne 0 -and $r.Output -match 'dotnet was not found on PATH') "exit $($r.Code): $($r.Output)"

    $noSln = Join-Path $scratch 'no-solution'
    New-Item -ItemType Directory -Force -Path $noSln | Out-Null
    $r = Invoke-Entry -Root $noSln
    Assert-Control 'a missing solution is an error and dotnet is not called' ($r.Code -ne 0 -and $r.Output -match 'Solution not found' -and $r.Calls.Count -eq 0) "exit $($r.Code): $($r.Output)"

    # A result file from an earlier run lies in a sibling results folder; this run writes none.
    $older = Join-Path (Join-Path (Join-Path $fakeRoot 'artifacts') 'test-results') '20200101T000000000Z'
    New-Item -ItemType Directory -Force -Path $older | Out-Null
    Copy-Item -LiteralPath (Get-ChildItem -LiteralPath (Join-Path (Join-Path $fakeRoot 'artifacts') 'test-results') -Recurse -Filter *.trx | Select-Object -First 1).FullName -Destination (Join-Path $older 'old.trx')
    $r = Invoke-Entry -Fake @{ FAKE_DOTNET_TRX = 'none' }
    Assert-Control 'a TRX left by an earlier run cannot stand in for this run' ($r.Code -ne 0 -and $r.Output -match 'No test was executed') "exit $($r.Code): $($r.Output)"

    # Source properties of the script itself.
    $text = Get-Content -LiteralPath $ScriptPath -Raw
    Assert-Control 'does not reference the removed SDK helper' ($text -notmatch 'Get-DevDotNetSdk') 'Get-DevDotNetSdk found'
    Assert-Control 'reads no environment file and no credential' ($text -notmatch '\.env\b' -and $text -notmatch 'SA_PASSWORD|Password\s*=|ConnectionString|SQLCMDPASSWORD') 'a credential or .env reference was found'
    Assert-Control 'sets no skip or optional-SQL switch' ($text -notmatch 'REQUIRE_SQL|SkipSql|-Skip') 'a skip or optional-SQL switch was found'
    $named = @([regex]::Matches($text, '(?:\.ai|\.github|tests|scripts|docs)[\\/][A-Za-z0-9_./\\-]*[A-Za-z0-9_]\.(?:ps1|md|yml)') | ForEach-Object { $_.Value })
    $missing = @($named | Where-Object { -not (Test-Path -LiteralPath (Join-Path $repoRoot $_)) })
    Assert-Control 'every repository file it names exists (and it names at least one)' ($named.Count -gt 0 -and $missing.Count -eq 0) ("missing: " + ($missing -join ', '))
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ("{0} passed, {1} failed" -f $script:pass, $script:fail)
if ($script:fail -gt 0) { exit 1 }
exit 0
