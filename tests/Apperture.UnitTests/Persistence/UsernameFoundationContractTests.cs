using System.Reflection;
using System.Runtime.CompilerServices;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.Persistence;

/// <summary>
/// Contract checks that stop a creation path from persisting a user without a proper username. The
/// database constraints themselves are proven by the SQL Server tests, not here (EF InMemory enforces
/// no collation or unique index).
/// </summary>
public sealed class UsernameFoundationContractTests
{
    [Fact]
    public void User_username_is_a_required_member_so_every_construction_must_assign_it()
    {
        var property = typeof(User).GetProperty(nameof(User.Username))!;

        property.GetCustomAttribute<RequiredMemberAttribute>().Should().NotBeNull(
            "removing 'required' would let a creation path compile without choosing a username");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Alice")] // not canonical
    [InlineData("jane@contoso.com")] // a new user cannot be created with a grandfathered shape
    [InlineData("abcdef0123456789abcdef01")] // user-id shaped
    public async Task AddAsync_rejects_a_user_without_an_assignable_username(string username)
    {
        await using var db = NewDb();
        var repository = new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance);
        var user = new User(new UserId("u1"), "Someone", [new IdentityLink(IdentitySource.Entra, "oid", null, TenantId: "t")], [], false)
        {
            Username = username
        };

        var act = async () => await repository.AddAsync(user);

        await act.Should().ThrowAsync<ArgumentException>();
        (await db.Users.CountAsync()).Should().Be(0);
        (await db.UsernameLedger.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AddAsync_requires_the_local_link_to_mirror_the_username()
    {
        await using var db = NewDb();
        var repository = new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance);
        var user = new User(new UserId("u1"), "Someone", [new IdentityLink(IdentitySource.Local, "alice", null)], [], false)
        {
            Username = "alice.smith"
        };

        var act = async () => await repository.AddAsync(user);

        (await act.Should().ThrowAsync<UsernameException>()).Which.Code.Should().Be(UsernameErrorCodes.Mismatch);
    }

    [Fact]
    public async Task AddAsync_rejects_two_local_links()
    {
        await using var db = NewDb();
        var repository = new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance);
        var user = new User(
            new UserId("u1"),
            "Someone",
            [new IdentityLink(IdentitySource.Local, "alice", null), new IdentityLink(IdentitySource.Local, "alice2", null)],
            [],
            false)
        {
            Username = "alice"
        };

        var act = async () => await repository.AddAsync(user);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AddAsync_writes_the_user_and_its_current_ledger_row_together()
    {
        await using var db = NewDb();
        var repository = new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance);
        var user = new User(new UserId("u1"), "Alice", [new IdentityLink(IdentitySource.Local, "alice", null)], [], false)
        {
            Username = "alice"
        };

        await repository.AddAsync(user);

        (await db.Users.SingleAsync()).Username.Should().Be("alice");
        var ledger = await db.UsernameLedger.SingleAsync();
        ledger.Username.Should().Be("alice");
        ledger.UserId.Should().Be("u1");
        ledger.RetiredAtUtc.Should().BeNull();
        (await repository.GetByIdAsync(new UserId("u1")))!.Username.Should().Be("alice");
    }

    [Fact]
    public async Task Adding_a_local_link_requires_the_users_username()
    {
        await using var db = NewDb();
        var repository = new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance);
        await repository.AddAsync(new User(new UserId("u1"), "Jane", [new IdentityLink(IdentitySource.Entra, "oid", null, TenantId: "t")], [], false)
        {
            Username = "jane.doe"
        });

        var mismatch = async () => await repository.AddIdentityLinkAsync(new UserId("u1"), IdentitySource.Local, "jane");

        (await mismatch.Should().ThrowAsync<UsernameException>()).Which.Code.Should().Be(UsernameErrorCodes.Mismatch);
        await repository.AddIdentityLinkAsync(new UserId("u1"), IdentitySource.Local, "jane.doe");
        (await db.IdentityLinks.CountAsync(l => l.Source == (int)IdentitySource.Local && l.ExternalId == "jane.doe")).Should().Be(1);
    }

    [Fact]
    public void Backfill_reads_the_same_canonicalization_audit_action_the_service_writes()
    {
        UsernameBackfill.RecoveryAccessCanonicalizedAuditAction.Should().Be(EmergencyAccessAuditActionNames.Canonicalized);
    }

    [Theory]
    [InlineData("userId=abc123 previousUsername=pat.admin", "abc123", "pat.admin")]
    [InlineData("userId=abc123 previousUsername=jane@contoso.com; Resource=ResourceDescriptor { ResourceType = user, ScopeKey = instance }", "abc123", "jane@contoso.com")]
    [InlineData("userId=abc123 previousUsername=john smith", "abc123", "john smith")]
    public void Backfill_parses_previous_recovery_access_names(string details, string userId, string previous)
    {
        UsernameBackfill.TryParseCanonicalization(details, out var parsedUser, out var parsedName).Should().BeTrue();
        parsedUser.Should().Be(userId);
        parsedName.Should().Be(previous);
    }

    [Theory]
    [InlineData("previousUsername=pat.admin")]
    [InlineData("userId=abc123 previousUsername=")]
    [InlineData("userId=abc123 previousUsername=Pat.Admin")] // never normalized at write time: not trusted
    [InlineData("userId=abc123 fallback=true")]
    public void Backfill_ignores_canonicalization_details_it_cannot_map_safely(string details)
    {
        UsernameBackfill.TryParseCanonicalization(details, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("Bootstrap administrator (00000000-0000-0000-0000-000000000001)", true)]
    [InlineData("Bootstrap administrator", false)]
    [InlineData("Pat Admin", false)]
    public void Backfill_skips_the_default_bootstrap_display_text(string displayName, bool isDefault)
    {
        UsernameBackfill.IsDefaultBootstrapDisplayName(displayName).Should().Be(isDefault);
    }

    private static AppertureDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase("username-contract-" + Guid.NewGuid().ToString("N"))
            .Options);
}
