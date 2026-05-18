using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Domain.Notifications;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications.Diagnostics;

public sealed class ExpectedRunMissingParityAcceptanceEvaluatorTests
{
    [Fact]
    public void Summarize_counts_missing_in_platform_as_blocking()
    {
        var parity = ExpectedRunMissingParityComparer.Build(
            "int-1",
            "inc-1",
            [
                new Epic69NotificationDiagnosticRow(
                    "l1",
                    "inc-1",
                    "int-1",
                    AttentionNotificationKinds.Activated,
                    "email",
                    "old@test.com",
                    "Succeeded",
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    null),
            ],
            [],
            [],
            DateTimeOffset.UtcNow);

        var summary = ExpectedRunMissingParityAcceptanceEvaluator.Summarize(
            "int-1",
            [parity],
            DateTimeOffset.UtcNow);

        summary.KindsMissingInPlatform.Should().Be(1);
        summary.LooksAcceptableForCutover.Should().BeFalse();
        summary.BlockingFindings.Should().NotBeEmpty();
    }

    [Fact]
    public void Summarize_aligned_parity_is_acceptable()
    {
        var now = DateTimeOffset.UtcNow;
        var parity = ExpectedRunMissingParityComparer.Build(
            "int-1",
            "inc-1",
            [
                new Epic69NotificationDiagnosticRow(
                    "l1",
                    "inc-1",
                    "int-1",
                    AttentionNotificationKinds.Activated,
                    "email",
                    "user@test.com",
                    "Succeeded",
                    null,
                    now,
                    now,
                    null),
            ],
            [
                new NotificationInstanceDiagnosticRow(
                    "n1",
                    NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                    NotificationKinds.Activation,
                    NotificationInstanceStatuses.Pending,
                    "int-1",
                    "inc-1",
                    null,
                    null,
                    now,
                    now),
            ],
            [
                new NotificationOutboxDiagnosticRow(
                    "o1",
                    "n1",
                    NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                    NotificationKinds.Activation,
                    "int-1",
                    "inc-1",
                    "user@test.com",
                    null,
                    null,
                    "subscription",
                    NotificationChannels.Email,
                    NotificationOutboxStatuses.Succeeded,
                    1,
                    null,
                    null,
                    now,
                    now,
                    now),
            ],
            now);

        var summary = ExpectedRunMissingParityAcceptanceEvaluator.Summarize("int-1", [parity], now);

        summary.KindsAligned.Should().BeGreaterThan(0);
        summary.KindsMissingInPlatform.Should().Be(0);
        summary.LooksAcceptableForCutover.Should().BeTrue();
        summary.BlockingFindings.Should().BeEmpty();
    }

    [Fact]
    public void Summarize_extra_in_platform_is_non_blocking()
    {
        var now = DateTimeOffset.UtcNow;
        var parity = ExpectedRunMissingParityComparer.Build(
            "int-1",
            "inc-1",
            [],
            [
                new NotificationInstanceDiagnosticRow(
                    "n1",
                    NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                    NotificationKinds.Activation,
                    NotificationInstanceStatuses.Pending,
                    "int-1",
                    "inc-1",
                    null,
                    null,
                    now,
                    now),
            ],
            [
                new NotificationOutboxDiagnosticRow(
                    "o1",
                    "n1",
                    NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                    NotificationKinds.Activation,
                    "int-1",
                    "inc-1",
                    "extra@test.com",
                    null,
                    null,
                    "subscription",
                    NotificationChannels.Email,
                    NotificationOutboxStatuses.Pending,
                    0,
                    null,
                    null,
                    null,
                    now,
                    now),
            ],
            now);

        var summary = ExpectedRunMissingParityAcceptanceEvaluator.Summarize("int-1", [parity], now);

        summary.KindsExtraInPlatform.Should().Be(1);
        summary.NonBlockingFindings.Should().NotBeEmpty();
    }
}
