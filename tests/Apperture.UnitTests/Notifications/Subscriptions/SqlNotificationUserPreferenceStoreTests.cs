using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications.Subscriptions;

public sealed class SqlNotificationUserPreferenceStoreTests
{
    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    [Fact]
    public async Task GetAsync_returns_null_when_no_row_exists()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        var result = await sut.GetAsync("user-1", "def-1", NotificationChannels.Toast);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_creates_a_new_row_when_none_exists()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        var created = await sut.SetAsync("user-1", "def-1", NotificationChannels.Toast, true, "actor-1");

        created.UserId.Should().Be("user-1");
        created.NotificationDefinitionId.Should().Be("def-1");
        created.Channel.Should().Be(NotificationChannels.Toast);
        created.IsEnabled.Should().BeTrue();
        created.UpdatedByActorId.Should().Be("actor-1");
        created.CreatedAtUtc.Should().Be(created.UpdatedAtUtc);

        var fetched = await sut.GetAsync("user-1", "def-1", NotificationChannels.Toast);
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task SetAsync_updates_in_place_on_a_second_call_for_the_same_key()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        var first = await sut.SetAsync("user-1", "def-1", NotificationChannels.Toast, true, "actor-1");
        await Task.Delay(15);
        var second = await sut.SetAsync("user-1", "def-1", NotificationChannels.Toast, false, "actor-2");

        second.Id.Should().Be(first.Id, "SetAsync upserts by the (userId, definitionId, channel) key");
        second.IsEnabled.Should().BeFalse();
        second.UpdatedByActorId.Should().Be("actor-2");
        second.UpdatedAtUtc.Should().BeAfter(first.UpdatedAtUtc);
        second.CreatedAtUtc.Should().Be(first.CreatedAtUtc, "creation time never changes on update");

        var allRows = await db.NotificationUserPreferences.AsNoTracking().ToListAsync();
        allRows.Should().ContainSingle("the second SetAsync call must update the existing row, not insert a duplicate");
    }

    [Fact]
    public async Task SetAsync_does_not_cross_contaminate_different_channels_for_the_same_user_and_definition()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        await sut.SetAsync("user-1", "def-1", NotificationChannels.Toast, true, null);
        await sut.SetAsync("user-1", "def-1", NotificationChannels.Email, false, null);

        (await sut.GetAsync("user-1", "def-1", NotificationChannels.Toast))!.IsEnabled.Should().BeTrue();
        (await sut.GetAsync("user-1", "def-1", NotificationChannels.Email))!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task ListForUserAsync_filters_by_user_and_optionally_by_definition_ids()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        await sut.SetAsync("user-1", "def-1", NotificationChannels.Toast, true, null);
        await sut.SetAsync("user-1", "def-2", NotificationChannels.Toast, false, null);
        await sut.SetAsync("user-2", "def-1", NotificationChannels.Toast, true, null);

        var allForUser1 = await sut.ListForUserAsync("user-1", null);
        allForUser1.Should().HaveCount(2);
        allForUser1.Should().OnlyContain(x => x.UserId == "user-1");

        var filtered = await sut.ListForUserAsync("user-1", new[] { "def-1" });
        filtered.Should().ContainSingle(x => x.NotificationDefinitionId == "def-1");
    }

    [Fact]
    public async Task DeleteForDefinitionAsync_removes_only_rows_for_that_definition_and_returns_the_count()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        await sut.SetAsync("user-1", "def-1", NotificationChannels.Toast, true, null);
        await sut.SetAsync("user-1", "def-1", NotificationChannels.Email, true, null);
        await sut.SetAsync("user-2", "def-1", NotificationChannels.Toast, true, null);
        await sut.SetAsync("user-1", "def-2", NotificationChannels.Toast, true, null);

        var removed = await sut.DeleteForDefinitionAsync("def-1");

        removed.Should().Be(3);
        (await db.NotificationUserPreferences.AsNoTracking().CountAsync(x => x.NotificationDefinitionId == "def-1")).Should().Be(0);
        (await db.NotificationUserPreferences.AsNoTracking().CountAsync(x => x.NotificationDefinitionId == "def-2")).Should().Be(1);
    }

    [Fact]
    public async Task DeleteForDefinitionAsync_returns_zero_when_no_rows_match()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationUserPreferenceStore(db);

        var removed = await sut.DeleteForDefinitionAsync("does-not-exist");

        removed.Should().Be(0);
    }
}
