#Requires -Version 7.2
<#
.SYNOPSIS
    Runs deliberate regression controls (mutations of production source) in a disposable clone, or classifies a preserved legacy log.
.DESCRIPTION
    RUN (default). Each mutation is an exact-text edit of one tracked file. The edit is applied only inside a standalone clone that this
    script creates from -Source at -Commit (never in -Source, never in a worktree of it, never in any -ProtectedRoot). Then, per mutation:
    build (a build failure is invalid, not a catch) -> prove the compiled assembly of the edited project changed -> run the named tests
    with a TRX -> restore the file byte for byte and prove it against the Git blob -> next. A baseline run of the same tests on the
    unedited tree comes first and must be green, otherwise nothing that follows means anything. After the last mutation the tree is
    rebuilt from restored source and the assemblies must equal the baseline's: a restored source file with a stale mutated binary is
    detected, not assumed away.

    A mutation that should be caught but passes is ESCAPED and fails the run. A mutation is CAUGHT only when at least one test failed for
    a reason other than infrastructure (database unreachable, login, timeout). No tests executed, an unreadable TRX, a build failure, an
    unchanged assembly or an all-infrastructure failure are INVALID: they are neither a catch nor an escape and also fail the run.
    Runs that need SQL Server (requiresSql) are refused when the SQL target is not configured in the process environment and reachable;
    the clone has no .env, so SQL comes from APPERTURE_TEST_SQL_CONNECTION_STRING only.

    REPLAY (-ReplaySummary). Read-only classification of a legacy mutation summary file (the WP4B-B format: one block per mutation with
    'unit:' / 'sql:' result lines and 'restored:'). Used to reproduce recorded caught / escaped counts from preserved evidence without
    running anything from the branch that produced it.

    Exit codes: 0 = every mutation caught as expected and all proofs hold; 1 = an escape, an invalid result, a failed proof or an unmet
    expectation; 2 = refused or tool error (nothing meaningful was run).
.PARAMETER Mutations      JSON file: { "mutations": [ { "id", "target", "edits": [ { "find", "replace", "occurrence" } ], "runs": [ { "project", "filter", "requiresSql" } ], "expect": "caught" | "escapes" } ] }
.PARAMETER Source         Repository to clone from (read only). Default: the repository containing this script.
.PARAMETER Commit         Commit under test (required for a run).
.PARAMETER Workspace      Directory for the disposable clone; must not exist (or be an empty directory) and must lie outside -Source and every -ProtectedRoot.
.PARAMETER ProtectedRoot  Extra directories the workspace must not be, contain or lie inside (the primary checkout and the active package are always protected).
.PARAMETER Out            Evidence folder (must be outside the workspace). Default: <Source>/artifacts/evidence/mutation/<utc>.
.PARAMETER Configuration  Build configuration (default Release). Protocol flags beyond this are the caller's: pass them in -BuildArgs.
#>
[CmdletBinding(DefaultParameterSetName = 'Run')]
param(
    [Parameter(ParameterSetName = 'Run', Mandatory)][string]$Mutations,
    [Parameter(ParameterSetName = 'Run', Mandatory)][string]$Commit,
    [Parameter(ParameterSetName = 'Run', Mandatory)][string]$Workspace,
    [Parameter(ParameterSetName = 'Run')][string]$Source,
    [Parameter(ParameterSetName = 'Run')][string[]]$ProtectedRoot = @(),
    [Parameter(ParameterSetName = 'Run')][string]$Configuration = 'Release',
    [Parameter(ParameterSetName = 'Run')][string[]]$BuildArgs = @('-nologo', '-v:q'),
    [Parameter(ParameterSetName = 'Replay', Mandatory)][string]$ReplaySummary,
    [Parameter(ParameterSetName = 'Replay')][string]$ExpectedPerMutation,
    [Nullable[int]]$ExpectCaught,
    [Nullable[int]]$ExpectEscaped,
    [Nullable[int]]$ExpectCount,
    [string]$Out,
    [switch]$PassThru
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ValidationCommon.ps1')
$ProtectedRoot = Expand-List $ProtectedRoot; $BuildArgs = Expand-List $BuildArgs

$InfraPattern = 'SqlException|network-related|Cannot open database|Login failed|actively refused|Timeout expired|TaskCanceledException|SqlTestUnavailableException'
function Stop-Refused([string]$Message) { Write-Host "REFUSED: $Message" -ForegroundColor Red; throw [System.InvalidOperationException]::new("REFUSED: $Message") }

function Complete-Run($summary, [string]$OutDir) {
    $summary.Findings = @($summary.Findings)
    if ($null -ne $ExpectCaught -and $summary.Caught -ne $ExpectCaught) { $summary.Findings += "expected $ExpectCaught caught, found $($summary.Caught)" }
    if ($null -ne $ExpectEscaped -and $summary.Escaped -ne $ExpectEscaped) { $summary.Findings += "expected $ExpectEscaped escaped, found $($summary.Escaped)" }
    if ($null -ne $ExpectCount -and $summary.Total -ne $ExpectCount) { $summary.Findings += "expected $ExpectCount mutations, found $($summary.Total)" }
    if ($summary.Invalid -gt 0) { $summary.Findings += "$($summary.Invalid) invalid mutation results (neither caught nor escaped)" }
    if ($summary.Unexpected -gt 0) { $summary.Findings += "$($summary.Unexpected) mutations did not behave as expected" }
    $summary.ExitCode = if ($summary.Findings.Count) { 1 } else { 0 }
    if ($OutDir) { Write-TextLf (Join-Path $OutDir 'mutation-results.json') ($summary | ConvertTo-Json -Depth 8); [void](Write-ChecksumFile $OutDir) }
    "mutations: $($summary.Total); caught $($summary.Caught); escaped $($summary.Escaped); invalid $($summary.Invalid)"
    foreach ($m in $summary.Results) { '  {0,-44} {1,-18} {2}' -f $m.Id, $m.Outcome, $m.Detail }
    if ($summary.Findings.Count) { 'FINDINGS:'; $summary.Findings | ForEach-Object { "  - $_" } } else { 'No findings.' }
    if ($PassThru) { $summary }
    exit $summary.ExitCode
}

# ====================================================================================================================== REPLAY
if ($PSCmdlet.ParameterSetName -eq 'Replay') {
    if (-not (Test-Path -LiteralPath $ReplaySummary -PathType Leaf)) { Write-Host "REFUSED: summary not found: $ReplaySummary"; exit 2 }
    $text = (Get-Content -LiteralPath $ReplaySummary -Raw) -replace "`r`n", "`n"
    $complete = $text -match '(?m)^MUTATIONS-ALLDONE\s*$'
    $blocks = [regex]::Split($text, '(?m)^(?==== MUTATION )') | Where-Object { $_ -like '=== MUTATION *' }
    $results = @()
    foreach ($b in $blocks) {
        $head = [regex]::Match($b, '^=== MUTATION (?<id>\S+) \((?<target>[^)]*)\)')
        $r = [ordered]@{ Id = $head.Groups['id'].Value; Target = $head.Groups['target'].Value; Outcome = 'Invalid'; Detail = ''; Layers = [ordered]@{}; Restored = $null }
        $problems = @()
        foreach ($layer in 'unit', 'sql') {
            $m = [regex]::Match($b, "(?m)^$layer`:\s+(?<v>Passed!|Failed!)\s+- Failed:\s+(?<f>\d+), Passed:\s+(?<p>\d+), Skipped:\s+(?<s>\d+), Total:\s+(?<t>\d+)")
            if ($m.Success) {
                $f = [int]$m.Groups['f'].Value; $p = [int]$m.Groups['p'].Value; $s = [int]$m.Groups['s'].Value; $t = [int]$m.Groups['t'].Value
                if ($f + $p + $s -ne $t) { $problems += "$layer counts inconsistent ($f+$p+$s != $t)" }
                if (($m.Groups['v'].Value -eq 'Failed!') -ne ($f -gt 0)) { $problems += "$layer verdict disagrees with its failed count" }
                $r.Layers[$layer] = [pscustomobject]@{ Failed = $f; Passed = $p; Skipped = $s; Total = $t }
            }
            elseif ($b -match "(?m)^BUILD-FAILED $layer") { $problems += "$layer build failed"; $r.Layers[$layer] = $null }
        }
        $rm = [regex]::Match($b, '(?m)^restored:\s+(True|False)'); $r.Restored = if ($rm.Success) { $rm.Groups[1].Value -eq 'True' } else { $null }
        if ($null -eq $r.Restored) { $problems += 'no restore line' } elseif (-not $r.Restored) { $problems += 'source not restored byte for byte' }
        $ran = @($r.Layers.Keys | Where-Object { $r.Layers[$_] })
        if ($ran.Count -eq 0) { $problems += 'no test layer ran' }
        $executed = ($ran | ForEach-Object { $r.Layers[$_].Passed + $r.Layers[$_].Failed } | Measure-Object -Sum).Sum
        if ($ran.Count -gt 0 -and $executed -eq 0) { $problems += 'no test executed' }
        if ($problems.Count) { $r.Outcome = 'Invalid'; $r.Detail = $problems -join '; ' }
        else {
            $caughtBy = @($ran | Where-Object { $r.Layers[$_].Failed -gt 0 })
            if ($caughtBy.Count) { $r.Outcome = 'Caught'; $r.Detail = 'caught by ' + (($caughtBy | ForEach-Object { "$($r.Layers[$_].Failed) $_" }) -join ', ') } else { $r.Outcome = 'Escaped'; $r.Detail = 'every layer passed' }
        }
        $results += [pscustomobject]$r
    }
    $summary = [pscustomobject]@{
        Mode = 'replay'; Summary = $ReplaySummary; SummarySha256 = Get-Sha256Hex $ReplaySummary; Complete = $complete; Total = $results.Count
        Caught = @($results | Where-Object Outcome -eq 'Caught').Count; Escaped = @($results | Where-Object Outcome -eq 'Escaped').Count
        Invalid = @($results | Where-Object Outcome -eq 'Invalid').Count; Unexpected = @($results | Where-Object Outcome -eq 'Escaped').Count; Results = $results; Findings = @(); ExitCode = 0
    }
    # Every recorded mutation is expected to be caught: an escape is a failed validation result by itself, whatever counts were expected.
    if (-not $complete) { $summary.Findings += 'the summary has no completion marker (truncated or still running)' }
    if ($ExpectedPerMutation) {
        $want = Get-Content -LiteralPath $ExpectedPerMutation -Raw | ConvertFrom-Json
        $byId = @{}; foreach ($m in $summary.Results) { $byId[$m.Id] = $m }
        foreach ($w in $want.mutations) {
            if (-not $byId.ContainsKey($w.id)) { $summary.Findings += "mutation $($w.id) missing from the summary"; continue }
            $m = $byId[$w.id]
            foreach ($layer in 'unit', 'sql') {
                $exp = $w.$layer; $got = if ($m.Layers.Contains($layer) -and $m.Layers[$layer]) { $m.Layers[$layer].Failed } else { 0 }
                if ($exp -ne $got) { $summary.Findings += "$($w.id): recorded $exp failing $layer tests, summary shows $got" }
            }
            if ($m.Outcome -ne $w.outcome) { $summary.Findings += "$($w.id): recorded outcome $($w.outcome), classified $($m.Outcome)" }
        }
        foreach ($m in $summary.Results) { if (-not @($want.mutations | Where-Object id -eq $m.Id)) { $summary.Findings += "mutation $($m.Id) is in the summary but not in the record" } }
    }
    if ($Out) { New-Item -ItemType Directory -Force $Out | Out-Null }
    Complete-Run $summary $Out
}

# ====================================================================================================================== RUN
try {
    if (-not $Source) { $Source = (Invoke-Git $PSScriptRoot @('rev-parse', '--show-toplevel')).Text }
    $src = Get-GitIdentity $Source; if (-not $src.Ok) { Stop-Refused "source is not a Git work tree: $Source" }
    $srcTop = [IO.Path]::GetFullPath($src.TopLevel)
    $resolved = Invoke-Git $srcTop @('rev-parse', '--verify', '--quiet', "$Commit^{commit}"); if ($resolved.Exit -ne 0) { Stop-Refused "commit '$Commit' not found in the source repository" }
    $sha = $resolved.Text
    if (-not (Test-Path -LiteralPath $Mutations -PathType Leaf)) { Stop-Refused "mutation file not found: $Mutations" }
    $def = Get-Content -LiteralPath $Mutations -Raw | ConvertFrom-Json
    if (-not $def.mutations -or @($def.mutations).Count -eq 0) { Stop-Refused 'the mutation file lists no mutations' }
    foreach ($m in $def.mutations) {   # validate and normalise once, so a malformed file is refused with a reason, not half-run
        foreach ($req in 'id', 'target', 'edits', 'runs') { if (-not $m.PSObject.Properties[$req] -or $null -eq $m.$req) { Stop-Refused "a mutation lacks '$req'" } }
        if ($m.id -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,80}$') { Stop-Refused "bad mutation id '$($m.id)'" }
        if (-not $m.PSObject.Properties['expect']) { $m | Add-Member -NotePropertyName expect -NotePropertyValue 'caught' }
        if ($m.expect -notin 'caught', 'escapes') { Stop-Refused "mutation '$($m.id)': expect must be 'caught' or 'escapes'" }
        if (@($m.edits).Count -eq 0 -or @($m.runs).Count -eq 0) { Stop-Refused "mutation '$($m.id)' needs at least one edit and one run" }
        foreach ($e in $m.edits) { foreach ($req in 'find', 'replace') { if (-not $e.PSObject.Properties[$req]) { Stop-Refused "mutation '$($m.id)': an edit lacks '$req'" } } }
        foreach ($run in $m.runs) {
            if (-not $run.PSObject.Properties['project'] -or -not $run.project) { Stop-Refused "mutation '$($m.id)': a run lacks 'project'" }
            if (-not $run.PSObject.Properties['filter']) { $run | Add-Member -NotePropertyName filter -NotePropertyValue '' }
            if (-not $run.PSObject.Properties['requiresSql']) { $run | Add-Member -NotePropertyName requiresSql -NotePropertyValue $false }
        }
    }
    $ids = @($def.mutations | ForEach-Object id); if (@($ids | Select-Object -Unique).Count -ne $ids.Count) { Stop-Refused 'duplicate mutation ids' }

    # --- the workspace must be disposable and nowhere near anything protected
    # Paths are compared as strings, so every path that names a folder is first required to be in one plain form: a drive-letter absolute path.
    # UNC (\\host\share, including \\localhost\F$), device (\\?\, \\.\) and 8.3 short-name spellings of the same folder are refused outright.
    foreach ($pair in @(@('workspace', $Workspace), @('source', $srcTop), @('evidence folder', $Out)) + @($ProtectedRoot | ForEach-Object { , @('protected root', $_) })) {
        if ($pair[1] -and -not (Test-LocalDrivePath $pair[1])) { Stop-Refused "the $($pair[0]) path '$($pair[1])' is not a plain absolute local path: UNC, device-prefixed and 8.3 short forms name a folder by a string that cannot be compared safely" }
    }
    $ws = [IO.Path]::GetFullPath($Workspace).TrimEnd('\', '/')
    # What is protected is decided by where this toolkit lives, not by where it was started or what -Source says: the toolkit's own repository
    # (its working tree, its common .git, and EVERY worktree of it, which includes the primary checkout) plus the source, extra roots and the cwd.
    $selfTop = [IO.Path]::GetFullPath((Invoke-Git $PSScriptRoot @('rev-parse', '--show-toplevel')).Text)
    $selfCommon = [IO.Path]::GetFullPath((Invoke-Git $PSScriptRoot @('rev-parse', '--path-format=absolute', '--git-common-dir')).Text)
    $worktrees = @(foreach ($t in @($srcTop, $selfTop)) { (Invoke-Git $t @('worktree', 'list', '--porcelain')).Text -split "`n" | Where-Object { $_ -like 'worktree *' } | ForEach-Object { [IO.Path]::GetFullPath($_.Substring(9)) } })
    $protected = @($srcTop, $src.GitDir, $selfTop, $selfCommon) + $worktrees + @($ProtectedRoot | ForEach-Object { [IO.Path]::GetFullPath($_) }) + @([IO.Path]::GetFullPath((Get-Location).Path))
    $protected = @($protected | ForEach-Object { [IO.Path]::GetFullPath($_) } | Select-Object -Unique)
    foreach ($p in $protected) {
        if ((Test-PathUnder $ws $p) -or (Test-PathUnder $p $ws)) { Stop-Refused "workspace '$ws' is, contains or lies inside the protected path '$p'" }
    }
    if (Test-HasReparsePoint $ws ([IO.Path]::GetPathRoot($ws))) { Stop-Refused 'the workspace path passes through a symbolic link or junction' }
    $envRisk = Get-BuildEnvironmentRisks
    if ($envRisk.Risky) { Stop-Refused "the environment changes what a build compiles (variables [$($envRisk.HookVariables -join ', ')]; user ImportBefore/ImportAfter files [$($envRisk.UserImportFiles -join ', ')]): a mutation result obtained under it would not be about the commit" }
    $aboveWs = @(Get-AncestorBuildFiles $ws)
    if ($aboveWs.Count -gt 0) { Stop-Refused "build configuration above the workspace ($($aboveWs[0])) would be imported by its builds and is not part of the commit under test" }
    if (Test-Path -LiteralPath $ws) { if ((Get-ChildItem -LiteralPath $ws -Force | Measure-Object).Count -gt 0) { Stop-Refused "workspace '$ws' exists and is not empty (a fresh clone is made every run)" } }
    # Evidence goes under THIS toolkit's artifacts/ (ignored by Git) by default, never into the source checkout.
    if (-not $Out) { $Out = Join-Path $selfTop ("artifacts/evidence/mutation/{0}-{1}" -f (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'), $sha.Substring(0, 8)) }
    $outFull = [IO.Path]::GetFullPath($Out)
    if (-not (Test-PathUnder $outFull (Join-Path $selfTop 'artifacts'))) {
        foreach ($p in $protected) { if (Test-PathUnder $outFull $p) { Stop-Refused "the evidence folder '$outFull' lies inside the protected path '$p' (only artifacts/ of the repository holding these scripts is allowed there)" } }
    }
    if ((Test-PathUnder $outFull $ws) -or (Test-PathUnder $ws $outFull)) { Stop-Refused 'the evidence folder must be outside the workspace' }
    if (Test-Path -LiteralPath $outFull) { if ((Get-ChildItem -LiteralPath $outFull -Force | Measure-Object).Count -gt 0) { Stop-Refused "evidence folder '$outFull' is not empty" } }
    New-Item -ItemType Directory -Force $outFull | Out-Null

    # --- protected-state fingerprint (read-only) before anything runs
    $fingerprint = {
        $targets = foreach ($m in $def.mutations) { $p = Join-Path $srcTop $m.target; if (Test-Path -LiteralPath $p -PathType Leaf) { "$($m.target)=$(Get-Sha256Hex $p)" } else { "$($m.target)=absent" } }
        [pscustomobject]@{
            Head = (Invoke-Git $srcTop @('rev-parse', 'HEAD')).Text; Status = Get-Sha256OfText (Invoke-Git $srcTop @('status', '--porcelain')).Text
            Targets = Get-Sha256OfText (($targets | Sort-Object) -join "`n"); IndexLock = (Test-Path -LiteralPath (Join-Path $src.GitDir 'index.lock'))
        }
    }
    $protBefore = & $fingerprint

    # --- the clone: standalone, no remote, so nothing it does can reach the source
    New-Item -ItemType Directory -Force $ws | Out-Null
    $r = Invoke-Git $ws @('init', '-q'); if ($r.Exit -ne 0) { Stop-Refused "git init failed in the workspace: $($r.Text)" }
    # Second layer: Git reports the real location of what was just created. If it is not where the string said (a path form the checks above
    # did not anticipate), the folder this tool created (it holds nothing but an empty .git) is removed and the run is refused.
    $canonical = [IO.Path]::GetFullPath((Invoke-Git $ws @('rev-parse', '--show-toplevel')).Text)
    foreach ($p in $protected) {
        if ((Test-PathUnder $canonical $p) -or (Test-PathUnder $p $canonical) -or $canonical -ne $ws) {
            if ((Get-ChildItem -LiteralPath $ws -Force | Where-Object Name -ne '.git' | Measure-Object).Count -eq 0) { Remove-Item -LiteralPath (Join-Path $ws '.git') -Recurse -Force -ErrorAction SilentlyContinue; Remove-Item -LiteralPath $ws -Force -ErrorAction SilentlyContinue }
            Stop-Refused "the workspace resolves to '$canonical', which is not where '$ws' says or is protected ('$p'); nothing was kept"
        }
    }
    foreach ($step in @(@('fetch', '-q', '--depth', '1', ([Uri]::new($srcTop)).AbsoluteUri, $sha), @('checkout', '-q', '--detach', 'FETCH_HEAD'))) {
        $r = Invoke-Git $ws $step; if ($r.Exit -ne 0) { Stop-Refused "git $($step[0]) failed in the workspace: $($r.Text)" }
    }
    $top = (Invoke-Git $ws @('rev-parse', '--show-toplevel')).Text
    if ([IO.Path]::GetFullPath($top) -ne $ws -or -not (Test-Path -LiteralPath (Join-Path $ws '.git') -PathType Container)) { Stop-Refused 'the workspace is not a standalone clone' }
    if ((Invoke-Git $ws @('rev-parse', 'HEAD')).Text -ne $sha) { Stop-Refused 'the workspace is not at the requested commit' }
    Add-Content -LiteralPath (Join-Path $ws '.git/info/exclude') -Value @('.apperture-disposable-workspace', 'artifacts/')
    Write-TextLf (Join-Path $ws '.apperture-disposable-workspace') ((@{ CreatedUtc = (Get-Date).ToUniversalTime().ToString('o'); Source = $srcTop; Commit = $sha } | ConvertTo-Json))
    $artifacts = Join-Path $ws 'artifacts/mutation'

    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
    $sqlEnv = Resolve-SqlTarget $ws -ProcessEnvironmentOnly

    # --- helpers
    $dotnet = {
        param([string]$Name, [string[]]$DotnetArgs)
        $log = Join-Path $outFull "$Name.log"
        $o = & dotnet @DotnetArgs 2>&1 | ForEach-Object { [string]$_ }
        $code = $LASTEXITCODE
        Write-TextLf $log (Protect-Text ($o -join "`n"))
        $code
    }
    $owningProject = {
        param([string]$RelTarget)
        $d = [IO.Path]::GetDirectoryName((Join-Path $ws $RelTarget))
        while ($d -and (Test-PathUnder $d $ws)) { $p = Get-ChildItem -LiteralPath $d -Filter *.csproj -File -ErrorAction SilentlyContinue | Select-Object -First 1; if ($p) { return $p }; $d = [IO.Path]::GetDirectoryName($d) }
        $null
    }
    $assemblyHash = {
        param($Csproj)
        $name = [IO.Path]::GetFileNameWithoutExtension($Csproj.Name)
        $dll = Join-Path $artifacts ("bin/{0}/{1}/{0}.dll" -f $name, $Configuration.ToLowerInvariant())
        # File: the bytes (a new build of any source text changes them, PDB checksum included). Code: what the assembly does (IL, string literals, constants).
        if (Test-Path -LiteralPath $dll) { [pscustomobject]@{ File = (Get-Sha256Hex $dll); Code = (Get-AssemblyCodeHash $dll) } } else { $null }
    }
    $runTests = {
        param([string]$Name, $Run)
        $trxDir = Join-Path $outFull "trx-$Name"; New-Item -ItemType Directory -Force $trxDir | Out-Null
        $proj = Join-Path $ws $Run.project
        $a = @('test', $proj, '-c', $Configuration, '--no-build', '--artifacts-path', $artifacts, '-nologo', '--logger', "trx;LogFileName=$Name.trx", '--results-directory', $trxDir)
        if ($Run.filter) { $a += @('--filter', $Run.filter) }
        $code = & $dotnet "test-$Name" $a
        $trx = Read-Trx (Join-Path $trxDir "$Name.trx")
        $failed = @($trx.Results | Where-Object Outcome -eq 'Failed')
        [pscustomobject]@{
            Project = $Run.project; Filter = $Run.filter; Exit = $code; TrxValid = $trx.Valid; TrxErrors = $trx.Errors; Trx = "trx-$Name/$Name.trx"
            Passed = @($trx.Results | Where-Object Outcome -eq 'Passed').Count; Failed = $failed.Count; NotExecuted = @($trx.Results | Where-Object Outcome -eq 'NotExecuted').Count
            InfraFailures = @($failed | Where-Object { $_.Message -match $InfraPattern }).Count
            FailedTests = @($failed | Select-Object -First 40 | ForEach-Object { $_.Name })
        }
    }

    # --- baseline: the same tests on the unedited tree must be green
    $sep = [string][char]31   # filters contain '|', so it cannot separate the key
    $runKeys = @($def.mutations | ForEach-Object { $_.runs } | ForEach-Object { "$($_.project)$sep$($_.filter)" } | Select-Object -Unique)
    $needsSql = @($def.mutations | ForEach-Object { $_.runs } | Where-Object { $_.requiresSql }).Count -gt 0
    if ($needsSql) {
        if (-not $sqlEnv.Configured) { Stop-Refused 'a run needs SQL Server but APPERTURE_TEST_SQL_CONNECTION_STRING is not set in the process environment (the disposable clone has no .env)' }
        if (-not (Test-TcpReachable $sqlEnv.Info.Host $(if ($sqlEnv.Info.Port) { $sqlEnv.Info.Port } else { 1433 }))) { Stop-Refused "SQL Server at $($sqlEnv.Info.Server) is not reachable; refusing to run mutations that need it" }
    }
    $projects = @($def.mutations | ForEach-Object { $_.runs } | ForEach-Object { $_.project } | Select-Object -Unique)
    $baseAssemblies = @{}
    foreach ($p in $projects) {
        if ((& $dotnet ("build-baseline-" + [IO.Path]::GetFileNameWithoutExtension($p)) (@('build', (Join-Path $ws $p), '-c', $Configuration, '--artifacts-path', $artifacts) + $BuildArgs)) -ne 0) { Stop-Refused "baseline build of $p failed" }
    }
    foreach ($m in $def.mutations) {
        $cs = & $owningProject $m.target; if (-not $cs) { Stop-Refused "mutation '$($m.id)': no project file contains '$($m.target)'" }
        if (-not $baseAssemblies.ContainsKey($cs.FullName)) {
            $h = & $assemblyHash $cs; if (-not $h) { Stop-Refused "cannot find the compiled assembly of $($cs.Name) under $artifacts (is the project built into the artifacts layout?)" }
            if (-not $h.Code) { Stop-Refused "cannot read the compiled assembly of $($cs.Name) to hash what it does ($script:CodeHashError): without that, a no-op edit cannot be told from a mutation" }
            $baseAssemblies[$cs.FullName] = $h
        }
    }
    $baselineRuns = @(); $baselineExecuted = @{}
    $i = 0
    foreach ($key in $runKeys) {
        $p, $f = $key -split $sep, 2; $i++
        $r = & $runTests "baseline-$i" ([pscustomobject]@{ project = $p; filter = $f })
        $baselineRuns += $r; $baselineExecuted[$key] = $r.Passed + $r.Failed
        if (-not $r.TrxValid -or $r.Failed -gt 0 -or ($r.Passed -eq 0) -or $r.Exit -ne 0 -or $r.NotExecuted -gt 0) { Stop-Refused "baseline is not green for '$p' ($f): $($r.Passed) passed, $($r.Failed) failed, TRX valid: $($r.TrxValid). Mutation results would mean nothing." }
    }

    # --- the mutations
    $results = @(); $unexpected = 0
    foreach ($m in $def.mutations) {
        $res = [ordered]@{ Id = $m.id; Target = $m.target; Expect = $(if ($m.expect) { $m.expect } else { 'caught' }); Outcome = 'Invalid'; Detail = ''; Edits = @(); Runs = @(); BinaryChanged = $null; CodeChanged = $null; Restored = $false }
        $full = [IO.Path]::GetFullPath((Join-Path $ws $m.target))
        if ($m.target -match '(^|[\\/])\.\.([\\/]|$)' -or [IO.Path]::IsPathRooted($m.target) -or -not (Test-PathUnder $full $ws) -or (Test-PathUnder $full (Join-Path $ws '.git')) -or (Test-PathUnder $full (Join-Path $ws 'artifacts'))) { Stop-Refused "mutation '$($m.id)': target '$($m.target)' is not a source file inside the workspace" }
        if ((Invoke-Git $ws @('ls-files', '--error-unmatch', '--', $m.target)).Exit -ne 0) { Stop-Refused "mutation '$($m.id)': '$($m.target)' is not a tracked file" }
        if (Test-HasReparsePoint $full $ws) { Stop-Refused "mutation '$($m.id)': target passes through a link" }
        $originalBytes = [IO.File]::ReadAllBytes($full); $hashBefore = Get-Sha256Hex $full
        $blob = (Invoke-Git $ws @('rev-parse', "HEAD:$($m.target)")).Text
        if ((Invoke-Git $ws @('hash-object', '--', $m.target)).Text -ne $blob) { Stop-Refused "mutation '$($m.id)': the target already differs from the commit" }
        $hasBom = $originalBytes.Length -ge 3 -and $originalBytes[0] -eq 0xEF -and $originalBytes[1] -eq 0xBB -and $originalBytes[2] -eq 0xBF
        $text = [Text.UTF8Encoding]::new($false).GetString($originalBytes, $(if ($hasBom) { 3 } else { 0 }), $originalBytes.Length - $(if ($hasBom) { 3 } else { 0 }))
        $eol = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
        try {
            $mutated = $text; $applyProblem = $null
            foreach ($e in $m.edits) {
                $find = ([string]$e.find -replace "`r`n", "`n") -replace "`n", $eol; $repl = ([string]$e.replace -replace "`r`n", "`n") -replace "`n", $eol
                if ($find.Length -eq 0) { $applyProblem = 'empty find text'; break }
                $positions = @(); $at = $mutated.IndexOf($find, [StringComparison]::Ordinal)
                while ($at -ge 0) { $positions += $at; $at = $mutated.IndexOf($find, $at + 1, [StringComparison]::Ordinal) }
                $occ = if ($e.PSObject.Properties['occurrence'] -and $e.occurrence) { [int]$e.occurrence } else { 0 }
                if ($occ -eq 0 -and $positions.Count -ne 1) { $applyProblem = "find text occurs $($positions.Count) times (needs exactly 1)"; break }
                if ($occ -gt $positions.Count) { $applyProblem = "occurrence $occ requested, found $($positions.Count)"; break }
                $pos = $positions[$(if ($occ -eq 0) { 0 } else { $occ - 1 })]
                $mutated = $mutated.Substring(0, $pos) + $repl + $mutated.Substring($pos + $find.Length)
                $res.Edits += [pscustomobject]@{ Found = $positions.Count; At = $pos }
            }
            if ($applyProblem) { $res.Detail = $applyProblem }
            elseif ($mutated -ceq $text) { $res.Detail = 'the edit changes nothing' }
            else {
                [IO.File]::WriteAllBytes($full, ([Text.UTF8Encoding]::new($hasBom).GetPreamble() + [Text.UTF8Encoding]::new($false).GetBytes($mutated)))
                $res.HashMutated = Get-Sha256Hex $full
                $cs = & $owningProject $m.target
                $built = $true
                foreach ($p in ($m.runs | ForEach-Object project | Select-Object -Unique)) {
                    $code = & $dotnet "mut-$($m.id)-build-$([IO.Path]::GetFileNameWithoutExtension($p))" (@('build', (Join-Path $ws $p), '-c', $Configuration, '--artifacts-path', $artifacts) + $BuildArgs)
                    if ($code -ne 0) { $built = $false; $res.Detail = "build failed for $p (exit $code)" }
                }
                if ($built) {
                    $now = if ($cs) { & $assemblyHash $cs } else { $null }; $was = if ($cs) { $baseAssemblies[$cs.FullName] } else { $null }
                    $res.BinaryChanged = [bool]($now -and $was -and $now.File -ne $was.File)
                    $res.CodeChanged = [bool]($now -and $was -and $now.Code -and $was.Code -and $now.Code -ne $was.Code)
                    if (-not $res.BinaryChanged) { $res.Detail = "the compiled assembly of $(if ($cs) { $cs.Name } else { 'the owning project' }) did not change: the edit did not reach the binary" }
                    elseif ($now.Code -and $was.Code -and -not $res.CodeChanged) { $res.Detail = 'the edit changes no code (comments or whitespace): it is not a mutation' }
                    else {
                        $k = 0
                        foreach ($run in $m.runs) { $k++; $res.Runs += (& $runTests "mut-$($m.id)-$k" $run) }
                        $bad = @($res.Runs | Where-Object { -not $_.TrxValid })
                        $executed = ($res.Runs | ForEach-Object { $_.Passed + $_.Failed } | Measure-Object -Sum).Sum
                        $failed = ($res.Runs | ForEach-Object { $_.Failed } | Measure-Object -Sum).Sum; $infra = ($res.Runs | ForEach-Object { $_.InfraFailures } | Measure-Object -Sum).Sum
                        # A host that crashed or aborted shows up as a non-zero exit with no failed test, or as fewer tests executed than the baseline ran.
                        $crashed = @($res.Runs | Where-Object { $_.Exit -ne 0 -and $_.Failed -eq 0 })
                        $short = @($res.Runs | Where-Object { ($_.Passed + $_.Failed) -lt $baselineExecuted["$($_.Project)$sep$($_.Filter)"] })
                        if ($bad.Count) { $res.Detail = "unreadable TRX: $($bad[0].TrxErrors -join '; ')" }
                        elseif ($crashed.Count) { $res.Detail = "the test host exited $($crashed[0].Exit) without a failed test (crash or abort?): nothing here shows the tests passed" }
                        elseif ($short.Count) { $res.Detail = "fewer tests executed ($($short[0].Passed + $short[0].Failed)) than the unedited baseline ran ($($baselineExecuted["$($short[0].Project)$sep$($short[0].Filter)"])): the run was cut short" }
                        elseif ($executed -eq 0) { $res.Detail = 'no test executed (the filter matched nothing?)' }
                        elseif ($failed -gt 0 -and $failed -eq $infra) { $res.Detail = "all $failed failures look like infrastructure, not assertions" }
                        elseif ($failed -gt 0) { $res.Outcome = 'Caught'; $res.Detail = "$failed failing tests" }
                        else { $res.Outcome = 'Escaped'; $res.Detail = "$executed tests passed with the mutation in place" }
                    }
                }
            }
        }
        finally {
            [IO.File]::WriteAllBytes($full, $originalBytes); (Get-Item -LiteralPath $full).LastWriteTime = Get-Date
            $res.HashRestored = Get-Sha256Hex $full
            $res.Restored = ($res.HashRestored -eq $hashBefore) -and ((Invoke-Git $ws @('hash-object', '--', $m.target)).Text -eq $blob) -and (-not (Invoke-Git $ws @('status', '--porcelain', '--untracked-files=no')).Text)
        }
        if (-not $res.Restored) { Stop-Refused "mutation '$($m.id)': the source was not restored byte for byte; the run is stopped" }
        $good = ($res.Outcome -eq 'Caught' -and $res.Expect -eq 'caught') -or ($res.Outcome -eq 'Escaped' -and $res.Expect -eq 'escapes')
        if (-not $good) { $unexpected++ }
        $results += [pscustomobject]$res
    }

    # --- the restored tree must rebuild to the baseline binaries
    $restoreProblems = @(); $restoreNotes = @()
    foreach ($p in $projects) {
        if ((& $dotnet ("build-restored-" + [IO.Path]::GetFileNameWithoutExtension($p)) (@('build', (Join-Path $ws $p), '-c', $Configuration, '--artifacts-path', $artifacts) + $BuildArgs)) -ne 0) { $restoreProblems += "rebuild of restored $p failed" }
    }
    foreach ($key in @($baseAssemblies.Keys)) {
        $now = & $assemblyHash ([IO.FileInfo]$key); $was = $baseAssemblies[$key]
        if (-not $now) { $restoreProblems += "assembly of $([IO.Path]::GetFileName($key)) is missing after the restore and rebuild" }
        elseif ($now.File -ne $was.File) {
            # Byte-identical is the normal outcome of a deterministic build. If only the bytes differ and the code is identical, record it; if the code differs, a mutated binary survived.
            if ($now.Code -and $was.Code -and $now.Code -eq $was.Code) { $restoreNotes += "assembly of $([IO.Path]::GetFileName($key)): file differs from the baseline after restore, code identical" }
            else { $restoreProblems += "assembly of $([IO.Path]::GetFileName($key)) differs from the baseline after restore and rebuild" }
        }
    }
    $protAfter = & $fingerprint
    $protectedUnchanged = ($protBefore.Head -eq $protAfter.Head) -and ($protBefore.Status -eq $protAfter.Status) -and ($protBefore.Targets -eq $protAfter.Targets) -and ($protBefore.IndexLock -eq $protAfter.IndexLock)

    $summary = [pscustomobject]@{
        Mode = 'run'; Commit = $sha; Source = $srcTop; Workspace = $ws; ProtectedPaths = $protected; Dotnet = (& dotnet --version); Configuration = $Configuration; BuildArgs = $BuildArgs
        Baseline = $baselineRuns; BaselineAssemblies = $baseAssemblies; ProtectedUnchanged = $protectedUnchanged; ProtectedBefore = $protBefore; ProtectedAfter = $protAfter
        RestoreVerified = ($restoreProblems.Count -eq 0); RestoreProblems = $restoreProblems; RestoreNotes = $restoreNotes
        Total = $results.Count; Caught = @($results | Where-Object Outcome -eq 'Caught').Count; Escaped = @($results | Where-Object Outcome -eq 'Escaped').Count
        Invalid = @($results | Where-Object Outcome -eq 'Invalid').Count; Unexpected = $unexpected; Results = $results; Findings = @(); ExitCode = 0
    }
    if (-not $protectedUnchanged) { $summary.Findings += 'the protected source checkout changed while the tool ran' }
    if ($restoreProblems.Count) { $summary.Findings += $restoreProblems }
    Complete-Run $summary $outFull
}
catch {
    # Complete-Run exits with its own code; anything that gets here is a refusal or a tool error: exit 2, never 1 (which means findings).
    if ($_.Exception.Message -notlike 'REFUSED:*') { Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red }
    exit 2
}
