# CTX-3B Plan 4 — Movement Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every successful move writes exactly two audit records (departure at source, arrival at target) sharing one `CorrelationId`; every blocked move writes exactly one `move_rejected` record at source. Source-filtered, target-filtered, and global Audit each see the correct record(s).

**Architecture:** `CorrelationId` exists on the `AuditRecord` domain record but is **not persisted today** — verified at review time: `AuditLogEntity` has no such column, `SqlAuditService.AppendAsync` never writes it, and `QueryAsync` (line 61) and `GetByIdAsync` (line 94) both hardcode `new CorrelationId(null)` on read. This plan adds real persistence first (Task 1), then wires the audit calls directly into `ResourceOwnershipMoveService.Move*Async` (Plan 2), inside the same already-open, gated transaction.

**The shared-DbContext claim, and why it is verified rather than assumed.** `AppertureDbContext` is registered `AddDbContext<AppertureDbContext>` (scoped); `IAuditService` is registered `AddScoped<IAuditService, SqlAuditService>` (`ServiceCollectionExtensions.Governance.cs:96` and `:214`); `SqlAuditService.AppendAsync` calls `_db.SaveChangesAsync` on that injected context. So in production DI the audit sink writes through the literal same `AppertureDbContext` instance as the move service, and its `SaveChangesAsync` enlists in the move's ambient transaction. This settles the design gate's own explicitly-open §8.8 question in favour of "append inside the move transaction."

That conclusion is load-bearing enough that **this plan tests it rather than restating it** (Task 3): one test proves an audit failure rolls the movement back, and one DI test proves the two services resolve the same context instance from one scope. Without them, the whole transactionality claim would silently degrade the day someone registers an audit sink with its own context. Note that `Apperture.RuntimeHost.Windows.WorkerHost/Program.cs:87` registers `AddScoped<IAuditService, SqlAuditService>` separately — confirm the move service is not reachable under that wiring, or that the same conclusion holds there.

**Tech Stack:** C#/.NET 9, EF Core migrations, xUnit, FluentAssertions.

**Spec:** `docs/01-architecture/transitions/resource-contexts/ctx3-design-gate.md` §8.8 (movement auditing), §13 CTX-3B step 4, §14 CTX-3B acceptance criteria (audit bullet). Product decision P2 (the `integration.deleted` audit) belongs to Plan 6a, not here.

## Global Constraints

- Never rewrite or delete an earlier audit record. Every move produces new rows only. Plan 2's historical-immutability suite already asserts that pre-existing `AuditLogs` rows are byte-identical across a move; this plan must keep that test green.
- **The migration adds one nullable column and nothing else.** No index, no backfill, no change to any existing row. Existing audit rows keep `CorrelationId = NULL` forever, which is correct: they were never part of a multi-record operation.
- **Plan 2 already wrote the rejected-path control flow in its final committing shape**, so this plan adds audit appends without rewriting Plan 2's control flow or invalidating Plan 2's tests. If the implementer finds themselves changing `if (!preview.CanMove) return` into a commit, Plan 2 was implemented wrong — fix Plan 2 rather than patching it here.
- **Use the validator's authoritative attached-Schedule set** (`IntegrationMoveValidation.AttachedSchedules`) for `movedScheduleIds`. Never re-query. The preview effect, the rows actually mutated, and the audit payload must describe the same schedules from the same single read.
- A blocked/rejected move writes **exactly one** `*.ownership.move_rejected` record at the source, never at the target and never with a null context.
- A successful move writes **exactly two** records (`moved_out` at source, `moved_in` at target), sharing one `CorrelationId` — never a single record, never a null-context record.
- Attached schedules moved with an Integration are listed in the Integration's own `movedScheduleIds` detail — do **not** write per-schedule audit rows for them (avoids N×2 spam; an Audit search by ScheduleId will not surface the move, which is an accepted, documented tradeoff).
- Action naming follows the house convention (`{resourceType}.{action}`, lowercase, dot-separated, ADR-0013): `integration.ownership.moved_out`/`.moved_in`/`.move_rejected`, and the same pattern for `connection.*` and `schedule.*`.
- The Integration/Connection/Schedule identity field on the record follows the shape `IntegrationSettingsService` already uses: `Resource: new ResourceDescriptor(ResourceTypeConstants.<Family>, id.Value)`.

---

## File Structure

**Modified files:**
- `src/Apperture.Persistence/Apperture.Persistence.SqlServer/Entities/AuditLogEntity.cs` — add `CorrelationId` column.
- `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlAuditService.cs` — persist and read back `CorrelationId`.
- `src/Apperture.Persistence/Apperture.Persistence.SqlServer/Migrations/` — new migration.
- `src/Apperture.Core/Apperture.Core.Domain/Governance/ActionConstants.cs` — nine new action constants.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceOwnershipMoveService.cs` — inject `IAuditService`; append audit records inside each `Move*Async` transaction; commit even on the rejected path (a behavior change from Plan 2, which let the empty rejected-path transaction roll back with nothing to persist — now there is something to persist).

**New files:**
- `src/Apperture.Core/Apperture.Core.Application/ResourceContexts/ResourceMoveAuditDetails.cs` — the two `Details`-JSON payload shapes.
- `tests/Apperture.UnitTests/ResourceContexts/ResourceMoveAuditTests.cs`
- `tests/Apperture.IntegrationTests/AuditCorrelationIdSqlServerTests.cs` (proves the new column round-trips against real SQL Server, not just InMemory)
- `tests/Apperture.IntegrationTests/ResourceMoveAuditFilteringSqlServerTests.cs` (**required**, not optional: proves the two-sided model through the real `SqlAuditService` query path)

**Interfaces produced for later plans:**
- Nothing new for Plan 5 beyond what already existed — Plan 5's controllers call `Move*Async` exactly as before; the audit side effect is transparent to them.

---

### Task 1: Persist `CorrelationId` on `AuditLogEntity`

**Files:**
- Modify: `src/Apperture.Persistence/Apperture.Persistence.SqlServer/Entities/AuditLogEntity.cs`
- Modify: `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlAuditService.cs`
- New: EF migration (name: `Ctx3B_AuditCorrelationId`)
- Test: `tests/Apperture.IntegrationTests/AuditCorrelationIdSqlServerTests.cs`

**Interfaces:**
- Produces: `AuditLogEntity.CorrelationId` (nullable string column), `SqlAuditService.AppendAsync`/`QueryAsync`/`GetByIdAsync` round-tripping it.

- [ ] **Step 1: Write the failing test**

```csharp
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>Design gate §8.8: CorrelationId must round-trip through real SQL Server, not just the domain model.</summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AuditCorrelationIdSqlServerTests
{
    [Fact]
    public async Task CorrelationId_round_trips_through_append_and_query()
    {
        var baseConnectionString = SqlServerTestConfiguration.GetConnectionString();
        if (baseConnectionString is null)
            return; // soft-skip

        var catalog = "Apperture_AuditCorrelation_" + Guid.NewGuid().ToString("N")[..12];
        var connectionString = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = catalog }.ConnectionString;

        try
        {
            SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);
        }
        catch (InvalidOperationException)
        {
            return; // soft-skip
        }

        try
        {
            await using var db = NewDb(connectionString);
            if (!await db.Database.CanConnectAsync())
                return; // soft-skip
            await db.Database.MigrateAsync();

            var service = new SqlAuditService(db);
            var correlationId = Guid.NewGuid().ToString("N");
            await service.AppendAsync(new AuditRecord(
                new AuditRecordId(Guid.NewGuid().ToString("N")),
                new ActorId("test-actor"),
                "integration.ownership.moved_out",
                new ResourceDescriptor(ResourceTypeConstants.Integration, "int-1"),
                Outcome: "",
                DateTimeOffset.UtcNow,
                new CorrelationId(correlationId),
                Details: null,
                ResourceContextId: "app-a"));

            var page = await service.QueryAsync(new AuditQuery(FilterByResourceContextId: "app-a", PageSize: 10));

            page.Items.Should().ContainSingle().Which.CorrelationId.Value.Should().Be(correlationId);
        }
        finally
        {
            await TryDropDatabaseAsync(baseConnectionString, catalog);
        }
    }

    private static AppertureDbContext NewDb(string connectionString) =>
        new(new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(connectionString)
            .Options);

    private static async Task TryDropDatabaseAsync(string baseConnectionString, string catalog)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = "master" };
            await using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{catalog}];";
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }
    }
}
```

Adjust the exact `AuditQuery`/`AuditPage` constructor/property names to their real current shape if this sketch's positional arguments (`FilterByResourceContextId`, `PageSize`, `.Items`) differ — read `IAuditService.cs`'s sibling `AuditQuery`/`AuditPage` record definitions before finalizing.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~AuditCorrelationIdSqlServerTests"`
Expected: FAIL — the returned record's `CorrelationId.Value` is `null`, not the appended value (current `QueryAsync` hardcodes `new CorrelationId(null)`).

- [ ] **Step 3: Add the column to the entity**

In `AuditLogEntity.cs`, add:

```csharp
    /// <summary>
    /// CTX-3B: links two related audit records (e.g. a move's departure and arrival rows) that share
    /// one operation. Null for every audit action that predates this column and for actions that are
    /// not part of a multi-record operation.
    /// </summary>
    public string? CorrelationId { get; set; }
```

- [ ] **Step 4: Wire `SqlAuditService`**

In `AppendAsync`, add `CorrelationId = record.CorrelationId.Value` to the `AuditLogEntity` initializer. In `QueryAsync` and `GetByIdAsync`, replace `new CorrelationId(null)` with `new CorrelationId(e.CorrelationId)` / `new CorrelationId(entity.CorrelationId)` respectively.

- [ ] **Step 5: Generate and apply the migration**

Run:
```bash
dotnet ef migrations add Ctx3B_AuditCorrelationId --project src/Apperture.Persistence/Apperture.Persistence.SqlServer --startup-project src/Apperture.ControlPlane/Apperture.Management.Host --context Apperture.Persistence.SqlServer.AppertureDbContext
```
Expected: a new migration adding one **nullable** `nvarchar` column `CorrelationId` to `AuditLogs`, with no other model changes. Inspect the generated migration file before committing and confirm all four of:

1. it adds exactly one column;
2. the column is nullable, so **every existing audit row is preserved untouched** with `CorrelationId = NULL`;
3. there is no `UPDATE`/backfill statement of any kind;
4. **there is no index on `CorrelationId`.**

On (4): an index here is deliberately declined. Nothing queries by correlation — `AuditQuery` has no correlation filter, this plan adds none, `SqlAuditService.QueryAsync` orders by `(At desc, Id desc)` and filters by actor/time/outcome/module/schedule/run/context, and the design gate calls correlation-collapsing in the Audit UI "an optional later nicety." An index on a column with no query predicate is pure write cost on the platform's highest-volume append-only table. Add one when, and only when, a query actually groups by it.

- [ ] **Step 6: Run to verify the test passes**

Run: `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~AuditCorrelationIdSqlServerTests"`
Expected: PASS against reachable SQL Server; soft-skip otherwise.

- [ ] **Step 7: Full IntegrationTests baseline**

Run: `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj`
Expected: no regressions against the 204/204 baseline.

- [ ] **Step 8: Commit**

```bash
git add src/Apperture.Persistence/Apperture.Persistence.SqlServer/Entities/AuditLogEntity.cs \
        src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlAuditService.cs \
        src/Apperture.Persistence/Apperture.Persistence.SqlServer/Migrations/*Ctx3B_AuditCorrelationId* \
        src/Apperture.Persistence/Apperture.Persistence.SqlServer/Migrations/AppertureDbContextModelSnapshot.cs \
        tests/Apperture.IntegrationTests/AuditCorrelationIdSqlServerTests.cs
git commit -m "CTX-3B: persist AuditRecord.CorrelationId (previously domain-only, never written)"
```

---

### Task 2: Move-audit action constants and `Details` payload shapes

**Files:**
- Modify: `src/Apperture.Core/Apperture.Core.Domain/Governance/ActionConstants.cs`
- Create: `src/Apperture.Core/Apperture.Core.Application/ResourceContexts/ResourceMoveAuditDetails.cs`

Data-only task; no TDD.

- [ ] **Step 1: Add the nine action constants**

```csharp
    // Resource ownership movement (CTX-3B, design gate §8.8)
    public const string IntegrationOwnershipMovedOut = "integration.ownership.moved_out";
    public const string IntegrationOwnershipMovedIn = "integration.ownership.moved_in";
    public const string IntegrationOwnershipMoveRejected = "integration.ownership.move_rejected";
    public const string ConnectionOwnershipMovedOut = "connection.ownership.moved_out";
    public const string ConnectionOwnershipMovedIn = "connection.ownership.moved_in";
    public const string ConnectionOwnershipMoveRejected = "connection.ownership.move_rejected";
    public const string ScheduleOwnershipMovedOut = "schedule.ownership.moved_out";
    public const string ScheduleOwnershipMovedIn = "schedule.ownership.moved_in";
    public const string ScheduleOwnershipMoveRejected = "schedule.ownership.move_rejected";
```

- [ ] **Step 2: Write the `Details` payload records**

```csharp
using System.Text.Json.Serialization;

namespace Apperture.Core.Application.ResourceContexts;

/// <summary>JSON payload for a successful move's moved_out/moved_in audit pair (design gate §8.8). Both records carry the identical payload.</summary>
public sealed record ResourceMoveAuditDetails(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("resourceId")] string ResourceId,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("sourceResourceContextId")] string SourceResourceContextId,
    [property: JsonPropertyName("targetResourceContextId")] string TargetResourceContextId,
    [property: JsonPropertyName("movedScheduleIds")] IReadOnlyList<string>? MovedScheduleIds = null);

/// <summary>JSON payload for a blocked move's single move_rejected audit record.</summary>
public sealed record ResourceMoveRejectedAuditDetails(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("resourceId")] string ResourceId,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("sourceResourceContextId")] string SourceResourceContextId,
    [property: JsonPropertyName("targetResourceContextId")] string TargetResourceContextId,
    [property: JsonPropertyName("blockerCodes")] IReadOnlyList<string> BlockerCodes);
```

- [ ] **Step 3: Build and commit**

```bash
dotnet build Apperture.slnx
git add src/Apperture.Core/Apperture.Core.Domain/Governance/ActionConstants.cs \
        src/Apperture.Core/Apperture.Core.Application/ResourceContexts/ResourceMoveAuditDetails.cs
git commit -m "CTX-3B: add movement audit action constants and Details payload shapes"
```

---

### Task 3: Wire audit into `ResourceOwnershipMoveService.Move*Async`

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceOwnershipMoveService.cs`
- Modify: `tests/Apperture.UnitTests/ResourceContexts/ResourceOwnershipMoveTestHarness.cs` (constructor needs `IAuditService`; already has a `RecordingAuditService`-style double available to model — see `ManagementServiceTestHarness`)
- Test: `tests/Apperture.UnitTests/ResourceContexts/ResourceMoveAuditTests.cs`

**Interfaces:**
- Consumes: `IAuditService.AppendAsync` (existing interface, now with real `CorrelationId` persistence from Task 1).
- Produces: two `AuditRecord`s per successful move, one per rejected move, all appended inside the same transaction as the mutation (or the same no-mutation transaction, for rejections).

- [ ] **Step 1: Add a `RecordingAuditService` double to the harness and write the failing tests**

Add to `ResourceOwnershipMoveTestHarness.cs` (mirrors `ManagementServiceTestHarness`'s private class, but exposed since this plan's tests need to inspect it directly):

```csharp
    public RecordingAuditService Audit { get; } = new();

    public sealed class RecordingAuditService : IAuditService
    {
        private readonly List<AuditRecord> _records = new();
        public IReadOnlyList<AuditRecord> Records => _records;
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            _records.Add(record);
            return ValueTask.CompletedTask;
        }
        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by ResourceOwnershipMoveTestHarness.");
        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by ResourceOwnershipMoveTestHarness.");
        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by ResourceOwnershipMoveTestHarness.");
    }
```

And pass `Audit` into the `ResourceOwnershipMoveService` constructor built in the harness's own constructor (see Task 1's/Plan 2's `Service = new ResourceOwnershipMoveService(...)` call — add `Audit` as the final argument once Step 3 below adds it to the real constructor).

```csharp
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

public sealed class ResourceMoveAuditTests
{
    [Fact]
    public async Task Successful_integration_move_writes_exactly_two_records_sharing_a_correlation_id()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);

        await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        h.Audit.Records.Should().HaveCount(2);
        var movedOut = h.Audit.Records.Should().ContainSingle(r => r.Action == ActionConstants.IntegrationOwnershipMovedOut).Subject;
        var movedIn = h.Audit.Records.Should().ContainSingle(r => r.Action == ActionConstants.IntegrationOwnershipMovedIn).Subject;
        movedOut.ResourceContextId.Should().Be(source.Id.Value);
        movedIn.ResourceContextId.Should().Be(target.Id.Value);
        movedOut.CorrelationId.Value.Should().NotBeNullOrEmpty();
        movedOut.CorrelationId.Value.Should().Be(movedIn.CorrelationId.Value);
    }

    [Fact]
    public async Task Blocked_integration_move_writes_exactly_one_rejected_record_at_source()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var connection = await h.CreateConnectionAsync(thirdParty.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);

        await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        h.Audit.Records.Should().HaveCount(1);
        var rejected = h.Audit.Records[0];
        rejected.Action.Should().Be(ActionConstants.IntegrationOwnershipMoveRejected);
        rejected.ResourceContextId.Should().Be(source.Id.Value);
        rejected.Details.Should().Contain("binding_connection_unreachable");
    }

    [Fact]
    public async Task Successful_connection_move_writes_two_records()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);

        await h.Service.MoveConnectionAsync(connection.Id, target.Id);

        h.Audit.Records.Should().HaveCount(2);
        h.Audit.Records.Should().Contain(r => r.Action == ActionConstants.ConnectionOwnershipMovedOut && r.ResourceContextId == source.Id.Value);
        h.Audit.Records.Should().Contain(r => r.Action == ActionConstants.ConnectionOwnershipMovedIn && r.ResourceContextId == target.Id.Value);
    }

    [Fact]
    public async Task Blocked_schedule_move_writes_one_rejected_record()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var schedule = await h.CreateAttachedScheduleAsync(integration.Id);

        await h.Service.MoveScheduleAsync(schedule.Id, target.Id);

        h.Audit.Records.Should().HaveCount(1);
        h.Audit.Records[0].Action.Should().Be(ActionConstants.ScheduleOwnershipMoveRejected);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceMoveAuditTests"`
Expected: FAIL (compile error — `IAuditService` not yet a dependency of `ResourceOwnershipMoveService`).

- [ ] **Step 3: Add `IAuditService` to the service and write audit inside each `Move*Async`**

Add `private readonly IAuditService _audit;` and the constructor parameter (append it last to minimize diff noise at existing call sites' argument lists). Then, in `MoveIntegrationAsync`, replace the body from the validation call onward:

```csharp
            var validation = await ValidateIntegrationMoveAsync(integration, targetResourceContextId, cancellationToken).ConfigureAwait(false);
            var preview = validation.Preview;

            if (!preview.CanMove)
            {
                await _audit.AppendAsync(new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    actor.Id,
                    ActionConstants.IntegrationOwnershipMoveRejected,
                    new ResourceDescriptor(ResourceTypeConstants.Integration, id.Value),
                    Outcome: "",
                    DateTimeOffset.UtcNow,
                    new CorrelationId(Guid.NewGuid().ToString("N")),
                    System.Text.Json.JsonSerializer.Serialize(new ResourceMoveRejectedAuditDetails(
                        ResourceMoveResourceTypes.Integration, id.Value, integration.DisplayName,
                        integration.ResourceContextId.Value, targetResourceContextId.Value,
                        preview.Blockers.Select(b => b.Code).ToList())),
                    ResourceContextId: integration.ResourceContextId.Value), cancellationToken).ConfigureAwait(false);

                await tx.CommitAsync(cancellationToken).ConfigureAwait(false); // persists the rejection record
                return preview;
            }

            var sourceContextId = integration.ResourceContextId.Value;
            await _integrationRepository.SetResourceContextIdAsync(id, targetResourceContextId, cancellationToken).ConfigureAwait(false);

            // The validator's authoritative set from Plan 1/Plan 2 -- never a second query. The
            // schedules_move_with_integration effect the operator saw, the rows actually mutated, and this
            // audit payload all describe the same schedules from one read inside one transaction.
            var movedScheduleIds = validation.AttachedSchedules.Select(s => s.Id.Value).ToList();
            foreach (var schedule in validation.AttachedSchedules)
                await _scheduleRepository.SetResourceContextIdAsync(schedule.Id, targetResourceContextId, cancellationToken).ConfigureAwait(false);

            var correlationId = new CorrelationId(Guid.NewGuid().ToString("N"));
            var detailsJson = System.Text.Json.JsonSerializer.Serialize(new ResourceMoveAuditDetails(
                ResourceMoveResourceTypes.Integration, id.Value, integration.DisplayName,
                sourceContextId, targetResourceContextId.Value, movedScheduleIds));

            await _audit.AppendAsync(new AuditRecord(
                new AuditRecordId(Guid.NewGuid().ToString("N")), actor.Id, ActionConstants.IntegrationOwnershipMovedOut,
                new ResourceDescriptor(ResourceTypeConstants.Integration, id.Value), Outcome: "",
                DateTimeOffset.UtcNow, correlationId, detailsJson, ResourceContextId: sourceContextId), cancellationToken).ConfigureAwait(false);
            await _audit.AppendAsync(new AuditRecord(
                new AuditRecordId(Guid.NewGuid().ToString("N")), actor.Id, ActionConstants.IntegrationOwnershipMovedIn,
                new ResourceDescriptor(ResourceTypeConstants.Integration, id.Value), Outcome: "",
                DateTimeOffset.UtcNow, correlationId, detailsJson, ResourceContextId: targetResourceContextId.Value), cancellationToken).ConfigureAwait(false);

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return preview;
```

Apply the identical pattern to `MoveConnectionAsync` (using `Connection`/`ConnectionOwnershipMoved*` constants, `connection.Name` for display, and no `movedScheduleIds`) and `MoveScheduleAsync` (using `Schedule`/`ScheduleOwnershipMoved*` constants, `schedule.Name`, no `movedScheduleIds`).

**No control-flow change from Plan 2.** Plan 2 already commits on both outcomes, precisely so this plan is purely additive: the rejected path's commit was a no-op there and now carries exactly one `move_rejected` row. The underlying resource is still untouched on that path (`SetResourceContextIdAsync` is never called), so the transaction's write set is one new audit row and nothing else.

**The gate still applies.** These appends happen inside the gated transaction Plan 2 opened, after `IResourceOwnershipMutationGate.AcquireAsync`. Do not add a second transaction, and do not move the audit append outside the gate.

- [ ] **Step 4: Update `ResourceOwnershipMoveTestHarness`'s constructor call**

Pass `Audit` as the final constructor argument to `ResourceOwnershipMoveService` in the harness (after Plan 2's `_db` and gate arguments).

- [ ] **Step 4a: Prove an audit failure rolls the movement back**

The whole "append inside the transaction" decision rests on this behavior, so it gets a test rather than a comment:

```csharp
    [Fact]
    public async Task Audit_failure_rolls_the_movement_back()
    {
        using var h = new ResourceOwnershipMoveTestHarness(audit: new ThrowingAuditService());
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var schedule = await h.CreateAttachedScheduleAsync(integration.Id);

        var act = async () => await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await h.IntegrationRepository.GetByIdAsync(integration.Id))!.ResourceContextId.Value
            .Should().Be(source.Id.Value, "an unauditable move must not be a successful move");
        (await h.ScheduleRepository.GetDefinitionAsync(schedule.Id))!.ResourceContextId!.Value
            .Should().Be(source.Id.Value);
    }
```

`ThrowingAuditService` throws from `AppendAsync`. Add an `audit:` parameter to the harness constructor so a test can substitute it.

**Note on the InMemory provider:** EF Core InMemory has no real transaction, so this test proves the exception propagates and the *service* performs no further writes, not that the database rolled back. Add the companion assertion against real SQL Server in `ResourceMoveAuditFilteringSqlServerTests` (Step 6), where the rollback is genuine — that is the version that actually proves the guarantee.

- [ ] **Step 4b: Prove the move service and the audit sink share one `AppertureDbContext`**

```csharp
    [Fact]
    public void Move_service_and_audit_service_resolve_the_same_DbContext_instance_per_scope()
    {
        // The entire "append inside the move transaction" design depends on this. If someone later
        // registers an audit sink with its own context or its own factory, transactionality degrades
        // silently and no other test in this epic would notice.
        using var provider = BuildProductionLikeServiceProvider();
        using var scope = provider.CreateScope();

        var moveService = scope.ServiceProvider.GetRequiredService<IResourceOwnershipMoveService>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

        ExtractDbContext(moveService).Should().BeSameAs(ExtractDbContext(auditService));
    }
```

Implement `ExtractDbContext` by reflection over the private field, or — cleaner if the codebase prefers it — assert instead that `scope.ServiceProvider.GetRequiredService<AppertureDbContext>()` is the same instance resolved twice and that both services are registered `Scoped` with no context factory in their dependency graph. Either form is acceptable; what matters is that a future registration change fails a test.

Also confirm, and record in the implementation notes: `Apperture.RuntimeHost.Windows.WorkerHost/Program.cs:87` registers `AddScoped<IAuditService, SqlAuditService>` independently. Verify `IResourceOwnershipMoveService` is not resolvable there, or that the same shared-context conclusion holds if it is.

- [ ] **Step 5: Run to verify tests pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceMoveAuditTests"`
Expected: PASS.

- [ ] **Step 6: Add source/target/global Audit-filter tests**

Add to `ResourceMoveAuditTests.cs`:

```csharp
    [Fact]
    public async Task Source_filtered_audit_sees_moved_out_target_filtered_sees_moved_in_global_sees_both()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);

        await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        var atSource = h.Audit.Records.Where(r => r.ResourceContextId == source.Id.Value).ToList();
        var atTarget = h.Audit.Records.Where(r => r.ResourceContextId == target.Id.Value).ToList();

        atSource.Should().ContainSingle().Which.Action.Should().Be(ActionConstants.IntegrationOwnershipMovedOut);
        atTarget.Should().ContainSingle().Which.Action.Should().Be(ActionConstants.IntegrationOwnershipMovedIn);
        h.Audit.Records.Should().HaveCount(2, "the unfiltered/global view sees both");
        atSource[0].CorrelationId.Value.Should().Be(atTarget[0].CorrelationId.Value);
    }
```

This unit-level test only proves that two records were written carrying two different `ResourceContextId` values — which the earlier test already covers. It filters an in-memory list with LINQ; it does **not** exercise `AuditQuery.FilterByResourceContextId` or `SqlAuditService.ApplyFilters` at all. Keep it as a cheap shape assertion, but it is not the proof of the two-sided model.

- [ ] **Step 6a: The real two-sided filtering test (required, not optional)**

Create `tests/Apperture.IntegrationTests/ResourceMoveAuditFilteringSqlServerTests.cs`, following `AuditCorrelationIdSqlServerTests`'s pattern and reusing Plan 2's `SqlServerScratchDatabase` helper. This is the test that proves the model an operator actually sees:

```csharp
    [Fact]
    public async Task Source_target_and_global_audit_queries_each_return_the_right_records()
    {
        // Real SqlAuditService, real AuditQuery.FilterByResourceContextId, real rows -- not a recording
        // double and not LINQ over a list. This is what proves an operator filtering Audit to the source
        // Application sees "moved out", filtering to the target sees "moved in", and the unfiltered view
        // sees both with one shared correlation id.
        // ... seed contexts + integration, run the production MoveIntegrationAsync ...

        var atSource = await auditService.QueryAsync(new AuditQuery(FilterByResourceContextId: source.Id.Value, PageSize: 10));
        var atTarget = await auditService.QueryAsync(new AuditQuery(FilterByResourceContextId: target.Id.Value, PageSize: 10));
        var global   = await auditService.QueryAsync(new AuditQuery(PageSize: 10));

        atSource.Items.Should().ContainSingle().Which.Action.Should().Be(ActionConstants.IntegrationOwnershipMovedOut);
        atTarget.Items.Should().ContainSingle().Which.Action.Should().Be(ActionConstants.IntegrationOwnershipMovedIn);
        global.Items.Should().HaveCount(2);
        atSource.Items[0].CorrelationId.Value.Should().Be(atTarget.Items[0].CorrelationId.Value);
        atSource.Items[0].CorrelationId.Value.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Blocked_move_writes_one_rejected_record_at_source_and_none_at_target()
    {
        // ... seed a blocked fixture, run MoveIntegrationAsync ...
        atSource.Items.Should().ContainSingle().Which.Action.Should().Be(ActionConstants.IntegrationOwnershipMoveRejected);
        atTarget.Items.Should().BeEmpty("a rejection is the source's event; the target never heard about it");
    }

    [Fact]
    public async Task Audit_failure_rolls_the_movement_back_on_real_sql_server()
    {
        // The genuine rollback proof that the InMemory test in Step 4a cannot give. Substitute an audit
        // sink that throws after the ownership write, then assert the persisted Integration row still
        // holds the SOURCE context after the exception -- i.e. the transaction really did roll back.
    }
```

Adjust the exact `AuditQuery`/`AuditPage` constructor and property names to their real shapes before finalizing (read the records next to `IAuditService`); the sketch's `FilterByResourceContextId`/`PageSize`/`.Items` names are from the review pass, not from a compile.

- [ ] **Step 7: Run and confirm**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceMoveAuditTests"`
Expected: PASS.

- [ ] **Step 8: Full regression run**

```bash
dotnet build Apperture.slnx
dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj
```
Expected: 0 errors, full green, no regressions against the 204/204 baseline.

- [ ] **Step 9: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceOwnershipMoveService.cs \
        tests/Apperture.UnitTests/ResourceContexts/ResourceOwnershipMoveTestHarness.cs \
        tests/Apperture.UnitTests/ResourceContexts/ResourceMoveAuditTests.cs \
        tests/Apperture.IntegrationTests/ResourceMoveAuditFilteringSqlServerTests.cs
git commit -m "CTX-3B: wire two-sided movement audit into Move*Async, inside the gated move transaction"
```

---

## Out of scope for this plan

- No Audit UI changes (e.g. collapsing correlated rows) — the design gate explicitly calls this "an optional later Audit UI nicety," not a CTX-3B requirement.
- No new `AuditQuery` filter for `CorrelationId`, and **no index on the column** — see Task 1 Step 5. Source/target filtering already works through the existing `ResourceContextId` filter.
- No changes to non-movement audit call sites (`IntegrationSettingsService`, etc.) beyond the shared `CorrelationId` persistence fix in Task 1, which benefits them too (they already pass `new CorrelationId(null)`, so nothing changes for them).
- **No Integration lifecycle audit.** The `integration.deleted` action (product decision P2) belongs to Plan 6a, alongside the delete guards and the secret-safe cleanup whose outcome it records. Broader Integration lifecycle audit consistency (create/update/activate/deactivate/retire — none of which is audited today) stays explicit later debt, recorded in Plan 6b's closure.

## Validation for this plan

- `dotnet build Apperture.slnx` — 0 errors.
- `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj` — full project green, including the audit-failure rollback and shared-DbContext tests.
- Plan 2's `ResourceMoveHistoricalImmutabilityTests` stays green — in particular `Move_appends_to_audit_without_rewriting_any_pre_existing_audit_row`, whose "only appended" half becomes meaningful for the first time with this plan's rows.
- `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj` — no regressions against the 204/204 baseline.
- **`AuditCorrelationIdSqlServerTests` and `ResourceMoveAuditFilteringSqlServerTests` must demonstrably execute** against a reachable SQL Server, not soft-skip. Record the executed/skipped counts; Plan 6b's closure requires this evidence alongside Plan 2's.
- `dotnet test Apperture.slnx` once at the end to confirm no incidental breakage elsewhere.
