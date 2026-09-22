using System.Reflection;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications;

/// <summary>
/// Drift detection between <see cref="PlatformEventSeverities"/> (backend, in
/// Apperture.Core.Application) and <see cref="NotificationToastSeverityMapper"/> (UI, in
/// Apperture.Management.Ui.Shared). Lives here rather than alongside
/// <c>NotificationToastSeverityMapperTests</c> in Apperture.Management.Ui.Shared.Tests because that
/// project deliberately does not reference Apperture.Core.Application (see the ArchTests boundary
/// rules Sdk6dOperatorUiBoundaryDependencies and Sdk7SharedRecordIdentityBoundaries, which assert
/// the production Apperture.Management.Ui.Shared assembly never references it). Apperture.UnitTests
/// already references both Apperture.Core.Application and Apperture.Management.Ui.Shared for
/// unrelated, pre-existing reasons, so this test rides on an existing dependency edge instead of
/// adding a new one to the UI test project.
/// </summary>
public sealed class NotificationToastSeverityMapperDriftTests
{
    /// <summary>
    /// Closes the drift-detection gap the compile-time CS8509 guard cannot cover: CS8509 only
    /// catches a case added to the internal <c>KnownNotificationSeverity</c> enum without a
    /// matching switch arm. It says nothing about the backend adding a 5th token to
    /// <see cref="PlatformEventSeverities"/> (e.g. a hypothetical "fatal") that
    /// <c>NotificationToastSeverityMapper.Parse</c> doesn't recognize — that token would silently
    /// fall through to <c>KnownNotificationSeverity.Unknown</c> -&gt;
    /// <see cref="ToastSeverity.Info"/>, with nothing failing: not the compile, not any per-token
    /// test in <c>NotificationToastSeverityMapperTests</c> (none of them iterate
    /// <see cref="PlatformEventSeverities"/> itself). This test reflects over
    /// <see cref="PlatformEventSeverities"/>' public constants so a new one added there is caught
    /// here even before anyone remembers to update the mapper.
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

        // The known-correct mapping per the spec, per the individual tests in
        // NotificationToastSeverityMapperTests (Critical_maps_to_*, Error_maps_to_*, Warning_maps_to_*,
        // Info_maps_to_*) -- NOT derived from Resolve itself, so this genuinely checks Resolve against
        // an independent expectation rather than against itself.
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
