#Requires -Version 7.2
<#
.SYNOPSIS
    Runs tests and writes an evidence folder that states exactly what ran, against what, and whether it can be believed.
.DESCRIPTION
    1. Runs scripts/dev/preflight.ps1. A FAIL stops the run: the folder then holds only the preflight report and a provenance file that
       says why no evidence exists.
    2. Records the tree under test (commit, tree hashes, dirty state), the tools, and the SQL target (server, catalog, user: no password).
    3. For each run in the plan: checks that the test assembly is newer than every tracked source file (or builds it, with -Build), runs
       `dotnet test` with a TRX, redacts passwords from the console log, and classifies the result:
         Passed         tests executed, none failed, none skipped or not executed, every expected class present, host exit 0
         Failed         at least one test failed
         NotDiscovered  nothing executed, fewer tests than expected, or an expected class produced no test
         NotExecuted    tests were skipped / not executed (unless the plan allows that exact name)
         StaleBinary    the assembly is older than the source it claims to test
         Inconclusive   no readable TRX, a TRX that disagrees with itself, or a host failure with no failed test
    4. For runs that need SQL Server it takes write markers before and after (Get-SqlEvidence.ps1 -Mode Markers, through the test
       container) and the run is SqlNotProven unless those markers moved.
    5. Writes provenance.json, summary.md and SHA256SUMS.txt, and verifies the checksum file against the folder.
    The evidence is AUTHORITATIVE only if the preflight passed, the tree was clean, every run Passed and SQL execution was proven where
    needed. Anything else is written down as NOT authoritative, with the reasons. A skipped or undiscovered test is never a pass.

    Exit codes: 0 = authoritative; 1 = evidence written but not authoritative, or the preflight blocked the run; 2 = tool error.
.PARAMETER Record   Short record name; the folder is <repo>/artifacts/evidence/<Record>/<utc>-<sha8>.
.PARAMETER Plan     JSON: { "runs": [ { "name", "project", "filter", "requiresSql", "expectedMinTests", "expectedClasses": [], "allowNotExecuted": [] } ] }
.PARAMETER Selection Select-Tests.ps1 -Json output; a run may set "selectionProject" to take its filter and expectedClasses from it.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,60}$')][string]$Record,
    [Parameter(Mandatory)][string]$Plan,
    [string]$Repo,
    [string]$Selection,
    [string]$ExpectedCommit,
    [string]$Out,
    [string]$ArtifactsPath,
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_-]{0,30}$')][string]$Configuration = 'Release',   # reaches `dotnet build -c`, which MSBuild splits on ';' into properties
    [switch]$Build,
    [string[]]$BuildArgs = @('-nologo', '-v:q'),
    [switch]$AllowDirty,
    [switch]$ProcessEnvironmentOnly,
    [string]$SqlContainer,
    [string]$DockerCommand = 'docker',
    [string]$EnvFile,
    [switch]$PassThru
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ValidationCommon.ps1')
$BuildArgs = Expand-List $BuildArgs

function Stop-Tool([string]$Message) { Write-Host "ERROR: $Message" -ForegroundColor Red; exit 2 }
try {
    if (-not $Repo) { $Repo = (Invoke-Git $PSScriptRoot @('rev-parse', '--show-toplevel')).Text }
    $id = Get-GitIdentity $Repo; if (-not $id.Ok) { Stop-Tool $id.Error }
    $Repo = $id.TopLevel
    if (-not (Test-Path -LiteralPath $Plan -PathType Leaf)) { Stop-Tool "plan not found: $Plan" }
    $planDef = Get-Content -LiteralPath $Plan -Raw | ConvertFrom-Json
    if (-not $planDef.runs -or @($planDef.runs).Count -eq 0) { Stop-Tool 'the plan holds no runs' }
    $runNames = @($planDef.runs | ForEach-Object { [string]$_.name })
    if (@($runNames | Select-Object -Unique).Count -ne $runNames.Count) { Stop-Tool 'two runs in the plan have the same name: the second would overwrite the first run''s TRX and log' }
    $sel = if ($Selection) { Get-Content -LiteralPath $Selection -Raw | ConvertFrom-Json } else { $null }
    $started = (Get-Date).ToUniversalTime()
    if (-not $Out) { $Out = Join-Path $Repo ("artifacts/evidence/{0}/{1}-{2}" -f $Record, $started.ToString('yyyyMMddTHHmmssZ'), $id.Head.Substring(0, 8)) }
    $Out = [IO.Path]::GetFullPath($Out)
    # Existing evidence is never touched, whatever else happens: this is checked before anything is run or written (the preflight repeats it).
    if ((Test-Path -LiteralPath $Out) -and (Get-ChildItem -LiteralPath $Out -Force | Measure-Object).Count -gt 0) { Stop-Tool "the evidence folder '$Out' exists and is not empty: evidence is never overwritten" }
    if (-not $ArtifactsPath) { $ArtifactsPath = Join-Path $Repo 'artifacts/evidence-build' }
    $ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)

    # ---- 1. preflight (it also vets the destination before anything is created)
    $needsSql = @($planDef.runs | Where-Object { $_.PSObject.Properties['requiresSql'] -and $_.requiresSql }).Count -gt 0
    $pf = @{ Repo = $Repo; OutputDir = $Out; PassThru = $true; Quiet = $true; RequireCleanTracked = (-not $AllowDirty) }
    if ($ExpectedCommit) { $pf.ExpectedCommit = $ExpectedCommit }
    if ($needsSql) { $pf.RequireSql = $true }
    if ($ProcessEnvironmentOnly) { $pf.ProcessEnvironmentOnly = $true }
    if ($SqlContainer) { $pf.SqlContainer = $SqlContainer; $pf.DockerCommand = $DockerCommand }
    if ($EnvFile) { $pf.EnvFile = $EnvFile }
    if ($Selection) { $pf.Selection = $Selection }
    $preflight = & (Join-Path $PSScriptRoot '../preflight.ps1') @pf
    $destFail = @($preflight.Checks | Where-Object { $_.Id -eq 'out.destination' -and $_.Status -eq 'FAIL' })
    if ($destFail.Count) { Stop-Tool "the evidence destination was refused and nothing was written: $($destFail[0].Message)" }
    New-Item -ItemType Directory -Force $Out | Out-Null
    foreach ($d in 'results', 'logs') { New-Item -ItemType Directory -Force (Join-Path $Out $d) | Out-Null }
    Write-TextLf (Join-Path $Out 'preflight.json') ($preflight | ConvertTo-Json -Depth 6)
    $reasons = New-Object System.Collections.Generic.List[string]
    $buildEnv = Get-BuildEnvironmentRisks
    $injected = @(Get-ProjectImportInjections $ArtifactsPath)
    # Build arguments reach the child dotnet process without any file or environment variable this tool inspects: -p:DefineConstants=...,
    # @x.rsp, but also flags that make `dotnet build` exit 0 without rewriting the output folder (--no-dependencies, -o, -t:Restore,
    # -getProperty:...), after which the tool would stamp whatever was already there. A deny-list of such forms can never be complete, so only
    # arguments that change reporting, not the build, are accepted; anything else makes the build non-authoritative. (A reviewed allow-list for
    # protocol flags belongs to the build-protocol package.)
    $vettedBuildArg = '^(?:--?|/)(?:nologo|v|verbosity|m|maxcpucount|clp|consoleloggerparameters|nr|nodereuse)(?:[:=].*)?$'
    $propArgs = @($BuildArgs | Where-Object { $_ -notmatch $vettedBuildArg })
    # Everything about THIS build that is not in the stamp's other fields and speaks against trusting the binary later. It is written into the
    # stamp, so that removing the cause afterwards (deleting the injected file, unsetting the variable) cannot launder the binary.
    $buildAgainst = @()
    if ($injected.Count) { $buildAgainst += "artifacts path held MSBuild import files ($($injected[0]))" }
    if ($buildEnv.Risky) { $buildAgainst += "environment build hooks [$($buildEnv.HookVariables -join ', ')] / user import files [$($buildEnv.UserImportFiles -join ', ')]" }
    if (@($id.AncestorBuildFiles).Count) { $buildAgainst += "build configuration above the repository ($(@($id.AncestorBuildFiles)[0]))" }
    if ($propArgs.Count) { $buildAgainst += "build arguments outside the vetted set (-nologo, -v, -m, -clp, -nr) [$($propArgs -join ' ')]" }
    $prov = [ordered]@{
        Record = $Record; StartedUtc = $started.ToString('o'); Authoritative = $false; Reasons = $reasons
        AuthoritativeMeans = 'No known channel was found by which the tested binary could differ from this commit (checked and recorded here). It is not a proof that the build environment was hermetic; see scripts/dev/validation/README.md, Provenance contract.'
        Tree = [ordered]@{ Head = $id.Head; Branch = $id.Branch; Tree = $id.Tree; TreeSrc = $id.Tree_src; TreeTests = $id.Tree_tests; DirtyTrackedFiles = $id.DirtyTrackedFiles; DiffHash = $id.DiffHash; UntrackedFiles = $id.UntrackedFiles; UntrackedBuildFiles = @($id.UntrackedBuildFiles | Select-Object -First 20); AncestorBuildFiles = @($id.AncestorBuildFiles); Origin = $id.OriginUrl }
        Tools = [ordered]@{ Dotnet = (& dotnet --version); Pwsh = $PSVersionTable.PSVersion.ToString(); Git = ((& git --version) -replace 'git version ', ''); Os = [Runtime.InteropServices.RuntimeInformation]::OSDescription }
        BuildEnvironment = [ordered]@{ ArtifactsPathInjectedImports = @($injected); HookVariables = @($buildEnv.HookVariables); UserImportFiles = @($buildEnv.UserImportFiles); OtherMsBuildNugetVariables = @($buildEnv.OtherVariables) }
        Command = [ordered]@{ Configuration = $Configuration; Build = [bool]$Build; BuildArgs = $BuildArgs; ArtifactsPath = $ArtifactsPath; PlanSha256 = (Get-Sha256Hex $Plan); SelectionSha256 = $(if ($Selection) { Get-Sha256Hex $Selection } else { $null }) }
        PreflightOk = $preflight.Ok; Sql = $preflight.Sql; Runs = @()
    }
    if (-not $preflight.Ok) {
        foreach ($c in ($preflight.Checks | Where-Object Status -eq 'FAIL')) { $reasons.Add("preflight $($c.Id): $($c.Message)") }
        $reasons.Add('the preflight failed: no test was run and nothing here is evidence')
    }
    else {
        if ($id.DirtyTrackedFiles -gt 0) { $reasons.Add("the tracked tree was dirty ($($id.DirtyTrackedFiles) files, diff $($id.DiffHash)): not evidence of commit $($id.Head)") }
        if ($injected.Count -gt 0) { $reasons.Add("the artifacts path holds MSBuild import files restore did not write (e.g. $($injected[0])): they are imported into every build there; use a fresh -ArtifactsPath") }
        if ($buildEnv.Risky) { $reasons.Add("the environment changes what a build compiles without any file in the repository (variables [$($buildEnv.HookVariables -join ', ')]; user import files [$($buildEnv.UserImportFiles -join ', ')]): not a build of the commit") }
        if (@($id.AncestorBuildFiles).Count -gt 0) { $reasons.Add("build configuration above the repository (e.g. $(@($id.AncestorBuildFiles)[0])) is imported by every build and is invisible to Git: the build is not a build of the commit") }
        if (@($id.UntrackedBuildFiles).Count -gt 0) { $reasons.Add("$(@($id.UntrackedBuildFiles).Count) untracked file(s) among the sources or build inputs (e.g. $(@($id.UntrackedBuildFiles)[0])): the build can compile them, so the tree is not the commit") }
        if ($Build -and $propArgs.Count) { $reasons.Add("build arguments outside the vetted set (-nologo, -v, -m, -clp, -nr): $($propArgs -join ' '). They can change what is compiled or leave the output folder as it was, so the binary is not shown to be a build of the commit") }
    }

    $runResults = @()
    if ($preflight.Ok) {
        # ---- SQL markers before
        $markersBefore = $null
        if ($needsSql -and $SqlContainer) {
            $mp = @{ Mode = 'Markers'; Container = $SqlContainer; Repo = $Repo; DockerCommand = $DockerCommand; Label = 'before'; PassThru = $true }
            if ($ProcessEnvironmentOnly) { $mp.ProcessEnvironmentOnly = $true }; if ($EnvFile) { $mp.EnvFile = $EnvFile }
            $markersBefore = try { & (Join-Path $PSScriptRoot 'Get-SqlEvidence.ps1') @mp } catch { $null }
            if (-not $markersBefore) { $reasons.Add('SQL write markers could not be collected before the run') }
        }
        $sourceTimes = $null
        foreach ($r in $planDef.runs) {
            $name = [string]$r.name
            if ($name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,60}$') { Stop-Tool "bad run name '$name'" }
            $filter = if ($r.PSObject.Properties['filter']) { [string]$r.filter } else { '' }
            $expected = if ($r.PSObject.Properties['expectedClasses']) { @($r.expectedClasses) } else { @() }
            if ($r.PSObject.Properties['selectionProject'] -and $r.selectionProject) {
                if (-not $sel) { Stop-Tool "run '$name' uses a selection but none was given" }
                $key = [string]$r.selectionProject
                if (-not $sel.Filters.PSObject.Properties[$key]) { Stop-Tool "the selection has no project '$key'" }
                $filter = [string]$sel.Filters.$key; $expected = @($sel.Selected | Where-Object Project -eq $key | ForEach-Object { if ($_.PSObject.Properties['Qualified'] -and @($_.Qualified).Count -gt 1) { @($_.Qualified) } else { $_.Class } })   # one simple name in several namespaces: each identity must produce a test
            }
            $proj = [IO.Path]::GetFullPath((Join-Path $Repo $r.project)); $projName = [IO.Path]::GetFileNameWithoutExtension($proj)
            $run = [ordered]@{ Name = $name; Project = $r.project; Filter = $filter; ExpectedClasses = $expected; Outcome = 'Inconclusive'; Detail = ''; Discovered = $null; BinaryProvenance = $null; ProvenanceEstablished = $false; Exit = $null; Trx = $null; TrxSha256 = $null; AssemblySha256 = $null; BinariesSha256 = $null; Counts = $null; MissingClasses = @(); NotExecuted = @(); Failed = @(); FastSqlCases = 0; DurationSeconds = 0 }
            $sw = [Diagnostics.Stopwatch]::StartNew()
            if ($Build) {
                & dotnet build $proj -c $Configuration --artifacts-path $ArtifactsPath @BuildArgs 2>&1 | ForEach-Object { [string]$_ } | ForEach-Object { Protect-Text $_ } | Set-Content -LiteralPath (Join-Path $Out "logs/$name-build.log")
                if ($LASTEXITCODE -ne 0) { $run.Detail = "build failed (exit $LASTEXITCODE)"; $run.Outcome = 'Inconclusive'; $runResults += [pscustomobject]$run; continue }
            }
            $dll = Join-Path $ArtifactsPath ("bin/{0}/{1}/{0}.dll" -f $projName, $Configuration.ToLowerInvariant())
            if (-not (Test-Path -LiteralPath $dll)) { $run.Outcome = 'StaleBinary'; $run.Detail = "no assembly at $dll (build first, or use -Build)"; $runResults += [pscustomobject]$run; continue }
            # Binary provenance. A build made here is stamped with the tree and the assembly hashes; a later run without -Build trusts the stamp only if
            # the tree is clean at the same commit and the assembly is byte-for-byte the one stamped. Otherwise the conservative rule applies: the
            # assembly must be newer than every tracked source file (a touched-but-identical file then asks for a rebuild, which is the safe answer).
            $stampPath = Join-Path $ArtifactsPath 'evidence-build-stamp.json'
            $stamp = if (Test-Path -LiteralPath $stampPath) { try { Get-Content -LiteralPath $stampPath -Raw | ConvertFrom-Json } catch { $null } } else { $null }
            if ($Build) {
                $asm = [ordered]@{}
                # Assemblies of an earlier build at the same commit are carried over only if that stamp recorded a clean build (a stamp without the
                # BuildAgainst field predates the check and is not trusted); otherwise they are dropped and so no longer stamped.
                $against = @($buildAgainst)
                if ($stamp -and $stamp.Head -eq $id.Head -and $stamp.DiffHash -eq $id.DiffHash -and $stamp.DirtyTrackedFiles -eq 0 -and $stamp.UntrackedBuildFiles -eq 0 -and $stamp.PSObject.Properties['BuildAgainst'] -and @($stamp.BuildAgainst).Count -eq 0) { foreach ($p in $stamp.Assemblies.PSObject.Properties) { $asm[$p.Name] = $p.Value } }
                $asm[$dll] = Get-BinariesHash (Split-Path $dll -Parent)   # the whole output folder: a replaced dependency must invalidate the stamp
                Write-TextLf $stampPath ([pscustomobject]@{ Head = $id.Head; Tree = $id.Tree; DirtyTrackedFiles = $id.DirtyTrackedFiles; UntrackedBuildFiles = @($id.UntrackedBuildFiles).Count; DiffHash = $id.DiffHash; BuiltUtc = (Get-Date).ToUniversalTime().ToString('o'); Dotnet = (& dotnet --version); Configuration = $Configuration; BuildArgs = @($BuildArgs); BuildAgainst = $against; Assemblies = $asm } | ConvertTo-Json -Depth 4)
                $run.BinaryProvenance = 'built by this run'; $run.ProvenanceEstablished = $true
            }
            else {
                # Only a clean tree at the stamped commit, with the stamped assembly, establishes provenance. File times can only REFUSE a stale
                # binary; a binary that is newer than the source may still have been built from another commit, so they never establish it.
                $stampOk = $stamp -and $stamp.Head -eq $id.Head -and $id.DirtyTrackedFiles -eq 0 -and $stamp.DirtyTrackedFiles -eq 0 -and @($id.UntrackedBuildFiles).Count -eq 0 -and @($id.AncestorBuildFiles).Count -eq 0 -and -not $buildEnv.Risky -and $injected.Count -eq 0 -and $stamp.UntrackedBuildFiles -eq 0 -and $stamp.PSObject.Properties['BuildAgainst'] -and @($stamp.BuildAgainst).Count -eq 0 -and $stamp.Assemblies.PSObject.Properties[$dll] -and $stamp.Assemblies.PSObject.Properties[$dll].Value -eq (Get-BinariesHash (Split-Path $dll -Parent))
                if ($stampOk) { $run.BinaryProvenance = "build stamp: built by this tool from tree $($stamp.Tree) with build arguments [$(@($stamp.BuildArgs) -join ' ')]; every assembly in the output folder is the stamped one"; $run.ProvenanceEstablished = $true }
                else {
                  $run.BinaryProvenance = 'file times only: the assembly is newer than every tracked source file, but nothing ties it to this commit (no -Build, no matching build stamp)'
                if (-not $sourceTimes) { $sourceTimes = (Invoke-Git $Repo @('ls-files', '--', 'src', 'tests', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json')).Text -split "`n" | Where-Object { $_ } | ForEach-Object { $p = Join-Path $Repo $_; if (Test-Path -LiteralPath $p) { [pscustomobject]@{ Path = $_; Time = [IO.File]::GetLastWriteTimeUtc($p) } } } }
                $dllTime = [IO.File]::GetLastWriteTimeUtc($dll); $newer = @($sourceTimes | Where-Object { $_.Time -gt $dllTime } | Select-Object -First 3)
                if ($newer.Count) { $run.Outcome = 'StaleBinary'; $run.Detail = "the assembly is older than tracked source (e.g. $($newer[0].Path)): it was not built from this tree"; $runResults += [pscustomobject]$run; continue }
                }
            }
            $run.AssemblySha256 = Get-Sha256Hex $dll; $run.BinariesSha256 = Get-BinariesHash (Split-Path $dll -Parent)
            # Discovery is asked of the test host separately from execution, so "never discovered" and "discovered but never ran" are different findings.
            $la = @('test', $proj, '-c', $Configuration, '--no-build', '--artifacts-path', $ArtifactsPath, '-nologo', '--list-tests'); if ($filter) { $la += @('--filter', $filter) }
            $listing = @(& dotnet @la 2>&1 | ForEach-Object { [string]$_ }); $listExit = $LASTEXITCODE
            Write-TextLf (Join-Path $Out "logs/$name-discovery.log") ((@($listing | ForEach-Object { Protect-Text $_ })) -join "`n")
            $run.Discovered = if ($listExit -eq 0) { @($listing | Where-Object { $_ -match '^\s{4}\S' }).Count } else { $null }
            $a = @('test', $proj, '-c', $Configuration, '--no-build', '--artifacts-path', $ArtifactsPath, '-nologo', '--logger', "trx;LogFileName=$name.trx", '--results-directory', (Join-Path $Out 'results'))
            if ($filter) { $a += @('--filter', $filter) }
            $console = & dotnet @a 2>&1 | ForEach-Object { Protect-Text ([string]$_) }
            $run.Exit = $LASTEXITCODE
            Write-TextLf (Join-Path $Out "logs/$name.log") (@($console) -join "`n")
            $run.DurationSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 1)
            $trxPath = Join-Path $Out "results/$name.trx"; $trx = Read-Trx $trxPath
            $run.Trx = "results/$name.trx"; $run.TrxSha256 = $trx.Sha256
            if (-not $trx.Valid) { $run.Detail = "no usable TRX: $($trx.Errors -join '; ')"; $runResults += [pscustomobject]$run; continue }
            $res = $trx.Results
            $passed = @($res | Where-Object Outcome -eq 'Passed').Count; $failed = @($res | Where-Object Outcome -eq 'Failed'); $notExec = @($res | Where-Object { $_.Outcome -ne 'Passed' -and $_.Outcome -ne 'Failed' })
            $allowed = if ($r.PSObject.Properties['allowNotExecuted']) { @($r.allowNotExecuted) } else { @() }
            $notExecBad = @($notExec | Where-Object { $n = $_.Name; -not ($allowed | Where-Object { $n -ceq $_ }) })   # exact, full test name as the TRX spells it; a substring would let one name excuse many tests
            $run.Counts = [ordered]@{ Total = $res.Count; Passed = $passed; Failed = $failed.Count; NotExecuted = $notExec.Count; RcsiFalse = @($res | Where-Object { (Get-RcsiMode $_.Name) -eq 'False' }).Count; RcsiTrue = @($res | Where-Object { (Get-RcsiMode $_.Name) -eq 'True' }).Count }
            $run.Failed = @($failed | Select-Object -First 30 | ForEach-Object { $_.Name }); $run.NotExecuted = @($notExecBad | Select-Object -First 30 | ForEach-Object { "[$($_.Outcome)] $($_.Name)" })
            $run.FastSqlCases = @($res | Where-Object { $_.Outcome -eq 'Passed' -and ($_.Class -match 'SqlServer' -or $_.Name -match 'SqlServer') -and (ConvertTo-Seconds $_.Duration) -lt 0.1 }).Count
            $minTests = if ($r.PSObject.Properties['expectedMinTests']) { [int]$r.expectedMinTests } else { 1 }
            $run.MissingClasses = @($expected | Where-Object { $c = $_; -not ($res | Where-Object { $_.Name.Contains(".$c.") -or $_.Name.StartsWith("$c.", [StringComparison]::Ordinal) -or $_.Class -eq $c -or $_.Class -like "*.$c" }) })
            if ($failed.Count) { $run.Outcome = 'Failed'; $run.Detail = "$($failed.Count) failed" }
            elseif (($passed + $failed.Count) -eq 0) { $run.Outcome = 'NotDiscovered'; $run.Detail = 'no test was executed' }
            elseif ($passed -lt $minTests) { $run.Outcome = 'NotDiscovered'; $run.Detail = "$passed tests executed, at least $minTests expected" }
            elseif ($run.MissingClasses.Count) { $run.Outcome = 'NotDiscovered'; $run.Detail = "no test found for: $($run.MissingClasses -join ', ')" }
            elseif ($null -ne $run.Discovered -and $run.Discovered -gt $res.Count) { $run.Outcome = 'NotExecuted'; $run.Detail = "the test host discovered $($run.Discovered) tests but the TRX holds $($res.Count) results" }
            elseif ($notExecBad.Count) { $run.Outcome = 'NotExecuted'; $run.Detail = "$($notExecBad.Count) tests skipped or not executed" }
            elseif ($run.Exit -ne 0) { $run.Outcome = 'Inconclusive'; $run.Detail = "the test host exited $($run.Exit) although no test failed" }
            else { $run.Outcome = 'Passed'; $run.Detail = "$passed passed" }
            $runResults += [pscustomobject]$run
        }
        # ---- SQL markers after
        $sqlProof = [ordered]@{ Required = $needsSql; Container = $SqlContainer; Collected = $false; UpdatesBefore = $null; UpdatesAfter = $null; Moved = $false; CatalogStartupsInWindow = $null; CatalogsWrittenInWindow = $null }
        if ($needsSql) {
            if ($markersBefore -and $SqlContainer) {
                $mp.Label = 'after'; $markersAfter = try { & (Join-Path $PSScriptRoot 'Get-SqlEvidence.ps1') @mp } catch { $null }
                if ($markersAfter) {
                    Write-TextLf (Join-Path $Out 'logs/sql-markers-before.json') ($markersBefore | ConvertTo-Json -Depth 5); Write-TextLf (Join-Path $Out 'logs/sql-markers-after.json') ($markersAfter | ConvertTo-Json -Depth 5)
                    $sqlProof.Collected = $true; $sqlProof.UpdatesBefore = $markersBefore.TotalUserUpdates; $sqlProof.UpdatesAfter = $markersAfter.TotalUserUpdates
                    $sqlProof.Moved = ($markersAfter.CatalogCount -ne $markersBefore.CatalogCount) -or ($markersAfter.TotalUserUpdates -ne $markersBefore.TotalUserUpdates)
                    # second, independent proof: test catalogs started during the run window (server error log)
                    $wp = @{ Mode = 'Window'; Container = $SqlContainer; Repo = $Repo; DockerCommand = $DockerCommand; FromUtc = $started.AddSeconds(-1); ToUtc = (Get-Date).ToUniversalTime().AddSeconds(2); PassThru = $true }
                    if ($ProcessEnvironmentOnly) { $wp.ProcessEnvironmentOnly = $true }; if ($EnvFile) { $wp.EnvFile = $EnvFile }
                    $win = try { & (Join-Path $PSScriptRoot 'Get-SqlEvidence.ps1') @wp } catch { $null }
                    $sqlProof.CatalogStartupsInWindow = if ($win) { $win.Startups } else { $null }
                    # third signal, for tests that write to a persistent shared catalog (no new catalog starts): a catalog whose last user write
                    # lies inside the run window. last_user_update is server-local time; the reported UTC offset places it. The window is the
                    # same one the start-up query uses.
                    $winFrom = $started.AddSeconds(-1); $winTo = (Get-Date).ToUniversalTime().AddSeconds(2)
                    $written = @(foreach ($c in @($markersAfter.Catalogs)) {
                        if ($c.LastWrite -and $c.LastWrite -ne 'null') {
                            $t = [datetime]::MinValue
                            if ([datetime]::TryParse([string]$c.LastWrite, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal, [ref]$t)) {
                                $utc = $t.AddMinutes(-[int]$markersAfter.UtcOffsetMinutes)
                                if ($utc -ge $winFrom -and $utc -le $winTo) { [string]$c.Catalog }
                            }
                        }
                    })
                    $sqlProof.CatalogsWrittenInWindow = $written.Count
                    if ($win) { Write-TextLf (Join-Path $Out 'logs/sql-window.json') ($win | ConvertTo-Json -Depth 5) }
                }
            }
            if (-not $sqlProof.Collected) { $reasons.Add('SqlNotProven: write markers were not collected (pass -SqlContainer for the test SQL container)') }
            elseif (-not $sqlProof.Moved) { $reasons.Add('SqlNotProven: the SQL write markers did not move during the run') }
            elseif (-not (($null -ne $sqlProof.CatalogStartupsInWindow -and $sqlProof.CatalogStartupsInWindow -gt 0) -or ($null -ne $sqlProof.CatalogsWrittenInWindow -and $sqlProof.CatalogsWrittenInWindow -gt 0))) { $reasons.Add('SqlNotProven: no test catalog started, and none was written, during the run window') }
        }
        $prov.Sql = [pscustomobject]@{ Target = $preflight.Sql; Proof = [pscustomobject]$sqlProof }
        foreach ($rr in $runResults) {
            if ($rr.Outcome -ne 'Passed') { $reasons.Add("run '$($rr.Name)': $($rr.Outcome) - $($rr.Detail)") }
            elseif (-not $rr.ProvenanceEstablished) { $reasons.Add("run '$($rr.Name)': passed, but $($rr.BinaryProvenance); rerun with -Build so the binary is tied to this commit") }
        }
    }
    $prov.Runs = $runResults
    $prov.FinishedUtc = (Get-Date).ToUniversalTime().ToString('o')
    $prov.Authoritative = ($reasons.Count -eq 0 -and $preflight.Ok -and @($runResults | Where-Object Outcome -ne 'Passed').Count -eq 0 -and $runResults.Count -gt 0)
    Write-TextLf (Join-Path $Out 'provenance.json') (([pscustomobject]$prov) | ConvertTo-Json -Depth 8)

    $md = New-Object System.Collections.Generic.List[string]
    $md.Add("# Evidence: $Record"); $md.Add('')
    $md.Add("**$(if ($prov.Authoritative) { 'AUTHORITATIVE' } else { 'NOT AUTHORITATIVE' })** - commit ``$($id.Head)`` ($($id.Branch)), tree ``$($id.Tree)``, $($id.DirtyTrackedFiles) modified tracked files, started $($prov.StartedUtc)"); $md.Add('')
    if ($reasons.Count) { $md.Add('Reasons it is not authoritative:'); foreach ($x in $reasons) { $md.Add("- $x") }; $md.Add('') }
    $md.Add("_Scope: $($prov.AuthoritativeMeans)_"); $md.Add('')
    $md.Add('| Run | Outcome | Total | Passed | Failed | Not executed | rcsi False / True | Seconds |'); $md.Add('|---|---|---|---|---|---|---|---|')
    foreach ($rr in $runResults) { $c = $rr.Counts; $md.Add("| $($rr.Name) | $($rr.Outcome) | $(if ($c) { $c.Total } else { '-' }) | $(if ($c) { $c.Passed } else { '-' }) | $(if ($c) { $c.Failed } else { '-' }) | $(if ($c) { $c.NotExecuted } else { '-' }) | $(if ($c) { "$($c.RcsiFalse) / $($c.RcsiTrue)" } else { '-' }) | $($rr.DurationSeconds) |") }
    Write-TextLf (Join-Path $Out 'summary.md') (($md -join "`n") + "`n")

    [void](Write-ChecksumFile $Out)
    $verify = Test-ChecksumFile $Out
    if (-not $verify.Ok) { Stop-Tool "the evidence folder does not verify against its own checksum file: $($verify.Errors -join '; ') $($verify.Bad -join ', ')" }
    Write-Host "Evidence: $Out"; Write-Host "  $(if ($prov.Authoritative) { 'AUTHORITATIVE' } else { 'NOT AUTHORITATIVE' }); $($runResults.Count) runs; checksums verified ($($verify.Checked) files)"
    foreach ($x in $reasons) { Write-Host "  - $x" }
    if ($PassThru) { [pscustomobject]@{ Out = $Out; Authoritative = $prov.Authoritative; Reasons = @($reasons); Runs = $runResults } }
    exit $(if ($prov.Authoritative) { 0 } else { 1 })
}
catch { Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red; Write-Host $_.ScriptStackTrace; exit 2 }
