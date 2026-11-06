#Requires -Version 7.2
<#
  Mutation check of the build-argument allow-list controls (DEV-1.3C). For each single-site mutant of lib/ValidationCommon.ps1 or
  Invoke-Mutation.ps1, copy scripts/dev/validation into a scratch folder inside the (ignored) artifacts folder, apply the edit to the copy,
  run `Test-Validation.ps1 -Only buildargs` there, and require at least one failing control. The tracked files are never edited.
#>
param([Parameter(Mandatory)][string]$Repo, [Parameter(Mandatory)][string]$OutDir)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$mutants = @(
    @{ Id = 'AL01'; File = 'lib/ValidationCommon.ps1'; Find = ')\z"'; Replace = ')$"'; Why = 'anchor \z weakened to $ (a trailing newline would match)' }
    @{ Id = 'AL02'; File = 'lib/ValidationCommon.ps1'; Find = '$exact -ccontains $a'; Replace = '$exact -contains $a'; Why = 'exact flags compared case-insensitively' }
    @{ Id = 'AL03'; File = 'lib/ValidationCommon.ps1'; Find = "'-p:UseSharedCompilation=false')"; Replace = "'-p:UseSharedCompilation=false', '-p:UseSharedCompilation=true')"; Why = 'a second property accepted' }
    @{ Id = 'AL04'; File = 'lib/ValidationCommon.ps1'; Find = '(?:nologo|'; Replace = '(?:nologo|p:[^ ]+|'; Why = 'any -p: property accepted as a reporting switch' }
    @{ Id = 'AL05'; File = 'lib/ValidationCommon.ps1'; Find = '[0-9]{1,3}'; Replace = '[0-9]*'; Why = '-m value unbounded / empty' }
    @{ Id = 'AL06'; File = 'lib/ValidationCommon.ps1'; Find = "`$levels = 'q|quiet|m|minimal|n|normal|d|detailed|diag|diagnostic'"; Replace = "`$levels = '.+'"; Why = 'verbosity level unconstrained' }
    @{ Id = 'AL07'; File = 'lib/ValidationCommon.ps1'; Find = "`$logger = '[A-Za-z]+(?:=[A-Za-z0-9]+)?(?:;[A-Za-z]+(?:=[A-Za-z0-9]+)?)*'"; Replace = "`$logger = '.+'"; Why = 'logger parameters unconstrained' }
    @{ Id = 'AL08'; File = 'lib/ValidationCommon.ps1'; Find = '(?:nr|nodereuse)[:=](?:true|false)'; Replace = '(?:nr|nodereuse)(?:[:=].*)?'; Why = 'node reuse value unconstrained' }
    @{ Id = 'AL09'; File = 'lib/ValidationCommon.ps1'; Find = '        $rejected += $a
    }'; Replace = '        $null = $a
    }'; Why = 'nothing is ever rejected' }
    @{ Id = 'AL10'; File = 'lib/ValidationCommon.ps1'; Find = "'--no-incremental', '--disable-build-servers', '-p:UseSharedCompilation=false')"; Replace = "'--no-incremental', '-p:UseSharedCompilation=false')"; Why = 'an accepted protocol flag dropped' }
    @{ Id = 'AL11'; File = 'lib/ValidationCommon.ps1'; Find = "if (`$null -eq `$a) { `$rejected += '<null>'; continue }"; Replace = "if (`$null -eq `$a) { continue }"; Why = 'a null argument silently accepted' }
    @{ Id = 'AL12'; File = 'Invoke-Mutation.ps1'; Find = 'if ($unvetted.Count) {'; Replace = 'if ($false) {'; Why = 'Invoke-Mutation no longer refuses unvetted arguments' }
    @{ Id = 'AL13'; File = 'Invoke-Evidence.ps1'; Find = '$propArgs = @(Get-UnvettedBuildArgs $BuildArgs)'; Replace = '$propArgs = @()'; Why = 'Invoke-Evidence no longer vets build arguments' }
    @{ Id = 'AL14'; File = 'lib/ValidationCommon.ps1'; Find = '(?:--?|/)(?:nologo'; Replace = '(?:--?|/)?(?:nologo'; Why = 'prefix optional (a bare word such as "o" or "nologo-x" style forms)' }
    # Added after the independent review (round 1, F1): these two escaped the first 14 controls.
    @{ Id = 'AL15'; File = 'lib/ValidationCommon.ps1'; Find = '$reporting = "^(?:--?|/)'; Replace = '$reporting = "(?:--?|/)'; Why = 'start anchor dropped (any argument that merely ENDS in an accepted switch matches, e.g. -o:D:\x\-m)' }
    @{ Id = 'AL16'; File = 'lib/ValidationCommon.ps1'; Find = '(?:--?|/)(?:nologo'; Replace = '(?:--?|/|\+)(?:nologo'; Why = 'a leading plus accepted as a switch prefix' }
)
$results = @()
foreach ($m in $mutants) {
    $copyRoot = Join-Path $Repo "artifacts/dev13c/mut-allow/$($m.Id)"
    if (Test-Path -LiteralPath $copyRoot) { Remove-Item -LiteralPath $copyRoot -Recurse -Force }
    $dst = Join-Path $copyRoot 'scripts/dev/validation'
    New-Item -ItemType Directory -Path $dst -Force | Out-Null
    Copy-Item -Path (Join-Path $Repo 'scripts/dev/validation/*') -Destination $dst -Recurse -Force
    $target = Join-Path $dst $m.File
    $text = [IO.File]::ReadAllText($target)
    $first = $text.IndexOf($m.Find, [StringComparison]::Ordinal)
    if ($first -lt 0) { $text2 = $text.Replace("`r`n", "`n"); $first = $text2.IndexOf($m.Find.Replace("`r`n", "`n"), [StringComparison]::Ordinal); if ($first -ge 0) { $text = $text2; $m.Find = $m.Find.Replace("`r`n", "`n") } }
    if ($first -lt 0) { $results += [pscustomobject]@{ Id = $m.Id; Why = $m.Why; Outcome = 'NOT APPLIED (text not found)'; Failing = '' }; continue }
    if ($text.IndexOf($m.Find, $first + 1, [StringComparison]::Ordinal) -ge 0) { $results += [pscustomobject]@{ Id = $m.Id; Why = $m.Why; Outcome = 'NOT APPLIED (text occurs more than once)'; Failing = '' }; continue }
    [IO.File]::WriteAllText($target, $text.Substring(0, $first) + $m.Replace + $text.Substring($first + $m.Find.Length))
    $out = & pwsh -NoProfile -File (Join-Path $dst 'tests/Test-Validation.ps1') -Only buildargs 2>&1 | ForEach-Object { [string]$_ }
    $code = $LASTEXITCODE
    $fails = @($out | Where-Object { $_ -match '^\s*FAIL ' } | ForEach-Object { $_.Trim() })
    $out | Set-Content -LiteralPath (Join-Path $OutDir "$($m.Id).log")
    $results += [pscustomobject]@{ Id = $m.Id; Why = $m.Why; Outcome = $(if ($fails.Count -gt 0 -and $code -ne 0) { "CAUGHT ($($fails.Count) controls)" } else { 'ESCAPED' }); Failing = ($fails | Select-Object -First 3) -join ' | ' }
    Remove-Item -LiteralPath $copyRoot -Recurse -Force
}
$results | Format-Table Id, Outcome, Why -AutoSize | Out-String -Width 220
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutDir 'summary.json')
