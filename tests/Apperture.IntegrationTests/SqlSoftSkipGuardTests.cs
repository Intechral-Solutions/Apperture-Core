using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// Source closure for the rule that a SQL Server test which cannot run against SQL Server is never reported as a plain pass:
/// every exit path taken because SQL Server is unavailable either throws or declares itself through
/// <see cref="SqlTestExecution.NotExecuted"/> (which throws when <c>APPERTURE_REQUIRE_SQL_TESTS=1</c>). The scanner reads the
/// code, not the layout: comments and string contents are blanked first, braces are matched, and each rule looks at what a block
/// does, so a re-indented or re-spelled copy of an old soft skip is still found. Four shapes are refused:
/// <list type="number">
/// <item>A raw acquisition (<c>SqlServerTestConfiguration.Get[Governance]ConnectionString()</c>, which returns null without
/// saying so) tested for null whose branch returns without throwing or declaring.</item>
/// <item>A <c>try</c> around <c>EnsureTestDatabaseExists</c> whose catch of <c>InvalidOperationException</c> (or anything wider)
/// returns without throwing or declaring.</item>
/// <item>An <c>if (!await ...CanConnectAsync())</c> branch that returns without throwing or declaring.</item>
/// <item>A LocalDB connection literal, or a direct read of the <c>APPERTURE_REQUIRE_SQL_TESTS</c> switch, outside the files that
/// own them.</item>
/// </list>
/// The scanner is exercised on synthetic sources and on a disposable copy of a real test file as well as on the real corpus.
/// </summary>
public sealed class SqlSoftSkipGuardTests
{
    private static readonly string[] Projects = ["tests/Apperture.IntegrationTests", "tests/Apperture.BrowserTests"];

    // ---- the real corpus -----------------------------------------------------------------------------------------------------

    [Fact]
    public void No_test_source_returns_early_from_an_unavailable_SQL_Server_without_declaring_it()
    {
        var root = RepoRoot();
        var findings = new List<SqlSoftSkipScanner.Finding>();
        var stats = new SqlSoftSkipScanner.Stats();
        var files = 0;

        foreach (var project in Projects)
        {
            foreach (var file in SqlSoftSkipScanner.EnumerateSourceFiles(Path.Combine(root, project)))
            {
                files++;
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                findings.AddRange(SqlSoftSkipScanner.Scan(File.ReadAllText(file), relative, stats));
            }
        }

        // A guard that sees nothing proves nothing: the walk and each rule's matcher must be live on the real corpus.
        files.Should().BeGreaterThan(150, "the walk must reach the integration and browser test sources");
        stats.RawAcquisitions.Should().BeGreaterThan(5, "the raw-acquisition matcher must see the known acquisition sites");
        stats.EnsureTries.Should().BeGreaterThan(5, "the try/catch matcher must see the known EnsureTestDatabaseExists sites");
        stats.CanConnectChecks.Should().BeGreaterThan(5, "the CanConnect matcher must see the known connectivity checks");
        stats.DeclaredExits.Should().BeGreaterThan(10, "the converted sites must be seen declaring themselves");

        findings.Should().BeEmpty(
            "a SQL test that cannot reach SQL Server must throw or call SqlTestExecution.NotExecuted, never return silently: "
            + string.Join("; ", findings.Select(f => f.ToString())));
    }

    [Fact]
    public void The_scanned_corpus_does_not_include_build_output()
    {
        var root = RepoRoot();

        foreach (var project in Projects)
        {
            foreach (var file in SqlSoftSkipScanner.EnumerateSourceFiles(Path.Combine(root, project)))
            {
                var segments = Path.GetRelativePath(root, file).Replace('\\', '/').Split('/');
                segments.Should().NotContain(["bin", "obj", "artifacts", "node_modules", "TestResults"]);
            }
        }
    }

    // ---- synthetic negative controls: each shape is refused, and its fixed form is accepted ----------------------------------

    [Theory]
    [MemberData(nameof(RefusedShapes))]
    public void Each_silent_skip_shape_is_refused(string name, string source, string rule)
    {
        var findings = SqlSoftSkipScanner.Scan(source, "Synthetic.cs", new SqlSoftSkipScanner.Stats());

        findings.Should().ContainSingle($"{name} must be found exactly once").Which.Rule.Should().Be(rule);
    }

    [Theory]
    [MemberData(nameof(AcceptedShapes))]
    public void Each_declared_or_failing_shape_is_accepted(string name, string source)
    {
        var findings = SqlSoftSkipScanner.Scan(source, "Synthetic.cs", new SqlSoftSkipScanner.Stats());

        findings.Should().BeEmpty($"{name} is an honest path: " + string.Join("; ", findings.Select(f => f.ToString())));
    }

    [Fact]
    public void A_silent_skip_added_to_a_disposable_copy_of_a_real_test_is_refused_and_removing_it_passes()
    {
        var root = RepoRoot();
        var original = File.ReadAllText(Path.Combine(root, "tests/Apperture.IntegrationTests/Ctx2MigrationSqlServerTests.cs"));
        var directory = Path.Combine(Path.GetTempPath(), "apperture-softskip-" + Guid.NewGuid().ToString("N"));
        var copyRoot = Path.Combine(directory, "tests", "Apperture.IntegrationTests");
        Directory.CreateDirectory(copyRoot);
        var copy = Path.Combine(copyRoot, "Ctx2MigrationSqlServerTests.cs");

        try
        {
            // The same file as it stood before the soft skips were routed through SqlTestExecution.
            var regressed = original
                .Replace("SqlTestExecution.ConnectionStringOrNotExecuted()", "SqlServerTestConfiguration.GetConnectionString()", StringComparison.Ordinal)
                .Replace("SqlTestExecution.NotExecuted(\"the test SQL Server could not be reached to create the catalog\");", string.Empty, StringComparison.Ordinal);
            regressed.Should().NotBe(original, "the injection must change the copy");
            File.WriteAllText(copy, regressed);

            ScanDirectory(directory).Should().NotBeEmpty("the old pattern must be refused");

            File.WriteAllText(copy, original);

            ScanDirectory(directory).Should().BeEmpty("the real file declares each skip");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static TheoryData<string, string, string> RefusedShapes() => new()
    {
        {
            "raw getter, null check, bare return",
            """
            class T { async Task A() {
                var cs = SqlServerTestConfiguration.GetConnectionString();
                if (cs is null)
                    return; // soft-skip
            } }
            """,
            SqlSoftSkipScanner.RawAcquisitionRule
        },
        {
            "raw getter, == null with braces, return null",
            """
            class T { static async Task<string?> A() {
                var configured = SqlServerTestConfiguration.GetGovernanceConnectionString ( );
                if ( configured == null ) { return null; }
                return configured;
            } }
            """,
            SqlSoftSkipScanner.RawAcquisitionRule
        },
        {
            "raw getter assigned to string?, null check, return inside nested braces",
            """
            class T { string? A() {
                string? cs = SqlServerTestConfiguration.GetConnectionString();
                if (cs is null)
                {
                    _output.WriteLine("not run");
                    return null;
                }
                return cs;
            } }
            """,
            SqlSoftSkipScanner.RawAcquisitionRule
        },
        {
            "catch InvalidOperationException around EnsureTestDatabaseExists that returns",
            """
            class T { void A(string cs) {
                try { SqlServerTestConfiguration.EnsureTestDatabaseExists(cs); }
                catch (InvalidOperationException) { return; }
            } }
            """,
            SqlSoftSkipScanner.CatchRule
        },
        {
            "bare catch around EnsureTestDatabaseExists that returns null",
            """
            class T { object? A(string cs) {
                try { SqlServerTestConfiguration.EnsureTestDatabaseExists(cs); }
                catch { return null; }
                return new object();
            } }
            """,
            SqlSoftSkipScanner.CatchRule
        },
        {
            "catch Exception with a filter around EnsureTestDatabaseExists that returns",
            """
            class T { object? A(string cs) {
                try { SqlServerTestConfiguration.EnsureTestDatabaseExists(cs); return new object(); }
                catch (Exception ex) when (ex is not FormatException and not ArgumentException) { cleanup(); return null; }
            } }
            """,
            SqlSoftSkipScanner.CatchRule
        },
        {
            "CanConnect false branch that returns",
            """
            class T { async Task A(Db db) {
                if (!await db.Database.CanConnectAsync())
                    return;
            } }
            """,
            SqlSoftSkipScanner.CanConnectRule
        },
        {
            "CanConnect false branch that disposes then returns null",
            """
            class T { async Task<Db?> A(Db db) {
                if (!await db.Database.CanConnectAsync(ct))
                {
                    await db.DisposeAsync();
                    return null;
                }
                return db;
            } }
            """,
            SqlSoftSkipScanner.CanConnectRule
        },
        {
            "CanConnect result kept in a variable, then a bare return",
            """
            class T { async Task A(Db db) {
                var canConnect = await db.Database.CanConnectAsync();
                if (!canConnect)
                    return; // Skip when unavailable
                canConnect.Should().BeTrue();
            } }
            """,
            SqlSoftSkipScanner.CanConnectRule
        },
        {
            "CanConnect compared with false, then a return",
            """
            class T { async Task A(Db db) {
                if (await db.Database.CanConnectAsync() == false) { return; }
            } }
            """,
            SqlSoftSkipScanner.CanConnectRule
        },
        {
            "CanConnect negated in extra parentheses, then a return",
            """
            class T { async Task A(Db db) {
                if (!(await db.Database.CanConnectAsync())) return;
            } }
            """,
            SqlSoftSkipScanner.CanConnectRule
        },
        {
            "implicit LocalDB fallback",
            """
            class T { string A() => Cfg.Get() ?? "Server=(localdb)\\mssqllocaldb;Database=X;Trusted_Connection=True;"; }
            """,
            SqlSoftSkipScanner.LocalDbRule
        },
        {
            "private re-read of the require switch",
            """
            class T { void A() { if (Environment.GetEnvironmentVariable("APPERTURE_REQUIRE_SQL_TESTS") == "1") throw new Exception(); } }
            """,
            SqlSoftSkipScanner.RequireSwitchRule
        },
    };

    public static TheoryData<string, string> AcceptedShapes() => new()
    {
        {
            "raw getter that throws",
            """
            class T { void A() {
                var cs = SqlServerTestConfiguration.GetConnectionString();
                if (cs is null)
                    throw new InvalidOperationException("no sql");
            } }
            """
        },
        {
            "raw getter that declares then returns",
            """
            class T { void A() {
                var cs = SqlServerTestConfiguration.GetConnectionString();
                if (cs is null) { SqlTestExecution.NotExecuted("none"); return; }
            } }
            """
        },
        {
            "the scratch helper, whose null is announced inside it",
            """
            class T { async Task A() {
                var scratch = await SqlServerScratchDatabase.CreateAsync("Apperture_X_");
                if (scratch is null) return;
            } }
            """
        },
        {
            "the approved acquisition, whose null is an announced skip",
            """
            class T { void A() {
                var cs = SqlTestExecution.ConnectionStringOrNotExecuted();
                if (cs is null) return;
            } }
            """
        },
        {
            "catch around EnsureTestDatabaseExists that declares then returns",
            """
            class T { void A(string cs) {
                try { SqlServerTestConfiguration.EnsureTestDatabaseExists(cs); }
                catch (InvalidOperationException) { SqlTestExecution.NotExecuted("unreachable"); return; }
            } }
            """
        },
        {
            "catch around EnsureTestDatabaseExists that rethrows",
            """
            class T { void A(string cs) {
                try { SqlServerTestConfiguration.EnsureTestDatabaseExists(cs); }
                catch (InvalidOperationException ex) { Log(ex); throw; }
            } }
            """
        },
        {
            "a catch of an unrelated type that returns",
            """
            class T { bool A(string cs) {
                try { SqlServerTestConfiguration.EnsureTestDatabaseExists(cs); return true; }
                catch (FormatException) { return false; }
            } }
            """
        },
        {
            "CanConnect false branch that declares",
            """
            class T { async Task A(Db db) {
                if (!await db.Database.CanConnectAsync()) { SqlTestExecution.NotExecuted("x"); return; }
            } }
            """
        },
        {
            "CanConnect false branch that throws",
            """
            class T { async Task A(Db db) {
                if (!await db.Database.CanConnectAsync())
                    throw new InvalidOperationException("cannot connect");
            } }
            """
        },
        {
            "CanConnect kept in a variable and asserted",
            """
            class T { async Task A(Db db) {
                var canConnect = await db.Database.CanConnectAsync();
                canConnect.Should().BeTrue();
                if (!canConnect) throw new InvalidOperationException("no");
            } }
            """
        },
        {
            "an unrelated null check that returns",
            """
            class T { void A() {
                var x = Lookup();
                if (x is null) return;
            } }
            """
        },
        {
            "the old patterns inside comments and strings",
            """
            class T { string A() {
                // var cs = SqlServerTestConfiguration.GetConnectionString(); if (cs is null) return; (localdb) APPERTURE_REQUIRE_SQL_TESTS
                /* try { EnsureTestDatabaseExists(x); } catch (InvalidOperationException) { return; } */
                return "if (!await db.Database.CanConnectAsync()) return;";
            } }
            """
        },
    };

    private static List<SqlSoftSkipScanner.Finding> ScanDirectory(string directory)
    {
        var findings = new List<SqlSoftSkipScanner.Finding>();
        foreach (var file in SqlSoftSkipScanner.EnumerateSourceFiles(directory))
            findings.AddRange(SqlSoftSkipScanner.Scan(File.ReadAllText(file), Path.GetRelativePath(directory, file), new SqlSoftSkipScanner.Stats()));

        return findings;
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Apperture.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Repository root (Apperture.slnx) not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>The scanner behind <see cref="SqlSoftSkipGuardTests"/>: text in, findings out, no file or project knowledge.</summary>
internal static class SqlSoftSkipScanner
{
    public const string RawAcquisitionRule = "raw-acquisition-null-return";
    public const string CatchRule = "ensure-catch-return";
    public const string CanConnectRule = "canconnect-false-return";
    public const string LocalDbRule = "localdb-literal";
    public const string RequireSwitchRule = "require-switch-reread";

    // The files that own what the last rule forbids elsewhere: the guard's LocalDB allowance and the switch itself.
    private static readonly string[] LocalDbOwners = ["SqlTestTargetGuard.cs", "SqlTestTargetGuardTests.cs", "SqlSoftSkipGuardTests.cs"];

    private static readonly string[] RequireSwitchOwners = ["SqlTestExecution.cs", "SqlSoftSkipGuardTests.cs"];

    private static readonly Regex RawAcquisition = new(
        @"\b(?:var|string\??)\s+(?<v>[A-Za-z_]\w*)\s*=\s*SqlServerTestConfiguration\s*\.\s*Get(?:Governance)?ConnectionString\s*\(\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex TryOpen = new(@"\btry\s*\{", RegexOptions.Compiled);
    private static readonly Regex CatchHeader = new(@"\G\s*catch\b", RegexOptions.Compiled);
    // if (!await x.CanConnectAsync()), if (!(await x.CanConnectAsync())), if (await x.CanConnectAsync() == false)
    private static readonly Regex CanConnectIf = new(
        @"\bif\s*\(\s*(?:!\s*\(?\s*await\s+[\w.]+\.CanConnectAsync\s*\(\s*[^()]*\)\s*\)?|await\s+[\w.]+\.CanConnectAsync\s*\(\s*[^()]*\)\s*==\s*false)\s*\)",
        RegexOptions.Compiled);

    // var ok = await x.CanConnectAsync(); ... if (!ok) ...
    private static readonly Regex CanConnectVariable = new(
        @"\b(?:var|bool)\s+(?<c>[A-Za-z_]\w*)\s*=\s*await\s+[\w.]+\.CanConnectAsync\s*\(\s*[^()]*\)\s*;", RegexOptions.Compiled);

    private static readonly Regex ReturnWord = new(@"\breturn\b", RegexOptions.Compiled);
    private static readonly Regex ThrowWord = new(@"\bthrow\b", RegexOptions.Compiled);
    private static readonly Regex DeclaresWord = new(@"\bNotExecuted\s*\(", RegexOptions.Compiled);
    private static readonly Regex LocalDb = new(@"\(localdb\)|mssqllocaldb", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public sealed record Finding(string File, int Line, string Rule)
    {
        public override string ToString() => $"{File}:{Line} [{Rule}]";
    }

    public sealed class Stats
    {
        public int RawAcquisitions { get; set; }
        public int EnsureTries { get; set; }
        public int CanConnectChecks { get; set; }
        public int DeclaredExits { get; set; }
    }

    /// <summary>Every <c>.cs</c> file under <paramref name="directory"/>, pruning build output while descending.</summary>
    public static IEnumerable<string> EnumerateSourceFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly))
            yield return file;

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (name is "bin" or "obj" or "artifacts" or "node_modules" or "TestResults")
                continue;

            foreach (var file in EnumerateSourceFiles(child))
                yield return file;
        }
    }

    public static List<Finding> Scan(string source, string path, Stats stats)
    {
        var fileName = Path.GetFileName(path);
        var findings = new List<Finding>();
        var withoutComments = Blank(source, blankStrings: false);
        var code = Blank(source, blankStrings: true);

        // Rule 1: a raw acquisition tested for null whose branch leaves without throwing or declaring.
        foreach (Match acquisition in RawAcquisition.Matches(code))
        {
            stats.RawAcquisitions++;
            var name = Regex.Escape(acquisition.Groups["v"].Value);
            var nullCheck = new Regex($@"\bif\s*\(\s*{name}\s*(?:is\s+null|==\s*null)\s*\)");
            foreach (Match check in nullCheck.Matches(code, acquisition.Index + acquisition.Length))
            {
                var branch = Branch(code, check.Index + check.Length);
                if (LeavesSilently(branch, stats))
                    findings.Add(new Finding(path, LineOf(code, check.Index), RawAcquisitionRule));
            }
        }

        // Rule 2: a try around EnsureTestDatabaseExists whose broad catch leaves without throwing or declaring.
        foreach (Match open in TryOpen.Matches(code))
        {
            var braceIndex = open.Index + open.Length - 1;
            var close = MatchingClose(code, braceIndex, '{', '}');
            if (close < 0 || !code.AsSpan(braceIndex, close - braceIndex).Contains("EnsureTestDatabaseExists", StringComparison.Ordinal))
                continue;

            stats.EnsureTries++;
            var position = close + 1;
            while (true)
            {
                var header = CatchHeader.Match(code, position);
                if (!header.Success)
                    break;

                var cursor = header.Index + header.Length;
                var type = string.Empty;
                cursor = SkipWhitespace(code, cursor);
                if (cursor < code.Length && code[cursor] == '(')
                {
                    var closeParen = MatchingClose(code, cursor, '(', ')');
                    type = code[(cursor + 1)..closeParen].Trim();
                    cursor = SkipWhitespace(code, closeParen + 1);
                }

                if (code.AsSpan(cursor).StartsWith("when", StringComparison.Ordinal) && cursor + 4 < code.Length && !char.IsLetterOrDigit(code[cursor + 4]))
                {
                    var filterOpen = code.IndexOf('(', cursor);
                    cursor = SkipWhitespace(code, MatchingClose(code, filterOpen, '(', ')') + 1);
                }

                if (cursor >= code.Length || code[cursor] != '{')
                    break;

                var catchClose = MatchingClose(code, cursor, '{', '}');
                var body = code[(cursor + 1)..catchClose];
                if (IsBroad(type) && LeavesSilently(body, stats))
                    findings.Add(new Finding(path, LineOf(code, header.Index), CatchRule));

                position = catchClose + 1;
            }
        }

        // Rule 3: a failed connectivity check whose branch leaves without throwing or declaring.
        foreach (Match check in CanConnectIf.Matches(code))
        {
            stats.CanConnectChecks++;
            if (LeavesSilently(Branch(code, check.Index + check.Length), stats))
                findings.Add(new Finding(path, LineOf(code, check.Index), CanConnectRule));
        }

        foreach (Match assignment in CanConnectVariable.Matches(code))
        {
            stats.CanConnectChecks++;
            var failed = new Regex($@"\bif\s*\(\s*!\s*{Regex.Escape(assignment.Groups["c"].Value)}\s*\)|\bif\s*\(\s*{Regex.Escape(assignment.Groups["c"].Value)}\s*==\s*false\s*\)");
            foreach (Match check in failed.Matches(code, assignment.Index + assignment.Length))
            {
                if (LeavesSilently(Branch(code, check.Index + check.Length), stats))
                    findings.Add(new Finding(path, LineOf(code, check.Index), CanConnectRule));
            }
        }

        // Rule 4: LocalDB and the require switch belong to the files that own them.
        if (!LocalDbOwners.Contains(fileName))
        {
            // One finding per line: a LocalDB server name is usually written as both "(localdb)" and "mssqllocaldb".
            foreach (var line in LocalDb.Matches(withoutComments).Select(m => LineOf(withoutComments, m.Index)).Distinct())
                findings.Add(new Finding(path, line, LocalDbRule));
        }

        if (!RequireSwitchOwners.Contains(fileName))
        {
            var at = withoutComments.IndexOf(SqlTestExecution.RequireVariable, StringComparison.Ordinal);
            while (at >= 0)
            {
                findings.Add(new Finding(path, LineOf(withoutComments, at), RequireSwitchRule));
                at = withoutComments.IndexOf(SqlTestExecution.RequireVariable, at + 1, StringComparison.Ordinal);
            }
        }

        return findings;
    }

    private static bool IsBroad(string catchType) =>
        catchType.Length == 0
        || catchType.Split(' ')[0] is "Exception" or "System.Exception" or "InvalidOperationException" or "System.InvalidOperationException"
            or "SqlException" or "Microsoft.Data.SqlClient.SqlException";

    private static bool LeavesSilently(string block, Stats stats)
    {
        if (!ReturnWord.IsMatch(block))
            return false;

        if (ThrowWord.IsMatch(block))
            return false;

        if (DeclaresWord.IsMatch(block))
        {
            stats.DeclaredExits++;
            return false;
        }

        return true;
    }

    /// <summary>The block or single statement that starts at <paramref name="index"/>, after an if or catch header.</summary>
    private static string Branch(string code, int index)
    {
        index = SkipWhitespace(code, index);
        if (index >= code.Length)
            return string.Empty;

        if (code[index] == '{')
        {
            var close = MatchingClose(code, index, '{', '}');
            return close < 0 ? code[index..] : code[(index + 1)..close];
        }

        var end = code.IndexOf(';', index);
        return end < 0 ? code[index..] : code[index..end];
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;

        return index;
    }

    private static int MatchingClose(string text, int openIndex, char open, char close)
    {
        var depth = 0;
        for (var i = openIndex; i < text.Length; i++)
        {
            if (text[i] == open)
                depth++;
            else if (text[i] == close && --depth == 0)
                return i;
        }

        return -1;
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return line;
    }

    /// <summary>
    /// The source with comments (and, if asked, the contents of string and character literals) replaced by spaces, so offsets and
    /// line numbers are unchanged but nothing inside a comment or a literal can be mistaken for code or hide a brace.
    /// </summary>
    internal static string Blank(string source, bool blankStrings)
    {
        var text = new StringBuilder(source);
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (c == '/' && next == '/')
            {
                for (; i < source.Length && source[i] != '\n' && source[i] != '\r'; i++)
                    text[i] = ' ';
            }
            else if (c == '/' && next == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                for (; i < end; i++)
                {
                    if (source[i] != '\n' && source[i] != '\r')
                        text[i] = ' ';
                }
            }
            else if (c == '"' && source.AsSpan(i).StartsWith("\"\"\"", StringComparison.Ordinal))
            {
                var quotes = 3;
                while (i + quotes < source.Length && source[i + quotes] == '"')
                    quotes++;

                var terminator = new string('"', quotes);
                var end = source.IndexOf(terminator, i + quotes, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + quotes;
                if (blankStrings)
                    BlankRange(text, source, i + quotes, end - quotes);
                i = end;
            }
            else if (c == '"')
            {
                var verbatim = i > 0 && (source[i - 1] == '@' || (i > 1 && source[i - 1] == '$' && source[i - 2] == '@') || (i > 1 && source[i - 1] == '@' && source[i - 2] == '$'));
                var start = i + 1;
                i++;
                while (i < source.Length)
                {
                    if (source[i] == '\\' && !verbatim)
                        i += 2;
                    else if (source[i] == '"' && verbatim && i + 1 < source.Length && source[i + 1] == '"')
                        i += 2;
                    else if (source[i] == '"' || (!verbatim && (source[i] == '\n' || source[i] == '\r')))
                        break;
                    else
                        i++;
                }

                if (blankStrings)
                    BlankRange(text, source, start, Math.Min(i, source.Length));
                i++;
            }
            else if (c == '\'' && !(i > 0 && char.IsLetterOrDigit(source[i - 1])))
            {
                var start = i + 1;
                i++;
                while (i < source.Length && source[i] != '\'' && source[i] != '\n')
                    i += source[i] == '\\' ? 2 : 1;

                if (blankStrings)
                    BlankRange(text, source, start, Math.Min(i, source.Length));
                i++;
            }
            else
            {
                i++;
            }
        }

        return text.ToString();
    }

    private static void BlankRange(StringBuilder text, string source, int start, int end)
    {
        for (var k = start; k < end && k < source.Length; k++)
        {
            if (source[k] != '\n' && source[k] != '\r')
                text[k] = ' ';
        }
    }
}
