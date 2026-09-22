using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications.Subscriptions;

/// <summary>
/// Proves the Toast-B spec's inheritance semantics: <c>effectiveToast(user, definition) =</c> the
/// user's explicit <c>toast</c> preference if present, otherwise <c>definition.ToastEnabledByDefault</c>.
/// Resolution happens at read time (never copy-on-subscribe), and a deleted/unknown definition never
/// throws -- it simply resolves to "not toast-eligible".
/// </summary>
public sealed class NotificationTogglePreferenceResolverTests
{
    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private static NotificationTogglePreferenceResolver CreateSut(AppertureDbContext db) =>
        new(new SqlNotificationUserPreferenceStore(db), new SqlNotificationDefinitionStore(db));

    private static async Task<string> SeedDefinitionAsync(AppertureDbContext db, bool toastEnabledByDefault)
    {
        var id = $"ndef_{Guid.NewGuid():N}";
        db.NotificationDefinitions.Add(new NotificationDefinitionEntity
        {
            Id = id,
            DisplayName = "Test Definition",
            Description = null,
            SourceKind = NotificationSourceKinds.PlatformEvent,
            Origin = NotificationDefinitionOrigins.UserCreated,
            Scope = NotificationDefinitionScopes.Integration,
            Mutability = NotificationDefinitionMutabilities.FullyEditable,
            ModuleId = null,
            IntegrationId = "int-1",
            PrimaryEventType = null,
            IsEnabled = true,
            DefaultSeverity = "warning",
            RuleJson = """{"schemaVersion":"apperture.notification.rule/v1"}""",
            TemplateJson = """{"schemaVersion":"apperture.notification.template/v1"}""",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            ToastEnabledByDefault = toastEnabledByDefault,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task SetToastDefaultAsync(AppertureDbContext db, string definitionId, bool toastEnabledByDefault)
    {
        var entity = await db.NotificationDefinitions.SingleAsync(x => x.Id == definitionId);
        entity.ToastEnabledByDefault = toastEnabledByDefault;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task No_preference_row_and_default_true_resolves_true()
    {
        await using var db = CreateDb();
        var defId = await SeedDefinitionAsync(db, toastEnabledByDefault: true);

        var result = await CreateSut(db).ResolveEffectiveToastEnabledAsync("user-1", defId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task No_preference_row_and_default_false_resolves_false()
    {
        await using var db = CreateDb();
        var defId = await SeedDefinitionAsync(db, toastEnabledByDefault: false);

        var result = await CreateSut(db).ResolveEffectiveToastEnabledAsync("user-1", defId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Explicit_true_preference_overrides_a_false_default()
    {
        await using var db = CreateDb();
        var defId = await SeedDefinitionAsync(db, toastEnabledByDefault: false);
        var preferences = new SqlNotificationUserPreferenceStore(db);
        await preferences.SetAsync("user-1", defId, NotificationChannels.Toast, true, null);

        var result = await CreateSut(db).ResolveEffectiveToastEnabledAsync("user-1", defId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Explicit_false_preference_overrides_a_true_default()
    {
        await using var db = CreateDb();
        var defId = await SeedDefinitionAsync(db, toastEnabledByDefault: true);
        var preferences = new SqlNotificationUserPreferenceStore(db);
        await preferences.SetAsync("user-1", defId, NotificationChannels.Toast, false, null);

        var result = await CreateSut(db).ResolveEffectiveToastEnabledAsync("user-1", defId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Explicit_choice_survives_a_definition_default_change()
    {
        await using var db = CreateDb();
        var defId = await SeedDefinitionAsync(db, toastEnabledByDefault: false);
        var preferences = new SqlNotificationUserPreferenceStore(db);
        var explicitPreference = await preferences.SetAsync("user-1", defId, NotificationChannels.Toast, true, null);

        // Flip the definition's default the opposite way after the explicit choice was made, through
        // the actual production write path for definition changes (not a direct entity mutation) --
        // this is precisely the invariant phase P2 depends on: UpdateAsync must never touch
        // NotificationUserPreferences.
        var definitionStore = new SqlNotificationDefinitionStore(db);
        var updated = await definitionStore.UpdateAsync(
            NotificationDefinitionId.From(defId),
            new NotificationDefinitionUpdateCommand(
                DisplayName: null,
                Description: null,
                RuleJson: null,
                TemplateJson: null,
                IsEnabled: null,
                DefaultSeverity: null,
                UpdatedByActorId: "actor-1",
                ToastEnabledByDefault: true));
        updated.Should().NotBeNull();
        updated!.ToastEnabledByDefault.Should().BeTrue();

        var result = await CreateSut(db).ResolveEffectiveToastEnabledAsync("user-1", defId);

        result.Should().BeTrue("the explicit user choice, not the new default, still governs");

        var preferenceAfterUpdate = await preferences.GetAsync("user-1", defId, NotificationChannels.Toast);
        preferenceAfterUpdate.Should().NotBeNull();
        preferenceAfterUpdate!.Id.Should().Be(explicitPreference.Id, "UpdateAsync must never touch NotificationUserPreferences");
        preferenceAfterUpdate.UpdatedAtUtc.Should().Be(
            explicitPreference.UpdatedAtUtc, "UpdateAsync must never touch NotificationUserPreferences");
    }

    [Fact]
    public async Task User_with_no_explicit_preference_picks_up_a_changed_default_on_next_resolve()
    {
        await using var db = CreateDb();
        var defId = await SeedDefinitionAsync(db, toastEnabledByDefault: false);
        var sut = CreateSut(db);

        (await sut.ResolveEffectiveToastEnabledAsync("user-1", defId)).Should().BeFalse();

        await SetToastDefaultAsync(db, defId, toastEnabledByDefault: true);

        (await sut.ResolveEffectiveToastEnabledAsync("user-1", defId)).Should().BeTrue(
            "resolution happens at read time, not copy-on-subscribe, so a definition default change is picked up immediately");
    }

    [Fact]
    public async Task Unknown_definition_resolves_false_and_does_not_throw()
    {
        await using var db = CreateDb();
        var sut = CreateSut(db);

        var act = async () => await sut.ResolveEffectiveToastEnabledAsync("user-1", "does-not-exist");

        (await act.Should().NotThrowAsync()).Which.Should().BeFalse();
    }
}
