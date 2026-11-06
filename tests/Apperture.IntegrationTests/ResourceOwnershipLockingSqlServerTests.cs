using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// Proves <c>GetByIdForUpdateAsync</c> genuinely takes a blocking row lock on SQL Server. This is the
/// CTX-3B <b>defense-in-depth</b> layer, not the correctness mechanism: correctness comes from the
/// ownership mutation gate (Task 1), which every participating path acquires first. Row locks exist so a
/// future mutation path that forgets the gate still cannot interleave freely with the six that have it.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ResourceOwnershipLockingSqlServerTests
{
    [Fact]
    public async Task GetByIdForUpdateAsync_blocks_a_concurrent_locked_reader_until_commit()
    {
        await using var fixture = await SqlServerScratchDatabase.CreateAsync("Apperture_LockProbe_");

        await using (var seedDb = fixture.NewDb())
        {
            seedDb.Integrations.Add(new IntegrationEntity
            {
                Id = "lock-probe-int", DisplayName = "Lock Probe", ModuleId = "Sample.Mod",
                ResourceContextId = ResourceContextIds.SharedValue, State = 0,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            });
            await seedDb.SaveChangesAsync();
        }

        var firstLockAcquired = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();
        var secondReadCompleted = new TaskCompletionSource();

        var firstTask = Task.Run(async () =>
        {
            await using var db = fixture.NewDb();
            await using var tx = await db.Database.BeginTransactionAsync();
            await new SqlIntegrationRepository(db).GetByIdForUpdateAsync(new IntegrationId("lock-probe-int"));
            firstLockAcquired.SetResult();
            await releaseFirst.Task;
            await tx.CommitAsync();
        });

        await firstLockAcquired.Task;

        var secondTask = Task.Run(async () =>
        {
            await using var db = fixture.NewDb();
            await using var tx = await db.Database.BeginTransactionAsync();
            await new SqlIntegrationRepository(db).GetByIdForUpdateAsync(new IntegrationId("lock-probe-int")); // blocks
            secondReadCompleted.SetResult();
            await tx.CommitAsync();
        });

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        secondReadCompleted.Task.IsCompleted.Should().BeFalse(
            "the second locked read must block behind the first transaction's UPDLOCK");

        releaseFirst.SetResult();
        await firstTask;
        await secondTask;

        secondReadCompleted.Task.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task SetResourceContextIdAsync_writes_only_the_ownership_column()
    {
        await using var fixture = await SqlServerScratchDatabase.CreateAsync("Apperture_NarrowWrite_");

        await using var db = fixture.NewDb();
        // The reserved "shared" ResourceContext row is seeded by the Ctx1_ResourceContextFoundation
        // migration itself (see that migration's migrationBuilder.Sql INSERT), so it must not be
        // re-inserted here -- doing so violates PK_ResourceContexts against the already-migrated row.
        // "app-a" is the move target and is *not* pre-seeded, and FK_Integrations_ResourceContexts_
        // ResourceContextId requires it to exist before SetResourceContextIdAsync can move a row onto it.
        db.ResourceContexts.Add(new ResourceContextEntity
        {
            Id = "app-a", Name = "App A", Slug = "app-a",
            Kind = ResourceContextKinds.Application, State = ResourceContextStates.Active, IsReserved = false,
            CreatedAtUtc = DateTimeOffset.UnixEpoch, UpdatedAtUtc = DateTimeOffset.UnixEpoch
        });
        db.Integrations.Add(new IntegrationEntity
        {
            Id = "narrow-int", DisplayName = "Original Name", ModuleId = "Sample.Mod", ScopeKey = "instance",
            ResourceContextId = ResourceContextIds.SharedValue, State = 0,
            CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch
        });
        await db.SaveChangesAsync();

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await new SqlIntegrationRepository(db).SetResourceContextIdAsync(
                new IntegrationId("narrow-int"), new ResourceContextId("app-a"));
            await tx.CommitAsync();
        }

        await using var verifyDb = fixture.NewDb();
        var entity = await verifyDb.Integrations.AsNoTracking().SingleAsync(e => e.Id == "narrow-int");
        entity.ResourceContextId.Should().Be("app-a");
        entity.DisplayName.Should().Be("Original Name");
        entity.ScopeKey.Should().Be("instance");
        entity.UpdatedAt.Should().Be(DateTimeOffset.UnixEpoch,
            "the narrow ownership write touches exactly one column and nothing else");
    }
}
