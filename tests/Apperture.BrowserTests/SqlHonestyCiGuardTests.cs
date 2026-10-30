using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.BrowserTests;

/// <summary>
/// DEV-1.1C: the SQL execution-honesty check (<c>scripts/dev/Test-SqlExecutionHonesty.ps1</c>) is part of the required
/// <c>build-and-test</c> job, after the Release build and before the ordinary tests, in the one invocation that can pass. These pin
/// that invocation and its surroundings, and each weakening a reviewer might not notice is shown to be detected on a mutated copy of
/// the real workflow. They read <c>.github/workflows/ci.yml</c> only; like <see cref="BrowserSecurityCiGuardTests"/> they run in a plain
/// <c>dotnet test Apperture.slnx</c>.
/// </summary>
public sealed class SqlHonestyCiGuardTests
{
    [Fact]
    public void Ci_runs_the_honesty_check_in_the_required_job_after_the_Release_build_and_before_the_tests()
    {
        var problems = SqlHonestyCiInvocation.Verify(Workflow());

        problems.Should().BeEmpty("the honesty invocation, its place and its neighbours are pinned: " + string.Join("; ", problems));
    }

    [Theory]
    [MemberData(nameof(Weakenings))]
    public void Each_weakening_of_the_honesty_invocation_is_detected(string name, Func<string, string> weaken)
    {
        var original = Workflow();
        var weakened = weaken(original);

        weakened.Should().NotBe(original, $"the mutation '{name}' must change the workflow");
        SqlHonestyCiInvocation.Verify(weakened).Should().NotBeEmpty($"'{name}' must be detected");
    }

    [Fact]
    public void A_comment_that_mentions_a_forbidden_flag_is_not_a_violation_and_a_commented_out_step_is_not_a_step()
    {
        var yaml = Workflow();

        SqlHonestyCiInvocation.Verify(yaml + "\n      # -Record and -DotnetPath are refused\n").Should().BeEmpty();
        SqlHonestyCiInvocation.Verify(CommentOutStep(yaml, "SQL execution honesty")).Should().NotBeEmpty();
    }

    public static TheoryData<string, Func<string, string>> Weakenings() => new()
    {
        { "the step is removed", y => RemoveStep(y, "SQL execution honesty") },
        { "the step is commented out", y => CommentOutStep(y, "SQL execution honesty") },
        { "the step runs after the tests", y => MoveStepAfter(y, "SQL execution honesty", "Test") },
        { "the step runs before the build", y => MoveStepBefore(y, "SQL execution honesty", "Build") },
        { "-Record is added", y => y.Replace("-NoBuild -OutputDirectory", "-NoBuild -Record -OutputDirectory", StringComparison.Ordinal) },
        { "a substitute dotnet is passed", y => y.Replace("-NoBuild -OutputDirectory", "-NoBuild -DotnetPath ./fake -OutputDirectory", StringComparison.Ordinal) },
        { "the configuration changes to Debug", y => y.Replace("Test-SqlExecutionHonesty.ps1 -Configuration Release", "Test-SqlExecutionHonesty.ps1 -Configuration Debug", StringComparison.Ordinal) },
        { "-NoBuild is dropped", y => y.Replace(" -NoBuild -OutputDirectory", " -OutputDirectory", StringComparison.Ordinal) },
        { "the output directory moves", y => y.Replace("-OutputDirectory artifacts/sql-execution-honesty", "-OutputDirectory artifacts/elsewhere", StringComparison.Ordinal) },
        { "another script is run", y => y.Replace("./scripts/dev/Test-SqlExecutionHonesty.ps1", "./scripts/dev/Test-Something.ps1", StringComparison.Ordinal) },
        { "failure is swallowed with || true", y => y.Replace("-OutputDirectory artifacts/sql-execution-honesty\n", "-OutputDirectory artifacts/sql-execution-honesty || true\n", StringComparison.Ordinal) },
        { "continue-on-error is set on the step", y => y.Replace("        timeout-minutes: 10\n", "        timeout-minutes: 10\n        continue-on-error: true\n", StringComparison.Ordinal) },
        { "the step is made conditional", y => y.Replace("        id: sql-honesty\n", "        id: sql-honesty\n        if: always() && false\n", StringComparison.Ordinal) },
        { "the timeout is removed", y => y.Replace("        timeout-minutes: 10\n        run: ./scripts/dev/Test-SqlExecutionHonesty.ps1", "        run: ./scripts/dev/Test-SqlExecutionHonesty.ps1", StringComparison.Ordinal) },
        { "the shell changes", y => y.Replace("        shell: pwsh\n        timeout-minutes: 10", "        shell: bash\n        timeout-minutes: 10", StringComparison.Ordinal) },
        { "a live SQL setting is given to the step", y => y.Replace("        id: sql-honesty\n", "        id: sql-honesty\n        env:\n          APPERTURE_TEST_SQL: Server=127.0.0.1,1433;User Id=sa;\n", StringComparison.Ordinal) },
        { "the retired switch is set", y => y.Replace("      APPERTURE_TEST_SFTP_HOST:", "      APPERTURE_REQUIRE_SQL_TESTS: \"1\"\n      APPERTURE_TEST_SFTP_HOST:", StringComparison.Ordinal) },
        { "the step id is dropped", y => y.Replace("        id: sql-honesty\n", string.Empty, StringComparison.Ordinal) },
        { "the report upload runs on any failure", y => y.Replace("if: failure() && steps.sql-honesty.outcome == 'failure'", "if: failure()", StringComparison.Ordinal) },
        { "the report upload also takes the logs", y => y.Replace("path: artifacts/sql-execution-honesty/sql-honesty-report.json", "path: artifacts/sql-execution-honesty", StringComparison.Ordinal) },
        { "the report upload is removed", y => RemoveStep(y, "Upload SQL execution honesty report") },
        { "the required job is renamed", y => y.Replace("\n  build-and-test:\n", "\n  build:\n", StringComparison.Ordinal) },
        { "the required job gets a display name", y => y.Replace("\n  build-and-test:\n    runs-on:", "\n  build-and-test:\n    name: build\n    runs-on:", StringComparison.Ordinal) },
        { "the browser-security job is removed", y => y.Replace("\n  browser-security:\n", "\n  browser-security-disabled:\n", StringComparison.Ordinal) },
        { "the ordinary tests no longer run against the CI SQL service", y => y.Replace("APPERTURE_TEST_SQL: ${{", "APPERTURE_UNUSED: ${{", StringComparison.Ordinal) },
        { "the ordinary test step is removed", y => RemoveStep(y, "Test") },
        { "the Release build is removed", y => RemoveStep(y, "Build") },
    };

    private static string RemoveStep(string yaml, string name)
    {
        var (lines, start, end) = Locate(yaml, name);
        lines.RemoveRange(start, end - start);
        return string.Join('\n', lines);
    }

    private static string CommentOutStep(string yaml, string name)
    {
        var (lines, start, end) = Locate(yaml, name);
        for (var i = start; i < end; i++)
        {
            if (lines[i].Length > 0)
                lines[i] = "# " + lines[i];
        }

        return string.Join('\n', lines);
    }

    private static string MoveStepAfter(string yaml, string name, string after)
    {
        var (lines, start, end) = Locate(yaml, name);
        var step = lines.GetRange(start, end - start);
        lines.RemoveRange(start, end - start);
        var (_, _, afterEnd) = Locate(string.Join('\n', lines), after);
        lines.InsertRange(afterEnd, step);
        return string.Join('\n', lines);
    }

    private static string MoveStepBefore(string yaml, string name, string before)
    {
        var (lines, start, end) = Locate(yaml, name);
        var step = lines.GetRange(start, end - start);
        lines.RemoveRange(start, end - start);
        var (_, beforeStart, _) = Locate(string.Join('\n', lines), before);
        lines.InsertRange(beforeStart, step);
        return string.Join('\n', lines);
    }

    /// <summary>
    /// The lines of the build-and-test step with this name: its leading comment lines, its header and body, and the blank line that
    /// separates it from the next step (the workflow has no blank line inside a step).
    /// </summary>
    private static (List<string> Lines, int Start, int End) Locate(string yaml, string name)
    {
        var lines = yaml.Split('\n').ToList();
        var jobStart = lines.FindIndex(l => l == "  build-and-test:");
        var jobEnd = lines.FindIndex(jobStart + 1, l => l.StartsWith("  browser-security:", StringComparison.Ordinal));
        var header = lines.FindIndex(jobStart + 1, l => l == $"      - name: {name}");
        header.Should().BeInRange(jobStart + 1, jobEnd - 1, $"step '{name}' must exist in build-and-test");

        var start = header;
        while (start - 1 > jobStart && lines[start - 1].TrimStart().StartsWith('#'))
            start--;

        var end = header + 1;
        while (end < lines.Count && lines[end].Length > 0)
            end++;
        if (end < lines.Count)
            end++;

        return (lines, start, end);
    }

    private static string Workflow()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Apperture.slnx")))
                return File.ReadAllText(Path.Combine(dir.FullName, ".github", "workflows", "ci.yml"));
        }

        throw new InvalidOperationException("Repository root (Apperture.slnx) not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// Reads the workflow as text, with comments removed, and lists what is wrong with the honesty invocation. Not a YAML parser: it
/// relies on the file's own two-space job and six-space step layout, so a re-indented workflow fails loudly rather than passing.
/// </summary>
internal static class SqlHonestyCiInvocation
{
    public const string Command =
        "./scripts/dev/Test-SqlExecutionHonesty.ps1 -Configuration Release -NoBuild -OutputDirectory artifacts/sql-execution-honesty";

    public static List<string> Verify(string yaml)
    {
        var problems = new List<string>();
        var text = StripComments(yaml.Replace("\r\n", "\n", StringComparison.Ordinal));

        if (text.Contains("APPERTURE_REQUIRE_SQL_TESTS", StringComparison.Ordinal))
            problems.Add("the retired APPERTURE_REQUIRE_SQL_TESTS switch is set");

        var jobs = SplitJobs(text);
        if (!jobs.TryGetValue("build-and-test", out var required))
            return [.. problems, "the required job id build-and-test is missing"];

        // The required check names: build-and-test (the job id; it must not be given another display name) and browser-security.
        if (Regex.IsMatch(required[..FirstStepOffset(required)], @"^    name:", RegexOptions.Multiline))
            problems.Add("build-and-test must keep its job id as its check name (no name: key)");
        if (!jobs.TryGetValue("browser-security", out var browser) || !Regex.IsMatch(browser, @"^    name: browser-security\s*$", RegexOptions.Multiline))
            problems.Add("the browser-security job and its check name must be preserved");
        if (!required.Contains("runs-on: ubuntu-24.04", StringComparison.Ordinal))
            problems.Add("build-and-test must keep running on ubuntu-24.04");

        var steps = SplitSteps(required);
        var build = steps.FindIndex(s => s.Text.Contains("dotnet build Apperture.slnx -c Release", StringComparison.Ordinal));
        var test = steps.FindIndex(s => s.Text.Contains("dotnet test Apperture.slnx", StringComparison.Ordinal));
        var honesty = steps.FindIndex(s => s.Name == "SQL execution honesty");
        var upload = steps.FindIndex(s => s.Name == "Upload SQL execution honesty report");

        if (build < 0)
            problems.Add("the Release build step is missing");
        if (test < 0)
            problems.Add("the ordinary dotnet test Apperture.slnx step is missing");
        else
        {
            var testStep = steps[test].Text;
            if (!testStep.Contains("dotnet test Apperture.slnx -c Release --no-build", StringComparison.Ordinal))
                problems.Add("the ordinary test step must stay a Release --no-build run of the solution");
            if (!testStep.Contains("APPERTURE_TEST_SQL:", StringComparison.Ordinal))
                problems.Add("the ordinary test step must keep running against the CI SQL service (APPERTURE_TEST_SQL)");
        }

        if (honesty < 0)
        {
            problems.Add("the 'SQL execution honesty' step is missing from build-and-test");
            return problems;
        }

        if (build >= 0 && honesty < build)
            problems.Add("the honesty step must run after the Release build");
        if (test >= 0 && honesty > test)
            problems.Add("the honesty step must run before the ordinary tests");

        var step = steps[honesty].Text;
        var run = Regex.Match(step, @"^        run: (?<c>.*)$", RegexOptions.Multiline);
        if (!run.Success || run.Groups["c"].Value.Trim() != Command)
            problems.Add($"the honesty step must run exactly: {Command}");
        if (!Regex.IsMatch(step, @"^        shell: pwsh\s*$", RegexOptions.Multiline))
            problems.Add("the honesty step must use shell: pwsh");
        if (!Regex.IsMatch(step, @"^        timeout-minutes: 10\s*$", RegexOptions.Multiline))
            problems.Add("the honesty step needs timeout-minutes: 10");
        if (!Regex.IsMatch(step, @"^        id: sql-honesty\s*$", RegexOptions.Multiline))
            problems.Add("the honesty step needs id: sql-honesty (the report upload keys on its outcome)");
        foreach (var forbidden in new[] { "continue-on-error", "||", "-Record", "-DotnetPath", "if:", "env:", "APPERTURE_TEST_SQL", "MSSQL_", "secrets." })
        {
            if (step.Contains(forbidden, StringComparison.Ordinal))
                problems.Add($"the honesty step must not contain '{forbidden}'");
        }

        if (upload < 0)
            problems.Add("the failure-only upload of the honesty report is missing");
        else
        {
            var uploadStep = steps[upload].Text;
            if (upload < honesty)
                problems.Add("the report upload must follow the honesty step");
            if (!uploadStep.Contains("if: failure() && steps.sql-honesty.outcome == 'failure'", StringComparison.Ordinal))
                problems.Add("the report upload must run only when the honesty step failed");
            if (!Regex.IsMatch(uploadStep, @"^          path: artifacts/sql-execution-honesty/sql-honesty-report\.json\s*$", RegexOptions.Multiline))
                problems.Add("the report upload must upload only sql-honesty-report.json (no logs, TRX or binaries)");
            if (uploadStep.Contains("continue-on-error", StringComparison.Ordinal))
                problems.Add("the report upload must not hide failures");
        }

        return problems;
    }

    private static string StripComments(string yaml) =>
        string.Join('\n', yaml.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    private static Dictionary<string, string> SplitJobs(string text)
    {
        var jobs = new Dictionary<string, string>(StringComparer.Ordinal);
        var jobsAt = text.IndexOf("\njobs:", StringComparison.Ordinal);
        if (jobsAt < 0)
            return jobs;

        var matches = Regex.Matches(text[jobsAt..], @"^  (?<id>[A-Za-z][\w-]*):\s*$", RegexOptions.Multiline);
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length - jobsAt;
            jobs[matches[i].Groups["id"].Value] = text[jobsAt..][start..end];
        }

        return jobs;
    }

    private static int FirstStepOffset(string job)
    {
        var at = job.IndexOf("\n    steps:", StringComparison.Ordinal);
        return at < 0 ? job.Length : at;
    }

    private static List<(string Name, string Text)> SplitSteps(string job)
    {
        var steps = new List<(string, string)>();
        var matches = Regex.Matches(job, @"^      - (?:name|uses): ?(?<n>.*)$", RegexOptions.Multiline);
        for (var i = 0; i < matches.Count; i++)
        {
            var end = i + 1 < matches.Count ? matches[i + 1].Index : job.Length;
            steps.Add((matches[i].Groups["n"].Value.Trim(), job[matches[i].Index..end]));
        }

        return steps;
    }
}
