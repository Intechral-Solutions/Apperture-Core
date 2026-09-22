using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;
using Apperture.ControlPlane.Security;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications.Subscriptions;

public sealed class OperatorNotificationSubscriptionServiceTests
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
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task List_returns_subscribable_platform_definitions()
    {
        await using var db = CreateDb();
        await SeedExpectedRunMissingDefinitionAsync(db);
        var sut = new OperatorNotificationSubscriptionService(
            new SqlNotificationDefinitionStore(db),
            new SqlNotificationSubscriptionStore(db),
            new SqlNotificationUserPreferenceStore(db));

        var prefs = await sut.ListEmailPreferencesAsync("user-1");

        prefs.Should().ContainSingle();
        prefs[0].DefinitionId.Should().Be(NotificationDefinitionIds.PlatformScheduleExpectedRunMissing);
        prefs[0].EmailEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Enable_creates_self_subscription_and_allows_email()
    {
        await using var db = CreateDb();
        await SeedExpectedRunMissingDefinitionAsync(db);
        var store = new SqlNotificationSubscriptionStore(db);
        var sut = new OperatorNotificationSubscriptionService(
            new SqlNotificationDefinitionStore(db), store, new SqlNotificationUserPreferenceStore(db));

        var updated = await sut.SetEmailEnabledAsync(
            "user-1",
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            emailEnabled: true);

        updated.EmailEnabled.Should().BeTrue();
        var sub = await store.FindUserSelfSubscriptionAsync("user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing);
        sub.Should().NotBeNull();
        sub!.Origin.Should().Be(NotificationSubscriptionOrigins.UserSelfSubscribed);
        sub.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Disable_writes_email_preference_without_touching_subscription_or_override()
    {
        await using var db = CreateDb();
        await SeedExpectedRunMissingDefinitionAsync(db);
        var store = new SqlNotificationSubscriptionStore(db);
        var preferences = new SqlNotificationUserPreferenceStore(db);
        var sut = new OperatorNotificationSubscriptionService(new SqlNotificationDefinitionStore(db), store, preferences);
        await sut.SetEmailEnabledAsync("user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, true);

        var updated = await sut.SetEmailEnabledAsync(
            "user-1",
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            emailEnabled: false);

        updated.EmailEnabled.Should().BeFalse();

        var sub = await store.FindUserSelfSubscriptionAsync("user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing);
        sub.Should().NotBeNull();
        sub!.IsEnabled.Should().BeTrue();

        (await store.HasGlobalUserOptOutAsync("user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing))
            .Should().BeFalse();

        var explicitPreference = await preferences.GetAsync(
            "user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, NotificationChannels.Email);
        explicitPreference.Should().NotBeNull();
        explicitPreference!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Enable_after_disable_survives_and_re_enables()
    {
        await using var db = CreateDb();
        await SeedExpectedRunMissingDefinitionAsync(db);
        var store = new SqlNotificationSubscriptionStore(db);
        var preferences = new SqlNotificationUserPreferenceStore(db);
        var sut = new OperatorNotificationSubscriptionService(new SqlNotificationDefinitionStore(db), store, preferences);

        await sut.SetEmailEnabledAsync("user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, true);
        await sut.SetEmailEnabledAsync("user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, false);
        var updated = await sut.SetEmailEnabledAsync(
            "user-1",
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            emailEnabled: true);

        updated.EmailEnabled.Should().BeTrue();

        var sub = await store.FindUserSelfSubscriptionAsync("user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing);
        sub.Should().NotBeNull();
        sub!.IsEnabled.Should().BeTrue();

        (await store.HasGlobalUserOptOutAsync("user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Admin_global_opt_out_still_suppresses_email_enabled_view_even_with_explicit_true_preference()
    {
        await using var db = CreateDb();
        await SeedExpectedRunMissingDefinitionAsync(db);
        var store = new SqlNotificationSubscriptionStore(db);
        var preferences = new SqlNotificationUserPreferenceStore(db);
        var sut = new OperatorNotificationSubscriptionService(new SqlNotificationDefinitionStore(db), store, preferences);

        await preferences.SetAsync(
            "user-1",
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            NotificationChannels.Email,
            isEnabled: true,
            updatedByActorId: "user-1");
        await store.SetUserOptOutAsync(
            new NotificationSubscriptionUserOverrideCommand(
                "user-1",
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                OperationalTeamId: null,
                CreatedByActorId: "admin-1"));

        var prefs = await sut.ListEmailPreferencesAsync("user-1");

        var preference = prefs.Should().ContainSingle().Subject;
        preference.EmailEnabled.Should().BeFalse();
        preference.HasGlobalEmailOptOut.Should().BeTrue();
    }

    [Fact]
    public async Task Set_for_unknown_definition_throws()
    {
        await using var db = CreateDb();
        var sut = new OperatorNotificationSubscriptionService(
            new SqlNotificationDefinitionStore(db),
            new SqlNotificationSubscriptionStore(db),
            new SqlNotificationUserPreferenceStore(db));

        var act = async () => await sut.SetEmailEnabledAsync("user-1", "unknown.def", true);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
