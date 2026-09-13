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
            new SqlNotificationSubscriptionStore(db));

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
        var sut = new OperatorNotificationSubscriptionService(new SqlNotificationDefinitionStore(db), store);

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
    public async Task Disable_sets_global_opt_out()
    {
        await using var db = CreateDb();
        await SeedExpectedRunMissingDefinitionAsync(db);
        var store = new SqlNotificationSubscriptionStore(db);
        var sut = new OperatorNotificationSubscriptionService(new SqlNotificationDefinitionStore(db), store);
        await sut.SetEmailEnabledAsync("user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, true);

        var updated = await sut.SetEmailEnabledAsync(
            "user-1",
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            emailEnabled: false);

        updated.EmailEnabled.Should().BeFalse();
        updated.HasGlobalEmailOptOut.Should().BeTrue();
        (await store.HasGlobalUserOptOutAsync("user-1", NotificationDefinitionIds.PlatformScheduleExpectedRunMissing))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Set_for_unknown_definition_throws()
    {
        await using var db = CreateDb();
        var sut = new OperatorNotificationSubscriptionService(
            new SqlNotificationDefinitionStore(db),
            new SqlNotificationSubscriptionStore(db));

        var act = async () => await sut.SetEmailEnabledAsync("user-1", "unknown.def", true);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
