<#
.SYNOPSIS
    Run-level proof that no SQL Server test in the IntegrationTests assembly can pass without SQL Server.
.DESCRIPTION
    Runs the whole Apperture.IntegrationTests assembly against an intentionally unreachable SQL endpoint and reconciles the outcomes:

      1. the assembly under test is the expected one and every discovered test was executed (and nothing else was);
      2. every test that PASSED is on the reviewed allow-list of non-SQL tests
         (tests/Apperture.IntegrationTests/SqlExecutionHonesty/approved-non-sql-passes.tsv);
      3. every allow-listed test is still discovered and still passes (so the list cannot rot into a blanket exemption);
      4. every test on the must-fail list (the former soft skips, required-unavailable-failures.tsv) FAILED, with a message that says
         SQL Server was unavailable, not for some unrelated reason;
      5. nothing was skipped or not executed, and enough tests failed that SQL Server was really exercised;
      6. the test run itself behaved as expected (a fresh TRX was written and agrees with its own counters, exit code 1);
      7. the result describes a source tree and a binary that correspond (see PROVENANCE).

    It catches the false passes a source scan cannot see: a silent return in any shape, a swallowed exception, a fixture that falls back
    to another database. A SQL-dependent test that returns, or is skipped, or passes against a fallback, is a Passed test that is not on
    the list, and the check fails.

    The child process is isolated from the developer's own SQL configuration: APPERTURE_*, MSSQL_* and ConnectionStrings__* variables are
    removed, the unreachable targets (203.0.113.x, a documentation address, with a 1 s connect timeout) are set, and
    APPERTURE_TEST_SQL_PROCESS_ENV_ONLY=1 stops the tests reading the repository .env or the runtime endpoint files. Nothing is read from
    .env here, no credential is used, and the development SQL server is never named. The connection strings carry a placeholder password.

    Changing either list, or this script, is a reviewed act: SqlExecutionHonesty/AllowListFingerprintTests pins the SHA-256 of each file,
    so an edit must be accompanied by an edit of the pin in test source. The pin is a safeguard against an accidental regression, not a
    substitute for review of a change to both the script and its pin. A script cannot certify its own integrity: a weakened copy can
    return PASS for its own run. What rejects an unreviewed change is the ordinary required Test step, which runs the pin tests; this
    script's own scenarios (UnitTests SqlExecutionHonestyScriptTests) show that it refuses the false-pass conditions it is meant to.

    EXIT CODES (the contract a gate can rely on)
      0  PASS      authoritative: every check held and the source/build provenance is established.
      1  FAIL      the evidence was rejected, or the run or the infrastructure failed (build, discovery, test host, TRX, git, ...).
      2  RECORDED  -Record only: diagnostic lists were captured. This is never an authoritative pass, and it is deliberately not 0.
    In GitHub Actions (or any CI=true environment) -Record and -DotnetPath are refused with exit 1.

    PROVENANCE
    A hash of the test assembly identifies the binary that ran; it does not by itself prove the binary was built from the current
    source. That relationship is established by one of:
      * built-by-script (no -NoBuild): this script ran dotnet build and it succeeded. MSBuild's own up-to-date check is the claim that
        the output corresponds to the inputs;
      * reused-existing-build (-NoBuild): the tracked tree must equal HEAD, no untracked file that the build would compile may exist,
        and the test assembly must be newer than every tracked build input (source, project, props, resource and config files). An edit
        to a source file, or a checkout that rewrites one, makes that file newer than the binary, and the run fails.
    Both modes record HEAD and its tree, the assembly path, SHA-256 and write time, the configuration, the SDK version, and an identity
    string. The limits that remain: timestamps are not cryptographic, so a deliberately back-dated file, or a build made from a dirty
    tree that was reverted afterwards with older timestamps, can defeat the check; and a modification time cannot see a deletion, so a
    commit made after the build that only deletes tracked files leaves the binary newer than every remaining input, and a stale binary
    can PASS with a report that names the new HEAD. In CI the checkout is fresh and the runner is ephemeral, so the Build step is the
    only writer of the binary and neither limit is reachable there. For authoritative local evidence omit -NoBuild.
.PARAMETER Configuration
    Build configuration, Debug by default.
.PARAMETER ArtifactsPath
    Optional --artifacts-path for the build and test. Keep it inside the repository: source-pin tests find the repository root from the
    test binaries and fail, and are then reported, when they cannot.
.PARAMETER NoBuild
    Reuse an existing build (the same -Configuration and -ArtifactsPath). The PASS then depends on the reused-build provenance checks.
.PARAMETER OutputDirectory
    Where the evidence goes. Default: artifacts/sql-execution-honesty/<utc timestamp> under the repository. When you name a directory the
    script clears only its own output files in it (build.log, discovery.log, run.log, sql-honesty.trx, sql-honesty-report.json,
    observed-*.txt); it never deletes anything else and never deletes recursively.
.PARAMETER Record
    Write the observed passing and failing tests to the output directory for review and do not apply the allow-list. For building or
    updating the lists only. Exits 2 (RECORDED), never 0. Infrastructure checks still apply and still exit 1.
.PARAMETER DotnetPath
    The dotnet executable (or a script standing in for it). A seam for the script's own tests only; refused in CI.
.OUTPUTS
    sql-honesty-report.json in the output directory, and the exit code above.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [string]$ArtifactsPath,
    [switch]$NoBuild,
    [string]$OutputDirectory,
    [switch]$Record,
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$ExitPass = 0
$ExitFail = 1
$ExitRecorded = 2
$runStart = [DateTime]::UtcNow

# ---- refusals that must not depend on anything else ----------------------------------------------------------------------------
$inCi = ($env:GITHUB_ACTIONS -eq 'true') -or ($env:CI -eq 'true')
if ($inCi -and $Record) {
    Write-Host 'REFUSED: -Record produces diagnostic lists, not proof, and is not permitted in CI. A required gate runs this script without it.' -ForegroundColor Red
    exit $ExitFail
}
if ($inCi -and $DotnetPath -ne 'dotnet') {
    Write-Host 'REFUSED: -DotnetPath is a test seam and is not permitted in CI.' -ForegroundColor Red
    exit $ExitFail
}

# ---- state the evidence section reads; every name is initialised so a failure anywhere still produces a report -----------------
$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$Message) { $violations.Add($Message); Write-Host "VIOLATION: $Message" -ForegroundColor Red }

$repoRoot = $null
$resolvedOutput = $null
$allow = $null
$mustFail = $null
$exit = $null
$listExit = $null
$discoveredCount = 0
$executedCount = 0
$assemblies = [System.Collections.Generic.HashSet[string]]::new()
$assemblyPaths = [System.Collections.Generic.HashSet[string]]::new()
$passedNames = [System.Collections.Generic.List[string]]::new()
$failedNames = [System.Collections.Generic.List[string]]::new()
$otherOutcomes = [System.Collections.Generic.List[string]]::new()
$failureCategories = [ordered]@{ unavailable = 0; configuration = 0; refused = 0; connection = 0; other = 0 }
$unexpectedPasses = [System.Collections.Generic.List[string]]::new()
$allowListNotPassing = [System.Collections.Generic.List[string]]::new()
$allowListNotDiscovered = [System.Collections.Generic.List[string]]::new()
$tolerated = [System.Collections.Generic.List[string]]::new()
$mustFailViolations = [System.Collections.Generic.List[string]]::new()
$unexecuted = [System.Collections.Generic.List[string]]::new()
$undiscovered = [System.Collections.Generic.List[string]]::new()
$gitIsDirectory = $false
$provenance = [ordered]@{
    buildMode                = if ($NoBuild) { 'reused-existing-build' } else { 'built-by-script' }
    configuration            = $Configuration
    dotnetExecutable         = $DotnetPath
    sdkVersion               = $null
    gitAvailable             = $false
    repositoryHeadFull       = $null
    repositoryTree           = $null
    trackedChangesFromHead   = @()
    untrackedBuildInputs     = @()
    testAssemblyPath         = $null
    testAssemblySha256       = $null
    testAssemblyLastWriteUtc = $null
    newestBuildInput         = $null
    newestBuildInputUtc      = $null
    assemblyNewerThanEveryBuildInput = $null
    identity                 = $null
}

function Get-NormalizedHash([string]$Path) {
    # SHA-256 of the UTF-8 text with CRLF normalised to LF: the same value AllowListFingerprintTests pins, whatever the checkout's line endings.
    $text = [System.IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
    return [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($text)))
}

function Invoke-Dotnet([string[]]$Arguments, [string]$LogPath) {
    $global:LASTEXITCODE = 0
    & $DotnetPath @Arguments *> $LogPath
    return $LASTEXITCODE
}

function Invoke-Git([string[]]$GitArguments) {
    # Ok = false when git is missing or the command failed: the caller must treat that as "unknown", never as "clean".
    $lines = [System.Collections.Generic.List[string]]::new()
    try {
        $global:LASTEXITCODE = 0
        $output = & git -C $repoRoot @GitArguments 2>$null
        if ($LASTEXITCODE -ne 0) { return [pscustomobject]@{ Ok = $false; Lines = $lines } }
        foreach ($line in @($output)) { if ($null -ne $line -and "$line".Length -gt 0) { $lines.Add("$line") } }
        return [pscustomobject]@{ Ok = $true; Lines = $lines }
    }
    catch { return [pscustomobject]@{ Ok = $false; Lines = $lines } }
}

function Read-List([string]$Path) {
    $entries = [System.Collections.Generic.List[object]]::new()
    $meta = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        if ($line.Trim().Length -eq 0) { continue }
        if ($line.StartsWith('#')) {
            if ($line -match '^#\s*(?<k>[a-z-]+):\s*(?<v>.+?)\s*$') { $meta[$Matches['k']] = $Matches['v'] }
            continue
        }
        $cells = $line.Split("`t")
        if ($cells.Length -lt 4) { throw "Malformed line in ${Path} (need id, category, condition, reason): $line" }
        $entries.Add([pscustomobject]@{ Id = $cells[0]; Category = $cells[1]; Condition = $cells[2]; Reason = $cells[3] })
    }
    return [pscustomobject]@{ Entries = $entries; Meta = $meta }
}

function Get-MethodKey([string]$Name) {
    # Discovery and the TRX spell a few theory rows differently (long arguments are truncated and escaped differently), so the two are
    # reconciled per test method, with counts: the method name is everything before the first "(" of a theory row.
    $paren = $Name.IndexOf('(')
    return $(if ($paren -lt 0) { $Name } else { $Name.Substring(0, $paren) })
}

# The only files this script writes in the output directory, and so the only files it ever removes from it.
$ownedFiles = @('build.log', 'discovery.log', 'run.log', 'sql-honesty.trx', 'sql-honesty-report.json', 'observed-passes.txt', 'observed-failures.txt')

try {
    # ---- locate the repository and the reviewed inputs -------------------------------------------------------------------------
    for ($dir = [System.IO.DirectoryInfo]::new($PSScriptRoot); $null -ne $dir; $dir = $dir.Parent) {
        if (Test-Path -LiteralPath (Join-Path $dir.FullName 'Apperture.slnx')) { $repoRoot = $dir.FullName; break }
    }
    if (-not $repoRoot) { throw "Repository root (Apperture.slnx) not found above $PSScriptRoot" }

    $project = Join-Path $repoRoot 'tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj'
    $listDirectory = Join-Path $repoRoot 'tests/Apperture.IntegrationTests/SqlExecutionHonesty'
    $allowListPath = Join-Path $listDirectory 'approved-non-sql-passes.tsv'
    $mustFailPath = Join-Path $listDirectory 'required-unavailable-failures.tsv'
    foreach ($required in $project, $allowListPath, $mustFailPath) {
        if (-not (Test-Path -LiteralPath $required)) { throw "Required input not found: $required" }
    }

    # ---- a fresh output location -----------------------------------------------------------------------------------------------
    if (-not $OutputDirectory) {
        $OutputDirectory = Join-Path $repoRoot ('artifacts/sql-execution-honesty/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))
    }
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $resolvedOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path
    foreach ($name in $ownedFiles) {
        $owned = Join-Path $resolvedOutput $name
        if (Test-Path -LiteralPath $owned -PathType Container) { throw "Refusing to use ${resolvedOutput}: $name is a directory." }
        if (Test-Path -LiteralPath $owned -PathType Leaf) { Remove-Item -LiteralPath $owned -Force }
    }

    # ---- read the reviewed lists -----------------------------------------------------------------------------------------------
    $allow = Read-List $allowListPath
    $mustFail = Read-List $mustFailPath
    $allowIds = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($entry in $allow.Entries) {
        if (-not $allowIds.TryAdd($entry.Id, $entry)) { throw "Duplicate allow-list entry: $($entry.Id)" }
        if ([string]::IsNullOrWhiteSpace($entry.Category) -or [string]::IsNullOrWhiteSpace($entry.Reason)) { throw "Allow-list entry without category or reason: $($entry.Id)" }
    }
    $minimumDiscovered = [int]$allow.Meta['minimum-discovered']
    $minimumFailed = [int]$allow.Meta['minimum-failed']
    $expectedAssembly = $allow.Meta['assembly']
    if ($minimumDiscovered -le 0 -or $minimumFailed -le 0 -or -not $expectedAssembly) { throw 'The allow-list header must declare assembly, minimum-discovered and minimum-failed.' }

    # ---- the isolated, unreachable environment ---------------------------------------------------------------------------------
    $unreachable = 'Server=203.0.113.7,1433;User Id=honesty;Password=placeholder-not-a-credential;TrustServerCertificate=True;Connect Timeout=1;Initial Catalog={0};'
    $childEnvironment = @{
        APPERTURE_TEST_SQL_CONNECTION_STRING = [string]::Format($unreachable, 'Apperture')
        APPERTURE_TEST_SQL                   = [string]::Format($unreachable, 'Apperture_ManagementGovernanceTests')
        APPERTURE_TEST_SQL_PROCESS_ENV_ONLY  = '1'
    }
    $saved = @{}
    foreach ($item in Get-ChildItem Env:) {
        if ($item.Name -match '^(APPERTURE_|MSSQL_|ConnectionStrings__)') { $saved[$item.Name] = $item.Value }
    }

    $commonArguments = @($project, '-c', $Configuration)
    if ($ArtifactsPath) { $commonArguments += @('--artifacts-path', $ArtifactsPath) }
    $discoveryLog = Join-Path $resolvedOutput 'discovery.log'
    $runLog = Join-Path $resolvedOutput 'run.log'

    try {
        foreach ($name in $saved.Keys) { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue }
        foreach ($name in $childEnvironment.Keys) { Set-Item -LiteralPath "Env:$name" -Value $childEnvironment[$name] }

        if (-not $NoBuild) {
            $buildLog = Join-Path $resolvedOutput 'build.log'
            $buildExit = Invoke-Dotnet (@('build') + $commonArguments + @('-v:q', '-nologo')) $buildLog
            if ($buildExit -ne 0) { throw "dotnet build failed (exit $buildExit); see $buildLog" }
        }

        $global:LASTEXITCODE = 0
        $versionOutput = & $DotnetPath --version 2>$null
        if ($LASTEXITCODE -eq 0 -and $versionOutput) { $provenance['sdkVersion'] = "$(@($versionOutput)[0])".Trim() }

        $listExit = Invoke-Dotnet (@('test') + $commonArguments + @('--no-build', '--list-tests')) $discoveryLog
        $exit = Invoke-Dotnet (@('test') + $commonArguments + @('--no-build', '--logger', 'trx;LogFileName=sql-honesty.trx', '--results-directory', $resolvedOutput)) $runLog
    }
    finally {
        foreach ($name in $childEnvironment.Keys) { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue }
        foreach ($name in $saved.Keys) { Set-Item -LiteralPath "Env:$name" -Value $saved[$name] }
    }

    # ---- discovery -------------------------------------------------------------------------------------------------------------
    $discovered = [System.Collections.Generic.Dictionary[string, int]]::new([System.StringComparer]::Ordinal)
    if (-not (Test-Path -LiteralPath $discoveryLog -PathType Leaf)) { throw "test discovery wrote no output ($discoveryLog)" }
    $discoveryText = [System.IO.File]::ReadAllLines($discoveryLog)
    if ($listExit -ne 0) { Add-Violation "test discovery failed (exit $listExit)" }
    foreach ($line in $discoveryText) {
        if ($line.StartsWith('    ') -and -not $line.StartsWith('     ')) {
            $name = $line.Substring(4)
            $discovered[$name] = 1 + $(if ($discovered.ContainsKey($name)) { $discovered[$name] } else { 0 })
        }
    }
    foreach ($count in $discovered.Values) { $discoveredCount += $count }
    if (-not ($discoveryText -match [regex]::Escape($expectedAssembly))) { Add-Violation "discovery did not report the expected assembly $expectedAssembly" }
    if ($discoveredCount -lt $minimumDiscovered) { Add-Violation "only $discoveredCount tests were discovered; the reviewed floor is $minimumDiscovered" }

    # ---- the run: a TRX this run wrote ------------------------------------------------------------------------------------------
    $trxPath = Join-Path $resolvedOutput 'sql-honesty.trx'
    $results = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[object]]]::new([System.StringComparer]::Ordinal)
    $counters = $null
    $trxUsable = $false
    if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
        Add-Violation "the test run wrote no TRX ($trxPath); the harness did not execute as expected (dotnet test exit $exit). See run.log"
    }
    elseif ([System.IO.File]::GetLastWriteTimeUtc($trxPath) -lt $runStart.AddSeconds(-2)) {
        Add-Violation "the TRX ($trxPath) is older than this run; a previous result must never stand in for this one"
    }
    else {
        $settings = [System.Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
        $reader = $null
        try {
            $reader = [System.Xml.XmlReader]::Create($trxPath, $settings)
            $current = $null
            while ($reader.Read()) {
                if ($reader.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
                switch ($reader.LocalName) {
                    'UnitTestResult' {
                        $current = [pscustomobject]@{ Name = $reader.GetAttribute('testName'); Outcome = $reader.GetAttribute('outcome'); Message = '' }
                        if (-not $results.ContainsKey($current.Name)) { $results[$current.Name] = [System.Collections.Generic.List[object]]::new() }
                        $results[$current.Name].Add($current)
                    }
                    'Message' {
                        if ($null -ne $current -and $current.Message.Length -eq 0) {
                            $text = $reader.ReadElementContentAsString() -replace '\s+', ' '
                            $current.Message = if ($text.Length -gt 600) { $text.Substring(0, 600) } else { $text }
                        }
                    }
                    'TestMethod' {
                        $codeBase = $reader.GetAttribute('codeBase')
                        if ($codeBase) { [void]$assemblies.Add([System.IO.Path]::GetFileName($codeBase)); [void]$assemblyPaths.Add($codeBase) }
                    }
                    'Counters' {
                        $counters = @{ total = [int]$reader.GetAttribute('total'); executed = [int]$reader.GetAttribute('executed'); passed = [int]$reader.GetAttribute('passed'); failed = [int]$reader.GetAttribute('failed') }
                    }
                }
            }
            $trxUsable = $true
        }
        catch [System.Xml.XmlException] {
            Add-Violation "the TRX is malformed and cannot be read: $($_.Exception.Message)"
        }
        finally { if ($null -ne $reader) { $reader.Dispose() } }
    }

    if ($trxUsable) {
        foreach ($name in $results.Keys) {
            foreach ($result in $results[$name]) {
                $executedCount++
                switch ($result.Outcome) {
                    'Passed' { $passedNames.Add($name) }
                    'Failed' {
                        $failedNames.Add($name)
                        $m = $result.Message
                        if ($m -match 'cannot run against SQL Server') { $failureCategories['unavailable']++ }
                        elseif ($m -match 'require a reachable test SQL Server|not configured') { $failureCategories['configuration']++ }
                        elseif ($m -match 'SqlTestTargetRefusedException|Refusing to') { $failureCategories['refused']++ }
                        elseif ($m -match 'SqlException|Connection failed|network-related|timeout|Timeout') { $failureCategories['connection']++ }
                        else { $failureCategories['other']++ }
                    }
                    default { $otherOutcomes.Add("$name => $($result.Outcome)") }
                }
            }
        }

        # ---- reconciliation ----------------------------------------------------------------------------------------------------
        if ($assemblies.Count -ne 1 -or -not $assemblies.Contains($expectedAssembly)) { Add-Violation "the TRX reports assemblies [$($assemblies -join ', ')], expected only $expectedAssembly" }
        if ($null -eq $counters) { Add-Violation 'the TRX has no result counters' }
        elseif ($counters.total -ne $executedCount -or $counters.passed -ne $passedNames.Count -or $counters.failed -ne $failedNames.Count) {
            Add-Violation "the TRX counters (total $($counters.total), passed $($counters.passed), failed $($counters.failed)) disagree with its results ($executedCount, $($passedNames.Count), $($failedNames.Count))"
        }
        if ($exit -ne 1) { Add-Violation "dotnet test exited $exit; with SQL unreachable it must exit 1 (some tests failed). 0 means nothing failed; anything else means the run did not complete normally" }
        if ($otherOutcomes.Count -gt 0) { Add-Violation "$($otherOutcomes.Count) tests neither passed nor failed (skipped or not executed), which this assembly does not allow: $(($otherOutcomes | Select-Object -First 5) -join '; ')" }
        if ($failedNames.Count -lt $minimumFailed) { Add-Violation "only $($failedNames.Count) tests failed; the reviewed floor is $minimumFailed, so SQL Server was not really exercised" }

        # Every discovered test executed, and nothing executed that was not discovered.
        $discoveredByMethod = [System.Collections.Generic.Dictionary[string, int]]::new([System.StringComparer]::Ordinal)
        foreach ($name in $discovered.Keys) {
            $key = Get-MethodKey $name
            $discoveredByMethod[$key] = $discovered[$name] + $(if ($discoveredByMethod.ContainsKey($key)) { $discoveredByMethod[$key] } else { 0 })
        }
        $executedByMethod = [System.Collections.Generic.Dictionary[string, int]]::new([System.StringComparer]::Ordinal)
        foreach ($name in $results.Keys) {
            $key = Get-MethodKey $name
            $executedByMethod[$key] = $results[$name].Count + $(if ($executedByMethod.ContainsKey($key)) { $executedByMethod[$key] } else { 0 })
        }
        foreach ($key in $discoveredByMethod.Keys) {
            $ran = if ($executedByMethod.ContainsKey($key)) { $executedByMethod[$key] } else { 0 }
            if ($ran -lt $discoveredByMethod[$key]) { $unexecuted.Add("$key (discovered $($discoveredByMethod[$key]), executed $ran)") }
        }
        foreach ($key in $executedByMethod.Keys) {
            $found = if ($discoveredByMethod.ContainsKey($key)) { $discoveredByMethod[$key] } else { 0 }
            if ($executedByMethod[$key] -gt $found) { $undiscovered.Add("$key (executed $($executedByMethod[$key]), discovered $found)") }
        }
        if ($discoveredCount -ne $executedCount) { Add-Violation "discovery found $discoveredCount tests but the run executed $executedCount" }
        if ($unexecuted.Count -gt 0) { Add-Violation "$($unexecuted.Count) discovered test methods were not fully executed (excluded or lost), for example: $(($unexecuted | Select-Object -First 3) -join '; ')" }
        if ($undiscovered.Count -gt 0) { Add-Violation "$($undiscovered.Count) test methods executed more rows than were discovered, for example: $(($undiscovered | Select-Object -First 3) -join '; ')" }

        $gitIsDirectory = Test-Path -LiteralPath (Join-Path $repoRoot '.git') -PathType Container

        if (-not $Record) {
            $passedSet = [System.Collections.Generic.HashSet[string]]::new($passedNames, [System.StringComparer]::Ordinal)
            foreach ($name in $passedNames) {
                if (-not $allowIds.ContainsKey($name)) { $unexpectedPasses.Add($name) }
            }
            foreach ($entry in $allow.Entries) {
                # An allow-listed identity is a TRX test name; its method must still be discovered.
                if (-not $discoveredByMethod.ContainsKey((Get-MethodKey $entry.Id))) { $allowListNotDiscovered.Add($entry.Id); continue }
                if ($passedSet.Contains($entry.Id)) { continue }
                # The one narrow, declared layout rule: a source pin that needs a .git directory may fail in a git worktree (.git is a file).
                if ($entry.Condition -eq 'requires-git-directory' -and -not $gitIsDirectory) { $tolerated.Add($entry.Id); continue }
                $allowListNotPassing.Add($entry.Id)
            }
            if ($unexpectedPasses.Count -gt 0) { Add-Violation "$($unexpectedPasses.Count) tests PASSED with SQL Server unreachable and are not on the approved non-SQL allow-list, for example: $(($unexpectedPasses | Select-Object -First 5) -join '; ')" }
            if ($allowListNotDiscovered.Count -gt 0) { Add-Violation "$($allowListNotDiscovered.Count) allow-listed tests were not discovered (renamed or removed?), for example: $(($allowListNotDiscovered | Select-Object -First 3) -join '; ')" }
            if ($allowListNotPassing.Count -gt 0) { Add-Violation "$($allowListNotPassing.Count) allow-listed non-SQL tests did not pass, for example: $(($allowListNotPassing | Select-Object -First 3) -join '; ')" }

            foreach ($entry in $mustFail.Entries) {
                if (-not $results.ContainsKey($entry.Id)) { $mustFailViolations.Add("$($entry.Id) was not executed"); continue }
                foreach ($result in $results[$entry.Id]) {
                    if ($result.Outcome -ne 'Failed') { $mustFailViolations.Add("$($entry.Id) => $($result.Outcome)") }
                    elseif ($result.Message -notmatch 'cannot run against SQL Server|require a reachable test SQL Server|not configured|Connection failed') {
                        $mustFailViolations.Add("$($entry.Id) failed, but not because SQL Server was unavailable: $($result.Message.Substring(0, [Math]::Min(120, $result.Message.Length)))")
                    }
                }
            }
            if ($mustFailViolations.Count -gt 0) { Add-Violation "$($mustFailViolations.Count) must-fail tests did not fail as an unavailable SQL Server: $(($mustFailViolations | Select-Object -First 3) -join '; ')" }
        }
        else {
            [System.IO.File]::WriteAllLines((Join-Path $resolvedOutput 'observed-passes.txt'), [string[]]($passedNames | Sort-Object -CaseSensitive))
            [System.IO.File]::WriteAllLines((Join-Path $resolvedOutput 'observed-failures.txt'), [string[]]($failedNames | Sort-Object -CaseSensitive))
            Write-Host 'RECORD mode: observed lists written; the allow-list was not applied, so this run is not proof (exit 2).' -ForegroundColor Yellow
        }
    }

    # ---- provenance: does this result describe this source and this binary? -----------------------------------------------------
    $head = Invoke-Git @('rev-parse', 'HEAD')
    $tree = Invoke-Git @('rev-parse', 'HEAD^{tree}')
    $changes = Invoke-Git @('status', '--porcelain', '--untracked-files=no')
    $tracked = Invoke-Git @('ls-files')
    $untracked = Invoke-Git @('ls-files', '--others', '--exclude-standard')
    $gitOk = $head.Ok -and $tree.Ok -and $changes.Ok -and $tracked.Ok -and $untracked.Ok -and $head.Lines.Count -eq 1
    $provenance['gitAvailable'] = $gitOk
    if ($gitOk) {
        $provenance['repositoryHeadFull'] = $head.Lines[0]
        $provenance['repositoryTree'] = $tree.Lines[0]
        $provenance['trackedChangesFromHead'] = @($changes.Lines)
    }

    # What the build compiles or copies: source, project, props, resource and config files under src/ and tests/, and at the root.
    $buildInput = '^(?:(?:src|tests)/.*|[^/]*)\.(?:cs|razor|cshtml|csproj|props|targets|resx|json|config|slnx)$'
    $newestInput = [DateTime]::MinValue
    $newestInputPath = $null
    if ($gitOk) {
        $provenance['untrackedBuildInputs'] = @($untracked.Lines | Where-Object { $_ -match $buildInput })
        foreach ($relative in $tracked.Lines) {
            if ($relative -notmatch $buildInput) { continue }
            $full = [System.IO.Path]::Combine($repoRoot, $relative)
            if (-not [System.IO.File]::Exists($full)) { continue }
            $written = [System.IO.File]::GetLastWriteTimeUtc($full)
            if ($written -gt $newestInput) { $newestInput = $written; $newestInputPath = $relative }
        }
        if ($null -ne $newestInputPath) {
            $provenance['newestBuildInput'] = $newestInputPath
            $provenance['newestBuildInputUtc'] = $newestInput.ToString('o')
        }
    }

    $assemblyPath = $null
    $assemblyWritten = $null
    $assemblyPathsFromTrx = @($assemblyPaths)
    if ($assemblyPathsFromTrx.Count -eq 1 -and [System.IO.File]::Exists($assemblyPathsFromTrx[0])) {
        $assemblyPath = [System.IO.Path]::GetFullPath($assemblyPathsFromTrx[0])
        $assemblyWritten = [System.IO.File]::GetLastWriteTimeUtc($assemblyPath)
        $provenance['testAssemblyPath'] = $assemblyPath
        $provenance['testAssemblySha256'] = [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($assemblyPath)))
        $provenance['testAssemblyLastWriteUtc'] = $assemblyWritten.ToString('o')
    }

    if ($gitOk -and $null -ne $assemblyWritten -and $null -ne $newestInputPath) {
        $provenance['assemblyNewerThanEveryBuildInput'] = ($assemblyWritten -ge $newestInput)
    }

    if ($gitOk -and $null -ne $provenance['testAssemblySha256']) {
        $provenance['identity'] = "commit=$($provenance['repositoryHeadFull']) tree=$($provenance['repositoryTree']) assembly-sha256=$($provenance['testAssemblySha256']) configuration=$Configuration sdk=$($provenance['sdkVersion']) build=$($provenance['buildMode'])"
    }

    if (-not $Record -and $trxUsable) {
        if (-not $gitOk) { Add-Violation 'source identity cannot be established: git is unavailable or failed, so the result cannot be tied to a commit' }
        else {
            $changed = @($provenance['trackedChangesFromHead'])
            $untrackedInputs = @($provenance['untrackedBuildInputs'])
            if ($changed.Count -gt 0) { Add-Violation "the working tree is dirty: $($changed.Count) tracked files differ from HEAD (for example $(($changed | Select-Object -First 3) -join '; ')); an authoritative result needs a clean tracked tree" }
            if ($untrackedInputs.Count -gt 0) { Add-Violation "$($untrackedInputs.Count) untracked files would be compiled or copied into the build but are not in HEAD (for example $(($untrackedInputs | Select-Object -First 3) -join '; '))" }
        }

        if ($null -eq $assemblyPath) { Add-Violation "the test assembly that ran could not be identified (TRX codeBase: [$($assemblyPathsFromTrx -join ', ')]), so its provenance is unknown" }
        else {
            $rootWithSeparator = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
            $under = $assemblyPath.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)
            if (-not $under) { Add-Violation "the test assembly ($assemblyPath) is not under the repository ($repoRoot)" }
            if ($assemblyPath -notmatch ('[\\/]' + [regex]::Escape($Configuration) + '[\\/]')) { Add-Violation "the test assembly path ($assemblyPath) is not a $Configuration build output" }
            if ($NoBuild -and $gitOk) {
                if ($null -eq $newestInputPath) { Add-Violation 'no tracked build input was found, so the age of the reused build cannot be judged' }
                elseif ($assemblyWritten -lt $newestInput) {
                    Add-Violation "the reused build is stale: $assemblyPath was written $($assemblyWritten.ToString('o')) but $newestInputPath changed $($newestInput.ToString('o')). Rebuild (omit -NoBuild)"
                }
            }
        }
    }
}
catch {
    Add-Violation "script error: $($_.Exception.Message)"
}

# ---- evidence ------------------------------------------------------------------------------------------------------------------
$verdict = if ($violations.Count -gt 0) { 'FAIL' } elseif ($Record) { 'RECORDED' } else { 'PASS' }
$exitCode = switch ($verdict) { 'PASS' { $ExitPass } 'RECORDED' { $ExitRecorded } default { $ExitFail } }

$scriptHash = $null
try { $scriptHash = Get-NormalizedHash $PSCommandPath } catch { $scriptHash = $null }
$listHash = { param($list, $path) if ($null -ne $list) { Get-NormalizedHash $path } else { $null } }

if ($null -ne $resolvedOutput) {
    $reportPath = Join-Path $resolvedOutput 'sql-honesty-report.json'
    try {
        $report = [ordered]@{
            tool                         = 'Test-SqlExecutionHonesty.ps1'
            scriptSha256                 = $scriptHash
            utc                          = [DateTime]::UtcNow.ToString('o')
            repositoryHead               = "$(if ($provenance['repositoryHeadFull']) { $provenance['repositoryHeadFull'].Substring(0, 8) })"
            configuration                = $Configuration
            mode                         = if ($Record) { 'record' } else { 'enforce' }
            verdict                      = $verdict
            authoritative                = ($verdict -eq 'PASS')
            exitCode                     = $exitCode
            provenance                   = $provenance
            gitIsDirectory               = $gitIsDirectory
            assembly                     = @($assemblies)
            discovered                   = $discoveredCount
            executed                     = $executedCount
            passed                       = $passedNames.Count
            failed                       = $failedNames.Count
            notPassedOrFailed            = $otherOutcomes.Count
            dotnetTestExitCode           = $exit
            failureCategories            = $failureCategories
            unexpectedPasses             = @($unexpectedPasses)
            allowListNotDiscovered       = @($allowListNotDiscovered)
            allowListNotPassing          = @($allowListNotPassing)
            toleratedByDeclaredCondition = @($tolerated)
            mustFailViolations           = @($mustFailViolations)
            discoveredNotExecuted        = @($unexecuted)
            executedNotDiscovered        = @($undiscovered)
            allowListEntries             = if ($null -ne $allow) { $allow.Entries.Count } else { 0 }
            mustFailEntries              = if ($null -ne $mustFail) { $mustFail.Entries.Count } else { 0 }
            allowListSha256              = & $listHash $allow (Join-Path $repoRoot 'tests/Apperture.IntegrationTests/SqlExecutionHonesty/approved-non-sql-passes.tsv')
            mustFailListSha256           = & $listHash $mustFail (Join-Path $repoRoot 'tests/Apperture.IntegrationTests/SqlExecutionHonesty/required-unavailable-failures.tsv')
            violations                   = @($violations)
        }
        [System.IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 6))
        Write-Host "Evidence: $reportPath"
    }
    catch {
        Write-Host "Could not write the report: $($_.Exception.Message)" -ForegroundColor Red
        $verdict = 'FAIL'
        $exitCode = $ExitFail
    }
}

Write-Host ("SQL execution honesty: discovered {0}, executed {1}, passed {2}, failed {3}, verdict {4} (exit {5})" -f $discoveredCount, $executedCount, $passedNames.Count, $failedNames.Count, $verdict, $exitCode)
exit $exitCode
