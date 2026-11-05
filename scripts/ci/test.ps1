<#
.SYNOPSIS
    Runs Apperture tests (unit, integration, arch, e2e) for the whole solution, locally.
.DESCRIPTION
    Executes `dotnet test` for Apperture.slnx with the `dotnet` found on PATH (the SDK that global.json names;
    `dotnet` itself applies global.json). Use -Filter to run a subset by name.

    The result files (TRX, one per test project) decide the outcome, not only the exit code of `dotnet test`. Exit code 0
    means all of: the test host exited 0; every result file is readable, complete (its counts are whole numbers), records
    run outcome Completed and agrees with its own result records; at least one test executed; and every executed test
    passed. Executed tests with no passing verdict (inconclusive, not runnable, disconnected) are a failure, so a run
    cannot be reported as a pass because nothing in it failed. A -Filter that matches nothing is a failure.

    Several result files: counts are summed. A project whose tests were all skipped is allowed beside passing projects
    and is named in the output as not authoritative. A project that selected no test is expected under -Filter (only the
    projects holding the matching tests run) and is reported; without -Filter it fails the run, because part of the
    suite never ran. Skipped tests are counted and reported, never treated as passed.

    Tests that need SQL Server FAIL when it is not reachable, by design (see tests/README.md, "A SQL test that cannot
    run against SQL Server fails"). This script never turns that into a skip. For the SQL-backed tests, point
    APPERTURE_TEST_SQL_CONNECTION_STRING at the disposable test SQL server (tests/README.md, "Test SQL target safety").
    GitHub Actions provides SQL through a service container in .github/workflows/ci.yml and does not use this script.

    A -Filter run is for iteration. It is never evidence for a work-package record and never replaces the required CI,
    which runs every test (.ai/playbooks/dev-loop.md).

    APPERTURE_REPO_ROOT (shared by the scripts in scripts/) points the run at another checkout instead of the one this
    script lives in. The script prints the solution it is about to test, so a redirect is visible.
.PARAMETER Configuration
    Debug (default) or Release. CI builds and tests Release.
.PARAMETER Filter
    One name fragment, matched as `FullyQualifiedName~<fragment>`: letters, digits, `_` and `.` only, so it cannot
    smuggle other filter operators in. For several classes call dotnet test directly.
.PARAMETER NoBuild
    Skip the build. The binaries must already be up to date for the configuration you name.
.EXAMPLE
    .\test.ps1
    .\test.ps1 -Filter "PasswordPolicyEvaluatorTests"
    .\test.ps1 -Configuration Release -NoBuild
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [ValidatePattern('^[A-Za-z0-9_.]+\z')]
    [string]$Filter,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$Solution = Join-Path $RepoRoot "Apperture.slnx"

if (-not (Test-Path -LiteralPath $Solution)) {
    throw "Solution not found: $Solution"
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet was not found on PATH. Install the .NET SDK that global.json names (https://dotnet.microsoft.com/download)."
}

# TRX files go under the ignored artifacts/ folder, one folder per run, so a previous run's result can never stand in for this one.
$runStamp = [DateTime]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$ResultsDir = Join-Path (Join-Path (Join-Path $RepoRoot "artifacts") "test-results") $runStamp

$testArgs = @("test", $Solution, "-c", $Configuration)
if ($NoBuild) { $testArgs += "--no-build" }
if ($Filter) { $testArgs += "--filter", "FullyQualifiedName~$Filter" }
$testArgs += "--logger", "trx", "--results-directory", $ResultsDir

Push-Location $RepoRoot
try {
    if ($Filter) {
        Write-Host "Filtered run (FullyQualifiedName~$Filter): for iteration only. It is not evidence, and the required CI runs every test." -ForegroundColor Yellow
    }
    Write-Host "Testing: $Solution" -ForegroundColor Cyan
    if ($env:APPERTURE_REPO_ROOT) {
        Write-Host "APPERTURE_REPO_ROOT is set: this run tests that checkout, not the one this script lives in." -ForegroundColor Yellow
    }
    Write-Host "Running tests..." -ForegroundColor Cyan
    & dotnet @testArgs
    $testExit = $LASTEXITCODE
    if ($testExit -ne 0) { throw "Tests failed (dotnet test exit code $testExit)." }

    # Exit 0 is not enough: `dotnet test` also exits 0 when a filter matches no test anywhere, and a result file can record
    # executed tests that neither passed nor failed. The result files (one per test project) are the evidence, so each is read
    # strictly: a count that is missing or not a whole number, an unreadable file, or a run outcome other than Completed fails.
    $executed = 0; $passed = 0; $failed = 0; $skipped = 0; $trxCount = 0; $errored = 0; $unsettled = 0
    $verdictFiles = 0; $recordFiles = 0   # files whose executed != passed; files whose records disagree with their counters
    $emptyFiles = 0; $skippedOnly = @()
    function Get-TrxCount([System.Xml.XmlElement]$Element, [string]$Name, [string]$File, [switch]$Optional) {
        if (-not $Element.HasAttribute($Name)) {
            if ($Optional) { return 0 }
            throw "Cannot read the test counters: the '$Name' count is missing in ${File}."
        }
        $n = 0
        $styles = [System.Globalization.NumberStyles]::None   # digits only: no sign, no space
        if (-not [int]::TryParse($Element.GetAttribute($Name), $styles, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$n)) {
            throw "Cannot read the test counters: '$Name' is not a whole number in ${File}."
        }
        return $n
    }
    if (Test-Path -LiteralPath $ResultsDir) {
        foreach ($trx in Get-ChildItem -LiteralPath $ResultsDir -Filter *.trx -File) {
            $doc = New-Object System.Xml.XmlDocument
            $doc.XmlResolver = $null
            try { $doc.Load($trx.FullName) } catch { throw "Cannot read the test counters in $($trx.FullName): $($_.Exception.Message)" }
            $counters = @($doc.GetElementsByTagName("Counters"))
            if ($counters.Count -ne 1) { throw "Cannot read the test counters in $($trx.FullName)." }
            $c = $counters[0]
            $summary = @($doc.GetElementsByTagName("ResultSummary"))
            $outcome = if ($summary.Count -eq 1) { $summary[0].GetAttribute("outcome") } else { "" }
            if ($outcome -ne "Completed") { throw "The result file $($trx.FullName) records run outcome '$outcome', not 'Completed'." }
            $trxCount++

            $fTotal = Get-TrxCount $c "total" $trx.FullName
            $fExecuted = Get-TrxCount $c "executed" $trx.FullName
            $fPassed = Get-TrxCount $c "passed" $trx.FullName
            $executed += $fExecuted
            $passed += $fPassed
            $failed += Get-TrxCount $c "failed" $trx.FullName
            $errored += (Get-TrxCount $c "error" $trx.FullName) + (Get-TrxCount $c "timeout" $trx.FullName) + (Get-TrxCount $c "aborted" $trx.FullName)
            # Verdicts other than passed, failed or errored (inconclusive, not runnable, disconnected, ...): optional in the file.
            foreach ($name in "inconclusive", "passedButRunAborted", "notRunnable", "disconnected", "warning", "pending", "inProgress", "completed") {
                $unsettled += Get-TrxCount $c $name $trx.FullName -Optional
            }
            $skipped += $fTotal - $fExecuted
            # Judged per file, so one file cannot make up for another: every test it executed passed.
            if ($fExecuted -ne $fPassed) { $verdictFiles++ }

            # The file's individual result records must be exactly what its counters say: the passed ones, the skipped ones
            # (outcome NotExecuted), and nothing else. A Failed, Error or Inconclusive record can therefore not pass as a skip.
            $records = @($doc.GetElementsByTagName("UnitTestResult"))
            $recPassed = @($records | Where-Object { $_.GetAttribute("outcome") -eq "Passed" }).Count
            $recSkipped = @($records | Where-Object { $_.GetAttribute("outcome") -eq "NotExecuted" }).Count
            if ($recPassed -ne $fPassed -or $recSkipped -ne ($fTotal - $fExecuted) -or $records.Count -ne ($recPassed + $recSkipped)) { $recordFiles++ }

            if ($fTotal -eq 0) { $emptyFiles++ }
            elseif ($fExecuted -eq 0) {
                $method = @($doc.GetElementsByTagName("TestMethod"))
                $assembly = if ($method.Count -gt 0) { $method[0].GetAttribute("codeBase") -replace '^.*[\\/]' } else { Split-Path -Leaf $trx.FullName }
                $skippedOnly += "$assembly executed no test ($fTotal test(s), every one skipped)"
            }
        }
    }
    if ($trxCount -eq 0 -or $executed -eq 0) {
        throw "No test was executed ($trxCount result file(s) in $ResultsDir). The run is not a pass: check -Filter and the configuration."
    }
    if ($failed -gt 0) { throw "Tests failed: the result files record $failed failed test(s) although the test host exited 0." }
    if ($errored -gt 0) { throw "The result files record $errored errored, timed-out or aborted test(s) although the test host exited 0." }
    # A pass means every test that was executed passed. Skipped tests are not executed and are reported below.
    if ($unsettled -gt 0 -or $verdictFiles -gt 0) {
        throw "Executed test(s) without a passing verdict (inconclusive, not runnable, disconnected or similar): $executed executed and $passed passed in all, $verdictFiles result file(s) where the two differ, $unsettled test(s) in other verdict counts. The run is not a pass."
    }
    if ($recordFiles -gt 0) {
        throw "$recordFiles result file(s) hold result records that do not match the counters (passed records, skipped records, or a record with any other outcome). The run is not a pass."
    }
    # One result file per test project. A project that selected no test is expected under -Filter and means a scope that never ran without it.
    if ($emptyFiles -gt 0 -and -not $Filter) {
        throw "$emptyFiles result file(s) selected no test in a run without -Filter: a test project discovered nothing, so part of the suite never ran. Compare with `dotnet test --list-tests`."
    }
    if ($emptyFiles -gt 0) {
        Write-Host "$emptyFiles result file(s) selected no test (expected under -Filter: only the projects holding matching tests run)." -ForegroundColor Yellow
    }
    foreach ($item in $skippedOnly) {
        Write-Host "$item. The run says nothing about that assembly: it is not authoritative for it." -ForegroundColor Yellow
    }

    Write-Host "Tests passed: $passed passed, $skipped skipped or not executed, $failed failed ($trxCount test assemblies; results in $ResultsDir)." -ForegroundColor Green
} finally {
    Pop-Location
}
