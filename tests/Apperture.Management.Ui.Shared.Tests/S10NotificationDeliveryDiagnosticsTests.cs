using Apperture.Core.Domain.Notifications;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class NotificationRecipientSkipReasonLabelsTests
{
    [Theory]
    [InlineData(NotificationRecipientSkipReasons.UserOptOut, "User opted out of email for this notification")]
    [InlineData(NotificationRecipientSkipReasons.MissingEmail, "Recipient does not have an email address")]
    [InlineData(NotificationRecipientSkipReasons.NoEmailSubscription, "Email subscription is not enabled")]
    [InlineData(NotificationRecipientSkipReasons.EmailOptedOut, "Email delivery is disabled by preference")]
    [InlineData(NotificationRecipientSkipReasons.DeliveryModeMuted, "Subscription delivery mode is muted")]
    [InlineData(NotificationRecipientSkipReasons.DeliveryModeDigestOnly, "digest-only")]
    [InlineData(NotificationRecipientSkipReasons.ContactDisabled, "Managed contact is disabled")]
    [InlineData(NotificationRecipientSkipReasons.UserDisabled, "User account is disabled")]
    [InlineData(NotificationRecipientSkipReasons.TeamDisabled, "Operational team is disabled")]
    [InlineData(NotificationRecipientSkipReasons.TeamEmpty, "Operational team has no members")]
    [InlineData(NotificationRecipientSkipReasons.DuplicateEmail, "Duplicate email address")]
    public void Label_maps_known_skip_reasons(string reason, string expectedFragment)
    {
        NotificationRecipientSkipReasonLabels.Label(reason).Should().Contain(expectedFragment);
        NotificationRecipientSkipReasonLabels.IsRecognized(reason).Should().BeTrue();
    }

    [Fact]
    public void Label_unknown_reason_returns_fallback()
    {
        NotificationRecipientSkipReasonLabels.Label("customReason").Should()
            .Be("Skipped for an unrecognized reason");
        NotificationRecipientSkipReasonLabels.IsRecognized("customReason").Should().BeFalse();
    }

    [Fact]
    public void Help_and_action_guidance_exist_for_opt_out_and_subscription_gaps()
    {
        NotificationRecipientSkipReasonLabels.HelpText(NotificationRecipientSkipReasons.UserOptOut)
            .Should().NotBeNullOrWhiteSpace();
        NotificationRecipientSkipReasonLabels.ActionGuidance(NotificationRecipientSkipReasons.NoEmailSubscription)
            .Should().Contain("My notifications");
        NotificationRecipientSkipReasonLabels.ActionGuidance(NotificationRecipientSkipReasons.MissingEmail)
            .Should().Contain("email address");
    }
}

public sealed class NotificationDeliveryErrorGuidanceTests
{
    [Fact]
    public void Summarize_maps_smtp_configuration_errors()
    {
        NotificationDeliveryErrorGuidance.Summarize("smtp_not_configured", "failed")
            .Should().Contain("SMTP is not configured");
        NotificationDeliveryErrorGuidance.Summarize("FromAddress missing", "failed")
            .Should().Contain("from address");
    }

    [Fact]
    public void Summarize_does_not_echo_secrets()
    {
        var guidance = NotificationDeliveryErrorGuidance.Summarize("Authentication failed for user secret-ref", "failed");
        guidance.Should().NotContain("secret-ref");
        guidance.Should().Contain("authentication");
    }
}

public sealed class S10NotificationDiagnosticsUiTests
{
    [Fact]
    public void Recipient_snapshot_display_uses_skip_reason_labels()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/Notifications/NotificationRecipientSnapshotDisplay.razor");
        content.Should().Contain("NotificationRecipientSkipReasonLabels.Label");
        content.Should().Contain("NotificationRecipientSkipReasonLabels.HelpText");
        content.Should().Contain("NotificationRecipientSkipReasonLabels.ActionGuidance");
        content.Should().Contain("recipient(s) considered");
    }

    [Fact]
    public void Instance_detail_shows_suppression_labels_and_delivery_guidance()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationInstanceDetail.razor");
        content.Should().Contain("NotificationInstanceSuppressionReasonLabels.Label");
        content.Should().Contain("NotificationDeliveryErrorGuidance.Summarize");
    }

    [Fact]
    public void Delivery_health_page_includes_error_guidance_column()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDelivery.razor");
        content.Should().Contain("NotificationDeliveryErrorGuidance.Summarize");
        content.Should().Contain("Guidance");
    }

    [Fact]
    public void Personal_email_preferences_explain_email_vs_in_app()
    {
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/PersonalNotificationEmailPreferences.razor");
        content.Should().Contain("Enabling email allows matching notifications");
        content.Should().Contain("Disabling email suppresses email only");
        content.Should().Contain("Administrators may still assign");
        content.Should().Contain("Daily digest");
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return File.ReadAllText(Path.Combine(dir, relativePath));
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
