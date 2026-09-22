using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.Accounts;

/// <summary>
/// Behaviour of the self-service account operations: what an operator may change, what is rejected,
/// what is audited, and that nothing reaches beyond the account the caller was resolved to.
/// </summary>
public sealed class AccountProfileServiceTests
{
    private static readonly UserId Owner = new("usr_owner");
    private static readonly UserId Other = new("usr_other");

    // --- Profile ---

    [Fact]
    public async Task GetProfileAsync_returns_the_stored_display_name_and_identity_links()
    {
        var fixture = new Fixture();

        var profile = await fixture.Service.GetProfileAsync(Owner);

        profile.Should().NotBeNull();
        profile!.UserId.Should().Be(Owner.Value);
        profile.DisplayName.Should().Be("Owner Person");
        profile.DisplayNameEditable.Should().BeTrue();
        profile.IdentityLinks.Should().ContainSingle(l => l.Source == nameof(IdentitySource.Local));
    }

    [Fact]
    public async Task GetProfileAsync_returns_null_for_an_unknown_user()
    {
        var fixture = new Fixture();

        var profile = await fixture.Service.GetProfileAsync(new UserId("usr_missing"));

        profile.Should().BeNull();
    }

    [Fact]
    public async Task Recovery_access_display_name_is_not_editable_and_cannot_be_changed()
    {
        var fixture = new Fixture();
        await fixture.Users.UpdateAsync(new User(
            Owner,
            EmergencyAccessIdentity.DisplayName,
            [new IdentityLink(IdentitySource.Local, EmergencyAccessIdentity.LocalUsername, null)],
            [],
            true));

        var profile = await fixture.Service.GetProfileAsync(Owner);

        profile!.DisplayNameEditable.Should().BeFalse();
        var act = () => fixture.Service.UpdateDisplayNameAsync(Owner, "Hacked").AsTask();
        var ex = await act.Should().ThrowAsync<EmergencyAccessException>();
        ex.Which.Code.Should().Be(EmergencyAccessErrorCodes.UnsafeOperation);
        fixture.Users.Get(Owner.Value).DisplayName.Should().Be(EmergencyAccessIdentity.DisplayName);
    }

    [Fact]
    public async Task Recovery_access_avatar_cannot_be_changed()
    {
        var fixture = new Fixture();
        await fixture.Users.UpdateAsync(new User(
            Owner,
            EmergencyAccessIdentity.DisplayName,
            [new IdentityLink(IdentitySource.Local, EmergencyAccessIdentity.LocalUsername, null)],
            [],
            true));

        var act = () => fixture.Service.SelectBuiltInAvatarAsync(Owner, "option-b").AsTask();
        var ex = await act.Should().ThrowAsync<EmergencyAccessException>();
        ex.Which.Code.Should().Be(EmergencyAccessErrorCodes.UnsafeOperation);
    }

    [Fact]
    public async Task UpdateDisplayNameAsync_trims_whitespace_and_persists_the_change()
    {
        var fixture = new Fixture();

        var profile = await fixture.Service.UpdateDisplayNameAsync(Owner, "   Renamed Operator   ");

        profile.DisplayName.Should().Be("Renamed Operator");
        fixture.Users.Get(Owner.Value).DisplayName.Should().Be("Renamed Operator");
    }

    [Fact]
    public async Task UpdateDisplayNameAsync_writes_an_audit_record_with_both_values()
    {
        var fixture = new Fixture();

        await fixture.Service.UpdateDisplayNameAsync(Owner, "Renamed Operator");

        var record = fixture.Audit.Records.Should()
            .ContainSingle(r => r.Action == AccountAuditActionNames.DisplayNameUpdated).Subject;
        record.ActorId.Value.Should().Be(Owner.Value);
        record.Details.Should().Contain("Owner Person").And.Contain("Renamed Operator");
    }

    [Fact]
    public async Task UpdateDisplayNameAsync_does_not_audit_when_the_name_is_unchanged()
    {
        var fixture = new Fixture();

        await fixture.Service.UpdateDisplayNameAsync(Owner, "Owner Person");

        fixture.Audit.Records.Should().BeEmpty("a no-op edit is not an account change worth recording");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateDisplayNameAsync_rejects_a_blank_name(string? candidate)
    {
        var fixture = new Fixture();

        var act = async () => await fixture.Service.UpdateDisplayNameAsync(Owner, candidate);

        await act.Should().ThrowAsync<ArgumentException>();
        fixture.Users.Get(Owner.Value).DisplayName.Should().Be("Owner Person");
    }

    [Fact]
    public async Task UpdateDisplayNameAsync_rejects_a_name_over_the_length_limit()
    {
        var fixture = new Fixture();

        var act = async () => await fixture.Service
            .UpdateDisplayNameAsync(Owner, new string('a', DisplayNameRules.MaxLength + 1));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpdateDisplayNameAsync_rejects_control_characters()
    {
        var fixture = new Fixture();

        var act = async () => await fixture.Service.UpdateDisplayNameAsync(Owner, "Bad\u0007Name");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Updating_one_profile_never_touches_another_operators_record()
    {
        var fixture = new Fixture();

        await fixture.Service.UpdateDisplayNameAsync(Owner, "Renamed Operator");

        fixture.Users.Get(Other.Value).DisplayName.Should().Be("Other Person");
    }

    // --- Preferences ---

    [Fact]
    public async Task GetPreferencesAsync_resolves_platform_defaults_when_no_row_exists()
    {
        var fixture = new Fixture();

        var preferences = await fixture.Service.GetPreferencesAsync(Owner);

        preferences.Appearance.Should().Be(AppearancePreference.System);
        preferences.TimeFormat.Should().Be(TimeFormatPreference.System);
        preferences.DisplayTimeZoneId.Should().BeNull();
    }

    [Fact]
    public async Task UpdatePreferencesAsync_round_trips_every_supported_value()
    {
        var fixture = new Fixture();

        await fixture.Service.UpdatePreferencesAsync(
            Owner,
            new AccountPreferencesUpdate("Dark", "Hour12", "UTC"));

        var reloaded = await fixture.Service.GetPreferencesAsync(Owner);
        reloaded.Appearance.Should().Be(AppearancePreference.Dark);
        reloaded.TimeFormat.Should().Be(TimeFormatPreference.Hour12);
        reloaded.DisplayTimeZoneId.Should().Be("UTC");
    }

    [Fact]
    public async Task UpdatePreferencesAsync_stores_the_timezone_on_the_user_record()
    {
        var fixture = new Fixture();

        await fixture.Service.UpdatePreferencesAsync(
            Owner,
            new AccountPreferencesUpdate("System", "System", "UTC"));

        // The timezone reuses the existing user column rather than introducing a second store.
        fixture.Users.Get(Owner.Value).DisplayTimeZoneId.Should().Be("UTC");
    }

    [Fact]
    public async Task UpdatePreferencesAsync_treats_a_null_timezone_as_follow_the_browser()
    {
        var fixture = new Fixture();
        await fixture.Service.UpdatePreferencesAsync(Owner, new AccountPreferencesUpdate("System", "System", "UTC"));

        await fixture.Service.UpdatePreferencesAsync(Owner, new AccountPreferencesUpdate("System", "System", null));

        fixture.Users.Get(Owner.Value).DisplayTimeZoneId.Should().BeNull();
    }

    [Theory]
    [InlineData("Neon")]
    [InlineData("light-mode")]
    [InlineData("")]
    [InlineData(null)]
    public async Task UpdatePreferencesAsync_rejects_an_unknown_appearance(string? appearance)
    {
        var fixture = new Fixture();

        var act = async () => await fixture.Service
            .UpdatePreferencesAsync(Owner, new AccountPreferencesUpdate(appearance, "System", null));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("Hour36")]
    [InlineData("12h")]
    [InlineData(null)]
    public async Task UpdatePreferencesAsync_rejects_an_unknown_time_format(string? timeFormat)
    {
        var fixture = new Fixture();

        var act = async () => await fixture.Service
            .UpdatePreferencesAsync(Owner, new AccountPreferencesUpdate("System", timeFormat, null));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("../../etc/localtime")]
    [InlineData("Not A Zone")]
    public async Task UpdatePreferencesAsync_rejects_an_unrecognized_timezone(string timeZoneId)
    {
        var fixture = new Fixture();

        var act = async () => await fixture.Service
            .UpdatePreferencesAsync(Owner, new AccountPreferencesUpdate("System", "System", timeZoneId));

        await act.Should().ThrowAsync<ArgumentException>();
        fixture.Users.Get(Owner.Value).DisplayTimeZoneId.Should().BeNull();
    }

    [Fact]
    public async Task UpdatePreferencesAsync_does_not_write_operational_audit_records()
    {
        var fixture = new Fixture();

        await fixture.Service.UpdatePreferencesAsync(Owner, new AccountPreferencesUpdate("Dark", "Hour12", "UTC"));

        // Appearance and clock choices change nothing operationally, so they stay out of the audit log.
        fixture.Audit.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Preferences_are_stored_per_user()
    {
        var fixture = new Fixture();

        await fixture.Service.UpdatePreferencesAsync(Owner, new AccountPreferencesUpdate("Dark", "Hour12", null));

        var otherPreferences = await fixture.Service.GetPreferencesAsync(Other);
        otherPreferences.Appearance.Should().Be(AppearancePreference.System);
        otherPreferences.TimeFormat.Should().Be(TimeFormatPreference.System);
    }

    // --- Avatar ---

    [Fact]
    public async Task A_user_who_has_chosen_nothing_gets_the_neutral_built_in_avatar()
    {
        var fixture = new Fixture();

        var profile = await fixture.Service.GetProfileAsync(Owner);

        profile!.Avatar.Kind.Should().Be(UserAvatarKind.BuiltIn);
        profile.Avatar.BuiltInOptionId.Should().Be(BuiltInAvatarCatalog.DefaultOptionId);
    }

    [Fact]
    public async Task SelectBuiltInAvatarAsync_persists_a_curated_choice()
    {
        var fixture = new Fixture();

        var avatar = await fixture.Service.SelectBuiltInAvatarAsync(Owner, "option-b");

        avatar.Kind.Should().Be(UserAvatarKind.BuiltIn);
        avatar.BuiltInOptionId.Should().Be("option-b");
        fixture.Users.Get(Owner.Value).EffectiveAvatar.BuiltInOptionId.Should().Be("option-b");
    }

    [Theory]
    [InlineData("dragon")]
    [InlineData("../neutral")]
    [InlineData("")]
    [InlineData(null)]
    public async Task SelectBuiltInAvatarAsync_rejects_an_option_outside_the_catalog(string? optionId)
    {
        var fixture = new Fixture();

        var act = async () => await fixture.Service.SelectBuiltInAvatarAsync(Owner, optionId);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UploadAvatarAsync_stores_one_rendition_per_configured_size()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.UploadAvatarAsync(Owner, ValidUpload());

        result.Succeeded.Should().BeTrue();
        result.Avatar.Kind.Should().Be(UserAvatarKind.Custom);
        fixture.Images.Rows.Where(r => r.UserId == Owner.Value)
            .Select(r => r.Size)
            .Should().BeEquivalentTo(ProfileImageLimits.RenditionSizes);
    }

    [Fact]
    public async Task UploadAvatarAsync_generates_the_storage_key_itself_and_ignores_the_client_file_name()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.UploadAvatarAsync(
            Owner,
            new AccountAvatarUpload("../../../etc/passwd.png", "image/png", ProfileImageTestImages.Png()));

        result.Succeeded.Should().BeTrue();
        var key = result.Avatar.ImageKey!;
        key.Should().StartWith("avt_");
        key.Should().NotContain("..").And.NotContain("/").And.NotContain("\\");
        key.Should().NotContain("passwd", "the client file name must never influence the storage key");
    }

    [Fact]
    public async Task Uploading_a_replacement_retires_the_previous_derived_images()
    {
        var fixture = new Fixture();

        var first = await fixture.Service.UploadAvatarAsync(Owner, ValidUpload());
        var second = await fixture.Service.UploadAvatarAsync(Owner, ValidUpload());

        second.Avatar.ImageKey.Should().NotBe(first.Avatar.ImageKey, "a replacement must get a new key");
        fixture.Images.KeysFor(Owner.Value).Should().Equal(second.Avatar.ImageKey!);
    }

    [Fact]
    public async Task Uploading_a_replacement_keeps_the_new_key_during_cleanup()
    {
        var fixture = new Fixture();
        await fixture.Service.UploadAvatarAsync(Owner, ValidUpload());

        var second = await fixture.Service.UploadAvatarAsync(Owner, ValidUpload());

        // Cleanup is scoped so the freshly stored image cannot be deleted along with the old one.
        fixture.Images.DeleteCalls.Last().Should().Be((Owner.Value, second.Avatar.ImageKey));
    }

    [Fact]
    public async Task UploadAvatarAsync_rejects_a_declared_type_outside_the_accepted_set()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.UploadAvatarAsync(
            Owner,
            new AccountAvatarUpload("payload.svg", "image/svg+xml", ProfileImageTestImages.Svg()));

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().Be(ProfileImageRejectionReason.UnsupportedDeclaredType);
        fixture.Images.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task UploadAvatarAsync_rejects_an_executable_disguised_with_an_image_name_and_type()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.UploadAvatarAsync(
            Owner,
            new AccountAvatarUpload("harmless.png", "image/png", ProfileImageTestImages.WindowsExecutable()));

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().Be(ProfileImageRejectionReason.UndecodableImage);
        fixture.Images.Rows.Should().BeEmpty();
        fixture.Users.Get(Owner.Value).EffectiveAvatar.Kind.Should().Be(UserAvatarKind.BuiltIn);
    }

    [Fact]
    public async Task UploadAvatarAsync_rejects_svg_content_even_when_the_name_and_type_claim_png()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.UploadAvatarAsync(
            Owner,
            new AccountAvatarUpload("vector.png", "image/png", ProfileImageTestImages.Svg()));

        result.Succeeded.Should().BeFalse();
        fixture.Images.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task A_rejected_upload_leaves_the_existing_avatar_untouched()
    {
        var fixture = new Fixture();
        var stored = await fixture.Service.UploadAvatarAsync(Owner, ValidUpload());

        var rejected = await fixture.Service.UploadAvatarAsync(
            Owner,
            new AccountAvatarUpload("bad.png", "image/png", ProfileImageTestImages.PlainText()));

        rejected.Succeeded.Should().BeFalse();
        rejected.Avatar.ImageKey.Should().Be(stored.Avatar.ImageKey);
        fixture.Users.Get(Owner.Value).EffectiveAvatar.ImageKey.Should().Be(stored.Avatar.ImageKey);
    }

    [Fact]
    public async Task A_rejected_upload_is_audited()
    {
        var fixture = new Fixture();

        await fixture.Service.UploadAvatarAsync(
            Owner,
            new AccountAvatarUpload("bad.exe", "application/octet-stream", ProfileImageTestImages.WindowsExecutable()));

        fixture.Audit.HasAction(AccountAuditActionNames.AvatarUploadRejected).Should().BeTrue();
    }

    [Fact]
    public async Task A_successful_upload_is_audited()
    {
        var fixture = new Fixture();

        await fixture.Service.UploadAvatarAsync(Owner, ValidUpload());

        fixture.Audit.HasAction(AccountAuditActionNames.AvatarUpdated).Should().BeTrue();
    }

    [Fact]
    public async Task ResetAvatarAsync_restores_the_default_and_deletes_every_stored_image()
    {
        var fixture = new Fixture();
        await fixture.Service.UploadAvatarAsync(Owner, ValidUpload());

        var avatar = await fixture.Service.ResetAvatarAsync(Owner);

        avatar.Should().Be(UserAvatar.Default);
        avatar.BuiltInOptionId.Should().Be(BuiltInAvatarCatalog.DefaultOptionId);
        fixture.Images.Rows.Where(r => r.UserId == Owner.Value).Should().BeEmpty();
        fixture.Audit.HasAction(AccountAuditActionNames.AvatarReset).Should().BeTrue();
    }

    [Fact]
    public async Task Choosing_an_illustration_deletes_a_previously_uploaded_photo()
    {
        var fixture = new Fixture();
        await fixture.Service.UploadAvatarAsync(Owner, ValidUpload());

        await fixture.Service.SelectBuiltInAvatarAsync(Owner, "option-c");

        fixture.Images.Rows.Where(r => r.UserId == Owner.Value).Should()
            .BeEmpty("switching to artwork must not leave the photo behind");
    }

    [Fact]
    public async Task GetOwnAvatarImageAsync_returns_the_requested_rendition()
    {
        var fixture = new Fixture();
        await fixture.Service.UploadAvatarAsync(Owner, ValidUpload());

        var image = await fixture.Service.GetOwnAvatarImageAsync(Owner, 128);

        image.Should().NotBeNull();
        image!.ContentType.Should().Be("image/png");
        image.Content.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(96)]
    [InlineData(512)]
    [InlineData(0)]
    [InlineData(-64)]
    public async Task GetOwnAvatarImageAsync_refuses_a_size_outside_the_generated_set(int size)
    {
        var fixture = new Fixture();
        await fixture.Service.UploadAvatarAsync(Owner, ValidUpload());

        var image = await fixture.Service.GetOwnAvatarImageAsync(Owner, size);

        image.Should().BeNull();
    }

    [Fact]
    public async Task GetOwnAvatarImageAsync_returns_nothing_for_a_user_on_the_built_in_avatar()
    {
        var fixture = new Fixture();

        var image = await fixture.Service.GetOwnAvatarImageAsync(Owner, 128);

        // Falling back to built-in artwork is the documented behaviour for a missing image.
        image.Should().BeNull();
    }

    [Fact]
    public async Task One_operators_uploaded_image_is_not_readable_through_another_operators_identity()
    {
        var fixture = new Fixture();
        await fixture.Service.UploadAvatarAsync(Owner, ValidUpload());

        var leaked = await fixture.Service.GetOwnAvatarImageAsync(Other, 128);

        leaked.Should().BeNull();
    }

    [Fact]
    public async Task Avatar_changes_apply_only_to_the_resolved_account()
    {
        var fixture = new Fixture();

        await fixture.Service.UploadAvatarAsync(Owner, ValidUpload());

        fixture.Users.Get(Other.Value).EffectiveAvatar.Should().Be(UserAvatar.Default);
        fixture.Images.Rows.Should().OnlyContain(r => r.UserId == Owner.Value);
    }

    // --- Notification preferences ---

    [Fact]
    public async Task GetNotificationPreferencesAsync_reports_the_in_app_channel_as_not_editable()
    {
        var fixture = new Fixture();

        var preferences = await fixture.Service.GetNotificationPreferencesAsync(Owner);

        preferences.Should().NotBeEmpty();
        preferences.Should().OnlyContain(p => p.Platform.Enabled && !p.Platform.Editable);
        preferences.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Platform.NotEditableReason));
    }

    [Fact]
    public async Task GetNotificationPreferencesAsync_reports_email_as_editable()
    {
        var fixture = new Fixture();

        var preferences = await fixture.Service.GetNotificationPreferencesAsync(Owner);

        preferences.Should().OnlyContain(p => p.Email.Editable);
        preferences.Should().OnlyContain(p => p.Email.NotEditableReason == null);
    }

    [Fact]
    public async Task GetNotificationPreferencesAsync_only_lists_definitions_the_pipeline_actually_honours()
    {
        var fixture = new Fixture();

        var preferences = await fixture.Service.GetNotificationPreferencesAsync(Owner);

        // The service delegates to the same subscription service the delivery pipeline reads, so the
        // page can never advertise a preference that has no effect.
        preferences.Select(p => p.DefinitionId).Should().BeEquivalentTo(
            "platform.schedule.expected_run_missing",
            "module.sample.helloworld.demo.completed");
    }

    [Fact]
    public async Task SetNotificationEmailPreferenceAsync_turns_email_off_for_the_calling_operator()
    {
        var fixture = new Fixture();
        fixture.Notifications.SeedEmailEnabled(Owner.Value, "platform.schedule.expected_run_missing", true);

        var updated = await fixture.Service.SetNotificationEmailPreferenceAsync(
            Owner,
            "platform.schedule.expected_run_missing",
            emailEnabled: false);

        updated.Email.Enabled.Should().BeFalse();
        fixture.Notifications.SetCalls.Should().ContainSingle(c =>
            c.UserId == Owner.Value && c.DefinitionId == "platform.schedule.expected_run_missing" && !c.EmailEnabled);
    }

    [Fact]
    public async Task SetNotificationEmailPreferenceAsync_writes_only_the_email_channel_leaving_toast_untouched()
    {
        var fixture = new Fixture();
        fixture.ToastPreferences.SeedToastEnabled(Owner.Value, "platform.schedule.expected_run_missing", true);

        var updated = await fixture.Service.SetNotificationEmailPreferenceAsync(
            Owner,
            "platform.schedule.expected_run_missing",
            emailEnabled: true);

        updated.Email.Enabled.Should().BeTrue();
        updated.Toast.Enabled.Should().BeTrue();
        fixture.Notifications.SetCalls.Should().ContainSingle(c =>
            c.UserId == Owner.Value && c.DefinitionId == "platform.schedule.expected_run_missing" && c.EmailEnabled);
        fixture.ToastPreferences.SetCalls.Should().BeEmpty("setting the email channel must never touch the toast service");
    }

    [Fact]
    public async Task Turning_email_off_does_not_change_the_in_app_channel()
    {
        var fixture = new Fixture();

        var updated = await fixture.Service.SetNotificationEmailPreferenceAsync(
            Owner,
            "platform.schedule.expected_run_missing",
            emailEnabled: false);

        updated.Platform.Enabled.Should().BeTrue();
        updated.Platform.Editable.Should().BeFalse();
    }

    [Fact]
    public async Task Notification_channel_changes_are_audited_because_they_affect_who_is_told()
    {
        var fixture = new Fixture();

        await fixture.Service.SetNotificationEmailPreferenceAsync(Owner, "platform.schedule.expected_run_missing", false);
        await fixture.Service.SetNotificationEmailPreferenceAsync(Owner, "platform.schedule.expected_run_missing", true);

        fixture.Audit.HasAction(AccountAuditActionNames.NotificationEmailOptOut).Should().BeTrue();
        fixture.Audit.HasAction(AccountAuditActionNames.NotificationEmailOptIn).Should().BeTrue();
    }

    [Fact]
    public async Task SetNotificationEmailPreferenceAsync_rejects_an_unknown_definition()
    {
        var fixture = new Fixture();

        var act = async () => await fixture.Service
            .SetNotificationEmailPreferenceAsync(Owner, "does.not.exist", true);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetNotificationEmailPreferenceAsync_rejects_a_blank_definition_id(string definitionId)
    {
        var fixture = new Fixture();

        var act = async () => await fixture.Service
            .SetNotificationEmailPreferenceAsync(Owner, definitionId, true);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Notification_preferences_are_scoped_to_the_calling_operator()
    {
        var fixture = new Fixture();

        await fixture.Service.SetNotificationEmailPreferenceAsync(Owner, "platform.schedule.expected_run_missing", true);

        fixture.Notifications.IsEmailEnabled(Other.Value, "platform.schedule.expected_run_missing")
            .Should().BeFalse();
    }

    [Fact]
    public async Task GetNotificationPreferencesAsync_zips_email_and_toast_rows_correctly_by_definition_id()
    {
        var fixture = new Fixture();
        fixture.Notifications.SeedEmailEnabled(Owner.Value, "platform.schedule.expected_run_missing", true);
        fixture.ToastPreferences.SeedToastEnabled(Owner.Value, "platform.schedule.expected_run_missing", true);
        fixture.ToastPreferences.SeedToastEnabled(Owner.Value, "module.sample.helloworld.demo.completed", false);

        var preferences = await fixture.Service.GetNotificationPreferencesAsync(Owner);

        var expectedRunMissing = preferences.Single(p => p.DefinitionId == "platform.schedule.expected_run_missing");
        expectedRunMissing.Email.Enabled.Should().BeTrue();
        expectedRunMissing.Toast.Enabled.Should().BeTrue();
        expectedRunMissing.Toast.Editable.Should().BeTrue();
        expectedRunMissing.Toast.NotEditableReason.Should().BeNull();

        var demoCompleted = preferences.Single(p => p.DefinitionId == "module.sample.helloworld.demo.completed");
        demoCompleted.Email.Enabled.Should().BeFalse();
        demoCompleted.Toast.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task SetNotificationToastPreferenceAsync_writes_only_the_toast_channel_leaving_email_untouched()
    {
        var fixture = new Fixture();
        fixture.Notifications.SeedEmailEnabled(Owner.Value, "platform.schedule.expected_run_missing", true);

        var updated = await fixture.Service.SetNotificationToastPreferenceAsync(
            Owner,
            "platform.schedule.expected_run_missing",
            toastEnabled: true);

        updated.Toast.Enabled.Should().BeTrue();
        updated.Email.Enabled.Should().BeTrue();
        fixture.ToastPreferences.SetCalls.Should().ContainSingle(c =>
            c.UserId == Owner.Value && c.DefinitionId == "platform.schedule.expected_run_missing" && c.ToastEnabled);
        fixture.Notifications.SetCalls.Should().BeEmpty("setting the toast channel must never touch the email service");
    }

    [Fact]
    public async Task Toast_channel_changes_are_audited()
    {
        var fixture = new Fixture();

        await fixture.Service.SetNotificationToastPreferenceAsync(Owner, "platform.schedule.expected_run_missing", false);
        await fixture.Service.SetNotificationToastPreferenceAsync(Owner, "platform.schedule.expected_run_missing", true);

        fixture.Audit.HasAction(AccountAuditActionNames.NotificationToastOptOut).Should().BeTrue();
        fixture.Audit.HasAction(AccountAuditActionNames.NotificationToastOptIn).Should().BeTrue();
    }

    [Fact]
    public async Task SetNotificationToastPreferenceAsync_rejects_an_unknown_definition()
    {
        var fixture = new Fixture();

        var act = async () => await fixture.Service
            .SetNotificationToastPreferenceAsync(Owner, "does.not.exist", true);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private static AccountAvatarUpload ValidUpload() =>
        new("portrait.png", "image/png", ProfileImageTestImages.Png(320, 240));

    private sealed class Fixture
    {
        public Fixture()
        {
            Users = new InMemoryUserRepository(
                new User(Owner, "Owner Person", [new IdentityLink(IdentitySource.Local, "owner", null)], [], false),
                new User(Other, "Other Person", [new IdentityLink(IdentitySource.Local, "other", null)], [], false));

            Preferences = new InMemoryUserPreferencesRepository();
            Images = new InMemoryUserProfileImageStore();
            Notifications = new StubOperatorNotificationSubscriptionService(
                ("platform.schedule.expected_run_missing", "Expected run missing", "A schedule did not run when expected."),
                ("module.sample.helloworld.demo.completed", "Demo completed", null));
            ToastPreferences = new StubOperatorNotificationToastPreferenceService(
                ("platform.schedule.expected_run_missing", "Expected run missing", "A schedule did not run when expected."),
                ("module.sample.helloworld.demo.completed", "Demo completed", null));
            Audit = new RecordingAuditService();

            Service = new AccountProfileService(
                Users,
                Preferences,
                Images,
                new SkiaProfileImageNormalizer(),
                Notifications,
                ToastPreferences,
                Audit,
                NullLogger<AccountProfileService>.Instance);
        }

        public InMemoryUserRepository Users { get; }
        public InMemoryUserPreferencesRepository Preferences { get; }
        public InMemoryUserProfileImageStore Images { get; }
        public StubOperatorNotificationSubscriptionService Notifications { get; }
        public StubOperatorNotificationToastPreferenceService ToastPreferences { get; }
        public RecordingAuditService Audit { get; }
        public AccountProfileService Service { get; }
    }
}
