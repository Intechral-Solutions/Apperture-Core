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
}
