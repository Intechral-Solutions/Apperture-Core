<#
.SYNOPSIS
    Controls for scripts/ci/test.ps1, run against a stand-in `dotnet` (no build, no SQL, no network).
.DESCRIPTION
    Each control runs test.ps1 in a child PowerShell with a fake `dotnet` first on PATH, in a throw-away repository root
    (APPERTURE_REPO_ROOT), and asserts the exit code and output. What they protect:

      * a failing test run is a failing exit code (including a host that exits 0 but whose TRX records a failure);
      * a run that executed nothing (a -Filter that matches no test, all tests skipped, no TRX) is never a pass;
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
$i = [array]::IndexOf($args, '--results-directory')
if ($env:FAKE_DOTNET_TRX -ne 'none' -and $i -ge 0) {
    $dir = $args[$i + 1]
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $n = if ($env:FAKE_DOTNET_TRXFILES) { [int]$env:FAKE_DOTNET_TRXFILES } else { 1 }
    for ($k = 1; $k -le $n; $k++) {
        $xml = '<?xml version="1.0" encoding="UTF-8"?><TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary outcome="Completed"><Counters total="{0}" executed="{1}" passed="{2}" failed="{3}" notExecuted="0" /></ResultSummary></TestRun>' -f $env:FAKE_DOTNET_TOTAL, $env:FAKE_DOTNET_EXECUTED, $env:FAKE_DOTNET_PASSED, $env:FAKE_DOTNET_FAILED
        Set-Content -LiteralPath (Join-Path $dir ("fake_$k.trx")) -Value $xml
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
    Assert-Control 'sets no skip or optional-SQL switch' ($text -notmatch 'REQUIRE_SQL|NotExecuted|SkipSql|-Skip') 'a skip or optional-SQL switch was found'
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
