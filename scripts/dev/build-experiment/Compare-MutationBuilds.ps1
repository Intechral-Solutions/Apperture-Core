#Requires -Version 7.2
<#
.SYNOPSIS
  Controlled comparison of one mutation outcome under two validation build configurations (DEV-1.3C, section 8).
.DESCRIPTION
  Does NOT use Invoke-Mutation -BuildArgs (not vetted). Instead, in a disposable standalone clone at the named commit, for each configuration:
    1. clean build of the test project into a fresh artifacts path, then the focused tests   (baseline, must pass)
    2. for each mutation: apply one exact text edit (must match exactly once), clean build into a fresh artifacts path with the SAME flags,
       run the same focused tests, record the failing tests, hash the mutated production assembly, restore the file, verify the blob hash.
  Configurations (build flags after `dotnet build <project> --artifacts-path <fresh> `):
    A : --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers -v:q -nologo      (established protocol)
    C : --no-incremental -m:4 -v:q -nologo                                                            (candidate)
  Tests: dotnet test <project> --no-build --artifacts-path <same> --filter <filter> --logger trx
  Output: <EvidenceDir>\mutation-comparison.json and .md plus every log and TRX.
#>
param(
    [Parameter(Mandatory)][string]$CloneRoot,       # new, standalone clone location (must not exist)
    [Parameter(Mandatory)][string]$SourceRepo,
    [Parameter(Mandatory)][string]$Commit,
    [Parameter(Mandatory)][string]$EvidenceDir,
    [string]$Project = 'tests/Apperture.UnitTests/Apperture.UnitTests.csproj',
    [string]$Filter = 'FullyQualifiedName~PasswordPolicyEvaluatorTests',
    [string]$Target = 'src/Apperture.Core/Apperture.Core.Domain/Identity/PasswordPolicyEvaluator.cs',
    [string]$MutatedAssemblyName = 'Apperture.Core.Domain.dll',
    [string[]]$Configs = @('A', 'C')
)
$ErrorActionPreference = 'Stop'
# Get-AssemblyCodeHash hashes what an assembly does (IL, string literals, constants), not its bytes. Every build here goes to a different
# artifacts path, and a raw file hash differs between paths even for identical source, so the raw hash cannot show that a mutation was compiled.
. (Join-Path $PSScriptRoot '../validation/lib/ValidationCommon.ps1')
$Configs = @($Configs | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
if (Test-Path -LiteralPath $CloneRoot) { throw "Clone path exists: $CloneRoot" }
New-Item -ItemType Directory -Path $EvidenceDir -Force | Out-Null
$flags = @{
    A = @('--no-incremental', '-m:1', '-p:UseSharedCompilation=false', '--disable-build-servers', '-v:q', '-nologo')
    C = @('--no-incremental', '-m:4', '-v:q', '-nologo')
}
$mutations = @(
    # M0 is a control that must NOT count as a mutation: a comment inside the expression compiles to identical code, so the validity check
    # has to classify it "code unchanged". If it does not, the check cannot tell a compiled mutation from a no-op.
    [pscustomobject]@{ Id = 'M0'; Find = 'password.Length >= profile.MinimumLength'; Replace = 'password.Length >= profile.MinimumLength /* no-op control */'; Declared = 'unchanged' }
    [pscustomobject]@{ Id = 'M1'; Find = 'password.Length >= profile.MinimumLength'; Replace = 'password.Length >= 1'; Declared = 'caught' }
    [pscustomobject]@{ Id = 'M2'; Find = 'password.Length >= profile.MinimumLength'; Replace = 'password.Length > profile.MinimumLength'; Declared = 'escaped' }
)

git clone --quiet --no-hardlinks $SourceRepo $CloneRoot
git -C $CloneRoot checkout --quiet --detach $Commit
if ((git -C $CloneRoot rev-parse HEAD).Trim() -ne $Commit) { throw 'clone is not at the commit' }
if (@(git -C $CloneRoot status --porcelain).Count) { throw 'clone not clean' }
$targetPath = Join-Path $CloneRoot $Target
$blobHash = (git -C $CloneRoot rev-parse "HEAD:$Target").Trim()
function Get-BlobHashOfFile($p) { (git -C $CloneRoot hash-object -- $p).Trim() }

function Run-Step([string]$Name, [string[]]$ArgList, [string]$Log) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & dotnet @ArgList *> $Log
    $code = $LASTEXITCODE
    $sw.Stop()
    [pscustomobject]@{ Name = $Name; ExitCode = $code; Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1) }
}
function Parse-Trx([string]$dir) {
    $trx = Get-ChildItem -LiteralPath $dir -Filter *.trx -Recurse | Select-Object -First 1
    if (-not $trx) { return $null }
    $x = [xml](Get-Content -LiteralPath $trx.FullName -Raw)
    $c = $x.TestRun.ResultSummary.Counters
    $failed = @($x.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq 'Failed' } | ForEach-Object { $_.testName } | Sort-Object)
    $all = @($x.TestRun.Results.UnitTestResult | ForEach-Object { $_.testName } | Sort-Object)
    [pscustomobject]@{ Total = [int]$c.total; Executed = [int]$c.executed; Passed = [int]$c.passed; Failed = [int]$c.failed; FailedTests = $failed; AllTests = $all }
}
function Build-AndTest([string]$Cfg, [string]$Label) {
    $art = Join-Path $CloneRoot "artifacts\$Cfg-$Label"
    $log = Join-Path $EvidenceDir "$Cfg-$Label"
    $freeMiB = [int](New-Object System.Diagnostics.PerformanceCounter('Memory', 'Available MBytes')).NextValue()
    if ($freeMiB -lt 2048) { throw "available memory $freeMiB MiB below 2048" }
    $b = Run-Step "build $Cfg $Label" (@('build', (Join-Path $CloneRoot $Project), '--artifacts-path', $art) + $flags[$Cfg]) "$log.build.log"
    if ($b.ExitCode -ne 0) { return [pscustomobject]@{ Cfg = $Cfg; Label = $Label; Build = $b; Test = $null; Trx = $null; Hash = $null; CodeHash = $null } }
    $resDir = Join-Path $EvidenceDir "$Cfg-$Label-trx"
    $t = Run-Step "test $Cfg $Label" @('test', (Join-Path $CloneRoot $Project), '--no-build', '--artifacts-path', $art, '--filter', $Filter, '--logger', 'trx', '--results-directory', $resDir, '-v:q', '-nologo') "$log.test.log"
    $asm = Get-ChildItem -LiteralPath (Join-Path $art 'bin') -Recurse -Filter $MutatedAssemblyName | Where-Object { $_.FullName -match 'Apperture\.Core\.Domain\\' } | Select-Object -First 1
    $hash = if ($asm) { (Get-FileHash -LiteralPath $asm.FullName -Algorithm SHA256).Hash } else { $null }
    $codeHash = if ($asm) { Get-AssemblyCodeHash $asm.FullName } else { $null }
    [pscustomobject]@{ Cfg = $Cfg; Label = $Label; Build = $b; Test = $t; Trx = (Parse-Trx $resDir); Hash = $hash; CodeHash = $codeHash }
}

$results = @()
foreach ($cfg in $Configs) {
    $results += Build-AndTest $cfg 'baseline'
    foreach ($m in $mutations) {
        $bytes = [IO.File]::ReadAllBytes($targetPath)
        $text = [Text.Encoding]::UTF8.GetString($bytes)
        $first = $text.IndexOf($m.Find, [StringComparison]::Ordinal)
        if ($first -lt 0 -or $text.IndexOf($m.Find, $first + 1, [StringComparison]::Ordinal) -ge 0) { throw "mutation $($m.Id): the text must occur exactly once" }
        $mutated = $text.Substring(0, $first) + $m.Replace + $text.Substring($first + $m.Find.Length)
        [IO.File]::WriteAllBytes($targetPath, [Text.Encoding]::UTF8.GetBytes($mutated))
        try { $results += Build-AndTest $cfg $m.Id }
        finally {
            [IO.File]::WriteAllBytes($targetPath, $bytes)
            (Get-Item -LiteralPath $targetPath).LastWriteTime = Get-Date
            if ((Get-BlobHashOfFile $targetPath) -ne $blobHash) { throw "restore of $Target did not reproduce blob $blobHash" }
        }
    }
    dotnet build-server shutdown *> $null   # servers started by this configuration's builds (none for config A)
}
if (@(git -C $CloneRoot status --porcelain).Count) { throw 'clone dirty after the run' }

$base = @{}; foreach ($r in $results | Where-Object { $_.Label -eq 'baseline' }) { $base[$r.Cfg] = $r }
$rows = foreach ($r in $results) {
    $outcome = if ($r.Label -eq 'baseline') { if ($r.Trx -and $r.Trx.Failed -eq 0 -and $r.Trx.Passed -gt 0) { 'baseline passed' } else { 'BASELINE NOT CLEAN' } }
    elseif (-not $r.Trx -or -not $r.CodeHash -or -not $base[$r.Cfg].CodeHash) { 'Invalid (no result or no code hash)' }
    elseif ($r.CodeHash -eq $base[$r.Cfg].CodeHash) { if ($r.Label -eq 'M0') { 'Control ok (code unchanged)' } else { 'Invalid (code unchanged)' } }
    elseif ($r.Label -eq 'M0') { 'CONTROL FAILED (no-op edit changed the code hash)' }
    elseif ($r.Trx.Failed -gt 0) { 'Caught' } else { 'Escaped' }
    [pscustomobject]@{ Config = $r.Cfg; Run = $r.Label; Outcome = $outcome; BuildExit = $r.Build.ExitCode; BuildSeconds = $r.Build.Seconds
        Executed = $(if ($r.Trx) { $r.Trx.Executed }); Passed = $(if ($r.Trx) { $r.Trx.Passed }); Failed = $(if ($r.Trx) { $r.Trx.Failed }); FailedTests = $(if ($r.Trx) { $r.Trx.FailedTests -join '; ' }); AssemblyHash = $r.Hash; CodeHash = $r.CodeHash }
}
# Path-independence control: the code hash of the same source must not depend on the artifacts path or the flags. The baselines of the
# configurations were built from the same source into different paths with different flags.
$baseCodeHashes = @($base.Values | ForEach-Object { $_.CodeHash } | Sort-Object -Unique)
$pathControl = if ($base.Count -lt 2) { 'not applicable (one configuration)' } elseif ($baseCodeHashes.Count -eq 1 -and $baseCodeHashes[0]) { 'ok: baselines of all configurations have the same code hash' } else { 'FAILED: baselines differ in code hash' }
@{ PathIndependenceControl = $pathControl; BaselineRawHashes = @($base.Values | ForEach-Object { "$($_.Cfg)=$($_.Hash)" }); BaselineCodeHashes = @($base.Values | ForEach-Object { "$($_.Cfg)=$($_.CodeHash)" }) } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDir 'mutation-controls.json') -Encoding utf8
"path-independence control: $pathControl"
$rows | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $EvidenceDir 'mutation-comparison.json') -Encoding utf8
$rows | Format-Table Config, Run, Outcome, Executed, Passed, Failed, BuildSeconds -AutoSize | Out-String -Width 200 | Set-Content -LiteralPath (Join-Path $EvidenceDir 'mutation-comparison.txt') -Encoding utf8
$rows | Format-Table Config, Run, Outcome, Executed, Passed, Failed, BuildSeconds -AutoSize | Out-String -Width 200
foreach ($r in $rows | Where-Object { $_.FailedTests }) { "$($r.Config)/$($r.Run): $($r.FailedTests)" }
