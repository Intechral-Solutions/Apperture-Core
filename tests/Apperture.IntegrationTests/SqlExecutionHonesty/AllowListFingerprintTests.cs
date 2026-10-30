using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Apperture.IntegrationTests.SqlExecutionHonesty;

/// <summary>
/// Makes a change to the run-level honesty lists a deliberate act. <c>scripts/dev/Test-SqlExecutionHonesty.ps1</c> refuses any test
/// that passes with SQL Server unreachable unless it is on <c>approved-non-sql-passes.tsv</c>, and expects every test on
/// <c>required-unavailable-failures.tsv</c> to fail. Anyone who edits either file must also edit the pinned fingerprint below, so the
/// change appears in the diff of a second, test-source file and cannot slip through as a data edit. These tests also keep the lists
/// well formed and keep the exemptions as narrow as they were reviewed.
/// </summary>
public sealed class AllowListFingerprintTests
{
    // Reviewed on 2026-10-30 (DEV-1.1B-R; DEV-1.1C added 22 identities: the source-guard controls and these pin tests). To change a
    // list: edit it, run the honesty check in -Record mode, review every added identity (is it really a test that needs no SQL
    // Server?), then replace the value below with the "actual" shown by the failing test.
    private const string AllowListFingerprint = "42ACB9AC2BAE00A6A132577877FA675E4D3B1BCD337C1E5E3FF24D4F9FEB44EA";
    private const string MustFailFingerprint = "17D8A73D2409E78F5BEEA5CE9A64AA8ECED7B3C826799F8D1F26E940E42573E3";

    // Reviewed on 2026-10-30 (DEV-1.1C). The honesty script is the mechanism that refuses a false pass, so a change to it is as
    // consequential as a change to a list: removing one enforcement line made ten false passes produce a successful verdict. This pin
    // is a safeguard against an accidental or unreviewed regression, not a substitute for reviewing a change to the script and this
    // value together. To change the script: edit it, run its tests (UnitTests SqlExecutionHonestyScriptTests), then replace the value
    // below with the "actual" shown by the failing test.
    private const string ScriptFingerprint = "C68A82A1B5C321794B62C379A4ABF9FBE2C749D7A5588463AC701FAC13A4E0D2";

    private static readonly string[] Categories =
    [
        "guard-unit", "config-unit", "source-scan", "unit", "static-script-text", "unreachable-by-design", "wiring",
        "conditional-diagnostic", "honesty-list",
    ];

    private static readonly string[] MustFailCategories = ["former-soft-skip", "former-localdb-fallback", "scratch-create"];
    private static readonly string[] Conditions = ["-", "requires-git-directory", "benchmark-not-requested"];

    [Fact]
    public void Both_lists_are_well_formed()
    {
        foreach (var (path, categories) in new[] { (AllowListPath, Categories), (MustFailPath, MustFailCategories) })
        {
            var entries = Read(path);
            entries.Should().NotBeEmpty();
            entries.Select(e => e.Id).Should().OnlyHaveUniqueItems($"{Path.GetFileName(path)} must not list an identity twice");
            foreach (var entry in entries)
            {
                categories.Should().Contain(entry.Category, $"{entry.Id} needs a category from the reviewed set");
                Conditions.Should().Contain(entry.Condition, $"{entry.Id} may only use a reviewed condition");
                entry.Reason.Trim().Should().NotBeEmpty($"{entry.Id} needs a reason");
                entry.Id.Should().StartWith("Apperture.IntegrationTests.", "identities are TRX test names of this assembly");
            }
        }

        var header = File.ReadAllLines(AllowListPath).Where(l => l.StartsWith('#')).ToArray();
        header.Should().Contain(l => l.StartsWith("# assembly: Apperture.IntegrationTests.dll", StringComparison.Ordinal));
        header.Should().Contain(l => l.StartsWith("# minimum-discovered:", StringComparison.Ordinal));
        header.Should().Contain(l => l.StartsWith("# minimum-failed:", StringComparison.Ordinal));
    }

    [Fact]
    public void The_lists_match_their_reviewed_fingerprints()
    {
        var allow = Fingerprint(AllowListPath);
        var mustFail = Fingerprint(MustFailPath);

        allow.Should().Be(AllowListFingerprint,
            "approved-non-sql-passes.tsv changed. Review each added identity (does it truly need no SQL Server?), then update AllowListFingerprint to this actual value");
        mustFail.Should().Be(MustFailFingerprint,
            "required-unavailable-failures.tsv changed. Review the change, then update MustFailFingerprint to this actual value");
    }

    [Fact]
    public void The_honesty_script_matches_its_reviewed_fingerprint()
    {
        Fingerprint(ScriptPath()).Should().Be(ScriptFingerprint,
            "scripts/dev/Test-SqlExecutionHonesty.ps1 changed. It is the mechanism that refuses a false pass: review the change together with its tests, then update ScriptFingerprint to this actual value");
    }

    [Theory]
    [MemberData(nameof(DecisiveLines))]
    public void Removing_or_weakening_a_decisive_line_of_the_script_changes_its_fingerprint(string name, string original, string replacement)
    {
        var text = File.ReadAllText(ScriptPath()).Replace("\r\n", "\n", StringComparison.Ordinal);
        text.Should().Contain(original, $"{name}: the decisive line must be present in the script for the control to mean anything");

        var mutated = text.Replace(original, replacement, StringComparison.Ordinal);

        mutated.Should().NotBe(text);
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mutated))).Should().NotBe(ScriptFingerprint, $"{name} must be detected by the pin");
    }

    public static TheoryData<string, string, string> DecisiveLines() => new()
    {
        { "the unexpected-pass violation is removed", "if ($unexpectedPasses.Count -gt 0) { Add-Violation", "if ($false) { Add-Violation" },
        { "the unexpected-pass collection is removed", "if (-not $allowIds.ContainsKey($name)) { $unexpectedPasses.Add($name) }", "if ($false) { $unexpectedPasses.Add($name) }" },
        { "the must-fail violation is removed", "if ($mustFailViolations.Count -gt 0) { Add-Violation", "if ($false) { Add-Violation" },
        { "the dirty-tree violation is removed", "if ($changed.Count -gt 0) { Add-Violation", "if ($false) { Add-Violation" },
        { "the stale-build violation is removed", "elseif ($assemblyWritten -lt $newestInput) {", "elseif ($false) {" },
        { "the exit-code check of the test run is weakened", "if ($exit -ne 1) { Add-Violation", "if ($exit -eq 99) { Add-Violation" },
        { "Record mode exits 0", "'RECORDED' { $ExitRecorded }", "'RECORDED' { $ExitPass }" },
        { "the CI refusal of Record is removed", "if ($inCi -and $Record) {", "if ($false) {" },
        { "the fresh-output cleanup is removed", "Remove-Item -LiteralPath $owned -Force", "$null = $owned" },
        { "the TRX age check is removed", "elseif ([System.IO.File]::GetLastWriteTimeUtc($trxPath) -lt $runStart.AddSeconds(-2)) {", "elseif ($false) {" },
    };

    [Fact]
    public void A_test_in_a_SQL_named_class_may_be_allow_listed_only_for_the_four_reviewed_reasons()
    {
        var sqlNamed = Read(AllowListPath).Where(e => ClassOf(e.Id).EndsWith("SqlServerTests", StringComparison.Ordinal)).ToArray();

        // Two inventory scripts' text checks, one more of the same family, and the cancellation test that passes because the server
        // is unreachable. Nothing else in a class named for SQL Server may pass without it.
        sqlNamed.Should().HaveCount(4);
        sqlNamed.Select(e => e.Category).Distinct().Order().Should().Equal("static-script-text", "unreachable-by-design");
    }

    [Fact]
    public void The_exemptions_are_exactly_the_reviewed_ones()
    {
        var entries = Read(AllowListPath);

        entries.Count(e => e.Condition == "requires-git-directory").Should().Be(1, "one source pin needs a .git directory");
        entries.Count(e => e.Condition == "benchmark-not-requested").Should().Be(1, "one opt-in benchmark gate");
        entries.Count(e => e.Category == "conditional-diagnostic").Should().Be(1);
        entries.Count(e => e.Category == "unreachable-by-design").Should().Be(1);
    }

    [Fact]
    public void No_test_is_both_allowed_to_pass_and_required_to_fail()
    {
        var allowed = Read(AllowListPath).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

        Read(MustFailPath).Select(e => e.Id).Where(allowed.Contains).Should().BeEmpty();
    }

    private record Entry(string Id, string Category, string Condition, string Reason);

    private static string ClassOf(string id)
    {
        var paren = id.IndexOf('(');
        var name = paren < 0 ? id : id[..paren];
        return name[..name.LastIndexOf('.')];
    }

    private static List<Entry> Read(string path) =>
        File.ReadAllLines(path)
            .Where(l => l.Trim().Length > 0 && !l.StartsWith('#'))
            .Select(l =>
            {
                var cells = l.Split('\t');
                cells.Length.Should().BeGreaterThanOrEqualTo(4, $"{Path.GetFileName(path)}: '{l}' needs id, category, condition and reason");
                return new Entry(cells[0], cells[1], cells[2], cells[3]);
            })
            .ToList();

    private static string Fingerprint(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal))));

    private static string AllowListPath => Path.Combine(ListDirectory(), "approved-non-sql-passes.tsv");

    private static string MustFailPath => Path.Combine(ListDirectory(), "required-unavailable-failures.tsv");

    private static string ListDirectory() => Path.Combine(RepoRoot(), "tests", "Apperture.IntegrationTests", "SqlExecutionHonesty");

    private static string ScriptPath() => Path.Combine(RepoRoot(), "scripts", "dev", "Test-SqlExecutionHonesty.ps1");

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
