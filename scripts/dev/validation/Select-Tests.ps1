#Requires -Version 7.2
<#
.SYNOPSIS
    Turns a change set into a test selection and a focused filter, from tests/test-manifest.json. Plans only: it never runs or enforces anything.
.DESCRIPTION
    Selection is for development iteration and for planning evidence. It never replaces the required hosted CI, which always runs everything.

    A change set is a Git range (-Base/-Head), the working tree (-Working), or an explicit list (-Files). For each changed file:
      - a changed test file selects the test classes it declares (DIRECT);
      - a changed production file selects the classes of every manifest rule whose path patterns match it (MAPPED / CROSS-CUTTING);
      - a file that matches a manifest 'everything' pattern says a full run is required;
      - a production file that matches no rule and is not ignored is reported UNMAPPED, never dropped.
    Manifest classes are resolved against the test sources at -TreeRev (or the working tree). A class that does not exist is MISSING; one that
    exists in several test projects is AMBIGUOUS; a wildcard that matches nothing is reported. All three are findings.
    Advisory CANDIDATES are test classes that name a type declared in a changed production file; they are listed, and added to the
    filter only with -IncludeCandidates, because a reference is evidence of exposure, not of coverage.
    -CurrentFilter takes the filter that was (or will be) used and reports the classes the selection names that it lacks (MISSED) and
    the classes it holds that the selection does not name (EXTRA). -CurrentFilterProject limits that comparison to one test project
    (a focused filter normally applies to the IntegrationTests project only).

    Exit codes: 0 = selection produced; 1 = findings under -Strict (UNMAPPED, MISSING, AMBIGUOUS, MISSED, full run required); 2 = input error.
#>
[CmdletBinding()]
param(
    [string]$Repo,
    [string]$Base,
    [string]$Head = 'HEAD',
    [switch]$Working,
    [string[]]$Files,
    [string]$TreeRev,
    [string]$Manifest,
    [string]$CurrentFilter,
    [string]$CurrentFilterProject,
    [switch]$IncludeCandidates,
    [string]$Json,
    [string]$FilterDir,
    [switch]$Strict,
    [switch]$PassThru,
    [switch]$Quiet
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ValidationCommon.ps1')
$Files = Expand-List $Files

function Stop-Input([string]$Message) { Write-Host "INPUT ERROR: $Message" -ForegroundColor Red; exit 2 }
function ConvertTo-GlobRegex([string]$Glob) {
    $sb = [Text.StringBuilder]::new('^'); $i = 0
    while ($i -lt $Glob.Length) {
        $c = $Glob[$i]
        if ($c -eq '*' -and $i + 1 -lt $Glob.Length -and $Glob[$i + 1] -eq '*') {
            if ($i + 2 -lt $Glob.Length -and $Glob[$i + 2] -eq '/') { [void]$sb.Append('(?:.*/)?'); $i += 3 } else { [void]$sb.Append('.*'); $i += 2 }
        }
        elseif ($c -eq '*') { [void]$sb.Append('[^/]*'); $i++ } elseif ($c -eq '?') { [void]$sb.Append('[^/]'); $i++ } else { [void]$sb.Append([regex]::Escape([string]$c)); $i++ }
    }
    [void]$sb.Append('$'); [regex]::new($sb.ToString(), 'IgnoreCase')
}
function Test-AnyGlob([string]$Path, $Globs) { foreach ($g in @($Globs)) { if ((ConvertTo-GlobRegex $g).IsMatch($Path)) { return $true } }; $false }

if (-not $Repo) { $Repo = (Invoke-Git $PSScriptRoot @('rev-parse', '--show-toplevel')).Text }
$id = Get-GitIdentity $Repo; if (-not $id.Ok) { Stop-Input $id.Error }
$Repo = $id.TopLevel
if (-not $Manifest) { $Manifest = Join-Path $Repo 'tests/test-manifest.json' }
if (-not (Test-Path -LiteralPath $Manifest -PathType Leaf)) { Stop-Input "manifest not found: $Manifest" }
try { $man = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json } catch { Stop-Input "manifest is not valid JSON: $($_.Exception.Message)" }
foreach ($k in 'version', 'projects', 'ignore', 'everything', 'rules') { if (-not $man.PSObject.Properties[$k]) { Stop-Input "manifest has no '$k' section" } }
if (-not $TreeRev -and -not $Working -and -not $Files) { $TreeRev = $Head }
# manifest project key for a tests/<folder>/... path (the folder name when the manifest does not list it)
$projectByFolder = @{}; foreach ($p in $man.projects.PSObject.Properties) { $projectByFolder[$p.Value] = $p.Name }
function Get-ProjectKey([string]$Path) { $folder = 'tests/' + ($Path -split '/')[1]; if ($projectByFolder.ContainsKey($folder)) { $projectByFolder[$folder] } else { ($Path -split '/')[1] } }
if ($CurrentFilterProject -and -not $projectByFolder.ContainsValue($CurrentFilterProject)) { Stop-Input "-CurrentFilterProject '$CurrentFilterProject' is not a project in the manifest ($($projectByFolder.Values -join ', '))" }

# ---- the change set
$changed = New-Object System.Collections.Generic.List[object]
if ($Files) { foreach ($f in $Files) { $changed.Add([pscustomobject]@{ Status = 'M'; Path = ($f -replace '\\', '/') }) } }
else {
    if (-not $Base) { $mb = Invoke-Git $Repo @('merge-base', $Head, 'origin/master'); if ($mb.Exit -ne 0) { Stop-Input 'no -Base given and no merge-base with origin/master' }; $Base = $mb.Text }
    $d = Invoke-Git $Repo @('diff', '--name-status', '-M', '--no-renames', $Base, $(if ($Working) { 'HEAD' } else { $Head }))
    if ($d.Exit -ne 0) { Stop-Input "git diff failed: $($d.Text)" }
    foreach ($line in ($d.Text -split "`n" | Where-Object { $_ })) { $s, $p = $line -split "`t", 2; $changed.Add([pscustomobject]@{ Status = $s.Substring(0, 1); Path = $p }) }
    if ($Working) {
        foreach ($line in ((Invoke-Git $Repo @('diff', '--name-status', '--no-renames', 'HEAD')).Text -split "`n" | Where-Object { $_ })) { $s, $p = $line -split "`t", 2; if (-not ($changed | Where-Object Path -eq $p)) { $changed.Add([pscustomobject]@{ Status = $s.Substring(0, 1); Path = $p }) } }
        foreach ($p in ((Invoke-Git $Repo @('ls-files', '--others', '--exclude-standard')).Text -split "`n" | Where-Object { $_ })) { $changed.Add([pscustomobject]@{ Status = 'A'; Path = $p }) }
    }
}

# ---- test class inventory (declarations), at a revision or in the working tree
$classRegex = '^\s*(public|internal)\s+(sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+[A-Za-z0-9_]+'
$rev = $TreeRev
# Every search goes through Invoke-GitGrep: exit 1 is "no match", anything else is a failed search, which must stop the run (an earlier
# version put --untracked after the pattern, git failed, and the empty result was read as "no test files").
# A test attribute is [Fact], [Theory] or any custom attribute named like one ([BrowserFact], [WindowsOnlyFact], [IanaTimeZoneFact], ...).
$factRegex = '(\[|,)[[:space:]]*[A-Za-z0-9_.]*(Fact|Theory)[[:space:]]*[],(]'   # also the second attribute in a list: [Trait("k","v"), Fact]
$nsRegex = '^[[:space:]]*namespace[[:space:]]+[A-Za-z0-9_.]+'
try {
    $gr = if ($rev) { Invoke-GitGrep $Repo @('-n', '-I', '-E', $classRegex, $rev, '--', 'tests') } else { Invoke-GitGrep $Repo @('-n', '-I', '--untracked', '-E', $classRegex, '--', 'tests') }
    $factGrep = if ($rev) { Invoke-GitGrep $Repo @('-l', '-E', $factRegex, $rev, '--', 'tests') } else { Invoke-GitGrep $Repo @('-l', '--untracked', '-E', $factRegex, '--', 'tests') }
    $nsGrep = if ($rev) { Invoke-GitGrep $Repo @('-n', '-I', '-E', $nsRegex, $rev, '--', 'tests') } else { Invoke-GitGrep $Repo @('-n', '-I', '--untracked', '-E', $nsRegex, '--', 'tests') }
}
catch { Stop-Input $_.Exception.Message }
$fileNamespace = @{}
foreach ($line in ($nsGrep.Text -split "`n" | Where-Object { $_ })) { $nm = [regex]::Match($line, '^(?:[^:]+:)?(?<f>tests/[^:]+\.cs):\d+:\s*namespace\s+(?<n>[A-Za-z0-9_.]+)'); if ($nm.Success -and -not $fileNamespace.ContainsKey($nm.Groups['f'].Value)) { $fileNamespace[$nm.Groups['f'].Value] = $nm.Groups['n'].Value } }
$classes = @{}; $fileClasses = @{}
foreach ($line in ($gr.Text -split "`n" | Where-Object { $_ })) {
    $m = [regex]::Match($line, '^(?:[^:]+:)?(?<f>tests/[^:]+\.cs):\d+:\s*(?:public|internal)\s+(?:(?:sealed|static|abstract|partial)\s+)*class\s+(?<c>[A-Za-z0-9_]+)')
    if (-not $m.Success) { continue }
    $f = $m.Groups['f'].Value; $c = $m.Groups['c'].Value; $proj = Get-ProjectKey $f
    if (-not $classes.ContainsKey($c)) { $classes[$c] = New-Object System.Collections.Generic.List[object] }
    if (-not ($classes[$c] | Where-Object { $_.File -eq $f })) { $classes[$c].Add([pscustomobject]@{ Project = $proj; File = $f }) }
    if (-not $fileClasses.ContainsKey($f)) { $fileClasses[$f] = New-Object System.Collections.Generic.List[string] }
    $fileClasses[$f].Add($c)
}
if ($classes.Count -eq 0) { Stop-Input 'found no test classes: wrong repository or revision?' }
# Files that hold tests ([Fact]/[Theory]). A class declared only in other files is a helper, never a selectable test class.
$factSet = [Collections.Generic.HashSet[string]]::new([string[]]@($factGrep.Text -split "`n" | Where-Object { $_ } | ForEach-Object { $_ -replace '^[^:]+:(?=tests/)', '' }))
if ($factSet.Count -eq 0) { Stop-Input 'found no file holding a [Fact] or [Theory]: wrong repository or revision?' }
function Get-TestClasses([string]$File) {
    # Test classes declared in a file that holds tests: those named like a test class, else the one named after the file.
    if (-not $factSet.Contains($File) -or -not $fileClasses.ContainsKey($File)) { return @() }
    $stem = [IO.Path]::GetFileNameWithoutExtension($File)
    $named = @($fileClasses[$File] | Where-Object { $_ -match 'Tests?$' } | Select-Object -Unique)
    if ($named.Count -gt 0) { return $named }
    @($fileClasses[$File] | Where-Object { $_ -eq $stem } | Select-Object -Unique)
}
function Test-IsTestClass([string]$Name) { foreach ($d in $classes[$Name]) { if ((Get-TestClasses $d.File) -contains $Name) { return $true } }; $false }
$supportTypes = New-Object System.Collections.Generic.HashSet[string]; $supportFiles = New-Object System.Collections.Generic.List[string]

# ---- evaluate
$selected = @{}      # "Project|Class" -> list of reasons
function Add-Selection([string]$Project, [string]$Class, [string]$Why) { $k = "$Project|$Class"; if (-not $selected.ContainsKey($k)) { $selected[$k] = New-Object System.Collections.Generic.List[string] }; if (-not $selected[$k].Contains($Why)) { $selected[$k].Add($Why) } }
$unmapped = New-Object System.Collections.Generic.List[string]; $ignored = New-Object System.Collections.Generic.List[string]; $missing = New-Object System.Collections.Generic.List[string]
$ambiguous = New-Object System.Collections.Generic.List[string]; $noMatch = New-Object System.Collections.Generic.List[string]; $fullRun = New-Object System.Collections.Generic.List[string]
$ruleReport = @()

$docGlobs = if ($man.PSObject.Properties['documentation']) { @($man.documentation) } else { @() }
$docsChanged = New-Object System.Collections.Generic.List[string]; $deletedTests = New-Object System.Collections.Generic.List[string]
# A deleted production file still triggers its rules and can be UNMAPPED: removing code is the change most likely to need a test run.
$prodChanged = @($changed | Where-Object { $_.Path -notlike 'tests/*' })
foreach ($c in $changed) {
    if ($c.Status -eq 'D' -and $c.Path -like 'tests/*') { $deletedTests.Add($c.Path); continue }
    # a shared fixture lives under tests/ too, so the 'everything' patterns are checked before a test file is treated as a test
    if (Test-AnyGlob $c.Path $man.everything.when) { $fullRun.Add("$($c.Path) ($($man.everything.reason))"); continue }
    if ($c.Path -like 'tests/*.cs') {
        if ($factSet.Contains($c.Path)) {
            $tc = @(Get-TestClasses $c.Path)
            if ($tc.Count -eq 0) { $ambiguous.Add("$($c.Path) holds tests but declares no class named like a test class") }
            foreach ($cls in $tc) { Add-Selection (Get-ProjectKey $c.Path) $cls "DIRECT: $($c.Path) changed" }
        }
        elseif ($fileClasses.ContainsKey($c.Path)) { $supportFiles.Add($c.Path); foreach ($t in $fileClasses[$c.Path]) { [void]$supportTypes.Add($t) } }
        continue
    }
    if (Test-AnyGlob $c.Path $docGlobs) { $docsChanged.Add($c.Path); continue }
    if (Test-AnyGlob $c.Path $man.ignore) { $ignored.Add($c.Path); continue }
    $hit = $false
    # Only a 'mapped' rule claims a file. A cross-cutting rule adds tests but says nothing about who covers the file itself.
    foreach ($rule in $man.rules) { if ($rule.kind -eq 'mapped' -and (Test-AnyGlob $c.Path $rule.paths)) { $hit = $true } }
    if (-not $hit) { $unmapped.Add($c.Path) }
}
foreach ($rule in $man.rules) {
    $trig = @($prodChanged | Where-Object { Test-AnyGlob $_.Path $rule.paths } | ForEach-Object Path)
    $ruleReport += [pscustomobject]@{ Id = $rule.id; Kind = $rule.kind; Triggered = ($trig.Count -gt 0); Files = $trig.Count; Tests = @($rule.tests) }
    if ($trig.Count -eq 0) { continue }
    foreach ($t in $rule.tests) {
        $names = @(if ($t.Contains('*')) { $rx = ConvertTo-GlobRegex $t; $classes.Keys | Where-Object { $rx.IsMatch($_) -and (Test-IsTestClass $_) } } else { $t })
        if ($t.Contains('*') -and $names.Count -eq 0) { $noMatch.Add("rule $($rule.id): wildcard '$t' matches no test class"); continue }
        foreach ($n in $names) {
            if (-not $classes.ContainsKey($n)) { $missing.Add("rule $($rule.id): class '$n' not found in $(if ($rev) { $rev } else { 'the working tree' })"); continue }
            if (-not (Test-IsTestClass $n)) { $missing.Add("rule $($rule.id): class '$n' exists but holds no tests"); continue }
            $projs = @($classes[$n] | ForEach-Object Project | Select-Object -Unique)
            if ($projs.Count -gt 1) { $ambiguous.Add("class '$n' is declared in $($projs -join ', ') (rule $($rule.id)); all are selected") }
            foreach ($p in $projs) { Add-Selection $p $n "$($rule.kind.ToUpper()) $($rule.id): $($trig[0])$(if ($trig.Count -gt 1) { " (+$($trig.Count - 1))" })" }
        }
    }
}

# ---- advisory candidates: tests that name a type declared in a changed production source file
$candidates = @{}
$srcFiles = @($prodChanged | Where-Object { $_.Path -like 'src/*.cs' -and $_.Status -ne 'D' } | ForEach-Object Path)
if ($srcFiles.Count -gt 0 -or $supportTypes.Count -gt 0) {
    $types = New-Object System.Collections.Generic.HashSet[string]
    foreach ($t in $supportTypes) { if ($t.Length -ge 6) { [void]$types.Add($t) } }   # a changed test helper exposes the tests that use it
    foreach ($f in $srcFiles) {
        $text = if ($rev) { (Invoke-Git $Repo @('show', "${rev}:$f")).Text } elseif (Test-Path (Join-Path $Repo $f)) { Get-Content -LiteralPath (Join-Path $Repo $f) -Raw } else { '' }
        foreach ($m in [regex]::Matches($text, '(?m)^\s*(?:public|internal)?\s*(?:(?:sealed|static|abstract|partial|readonly)\s+)*(?:class|interface|record(?: struct)?|enum|struct)\s+([A-Z][A-Za-z0-9_]{5,})')) { [void]$types.Add($m.Groups[1].Value) }
    }
    if ($types.Count -gt 0) {
        $patterns = @(); foreach ($t in $types) { $patterns += @('-e', $t) }
        try { $g = if ($rev) { Invoke-GitGrep $Repo (@('-l', '-w', '-I', '-F') + $patterns + @($rev, '--', 'tests')) } else { Invoke-GitGrep $Repo (@('-l', '-w', '-I', '-F', '--untracked') + $patterns + @('--', 'tests')) } }
        catch { Stop-Input $_.Exception.Message }
        foreach ($line in ($g.Text -split "`n" | Where-Object { $_ })) {
            $f = $line -replace '^[^:]+:(?=tests/)', ''
            foreach ($cls in (Get-TestClasses $f)) {
                $k = "$(Get-ProjectKey $f)|$cls"
                if (-not $selected.ContainsKey($k)) { $candidates[$k] = $true }
            }
        }
    }
}
# ---- tests that read documentation: a changed document can break them, and docs are not 'ignorable' for that reason
$docReaders = @{}
if ($docsChanged.Count -gt 0) {
    try { $dg = if ($rev) { Invoke-GitGrep $Repo @('-l', '-I', '-E', '"docs"|docs[/\\]', $rev, '--', 'tests') } else { Invoke-GitGrep $Repo @('-l', '-I', '--untracked', '-E', '"docs"|docs[/\\]', '--', 'tests') } }
    catch { Stop-Input $_.Exception.Message }
    foreach ($line in ($dg.Text -split "`n" | Where-Object { $_ })) {
        $f = $line -replace '^[^:]+:(?=tests/)', ''
        foreach ($cls in (Get-TestClasses $f)) { $k = "$(Get-ProjectKey $f)|$cls"; if (-not $selected.ContainsKey($k)) { $docReaders[$k] = $true } }
    }
}
if ($IncludeCandidates) {
    foreach ($k in @($candidates.Keys)) { $p, $c = $k -split '\|', 2; Add-Selection $p $c 'CANDIDATE: names a type declared in a changed file' }
    foreach ($k in @($docReaders.Keys)) { $p, $c = $k -split '\|', 2; Add-Selection $p $c 'CANDIDATE: reads documentation, which changed' }
    $candidates = @{}; $docReaders = @{}
}

# ---- compare with a given filter
function Get-FilterClasses([string]$Filter) {
    # Only 'FullyQualifiedName~<class>' clauses joined by '|' are understood. Anything else (a negation, '&', parentheses, another property)
    # can include or exclude tests in ways this tool does not model, so it is refused instead of being counted as "includes the class".
    $out = @()
    foreach ($token in ($Filter.Trim() -split '\|')) {
        if ($token -notmatch '^\s*FullyQualifiedName\s*~\s*\.?(?<c>[A-Za-z0-9_.]+?)\.?\s*$') { Stop-Input "the filter holds a clause this tool does not understand: '$($token.Trim())' (only FullyQualifiedName~<class> clauses joined by | are compared)" }
        $out += $Matches.c   # a bare class name, or a namespace-qualified one; the qualification is kept and compared
    }
    $out
}
function Test-ClassCovered([string]$Class, [string]$Project, [string[]]$Tokens) {
    # A bare token covers the class of that name. A qualified token covers it only when it is that class's own namespace-qualified name (or a
    # dotted suffix of it): FullyQualifiedName~Other.Ns.FooTests does not cover Apperture.X.FooTests.
    foreach ($t in $Tokens) {
        if (-not $t.Contains('.')) { if ($t -eq $Class) { return $true }; continue }
        foreach ($d in @($classes[$Class] | Where-Object { $_.Project -eq $Project })) {
            $ns = if ($fileNamespace.ContainsKey($d.File)) { $fileNamespace[$d.File] } else { '' }
            $full = if ($ns) { "$ns.$Class" } else { $Class }
            if ($full -eq $t -or $full.EndsWith(".$t", [StringComparison]::Ordinal)) { return $true }
        }
    }
    $false
}
$scoped = @($selected.Keys | Where-Object { -not $CurrentFilterProject -or ($_ -split '\|', 2)[0] -eq $CurrentFilterProject })
$selectedClassNames = @($scoped | ForEach-Object { ($_ -split '\|', 2)[1] } | Sort-Object -Unique)
$missed = @(); $extra = @(); $currentClasses = @()
if ($CurrentFilter) {
    if ($CurrentFilterProject -and $selectedClassNames.Count -eq 0) { Stop-Input "the selection holds no class in project '$CurrentFilterProject': the comparison with the current filter would be empty, not clean" }
    $text = if (Test-Path -LiteralPath $CurrentFilter -PathType Leaf) { Get-Content -LiteralPath $CurrentFilter -Raw } else { $CurrentFilter }
    $currentClasses = @(Get-FilterClasses $text | Sort-Object -Unique)
    $missed = @($scoped | ForEach-Object { $p, $c = $_ -split '\|', 2; if (-not (Test-ClassCovered $c $p $currentClasses)) { $c } } | Sort-Object -Unique)
    $extra = @($currentClasses | Where-Object { $tok = $_; -not (@($scoped | Where-Object { $p, $c = $_ -split '\|', 2; Test-ClassCovered $c $p @($tok) }).Count) })
}

# ---- filters per project
$filters = [ordered]@{}
foreach ($grp in ($selected.Keys | Group-Object { ($_ -split '\|', 2)[0] } | Sort-Object Name)) {
    $filters[$grp.Name] = (@($grp.Group | ForEach-Object { ($_ -split '\|', 2)[1] } | Sort-Object -Unique) | ForEach-Object { "FullyQualifiedName~.$_." }) -join '|'
}
$findings = New-Object System.Collections.Generic.List[string]
if ($Strict) {
    if ($unmapped.Count) { $findings.Add("$($unmapped.Count) production files match no manifest rule (UNMAPPED)") }
    if ($missing.Count) { $findings.Add("$($missing.Count) manifest classes are missing from the tree") }
    if ($ambiguous.Count) { $findings.Add("$($ambiguous.Count) ambiguous class names") }
    if ($noMatch.Count) { $findings.Add("$($noMatch.Count) wildcards match nothing") }
    if ($fullRun.Count) { $findings.Add("a full run is required ($($fullRun.Count) changed files match the 'everything' patterns)") }
    if ($missed.Count) { $findings.Add("$($missed.Count) selected classes are missing from the current filter") }
    if ($docsChanged.Count -gt 0 -and $docReaders.Count -gt 0) { $findings.Add("$($docsChanged.Count) documentation files changed and $($docReaders.Count) test classes read documentation (not selected)") }
}
$res = [pscustomobject]@{
    Repo = $Repo; Base = $Base; Head = $Head; TreeRev = $rev; Manifest = $Manifest; Changed = @($changed | ForEach-Object { "$($_.Status) $($_.Path)" })
    Selected = @($selected.Keys | Sort-Object | ForEach-Object { $p, $c = $_ -split '\|', 2; [pscustomobject]@{ Project = $p; Class = $c; Why = @($selected[$_]) } })
    SupportFilesChanged = @($supportFiles); DocumentationChanged = @($docsChanged); DeletedTests = @($deletedTests)
    DocumentationReaders = @($docReaders.Keys | Sort-Object | ForEach-Object { $p, $c = $_ -split '\|', 2; [pscustomobject]@{ Project = $p; Class = $c } }); Filters = $filters; Rules = $ruleReport; FullRunRequired = @($fullRun); Unmapped = @($unmapped); Ignored = $ignored.Count; Missing = @($missing); Ambiguous = @($ambiguous); WildcardsMatchingNothing = @($noMatch)
    Candidates = @($candidates.Keys | Sort-Object | ForEach-Object { $p, $c = $_ -split '\|', 2; [pscustomobject]@{ Project = $p; Class = $c } })
    CurrentFilterClasses = $currentClasses; MissedByCurrentFilter = $missed; InCurrentFilterOnly = $extra; Findings = @($findings); ExitCode = $(if ($findings.Count) { 1 } else { 0 })
}
if ($Json) { Write-TextLf $Json ($res | ConvertTo-Json -Depth 6) }
if ($FilterDir) { New-Item -ItemType Directory -Force $FilterDir | Out-Null; foreach ($p in $filters.Keys) { Write-TextLf (Join-Path $FilterDir "$p.filter.txt") $filters[$p] } }
if (-not $Quiet) {
    "Test selection ($($changed.Count) changed files; classes read from $(if ($rev) { $rev } else { 'the working tree' }); manifest $(Split-Path $Manifest -Leaf))"
    "  selected: $($selected.Count) classes; candidates (advisory, not in the filter): $($candidates.Count); ignored files: $($ignored.Count)"
    $res.Selected | ForEach-Object { "    [{0}] {1}  <- {2}" -f $_.Project, $_.Class, $_.Why[0] }
    if ($supportFiles.Count) { "  changed test support files (no tests of their own; their users are listed as candidates): $($supportFiles.Count)"; $supportFiles | ForEach-Object { "    $_" } }
    'Cross-cutting and mapped rules:'; $ruleReport | ForEach-Object { "    {0,-8} {1,-26} {2}" -f $(if ($_.Triggered) { 'TRIGGERED' } else { '-' }), $_.Id, $_.Kind }
    if ($fullRun.Count) { 'FULL RUN REQUIRED by:'; $fullRun | ForEach-Object { "    $_" } }
    if ($unmapped.Count) { 'UNMAPPED production files (no rule claims them):'; $unmapped | ForEach-Object { "    $_" } }
    foreach ($pair in @(@('MISSING', $missing), @('AMBIGUOUS', $ambiguous), @('WILDCARD MATCHES NOTHING', $noMatch))) { if ($pair[1].Count) { "$($pair[0]):"; $pair[1] | ForEach-Object { "    $_" } } }
    if ($docsChanged.Count) { "$($docsChanged.Count) documentation files changed; test classes that read documentation (not selected): $(if ($docReaders.Count) { (($docReaders.Keys | Sort-Object | ForEach-Object { ($_ -split '\|', 2)[1] }) -join ', ') } else { 'none found' })" }
    if ($deletedTests.Count) { "deleted test files (nothing to run for them): $($deletedTests -join ', ')" }
    if ($candidates.Count) { 'Advisory candidates (name a changed type; not selected):'; $res.Candidates | Select-Object -First 25 | ForEach-Object { "    [{0}] {1}" -f $_.Project, $_.Class } }
    if ($CurrentFilter) { "Current filter holds $($currentClasses.Count) classes. MISSED by it ($($missed.Count)):"; $missed | ForEach-Object { "    $_" }; if ($extra.Count) { "  in the filter but not selected: $($extra.Count)" } }
    'Filters:'; foreach ($p in $filters.Keys) { "  $p : --filter `"$($filters[$p])`"" }
    if ($findings.Count) { 'FINDINGS:'; $findings | ForEach-Object { "  - $_" } }
}
if ($PassThru) { $res }
exit $res.ExitCode
