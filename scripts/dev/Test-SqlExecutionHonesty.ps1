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
      6. the test run itself behaved as expected (a TRX was written and agrees with its own counters, exit code 1).

    It catches the false passes a source scan cannot see: a silent return in any shape, a swallowed exception, a fixture that falls back
    to another database. A SQL-dependent test that returns, or is skipped, or passes against a fallback, is a Passed test that is not on
    the list, and the check fails.

    The child process is isolated from the developer's own SQL configuration: APPERTURE_*, MSSQL_* and ConnectionStrings__* variables are
    removed, the unreachable targets (203.0.113.x, a documentation address, with a 1 s connect timeout) are set, and
    APPERTURE_TEST_SQL_PROCESS_ENV_ONLY=1 stops the tests reading the repository .env or the runtime endpoint files. Nothing is read from
    .env here, no credential is used, and the development SQL server is never named. The connection strings carry a placeholder password.

    Changing either list is a reviewed act: SqlExecutionHonesty/AllowListFingerprintTests pins the SHA-256 of each file, so an edit must
    be accompanied by an edit of the pin in test source.
.PARAMETER Configuration
    Build configuration, Debug by default.
.PARAMETER ArtifactsPath
    Optional --artifacts-path for the build and test. Keep it inside the repository: source-pin tests find the repository root from the
    test binaries and fail, and are then reported, when they cannot.
.PARAMETER NoBuild
    Reuse an existing build (the same -Configuration and -ArtifactsPath).
.PARAMETER OutputDirectory
    Where the evidence goes. Default: artifacts/sql-execution-honesty/<utc timestamp> under the repository.
.PARAMETER Record
    Write the observed passing and failing tests to the output directory for review and do not apply the allow-list. For building or
    updating the lists only; the run is not proof. Infrastructure checks still apply.
.OUTPUTS
    sql-honesty-report.json in the output directory; exit code 0 only if every check holds.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [string]$ArtifactsPath,
    [switch]$NoBuild,
    [string]$OutputDirectory,
    [switch]$Record
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# ---- locate the repository and the reviewed inputs -----------------------------------------------------------------------------
$repoRoot = $null
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

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot ('artifacts/sql-execution-honesty/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path

$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$Message) { $violations.Add($Message); Write-Host "VIOLATION: $Message" -ForegroundColor Red }

# ---- read the reviewed lists ---------------------------------------------------------------------------------------------------
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

# ---- the isolated, unreachable environment -------------------------------------------------------------------------------------
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

function Invoke-Dotnet([string[]]$Arguments, [string]$LogPath) {
    $global:LASTEXITCODE = 0
    & dotnet @Arguments *> $LogPath
    return $LASTEXITCODE
}

$commonArguments = @($project, '-c', $Configuration)
if ($ArtifactsPath) { $commonArguments += @('--artifacts-path', $ArtifactsPath) }

$exit = $null
$listExit = $null
try {
    foreach ($name in $saved.Keys) { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue }
    foreach ($name in $childEnvironment.Keys) { Set-Item -LiteralPath "Env:$name" -Value $childEnvironment[$name] }

    if (-not $NoBuild) {
        $buildExit = Invoke-Dotnet (@('build') + $commonArguments + @('-v:q', '-nologo')) (Join-Path $OutputDirectory 'build.log')
        if ($buildExit -ne 0) { throw "dotnet build failed (exit $buildExit); see $(Join-Path $OutputDirectory 'build.log')" }
    }

    $listLog = Join-Path $OutputDirectory 'discovery.log'
    $listExit = Invoke-Dotnet (@('test') + $commonArguments + @('--no-build', '--list-tests')) $listLog

    $trxName = 'sql-honesty.trx'
    $exit = Invoke-Dotnet (@('test') + $commonArguments + @('--no-build', '--logger', "trx;LogFileName=$trxName", '--results-directory', $OutputDirectory)) (Join-Path $OutputDirectory 'run.log')
}
finally {
    foreach ($name in $childEnvironment.Keys) { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue }
    foreach ($name in $saved.Keys) { Set-Item -LiteralPath "Env:$name" -Value $saved[$name] }
}

# ---- discovery -----------------------------------------------------------------------------------------------------------------
$discovered = [System.Collections.Generic.Dictionary[string, int]]::new([System.StringComparer]::Ordinal)
$discoveryText = [System.IO.File]::ReadAllLines((Join-Path $OutputDirectory 'discovery.log'))
if ($listExit -ne 0) { Add-Violation "test discovery failed (exit $listExit)" }
foreach ($line in $discoveryText) {
    if ($line.StartsWith('    ') -and -not $line.StartsWith('     ')) {
        $name = $line.Substring(4)
        $discovered[$name] = 1 + $(if ($discovered.ContainsKey($name)) { $discovered[$name] } else { 0 })
    }
}
$discoveredCount = 0
foreach ($count in $discovered.Values) { $discoveredCount += $count }
if (-not ($discoveryText -match [regex]::Escape($expectedAssembly))) { Add-Violation "discovery did not report the expected assembly $expectedAssembly" }
if ($discoveredCount -lt $minimumDiscovered) { Add-Violation "only $discoveredCount tests were discovered; the reviewed floor is $minimumDiscovered" }

# ---- the run --------------------------------------------------------------------------------------------------------------------
$trxPath = Join-Path $OutputDirectory 'sql-honesty.trx'
if (-not (Test-Path -LiteralPath $trxPath)) { throw "The test run wrote no TRX ($trxPath); the harness did not execute as expected. See run.log." }

$results = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[object]]]::new([System.StringComparer]::Ordinal)
$assemblies = [System.Collections.Generic.HashSet[string]]::new()
$counters = $null
$settings = [System.Xml.XmlReaderSettings]::new()
$settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
$reader = [System.Xml.XmlReader]::Create($trxPath, $settings)
try {
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
                if ($codeBase) { [void]$assemblies.Add([System.IO.Path]::GetFileName($codeBase)) }
            }
            'Counters' {
                $counters = @{ total = [int]$reader.GetAttribute('total'); executed = [int]$reader.GetAttribute('executed'); passed = [int]$reader.GetAttribute('passed'); failed = [int]$reader.GetAttribute('failed') }
            }
        }
    }
}
finally { $reader.Dispose() }

$executedCount = 0
$passedNames = [System.Collections.Generic.List[string]]::new()
$failedNames = [System.Collections.Generic.List[string]]::new()
$otherOutcomes = [System.Collections.Generic.List[string]]::new()
$failureCategories = [ordered]@{ unavailable = 0; configuration = 0; refused = 0; connection = 0; other = 0 }
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

# ---- reconciliation ------------------------------------------------------------------------------------------------------------
if ($assemblies.Count -ne 1 -or -not $assemblies.Contains($expectedAssembly)) { Add-Violation "the TRX reports assemblies [$($assemblies -join ', ')], expected only $expectedAssembly" }
if ($null -eq $counters) { Add-Violation 'the TRX has no result counters' }
elseif ($counters.total -ne $executedCount -or $counters.passed -ne $passedNames.Count -or $counters.failed -ne $failedNames.Count) {
    Add-Violation "the TRX counters (total $($counters.total), passed $($counters.passed), failed $($counters.failed)) disagree with its results ($executedCount, $($passedNames.Count), $($failedNames.Count))"
}
if ($exit -ne 1) { Add-Violation "dotnet test exited $exit; with SQL unreachable it must exit 1 (some tests failed). 0 means nothing failed; anything else means the run did not complete normally" }
if ($otherOutcomes.Count -gt 0) { Add-Violation "$($otherOutcomes.Count) tests neither passed nor failed (skipped or not executed), which this assembly does not allow: $(($otherOutcomes | Select-Object -First 5) -join '; ')" }
if ($failedNames.Count -lt $minimumFailed) { Add-Violation "only $($failedNames.Count) tests failed; the reviewed floor is $minimumFailed, so SQL Server was not really exercised" }

# Every discovered test executed, and nothing executed that was not discovered. Discovery and the TRX spell a few theory rows
# differently (long arguments are truncated and escaped differently), so the two are reconciled per test method, with counts:
# the method name is everything before the first "(" of a theory row, and a lost or extra row changes a method's count.
function Get-MethodKey([string]$Name) {
    $paren = $Name.IndexOf('(')
    return $(if ($paren -lt 0) { $Name } else { $Name.Substring(0, $paren) })
}
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
$undiscovered = [System.Collections.Generic.List[string]]::new()
$unexecuted = [System.Collections.Generic.List[string]]::new()
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
$unexpectedPasses = [System.Collections.Generic.List[string]]::new()
$allowListNotPassing = [System.Collections.Generic.List[string]]::new()
$allowListNotDiscovered = [System.Collections.Generic.List[string]]::new()
$tolerated = [System.Collections.Generic.List[string]]::new()
$mustFailViolations = [System.Collections.Generic.List[string]]::new()

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
    [System.IO.File]::WriteAllLines((Join-Path $OutputDirectory 'observed-passes.txt'), [string[]]($passedNames | Sort-Object -CaseSensitive))
    [System.IO.File]::WriteAllLines((Join-Path $OutputDirectory 'observed-failures.txt'), [string[]]($failedNames | Sort-Object -CaseSensitive))
    Write-Host 'RECORD mode: observed lists written; the allow-list was not applied, so this run is not proof.' -ForegroundColor Yellow
}

# ---- evidence --------------------------------------------------------------------------------------------------------------------
# SHA-256 of the UTF-8 text with CRLF normalised to LF: the same value AllowListFingerprintTests pins, whatever the checkout's line endings.
function Get-NormalizedHash([string]$Path) {
    $text = [System.IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
    return [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($text)))
}

$head = (& git -C $repoRoot rev-parse --short HEAD 2>$null)
$report = [ordered]@{
    tool                       = 'Test-SqlExecutionHonesty.ps1'
    utc                        = [DateTime]::UtcNow.ToString('o')
    repositoryHead             = "$head"
    configuration              = $Configuration
    mode                       = if ($Record) { 'record' } else { 'enforce' }
    gitIsDirectory             = $gitIsDirectory
    assembly                   = @($assemblies)
    discovered                 = $discoveredCount
    executed                   = $executedCount
    passed                     = $passedNames.Count
    failed                     = $failedNames.Count
    notPassedOrFailed          = $otherOutcomes.Count
    dotnetTestExitCode         = $exit
    failureCategories          = $failureCategories
    unexpectedPasses           = @($unexpectedPasses)
    allowListNotDiscovered     = @($allowListNotDiscovered)
    allowListNotPassing        = @($allowListNotPassing)
    toleratedByDeclaredCondition = @($tolerated)
    mustFailViolations         = @($mustFailViolations)
    discoveredNotExecuted      = @($unexecuted)
    executedNotDiscovered      = @($undiscovered)
    allowListEntries           = $allow.Entries.Count
    mustFailEntries            = $mustFail.Entries.Count
    allowListSha256            = Get-NormalizedHash $allowListPath
    mustFailListSha256         = Get-NormalizedHash $mustFailPath
    violations                 = @($violations)
    verdict                    = if ($violations.Count -eq 0 -and -not $Record) { 'PASS' } elseif ($Record -and $violations.Count -eq 0) { 'RECORDED' } else { 'FAIL' }
}
$reportPath = Join-Path $OutputDirectory 'sql-honesty-report.json'
[System.IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 6))

Write-Host ("SQL execution honesty: discovered {0}, executed {1}, passed {2} (allow-listed {3}), failed {4}, verdict {5}" -f $discoveredCount, $executedCount, $passedNames.Count, $allow.Entries.Count, $failedNames.Count, $report.verdict)
Write-Host "Evidence: $reportPath"
if ($report.verdict -eq 'FAIL') { exit 1 }
exit 0
