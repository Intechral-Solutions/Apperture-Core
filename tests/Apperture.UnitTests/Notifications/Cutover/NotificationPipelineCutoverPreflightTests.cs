using Apperture.Core.Application.Notifications.Cutover;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.Notifications.Delivery;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications.Cutover;

public sealed class NotificationPipelineCutoverPreflightTests
{
    private static NotificationConfigurationDiagnostics DefaultConfig(
        bool usePlatform = false,
        bool delivery = false,
        bool instances = false,
        bool evaluation = false,
        bool platformEvents = false) =>
        new(
            platformEvents,
            evaluation,
            instances,
            delivery,
            usePlatform,
            NotificationConfigurationSafetyValidator.Validate(
                new NotificationConfigurationSnapshot(platformEvents, evaluation, instances, delivery, usePlatform)),
            DateTimeOffset.UtcNow);

    private static EmailNotificationOptions EmailOptions(
        bool enabled = true,
        string host = "smtp.example.com",
        string from = "noreply@example.com",
        string? user = null,
        string? passwordRef = null) =>
        new()
        {
            Enabled = enabled,
            SmtpHost = host,
            FromAddress = from,
            SmtpUser = user ?? "",
            SmtpPasswordRef = passwordRef,
        };

    [Fact]
    public void Cutover_gate_chooses_legacy_when_platform_pipeline_disabled()
    {
        PlatformNotificationCutoverGate
            .ShouldInvokeEpic69Orchestrator(new Core.Application.Notifications.Instances.NotificationPipelineOptions { Enabled = false })
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Cutover_gate_chooses_platform_when_enabled()
    {
        PlatformNotificationCutoverGate
            .ShouldInvokeEpic69Orchestrator(new Core.Application.Notifications.Instances.NotificationPipelineOptions { Enabled = true })
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Preflight_reports_missing_expected_run_missing_definition()
    {
        var status = NotificationPipelineCutoverPreflight.Evaluate(
            DefaultConfig(platformEvents: true, evaluation: true, instances: true),
            EmailOptions(),
            "https://mgmt.example.com",
            expectedRunMissingDefinitionExists: false,
            expectedRunMissingDefinitionEnabled: false,
            DateTimeOffset.UtcNow);

        status.ExpectedRunMissingDefinitionExists.Should().BeFalse();
        status.CriticalPreflightPassed.Should().BeFalse();
        status.PreflightChecks.Should().Contain(c =>
            c.Code == NotificationPipelineCutoverPreflight.CheckExpectedRunMissingDefinition && !c.Passed);
    }

    [Fact]
    public void Preflight_reports_missing_management_host_url()
    {
        var status = NotificationPipelineCutoverPreflight.Evaluate(
            DefaultConfig(platformEvents: true, evaluation: true, instances: true),
            EmailOptions(),
            managementHostBaseUrl: null,
            expectedRunMissingDefinitionExists: true,
            expectedRunMissingDefinitionEnabled: true,
            DateTimeOffset.UtcNow);

        status.ManagementHostUrlConfigured.Should().BeFalse();
        status.PreflightChecks.Should().Contain(c =>
            c.Code == NotificationPipelineCutoverPreflight.CheckManagementHostUrl && !c.Passed);
    }

    [Fact]
    public void Preflight_reports_missing_smtp_host()
    {
        var status = NotificationPipelineCutoverPreflight.Evaluate(
            DefaultConfig(platformEvents: true, evaluation: true, instances: true),
            EmailOptions(host: ""),
            "https://mgmt.example.com",
            true,
            true,
            DateTimeOffset.UtcNow);

        status.EmailSmtpHostConfigured.Should().BeFalse();
        status.CriticalPreflightPassed.Should().BeFalse();
    }

    [Fact]
    public void Active_path_is_dual_when_legacy_and_platform_delivery_enabled()
    {
        var status = NotificationPipelineCutoverPreflight.Evaluate(
            DefaultConfig(delivery: true, instances: true, evaluation: true, platformEvents: true),
            EmailOptions(),
            "https://mgmt.example.com",
            true,
            true,
            DateTimeOffset.UtcNow);

        status.ActiveDeliveryPath.Should().Be("dual");
        status.LegacyEpic69PathActive.Should().BeTrue();
    }

    [Fact]
    public void Active_path_is_platform_when_use_platform_pipeline_enabled()
    {
        var status = NotificationPipelineCutoverPreflight.Evaluate(
            DefaultConfig(usePlatform: true, delivery: true, instances: true, evaluation: true, platformEvents: true),
            EmailOptions(),
            "https://mgmt.example.com",
            true,
            true,
            DateTimeOffset.UtcNow);

        status.ActiveDeliveryPath.Should().Be("platform");
        status.LegacyEpic69PathActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false, "legacy")]
    [InlineData(false, true, "dual")]
    [InlineData(true, true, "platform")]
    [InlineData(true, false, "platform_no_delivery")]
    public void ResolveActiveDeliveryPath_matches_flags(bool usePlatform, bool delivery, string expectedPath)
    {
        var config = DefaultConfig(usePlatform, delivery);
        var legacy = !usePlatform;
        NotificationPipelineCutoverPreflight.ResolveActiveDeliveryPath(config, legacy).Path.Should().Be(expectedPath);
    }
}
