#Requires -Version 7.2
<#
.SYNOPSIS
  Second toolkit pass of the first record (DEV-1.3C, section 9), in the clone Run-FirstRecord.ps1 left behind.
.DESCRIPTION
  Two differences from the first toolkit pass, both deliberate:
  1. the plan names the one opt-in skipped test (allowNotExecuted takes the exact, case-sensitive test name), so the record can be authoritative;
  2. the artifacts path is `artifacts/tkt`, the same length as the established run's `artifacts/est`, so that path-derived bytes in the
     assemblies have the same length (a different length shifts PE section sizes and offsets, which the hash normaliser does not undo).
  Then compares TRX (Compare-Trx -Strict) and the managed assemblies of the two outputs.
#>
param(
    [Parameter(Mandatory)][string]$CloneRoot,
    [Parameter(Mandatory)][string]$Commit,
    [Parameter(Mandatory)][string]$EvidenceDir,      # the first-record evidence folder (holds est-trx and plan.json)
    [string]$UnitFilter = 'FullyQualifiedName~Apperture.UnitTests.Accounts.'
)
$ErrorActionPreference = 'Stop'
$units = 'tests/Apperture.UnitTests/Apperture.UnitTests.csproj'; $arch = 'tests/Apperture.ArchTests/Apperture.ArchTests.csproj'
$skip = 'Apperture.UnitTests.Accounts.PasswordEvaluationDesktopTimingDiagnostics.Records_desktop_zxcvbn_timings_for_representative_passwords'
$plan = Join-Path $EvidenceDir 'plan2.json'
@{ runs = @(
        @{ name = 'units'; project = $units; filter = $UnitFilter; requiresSql = $false; expectedMinTests = 50; allowNotExecuted = @($skip) }
        @{ name = 'arch'; project = $arch; requiresSql = $false; expectedMinTests = 30 }
    ) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $plan -Encoding utf8
$art = Join-Path $CloneRoot 'artifacts/tkt'
if (Test-Path -LiteralPath $art) { throw "exists: $art" }
$sw = [Diagnostics.Stopwatch]::StartNew()
& pwsh -NoProfile -File (Join-Path $CloneRoot 'scripts/dev/validation/Invoke-Evidence.ps1') -Record 'dev13c-first-b' -Plan $plan -Repo $CloneRoot -ExpectedCommit $Commit `
    -Configuration Debug -ArtifactsPath $art -Build '-BuildArgs:--no-incremental,-m:4,-v:q,-nologo' -ProcessEnvironmentOnly *> (Join-Path $EvidenceDir 'toolkit2-console.log')
$exit = $LASTEXITCODE; $sw.Stop()
dotnet build-server shutdown *> $null
"toolkit pass 2: exit $exit (0 = authoritative), $([math]::Round($sw.Elapsed.TotalSeconds, 1)) s"
$src = Get-ChildItem -LiteralPath (Join-Path $CloneRoot 'artifacts/evidence/dev13c-first-b') -Directory | Sort-Object Name | Select-Object -Last 1
Copy-Item -LiteralPath $src.FullName -Destination (Join-Path $EvidenceDir 'toolkit2') -Recurse
$cmp = Join-Path $CloneRoot 'scripts/dev/validation/Compare-Trx.ps1'
foreach ($r in @(@{ n = 'units'; e = 'est-units.trx' }, @{ n = 'arch'; e = 'est-arch.trx' })) {
    $cand = Get-ChildItem -LiteralPath (Join-Path $EvidenceDir 'toolkit2/results') -Filter *.trx -Recurse | Where-Object { $_.Name -like "*$($r.n)*" } | Select-Object -First 1
    & pwsh -NoProfile -File $cmp -Baseline (Join-Path $EvidenceDir "est-trx\$($r.e)") -Candidate $cand.FullName -Strict *> (Join-Path $EvidenceDir "compare2-$($r.n).txt")
    "Compare-Trx -Strict $($r.n): exit $LASTEXITCODE"
}
$h = $PSScriptRoot
& pwsh -NoProfile -File (Join-Path $h 'Get-AssemblyInventory.ps1') -ArtifactsPath (Join-Path $CloneRoot 'artifacts/est') -OutFile (Join-Path $EvidenceDir 'inventory-est.csv') -MaskRegexGeneratorHash
& pwsh -NoProfile -File (Join-Path $h 'Get-AssemblyInventory.ps1') -ArtifactsPath $art -OutFile (Join-Path $EvidenceDir 'inventory-tkt.csv') -MaskRegexGeneratorHash
$a = Import-Csv (Join-Path $EvidenceDir 'inventory-est.csv'); $b = Import-Csv (Join-Path $EvidenceDir 'inventory-tkt.csv')
$ha = @{}; $a | ForEach-Object { $ha[$_.Path] = $_ }; $hb = @{}; $b | ForEach-Object { $hb[$_.Path] = $_ }
$common = @($ha.Keys | Where-Object { $hb.ContainsKey($_) })
$mg = @($common | Where-Object { $ha[$_].Kind -eq 'pe-managed' })
$out = @("files: established $($a.Count), toolkit $($b.Count), common $($common.Count), only established $(@($ha.Keys | Where-Object { -not $hb.ContainsKey($_) }).Count), only toolkit $(@($hb.Keys | Where-Object { -not $ha.ContainsKey($_) }).Count)",
    "managed assemblies in common: $($mg.Count); raw-identical: $(@($mg | Where-Object { $ha[$_].Sha256 -eq $hb[$_].Sha256 }).Count); identical after normalisation and masking: $(@($mg | Where-Object { $ha[$_].NormalizedSha256 -eq $hb[$_].NormalizedSha256 }).Count)")
$diff = @($mg | Where-Object { $ha[$_].NormalizedSha256 -ne $hb[$_].NormalizedSha256 })
$out += "managed assemblies still different: $($diff.Count)"; $out += $diff | ForEach-Object { "  $_" }
$out | Set-Content -LiteralPath (Join-Path $EvidenceDir 'binary-identity.txt') -Encoding utf8
$out
