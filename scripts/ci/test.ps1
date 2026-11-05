<#
.SYNOPSIS
    Runs Apperture tests (unit, integration, arch, e2e) for the whole solution, locally.
.DESCRIPTION
    Executes `dotnet test` for Apperture.slnx with the `dotnet` found on PATH (the SDK that global.json names;
    `dotnet` itself applies global.json). Use -Filter to run a subset by name.

    A run that executed no test at all is a failure, so a -Filter that matches nothing (a typo, a renamed class) cannot
    look like a pass. Exit code: 0 only when the test host exited 0, at least one test executed and none failed.

    Tests that need SQL Server FAIL when it is not reachable, by design (see tests/README.md, "A SQL test that cannot
    run against SQL Server fails"). This script never turns that into a skip. For the SQL-backed tests, point
    APPERTURE_TEST_SQL_CONNECTION_STRING at the disposable test SQL server (tests/README.md, "Test SQL target safety").
    GitHub Actions provides SQL through a service container in .github/workflows/ci.yml and does not use this script.

    A -Filter run is for iteration. It is never evidence for a work-package record and never replaces the required CI,
    which runs every test (.ai/playbooks/dev-loop.md).
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
    Write-Host "Running tests..." -ForegroundColor Cyan
    & dotnet @testArgs
    $testExit = $LASTEXITCODE
    if ($testExit -ne 0) { throw "Tests failed (dotnet test exit code $testExit)." }

    # Exit 0 is not enough: `dotnet test` also exits 0 when a filter matches no test anywhere.
    $executed = 0; $passed = 0; $failed = 0; $skipped = 0; $trxCount = 0
    if (Test-Path -LiteralPath $ResultsDir) {
        foreach ($trx in Get-ChildItem -LiteralPath $ResultsDir -Filter *.trx -File) {
            $doc = New-Object System.Xml.XmlDocument
            $doc.XmlResolver = $null
            $doc.Load($trx.FullName)
            $counters = @($doc.GetElementsByTagName("Counters"))
            if ($counters.Count -ne 1) { throw "Cannot read the test counters in $($trx.FullName)." }
            $c = $counters[0]
            $trxCount++
            $executed += [int]$c.GetAttribute("executed")
            $passed += [int]$c.GetAttribute("passed")
            $failed += [int]$c.GetAttribute("failed")
            $total = [int]$c.GetAttribute("total")
            $skipped += $total - [int]$c.GetAttribute("executed")
        }
    }
    if ($trxCount -eq 0 -or $executed -eq 0) {
        throw "No test was executed ($trxCount result file(s) in $ResultsDir). The run is not a pass: check -Filter and the configuration."
    }
    if ($failed -gt 0) { throw "Tests failed: the result files record $failed failed test(s) although the test host exited 0." }

    Write-Host "Tests passed: $passed passed, $skipped skipped or not executed, $failed failed ($trxCount test assemblies; results in $ResultsDir)." -ForegroundColor Green
} finally {
    Pop-Location
}
