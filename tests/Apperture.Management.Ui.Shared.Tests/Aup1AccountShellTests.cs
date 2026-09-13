using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// Account area routing, breadcrumbs, appearance precedence, timezone catalog, and clock formatting.
/// </summary>
public sealed class Aup1AccountShellTests
{
    // --- Routes ---

    [Fact]
    public void The_account_area_exposes_four_sections()
    {
        AccountUiRoutes.Sections.Select(s => s.Href).Should().Equal(
            AccountUiRoutes.Profile,
            AccountUiRoutes.Notifications,
            AccountUiRoutes.Preferences,
            AccountUiRoutes.Account);
    }

    [Fact]
    public void Every_section_has_a_label_and_a_description()
    {
        AccountUiRoutes.Sections.Should().OnlyContain(s =>
            !string.IsNullOrWhiteSpace(s.Label) && !string.IsNullOrWhiteSpace(s.Description));
    }

    [Fact]
    public void The_account_root_resolves_to_the_account_section()
    {
        var section = AccountUiRoutes.ResolveSection(AccountUiRoutes.Root);
        section!.Href.Should().Be(AccountUiRoutes.Account);
        section.Label.Should().Be("Account");
    }

    [Theory]
    [InlineData("/account/profile", "/account/profile")]
    [InlineData("/account/notifications", "/account/notifications")]
    [InlineData("/account/preferences", "/account/preferences")]
    [InlineData("/account/security", "/account")]
    [InlineData("/account", "/account")]
    [InlineData("/ACCOUNT/PROFILE", "/account/profile")]
    [InlineData("/account/profile/", "/account/profile")]
    [InlineData("/account/profile?tab=x", "/account/profile")]
    public void Section_resolution_tolerates_casing_trailing_slashes_and_query_strings(string path, string expected)
    {
        AccountUiRoutes.ResolveSection(path)!.Href.Should().Be(expected);
    }

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/config/platform-settings")]
    [InlineData("/me/notifications")]
    [InlineData("/preferences")]
    [InlineData("")]
    [InlineData(null)]
    public void Paths_outside_the_account_area_do_not_resolve_to_a_section(string? path)
    {
        AccountUiRoutes.ResolveSection(path).Should().BeNull();
        AccountUiRoutes.IsAccountPath(path).Should().BeFalse();
    }

    [Fact]
    public void The_notification_inbox_is_a_separate_surface_from_notification_preferences()
    {
        AccountUiRoutes.NotificationInbox.Should().NotBe(AccountUiRoutes.Notifications);
    }

    // --- Breadcrumbs ---

    [Theory]
    [InlineData("/account/profile", "Profile")]
    [InlineData("/account/notifications", "Notifications")]
    [InlineData("/account/preferences", "Appearance & regional")]
    public void Each_nested_account_section_resolves_a_titled_breadcrumb_under_an_account_parent(string path, string title)
    {
        var orientation = ManagementBreadcrumbResolver.ResolveOrientation(path);

        orientation.Title.Should().Be(title);
        orientation.Parents.Should().ContainSingle(p => p.Label == "Account" && p.Href == AccountUiRoutes.Root);
    }

    [Theory]
    [InlineData("/account")]
    [InlineData("/account/security")]
    public void The_account_section_avoids_a_duplicate_account_breadcrumb(string path)
    {
        var orientation = ManagementBreadcrumbResolver.ResolveOrientation(path);

        orientation.Title.Should().Be("Account");
        orientation.Parents.Should().BeEmpty();
    }

    [Fact]
    public void The_legacy_preferences_path_still_resolves_to_the_appearance_section()
    {
        var orientation = ManagementBreadcrumbResolver.ResolveOrientation("/preferences");

        orientation.Title.Should().Be("Appearance & regional");
    }

    [Fact]
    public void The_notification_inbox_now_resolves_a_breadcrumb_instead_of_falling_through()
    {
        var orientation = ManagementBreadcrumbResolver.ResolveOrientation("/me/notifications");

        orientation.Title.Should().Be("My notifications");
    }

    [Fact]
    public void The_account_root_keeps_its_own_breadcrumb()
    {
        ManagementBreadcrumbResolver.ResolveOrientation("/account").Title.Should().Be("Account");
    }

    // --- Appearance precedence ---

    [Fact]
    public void The_account_preference_outranks_the_browser_value()
    {
        var (preference, source) = AppearancePreferenceResolution.Resolve("Dark", "Light");

        preference.Should().Be(ThemePreference.Dark);
        source.Should().Be(AppearancePreferenceSource.AccountPreference);
    }

    [Fact]
    public void The_browser_value_applies_when_the_account_has_no_stored_preference()
    {
        var (preference, source) = AppearancePreferenceResolution.Resolve(null, "Light");

        preference.Should().Be(ThemePreference.Light);
        source.Should().Be(AppearancePreferenceSource.Browser);
    }

    [Fact]
    public void The_platform_default_applies_when_nothing_is_stored_anywhere()
    {
        var (preference, source) = AppearancePreferenceResolution.Resolve(null, null);

        preference.Should().Be(ThemePreference.System);
        source.Should().Be(AppearancePreferenceSource.PlatformDefault);
    }

    [Fact]
    public void An_account_preference_of_System_still_wins_over_a_browser_choice()
    {
        // System is a real choice meaning "follow my device", not an absence of one.
        var (preference, source) = AppearancePreferenceResolution.Resolve("System", "Dark");

        preference.Should().Be(ThemePreference.System);
        source.Should().Be(AppearancePreferenceSource.AccountPreference);
    }

    [Fact]
    public void A_corrupt_account_preference_falls_through_to_the_browser_value()
    {
        var (preference, source) = AppearancePreferenceResolution.Resolve("Chartreuse", "Dark");

        preference.Should().Be(ThemePreference.Dark);
        source.Should().Be(AppearancePreferenceSource.Browser);
    }

    [Fact]
    public void A_corrupt_value_at_every_tier_falls_back_to_the_platform_default()
    {
        var (preference, source) = AppearancePreferenceResolution.Resolve("Chartreuse", "Neon");

        preference.Should().Be(ThemePreference.System);
        source.Should().Be(AppearancePreferenceSource.PlatformDefault);
    }

    [Fact]
    public void The_platform_default_appearance_is_System()
    {
        AppearancePreferenceResolution.PlatformDefault.Should().Be(ThemePreference.System);
    }

    [Fact]
    public void Every_precedence_source_has_operator_facing_wording()
    {
        foreach (var source in Enum.GetValues<AppearancePreferenceSource>())
            AppearancePreferenceResolution.Describe(source).Should().NotBeNullOrWhiteSpace();
    }

    // --- Timezone catalog ---

    [Fact]
    public void The_catalog_offers_only_zones_this_host_recognizes()
    {
        // A selectable id the server would reject is a bug the operator cannot work around.
        OperatorTimeZoneCatalog.All.Should().NotBeEmpty();

        var unrecognized = OperatorTimeZoneCatalog.All
            .Where(c => !OperatorTimestampFormat.TryFind(c.Id, out _))
            .Select(c => c.Id)
            .ToList();
        unrecognized.Should().BeEmpty();
    }

    [Fact]
    public void The_suggested_list_is_short_enough_to_scan()
    {
        OperatorTimeZoneCatalog.Suggested.Should().NotBeEmpty();
        OperatorTimeZoneCatalog.Suggested.Count.Should().BeLessThan(25);
    }

    [Fact]
    public void An_empty_search_returns_the_curated_suggestions()
    {
        OperatorTimeZoneCatalog.Search("").Should().BeEquivalentTo(OperatorTimeZoneCatalog.Suggested);
        OperatorTimeZoneCatalog.Search(null).Should().BeEquivalentTo(OperatorTimeZoneCatalog.Suggested);
    }

    [Fact]
    public void Catalog_identifiers_are_canonicalized_to_the_iana_form()
    {
        // Windows hosts enumerate zones as "South Africa Standard Time", but browsers report IANA and
        // Apperture's schedule model prefers IANA. Offering the Windows form would store a value that
        // disagrees with browser detection.
        var ids = OperatorTimeZoneCatalog.All.Select(c => c.Id).ToList();

        ids.Should().Contain("Africa/Johannesburg");
        ids.Should().Contain("America/New_York");
        ids.Should().NotContain("South Africa Standard Time");
        ids.Should().NotContain("Eastern Standard Time");
    }

    [Fact]
    public void Searching_matches_on_the_identifier()
    {
        var results = OperatorTimeZoneCatalog.Search("Johannesburg");

        results.Should().NotBeEmpty();
        results.Should().Contain(c => c.Id == "Africa/Johannesburg");
    }

    [Fact]
    public void Searching_treats_underscores_in_identifiers_as_spaces()
    {
        // An operator types "Los Angeles", not "Los_Angeles".
        var results = OperatorTimeZoneCatalog.Search("Los Angeles");

        results.Should().Contain(c => c.Id == "America/Los_Angeles");
    }

    [Fact]
    public void Catalog_identifiers_are_unique()
    {
        OperatorTimeZoneCatalog.All.Select(c => c.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_curated_suggestion_is_present_in_the_full_catalog()
    {
        var allIds = OperatorTimeZoneCatalog.All.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        OperatorTimeZoneCatalog.Suggested.Should().OnlyContain(c => allIds.Contains(c.Id));
        OperatorTimeZoneCatalog.Suggested.Should().Contain(c => c.Id == OperatorTimestampFormat.UtcId);
    }

    [Fact]
    public void Searching_for_nonsense_returns_nothing_rather_than_everything()
    {
        OperatorTimeZoneCatalog.Search("Mars/Olympus_Mons").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("../../etc/localtime")]
    [InlineData("")]
    [InlineData(null)]
    public void Unrecognized_timezone_identifiers_are_reported_as_unknown(string? timeZoneId)
    {
        OperatorTimeZoneCatalog.IsKnown(timeZoneId).Should().BeFalse();
    }

    [Fact]
    public void An_absent_timezone_is_described_as_following_the_browser()
    {
        OperatorTimeZoneCatalog.DescribeId(null).Should().Be("Follow this browser");
    }

    [Fact]
    public void Choice_labels_lead_with_the_identifier_and_include_the_utc_offset()
    {
        var choice = new OperatorTimeZoneChoice("Africa/Johannesburg", "South Africa Standard Time", TimeSpan.FromHours(2));

        choice.Label.Should().Be("Africa/Johannesburg (UTC+02:00)");
    }

    [Fact]
    public void Negative_offsets_are_labelled_with_a_minus_sign()
    {
        var choice = new OperatorTimeZoneChoice("America/New_York", "Eastern Standard Time", TimeSpan.FromHours(-5));

        choice.Label.Should().Be("America/New_York (UTC-05:00)");
    }

    [Fact]
    public void Half_hour_offsets_are_labelled_precisely()
    {
        var choice = new OperatorTimeZoneChoice("Asia/Kolkata", "India Standard Time", new TimeSpan(5, 30, 0));

        choice.Label.Should().Be("Asia/Kolkata (UTC+05:30)");
    }

    // --- Clock format ---

    [Fact]
    public void Timestamps_render_on_a_24_hour_clock_by_default()
    {
        var value = new DateTimeOffset(2026, 8, 18, 19, 5, 30, TimeSpan.Zero);

        var formatted = OperatorTimestampFormat.FormatPrimary(
            value,
            OperatorTimestampFormat.UtcId,
            OperatorDisplayTimeZoneSource.UtcFallback);

        formatted.Should().Contain("19:05:30");
    }

    [Fact]
    public void A_twelve_hour_preference_renders_a_meridiem_suffix()
    {
        var value = new DateTimeOffset(2026, 8, 18, 19, 5, 30, TimeSpan.Zero);

        var formatted = OperatorTimestampFormat.FormatPrimary(
            value,
            OperatorTimestampFormat.UtcId,
            OperatorDisplayTimeZoneSource.UtcFallback,
            clockFormat: OperatorClockFormat.Hour12);

        formatted.Should().Contain("7:05:30 PM");
        formatted.Should().NotContain("19:05:30");
    }

    [Fact]
    public void The_utc_detail_line_stays_on_a_24_hour_clock_because_it_is_execution_truth()
    {
        var value = new DateTimeOffset(2026, 8, 18, 19, 5, 30, TimeSpan.Zero);

        var result = OperatorTimestampFormat.Format(
            value,
            OperatorTimestampFormat.UtcId,
            OperatorDisplayTimeZoneSource.UtcFallback,
            showUtcDetail: true,
            clockFormat: OperatorClockFormat.Hour12);

        result.UtcDetail.Should().Be("2026-08-18 19:05:30 UTC");
    }

    [Fact]
    public void The_date_portion_stays_iso_regardless_of_the_clock_preference()
    {
        var value = new DateTimeOffset(2026, 8, 18, 19, 5, 30, TimeSpan.Zero);

        foreach (var clock in Enum.GetValues<OperatorClockFormat>())
        {
            var formatted = OperatorTimestampFormat.FormatPrimary(
                value,
                OperatorTimestampFormat.UtcId,
                OperatorDisplayTimeZoneSource.UtcFallback,
                clockFormat: clock);
            formatted.Should().StartWith("2026-08-18");
        }
    }

    [Theory]
    [InlineData("Hour12", OperatorClockFormat.Hour12)]
    [InlineData("hour12", OperatorClockFormat.Hour12)]
    [InlineData("Hour24", OperatorClockFormat.Hour24)]
    [InlineData("System", OperatorClockFormat.Hour24)]
    [InlineData("nonsense", OperatorClockFormat.Hour24)]
    [InlineData(null)]
    public void Stored_clock_preferences_map_to_a_format_and_default_to_24_hour(
        string? stored,
        OperatorClockFormat expected = OperatorClockFormat.Hour24)
    {
        OperatorTimestampFormat.ParseClockFormatOrDefault(stored).Should().Be(expected);
    }
}
