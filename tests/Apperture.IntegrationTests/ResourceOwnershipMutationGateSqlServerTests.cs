using Apperture.Core.Application.ResourceContexts;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// The load-bearing primitive for all CTX-3B race prevention (design gate §8.7, product decision P1):
/// one transaction-owned application lock that every ownership-sensitive mutation path acquires FIRST.
/// If these tests are red, none of the higher-level guarantees in
/// <see cref="ResourceOwnershipMoveConcurrencySqlServerTests"/> can hold.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ResourceOwnershipMutationGateSqlServerTests
{
    [Fact]
    public async Task Gate_blocks_a_second_holder_until_the_first_transaction_commits()
    {
        await using var fixture = await SqlServerScratchDatabase.CreateAsync("Apperture_Gate_");

        var firstAcquired = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();
        var secondAcquired = new TaskCompletionSource();

        var firstTask = Task.Run(async () =>
        {
            await using var db = fixture.NewDb();
            await using var tx = await db.Database.BeginTransactionAsync();
            await new SqlResourceOwnershipMutationGate(db).AcquireAsync();
            firstAcquired.SetResult();
            await releaseFirst.Task;
            await tx.CommitAsync();
        });

        await firstAcquired.Task;

        var secondTask = Task.Run(async () =>
        {
            await using var db = fixture.NewDb();
            await using var tx = await db.Database.BeginTransactionAsync();
            await new SqlResourceOwnershipMutationGate(db).AcquireAsync(); // must block here
            secondAcquired.SetResult();
            await tx.CommitAsync();
        });

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        secondAcquired.Task.IsCompleted.Should().BeFalse(
            "the second holder must block behind the first transaction's exclusive application lock");

        releaseFirst.SetResult();
        await firstTask;
        await secondTask;

        secondAcquired.Task.IsCompleted.Should().BeTrue("the gate is released when the first transaction commits");
    }

    [Fact]
    public async Task Gate_timeout_surfaces_as_a_human_readable_busy_conflict()
    {
        await using var fixture = await SqlServerScratchDatabase.CreateAsync("Apperture_GateBusy_");

        var firstAcquired = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();

        var firstTask = Task.Run(async () =>
        {
            await using var db = fixture.NewDb();
            await using var tx = await db.Database.BeginTransactionAsync();
            await new SqlResourceOwnershipMutationGate(db).AcquireAsync();
            firstAcquired.SetResult();
            await releaseFirst.Task;
            await tx.CommitAsync();
        });

        await firstAcquired.Task;

        await using (var db = fixture.NewDb())
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var gate = new SqlResourceOwnershipMutationGate(db, timeout: TimeSpan.FromMilliseconds(250));
            var act = async () => await gate.AcquireAsync();

            var thrown = (await act.Should().ThrowAsync<ResourceOwnershipBusyException>()).Which;
            thrown.Code.Should().Be(ResourceOwnershipBusyException.BusyCode);
            thrown.Message.Should().NotContain("sp_getapplock",
                "operators see a human-readable transient conflict, never a raw SQL/app-lock failure");
        }

        releaseFirst.SetResult();
        await firstTask;
    }

    [Fact]
    public async Task Gate_refuses_to_acquire_outside_a_transaction()
    {
        await using var fixture = await SqlServerScratchDatabase.CreateAsync("Apperture_GateNoTx_");

        await using var db = fixture.NewDb();
        var act = async () => await new SqlResourceOwnershipMutationGate(db).AcquireAsync();

        await act.Should().ThrowAsync<InvalidOperationException>(
            "a transaction-owned lock acquired outside a transaction would be released immediately and guarantee nothing");
    }
}
