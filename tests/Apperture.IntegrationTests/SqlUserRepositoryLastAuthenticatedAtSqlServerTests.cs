using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Apperture.SqlTestTargets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// SQL Server evidence that concurrent <see cref="SqlUserRepository.UpdateLastAuthenticatedAtAsync"/>
/// and user resolution do not fail requests due to optimistic concurrency on identity links.
/// It runs against the configured test SQL Server only (never an implicit LocalDB). Without one it fails (see
/// <see cref="SqlTestExecution"/>); a refused target or a migration failure fails it too, because nothing here catches them.
/// </summary>
public sealed class SqlUserRepositoryLastAuthenticatedAtSqlServerTests
{
    private static async Task<(AppertureDbContext Db, DbContextOptions<AppertureDbContext> Options)> CreateMigratedDbAsync()
    {
        var connectionString = SqlServerTestConfiguration.RequireConnectionString();
        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        var options = new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(connectionString)
            .Options;

        var db = new AppertureDbContext(options);
        try
        {
            if (!await db.Database.CanConnectAsync())
                throw SqlTestExecution.Unavailable("the test catalog was created but cannot be connected to");

            await AppertureDatabaseMigrator.MigrateAsync(db);
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }

        return (db, options);
    }

    [Fact]
    public async Task UpdateLastAuthenticatedAtAsync_parallel_calls_do_not_throw()
    {
        var (db, options) = await CreateMigratedDbAsync();
        await using (db)
        {
            var externalId = $"last-auth-{Guid.NewGuid():N}";
            var userId = $"user-{Guid.NewGuid():N}"[..24];
            var linkId = $"link-{Guid.NewGuid():N}"[..24];
            await SeedIdentityLinkAsync(db, userId, linkId, externalId, lastAuthenticatedAt: null);

            var at = DateTimeOffset.UtcNow;
            var tasks = Enumerable.Range(0, 20).Select(async _ =>
            {
                await using var taskDb = new AppertureDbContext(options);
                var repo = new SqlUserRepository(taskDb, NullLogger<SqlUserRepository>.Instance);
                await repo.UpdateLastAuthenticatedAtAsync(IdentitySource.Local, externalId, at, CancellationToken.None);
            });

            var act = () => Task.WhenAll(tasks);
            await act.Should().NotThrowAsync();

            var updated = await db.IdentityLinks.AsNoTracking()
                .SingleAsync(l => l.Source == (int)IdentitySource.Local && l.ExternalId == externalId);
            updated.LastAuthenticatedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task ResolveUserIdAsync_parallel_calls_do_not_throw()
    {
        var (db, options) = await CreateMigratedDbAsync();
        await using (db)
        {
            var externalId = $"resolve-{Guid.NewGuid():N}";
            var userId = $"user-{Guid.NewGuid():N}"[..24];
            var linkId = $"link-{Guid.NewGuid():N}"[..24];
            await SeedIdentityLinkAsync(db, userId, linkId, externalId, lastAuthenticatedAt: null);

            // A local session's actor: the user id, bound to the user's Local link.
            var actor = new HumanActor(new ActorId(userId), "Operator", preferredIdentitySource: IdentitySource.Local);
            var tasks = Enumerable.Range(0, 20).Select(async _ =>
            {
                await using var taskDb = new AppertureDbContext(options);
                var repo = new SqlUserRepository(taskDb, NullLogger<SqlUserRepository>.Instance);
                var resolver = new SqlPrincipalToUserResolver(repo);
                await resolver.ResolveUserIdAsync(actor, CancellationToken.None);
            });

            var act = () => Task.WhenAll(tasks);
            await act.Should().NotThrowAsync();

            await using var verifyDb = new AppertureDbContext(options);
            var verifyRepo = new SqlUserRepository(verifyDb, NullLogger<SqlUserRepository>.Instance);
            var verifyResolver = new SqlPrincipalToUserResolver(verifyRepo);
            var resolved = await verifyResolver.ResolveUserIdAsync(actor, CancellationToken.None);
            resolved.Should().Be(new UserId(userId));
        }
    }

    [Fact]
    public async Task UpdateLastAuthenticatedAtAsync_throttles_writes_within_interval()
    {
        var (db, _) = await CreateMigratedDbAsync();
        await using var _db = db;

        var externalId = $"throttle-{Guid.NewGuid():N}";
        var userId = $"user-{Guid.NewGuid():N}"[..24];
        var linkId = $"link-{Guid.NewGuid():N}"[..24];
        var initial = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        await SeedIdentityLinkAsync(db, userId, linkId, externalId, initial);

        var repo = new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance);
        await repo.UpdateLastAuthenticatedAtAsync(
            IdentitySource.Local,
            externalId,
            initial.AddSeconds(30),
            CancellationToken.None);

        var row = await db.IdentityLinks.AsNoTracking()
            .SingleAsync(l => l.Source == (int)IdentitySource.Local && l.ExternalId == externalId);
        row.LastAuthenticatedAt.Should().Be(initial);
    }

    private static async Task SeedIdentityLinkAsync(
        AppertureDbContext db,
        string userId,
        string linkId,
        string externalId,
        DateTimeOffset? lastAuthenticatedAt)
    {
        db.AddUsersWithCurrentUsernames(new UserEntity
        {
            Id = userId,
            Username = externalId,
            DisplayName = "Operator"
        });
        db.IdentityLinks.Add(new IdentityLinkEntity
        {
            Id = linkId,
            UserId = userId,
            Source = (int)IdentitySource.Local,
            ExternalId = externalId,
            LastAuthenticatedAt = lastAuthenticatedAt
        });
        await db.SaveChangesAsync();
    }
}
