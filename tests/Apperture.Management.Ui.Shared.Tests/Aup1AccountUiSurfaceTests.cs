using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// Source-level checks on the account UI, following this project's existing convention of asserting
/// against Razor sources. These cover the guarantees that are easy to regress by editing markup:
/// which routes exist, that placeholder copy is gone, that administrator-owned settings are not
/// presented as personal preferences, and that form controls carry real labels.
/// </summary>
public sealed class Aup1AccountUiSurfaceTests
{
    private const string ClientRoot = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client";

    private static readonly string[] AccountPages =
    [
        $"{ClientRoot}/Pages/Account/AccountProfile.razor",
        $"{ClientRoot}/Pages/Account/AccountNotifications.razor",
        $"{ClientRoot}/Pages/Account/AccountPreferences.razor",
        $"{ClientRoot}/Pages/Account/AccountSecurity.razor",
    ];

    // --- Routes and structure ---

    [Theory]
    [InlineData("Pages/Account/AccountProfile.razor", "/account/profile")]
    [InlineData("Pages/Account/AccountNotifications.razor", "/account/notifications")]
    [InlineData("Pages/Account/AccountPreferences.razor", "/account/preferences")]
    [InlineData("Pages/Account/AccountSecurity.razor", "/account/security")]
    public void Each_account_section_page_declares_its_route(string relativePath, string route)
    {
        var content = ReadRepoFile($"{ClientRoot}/{relativePath}");

        content.Should().Contain($"@page \"{route}\"");
    }

    [Fact]
    public void The_account_root_and_the_legacy_preferences_path_both_redirect()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountRedirect.razor");

        content.Should().Contain("@page \"/account\"");
        content.Should().Contain("@page \"/preferences\"");
        content.Should().Contain("replace: true", "a redirect should not add a history entry");
    }

    [Fact]
    public void The_placeholder_account_and_preferences_pages_are_gone()
    {
        FileExists($"{ClientRoot}/Pages/Account.razor").Should().BeFalse();
        FileExists($"{ClientRoot}/Pages/Preferences.razor").Should().BeFalse();
    }

    [Fact]
    public void Every_account_page_renders_the_section_navigation()
    {
        foreach (var page in AccountPages)
            ReadRepoFile(page).Should().Contain("<AccountSectionNav", $"{page} must be navigable within the account area");
    }

    [Fact]
    public void The_user_menu_uses_an_avatar_trigger_and_text_identity_in_the_dropdown()
    {
        var content = ReadRepoFile($"{ClientRoot}/Molecules/UserAccountMenu.razor");

        content.Should().Contain("AccountUiRoutes.Profile");
        content.Should().Contain("AccountUiRoutes.Notifications");
        content.Should().Contain("AccountUiRoutes.Preferences");
        content.Should().Contain("AccountUiRoutes.Security");
        content.Should().Contain("<AccountAvatar");
        content.Should().Contain("Sign out");
        content.Should().Contain(">Notifications</a>");
        content.Should().Contain(">Appearance</a>");
        content.Should().NotContain("Notification preferences");
        content.Should().NotContain("Appearance &amp; regional");

        // The trigger is the only avatar; the open menu header is name and sign-in, not a second photo.
        var triggerEnd = content.IndexOf("</button>", StringComparison.Ordinal);
        var dropdownStart = content.IndexOf("@if (IsOpen)", StringComparison.Ordinal);
        triggerEnd.Should().BeGreaterThan(0);
        dropdownStart.Should().BeGreaterThan(triggerEnd);
        content[..dropdownStart].Should().Contain("<AccountAvatar");
        content[dropdownStart..].Should().NotContain("<AccountAvatar");
        content[dropdownStart..].Should().Contain("Session.User.DisplayName");
        content[dropdownStart..].Should().Contain("SecondaryIdentity(Session.User)");
        content.Should().Contain("TopBarMenuCoordinator");
    }

    [Fact]
    public void The_user_menu_no_longer_links_to_the_retired_preferences_path()
    {
        var content = ReadRepoFile($"{ClientRoot}/Molecules/UserAccountMenu.razor");

        content.Should().NotContain("href=\"/preferences\"");
        content.Should().NotContain("href=\"/account\"");
    }

    // --- Placeholder copy removed ---

    [Fact]
    public void No_account_page_still_says_a_capability_is_unimplemented_or_a_stub()
    {
        foreach (var page in AccountPages.Append($"{ClientRoot}/Molecules/AccountSectionNav.razor"))
        {
            var content = ReadRepoFile(page);
            content.Should().NotContainEquivalentOf("not implemented yet");
            content.Should().NotContainEquivalentOf("stub");
            content.Should().NotContainEquivalentOf("coming later");
        }
    }

    [Fact]
    public void The_developer_task_list_no_longer_calls_preferences_a_stub()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Config/DeveloperTasks.razor");

        content.Should().NotContainEquivalentOf("preferences stub");
    }

    // --- Administrator versus user ownership ---

    [Fact]
    public void The_preferences_page_states_that_platform_settings_are_administrator_owned()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountPreferences.razor");

        content.Should().Contain("managed by administrators");
    }

    [Fact]
    public void No_account_page_exposes_an_administrator_owned_platform_setting()
    {
        // Personal preferences must not become a second, unaudited route into instance configuration.
        var forbidden = new[]
        {
            "PlatformSetting",
            "ListPlatformSettingsAsync",
            "SetPlatformSettingAsync",
            "Notifications.Email.SmtpHost",
            "SmtpPassword",
            "ISecretStore",
            "IntegrationSettings",
            "SchedulingDefaultTimeZoneId",
        };

        foreach (var page in AccountPages)
        {
            var content = ReadRepoFile(page);
            foreach (var term in forbidden)
                content.Should().NotContain(term, $"{page} must not surface administrator-owned configuration");
        }
    }

    // --- No controls for capabilities that do not exist ---

    [Fact]
    public void No_account_page_renders_a_control_for_an_unbuilt_notification_capability()
    {
        var forbidden = new[] { "quiet hours", "vacation", "escalation", "SMS", "mobile push", "journal" };

        foreach (var page in AccountPages)
        {
            var content = ReadRepoFile(page);
            foreach (var term in forbidden)
            {
                // Naming a capability inside an input, select, or checkbox would imply it is configurable.
                Regex.IsMatch(content, $@"<(input|select)[^>]*{Regex.Escape(term)}", RegexOptions.IgnoreCase)
                    .Should().BeFalse($"{page} must not render a control for '{term}'");
            }
        }
    }

    [Fact]
    public void The_notifications_page_uses_the_documented_empty_state_wording()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountNotifications.razor");

        content.Should().Contain("There are no personal notification preferences to configure yet.");
    }

    // --- Accessibility ---

    [Fact]
    public void Avatar_options_carry_a_visible_text_label_and_are_a_real_radio_group()
    {
        var content = ReadRepoFile($"{ClientRoot}/Organisms/AvatarPicker.razor");

        content.Should().Contain("role=\"radiogroup\"");
        content.Should().Contain("type=\"radio\"");
        content.Should().Contain("@option.DisplayName", "an illustration must never be the only label");
        content.Should().Contain("Selected", "selection state must be conveyed textually, not by colour alone");
    }

    [Fact]
    public void Avatar_upload_states_the_accepted_formats_and_size_limit()
    {
        var content = ReadRepoFile($"{ClientRoot}/Organisms/AvatarPicker.razor");

        content.Should().Contain("JPEG, PNG, or WebP");
        content.Should().Contain("MaxUploadMegabytes");
        content.Should().Contain("aria-describedby=\"avatar-upload-help\"");
    }

    [Fact]
    public void Avatar_upload_works_without_drag_and_drop_and_announces_errors()
    {
        var content = ReadRepoFile($"{ClientRoot}/Organisms/AvatarPicker.razor");

        content.Should().Contain("<InputFile", "a file input is keyboard reachable without drag and drop");
        content.Should().Contain("role=\"alert\"");
        content.Should().NotContain("ondrop");
    }

    [Fact]
    public void Avatar_previews_are_decorative_so_they_do_not_produce_noisy_alt_text()
    {
        var content = ReadRepoFile($"{ClientRoot}/Atoms/AccountAvatar.razor");

        content.Should().Contain("alt=\"\"");
        content.Should().Contain("aria-hidden=\"true\"");
    }

    [Fact]
    public void Removing_a_custom_photo_asks_for_confirmation()
    {
        var content = ReadRepoFile($"{ClientRoot}/Organisms/AvatarPicker.razor");

        content.Should().Contain("<ConfirmActionDialog");
        content.Should().Contain("Remove custom profile photo?");
    }

    [Fact]
    public void Display_name_validation_is_associated_with_its_field_rather_than_shown_as_a_toast()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountProfile.razor");

        content.Should().Contain("id=\"display-name-error\"");
        content.Should().Contain("aria-describedby=\"@(_displayNameError is null ? null : \"display-name-error\")\"");
        content.Should().Contain("aria-invalid");
        content.Should().Contain("role=\"alert\"");
    }

    [Fact]
    public void Every_account_form_control_is_labelled()
    {
        foreach (var page in AccountPages)
        {
            var content = ReadRepoFile(page);
            var inputs = Regex.Matches(content, @"<input\b[^>]*>", RegexOptions.Singleline);

            foreach (Match input in inputs)
            {
                var tag = input.Value;
                var hasId = Regex.IsMatch(tag, @"\bid=""[^""]+""");
                var hasAriaLabel = tag.Contains("aria-label", StringComparison.Ordinal);

                (hasId || hasAriaLabel).Should().BeTrue(
                    $"{page} has an input with neither an id for a label nor an aria-label: {tag}");
            }
        }
    }

    [Fact]
    public void Notification_channel_controls_are_explicitly_labelled()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountNotifications.razor");

        content.Should().Contain("aria-label=\"@($\"Email me about {preference.DisplayName}\")\"");
        content.Should().Contain("In-app");
        content.Should().Contain("Email");
    }

    [Fact]
    public void The_notifications_layout_stacks_rather_than_scrolling_horizontally_on_narrow_screens()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountNotifications.razor");

        // A list with responsive flex direction, not a fixed-width table.
        content.Should().Contain("flex-col gap-3 md:flex-row");
        content.Should().NotContain("<table");
        content.Should().NotContain("overflow-x-auto");
    }

    [Fact]
    public void Section_navigation_marks_the_current_page_for_assistive_technology()
    {
        var content = ReadRepoFile($"{ClientRoot}/Molecules/AccountSectionNav.razor");

        content.Should().Contain("aria-current=");
        content.Should().Contain("aria-label=\"Account sections\"");
    }

    [Fact]
    public void Section_navigation_does_not_use_the_reserved_razor_section_directive_as_a_loop_variable()
    {
        var content = ReadRepoFile($"{ClientRoot}/Molecules/AccountSectionNav.razor");

        // `@section` is a Razor layout directive. Markup like href="@section.Href" fails the
        // compiler with RZ9979 / RZ2005 / RZ1011. Bind a differently named loop variable instead.
        content.Should().NotContain("@section.");
        content.Should().NotContain("foreach (var section ");
    }

    [Fact]
    public void The_timezone_selector_is_keyboard_usable_and_offers_detection_as_a_convenience()
    {
        var content = ReadRepoFile($"{ClientRoot}/Molecules/TimeZoneSelector.razor");

        content.Should().Contain("<SearchableList");
        content.Should().Contain("appertureShell.getTimeZone");
        content.Should().Contain("Use this browser");
        content.Should().NotContain("<select");
    }

    [Fact]
    public void Appearance_and_clock_choices_are_real_radio_groups_with_text_labels()
    {
        var preferences = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountPreferences.razor");
        var themeCards = ReadRepoFile($"{ClientRoot}/Molecules/ThemeCardGroup.razor");
        var segmented = ReadRepoFile($"{ClientRoot}/Atoms/SegmentedControl.razor");

        preferences.Should().Contain("<ThemeCardGroup");
        preferences.Should().Contain("<SegmentedControl");
        preferences.Should().Contain("aria-label=\"Clock format\"");
        preferences.Should().Contain("ui-settings-grid");
        preferences.Should().NotContain("· Selected");

        themeCards.Should().Contain("role=\"radiogroup\"");
        themeCards.Should().Contain("ui-theme-card__badge");
        themeCards.Should().Contain("sr-only");
        themeCards.Should().Contain("Selected");
        themeCards.Should().NotContain("· Selected");

        segmented.Should().Contain("role=\"radiogroup\"");
        segmented.Should().Contain("sr-only");
        segmented.Should().Contain("Selected");
        segmented.Should().NotContain("· Selected");
    }

    // --- Identity ownership ---

    [Fact]
    public void The_profile_page_presents_provider_controlled_fields_as_read_only()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountProfile.razor");

        content.Should().Contain("come from the identity provider");
        // Email and principal are rendered in a definition list, never as editable inputs.
        content.Should().NotContain("id=\"email-address\"");
        content.Should().NotContain("id=\"user-principal\"");
    }

    [Fact]
    public void The_security_page_attributes_directory_managed_features_to_the_identity_provider()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountSecurity.razor");

        content.Should().Contain("Managed by Microsoft Entra ID");
        content.Should().Contain("managed by your organization's directory");
    }

    [Fact]
    public void The_security_page_does_not_offer_unsupported_authentication_features()
    {
        var content = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountSecurity.razor");

        // MFA and passkeys are described as provider-owned, never rendered as Apperture controls.
        Regex.IsMatch(content, @"<(input|button|select)[^>]*(passkey|two-factor|enroll)", RegexOptions.IgnoreCase)
            .Should().BeFalse();
    }

    [Fact]
    public void The_security_page_does_not_let_ordinary_users_reenable_local_sign_in()
    {
        var security = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountSecurity.razor");
        security.Should().Contain("Primary sign-in method");
        security.Should().Contain("The password itself has not been deleted");
        security.Should().NotContain("Enable local sign-in");
        security.Should().NotContain("Enable local login");
    }

    [Fact]
    public void User_detail_gates_enable_local_sign_in_on_credential_manage()
    {
        var detail = ReadRepoFile($"{ClientRoot}/Pages/Config/Governance/UserDetail.razor");
        detail.Should().Contain("CanManageCredentials");
        detail.Should().Contain("Enable local sign-in");
        detail.Should().Contain("signin/step-up");
        detail.Should().Contain("Strong authentication is not configured");
    }

    [Fact]
    public void Technical_identifiers_are_kept_in_progressive_disclosure_rather_than_as_primary_content()
    {
        var profile = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountProfile.razor");
        var notifications = ReadRepoFile($"{ClientRoot}/Pages/Account/AccountNotifications.razor");

        profile.Should().Contain("<TechnicalDetails");
        profile.Should().Contain("Label=\"User id\"");
        notifications.Should().Contain("Label=\"Notification id\"");
    }

    // --- No planning terminology in operator-facing copy ---

    [Fact]
    public void Account_surfaces_contain_no_internal_planning_labels()
    {
        var patterns = new[]
        {
            @"\bAUP-\d",
            @"\bSDK-\d",
            @"\bSWM-\d",
            @"\bBDS-\d",
            @"\bUIX-\d",
            @"\bRR-\d",
            @"\bPFB-\d",
            @"\bEpic\s+\d",
        };

        var surfaces = AccountPages
            .Append($"{ClientRoot}/Pages/Account/AccountRedirect.razor")
            .Append($"{ClientRoot}/Molecules/AccountSectionNav.razor")
            .Append($"{ClientRoot}/Molecules/TimeZoneSelector.razor")
            .Append($"{ClientRoot}/Organisms/AvatarPicker.razor")
            .Append($"{ClientRoot}/Atoms/AccountAvatar.razor")
            .Append($"{ClientRoot}/Atoms/BuiltInAvatarGlyph.razor");

        foreach (var surface in surfaces)
        {
            var content = ReadRepoFile(surface);
            foreach (var pattern in patterns)
            {
                Regex.IsMatch(content, pattern).Should().BeFalse(
                    $"{surface} must not carry internal planning labels in any form, including comments");
            }
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return dir!.FullName;
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    private static bool FileExists(string relativePath) =>
        File.Exists(Path.Combine(RepoRoot(), relativePath));
}
