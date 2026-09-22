using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// <see cref="NotificationToastSeverityMapper"/>. Plain xUnit + FluentAssertions, mirroring
/// <c>ToastFoundationTests</c>'s style: no <c>TimeProvider</c>, no component host — this mapper is
/// pure and presentation-agnostic.
/// </summary>
public sealed class NotificationToastSeverityMapperTests
{
    [Fact]
    public void Critical_maps_to_error_with_explicit_ten_second_duration()
    {
        var result = NotificationToastSeverityMapper.Resolve("critical");

        result.Severity.Should().Be(ToastSeverity.Error);
        result.Duration.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Error_maps_to_error_with_explicit_ten_second_duration()
    {
        var result = NotificationToastSeverityMapper.Resolve("error");

        result.Severity.Should().Be(ToastSeverity.Error);
        result.Duration.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Warning_maps_to_warning_with_default_duration()
    {
        var result = NotificationToastSeverityMapper.Resolve("warning");

        result.Severity.Should().Be(ToastSeverity.Warning);
        result.Duration.Should().BeNull();
    }

    [Fact]
    public void Info_maps_to_info_with_default_duration()
    {
        var result = NotificationToastSeverityMapper.Resolve("info");

        result.Severity.Should().Be(ToastSeverity.Info);
        result.Duration.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("debug")]
    [InlineData("CRITICAL_TYPO")]
    public void Null_empty_and_unknown_tokens_all_map_to_info_with_default_duration(string? token)
    {
        var result = NotificationToastSeverityMapper.Resolve(token);

        result.Severity.Should().Be(ToastSeverity.Info);
        result.Duration.Should().BeNull();
    }

    [Fact]
    public void Case_is_not_significant()
    {
        var lower = NotificationToastSeverityMapper.Resolve("critical");
        var upper = NotificationToastSeverityMapper.Resolve("CRITICAL");
        var title = NotificationToastSeverityMapper.Resolve("Critical");

        upper.Should().Be(lower);
        title.Should().Be(lower);
    }

    [Fact]
    public void Never_maps_to_success()
    {
        // Guaranteed by construction (no switch arm in NotificationToastSeverityMapper.Resolve
        // produces ToastSeverity.Success), but named explicitly so a future edit that accidentally
        // introduces Success fails a targeted regression test, not just an incidental one. This is
        // the spec's explicit callout: a Notification *kind* like "resolved" must never be able to
        // reach this mapper at all (Resolve only accepts a severity token), and nothing this mapper
        // does may independently produce Success either.
        var tokens = new[]
        {
            "critical", "error", "warning", "info",
            "CRITICAL", "Error", "Warning", "INFO",
            null, "", "   ", "debug", "resolved", "success", "CRITICAL_TYPO",
        };

        foreach (var token in tokens)
            NotificationToastSeverityMapper.Resolve(token).Severity.Should().NotBe(ToastSeverity.Success);
    }

    [Fact]
    public void The_four_known_tokens_each_resolve_distinctly_this_is_the_runtime_half_of_the_exhaustiveness_guard()
    {
        // This test (plus tests 1-4 above, individually) is the RUNTIME half of the exhaustiveness
        // guarantee: it proves the four current backend tokens are each recognized and correctly
        // attributed, not silently collapsing to the Unknown/Info fallback by coincidence. The
        // COMPILE-TIME half lives in NotificationToastSeverityMapper.Resolve's own switch
        // expression: it has no discard arm over the closed KnownNotificationSeverity enum, and
        // Apperture.Management.Ui.Shared.csproj scopes <WarningsAsErrors>CS8509</WarningsAsErrors>
        // to itself so an unhandled case there fails this project's build. See the comment on that
        // switch for the full two-layer guarantee (compile-time warning-elevated-to-error, plus a
        // SwitchExpressionException runtime backstop either way).
        NotificationToastSeverityMapper.Resolve("critical").Severity.Should().Be(ToastSeverity.Error);
        NotificationToastSeverityMapper.Resolve("error").Severity.Should().Be(ToastSeverity.Error);
        NotificationToastSeverityMapper.Resolve("warning").Severity.Should().Be(ToastSeverity.Warning);
        NotificationToastSeverityMapper.Resolve("info").Severity.Should().Be(ToastSeverity.Info);

        // Warning and Info must be distinguishable from each other, not both quietly resolving to
        // the same fallback value that Unknown also happens to produce.
        NotificationToastSeverityMapper.Resolve("warning").Severity
            .Should().NotBe(NotificationToastSeverityMapper.Resolve("info").Severity);

        // Critical and Error deliberately DO collapse to the same presentation (both Error, both
        // 10s) — that's the spec, not a bug — so there's nothing to assert distinct between them.
    }
}
