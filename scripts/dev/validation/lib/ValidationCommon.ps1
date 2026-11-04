#Requires -Version 7.2
# Shared helpers for the validation toolkit (dot-sourced; defines functions only, runs nothing).
# Nothing here prints, stores or passes a credential: connection strings are reduced to host, port, catalog and user.
# Entry scripts set strict mode themselves; a dot-sourced library must not change the caller's. These functions must therefore work under
# `Set-StrictMode -Version Latest` (a property that does not exist throws there and is silently $null elsewhere): the self-test runs them strict.
$script:CodeHashError = ''

# ---------------------------------------------------------------------------------------------------------------- arguments
function Expand-List($Values, [string]$Separator = ',') {
    # `pwsh -File tool.ps1 -List a,b` hands a [string[]] parameter the single string 'a,b' (and drops a second value given after a space),
    # so list parameters are split here. Inside PowerShell (the call operator) real arrays pass through unchanged.
    @($Values | Where-Object { $null -ne $_ } | ForEach-Object { ([string]$_) -split [regex]::Escape($Separator) } | Where-Object { $_ -ne '' })
}

# ---------------------------------------------------------------------------------------------------------------- hashing / files
function Get-Sha256Hex([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

function Get-Sha256OfText([string]$Text) {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Text)
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Write-TextLf([string]$Path, [string]$Text) {
    # UTF-8 without BOM, LF endings, so `sha256sum -c` and Git agree on every platform.
    [IO.File]::WriteAllText($Path, ($Text -replace "`r`n", "`n"), [Text.UTF8Encoding]::new($false))
}

function Get-RelativeFiles([string]$Root, [string[]]$ExcludeNames = @()) {
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $opts = [IO.EnumerationOptions]@{ RecurseSubdirectories = $true; AttributesToSkip = 0 }
    $list = foreach ($f in [IO.Directory]::EnumerateFiles($rootFull, '*', $opts)) {
        $rel = $f.Substring($rootFull.Length + 1).Replace('\', '/')
        if ($ExcludeNames -notcontains $rel) { $rel }
    }
    @($list | Sort-Object { $_ } -CaseSensitive)
}

function Write-ChecksumFile([string]$Root, [string]$FileName = 'SHA256SUMS.txt', [string[]]$ExcludeNames = @()) {
    $exclude = @($FileName) + $ExcludeNames
    $lines = foreach ($rel in (Get-RelativeFiles $Root $exclude)) { '{0}  {1}' -f (Get-Sha256Hex (Join-Path $Root $rel)), $rel }
    Write-TextLf (Join-Path $Root $FileName) ((@($lines) -join "`n") + "`n")
    @($lines).Count
}

function Test-ChecksumFile([string]$Root, [string]$FileName = 'SHA256SUMS.txt', [string[]]$ExcludeNames = @()) {
    # Verifies a sha256sum-style file against the folder: every listed file present and identical, no unlisted file present, no
    # listed path escaping the folder, no duplicate entry. Returns an object; Ok is true only when all of that holds.
    $r = [pscustomobject]@{ Ok = $false; Checked = 0; Bad = @(); Missing = @(); Extra = @(); Errors = @() }
    $sums = Join-Path $Root $FileName
    if (-not (Test-Path -LiteralPath $sums -PathType Leaf)) { $r.Errors += "checksum file '$FileName' not found"; return $r }
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $listed = @{}
    foreach ($line in [IO.File]::ReadAllLines($sums)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch '^([0-9a-fA-F]{64}) [ *](.+)$') { $r.Errors += "malformed line: $($line.Substring(0, [Math]::Min(60, $line.Length)))"; continue }
        $hash = $Matches[1].ToLowerInvariant(); $rel = $Matches[2].Trim()
        if ($rel -match '(^|[\\/])\.\.([\\/]|$)' -or [IO.Path]::IsPathRooted($rel)) { $r.Errors += "path escapes the folder: $rel"; continue }
        if ($listed.ContainsKey($rel)) { $r.Errors += "duplicate entry: $rel"; continue }
        $listed[$rel] = $hash
    }
    foreach ($rel in $listed.Keys) {
        $full = [IO.Path]::GetFullPath((Join-Path $rootFull $rel))
        if (-not $full.StartsWith($rootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { $r.Errors += "path escapes the folder: $rel"; continue }
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { $r.Missing += $rel; continue }
        $r.Checked++
        if ((Get-Sha256Hex $full) -ne $listed[$rel]) { $r.Bad += $rel }
    }
    foreach ($rel in (Get-RelativeFiles $Root (@($FileName) + $ExcludeNames))) { if (-not $listed.ContainsKey($rel)) { $r.Extra += $rel } }
    $r.Ok = ($r.Bad.Count + $r.Missing.Count + $r.Extra.Count + $r.Errors.Count) -eq 0 -and $r.Checked -gt 0
    $r
}

# ---------------------------------------------------------------------------------------------------------------- paths
function Test-PathUnder([string]$Path, [string]$Root) {
    $p = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/'); $r = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $p.Equals($r, [StringComparison]::OrdinalIgnoreCase) -or $p.StartsWith($r + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Test-LocalDrivePath([string]$Path) {
    # An absolute path on a local drive (or a rooted POSIX path) with no UNC, device (\\?\, \\.\) or 8.3 short-name form. Those forms name the
    # same folder under a different string, which defeats a containment check made on strings; they are refused, not normalised.
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    if ($Path.StartsWith('\\') -or $Path.StartsWith('//')) { return $false }
    if ($IsWindows) { if ($Path -notmatch '^[A-Za-z]:[\\/]') { return $false } } elseif (-not $Path.StartsWith('/')) { return $false }
    if ($Path -match '~\d') { return $false }
    if ($IsWindows -and $Path.Substring(2).Contains(':')) { return $false }   # a second colon is an NTFS alternate-data-stream name (prot::$INDEX_ALLOCATION is the folder 'prot' itself)
    if ($Path -match '[ .]([\\/]|$)') { return $false }   # a segment ending in a space or a dot names a different folder on Windows than its text says
    $true
}

function Get-AncestorBuildFiles([string]$Dir) {
    # MSBuild looks for Directory.Build.props / .targets / .rsp, Directory.Packages.props and NuGet.config in every folder ABOVE a project, up to the
    # drive root, and Git cannot see them. One that sits above the repository changes what a build compiles (it can add a source from anywhere), so
    # evidence from such a place is not evidence of the commit. Conservative: any such file counts, even one a nearer file would shadow.
    $names = 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Build.rsp', 'Directory.Packages.props', 'Directory.Solution.props', 'Directory.Solution.targets', 'NuGet.config', 'nuget.config'
    $found = @()
    $d = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Dir).TrimEnd('\', '/'))
    while ($d) {
        foreach ($n in $names) { $p = Join-Path $d $n; if (Test-Path -LiteralPath $p -PathType Leaf) { $found += $p } }
        $parent = [IO.Path]::GetDirectoryName($d); if ($parent -eq $d) { break }; $d = $parent
    }
    @($found | Select-Object -Unique)
}

function Get-BuildEnvironmentRisks {
    # Things outside the repository that change what a build compiles without any file in it: MSBuild import hooks set as environment variables
    # (CustomBefore*/CustomAfter*, DirectoryBuild*Path, ImportDirectoryBuild*, MSBuildExtensionsPath*, MSBuildSDKsPath ...) and files in the
    # user's MSBuild ImportBefore / ImportAfter folders, which every build picks up. The other MSBUILD* and NUGET_* variables are only listed
    # (names, never values: they can carry tokens). The machine-wide SDK ImportAfter folders hold the SDK's own files and are not examined.
    $hook = '^(CustomBefore|CustomAfter|DirectoryBuild|ImportDirectoryBuild|ImportByWildcard|MSBuildExtensionsPath|MSBuildSDKsPath|MSBuildUserExtensionsPath|MSBuildProjectExtensionsPath|BaseIntermediateOutputPath|BaseOutputPath)'
    $vars = [Environment]::GetEnvironmentVariables()
    $names = @($vars.Keys | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrEmpty([string]$vars[$_]) })   # a variable with an empty value sets nothing
    $hooks = @($names | Where-Object { $_ -match $hook } | Sort-Object)
    $others = @($names | Where-Object { $_ -match '^(MSBUILD|NUGET_)' -and $_ -notmatch $hook } | Sort-Object)
    $files = @()
    $localAppData = [Environment]::GetEnvironmentVariable('LOCALAPPDATA')
    if ($localAppData) {
        $root = Join-Path $localAppData 'Microsoft/MSBuild'
        if (Test-Path -LiteralPath $root -PathType Container) {
            $files = @(Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '[\\/]Import(Before|After)[\\/]' -and $_.Extension -in '.targets', '.props' } | ForEach-Object FullName)
        }
    }
    [pscustomobject]@{ HookVariables = $hooks; OtherVariables = $others; UserImportFiles = $files; Risky = ($hooks.Count + $files.Count) -gt 0 }
}

function Get-ProjectImportInjections([string]$ArtifactsPath) {
    # MSBuild imports <project file>.*.props and <project file>.*.targets from the project-extensions folder, which under --artifacts-path is
    # <ArtifactsPath>/obj/<project>/. Restore writes exactly one such pair (<project>.csproj.nuget.g.props / .nuget.g.targets); any other
    # file there was put there by someone and is compiled into the next build. A reused artifacts path makes this realistic.
    $obj = Join-Path $ArtifactsPath 'obj'
    if (-not (Test-Path -LiteralPath $obj -PathType Container)) { return @() }
    @(foreach ($dir in Get-ChildItem -LiteralPath $obj -Directory -ErrorAction SilentlyContinue) {
            # Only the exact names restore writes are exempt: <folder>.csproj.nuget.g.props / .targets (or fsproj / vbproj). A name that merely ends
            # in .nuget.g.targets (Calc.Tests.csproj.evil.nuget.g.targets) is still imported by MSBuild and still counts.
            $restoreNames = foreach ($pe in 'csproj', 'fsproj', 'vbproj') { foreach ($x in 'props', 'targets') { "$($dir.Name).$pe.nuget.g.$x" } }
            Get-ChildItem -LiteralPath $dir.FullName -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in '.props', '.targets' -and $_.Name -notin $restoreNames } | ForEach-Object FullName
        })
}

function Get-BinariesHash([string]$Folder) {
    # One hash over everything a test run can read from its output folder: EVERY file, by relative path and content. A stamp that covered only
    # the test assembly could be reused after a dependency (the production assembly) or a content file the tests read had been replaced.
    # Left out: symbol files and what a test run itself writes (TRX, logs, coverage, a TestResults folder), so that running the tests does not
    # invalidate the stamp it was run under.
    if (-not (Test-Path -LiteralPath $Folder -PathType Container)) { return $null }
    $root = [IO.Path]::GetFullPath($Folder).TrimEnd('\', '/')
    $lines = foreach ($rel in (Get-RelativeFiles $root)) { if ($rel -notmatch '\.(pdb|trx|log|coverage)$|(^|/)TestResults/') { '{0}  {1}' -f (Get-Sha256Hex (Join-Path $root $rel)), $rel } }
    Get-Sha256OfText ((@($lines) -join "`n"))
}

function Test-HasReparsePoint([string]$Path, [string]$StopAt) {
    # True when the path, or any existing ancestor below StopAt, is a symbolic link / junction.
    $p = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/'); $stop = [IO.Path]::GetFullPath($StopAt).TrimEnd('\', '/')
    while ($p.Length -gt $stop.Length -and (Test-PathUnder $p $stop)) {
        if (Test-Path -LiteralPath $p) { if ((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { return $true } }
        $parent = [IO.Path]::GetDirectoryName($p); if ($null -eq $parent -or $parent -eq $p) { break }; $p = $parent
    }
    $false
}

# ---------------------------------------------------------------------------------------------------------------- git
function Invoke-Git([string]$Repo, [string[]]$GitArgs) {
    $out = & git --no-optional-locks -C $Repo @GitArgs 2>&1
    [pscustomobject]@{ Exit = $LASTEXITCODE; Text = (@($out | ForEach-Object { [string]$_ }) -join "`n").TrimEnd() }
}

function Invoke-GitGrep([string]$Repo, [string[]]$GrepArgs) {
    # git grep exits 0 on matches, 1 on none and 128 (or more) on failure. A failed search must never look like "found nothing".
    $r = Invoke-Git $Repo (@('grep') + $GrepArgs)
    if ($r.Exit -gt 1) { throw "git grep failed (exit $($r.Exit)): $($r.Text)" }
    $r
}

function Get-GitIdentity([string]$Repo) {
    # Facts about the tree under test. Dirty is the tracked state only: untracked files are counted, not hashed.
    $id = [ordered]@{ Repo = $Repo; Ok = $false }
    $top = Invoke-Git $Repo @('rev-parse', '--show-toplevel')
    if ($top.Exit -ne 0) { $id.Error = "not a Git work tree: $($top.Text)"; return [pscustomobject]$id }
    $id.TopLevel = $top.Text
    $id.Head = (Invoke-Git $Repo @('rev-parse', 'HEAD')).Text
    $branch = Invoke-Git $Repo @('symbolic-ref', '--short', '-q', 'HEAD'); $id.Branch = if ($branch.Exit -eq 0) { $branch.Text } else { 'DETACHED' }
    $id.Tree = (Invoke-Git $Repo @('rev-parse', 'HEAD^{tree}')).Text
    foreach ($d in 'src', 'tests') { $t = Invoke-Git $Repo @('rev-parse', "HEAD:$d"); $id["Tree_$d"] = if ($t.Exit -eq 0) { $t.Text } else { $null } }
    $dirty = (Invoke-Git $Repo @('status', '--porcelain', '--untracked-files=no')).Text
    $id.DirtyTrackedFiles = if ($dirty) { @($dirty -split "`n").Count } else { 0 }
    $id.DiffHash = if ($dirty) { Get-Sha256OfText ((Invoke-Git $Repo @('diff', 'HEAD', '--binary')).Text) } else { $null }
    $untracked = @((Invoke-Git $Repo @('ls-files', '--others', '--exclude-standard')).Text -split "`n" | Where-Object { $_ })
    $id.UntrackedFiles = $untracked.Count
    # An untracked file that is not ignored can still be compiled: a tree is not the commit if one sits among the sources or the build inputs.
    $buildInputs = @($untracked | Where-Object { $_ -match '^(src|tests|poc)/' -or $_ -match '^(Directory\..*|global\.json|nuget\.config|[^/]*\.(props|targets|slnx?|csproj))$' })
    # ...and so can one that Git IGNORES: the SDK compiles every *.cs it finds, whatever .gitignore says (this repository ignores Logs/, Out/, Release/,
    # Debug/, packages/ and similar folders anywhere). Only the extensions the SDK picks up by default, and never build output.
    $exts = 'cs', 'razor', 'cshtml', 'resx', 'fs', 'vb', 'csproj', 'props', 'targets'
    # MSBuild also imports <project>.user and .rsp response files automatically, and a .user file can pull in a source from anywhere on disk, so
    # those (and ignored props/targets/project files at any depth) count as build inputs wherever they are.
    $specs = @(foreach ($root in 'src', 'tests', 'poc') { foreach ($e in $exts) { "$root/*.$e" } }) + @('*.user', '*.rsp', '*.props', '*.targets', '*.csproj', '*.sln', '*.slnx')
    $ignored = @((Invoke-Git $Repo (@('ls-files', '--others', '--ignored', '--exclude-standard', '--') + $specs)).Text -split "`n" | Where-Object { $_ -and $_ -notmatch '(^|/)(bin|obj|node_modules|artifacts)/' })
    $id.UntrackedBuildFiles = @($buildInputs + $ignored | Select-Object -Unique)
    $id.IgnoredBuildFiles = $ignored.Count
    $id.AncestorBuildFiles = @(Get-AncestorBuildFiles $id.TopLevel)
    $gitDir = (Invoke-Git $Repo @('rev-parse', '--absolute-git-dir')).Text
    $id.GitDir = $gitDir; $id.IndexLockPresent = Test-Path -LiteralPath (Join-Path $gitDir 'index.lock')
    $remote = Invoke-Git $Repo @('remote', 'get-url', 'origin'); $id.OriginUrl = if ($remote.Exit -eq 0) { $remote.Text -replace '://[^@/]*@', '://' } else { $null }
    $id.Ok = $true
    [pscustomobject]$id
}

# ---------------------------------------------------------------------------------------------------------------- secrets
function Protect-Text([string]$Text) {
    # Redacts password-like pairs so a log or message can be stored and archived: Password=x, Password="x", pwd = x, MSSQL_SA_PASSWORD=x,
    # "Password": "x", Password: x, and a sqlcmd -P x. Deliberately over-eager: a redacted harmless word is cheaper than a stored secret.
    # An unquoted value runs to the next ';' or the end of the line, so a passphrase with spaces or commas does not keep a readable tail.
    $t = [regex]::Replace($Text, '(?i)(\b[\w.-]*(?:password|passwd|pwd)[\w.-]*"?\s*[=:]\s*)("(?:[^"\\]|\\.)*"|''[^'']*''|[^;\r\n]+)', '$1***')
    [regex]::Replace($t, '(?i)(\s-P\s+)(\S+)', '$1***')
}

function Get-SafeConnectionInfo([string]$ConnectionString) {
    # Reduces a SQL Server connection string to the facts the toolkit may show. The password is never returned.
    # SqlClient lets a keyword repeat (or be spelled by an alias) and the LAST value wins, so the last one is what is reported. When the repeated
    # keyword carries different values the string is Ambiguous, which the preflight treats as a failure.
    $pairs = @(foreach ($part in ($ConnectionString -split ';')) { if ($part -match '^\s*([^=]+?)\s*=\s*(.*?)\s*$') { [pscustomobject]@{ Key = $Matches[1].ToLowerInvariant(); Value = $Matches[2].Trim('"', "'") } } })
    $ambiguous = $false
    $pick = {
        param([string[]]$Aliases)
        $hits = @($pairs | Where-Object { $_.Key -in $Aliases })
        if ($hits.Count -eq 0) { return $null }
        if (@($hits.Value | Select-Object -Unique).Count -gt 1) { $script:connAmbiguous = $true }
        $hits[-1].Value
    }
    $script:connAmbiguous = $false
    $rawServer = [string](& $pick @('server', 'data source', 'address', 'addr', 'network address'))
    $namedPipe = $rawServer -match '^(?i)(np:|lpc:|\\\\\.\\pipe\\)' -or $rawServer -match '(?i)^\(?localdb\)?'
    $server = $rawServer -replace '^(?i)(tcp|np|lpc|admin|via):', ''
    $hostName = $server; $port = $null
    if ($server -match '^(?<h>[^,]+?)\s*,\s*(?<p>\d+)$') { $hostName = $Matches.h; $port = [int]$Matches.p }
    $db = [string](& $pick @('database', 'initial catalog')); $user = [string](& $pick @('user id', 'uid', 'user'))
    $has = @($pairs | Where-Object { $_.Key -in 'password', 'pwd' }).Count -gt 0
    $integrated = (@($pairs | Where-Object { $_.Key -eq 'integrated security' -and $_.Value -in 'true', 'sspi', 'yes' }).Count -gt 0) -or (@($pairs | Where-Object { $_.Key -eq 'trusted_connection' -and $_.Value -in 'true', 'yes' }).Count -gt 0)
    [pscustomobject]@{ Server = $server; Host = $hostName.Trim(); Port = $port; Catalog = $db; User = $user; IntegratedSecurity = $integrated; HasPassword = $has; NamedPipeOrLocalDb = $namedPipe; Ambiguous = $script:connAmbiguous }
}

function Test-SqlBaseCatalog([string]$Catalog) {
    # The catalog a test connection string names is the BASE: the tests replace it with the disposable catalogs they create and drop, and the
    # repository's own guard (SqlTestTargetGuard) judges only the catalog being operated on, allowing a base that defaults to the bare
    # development name 'Apperture' ("CI does"). So the base is acceptable when it is that name or Apperture_<suffix>; anything else, or none,
    # is a configuration this tool cannot place. The development SERVER is excluded separately (port 14333), not by this name.
    [bool]($Catalog -and ($Catalog -ieq 'Apperture' -or $Catalog -match '^Apperture_[A-Za-z0-9_]+$'))
}

function Resolve-SqlTarget([string]$Repo, [switch]$ProcessEnvironmentOnly, [string]$EnvFile) {
    # Same precedence as the tests (process environment first, then the repository .env), reduced to a safe description.
    # Returns the connection string only inside the object's hidden field for in-process use; Describe() output carries no secret.
    $var = 'APPERTURE_TEST_SQL_CONNECTION_STRING'
    $value = [Environment]::GetEnvironmentVariable($var); $source = if ($value) { "process environment $var" } else { $null }
    if (-not $value -and -not $ProcessEnvironmentOnly) {
        $file = if ($EnvFile) { $EnvFile } else { Join-Path $Repo '.env' }
        if (Test-Path -LiteralPath $file -PathType Leaf) {
            $line = Get-Content -LiteralPath $file | Where-Object { $_ -match "^\s*$var\s*=" } | Select-Object -First 1
            if ($line) { $value = (($line -replace '^[^=]+=', '').Trim().Trim('"', "'")); $source = "$(Split-Path $file -Leaf) $var" }
        }
    }
    if (-not $value) { return [pscustomobject]@{ Configured = $false; Source = $null; Info = $null; Raw = $null } }
    [pscustomobject]@{ Configured = $true; Source = $source; Info = (Get-SafeConnectionInfo $value); Raw = $value }
}

# ---------------------------------------------------------------------------------------------------------------- TRX
function Read-Trx([string]$Path) {
    # Parses a TRX file with DTD processing off and reports whether it can be trusted: well-formed, a TestRun, results present,
    # and the summary counters agree with the results themselves (a truncated or hand-edited file does not).
    $t = [ordered]@{ Path = $Path; Valid = $false; Errors = @(); Sha256 = $null; Start = $null; Finish = $null; RunOutcome = $null; Counters = $null; Results = @() }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { $t.Errors += 'file not found'; return [pscustomobject]$t }
    $t.Sha256 = Get-Sha256Hex $Path
    $defs = @{}; $results = New-Object System.Collections.Generic.List[object]; $counters = $null; $sawRun = $false
    $settings = [Xml.XmlReaderSettings]@{ DtdProcessing = [Xml.DtdProcessing]::Prohibit; XmlResolver = $null }
    $reader = $null
    try {
        $reader = [Xml.XmlReader]::Create($Path, $settings)
        $currentTest = $null
        while ($reader.Read()) {
            if ($reader.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
            switch ($reader.LocalName) {
                'TestRun' { $sawRun = $true }
                'Times' { if (-not $t.Start) { $t.Start = $reader.GetAttribute('start'); $t.Finish = $reader.GetAttribute('finish') } }
                'UnitTest' { $currentTest = $reader.GetAttribute('id') }
                'TestMethod' { if ($currentTest) { $defs[$currentTest] = [pscustomobject]@{ Class = $reader.GetAttribute('className'); Method = $reader.GetAttribute('name') } } }
                'ResultSummary' { $t.RunOutcome = $reader.GetAttribute('outcome') }
                'Counters' { $counters = @{}; while ($reader.MoveToNextAttribute()) { $counters[$reader.Name] = [int]$reader.Value }; $reader.MoveToElement() | Out-Null }
                'UnitTestResult' {
                    $r = [pscustomobject]@{
                        TestId = $reader.GetAttribute('testId'); ExecutionId = $reader.GetAttribute('executionId'); Name = $reader.GetAttribute('testName')
                        Outcome = $reader.GetAttribute('outcome'); Duration = $reader.GetAttribute('duration'); Message = ''; Class = $null
                    }
                    if ($reader.IsEmptyElement -eq $false) {
                        $sub = $reader.ReadSubtree()
                        while ($sub.Read()) { if ($sub.NodeType -eq [Xml.XmlNodeType]::Element -and $sub.LocalName -eq 'Message') { $m = $sub.ReadElementContentAsString(); $r.Message = ($m -replace '\s+', ' ').Trim(); if ($r.Message.Length -gt 400) { $r.Message = $r.Message.Substring(0, 400) }; break } }
                        $sub.Dispose()
                    }
                    $results.Add($r)
                }
            }
        }
    }
    catch { $t.Errors += "not a readable TRX: $($_.Exception.Message)" }
    finally { if ($reader) { $reader.Dispose() } }
    if ($t.Errors.Count -eq 0) {
        if (-not $sawRun) { $t.Errors += 'no TestRun element' }
        if ($results.Count -eq 0) { $t.Errors += 'no test results in the file' }
        foreach ($r in $results) { if ($defs.ContainsKey($r.TestId)) { $r.Class = $defs[$r.TestId].Class } }
        $t.Results = $results.ToArray(); $t.Counters = $counters
        # A run that was aborted or never finished is not a result, whatever its partial counters say.
        if ($t.RunOutcome -notin 'Completed', 'Failed') { $t.Errors += "the run outcome is '$($t.RunOutcome)', not Completed or Failed (aborted or unfinished)" }
        if ($null -eq $counters) { $t.Errors += 'no summary counters (the run did not finish writing the file)' }
        elseif (@('total', 'executed', 'passed', 'failed' | Where-Object { -not $counters.ContainsKey($_) }).Count -gt 0) { $t.Errors += "the summary counters are incomplete (missing: $(@('total', 'executed', 'passed', 'failed' | Where-Object { -not $counters.ContainsKey($_) }) -join ', '))" }
        else {
            $by = $results | Group-Object Outcome -AsHashTable -AsString; if ($null -eq $by) { $by = @{} }
            $n = { param($k) if ($by.ContainsKey($k)) { @($by[$k]).Count } else { 0 } }
            if ($counters.ContainsKey('total') -and $counters['total'] -ne $results.Count) { $t.Errors += "counters say $($counters['total']) results, the file holds $($results.Count)" }
            if ($counters.ContainsKey('passed') -and $counters['passed'] -ne (& $n 'Passed')) { $t.Errors += "counters say $($counters['passed']) passed, the file holds $(& $n 'Passed')" }
            if ($counters.ContainsKey('failed') -and $counters['failed'] -ne (& $n 'Failed')) { $t.Errors += "counters say $($counters['failed']) failed, the file holds $(& $n 'Failed')" }
            # vstest leaves its 'notExecuted' counter at 0 for a skipped test (outcome NotExecuted); what it keeps right is executed = total - skipped
            if ($counters.ContainsKey('executed') -and $counters['executed'] -ne ($results.Count - (& $n 'NotExecuted'))) { $t.Errors += "counters say $($counters['executed']) executed, the file holds $($results.Count - (& $n 'NotExecuted')) executed results" }
        }
        $dupIds = @($results | Group-Object ExecutionId | Where-Object Count -gt 1)
        if ($dupIds.Count -gt 0) { $t.Errors += "$($dupIds.Count) duplicated execution ids" }
    }
    $t.Valid = $t.Errors.Count -eq 0
    [pscustomobject]$t
}

function Test-TcpReachable([string]$HostName, [int]$Port, [int]$TimeoutMs = 2000) {
    # Reachability only: opens a TCP connection and closes it. No login, no credential.
    $client = [Net.Sockets.TcpClient]::new()
    try { $task = $client.ConnectAsync($HostName, $Port); $task.Wait($TimeoutMs) -and $client.Connected } catch { $false } finally { $client.Dispose() }
}

function Get-AssemblyCodeHash([string]$Path) {
    # Hash of what an assembly does, not of its bytes: every method body's IL, the user-string heap (string literals) and the constant
    # blobs. The file hash is not usable for "did behaviour change": a compiled assembly embeds its PDB checksum, so a comment-only edit
    # changes the file. Returns $null when the file cannot be read as a managed assembly.
    try {
        $stream = [IO.File]::OpenRead($Path)
        try {
            $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
            $md = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
            $sha = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
            $put = { param([string]$Text) $sha.AppendData([Text.Encoding]::UTF8.GetBytes($Text)) }
            # Shape: every type's name and attributes (visibility), every field's name, attributes and signature.
            foreach ($th in $md.TypeDefinitions) {
                $td = $md.GetTypeDefinition($th)
                $base = if ($td.BaseType.IsNil) { 0 } else { [Reflection.Metadata.Ecma335.MetadataTokens]::GetToken($td.BaseType) }
                $ifaces = @(foreach ($ih in $td.GetInterfaceImplementations()) { [Reflection.Metadata.Ecma335.MetadataTokens]::GetToken($md.GetInterfaceImplementation($ih).Interface) }) -join ','
                & $put "T|$($md.GetString($td.Namespace)).$($md.GetString($td.Name))|$([int]$td.Attributes)|base=$base|implements=$ifaces|"
            }
            foreach ($fh in $md.FieldDefinitions) { $fd = $md.GetFieldDefinition($fh); & $put "F|$($md.GetString($fd.Name))|$([int]$fd.Attributes)|"; $sha.AppendData($md.GetBlobBytes($fd.Signature)) }
            # Custom attributes with their arguments: a [Description("Admin")] that becomes ("Anyone") is a behavioural change with no IL change.
            foreach ($ah in $md.CustomAttributes) {
                $ca = $md.GetCustomAttribute($ah)
                & $put "A|$([Reflection.Metadata.Ecma335.MetadataTokens]::GetToken($ca.Parent))|$([Reflection.Metadata.Ecma335.MetadataTokens]::GetToken($ca.Constructor))|"
                if (-not $ca.Value.IsNil) { $sha.AppendData($md.GetBlobBytes($ca.Value)) }
            }
            foreach ($h in $md.MethodDefinitions) {
                $m = $md.GetMethodDefinition($h)
                & $put "M|$($md.GetString($m.Name))|$([int]$m.Attributes)|$([int]$m.ImplAttributes)|"; $sha.AppendData($md.GetBlobBytes($m.Signature))
                if ($m.RelativeVirtualAddress -ne 0) {
                    $body = [Reflection.Metadata.PEReaderExtensions]::GetMethodBody($pe, $m.RelativeVirtualAddress)
                    $sha.AppendData($body.GetILBytes())
                    if (-not $body.LocalSignature.IsNil) { $sha.AppendData($md.GetBlobBytes($md.GetStandaloneSignature($body.LocalSignature).Signature)) }
                }
            }
            # The user-string heap starts with a one-byte empty-string sentinel; an assembly with no string literal has nothing after it to walk.
            if ([Reflection.Metadata.Ecma335.MetadataReaderExtensions]::GetHeapSize($md, [Reflection.Metadata.Ecma335.HeapIndex]::UserString) -gt 1) {
                # A heap that holds only padding (no literal) makes the first step throw; that means "no strings", not "unreadable assembly".
                try {
                    $us = [Reflection.Metadata.Ecma335.MetadataReaderExtensions]::GetNextHandle($md, [Reflection.Metadata.Ecma335.MetadataTokens]::UserStringHandle(0))
                    while (-not $us.IsNil) { $s = $md.GetUserString($us); $sha.AppendData([Text.Encoding]::Unicode.GetBytes($s + "`0")); $us = [Reflection.Metadata.Ecma335.MetadataReaderExtensions]::GetNextHandle($md, $us) }
                }
                catch [System.BadImageFormatException], [System.ArgumentException], [System.InvalidOperationException] { }
            }
            $constantRows = [Reflection.Metadata.Ecma335.MetadataReaderExtensions]::GetTableRowCount($md, [Reflection.Metadata.Ecma335.TableIndex]::Constant)
            for ($row = 1; $row -le $constantRows; $row++) { $sha.AppendData($md.GetBlobBytes($md.GetConstant([Reflection.Metadata.Ecma335.MetadataTokens]::ConstantHandle($row)).Value)) }
            [Convert]::ToHexString($sha.GetHashAndReset()).ToLowerInvariant()
        }
        finally { $stream.Dispose() }
    }
    catch { $script:CodeHashError = "$($_.Exception.GetType().Name): $($_.Exception.Message)"; $null }
}

function Get-RcsiMode([string]$TestName) { if ($TestName -match 'rcsi:\s*(True|False)') { $Matches[1] } else { 'none' } }

function ConvertTo-Seconds([string]$Duration) { if ($Duration) { try { [TimeSpan]::Parse($Duration).TotalSeconds } catch { 0.0 } } else { 0.0 } }
