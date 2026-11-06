#Requires -Version 7.2
param(
    [Parameter(Mandatory)][string]$Reference,       # trial name used as the reference, e.g. A1
    [Parameter(Mandatory)][string[]]$Others,
    [string]$EvidenceRoot = (Join-Path ((git -C $PSScriptRoot rev-parse --show-toplevel).Trim()) 'artifacts/dev13c/evidence'),
    [string]$OutFile,
    [string]$InventoryName = 'inventory.csv'
)
$Others = @($Others | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
function Load($t) { $h = @{}; Import-Csv (Join-Path $EvidenceRoot "$t\$InventoryName") | ForEach-Object { $h[$_.Path] = $_ }; $h }
$ref = Load $Reference
$lines = [System.Collections.Generic.List[string]]::new()
foreach ($o in $Others) {
    $x = Load $o
    $onlyRef = @($ref.Keys | Where-Object { -not $x.ContainsKey($_) })
    $onlyOth = @($x.Keys | Where-Object { -not $ref.ContainsKey($_) })
    $common = @($ref.Keys | Where-Object { $x.ContainsKey($_) })
    $rawEq = 0; $rawDiffNormEq = 0; $normDiff = @(); $nonPeRawDiff = @(); $nativeRawDiff = @()
    foreach ($p in $common) {
        $a = $ref[$p]; $b = $x[$p]
        if ($a.Sha256 -eq $b.Sha256) { $rawEq++; continue }
        if ($a.Kind -eq 'pe-managed' -and $b.Kind -eq 'pe-managed') {
            if ($a.NormalizedSha256 -eq $b.NormalizedSha256) { $rawDiffNormEq++ } else { $normDiff += $p }
        }
        elseif ($a.Kind -like 'pe-*') { $nativeRawDiff += $p } else { $nonPeRawDiff += $p }
    }
    $managed = @($common | Where-Object { $ref[$_].Kind -eq 'pe-managed' }).Count
    $lines.Add("=== $Reference vs $o")
    $lines.Add("files: reference $($ref.Count), other $($x.Count), common $($common.Count), only in reference $($onlyRef.Count), only in other $($onlyOth.Count)")
    $lines.Add("common files raw-identical: $rawEq")
    $lines.Add("managed assemblies (common): $managed; raw differs but normalised identical: $rawDiffNormEq; normalised DIFFERENT: $($normDiff.Count)")
    $lines.Add("native PE (apphost) raw-different: $($nativeRawDiff.Count); other files raw-different: $($nonPeRawDiff.Count)")
    foreach ($p in ($onlyRef | Select-Object -First 15)) { $lines.Add("  only-in-$Reference  $p") }
    foreach ($p in ($onlyOth | Select-Object -First 15)) { $lines.Add("  only-in-$o  $p") }
    foreach ($p in ($normDiff | Select-Object -First 40)) { $lines.Add("  NORM-DIFF  $p") }
    foreach ($p in ($nativeRawDiff | Select-Object -First 15)) { $lines.Add("  native-raw-diff  $p") }
    foreach ($p in ($nonPeRawDiff | Select-Object -First 40)) { $lines.Add("  other-raw-diff  $p") }
}
$lines
if ($OutFile) { $lines | Set-Content -LiteralPath $OutFile -Encoding utf8 }
