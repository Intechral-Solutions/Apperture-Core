using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Accounts;

/// <summary>
/// The account domain model: the curated avatar catalog, avatar storage round-tripping, typed
/// preference parsing, and display-name rules.
/// </summary>
public sealed class AccountDomainModelTests
{
    // --- Built-in avatar catalog ---

    [Fact]
    public void The_catalog_stays_deliberately_small()
    {
        // AUP-1 ships a curated set, not a marketplace. A growing catalog is a product decision that
        // should require changing this expectation on purpose.
        BuiltInAvatarCatalog.Options.Should().HaveCount(3);
    }

    [Fact]
    public void The_default_option_is_option_a_and_comes_first()
    {
        BuiltInAvatarCatalog.DefaultOptionId.Should().Be("option-a");
        BuiltInAvatarCatalog.DefaultOption.Id.Should().Be("option-a");
        BuiltInAvatarCatalog.Options[0].Id.Should().Be(BuiltInAvatarCatalog.DefaultOptionId);
        BuiltInAvatarCatalog.Options.Select(o => o.DisplayName).Should().Equal("Option A", "Option B", "Option C");
        BuiltInAvatarCatalog.ImagePath("option-a").Should().Be("images/account/avatars/option-a.jpg");
        BuiltInAvatarCatalog.ImagePath("option-b").Should().Be("images/account/avatars/option-b.jpg");
        BuiltInAvatarCatalog.ImagePath("option-c").Should().Be("images/account/avatars/option-c.jpg");
    }

    [Fact]
    public void Option_ids_are_unique()
    {
        BuiltInAvatarCatalog.Options.Select(o => o.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Option_labels_describe_visual_style_rather_than_asserting_an_identity()
    {
        // Choosing an illustration says nothing about the operator, so the labels must not read as
        // demographic categories.
        var forbidden = new[] { "male", "female", "man", "woman", "boy", "girl", "masculine", "feminine" };

        foreach (var option in BuiltInAvatarCatalog.Options)
        {
            foreach (var term in forbidden)
            {
                option.DisplayName.Should().NotContainEquivalentOf(term);
                option.Description.Should().NotContainEquivalentOf(term);
            }
        }
    }

    [Fact]
    public void Every_option_has_a_label_and_a_description()
    {
        BuiltInAvatarCatalog.Options.Should().OnlyContain(o =>
            !string.IsNullOrWhiteSpace(o.DisplayName) && !string.IsNullOrWhiteSpace(o.Description));
    }

    [Theory]
    [InlineData("dragon")]
    [InlineData("NEUTRAL")]
    [InlineData("../neutral")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_option_ids_are_not_recognized(string? optionId)
    {
        BuiltInAvatarCatalog.IsKnownOptionId(optionId).Should().BeFalse();
    }

    [Fact]
    public void RequireOptionId_throws_for_an_option_outside_the_catalog()
    {
        var act = () => BuiltInAvatarCatalog.RequireOptionId("dragon");

        act.Should().Throw<ArgumentException>();
    }

    // --- Avatar storage round-trip ---

    [Fact]
    public void An_unset_avatar_resolves_to_the_neutral_default()
    {
        UserAvatar.FromStorage(null, null, null).Should().Be(UserAvatar.Default);
    }

    [Fact]
    public void A_custom_avatar_round_trips_through_storage()
    {
        var stored = UserAvatar.ForCustomImage("avt_1234");

        var restored = UserAvatar.FromStorage(stored.StorageKind, stored.BuiltInOptionId, stored.ImageKey);

        restored.Should().Be(stored);
        restored.Kind.Should().Be(UserAvatarKind.Custom);
    }

    [Fact]
    public void A_built_in_avatar_round_trips_through_storage()
    {
        var stored = UserAvatar.ForBuiltIn("option-c");

        var restored = UserAvatar.FromStorage(stored.StorageKind, stored.BuiltInOptionId, stored.ImageKey);

        restored.Should().Be(stored);
        restored.BuiltInOptionId.Should().Be("option-c");
    }

    [Fact]
    public void A_custom_avatar_row_with_no_image_key_falls_back_to_the_default()
    {
        // Guards against a half-written row rendering as a broken image.
        UserAvatar.FromStorage(UserAvatarStorageKinds.Custom, null, null).Should().Be(UserAvatar.Default);
    }

    [Fact]
    public void A_built_in_avatar_row_naming_a_retired_option_falls_back_to_the_default()
    {
        UserAvatar.FromStorage(UserAvatarStorageKinds.BuiltIn, "retired-option", null).Should().Be(UserAvatar.Default);
    }

    [Theory]
    [InlineData("neutral", "option-a")]
    [InlineData("neutral-bold", "option-a")]
    [InlineData("long-hair", "option-b")]
    [InlineData("short-hair", "option-c")]
    [InlineData("option-a", "option-a")]
    public void Legacy_illustration_ids_canonicalize_onto_the_three_options(string stored, string expected)
    {
        BuiltInAvatarCatalog.CanonicalizeOptionId(stored).Should().Be(expected);
        UserAvatar.FromStorage(UserAvatarStorageKinds.BuiltIn, stored, null).BuiltInOptionId.Should().Be(expected);
        UserAvatar.ForBuiltIn(stored).BuiltInOptionId.Should().Be(expected);
    }

    [Fact]
    public void The_version_token_changes_when_a_custom_image_is_replaced()
    {
        var first = UserAvatar.ForCustomImage("avt_aaa");
        var second = UserAvatar.ForCustomImage("avt_bbb");

        // Cache-busting depends on this: an unchanged token would serve a stale avatar.
        second.VersionToken.Should().NotBe(first.VersionToken);
    }

    [Fact]
    public void The_version_token_changes_when_a_different_illustration_is_chosen()
    {
        UserAvatar.ForBuiltIn("option-b").VersionToken
            .Should().NotBe(UserAvatar.ForBuiltIn("option-a").VersionToken);
    }

    [Fact]
    public void ForCustomImage_rejects_a_blank_image_key()
    {
        var act = () => UserAvatar.ForCustomImage("  ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_user_loaded_without_an_avatar_still_reports_the_default()
    {
        var user = new User(new UserId("usr_1"), "Someone", [], [], false);

        user.EffectiveAvatar.Should().Be(UserAvatar.Default);
    }

    // --- Typed preferences ---

    [Fact]
    public void Default_preferences_follow_the_system_for_both_appearance_and_clock()
    {
        UserPreferences.Default.Appearance.Should().Be(AppearancePreference.System);
        UserPreferences.Default.TimeFormat.Should().Be(TimeFormatPreference.System);
    }

    [Theory]
    [InlineData("Dark", AppearancePreference.Dark)]
    [InlineData("dark", AppearancePreference.Dark)]
    [InlineData(" Light ", AppearancePreference.Light)]
    [InlineData("System", AppearancePreference.System)]
    public void Appearance_parses_case_insensitively(string input, AppearancePreference expected)
    {
        UserPreferences.TryParseAppearance(input, out var parsed).Should().BeTrue();
        parsed.Should().Be(expected);
    }

    [Theory]
    [InlineData("Neon")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("99")]
    public void Strict_appearance_parsing_reports_failure_instead_of_substituting_a_default(string? input)
    {
        // The strict parse is what lets the API reject an unknown value rather than silently coercing it.
        UserPreferences.TryParseAppearance(input, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("Hour12", TimeFormatPreference.Hour12)]
    [InlineData("hour24", TimeFormatPreference.Hour24)]
    [InlineData("System", TimeFormatPreference.System)]
    public void Time_format_parses_case_insensitively(string input, TimeFormatPreference expected)
    {
        UserPreferences.TryParseTimeFormat(input, out var parsed).Should().BeTrue();
        parsed.Should().Be(expected);
    }

    [Theory]
    [InlineData("Hour36")]
    [InlineData("12h")]
    [InlineData(null)]
    public void Strict_time_format_parsing_reports_failure(string? input)
    {
        UserPreferences.TryParseTimeFormat(input, out _).Should().BeFalse();
    }

    [Fact]
    public void A_corrupt_stored_row_resolves_to_defaults_rather_than_failing_to_load()
    {
        var preferences = UserPreferences.FromStorage("Chartreuse", "Hour36");

        preferences.Should().Be(UserPreferences.Default);
    }

    [Fact]
    public void A_valid_stored_row_round_trips()
    {
        var preferences = UserPreferences.FromStorage("Dark", "Hour12");

        preferences.Appearance.Should().Be(AppearancePreference.Dark);
        preferences.TimeFormat.Should().Be(TimeFormatPreference.Hour12);
    }

    // --- Display name rules ---

    [Theory]
    [InlineData("  Ada Lovelace  ", "Ada Lovelace")]
    [InlineData("Ada", "Ada")]
    [InlineData("Ada  Lovelace", "Ada  Lovelace")]
    public void Display_names_are_trimmed_but_interior_spacing_is_preserved(string input, string expected)
    {
        DisplayNameRules.TryNormalize(input, out var normalized, out var error).Should().BeTrue();
        normalized.Should().Be(expected);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("     ")]
    public void A_blank_display_name_is_rejected_with_a_reason(string? input)
    {
        DisplayNameRules.TryNormalize(input, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_display_name_at_the_length_limit_is_accepted()
    {
        var atLimit = new string('a', DisplayNameRules.MaxLength);

        DisplayNameRules.TryNormalize(atLimit, out _, out _).Should().BeTrue();
    }

    [Fact]
    public void A_display_name_over_the_length_limit_is_rejected()
    {
        var overLimit = new string('a', DisplayNameRules.MaxLength + 1);

        DisplayNameRules.TryNormalize(overLimit, out _, out var error).Should().BeFalse();
        error.Should().Contain(DisplayNameRules.MaxLength.ToString());
    }

    [Theory]
    [InlineData("Line\nBreak")]
    [InlineData("Tab\tSeparated")]
    [InlineData("Bell\u0007Character")]
    public void Control_characters_are_rejected(string input)
    {
        DisplayNameRules.TryNormalize(input, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Non_latin_display_names_are_accepted()
    {
        DisplayNameRules.TryNormalize("Sipho Nkosi 中文 مرحبا", out var normalized, out _).Should().BeTrue();
        normalized.Should().Be("Sipho Nkosi 中文 مرحبا");
    }
}
