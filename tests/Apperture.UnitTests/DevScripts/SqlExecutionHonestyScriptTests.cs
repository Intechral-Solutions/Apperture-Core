using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.DevScripts;

/// <summary>
/// The exit-code, freshness and provenance contract of <c>scripts/dev/Test-SqlExecutionHonesty.ps1</c>. The real script runs in
/// PowerShell 7 against a throw-away repository (its own git history, lists and a fake test assembly) with a stand-in for
/// <c>dotnet</c> that writes whatever TRX, discovery output and exit code the scenario calls for. Nothing here touches a SQL Server
/// or the real repository; the script text under test is a copy of the real file. Scenarios run concurrently once, and each test
/// asserts on its scenario's stored result. PowerShell 7 (<c>pwsh</c>) and git are required, and a machine without them fails the
/// tests rather than skipping them.
/// </summary>
public sealed class SqlExecutionHonestyScriptTests : IClassFixture<SqlExecutionHonestyScriptTests.Scenarios>
{
    private readonly Scenarios _scenarios;

    public SqlExecutionHonestyScriptTests(Scenarios scenarios) => _scenarios = scenarios;

    // ---- the exit-code contract ----------------------------------------------------------------------------------------------

    [Fact]
    public void A_clean_run_with_only_allow_listed_passes_exits_0_and_is_authoritative()
    {
        var run = _scenarios.Run("pass");

        run.ExitCode.Should().Be(0, run.Output);
        run.Verdict.Should().Be("PASS");
        run.Report.GetProperty("authoritative").GetBoolean().Should().BeTrue();
        run.Report.GetProperty("exitCode").GetInt32().Should().Be(0);
        run.Violations.Should().BeEmpty();
        run.Report.GetProperty("discovered").GetInt32().Should().Be(4);
        run.Report.GetProperty("executed").GetInt32().Should().Be(4);
    }

    [Fact]
    public void A_test_that_passes_without_being_on_the_allow_list_fails_the_run()
    {
        var run = _scenarios.Run("unexpected-pass");

        run.ExitCode.Should().Be(1, run.Output);
        run.Verdict.Should().Be("FAIL");
        run.Strings("unexpectedPasses").Should().Equal(Scenarios.NewSqlTest);
        run.Strings("mustFailViolations").Should().BeEmpty("only the unexpected-pass check can see a brand-new test that silently returns");
        run.Violations.Should().ContainSingle().Which.Should().Contain("not on the approved non-SQL allow-list");
    }

    [Fact]
    public void A_former_soft_skip_that_passes_fails_the_run()
    {
        var run = _scenarios.Run("must-fail-passed");

        run.ExitCode.Should().Be(1, run.Output);
        run.Strings("unexpectedPasses").Should().Equal(Scenarios.SqlTestOne);
        run.Strings("mustFailViolations").Should().ContainSingle().Which.Should().Contain(Scenarios.SqlTestOne);
    }

    [Fact]
    public void Record_mode_exits_2_and_is_never_an_authoritative_pass()
    {
        var run = _scenarios.Run("record");

        run.ExitCode.Should().Be(2, run.Output);
        run.Verdict.Should().Be("RECORDED");
        run.Report.GetProperty("authoritative").GetBoolean().Should().BeFalse();
        run.Report.GetProperty("exitCode").GetInt32().Should().Be(2);
        File.Exists(Path.Combine(run.Out, "observed-passes.txt")).Should().BeTrue();
        File.ReadAllLines(Path.Combine(run.Out, "observed-passes.txt")).Should().Contain(Scenarios.SqlTestOne,
            "record mode lists what passed so a reviewer can see a false pass; it must not turn it into a success");
    }

    [Fact]
    public void Record_mode_cannot_hide_an_infrastructure_failure_behind_exit_2()
    {
        var run = _scenarios.Run("record-no-trx");

        run.ExitCode.Should().Be(1, run.Output);
        run.Verdict.Should().Be("FAIL");
    }

    [Fact]
    public void Record_is_refused_in_CI_before_anything_runs()
    {
        var run = _scenarios.Run("record-in-ci");

        run.ExitCode.Should().Be(1, run.Output);
        run.Output.Should().Contain("REFUSED").And.Contain("-Record");
        run.ReportOrNull.Should().BeNull("the script must stop before it writes evidence that could be mistaken for a result");
        File.Exists(Path.Combine(run.Dir, "fake", "calls.log")).Should().BeFalse("dotnet must never be invoked");
    }

    [Fact]
    public void A_substitute_dotnet_is_refused_in_CI()
    {
        var run = _scenarios.Run("dotnet-path-in-ci");

        run.ExitCode.Should().Be(1, run.Output);
        run.Output.Should().Contain("REFUSED").And.Contain("-DotnetPath");
        File.Exists(Path.Combine(run.Dir, "fake", "calls.log")).Should().BeFalse();
    }

    // ---- provenance: source and build must correspond -------------------------------------------------------------------------

    [Fact]
    public void The_report_identifies_the_commit_the_assembly_the_configuration_the_sdk_and_how_the_build_was_obtained()
    {
        var run = _scenarios.Run("pass");
        var provenance = run.Report.GetProperty("provenance");

        provenance.GetProperty("repositoryHeadFull").GetString().Should().Be(run.Head);
        provenance.GetProperty("repositoryTree").GetString().Should().Be(run.Tree);
        provenance.GetProperty("trackedChangesFromHead").GetArrayLength().Should().Be(0);
        provenance.GetProperty("buildMode").GetString().Should().Be("reused-existing-build");
        provenance.GetProperty("configuration").GetString().Should().Be("Release");
        provenance.GetProperty("sdkVersion").GetString().Should().Be("10.0.100");
        provenance.GetProperty("testAssemblySha256").GetString().Should().Be(run.AssemblySha256);
        provenance.GetProperty("testAssemblyPath").GetString().Should().Be(run.AssemblyPath);
        provenance.GetProperty("assemblyNewerThanEveryBuildInput").GetBoolean().Should().BeTrue();
        provenance.GetProperty("identity").GetString().Should()
            .Contain(run.Head).And.Contain(run.Tree).And.Contain(run.AssemblySha256).And.Contain("configuration=Release").And.Contain("sdk=10.0.100");
        run.Report.GetProperty("scriptSha256").GetString().Should().Be(run.ScriptSha256, "the report names the exact script that produced it");
    }

    [Fact]
    public void A_script_that_builds_records_that_it_did_and_builds_before_it_tests()
    {
        var run = _scenarios.Run("built-by-script");

        run.ExitCode.Should().Be(0, run.Output);
        run.Report.GetProperty("provenance").GetProperty("buildMode").GetString().Should().Be("built-by-script");
        var calls = File.ReadAllLines(Path.Combine(run.Dir, "fake", "calls.log"));
        calls.Select(c => c.Split(' ')[0]).Should().ContainInOrder("build", "--version", "test", "test");
    }

    [Fact]
    public void A_failed_build_is_a_failed_run()
    {
        var run = _scenarios.Run("build-fails");

        run.ExitCode.Should().Be(1, run.Output);
        run.Verdict.Should().Be("FAIL");
        run.Violations.Should().Contain(v => v.Contains("dotnet build failed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_dirty_tracked_source_tree_cannot_produce_a_pass()
    {
        var run = _scenarios.Run("dirty-tracked");

        run.ExitCode.Should().Be(1, run.Output);
        run.Violations.Should().Contain(v => v.Contains("working tree is dirty", StringComparison.Ordinal));
        run.Report.GetProperty("provenance").GetProperty("trackedChangesFromHead").GetArrayLength().Should().Be(1);
        run.Report.GetProperty("provenance").GetProperty("assemblyNewerThanEveryBuildInput").GetBoolean().Should().BeTrue(
            "only dirtiness is wrong here, so the dirty check alone must catch it");
    }

    [Fact]
    public void A_binary_older_than_the_source_it_is_said_to_describe_cannot_produce_a_pass()
    {
        var run = _scenarios.Run("stale-build");

        run.ExitCode.Should().Be(1, run.Output);
        run.Violations.Should().Contain(v => v.Contains("reused build is stale", StringComparison.Ordinal));
        run.Report.GetProperty("provenance").GetProperty("trackedChangesFromHead").GetArrayLength().Should().Be(0,
            "the tree is clean here, so only the build age check can catch it");
        run.Report.GetProperty("provenance").GetProperty("assemblyNewerThanEveryBuildInput").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void An_untracked_source_file_that_the_build_would_compile_cannot_produce_a_pass()
    {
        var run = _scenarios.Run("untracked-source");

        run.ExitCode.Should().Be(1, run.Output);
        run.Violations.Should().Contain(v => v.Contains("untracked files would be compiled", StringComparison.Ordinal));
    }

    [Fact]
    public void An_untracked_log_or_report_file_does_not_count_as_source()
    {
        var run = _scenarios.Run("untracked-log");

        run.ExitCode.Should().Be(0, run.Output);
    }

    [Fact]
    public void A_test_assembly_from_another_configuration_is_rejected()
    {
        var run = _scenarios.Run("wrong-configuration");

        run.ExitCode.Should().Be(1, run.Output);
        run.Violations.Should().Contain(v => v.Contains("is not a Release build output", StringComparison.Ordinal));
    }

    [Fact]
    public void A_test_assembly_outside_the_repository_is_rejected()
    {
        var run = _scenarios.Run("assembly-outside-repository");

        run.ExitCode.Should().Be(1, run.Output);
        run.Violations.Should().Contain(v => v.Contains("is not under the repository", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_git_the_result_cannot_be_tied_to_a_commit_and_does_not_pass()
    {
        var run = _scenarios.Run("not-a-git-repository");

        run.ExitCode.Should().Be(1, run.Output);
        run.Violations.Should().Contain(v => v.Contains("source identity cannot be established", StringComparison.Ordinal));
    }

    // ---- fresh results ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_stale_passing_report_and_TRX_cannot_conceal_a_run_that_wrote_no_TRX()
    {
        var run = _scenarios.Run("stale-output");

        run.ExitCode.Should().Be(1, run.Output);
        run.Verdict.Should().Be("FAIL", "the stale report said PASS and must have been replaced");
        run.Violations.Should().Contain(v => v.Contains("wrote no TRX", StringComparison.Ordinal));
        File.Exists(Path.Combine(run.Out, "sql-honesty.trx")).Should().BeFalse("the previous TRX is script-owned and must be cleared before the run");
        File.ReadAllText(Path.Combine(run.Out, "keep-me.txt")).Should().Be("not the script's file", "only the script's own files are ever removed");
        Directory.Exists(Path.Combine(run.Out, "keep-me-directory")).Should().BeTrue("nothing is deleted recursively");
    }

    [Fact]
    public void A_TRX_that_predates_the_run_is_rejected()
    {
        var run = _scenarios.Run("old-trx");

        run.ExitCode.Should().Be(1, run.Output);
        run.Violations.Should().Contain(v => v.Contains("older than this run", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_TRX_fails_and_still_leaves_a_report()
    {
        var run = _scenarios.Run("no-trx");

        run.ExitCode.Should().Be(1, run.Output);
        run.Verdict.Should().Be("FAIL");
        run.Violations.Should().Contain(v => v.Contains("wrote no TRX", StringComparison.Ordinal));
    }

    [Fact]
    public void A_malformed_TRX_fails()
    {
        var run = _scenarios.Run("malformed-trx");

        run.ExitCode.Should().Be(1, run.Output);
        run.Violations.Should().Contain(v => v.Contains("malformed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_user_supplied_output_directory_is_not_deleted_and_a_default_one_is_fresh_under_the_repository()
    {
        var run = _scenarios.Run("default-output");

        run.ExitCode.Should().Be(0, run.Output);
        var reports = Directory.GetFiles(Path.Combine(run.Repo, "artifacts", "sql-execution-honesty"), "sql-honesty-report.json", SearchOption.AllDirectories);
        reports.Should().ContainSingle();
        Path.GetFileName(Path.GetDirectoryName(reports[0])!).Should().MatchRegex(@"^\d{8}T\d{6}Z$");
    }

    // ---- the test run itself ------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("exit-0-despite-failures", 0)]
    [InlineData("exit-crash", 137)]
    public void An_unexpected_test_process_exit_code_fails_the_run(string id, int exitCode)
    {
        var run = _scenarios.Run(id);

        run.ExitCode.Should().Be(1, run.Output);
        run.Violations.Should().Contain(v => v.Contains($"dotnet test exited {exitCode}", StringComparison.Ordinal));
    }

    [Fact]
    public void A_failed_discovery_with_no_output_fails_the_run()
    {
        var run = _scenarios.Run("discovery-fails");

        run.ExitCode.Should().Be(1, run.Output);
        run.Violations.Should().Contain(v => v.Contains("test discovery failed", StringComparison.Ordinal));
        run.Violations.Should().Contain(v => v.Contains("reviewed floor", StringComparison.Ordinal));
    }

    [Fact]
    public void A_test_that_was_discovered_but_not_executed_fails_the_run()
    {
        var run = _scenarios.Run("discovered-not-executed");

        run.ExitCode.Should().Be(1, run.Output);
        run.Violations.Should().Contain(v => v.Contains("discovery found 5 tests but the run executed 4", StringComparison.Ordinal));
        run.Strings("discoveredNotExecuted").Should().ContainSingle().Which.Should().Contain("Extra_test_that_never_ran");
    }

    [Fact]
    public void A_skipped_or_not_executed_test_fails_the_run()
    {
        var run = _scenarios.Run("skipped");

        run.ExitCode.Should().Be(1, run.Output);
        run.Violations.Should().Contain(v => v.Contains("neither passed nor failed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_must_fail_test_that_fails_for_another_reason_fails_the_run()
    {
        var run = _scenarios.Run("must-fail-wrong-reason");

        run.ExitCode.Should().Be(1, run.Output);
        run.Violations.Should().Contain(v => v.Contains("must-fail tests did not fail as an unavailable SQL Server", StringComparison.Ordinal));
    }

    // ---- isolation from live SQL credentials ----------------------------------------------------------------------------------------

    [Fact]
    public void The_test_process_never_sees_live_SQL_settings_and_gets_only_the_unreachable_placeholders()
    {
        var run = _scenarios.Run("credential-isolation");
        run.ExitCode.Should().Be(0, run.Output);

        var seen = File.ReadAllText(Path.Combine(run.Dir, "fake", "env-test.txt"));
        seen.Should().NotContain(Scenarios.LiveMarker, "no live password may reach the test process");
        seen.Should().NotContain("MSSQL_").And.NotContain("ConnectionStrings__").And.NotContain("APPERTURE_CI_MSSQL");
        seen.Should().NotContain("APPERTURE_TEST_SFTP", "every APPERTURE_ variable is removed");
        seen.Should().Contain("APPERTURE_TEST_SQL_PROCESS_ENV_ONLY=1").And.Contain("203.0.113.7").And.Contain("placeholder-not-a-credential");
        run.Output.Should().NotContain(Scenarios.LiveMarker);
        File.ReadAllText(Path.Combine(run.Out, "sql-honesty-report.json")).Should().NotContain(Scenarios.LiveMarker);
    }

    // ---- negative control: the decisive check -----------------------------------------------------------------------------------

    [Fact]
    public void A_copy_of_the_script_without_the_unexpected_pass_check_would_let_the_false_pass_through_so_the_scenario_is_what_detects_it()
    {
        var mutant = _scenarios.Run("mutant-without-unexpected-pass-check");

        mutant.ExitCode.Should().Be(0, "the mutant must be accepted by the scenario, which shows the scenario discriminates; "
            + "the real script is refused in A_test_that_passes_without_being_on_the_allow_list_fails_the_run");
        mutant.Strings("unexpectedPasses").Should().ContainSingle().Which.Should().Be(Scenarios.NewSqlTest, "the evidence of the false pass is still collected");
        _scenarios.Run("unexpected-pass").ExitCode.Should().Be(1, "the same scenario is refused by the real script");
    }

    // ============================================================================================================================

    public sealed class Scenarios : IDisposable
    {
        public const string NonSqlTest = "Apperture.IntegrationTests.NonSql.Passes_without_sql";
        public const string SqlTestOne = "Apperture.IntegrationTests.Sql.Needs_sql_one";
        public const string SqlTestTwo = "Apperture.IntegrationTests.Sql.Needs_sql_two";
        public const string OtherTest = "Apperture.IntegrationTests.Sql.Other_failing";
        public const string NewSqlTest = "Apperture.IntegrationTests.Sql.New_test_that_silently_returns";
        public const string LiveMarker = "live-password-marker-9f2c";

        private const string Unavailable = "Apperture.IntegrationTests.SqlTestUnavailableException: This test cannot run against SQL Server: the target is unreachable.";

        private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly string _root = Path.Combine(Path.GetTempPath(), "apperture-honesty-script-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, ScenarioRun> _runs = new();

        public Scenarios()
        {
            var realScript = Path.Combine(RepoRoot(), "scripts", "dev", "Test-SqlExecutionHonesty.ps1");
            File.Exists(realScript).Should().BeTrue();
            Directory.CreateDirectory(_root);

            var definitions = Definitions(File.ReadAllText(realScript));
            var results = new System.Collections.Concurrent.ConcurrentDictionary<string, ScenarioRun>();
            Parallel.ForEach(definitions, new ParallelOptions { MaxDegreeOfParallelism = 4 }, definition => results[definition.Id] = Execute(definition));
            foreach (var pair in results)
                _runs[pair.Key] = pair.Value;
        }

        public ScenarioRun Run(string id) => _runs[id];

        public void Dispose()
        {
            try
            {
                // git writes its object files read-only, which Windows will not delete.
                foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);

                Directory.Delete(_root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // best effort; the folder is in the OS temp directory
            }
        }

        // ---- scenario definitions ---------------------------------------------------------------------------------------------

        private static List<Definition> Definitions(string script)
        {
            var list = new List<Definition>();

            Definition Add(string id, Action<Definition>? configure = null)
            {
                var definition = new Definition(id, script);
                configure?.Invoke(definition);
                list.Add(definition);
                return definition;
            }

            Add("pass");
            Add("unexpected-pass", AddSilentlyReturningTest);
            Add("must-fail-passed", d => d.Fake.Results.Single(r => r.Name == SqlTestOne).Outcome = "Passed");
            Add("record", d =>
            {
                d.Arguments.Add("-Record");
                d.Fake.Results.Single(r => r.Name == SqlTestOne).Outcome = "Passed";
            });
            Add("record-no-trx", d =>
            {
                d.Arguments.Add("-Record");
                d.Fake.Trx = "none";
            });
            Add("record-in-ci", d =>
            {
                d.Arguments.Add("-Record");
                d.Environment["GITHUB_ACTIONS"] = "true";
            });
            Add("dotnet-path-in-ci", d => d.Environment["CI"] = "true");
            Add("built-by-script", d => d.NoBuild = false);
            Add("build-fails", d =>
            {
                d.NoBuild = false;
                d.Fake.BuildExit = 1;
            });
            Add("dirty-tracked", d => d.AfterBuild = repo =>
            {
                File.AppendAllText(repo.Path("src/Lib/Lib.cs"), "// changed after the build\n");
                File.SetLastWriteTimeUtc(repo.Path("src/Lib/Lib.cs"), repo.SourceTime);
            });
            Add("stale-build", d => d.AfterBuild = repo => File.SetLastWriteTimeUtc(repo.Path("src/Lib/Lib.cs"), repo.BuildTime.AddMinutes(5)));
            Add("untracked-source", d => d.AfterBuild = repo => File.WriteAllText(repo.Path("src/Lib/New.cs"), "class New {}\n"));
            Add("untracked-log", d => d.AfterBuild = repo => File.WriteAllText(repo.Path("notes.log"), "scratch\n"));
            Add("wrong-configuration", d => d.AssemblyConfiguration = "Debug");
            Add("assembly-outside-repository", d => d.AssemblyOutsideRepository = true);
            Add("not-a-git-repository", d => d.NoGit = true);
            Add("stale-output", d =>
            {
                d.Fake.Trx = "none";
                d.BeforeRun = repo =>
                {
                    File.WriteAllText(Path.Combine(repo.Out, "sql-honesty-report.json"), "{ \"verdict\": \"PASS\", \"exitCode\": 0 }");
                    File.WriteAllText(Path.Combine(repo.Out, "sql-honesty.trx"), "<TestRun>stale</TestRun>");
                    File.WriteAllText(Path.Combine(repo.Out, "keep-me.txt"), "not the script's file");
                    Directory.CreateDirectory(Path.Combine(repo.Out, "keep-me-directory"));
                    File.WriteAllText(Path.Combine(repo.Out, "keep-me-directory", "inner.txt"), "inner");
                };
            });
            Add("old-trx", d => d.Fake.TrxAgeDays = 3);
            Add("no-trx", d => d.Fake.Trx = "none");
            Add("malformed-trx", d => d.Fake.Trx = "malformed");
            Add("default-output", d => d.DefaultOutput = true);
            Add("exit-0-despite-failures", d => d.Fake.TestExit = 0);
            Add("exit-crash", d => d.Fake.TestExit = 137);
            Add("discovery-fails", d =>
            {
                d.Fake.ListExit = 1;
                d.Fake.Discovery.Clear();
            });
            Add("discovered-not-executed", d => d.Fake.Discovery.Add("Apperture.IntegrationTests.Sql.Extra_test_that_never_ran"));
            Add("skipped", d => d.Fake.Results.Single(r => r.Name == OtherTest).Outcome = "NotExecuted");
            Add("must-fail-wrong-reason", d => d.Fake.Results.Single(r => r.Name == SqlTestTwo).Message = "System.NullReferenceException: Object reference not set.");
            Add("credential-isolation", d =>
            {
                d.Environment["MSSQL_SA_PASSWORD"] = LiveMarker;
                d.Environment["MSSQL_TEST_SERVER"] = "127.0.0.1,1433";
                d.Environment["APPERTURE_CI_MSSQL_SA_PASSWORD"] = LiveMarker;
                d.Environment["APPERTURE_TEST_SQL"] = $"Server=127.0.0.1,1433;User Id=sa;Password={LiveMarker};";
                d.Environment["APPERTURE_TEST_SQL_CONNECTION_STRING"] = $"Server=127.0.0.1,1433;User Id=sa;Password={LiveMarker};";
                d.Environment["APPERTURE_TEST_SFTP_PASSWORD"] = LiveMarker;
                d.Environment["ConnectionStrings__Default"] = $"Server=.;Password={LiveMarker};";
            });
            Add("mutant-without-unexpected-pass-check", d =>
            {
                AddSilentlyReturningTest(d);
                d.TransformScript = text =>
                {
                    var mutated = Regex.Replace(text, @"^[ \t]*if \(\$unexpectedPasses\.Count -gt 0\) \{ Add-Violation[^\n]*\n", string.Empty, RegexOptions.Multiline);
                    mutated.Should().NotBe(text, "the mutation must remove the decisive line");
                    return mutated;
                };
            });

            return list;
        }

        // A new SQL test that returns silently: it passes, and it is on neither list. Only the unexpected-pass check can see it.
        private static void AddSilentlyReturningTest(Definition d)
        {
            d.Fake.Discovery.Add(NewSqlTest);
            d.Fake.Results.Add(new FakeResult { Name = NewSqlTest, Outcome = "Passed" });
        }

        // ---- running one scenario -----------------------------------------------------------------------------------------------

        private ScenarioRun Execute(Definition definition)
        {
            var dir = Path.Combine(_root, definition.Id);
            var repo = Path.Combine(dir, "repo");
            var fake = Path.Combine(dir, "fake");
            var outDir = Path.Combine(dir, "out");
            Directory.CreateDirectory(fake);
            Directory.CreateDirectory(outDir);

            var script = definition.TransformScript?.Invoke(definition.Script) ?? definition.Script;
            var scriptPath = Path.Combine(repo, "scripts", "dev", "Test-SqlExecutionHonesty.ps1");
            WriteFile(Path.Combine(repo, "Apperture.slnx"), "<Solution />\n");
            WriteFile(scriptPath, script);
            WriteFile(Path.Combine(repo, "tests", "Apperture.IntegrationTests", "Apperture.IntegrationTests.csproj"), "<Project />\n");
            WriteFile(Path.Combine(repo, "src", "Lib", "Lib.cs"), "class Lib {}\n");
            WriteFile(Path.Combine(repo, ".gitignore"), "bin/\nobj/\nartifacts/\n");
            var lists = Path.Combine(repo, "tests", "Apperture.IntegrationTests", "SqlExecutionHonesty");
            WriteFile(Path.Combine(lists, "approved-non-sql-passes.tsv"),
                "# assembly: Apperture.IntegrationTests.dll\n# minimum-discovered: 4\n# minimum-failed: 2\n"
                + $"{NonSqlTest}\tunit\t-\tneeds no SQL Server\n");
            WriteFile(Path.Combine(lists, "required-unavailable-failures.tsv"),
                $"{SqlTestOne}\tformer-soft-skip\t-\tneeds SQL Server\n{SqlTestTwo}\tformer-soft-skip\t-\tneeds SQL Server\n");

            string head = string.Empty, tree = string.Empty;
            if (!definition.NoGit)
            {
                Git(repo, "init", "-q", "-b", "main");
                Git(repo, "add", "-A");
                Git(repo, "-c", "user.name=honesty", "-c", "user.email=honesty@example.invalid", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "fixture");
                head = Git(repo, "rev-parse", "HEAD").Trim();
                tree = Git(repo, "rev-parse", "HEAD^{tree}").Trim();
            }

            // A binary built after every source file was written (git checkout, then dotnet build).
            var now = DateTime.UtcNow;
            var layout = new Layout(repo, outDir, now.AddMinutes(-10), now);
            foreach (var source in new[] { "src/Lib/Lib.cs", "tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj", "Apperture.slnx" })
                File.SetLastWriteTimeUtc(layout.Path(source), layout.SourceTime);

            var configuration = definition.AssemblyConfiguration;
            var assembly = definition.AssemblyOutsideRepository
                ? Path.Combine(dir, "elsewhere", "Release", "Apperture.IntegrationTests.dll")
                : Path.Combine(repo, "tests", "Apperture.IntegrationTests", "bin", configuration, "net10.0", "Apperture.IntegrationTests.dll");
            WriteFile(assembly, "fake test assembly " + definition.Id);
            File.SetLastWriteTimeUtc(assembly, layout.BuildTime);
            definition.AfterBuild?.Invoke(layout);
            definition.BeforeRun?.Invoke(layout);

            definition.Fake.AssemblyPath = assembly;
            File.WriteAllText(Path.Combine(fake, "scenario.json"), JsonSerializer.Serialize(definition.Fake, Json));
            File.WriteAllText(Path.Combine(fake, "dotnet.ps1"), FakeDotnet);

            var arguments = new List<string> { "-NoProfile", "-NonInteractive", "-File", scriptPath, "-Configuration", "Release" };
            if (definition.NoBuild)
                arguments.Add("-NoBuild");
            if (!definition.DefaultOutput)
                arguments.AddRange(["-OutputDirectory", outDir]);
            arguments.AddRange(["-DotnetPath", Path.Combine(fake, "dotnet.ps1")]);
            arguments.AddRange(definition.Arguments);

            var (exitCode, output) = RunPowerShell(arguments, definition.Environment);

            var artifacts = Path.Combine(repo, "artifacts");
            var reportPath = definition.DefaultOutput
                ? Directory.Exists(artifacts) ? Directory.GetFiles(artifacts, "sql-honesty-report.json", SearchOption.AllDirectories).FirstOrDefault() : null
                : Path.Combine(outDir, "sql-honesty-report.json");
            JsonElement? report = null;
            if (reportPath is not null && File.Exists(reportPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
                report = document.RootElement.Clone();
            }

            return new ScenarioRun(
                exitCode, output, report, dir, repo, outDir, head, tree,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly))), Path.GetFullPath(assembly),
                Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(script.Replace("\r\n", "\n", StringComparison.Ordinal)))));
        }

        private static (int ExitCode, string Output) RunPowerShell(IEnumerable<string> arguments, Dictionary<string, string> environment)
        {
            var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);

            // The scenarios must not inherit the CI markers that make the script refuse -Record, or the machine's own SQL settings.
            foreach (var name in new[] { "GITHUB_ACTIONS", "CI" })
                start.Environment.Remove(name);
            foreach (var name in start.Environment.Keys.Where(k => k.StartsWith("APPERTURE_", StringComparison.OrdinalIgnoreCase)
                         || k.StartsWith("MSSQL_", StringComparison.OrdinalIgnoreCase)
                         || k.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase)).ToList())
                start.Environment.Remove(name);
            foreach (var (name, value) in environment)
                start.Environment[name] = value;

            Process process;
            try
            {
                process = Process.Start(start)!;
            }
            catch (Win32Exception ex)
            {
                throw new InvalidOperationException("PowerShell 7 (pwsh) must be on PATH to run the development-script tests.", ex);
            }

            using (process)
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(milliseconds: 120_000))
                {
                    process.Kill(entireProcessTree: true);
                    throw new TimeoutException("pwsh did not finish an honesty-script scenario within 120 s.");
                }

                return (process.ExitCode, stdout.Result + stderr.Result);
            }
        }

        private static string Git(string repo, params string[] arguments)
        {
            var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = repo };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);

            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit(60_000).Should().BeTrue("git must finish");
            process.ExitCode.Should().Be(0, $"git {string.Join(' ', arguments)}: {stderr.Result}");
            return stdout.Result;
        }

        private static void WriteFile(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, Utf8);
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

        // ---- scenario model -----------------------------------------------------------------------------------------------------

        private sealed class Definition(string id, string script)
        {
            public string Id { get; } = id;
            public string Script { get; } = script;
            public FakeRun Fake { get; } = FakeRun.Baseline();
            public List<string> Arguments { get; } = [];
            public Dictionary<string, string> Environment { get; } = [];
            public bool NoBuild { get; set; } = true;
            public bool DefaultOutput { get; set; }
            public bool NoGit { get; set; }
            public bool AssemblyOutsideRepository { get; set; }
            public string AssemblyConfiguration { get; set; } = "Release";
            public Action<Layout>? AfterBuild { get; set; }
            public Action<Layout>? BeforeRun { get; set; }
            public Func<string, string>? TransformScript { get; set; }
        }

        private sealed class Layout(string repo, string outDirectory, DateTime sourceTime, DateTime buildTime)
        {
            public string Repo { get; } = repo;
            public string Out { get; } = outDirectory;
            public DateTime SourceTime { get; } = sourceTime;
            public DateTime BuildTime { get; } = buildTime;

            public string Path(string relative) => System.IO.Path.Combine(Repo, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        }

        public sealed class FakeRun
        {
            public int BuildExit { get; set; }
            public int ListExit { get; set; }
            public int TestExit { get; set; } = 1;
            public string Trx { get; set; } = "write";
            public int TrxAgeDays { get; set; }
            public string AssemblyPath { get; set; } = string.Empty;
            public List<string> Discovery { get; set; } = [];
            public List<FakeResult> Results { get; set; } = [];

            /// <summary>Four tests: one non-SQL pass on the allow-list, two must-fail tests, one other failing test.</summary>
            public static FakeRun Baseline() => new()
            {
                Discovery = [NonSqlTest, SqlTestOne, SqlTestTwo, OtherTest],
                Results =
                [
                    new FakeResult { Name = NonSqlTest, Outcome = "Passed" },
                    new FakeResult { Name = SqlTestOne, Outcome = "Failed", Message = Unavailable },
                    new FakeResult { Name = SqlTestTwo, Outcome = "Failed", Message = Unavailable },
                    new FakeResult { Name = OtherTest, Outcome = "Failed", Message = "Microsoft.Data.SqlClient.SqlException: Connection failed (network-related)." },
                ],
            };
        }

        public sealed class FakeResult
        {
            public string Name { get; set; } = string.Empty;
            public string Outcome { get; set; } = "Passed";
            public string Message { get; set; } = string.Empty;
        }

        public sealed record ScenarioRun(
            int ExitCode, string Output, JsonElement? ReportOrNull, string Dir, string Repo, string Out, string Head, string Tree,
            string AssemblySha256, string AssemblyPath, string ScriptSha256)
        {
            public JsonElement Report => ReportOrNull ?? throw new InvalidOperationException("The scenario wrote no report. Output: " + Output);

            public string Verdict => Report.GetProperty("verdict").GetString()!;

            public IReadOnlyList<string> Violations => Strings("violations");

            public IReadOnlyList<string> Strings(string property) =>
                Report.GetProperty(property).EnumerateArray().Select(e => e.GetString()!).ToList();
        }

        /// <summary>
        /// Stands in for <c>dotnet</c>: <c>build</c> exits as told, <c>--version</c> answers, <c>test --list-tests</c> prints the scenario's
        /// discovery, and <c>test</c> writes the scenario's TRX (or none, or a broken one) and exits as told. It records its calls and the
        /// environment the test process would have seen.
        /// </summary>
        private const string FakeDotnet = """
            $ErrorActionPreference = 'Stop'
            $scenario = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'scenario.json') -Raw | ConvertFrom-Json
            $a = @($args)
            Add-Content -LiteralPath (Join-Path $PSScriptRoot 'calls.log') -Value ($a -join ' ')
            if ($a[0] -eq '--version') { '10.0.100'; exit 0 }
            if ($a[0] -eq 'build') { 'fake build'; exit ([int]$scenario.buildExit) }
            if ($a[0] -eq 'test' -and ($a -contains '--list-tests')) {
                if ($scenario.discovery.Count -gt 0) {
                    "Test run for $($scenario.assemblyPath) (.NETCoreApp,Version=v10.0)"
                    'The following Tests are available:'
                    foreach ($n in $scenario.discovery) { "    $n" }
                }
                exit ([int]$scenario.listExit)
            }
            if ($a[0] -eq 'test') {
                Get-ChildItem Env: | ForEach-Object { "$($_.Name)=$($_.Value)" } | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'env-test.txt')
                $dir = $a[[array]::IndexOf($a, '--results-directory') + 1]
                $file = ($a[[array]::IndexOf($a, '--logger') + 1] -split 'LogFileName=')[1]
                $path = Join-Path $dir $file
                if ($scenario.trx -eq 'malformed') { Set-Content -LiteralPath $path -Value '<TestRun><Results><UnitTestResult testName=' }
                elseif ($scenario.trx -eq 'write') {
                    $sb = [System.Text.StringBuilder]::new()
                    [void]$sb.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
                    [void]$sb.AppendLine('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>')
                    $passed = 0; $failed = 0; $executed = 0
                    foreach ($r in $scenario.results) {
                        $n = [System.Security.SecurityElement]::Escape($r.name)
                        if ($r.outcome -eq 'Failed') {
                            $failed++; $executed++
                            $m = [System.Security.SecurityElement]::Escape($r.message)
                            [void]$sb.AppendLine("<UnitTestResult testName=`"$n`" outcome=`"Failed`"><Output><ErrorInfo><Message>$m</Message></ErrorInfo></Output></UnitTestResult>")
                        }
                        else {
                            if ($r.outcome -eq 'Passed') { $passed++; $executed++ }
                            [void]$sb.AppendLine("<UnitTestResult testName=`"$n`" outcome=`"$($r.outcome)`" />")
                        }
                    }
                    $total = @($scenario.results).Count
                    [void]$sb.AppendLine('</Results>')
                    [void]$sb.AppendLine("<TestDefinitions><UnitTest name=`"x`"><TestMethod codeBase=`"$([System.Security.SecurityElement]::Escape($scenario.assemblyPath))`" /></UnitTest></TestDefinitions>")
                    [void]$sb.AppendLine("<ResultSummary><Counters total=`"$total`" executed=`"$executed`" passed=`"$passed`" failed=`"$failed`" /></ResultSummary></TestRun>")
                    [System.IO.File]::WriteAllText($path, $sb.ToString())
                    if ($scenario.trxAgeDays -gt 0) { (Get-Item -LiteralPath $path).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-$scenario.trxAgeDays) }
                }
                exit ([int]$scenario.testExit)
            }
            exit 99
            """;
    }
}
