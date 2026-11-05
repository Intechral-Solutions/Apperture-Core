#Requires -Version 7.2
<#
.SYNOPSIS
  One controlled, measured clean build trial of Apperture.slnx (DEV-1.3C build-protocol experiment).
.DESCRIPTION
  Runs from a clean git worktree at an expected commit. For one configuration (A, B, C, D or D0) it
  1. checks the pre-conditions (clean tree, commit, free memory, no foreign build activity, clean MSBuild environment),
  2. writes the exact commands to command.txt BEFORE running anything,
  3. runs `dotnet restore` (timed separately) and `dotnet build --no-restore` (timed) into a NEW artifacts path,
  4. samples available memory, paging, CPU and the build processes' memory once per second,
  5. stops only its own build processes if available memory drops below the abort threshold,
  6. counts errors and warnings, looks for crashes (log patterns and Windows event log),
  7. runs `dotnet build-server shutdown` if processes it did not find at the start remain (global; safe only because step 1 refuses to start next to a foreign build process),
  8. writes an assembly inventory (outside the timed section).
  Nothing here changes the repository, any toolchain setting, or any other process.
.NOTES
  PowerShell 7.2+. The harness lives outside the tracked tree while trials run so that the worktree stays clean.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('A', 'B', 'C', 'D', 'C0', 'D0')][string]$Config,
    [Parameter(Mandatory)][string]$Name,
    [Parameter(Mandatory)][string]$ExpectedCommit,
    [Parameter(Mandatory)][string]$RepoRoot,
    [Parameter(Mandatory)][string]$OutputRoot,      # artifacts path parent; the trial uses <OutputRoot>\<Name>
    [Parameter(Mandatory)][string]$EvidenceRoot,    # evidence parent; the trial writes <EvidenceRoot>\<Name>
    [string]$Solution = 'Apperture.slnx',
    [string]$OutputName,                            # same-path control: build into <OutputRoot>\<OutputName> instead of <OutputRoot>\<Name>
    [int]$MinStartAvailableMiB = 2048,
    [int]$AbortBelowAvailableMiB = 600,             # stop our own build above the 500 MiB acceptance floor
    [int]$SampleMs = 1000,
    [switch]$SkipInventory,
    [switch]$AllowKnownIdleProcess                  # tolerate the VS Code C# DevKit build host (recorded, never touched)
    ,[switch]$AllowVisualStudio                    # non-timing controls only: tolerate (and record) the user's Visual Studio processes
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---- configuration matrix: the flags that differ between trials ------------------------------------------
$matrix = @{
    A  = @('--no-incremental', '-m:1', '-p:UseSharedCompilation=false', '--disable-build-servers')   # established protocol
    B  = @('--no-incremental', '-m:1')                                                                # serialised, servers enabled
    C  = @('--no-incremental', '-m:4')                                                                # parallel (4), servers enabled
    D  = @('--no-incremental')                                                                        # SDK defaults for parallelism and servers
    C0 = @('-m:4')                                                                                    # supplementary: -m:4 without --no-incremental
    D0 = @()                                                                                          # SDK defaults, no --no-incremental (supplementary)
}
$buildFlags = $matrix[$Config]
$common = @('-v:q', '-nologo')
# restore accepts the same parallelism / server switches but not --no-incremental
$restoreFlags = @($buildFlags | Where-Object { $_ -ne '--no-incremental' })

$art = Join-Path $OutputRoot $(if ($OutputName) { $OutputName } else { $Name })
$ev = Join-Path $EvidenceRoot $Name

function Get-AvailMiB { return [int]$script:ctrAvail.NextValue() }

# ---- pre-conditions ---------------------------------------------------------------------------------------
Set-Location -LiteralPath $RepoRoot
if (Test-Path -LiteralPath $art) { throw "Output path already exists (trials must not share output): $art" }
if (Test-Path -LiteralPath $ev) { throw "Evidence path already exists: $ev" }
$head = (git rev-parse HEAD).Trim()
if ($head -ne $ExpectedCommit) { throw "HEAD is $head, expected $ExpectedCommit" }
$dirty = @(git status --porcelain=v1 --untracked-files=all)
if ($dirty.Count -gt 0) { throw "Working tree is not clean:`n$($dirty -join "`n")" }
$tree = (git rev-parse 'HEAD^{tree}').Trim()
$badEnv = @(Get-ChildItem Env: | Where-Object { $_.Name -match '^(MSBUILD|DOTNET_|NUGET_|COMPlus_|DOTNET$)' -or $_.Name -in @('Configuration', 'Platform', 'OutputPath', 'DefineConstants', 'ArtifactsPath', 'UseArtifactsOutput') })
if ($badEnv.Count -gt 0) { throw "Build-affecting environment variables are set: $(($badEnv | ForEach-Object Name) -join ', ')" }

$script:ctrAvail = [System.Diagnostics.PerformanceCounter]::new('Memory', 'Available MBytes')
$ctrPages = [System.Diagnostics.PerformanceCounter]::new('Memory', 'Pages/sec')
$ctrCpu = [System.Diagnostics.PerformanceCounter]::new('Processor', '% Processor Time', '_Total')
$ctrPagefile = [System.Diagnostics.PerformanceCounter]::new('Paging File', '% Usage', '_Total')
[void]$ctrPages.NextValue(); [void]$ctrCpu.NextValue()
Start-Sleep -Milliseconds 1000

$startAvail = Get-AvailMiB
if ($startAvail -lt $MinStartAvailableMiB) { throw "Available memory $startAvail MiB is below the start floor $MinStartAvailableMiB MiB" }

$watched = 'dotnet', 'MSBuild', 'VBCSCompiler', 'csc', 'vbc', 'node', 'npm', 'npx', 'esbuild', 'tailwindcss', 'cmd', 'conhost', 'aspire', 'dcp', 'dcpctrl'
$knownIdle = @()
$foreign = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -replace '\.exe$' -in @('MSBuild', 'VBCSCompiler', 'csc', 'vbc', 'dotnet', 'aspire', 'dcp', 'dcpctrl') })
foreach ($f in $foreign) {
    $isDevKit = ($f.CommandLine -like '*ms-dotnettools.csdevkit*') -and ($f.Name -eq 'dotnet.exe')
    if ($isDevKit -and $AllowKnownIdleProcess) { $knownIdle += $f; continue }
    if ($AllowVisualStudio -and ([string]$f.CommandLine -like '*Microsoft Visual Studio*')) { $knownIdle += $f; continue }
    throw "Foreign build-related process present: PID $($f.ProcessId) $($f.Name) $($f.CommandLine)"
}
$baselinePids = @(Get-Process | ForEach-Object Id)

# idle-load snapshot: 5 s of CPU and the memory top-5
$cpuPre = 1..5 | ForEach-Object { Start-Sleep -Milliseconds 1000; [math]::Round($ctrCpu.NextValue(), 1) }
$topMem = Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 8 | ForEach-Object { '{0} {1} MiB' -f $_.ProcessName, [math]::Round($_.WorkingSet64 / 1MB) }

New-Item -ItemType Directory -Path $ev -Force | Out-Null
$sdk = (dotnet --version).Trim()

$restoreCmd = @('restore', $Solution, '--artifacts-path', $art) + $restoreFlags + $common
$buildCmd = @('build', $Solution, '--artifacts-path', $art, '--no-restore') + $buildFlags + $common
$cmdText = @(
    "Trial   : $Name  (configuration $Config)"
    "Commit  : $head   tree $tree"
    "SDK     : $sdk"
    "Cwd     : $RepoRoot"
    "Restore : dotnet $($restoreCmd -join ' ')"
    "Build   : dotnet $($buildCmd -join ' ')"
    "Written : $(Get-Date -Format o) (before any command ran)"
)
Set-Content -LiteralPath (Join-Path $ev 'command.txt') -Value $cmdText -Encoding utf8
Set-Content -LiteralPath (Join-Path $ev 'env.txt') -Value (@(Get-ChildItem Env: | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Value)" } | Where-Object { $_ -notmatch '(?i)(token|secret|password|key)=' }) -join "`n") -Encoding utf8
Set-Content -LiteralPath (Join-Path $ev 'procs-before.txt') -Value (@(Get-CimInstance Win32_Process | Sort-Object Name | ForEach-Object { $cl = (([string]$_.CommandLine) -replace '\s+', ' '); '{0} {1} {2}' -f $_.ProcessId, $_.Name, $cl.Substring(0, [math]::Min(160, $cl.Length)) }) -join "`n") -Encoding utf8

# ---- run helper -------------------------------------------------------------------------------------------
$script:abort = $null
$samples = [System.Collections.Generic.List[object]]::new()

function Stop-OwnBuild($rootPid) {
    # Stops only processes that are descendants of our dotnet process or new build processes of ours.
    $all = Get-CimInstance Win32_Process
    $kill = [System.Collections.Generic.HashSet[int]]::new()
    $queue = [System.Collections.Generic.Queue[int]]::new(); $queue.Enqueue($rootPid)
    while ($queue.Count) { $c = $queue.Dequeue(); if ($kill.Add($c)) { $all | Where-Object { $_.ParentProcessId -eq $c } | ForEach-Object { $queue.Enqueue([int]$_.ProcessId) } } }
    foreach ($p in $all) { if ($p.ProcessId -notin $baselinePids -and ($p.Name -replace '\.exe$') -in @('MSBuild', 'VBCSCompiler', 'csc', 'vbc', 'dotnet')) { [void]$kill.Add([int]$p.ProcessId) } }
    foreach ($id in $kill) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
}

function Invoke-Phase([string]$Phase, [string[]]$ArgList, [string]$LogBase) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $t0 = Get-Date
    $psi = Start-Process -FilePath 'dotnet' -ArgumentList $ArgList -NoNewWindow -PassThru -WorkingDirectory $RepoRoot `
        -RedirectStandardOutput "$LogBase.out.log" -RedirectStandardError "$LogBase.err.log"
    $minAvail = [int]::MaxValue; $maxBuildPrivMiB = 0; $maxBuildWsMiB = 0; $maxProcs = 0; $maxPages = 0.0; $maxPf = 0.0; $n = 0; $cpuSum = 0.0
    while (-not $psi.HasExited) {
        Start-Sleep -Milliseconds $SampleMs
        $avail = Get-AvailMiB
        $procs = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Id -notin $baselinePids -and $_.ProcessName -in $watched })
        $privB = 0L; $wsB = 0L
        foreach ($pp in $procs) { $privB += $pp.PrivateMemorySize64; $wsB += $pp.WorkingSet64 }
        $priv = [math]::Round($privB / 1MB)
        $ws = [math]::Round($wsB / 1MB)
        $pages = [math]::Round($ctrPages.NextValue(), 1); $cpu = [math]::Round($ctrCpu.NextValue(), 1); $pf = [math]::Round($ctrPagefile.NextValue(), 1)
        $samples.Add([pscustomobject]@{ Phase = $Phase; ElapsedS = [math]::Round($sw.Elapsed.TotalSeconds, 1); AvailMiB = $avail; BuildPrivMiB = $priv; BuildWsMiB = $ws; BuildProcs = $procs.Count; PagesPerSec = $pages; CpuPct = $cpu; PagefilePct = $pf })
        $n++; $cpuSum += $cpu
        if ($avail -lt $minAvail) { $minAvail = $avail }
        if ($priv -gt $maxBuildPrivMiB) { $maxBuildPrivMiB = $priv }
        if ($ws -gt $maxBuildWsMiB) { $maxBuildWsMiB = $ws }
        if ($procs.Count -gt $maxProcs) { $maxProcs = $procs.Count }
        if ($pages -gt $maxPages) { $maxPages = $pages }
        if ($pf -gt $maxPf) { $maxPf = $pf }
        if ($avail -lt $AbortBelowAvailableMiB -and -not $script:abort) {
            $script:abort = "Available memory $avail MiB fell below the abort threshold $AbortBelowAvailableMiB MiB during $Phase at $([math]::Round($sw.Elapsed.TotalSeconds)) s"
            Stop-OwnBuild $psi.Id
        }
    }
    $psi.WaitForExit()
    $sw.Stop()
    [pscustomobject]@{
        Phase = $Phase; ExitCode = $psi.ExitCode; Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1); Start = $t0; End = Get-Date
        MinAvailMiB = $(if ($minAvail -eq [int]::MaxValue) { $null } else { $minAvail }); MaxBuildPrivateMiB = $maxBuildPrivMiB; MaxBuildWorkingSetMiB = $maxBuildWsMiB
        MaxBuildProcesses = $maxProcs; MaxPagesPerSec = $maxPages; MaxPagefilePct = $maxPf; AvgCpuPct = $(if ($n) { [math]::Round($cpuSum / $n, 1) } else { $null }); Samples = $n
    }
}

# ---- timed section ----------------------------------------------------------------------------------------
$trialStart = Get-Date
$restore = Invoke-Phase 'restore' $restoreCmd (Join-Path $ev 'restore')
$build = $null
if ($restore.ExitCode -eq 0 -and -not $script:abort) { $build = Invoke-Phase 'build' $buildCmd (Join-Path $ev 'build') }
$trialEnd = Get-Date

$samples | Export-Csv -LiteralPath (Join-Path $ev 'samples.csv') -NoTypeInformation -Encoding utf8
$endAvail = Get-AvailMiB

# ---- servers started by this trial ------------------------------------------------------------------------
$leftover = @(Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -notin $baselinePids -and ($_.Name -replace '\.exe$') -in @('MSBuild', 'VBCSCompiler', 'csc', 'vbc', 'dotnet') } |
        ForEach-Object { '{0} {1} {2}' -f $_.ProcessId, $_.Name, (([string]$_.CommandLine) -replace '\s+', ' ') })
Set-Content -LiteralPath (Join-Path $ev 'servers-after-build.txt') -Value ($leftover -join "`n") -Encoding utf8
$shutdownNote = 'none needed'
if ($leftover.Count -gt 0) {
    # every one of these has a PID that did not exist before the trial, so it is ours; graceful shutdown only
    & dotnet build-server shutdown *> (Join-Path $ev 'build-server-shutdown.txt')
    $shutdownNote = "dotnet build-server shutdown (exit $LASTEXITCODE)"
    Start-Sleep -Seconds 5
}
$stillThere = @(Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -notin $baselinePids -and ($_.Name -replace '\.exe$') -in @('MSBuild', 'VBCSCompiler', 'csc', 'vbc') })

# ---- diagnostics: warnings, errors, crash signals ---------------------------------------------------------
$logs = @('restore.out.log', 'restore.err.log', 'build.out.log', 'build.err.log') | ForEach-Object { Join-Path $ev $_ } | Where-Object { Test-Path -LiteralPath $_ }
$allLines = @($logs | ForEach-Object { Get-Content -LiteralPath $_ })
$norm = { param($l) ($l -replace [regex]::Escape($art), '<ART>') -replace [regex]::Escape($RepoRoot), '<REPO>' }
$warnLines = @($allLines | Where-Object { $_ -match ': warning [A-Za-z]+\d+:' } | ForEach-Object { & $norm $_ })
$errLines = @($allLines | Where-Object { $_ -match ': error [A-Za-z]+\d+:' } | ForEach-Object { & $norm $_ })
$warnLines | Sort-Object | Set-Content -LiteralPath (Join-Path $ev 'warnings-all.txt') -Encoding utf8
$warnUnique = @($warnLines | Sort-Object -Unique)
$phaseWarn = @{}
foreach ($ph in 'restore', 'build') {
    $pl = @(@("$ph.out.log", "$ph.err.log") | ForEach-Object { Join-Path $ev $_ } | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { Get-Content -LiteralPath $_ })
    $pw = @($pl | Where-Object { $_ -match ': warning [A-Za-z]+\d+:' } | ForEach-Object { & $norm $_ })
    $phaseWarn[$ph] = [ordered]@{ Raw = $pw.Count; Unique = @($pw | Sort-Object -Unique).Count }
}
$warnUnique | Set-Content -LiteralPath (Join-Path $ev 'warnings-unique.txt') -Encoding utf8
$errLines | Sort-Object | Set-Content -LiteralPath (Join-Path $ev 'errors.txt') -Encoding utf8
$summaryLines = @($allLines | Where-Object { $_ -match '^\s*\d+\s+(Warning|Error)\(s\)' -or $_ -match 'Build (succeeded|FAILED)' -or $_ -match 'Time Elapsed' })
$crashPatterns = 'MSB4166|MSB4015|MSB4018|MSB4181|child node .* exited|node .* exited prematurely|compiler server|VBCSCompiler.* (crash|terminated)|Unhandled exception|OutOfMemory|CS8032|MSB3883|The build was canceled|Process is terminated'
$crashLines = @($allLines | Where-Object { $_ -match $crashPatterns })
$eventLines = @()
try {
    $eventLines = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $trialStart.AddSeconds(-2); EndTime = $trialEnd.AddSeconds(30) } -ErrorAction Stop |
            Where-Object { $_.ProviderName -in @('Application Error', '.NET Runtime', 'Windows Error Reporting') -and $_.Message -match 'dotnet|MSBuild|VBCSCompiler|csc|testhost' } |
            ForEach-Object { "$($_.TimeCreated.ToString('o')) $($_.ProviderName): $(($_.Message -split "`n")[0])" })
}
catch { if ($_.Exception.Message -notmatch 'No events were found') { $eventLines = @("event log query failed: $($_.Exception.Message)") } }
Set-Content -LiteralPath (Join-Path $ev 'crash-signals.txt') -Value (@($crashLines) + @($eventLines)) -Encoding utf8
Set-Content -LiteralPath (Join-Path $ev 'summary-lines.txt') -Value $summaryLines -Encoding utf8

# ---- environment facts ------------------------------------------------------------------------------------
$av = $null
try { $mp = Get-MpComputerStatus -ErrorAction Stop; $av = "RealTimeProtection=$($mp.RealTimeProtectionEnabled) AMServiceEnabled=$($mp.AMServiceEnabled)" } catch { $av = "unavailable: $($_.Exception.Message)" }
$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1

# ---- result record ----------------------------------------------------------------------------------------
$buildOk = ($null -ne $build) -and ($build.ExitCode -eq 0) -and ($restore.ExitCode -eq 0)
$minAvailAll = (@($restore.MinAvailMiB, $(if ($build) { $build.MinAvailMiB })) | Where-Object { $null -ne $_ } | Measure-Object -Minimum).Minimum
$result = [ordered]@{
    Trial = $Name; Configuration = $Config; Commit = $head; Tree = $tree; Sdk = $sdk
    Solution = $Solution; ArtifactsPath = $art; EvidencePath = $ev
    RestoreCommand = "dotnet $($restoreCmd -join ' ')"; BuildCommand = "dotnet $($buildCmd -join ' ')"
    TrialStart = $trialStart.ToString('o'); TrialEnd = $trialEnd.ToString('o')
    Restore = $restore; Build = $build
    TotalSeconds = [math]::Round($restore.Seconds + $(if ($build) { $build.Seconds } else { 0 }), 1)
    BuildSucceeded = $buildOk
    Aborted = $script:abort
    ErrorLines = $errLines.Count; WarningLinesRaw = $warnLines.Count; WarningLinesUnique = $warnUnique.Count
    WarningsByPhase = $phaseWarn
    SummaryLines = $summaryLines
    CrashSignals = (@($crashLines) + @($eventLines)).Count
    LowestAvailableMiB = $minAvailAll; StartAvailableMiB = $startAvail; EndAvailableMiB = $endAvail
    AvailableAboveFloor500 = ($null -ne $minAvailAll) -and ($minAvailAll -gt 500)
    ServersAfterBuild = $leftover; ServerShutdown = $shutdownNote; ServersRemainingAfterShutdown = $stillThere.Count
    Environment = [ordered]@{
        Os = "$($os.Caption) $($os.Version)"; Cpu = $cpu.Name; Logical = $cpu.NumberOfLogicalProcessors; TotalMemoryGiB = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
        PreTrialCpuPct5s = $cpuPre; TopMemoryProcessesBefore = $topMem; KnownIdleForeignProcesses = @($knownIdle | ForEach-Object { "$($_.ProcessId) $($_.Name)" })
        Defender = $av; PowerShell = $PSVersionTable.PSVersion.ToString()
    }
}
($result | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath (Join-Path $ev 'trial.json') -Encoding utf8

# ---- inventory (outside the timed section) ----------------------------------------------------------------
if ($buildOk -and -not $SkipInventory) {
    & (Join-Path $PSScriptRoot 'Get-AssemblyInventory.ps1') -ArtifactsPath $art -OutFile (Join-Path $ev 'inventory.csv')
}
"{0}: {1}  restore {2}s  build {3}s  total {4}s  min-avail {5} MiB  warnings {6} (unique {7})  errors {8}  crash-signals {9}  exit {10}/{11}{12}" -f `
    $Name, $Config, $restore.Seconds, $(if ($build) { $build.Seconds } else { 'n/a' }), $result.TotalSeconds, $minAvailAll, $warnLines.Count, $warnUnique.Count, $errLines.Count, $result.CrashSignals, $restore.ExitCode, $(if ($build) { $build.ExitCode } else { 'n/a' }), $(if ($script:abort) { "  ABORTED: $($script:abort)" } else { '' })
