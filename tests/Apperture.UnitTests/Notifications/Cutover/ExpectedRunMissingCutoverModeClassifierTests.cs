using Apperture.Core.Application.Notifications.Cutover;
using Apperture.Core.Application.Notifications.Diagnostics;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications.Cutover;

public sealed class ExpectedRunMissingCutoverModeClassifierTests
{
    [Fact]
    public void Classifies_production_safe_defaults()
    {
        var modes = ExpectedRunMissingCutoverOperatingModes.Classify(AllOff());
        modes.Should().Contain(ExpectedRunMissingCutoverOperatingModes.ProductionSafeDefaults);
    }

    [Fact]
    public void Classifies_shadow_and_dry_run()
    {
        var modes = ExpectedRunMissingCutoverOperatingModes.Classify(
            new NotificationConfigurationDiagnostics(true, true, true, false, false, [], DateTimeOffset.UtcNow));
        modes.Should().Contain(ExpectedRunMissingCutoverOperatingModes.ShadowMode);
        modes.Should().Contain(ExpectedRunMissingCutoverOperatingModes.DryRunMode);
    }

    [Fact]
    public void Classifies_dual_send_risk()
    {
        var modes = ExpectedRunMissingCutoverOperatingModes.Classify(
            new NotificationConfigurationDiagnostics(true, true, true, true, false, [], DateTimeOffset.UtcNow));
        modes.Should().Contain(ExpectedRunMissingCutoverOperatingModes.DualSendRisk);
    }

    [Fact]
    public void Classifies_no_send_risk()
    {
        var modes = ExpectedRunMissingCutoverOperatingModes.Classify(
            new NotificationConfigurationDiagnostics(true, true, true, false, true, [], DateTimeOffset.UtcNow));
        modes.Should().Contain(ExpectedRunMissingCutoverOperatingModes.NoSendRisk);
    }

    [Fact]
    public void Classifies_platform_cutover_mode()
    {
        var modes = ExpectedRunMissingCutoverOperatingModes.Classify(
            new NotificationConfigurationDiagnostics(true, true, true, true, true, [], DateTimeOffset.UtcNow));
        modes.Should().Contain(ExpectedRunMissingCutoverOperatingModes.PlatformCutoverMode);
    }

    private static NotificationConfigurationDiagnostics AllOff() =>
        new(false, false, false, false, false, [], DateTimeOffset.UtcNow);
}
