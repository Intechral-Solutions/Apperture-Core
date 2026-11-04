#Requires -Version 7.2
<#
.SYNOPSIS
    Read-only state report that decides whether a validation run may claim to be evidence.
.DESCRIPTION
    Checks the repository and intended commit, the tracked source state, the tools, the SQL target (reduced to server, catalog and user:
    the password is never read into the output), the evidence destination and, when given a selection, that the selected test classes
    exist and (with -Discover) are discovered by the test host. Writes nothing except the optional -Json report and a probe file in the
    evidence destination. A FAIL blocks any claim of valid evidence: scripts/dev/validation/Invoke-Evidence.ps1 runs these checks and
    refuses to start on a FAIL.

    The SQL checks here are advisory about the target (the authority is the C# SqlTestTargetGuard in the test run) and authoritative about
    reachability: with -RequireSql an unreachable target is a FAIL, because the SQL tests fail by design when SQL Server cannot be
    reached and that run could not be evidence.

    Exit codes: 0 = no FAIL (warnings may exist); 1 = at least one FAIL; 2 = the preflight itself could not run.
.PARAMETER ExpectedCommit      Full or abbreviated (7+) commit the tree must be at.
.PARAMETER RequireCleanTracked No modified tracked file (untracked files are reported, not blocking).
.PARAMETER RequireSql          The run includes SQL tests: the target must be configured, safe-looking and reachable.
.PARAMETER ProcessEnvironmentOnly Resolve SQL from the process environment only (as APPERTURE_TEST_SQL_PROCESS_ENV_ONLY=1 does for the tests).
.PARAMETER SqlContainer        Docker container expected to host the test SQL Server: must be running; its StartedAt is reported.
.PARAMETER OutputDir           Evidence destination to validate (not created if it does not exist; probed if it does).
.PARAMETER Selection           JSON written by Select-Tests.ps1 -Json: every selected class must exist in the tree.
.PARAMETER Discover            With -Selection: ask the test host (dotnet test --list-tests, no build) that every selected class yields tests.
.PARAMETER MinFreeGiB          Free space required at the destination (default 5).
#>
[CmdletBinding()]
param(
    [string]$Repo,
    [string]$ExpectedCommit,
    [string]$ExpectedRepositoryName = 'Apperture-Core',
    [switch]$RequireCleanTracked,
    [switch]$RequireSql,
    [switch]$ProcessEnvironmentOnly,
    [string]$EnvFile,
    [string]$SqlContainer,
    [string]$DockerCommand = 'docker',
    [string]$OutputDir,
    [string]$Selection,
    [switch]$Discover,
    [double]$MinFreeGiB = 5,
    [string]$Json,
    [switch]$PassThru,
    [switch]$Quiet
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'validation/lib/ValidationCommon.ps1')

$checks = New-Object System.Collections.Generic.List[object]
$id = $null; $sqlInfo = $null; $internal = $false
function Add-Check([string]$Id, [string]$Status, [string]$Message) { $checks.Add([pscustomobject]@{ Id = $Id; Status = $Status; Message = $Message }) }

try {
    if (-not $Repo) { $Repo = (Invoke-Git $PSScriptRoot @('rev-parse', '--show-toplevel')).Text }
    $id = Get-GitIdentity $Repo
    if (-not $id.Ok) { Add-Check 'repo.worktree' 'FAIL' $id.Error; throw [System.OperationCanceledException]::new('no repository') }
    $Repo = $id.TopLevel
    Add-Check 'repo.worktree' 'PASS' "$Repo (branch $($id.Branch), $(if (Test-Path -LiteralPath (Join-Path $Repo '.git') -PathType Leaf) { 'linked worktree' } else { 'standalone checkout' }))"

    # --- the right repository, at the intended commit
    if (-not $id.OriginUrl) { Add-Check 'repo.identity' 'WARN' 'no origin remote: the repository cannot be confirmed by its remote' }
    elseif ($id.OriginUrl -match [regex]::Escape($ExpectedRepositoryName)) { Add-Check 'repo.identity' 'PASS' "origin $($id.OriginUrl)" }
    else { Add-Check 'repo.identity' 'FAIL' "origin '$($id.OriginUrl)' does not name $ExpectedRepositoryName" }
    if ($ExpectedCommit) {
        if ($ExpectedCommit.Length -lt 7) { Add-Check 'repo.commit' 'FAIL' 'the expected commit must be at least 7 characters' }
        elseif ($id.Head.StartsWith($ExpectedCommit, [StringComparison]::OrdinalIgnoreCase)) { Add-Check 'repo.commit' 'PASS' "HEAD $($id.Head) is the expected commit" }
        else { Add-Check 'repo.commit' 'FAIL' "HEAD is $($id.Head), expected $ExpectedCommit" }
    }
    else { Add-Check 'repo.commit' 'INFO' "HEAD $($id.Head) (no expected commit given: the record must state it)" }

    # --- source state
    if ($id.DirtyTrackedFiles -eq 0) { Add-Check 'repo.clean' 'PASS' "no modified tracked file ($($id.UntrackedFiles) untracked)" }
    else { Add-Check 'repo.clean' $(if ($RequireCleanTracked) { 'FAIL' } else { 'WARN' }) "$($id.DirtyTrackedFiles) modified tracked files (diff hash $($id.DiffHash)); evidence of a dirty tree is not evidence of the commit" }
    if (@($id.UntrackedBuildFiles).Count -gt 0) { Add-Check 'repo.untracked' $(if ($RequireCleanTracked) { 'FAIL' } else { 'WARN' }) "$(@($id.UntrackedBuildFiles).Count) untracked file(s) among the sources or build inputs, e.g. $(@($id.UntrackedBuildFiles | Select-Object -First 3) -join ', '): a build compiles them, so the tree is not the commit" }
    if (@($id.AncestorBuildFiles).Count -gt 0) { Add-Check 'repo.ancestors' $(if ($RequireCleanTracked) { 'FAIL' } else { 'WARN' }) "MSBuild/NuGet configuration above the repository, invisible to Git, is imported by every build: $(@($id.AncestorBuildFiles | Select-Object -First 3) -join ', ')" }
    $envRisk = Get-BuildEnvironmentRisks
    if ($envRisk.Risky) { Add-Check 'env.msbuild' $(if ($RequireCleanTracked) { 'FAIL' } else { 'WARN' }) "the environment changes what a build compiles without any file in the repository: variables [$($envRisk.HookVariables -join ', ')]; user ImportBefore/ImportAfter files [$($envRisk.UserImportFiles -join ', ')]" }
    elseif ($envRisk.OtherVariables.Count) { Add-Check 'env.msbuild' 'INFO' "MSBuild/NuGet variables are set (names only): $($envRisk.OtherVariables -join ', ')" }
    if ($id.IndexLockPresent) { Add-Check 'repo.indexlock' 'WARN' "an index.lock exists in $($id.GitDir): commands that write the index will fail; this tool does not touch it" }

    # --- tools
    [void](& dotnet --version 2>&1); $dnExit = $LASTEXITCODE
    Push-Location $Repo; try { $dnRepo = & dotnet --version 2>&1; $dnRepoExit = $LASTEXITCODE } finally { Pop-Location }
    if ($dnExit -ne 0) { Add-Check 'tool.dotnet' 'FAIL' 'dotnet is not available' }
    elseif ($dnRepoExit -ne 0) { Add-Check 'tool.dotnet' 'FAIL' "no installed SDK satisfies the repository's global.json: $(@($dnRepo)[0])" }
    else { Add-Check 'tool.dotnet' 'PASS' "SDK $dnRepo satisfies global.json" }
    Add-Check 'tool.pwsh' 'PASS' "PowerShell $($PSVersionTable.PSVersion)"

    # --- SQL target
    $sql = Resolve-SqlTarget $Repo -ProcessEnvironmentOnly:$ProcessEnvironmentOnly -EnvFile $EnvFile
    $sqlInfo = $null
    if (-not $sql.Configured) { Add-Check 'sql.target' $(if ($RequireSql) { 'FAIL' } else { 'INFO' }) 'no APPERTURE_TEST_SQL_CONNECTION_STRING in the process environment or .env: SQL tests would fail by design' }
    else {
        $i = $sql.Info; $sqlInfo = $i
        $problems = @()
        if ($i.Port -eq 14333) { $problems += 'port 14333 is the development SQL server' }
        if (-not $i.Catalog) { $problems += 'no catalog named' } elseif ($i.Catalog -notmatch '^Apperture_.+') { $problems += "catalog '$($i.Catalog)' does not start with Apperture_" }
        if ($i.NamedPipeOrLocalDb) { $problems += 'a named pipe or LocalDB target is not a supported test target' }
        if ($i.Ambiguous) { $problems += 'a keyword (server, database or user) appears more than once with different values; SqlClient uses the last, a reader of the text may not' }
        if ($i.Server -and -not $i.Port -and $i.Server -match ',') { $problems += 'the port could not be read from the server value' }
        if ($problems.Count) { Add-Check 'sql.target' 'FAIL' "$($i.Server) / $($i.Catalog) / user '$($i.User)' from $($sql.Source): $($problems -join '; ')" }
        else {
            $local = $i.Host -in 'localhost', '127.0.0.1', '::1', '.' -or $i.Host -match '^(10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.)'
            Add-Check 'sql.target' $(if ($local) { 'PASS' } else { 'WARN' }) "$($i.Server) / $($i.Catalog) / user '$($i.User)' from $($sql.Source), password $(if ($i.HasPassword) { 'present' } else { 'absent' })$(if (-not $local) { '; not a loopback or private address: only the C# target guard can vouch for it' })"
        }
        if ($RequireSql -and $problems.Count) {
            # A target that was just rejected (the development server above all) is never connected to, not even to see whether it answers.
            Add-Check 'sql.reachable' 'INFO' 'not probed: the target was rejected above'
        }
        elseif ($RequireSql) {
            $port = if ($i.Port) { $i.Port } else { 1433 }
            if (Test-TcpReachable $i.Host $port) { Add-Check 'sql.reachable' 'PASS' "TCP $($i.Host):$port accepts connections" }
            else { Add-Check 'sql.reachable' 'FAIL' "TCP $($i.Host):$port is not reachable: the SQL tests would fail by design" }
        }
    }
    if ($SqlContainer) {
        $d = & $DockerCommand inspect -f '{{.State.Status}} running={{.State.Running}} startedAt={{.State.StartedAt}}' $SqlContainer 2>&1; $dExit = $LASTEXITCODE
        if ($dExit -ne 0) { Add-Check 'sql.container' $(if ($RequireSql) { 'FAIL' } else { 'WARN' }) "container '$SqlContainer' cannot be inspected: $(@($d)[0])" }
        elseif ("$d" -match 'running=true') { Add-Check 'sql.container' 'PASS' "$SqlContainer $d" }
        else { Add-Check 'sql.container' 'FAIL' "$SqlContainer is not running: $d" }
    }

    # --- evidence destination
    if ($OutputDir) {
        $out = [IO.Path]::GetFullPath($OutputDir); $problems = @()
        $artifactsRoot = Join-Path $Repo 'artifacts'
        if (-not (Test-PathUnder $out $artifactsRoot)) { $problems += "outside $artifactsRoot (the evidence convention keeps working evidence in the ignored artifacts/ folder)" }
        if ($out -match '[\\/](bin|obj)([\\/]|$)') { $problems += 'inside a bin or obj folder (build output is deleted at package close)' }
        if (Test-HasReparsePoint $out $Repo) { $problems += 'passes through a link' }
        if ((Invoke-Git $Repo @('ls-files', '--', $out)).Text) { $problems += 'contains tracked files' }
        if ((Test-Path -LiteralPath $out) -and (Get-ChildItem -LiteralPath $out -Force | Measure-Object).Count -gt 0) { $problems += 'is not empty (evidence is never overwritten)' }
        $probeRoot = $out; while ($probeRoot -and -not (Test-Path -LiteralPath $probeRoot)) { $probeRoot = [IO.Path]::GetDirectoryName($probeRoot) }
        if ($probeRoot) {
            try { $probe = Join-Path $probeRoot ".preflight-probe-$([guid]::NewGuid().ToString('N'))"; [IO.File]::WriteAllText($probe, 'x'); Remove-Item -LiteralPath $probe -Force }
            catch { $problems += "not writable ($probeRoot)" }
            try { $free = [Math]::Round((Get-PSDrive -Name ([IO.Path]::GetPathRoot($probeRoot).Substring(0, 1))).Free / 1GB, 1); if ($free -lt $MinFreeGiB) { $problems += "only $free GiB free (need $MinFreeGiB)" } } catch { }
        }
        if ($problems.Count) { Add-Check 'out.destination' 'FAIL' "$out : $($problems -join '; ')" } else { Add-Check 'out.destination' 'PASS' "$out is a safe, empty, writable destination" }
    }

    # --- test selection and discovery
    if ($Selection) {
        if (-not (Test-Path -LiteralPath $Selection -PathType Leaf)) { Add-Check 'selection.file' 'FAIL' "selection file not found: $Selection" }
        else {
            $sel = Get-Content -LiteralPath $Selection -Raw | ConvertFrom-Json
            try {
                $declared = (Invoke-GitGrep $Repo @('-h', '-o', '-E', '-I', '--untracked', '^\s*(public|internal)\s+(sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+[A-Za-z0-9_]+', '--', 'tests')).Text -split "`n" | ForEach-Object { ($_ -split '\s+')[-1] }
                $absent = @($sel.Selected | Where-Object { $_.Class -notin $declared } | ForEach-Object Class)
                if ($absent.Count) { Add-Check 'selection.exist' 'FAIL' "$($absent.Count) selected classes do not exist in this tree: $($absent -join ', ')" } else { Add-Check 'selection.exist' 'PASS' "all $(@($sel.Selected).Count) selected classes exist" }
            }
            catch { Add-Check 'selection.exist' 'FAIL' "the search for the selected classes failed: $($_.Exception.Message)" }
            foreach ($f in @($sel.Missing) + @($sel.Ambiguous) + @($sel.Unmapped)) { if ($f) { Add-Check 'selection.report' 'WARN' "selector reported: $f" } }
            if ($Discover) {
                foreach ($p in $sel.Filters.PSObject.Properties) {
                    $csproj = Get-Item -LiteralPath (Join-Path $Repo "tests/Apperture.$($p.Name)/Apperture.$($p.Name).csproj") -ErrorAction SilentlyContinue
                    if (-not $csproj) { Add-Check "discover.$($p.Name)" 'FAIL' "no test project tests/Apperture.$($p.Name) found"; continue }
                    $list = & dotnet test $csproj.FullName --no-build --list-tests --filter $p.Value -nologo 2>&1 | ForEach-Object { [string]$_ }
                    if ($LASTEXITCODE -ne 0) { Add-Check "discover.$($p.Name)" 'FAIL' "the test host could not list tests (build first, or the filter is invalid): $($list | Select-Object -Last 1)"; continue }
                    $names = @($list | Where-Object { $_ -match '^\s{4}\S' } | ForEach-Object { $_.Trim() })
                    $none = @($sel.Selected | Where-Object Project -eq $p.Name | ForEach-Object Class | Where-Object { $c = $_; -not ($names | Where-Object { $_.Contains(".$c.") }) })
                    if ($names.Count -eq 0) { Add-Check "discover.$($p.Name)" 'FAIL' 'the filter discovers no test' }
                    elseif ($none.Count) { Add-Check "discover.$($p.Name)" 'FAIL' "$($names.Count) tests discovered, but none for: $($none -join ', ')" }
                    else { Add-Check "discover.$($p.Name)" 'PASS' "$($names.Count) tests discovered; every selected class yields tests" }
                }
            }
        }
    }
}
catch [System.OperationCanceledException] { }
catch { Add-Check 'preflight.error' 'FAIL' "the preflight could not complete: $($_.Exception.Message)"; $internal = $true }

$fails = @($checks | Where-Object Status -eq 'FAIL'); $warns = @($checks | Where-Object Status -eq 'WARN')
$result = [pscustomobject]@{
    Ok = ($fails.Count -eq 0); GeneratedUtc = (Get-Date).ToUniversalTime().ToString('o'); Repo = $Repo
    Head = $(if ($id -and $id.Ok) { $id.Head } else { $null }); Branch = $(if ($id -and $id.Ok) { $id.Branch } else { $null })
    Identity = $id; Sql = $sqlInfo; Checks = $checks.ToArray()
}
if ($Json) { Write-TextLf $Json ($result | ConvertTo-Json -Depth 6) }
if (-not $Quiet) {
    foreach ($c in $checks) { '{0,-5} {1,-18} {2}' -f $c.Status, $c.Id, $c.Message }
    if ($fails.Count) { "BLOCKED: $($fails.Count) failed checks. Nothing run from here may be called valid evidence." } else { "READY ($($warns.Count) warnings)." }
}
if ($PassThru) { $result }
exit $(if ($internal) { 2 } elseif ($fails.Count) { 1 } else { 0 })
