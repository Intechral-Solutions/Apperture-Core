using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;
using Apperture.ControlPlane.Security;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications.Subscriptions;

/// <summary>
/// Toast's sibling to <see cref="OperatorNotificationSubscriptionServiceTests"/>: same harness, same
/// definition-listing/filtering logic, but a pure preference-store read/write with no
/// subscription/route concept behind it.
/// </summary>
public sealed class OperatorNotificationToastPreferenceServiceTests
{
    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private static async Task SeedExpectedRunMissingDefinitionAsync(AppertureDbContext db)
    {
        var builtin = ExpectedRunMissingNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        db.NotificationDefinitions.Add(new NotificationDefinitionEntity
        {
            Id = builtin.Id.Value,
            DisplayName = builtin.DisplayName,
            Description = builtin.Description,
            SourceKind = builtin.SourceKind,
            Origin = builtin.Origin,
            Scope = builtin.Scope,
            Mutability = builtin.Mutability,
            PrimaryEventType = builtin.PrimaryEventType,
            IsEnabled = builtin.IsEnabled,
            DefaultSeverity = builtin.DefaultSeverity,
            RuleJson = builtin.RuleJson,
            TemplateJson = builtin.TemplateJson,
            CreatedAtUtc = builtin.CreatedAtUtc,
            UpdatedAtUtc = builtin.UpdatedAtUtc,
            ToastEnabledByDefault = builtin.ToastEnabledByDefault,
        });
        await db.SaveChangesAsync();
    }

    private static OperatorNotificationToastPreferenceService CreateSut(AppertureDbContext db)
    {
        var definitions = new SqlNotificationDefinitionStore(db);
        var preferences = new SqlNotificationUserPreferenceStore(db);
        return new OperatorNotificationToastPreferenceService(
            definitions, preferences, new NotificationTogglePreferenceResolver(preferences, definitions));
    }

    [Fact]
    public async Task List_returns_subscribable_definitions_with_the_inherited_default_when_no_explicit_preference_exists()
    {
        await using var db = CreateDb();
        await SeedExpectedRunMissingDefinitionAsync(db);
        var sut = CreateSut(db);

        var prefs = await sut.ListToastPreferencesAsync("user-1");

        prefs.Should().ContainSingle();
        prefs[0].DefinitionId.Should().Be(NotificationDefinitionIds.PlatformScheduleExpectedRunMissing);
        // ExpectedRunMissingNotificationDefinitionSeed.BuiltIn does not set ToastEnabledByDefault, so it
        // defaults to false, and with no explicit preference row the resolver falls back to that default.
        prefs[0].ToastEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task List_reflects_an_explicit_preference_that_overrides_the_default()
    {
        await using var db = CreateDb();
        await SeedExpectedRunMissingDefinitionAsync(db);
        var preferences = new SqlNotificationUserPreferenceStore(db);
        await preferences.SetAsync(
            "user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, NotificationChannels.Toast,
            isEnabled: true, updatedByActorId: "user-1");
        var sut = CreateSut(db);

        var prefs = await sut.ListToastPreferencesAsync("user-1");

        prefs.Should().ContainSingle();
        prefs[0].ToastEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Set_writes_an_explicit_preference_row_and_the_returned_state_reflects_it()
    {
        await using var db = CreateDb();
        await SeedExpectedRunMissingDefinitionAsync(db);
        var preferences = new SqlNotificationUserPreferenceStore(db);
        var sut = new OperatorNotificationToastPreferenceService(
            new SqlNotificationDefinitionStore(db), preferences,
            new NotificationTogglePreferenceResolver(preferences, new SqlNotificationDefinitionStore(db)));

        var updated = await sut.SetToastEnabledAsync(
            "user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, toastEnabled: true);

        updated.ToastEnabled.Should().BeTrue();
        var explicitPreference = await preferences.GetAsync(
            "user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, NotificationChannels.Toast);
        explicitPreference.Should().NotBeNull();
        explicitPreference!.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Set_then_unset_survives_and_flips_back()
    {
        await using var db = CreateDb();
        await SeedExpectedRunMissingDefinitionAsync(db);
        var sut = CreateSut(db);

        await sut.SetToastEnabledAsync("user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, true);
        var updated = await sut.SetToastEnabledAsync(
            "user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, false);

        updated.ToastEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Set_for_unknown_definition_throws()
    {
        await using var db = CreateDb();
        var sut = CreateSut(db);

        var act = async () => await sut.SetToastEnabledAsync("user-1", "unknown.def", true);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Set_for_a_non_subscribable_definition_throws()
    {
        await using var db = CreateDb();
        db.NotificationDefinitions.Add(new NotificationDefinitionEntity
        {
            Id = "not.subscribable",
            DisplayName = "Not subscribable",
            Description = null,
            SourceKind = NotificationSourceKinds.PlatformEvent,
            Origin = NotificationDefinitionOrigins.PlatformBuiltIn,
            Scope = NotificationDefinitionScopes.Platform,
            Mutability = NotificationDefinitionMutabilities.ReadOnly,
            PrimaryEventType = null,
            IsEnabled = true,
            DefaultSeverity = "info",
            RuleJson = """{"schemaVersion":"apperture.notification.rule/v1"}""",
            TemplateJson = """{"schemaVersion":"apperture.notification.template/v1"}""",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var act = async () => await sut.SetToastEnabledAsync("user-1", "not.subscribable", true);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
