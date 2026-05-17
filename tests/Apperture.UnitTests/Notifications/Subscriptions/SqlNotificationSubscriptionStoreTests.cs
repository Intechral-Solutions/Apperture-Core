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
}
