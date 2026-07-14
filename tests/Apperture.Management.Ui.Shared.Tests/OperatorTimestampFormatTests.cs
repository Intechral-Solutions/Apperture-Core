using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>SWM-4: operator display timestamp formatting.</summary>
public sealed class OperatorTimestampFormatTests
{
    private static readonly DateTimeOffset NoonUtc =
        new(2025, 6, 15, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ResolveEffectiveTimeZoneId_Invalid_Falls_Back_To_Utc()
    {
        OperatorTimestampFormat.ResolveEffectiveTimeZoneId(null).Should().Be("UTC");
        OperatorTimestampFormat.ResolveEffectiveTimeZoneId(" ").Should().Be("UTC");
        OperatorTimestampFormat.ResolveEffectiveTimeZoneId("Not/AZone").Should().Be("UTC");
    }

    [Fact]
    public void FromBrowserCandidate_Uses_Valid_Iana()
    {
        if (!OperatorTimestampFormat.TryFind("America/Los_Angeles", out _))
            return;

        var (id, source) = OperatorDisplayTimeZoneResolution.FromBrowserCandidate("America/Los_Angeles");
        id.Should().Be("America/Los_Angeles");
        source.Should().Be(OperatorDisplayTimeZoneSource.Browser);
    }

    [Fact]
    public void FromBrowserCandidate_Invalid_Or_Missing_Falls_Back_To_Utc()
    {
        OperatorDisplayTimeZoneResolution.FromBrowserCandidate(null)
            .Should().Be(("UTC", OperatorDisplayTimeZoneSource.UtcFallback));
        OperatorDisplayTimeZoneResolution.FromBrowserCandidate("Not/AZone")
            .Should().Be(("UTC", OperatorDisplayTimeZoneSource.UtcFallback));
    }

    [Fact]
    public void Format_Uses_Browser_Zone_When_Valid()
    {
        if (!OperatorTimestampFormat.TryFind("America/Los_Angeles", out _))
            return;

        var result = OperatorTimestampFormat.Format(
            NoonUtc,
            "America/Los_Angeles",
            OperatorDisplayTimeZoneSource.Browser,
            showUtcDetail: true);

        result.Primary.Should().Contain("Your time (America/Los_Angeles)");
        result.Primary.Should().Contain("2025-06-15 12:00:00");
        result.UtcDetail.Should().Be("2025-06-15 19:00:00 UTC");
        result.Source.Should().Be(OperatorDisplayTimeZoneSource.Browser);
    }

    [Fact]
    public void Format_Invalid_Zone_Falls_Back_To_Utc_Label()
    {
        var result = OperatorTimestampFormat.Format(
            NoonUtc,
            "Invalid/Zone",
            OperatorDisplayTimeZoneSource.Browser,
            showUtcDetail: true);

        result.Primary.Should().Contain("UTC");
        result.Primary.Should().Contain("2025-06-15 19:00:00");
        result.UtcDetail.Should().Be("2025-06-15 19:00:00 UTC");
    }

    [Fact]
    public void Format_Null_Uses_Placeholder()
    {
        var result = OperatorTimestampFormat.Format(
            null,
            "UTC",
            OperatorDisplayTimeZoneSource.UtcFallback);

        result.Primary.Should().Be("—");
        result.UtcDetail.Should().BeNull();
    }

    [Fact]
    public void FormatZoneLabel_Browser_Uses_Your_Time_Prefix()
    {
        OperatorTimestampFormat.FormatZoneLabel("America/Los_Angeles", OperatorDisplayTimeZoneSource.Browser)
            .Should().Be("Your time (America/Los_Angeles)");
        OperatorTimestampFormat.FormatZoneLabel("UTC", OperatorDisplayTimeZoneSource.UtcFallback)
            .Should().Be("UTC");
    }

    [Fact]
    public void Format_Dst_Spring_Conversion_Is_Sane()
    {
        if (!OperatorTimestampFormat.TryFind("America/Los_Angeles", out _))
            return;

        // 2025-03-09 10:00 UTC = 03:00 PDT after spring-forward.
        var utc = new DateTimeOffset(2025, 3, 9, 10, 0, 0, TimeSpan.Zero);
        var result = OperatorTimestampFormat.Format(
            utc,
            "America/Los_Angeles",
            OperatorDisplayTimeZoneSource.Browser);

        result.Primary.Should().Contain("2025-03-09 03:00:00");
        result.Primary.Should().Contain("America/Los_Angeles");
    }

    [Fact]
    public void FormatRunListWhenLine_Includes_Timezone_Label()
    {
        var run = new RunSummaryDto(
            RunId: "r1",
            ExecutionInstanceId: "i1",
            ExecutionDefinitionId: "wf",
            Status: "Completed",
            CreatedAt: NoonUtc,
            CompletedAt: NoonUtc,
            CorrelationKey: null,
            RetryOfRunId: null,
            ExecutionKind: "workflow",
            IntegrationId: null);

        var line = RunScheduleOperatorUiHelpers.FormatRunListWhenLine(
            run,
            "UTC",
            OperatorDisplayTimeZoneSource.UtcFallback);

        line.Should().StartWith("Ended ");
        line.Should().Contain("UTC");
    }
}
