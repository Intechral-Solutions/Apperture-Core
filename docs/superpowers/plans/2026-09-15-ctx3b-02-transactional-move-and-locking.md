# CTX-3B Plan 2 — Transactional Move and Ownership Mutation Serialization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Integration/Connection/Schedule moves real, transactional and race-safe, by introducing **one** serialization point for every ownership-sensitive mutation in the platform, so that a move and a concurrent ownership-sensitive write can never persist an invalid cross-context relationship — and so that correctness never depends on cross-entity row-lock ordering.

## The invariant this plan exists to protect

> No persisted binding `(I, C)` may exist where `ResourceContextReferenceRule.CanReference(I.ResourceContextId, C.ResourceContextId)` is false.
>
> And: no archived ResourceContext may acquire newly-moved live ownership.

## Why single-row locking is not sufficient (the finding that shaped this plan)

The first draft of this plan locked only the *primary* row in each move (the Integration being moved, or the Connection being moved) and read the counterparty with an ordinary unlocked `AsNoTracking()` read. That leaves the following interleaving completely unserialized, because the two transactions never touch the same row:

Setup: Integration `I` in App A. Connection `C` in Shared. Binding `I→C` exists.

| Step | Tx1: `MoveIntegration(I, B)` | Tx2: `MoveConnection(C, A)` |
|---|---|---|
| 1 | `UPDLOCK` on `I` | |
| 2 | | `UPDLOCK` on `C` — **no conflict, different row** |
| 3 | reads `C` unlocked → Shared. `CanReference(B, shared)` = **true**. No blockers. | |
| 4 | | reads `I` unlocked → committed value still **A**. `CanReference(A, A)` = **true**. No blockers. |
| 5 | writes `I.ctx = B`, commit | writes `C.ctx = A`, commit |

Final state: `I` in B, `C` in A, binding `I→C` persisted. `CanReference(B, A)` = **false**. The invariant is violated by two supported API calls, with no error and two clean-looking audit pairs.

The obvious repair — have each move also lock its counterparties — creates a classic ABBA deadlock, because the three paths would then acquire the same two entity types in opposite orders:

```text
Integration move:  I  →  C₁..Cₙ
Connection move:   C  →  I₁..Iₘ
Binding create:    I  →  C
```

`MoveIntegration` holds `I` and wants `C`; `MoveConnection` holds `C` and wants `I`. Two further complications rule out a simple pairwise ordering argument: a move may need to lock *many* counterparty rows, and the set of counterparties is itself racy (it cannot be known until the bindings have been read, and the binding set can change between that read and the locks).

**Product decision P1 (approved): serialize on one transaction-owned SQL Server application lock instead.** One lock means one order, so deadlock between these paths is impossible by construction, the multi-row and set-growth problems disappear, and the guarantee is directly testable rather than being an unenforced ordering invariant that lives in reviewers' heads.

## Architecture

**The ownership mutation gate.** A single named, transaction-owned SQL Server application lock — `apperture:resource-ownership` — acquired via `sp_getapplock`, wrapped in one narrow abstraction:

```text
IResourceOwnershipMutationGate.AcquireAsync(ct)
```

Every ownership-sensitive mutation path does exactly this, in exactly this order:

```text
1. Begin the database transaction
2. Acquire the ownership mutation gate          <-- globally first
3. Authoritatively re-read the rows it needs    (UPDLOCK on rows it will mutate)
4. Run the shared Plan 1 validator against that re-read state
5. Mutate, through the narrow SetResourceContextIdAsync writes only
6. Commit
```

The six participating paths:

| # | Path | Gate | Rows it `UPDLOCK`s |
|---|---|---|---|
| 1 | `ResourceOwnershipMoveService.MoveIntegrationAsync` | ✅ | the Integration |
| 2 | `ResourceOwnershipMoveService.MoveConnectionAsync` | ✅ | the Connection |
| 3 | `ResourceOwnershipMoveService.MoveScheduleAsync` (standalone) | ✅ | the Schedule |
| 4 | `BindingService.CreateBindingAsync` | ✅ | the Integration and the Connection |
| 5 | `SqlScheduleManagementService.CreateScheduleAsync` (attach path) | ✅ | the Integration |
| 6 | `ResourceContextManagementService.ArchiveAsync` | ✅ | — (reads the ownership summary) |

Row-level `UPDLOCK, ROWLOCK` on the rows actually being mutated is **retained as defense in depth**, so a future mutation path that forgets the gate still cannot interleave freely with these six. But **correctness does not depend on cross-entity row-lock ordering** — it depends on the gate, which is acquired before any ownership-sensitive read in all six paths.

**Not a distributed-lock framework.** `IResourceOwnershipMutationGate` has one method, one lock name, one purpose. It owns the lock name, transaction-owned acquisition, the bounded timeout, return-code handling, and non-relational (InMemory) behavior. Do not generalize it, do not add a second lock name, and do not scatter raw `sp_getapplock` calls through services.

**Scope and cost.** The gate is **database-scoped** and intentionally serializes these six operations instance-wide. All six are low-frequency operator actions (clicking Move, clicking Bind, creating a schedule, archiving an Application). Nothing on the runtime hot path — scheduler claim, dispatch, run writes, readiness evaluation — touches this lock, so throughput is unaffected. Contention is bounded by how fast a human clicks.

**Retry safety.** Verified: `ServiceCollectionExtensions.cs:36` and `:62` both call `sqlOptions.EnableRetryOnFailure(opts.MaxRetryCount)`, so `_db.Database.CreateExecutionStrategy()` returns `SqlServerRetryingExecutionStrategy`, which re-executes the delegate on transient failures including deadlock (error 1205). That retry runs against the **same** `AppertureDbContext`, so every attempt must be self-contained:

- **`AsNoTracking()` locked reads** — nothing is cached in the change tracker, so a retried attempt cannot read back a stale in-memory value that a rolled-back attempt left behind.
- **`ExecuteUpdateAsync` narrow relational writes** — the write bypasses the change tracker entirely, so a rolled-back attempt leaves no `Unchanged`-marked entity carrying the post-mutation value. (This is the failure mode the first draft had: `FromSql` tracked read + tracked `SaveChangesAsync` meant a deadlock retry would re-validate against the mutated in-memory value, hit `already_in_target`, write nothing, and **report success having persisted nothing**.)
- **`_db.ChangeTracker.Clear()` as the first statement inside every execution-strategy delegate**, before the transaction is opened.
- Nothing captured outside the delegate may carry state from a previous attempt.

**Tech Stack:** C#/.NET 9, EF Core (`FromSqlInterpolated`, `ExecuteUpdateAsync`, `Database.BeginTransactionAsync`, `Database.CreateExecutionStrategy`), SQL Server (`sp_getapplock`, `WITH (UPDLOCK, ROWLOCK)`), xUnit, FluentAssertions.

**Spec:** `docs/01-architecture/transitions/resource-contexts/ctx3-design-gate.md` §8.4 step 1/4, §8.5 steps 1/5, §8.6, §8.7 (races), §13 CTX-3B step 2, §14 CTX-3B acceptance criteria (concurrency and historical-immutability bullets). Product decisions P1 and P3 from the CTX-3B review.

---

## Global Constraints

- **The gate is acquired first, always.** Begin transaction, then `AcquireAsync`, *then* any ownership-sensitive read, any `UPDLOCK` read, and any mutation. A path that reads first and gates second is wrong even if it happens to pass its tests.
- **No correctness argument may rest on lock ordering.** If a reviewer has to reason about which entity type is locked before which, the design has regressed. Row locks are defense in depth only.
- **Do not introduce `SERIALIZABLE` isolation, table locks, a second application-lock name, or a generic locking framework.**
- Preview and move share the **same validator** (Plan 1's `Validate*MoveAsync`). Move calls the identical private method, inside the transaction, after the gate and the locked re-reads. Do not fork or duplicate validation logic.
- `IIntegrationRepository.UpdateAsync` / `IConnectionRepository.UpdateAsync` / `IScheduleRepository.UpdateDefinitionAsync` deliberately never rewrite `ResourceContextId` (existing comments in all three `Sql*Repository` files) — this plan does not change that. Ownership writes go exclusively through the new narrow `SetResourceContextIdAsync` methods.
- **Relational-only transactions:** `_db.Database.IsRelational() ? await BeginTransactionAsync(ct) : null`, matching the house pattern in `OperationStartDispatchService.cs:79-81`. Do not rely on a suppressed `InMemoryEventId.TransactionIgnoredWarning` to make unconditional `BeginTransactionAsync` work.
- **`AsNoTracking()` locked reads, `ExecuteUpdateAsync` narrow writes, `ChangeTracker.Clear()` per attempt** — see "Retry safety" above. Every one of the three is load-bearing.
- **Write the rejected-move control flow in its final shape now** (commit the transaction rather than abandoning it), so Plan 4 can add its audit append without rewriting this plan's control flow or invalidating this plan's tests.
- No audit records are written by this plan (Plan 4). No readiness issue code (Plan 3). No HTTP surface (Plan 5).

---

## File Structure

**New files:**
- `src/Apperture.Core/Apperture.Core.Application/ResourceContexts/IResourceOwnershipMutationGate.cs` — the one-method abstraction plus `ResourceOwnershipBusyException`.
- `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlResourceOwnershipMutationGate.cs` — the `sp_getapplock` implementation.
- `tests/Apperture.IntegrationTests/ResourceOwnershipMutationGateSqlServerTests.cs` — proves the gate genuinely serializes two transactions and that a timeout surfaces as `ownership_operation_busy`.
- `tests/Apperture.IntegrationTests/ResourceOwnershipLockingSqlServerTests.cs` — proves `GetByIdForUpdateAsync` takes a real blocking row lock (the defense-in-depth layer).
- `tests/Apperture.IntegrationTests/ResourceOwnershipMoveConcurrencySqlServerTests.cs` — the deterministic interleaving tests.
- `tests/Apperture.UnitTests/ResourceContexts/ResourceOwnershipMoveMutationTests.cs` — non-concurrency behavioral tests for the now-real `Move*Async`.
- `tests/Apperture.UnitTests/ResourceContexts/ResourceMoveHistoricalImmutabilityTests.cs` — CTX-2 provenance immutability, run against production `Move*Async`.

**Modified files:**
- `src/Apperture.Core/Apperture.Core.Application/ResourceContexts/IResourceOwnershipMoveService.cs` — add the three `Move*Async` members (declaration and implementation land together; Plan 1 deliberately left them out).
- `src/Apperture.Core/Apperture.Core.Application/Services/IIntegrationRepository.cs`, `IConnectionRepository.cs`, `IScheduleRepository.cs` — add `GetByIdForUpdateAsync` and `SetResourceContextIdAsync`.
- `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlIntegrationRepository.cs`, `SqlConnectionRepository.cs`, `SqlScheduleRepository.cs`.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/BindingService.cs`.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/SqlScheduleManagementService.cs`.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceContextManagementService.cs`.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceOwnershipMoveService.cs`.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Integration.cs` — register the gate.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/Apperture.ControlPlane.Security.csproj` — add `<InternalsVisibleTo Include="Apperture.IntegrationTests" />` (it already has one for `Apperture.UnitTests`).
- `tests/Apperture.UnitTests/ResourceContexts/ResourceOwnershipMoveTestHarness.cs`.

**Interfaces produced for later plans:**
- `IResourceOwnershipMutationGate` — Plan 6a does **not** need it (Integration hard-delete is guarded by blockers, not by ownership reachability), but any future ownership-sensitive mutation must join the six.
- `IResourceOwnershipMoveService.Move*Async` — now real. Plan 4 adds audit appends inside the same transaction; Plan 5's controllers call them.
- `ResourceOwnershipMoveService.MidTransactionHook` (internal, test-only) — Plan 4 may reuse it to prove audit-failure rollback.

---

### Task 1: The ownership mutation gate, and the P3 preflight

**Files:**
- Create: `src/Apperture.Core/Apperture.Core.Application/ResourceContexts/IResourceOwnershipMutationGate.cs`
- Create: `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlResourceOwnershipMutationGate.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Integration.cs`
- Test: `tests/Apperture.IntegrationTests/ResourceOwnershipMutationGateSqlServerTests.cs`

**Interfaces:**
- Produces: `IResourceOwnershipMutationGate.AcquireAsync`, `ResourceOwnershipBusyException`, `SqlResourceOwnershipMutationGate`.

This task is first because the serialization decision determines what every later task locks, re-reads and tests. Do not start Task 2 until the gate exists and its test passes.

- [ ] **Step 0: Run the product-decision P3 preflight before writing any code**

Against a representative database (dev, and any reachable production-shaped snapshot), run:

```sql
SELECT s.Id, s.Name, s.IntegrationId,
       s.ResourceContextId  AS ScheduleContext,
       i.ResourceContextId  AS IntegrationContext
FROM   [ScheduleDefinitions] s
JOIN   [Integrations] i ON i.Id = s.IntegrationId
WHERE  s.IntegrationId IS NOT NULL
  AND  ISNULL(NULLIF(LTRIM(RTRIM(s.ResourceContextId)), ''), 'shared')
       <> i.ResourceContextId;
```

The `ISNULL(NULLIF(...))` wrapper mirrors `SqlScheduleRepository.ToDefinition`, which coalesces a null or whitespace column to `shared` — a raw `s.ResourceContextId <> i.ResourceContextId` comparison would miss exactly the legacy NULL rows this preflight exists to find.

- **Count = 0 (expected):** record the result in the implementation notes, keep `schedule_context_inconsistent` as a strict hard blocker, and add no repair operation. Proceed.
- **Count > 0:** **stop.** Inspect the actual rows and report them before implementation continues. Prefer a deliberate one-time support/DBA repair over building a generalized operator repair feature, unless the evidence shows such a feature is genuinely needed. A move must never silently auto-repair ownership drift.

- [ ] **Step 1: Write the failing gate test**

```csharp
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
        if (fixture is null)
            return; // soft-skip: no reachable SQL endpoint configured

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
        if (fixture is null)
            return; // soft-skip

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
        if (fixture is null)
            return; // soft-skip

        await using var db = fixture.NewDb();
        var act = async () => await new SqlResourceOwnershipMutationGate(db).AcquireAsync();

        await act.Should().ThrowAsync<InvalidOperationException>(
            "a transaction-owned lock acquired outside a transaction would be released immediately and guarantee nothing");
    }
}
```

`SqlServerScratchDatabase` is a small shared helper this task introduces in `tests/Apperture.IntegrationTests/` (throwaway catalog, `EnsureTestDatabaseExists`, `MigrateAsync`, `NewDb()`, best-effort drop on dispose, `null` when no endpoint is reachable), factored out of the existing duplicated try/finally block in `ResourceContextMigrationSqlServerTests`. Every SQL Server test in this plan and in Plan 4 uses it, so the catalog lifecycle is written once. **Per-test throwaway catalogs are what keep these tests safe to run in parallel — do not disable test parallelism anywhere.**

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~ResourceOwnershipMutationGateSqlServerTests"`
Expected: FAIL — compile error, nothing exists yet.

- [ ] **Step 3: Write the abstraction**

```csharp
namespace Apperture.Core.Application.ResourceContexts;

/// <summary>
/// The single serialization point for ResourceContext ownership mutations (design gate §8.7, CTX-3B
/// product decision P1).
/// </summary>
/// <remarks>
/// <para>
/// Six paths participate, and every one of them acquires this gate as the first thing it does after
/// opening its transaction, before any ownership-sensitive read, any row lock, and any mutation:
/// Integration move, Connection move, standalone Schedule move, binding creation, Integration-attached
/// Schedule creation, and ResourceContext archive.
/// </para>
/// <para>
/// <b>Why one gate rather than ordered row locks.</b> Ownership validity is a property of a *pair* of
/// rows (a binding's Integration and its Connection), and the two move operations approach that pair from
/// opposite ends. Locking only the primary row leaves the two moves completely unserialized; locking both
/// ends in each path creates an ABBA deadlock between Integration move and Connection move, made worse by
/// multi-row counterparty sets whose membership is itself racy. One lock means one order, so deadlock
/// between these paths is impossible by construction and no ordering invariant has to be maintained by
/// reviewers.
/// </para>
/// <para>
/// <b>This is not a distributed-lock framework.</b> One lock name, one purpose, one method. Do not
/// generalize it, do not add a second resource name, and do not call <c>sp_getapplock</c> directly
/// anywhere else.
/// </para>
/// <para>
/// <b>Scope and cost.</b> The lock is database-scoped and intentionally serializes these six low-frequency
/// operator mutations instance-wide. No runtime path — scheduler claim, dispatch, run writes, readiness
/// evaluation — acquires it, so throughput is unaffected.
/// </para>
/// </remarks>
public interface IResourceOwnershipMutationGate
{
    /// <summary>
    /// Acquires the ownership mutation gate for the lifetime of the caller's ambient transaction.
    /// Released automatically on commit or rollback.
    /// </summary>
    /// <exception cref="InvalidOperationException">No transaction is open on the underlying context.</exception>
    /// <exception cref="ResourceOwnershipBusyException">
    /// Another ownership mutation held the gate past the configured timeout. Transient: the caller should
    /// surface it as a retryable conflict, never as an internal error.
    /// </exception>
    ValueTask AcquireAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Another ownership mutation is in progress and did not release the gate within the bounded timeout.
/// Transient and safe to retry. Carries a stable machine code so the API can map it to a specific
/// operator-facing message rather than a generic 500.
/// </summary>
public sealed class ResourceOwnershipBusyException : Exception
{
    /// <summary>Stable machine code for the API/UI. Plan 5 maps this to 409.</summary>
    public const string BusyCode = "ownership_operation_busy";

    public ResourceOwnershipBusyException(string message) : base(message) { }

    public string Code => BusyCode;
}
```

- [ ] **Step 4: Write the SQL Server implementation**

```csharp
using System.Data;
using Apperture.Core.Application.ResourceContexts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Apperture.Persistence.SqlServer;

/// <inheritdoc cref="IResourceOwnershipMutationGate" />
public sealed class SqlResourceOwnershipMutationGate : IResourceOwnershipMutationGate
{
    /// <summary>The one and only ownership gate resource name. Never parameterize or extend this.</summary>
    public const string LockResource = "apperture:resource-ownership";

    /// <summary>
    /// Bounded by design. Long enough that a normal operator mutation behind another one never fails,
    /// short enough that a stuck transaction surfaces as a clear transient conflict instead of an
    /// indefinitely hanging request.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly AppertureDbContext _db;
    private readonly TimeSpan _timeout;

    public SqlResourceOwnershipMutationGate(AppertureDbContext db, TimeSpan? timeout = null)
    {
        _db = db;
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <inheritdoc />
    public async ValueTask AcquireAsync(CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsRelational())
        {
            // EF Core InMemory has no lock manager and no transactions. Unit tests exercise ownership
            // business logic; serialization itself is only provable against real SQL Server, which is
            // what ResourceOwnershipMutationGateSqlServerTests and
            // ResourceOwnershipMoveConcurrencySqlServerTests do.
            return;
        }

        var transaction = _db.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "The ownership mutation gate must be acquired inside an already-open transaction. " +
                "A 'Transaction'-owned application lock taken outside a transaction is released " +
                "immediately and guarantees nothing.");

        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', " +
            "@LockOwner = N'Transaction', @LockTimeout = @lockTimeout;";

        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        command.Parameters.Add(result);
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = LockResource });
        command.Parameters.Add(new SqlParameter("@lockTimeout", SqlDbType.Int) { Value = (int)_timeout.TotalMilliseconds });

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // sp_getapplock: 0 = granted, 1 = granted after waiting, -1 = timeout, -2 = cancelled,
        // -3 = chosen as deadlock victim, -999 = parameter/call error.
        var code = result.Value is int value ? value : -999;
        if (code >= 0)
            return;

        if (code is -1 or -2 or -3)
        {
            throw new ResourceOwnershipBusyException(
                "Another ownership change is in progress. Wait a moment and try again.");
        }

        throw new InvalidOperationException(
            $"Failed to acquire the resource ownership mutation gate (code {code}).");
    }
}
```

- [ ] **Step 5: Register the gate**

In `ServiceCollectionExtensions.Integration.cs`, alongside the Plan 1 move-service registration:

```csharp
        services.AddScoped<Apperture.Core.Application.ResourceContexts.IResourceOwnershipMutationGate,
                           Apperture.Persistence.SqlServer.SqlResourceOwnershipMutationGate>();
```

The gate is `Scoped` for the same reason the rest of this epic is: it must observe the request's own `AppertureDbContext` and its `CurrentTransaction`.

- [ ] **Step 6: Run to verify it passes**

Run: `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~ResourceOwnershipMutationGateSqlServerTests"`
Expected: PASS against a reachable SQL Server. **A soft-skip is not a pass** — see this plan's Validation section.

- [ ] **Step 7: Commit**

```bash
dotnet build Apperture.slnx
git add src/Apperture.Core/Apperture.Core.Application/ResourceContexts/IResourceOwnershipMutationGate.cs \
        src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlResourceOwnershipMutationGate.cs \
        src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Integration.cs \
        tests/Apperture.IntegrationTests/SqlServerScratchDatabase.cs \
        tests/Apperture.IntegrationTests/ResourceOwnershipMutationGateSqlServerTests.cs
git commit -m "CTX-3B: add the resource ownership mutation gate (transaction-owned application lock)"
```

---

### Task 2: Locked-read and narrow ownership-write repository methods

**Files:**
- Modify: `src/Apperture.Core/Apperture.Core.Application/Services/IIntegrationRepository.cs`, `IConnectionRepository.cs`, `IScheduleRepository.cs`
- Modify: `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlIntegrationRepository.cs`, `SqlConnectionRepository.cs`, `SqlScheduleRepository.cs`
- Test: `tests/Apperture.IntegrationTests/ResourceOwnershipLockingSqlServerTests.cs`

**Interfaces:**
- Produces: `GetByIdForUpdateAsync(TId, CancellationToken)` and `SetResourceContextIdAsync(TId, ResourceContextId, CancellationToken)` on **all three** repository interfaces. Both must be called inside an already-open transaction on the same `AppertureDbContext`, after the gate has been acquired; neither opens its own transaction.

- [ ] **Step 1: Write the failing row-lock test**

```csharp
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
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
        if (fixture is null)
            return; // soft-skip

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
        if (fixture is null)
            return; // soft-skip

        await using var db = fixture.NewDb();
        db.ResourceContexts.Add(new ResourceContextEntity
        {
            Id = ResourceContextIds.SharedValue, Name = "Shared", Slug = "shared",
            Kind = ResourceContextKinds.Shared, State = ResourceContextStates.Active, IsReserved = true,
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
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~ResourceOwnershipLockingSqlServerTests"`
Expected: FAIL — compile error, the two methods do not exist.

- [ ] **Step 3: Add the interface methods**

In `IIntegrationRepository.cs`:

```csharp
    /// <summary>
    /// Reads an Integration for an ownership-sensitive write, taking a row-level update lock held for the
    /// lifetime of the caller's ambient transaction on this same <c>AppertureDbContext</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Must be called inside an already-open transaction, <b>after</b>
    /// <c>IResourceOwnershipMutationGate.AcquireAsync</c>. The gate is what makes the operation correct;
    /// this row lock is defense in depth against a future path that skips the gate.
    /// </para>
    /// <para>
    /// The result is <b>not tracked</b>. `EnableRetryOnFailure` is active in production, so a rolled-back
    /// attempt must leave nothing in the change tracker for the retried attempt to read back as if it were
    /// current state.
    /// </para>
    /// <para>
    /// On the EF Core InMemory provider (unit tests) this degrades to a plain untracked read with no real
    /// lock — serialization itself can only be proven against real SQL Server.
    /// </para>
    /// </remarks>
    ValueTask<DomainIntegration?> GetByIdForUpdateAsync(IntegrationId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes only the owning <c>ResourceContextId</c> column. The only supported way to change an
    /// Integration's ownership (CTX-3B); <see cref="UpdateAsync"/> deliberately never touches this column.
    /// Must be called inside the same gated transaction as the preceding
    /// <see cref="GetByIdForUpdateAsync"/>.
    /// </summary>
    /// <remarks>
    /// Implemented with <c>ExecuteUpdateAsync</c> on relational providers, so it bypasses the change
    /// tracker entirely: it writes exactly one column, never flushes unrelated pending changes on the
    /// shared request-scoped context, and leaves no tracked state behind for an execution-strategy retry.
    /// </remarks>
    ValueTask SetResourceContextIdAsync(IntegrationId id, ResourceContextId resourceContextId, CancellationToken cancellationToken = default);
```

Mirror in `IConnectionRepository.cs` (`ConnectionId`/`Connection`) and `IScheduleRepository.cs` (`ScheduleId`/`ScheduleDefinition`). **All three interfaces get both methods in this task** — including the Schedule variant, which the first draft of this plan left until later and then had to patch in.

- [ ] **Step 4: Implement in `SqlIntegrationRepository`**

```csharp
    /// <inheritdoc />
    public async ValueTask<DomainIntegration?> GetByIdForUpdateAsync(IntegrationId id, CancellationToken cancellationToken = default)
    {
        List<IntegrationEntity> rows;
        if (_db.Database.IsRelational())
        {
            // AsNoTracking is load-bearing: see the interface remarks on execution-strategy retries.
            // The UPDLOCK hint lives in the SQL text, so the lock is taken and held to end of transaction
            // regardless of tracking behavior.
            rows = await _db.Integrations
                .FromSqlInterpolated($"SELECT * FROM [Integrations] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id.Value}")
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }
        else
        {
            rows = await _db.Integrations.AsNoTracking()
                .Where(e => e.Id == id.Value)
                .ToListAsync(cancellationToken);
        }

        var entity = rows.SingleOrDefault();
        return entity is null ? null : ToDomain(entity);
    }

    /// <inheritdoc />
    public async ValueTask SetResourceContextIdAsync(IntegrationId id, ResourceContextId resourceContextId, CancellationToken cancellationToken = default)
    {
        if (_db.Database.IsRelational())
        {
            var affected = await _db.Integrations
                .Where(e => e.Id == id.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ResourceContextId, resourceContextId.Value), cancellationToken);
            if (affected == 0)
                throw new InvalidOperationException($"Integration {id.Value} not found.");
            return;
        }

        // InMemory fallback: ExecuteUpdateAsync is unsupported there. Same precedent as
        // SqlScheduleRepository's existing ExecuteUpdateAsync/tracked-fallback pair.
        var entity = await _db.Integrations.FirstOrDefaultAsync(e => e.Id == id.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Integration {id.Value} not found.");
        entity.ResourceContextId = resourceContextId.Value;
        await _db.SaveChangesAsync(cancellationToken);
    }
```

Do the equivalent for `SqlConnectionRepository` (table `[Connections]`, entity `ConnectionEntity`, mapper `ToDomain`) and `SqlScheduleRepository` (table `[ScheduleDefinitions]`, entity `ScheduleDefinitionEntity`, mapper `ToDefinition`).

**Verify the three table names against the model before writing the SQL** (`AppertureDbContext`'s `DbSet` names and any `ToTable` call). At review time they are `Integrations`, `Connections` and `ScheduleDefinitions` by convention, with no overriding `ToTable`.

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~ResourceOwnershipLockingSqlServerTests"`
Expected: PASS against a reachable SQL Server.

- [ ] **Step 6: Build and commit**

```bash
dotnet build Apperture.slnx
git add src/Apperture.Core/Apperture.Core.Application/Services/IIntegrationRepository.cs \
        src/Apperture.Core/Apperture.Core.Application/Services/IConnectionRepository.cs \
        src/Apperture.Core/Apperture.Core.Application/Services/IScheduleRepository.cs \
        src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlIntegrationRepository.cs \
        src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlConnectionRepository.cs \
        src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlScheduleRepository.cs \
        tests/Apperture.IntegrationTests/ResourceOwnershipLockingSqlServerTests.cs
git commit -m "CTX-3B: add untracked locked-read and narrow ExecuteUpdate ownership-write repository methods"
```

---

### Task 3: Gate and serialize `BindingService.CreateBindingAsync`

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/BindingService.cs`

**Interfaces:**
- Consumes: `IResourceOwnershipMutationGate` (Task 1), `IIntegrationRepository.GetByIdForUpdateAsync`, `IConnectionRepository.GetByIdForUpdateAsync` (Task 2). Adds `AppertureDbContext` and `IResourceOwnershipMutationGate` to the constructor.

`Apperture.ControlPlane.Security` already references `Apperture.Persistence.SqlServer` (verified in its `.csproj`), so injecting `AppertureDbContext` here introduces no new layering dependency and breaks no ArchTest.

This task's correctness is proven by Task 8's interleaving tests, not by an isolated unit test — there is nothing to observe about serialization without two real concurrent transactions.

- [ ] **Step 1: Add the two constructor dependencies**

```csharp
    private readonly AppertureDbContext _db;
    private readonly IResourceOwnershipMutationGate _ownershipGate;

    public BindingService(
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        IBindingRepository bindingRepository,
        IConnectionRepository connectionRepository,
        IBindingEligibilityService eligibilityService,
        IIntegrationRepository integrationRepository,
        AppertureDbContext db,
        IResourceOwnershipMutationGate ownershipGate)
    {
        _principalContext = principalContext;
        _auth = auth;
        _bindingRepository = bindingRepository;
        _connectionRepository = connectionRepository;
        _eligibilityService = eligibilityService;
        _integrationRepository = integrationRepository;
        _db = db;
        _ownershipGate = ownershipGate;
    }
```

Add `using Apperture.Persistence.SqlServer;`.

- [ ] **Step 2: Gate, then lock, then validate, then write**

Replace `CreateBindingAsync`'s body from the actor check onward:

```csharp
    /// <inheritdoc />
    public async ValueTask<Binding> CreateBindingAsync(
        string moduleId,
        string slotName,
        string contractType,
        ConnectionId connectionId,
        IntegrationId integrationId,
        CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // EnableRetryOnFailure is active: every attempt must be self-contained.
            _db.ChangeTracker.Clear();

            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            // Design gate §8.7 / product decision P1: creating a binding asserts a cross-context
            // relationship, so it is an ownership-sensitive mutation and takes the gate FIRST -- before
            // reading either row. A concurrent Integration move, Connection move or archive is therefore
            // either entirely before or entirely after this transaction, never interleaved with it.
            await _ownershipGate.AcquireAsync(cancellationToken).ConfigureAwait(false);

            var integration = await _integrationRepository.GetByIdForUpdateAsync(integrationId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Integration {integrationId.Value} not found.");
            if (integration.ModuleId != moduleId)
                throw new InvalidOperationException($"Integration module {integration.ModuleId} does not match binding module {moduleId}.");

            var identity = integration.GetIdentity();
            var allowedIntegration = await _auth.EvaluateAsync(
                actor,
                new PermissionDescriptor(ActionConstants.IntegrationUpdate, GovernedResourceDescriptor.FromIdentity(identity)),
                GovernedResourceDescriptor.FromIdentity(identity),
                identity,
                cancellationToken).ConfigureAwait(false);
            if (!allowedIntegration)
                throw new UnauthorizedAccessException("Not authorized to manage bindings for this integration.");

            var existing = await _bindingRepository.GetBySlotAndIntegrationAsync(moduleId, slotName, integrationId, cancellationToken).ConfigureAwait(false);
            if (existing is not null && existing.State == BindingState.Active)
                throw new InvalidOperationException($"Slot {slotName} is already bound for this integration.");

            var slotScopeKey = ScopeKeyConstants.Slot(moduleId, slotName);
            var allowed = await _auth.EvaluateAsync(
                actor,
                new PermissionDescriptor(ActionConstants.BindingAssign, new ResourceDescriptor(ResourceTypeConstants.Binding, slotScopeKey)),
                null, null, cancellationToken).ConfigureAwait(false);
            if (!allowed)
                throw new UnauthorizedAccessException("Not authorized to assign bindings to this slot.");

            // Authoritative ResourceContext reference check against a fresh, locked read of the Connection
            // row, inside the gate. No concurrent move can have committed between this read and this
            // transaction's own commit.
            var connection = await _connectionRepository.GetByIdForUpdateAsync(connectionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Connection {connectionId.Value} not found.");
            if (!ResourceContextReferenceRule.CanReference(integration.ResourceContextId, connection.ResourceContextId))
            {
                throw new InvalidOperationException(
                    $"Connection {connectionId.Value} belongs to ResourceContext '{connection.ResourceContextId.Value}', which is neither this Integration's ResourceContext ('{integration.ResourceContextId.Value}') nor Shared.");
            }

            var eligible = await _eligibilityService.GetEligibleConnectionsAsync(
                moduleId, slotName, contractType, integration.ResourceContextId, cancellationToken).ConfigureAwait(false);
            if (!eligible.Any(c => c.Id.Value == connectionId.Value))
                throw new InvalidOperationException($"Connection {connectionId.Value} is not eligible for binding to slot {moduleId}:{slotName}.");

            var bindingId = new BindingId(Guid.NewGuid().ToString("N")[..24]);
            var created = new Binding(bindingId, integrationId, moduleId, slotName, contractType, connectionId, null, BindingState.Active);
            await _bindingRepository.AddAsync(created, cancellationToken).ConfigureAwait(false);

            if (tx is not null)
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return created;
        }).ConfigureAwait(false);
    }
```

`CreateCapabilityBindingAsync` is unchanged — it references no Connection and no Integration, so it asserts no cross-context relationship and is not an ownership-sensitive mutation.

- [ ] **Step 3: Update every manual `new BindingService(...)` call site**

Run `grep -rn "new BindingService(" src tests` and add the two new arguments. Production DI resolves both automatically. Test harnesses pass their own `AppertureDbContext` and a gate instance — on InMemory, `SqlResourceOwnershipMutationGate.AcquireAsync` is a documented no-op, so harnesses can construct the real gate rather than a fake.

**This is the largest mechanical diff in the epic.** Expect `ResourceContextOwnershipTestHarness`, `ManagementServiceTestHarness`, `ResourceOwnershipMoveTestHarness` and any ad-hoc constructions in existing test files. Budget for it.

- [ ] **Step 4: Build and run the existing baselines**

```bash
dotnet build Apperture.slnx
dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj
```

Expected: 0 errors; no new failures relative to the 2170/2170 UnitTests and 204/204 IntegrationTests baselines. This task changes `BindingService`'s internals but not its externally observable behavior for the non-racing case.

- [ ] **Step 5: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.ControlPlane.Security/BindingService.cs
git add -u # test-harness constructor call sites from Step 3
git commit -m "CTX-3B: gate and serialize binding creation against concurrent ownership moves"
```

---

### Task 4: Gate and serialize attached-schedule creation

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/SqlScheduleManagementService.cs`

**Interfaces:**
- Consumes: `IResourceOwnershipMutationGate`, `IIntegrationRepository.GetByIdForUpdateAsync`. Adds `AppertureDbContext` and `IResourceOwnershipMutationGate` to the constructor.

- [ ] **Step 1: Add the two constructor dependencies**

```csharp
    private readonly AppertureDbContext _db;
    private readonly IResourceOwnershipMutationGate _ownershipGate;

    public SqlScheduleManagementService(
        IScheduleRepository repo,
        ISchedulerClaimOptions claimOptions,
        TimeProvider timeProvider,
        IResourceContextService resourceContexts,
        IIntegrationRepository integrationRepository,
        AppertureDbContext db,
        IResourceOwnershipMutationGate ownershipGate)
    {
        _repo = repo;
        _claimOptions = claimOptions;
        _timeProvider = timeProvider;
        _resourceContexts = resourceContexts;
        _integrationRepository = integrationRepository;
        _db = db;
        _ownershipGate = ownershipGate;
    }
```

- [ ] **Step 2: Gate the attach path only**

Replace `CreateScheduleAsync` and `ResolveOwningResourceContextAsync`:

```csharp
    /// <inheritdoc />
    public async ValueTask<ScheduleDefinition> CreateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default)
    {
        if (definition.IntegrationId is null)
        {
            // Standalone: inherits from nothing, so it asserts no cross-context relationship and is not
            // an ownership-sensitive mutation. Unchanged, ungated.
            var owningContextId = await ResolveOwningResourceContextAsync(definition, integration: null, cancellationToken).ConfigureAwait(false);
            var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(null, definition with { ResourceContextId = owningContextId }, _timeProvider.GetUtcNow());
            await _repo.AddDefinitionAsync(merged, cancellationToken);
            return merged;
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();

            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            // Attach path: the new Schedule inherits the Integration's ownership, so this IS an
            // ownership-sensitive mutation. Gate first, then read the Integration under a row lock, so a
            // concurrent Integration move cannot commit a new ResourceContextId between the read and the
            // insert (which would persist a schedule already inconsistent with its own Integration).
            await _ownershipGate.AcquireAsync(cancellationToken).ConfigureAwait(false);

            var integration = await _integrationRepository.GetByIdForUpdateAsync(definition.IntegrationId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Integration {definition.IntegrationId.Value} not found.");

            var owningContextId = await ResolveOwningResourceContextAsync(definition, integration, cancellationToken).ConfigureAwait(false);
            var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(null, definition with { ResourceContextId = owningContextId }, _timeProvider.GetUtcNow());
            await _repo.AddDefinitionAsync(merged, cancellationToken).ConfigureAwait(false);

            if (tx is not null)
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return merged;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Authoritative Schedule ownership rule. See <see cref="IScheduleManagementService.CreateScheduleAsync"/>.
    /// </summary>
    /// <param name="integration">
    /// The already-locked Integration for the attach path (loaded by the caller under
    /// <see cref="IIntegrationRepository.GetByIdForUpdateAsync"/>, inside the ownership gate), or null for
    /// the standalone path, which has no Integration to inherit from.
    /// </param>
    private async ValueTask<ResourceContextId> ResolveOwningResourceContextAsync(
        ScheduleDefinition definition,
        Apperture.Core.Domain.Integration.Integration? integration,
        CancellationToken cancellationToken)
    {
        if (integration is null)
        {
            var standaloneContext = await _resourceContexts
                .ResolveForNewOwnershipAsync(definition.ResourceContextId, cancellationToken)
                .ConfigureAwait(false);
            return standaloneContext.Id;
        }

        if (definition.ResourceContextId is null)
        {
            if (definition.Enabled)
            {
                await ResourceContextReactivationGuard.EnsureOwningContextIsActiveAsync(
                    integration.ResourceContextId, _resourceContexts, "creating an enabled schedule for this integration", cancellationToken).ConfigureAwait(false);
            }
            return integration.ResourceContextId;
        }

        if (!string.Equals(definition.ResourceContextId.Value, integration.ResourceContextId.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Schedule ResourceContext '{definition.ResourceContextId.Value}' does not match the owning Integration's ResourceContext '{integration.ResourceContextId.Value}'. An Integration-attached schedule belongs to its Integration's ResourceContext.");
        }

        var resolved = await _resourceContexts
            .ResolveForNewOwnershipAsync(definition.ResourceContextId, cancellationToken)
            .ConfigureAwait(false);
        return resolved.Id;
    }
```

The parameter change is deliberate: the helper no longer does its own unlocked `GetByIdAsync`, because the caller must control exactly when and how that read happens — gated, locked, inside the transaction.

- [ ] **Step 3: Update every manual `new SqlScheduleManagementService(...)` call site**

`grep -rn "new SqlScheduleManagementService(" src tests`; DI call sites need no change.

- [ ] **Step 4: Build and run the baselines**

```bash
dotnet build Apperture.slnx
dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj
```

- [ ] **Step 5: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.ControlPlane.Security/SqlScheduleManagementService.cs
git add -u
git commit -m "CTX-3B: gate attached-schedule creation against concurrent Integration moves"
```

---

### Task 5: Gate Application archive against an in-flight move

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceContextManagementService.cs`

**Interfaces:**
- Consumes: `IResourceOwnershipMutationGate`. Adds `AppertureDbContext` and `IResourceOwnershipMutationGate` to the constructor.

**Why archive is an ownership-sensitive mutation.** CTX-3A's archive rule is "the Application owns nothing live." `ResourceContextManagementService.ArchiveAsync` evaluates that ownership summary with no serialization at all. A move of a live Integration **into** target `T`, concurrent with archiving `T`, can therefore leave an archived Application owning a live Integration — defeating the reactivation guard and breaking the invariant the archive rule exists to state. The move's own `target_archived` check reads the ResourceContext row unlocked, so neither side sees the other. Both sides must take the gate.

- [ ] **Step 1: Write the failing behavioral test**

Add to the existing archive test suite (InMemory harness; the real interleaving is proven in Task 8):

```csharp
    [Fact]
    public async Task Archive_reads_ownership_under_the_ownership_gate()
    {
        // Documents the contract at the unit level: ArchiveAsync must not evaluate the ownership summary
        // before acquiring the gate. The observable proof is Task 8's
        // Move_into_target_and_archive_target_are_serialized SQL Server test; this test pins the call
        // order so a refactor cannot silently drop the gate.
        using var h = new ResourceContextManagementTestHarness(recordingGate: true);
        var app = await h.CreateApplicationAsync("CampusNexus");

        await h.Management.ArchiveAsync(app.Id);

        h.Gate.AcquiredBeforeFirstOwnershipRead.Should().BeTrue();
    }
```

`ResourceContextManagementTestHarness` gets a recording `IResourceOwnershipMutationGate` double that notes whether `AcquireAsync` ran before the first ownership-summary read. If no such harness exists yet, add the smallest one that constructs the real `ResourceContextManagementService`.

- [ ] **Step 2: Gate `ArchiveAsync`**

```csharp
    /// <inheritdoc />
    public async ValueTask<ResourceContext> ArchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default)
    {
        var actor = await RequireManageAsync(cancellationToken).ConfigureAwait(false);

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();

            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            // The archive rule is a statement about ownership ("nothing live"), so evaluating it is an
            // ownership-sensitive read and archiving is an ownership-sensitive mutation. Without the gate,
            // a concurrent move into this context can add live ownership between the summary read and the
            // state change, producing an archived Application that owns a live Integration.
            await _ownershipGate.AcquireAsync(cancellationToken).ConfigureAwait(false);

            var before = await _resourceContexts.GetAsync(id, cancellationToken).ConfigureAwait(false);
            var archived = await _resourceContexts.ArchiveAsync(id, cancellationToken).ConfigureAwait(false);
            if (before is not null && before.IsActive)
            {
                await AppendAuditAsync("resource_context.archived", actor.Id, id, $"Archived '{archived.Name}'.", cancellationToken).ConfigureAwait(false);
            }

            if (tx is not null)
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return archived;
        }).ConfigureAwait(false);
    }
```

`UnarchiveAsync`, `UpdateAsync`, `CreateAsync` are **not** gated: none of them adds or removes ownership, and unarchiving only widens what is permitted.

- [ ] **Step 3: Update call sites, build, run baselines, commit**

```bash
dotnet build Apperture.slnx
dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj
git add src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceContextManagementService.cs
git add -u
git commit -m "CTX-3B: gate Application archive against concurrent ownership moves"
```

---

### Task 6: Real, transactional `Move*Async`

**Files:**
- Modify: `src/Apperture.Core/Apperture.Core.Application/ResourceContexts/IResourceOwnershipMoveService.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceOwnershipMoveService.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/Apperture.ControlPlane.Security.csproj`
- Test: `tests/Apperture.UnitTests/ResourceContexts/ResourceOwnershipMoveMutationTests.cs`
- Modify: `tests/Apperture.UnitTests/ResourceContexts/ResourceOwnershipMoveTestHarness.cs`

**Interfaces:**
- Produces: `IResourceOwnershipMoveService.MoveIntegrationAsync`/`MoveConnectionAsync`/`MoveScheduleAsync` — **declared and implemented in this same commit**, per Plan 1's no-stub rule.
- Produces: `ResourceOwnershipMoveService.MidTransactionHook` — `internal`, test-only, consumed by Task 8 and available to Plan 4.

- [ ] **Step 1: Write the failing tests**

```csharp
using Apperture.Core.Application.ResourceContexts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

public sealed class ResourceOwnershipMoveMutationTests
{
    [Fact]
    public async Task Successful_integration_move_updates_current_ownership_and_moves_attached_schedules()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var schedule = await h.CreateAttachedScheduleAsync(integration.Id);

        var result = await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        result.CanMove.Should().BeTrue();
        (await h.IntegrationRepository.GetByIdAsync(integration.Id))!.ResourceContextId.Value.Should().Be(target.Id.Value);
        (await h.ScheduleRepository.GetDefinitionAsync(schedule.Id))!.ResourceContextId!.Value.Should().Be(target.Id.Value);
    }

    [Fact]
    public async Task Blocked_integration_move_leaves_the_integration_and_its_schedules_unchanged()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var schedule = await h.CreateAttachedScheduleAsync(integration.Id);
        var connection = await h.CreateConnectionAsync(thirdParty.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);

        var result = await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        result.CanMove.Should().BeFalse();
        (await h.IntegrationRepository.GetByIdAsync(integration.Id))!.ResourceContextId.Value.Should().Be(source.Id.Value);
        (await h.ScheduleRepository.GetDefinitionAsync(schedule.Id))!.ResourceContextId!.Value.Should().Be(source.Id.Value,
            "atomicity cuts both ways -- a blocked move must not move the attached schedules either");
    }

    [Fact]
    public async Task Successful_connection_move_updates_current_ownership()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);

        var result = await h.Service.MoveConnectionAsync(connection.Id, target.Id);

        result.CanMove.Should().BeTrue();
        (await h.ConnectionRepository.GetByIdAsync(connection.Id))!.ResourceContextId.Value.Should().Be(target.Id.Value);
    }

    [Fact]
    public async Task Blocked_connection_move_mutates_nothing()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var connection = await h.CreateConnectionAsync(source.Id);
        var boundIntegration = await h.CreateIntegrationAsync(thirdParty.Id);
        await h.AddDirectBindingAsync(boundIntegration.Id, boundIntegration.ModuleId, connection.Id);

        var result = await h.Service.MoveConnectionAsync(connection.Id, target.Id);

        result.CanMove.Should().BeFalse();
        (await h.ConnectionRepository.GetByIdAsync(connection.Id))!.ResourceContextId.Value.Should().Be(source.Id.Value);
    }

    [Fact]
    public async Task Standalone_schedule_move_updates_current_ownership()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var schedule = await h.CreateStandaloneScheduleAsync(source.Id);

        var result = await h.Service.MoveScheduleAsync(schedule.Id, target.Id);

        result.CanMove.Should().BeTrue();
        (await h.ScheduleRepository.GetDefinitionAsync(schedule.Id))!.ResourceContextId!.Value.Should().Be(target.Id.Value);
    }

    [Fact]
    public async Task Attached_schedule_move_is_rejected_and_mutates_nothing()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var schedule = await h.CreateAttachedScheduleAsync(integration.Id);

        var result = await h.Service.MoveScheduleAsync(schedule.Id, target.Id);

        result.CanMove.Should().BeFalse();
        (await h.ScheduleRepository.GetDefinitionAsync(schedule.Id))!.ResourceContextId!.Value.Should().Be(source.Id.Value);
    }

    [Fact]
    public async Task Moving_out_of_an_archived_source_succeeds()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id, IntegrationState.Retired);
        await h.ArchiveAsync(source.Id);

        var result = await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        result.CanMove.Should().BeTrue("moving out of an archived Application is legitimate cleanup");
        (await h.IntegrationRepository.GetByIdAsync(integration.Id))!.ResourceContextId.Value.Should().Be(target.Id.Value);
    }

    [Fact]
    public async Task Not_authorized_move_throws_and_mutates_nothing()
    {
        using var h = new ResourceOwnershipMoveTestHarness(grantedAction: null);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);

        var act = async () => await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        (await h.IntegrationRepository.GetByIdAsync(integration.Id))!.ResourceContextId.Value.Should().Be(source.Id.Value);
    }

    [Fact]
    public async Task Move_requires_the_family_permission_not_resource_context_manage()
    {
        using var h = new ResourceOwnershipMoveTestHarness(
            grantedAction: Apperture.Core.Domain.Governance.ActionConstants.ResourceContextManage,
            grantedResourceType: Apperture.Core.Domain.Governance.ResourceTypeConstants.ResourceContext);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var connection = await h.CreateConnectionAsync(source.Id);
        var schedule = await h.CreateStandaloneScheduleAsync(source.Id);

        var moveIntegration = async () => await h.Service.MoveIntegrationAsync(integration.Id, target.Id);
        var moveConnection = async () => await h.Service.MoveConnectionAsync(connection.Id, target.Id);
        var moveSchedule = async () => await h.Service.MoveScheduleAsync(schedule.Id, target.Id);

        await moveIntegration.Should().ThrowAsync<UnauthorizedAccessException>();
        await moveConnection.Should().ThrowAsync<UnauthorizedAccessException>();
        await moveSchedule.Should().ThrowAsync<UnauthorizedAccessException>(
            "the mutating path enforces the same family permissions as preview (§9.3), not resource_context.manage");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Expected: FAIL — compile error, `Move*Async` does not exist (Plan 1 deliberately declared no stub).

- [ ] **Step 3: Add the interface members**

```csharp
    ValueTask<ResourceMovePreview> MoveIntegrationAsync(IntegrationId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default);
    ValueTask<ResourceMovePreview> MoveConnectionAsync(ConnectionId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default);
    ValueTask<ResourceMovePreview> MoveScheduleAsync(ScheduleId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Add the dependencies, the test hook, and the three implementations**

Add `<InternalsVisibleTo Include="Apperture.IntegrationTests" />` to `Apperture.ControlPlane.Security.csproj` (it already has one for `Apperture.UnitTests`).

```csharp
    private readonly AppertureDbContext _db;
    private readonly IResourceOwnershipMutationGate _ownershipGate;

    public ResourceOwnershipMoveService(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IIntegrationRepository integrationRepository,
        IConnectionRepository connectionRepository,
        IScheduleRepository scheduleRepository,
        IBindingRepository bindingRepository,
        IResourceContextService resourceContexts,
        AppertureDbContext db,
        IResourceOwnershipMutationGate ownershipGate)
    {
        // ... existing assignments ...
        _db = db;
        _ownershipGate = ownershipGate;
    }

    /// <summary>
    /// Test-only synchronization point, fired inside the move transaction after the gate has been
    /// acquired and validation has run, but before commit. Lets a concurrency test hold one transaction
    /// open at a precisely known moment and observe what a second operation does. Never set in production
    /// (nothing outside the test assemblies can see it).
    /// </summary>
    internal Func<string, CancellationToken, Task>? MidTransactionHook { get; set; }

    private Task FireHookAsync(string checkpoint, CancellationToken cancellationToken) =>
        MidTransactionHook?.Invoke(checkpoint, cancellationToken) ?? Task.CompletedTask;

    /// <inheritdoc />
    public async ValueTask<ResourceMovePreview> MoveIntegrationAsync(
        IntegrationId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // EnableRetryOnFailure is active: every attempt must be self-contained.
            _db.ChangeTracker.Clear();

            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            // Gate first, before ANY ownership-sensitive read (product decision P1).
            await _ownershipGate.AcquireAsync(cancellationToken).ConfigureAwait(false);

            // Defense in depth on the row we will mutate (§8.4 step 1).
            var integration = await _integrationRepository.GetByIdForUpdateAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Integration {id.Value} not found.");

            await AuthorizeAsync(actor, ActionConstants.IntegrationUpdate, integration.GetIdentity(), cancellationToken).ConfigureAwait(false);

            // The identical validator preview uses (§8.2) -- now against state re-read inside the gate.
            var validation = await ValidateIntegrationMoveAsync(integration, targetResourceContextId, cancellationToken).ConfigureAwait(false);

            await FireHookAsync("integration:validated", cancellationToken).ConfigureAwait(false);

            if (validation.Preview.CanMove)
            {
                await _integrationRepository.SetResourceContextIdAsync(id, targetResourceContextId, cancellationToken).ConfigureAwait(false);

                // The authoritative attached-Schedule set from the validator's single read -- never a
                // second query, so preview effects, the mutation and Plan 4's audit all describe the
                // same schedules.
                foreach (var schedule in validation.AttachedSchedules)
                    await _scheduleRepository.SetResourceContextIdAsync(schedule.Id, targetResourceContextId, cancellationToken).ConfigureAwait(false);
            }

            // Final control-flow shape (see Global Constraints): commit on both outcomes. On the rejected
            // path nothing was written, so this commit is a no-op today -- and Plan 4 adds the single
            // move_rejected audit row to it without rewriting any of this.
            if (tx is not null)
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return validation.Preview;
        }).ConfigureAwait(false);
    }
```

`MoveConnectionAsync` and `MoveScheduleAsync` follow the identical shape: clear tracker → relational-only transaction → **gate** → `GetByIdForUpdateAsync` → authorize → validate → hook (`"connection:validated"` / `"schedule:validated"`) → mutate only when `CanMove` → commit → return the preview.

**`MoveScheduleAsync` authorizes inside the transaction**, via `AuthorizeScheduleAsync(cancellationToken)` called after the locked read — matching its two siblings. The first draft authorized it outside the transaction; that inconsistency is removed here.

Add the actor-carrying authorization overload used by the move paths; leave Plan 1's `AuthorizeAsync(string, IGovernedResourceIdentity, CancellationToken)` overload untouched so the `Preview*Async` methods do not change:

```csharp
    private async ValueTask AuthorizeAsync(IActor actor, string action, IGovernedResourceIdentity identity, CancellationToken cancellationToken)
    {
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(action, GovernedResourceDescriptor.FromIdentity(identity)),
                GovernedResourceDescriptor.FromIdentity(identity),
                identity),
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException($"Not authorized to move this resource ({action}).");
    }
```

- [ ] **Step 5: Update the test harness**

`ResourceOwnershipMoveTestHarness` passes `_db` and a real `SqlResourceOwnershipMutationGate(_db)` into the service (the gate is a documented no-op on InMemory), and passes `_db` plus a gate into its `BindingService` and `SqlScheduleManagementService` constructions per Tasks 3 and 4.

- [ ] **Step 6: Run to verify tests pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceOwnershipMove"`
Expected: PASS — Plan 1's preview tests stay green, and this task's mutation tests now pass.

- [ ] **Step 7: Full regression run and commit**

```bash
dotnet build Apperture.slnx
dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj
git add src/Apperture.Core/Apperture.Core.Application/ResourceContexts/IResourceOwnershipMoveService.cs \
        src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceOwnershipMoveService.cs \
        src/Apperture.ControlPlane/Apperture.ControlPlane.Security/Apperture.ControlPlane.Security.csproj \
        tests/Apperture.UnitTests/ResourceContexts/ResourceOwnershipMoveTestHarness.cs \
        tests/Apperture.UnitTests/ResourceContexts/ResourceOwnershipMoveMutationTests.cs
git commit -m "CTX-3B: implement gated, transactional Move*Async for Integration/Connection/Schedule"
```

---

### Task 7: Historical immutability (CTX-2 provenance)

**Files:**
- Create: `tests/Apperture.UnitTests/ResourceContexts/ResourceMoveHistoricalImmutabilityTests.cs`

Design gate §14 requires that no move alters any CTX-2 historical snapshot. The first draft of this plan asserted this nowhere, while the closure claimed "tests in Plan 2 assert this directly" — a statement that was false against the plans as written. This task closes that gap so the closure's claim becomes true. **The tests run the production `Move*Async` methods**, not a reimplementation.

- [ ] **Step 1: Enumerate the historical families from code, not from memory**

Run `grep -rln "IntegrationId" src/Apperture.Persistence/Apperture.Persistence.SqlServer/Entities/` and `grep -rln "ResourceContextId" src/Apperture.Persistence/Apperture.Persistence.SqlServer/Entities/`, and reconcile against this list confirmed at review time:

| Entity | Carries | Note |
|---|---|---|
| `OperationRunEntity` | `IntegrationId`, `ResourceContextId` | |
| `ScheduleRunEntity` | `ResourceContextId` | |
| `WorkflowInstanceEntity` | `IntegrationId` | **confirmed present** |
| `OperationInstanceEntity` | `ResourceContextId` | |
| `WorkflowStartRequestEntity` | `IntegrationId` | |
| `OperationStartRequestEntity` | `IntegrationId` | |
| `OperationItemizationEntities` | `IntegrationId` | **found during review; not in the original plan's list** — includes the OperationGroup/OperationItem persistence structures |
| `PlatformEventEntity` | `IntegrationId` | |
| `NotificationInstanceEntity` | `IntegrationId` | **literal column**, not a join-derived value |
| `AttentionIncidentEntity` | `IntegrationId` | |
| `AuditLogEntity` | `ResourceContextId` | pre-existing rows must be byte-identical |

If the grep finds a family not in this table, add it to the test rather than assuming it is out of scope.

- [ ] **Step 2: Write the tests**

For each of the three move families, and for **both** the successful and the blocked outcome:

```csharp
    [Fact]
    public async Task Integration_move_never_rewrites_any_CTX2_historical_snapshot()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        await h.CreateAttachedScheduleAsync(integration.Id);

        // Seed one row per historical family, every one snapshotted to the SOURCE context, so a move that
        // wrongly "followed" current ownership would visibly rewrite them to the target.
        await h.SeedHistoricalProvenanceAsync(integration.Id, source.Id);
        var before = await h.CaptureHistoricalProvenanceAsync();

        var result = await h.Service.MoveIntegrationAsync(integration.Id, target.Id);
        result.CanMove.Should().BeTrue();

        var after = await h.CaptureHistoricalProvenanceAsync();
        after.Should().BeEquivalentTo(before,
            "CTX-2 provenance records what happened, not what is currently owned -- a move must never rewrite it");
    }
```

`SeedHistoricalProvenanceAsync` / `CaptureHistoricalProvenanceAsync` are harness helpers added in this task. `Capture` returns an ordered, comparable projection of **every** family in Step 1's table — at minimum `(TableName, RowId, IntegrationId, ResourceContextId)` per row — so a single `BeEquivalentTo` covers all of them and a newly-added family only needs one line in the capture.

Required test cases:

1. `Integration_move_never_rewrites_any_CTX2_historical_snapshot` (successful move).
2. `Blocked_integration_move_never_rewrites_any_CTX2_historical_snapshot`.
3. `Connection_move_never_rewrites_any_CTX2_historical_snapshot`.
4. `Standalone_schedule_move_never_rewrites_any_CTX2_historical_snapshot`.
5. `Move_appends_to_audit_without_rewriting_any_pre_existing_audit_row` — capture the full `AuditLogs` table before the move, assert every pre-existing row is byte-identical afterwards and that only new rows may have been added. (Plan 2 writes no audit rows at all, so today this asserts the table is unchanged; Plan 4 makes the "only appended" half meaningful without this test needing to change.)
6. `Integration_move_does_not_change_the_bindings_it_evaluated` — bindings are edges with no ownership column; assert the binding rows are byte-identical before and after, including their `ConnectionId` and `IntegrationId`.

- [ ] **Step 3: Run, then commit**

```bash
dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceMoveHistoricalImmutabilityTests"
git add tests/Apperture.UnitTests/ResourceContexts/ResourceMoveHistoricalImmutabilityTests.cs \
        tests/Apperture.UnitTests/ResourceContexts/ResourceOwnershipMoveTestHarness.cs
git commit -m "CTX-3B: assert moves never rewrite CTX-2 historical provenance"
```

---

### Task 8: Deterministic SQL Server concurrency tests

**Files:**
- Create: `tests/Apperture.IntegrationTests/ResourceOwnershipMoveConcurrencySqlServerTests.cs`

The first draft's "concurrency test" fired two tasks with no interleaving control and asserted a post-condition that already held before any of this plan's code existed. **These tests drive specific interleavings** using the `MidTransactionHook` from Task 6, and assert *which* participant blocked as well as the final persisted state.

- [ ] **Step 1: Write the harness shape**

One helper drives every case:

```csharp
    /// <summary>
    /// Holds operation A open at its mid-transaction checkpoint, starts operation B, observes whether B
    /// blocks, then releases A and lets B finish. Returns what each side did.
    /// </summary>
    private static async Task<InterleavingResult> RaceAsync(
        Func<MidTransactionHook, Task> operationA,
        Func<Task> operationB,
        TimeSpan observeWindow) { /* ... */ }
```

`InterleavingResult` records: whether B had completed while A was still holding, whether B threw, and what B threw. Every test then asserts three things: **who blocked**, **what each side returned**, and **the final persisted invariant**.

- [ ] **Step 2: Write the required interleavings**

All six are required. None of them is optional or "if time permits."

| # | Test | Must prove |
|---|---|---|
| 1 | `Integration_move_and_binding_creation_are_serialized` | B blocks at the gate while A holds. Whichever commits first, the other re-reads under the gate and either rejects or succeeds correctly. No cross-context binding persists. |
| 2 | `Connection_move_and_binding_creation_are_serialized` | Same, from the Connection side. Specifically: bind-first ⇒ the move sees the new binding and blocks; move-first ⇒ the bind sees the moved Connection and is rejected by `CanReference`. |
| 3a | `Integration_move_then_connection_move_is_serialized` | **The case the first draft missed.** Integration move holds; Connection move blocks; on release, the Connection move re-validates against the *moved* Integration and blocks with `binding_integration_unreachable`. |
| 3b | `Connection_move_then_integration_move_is_serialized` | The mirror ordering. Connection move holds; Integration move blocks; on release, the Integration move re-validates against the *moved* Connection and blocks with `binding_connection_unreachable`. |
| 4 | `Integration_move_and_attached_schedule_creation_are_serialized` | Schedule creation blocks at the gate; on release it inherits the **post-move** context, so the new Schedule is never created inconsistent with its own Integration. |
| 5 | `Move_into_target_and_archive_target_are_serialized` | Archive blocks while the move holds; on release, archive sees the newly-moved live Integration and is refused. No archived context ends up owning live resources. |
| 6 | `Two_concurrent_connection_moves_are_serialized` | Retained because the gate makes it cheap and it proves same-type serialization, not only cross-type. Exactly one wins; the second re-validates and reports `already_in_target`. |

Every test ends with the same invariant sweep:

```csharp
        // Invariant, asserted identically in every interleaving test.
        await using var assertDb = fixture.NewDb();
        var bindings = await new SqlBindingRepository(assertDb).GetByConnectionIdAsync(connection.Id);
        foreach (var binding in bindings.Where(b => b.IntegrationId is not null))
        {
            var boundIntegration = await new SqlIntegrationRepository(assertDb).GetByIdAsync(binding.IntegrationId!);
            var finalConnection = await new SqlConnectionRepository(assertDb).GetByIdAsync(connection.Id);
            ResourceContextReferenceRule
                .CanReference(boundIntegration!.ResourceContextId, finalConnection!.ResourceContextId)
                .Should().BeTrue("no persisted binding may end up cross-context-invalid under any interleaving");
        }
```

Plus, for case 5, an archived-target sweep: no ResourceContext whose state is not Active may own a non-Retired Integration, an Active Connection, or an Enabled Schedule.

- [ ] **Step 3: Add a no-deadlock assertion**

Across the full set, assert that **no test observed SQL error 1205**. A deadlock anywhere in these six interleavings means the gate was not acquired first somewhere, which is the exact regression this architecture exists to prevent. Fail loudly rather than relying on the retrying execution strategy to hide it.

- [ ] **Step 4: Run — and record real execution**

```bash
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~ResourceOwnershipMoveConcurrencySqlServerTests"
```

Expected: PASS against a reachable SQL Server, with **all six tests reported as executed, not skipped**. Record the test-run output (counts and names) in the implementation notes — Plan 6b's closure requires this evidence.

- [ ] **Step 5: Full baseline, then commit**

```bash
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj
git add tests/Apperture.IntegrationTests/ResourceOwnershipMoveConcurrencySqlServerTests.cs
git commit -m "CTX-3B: add deterministic ownership-mutation interleaving tests"
```

---

## Out of scope for this plan

- No `binding_cross_context` readiness issue (Plan 3) — this plan prevents the race; the runtime backstop for data that reaches an invalid state through some other path is Plan 3.
- No audit records for moves (Plan 4). This plan's control flow is already in its final shape, so Plan 4 is purely additive.
- No HTTP endpoints, client methods, or UI (Plan 5). `ResourceOwnershipBusyException` is produced here; Plan 5 maps it to a 409 with the `ownership_operation_busy` code.
- No Integration hard-delete referential-lifecycle work (Plan 6a).
- No schedule-context repair operation. If the Task 1 Step 0 preflight returns rows, stop and report rather than building one (product decision P3).

## Validation for this plan

- `dotnet build Apperture.slnx` — 0 errors.
- `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj` — full project green, including the mutation and historical-immutability tests.
- `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj` — no regressions against the 204/204 baseline.
- `dotnet test Apperture.slnx` once at the end, to catch incidental breakage in `Ui.Shared.Tests`, `Ui.Client.Tests` and `ArchTests` from the constructor changes in Tasks 3-6.
- **The SQL Server tests must demonstrably execute.** Every SQL-backed test in this plan soft-skips when no endpoint is reachable, which means the entire concurrency guarantee of CTX-3B can silently not run inside a green suite. Record the actual executed/skipped counts for `ResourceOwnershipMutationGateSqlServerTests`, `ResourceOwnershipLockingSqlServerTests` and `ResourceOwnershipMoveConcurrencySqlServerTests`. **A soft-skipped green suite is not acceptable evidence for CTX-3B closure** (Plan 6b restates this requirement).
- Do not disable test parallelism anywhere. Per-test throwaway catalogs already isolate these tests, and the ownership gate is database-scoped, so it does not couple parallel test catalogs.
