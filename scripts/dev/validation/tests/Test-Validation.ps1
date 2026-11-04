#Requires -Version 7.2
<#
.SYNOPSIS
    Self-test of the validation toolkit: every tool against good input and against evidence that is corrupted, missing, mismatched or misleading.
.DESCRIPTION
    A validation tool is the single point of trust for a record, so it is itself checked here, by negative controls: each section feeds a
    tool something it must reject (or flag) and asserts that it did. Fixtures (throw-away Git repositories, TRX files, a small .NET solution) are
    built in a scratch folder next to the repository (-WorkRoot), which is removed at the end. The preflight and mutation sections also create
    and remove a few folders under the repository's own ignored artifacts/ (selftest-*); no tracked file is written.

    Sections: lib (checksums, TRX reader), trx (Compare-Trx), select (Select-Tests), replay (Invoke-Mutation -ReplaySummary),
    sql (Get-SqlEvidence with a fake docker), preflight, export (Export-Evidence), and two live sections that build a small .NET fixture
    solution: mutation (Invoke-Mutation) and evidence (Invoke-Evidence). The live sections need the xunit packages in the local NuGet
    cache or a package source. If they cannot run they are reported as SKIPPED and the exit code is 3, never 0: an untested tool is not a
    tested one.

    Exit codes: 0 = every section ran and every control behaved; 1 = a control failed; 3 = all that ran passed, but part was skipped.
#>
[CmdletBinding()]
param(
    [string]$WorkRoot,
    [string[]]$Only,
    [switch]$SkipLive,
    [switch]$KeepWork
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$val = Split-Path $PSScriptRoot -Parent
. (Join-Path $val 'lib/ValidationCommon.ps1')
$Only = Expand-List $Only
$repoRoot = (Invoke-Git $PSScriptRoot @('rev-parse', '--show-toplevel')).Text
$pwshExe = (Get-Process -Id $PID).Path
if (-not $WorkRoot) { $WorkRoot = Join-Path (Split-Path $repoRoot -Parent) (".validation-selftest-" + [guid]::NewGuid().ToString('N').Substring(0, 8)) }
$WorkRoot = [IO.Path]::GetFullPath($WorkRoot)
if (Test-PathUnder $WorkRoot $repoRoot) { throw "work root must be outside the repository: $WorkRoot" }
if (Test-Path -LiteralPath $WorkRoot) { throw "work root exists: $WorkRoot" }
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
[IO.File]::WriteAllText((Join-Path $WorkRoot '.validation-selftest'), 'safe to delete')

$script:pass = 0; $script:fail = 0; $script:skipped = @()
function Check([string]$Name, [object]$Condition, [string]$Detail = '') {
    $ok = if ($Condition -is [array]) { $Condition.Count -gt 0 } else { [bool]$Condition }   # `-match` on an array returns the matches
    if ($ok) { $script:pass++; Write-Host "  ok   $Name" } else { $script:fail++; Write-Host "  FAIL $Name $Detail" -ForegroundColor Red }
}
function Invoke-Tool([string]$Script, [string[]]$ToolArgs, [hashtable]$Env = @{}) {
    $saved = @{}; foreach ($k in $Env.Keys) { $saved[$k] = [Environment]::GetEnvironmentVariable($k); [Environment]::SetEnvironmentVariable($k, $Env[$k]) }
    try { $out = & $pwshExe -NoProfile -File $Script @ToolArgs 2>&1 | ForEach-Object { [string]$_ }; [pscustomobject]@{ Exit = $LASTEXITCODE; Out = ($out -join "`n") } }
    finally { foreach ($k in $saved.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k]) } }
}
function Want-Section([string]$Name) { (-not $Only) -or ($Only -contains $Name) }
function Skip-Section([string]$Name, [string]$Why) { $script:skipped += "$Name ($Why)"; Write-Host "  SKIPPED $Name : $Why" -ForegroundColor Yellow }

# ---------------------------------------------------------------------------------------------------------------- fixtures
function New-Trx([string]$Path, $Tests, [hashtable]$CounterOverride = @{}, [switch]$Truncate, [switch]$NoCounters, [string]$RunOutcome = 'Completed', [switch]$EmptyCounters) {
    $ns = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'
    $sb = [Text.StringBuilder]::new()
    [void]$sb.AppendLine("<?xml version=`"1.0`" encoding=`"UTF-8`"?><TestRun id=`"$([guid]::NewGuid())`" xmlns=`"$ns`"><Times creation=`"2026-01-01T00:00:00Z`" start=`"2026-01-01T00:00:00Z`" finish=`"2026-01-01T00:01:00Z`" /><Results>")
    foreach ($t in $Tests) {
        $dur = if ($t.ContainsKey('Duration')) { $t.Duration } else { '00:00:01.0000000' }
        $err = if ($t.Outcome -eq 'Failed') { "<Output><ErrorInfo><Message>$(if ($t.ContainsKey('Message')) { $t.Message } else { 'Assert.True() Failure' })</Message></ErrorInfo></Output>" } else { '' }
        [void]$sb.AppendLine("<UnitTestResult executionId=`"$([guid]::NewGuid())`" testId=`"$($t.Id)`" testName=`"$([Security.SecurityElement]::Escape($t.Name))`" duration=`"$dur`" outcome=`"$($t.Outcome)`">$err</UnitTestResult>")
    }
    [void]$sb.AppendLine('</Results><TestDefinitions>')
    foreach ($t in $Tests) { [void]$sb.AppendLine("<UnitTest name=`"x`" id=`"$($t.Id)`"><TestMethod className=`"$($t.Class)`" name=`"x`" /></UnitTest>") }
    [void]$sb.AppendLine('</TestDefinitions>')
    $c = [ordered]@{ total = @($Tests).Count; executed = @($Tests | Where-Object { $_.Outcome -ne 'NotExecuted' }).Count; passed = @($Tests | Where-Object { $_.Outcome -eq 'Passed' }).Count; failed = @($Tests | Where-Object { $_.Outcome -eq 'Failed' }).Count; notExecuted = 0 }   # as vstest writes it: a skipped test leaves notExecuted at 0 and is left out of executed
    foreach ($k in $CounterOverride.Keys) { $c[$k] = $CounterOverride[$k] }
    $attrs = ($c.GetEnumerator() | ForEach-Object { '{0}="{1}"' -f $_.Key, $_.Value }) -join ' '
    if ($EmptyCounters) { $attrs = '' }
    if (-not $NoCounters) { [void]$sb.AppendLine("<ResultSummary outcome=`"$RunOutcome`"><Counters $attrs /></ResultSummary>") }
    [void]$sb.AppendLine('</TestRun>')
    $text = $sb.ToString(); if ($Truncate) { $text = $text.Substring(0, [int]($text.Length * 0.6)) }
    [IO.File]::WriteAllText($Path, $text, [Text.UTF8Encoding]::new($false))
}
function New-FakeDocker([string]$Dir) {
    # A stand-in for `docker`, called in-process by the tools. It records its arguments and whether the password arrived through the
    # environment, answers `inspect`, and answers sqlcmd queries: markers (fixed, 'moved', or stepping with each call when FAKE_STATE_FILE
    # is set) and the error-log window (FAKE_STARTUPS, default 7). FAKE_DOCKER_FAIL=1 makes every call fail.
    $path = Join-Path $Dir 'fake-docker.ps1'
    [IO.File]::WriteAllText($path, @'
param()
$log = Join-Path $PSScriptRoot 'docker-args.txt'
Set-Content -LiteralPath $log -Value ($args -join "`n")
Add-Content -LiteralPath $log -Value ("ENV_PASSWORD_PRESENT=" + [bool]$env:SQLCMDPASSWORD)
if ($env:FAKE_DOCKER_FAIL -eq '1') { Write-Error 'Cannot connect'; exit 1 }
if ($args[0] -eq 'inspect') { 'running running=true startedAt=2026-11-04T00:00:00Z'; exit 0 }
if ($args -join ' ' -match 'xp_readerrorlog') { $s = if ($env:FAKE_STARTUPS) { $env:FAKE_STARTUPS } else { '7' }; 'server_utc=2026-11-04T10:00:00.000'; "startups=$s"; 'first=2026-11-04T09:00:00.000|last=2026-11-04T09:50:00.000'; exit 0 }
$updates = if ($env:FAKE_MARKERS -eq 'moved') { 120 } else { 100 }
if ($env:FAKE_STATE_FILE) { $n = if (Test-Path $env:FAKE_STATE_FILE) { [int](Get-Content $env:FAKE_STATE_FILE) } else { 0 }; $updates = 100 + $n * [int]$env:FAKE_STEP; Set-Content $env:FAKE_STATE_FILE ($n + 1) }
$lw = '2026-11-04T09:59:00.000'; $off = if ($env:FAKE_UTC_OFFSET) { $env:FAKE_UTC_OFFSET } else { '0' }
# FAKE_WRITE_NOW=1: the server's last write is "just now" (a persistent shared catalog written during the run), in server-local time
if ($env:FAKE_WRITE_NOW -eq '1') { $lw = [datetime]::UtcNow.AddMinutes([int]$off).ToString('yyyy-MM-ddTHH:mm:ss.fff') }
'server_utc=2026-11-04T10:00:00.000'; if ($env:FAKE_NO_OFFSET -ne '1') { "utc_offset_minutes=$off" }; "catalog|Apperture_A|$lw|$updates"; 'catalog|Apperture_B|null|0'
exit 0
'@)
    $path
}
$cls = 'Fx.Tests.SampleSqlServerTests'
function T([string]$Id, [string]$Name, [string]$Outcome = 'Passed', [string]$Duration = '00:00:01.0000000', [string]$Class = $cls) { @{ Id = $Id; Name = "$Class.$Name"; Outcome = $Outcome; Duration = $Duration; Class = $Class } }

# ====================================================================================================================== lib
if (Want-Section 'lib') {
    Write-Host 'lib: checksums, TRX reader, redaction'
    $d = Join-Path $WorkRoot 'lib'; New-Item -ItemType Directory $d | Out-Null
    [IO.File]::WriteAllText("$d/a.txt", 'alpha'); New-Item -ItemType Directory "$d/sub" | Out-Null; [IO.File]::WriteAllText("$d/sub/b.txt", 'beta')
    [void](Write-ChecksumFile $d)
    $sums = [IO.File]::ReadAllText("$d/SHA256SUMS.txt")
    Check 'checksum file has LF endings and no BOM' (-not $sums.Contains("`r") -and -not $sums.StartsWith([char]0xFEFF))
    Check 'a fresh folder verifies' (Test-ChecksumFile $d).Ok
    if (Get-Command sha256sum -ErrorAction SilentlyContinue) {
        Push-Location $d; try { $viaSha = (& sha256sum -c SHA256SUMS.txt 2>&1); $shaExit = $LASTEXITCODE } finally { Pop-Location }
        Check 'the independent sha256sum -c agrees with the file' ($shaExit -eq 0) ($viaSha -join ' ')
    }
    else { Write-Host '  note sha256sum is not on PATH: only the toolkit verifier checked the file' }
    [IO.File]::WriteAllText("$d/a.txt", 'alphA'); $r = Test-ChecksumFile $d
    Check 'a changed file is detected' ((-not $r.Ok) -and $r.Bad -contains 'a.txt'); [IO.File]::WriteAllText("$d/a.txt", 'alpha')
    Remove-Item "$d/sub/b.txt"; $r = Test-ChecksumFile $d
    Check 'a missing file is detected' ((-not $r.Ok) -and $r.Missing -contains 'sub/b.txt'); [IO.File]::WriteAllText("$d/sub/b.txt", 'beta')
    [IO.File]::WriteAllText("$d/extra.txt", 'x'); $r = Test-ChecksumFile $d
    Check 'an unlisted file is detected' ((-not $r.Ok) -and $r.Extra -contains 'extra.txt'); Remove-Item "$d/extra.txt"
    [IO.File]::AppendAllText("$d/SHA256SUMS.txt", ("{0}  ../outside.txt`n" -f ('0' * 64))); $r = Test-ChecksumFile $d
    Check 'a path escaping the folder is rejected' ((-not $r.Ok) -and ($r.Errors -join ' ') -match 'escapes')
    Write-TextLf "$d/SHA256SUMS.txt" ("{0}  a.txt`n{0}  a.txt`n" -f (Get-Sha256Hex "$d/a.txt")); $r = Test-ChecksumFile $d
    Check 'a duplicate entry is rejected' ((-not $r.Ok) -and ($r.Errors -join ' ') -match 'duplicate')
    Write-TextLf "$d/SHA256SUMS.txt" "not a checksum line`n"; Check 'a malformed line is rejected' (-not (Test-ChecksumFile $d).Ok)
    Remove-Item "$d/SHA256SUMS.txt"; Check 'a missing checksum file is rejected' (-not (Test-ChecksumFile $d).Ok)

    $good = @((T 'id1' 'A'), (T 'id2' 'B' 'Failed'), (T 'id3' 'C' 'NotExecuted'))
    New-Trx "$d/good.trx" $good; $t = Read-Trx "$d/good.trx"
    Check 'a well-formed TRX is valid and its classes resolve' ($t.Valid -and $t.Results.Count -eq 3 -and $t.Results[0].Class -eq $cls) ($t.Errors -join ';')
    New-Trx "$d/trunc.trx" $good -Truncate; Check 'a truncated TRX is invalid' (-not (Read-Trx "$d/trunc.trx").Valid)
    New-Trx "$d/bad-total.trx" $good @{ total = 99 }; Check 'counters that disagree with the results are invalid' (-not (Read-Trx "$d/bad-total.trx").Valid)
    New-Trx "$d/bad-passed.trx" $good @{ passed = 3 }; Check 'an inflated passed counter is invalid' (-not (Read-Trx "$d/bad-passed.trx").Valid)
    New-Trx "$d/bad-exec.trx" $good @{ executed = 3 }; Check 'an executed counter that counts a skipped test is invalid' (-not (Read-Trx "$d/bad-exec.trx").Valid)
    Check 'a real-shaped TRX with a skipped test (notExecuted counter 0, executed = total - skipped) is valid' ((Read-Trx "$d/good.trx").Valid)
    New-Trx "$d/nocounters.trx" $good -NoCounters; Check 'a TRX with no summary is invalid' (-not (Read-Trx "$d/nocounters.trx").Valid)
    New-Trx "$d/empty.trx" @(); Check 'a TRX with no results is invalid' (-not (Read-Trx "$d/empty.trx").Valid)
    [IO.File]::WriteAllText("$d/notxml.trx", 'this is not xml'); Check 'a non-XML file is invalid' (-not (Read-Trx "$d/notxml.trx").Valid)
    [IO.File]::WriteAllText("$d/dtd.trx", '<?xml version="1.0"?><!DOCTYPE x [<!ENTITY a "b">]><TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"/>'); Check 'a TRX with a DTD is refused' (-not (Read-Trx "$d/dtd.trx").Valid)
    Check 'a missing file is invalid' (-not (Read-Trx "$d/none.trx").Valid)
    $coreLib = [object].Assembly.Location
    $code1 = Get-AssemblyCodeHash $coreLib; $code2 = Get-AssemblyCodeHash $coreLib
    Check 'the assembly code hash works under strict mode and is stable (a silent null here once disabled the no-op-edit check)' ($code1 -and $code1 -eq $code2) $script:CodeHashError
    Check 'a file that is not a managed assembly has no code hash' ($null -eq (Get-AssemblyCodeHash (Join-Path $d 'a.txt')))
    New-Trx "$d/aborted.trx" $good -RunOutcome 'Aborted'; Check 'a run that was aborted is invalid whatever its counters say' (-not (Read-Trx "$d/aborted.trx").Valid)
    New-Trx "$d/emptyc.trx" $good -EmptyCounters; Check 'an empty <Counters/> element is invalid' (-not (Read-Trx "$d/emptyc.trx").Valid)
    New-Trx "$d/failedrun.trx" @((T 'f1' 'A' 'Failed')) -RunOutcome 'Failed'; Check 'a run that finished with failures (outcome Failed) is valid' ((Read-Trx "$d/failedrun.trx").Valid)
    foreach ($case in @(
            @('Password="Hunter2"'), @('MSSQL_SA_PASSWORD=Hunter2'), @('SA_PASSWORD: Hunter2'), @('{"Password": "Hunter2"}'), @("Password='Hunter2'"), @('sqlcmd -S x -U sa -P Hunter2 -Q y'), @('Server=a;pwd = Hunter2;Db=b'), @('Login failed; Password=Hunter2;x'))) {
        Check "redaction removes the secret from: $($case[0])" ((Protect-Text $case[0]) -notmatch 'Hunter2')
    }
    foreach ($tail in @('Password=my secret phrase;x', 'Password=abc,def', 'pwd = correct horse battery', 'MSSQL_SA_PASSWORD=a b c')) {
        $red = Protect-Text $tail
        Check "redaction leaves no readable tail of the secret: $tail" (($red -notmatch 'secret|phrase|def|horse|battery|\bb\b|\bc\b|abc,')) $red
    }
    Check 'a JSON password with an escaped quote leaves no readable tail' ((Protect-Text '{"password":"a\"Hunter2"}') -notmatch 'Hunter2')
    foreach ($weird in @('F:\work\prot \ws', 'F:\work\x.\ws', 'F:\work\ws ', 'F:\work\prot::$INDEX_ALLOCATION\ws', 'F:\work\a:b\ws')) { Check "a path segment ending in a space or dot is refused: '$weird'" (-not (Test-LocalDrivePath $weird)) }
    # build-environment risks: hook variables and user ImportBefore/ImportAfter files (restored in finally; nothing here runs a build)
    Check 'premise: the environment running this self-test carries no MSBuild hook variable and no user import file' (-not (Get-BuildEnvironmentRisks).Risky)
    $savedHook = $env:CustomAfterMicrosoftCommonTargets; $savedNuget = $env:NUGET_SELFTEST_PROBE; $savedLad = $env:LOCALAPPDATA
    try {
        $env:CustomAfterMicrosoftCommonTargets = 'F:\x\hook.targets'; $rk = Get-BuildEnvironmentRisks
        Check 'an MSBuild import hook set as an environment variable is a risk, listed by name' ($rk.Risky -and $rk.HookVariables -contains 'CustomAfterMicrosoftCommonTargets' -and ($rk | ConvertTo-Json) -notmatch 'hook.targets')
        Remove-Item Env:CustomAfterMicrosoftCommonTargets
        foreach ($v in 'DirectoryBuildTargetsPath', 'DirectoryBuildPropsPath', 'ImportDirectoryBuildTargets', 'CustomBeforeMicrosoftCommonProps', 'MSBuildExtensionsPath', 'MSBuildSDKsPath', 'MSBuildProjectExtensionsPath', 'BaseIntermediateOutputPath', 'BaseOutputPath') {
            Set-Item -LiteralPath "Env:$v" -Value 'x'; $hit = (Get-BuildEnvironmentRisks).HookVariables -contains $v; Remove-Item -LiteralPath "Env:$v"
            Check "$v is treated as a build hook" $hit
            Check "...and is gone again afterwards (the test must not leave the environment changed): $v" ($null -eq [Environment]::GetEnvironmentVariable($v))
        }
        $env:NUGET_SELFTEST_PROBE = 'secret-token-value'; $rk = Get-BuildEnvironmentRisks
        Check 'other NUGET_/MSBUILD variables are listed by name only (their values can be tokens) and are not a blocking risk' ((-not $rk.Risky) -and $rk.OtherVariables -contains 'NUGET_SELFTEST_PROBE' -and ($rk | ConvertTo-Json) -notmatch 'secret-token-value')
        Remove-Item Env:NUGET_SELFTEST_PROBE
        $fakeLad = Join-Path $d 'localappdata'; New-Item -ItemType Directory "$fakeLad/Microsoft/MSBuild/Current/Microsoft.Common.targets/ImportAfter" | Out-Null
        [IO.File]::WriteAllText("$fakeLad/Microsoft/MSBuild/Current/Microsoft.Common.targets/ImportAfter/x.targets", '<Project/>'); [IO.File]::WriteAllText("$fakeLad/Microsoft/MSBuild/Current/readme.txt", 'not an import')
        $env:LOCALAPPDATA = $fakeLad; $rk = Get-BuildEnvironmentRisks
        Check 'a file in the user MSBuild ImportAfter folder is a risk (a text file elsewhere is not)' ($rk.Risky -and @($rk.UserImportFiles).Count -eq 1 -and $rk.UserImportFiles[0] -like '*ImportAfter*x.targets')
    }
    finally {
        if ($null -eq $savedHook) { Remove-Item Env:CustomAfterMicrosoftCommonTargets -ErrorAction SilentlyContinue } else { $env:CustomAfterMicrosoftCommonTargets = $savedHook }
        if ($null -eq $savedNuget) { Remove-Item Env:NUGET_SELFTEST_PROBE -ErrorAction SilentlyContinue } else { $env:NUGET_SELFTEST_PROBE = $savedNuget }
        $env:LOCALAPPDATA = $savedLad
    }
    Check 'an environment variable with an empty value is not a hook' ((& { $env:CustomAfterMicrosoftCommonTargets = ''; try { -not (Get-BuildEnvironmentRisks).Risky } finally { Remove-Item Env:CustomAfterMicrosoftCommonTargets -ErrorAction SilentlyContinue } }))
    Check 'the environment is back to clean after the build-environment controls' (-not (Get-BuildEnvironmentRisks).Risky -and $null -eq [Environment]::GetEnvironmentVariable('CustomAfterMicrosoftCommonTargets'))
    Check 'redaction leaves ordinary text alone' ((Protect-Text 'Server=127.0.0.1,1433;Database=Apperture_X;User Id=sa') -eq 'Server=127.0.0.1,1433;Database=Apperture_X;User Id=sa')
    $ci2 = Get-SafeConnectionInfo 'Server=a,1433;Database=Apperture_X;Data Source=a,14333;Initial Catalog=Apperture'
    Check 'a repeated keyword reports the LAST value (as SqlClient does) and marks the string ambiguous' ($ci2.Port -eq 14333 -and $ci2.Catalog -eq 'Apperture' -and $ci2.Ambiguous)
    Check 'a space after the comma still yields the port' ((Get-SafeConnectionInfo 'Server=localhost, 14333;Database=Apperture_X').Port -eq 14333)
    Check 'a named pipe is flagged' ((Get-SafeConnectionInfo 'Server=np:\\.\pipe\LOCALDB#ABC\tsql\query;Database=Apperture_X').NamedPipeOrLocalDb)
    Check 'LocalDB is flagged' ((Get-SafeConnectionInfo 'Server=(localdb)\MSSQLLocalDB;Database=Apperture_X').NamedPipeOrLocalDb)
    Check 'an ordinary server is not ambiguous and not a pipe' ((-not (Get-SafeConnectionInfo 'Server=127.0.0.1,1433;Database=Apperture_X').Ambiguous) -and (-not (Get-SafeConnectionInfo 'Server=127.0.0.1,1433;Database=Apperture_X').NamedPipeOrLocalDb))
    $driveLetter = $WorkRoot.Substring(0, 1)
    Check 'a plain drive-letter path is accepted' (Test-LocalDrivePath "${driveLetter}:\work\x")
    foreach ($bad in @("\\localhost\${driveLetter}`$\work\x", "\\?\${driveLetter}:\work\x", "\\.\${driveLetter}:\work\x", "${driveLetter}:\PROGRA~1\x", 'relative\path', '', '//server/share/x')) {
        Check "a non-plain path form is refused: $bad" (-not (Test-LocalDrivePath $bad))
    }
    $gg = $null; try { [void](Invoke-GitGrep $repoRoot @('-l', 'x', '--no-such-option-at-all', '--', 'tests')); $gg = 'no throw' } catch { $gg = 'threw' }
    Check 'a failed git grep throws instead of looking like "no match"' ($gg -eq 'threw')
    Check 'a git grep with no match is not an error' ((Invoke-GitGrep $repoRoot @('-l', 'zzzNoSuchTextAnywhere9876', '--', 'scripts/README.md')).Exit -eq 1)
    Check 'passwords are redacted from text' ((Protect-Text 'Login failed. Password=Sup3rSecret;x pwd = abc;y') -notmatch 'Sup3rSecret|abc')
    $ci = Get-SafeConnectionInfo 'Server=tcp:127.0.0.1,1433;Database=Apperture_X;User Id=sa;Password=Sup3rSecret'
    Check 'a connection string is reduced to host, port, catalog and user' ($ci.Host -eq '127.0.0.1' -and $ci.Port -eq 1433 -and $ci.Catalog -eq 'Apperture_X' -and $ci.User -eq 'sa' -and $ci.HasPassword -and ($ci | ConvertTo-Json) -notmatch 'Sup3rSecret')
}

# ====================================================================================================================== trx
if (Want-Section 'trx') {
    Write-Host 'trx: Compare-Trx'
    $d = Join-Path $WorkRoot 'trx'; New-Item -ItemType Directory $d | Out-Null
    $ct = Join-Path $val 'Compare-Trx.ps1'
    $base = @((T 'i1' 'One(rcsi: False)'), (T 'i2' 'One(rcsi: True)'), (T 'i3' 'Two'), (T 'i4' 'Row(x: 1)'), (T 'i5' 'Row(x: 2)'), (T 'i6' 'Same'), (T 'i7' 'Same'), (T 'i8' 'Gone'), (T 'i9' 'Slow(rcsi: False)' 'Passed' '00:00:02.0000000'))
    $cand = @((T 'i1' 'One(rcsi: False)'), (T 'i2' 'One(rcsi: True)' 'Failed'), (T 'i3' 'Two' 'NotExecuted'), (T 'i4' 'Row(x: 1)'), (T 'j5' 'Row(x: 3)'), (T 'i6' 'Same'), (T 'i7' 'Same'), (T 'n1' 'New(rcsi: False)'), (T 'n2' 'New(rcsi: True)'), (T 'i9' 'Slow(rcsi: False)' 'Passed' '00:00:00.0100000'))
    New-Trx "$d/base.trx" $base; New-Trx "$d/cand.trx" $cand
    $r = Invoke-Tool $ct @('-Baseline', "$d/base.trx", '-Candidate', "$d/cand.trx", '-Json', "$d/out.json", '-Quiet')
    $j = Get-Content "$d/out.json" -Raw | ConvertFrom-Json
    Check 'added tests are listed, including a changed theory row' (($j.Added -contains "$cls.New(rcsi: False)") -and ($j.Added -contains "$cls.Row(x: 3)") -and $j.Added.Count -eq 3)
    Check 'removed tests are listed, including a changed theory row' (($j.Removed -contains "$cls.Row(x: 2)") -and ($j.Removed -contains "$cls.Gone") -and $j.Removed.Count -eq 2)
    Check 'outcome changes and not-executed tests are reported' ($j.OutcomeChanged.Count -eq 2 -and $j.CandidateNotExecuted.Count -eq 1 -and $j.CandidateFailed.Count -eq 1)
    Check 'the isolation-mode split of what was added is counted' ($j.AddedRcsi.False -eq 1 -and $j.AddedRcsi.True -eq 1 -and $j.AddedRcsi.none -eq 1)
    Check 'tests sharing a display name are compared by id, not collapsed' ($j.DuplicateNamesCandidate -eq 1 -and $j.CandidateCounts.Total -eq 10 -and $j.MatchedById -eq 7)
    Check 'a SqlServerTests case that became fast is a soft-skip suspect' ($j.SoftSkipSuspects.Count -eq 1 -and $j.SoftSkipSuspects[0] -like '*Slow*')
    Check 'without -Strict a comparison with findings still exits 0' ($r.Exit -eq 0)
    Check '-Strict turns regressions into findings (exit 1)' ((Invoke-Tool $ct @('-Baseline', "$d/base.trx", '-Candidate', "$d/cand.trx", '-Strict', '-Quiet')).Exit -eq 1)
    Check 'an unmet -ExpectAdded is a finding' ((Invoke-Tool $ct @('-Baseline', "$d/base.trx", '-Candidate', "$d/cand.trx", '-ExpectAdded', '36', '-Quiet')).Exit -eq 1)
    Check 'a met -ExpectAdded is not' ((Invoke-Tool $ct @('-Baseline', "$d/base.trx", '-Candidate', "$d/cand.trx", '-ExpectAdded', '3', '-Quiet')).Exit -eq 0)
    Check 'an unmet -ExpectRcsiAdded is a finding' ((Invoke-Tool $ct @('-Baseline', "$d/base.trx", '-Candidate', "$d/cand.trx", '-ExpectRcsiAdded', 'True=18,False=18', '-Quiet')).Exit -eq 1)
    Check 'an unexpected failing test is a finding' ((Invoke-Tool $ct @('-Baseline', "$d/base.trx", '-Candidate', "$d/cand.trx", '-ExpectFailed', 'NoSuchTest', '-Quiet')).Exit -eq 1)
    Check 'the same file on both sides is an input error' ((Invoke-Tool $ct @('-Baseline', "$d/base.trx", '-Candidate', "$d/base.trx", '-Quiet')).Exit -eq 2)
    Copy-Item "$d/base.trx" "$d/base-copy.trx"; Check 'identical content under another name is an input error' ((Invoke-Tool $ct @('-Baseline', "$d/base.trx", '-Candidate', "$d/base-copy.trx", '-Quiet')).Exit -eq 2)
    New-Trx "$d/trunc.trx" $cand -Truncate; Check 'a truncated candidate is an input error, not a comparison' ((Invoke-Tool $ct @('-Baseline', "$d/base.trx", '-Candidate', "$d/trunc.trx", '-Quiet')).Exit -eq 2)
    New-Trx "$d/lie.trx" $cand @{ failed = 0; passed = 10 }; Check 'a candidate whose counters hide a failure is an input error' ((Invoke-Tool $ct @('-Baseline', "$d/base.trx", '-Candidate', "$d/lie.trx", '-Quiet')).Exit -eq 2)
    Check 'a missing file is an input error' ((Invoke-Tool $ct @('-Baseline', "$d/base.trx", '-Candidate', "$d/nope.trx", '-Quiet')).Exit -eq 2)
    New-Item -ItemType Directory "$d/emptydir" | Out-Null; Check 'a folder with no TRX is an input error' ((Invoke-Tool $ct @('-Baseline', "$d/base.trx", '-Candidate', "$d/emptydir", '-Quiet')).Exit -eq 2)
    $renamed = @($base | ForEach-Object { $x = $_.Clone(); $x.Id = "r$($x.Id)"; $x })
    New-Trx "$d/ids-changed.trx" $renamed; $r = Invoke-Tool $ct @('-Baseline', "$d/base.trx", '-Candidate', "$d/ids-changed.trx", '-Json', "$d/r.json", '-Quiet'); $j = Get-Content "$d/r.json" -Raw | ConvertFrom-Json
    Check 'when ids all change, identical names still match (by name) and nothing is reported added or removed' ($j.MatchedByName -eq 9 -and $j.Added.Count -eq 0 -and $j.Removed.Count -eq 0)
}

# ====================================================================================================================== select
if (Want-Section 'select') {
    Write-Host 'select: Select-Tests'
    $fx = Join-Path $WorkRoot 'selrepo'; New-Item -ItemType Directory "$fx/src/App", "$fx/tests/Apperture.UnitTests", "$fx/tests/Apperture.IntegrationTests", "$fx/docs" | Out-Null
    function W([string]$p, [string]$t) { [IO.File]::WriteAllText((Join-Path $fx $p), $t) }
    W 'src/App/Widget.cs' "public class WidgetService { }`n"; W 'src/App/Orphan.cs' "public class OrphanThing { }`n"; W 'src/App/Program.cs' "public class ProgramEntry { }`n"
    W 'tests/Apperture.UnitTests/WidgetServiceTests.cs' "public class WidgetServiceTests { [Fact] public void A() { var s = new WidgetService(); } }`n"
    W 'tests/Apperture.UnitTests/OtherTests.cs' "public class OtherTests { [Fact] public void A() { var b = new WidgetBuilder(); } }`n"
    W 'tests/Apperture.UnitTests/OrphanUserTests.cs' "public class OrphanUserTests { [Fact] public void A() { var o = new OrphanThing(); } }`n"
    New-Item -ItemType Directory "$fx/src/Other" | Out-Null; W 'src/Other/Program.cs' "public class OtherEntry { }`n"
    W 'tests/Apperture.UnitTests/CompositionTests.cs' "public class AppCompositionTests { [Fact] public void A() { } }`n"
    W 'tests/Apperture.UnitTests/Helpers.cs' "public class WidgetBuilder { public WidgetService Make() => new WidgetService(); }`n"
    W 'tests/Apperture.IntegrationTests/WidgetHttpTests.cs' "public class WidgetHttpTests { [Fact] public void A() { var s = new WidgetService(); } }`n"
    W 'tests/Apperture.IntegrationTests/DupTests.cs' "public class DupTests { [Fact] public void A() { } }`n"; W 'tests/Apperture.UnitTests/DupTests.cs' "public class DupTests { [Fact] public void A() { } }`n"
    W 'tests/Apperture.IntegrationTests/SqlServerTestConfiguration.cs' "public class SqlServerTestConfiguration { }`n"; W 'docs/note.md' "x`n"
    W 'tests/Apperture.UnitTests/DocsReaderTests.cs' "public class DocsReaderTests { [Fact] public void A() { var t = System.IO.File.ReadAllText(`"docs/note.md`"); } }`n"
    $man = [ordered]@{ version = 1; projects = [ordered]@{ UnitTests = 'tests/Apperture.UnitTests'; IntegrationTests = 'tests/Apperture.IntegrationTests' }; documentation = @('docs/**', '**/*.md'); ignore = @('artifacts/**')
        everything = @{ reason = 'shared fixture'; when = @('tests/Apperture.IntegrationTests/SqlServerTestConfiguration.cs') }
        rules = @(
            @{ id = 'widget'; kind = 'mapped'; paths = @('src/App/Widget.cs'); tests = @('WidgetServiceTests', 'WidgetHttpTests'); reason = 'r' },
            @{ id = 'missing'; kind = 'mapped'; paths = @('src/App/Program.cs'); tests = @('NoSuchTests', 'DupTests'); reason = 'r' },
            @{ id = 'xc-comp'; kind = 'cross-cutting'; paths = @('src/**/Program.cs'); tests = @('*CompositionTests', '*NothingMatchesThis'); reason = 'r' },
            @{ id = 'helper'; kind = 'mapped'; paths = @('src/App/Helper.cs'); tests = @('WidgetBuilder'); reason = 'a class that holds no tests' }) }
    W 'tests/test-manifest.json' ($man | ConvertTo-Json -Depth 6)
    $g = { param([string[]]$a) & git -C $fx @a 2>&1 | Out-Null }
    & $g @('init', '-q'); & $g @('config', 'user.email', 't@example.invalid'); & $g @('config', 'user.name', 't'); & $g @('add', '-A'); & $g @('commit', '-q', '-m', 'base')
    $base = (& git -C $fx rev-parse HEAD)
    $st = Join-Path $val 'Select-Tests.ps1'
    $sel = { param([string[]]$files, [string[]]$extra = @()) Remove-Item "$WorkRoot/sel.json" -ErrorAction SilentlyContinue; $r = Invoke-Tool $st (@('-Repo', $fx, '-Files') + $files + @('-TreeRev', 'HEAD', '-Json', "$WorkRoot/sel.json", '-Quiet') + $extra); $r | Add-Member Json (Get-Content "$WorkRoot/sel.json" -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json -ErrorAction SilentlyContinue) -PassThru }
    $r = & $sel @('src/App/Widget.cs')
    Check 'a mapped production file selects its rule classes in the right projects' ((@($r.Json.Selected | Where-Object { $_.Class -eq 'WidgetServiceTests' -and $_.Project -eq 'UnitTests' }).Count -eq 1) -and (@($r.Json.Selected | Where-Object { $_.Class -eq 'WidgetHttpTests' -and $_.Project -eq 'IntegrationTests' }).Count -eq 1))
    $r = & $sel @('src/App/Orphan.cs')
    Check 'a production file no rule claims is reported UNMAPPED' (($r.Json.Unmapped -contains 'src/App/Orphan.cs'))
    Check 'a test that names the changed type is a CANDIDATE: listed, not selected' ((@($r.Json.Candidates | Where-Object Class -eq 'OrphanUserTests').Count -eq 1) -and @($r.Json.Selected | Where-Object Class -eq 'OrphanUserTests').Count -eq 0)
    $r = & $sel @('src/App/Orphan.cs') @('-IncludeCandidates'); Check '-IncludeCandidates puts the candidate into the selection' (@($r.Json.Selected | Where-Object Class -eq 'OrphanUserTests').Count -eq 1)
    Check 'under -Strict an UNMAPPED file is a finding (exit 1)' ((& $sel @('src/App/Orphan.cs') @('-Strict')).Exit -eq 1)
    $r = & $sel @('docs/note.md', 'src/App/Widget.cs')
    Check 'a changed document is counted, not mapped or flagged as unmapped, and the test that reads documentation is listed' (($r.Json.DocumentationChanged.Count -eq 1) -and $r.Json.Unmapped.Count -eq 0 -and (@($r.Json.DocumentationReaders | Where-Object Class -eq 'DocsReaderTests').Count -eq 1))
    Check 'under -Strict a document change with a documentation-reading test is a finding, not silence' ((& $sel @('docs/note.md') @('-Strict')).Exit -eq 1)
    $r = & $sel @('artifacts/x/log.txt'); Check 'an ignored path is counted as ignored' ($r.Json.Ignored -eq 1)
    $r = & $sel @('src/App/Program.cs')
    Check 'a manifest class that does not exist is MISSING' ($r.Json.Missing -match 'NoSuchTests')
    Check 'a class declared in two projects is AMBIGUOUS and selected in both' (($r.Json.Ambiguous -match 'DupTests') -and @($r.Json.Selected | Where-Object Class -eq 'DupTests').Count -eq 2)
    Check 'a wildcard that matches nothing is reported' ($r.Json.WildcardsMatchingNothing -match 'NothingMatchesThis')
    Check 'a wildcard that matches selects the matching test class' (@($r.Json.Selected | Where-Object Class -eq 'AppCompositionTests').Count -eq 1)
    $r = & $sel @('src/Other/Program.cs')
    Check 'a cross-cutting rule adds its tests but does not claim the file (it stays UNMAPPED)' (($r.Json.Unmapped -contains 'src/Other/Program.cs') -and (@($r.Json.Selected | Where-Object Class -eq 'AppCompositionTests').Count -eq 1))
    $r = & $sel @('src/App/Helper.cs'); Check 'a rule class that holds no tests is MISSING, not selected' (($r.Json.Missing -match 'holds no tests') -and @($r.Json.Selected).Count -eq 0)
    $r = & $sel @('tests/Apperture.UnitTests/OtherTests.cs'); Check 'a changed test file selects its classes directly' (@($r.Json.Selected | Where-Object { $_.Class -eq 'OtherTests' -and $_.Why[0] -like 'DIRECT*' }).Count -eq 1)
    $r = & $sel @('tests/Apperture.UnitTests/Helpers.cs'); Check 'a changed test helper selects nothing itself and lists the tests that use it as candidates' ((@($r.Json.Selected).Count -eq 0) -and ($r.Json.SupportFilesChanged -contains 'tests/Apperture.UnitTests/Helpers.cs') -and (@($r.Json.Candidates | Where-Object Class -eq 'OtherTests').Count -eq 1))
    $r = & $sel @('tests/Apperture.IntegrationTests/SqlServerTestConfiguration.cs'); Check 'a shared fixture change says a full run is required' ($r.Json.FullRunRequired.Count -eq 1); Check 'and -Strict makes that a finding' ((& $sel @('tests/Apperture.IntegrationTests/SqlServerTestConfiguration.cs') @('-Strict')).Exit -eq 1)
    $r = & $sel @('src/App/Widget.cs') @('-CurrentFilter', 'FullyQualifiedName~.WidgetServiceTests.', '-CurrentFilterProject', 'UnitTests')
    Check 'a filter that already holds the selected class reports nothing MISSED' (@($r.Json.MissedByCurrentFilter).Count -eq 0 -and $r.Json.CurrentFilterClasses -contains 'WidgetServiceTests')
    $r = & $sel @('src/App/Widget.cs') @('-CurrentFilter', 'FullyQualifiedName~.SomethingElse.', '-CurrentFilterProject', 'UnitTests')
    Check 'a filter holding other classes reports the selected one MISSED and the other EXTRA' (($r.Json.MissedByCurrentFilter -contains 'WidgetServiceTests') -and ($r.Json.InCurrentFilterOnly -contains 'SomethingElse'))
    Check 'an unknown -CurrentFilterProject is an input error, not an empty comparison' ((& $sel @('src/App/Widget.cs') @('-CurrentFilter', 'x', '-CurrentFilterProject', 'NoSuchProject')).Exit -eq 2)
    Check 'a project with no selected class is an input error, not a clean comparison' ((& $sel @('tests/Apperture.UnitTests/OtherTests.cs') @('-CurrentFilter', 'FullyQualifiedName~.X.', '-CurrentFilterProject', 'IntegrationTests')).Exit -eq 2)
    Check 'a filter with a negated clause is refused, not counted as including the class' ((& $sel @('src/App/Widget.cs') @('-CurrentFilter', 'FullyQualifiedName!~.WidgetServiceTests.', '-CurrentFilterProject', 'UnitTests')).Exit -eq 2)
    Check 'a filter combining clauses with & is refused' ((& $sel @('src/App/Widget.cs') @('-CurrentFilter', 'FullyQualifiedName~.A.&FullyQualifiedName~.B.', '-CurrentFilterProject', 'UnitTests')).Exit -eq 2)
    Check 'a filter on another property is refused' ((& $sel @('src/App/Widget.cs') @('-CurrentFilter', 'Category=Fast', '-CurrentFilterProject', 'UnitTests')).Exit -eq 2)
    # Working-tree mode (no -TreeRev) is the default for -Files and -Working. An earlier version passed --untracked after the pattern, git failed,
    # and the empty answer was read as "no test files": every test file became a support file and -Strict exited 0.
    $wt = { param([string[]]$files, [string[]]$extra = @()) Remove-Item "$WorkRoot/selwt.json" -ErrorAction SilentlyContinue; $r = Invoke-Tool $st (@('-Repo', $fx, '-Files') + $files + @('-Json', "$WorkRoot/selwt.json", '-Quiet') + $extra); $r | Add-Member Json (Get-Content "$WorkRoot/selwt.json" -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json -ErrorAction SilentlyContinue) -PassThru }
    $r = & $wt @('tests/Apperture.UnitTests/OtherTests.cs')
    Check 'working-tree mode: a changed test file selects its class (it was a "support file" before the fix)' (@($r.Json.Selected | Where-Object { $_.Class -eq 'OtherTests' -and $_.Why[0] -like 'DIRECT*' }).Count -eq 1 -and @($r.Json.SupportFilesChanged).Count -eq 0) $r.Out
    $r = & $wt @('src/App/Widget.cs'); Check 'working-tree mode: a production file selects its rule classes' (@($r.Json.Selected | Where-Object Class -eq 'WidgetServiceTests').Count -eq 1 -and $r.Json.Missing.Count -eq 0)
    [IO.File]::WriteAllText("$fx/tests/Apperture.UnitTests/NewUntrackedTests.cs", "public class NewUntrackedTests { [Fact] public void A() { } }`n")
    $r = & $wt @('tests/Apperture.UnitTests/NewUntrackedTests.cs'); Check 'working-tree mode sees an untracked new test file' (@($r.Json.Selected | Where-Object Class -eq 'NewUntrackedTests').Count -eq 1)
    Remove-Item "$fx/tests/Apperture.UnitTests/NewUntrackedTests.cs"
    $r = Invoke-Tool $st @('-Repo', $WorkRoot, '-Files', 'src/App/Widget.cs', '-Quiet'); Check 'a directory that is not a Git work tree is an input error' ($r.Exit -eq 2)
    # custom test attributes ([BrowserFact] and friends) mark test classes too; a class using only those was a "support file" before
    W 'tests/Apperture.UnitTests/CustomAttributeTests.cs' "public class BrowserLikeTests { [WindowsOnlyFact] public void A() { } [BrowserTheory(Skip = `"x`")] public void B(int n) { } }`n"
    $r = & $wt @('tests/Apperture.UnitTests/CustomAttributeTests.cs'); Check 'a class whose tests use custom attributes ([WindowsOnlyFact], [BrowserTheory]) is a test class: selected DIRECT, not a support file' (@($r.Json.Selected | Where-Object { $_.Class -eq 'BrowserLikeTests' -and $_.Why[0] -like 'DIRECT*' }).Count -eq 1 -and @($r.Json.SupportFilesChanged).Count -eq 0) $r.Out
    W 'tests/Apperture.UnitTests/TraitFirstTests.cs' "public class TraitFirstTests { [Trait(`"k`", `"v`"), Fact] public void A() { } }`n"
    $r = & $wt @('tests/Apperture.UnitTests/TraitFirstTests.cs'); Check 'a test whose attribute list starts with another attribute ([Trait(..), Fact]) is still a test class' (@($r.Json.Selected | Where-Object { $_.Class -eq 'TraitFirstTests' }).Count -eq 1 -and @($r.Json.SupportFilesChanged).Count -eq 0) $r.Out
    Remove-Item "$fx/tests/Apperture.UnitTests/TraitFirstTests.cs"
    # a qualified filter clause covers a class only if it is that class's own qualified name
    W 'tests/Apperture.UnitTests/NsA.cs' "namespace Fx.A;`npublic class SameNameTests { [Fact] public void A() { } }`n"
    $r = & $wt @('tests/Apperture.UnitTests/NsA.cs') @('-CurrentFilter', 'FullyQualifiedName~Other.Ns.SameNameTests', '-CurrentFilterProject', 'UnitTests')
    Check 'a namespace-qualified filter clause for a different namespace does NOT cover the class (it is MISSED)' (($r.Json.MissedByCurrentFilter -contains 'SameNameTests') -and ($r.Json.InCurrentFilterOnly -contains 'Other.Ns.SameNameTests')) $r.Out
    $r = & $wt @('tests/Apperture.UnitTests/NsA.cs') @('-CurrentFilter', 'FullyQualifiedName~Fx.A.SameNameTests', '-CurrentFilterProject', 'UnitTests')
    Check 'the class''s own qualified name covers it' (@($r.Json.MissedByCurrentFilter).Count -eq 0 -and @($r.Json.InCurrentFilterOnly).Count -eq 0) $r.Out
    # The same simple name in two namespaces of one project shares the selection key. Covering only one of them must not read as covered
    # (the real tree has SchedulesControllerTests in ControlPlane and Governance; a filter naming the Governance one reported nothing missed).
    $r = & $wt @('tests/Apperture.UnitTests/NsA.cs') @('-CurrentFilter', 'FullyQualifiedName~Fx.A.SameNameTests', '-CurrentFilterProject', 'UnitTests')
    Check 'premise: with one namespace there is no ambiguity' (@($r.Json.Ambiguous).Count -eq 0 -and @($r.Json.Selected[0].Qualified).Count -eq 1) $r.Out
    W 'tests/Apperture.UnitTests/NsB.cs' "namespace Fx.B;`npublic class SameNameTests { [Fact] public void B() { } }`n"
    $r = & $wt @('tests/Apperture.UnitTests/NsA.cs') @('-CurrentFilter', 'FullyQualifiedName~Fx.B.SameNameTests', '-CurrentFilterProject', 'UnitTests', '-Strict')
    Check 'a simple name in two namespaces is reported AMBIGUOUS with both identities, and is a finding under -Strict' (($r.Json.Ambiguous -match 'SameNameTests.*2 namespaces') -and @($r.Json.Selected[0].Qualified).Count -eq 2 -and $r.Exit -eq 1) $r.Out
    Check 'a filter naming only the OTHER namespace misses the class (it was reported covered before the fix)' (($r.Json.MissedByCurrentFilter -contains 'SameNameTests') -and ($r.Json.InCurrentFilterOnly -notcontains 'Fx.B.SameNameTests')) $r.Out
    $r = & $wt @('tests/Apperture.UnitTests/NsA.cs') @('-CurrentFilter', 'FullyQualifiedName~Fx.A.SameNameTests', '-CurrentFilterProject', 'UnitTests')
    Check 'a filter naming only the CHANGED namespace still misses the class: the other identity is not covered' ($r.Json.MissedByCurrentFilter -contains 'SameNameTests') $r.Out
    $r = & $wt @('tests/Apperture.UnitTests/NsA.cs') @('-CurrentFilter', 'FullyQualifiedName~Fx.A.SameNameTests|FullyQualifiedName~Fx.B.SameNameTests', '-CurrentFilterProject', 'UnitTests')
    Check 'counter-control: a filter naming both qualified identities covers the class' (@($r.Json.MissedByCurrentFilter).Count -eq 0 -and @($r.Json.InCurrentFilterOnly).Count -eq 0) $r.Out
    $r = & $wt @('tests/Apperture.UnitTests/NsA.cs') @('-CurrentFilter', 'FullyQualifiedName~SameNameTests', '-CurrentFilterProject', 'UnitTests')
    Check 'counter-control: the bare simple name covers every identity' (@($r.Json.MissedByCurrentFilter).Count -eq 0) $r.Out
    W 'tests/Apperture.UnitTests/PartA.cs' "namespace Fx.P;`npublic partial class PartTests { [Fact] public void A() { } }`n"; W 'tests/Apperture.UnitTests/PartA.More.cs' "namespace Fx.P;`npublic partial class PartTests { [Fact] public void B() { } }`n"
    $r = & $wt @('tests/Apperture.UnitTests/PartA.cs', 'tests/Apperture.UnitTests/PartA.More.cs')
    Check 'counter-control: partial parts of one class in one namespace are ONE identity, not an ambiguity' (@($r.Json.Ambiguous).Count -eq 0 -and @($r.Json.Selected | Where-Object Class -eq 'PartTests').Count -eq 1 -and @(($r.Json.Selected | Where-Object Class -eq 'PartTests').Qualified).Count -eq 1) $r.Out
    Remove-Item "$fx/tests/Apperture.UnitTests/NsB.cs", "$fx/tests/Apperture.UnitTests/PartA.cs", "$fx/tests/Apperture.UnitTests/PartA.More.cs"
    Remove-Item "$fx/tests/Apperture.UnitTests/CustomAttributeTests.cs", "$fx/tests/Apperture.UnitTests/NsA.cs"
    # a deleted production file still triggers its rules and can be UNMAPPED
    & $g @('rm', '-q', 'src/App/Widget.cs', 'src/App/Orphan.cs'); & $g @('commit', '-q', '-m', 'delete two production files')
    Remove-Item "$WorkRoot/seldel.json" -ErrorAction SilentlyContinue
    $r = Invoke-Tool $st @('-Repo', $fx, '-Base', $base, '-Head', 'HEAD', '-TreeRev', 'HEAD', '-Json', "$WorkRoot/seldel.json", '-Quiet'); $jd = Get-Content "$WorkRoot/seldel.json" -Raw | ConvertFrom-Json
    Check 'a deleted production file still triggers its rule (the classes are selected)' (@($jd.Selected | Where-Object Class -eq 'WidgetServiceTests').Count -eq 1)
    Check 'a deleted production file that no rule claims is UNMAPPED' ($jd.Unmapped -contains 'src/App/Orphan.cs')
    $bad = Join-Path $WorkRoot 'bad-manifest.json'; [IO.File]::WriteAllText($bad, '{ not json'); Check 'a manifest that is not JSON is an input error' ((& $sel @('src/App/Widget.cs') @('-Manifest', $bad)).Exit -eq 2)
    W 'tests/test-manifest.json' '{"version":1}'; Check 'a manifest missing sections is an input error' ((& $sel @('src/App/Widget.cs')).Exit -eq 2)
}

# ====================================================================================================================== replay
if (Want-Section 'replay') {
    Write-Host 'replay: Invoke-Mutation -ReplaySummary'
    $d = Join-Path $WorkRoot 'replay'; New-Item -ItemType Directory $d | Out-Null
    $im = Join-Path $val 'Invoke-Mutation.ps1'
    function Block([string]$Id, [string]$Unit, [string]$Sql, [string]$Restored = 'True') { "=== MUTATION $Id (src/X.cs)`n$(if ($Unit) { "unit: $Unit`n" })$(if ($Sql) { "sql:  $Sql`n" })restored: $Restored`n" }
    $ok = 'Failed!  - Failed:     2, Passed:    94, Skipped:     0, Total:    96, Duration: 3 ms - U.dll (net10.0)'
    $passLine = 'Passed!  - Failed:     0, Passed:    96, Skipped:     0, Total:    96, Duration: 3 ms - U.dll (net10.0)'
    $sqlFail = 'Failed!  - Failed:     4, Passed:    32, Skipped:     0, Total:    36, Duration: 4 m 1 s - I.dll (net10.0)'
    $run = { param($name, $text, [string[]]$extra = @()) [IO.File]::WriteAllText("$d/$name", $text); $r = Invoke-Tool $im (@('-ReplaySummary', "$d/$name", '-Out', "$d/out-$name") + $extra + @('-PassThru')); $r }
    $allGood = (Block 'M1' $ok '') + (Block 'M2' $passLine $sqlFail) + (Block 'M3' $ok $sqlFail) + "MUTATIONS-ALLDONE`n"
    $r = & $run 'good.txt' $allGood '-ExpectCaught', '3', '-ExpectEscaped', '0', '-ExpectCount', '3'
    Check 'caught-by-either-layer mutations are caught (a unit pass with an SQL failure counts)' ($r.Exit -eq 0) $r.Out
    $r = & $run 'escaped.txt' ((Block 'M1' $ok '') + (Block 'M2' $passLine '') + "MUTATIONS-ALLDONE`n")
    Check 'a mutation that passes every layer is ESCAPED and fails the run' (($r.Exit -eq 1) -and $r.Out -match 'M2\s+Escaped')
    $r = & $run 'notrestored.txt' ((Block 'M1' $ok '' 'False') + "MUTATIONS-ALLDONE`n"); Check 'a mutation whose source was not restored is invalid' (($r.Exit -eq 1) -and $r.Out -match 'Invalid')
    $r = & $run 'norestoreline.txt' ("=== MUTATION M1 (src/X.cs)`nunit: $ok`n" + "MUTATIONS-ALLDONE`n"); Check 'a block with no restore line is invalid' (($r.Exit -eq 1) -and $r.Out -match 'no restore line')
    $r = & $run 'trunc.txt' ((Block 'M1' $ok '') + (Block 'M2' $ok '')); Check 'a summary with no completion marker is a finding' (($r.Exit -eq 1) -and $r.Out -match 'completion marker')
    $r = & $run 'notests.txt' ((Block 'M1' '' '') + "MUTATIONS-ALLDONE`n"); Check 'a block that ran no test layer is invalid, not caught' (($r.Exit -eq 1) -and $r.Out -match 'no test layer ran')
    $r = & $run 'zero.txt' ((Block 'M1' 'Passed!  - Failed:     0, Passed:     0, Skipped:     0, Total:     0, Duration: 3 ms - U.dll (net10.0)' '') + "MUTATIONS-ALLDONE`n"); Check 'a layer that executed no test is invalid' (($r.Exit -eq 1) -and $r.Out -match 'no test executed')
    $r = & $run 'inconsistent.txt' ((Block 'M1' 'Failed!  - Failed:     2, Passed:    90, Skipped:     0, Total:    96, Duration: 3 ms - U.dll (net10.0)' '') + "MUTATIONS-ALLDONE`n"); Check 'counts that do not add up are invalid' (($r.Exit -eq 1) -and $r.Out -match 'inconsistent')
    $r = & $run 'verdict.txt' ((Block 'M1' 'Failed!  - Failed:     0, Passed:    96, Skipped:     0, Total:    96, Duration: 3 ms - U.dll (net10.0)' '') + "MUTATIONS-ALLDONE`n"); Check 'a Failed! verdict with zero failures is invalid' (($r.Exit -eq 1) -and $r.Out -match 'disagrees')
    $r = & $run 'buildfail.txt' ("=== MUTATION M1 (src/X.cs)`nBUILD-FAILED unit (exit 1)`nrestored: True`nMUTATIONS-ALLDONE`n"); Check 'a build failure is invalid, not a catch' (($r.Exit -eq 1) -and $r.Out -match 'build failed')
    $r = & $run 'good2.txt' $allGood '-ExpectCaught', '4'; Check 'an unmet -ExpectCaught is a finding' ($r.Exit -eq 1)
    $r = & $run 'good3.txt' $allGood '-ExpectCount', '16'; Check 'an unmet -ExpectCount is a finding' ($r.Exit -eq 1)
    $exp = @{ mutations = @(@{ id = 'M1'; unit = 2; sql = 0; outcome = 'Caught' }, @{ id = 'M2'; unit = 0; sql = 4; outcome = 'Caught' }, @{ id = 'M3'; unit = 2; sql = 4; outcome = 'Caught' }) }
    [IO.File]::WriteAllText("$d/exp.json", ($exp | ConvertTo-Json -Depth 5))
    $r = & $run 'good4.txt' $allGood '-ExpectedPerMutation', "$d/exp.json"; Check 'recorded per-mutation failure counts that match pass' ($r.Exit -eq 0) $r.Out
    $exp.mutations[0].unit = 5; [IO.File]::WriteAllText("$d/exp2.json", ($exp | ConvertTo-Json -Depth 5))
    $r = & $run 'good5.txt' $allGood '-ExpectedPerMutation', "$d/exp2.json"; Check 'a recorded failure count that differs is a finding' (($r.Exit -eq 1) -and $r.Out -match 'recorded 5')
    $exp.mutations[0].unit = 2; $exp.mutations = @($exp.mutations[0], $exp.mutations[1]); [IO.File]::WriteAllText("$d/exp3.json", ($exp | ConvertTo-Json -Depth 5))
    $r = & $run 'good6.txt' $allGood '-ExpectedPerMutation', "$d/exp3.json"; Check 'a mutation in the summary but not in the record is a finding' (($r.Exit -eq 1) -and $r.Out -match 'not in the record')
    $r = Invoke-Tool $im @('-ReplaySummary', "$d/missing.txt"); Check 'a missing summary file is refused (exit 2)' ($r.Exit -eq 2)
}

# ====================================================================================================================== sql
if (Want-Section 'sql') {
    Write-Host 'sql: Get-SqlEvidence with a fake docker'
    $d = Join-Path $WorkRoot 'sql'; New-Item -ItemType Directory $d | Out-Null
    $ge = Join-Path $val 'Get-SqlEvidence.ps1'; $secret = 'Zx9-NotARealSecret'
    $fake = New-FakeDocker $d
    $cmd = $fake  # called in-process by the tool; a .cmd wrapper would cut the multi-line query at the first newline
    $conn = "Server=127.0.0.1,1433;Database=Apperture_SelfTest;User Id=sa;Password=$secret;TrustServerCertificate=True"
    $envOk = @{ APPERTURE_TEST_SQL_CONNECTION_STRING = $conn }
    $r = Invoke-Tool $ge @('-Mode', 'Markers', '-DockerCommand', $cmd, '-ProcessEnvironmentOnly', '-Repo', $repoRoot, '-Json', "$d/m.json") $envOk
    $j = Get-Content "$d/m.json" -Raw | ConvertFrom-Json -ErrorAction SilentlyContinue
    Check 'markers are collected per catalog' (($r.Exit -eq 0) -and $j.CatalogCount -eq 2 -and $j.TotalUserUpdates -eq 100) $r.Out
    $argsLog = Get-Content "$d/docker-args.txt" -Raw
    Check 'the password is not on the docker command line' ($argsLog -notmatch [regex]::Escape($secret)); Check 'the password reached the child only through the environment' ($argsLog -match 'ENV_PASSWORD_PRESENT=True')
    Check 'the password is in no output and no result file' (($r.Out -notmatch [regex]::Escape($secret)) -and ((Get-Content "$d/m.json" -Raw) -notmatch [regex]::Escape($secret)))
    $r2 = Invoke-Tool $ge @('-Mode', 'Markers', '-DockerCommand', $cmd, '-ProcessEnvironmentOnly', '-Repo', $repoRoot, '-Json', "$d/m2.json") ($envOk + @{ FAKE_MARKERS = 'moved' })
    $j2 = Get-Content "$d/m2.json" -Raw | ConvertFrom-Json; Check 'a second collection shows the markers moving' ($j2.TotalUserUpdates -eq 120 -and $j2.TotalUserUpdates -gt $j.TotalUserUpdates)
    $r = Invoke-Tool $ge @('-Mode', 'Window', '-FromUtc', '2026-11-04T09:00:00Z', '-ToUtc', '2026-11-04T09:55:00Z', '-DockerCommand', $cmd, '-ProcessEnvironmentOnly', '-Repo', $repoRoot, '-Json', "$d/w.json") $envOk
    Check 'the catalog start-up window is collected' (($r.Exit -eq 0) -and (Get-Content "$d/w.json" -Raw | ConvertFrom-Json).Startups -eq 7) $r.Out
    Check 'the markers carry the server''s UTC offset' ($j.UtcOffsetMinutes -eq 0)
    Check 'an answer without the UTC offset is "not collected" (exit 2): the write times could not be placed' ((Invoke-Tool $ge @('-Mode', 'Markers', '-DockerCommand', $cmd, '-ProcessEnvironmentOnly', '-Repo', $repoRoot) ($envOk + @{ FAKE_NO_OFFSET = '1' })).Exit -eq 2)
    Check 'a window without bounds is refused' ((Invoke-Tool $ge @('-Mode', 'Window', '-DockerCommand', $cmd, '-ProcessEnvironmentOnly', '-Repo', $repoRoot) $envOk).Exit -eq 2)
    Check 'no configuration is "not collected" (exit 2), not an empty result' ((Invoke-Tool $ge @('-Mode', 'Markers', '-DockerCommand', $cmd, '-ProcessEnvironmentOnly', '-Repo', $repoRoot) @{ APPERTURE_TEST_SQL_CONNECTION_STRING = '' }).Exit -eq 2)
    Check 'the development SQL port is refused' ((Invoke-Tool $ge @('-Mode', 'Markers', '-DockerCommand', $cmd, '-ProcessEnvironmentOnly', '-Repo', $repoRoot) @{ APPERTURE_TEST_SQL_CONNECTION_STRING = ($conn -replace '1433', '14333') }).Exit -eq 2)
    Check 'a catalog that is neither Apperture nor Apperture_<name> is refused' ((Invoke-Tool $ge @('-Mode', 'Markers', '-DockerCommand', $cmd, '-ProcessEnvironmentOnly', '-Repo', $repoRoot) @{ APPERTURE_TEST_SQL_CONNECTION_STRING = ($conn -replace 'Apperture_SelfTest', 'master') }).Exit -eq 2)
    Check 'a name that only starts with Apperture (ApperturePROD) is refused' ((Invoke-Tool $ge @('-Mode', 'Markers', '-DockerCommand', $cmd, '-ProcessEnvironmentOnly', '-Repo', $repoRoot) @{ APPERTURE_TEST_SQL_CONNECTION_STRING = ($conn -replace 'Apperture_SelfTest', 'ApperturePROD') }).Exit -eq 2)
    # The repository's own test configuration names the base catalog Apperture (the tests replace it with disposable ones); a real-container smoke run found this refused.
    Check 'counter-control: the base catalog name Apperture (the repository test configuration and CI) is accepted' ((Invoke-Tool $ge @('-Mode', 'Markers', '-DockerCommand', $cmd, '-ProcessEnvironmentOnly', '-Repo', $repoRoot) @{ APPERTURE_TEST_SQL_CONNECTION_STRING = ($conn -replace 'Apperture_SelfTest', 'Apperture') }).Exit -eq 0)
    Check 'counter-control: the development port is still refused with that base catalog' ((Invoke-Tool $ge @('-Mode', 'Markers', '-DockerCommand', $cmd, '-ProcessEnvironmentOnly', '-Repo', $repoRoot) @{ APPERTURE_TEST_SQL_CONNECTION_STRING = (($conn -replace 'Apperture_SelfTest', 'Apperture') -replace '1433', '14333') }).Exit -eq 2)
    $r = Invoke-Tool $ge @('-Mode', 'Markers', '-DockerCommand', $cmd, '-ProcessEnvironmentOnly', '-Repo', $repoRoot) ($envOk + @{ FAKE_DOCKER_FAIL = '1' })
    Check 'a docker or sqlcmd failure is "not collected" (exit 2) and leaks no password' (($r.Exit -eq 2) -and ($r.Out -notmatch [regex]::Escape($secret)))
}

# ====================================================================================================================== preflight
if (Want-Section 'preflight') {
    Write-Host 'preflight'
    $pf = Join-Path $val '../preflight.ps1'
    $conn = 'Server=127.0.0.1,1433;Database=Apperture_SelfTest;User Id=sa;Password=placeholder-not-a-secret;TrustServerCertificate=True'
    $base = @('-Repo', $repoRoot, '-ProcessEnvironmentOnly', '-Json', "$WorkRoot/pf.json", '-Quiet')
    $head = (Invoke-Git $repoRoot @('rev-parse', 'HEAD')).Text
    $r = Invoke-Tool $pf ($base + @('-ExpectedCommit', $head.Substring(0, 10)))
    Check 'the intended commit passes' (($r.Exit -eq 0) -and ((Get-Content "$WorkRoot/pf.json" -Raw | ConvertFrom-Json).Checks | Where-Object { $_.Id -eq 'repo.commit' -and $_.Status -eq 'PASS' })) $r.Out
    Check 'a different commit is a FAIL' ((Invoke-Tool $pf ($base + @('-ExpectedCommit', 'deadbeef12'))).Exit -eq 1)
    Check 'a too-short commit is a FAIL' ((Invoke-Tool $pf ($base + @('-ExpectedCommit', 'abc'))).Exit -eq 1)
    Check 'a repository that is not Apperture-Core is a FAIL' ((Invoke-Tool $pf ($base + @('-ExpectedRepositoryName', 'Some-Other-Repo'))).Exit -eq 1)
    # Each target control asserts the status of the SPECIFIC check (sql.target), not just the exit code: an unreachable port alone also exits 1,
    # so an exit-code-only control would pass even with the dev-port / catalog / LocalDB test deleted.
    $targetStatus = {
        param([string]$ConnectionString)
        Remove-Item "$WorkRoot/pf.json" -ErrorAction SilentlyContinue
        $run = Invoke-Tool $pf ($base + @('-RequireSql')) @{ APPERTURE_TEST_SQL_CONNECTION_STRING = $ConnectionString }
        $report = Get-Content "$WorkRoot/pf.json" -Raw | ConvertFrom-Json
        [pscustomobject]@{ Exit = $run.Exit; Target = @($report.Checks | Where-Object Id -eq 'sql.target')[0] }
    }
    $good = & $targetStatus $conn
    Check 'control: a well-formed disposable target is not flagged by sql.target (so the failures below come from the check under test)' ($good.Target.Status -in 'PASS', 'WARN') $good.Target.Message
    $baseOk = & $targetStatus ($conn -replace 'Apperture_SelfTest', 'Apperture')
    Check 'counter-control: the base catalog name Apperture is NOT flagged by sql.target (the repository test configuration; found by a real-container smoke run)' ($baseOk.Target.Status -in 'PASS', 'WARN') "$($baseOk.Target.Status) $($baseOk.Target.Message)"
    $baseDev = & $targetStatus (($conn -replace 'Apperture_SelfTest', 'Apperture') -replace '1433', '14333')
    Check 'but the base catalog name Apperture on the development port is still a FAIL' ($baseDev.Target.Status -eq 'FAIL') "$($baseDev.Target.Status) $($baseDev.Target.Message)"
    $targetCases = @(
        @('nothing configured', ''), @('the development SQL port 14333', ($conn -replace '1433', '14333')), @('a catalog that is not Apperture_*', ($conn -replace 'Apperture_SelfTest', 'master')),
        @('a name that only starts with Apperture', ($conn -replace 'Apperture_SelfTest', 'ApperturePROD')), @('an Apperture_ name with a character outside letters, digits and underscore', ($conn -replace 'Apperture_SelfTest', 'Apperture_a-b')), @('LocalDB', ($conn -replace '127.0.0.1,1433', '(localdb)\MSSQLLocalDB')),
        @('a repeated server keyword whose LAST value is the dev port', ($conn -replace 'Server=127.0.0.1,1433;', 'Server=127.0.0.1,1433;Data Source=127.0.0.1,14333;')),
        @('a space after the comma before the dev port', ($conn -replace '127.0.0.1,1433', '127.0.0.1, 14333')), @('a named pipe', ($conn -replace '127.0.0.1,1433', 'np:\\.\pipe\LOCALDB#ABC\tsql\query')),
        @('a repeated catalog keyword with different values', ($conn + ';Initial Catalog=Apperture_Other')))
    foreach ($case in $targetCases) {
        $t = & $targetStatus $case[1]
        Check "sql.target is a FAIL for $($case[0])" (($t.Target.Status -eq 'FAIL') -and ($t.Exit -eq 1)) "$($t.Target.Status) $($t.Target.Message)"
    }
    # environment hooks: asserted on the specific check, with -RequireCleanTracked (the evidence default) and without it
    $envCheck = {
        param([hashtable]$EnvVars, [string[]]$Extra = @())
        Remove-Item "$WorkRoot/pf.json" -ErrorAction SilentlyContinue
        [void](Invoke-Tool $pf ($base + $Extra) $EnvVars)
        @((Get-Content "$WorkRoot/pf.json" -Raw | ConvertFrom-Json).Checks | Where-Object Id -eq 'env.msbuild')[0]
    }
    $hookFile = Join-Path $WorkRoot 'hook.targets'; [IO.File]::WriteAllText($hookFile, '<Project/>')
    foreach ($case in @(@('CustomAfterMicrosoftCommonTargets'), @('DirectoryBuildTargetsPath'), @('CustomBeforeDirectoryBuildProps'))) {
        $c = & $envCheck @{ $case[0] = $hookFile } @('-RequireCleanTracked')
        Check "$($case[0]) set in the environment is a FAIL under -RequireCleanTracked" ($c.Status -eq 'FAIL') $c.Message
        $c = & $envCheck @{ $case[0] = $hookFile }
        Check "...and a WARN without it, never silent: $($case[0])" ($c.Status -eq 'WARN') $c.Message
    }
    $ladRoot = Join-Path $WorkRoot 'pf-localappdata'; New-Item -ItemType Directory "$ladRoot/Microsoft/MSBuild/Current/Microsoft.Common.targets/ImportBefore" | Out-Null
    [IO.File]::WriteAllText("$ladRoot/Microsoft/MSBuild/Current/Microsoft.Common.targets/ImportBefore/y.targets", '<Project/>')
    $c = & $envCheck @{ LOCALAPPDATA = $ladRoot } @('-RequireCleanTracked'); Check 'a user ImportBefore file is a FAIL under -RequireCleanTracked' ($c.Status -eq 'FAIL') $c.Message
    $c = & $envCheck @{ NUGET_SELFTEST_PROBE = 'v' } @('-RequireCleanTracked'); Check 'control: an unrelated NUGET_ variable is only informational' ($c.Status -eq 'INFO') $c.Message
    # A rejected target is never connected to, not even to see whether it answers (the development server above all). A local listener stands in
    # for the server: it sees a connection from an accepted target and none from a rejected one.
    $lis = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $lis.Start()
    try {
        $lp = $lis.LocalEndpoint.Port
        $rej = & $targetStatus "Server=127.0.0.1,$lp;Database=master;User Id=sa;Password=placeholder-not-a-secret"
        $reach = @((Get-Content "$WorkRoot/pf.json" -Raw | ConvertFrom-Json).Checks | Where-Object Id -eq 'sql.reachable')[0]
        Start-Sleep -Milliseconds 400
        Check 'a rejected target is not probed: the listener saw no connection and the report says "not probed"' (($rej.Target.Status -eq 'FAIL') -and -not $lis.Pending() -and $reach.Status -eq 'INFO' -and $reach.Message -like 'not probed*')
        $acc = & $targetStatus "Server=127.0.0.1,$lp;Database=Apperture_SelfTest;User Id=sa;Password=placeholder-not-a-secret"
        Start-Sleep -Milliseconds 400
        Check 'control: an accepted target IS probed (the listener saw a connection), so the check above can fail' (($acc.Target.Status -in 'PASS', 'WARN') -and $lis.Pending())
    }
    finally { $lis.Stop() }
    $r = Invoke-Tool $pf ($base + @('-RequireSql')) @{ APPERTURE_TEST_SQL_CONNECTION_STRING = ($conn -replace '1433', '1') }
    Check 'an unreachable SQL target is a FAIL with -RequireSql' (($r.Exit -eq 1) -and ((Get-Content "$WorkRoot/pf.json" -Raw | ConvertFrom-Json).Checks | Where-Object { $_.Id -eq 'sql.reachable' -and $_.Status -eq 'FAIL' }))
    Check 'the report carries no password' ((Get-Content "$WorkRoot/pf.json" -Raw) -notmatch 'placeholder-not-a-secret')
    Check 'an output folder outside artifacts/ is a FAIL' ((Invoke-Tool $pf ($base + @('-OutputDir', (Join-Path $WorkRoot 'elsewhere')))).Exit -eq 1)
    Check 'an output folder inside a bin folder is a FAIL' ((Invoke-Tool $pf ($base + @('-OutputDir', (Join-Path $repoRoot 'artifacts/x/bin/out')))).Exit -eq 1)
    $busy = Join-Path $repoRoot 'artifacts/selftest-busy'; New-Item -ItemType Directory -Force $busy | Out-Null; [IO.File]::WriteAllText("$busy/f.txt", 'x')
    try { Check 'a non-empty output folder is a FAIL (evidence is never overwritten)' ((Invoke-Tool $pf ($base + @('-OutputDir', $busy))).Exit -eq 1) } finally { Remove-Item -LiteralPath $busy -Recurse -Force }
    Check 'a fresh folder under artifacts/ passes' ((Invoke-Tool $pf ($base + @('-OutputDir', (Join-Path $repoRoot 'artifacts/selftest-fresh')))).Exit -eq 0)
    [IO.File]::WriteAllText("$WorkRoot/sel-missing.json", (@{ Selected = @(@{ Project = 'UnitTests'; Class = 'NoSuchClassAnywhereTests' }); Filters = @{}; Missing = @(); Ambiguous = @(); Unmapped = @() } | ConvertTo-Json -Depth 5))
    Check 'a selected class that does not exist in the tree is a FAIL' ((Invoke-Tool $pf ($base + @('-Selection', "$WorkRoot/sel-missing.json"))).Exit -eq 1)
    Check 'a selection file that does not exist is a FAIL' ((Invoke-Tool $pf ($base + @('-Selection', "$WorkRoot/none.json"))).Exit -eq 1)
}

# ====================================================================================================================== live fixture
$liveSections = @(@('mutation', 'evidence', 'export') | Where-Object { Want-Section $_ })
if ($liveSections.Count -gt 0) {
    if ($SkipLive) { foreach ($s in $liveSections) { Skip-Section $s '-SkipLive' } }
    else {
        Write-Host 'building the .NET fixture solution (live sections)'
        $fx = Join-Path $WorkRoot 'fixture'; New-Item -ItemType Directory "$fx/src/Calc", "$fx/tests/Calc.Tests" | Out-Null
        function Put-FixtureFile([string]$p, [string]$t) { [IO.File]::WriteAllText((Join-Path $fx $p), $t) }
        Put-FixtureFile 'src/Calc/Calc.csproj' "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>"
        Put-FixtureFile 'src/Calc/Calc.cs' "namespace Calc;`npublic static class Maths`n{`n    [System.ComponentModel.Description(`"Admin`")]`n    public static int Add(int a, int b)`n    {`n        return a + b;`n    }`n    public static bool IsEven(int n)`n    {`n        return n % 2 == 0;`n    }`n    public static int Untested(int n)`n    {`n        return n * 2;`n    }`n    // a comment that changes no code`n}`n"
        Put-FixtureFile 'tests/Calc.Tests/Calc.Tests.csproj' "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><IsPackable>false</IsPackable></PropertyGroup><ItemGroup><PackageReference Include=`"Microsoft.NET.Test.Sdk`" Version=`"18.10.0`" /><PackageReference Include=`"xunit`" Version=`"2.9.3`" /><PackageReference Include=`"xunit.runner.visualstudio`" Version=`"4.0.0`" /></ItemGroup><ItemGroup><ProjectReference Include=`"..\..\src\Calc\Calc.csproj`" /></ItemGroup><ItemGroup><None Include=`"expected.txt`" CopyToOutputDirectory=`"PreserveNewest`" /></ItemGroup></Project>"
        Put-FixtureFile 'tests/Calc.Tests/expected.txt' "the content a test might read`n"
        Put-FixtureFile 'tests/Calc.Tests/CalcTests.cs' "using Xunit;`nnamespace Calc.Tests;`npublic class AddTests`n{`n    [Fact] public void Adds() => Assert.Equal(5, Maths.Add(2, 3));`n    [Theory] [InlineData(2, true)] [InlineData(3, false)] public void Even(int n, bool e) => Assert.Equal(e, Maths.IsEven(n));`n}`npublic class SkippedTests`n{`n    [Fact(Skip = `"not today`")] public void Skipped() { }`n}`npublic class UntestedSqlServerTests`n{`n    [Fact] public void Fast() { }`n}`n"
        Put-FixtureFile '.gitignore' "bin/`nobj/`nartifacts/`nLogs/`n*.user`narchives/`n"; Put-FixtureFile 'tests/test-manifest.json' '{"version":1,"projects":{"Calc.Tests":"tests/Calc.Tests"},"ignore":[],"everything":{"reason":"r","when":[]},"rules":[]}'
        $fxg = { param([string[]]$a) & git -C $fx @a 2>&1 | Out-Null }
        & $fxg @('init', '-q'); & $fxg @('config', 'user.email', 't@example.invalid'); & $fxg @('config', 'user.name', 't'); & $fxg @('add', '-A'); & $fxg @('commit', '-q', '-m', 'fixture')
        $fxHead = (& git -C $fx rev-parse HEAD)
        # a folder with MSBuild configuration in it, for the "configuration above the repository / workspace" controls; only things placed under it are affected
        $anc = Join-Path $WorkRoot 'anc'; New-Item -ItemType Directory $anc | Out-Null
        [IO.File]::WriteAllText((Join-Path $anc 'Directory.Build.targets'), '<Project><ItemGroup><Compile Include="$(MSBuildThisFileDirectory)extra\Impl.cs" /></ItemGroup></Project>')
        $probe = & dotnet build "$fx/tests/Calc.Tests/Calc.Tests.csproj" -c Release --artifacts-path "$WorkRoot/probe-build" -nologo -v:q 2>&1 | ForEach-Object { [string]$_ }
        if ($LASTEXITCODE -ne 0) { foreach ($s in $liveSections) { Skip-Section $s "the fixture solution cannot be built here: $(($probe | Select-Object -Last 2) -join ' ')" } }
        else {
            $fxArt = Join-Path $WorkRoot 'fx-build'
            # ---------------------------------------------------------------------------------------------------- mutation
            if (Want-Section 'mutation') {
                Write-Host 'mutation: Invoke-Mutation, live'
                $im = Join-Path $val 'Invoke-Mutation.ps1'; $d = Join-Path $WorkRoot 'mut'; New-Item -ItemType Directory $d | Out-Null
                $runs = @(@{ project = 'tests/Calc.Tests/Calc.Tests.csproj'; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; requiresSql = $false })
                $mk = { param($list) $f = Join-Path $d ("m-" + [guid]::NewGuid().ToString('N').Substring(0, 6) + '.json'); [IO.File]::WriteAllText($f, (@{ mutations = $list } | ConvertTo-Json -Depth 8)); $f }
                $caught = @{ id = 'caught'; target = 'src/Calc/Calc.cs'; edits = @(@{ find = 'return a + b;'; replace = 'return a - b;' }); runs = $runs }
                $escaped = @{ id = 'escaped'; target = 'src/Calc/Calc.cs'; edits = @(@{ find = 'return n * 2;'; replace = 'return n * 3;' }); runs = $runs }
                $wsN = 0; $go = { param($list, [string[]]$extra = @(), [string]$ws = '') $script:wsN++; $w = if ($ws) { $ws } else { Join-Path $WorkRoot "ws$($script:wsN)" }; $o = Join-Path $WorkRoot "mout$($script:wsN)"; $r = Invoke-Tool $im (@('-Mutations', (& $mk $list), '-Source', $fx, '-Commit', $fxHead, '-Workspace', $w, '-Out', $o) + $extra); $r | Add-Member Ws $w -PassThru | Add-Member OutDir $o -PassThru }
                Push-Location $repoRoot
                try {
                    $before = (Get-Sha256Hex "$fx/src/Calc/Calc.cs")
                    $r = & $go @($caught)
                    $res = Get-Content "$($r.OutDir)/mutation-results.json" -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json
                    Check 'a mutation the tests catch is CAUGHT, with the binary and its code changed and the source restored' (($r.Exit -eq 0) -and $res.Caught -eq 1 -and $res.Results[0].BinaryChanged -and $res.Results[0].CodeChanged -and $res.Results[0].Restored) $r.Out
                    Check 'the restored tree rebuilds to the baseline assembly and the source checkout is untouched' ($res.RestoreVerified -and $res.ProtectedUnchanged -and (Get-Sha256Hex "$fx/src/Calc/Calc.cs") -eq $before)
                    Check 'the evidence folder verifies against its checksums' (Test-ChecksumFile $r.OutDir).Ok
                    Check 'the workspace is a standalone clone with no remote' ((Test-Path "$($r.Ws)/.git" -PathType Container) -and -not (& git -C $r.Ws remote))
                    $r = & $go @($escaped); $res = Get-Content "$($r.OutDir)/mutation-results.json" -Raw | ConvertFrom-Json
                    Check 'a mutation the tests do not catch is ESCAPED and fails the run' (($r.Exit -eq 1) -and $res.Escaped -eq 1) $r.Out
                    $known = $escaped.Clone(); $known.id = 'known'; $known.expect = 'escapes'; $r = & $go @($known); Check 'a documented residual (expect: escapes) that does escape does not fail the run' ($r.Exit -eq 0) $r.Out
                    $wrongExpect = $caught.Clone(); $wrongExpect.id = 'wrong'; $wrongExpect.expect = 'escapes'
                    $r = & $go @($wrongExpect); Check 'a mutation expected to escape that is caught is flagged as unexpected' ($r.Exit -eq 1)
                    $broken = @{ id = 'broken'; target = 'src/Calc/Calc.cs'; edits = @(@{ find = 'return a + b;'; replace = 'return a +;' }); runs = $runs }
                    $r = & $go @($broken); $res = Get-Content "$($r.OutDir)/mutation-results.json" -Raw | ConvertFrom-Json
                    Check 'a mutation that does not compile is INVALID, not caught' (($r.Exit -eq 1) -and $res.Results[0].Outcome -eq 'Invalid' -and $res.Results[0].Detail -match 'build failed' -and $res.Caught -eq 0)
                    $comment = @{ id = 'comment'; target = 'src/Calc/Calc.cs'; edits = @(@{ find = '// a comment that changes no code'; replace = '// another comment' }); runs = $runs }
                    $r = & $go @($comment); $res = Get-Content "$($r.OutDir)/mutation-results.json" -Raw | ConvertFrom-Json
                    Check 'a comment-only edit changes the file bytes (PDB checksum) but no code, so it is INVALID: not a mutation' (($r.Exit -eq 1) -and $res.Results[0].Outcome -eq 'Invalid' -and $res.Results[0].BinaryChanged -and -not $res.Results[0].CodeChanged -and $res.Results[0].Detail -match 'no code') $r.Out
                    $attr = @{ id = 'attr'; target = 'src/Calc/Calc.cs'; edits = @(@{ find = 'Description("Admin")'; replace = 'Description("Anyone")' }); runs = $runs }
                    $r = & $go @($attr); $res = Get-Content "$($r.OutDir)/mutation-results.json" -Raw | ConvertFrom-Json
                    Check 'an attribute-argument edit changes behaviour (code hash differs), so it is a real mutation: here it ESCAPES the tests, it is not dismissed as "no code"' (($res.Results[0].CodeChanged) -and $res.Results[0].Outcome -eq 'Escaped' -and $res.Results[0].Detail -notmatch 'no code') $r.Out
                    $nomatch = @{ id = 'nomatch'; target = 'src/Calc/Calc.cs'; edits = @(@{ find = 'return a + b;'; replace = 'return a - b;' }); runs = @(@{ project = 'tests/Calc.Tests/Calc.Tests.csproj'; filter = 'FullyQualifiedName~NoSuchClassAnywhere'; requiresSql = $false }) }
                    $r = & $go @($nomatch); Check 'a filter that matches no test makes the baseline refuse (exit 2): no meaningless run' ($r.Exit -eq 2) $r.Out
                    $ambig = @{ id = 'ambig'; target = 'src/Calc/Calc.cs'; edits = @(@{ find = 'return'; replace = 'return -' }); runs = $runs }
                    $r = & $go @($ambig); $res = Get-Content "$($r.OutDir)/mutation-results.json" -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json
                    Check 'a find text that occurs more than once is INVALID' (($r.Exit -eq 1) -and $res.Results[0].Detail -match 'occurs')
                    $empty = @{ id = 'empty'; target = 'src/Calc/Calc.cs'; edits = @(@{ find = ''; replace = 'x' }); runs = $runs }; $r = & $go @($empty); Check 'an empty find text is INVALID' ($r.Exit -eq 1)
                    $untracked = @{ id = 'untracked'; target = 'tests/Calc.Tests/obj/project.assets.json'; edits = @(@{ find = 'a'; replace = 'b' }); runs = $runs }; $r = & $go @($untracked); Check 'a target that is not a tracked source file is refused' ($r.Exit -eq 2)
                    $esc = @{ id = 'esc'; target = '../outside.cs'; edits = @(@{ find = 'a'; replace = 'b' }); runs = $runs }; $r = & $go @($esc); Check 'a target path that climbs out of the workspace is refused' ($r.Exit -eq 2)
                    $r = & $go @($caught) @() $fx; Check 'a workspace that is the source repository is refused' ($r.Exit -eq 2 -and (Get-Sha256Hex "$fx/src/Calc/Calc.cs") -eq $before)
                    $r = & $go @($caught) @() (Join-Path $fx 'sub-workspace'); Check 'a workspace inside the source repository is refused' ($r.Exit -eq 2)
                    $r = & $go @($caught) @() $repoRoot; Check 'a workspace that is the active package is refused' ($r.Exit -eq 2)
                    $r = & $go @($caught) @() (Join-Path $repoRoot 'artifacts/selftest-ws'); Check 'a workspace inside the active package is refused' ($r.Exit -eq 2)
                    $r = & $go @($caught) @('-ProtectedRoot', $WorkRoot) ; Check 'a workspace inside an extra protected root is refused' ($r.Exit -eq 2)
                    $r = & $go @($caught) @() (Join-Path $anc 'ws-under-config'); Check 'a workspace below MSBuild configuration that is not part of the commit is refused, and nothing is created' (($r.Exit -eq 2) -and ($r.Out -match 'build configuration above the workspace') -and -not (Test-Path (Join-Path $anc 'ws-under-config')))
                    $hookT = Join-Path $WorkRoot 'env-hook-mut.targets'; [IO.File]::WriteAllText($hookT, '<Project/>')
                    $r = Invoke-Tool $im @('-Mutations', (& $mk @($caught)), '-Source', $fx, '-Commit', $fxHead, '-Workspace', (Join-Path $WorkRoot 'ws-envhook'), '-Out', (Join-Path $WorkRoot 'mout-envhook')) @{ CustomBeforeMicrosoftCommonTargets = $hookT }
                    Check 'a mutation run is refused while an MSBuild import hook is set in the environment, and nothing is created' (($r.Exit -eq 2) -and ($r.Out -match 'environment changes what a build compiles') -and -not (Test-Path (Join-Path $WorkRoot 'ws-envhook')))
                    New-Item -ItemType Directory (Join-Path $WorkRoot 'full') | Out-Null; [IO.File]::WriteAllText((Join-Path $WorkRoot 'full/x'), 'x'); $r = & $go @($caught) @() (Join-Path $WorkRoot 'full'); Check 'a non-empty workspace is refused' ($r.Exit -eq 2)
                    $crash = @{ id = 'crash'; target = 'src/Calc/Calc.cs'; expect = 'escapes'; edits = @(@{ find = 'return a + b;'; replace = 'System.Environment.FailFast("boom"); return a + b;' }); runs = $runs }
                    $r = & $go @($crash); $res = Get-Content "$($r.OutDir)/mutation-results.json" -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json
                    Check 'a mutation that crashes the test host is INVALID, never ESCAPED (even when an escape was expected)' (($r.Exit -eq 1) -and $res.Escaped -eq 0 -and $res.Results[0].Outcome -eq 'Invalid') $r.Out
                    # Isolation does not depend on where the tool was started or what -Source says: the repository holding these scripts, and every worktree of it
                    # (the primary checkout is one), are protected by themselves.
                    $mainWorktree = ((Invoke-Git $repoRoot @('worktree', 'list', '--porcelain')).Text -split "`n")[0].Substring(9)
                    Push-Location $WorkRoot
                    try { $r1 = & $go @($caught) @() (Join-Path $repoRoot 'artifacts/selftest-ws2') }
                    finally { Pop-Location }
                    Check 'the toolkit''s own repository is protected whatever the current directory' (($r1.Exit -eq 2) -and -not (Test-Path (Join-Path $repoRoot 'artifacts/selftest-ws2')))
                    # The primary checkout is never aimed at (a regression would write into it); its protection is read from the list the tool recorded.
                    $rp = & $go @($caught); $resP = Get-Content "$($rp.OutDir)/mutation-results.json" -Raw | ConvertFrom-Json
                    $protectedList = @($resP.ProtectedPaths | ForEach-Object { [IO.Path]::GetFullPath($_) })
                    Check 'the protected list holds the primary checkout, every worktree of the toolkit''s repository and its common .git' (($protectedList -contains [IO.Path]::GetFullPath($mainWorktree)) -and ($protectedList -contains [IO.Path]::GetFullPath($repoRoot)) -and (@($protectedList | Where-Object { $_ -like '*\.git' -or $_ -like '*/.git' }).Count -ge 1))
                    $r = Invoke-Tool $im @('-Mutations', (& $mk @($caught)), '-Source', $fx, '-Commit', $fxHead, '-Workspace', (Join-Path $WorkRoot 'ws-out'), '-Out', (Join-Path $repoRoot 'src/zz-selftest-out'))
                    Check 'an evidence folder inside a protected checkout (outside its artifacts/) is refused' (($r.Exit -eq 2) -and -not (Test-Path (Join-Path $repoRoot 'src/zz-selftest-out')))
                    if ($IsWindows) {
                        $drv = $WorkRoot.Substring(0, 1); $tail = $WorkRoot.Substring(3)
                        $pkgTail = $repoRoot.Substring(3) -replace '/', '\'
                        foreach ($case in @(
                                @('a UNC spelling of a workspace', "\\localhost\${drv}`$\$tail\unc-ws", (Join-Path $WorkRoot 'unc-ws')),
                                @('a UNC spelling of a workspace inside the active package', "\\localhost\${drv}`$\$pkgTail\artifacts\unc-in-package", (Join-Path $repoRoot 'artifacts/unc-in-package')),
                                @('a device-path (\\?\) spelling of a workspace', "\\?\$WorkRoot\dev-ws", (Join-Path $WorkRoot 'dev-ws')),
                                @('a device-path (\\?\) spelling inside the active package', "\\?\$repoRoot\artifacts\dev-in-package", (Join-Path $repoRoot 'artifacts/dev-in-package')),
                                @('an 8.3 short-name spelling', "${drv}:\PROGRA~1\mutation-ws", "${drv}:\PROGRA~1\mutation-ws"),
                                @('a segment ending in a space', ((Join-Path $WorkRoot 'trailing-space ') + '\ws'), (Join-Path $WorkRoot 'trailing-space')))) {
                            $r = & $go @($caught) @() $case[1]
                            Check "$($case[0]) is refused and nothing is created" (($r.Exit -eq 2) -and ($r.Out -match 'plain absolute local path') -and -not (Test-Path -LiteralPath $case[2])) $r.Out
                        }
                        $r = & $go @($caught) @('-ProtectedRoot', "\\localhost\${drv}`$\$tail"); Check 'a protected root given in UNC form is refused (it could not be compared)' ($r.Exit -eq 2 -and $r.Out -match 'plain absolute local path')
                    }
                    if ($IsWindows) {
                        $linkTarget = Join-Path $WorkRoot 'linktarget'; New-Item -ItemType Directory $linkTarget | Out-Null; $link = Join-Path $WorkRoot 'link'
                        & cmd /c mklink /J "$link" "$linkTarget" 2>&1 | Out-Null
                        if (Test-Path $link) { $r = & $go @($caught) @() (Join-Path $link 'ws'); Check 'a workspace behind a junction is refused' ($r.Exit -eq 2); (Get-Item $link).Delete() }
                    }
                    $r = Invoke-Tool $im @('-Mutations', (& $mk @($caught)), '-Source', $fx, '-Commit', 'deadbeefdeadbeef', '-Workspace', (Join-Path $WorkRoot 'wsX'), '-Out', (Join-Path $WorkRoot 'moutX')); Check 'an unknown commit is refused' ($r.Exit -eq 2)
                    $sqlmut = @{ id = 'sql'; target = 'src/Calc/Calc.cs'; edits = @(@{ find = 'return a + b;'; replace = 'return a - b;' }); runs = @(@{ project = 'tests/Calc.Tests/Calc.Tests.csproj'; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; requiresSql = $true }) }
                    $env0 = $env:APPERTURE_TEST_SQL_CONNECTION_STRING; $env:APPERTURE_TEST_SQL_CONNECTION_STRING = ''
                    try { $r = & $go @($sqlmut); Check 'mutations that need SQL are refused when SQL is not configured' (($r.Exit -eq 2) -and $r.Out -match 'SQL') $r.Out
                        $env:APPERTURE_TEST_SQL_CONNECTION_STRING = 'Server=127.0.0.1,1;Database=Apperture_SelfTest;User Id=sa;Password=placeholder-not-a-secret'; $r = & $go @($sqlmut); Check 'mutations that need SQL are refused when SQL is unreachable' (($r.Exit -eq 2) -and $r.Out -match 'not reachable') $r.Out }
                    finally { $env:APPERTURE_TEST_SQL_CONNECTION_STRING = $env0 }
                }
                finally { Pop-Location }
            }
            # ---------------------------------------------------------------------------------------------------- evidence
            if (Want-Section 'evidence') {
                Write-Host 'evidence: Invoke-Evidence, live'
                $ie = Join-Path $val 'Invoke-Evidence.ps1'; $d = Join-Path $WorkRoot 'ev'; New-Item -ItemType Directory $d | Out-Null
                $plan = { param($runs) $f = Join-Path $d ("p-" + [guid]::NewGuid().ToString('N').Substring(0, 6) + '.json'); [IO.File]::WriteAllText($f, (@{ runs = $runs } | ConvertTo-Json -Depth 6)); $f }
                $ev = { param($runs, [string[]]$extra = @(), [hashtable]$envv = @{}) $f = & $plan $runs; $r = Invoke-Tool $ie (@('-Record', 'selftest', '-Plan', $f, '-Repo', $fx, '-ProcessEnvironmentOnly') + $extra) $envv; $folders = @(Get-ChildItem "$fx/artifacts/evidence/selftest" -Directory -ErrorAction SilentlyContinue | Sort-Object LastWriteTime); $latest = if ($folders.Count) { $folders[-1].FullName } else { $null }; $prov = if ($latest -and (Test-Path (Join-Path $latest 'provenance.json'))) { Get-Content (Join-Path $latest 'provenance.json') -Raw | ConvertFrom-Json } else { $null }; $r | Add-Member Folder $latest -PassThru | Add-Member Prov $prov -PassThru }
                $proj = 'tests/Calc.Tests/Calc.Tests.csproj'
                $r = & $ev @(@{ name = 'add'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedMinTests = 3; expectedClasses = @('AddTests') }) @('-Build')
                Check 'a clean tree with every expected test passing is AUTHORITATIVE' (($r.Exit -eq 0) -and $r.Prov.Authoritative -and $r.Prov.Runs[0].Outcome -eq 'Passed' -and $r.Prov.Runs[0].Counts.Passed -eq 3) $r.Out
                Check 'provenance records the commit, tree hashes, tools and assembly hash' (($r.Prov.Tree.Head -eq $fxHead) -and $r.Prov.Tree.Tree -and $r.Prov.Tools.Dotnet -and $r.Prov.Runs[0].AssemblySha256 -and $r.Prov.Runs[0].TrxSha256)
                Check 'the evidence folder verifies against its checksums' (Test-ChecksumFile $r.Folder).Ok
                [IO.File]::AppendAllText("$($r.Folder)/results/add.trx", ' '); Check 'tampering with a TRX after the fact is detected by the checksums' (-not (Test-ChecksumFile $r.Folder).Ok)
                $r = & $ev @(@{ name = 'skipped'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.SkippedTests' })
                Check 'a run whose only test is skipped is NOT a pass (it never ran)' (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and $r.Prov.Runs[0].Outcome -in 'NotDiscovered', 'NotExecuted') $r.Out
                $r = & $ev @(@{ name = 'none'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.NoSuchClass' })
                Check 'a filter that selects no test is NotDiscovered, not a pass' (($r.Exit -eq 1) -and $r.Prov.Runs[0].Outcome -in 'NotDiscovered', 'Inconclusive') $r.Out
                $r = & $ev @(@{ name = 'few'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedMinTests = 50 })
                Check 'fewer tests than expected is NotDiscovered' (($r.Exit -eq 1) -and $r.Prov.Runs[0].Outcome -eq 'NotDiscovered')
                $r = & $ev @(@{ name = 'missclass'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedClasses = @('AddTests', 'ClassThatIsNotThere') })
                Check 'an expected class that produced no test is NotDiscovered' (($r.Exit -eq 1) -and $r.Prov.Runs[0].Outcome -eq 'NotDiscovered' -and $r.Prov.Runs[0].MissingClasses -contains 'ClassThatIsNotThere')
                # A selection that names one simple name in two namespaces expects BOTH identities to produce a test (the simple name alone is satisfied by either)
                $selFilter = 'FullyQualifiedName~Calc.Tests.AddTests'
                $selOk = Join-Path $d 'sel-qual-ok.json'; $selBad = Join-Path $d 'sel-qual-bad.json'
                [IO.File]::WriteAllText($selOk, (@{ Selected = @(@{ Project = 'Calc'; Class = 'AddTests'; Qualified = @('Calc.Tests.AddTests') }); Filters = @{ Calc = $selFilter }; Missing = @(); Ambiguous = @(); Unmapped = @() } | ConvertTo-Json -Depth 6))
                [IO.File]::WriteAllText($selBad, (@{ Selected = @(@{ Project = 'Calc'; Class = 'AddTests'; Qualified = @('Calc.Tests.AddTests', 'Other.Ns.AddTests') }); Filters = @{ Calc = $selFilter }; Missing = @(); Ambiguous = @('class ''AddTests'' ... 2 namespaces'); Unmapped = @() } | ConvertTo-Json -Depth 6))
                $r = & $ev @(@{ name = 'qualok'; project = $proj; selectionProject = 'Calc'; expectedMinTests = 3 }) @('-Selection', $selOk)
                Check 'selection-driven run, one identity present: passes (counter-control for the ambiguity check)' (($r.Exit -eq 0) -and $r.Prov.Runs[0].Outcome -eq 'Passed' -and $r.Prov.Runs[0].MissingClasses.Count -eq 0) $r.Out
                $r = & $ev @(@{ name = 'qualbad'; project = $proj; selectionProject = 'Calc'; expectedMinTests = 3 }) @('-Selection', $selBad)
                Check 'selection-driven run: a second namespace identity that produced no test makes the run NotDiscovered (the simple name alone would have passed)' (($r.Exit -eq 1) -and $r.Prov.Runs[0].Outcome -eq 'NotDiscovered' -and $r.Prov.Runs[0].MissingClasses -contains 'Other.Ns.AddTests') $r.Out
                $r = & $ev @(@{ name = 'allowskip'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests|FullyQualifiedName~Calc.Tests.SkippedTests'; allowNotExecuted = @('Calc.Tests.SkippedTests.Skipped'); expectedMinTests = 3 })
                Check 'a skip the plan names exactly (full test name) is allowed and recorded' (($r.Exit -eq 0) -and $r.Prov.Runs[0].Counts.NotExecuted -eq 1) $r.Out
                foreach ($loose in 'Skipped', 'Calc', 'Calc.Tests.SkippedTests', 'calc.tests.skippedtests.skipped') {
                    $r = & $ev @(@{ name = 'loose'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests|FullyQualifiedName~Calc.Tests.SkippedTests'; allowNotExecuted = @($loose); expectedMinTests = 3 })
                    Check "a skip allowance that is only a substring or a different case ('$loose') allows nothing" (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and $r.Prov.Runs[0].Outcome -eq 'NotExecuted') $r.Out
                }
                $dupPlan = & $plan @(@{ name = 'same'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests' }, @{ name = 'same'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.SkippedTests' })
                $r = Invoke-Tool $ie @('-Record', 'selftest', '-Plan', $dupPlan, '-Repo', $fx, '-ProcessEnvironmentOnly'); Check 'two runs with the same name are refused (the second would overwrite the first run''s TRX)' ($r.Exit -eq 2 -and $r.Out -match 'same name')
                $r = & $ev @(@{ name = 'add'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests' }) @('-ExpectedCommit', 'deadbeef12')
                Check 'a wrong expected commit blocks the run: no test runs, nothing is evidence' (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and @($r.Prov.Runs).Count -eq 0 -and (-not (Test-Path "$($r.Folder)/results/add.trx")))
                $r = & $ev @(@{ name = 'sqlrun'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; requiresSql = $true })
                Check 'a run that needs SQL is blocked when SQL is not configured' (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and $r.Prov.PreflightOk -eq $false)
                Check 'a missing plan is a tool error (exit 2)' ((Invoke-Tool $ie @('-Record', 'selftest', '-Plan', "$d/none.json", '-Repo', $fx, '-ProcessEnvironmentOnly')).Exit -eq 2)
                Check 'a bad record name is refused' ((Invoke-Tool $ie @('-Record', '../x', '-Plan', (& $plan @(@{ name = 'a'; project = $proj })), '-Repo', $fx, '-ProcessEnvironmentOnly')).Exit -ne 0)
                [IO.File]::AppendAllText("$fx/src/Calc/Calc.cs", "// touched after the build`n")
                $r = & $ev @(@{ name = 'stale'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests' }) @('-AllowDirty')
                Check 'a dirty tree is never authoritative, and a source file newer than the assembly makes the binary STALE' (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and $r.Prov.Runs[0].Outcome -eq 'StaleBinary') $r.Out
                $r = & $ev @(@{ name = 'dirty'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests' })
                Check 'a dirty tracked tree blocks the run by default' (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and $r.Prov.PreflightOk -eq $false)
                & git -C $fx checkout -- src/Calc/Calc.cs 2>&1 | Out-Null
                $r = & $ev @(@{ name = 'rebuilt'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedMinTests = 3 }) @('-Build')
                Check 'with -Build the tool builds first and then runs authoritatively' (($r.Exit -eq 0) -and $r.Prov.Authoritative) $r.Out
                (Get-Item "$fx/src/Calc/Calc.cs").LastWriteTime = Get-Date
                $r = & $ev @(@{ name = 'stamped'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedMinTests = 3 })
                Check 'a source file touched after the build, with identical content, is accepted through the build stamp' (($r.Exit -eq 0) -and $r.Prov.Authoritative -and $r.Prov.Runs[0].BinaryProvenance -like 'build stamp*') $r.Out
                $stampFile = Join-Path $fx 'artifacts/evidence-build/evidence-build-stamp.json'; $stampText = Get-Content $stampFile -Raw
                [IO.File]::WriteAllText($stampFile, ([regex]::Replace($stampText, '"[0-9a-f]{64}"', ('"' + ('0' * 64) + '"'))))
                $r = & $ev @(@{ name = 'badstamp'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests' })
                Check 'a stamp whose assembly hash no longer matches is not trusted: the file-time rule applies and the binary is STALE' (($r.Exit -eq 1) -and $r.Prov.Runs[0].Outcome -eq 'StaleBinary') $r.Out
                [IO.File]::WriteAllText($stampFile, $stampText)
                # ---- the stamp covers everything the test host loads from the output folder, not only the test assembly
                $depDll = Join-Path $fx 'artifacts/evidence-build/bin/Calc.Tests/release/Calc.dll'
                Check 'premise: the production assembly is copied into the test output folder' (Test-Path -LiteralPath $depDll)
                $depBytes = [IO.File]::ReadAllBytes($depDll)
                [IO.File]::WriteAllBytes($depDll, [byte[]]($depBytes + [byte[]](0, 0, 0, 0)))   # a dependency in the output folder changes; the test assembly is untouched
                $r = & $ev @(@{ name = 'depswap'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedMinTests = 3 })
                Check 'a changed dependency in the output folder invalidates the stamp: not authoritative, "file times only"' (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and $r.Prov.Runs[0].BinaryProvenance -like 'file times only*') $r.Out
                [IO.File]::WriteAllBytes($depDll, $depBytes)
                $r = & $ev @(@{ name = 'depback'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedMinTests = 3 })
                Check 'control: with the dependency byte-identical again, the same stamp is trusted again' (($r.Exit -eq 0) -and $r.Prov.Authoritative -and $r.Prov.Runs[0].BinaryProvenance -like 'build stamp*') $r.Out
                # ---- a source file in a folder that .gitignore hides is still compiled by the SDK
                New-Item -ItemType Directory "$fx/src/Calc/Logs" | Out-Null; [IO.File]::WriteAllText("$fx/src/Calc/Logs/Impl.cs", "namespace Calc; public static class Impl { }`n")
                & git -C $fx check-ignore -q src/Calc/Logs/Impl.cs; Check 'premise: the file is ignored by Git (invisible to git status)' ($LASTEXITCODE -eq 0)
                $r = & $ev @(@{ name = 'ignoredsrc'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests' }) @('-Build')
                Check 'an ignored source file under src/ blocks the run: the build would compile it' (($r.Exit -eq 1) -and $r.Prov.PreflightOk -eq $false -and @($r.Prov.Runs).Count -eq 0 -and ($r.Prov.Tree.UntrackedBuildFiles -contains 'src/Calc/Logs/Impl.cs')) $r.Out
                Remove-Item "$fx/src/Calc/Logs" -Recurse -Force
                # ---- MSBuild imports <project>.user automatically, and a .user file can add a source from anywhere on disk
                [IO.File]::WriteAllText("$fx/src/Calc/Calc.csproj.user", "<Project><ItemGroup><Compile Include=`"..\..\archives\Impl.cs`" /></ItemGroup></Project>")
                & git -C $fx check-ignore -q src/Calc/Calc.csproj.user; Check 'premise: the .csproj.user file is ignored by Git' ($LASTEXITCODE -eq 0)
                $r = & $ev @(@{ name = 'userfile'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests' }) @('-Build')
                Check 'an ignored .csproj.user file is a build input: the run is blocked' (($r.Exit -eq 1) -and $r.Prov.PreflightOk -eq $false -and @($r.Prov.Runs).Count -eq 0 -and ($r.Prov.Tree.UntrackedBuildFiles -contains 'src/Calc/Calc.csproj.user')) $r.Out
                Remove-Item "$fx/src/Calc/Calc.csproj.user"
                # ---- Directory.Build.targets / .rsp are searched for in every folder ABOVE the project, where Git cannot see them
                Check 'premise: no MSBuild configuration sits above the scratch folder (or every build below would be affected)' (@(Get-AncestorBuildFiles $fx).Count -eq 0)
                Check 'the ancestor search finds a Directory.Build.targets in a parent folder' (@(Get-AncestorBuildFiles (Join-Path $anc 'fixture-anc')) -contains (Join-Path $anc 'Directory.Build.targets'))
                $fa = Join-Path $anc 'fixture-anc'; & git clone -q $fx $fa 2>&1 | Out-Null; & git -C $fa remote remove origin
                $fplan = & $plan @(@{ name = 'ancestor'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedMinTests = 3 })
                $r = Invoke-Tool $ie @('-Record', 'selftest', '-Plan', $fplan, '-Repo', $fa, '-ProcessEnvironmentOnly', '-Build')
                $aProv = Get-Content ((Get-ChildItem "$fa/artifacts/evidence/selftest" -Directory | Select-Object -Last 1).FullName + '/provenance.json') -Raw | ConvertFrom-Json
                Check 'configuration above the repository blocks the run in the preflight: no test runs' (($r.Exit -eq 1) -and -not $aProv.Authoritative -and $aProv.PreflightOk -eq $false -and @($aProv.Runs).Count -eq 0 -and ($aProv.Reasons -match 'configuration above the repository')) $r.Out
                $r = Invoke-Tool $ie @('-Record', 'selftest', '-Plan', $fplan, '-Repo', $fa, '-ProcessEnvironmentOnly', '-Build', '-AllowDirty', '-ArtifactsPath', (Join-Path $WorkRoot 'anc-own-build'))
                $aProv2 = Get-Content ((Get-ChildItem "$fa/artifacts/evidence/selftest" -Directory | Sort-Object LastWriteTime | Select-Object -Last 1).FullName + '/provenance.json') -Raw | ConvertFrom-Json
                Check 'with -AllowDirty the run proceeds, but evidence built under that configuration is never authoritative' (($r.Exit -eq 1) -and -not $aProv2.Authoritative -and ($aProv2.Reasons -match 'build configuration above the repository')) $r.Out
                # ---- an MSBuild import hook set in the ENVIRONMENT changes what is compiled with no file in the repository at all
                $hookTargets = Join-Path $WorkRoot 'env-hook.targets'; [IO.File]::WriteAllText($hookTargets, '<Project/>')
                $r = & $ev @(@{ name = 'envhook'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedMinTests = 3 }) @('-Build') @{ CustomAfterMicrosoftCommonTargets = $hookTargets }
                Check 'an MSBuild import hook in the environment blocks the run in the preflight: no test runs, the variable is named in the provenance' (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and $r.Prov.PreflightOk -eq $false -and @($r.Prov.Runs).Count -eq 0 -and ($r.Prov.BuildEnvironment.HookVariables -contains 'CustomAfterMicrosoftCommonTargets') -and ($r.Prov.Reasons -match 'environment changes what a build compiles')) $r.Out
                $r = & $ev @(@{ name = 'envhook2'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedMinTests = 3 }) @('-Build', '-AllowDirty', '-ArtifactsPath', (Join-Path $WorkRoot 'env-own-build')) @{ DirectoryBuildTargetsPath = $hookTargets; ImportDirectoryBuildTargets = 'true' }
                Check 'with -AllowDirty it runs but is never authoritative under such a hook' (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and ($r.Prov.BuildEnvironment.HookVariables -contains 'DirectoryBuildTargetsPath') -and ($r.Prov.Reasons -match 'environment changes what a build compiles')) $r.Out
                # ---- MSBuild imports <project>.*.targets / *.props from <ArtifactsPath>/obj/<project>/, where a reused artifacts path can hold a stale one
                $injectFile = Join-Path $fx 'artifacts/evidence-build/obj/Calc/Calc.csproj.inject.targets'
                Check 'premise: restore has written its own .nuget.g files there (they are allowed) and nothing else' ((Test-Path (Split-Path $injectFile -Parent)) -and @(Get-ProjectImportInjections (Join-Path $fx 'artifacts/evidence-build')).Count -eq 0)
                [IO.File]::WriteAllText($injectFile, '<Project/>')
                $r = & $ev @(@{ name = 'injected'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedMinTests = 3 })
                Check 'an extra import file in the artifacts path makes the evidence not authoritative and is named, without -Build (stamp untrusted)' (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and (@($r.Prov.BuildEnvironment.ArtifactsPathInjectedImports) -contains $injectFile) -and ($r.Prov.Reasons -match 'import files restore did not write')) $r.Out
                $r = & $ev @(@{ name = 'injected2'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedMinTests = 3 }) @('-Build')
                Check '...and with -Build as well (the build would import it)' (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and ($r.Prov.Reasons -match 'import files restore did not write')) $r.Out
                Remove-Item $injectFile
                $r = & $ev @(@{ name = 'injected3'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedMinTests = 3 }) @('-Build')
                Check 'control: with the extra file gone a build is authoritative again (and re-stamps)' (($r.Exit -eq 0) -and $r.Prov.Authoritative) $r.Out
                # ---- a content file the tests read from the output folder is part of what was built
                $contentFile = Join-Path $fx 'artifacts/evidence-build/bin/Calc.Tests/release/expected.txt'
                Check 'premise: the content file is copied into the test output folder' (Test-Path -LiteralPath $contentFile)
                $contentText = [IO.File]::ReadAllText($contentFile); [IO.File]::WriteAllText($contentFile, 'edited after the build')
                $r = & $ev @(@{ name = 'contentswap'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedMinTests = 3 })
                Check 'a changed content file in the output folder invalidates the stamp: not authoritative, "file times only"' (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and $r.Prov.Runs[0].BinaryProvenance -like 'file times only*') $r.Out
                [IO.File]::WriteAllText($contentFile, $contentText)
                $r = & $ev @(@{ name = 'contentback'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedMinTests = 3 })
                Check 'control: with the content file as built again, the stamp is trusted again' (($r.Exit -eq 0) -and $r.Prov.Authoritative -and $r.Prov.Runs[0].BinaryProvenance -like 'build stamp*') $r.Out
                # ---- an untracked file among the sources is compiled by the build, so the tree is not the commit
                [IO.File]::WriteAllText("$fx/src/Calc/Extra.cs", "namespace Calc; public static class Extra { }`n")
                $r = & $ev @(@{ name = 'untracked'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests' }) @('-Build')
                Check 'an untracked source file blocks the run in the preflight: no test runs' (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and $r.Prov.PreflightOk -eq $false -and @($r.Prov.Runs).Count -eq 0 -and ($r.Prov.Reasons -match 'untracked')) $r.Out
                $r = & $ev @(@{ name = 'untracked2'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedMinTests = 3 }) @('-Build', '-AllowDirty')
                Check 'with -AllowDirty the run proceeds but an untracked source file still makes it NOT authoritative' (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and ($r.Prov.Reasons -match 'untracked file') -and $r.Prov.Tree.UntrackedBuildFiles.Count -eq 1) $r.Out
                Remove-Item "$fx/src/Calc/Extra.cs"
                $r = & $ev @(@{ name = 'restamp'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedMinTests = 3 }) @('-Build')
                Check 'after the untracked file is gone a clean build is authoritative again (and re-stamps the build)' (($r.Exit -eq 0) -and $r.Prov.Authoritative) $r.Out
                # ---- a binary built from ANOTHER commit, newer than this tree's sources, must not become evidence for this commit
                $fy = Join-Path $WorkRoot 'fixture-y'
                & git clone -q $fx $fy 2>&1 | Out-Null; & git -C $fy config user.email 't@example.invalid'; & git -C $fy config user.name 't'; & git -C $fy remote remove origin   # the clone's origin is the fixture's path, which the repository-identity check rightly rejects
                [IO.File]::WriteAllText("$fy/src/Calc/Calc.cs", ([IO.File]::ReadAllText("$fy/src/Calc/Calc.cs") -replace 'return a \+ b;', 'return a - b;'))
                & git -C $fy commit -q -am 'a different commit with a wrong Add' 2>&1 | Out-Null
                $shared = Join-Path $WorkRoot 'shared-build'
                & dotnet build "$fx/tests/Calc.Tests/Calc.Tests.csproj" -c Release --artifacts-path $shared -nologo -v:q 2>&1 | Out-Null   # commit X's binary, built AFTER Y's files were written
                $f = & $plan @(@{ name = 'wrongbinary'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; expectedMinTests = 3 })
                $r = Invoke-Tool $ie @('-Record', 'selftest', '-Plan', $f, '-Repo', $fy, '-ProcessEnvironmentOnly', '-ArtifactsPath', $shared)
                $yProv = Get-Content ((Get-ChildItem "$fy/artifacts/evidence/selftest" -Directory | Select-Object -Last 1).FullName + '/provenance.json') -Raw | ConvertFrom-Json
                Check 'a binary built from another commit passes the tests (the dangerous premise) but is NOT authoritative for this commit' (($yProv.Runs[0].Outcome -eq 'Passed') -and -not $yProv.Authoritative -and ($yProv.Reasons -match 'file times only') -and $r.Exit -eq 1) $r.Out
                $r = Invoke-Tool $ie @('-Record', 'selftest', '-Plan', $f, '-Repo', $fy, '-ProcessEnvironmentOnly', '-ArtifactsPath', (Join-Path $WorkRoot 'y-own-build'), '-Build')
                $yProv2 = Get-Content ((Get-ChildItem "$fy/artifacts/evidence/selftest" -Directory | Sort-Object LastWriteTime | Select-Object -Last 1).FullName + '/provenance.json') -Raw | ConvertFrom-Json
                Check 'the same commit built by the tool itself shows its own wrong Add: the tests fail, as they must' ($yProv2.Runs[0].Outcome -eq 'Failed' -and -not $yProv2.Authoritative)
                # ---- existing evidence is never written over, whatever else is wrong with the request
                $keep = Join-Path $fx 'artifacts/evidence/keepme'; New-Item -ItemType Directory "$keep/results" | Out-Null
                [IO.File]::WriteAllText("$keep/provenance.json", '{"original":true}'); [IO.File]::WriteAllText("$keep/results/old.trx", 'old trx'); [void](Write-ChecksumFile $keep)
                $keepHash = Get-Sha256Hex "$keep/provenance.json"; $keepSums = Get-Sha256Hex "$keep/SHA256SUMS.txt"
                $f2 = & $plan @(@{ name = 'overwrite'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests' })
                $r = Invoke-Tool $ie @('-Record', 'selftest', '-Plan', $f2, '-Repo', $fx, '-ProcessEnvironmentOnly', '-Out', $keep)
                Check 'an existing evidence folder passed as -Out is refused (exit 2) and left exactly as it was' (($r.Exit -eq 2) -and (Get-Sha256Hex "$keep/provenance.json") -eq $keepHash -and (Get-Sha256Hex "$keep/SHA256SUMS.txt") -eq $keepSums -and (Test-ChecksumFile $keep).Ok) $r.Out
                $outside = Join-Path $WorkRoot 'elsewhere-evidence'
                $r = Invoke-Tool $ie @('-Record', 'selftest', '-Plan', $f2, '-Repo', $fx, '-ProcessEnvironmentOnly', '-Out', $outside)
                Check 'a destination outside artifacts/ is refused (exit 2) and nothing is created there' (($r.Exit -eq 2) -and -not (Test-Path $outside)) $r.Out
                # ---- SQL proof, with a local TCP listener standing in for the server and a stateful fake docker
                $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $listener.Start()
                try {
                    $port = $listener.LocalEndpoint.Port; $pwText = 'placeholder-not-a-secret'
                    $sqlEnv = @{ APPERTURE_TEST_SQL_CONNECTION_STRING = "Server=127.0.0.1,$port;Database=Apperture_SelfTest;User Id=sa;Password=$pwText;TrustServerCertificate=True" }
                    $fakeDir = Join-Path $d 'fakedocker'; New-Item -ItemType Directory $fakeDir | Out-Null; $fk = New-FakeDocker $fakeDir; $state = Join-Path $fakeDir 'state.txt'
                    $sqlPlan = @(@{ name = 'sqlrun'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.AddTests'; requiresSql = $true; expectedMinTests = 3 })
                    $sx = @('-SqlContainer', 'apperture-test-sql', '-DockerCommand', $fk)
                    Remove-Item $state -ErrorAction SilentlyContinue
                    $r = & $ev $sqlPlan $sx ($sqlEnv + @{ FAKE_STATE_FILE = $state; FAKE_STEP = '20' })
                    Check 'SQL proof: markers that moved and catalogs that started make the evidence AUTHORITATIVE' (($r.Exit -eq 0) -and $r.Prov.Authoritative -and $r.Prov.Sql.Proof.Moved -and $r.Prov.Sql.Proof.CatalogStartupsInWindow -eq 7) $r.Out
                    Check 'the SQL target is recorded without its password, and no evidence file contains it' (($r.Prov.Sql.Target.Catalog -eq 'Apperture_SelfTest') -and -not (Get-ChildItem $r.Folder -Recurse -File | Select-String -SimpleMatch $pwText -List))
                    Remove-Item $state -ErrorAction SilentlyContinue
                    $r = & $ev $sqlPlan $sx ($sqlEnv + @{ FAKE_STATE_FILE = $state; FAKE_STEP = '0' })
                    Check 'SQL proof: markers that did not move are SqlNotProven, so not authoritative' (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and ($r.Prov.Reasons -match 'did not move')) $r.Out
                    Remove-Item $state -ErrorAction SilentlyContinue
                    $r = & $ev $sqlPlan $sx ($sqlEnv + @{ FAKE_STATE_FILE = $state; FAKE_STEP = '20'; FAKE_STARTUPS = '0' })
                    Check 'SQL proof: markers that moved but no catalog start-up in the window are SqlNotProven' (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and ($r.Prov.Reasons -match 'no test catalog started')) $r.Out
                    # A test that writes to a persistent shared catalog starts no new catalog (found by a real-container smoke run); a catalog write inside the window proves it.
                    Remove-Item $state -ErrorAction SilentlyContinue
                    $r = & $ev $sqlPlan $sx ($sqlEnv + @{ FAKE_STATE_FILE = $state; FAKE_STEP = '20'; FAKE_STARTUPS = '0'; FAKE_WRITE_NOW = '1' })
                    Check 'SQL proof: no catalog started, but a catalog was written inside the run window: AUTHORITATIVE' (($r.Exit -eq 0) -and $r.Prov.Authoritative -and $r.Prov.Sql.Proof.CatalogStartupsInWindow -eq 0 -and $r.Prov.Sql.Proof.CatalogsWrittenInWindow -ge 1) $r.Out
                    Remove-Item $state -ErrorAction SilentlyContinue
                    $r = & $ev $sqlPlan $sx ($sqlEnv + @{ FAKE_STATE_FILE = $state; FAKE_STEP = '20'; FAKE_STARTUPS = '0'; FAKE_WRITE_NOW = '1'; FAKE_UTC_OFFSET = '120' })
                    Check 'SQL proof: the server''s UTC offset is applied (a write 2 hours ahead in server-local time is still inside the window)' (($r.Exit -eq 0) -and $r.Prov.Authoritative -and $r.Prov.Sql.Proof.CatalogsWrittenInWindow -ge 1) $r.Out
                    Remove-Item $state -ErrorAction SilentlyContinue
                    $r = & $ev $sqlPlan $sx ($sqlEnv + @{ FAKE_STATE_FILE = $state; FAKE_STEP = '0'; FAKE_STARTUPS = '0'; FAKE_WRITE_NOW = '1' })
                    Check 'SQL proof: a recent write alone is not enough: the markers must also have moved' (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and ($r.Prov.Reasons -match 'did not move')) $r.Out
                    $r = & $ev $sqlPlan @() $sqlEnv
                    Check 'SQL proof: a SQL run with no container to ask is SqlNotProven, never silently valid' (($r.Exit -eq 1) -and -not $r.Prov.Authoritative -and ($r.Prov.Reasons -match 'markers were not collected')) $r.Out
                    $r = & $ev $sqlPlan $sx ($sqlEnv + @{ FAKE_DOCKER_FAIL = '1' })
                    Check 'SQL proof: a container that cannot be inspected blocks the run in the preflight' (($r.Exit -eq 1) -and $r.Prov.PreflightOk -eq $false -and @($r.Prov.Runs).Count -eq 0) $r.Out
                    $listener.Stop()
                    $r = & $ev $sqlPlan $sx ($sqlEnv + @{ FAKE_STATE_FILE = $state; FAKE_STEP = '20' })
                    Check 'SQL proof: an unreachable server blocks the run: no test is executed' (($r.Exit -eq 1) -and $r.Prov.PreflightOk -eq $false -and @($r.Prov.Runs).Count -eq 0) $r.Out
                }
                finally { try { $listener.Stop() } catch { } }
                [IO.File]::WriteAllText("$fx/tests/Calc.Tests/FailTests.cs", "using Xunit;`nnamespace Calc.Tests;`npublic class FailTests { [Fact] public void Fails() => Assert.Fail(`"could not connect: Server=x;Password=Hunter2;Database=d`"); }`n")
                & git -C $fx add -A 2>&1 | Out-Null; & git -C $fx commit -q -m 'failing test' 2>&1 | Out-Null
                $r = & $ev @(@{ name = 'failing'; project = $proj; filter = 'FullyQualifiedName~Calc.Tests.FailTests' }) @('-Build')
                Check 'a failing test is Failed and the evidence is not authoritative' (($r.Exit -eq 1) -and $r.Prov.Runs[0].Outcome -eq 'Failed' -and -not $r.Prov.Authoritative) $r.Out
                $consoleLog = Get-Content "$($r.Folder)/logs/failing.log" -Raw
                Check 'a password that appears in a failure message is redacted in the stored console log (the log shows the redaction ran)' (($consoleLog -match 'Password=\*\*\*') -and ($consoleLog -notmatch 'Hunter2')) $consoleLog.Substring(0, [Math]::Min(300, $consoleLog.Length))
                Check 'the TRX is stored as the test host wrote it (hashed, not edited), so a secret printed by a test would be in it: tests must not print secrets' ((Get-Content "$($r.Folder)/results/failing.trx" -Raw) -match 'Hunter2')
            }
            # ---------------------------------------------------------------------------------------------------- export
            if (Want-Section 'export') {
                Write-Host 'export: Export-Evidence'
                $ex = Join-Path $val 'Export-Evidence.ps1'; $d = Join-Path $WorkRoot 'exp'; New-Item -ItemType Directory "$d/src/results", "$d/dest" | Out-Null
                [IO.File]::WriteAllText("$d/src/results/a.trx", 'trx'); [IO.File]::WriteAllText("$d/src/provenance.json", '{"Tree":{"Head":"abc"}}'); [void](Write-ChecksumFile "$d/src")
                $r = Invoke-Tool $ex @('-Source', "$d/src", '-DestinationRoot', "$d/dest", '-Name', 'rec1', '-Repo', $repoRoot)
                $dest = Join-Path (Join-Path "$d/dest" (Get-Date -Format 'yyyy-MM-dd')) 'rec1'
                Check 'a verified source is copied, re-hashed and marked' (($r.Exit -eq 0) -and (Test-ChecksumFile $dest -ExcludeNames 'ARCHIVE-VERIFIED.txt').Ok -and (Test-Path "$dest/ARCHIVE-VERIFIED.txt")) $r.Out
                Check 'the archive note says it is not an off-machine backup' ((Get-Content "$dest/ARCHIVE-VERIFIED.txt" -Raw) -match 'NOT an off-machine backup')
                Check 'the same destination is never overwritten' ((Invoke-Tool $ex @('-Source', "$d/src", '-DestinationRoot', "$d/dest", '-Name', 'rec1', '-Repo', $repoRoot)).Exit -eq 2)
                [IO.File]::AppendAllText("$d/src/results/a.trx", 'x'); Check 'a source that no longer verifies is refused' ((Invoke-Tool $ex @('-Source', "$d/src", '-DestinationRoot', "$d/dest", '-Name', 'rec2', '-Repo', $repoRoot)).Exit -eq 2)
                Check 'a destination inside the repository is refused' ((Invoke-Tool $ex @('-Source', "$d/src", '-DestinationRoot', (Join-Path $repoRoot 'artifacts/x'), '-Name', 'rec3', '-Repo', $repoRoot)).Exit -eq 2)
                Check 'a destination inside the source is refused' ((Invoke-Tool $ex @('-Source', "$d/src", '-DestinationRoot', "$d/src/inner", '-Name', 'rec4', '-Repo', $repoRoot)).Exit -eq 2)
            }
        }
    }
}

# ---------------------------------------------------------------------------------------------------------------- end
if (-not $KeepWork -and (Test-Path -LiteralPath (Join-Path $WorkRoot '.validation-selftest'))) {
    Get-ChildItem -LiteralPath $WorkRoot -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Delete() } catch { } }
    Remove-Item -LiteralPath $WorkRoot -Recurse -Force -ErrorAction SilentlyContinue
}
foreach ($leftover in 'artifacts/selftest-fresh', 'artifacts/selftest-busy', 'artifacts/selftest-ws') { $p = Join-Path $repoRoot $leftover; if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction SilentlyContinue } }
''
Write-Host "controls passed: $script:pass; failed: $script:fail; skipped sections: $($script:skipped.Count)"
foreach ($s in $script:skipped) { Write-Host "  SKIPPED $s" -ForegroundColor Yellow }
if ($script:fail -gt 0) { exit 1 }
if ($script:skipped.Count -gt 0) { exit 3 }
exit 0
