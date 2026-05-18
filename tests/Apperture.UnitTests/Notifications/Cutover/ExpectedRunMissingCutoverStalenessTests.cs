using Apperture.Core.Application.Notifications.Cutover;
using Apperture.Core.Application.Notifications.Diagnostics;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications.Cutover;

public sealed class ExpectedRunMissingCutoverStalenessTests
{
    [Fact]
    public void Sign_off_stale_when_parity_acceptability_changed()
    {
        var signOff = SampleSignOff(parityAcceptable: true);
        var parity = SampleParity(acceptable: false);
        var config = SampleConfig();

        ExpectedRunMissingCutoverStaleness.IsSignOffStale(signOff, parity, config).Should().BeTrue();
    }

    [Fact]
    public void Sign_off_not_stale_when_unchanged()
    {
        var signOff = SampleSignOff(parityAcceptable: true, warningCodes: ["dual_send_risk"]);
        var parity = SampleParity(acceptable: true);
        var config = SampleConfig(warnings: [new NotificationConfigurationSafetyWarning("dual_send_risk", "msg")]);

        ExpectedRunMissingCutoverStaleness.IsSignOffStale(signOff, parity, config).Should().BeFalse();
    }

    private static ExpectedRunMissingCutoverSignOff SampleSignOff(
        bool parityAcceptable,
        IReadOnlyList<string>? warningCodes = null) =>
        new(
            "so-1",
            "int-1",
            "platform.schedule.expected_run_missing",
            ExpectedRunMissingCutoverSignOffStatuses.ReadyForCutover,
            DateTimeOffset.UtcNow,
            "user:1",
            "Op",
            parityAcceptable,
            warningCodes ?? [],
            [],
            [],
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private static ExpectedRunMissingParityAcceptanceSummary SampleParity(bool acceptable) =>
        new("int-1", 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], [], acceptable, DateTimeOffset.UtcNow);

    private static NotificationConfigurationDiagnostics SampleConfig(
        IReadOnlyList<NotificationConfigurationSafetyWarning>? warnings = null) =>
        new(true, true, true, false, false, warnings ?? [], DateTimeOffset.UtcNow);
}
