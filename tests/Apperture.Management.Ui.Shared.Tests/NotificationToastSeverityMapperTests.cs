using System.Reflection;
using Apperture.Core.Application.PlatformEvents;
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

    /// <summary>
    /// Closes the drift-detection gap the compile-time CS8509 guard cannot cover: CS8509 only
    /// catches a case added to the internal <see cref="KnownNotificationSeverity"/> enum without a
    /// matching switch arm. It says nothing about the backend adding a 5th token to
    /// <see cref="PlatformEventSeverities"/> (e.g. a hypothetical "fatal") that
    /// <c>NotificationToastSeverityMapper.Parse</c> doesn't recognize — that token would silently
    /// fall through to <see cref="KnownNotificationSeverity.Unknown"/> -&gt;
    /// <see cref="ToastSeverity.Info"/>, with nothing failing: not the compile, not any
    /// per-token test above (none of them iterate <see cref="PlatformEventSeverities"/> itself).
    /// This test reflects over <see cref="PlatformEventSeverities"/>' public constants so a new one
    /// added there is caught here even before anyone remembers to update the mapper.
    /// </summary>
    [Fact]
    public void Every_PlatformEventSeverities_constant_is_recognized_by_the_mapper_not_coincidentally_Info()
    {
        var fields = typeof(PlatformEventSeverities)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .ToArray();

        // Pins today's count so a 5th severity constant added to PlatformEventSeverities in the
        // future trips this test immediately, even before the failure below (which needs the new
        // token to also be missing from `expected`) would otherwise catch it.
        fields.Should().HaveCount(4,
            "PlatformEventSeverities is expected to declare exactly Critical/Error/Warning/Info today; " +
            "if this fails, a severity token was added to or removed from PlatformEventSeverities and " +
            "NotificationToastSeverityMapper.Parse (plus this test) needs a matching update");

        // The known-correct mapping per the spec, per the individual tests above (Critical_maps_to_*,
        // Error_maps_to_*, Warning_maps_to_*, Info_maps_to_*) -- NOT derived from Resolve itself, so
        // this genuinely checks Resolve against an independent expectation rather than against
        // itself.
        var expected = new Dictionary<string, (ToastSeverity Severity, TimeSpan? Duration)>
        {
            [PlatformEventSeverities.Critical] = (ToastSeverity.Error, TimeSpan.FromSeconds(10)),
            [PlatformEventSeverities.Error] = (ToastSeverity.Error, TimeSpan.FromSeconds(10)),
            [PlatformEventSeverities.Warning] = (ToastSeverity.Warning, null),
            [PlatformEventSeverities.Info] = (ToastSeverity.Info, null),
        };

        foreach (var field in fields)
        {
            var token = (string)field.GetValue(null)!;

            expected.Should().ContainKey(token,
                $"PlatformEventSeverities.{field.Name} (\"{token}\") is a token this test does not yet know the " +
                "expected mapping for -- it must be added to `expected` above alongside a matching arm in " +
                "NotificationToastSeverityMapper.Parse, not left to fall through to Unknown");

            var (expectedSeverity, expectedDuration) = expected[token];
            var result = NotificationToastSeverityMapper.Resolve(token);

            result.Severity.Should().Be(expectedSeverity,
                $"PlatformEventSeverities.{field.Name} (\"{token}\") must resolve to its known severity, not " +
                "silently collapse to Unknown -> Info by falling through NotificationToastSeverityMapper.Parse's " +
                "discard arm");
            result.Duration.Should().Be(expectedDuration,
                $"PlatformEventSeverities.{field.Name} (\"{token}\") must resolve to its known duration");
        }
    }
}
