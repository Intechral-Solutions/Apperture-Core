#Requires -Version 7.2
<#
.SYNOPSIS
  First toolkit-backed validation record, run beside the established procedure on the same commit (DEV-1.3C, section 9).
.DESCRIPTION
  In a disposable standalone clone at -Commit:
    1. ESTABLISHED procedure: clean builds of the two test projects with the protocol flags
       (--no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers -v:q -nologo, Debug, fresh --artifacts-path), then
       `dotnet test --no-build` with the same selection and a TRX each.
    2. TOOLKIT: Invoke-Evidence -Build with the candidate flags (--no-incremental -m:4 -v:q -nologo, Debug), the same selection, a fresh path.
    3. Compare: Compare-Trx -Strict per run (selected test identities, executed/skipped counts, outcomes), the toolkit's own provenance
       and summary, and the managed assemblies of both build outputs (raw and normalised hashes).
  The established procedure stays authoritative; this only compares. No SQL is used (the selection needs none).
#>
param(
    [Parameter(Mandatory)][string]$CloneRoot,
    [Parameter(Mandatory)][string]$SourceRepo,
    [Parameter(Mandatory)][string]$Commit,
    [Parameter(Mandatory)][string]$EvidenceDir,
    [string]$UnitFilter = 'FullyQualifiedName~Apperture.UnitTests.Accounts.'
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $CloneRoot) { throw "Clone path exists: $CloneRoot" }
New-Item -ItemType Directory -Path $EvidenceDir -Force | Out-Null
git clone --quiet --no-hardlinks $SourceRepo $CloneRoot
# the preflight requires the origin to name Apperture-Core: a clone of a local path would not; use the source repository's own remote URL (configuration only, no fetch)
$originUrl = (git -C $SourceRepo remote get-url origin).Trim()
git -C $CloneRoot remote set-url origin $originUrl
git -C $CloneRoot checkout --quiet --detach $Commit
if ((git -C $CloneRoot rev-parse HEAD).Trim() -ne $Commit) { throw 'clone is not at the commit' }
if (@(git -C $CloneRoot status --porcelain --untracked-files=all).Count) { throw 'clone not clean' }
$tree = (git -C $CloneRoot rev-parse 'HEAD^{tree}').Trim()
$units = 'tests/Apperture.UnitTests/Apperture.UnitTests.csproj'; $arch = 'tests/Apperture.ArchTests/Apperture.ArchTests.csproj'
$log = [System.Collections.Generic.List[string]]::new()
function Note([string]$s) { $log.Add($s); $s }
Note "commit $Commit tree $tree sdk $(dotnet --version) started $(Get-Date -Format o)"

function Time-Step([string]$Name, [string[]]$A, [string]$LogFile) {
    $sw = [Diagnostics.Stopwatch]::StartNew(); & dotnet @A *> $LogFile; $c = $LASTEXITCODE; $sw.Stop()
    Note ("{0}: exit {1}, {2:N1} s" -f $Name, $c, $sw.Elapsed.TotalSeconds)
    if ($c -ne 0) { throw "$Name failed (exit $c), see $LogFile" }
}

# ---- 1. established procedure -------------------------------------------------------------------------------------
$est = Join-Path $CloneRoot 'artifacts\est'
$estFlags = @('--no-incremental', '-m:1', '-p:UseSharedCompilation=false', '--disable-build-servers', '-v:q', '-nologo')
Time-Step 'established build UnitTests' (@('build', (Join-Path $CloneRoot $units), '--artifacts-path', $est) + $estFlags) (Join-Path $EvidenceDir 'est-build-units.log')
Time-Step 'established build ArchTests' (@('build', (Join-Path $CloneRoot $arch), '--artifacts-path', $est) + $estFlags) (Join-Path $EvidenceDir 'est-build-arch.log')
Time-Step 'established test UnitTests' @('test', (Join-Path $CloneRoot $units), '--no-build', '--artifacts-path', $est, '--filter', $UnitFilter, '--logger', 'trx;LogFileName=est-units.trx', '--results-directory', (Join-Path $EvidenceDir 'est-trx'), '-nologo') (Join-Path $EvidenceDir 'est-test-units.log')
Time-Step 'established test ArchTests' @('test', (Join-Path $CloneRoot $arch), '--no-build', '--artifacts-path', $est, '--logger', 'trx;LogFileName=est-arch.trx', '--results-directory', (Join-Path $EvidenceDir 'est-trx'), '-nologo') (Join-Path $EvidenceDir 'est-test-arch.log')

# ---- 2. toolkit ---------------------------------------------------------------------------------------------------
$plan = Join-Path $EvidenceDir 'plan.json'
@{ runs = @(
        @{ name = 'units'; project = $units; filter = $UnitFilter; requiresSql = $false; expectedMinTests = 50 }
        @{ name = 'arch'; project = $arch; requiresSql = $false; expectedMinTests = 30 }
    ) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $plan -Encoding utf8
$tkRoot = Join-Path $CloneRoot 'artifacts/evidence/dev13c-first'
$sw = [Diagnostics.Stopwatch]::StartNew()
& pwsh -NoProfile -File (Join-Path $CloneRoot 'scripts/dev/validation/Invoke-Evidence.ps1') -Record 'dev13c-first' -Plan $plan -Repo $CloneRoot -ExpectedCommit $Commit `
    -Configuration Debug -Build '-BuildArgs:--no-incremental,-m:4,-v:q,-nologo' -ProcessEnvironmentOnly *> (Join-Path $EvidenceDir 'toolkit-console.log')
$tkExit = $LASTEXITCODE; $sw.Stop()
Note ("toolkit Invoke-Evidence: exit {0} (0 = authoritative), {1:N1} s" -f $tkExit, $sw.Elapsed.TotalSeconds)
dotnet build-server shutdown *> $null
# the tool writes working evidence under the clone's own artifacts/ (it refuses a destination elsewhere); copy it out, then use the copy
$tkSrc = Get-ChildItem -LiteralPath $tkRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
if (-not $tkSrc) { Note 'the toolkit wrote no evidence folder'; $log | Set-Content -LiteralPath (Join-Path $EvidenceDir 'first-record-run.txt') -Encoding utf8; throw 'no toolkit evidence' }
$tkOut = Join-Path $EvidenceDir 'toolkit'
Copy-Item -LiteralPath $tkSrc.FullName -Destination $tkOut -Recurse
Note "toolkit evidence folder: $($tkSrc.Name)"

# ---- 3. compare ---------------------------------------------------------------------------------------------------
$cmp = Join-Path $CloneRoot 'scripts/dev/validation/Compare-Trx.ps1'
foreach ($r in @(@{ n = 'units'; e = 'est-units.trx' }, @{ n = 'arch'; e = 'est-arch.trx' })) {
    $cand = Get-ChildItem -LiteralPath (Join-Path $tkOut 'results') -Filter *.trx -Recurse | Where-Object { $_.Name -like "*$($r.n)*" } | Select-Object -First 1
    if (-not $cand) { Note "no toolkit TRX found for $($r.n)"; continue }
    & pwsh -NoProfile -File $cmp -Baseline (Join-Path $EvidenceDir "est-trx\$($r.e)") -Candidate $cand.FullName -Strict *> (Join-Path $EvidenceDir "compare-$($r.n).txt")
    Note "Compare-Trx -Strict $($r.n): exit $LASTEXITCODE (0 = identical tests and outcomes)"
}
$log | Set-Content -LiteralPath (Join-Path $EvidenceDir 'first-record-run.txt') -Encoding utf8
