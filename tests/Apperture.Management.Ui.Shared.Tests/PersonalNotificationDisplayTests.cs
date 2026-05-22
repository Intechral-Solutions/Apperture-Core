using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class PersonalNotificationDisplayTests
{
    [Fact]
    public void GetMenuTitle_prefers_summary_over_subject()
    {
        PersonalNotificationDisplay.GetMenuTitle(
                "[Apperture] Attention: expected_run_missing (int-1)",
                "Expected run missing for Payroll sync")
            .Should()
            .Be("Expected run missing for Payroll sync");
    }

    [Fact]
    public void CompactSubject_strips_apperture_prefix_and_trailing_integration_id()
    {
        PersonalNotificationDisplay.CompactSubject(
                "[Apperture] Attention: expected_run_missing (49955a58-e842-44a2-8af8-341b)")
            .Should()
            .Be("Attention: expected run missing");
    }

    [Fact]
    public void GetMenuTitle_uses_compact_subject_when_summary_missing()
    {
        PersonalNotificationDisplay.GetMenuTitle(
                "[Apperture] Reminder: expected_run_missing (int-1)",
                summary: null)
            .Should()
            .Be("Reminder: expected run missing");
    }
}
