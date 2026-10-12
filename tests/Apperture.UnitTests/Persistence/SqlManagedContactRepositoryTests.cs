using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Persistence;

public sealed class SqlManagedContactRepositoryTests
{
    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    [Fact]
    public async Task Upsert_entra_twice_updates_same_contact_and_link()
    {
        await using var db = CreateDb();
        var sut = new SqlManagedContactRepository(db);
        var now = DateTimeOffset.UtcNow;
        var r1 = await sut.UpsertFromEntraDirectoryUserAsync("Alice", "alice@contoso.com", "alice@contoso.com", "oid-aaa", now);
        r1.Created.Should().BeTrue();

        var r2 = await sut.UpsertFromEntraDirectoryUserAsync("Alice Updated", "alice@contoso.com", "alice@contoso.com", "oid-aaa", now.AddMinutes(1));
        r2.Created.Should().BeFalse();
        r2.ManagedContactId.Should().Be(r1.ManagedContactId);

        var link = await sut.FindIdentityLinkByEntraObjectIdAsync("oid-aaa");
        link.Should().NotBeNull();
        link!.ManagedContactId.Should().Be(r1.ManagedContactId);

        var row = await sut.GetByIdAsync(r1.ManagedContactId);
        row!.DisplayName.Should().Be("Alice Updated");
    }

    [Fact]
    public async Task Touching_the_sync_time_changes_only_the_directory_links_times_and_no_contact_field()
    {
        await using var db = CreateDb();
        var sut = new SqlManagedContactRepository(db);
        var first = DateTimeOffset.UtcNow.AddHours(-5);
        var created = await sut.UpsertFromEntraDirectoryUserAsync("Alice", "alice@contoso.com", "alice@contoso.com", "oid-touch", first);
        var contactBefore = (await sut.GetByIdAsync(created.ManagedContactId))!;
        // Another writer has since changed the contact; a sync-time refresh must not write the directory's older values back.
        await sut.SetIsEnabledAsync(created.ManagedContactId, false, first.AddHours(1));
        var later = DateTimeOffset.UtcNow;

        await sut.TouchEntraLinkSyncAsync("oid-touch", later);

        var link = (await sut.FindIdentityLinkByEntraObjectIdAsync("oid-touch"))!;
        link.LastSyncedAtUtc.Should().Be(later);
        link.UpdatedAtUtc.Should().Be(later);
        var contactAfter = (await sut.GetByIdAsync(created.ManagedContactId))!;
        contactAfter.DisplayName.Should().Be(contactBefore.DisplayName);
        contactAfter.EmailAddress.Should().Be(contactBefore.EmailAddress);
        contactAfter.IsEnabled.Should().BeFalse("the refresh wrote no contact field");
        contactAfter.UpdatedAtUtc.Should().Be(first.AddHours(1), "not even the contact's update time moved");
        await sut.TouchEntraLinkSyncAsync("oid-unknown", later);
    }
}
