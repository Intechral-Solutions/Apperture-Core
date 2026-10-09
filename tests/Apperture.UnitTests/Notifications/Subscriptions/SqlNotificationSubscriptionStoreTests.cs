using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications.Subscriptions;

public sealed class SqlNotificationSubscriptionStoreTests
{
    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    /// <summary>
    /// The integration a subscription refers to. Adding a subscription that names one takes the integration row lock and requires
    /// the row (AUD-1.4 slice 3).
    /// </summary>
    private static async Task SeedIntegrationAsync(AppertureDbContext db, string id)
    {
        db.Integrations.Add(new IntegrationEntity
        {
            Id = id,
            DisplayName = "Test Integration",
            ModuleId = "Sample.Mod",
            ResourceContextId = "shared",
            State = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_subscription_that_names_an_integration_that_does_not_exist_is_refused_and_stores_nothing()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationSubscriptionStore(db);

        var act = async () => await sut.AddAsync(
            new NotificationSubscriptionCreateCommand(
                "def-1", "int-gone", NotificationRecipientKinds.Principal, "u1",
                NotificationDeliveryModes.Immediate, NotificationSubscriptionOrigins.AdminAssigned, true, null));

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("int-gone");
        (await db.NotificationSubscriptions.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_subscription_without_an_integration_needs_no_integration_and_runs_no_reauthorization()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationSubscriptionStore(db);
        var reauthorized = 0;

        var created = await sut.AddAsync(
            new NotificationSubscriptionCreateCommand(
                "def-1", null, NotificationRecipientKinds.Principal, "u1",
                NotificationDeliveryModes.Immediate, NotificationSubscriptionOrigins.UserSelfSubscribed, true, "u1"),
            reauthorize: _ =>
            {
                reauthorized++;
                return ValueTask.CompletedTask;
            });

        created.IntegrationId.Should().BeNull();
        reauthorized.Should().Be(0, "there is no integration row to lock, so nothing is re-checked under it");
        (await db.NotificationSubscriptions.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_subscription_that_names_an_integration_runs_the_callers_authorization_again_and_stores_nothing_when_it_fails()
    {
        await using var db = CreateDb();
        await SeedIntegrationAsync(db, "int-a");
        var sut = new SqlNotificationSubscriptionStore(db);

        var act = async () => await sut.AddAsync(
            new NotificationSubscriptionCreateCommand(
                "def-1", "int-a", NotificationRecipientKinds.Principal, "u1",
                NotificationDeliveryModes.Immediate, NotificationSubscriptionOrigins.AdminAssigned, true, null),
            reauthorize: _ => throw new UnauthorizedAccessException("moved"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        (await db.NotificationSubscriptions.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Add_list_patch_remove_and_opt_out_roundtrip()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationSubscriptionStore(db);
        const string defId = "def-1";

        var created = await sut.AddAsync(
            new NotificationSubscriptionCreateCommand(
                defId,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.AdminAssigned,
                true,
                null));

        var list = await sut.ListByDefinitionAsync(defId);
        list.Should().ContainSingle(x => x.Id == created.Id);

        var disabled = await sut.SetEnabledAsync(created.Id, false, null);
        disabled!.IsEnabled.Should().BeFalse();

        var digest = await sut.SetDeliveryModeAsync(
            created.Id,
            NotificationDeliveryModes.DigestOnly,
            null);
        digest!.DeliveryMode.Should().Be(NotificationDeliveryModes.DigestOnly);

        var optOut = await sut.SetUserOptOutAsync(
            new NotificationSubscriptionUserOverrideCommand("user-1", defId, null, null));
        optOut.UserId.Should().Be("user-1");

        var removedOptOut = await sut.RemoveUserOptOutAsync("user-1", defId, null);
        removedOptOut.Should().BeTrue();

        var removed = await sut.RemoveAsync(created.Id);
        removed.Should().BeTrue();
        (await sut.GetAsync(created.Id)).Should().BeNull();
    }

    [Fact]
    public async Task ListEnabledForResolution_filters_integration_scope()
    {
        await using var db = CreateDb();
        await SeedIntegrationAsync(db, "int-a");
        var sut = new SqlNotificationSubscriptionStore(db);
        const string defId = NotificationDefinitionIds.PlatformScheduleExpectedRunMissing;

        await sut.AddAsync(
            new NotificationSubscriptionCreateCommand(
                defId, null, NotificationRecipientKinds.Principal, "u1",
                NotificationDeliveryModes.Immediate, NotificationSubscriptionOrigins.AdminAssigned, true, null));
        await sut.AddAsync(
            new NotificationSubscriptionCreateCommand(
                defId, "int-a", NotificationRecipientKinds.Principal, "u2",
                NotificationDeliveryModes.Immediate, NotificationSubscriptionOrigins.AdminAssigned, true, null));

        var platformOnly = await sut.ListEnabledForResolutionAsync(defId, integrationId: null);
        platformOnly.Should().ContainSingle(x => x.RecipientRef == "u1");

        var withIntegration = await sut.ListEnabledForResolutionAsync(defId, "int-a");
        withIntegration.Should().HaveCount(2);
    }

    [Fact]
    public async Task Find_user_self_subscription_and_global_opt_out()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationSubscriptionStore(db);
        const string defId = "def-1";

        await sut.AddAsync(
            new NotificationSubscriptionCreateCommand(
                defId,
                null,
                NotificationRecipientKinds.Principal,
                "user-1",
                NotificationDeliveryModes.Immediate,
                NotificationSubscriptionOrigins.UserSelfSubscribed,
                true,
                "user-1"));

        (await sut.FindUserSelfSubscriptionAsync("user-1", defId)).Should().NotBeNull();
        (await sut.HasGlobalUserOptOutAsync("user-1", defId)).Should().BeFalse();

        await sut.SetUserOptOutAsync(
            new NotificationSubscriptionUserOverrideCommand("user-1", defId, null, "user-1"));
        (await sut.HasGlobalUserOptOutAsync("user-1", defId)).Should().BeTrue();
    }
}
