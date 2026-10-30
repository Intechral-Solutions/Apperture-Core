using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// An inexpensive, lexical second line of defence for the rule that a SQL Server test which cannot run against SQL Server fails
/// (see <see cref="SqlTestExecution"/>). It is not exhaustive and does not claim to be: the run-level check
/// <c>scripts/dev/Test-SqlExecutionHonesty.ps1</c> is what catches a false pass this scan cannot recognise.
/// </summary>
/// <remarks>
/// <para>
/// The scanner reads code, not layout: comments and string contents are blanked, parentheses and braces are matched, and each rule
/// looks at what a construct does, so a re-indented, re-spelled or re-shaped copy of an old soft skip is still found. Seven rules:
/// </para>
/// <list type="number">
/// <item><b>null-returning-getter</b>: <c>SqlServerTestConfiguration.Get[Governance]ConnectionString()</c> returns null without
/// saying so, so any call outside its owners must carry <c>?? throw</c> in the same statement. Every shape that tests that null
/// (pattern match, <c>IsNullOrEmpty</c>, inverted guard, field) is out of scope because the getter itself is refused;
/// <c>RequireConnectionString()</c> is the way to obtain a server.</item>
/// <item><b>availability-condition-without-throw</b>: an <c>if</c> whose condition mentions SQL availability
/// (<c>CanConnect</c>, <c>CanConnectAsync</c> in any spelling, <c>SqlAvailable</c>, <c>Reachable</c>, or a variable assigned from
/// one) must throw in a branch. It may not return, or wrap the test body, and carry on.</item>
/// <item><b>swallowing-catch</b>: a broad catch (bare, <c>Exception</c>, <c>InvalidOperationException</c>, <c>SqlException</c>,
/// <c>DbException</c>) that does not throw, around SQL setup code or ending in a bare return. A best-effort <c>DROP DATABASE</c>
/// cleanup is the one allowed swallow.</item>
/// <item><b>skip-vocabulary</b>: a member or call named <c>NotExecuted</c>, <c>SoftSkip*</c>, <c>ConnectionStringOrNotExecuted</c> or
/// <c>Try{Create,Connect,Open,Seed,Migrate,Prepare,Acquire}*</c>, the names a soft-skip helper takes.</item>
/// <item><b>localdb-literal</b>: <c>(localdb)</c> or <c>mssqllocaldb</c> outside the target guard and its tests.</item>
/// <item><b>retired-switch</b>: <c>APPERTURE_REQUIRE_SQL_TESTS</c> outside the test that proves it is inert.</item>
/// </list>
/// <para>
/// The rules are exercised on synthetic sources (including the independent review's P01-P12 shapes), on a disposable copy of a real
/// test, and on the real corpus, with non-empty floors so that a walk or matcher that sees nothing fails.
/// </para>
/// </remarks>
public sealed class SqlSoftSkipGuardTests
{
    private static readonly string[] Projects = ["tests/Apperture.IntegrationTests", "tests/Apperture.BrowserTests"];

    // ---- the real corpus -----------------------------------------------------------------------------------------------------

    [Fact]
    public void No_test_source_returns_early_from_an_unavailable_SQL_Server_or_swallows_it()
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

        // A guard that sees nothing proves nothing: the walk and each matcher must be live on the real corpus.
        files.Should().BeGreaterThan(150, "the walk must reach the integration and browser test sources");
        stats.GetterCalls.Should().BeGreaterThan(2, "the getter matcher must see the owners' and fixtures' calls");
        stats.AvailabilityConditions.Should().BeGreaterThan(5, "the availability matcher must see the known CanConnect checks");
        stats.BroadCatches.Should().BeGreaterThan(5, "the catch matcher must see the known broad catches");
        stats.CleanupCatches.Should().BeGreaterThan(2, "the best-effort DROP DATABASE cleanups must be recognised as such");

        findings.Should().BeEmpty(
            "a SQL test that cannot reach SQL Server must throw, never return silently or swallow it: "
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

        findings.Select(f => f.Rule).Should().Contain(rule, $"{name} must be refused by rule {rule}");
    }

    [Theory]
    [MemberData(nameof(AcceptedShapes))]
    public void Each_failing_or_cleanup_shape_is_accepted(string name, string source)
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
            // The old soft skip in a shape no earlier version of this scan recognised: a pattern-matched getter and a bare return.
            var regressed = original.Replace(
                "var baseConnectionString = SqlServerTestConfiguration.RequireConnectionString();",
                "if (SqlServerTestConfiguration.GetConnectionString() is not { } baseConnectionString) return;",
                StringComparison.Ordinal);
            regressed.Should().NotBe(original, "the injection must change the copy");
            File.WriteAllText(copy, regressed);

            ScanDirectory(directory).Should().NotBeEmpty("the soft skip must be refused");

            File.WriteAllText(copy, original);

            ScanDirectory(directory).Should().BeEmpty("the real file fails when SQL Server is unavailable");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static TheoryData<string, string, string> RefusedShapes() => new()
    {
        {
            "C00 control: the original raw getter, null then bare return",
            """
            class T { async Task A() {
                var cs = SqlServerTestConfiguration.GetConnectionString();
                if (cs is null)
                    return; // soft-skip
            } }
            """,
            SqlSoftSkipScanner.GetterRule
        },
        {
            "P01 pattern-matched getter exit",
            """
            class T { async Task A() {
                if (SqlServerTestConfiguration.GetConnectionString() is not { } cs) return;
                Use(cs);
            } }
            """,
            SqlSoftSkipScanner.GetterRule
        },
        {
            "P02 string.IsNullOrEmpty check",
            """
            class T { async Task A() {
                var cs = SqlServerTestConfiguration.GetGovernanceConnectionString();
                if (string.IsNullOrEmpty(cs)) { return; }
            } }
            """,
            SqlSoftSkipScanner.GetterRule
        },
        {
            "P03 inverted guard wraps the body, no return",
            """
            class T { async Task A() {
                var cs = SqlServerTestConfiguration.GetConnectionString();
                if (cs is not null)
                {
                    await RunTestBody(cs);
                }
            } }
            """,
            SqlSoftSkipScanner.GetterRule
        },
        {
            "P04 assignment to a field",
            """
            class T { string? _cs; async Task A() {
                _cs = SqlServerTestConfiguration . GetConnectionString ( );
                if (_cs == null) return;
            } }
            """,
            SqlSoftSkipScanner.GetterRule
        },
        {
            "P05 CanConnectAsync().ConfigureAwait(false)",
            """
            class T { async Task A(Db db) {
                if (!await db.Database.CanConnectAsync().ConfigureAwait(false)) return;
            } }
            """,
            SqlSoftSkipScanner.AvailabilityRule
        },
        {
            "P06 synchronous CanConnect()",
            """
            class T { void A(Db db) {
                if (!db.Database.CanConnect()) return;
            } }
            """,
            SqlSoftSkipScanner.AvailabilityRule
        },
        {
            "P07 CanConnectAsync() is false",
            """
            class T { async Task A(Db db) {
                if (await db.Database.CanConnectAsync() is false) { return; }
            } }
            """,
            SqlSoftSkipScanner.AvailabilityRule
        },
        {
            "P08 helper indirection inside the try",
            """
            class T { async Task A(string cs) {
                try { await Prepare(cs); }
                catch (Exception) { return; }
                await Body();
            } }
            """,
            SqlSoftSkipScanner.CatchRule
        },
        {
            "P09 flag set in catch, return after",
            """
            class T { void A(string cs) {
                var ok = true;
                try { SqlServerTestConfiguration.EnsureTestDatabaseExists(cs); }
                catch (InvalidOperationException) { ok = false; }
                if (!ok) return;
            } }
            """,
            SqlSoftSkipScanner.CatchRule
        },
        {
            "P10 fixture flag early exit",
            """
            class T { async Task A() {
                if (!_fixture.SqlAvailable) return;
                await Body();
            } }
            """,
            SqlSoftSkipScanner.AvailabilityRule
        },
        {
            "P11 private NotExecuted that only logs",
            """
            class T {
                private void NotExecuted(string reason) { _output.WriteLine("NOT EXECUTED: " + reason); }
                void A() { NotExecuted("no sql"); return; }
            }
            """,
            SqlSoftSkipScanner.VocabularyRule
        },
        {
            "P12 catch DbException around Ensure",
            """
            class T { void A(string cs) {
                try { SqlServerTestConfiguration.EnsureTestDatabaseExists(cs); }
                catch (DbException) { return; }
            } }
            """,
            SqlSoftSkipScanner.CatchRule
        },
        {
            "catching the unavailable failure itself and returning",
            """
            class T { async Task A() {
                string cs;
                try { cs = SqlServerTestConfiguration.RequireConnectionString(); }
                catch (SqlTestUnavailableException) { return; }
                Use(cs);
            } }
            """,
            SqlSoftSkipScanner.CatchRule
        },
        {
            "catching the refusal itself and returning",
            """
            class T { async Task A() {
                try { await SqlServerScratchDatabase.CreateAsync("Apperture_X_"); }
                catch (SqlTestTargetRefusedException) { return; }
            } }
            """,
            SqlSoftSkipScanner.CatchRule
        },
        {
            "availability result kept in a variable, then a bare return",
            """
            class T { async Task A(Db db) {
                var canConnect = await db.Database.CanConnectAsync();
                if (!canConnect)
                    return; // Skip when unavailable
                canConnect.Should().BeTrue();
            } }
            """,
            SqlSoftSkipScanner.AvailabilityRule
        },
        {
            "availability compared with false, braces, dispose then return null",
            """
            class T { async Task<Db?> A(Db db) {
                if (await db.Database.CanConnectAsync() == false) { await db.DisposeAsync(); return null; }
                return db;
            } }
            """,
            SqlSoftSkipScanner.AvailabilityRule
        },
        {
            "broad catch with a filter that returns null",
            """
            class T { object? A(string cs) {
                try { SqlServerTestConfiguration.EnsureTestDatabaseExists(cs); return new object(); }
                catch (Exception ex) when (ex is not FormatException and not ArgumentException) { cleanup(); return null; }
            } }
            """,
            SqlSoftSkipScanner.CatchRule
        },
        {
            "Try* acquisition helper",
            """
            class T { async Task A() {
                var db = await TryCreateMigratedDbAsync();
                if (db is null) return;
            } }
            """,
            SqlSoftSkipScanner.VocabularyRule
        },
        {
            "implicit LocalDB fallback",
            """
            class T { string A() => Cfg.Get() ?? "Server=(localdb)\\mssqllocaldb;Database=X;Trusted_Connection=True;"; }
            """,
            SqlSoftSkipScanner.LocalDbRule
        },
        {
            "private re-read of the retired switch",
            """
            class T { void A() { if (Environment.GetEnvironmentVariable("APPERTURE_REQUIRE_SQL_TESTS") == "1") throw new Exception(); } }
            """,
            SqlSoftSkipScanner.RetiredSwitchRule
        },
    };

    public static TheoryData<string, string> AcceptedShapes() => new()
    {
        {
            "the getter with ?? throw in the same statement",
            """
            class T { void A() {
                var cs = SqlServerTestConfiguration.GetGovernanceConnectionString()
                    ?? throw SqlTestExecution.Unavailable("none");
            } }
            """
        },
        {
            "RequireConnectionString, which cannot return null",
            """
            class T { void A() { var cs = SqlServerTestConfiguration.RequireConnectionString(); Use(cs); } }
            """
        },
        {
            "the EF Database.GetConnectionString()",
            """
            class T { string A(Db db) => new Builder(db.Database.GetConnectionString()).ConnectionString; }
            """
        },
        {
            "an availability check that throws",
            """
            class T { async Task A(Db db) {
                if (!await db.Database.CanConnectAsync()) throw SqlTestExecution.Unavailable("x");
            } }
            """
        },
        {
            "an availability check that throws in the else branch",
            """
            class T { async Task A(Db db) {
                if (await db.Database.CanConnectAsync()) { await Body(); }
                else { throw SqlTestExecution.Unavailable("x"); }
            } }
            """
        },
        {
            "an availability variable that is asserted and thrown on",
            """
            class T { async Task A(Db db) {
                var canConnect = await db.Database.CanConnectAsync();
                canConnect.Should().BeTrue();
                if (!canConnect) throw new InvalidOperationException("no");
            } }
            """
        },
        {
            "a best-effort DROP DATABASE cleanup that swallows",
            """
            class T { async Task Drop(string master, string c) {
                try { await Run(master, "DROP DATABASE [" + c + "]"); }
                catch { }
            } }
            """
        },
        {
            "a catch that rethrows after cleanup",
            """
            class T { async Task A(string cs) {
                try { SqlServerTestConfiguration.EnsureTestDatabaseExists(cs); }
                catch (InvalidOperationException ex) { Log(ex); throw; }
            } }
            """
        },
        {
            "a catch of an unrelated type around SQL setup",
            """
            class T { bool A(string cs) {
                try { SqlServerTestConfiguration.EnsureTestDatabaseExists(cs); return true; }
                catch (FormatException) { return false; }
            } }
            """
        },
        {
            "a network probe that returns false from a catch (no SQL setup, not a bare return)",
            """
            class T { async Task<bool> Open(string host) {
                try { using var tcp = new TcpClient(); await tcp.ConnectAsync(host, 22); return true; }
                catch { return false; }
            } }
            """
        },
        {
            "an exception captured by a concurrency test",
            """
            class T { async Task A() {
                try { await Race(); }
                catch (Exception ex) { errors.Add(ex); }
            } }
            """
        },
        {
            "an unrelated null check that returns",
            """
            class T { void A() { var x = Lookup(); if (x is null) return; } }
            """
        },
        {
            "the old patterns inside comments and strings",
            """
            class T { string A() {
                // var cs = SqlServerTestConfiguration.GetConnectionString(); if (cs is null) return; (localdb) APPERTURE_REQUIRE_SQL_TESTS
                /* try { EnsureTestDatabaseExists(x); } catch (InvalidOperationException) { return; } NotExecuted( */
                return "if (!await db.Database.CanConnectAsync()) return; TryCreateDbAsync()";
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
    public const string GetterRule = "null-returning-getter";
    public const string AvailabilityRule = "availability-condition-without-throw";
    public const string CatchRule = "swallowing-catch";
    public const string VocabularyRule = "skip-vocabulary";
    public const string LocalDbRule = "localdb-literal";
    public const string RetiredSwitchRule = "retired-switch";

    // The owners of what the rules refuse elsewhere. The two Management Host fixtures may call the governance getter because each
    // pairs it with ?? throw, which rule 1 checks for them like any other caller.
    private static readonly string[] GetterOwners = ["SqlServerTestConfiguration.cs", "SqlServerTestConfigurationTests.cs"];
    private static readonly string[] LocalDbOwners = ["SqlTestTargetGuard.cs", "SqlTestTargetGuardTests.cs", "SqlSoftSkipGuardTests.cs"];
    private static readonly string[] RetiredSwitchOwners = ["SqlServerTestConfigurationTests.cs", "SqlSoftSkipGuardTests.cs"];

    // The null-returning getters, but not EF's db.Database.GetConnectionString().
    private static readonly Regex Getter = new(
        @"(?<!Database\s*\.\s*)\bGet(?:Governance)?ConnectionString\s*\(", RegexOptions.Compiled);

    private static readonly Regex IfOpen = new(@"\bif\s*\(", RegexOptions.Compiled);
    private static readonly Regex ElseWord = new(@"\G\s*else\b", RegexOptions.Compiled);
    private static readonly Regex TryOpen = new(@"\btry\s*\{", RegexOptions.Compiled);
    private static readonly Regex CatchHeader = new(@"\G\s*catch\b", RegexOptions.Compiled);
    private static readonly Regex ThrowWord = new(@"\bthrow\b", RegexOptions.Compiled);
    // A bare return: what a skipped test method does. A catch that returns a value (null, false) is a probe or a lookup.
    private static readonly Regex ReturnNothing = new(@"\breturn\s*;", RegexOptions.Compiled);

    private const string AvailabilityVocabulary =
        @"\bCanConnect(?:Async)?\b|\bSqlAvailable\b|\bIsSqlAvailable\b|\bSqlReachable\b|\bIsReachable\b|\bIsAvailable\b|\bReachable\b";

    private static readonly Regex Availability = new(AvailabilityVocabulary, RegexOptions.Compiled);

    // name = <expression mentioning availability>;  (declaration or assignment)
    private static readonly Regex AvailabilityAssignment = new(
        @"\b(?<n>[A-Za-z_]\w*)\s*=\s*[^;=][^;]*?(?:" + AvailabilityVocabulary + @")[^;]*;", RegexOptions.Compiled);

    // Code a test runs to reach or prepare SQL Server: what a swallowing catch must not hide.
    private static readonly Regex SqlSetup = new(
        @"EnsureTestDatabaseExists|RequireConnectionString|\bGet(?:Governance)?ConnectionString\b|SqlServerScratchDatabase|AuditSqlScratch|\bCanConnect(?:Async)?\b|\bMigrateAsync\b|AppertureDatabaseMigrator|\bCreate(?:Migrated|Seeded|Empty)\w*Async\b|\bSeedAsync\b",
        RegexOptions.Compiled);

    private static readonly Regex SkipVocabulary = new(
        @"\bNotExecuted\w*\s*\(|\bConnectionStringOrNotExecuted\b|\bSoftSkip\w*\s*\(|(?<!\.\s*)\bTry(?:Create|Connect|Seed|Migrate|Prepare|Acquire)\w*Async\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex LocalDb = new(@"\(localdb\)|mssqllocaldb", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const string RetiredSwitch = "APPERTURE_REQUIRE_SQL_TESTS";

    private static readonly string[] BroadCatchTypes =
    [
        "Exception", "System.Exception", "InvalidOperationException", "System.InvalidOperationException",
        "SqlException", "Microsoft.Data.SqlClient.SqlException", "DbException", "System.Data.Common.DbException",

        // The failures this suite throws on purpose. Catching one and carrying on is exactly a soft skip.
        "SqlTestUnavailableException", "SqlTestTargetRefusedException",
    ];

    public sealed record Finding(string File, int Line, string Rule)
    {
        public override string ToString() => $"{File}:{Line} [{Rule}]";
    }

    public sealed class Stats
    {
        public int GetterCalls { get; set; }
        public int AvailabilityConditions { get; set; }
        public int BroadCatches { get; set; }
        public int CleanupCatches { get; set; }
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

        ScanGetters(code, path, fileName, findings, stats);
        ScanAvailabilityConditions(code, path, findings, stats);
        ScanCatches(code, withoutComments, path, findings, stats);

        foreach (Match word in SkipVocabulary.Matches(code))
            findings.Add(new Finding(path, LineOf(code, word.Index), VocabularyRule));

        if (!LocalDbOwners.Contains(fileName))
        {
            // One finding per line: a LocalDB server name is usually written as both "(localdb)" and "mssqllocaldb".
            foreach (var line in LocalDb.Matches(withoutComments).Select(m => LineOf(withoutComments, m.Index)).Distinct())
                findings.Add(new Finding(path, line, LocalDbRule));
        }

        if (!RetiredSwitchOwners.Contains(fileName))
        {
            for (var at = withoutComments.IndexOf(RetiredSwitch, StringComparison.Ordinal); at >= 0;
                 at = withoutComments.IndexOf(RetiredSwitch, at + 1, StringComparison.Ordinal))
                findings.Add(new Finding(path, LineOf(withoutComments, at), RetiredSwitchRule));
        }

        return findings;
    }

    private static void ScanGetters(string code, string path, string fileName, List<Finding> findings, Stats stats)
    {
        foreach (Match call in Getter.Matches(code))
        {
            stats.GetterCalls++;
            if (GetterOwners.Contains(fileName))
                continue;

            var end = code.IndexOf(';', call.Index);
            var statement = end < 0 ? code[call.Index..] : code[call.Index..end];
            if (!Regex.IsMatch(statement, @"\?\?\s*throw\b"))
                findings.Add(new Finding(path, LineOf(code, call.Index), GetterRule));
        }
    }

    private static void ScanAvailabilityConditions(string code, string path, List<Finding> findings, Stats stats)
    {
        // Variables that hold an availability answer: var ok = await db.Database.CanConnectAsync();
        var carriers = AvailabilityAssignment.Matches(code).Select(m => Regex.Escape(m.Groups["n"].Value)).Distinct().ToList();
        var carrierPattern = carriers.Count == 0 ? null : new Regex(@"\b(?:" + string.Join("|", carriers) + @")\b");

        foreach (Match open in IfOpen.Matches(code))
        {
            var parenIndex = open.Index + open.Length - 1;
            var closeParen = MatchingClose(code, parenIndex, '(', ')');
            if (closeParen < 0)
                continue;

            var condition = code[(parenIndex + 1)..closeParen];
            if (!Availability.IsMatch(condition) && !(carrierPattern?.IsMatch(condition) ?? false))
                continue;

            stats.AvailabilityConditions++;
            var thenBranch = Branch(code, closeParen + 1, out var thenEnd);
            var elseBranch = string.Empty;
            var elseMatch = ElseWord.Match(code, thenEnd);
            if (elseMatch.Success)
                elseBranch = Branch(code, elseMatch.Index + elseMatch.Length, out _);

            if (!ThrowWord.IsMatch(thenBranch) && !ThrowWord.IsMatch(elseBranch))
                findings.Add(new Finding(path, LineOf(code, open.Index), AvailabilityRule));
        }
    }

    private static void ScanCatches(string code, string withoutComments, string path, List<Finding> findings, Stats stats)
    {
        foreach (Match open in TryOpen.Matches(code))
        {
            var braceIndex = open.Index + open.Length - 1;
            var close = MatchingClose(code, braceIndex, '{', '}');
            if (close < 0)
                continue;

            var tryCode = code[braceIndex..close];
            var cleanup = withoutComments[braceIndex..close].Contains("DROP DATABASE", StringComparison.OrdinalIgnoreCase);
            var touchesSql = SqlSetup.IsMatch(tryCode);

            var position = close + 1;
            while (true)
            {
                var header = CatchHeader.Match(code, position);
                if (!header.Success)
                    break;

                var cursor = SkipWhitespace(code, header.Index + header.Length);
                var type = string.Empty;
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
                if (IsBroad(type))
                {
                    stats.BroadCatches++;
                    if (cleanup)
                        stats.CleanupCatches++;
                    else if (!ThrowWord.IsMatch(body) && (touchesSql || ReturnNothing.IsMatch(body)))
                        findings.Add(new Finding(path, LineOf(code, header.Index), CatchRule));
                }

                position = catchClose + 1;
            }
        }
    }

    private static bool IsBroad(string catchType) =>
        catchType.Length == 0 || BroadCatchTypes.Contains(catchType.Split(' ')[0]);

    /// <summary>The block or single statement that starts at <paramref name="index"/>, after an if header or an else.</summary>
    private static string Branch(string code, int index, out int end)
    {
        index = SkipWhitespace(code, index);
        end = index;
        if (index >= code.Length)
            return string.Empty;

        if (code[index] == '{')
        {
            var close = MatchingClose(code, index, '{', '}');
            end = close < 0 ? code.Length : close + 1;
            return close < 0 ? code[index..] : code[(index + 1)..close];
        }

        var semicolon = code.IndexOf(';', index);
        end = semicolon < 0 ? code.Length : semicolon + 1;
        return semicolon < 0 ? code[index..] : code[index..semicolon];
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
