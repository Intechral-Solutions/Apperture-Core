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

    [Fact]
    public void Summarize_maps_tls_and_generic_smtp_client_failures()
    {
        NotificationDeliveryErrorGuidance
            .Summarize("Failure sending mail. → Server does not support secure connections.", "failed")
            .Should().Contain("TLS/STARTTLS");
        NotificationDeliveryErrorGuidance
            .Summarize("Failure sending mail.", "failed")
            .Should().Contain("SMTP send failed");
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
        // AUP-1 moved these controls to Account → Notifications; the explanation must travel with them.
        var content = ReadRepoFile(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Account/AccountNotifications.razor");
        content.Should().Contain("appear in your");
        content.Should().Contain("turning email or toast off suppresses that channel for you only");
        content.Should().Contain("Administrators decide which notifications exist");
        content.Should().Contain("Digest summaries and quiet hours");
    }

    private const string DiagnosticsPage =
        "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDiagnostics.razor";

    [Fact]
    public void Diagnostics_page_states_its_purpose_and_groups_the_tools()
    {
        var content = ReadRepoFile(DiagnosticsPage);

        content.Should().Contain("Check that notifications are set up and delivering");
        content.Should().Contain("Title=\"Overall delivery health\"");
        content.Should().Contain("Title=\"Send a test email\"");
        content.Should().Contain("Title=\"Integration to check\"");
        content.Should().Contain("Title=\"Look up notification activity\"");
        // The tool with side effects sits behind progressive disclosure and is closed by default.
        content.Should().Contain("<ManagementAdvancedSection Title=\"Pipeline test (admin)\"");
        content.Should().NotContain("InitiallyOpen");
    }

    [Fact]
    public void Diagnostics_page_explains_every_input_in_visible_text()
    {
        var content = ReadRepoFile(DiagnosticsPage);

        // Each input has a label tied to it and help text that is associated, not hover-only.
        foreach (var id in new[] { "diag-smtp-recipient", "diag-integration", "diag-definition", "diag-dry-run", "diag-dispatch" })
            content.Should().Contain($"for=\"{id}\"", because: id);
        foreach (var describedBy in new[] { "diag-integration-help", "diag-definition-help", "diag-dry-run-help", "diag-dispatch-help" })
        {
            content.Should().Contain($"aria-describedby=\"{describedBy}\"", because: describedBy);
            content.Should().Contain($"id=\"{describedBy}\"", because: describedBy);
        }

        // No explanation lives only in a hover tooltip (a lower-case title attribute).
        System.Text.RegularExpressions.Regex.IsMatch(content, "\\stitle=\"").Should().BeFalse();
    }

    [Fact]
    public void Diagnostics_page_warns_that_a_live_pipeline_test_is_real()
    {
        var content = ReadRepoFile(DiagnosticsPage);

        content.Should().Contain("Dry run (check only)");
        content.Should().Contain("Saves nothing and sends nothing");
        content.Should().Contain("this creates a real notification for the integration");
        content.Should().Contain("real recipients");
        content.Should().Contain("Run dry-run test");
        content.Should().Contain("Run live test");
        // The warning only shows when dry-run is turned off.
        content.Should().Contain("@if (!_pipelineTestDryRun)");
        // The page says which definition the pipeline test uses.
        content.Should().Contain("Schedule expected run missing");
    }

    [Fact]
    public void Diagnostics_page_no_longer_promises_tables_it_does_not_show()
    {
        var content = ReadRepoFile(DiagnosticsPage);

        content.Should().NotContain("required for tables");
        content.Should().Contain("This is a summary");
        content.Should().Contain("NotificationUiRoutes.IntegrationNotifications(");
        content.Should().Contain("NotificationUiRoutes.Delivery()");
    }

    [Fact]
    public void Diagnostics_page_keeps_every_existing_capability()
    {
        var content = ReadRepoFile(DiagnosticsPage);

        content.Should().Contain("Client.GetNotificationDeliveryHealthAsync()");
        content.Should().Contain("Client.GetNotificationConfigurationDiagnosticsAsync()");
        content.Should().Contain("Client.GetNotificationIntegrationDiagnosticsAsync(");
        content.Should().Contain("Client.SendNotificationSmtpTestAsync(");
        content.Should().Contain("Client.RunPlatformNotificationPipelineTestAsync(");
        content.Should().Contain("DryRun: _pipelineTestDryRun");
        content.Should().Contain("DispatchImmediately: !_pipelineTestDryRun && _pipelineTestDispatch");
        content.Should().Contain("q.TryGetValue(\"integrationId\"");
        content.Should().Contain("q.TryGetValue(\"definitionId\"");
        content.Should().Contain("@if (_canManage)");
        content.Should().Contain("Requires integration read and notification definition read.");
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
