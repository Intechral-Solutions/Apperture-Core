using System.Text.RegularExpressions;
using Apperture.RuntimePlane.Dispatch.SqlServer;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Runtime;

/// <summary>
/// The scheduler's audit rows are correlated per fire. <c>AuditLogs.CorrelationId</c> is <c>nvarchar(64)</c>; the first form of this
/// identifier, <c>schedule:{id}:{fire:O}</c> over a zero-offset <see cref="DateTimeOffset"/>, is 67 characters for a generated
/// schedule id, so every scheduler audit insert failed and the best-effort catch swallowed it.
/// </summary>
public sealed class ScheduleFireCorrelationTests
{
    /// <summary>The shape every platform-generated schedule id has (<c>Guid.ToString("N")[..24]</c>).</summary>
    private const string GeneratedScheduleId = "7dc8a20b735d41e7b3dbc386";

    private static readonly DateTimeOffset Fire = new(2026, 10, 23, 6, 19, 0, TimeSpan.Zero);

    [Fact]
    public void The_original_format_did_not_fit_the_audit_column()
    {
        var original = $"schedule:{GeneratedScheduleId}:{Fire:O}";

        original.Length.Should().Be(67);
        original.Length.Should().BeGreaterThan(ScheduleFireCorrelation.AuditCorrelationIdMaxLength);
    }

    [Fact]
    public void A_generated_schedule_id_and_a_normalized_fire_give_a_per_fire_identifier_that_fits_the_column()
    {
        var value = ScheduleFireCorrelation.ForAudit(GeneratedScheduleId, Fire);

        value.Should().Be($"schedule:{GeneratedScheduleId}:2026-10-23T06:19:00.0000000Z");
        value!.Length.Should().BeLessThanOrEqualTo(ScheduleFireCorrelation.AuditCorrelationIdMaxLength);
    }

    [Fact]
    public void The_identifier_names_the_same_instant_whatever_offset_the_fire_was_expressed_in()
    {
        var local = new DateTimeOffset(2026, 10, 23, 8, 19, 0, TimeSpan.FromHours(2));

        ScheduleFireCorrelation.ForAudit(GeneratedScheduleId, local)
            .Should().Be(ScheduleFireCorrelation.ForAudit(GeneratedScheduleId, Fire));
    }

    [Fact]
    public void Each_fire_of_a_schedule_has_its_own_identifier_and_the_same_fire_always_has_the_same_one()
    {
        var next = Fire.AddMinutes(1);

        ScheduleFireCorrelation.ForAudit(GeneratedScheduleId, Fire).Should().NotBe(ScheduleFireCorrelation.ForAudit(GeneratedScheduleId, next));
        ScheduleFireCorrelation.ForAudit(GeneratedScheduleId, Fire).Should().Be(ScheduleFireCorrelation.ForAudit(GeneratedScheduleId, Fire));
    }

    [Fact]
    public void A_schedule_id_too_long_for_the_per_fire_form_falls_back_to_the_runs_own_correlation()
    {
        // 64 - len("schedule:") = 55 characters is the longest id the run-level form holds; the per-fire form needs 29 more.
        var longId = new string('a', 40);

        ScheduleFireCorrelation.ForAudit(longId, Fire).Should().Be($"schedule:{longId}");
    }

    [Fact]
    public void A_schedule_id_that_fits_no_form_is_left_out_rather_than_truncated()
    {
        var hugeId = new string('a', 60);

        ScheduleFireCorrelation.ForAudit(hugeId, Fire).Should().BeNull();
    }

    [Fact]
    public void The_dispatch_runner_builds_no_audit_correlation_by_interpolation()
    {
        var text = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Apperture.RuntimePlane", "Apperture.RuntimePlane.Dispatch.SqlServer", "ScheduleDispatchRunner.cs"));

        Regex.Matches(text, @"new CorrelationId\(\$""").Count.Should().Be(
            0,
            "an interpolated correlation identifier is unbounded; every scheduler audit row takes it from ScheduleFireCorrelation.ForAudit");
        Regex.Matches(text, @"new CorrelationId\(ScheduleFireCorrelation\.ForAudit\(").Count.Should().BeGreaterThan(0);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
