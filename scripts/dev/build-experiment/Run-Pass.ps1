#Requires -Version 7.2
param(
    [Parameter(Mandatory)][string[]]$Trials,          # e.g. A1,B1,C1,D1  (letter(s) = configuration, remainder = repeat number)
    [Parameter(Mandatory)][string]$ExpectedCommit,
    [int]$CooldownSeconds = 60,
    [string]$SamePathName                              # same-path control: every trial builds into out\<SamePathName>, moved to out\<SamePathName>-<trial> afterwards
    ,[switch]$AllowVisualStudio
)
$ErrorActionPreference = 'Stop'
$Trials = @($Trials | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$repo = (git -C $PSScriptRoot rev-parse --show-toplevel).Trim()
$h = $PSScriptRoot
$outRoot = Join-Path $repo 'artifacts\dev13c\out'
foreach ($t in $Trials) {
    $cfg = $t -replace '_?\d+$', ''     # A1 -> A, D1 -> D, D0_1 -> D0
    "[$(Get-Date -Format o)] starting $t"
    $extra = @(); if ($SamePathName) { $extra = @('-OutputName', $SamePathName) }
    if ($AllowVisualStudio) { $extra += '-AllowVisualStudio' }
    & pwsh -NoProfile -File (Join-Path $h 'Invoke-BuildTrial.ps1') -Config $cfg -Name $t -ExpectedCommit $ExpectedCommit -RepoRoot $repo `
        -OutputRoot $outRoot -EvidenceRoot (Join-Path $repo 'artifacts\dev13c\evidence') -AllowKnownIdleProcess @extra
    if ($LASTEXITCODE -ne 0) { "[$(Get-Date -Format o)] TRIAL $t FAILED with exit $LASTEXITCODE - pass stopped"; exit 1 }
    if ($SamePathName) {
        Move-Item -LiteralPath (Join-Path $outRoot $SamePathName) -Destination (Join-Path $outRoot "$SamePathName-$t")
        "[$(Get-Date -Format o)] moved out\$SamePathName to out\$SamePathName-$t"
    }
    "[$(Get-Date -Format o)] finished $t; cooling down $CooldownSeconds s"
    Start-Sleep -Seconds $CooldownSeconds
}
"[$(Get-Date -Format o)] pass complete"
