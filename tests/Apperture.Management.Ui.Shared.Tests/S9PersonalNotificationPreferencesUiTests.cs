using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// Guards the personal notification preference surface. AUP-1 moved the preference controls out of the
/// inbox page and into Account → Notifications, so these assertions follow the controls to their new
/// home while keeping the original guarantees: the inbox still leads an operator to their preferences,
/// and unbuilt delivery features are still described as unavailable rather than rendered as controls.
/// </summary>
public sealed class S9PersonalNotificationPreferencesUiTests
{
    [Fact]
    public void Personal_notifications_page_links_to_account_notification_preferences()
    {
        var content = ReadRepoFile("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Me/PersonalNotifications.razor");
        content.Should().Contain("AccountUiRoutes.Notifications");
        content.Should().Contain("Account → Notifications");
    }

    [Fact]
    public void Account_notification_preferences_page_uses_the_account_preference_endpoints()
    {
        var content = ReadRepoFile("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Account/AccountNotifications.razor");
        content.Should().Contain("GetAccountNotificationPreferencesAsync");
        content.Should().Contain("SetAccountNotificationPreferenceAsync");
    }

    [Fact]
    public void Account_notification_preferences_page_reports_unbuilt_delivery_features_as_unavailable()
    {
        var content = ReadRepoFile("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Account/AccountNotifications.razor");
        content.Should().Contain("Not available yet");
        content.Should().Contain("Digest summaries, quiet hours");
    }

    [Fact]
    public void Account_notification_preferences_page_renders_no_control_for_the_in_app_channel()
    {
        var content = ReadRepoFile("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Account/AccountNotifications.razor");

        // The in-app channel has no per-user switch in the delivery pipeline, so it is stated in text.
        // A disabled checkbox would imply an editable preference that does not exist.
        content.Should().Contain("Always on");
        content.Should().Contain("PlatformNotEditableReason");
        content.Should().NotContain("disabled=\"@(!preference.PlatformEditable)\"");
    }

    [Fact]
    public void Account_notification_preferences_page_renders_a_toast_toggle_alongside_email()
    {
        var content = ReadRepoFile("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Account/AccountNotifications.razor");

        // Toast-B P5 Task 2: the Toast column mirrors the existing Email column's per-row autosave shape.
        content.Should().Contain("ToggleToastAsync");
        content.Should().Contain("Show as toast");
        content.Should().Contain("aria-label=\"@($\"Show a toast for {preference.DisplayName}\")\"");

        // Regression: the email-focused controls are still present alongside the new toast column.
        content.Should().Contain("ToggleEmailAsync");
        content.Should().Contain("aria-label=\"@($\"Email me about {preference.DisplayName}\")\"");
    }

    [Fact]
    public void Account_notification_preferences_page_scopes_busy_state_per_channel()
    {
        var content = ReadRepoFile("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Account/AccountNotifications.razor");

        // Busy state must be scoped per (definitionId, channel), not per row, so toggling Email doesn't
        // disable the Toast control for the same notification and vice versa.
        content.Should().Contain("IsBusy(string definitionId, string channel)");
        content.Should().Contain(":email\"");
        content.Should().Contain(":toast\"");
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath));
    }
}
