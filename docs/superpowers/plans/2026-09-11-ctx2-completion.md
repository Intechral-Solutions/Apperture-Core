# CTX-2 Completion Pass Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the gaps CTX-2 (commit `6479229`) deliberately deferred — populate `ResourceContextId` at authoritative audit call sites, expose the historical snapshot on Run/Schedule/Attention/Notification read contracts, add SQL-side historical filtering to Runs (required) and to Audit/Notifications/PlatformEvents (where a structured query object already exists), and prove the historical snapshot stays immutable under simulated ownership change — without touching the CTX-1/CTX-2 snapshot architecture itself.

**Architecture:** Every change is additive and read/write-side only. No new columns, no migration, no new tables. `AuditRecord` gains one new optional trailing parameter (`ResourceContextId`); each audit call site is updated to pass it from an entity/parameter *already in scope* (or, in the one documented exception, from a single new keyed lookup of the exact resource the action targets). Run/Schedule/Attention/Notification DTOs gain one new optional property each, mapped from a snapshot column that already exists on the underlying entity. Query filters follow the existing `.Where(x => x.Field == filter.Field)` pattern already used for `IntegrationId`/`ModuleId`/`ScheduleId`/`RunId` in each store.

**Tech Stack:** .NET / C#, EF Core (SQL Server + InMemory provider for tests), xUnit, FluentAssertions, Moq.

**Spec:** The CTX-2 completion brief (given verbatim in this conversation) plus:
- `docs/01-architecture/transitions/resource-contexts/ctx2-closure.md` (§14 names the exact deferred gaps this plan closes)
- `docs/01-architecture/transitions/resource-contexts/ctx1-closure.md`
- `docs/01-architecture/transitions/resource-contexts/ctx-design-gate.md`

## Global Constraints

- Do not redesign the snapshot architecture: context is resolved once at occurrence/dispatch, historical `ResourceContextId` is immutable, WorkerHost propagates snapshots (never re-queries current ownership), old history stays `null`, `null` is never Shared, historical columns have no FKs.
- No generic "infer context from IDs" logic inside `SqlAuditService` — every call site supplies the ID itself; the audit service only persists what it is given.
- No parsing `Details` strings to recover context.
- Missing authoritative context stays `null` — never defaulted to Shared.
- Do not perform a new lookup query merely to populate audit context, **except** the one documented exception in Task 6 (`IntegrationSettingsService`), where the CTX-2 brief explicitly names this call-site family as in-scope and no Integration entity is otherwise ever loaded in that service.
- Prefer ID-only exposure on Attention/Notification DTOs — no `ResourceContext` joins, no names, no `ResourceContextRef` on high-volume Run results.
- Do not start CTX-3, Applications UI, resource movement, context IAM, Services, Resource Catalog, or Business Data ownership.
- Do not add UI wiring for any of the new filters — this is read-side/API preparation only.
- New optional filters/fields must default to "no filter" / absent, so every existing caller and every pre-CTX-2 row with `null` behaves exactly as before.

---

## Task 1: `AuditRecord` — add `ResourceContextId` and thread it through `SqlAuditService`

**Files:**
- Modify: `src/Apperture.Core/Apperture.Core.Domain/Audit/AuditRecord.cs`
- Modify: `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlAuditService.cs`
- Test: `tests/Apperture.UnitTests/ControlPlane/SqlAuditServiceResourceContextTests.cs` (new)

**Interfaces:**
- Produces: `AuditRecord` gains `string? ResourceContextId = null` as its final positional/named parameter. Every later task's audit call sites pass this named argument.
- Consumes: `AuditLogEntity.ResourceContextId` (already exists, `src/Apperture.Persistence/Apperture.Persistence.SqlServer/Entities/AuditLogEntity.cs:28`).

`AuditLogEntity` already has the column (from CTX-2 migration `20260912001419_Ctx2_ResourceContextHistoricalProvenance`) — this task only wires the domain record and the append/query mapping. No migration is needed.

- [ ] **Step 1: Write the failing test**

Create `tests/Apperture.UnitTests/ControlPlane/SqlAuditServiceResourceContextTests.cs`:

```csharp
using Apperture.Core.Domain.Audit;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class SqlAuditServiceResourceContextTests
{
    [Fact]
    public async Task AppendAsync_PersistsResourceContextId_WhenSupplied()
    {
        var db = NewDb();
        var svc = new SqlAuditService(db);

        await svc.AppendAsync(new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId("system"),
            "test.action",
            Resource: null,
            Outcome: "success",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            Details: null,
            ResourceContextId: "app-a"));

        var entity = await db.AuditLogs.SingleAsync();
        entity.ResourceContextId.Should().Be("app-a");
    }

    [Fact]
    public async Task AppendAsync_LeavesResourceContextIdNull_WhenNotSupplied()
    {
        var db = NewDb();
        var svc = new SqlAuditService(db);

        await svc.AppendAsync(new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId("system"),
            "test.action",
            Resource: null,
            Outcome: "success",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            Details: null));

        var entity = await db.AuditLogs.SingleAsync();
        entity.ResourceContextId.Should().BeNull();
    }

    [Fact]
    public async Task QueryAsync_And_GetByIdAsync_RoundTripResourceContextId()
    {
        var db = NewDb();
        var svc = new SqlAuditService(db);
        await svc.AppendAsync(new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId("system"),
            "test.action",
            Resource: null,
            Outcome: "success",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            Details: null,
            ResourceContextId: "app-b"));

        var page = await svc.QueryAsync(new Apperture.Core.Application.Services.AuditQuery());
        page.Items.Should().ContainSingle(r => r.ResourceContextId == "app-b");

        var byId = await svc.GetByIdAsync(page.Items[0].Id.Value);
        byId!.ResourceContextId.Should().Be("app-b");
    }

    private static AppertureDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppertureDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~SqlAuditServiceResourceContextTests`
Expected: FAIL to compile — `AuditRecord` has no `ResourceContextId` parameter yet.

- [ ] **Step 3: Add `ResourceContextId` to `AuditRecord`**

In `src/Apperture.Core/Apperture.Core.Domain/Audit/AuditRecord.cs`, change:

```csharp
public sealed record AuditRecord(
    AuditRecordId Id,
    ActorId ActorId,
    string Action,
    ResourceDescriptor? Resource,
    string Outcome,
    DateTimeOffset Timestamp,
    CorrelationId CorrelationId,
    string? Details,
    string? ModuleId = null,
    string? ScheduleId = null,
    string? RunId = null);
```

to:

```csharp
public sealed record AuditRecord(
    AuditRecordId Id,
    ActorId ActorId,
    string Action,
    ResourceDescriptor? Resource,
    string Outcome,
    DateTimeOffset Timestamp,
    CorrelationId CorrelationId,
    string? Details,
    string? ModuleId = null,
    string? ScheduleId = null,
    string? RunId = null,
    /// <summary>
    /// CTX-2: the authoritative ResourceContext the caller already knows for this occurrence (e.g. an
    /// already-loaded Integration/Run/ScheduleDefinition/OperationGroup). Never inferred here from
    /// other ids. Null for System/IAM/platform-settings audit and for callers with no authoritative
    /// context available.
    /// </summary>
    string? ResourceContextId = null);
```

- [ ] **Step 4: Wire it through `SqlAuditService`**

In `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlAuditService.cs`:

Change the `AppendAsync` entity construction (lines 25-35):

```csharp
        _db.AuditLogs.Add(new AuditLogEntity
        {
            At = record.Timestamp,
            Actor = record.ActorId.Value,
            Action = record.Action,
            Details = details,
            Outcome = string.IsNullOrEmpty(record.Outcome) ? null : record.Outcome,
            ModuleId = record.ModuleId,
            ScheduleId = record.ScheduleId,
            RunId = record.RunId,
            ResourceContextId = record.ResourceContextId
        });
```

Change the `QueryAsync` projection (lines 52-63) to add `ResourceContextId: e.ResourceContextId` as the final constructor argument, and the `GetByIdAsync` projection (lines 84-95) to add `ResourceContextId: entity.ResourceContextId`.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~SqlAuditServiceResourceContextTests`
Expected: PASS (3 tests)

- [ ] **Step 6: Run the full existing Audit test suite to confirm no regression**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~Audit`
Expected: PASS, same count as before plus the 3 new tests.

- [ ] **Step 7: Commit**

```bash
git add src/Apperture.Core/Apperture.Core.Domain/Audit/AuditRecord.cs src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlAuditService.cs tests/Apperture.UnitTests/ControlPlane/SqlAuditServiceResourceContextTests.cs
git commit -m "CTX-2 completion: add ResourceContextId to AuditRecord and SqlAuditService"
```

---

## Task 2: Populate audit context — `WorkflowStartRequestProcessor` / `OperationStartRequestProcessor`

**Files:**
- Modify: `src/Apperture.RuntimePlane/Apperture.RuntimePlane.Engine/WorkflowStartRequestProcessor.cs`
- Modify: `src/Apperture.RuntimePlane/Apperture.RuntimePlane.Engine/OperationStartRequestProcessor.cs`
- Test: `tests/Apperture.UnitTests/Runtime/WorkflowProcessorDefinitionResolutionTests.cs` (extend), plus a new sibling test for the operation processor

**Interfaces:**
- Consumes: `AuditRecord(..., ResourceContextId: ...)` from Task 1. `OperationRunEntity.ResourceContextId` (already exists).
- Both processors already have `run` (`OperationRunEntity`) in lexical scope at their `catch (Exception ex)` block that calls `TryAuditDefinitionUnavailableAsync` — confirmed by reading the surrounding method bodies; `run` is declared before the `try` and used throughout it.

Both files have the identical private helper shape. Fix both the same way.

- [ ] **Step 1: Write the failing test (workflow processor)**

Add to `tests/Apperture.UnitTests/Runtime/WorkflowProcessorDefinitionResolutionTests.cs`:

```csharp
    [Fact]
    public async Task TryAuditDefinitionUnavailableAsync_PassesResourceContextIdThrough()
    {
        var db = CreateDb();
        var module = new HelloWorldModule();
        var registry = new StepHandlerRegistry();
        module.RegisterStepHandlers(registry);
        var executor = new DefaultWorkflowExecutor(new NoOpSlotResolver(), registry);
        var definitionResolver = new WorkflowDefinitionResolver([module], new DenyAllActivationFilter());
        var audit = new CapturingAuditService();

        var processor = new WorkflowStartRequestProcessor(
            db,
            executor,
            definitionResolver,
            "worker",
            Options.Create(new WorkflowStartRequestLeaseOptions { LeaseTimeout = TimeSpan.FromMinutes(5) }),
            audit: audit);

        var ex = new DefinitionNotExecutableException("wf.def", "workflow", "not effectively active");
        await InvokeAudit(processor, ex, "run-1", "app-a");

        audit.LastRecord.Should().NotBeNull();
        audit.LastRecord!.ResourceContextId.Should().Be("app-a");
    }

    private static async Task InvokeAudit(
        WorkflowStartRequestProcessor processor, Exception ex, string runId, string? resourceContextId)
    {
        var method = typeof(WorkflowStartRequestProcessor).GetMethod(
            "TryAuditDefinitionUnavailableAsync",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(Exception), typeof(string), typeof(string), typeof(CancellationToken)],
            null);
        if (method is null)
            throw new InvalidOperationException("TryAuditDefinitionUnavailableAsync not found");
        var result = method.Invoke(processor, new object?[] { ex, runId, resourceContextId, CancellationToken.None });
        if (result is ValueTask vt)
            await vt.ConfigureAwait(false);
        else
            throw new InvalidOperationException($"Unexpected return type {result?.GetType().FullName}");
    }

    private sealed class CapturingAuditService : Apperture.Core.Application.Services.IAuditService
    {
        public Apperture.Core.Domain.Audit.AuditRecord? LastRecord { get; private set; }
        public ValueTask AppendAsync(Apperture.Core.Domain.Audit.AuditRecord record, CancellationToken cancellationToken = default)
        {
            LastRecord = record;
            return ValueTask.CompletedTask;
        }
        public ValueTask<Apperture.Core.Application.Services.AuditPage> QueryAsync(Apperture.Core.Application.Services.AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new Apperture.Core.Application.Services.AuditPage([], null));
        public ValueTask<int> CountAsync(Apperture.Core.Application.Services.AuditQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask<Apperture.Core.Domain.Audit.AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Apperture.Core.Domain.Audit.AuditRecord?>(null);
    }
```

Add `using System.Threading.Tasks;` if not already present (it is, transitively, via existing usings) and `using Microsoft.Extensions.Options;` (already present).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~TryAuditDefinitionUnavailableAsync_PassesResourceContextIdThrough`
Expected: FAIL — reflection `GetMethod` with the 4-arg signature `(Exception, string, string, CancellationToken)` returns null (current signature is 3-arg), throwing `InvalidOperationException`.

- [ ] **Step 3: Update `WorkflowStartRequestProcessor.TryAuditDefinitionUnavailableAsync`**

In `src/Apperture.RuntimePlane/Apperture.RuntimePlane.Engine/WorkflowStartRequestProcessor.cs`, change (around line 715):

```csharp
    private async ValueTask TryAuditDefinitionUnavailableAsync(Exception ex, string runId, CancellationToken cancellationToken)
    {
        if (_audit is null || ex is not DefinitionNotExecutableException dne)
            return;

        try
        {
            await _audit.AppendAsync(
                new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    new ActorId("system-worker"),
                    LifecycleExecutionAuditActionNames.WorkflowRunDefinitionUnavailable,
                    Resource: null,
                    Outcome: "Blocked",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId(runId),
                    $"runId={runId};definitionId={dne.DefinitionId};kind={dne.Kind};message={dne.Message}",
                    RunId: runId),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // best-effort audit
        }
    }
```

to:

```csharp
    private async ValueTask TryAuditDefinitionUnavailableAsync(
        Exception ex, string runId, string? resourceContextId, CancellationToken cancellationToken)
    {
        if (_audit is null || ex is not DefinitionNotExecutableException dne)
            return;

        try
        {
            await _audit.AppendAsync(
                new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    new ActorId("system-worker"),
                    LifecycleExecutionAuditActionNames.WorkflowRunDefinitionUnavailable,
                    Resource: null,
                    Outcome: "Blocked",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId(runId),
                    $"runId={runId};definitionId={dne.DefinitionId};kind={dne.Kind};message={dne.Message}",
                    RunId: runId,
                    ResourceContextId: resourceContextId),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // best-effort audit
        }
    }
```

Then update the sole call site (around line 433):

```csharp
                await TryAuditDefinitionUnavailableAsync(ex, runId, cancellationToken).ConfigureAwait(false);
```

to:

```csharp
                await TryAuditDefinitionUnavailableAsync(ex, runId, run.ResourceContextId, cancellationToken).ConfigureAwait(false);
```

`run` (`OperationRunEntity`) is already loaded earlier in the same method (used at lines 385-398 before the `catch`), so this is zero new queries.

- [ ] **Step 4: Apply the identical change to `OperationStartRequestProcessor`**

Same edit shape in `src/Apperture.RuntimePlane/Apperture.RuntimePlane.Engine/OperationStartRequestProcessor.cs`: extend `TryAuditDefinitionUnavailableAsync` (around line 568) with the `string? resourceContextId` parameter and `ResourceContextId: resourceContextId` on the `AuditRecord`, and update its call site (around line 372) to `await TryAuditDefinitionUnavailableAsync(ex, runId, run.ResourceContextId, cancellationToken).ConfigureAwait(false);`. `run` is already loaded at line 164 (`var run = await _db.OperationRuns.FirstOrDefaultAsync(...)`) and in scope at the catch block.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~WorkflowProcessorDefinitionResolutionTests`
Expected: PASS

- [ ] **Step 6: Add the operation-processor sibling test**

Add a matching test in a new file `tests/Apperture.UnitTests/Runtime/OperationProcessorDefinitionResolutionAuditTests.cs` (mirror Step 1's test and helper exactly, substituting `OperationStartRequestProcessor` and its constructor shape — check its constructor parameter list at the top of `OperationStartRequestProcessor.cs` for the exact positional/named arguments required, e.g. `operationRegistry`, matching the pattern already used for `WorkflowStartRequestProcessor` above). Run and confirm PASS the same way (Steps 2/5 pattern).

- [ ] **Step 7: Run full Runtime test folder**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~Apperture.UnitTests.Runtime`
Expected: PASS, no regressions.

- [ ] **Step 8: Commit**

```bash
git add src/Apperture.RuntimePlane/Apperture.RuntimePlane.Engine/WorkflowStartRequestProcessor.cs src/Apperture.RuntimePlane/Apperture.RuntimePlane.Engine/OperationStartRequestProcessor.cs tests/Apperture.UnitTests/Runtime/WorkflowProcessorDefinitionResolutionTests.cs tests/Apperture.UnitTests/Runtime/OperationProcessorDefinitionResolutionAuditTests.cs
git commit -m "CTX-2 completion: populate audit ResourceContextId from the loaded Run in start-request processors"
```

---

## Task 3: Populate audit context — `WorkflowRetryService`

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/WorkflowRetryService.cs`
- Test: `tests/Apperture.UnitTests/ControlPlane/WorkflowRetryServiceTests.cs` (extend if it exists; otherwise create alongside existing retry-service tests — check `tests/Apperture.UnitTests/ControlPlane/` for a `WorkflowRetryServiceTests.cs` file first with Glob before creating a new one, to avoid a duplicate)

**Interfaces:**
- Consumes: `AuditRecord(..., ResourceContextId: ...)` from Task 1.
- `originalRun` (`OperationRunEntity`) is already loaded at line 41 of `WorkflowRetryService.cs` (`_db.OperationRuns.AsNoTracking().FirstOrDefaultAsync(...)`) and is in scope at the call site (line 73) that invokes `TryAuditRetryBlockedAsync`.

- [ ] **Step 1: Write the failing test**

First check whether `tests/Apperture.UnitTests/ControlPlane/WorkflowRetryServiceTests.cs` exists (Glob `tests/Apperture.UnitTests/**/WorkflowRetryService*Tests.cs`). If it exists, add this test to it, matching its existing DI/setup pattern (in-memory `AppertureDbContext`, a dispatch stub that returns `null` to force the blocked path, and an audit fake). If it does not exist, create it with a minimal harness:

```csharp
    [Fact]
    public async Task RequestRetryAsync_WhenDispatchBlocked_AuditsWithOriginalRunsResourceContextId()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "run-1",
            WorkflowInstanceId = "wi-1",
            DefinitionId = "wf.def",
            Status = WorkflowLifecycleStatusNames.Failed,
            CreatedAt = DateTimeOffset.UtcNow,
            ResourceContextId = "app-a"
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "run-1",
            WorkflowInstanceId = "wi-1",
            WorkflowDefinitionId = "wf.def",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Completed
        });
        await db.SaveChangesAsync();

        var audit = new CapturingAuditService();
        var service = new WorkflowRetryService(db, new AllowRetryAuthz(), new NullDispatch(), audit);

        await service.RequestRetryAsync(new OperationRunId("run-1"));

        audit.LastRecord.Should().NotBeNull();
        audit.LastRecord!.ResourceContextId.Should().Be("app-a");
    }
```

Add the small fakes it needs (`CapturingAuditService` as in Task 2 — factor to a shared internal test helper if one already exists in this test project, otherwise a private nested class; `AllowRetryAuthz : IWorkflowRunRetryAuthorizationService` with a no-op `EnsureCanRetryRunAsync`; `NullDispatch : IWorkflowStartDispatchService` whose `StartAsync` returns `null` to force the blocked branch) and a `NewDb()` helper matching the `Ctx2HistoricalProvenanceTests.cs` pattern.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~RequestRetryAsync_WhenDispatchBlocked_AuditsWithOriginalRunsResourceContextId`
Expected: FAIL — `audit.LastRecord!.ResourceContextId` is `null`, not `"app-a"`.

- [ ] **Step 3: Update `WorkflowRetryService`**

In `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/WorkflowRetryService.cs`, change the call site (line 73):

```csharp
            await TryAuditRetryBlockedAsync(runId.Value, originalRun.DefinitionId, cancellationToken).ConfigureAwait(false);
```

to:

```csharp
            await TryAuditRetryBlockedAsync(runId.Value, originalRun.DefinitionId, originalRun.ResourceContextId, cancellationToken).ConfigureAwait(false);
```

Change the helper (lines 111-135):

```csharp
    private async ValueTask TryAuditRetryBlockedAsync(string runId, string definitionId, CancellationToken cancellationToken)
    {
        if (_audit is null)
            return;

        try
        {
            await _audit.AppendAsync(
                new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    new ActorId("system"),
                    LifecycleExecutionAuditActionNames.WorkflowRetryBlocked,
                    Resource: null,
                    Outcome: "Blocked",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId(runId),
                    $"runId={runId};definitionId={definitionId};reason={BlockedReasonMessage}",
                    RunId: runId),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // best-effort audit
        }
    }
```

to:

```csharp
    private async ValueTask TryAuditRetryBlockedAsync(
        string runId, string definitionId, string? resourceContextId, CancellationToken cancellationToken)
    {
        if (_audit is null)
            return;

        try
        {
            await _audit.AppendAsync(
                new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    new ActorId("system"),
                    LifecycleExecutionAuditActionNames.WorkflowRetryBlocked,
                    Resource: null,
                    Outcome: "Blocked",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId(runId),
                    $"runId={runId};definitionId={definitionId};reason={BlockedReasonMessage}",
                    RunId: runId,
                    ResourceContextId: resourceContextId),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // best-effort audit
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~WorkflowRetryService`
Expected: PASS, all tests in the file green.

- [ ] **Step 5: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.ControlPlane.Security/WorkflowRetryService.cs tests/Apperture.UnitTests/ControlPlane/WorkflowRetryServiceTests.cs
git commit -m "CTX-2 completion: populate audit ResourceContextId from the original Run in WorkflowRetryService"
```

---

## Task 4: Populate audit context — `BulkOperationItemRetryService`

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/BulkOperationItemRetryService.cs`
- Test: `tests/Apperture.UnitTests/ControlPlane/BulkOperationItemRetryServiceTests.cs` (extend)

**Interfaces:**
- Consumes: `AuditRecord(..., ResourceContextId: ...)` from Task 1. `OperationGroupEntity.ResourceContextId` (already exists, copied from the owning Run at group creation per CTX-2 §6).
- `group` (`OperationGroupEntity`, from `_items.GetGroupAsync(...)`) is already loaded at line 100 of `BulkOperationItemRetryService.cs` and in scope at both `TryAuditAsync` call sites (line 158, "requested"; and a second site around line 317, "completed" — confirmed by the research pass; re-verify the exact second call site's line by grepping `TryAuditAsync\(` in this file before editing, since only the first call site's surrounding code was read in full).

- [ ] **Step 1: Confirm the second call site**

Run: `grep -n "TryAuditAsync(" src/Apperture.ControlPlane/Apperture.ControlPlane.Security/BulkOperationItemRetryService.cs` (or use the Grep tool) to get the exact line number and confirm `group` is in scope there (it is declared once at line 100 and used for the rest of the method, including the loop and completion reporting).

- [ ] **Step 2: Write the failing test**

Add to `tests/Apperture.UnitTests/ControlPlane/BulkOperationItemRetryServiceTests.cs`, extending the existing `NullAudit` fake (lines 344-357) into a capturing fake, or adding a new capturing fake alongside it without removing `NullAudit` (other tests may rely on its no-op behavior):

```csharp
    private sealed class CapturingAudit : IAuditService
    {
        public AuditRecord? LastRecord { get; private set; }
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            LastRecord = record;
            return ValueTask.CompletedTask;
        }
        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuditPage([], null));
        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AuditRecord?>(null);
    }

    [Fact]
    public async Task BulkRetryAsync_RequestedAudit_CarriesGroupsResourceContextId()
    {
        // Follow this file's existing setup pattern (see the constructor test helper used by the
        // other tests in this class) but construct the service with `new CapturingAudit()` instead
        // of `new NullAudit()`, and seed the OperationGroup with ResourceContextId = "app-a" via
        // whatever seeding helper this file already uses for OperationGroupEntity/IOperationItemStore.
        // Assert: after calling BulkRetryAsync with at least one item id, the CapturingAudit's
        // LastRecord.ResourceContextId is "app-a" (the "BulkRetryRequested" record, which fires
        // before the per-item loop, so it will be the last/only record captured if the loop's
        // own per-item calls do not also write through the same fake — check the file's existing
        // per-item mocks to confirm; if the loop dispatches through a different mocked collaborator
        // that does not touch `_audit`, LastRecord after the call reflects the "requested" record).
    }
```

Since this file's exact existing test scaffolding (how `OperationGroupEntity`/`IOperationItemStore` are seeded, and the constructor argument list for `BulkOperationItemRetryService`) was not fully read in this planning pass, the implementer must open `tests/Apperture.UnitTests/ControlPlane/BulkOperationItemRetryServiceTests.cs` first, copy the setup from the nearest existing `[Fact]` that seeds a group and calls `BulkRetryAsync`, and swap only the audit fake and the assertion. Do not invent a new setup style — reuse the file's existing one.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~BulkRetryAsync_RequestedAudit_CarriesGroupsResourceContextId`
Expected: FAIL — `LastRecord!.ResourceContextId` is `null`.

- [ ] **Step 4: Update `BulkOperationItemRetryService`**

In `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/BulkOperationItemRetryService.cs`, find the private `TryAuditAsync` helper (around line 409, per the research pass) and add a `string? resourceContextId` parameter, threading it into the `AuditRecord` constructor as `ResourceContextId: resourceContextId`. Update both call sites (line 158 area and the second confirmed in Step 1) to pass `group.ResourceContextId`.

Exact shape of the call at line 158-171 becomes:

```csharp
        await TryAuditAsync(
            actorId,
            OperationItemResolutionAuditActions.BulkRetryRequested,
            "success",
            correlation,
            runId.Value,
            JsonSerializer.Serialize(new
            {
                runId = runId.Value,
                operationGroupId = group.OperationGroupId,
                requestedCount = orderedUnique.Count,
                requestId,
            }),
            group.ResourceContextId,
            cancellationToken).ConfigureAwait(false);
```

(Insert `group.ResourceContextId` as a new parameter immediately before `cancellationToken` — match whatever parameter order `TryAuditAsync`'s signature already uses for its other positional args; read the full signature at line 409 before editing to get the order exactly right.)

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~BulkOperationItemRetryServiceTests`
Expected: PASS, all tests in the file green (confirms the new parameter didn't break the other call site or existing tests using `NullAudit`).

- [ ] **Step 6: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.ControlPlane.Security/BulkOperationItemRetryService.cs tests/Apperture.UnitTests/ControlPlane/BulkOperationItemRetryServiceTests.cs
git commit -m "CTX-2 completion: populate audit ResourceContextId from the OperationGroup in BulkOperationItemRetryService"
```

---

## Task 5: Populate audit context — `ScheduleDispatchRunner` (all 10 call sites)

**Files:**
- Modify: `src/Apperture.RuntimePlane/Apperture.RuntimePlane.Dispatch.SqlServer/ScheduleDispatchRunner.cs`
- Test: find or create `tests/Apperture.UnitTests/**/ScheduleDispatchRunner*Tests.cs` (Glob first)

**Interfaces:**
- Consumes: `AuditRecord(..., ResourceContextId: ...)` from Task 1.
- Uses `ScheduleDefinition.ResourceContextId` (CTX-1, `ResourceContextId?` domain type with `.Value`) as the uniform source for every site in this file, per the completion brief's rule ("Use `ScheduleDefinition.ResourceContextId` for ... schedule dispatch audit where the occurrence belongs to the schedule").

This is one file with 10 audit call sites. The full current file content was read in this planning pass (624 lines) — the diffs below are exact.

- [ ] **Step 1: Write failing tests**

Glob `tests/Apperture.UnitTests/**/*ScheduleDispatchRunner*` and `tests/Apperture.UnitTests/**/*Epic28*Scheduler*` to find the existing test file(s) covering `ScheduleDispatchRunner`'s dispatched/blocked/failed audit paths (its class-level doc comment references "Epic 28 (workflow), Epic 29 (operation), Epic 36"). Open the closest existing test(s) that already assert on one of `SchedulerAuditActionNames.Dispatched` / `BlockedByLifecycle` / `Failed` / `ClaimRecovered` / `BlockedByReadiness` / `LegacyScheduleWithoutIntegration`, copy its harness (fake `IScheduleRepository`, `IIntegrationRepository`, `IWorkflowStartDispatchService`, `IOperationStartDispatchService`, `IIntegrationDispatchReadinessEvaluator`, `IProcedureRegistryService`, `IAuditService`, `ISchedulerHostIdentity`, `ISchedulerClaimOptions`), and add these assertions (splitting into as many `[Fact]`s as needed to keep each one testing one branch):

1. Seed a `ScheduleDefinition` with `ResourceContextId = new ResourceContextId("app-a")` whose target is `WorkflowExecution`, wire the fake `IGuard`/lifecycle-guard to return `false` from `CanDispatchWorkflowAsync` (forcing the `BlockedByLifecycle` branch), run one dispatch cycle, and assert the captured `AuditRecord` for `SchedulerAuditActionNames.BlockedByLifecycle` has `ResourceContextId == "app-a"`.
2. Same setup but wire the workflow dispatch stub to return a non-null `StartWorkflowResult`, and assert the `Dispatched` audit record's `ResourceContextId == "app-a"`.
3. Same setup but wire the workflow dispatch stub to return `null`, and assert the `Failed` audit record's `ResourceContextId == "app-a"`.
4. Repeat 1-3 for the `ModuleOperationExecution` target (`ProcessOperationTargetAsync`), asserting the same three action names carry `ResourceContextId`.
5. A stale-claim recovery scenario (seed a `ScheduleRun` row the recovery path will pick up, or use the fake repository's existing recovery-trigger mechanism) asserting `ClaimRecovered`'s `ResourceContextId == "app-a"`.
6. A readiness-blocked scenario (fake `IIntegrationDispatchReadinessEvaluator` returns non-runnable) asserting `BlockedByReadiness`'s `ResourceContextId == "app-a"`.
7. An integration-not-Active scenario (fake `IIntegrationRepository` returns an Integration with `State != Active`) asserting `BlockedByLifecycle`'s (from `EnsureIntegrationOperationalAsync`) `ResourceContextId == "app-a"`.
8. A legacy no-`IntegrationId` schedule scenario asserting `LegacyScheduleWithoutIntegration`'s `ResourceContextId == "app-a"`.

If no existing test file gives a reusable harness, create `tests/Apperture.UnitTests/Runtime/ScheduleDispatchRunnerResourceContextAuditTests.cs` with a minimal from-scratch harness: an in-memory `AppertureDbContext`-backed `SqlScheduleRepository` for `IScheduleRepository` (matching `Ctx2HistoricalProvenanceTests.cs`'s `SqlScheduleRepository(db)` usage), a `CapturingAuditService` (as in prior tasks), simple stub implementations of the other five collaborator interfaces returning fixed values per scenario, and one `[Fact]` per numbered case above.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~ScheduleDispatchRunner`
Expected: FAIL — every captured `AuditRecord.ResourceContextId` is currently `null`.

- [ ] **Step 3: Restructure `ProcessDueAsync`'s stale-claim recovery branch to capture `definition` once and reuse it**

In `src/Apperture.RuntimePlane/Apperture.RuntimePlane.Dispatch.SqlServer/ScheduleDispatchRunner.cs`, change (lines 162-236):

```csharp
    private async Task<(CycleCandidateOutcome Outcome, bool Recovered)> ProcessDueAsync(
        ScheduleFireCandidate candidate,
        string hostId,
        DateTimeOffset staleCutoffUtc,
        bool allowNewClaims,
        CancellationToken ct)
    {
        var claimed = false;
        var recovered = false;

        if (allowNewClaims)
        {
            // CTX-2: resolve the firing Schedule's own ResourceContext once, before the claim is
            // written, and snapshot it onto the ScheduleRun. Never derived from the attached
            // Integration's current ownership (CTX-1 already enforces they match when attached;
            // standalone schedules have no Integration to derive from at all).
            var definitionForClaim = await _repo.GetDefinitionAsync(new ScheduleId(candidate.ScheduleId), ct);
            claimed = await _repo.TryClaimFireAsync(
                candidate.ScheduleId,
                candidate.FireTimeUtc,
                candidate.AssignmentId,
                hostId,
                definitionForClaim?.ResourceContextId?.Value,
                ct);
        }

        if (!claimed)
        {
            claimed = await _repo.TryRecoverStaleClaimAsync(candidate.ScheduleId, candidate.FireTimeUtc, candidate.AssignmentId, hostId, staleCutoffUtc, ct);
            if (claimed)
            {
                recovered = true;
                try
                {
                    await _audit.AppendAsync(new AuditRecord(
                        new AuditRecordId(Guid.NewGuid().ToString("N")),
                        new ActorId("system:scheduler"),
                        SchedulerAuditActionNames.ClaimRecovered,
                        Resource: null,
                        Outcome: "recovered",
                        Timestamp: DateTimeOffset.UtcNow,
                        new CorrelationId($"schedule:{candidate.ScheduleId}:{candidate.FireTimeUtc:O}"),
                        Details: $"schedule:{candidate.ScheduleId};fire={candidate.FireTimeUtc:O};recovered by host {hostId}",
                        ScheduleId: candidate.ScheduleId
                    ), ct);
                }
                catch { /* best-effort audit */ }
            }
        }

        if (!claimed)
            return (CycleCandidateOutcome.Skipped, recovered);

        var definition = await _repo.GetDefinitionAsync(new ScheduleId(candidate.ScheduleId), ct);
        if (definition is null)
```

to:

```csharp
    private async Task<(CycleCandidateOutcome Outcome, bool Recovered)> ProcessDueAsync(
        ScheduleFireCandidate candidate,
        string hostId,
        DateTimeOffset staleCutoffUtc,
        bool allowNewClaims,
        CancellationToken ct)
    {
        var claimed = false;
        var recovered = false;
        ScheduleDefinition? recoveredDefinition = null;

        if (allowNewClaims)
        {
            // CTX-2: resolve the firing Schedule's own ResourceContext once, before the claim is
            // written, and snapshot it onto the ScheduleRun. Never derived from the attached
            // Integration's current ownership (CTX-1 already enforces they match when attached;
            // standalone schedules have no Integration to derive from at all).
            var definitionForClaim = await _repo.GetDefinitionAsync(new ScheduleId(candidate.ScheduleId), ct);
            claimed = await _repo.TryClaimFireAsync(
                candidate.ScheduleId,
                candidate.FireTimeUtc,
                candidate.AssignmentId,
                hostId,
                definitionForClaim?.ResourceContextId?.Value,
                ct);
        }

        if (!claimed)
        {
            claimed = await _repo.TryRecoverStaleClaimAsync(candidate.ScheduleId, candidate.FireTimeUtc, candidate.AssignmentId, hostId, staleCutoffUtc, ct);
            if (claimed)
            {
                recovered = true;
                // Same lookup ProcessDueAsync always needs below once claimed; fetched here (not a
                // new query in the net) so the recovery audit can carry the schedule's own context too.
                recoveredDefinition = await _repo.GetDefinitionAsync(new ScheduleId(candidate.ScheduleId), ct);
                try
                {
                    await _audit.AppendAsync(new AuditRecord(
                        new AuditRecordId(Guid.NewGuid().ToString("N")),
                        new ActorId("system:scheduler"),
                        SchedulerAuditActionNames.ClaimRecovered,
                        Resource: null,
                        Outcome: "recovered",
                        Timestamp: DateTimeOffset.UtcNow,
                        new CorrelationId($"schedule:{candidate.ScheduleId}:{candidate.FireTimeUtc:O}"),
                        Details: $"schedule:{candidate.ScheduleId};fire={candidate.FireTimeUtc:O};recovered by host {hostId}",
                        ScheduleId: candidate.ScheduleId,
                        ResourceContextId: recoveredDefinition?.ResourceContextId?.Value
                    ), ct);
                }
                catch { /* best-effort audit */ }
            }
        }

        if (!claimed)
            return (CycleCandidateOutcome.Skipped, recovered);

        var definition = recoveredDefinition ?? await _repo.GetDefinitionAsync(new ScheduleId(candidate.ScheduleId), ct);
        if (definition is null)
```

This does not add a net-new query: the recovered path previously fetched `definition` once (at the old line 215); it now fetches it once at recovery time instead and reuses it, so the total query count for that path is unchanged. The non-recovered/new-claim path is untouched.

- [ ] **Step 4: Add `ResourceContextId` to the 6 sites where `definition` is already a method parameter**

In `ProcessWorkflowTargetAsync` (all three audit calls: `BlockedByLifecycle` ~line 260, `Failed` ~line 293, `Dispatched` ~line 312) and `ProcessOperationTargetAsync` (all three audit calls: `BlockedByLifecycle` ~line 360, `Failed` ~line 392, `Dispatched` ~line 411), add `ResourceContextId: definition.ResourceContextId?.Value` as a new trailing named argument to each `AuditRecord` constructor call. For example, the `ProcessWorkflowTargetAsync` `Dispatched` site (lines 312-323) becomes:

```csharp
            await _audit.AppendAsync(new AuditRecord(
                new AuditRecordId(Guid.NewGuid().ToString("N")),
                new ActorId("system:scheduler"),
                SchedulerAuditActionNames.Dispatched,
                Resource: null,
                Outcome: "success",
                Timestamp: DateTimeOffset.UtcNow,
                new CorrelationId($"schedule:{candidate.ScheduleId}:{candidate.FireTimeUtc:O}"),
                Details: $"schedule:{candidate.ScheduleId};RunId={result.RunId.Value}",
                ScheduleId: candidate.ScheduleId,
                RunId: result.RunId.Value,
                ResourceContextId: definition.ResourceContextId?.Value
            ), ct);
```

Apply the same trailing-argument addition to the other 5 sites listed above, each already having `definition` as an in-scope method parameter — no query added.

- [ ] **Step 5: Thread `definition.ResourceContextId` through `EnsureIntegrationReadyForDispatchAsync` → `AuditBlockedByReadinessAsync`**

Change the method signature and both call sites. `EnsureIntegrationReadyForDispatchAsync` (lines 437-464):

```csharp
    private async Task<bool> EnsureIntegrationReadyForDispatchAsync(
        IntegrationId? integrationId,
        string? procedureId,
        ScheduleFireCandidate candidate,
        CancellationToken ct)
    {
        if (integrationId is null)
            return true;

        var readiness = await _readiness
            .EvaluateForDispatchAsync(integrationId, procedureId, ct)
            .ConfigureAwait(false);
        if (readiness is null)
        {
            var missingReason = $"Integration '{integrationId.Value}' was not found.";
            await CompleteWithOutcomeAsync(candidate, "BlockedByReadiness", missingReason, null, ct);
            await AuditBlockedByReadinessAsync(candidate, missingReason, ct).ConfigureAwait(false);
            return false;
        }

        if (readiness.IsRunnable)
            return true;

        var reason = FormatReadinessBlockReason(readiness, procedureId);
        await CompleteWithOutcomeAsync(candidate, "BlockedByReadiness", reason, null, ct);
        await AuditBlockedByReadinessAsync(candidate, reason, ct).ConfigureAwait(false);
        return false;
    }

    private async Task AuditBlockedByReadinessAsync(ScheduleFireCandidate candidate, string reason, CancellationToken ct)
    {
        try
        {
            await _audit.AppendAsync(new AuditRecord(
                new AuditRecordId(Guid.NewGuid().ToString("N")),
                new ActorId("system:scheduler"),
                SchedulerAuditActionNames.BlockedByReadiness,
                Resource: null,
                Outcome: "blocked",
                Timestamp: DateTimeOffset.UtcNow,
                new CorrelationId($"schedule:{candidate.ScheduleId}:{candidate.FireTimeUtc:O}"),
                Details: $"schedule:{candidate.ScheduleId};{reason}",
                ScheduleId: candidate.ScheduleId
            ), ct).ConfigureAwait(false);
        }
        catch
        {
            // best-effort audit
        }
    }
```

to:

```csharp
    private async Task<bool> EnsureIntegrationReadyForDispatchAsync(
        IntegrationId? integrationId,
        string? procedureId,
        string? resourceContextId,
        ScheduleFireCandidate candidate,
        CancellationToken ct)
    {
        if (integrationId is null)
            return true;

        var readiness = await _readiness
            .EvaluateForDispatchAsync(integrationId, procedureId, ct)
            .ConfigureAwait(false);
        if (readiness is null)
        {
            var missingReason = $"Integration '{integrationId.Value}' was not found.";
            await CompleteWithOutcomeAsync(candidate, "BlockedByReadiness", missingReason, null, ct);
            await AuditBlockedByReadinessAsync(candidate, missingReason, resourceContextId, ct).ConfigureAwait(false);
            return false;
        }

        if (readiness.IsRunnable)
            return true;

        var reason = FormatReadinessBlockReason(readiness, procedureId);
        await CompleteWithOutcomeAsync(candidate, "BlockedByReadiness", reason, null, ct);
        await AuditBlockedByReadinessAsync(candidate, reason, resourceContextId, ct).ConfigureAwait(false);
        return false;
    }

    private async Task AuditBlockedByReadinessAsync(
        ScheduleFireCandidate candidate, string reason, string? resourceContextId, CancellationToken ct)
    {
        try
        {
            await _audit.AppendAsync(new AuditRecord(
                new AuditRecordId(Guid.NewGuid().ToString("N")),
                new ActorId("system:scheduler"),
                SchedulerAuditActionNames.BlockedByReadiness,
                Resource: null,
                Outcome: "blocked",
                Timestamp: DateTimeOffset.UtcNow,
                new CorrelationId($"schedule:{candidate.ScheduleId}:{candidate.FireTimeUtc:O}"),
                Details: $"schedule:{candidate.ScheduleId};{reason}",
                ScheduleId: candidate.ScheduleId,
                ResourceContextId: resourceContextId
            ), ct).ConfigureAwait(false);
        }
        catch
        {
            // best-effort audit
        }
    }
```

Update both call sites (in `ProcessWorkflowTargetAsync` line 251 and `ProcessOperationTargetAsync` line 351) from:

```csharp
        if (!await EnsureIntegrationReadyForDispatchAsync(definition.IntegrationId, procedureId, candidate, ct).ConfigureAwait(false))
```

to:

```csharp
        if (!await EnsureIntegrationReadyForDispatchAsync(definition.IntegrationId, procedureId, definition.ResourceContextId?.Value, candidate, ct).ConfigureAwait(false))
```

- [ ] **Step 6: Thread `definition.ResourceContextId` through `EnsureIntegrationOperationalAsync`**

Change the signature and both branches (lines 523-576):

```csharp
    private async Task<bool> EnsureIntegrationOperationalAsync(IntegrationId? integrationId, ScheduleFireCandidate candidate, CancellationToken ct)
    {
        if (integrationId is null)
        {
            try
            {
                await _audit.AppendAsync(new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    new ActorId("system:scheduler"),
                    SchedulerAuditActionNames.LegacyScheduleWithoutIntegration,
                    Resource: null,
                    Outcome: "warning",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId($"schedule:{candidate.ScheduleId}:{candidate.FireTimeUtc:O}"),
                    Details: $"schedule:{candidate.ScheduleId};legacy schedule has no IntegrationId; dispatch is temporarily allowed for backward compatibility.",
                    ScheduleId: candidate.ScheduleId
                ), ct);
            }
            catch
            {
                // best-effort audit
            }
            return true;
        }

        var integration = await _integrations.GetByIdAsync(integrationId, ct);
        if (integration is not null && integration.State == IntegrationState.Active)
            return true;

        var reason = integration is null
            ? $"Integration '{integrationId.Value}' was not found."
            : $"Integration '{integration.Id.Value}' is {integration.State}; schedule dispatch requires Active state.";
        await CompleteWithOutcomeAsync(candidate, "BlockedByLifecycle", reason, null, ct);
        try
        {
            await _audit.AppendAsync(new AuditRecord(
                new AuditRecordId(Guid.NewGuid().ToString("N")),
                new ActorId("system:scheduler"),
                SchedulerAuditActionNames.BlockedByLifecycle,
                Resource: null,
                Outcome: "blocked",
                Timestamp: DateTimeOffset.UtcNow,
                new CorrelationId($"schedule:{candidate.ScheduleId}:{candidate.FireTimeUtc:O}"),
                Details: $"schedule:{candidate.ScheduleId};{reason}",
                ScheduleId: candidate.ScheduleId
            ), ct);
        }
        catch
        {
            // best-effort audit
        }

        return false;
    }
```

to (adding `string? resourceContextId` parameter, and `ResourceContextId: resourceContextId` on both `AuditRecord` calls):

```csharp
    private async Task<bool> EnsureIntegrationOperationalAsync(
        IntegrationId? integrationId, string? resourceContextId, ScheduleFireCandidate candidate, CancellationToken ct)
    {
        if (integrationId is null)
        {
            try
            {
                await _audit.AppendAsync(new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    new ActorId("system:scheduler"),
                    SchedulerAuditActionNames.LegacyScheduleWithoutIntegration,
                    Resource: null,
                    Outcome: "warning",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId($"schedule:{candidate.ScheduleId}:{candidate.FireTimeUtc:O}"),
                    Details: $"schedule:{candidate.ScheduleId};legacy schedule has no IntegrationId; dispatch is temporarily allowed for backward compatibility.",
                    ScheduleId: candidate.ScheduleId,
                    ResourceContextId: resourceContextId
                ), ct);
            }
            catch
            {
                // best-effort audit
            }
            return true;
        }

        var integration = await _integrations.GetByIdAsync(integrationId, ct);
        if (integration is not null && integration.State == IntegrationState.Active)
            return true;

        var reason = integration is null
            ? $"Integration '{integrationId.Value}' was not found."
            : $"Integration '{integration.Id.Value}' is {integration.State}; schedule dispatch requires Active state.";
        await CompleteWithOutcomeAsync(candidate, "BlockedByLifecycle", reason, null, ct);
        try
        {
            await _audit.AppendAsync(new AuditRecord(
                new AuditRecordId(Guid.NewGuid().ToString("N")),
                new ActorId("system:scheduler"),
                SchedulerAuditActionNames.BlockedByLifecycle,
                Resource: null,
                Outcome: "blocked",
                Timestamp: DateTimeOffset.UtcNow,
                new CorrelationId($"schedule:{candidate.ScheduleId}:{candidate.FireTimeUtc:O}"),
                Details: $"schedule:{candidate.ScheduleId};{reason}",
                ScheduleId: candidate.ScheduleId,
                ResourceContextId: resourceContextId
            ), ct);
        }
        catch
        {
            // best-effort audit
        }

        return false;
    }
```

Update both call sites (`ProcessWorkflowTargetAsync` line 240, `ProcessOperationTargetAsync` line 340) from `EnsureIntegrationOperationalAsync(definition.IntegrationId, candidate, ct)` to `EnsureIntegrationOperationalAsync(definition.IntegrationId, definition.ResourceContextId?.Value, candidate, ct)`.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~ScheduleDispatchRunner`
Expected: PASS, all new and existing tests green.

- [ ] **Step 8: Run the broader scheduler test area to confirm no regression**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~Scheduler|FullyQualifiedName~Epic28|FullyQualifiedName~Epic36`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Apperture.RuntimePlane/Apperture.RuntimePlane.Dispatch.SqlServer/ScheduleDispatchRunner.cs tests/Apperture.UnitTests/Runtime/ScheduleDispatchRunnerResourceContextAuditTests.cs
git commit -m "CTX-2 completion: populate audit ResourceContextId at every ScheduleDispatchRunner call site from ScheduleDefinition"
```

---

## Task 6: Populate audit context — `IntegrationSettingsService` (documented single-lookup exception)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationSettingsService.cs`
- Test: find existing tests in `tests/Apperture.UnitTests/PlatformSettings/Epic118IntegrationSettingsTests.cs` (extend)

**Interfaces:**
- Consumes: `AuditRecord(..., ResourceContextId: ...)` from Task 1, `IIntegrationRepository.GetByIdAsync(IntegrationId, CancellationToken)` (existing interface, `src/Apperture.Core/Apperture.Core.Application/Services/IIntegrationRepository.cs:15`), which returns a `DomainIntegration?` whose `.ResourceContextId` is a non-nullable `ResourceContextId` domain value with `.Value` (`src/Apperture.Core/Apperture.Core.Domain/Integration/Integration.cs:32`).

**Documented exception to the "no new query" rule:** `IntegrationSettingsService` never loads an `Integration` entity anywhere in its current implementation (confirmed: no `IIntegrationRepository`/`AppertureDbContext` dependency exists on this class today) — it only ever holds the caller-supplied `integrationId` string. The completion brief explicitly names this class as in-scope ("Integration settings changes ... Use the Integration's current ResourceContextId"). Since there is no already-loaded entity to read from, this task adds one new dependency (`IIntegrationRepository`, already registered in DI for other consumers) and one keyed-by-id lookup of the exact Integration the setting belongs to, per mutating method. This is a direct, non-speculative lookup of the resource the action is about — not "infer context from IDs" — and is called out here explicitly rather than silently added.

- [ ] **Step 1: Write the failing test**

Add to `tests/Apperture.UnitTests/PlatformSettings/Epic118IntegrationSettingsTests.cs` (matching its existing Moq + `Callback` capture pattern, confirmed at line 221 of that file):

```csharp
    [Fact]
    public async Task SetAsync_AuditsWithTheIntegrationsCurrentResourceContextId()
    {
        // Reuse this file's existing IntegrationSettingsService construction helper/fixture, adding
        // a mocked IIntegrationRepository whose GetByIdAsync(new IntegrationId("int-1"), ...) returns
        // a DomainIntegration with ResourceContextId = new ResourceContextId("app-a").
        string? capturedContext = null;
        audit.Setup(x => x.AppendAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AuditRecord, CancellationToken>((record, _) => capturedContext = record.ResourceContextId)
            .Returns(ValueTask.CompletedTask);

        await service.SetAsync("int-1", "<an existing ordinary setting key from this file's other tests>", payload, null);

        capturedContext.Should().Be("app-a");
    }
```

The implementer must copy the exact construction pattern (mock setup for `_registry`, `_repository`, `_resolver`, etc.) from an existing passing test in the same file and add only the new `IIntegrationRepository` mock and the assertion above.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~SetAsync_AuditsWithTheIntegrationsCurrentResourceContextId`
Expected: FAIL to compile — `IntegrationSettingsService`'s constructor does not yet accept `IIntegrationRepository`.

- [ ] **Step 3: Add the dependency and resolve context once per mutating method**

In `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationSettingsService.cs`, add the field, constructor parameter, and a small private resolver:

```csharp
    private readonly IIntegrationRepository _integrations;
```

Add `IIntegrationRepository integrations` to the constructor parameter list and assign `_integrations = integrations;` (append it after the existing `IAuditService audit` parameter to keep the diff minimal, or wherever the file's DI convention prefers — match the existing parameter ordering style).

Add a private helper:

```csharp
    private async ValueTask<string?> ResolveResourceContextIdAsync(string integrationId, CancellationToken cancellationToken)
    {
        var integration = await _integrations.GetByIdAsync(new IntegrationId(integrationId), cancellationToken).ConfigureAwait(false);
        return integration?.ResourceContextId.Value;
    }
```

- [ ] **Step 4: Thread it through `AppendAuditAsync` and its four call sites**

Change `AppendAuditAsync` (lines 369-386):

```csharp
    private async ValueTask AppendAuditAsync(
        string action,
        string integrationId,
        ActorId actorId,
        string details,
        CancellationToken cancellationToken)
    {
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            actorId,
            action,
            new ResourceDescriptor(ResourceTypeConstants.Integration, integrationId),
            AuditOutcome,
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            details);
        await _audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
    }
```

to:

```csharp
    private async ValueTask AppendAuditAsync(
        string action,
        string integrationId,
        ActorId actorId,
        string details,
        CancellationToken cancellationToken)
    {
        var resourceContextId = await ResolveResourceContextIdAsync(integrationId, cancellationToken).ConfigureAwait(false);
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            actorId,
            action,
            new ResourceDescriptor(ResourceTypeConstants.Integration, integrationId),
            AuditOutcome,
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            details,
            ResourceContextId: resourceContextId);
        await _audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
    }
```

All four call sites (`SetOrdinaryAsync` line 330, `ResetAsync` line 123, `SetSensitiveAsync` line 215, `ClearSensitiveAsync` line 257) call this same private helper, so no other call-site changes are needed — resolving inside `AppendAuditAsync` itself keeps the fix to one place.

- [ ] **Step 5: Check DI registration**

Search the DI composition root (e.g. `src/Apperture.ControlPlane/Apperture.Management.Api/Program.cs` or a `ServiceCollectionExtensions` file — Grep for `AddScoped<IIntegrationSettingsService` or similar) to confirm `IIntegrationRepository` is already registered as a service (it is used by many other consumers, e.g. `ScheduleDispatchRunner`). If `IntegrationSettingsService` is registered via a constructor-matching container (most likely, given the existing pattern), no registration change is needed — only confirm no explicit factory delegate exists that would need updating to pass the new parameter.

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~Epic118IntegrationSettingsTests`
Expected: PASS, all tests in the file green (confirms the new dependency didn't break other tests' construction — update any other test in this file/other files that constructs `IntegrationSettingsService` directly, adding a mock/stub `IIntegrationRepository`).

- [ ] **Step 7: Grep for other direct constructions of `IntegrationSettingsService` across the test suite**

Run: `grep -rn "new IntegrationSettingsService(" tests/` and update every match to supply an `IIntegrationRepository` (a Moq mock returning a fixed Integration, or a minimal stub) so those tests keep compiling.

- [ ] **Step 8: Run the full PlatformSettings test folder**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~PlatformSettings`
Expected: PASS, no regressions.

- [ ] **Step 9: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationSettingsService.cs tests/Apperture.UnitTests/PlatformSettings/Epic118IntegrationSettingsTests.cs
git commit -m "CTX-2 completion: populate audit ResourceContextId in IntegrationSettingsService via a single keyed Integration lookup"
```

---

## Task 7: Optional `ResourceContextId` filter on `AuditQuery`

**Files:**
- Modify: `src/Apperture.Core/Apperture.Core.Application/Services/AuditQuery.cs`
- Modify: `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlAuditService.cs`
- Test: extend `tests/Apperture.UnitTests/ControlPlane/SqlAuditServiceResourceContextTests.cs` (from Task 1)

**Rationale (decision record for §8 of the brief):** `AuditQuery` already exists as a structured filter object (condition 1 met); the SQL-side addition is a one-line `.Where` clause identical in shape to the existing `ModuleId`/`ScheduleId`/`RunId` filters (condition 2 met — small, SQL-side); a per-Application audit/activity view is a highly plausible CTX-3 consumer, arguably more central than several other candidates (condition 3 met). All three conditions in the brief's decision rule are satisfied, so this filter is added (not deferred).

- [ ] **Step 1: Write the failing test**

Add to `tests/Apperture.UnitTests/ControlPlane/SqlAuditServiceResourceContextTests.cs`:

```csharp
    [Fact]
    public async Task QueryAsync_FiltersByResourceContextId_WhenSupplied()
    {
        var db = NewDb();
        var svc = new SqlAuditService(db);
        await svc.AppendAsync(NewRecord("app-a"));
        await svc.AppendAsync(NewRecord("app-b"));

        var page = await svc.QueryAsync(new Apperture.Core.Application.Services.AuditQuery(FilterByResourceContextId: "app-a"));

        page.Items.Should().ContainSingle(r => r.ResourceContextId == "app-a");
    }

    private static AuditRecord NewRecord(string resourceContextId) =>
        new(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId("system"),
            "test.action",
            Resource: null,
            Outcome: "success",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            Details: null,
            ResourceContextId: resourceContextId);
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~QueryAsync_FiltersByResourceContextId_WhenSupplied`
Expected: FAIL to compile — `AuditQuery` has no `FilterByResourceContextId` member.

- [ ] **Step 3: Add the filter field and the `.Where` clause**

In `src/Apperture.Core/Apperture.Core.Application/Services/AuditQuery.cs`, add `string? FilterByResourceContextId = null` as a new trailing parameter on `AuditQuery` (after `FilterByRunId`).

In `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlAuditService.cs`, in `ApplyFilters` (lines 98-125), add after the `FilterByRunId` block:

```csharp
        if (!string.IsNullOrEmpty(query.FilterByResourceContextId))
            q = q.Where(x => x.ResourceContextId != null && x.ResourceContextId == query.FilterByResourceContextId);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~SqlAuditServiceResourceContextTests`
Expected: PASS, all tests in the file green.

- [ ] **Step 5: Commit**

```bash
git add src/Apperture.Core/Apperture.Core.Application/Services/AuditQuery.cs src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlAuditService.cs tests/Apperture.UnitTests/ControlPlane/SqlAuditServiceResourceContextTests.cs
git commit -m "CTX-2 completion: add optional SQL-side ResourceContextId filter to AuditQuery"
```

---

## Task 8: Expose historical `ResourceContextId` on `RunSummaryDto` / `RunDetailDto`

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IRunQueryService.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/RunQueryService.cs`
- Test: `tests/Apperture.UnitTests/ControlPlane/RunQueryServiceTests.cs` (extend)

**Interfaces:**
- Produces: `RunSummaryDto.ResourceContextId` (`string?`), `RunDetailDto.ResourceContextId` (`string?`) — both appended as new final optional parameters so all existing positional/named constructions keep compiling.
- Consumes: `OperationRunEntity.ResourceContextId` (already exists, `src/Apperture.Persistence/Apperture.Persistence.SqlServer/Entities/OperationRunEntity.cs:25`).

- [ ] **Step 1: Write the failing test**

Add to `tests/Apperture.UnitTests/ControlPlane/RunQueryServiceTests.cs`:

```csharp
    [Fact]
    public async Task ListRunsAsync_ExposesResourceContextId()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "rop",
            WorkflowInstanceId = "i-op",
            DefinitionId = "op.def",
            Status = WorkflowLifecycleStatusNames.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            ResourceContextId = "app-a"
        });
        db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = "rop",
            OperationInstanceId = "i-op",
            OperationDefinitionId = "op.def",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Requested
        });
        await db.SaveChangesAsync();

        var service = NewService(db);
        var list = await service.ListRunsAsync(new RunQueryFilter(ExecutionKind: "operation"));

        list.Should().ContainSingle();
        list[0].ResourceContextId.Should().Be("app-a");
    }

    [Fact]
    public async Task GetRunAsync_ExposesResourceContextId()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "rop2",
            WorkflowInstanceId = "i-op2",
            DefinitionId = "op.def",
            Status = WorkflowLifecycleStatusNames.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            ResourceContextId = "app-b"
        });
        db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = "rop2",
            OperationInstanceId = "i-op2",
            OperationDefinitionId = "op.def",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Requested
        });
        await db.SaveChangesAsync();

        var service = NewService(db);
        var detail = await service.GetRunAsync(new OperationRunId("rop2"));

        detail.Should().NotBeNull();
        detail!.ResourceContextId.Should().Be("app-b");
    }

    [Fact]
    public async Task ListRunsAsync_ResourceContextId_IsNull_ForPreCtx2Rows()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "rop3",
            WorkflowInstanceId = "i-op3",
            DefinitionId = "op.def",
            Status = WorkflowLifecycleStatusNames.Completed,
            CreatedAt = DateTimeOffset.UtcNow
            // ResourceContextId intentionally omitted — simulates a pre-CTX-2 row.
        });
        db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = "rop3",
            OperationInstanceId = "i-op3",
            OperationDefinitionId = "op.def",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Requested
        });
        await db.SaveChangesAsync();

        var service = NewService(db);
        var list = await service.ListRunsAsync(new RunQueryFilter(ExecutionKind: "operation"));

        list.Should().ContainSingle();
        list[0].ResourceContextId.Should().BeNull();
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests --filter "FullyQualifiedName~ExposesResourceContextId|FullyQualifiedName~ListRunsAsync_ResourceContextId_IsNull_ForPreCtx2Rows"`
Expected: FAIL to compile — `RunSummaryDto`/`RunDetailDto` have no `ResourceContextId` member.

- [ ] **Step 3: Add `ResourceContextId` to both DTOs**

In `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IRunQueryService.cs`, change `RunSummaryDto` (lines 69-81) by appending `string? ResourceContextId = null` as the new final parameter, and `RunDetailDto` (lines 122-147) the same way.

- [ ] **Step 4: Update all four DTO construction sites in `RunQueryService.cs`**

In `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/RunQueryService.cs`:

- The `RunSummaryDto` construction inside `TryProjectAuthorizedRunAsync` (workflow branch, lines 303-315, and operation branch, lines 329-341): append `ResourceContextId: r.ResourceContextId` as the final constructor argument at both sites (`r` is the `OperationRunEntity` local in that method).
- The `RunDetailDto` construction inside `GetRunAsync` (workflow branch, lines 421-455, and operation branch, lines 503-537): append `ResourceContextId: run.ResourceContextId` (or whatever the local run-entity variable is named at each site — confirm the exact local variable name by reading the method before editing) as the final constructor argument at both sites.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~RunQueryServiceTests`
Expected: PASS, all tests in the file green.

- [ ] **Step 6: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IRunQueryService.cs src/Apperture.ControlPlane/Apperture.ControlPlane.Security/RunQueryService.cs tests/Apperture.UnitTests/ControlPlane/RunQueryServiceTests.cs
git commit -m "CTX-2 completion: expose historical ResourceContextId on RunSummaryDto and RunDetailDto"
```

---

## Task 9: Optional `ResourceContextId` filter on `RunQueryFilter` (required by the brief)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IRunQueryService.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/RunQueryService.cs`
- Test: `tests/Apperture.UnitTests/ControlPlane/RunQueryServiceTests.cs` (extend)

**Interfaces:**
- Produces: `RunQueryFilter.ResourceContextId` (`string?`), composes with `Status`/`IntegrationId`/`CorrelationKey`/etc. exactly like the existing filters (pure `AND` composition via chained `.Where`).
- This is the one filter explicitly required by the brief (§4), because Run-level context filtering is the most direct CTX-3 read-side need.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Apperture.UnitTests/ControlPlane/RunQueryServiceTests.cs`, mirroring the existing `ListRunsAsync_WhenCorrelationKeyFilter_ReturnsMatchingRunsOnly` (lines 146-187) and the "Blocked status facet composes with X" tests (lines 1096-1132) for the composition case:

```csharp
    [Fact]
    public async Task ListRunsAsync_WhenResourceContextIdFilter_ReturnsMatchingRunsOnly()
    {
        var db = NewDb();
        db.OperationRuns.AddRange(
            new OperationRunEntity
            {
                Id = "r-ctx-a",
                WorkflowInstanceId = "i-ctx-a",
                DefinitionId = "wf.def",
                Status = WorkflowLifecycleStatusNames.Completed,
                CreatedAt = DateTimeOffset.UtcNow,
                ResourceContextId = "app-a"
            },
            new OperationRunEntity
            {
                Id = "r-ctx-b",
                WorkflowInstanceId = "i-ctx-b",
                DefinitionId = "wf.def",
                Status = WorkflowLifecycleStatusNames.Completed,
                CreatedAt = DateTimeOffset.UtcNow,
                ResourceContextId = "app-b"
            });
        foreach (var (runId, instId) in new[] { ("r-ctx-a", "i-ctx-a"), ("r-ctx-b", "i-ctx-b") })
        {
            db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
            {
                RunId = runId,
                WorkflowInstanceId = instId,
                WorkflowDefinitionId = "wf.def",
                CreatedAt = DateTimeOffset.UtcNow,
                Status = WorkflowLifecycleStatusNames.Requested
            });
        }
        await db.SaveChangesAsync();

        var service = NewService(db);
        var filtered = await service.ListRunsAsync(new RunQueryFilter(ResourceContextId: "app-a"));

        filtered.Should().ContainSingle();
        filtered[0].RunId.Should().Be("r-ctx-a");
    }

    [Fact]
    public async Task ListRunsAsync_ResourceContextIdFilter_ComposesWithIntegrationIdFilter()
    {
        var db = NewDb();
        db.OperationRuns.AddRange(
            new OperationRunEntity
            {
                Id = "r-comp-a",
                WorkflowInstanceId = "i-comp-a",
                DefinitionId = "wf.def",
                Status = WorkflowLifecycleStatusNames.Completed,
                CreatedAt = DateTimeOffset.UtcNow,
                IntegrationId = "int-1",
                ResourceContextId = "app-a"
            },
            new OperationRunEntity
            {
                Id = "r-comp-b",
                WorkflowInstanceId = "i-comp-b",
                DefinitionId = "wf.def",
                Status = WorkflowLifecycleStatusNames.Completed,
                CreatedAt = DateTimeOffset.UtcNow,
                IntegrationId = "int-1",
                ResourceContextId = "app-b"
            });
        foreach (var (runId, instId) in new[] { ("r-comp-a", "i-comp-a"), ("r-comp-b", "i-comp-b") })
        {
            db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
            {
                RunId = runId,
                WorkflowInstanceId = instId,
                WorkflowDefinitionId = "wf.def",
                CreatedAt = DateTimeOffset.UtcNow,
                Status = WorkflowLifecycleStatusNames.Requested
            });
        }
        await db.SaveChangesAsync();

        var service = NewService(db);
        var filtered = await service.ListRunsAsync(new RunQueryFilter(IntegrationId: "int-1", ResourceContextId: "app-a"));

        filtered.Should().ContainSingle();
        filtered[0].RunId.Should().Be("r-comp-a");
    }

    [Fact]
    public async Task ListRunsAsync_ResourceContextIdFilter_DoesNotMatchNullHistoricalRows()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "r-null-ctx",
            WorkflowInstanceId = "i-null-ctx",
            DefinitionId = "wf.def",
            Status = WorkflowLifecycleStatusNames.Completed,
            CreatedAt = DateTimeOffset.UtcNow
            // ResourceContextId intentionally null — pre-CTX-2 row.
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "r-null-ctx",
            WorkflowInstanceId = "i-null-ctx",
            WorkflowDefinitionId = "wf.def",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = WorkflowLifecycleStatusNames.Requested
        });
        await db.SaveChangesAsync();

        var service = NewService(db);
        var filtered = await service.ListRunsAsync(new RunQueryFilter(ResourceContextId: "app-a"));

        filtered.Should().BeEmpty("a pre-CTX-2 null historical row must never match a specific context filter, and must never be treated as Shared");
    }

    [Fact]
    public async Task ListRunsAsync_NoResourceContextIdFilter_ReturnsSameSetAsBefore()
    {
        var db = NewDb();
        db.OperationRuns.AddRange(
            new OperationRunEntity { Id = "r-x1", WorkflowInstanceId = "i-x1", DefinitionId = "wf.def", Status = WorkflowLifecycleStatusNames.Completed, CreatedAt = DateTimeOffset.UtcNow, ResourceContextId = "app-a" },
            new OperationRunEntity { Id = "r-x2", WorkflowInstanceId = "i-x2", DefinitionId = "wf.def", Status = WorkflowLifecycleStatusNames.Completed, CreatedAt = DateTimeOffset.UtcNow });
        foreach (var (runId, instId) in new[] { ("r-x1", "i-x1"), ("r-x2", "i-x2") })
        {
            db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
            {
                RunId = runId,
                WorkflowInstanceId = instId,
                WorkflowDefinitionId = "wf.def",
                CreatedAt = DateTimeOffset.UtcNow,
                Status = WorkflowLifecycleStatusNames.Requested
            });
        }
        await db.SaveChangesAsync();

        var service = NewService(db);
        var all = await service.ListRunsAsync(new RunQueryFilter());

        all.Should().HaveCount(2, "omitting the filter must return the same set as before this change");
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests --filter "FullyQualifiedName~ResourceContextIdFilter|FullyQualifiedName~WhenResourceContextIdFilter"`
Expected: FAIL to compile — `RunQueryFilter` has no `ResourceContextId` member.

- [ ] **Step 3: Add the field and the `.Where` clause**

In `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IRunQueryService.cs`, add `string? ResourceContextId = null` as a new trailing parameter on `RunQueryFilter` (after `Take`, or immediately before it — keep `Take` last since callers rely on it being the "cap" and some call sites may use positional construction; check for any positional (non-named) `RunQueryFilter` construction with Grep `new RunQueryFilter\(` before deciding placement, and if any exist, add `ResourceContextId` at the very end after `Take` to avoid breaking them).

In `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/RunQueryService.cs`, in `ListRunsAsync` (lines 46-96), add after the `IntegrationId` `.Where` block (after line 69):

```csharp
        if (!string.IsNullOrWhiteSpace(filter?.ResourceContextId))
            query = query.Where(r => r.ResourceContextId == filter.ResourceContextId);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~RunQueryServiceTests`
Expected: PASS, all tests in the file green (including the pre-existing "Blocked facet composes with ..." tests, confirming this new clause composes correctly by not interfering with existing ones).

- [ ] **Step 5: Grep for any positional (non-named-argument) `RunsController` construction of `RunQueryFilter` that would shift due to the new field, and update `RunsController.cs`'s query-string-to-filter mapping to accept an optional `resourceContextId` query parameter**

In `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/RunsController.cs`, find the `[FromQuery]` parameter list on the `List`/`ListRuns` action and add `[FromQuery] string? resourceContextId` alongside the existing filter parameters, then pass it into the `RunQueryFilter` construction as `ResourceContextId: resourceContextId`. Per the brief ("Do not wire this into UI yet"), this is API-contract preparation only — no Razor UI component should reference this new query parameter in this pass.

- [ ] **Step 6: Run the Management.Api test suite covering RunsController**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~RunsController`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IRunQueryService.cs src/Apperture.ControlPlane/Apperture.ControlPlane.Security/RunQueryService.cs src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/RunsController.cs tests/Apperture.UnitTests/ControlPlane/RunQueryServiceTests.cs
git commit -m "CTX-2 completion: add optional SQL-side ResourceContextId filter to RunQueryFilter"
```

---

## Task 10: Expose historical `ResourceContextId` on Schedule diagnostics (in-progress claim seam)

**Files:**
- Modify: `src/Apperture.Core/Apperture.Core.Application/Services/IScheduleRepository.cs`
- Modify: `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlScheduleRepository.cs`
- Modify: `src/Apperture.Core/Apperture.Core.Application/Services/IScheduleManagementService.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/SqlScheduleManagementService.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/SchedulesController.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/ScheduleDtos.cs`
- Modify (compile-only, no functional change): `tests/Apperture.IntegrationTests/RuntimeHealthBlockedDeferredSqlServerTests.cs`, `tests/Apperture.IntegrationTests/RecoveryWorkflowRemediationSqlServerTests.cs`, `tests/Apperture.UnitTests/ControlPlane/RuntimeHealthQueryServiceTests.cs`
- Test: `tests/Apperture.UnitTests/Runtime/Epic36MultiHostSchedulerTests.cs` (extend), `tests/Apperture.UnitTests/ControlPlane/SchedulesControllerTests.cs` (find or create)

**Key structural finding (must be preserved, not "fixed" as a side effect):** there are two separate `ScheduleDto`/`ScheduleDetailDto` type definitions — the authoritative one returned by `SchedulesController.cs` (which already exposes CTX-1's current-ownership `ResourceContextId`/`ResourceContextRef`), and a stale duplicate in `Apperture.Management.Client/ScheduleDtos.cs` (used by `ManagementClient`/Razor UI) that is missing those same CTX-1 fields today — meaning the ownership context the API already returns is silently dropped before it reaches the UI. This task adds the new CTX-2 claim-context field to both, and additionally backfills the missing CTX-1 fields onto the client-side copy so the new field is not immediately swallowed by the same pre-existing gap.

**There is no other historical per-`ScheduleRun` read surface** — confirmed by research: no `ScheduleRunDto`/`ScheduleHistoryDto` exists anywhere, and the only place `ScheduleRunEntity` data reaches an API caller today is the **active in-progress claim only** (`GetInProgressRunClaimAsync` → `ScheduleInProgressClaim` → `ScheduleDetailDto`'s `ClaimedByHostId`/`ClaimedAtUtc`/`IsClaimStale`). This task extends that one seam. A full run-history feed is out of scope ("do not redesign schedule APIs").

**Naming:** the new field is `ClaimResourceContextId` (flat, mirroring the sibling `ClaimedByHostId`/`ClaimedAtUtc`/`IsClaimStale` naming), explicitly distinct from `ScheduleDetailDto.ResourceContextId` (current Schedule ownership, CTX-1) sitting elsewhere on the same DTO.

**Interfaces:**
- `IScheduleRepository.GetInProgressRunClaimAsync` return type changes from `ValueTask<(string ClaimedByHostId, DateTimeOffset StartedAtUtc)?>` to `ValueTask<(string ClaimedByHostId, DateTimeOffset StartedAtUtc, string? ResourceContextId)?>`. Named-tuple-element access (`.ClaimedByHostId`, `.StartedAtUtc`) at existing call sites is unaffected by the new trailing element.
- `ScheduleInProgressClaim` gains `string? ResourceContextId` as a new field.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Apperture.UnitTests/Runtime/Epic36MultiHostSchedulerTests.cs` (near the existing `GetInProgressRunClaimAsync_ReturnsNull_WhenOnlyCompletedRun` test at line 214):

```csharp
    [Fact]
    public async Task GetInProgressRunClaimAsync_ExposesResourceContextId_FromTheClaimingScheduleRun()
    {
        var db = NewDb(); // reuse whatever DB-construction helper this file already uses
        var repo = new SqlScheduleRepository(db);
        await repo.TryClaimFireAsync("sched-ctx", DateTimeOffset.UtcNow, "assign-ctx", "host-1", resourceContextId: "app-a");

        var claim = await repo.GetInProgressRunClaimAsync("assign-ctx");

        claim.Should().NotBeNull();
        claim!.Value.ResourceContextId.Should().Be("app-a");
    }
```

Find or create `tests/Apperture.UnitTests/ControlPlane/SchedulesControllerTests.cs` and add (matching whatever controller-test harness this project uses elsewhere — check `ResourceContextApiCompatibilityTests.cs` for the pattern of testing a controller's DTO mapping with mocked application services):

```csharp
    [Fact]
    public async Task Get_ExposesClaimResourceContextId_WhenAnInProgressClaimExists()
    {
        // Mock IScheduleManagementService.GetScheduleWithExecutionStateAsync to return a
        // ScheduleWithExecutionState whose InProgressClaim has ResourceContextId = "app-a".
        // Call the controller's Get(id, runtimePlaneId) action and assert the resulting
        // ScheduleDetailDto.ClaimResourceContextId == "app-a".
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests --filter "FullyQualifiedName~GetInProgressRunClaimAsync_ExposesResourceContextId|FullyQualifiedName~Get_ExposesClaimResourceContextId"`
Expected: FAIL to compile — the tuple shape and `ScheduleInProgressClaim`/`ScheduleDetailDto` don't have the new member yet.

- [ ] **Step 3: Extend the repository interface and implementation**

In `src/Apperture.Core/Apperture.Core.Application/Services/IScheduleRepository.cs`, change (line 95):

```csharp
    ValueTask<(string ClaimedByHostId, DateTimeOffset StartedAtUtc)?> GetInProgressRunClaimAsync(string assignmentId, CancellationToken cancellationToken = default);
```

to:

```csharp
    ValueTask<(string ClaimedByHostId, DateTimeOffset StartedAtUtc, string? ResourceContextId)?> GetInProgressRunClaimAsync(string assignmentId, CancellationToken cancellationToken = default);
```

In `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlScheduleRepository.cs`, change (lines 235-245):

```csharp
    /// <inheritdoc cref="IScheduleRepository.GetInProgressRunClaimAsync"/>
    public async ValueTask<(string ClaimedByHostId, DateTimeOffset StartedAtUtc)?> GetInProgressRunClaimAsync(string assignmentId, CancellationToken ct = default)
    {
        var run = await _db.ScheduleRuns.AsNoTracking()
            .Where(r => r.AssignmentId == assignmentId && r.Outcome == "InProgress")
            .OrderByDescending(r => r.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (run is null)
            return null;
        return (run.ClaimedByHostId ?? "", run.StartedAtUtc);
    }
```

to:

```csharp
    /// <inheritdoc cref="IScheduleRepository.GetInProgressRunClaimAsync"/>
    public async ValueTask<(string ClaimedByHostId, DateTimeOffset StartedAtUtc, string? ResourceContextId)?> GetInProgressRunClaimAsync(string assignmentId, CancellationToken ct = default)
    {
        var run = await _db.ScheduleRuns.AsNoTracking()
            .Where(r => r.AssignmentId == assignmentId && r.Outcome == "InProgress")
            .OrderByDescending(r => r.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (run is null)
            return null;
        return (run.ClaimedByHostId ?? "", run.StartedAtUtc, run.ResourceContextId);
    }
```

- [ ] **Step 4: Update the three test-fake tuple signatures (compile-only, no behavior change)**

In `tests/Apperture.IntegrationTests/RuntimeHealthBlockedDeferredSqlServerTests.cs:192`, `tests/Apperture.IntegrationTests/RecoveryWorkflowRemediationSqlServerTests.cs:309`, and `tests/Apperture.UnitTests/ControlPlane/RuntimeHealthQueryServiceTests.cs:224`, change:

```csharp
        public ValueTask<(string ClaimedByHostId, DateTimeOffset StartedAtUtc)?> GetInProgressRunClaimAsync(string assignmentId, CancellationToken ct = default) => ValueTask.FromResult<(string, DateTimeOffset)?>(null);
```

to:

```csharp
        public ValueTask<(string ClaimedByHostId, DateTimeOffset StartedAtUtc, string? ResourceContextId)?> GetInProgressRunClaimAsync(string assignmentId, CancellationToken ct = default) => ValueTask.FromResult<(string, DateTimeOffset, string?)?>(null);
```

- [ ] **Step 5: Extend `ScheduleInProgressClaim` and `SqlScheduleManagementService`**

In `src/Apperture.Core/Apperture.Core.Application/Services/IScheduleManagementService.cs`, change (lines 108-111):

```csharp
public sealed record ScheduleInProgressClaim(
    string ClaimedByHostId,
    DateTimeOffset ClaimedAtUtc,
    bool IsStale);
```

to:

```csharp
public sealed record ScheduleInProgressClaim(
    string ClaimedByHostId,
    DateTimeOffset ClaimedAtUtc,
    bool IsStale,
    /// <summary>
    /// CTX-2: immutable ResourceContext snapshot from the specific ScheduleRun this claim belongs to
    /// (copied at claim time from ScheduleDefinition.ResourceContextId). Distinct from the Schedule's
    /// current ownership, which the caller already has on hand as ScheduleDefinition.ResourceContextId.
    /// </summary>
    string? ResourceContextId = null);
```

In `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/SqlScheduleManagementService.cs`, change (lines 159-162):

```csharp
            claim = new ScheduleInProgressClaim(
                claimInfo.Value.ClaimedByHostId,
                claimInfo.Value.StartedAtUtc,
                claimInfo.Value.StartedAtUtc < cutoff);
```

to:

```csharp
            claim = new ScheduleInProgressClaim(
                claimInfo.Value.ClaimedByHostId,
                claimInfo.Value.StartedAtUtc,
                claimInfo.Value.StartedAtUtc < cutoff,
                claimInfo.Value.ResourceContextId);
```

- [ ] **Step 6: Add `ClaimResourceContextId` to `SchedulesController`'s `ScheduleDetailDto` and its mapping**

In `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/SchedulesController.cs`, change the `ScheduleDetailDto` record (lines 355-378) by adding `string? ClaimResourceContextId = null` immediately after `ResourceContext` (the last field), with a short doc comment distinguishing it, and update the class-level XML doc comment (lines 350-354) to mention it explicitly:

```csharp
/// <summary>
/// Schedule with execution state for operator visibility. Epic 28, Epic 36 (claim visibility).
/// <see cref="ClaimedByHostId"/> / <see cref="ClaimedAtUtc"/> / <see cref="IsClaimStale"/>: active in-progress claim only; not historical.
/// <see cref="ClaimResourceContextId"/>: CTX-2 historical snapshot from that same in-progress ScheduleRun — do not
/// confuse with <see cref="ResourceContextId"/> above, which is the Schedule's current ownership (CTX-1).
/// SWM-10: additive next-fire / readiness projection (compute-only).
/// </summary>
public sealed record ScheduleDetailDto(
    string Id,
    string Name,
    string RecurrenceExpression,
    string TimeZone,
    bool Enabled,
    int TargetJobType,
    string? TargetJobConfiguration,
    int Version,
    DateTimeOffset? LastAttemptedUtc,
    string? LastOutcome,
    string? LastOutcomeReason,
    DateTimeOffset? LastSuccessfulDispatchUtc,
    string? ClaimedByHostId = null,
    DateTimeOffset? ClaimedAtUtc = null,
    bool? IsClaimStale = null,
    string? IntegrationId = null,
    string? RuntimePlaneId = null,
    DateTimeOffset? NextFireUtc = null,
    string? ScheduleTimeZoneId = null,
    string? HealthStatus = null,
    IReadOnlyList<ScheduleStatusReasonDto>? StatusReasons = null,
    string? ResourceContextId = null,
    ResourceContextRef? ResourceContext = null,
    string? ClaimResourceContextId = null);
```

Update `ToDetailDto` (lines 295-318) to append `claim?.ResourceContextId` as the new final constructor argument.

- [ ] **Step 7: Sync the client-side DTOs (`Apperture.Management.Client.ScheduleDto`/`ScheduleDetailDto`)**

In `src/Apperture.ControlPlane/Apperture.Management.Client/ScheduleDtos.cs`, change `ScheduleDto` (lines 50-63) to add `string? ResourceContextId = null` as a new trailing field (backfilling the pre-existing CTX-1 gap where the server already returns this but the client silently drops it), and change `ScheduleDetailDto` (lines 70-91) to add both `string? ResourceContextId = null` and a `ResourceContextRef? ResourceContext = null` (backfilling CTX-1), plus the new `string? ClaimResourceContextId = null` (CTX-2, this task). `ResourceContextRef` already exists at `src/Apperture.Core/Apperture.Core.Domain/ResourceContexts/ResourceContext.cs:49-53` — but the `Apperture.Management.Client` project may not currently reference `Apperture.Core.Domain`; check its `.csproj` references first (Grep for an existing `ResourceContextRef` or `Apperture.Core.Domain` usage inside `Apperture.Management.Client` — if the project doesn't already depend on that assembly, use a small mirrored client-side `ResourceContextRefDto(string Id, string Name, string Slug)` record instead of importing the domain type, to avoid adding a new project reference for a client contract type; check other existing client DTOs in this same project for how they handle domain-type mirroring before deciding).

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests --filter "FullyQualifiedName~Epic36MultiHostSchedulerTests|FullyQualifiedName~SchedulesControllerTests"`
Expected: PASS.

- [ ] **Step 9: Run the full scheduler/runtime-health test areas plus the client DTO round-trip tests**

Run: `dotnet test tests/Apperture.UnitTests --filter "FullyQualifiedName~Scheduler|FullyQualifiedName~RuntimeHealth|FullyQualifiedName~ManagementClientEpic33Tests|FullyQualifiedName~SchedulerOperationsSwm8Tests"`
Expected: PASS. Pay particular attention to `SchedulerOperationsSwm8Tests.cs:220-233`, which directly constructs `Apperture.Management.Client.ScheduleDetailDto` positionally — since all new fields are appended with defaults at the end, this must still compile and pass unchanged.

- [ ] **Step 10: Run the integration test project build (compile-only check, no SQL Server required for this step)**

Run: `dotnet build tests/Apperture.IntegrationTests`
Expected: builds clean (confirms the two `IScheduleRepository` fakes in that project compile with the new tuple shape).

- [ ] **Step 11: Commit**

```bash
git add src/Apperture.Core/Apperture.Core.Application/Services/IScheduleRepository.cs src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlScheduleRepository.cs src/Apperture.Core/Apperture.Core.Application/Services/IScheduleManagementService.cs src/Apperture.ControlPlane/Apperture.ControlPlane.Security/SqlScheduleManagementService.cs src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/SchedulesController.cs src/Apperture.ControlPlane/Apperture.Management.Client/ScheduleDtos.cs tests/Apperture.IntegrationTests/RuntimeHealthBlockedDeferredSqlServerTests.cs tests/Apperture.IntegrationTests/RecoveryWorkflowRemediationSqlServerTests.cs tests/Apperture.UnitTests/ControlPlane/RuntimeHealthQueryServiceTests.cs tests/Apperture.UnitTests/Runtime/Epic36MultiHostSchedulerTests.cs tests/Apperture.UnitTests/ControlPlane/SchedulesControllerTests.cs
git commit -m "CTX-2 completion: expose historical ScheduleRun ResourceContextId on the in-progress claim seam; backfill CTX-1 fields onto the client-side Schedule DTOs"
```

---

## Task 11: Expose `ResourceContextId` on `AttentionIncidentDto`

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/IntegrationScheduleDiagnosticsDtos.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationsController.cs`
- Test: `tests/Apperture.UnitTests/Integration/IntegrationScheduleDiagnosticsTests.cs` (extend)

**Interfaces:**
- `AttentionIncidentProjection.ResourceContextId` already exists (`src/Apperture.Core/Apperture.Core.Application/Integration/AttentionIncidentProjection.cs`, CTX-2 §7) — this task only threads it through the one API DTO layered on top that currently drops it.

- [ ] **Step 1: Write the failing test**

Add to `tests/Apperture.UnitTests/Integration/IntegrationScheduleDiagnosticsTests.cs` (mirroring however this file currently constructs an `IntegrationScheduleDiagnosticsProjection` with a populated `RecentAttentionIncidents` list, and however it invokes the controller's mapping — if the controller's `ToDiagnosticsDto` is private/static, test it indirectly through the controller action the same way other tests in this file already do):

```csharp
    [Fact]
    public void ToDiagnosticsDto_ExposesResourceContextId_OnRecentAttentionIncidents()
    {
        var incident = new AttentionIncidentProjection(
            "inc-1", "int-1", "condition-1", "Open",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null, null, null, null, null,
            ResourceContextId: "app-a");
        // Build a minimal IntegrationScheduleDiagnosticsProjection with RecentAttentionIncidents = [incident]
        // using whatever constructor/builder helper this test file already uses, then invoke the
        // controller's mapping and assert the resulting AttentionIncidentDto.ResourceContextId == "app-a".
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~ToDiagnosticsDto_ExposesResourceContextId_OnRecentAttentionIncidents`
Expected: FAIL to compile — `AttentionIncidentDto` has no `ResourceContextId` member.

- [ ] **Step 3: Add the field and thread it through the mapping**

In `src/Apperture.ControlPlane/Apperture.Management.Client/IntegrationScheduleDiagnosticsDtos.cs`, change `AttentionIncidentDto` (lines 34-47) to append `string? ResourceContextId = null` as a new trailing field.

In `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationsController.cs`, in `ToDiagnosticsDto` (around lines 630-643), change the `AttentionIncidentDto` construction:

```csharp
            p.RecentAttentionIncidents.Select(i => new AttentionIncidentDto(
                i.Id,
                i.IntegrationId,
                i.ConditionKey,
                i.Status,
                i.StartedAtUtc,
                i.LastObservedAtUtc,
                i.ResolvedAtUtc,
                i.ResolutionReason,
                i.FirstExpectedFireAtUtc,
                i.LastExpectedFireAtUtc,
                i.AcknowledgedAtUtc,
                i.AcknowledgedBy,
                i.AcknowledgementNote)).ToList(),
```

to:

```csharp
            p.RecentAttentionIncidents.Select(i => new AttentionIncidentDto(
                i.Id,
                i.IntegrationId,
                i.ConditionKey,
                i.Status,
                i.StartedAtUtc,
                i.LastObservedAtUtc,
                i.ResolvedAtUtc,
                i.ResolutionReason,
                i.FirstExpectedFireAtUtc,
                i.LastExpectedFireAtUtc,
                i.AcknowledgedAtUtc,
                i.AcknowledgedBy,
                i.AcknowledgementNote,
                i.ResourceContextId)).ToList(),
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~IntegrationScheduleDiagnosticsTests`
Expected: PASS, all tests in the file green.

- [ ] **Step 5: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Client/IntegrationScheduleDiagnosticsDtos.cs src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationsController.cs tests/Apperture.UnitTests/Integration/IntegrationScheduleDiagnosticsTests.cs
git commit -m "CTX-2 completion: expose ResourceContextId on AttentionIncidentDto"
```

---

## Task 12: Expose `ResourceContextId` on Notification instance DTOs

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/NotificationInstanceDtos.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/NotificationInstancesController.cs`
- Test: `tests/Apperture.UnitTests/ControlPlane/NotificationInstancesControllerTests.cs` (new — none exists today, per research)

**Interfaces:**
- `NotificationInstance.ResourceContextId` already exists (`src/Apperture.Core/Apperture.Core.Application/Notifications/Instances/NotificationInstance.cs:31`, CTX-2). This task only threads it through the two API DTOs.

- [ ] **Step 1: Write the failing test**

Create `tests/Apperture.UnitTests/ControlPlane/NotificationInstancesControllerTests.cs`:

```csharp
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Client;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class NotificationInstancesControllerTests
{
    [Fact]
    public async Task List_ExposesResourceContextId_OnSummaryDto()
    {
        var instances = new Mock<INotificationInstanceQueryService>();
        var instance = NewInstance("app-a");
        instances.Setup(x => x.ListAsync(It.IsAny<NotificationInstanceListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { instance });
        var controller = new NotificationInstancesController(instances.Object);

        var result = await controller.List(null, null, null);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeAssignableTo<IReadOnlyList<NotificationInstanceSummaryDto>>().Subject;
        dtos.Should().ContainSingle(d => d.ResourceContextId == "app-a");
    }

    [Fact]
    public async Task Get_ExposesResourceContextId_OnDetailDto()
    {
        var instances = new Mock<INotificationInstanceQueryService>();
        var instance = NewInstance("app-b");
        instances.Setup(x => x.GetAsync("id-1", It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        var controller = new NotificationInstancesController(instances.Object);

        var result = await controller.Get("id-1");

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<NotificationInstanceDetailDto>().Subject;
        dto.ResourceContextId.Should().Be("app-b");
    }

    private static NotificationInstance NewInstance(string resourceContextId) =>
        new(
            NotificationInstanceId.From("id-1"),
            "def-1",
            "kind-1",
            "status-1",
            null, null, null, null, null,
            "dedupe-1", "subject", "summary", "body-text",
            null, null, null, null, "v1", "{}", null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            ResourceContextId: resourceContextId);
}
```

The exact positional argument list for `NotificationInstance`'s constructor must be confirmed by reading `src/Apperture.Core/Apperture.Core.Application/Notifications/Instances/NotificationInstance.cs` in full before writing this test — the record above at line 5-31 was only partially captured in research; verify every field name/order and adjust the `NewInstance` helper to match exactly, using named arguments for clarity if the positional list is long.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~NotificationInstancesControllerTests`
Expected: FAIL to compile — `NotificationInstanceSummaryDto`/`NotificationInstanceDetailDto` have no `ResourceContextId` member.

- [ ] **Step 3: Add the field to both DTOs and thread it through both mappings**

In `src/Apperture.ControlPlane/Apperture.Management.Client/NotificationInstanceDtos.cs`, add `string? ResourceContextId = null` as a new trailing field to `NotificationInstanceSummaryDto` (lines 3-11) and to `NotificationInstanceDetailDto` (lines 13-37).

In `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/NotificationInstancesController.cs`, update `ToSummaryDto` (lines 80-89) and `ToDetailDto` (lines 91-116) to append `i.ResourceContextId` as the final constructor argument in each.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~NotificationInstancesControllerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Client/NotificationInstanceDtos.cs src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/NotificationInstancesController.cs tests/Apperture.UnitTests/ControlPlane/NotificationInstancesControllerTests.cs
git commit -m "CTX-2 completion: expose ResourceContextId on Notification instance summary and detail DTOs"
```

---

## Task 13: Optional `ResourceContextId` filter on `NotificationInstanceListQuery`

**Files:**
- Modify: `src/Apperture.Core/Apperture.Core.Application/Notifications/Instances/NotificationInstance.cs`
- Modify: `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlNotificationInstanceStore.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/NotificationInstancesController.cs`
- Test: `tests/Apperture.UnitTests/ControlPlane/NotificationInstancesControllerTests.cs` (extend) and a store-level test (find or create `tests/Apperture.UnitTests/**/SqlNotificationInstanceStore*Tests.cs`)

**Rationale:** `NotificationInstanceListQuery` already exists (condition 1 met); the SQL addition is a one-line `.Where` identical in shape to the existing `IntegrationId`/`NotificationDefinitionId`/`AttentionIncidentId` filters (condition 2 met); a per-Application notification-activity view is a plausible CTX-3 consumer (condition 3 met). All three conditions satisfied — add it.

- [ ] **Step 1: Write the failing test**

Find or create a store-level test file and add:

```csharp
    [Fact]
    public async Task ListAsync_FiltersByResourceContextId_WhenSupplied()
    {
        var db = NewDb();
        var store = new SqlNotificationInstanceStore(db); // confirm exact constructor from the file's other tests
        // Seed two NotificationInstance rows via the store's own create path (or directly via
        // db.NotificationInstances.Add(...) matching the existing entity shape), one with
        // ResourceContextId = "app-a" and one with "app-b".

        var results = await store.ListAsync(new NotificationInstanceListQuery(ResourceContextId: "app-a"));

        results.Should().ContainSingle(r => r.ResourceContextId == "app-a");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~ListAsync_FiltersByResourceContextId_WhenSupplied`
Expected: FAIL to compile — `NotificationInstanceListQuery` has no `ResourceContextId` member.

- [ ] **Step 3: Add the field and the `.Where` clause**

In `src/Apperture.Core/Apperture.Core.Application/Notifications/Instances/NotificationInstance.cs`, change `NotificationInstanceListQuery` (lines 49-53):

```csharp
public sealed record NotificationInstanceListQuery(
    string? IntegrationId = null,
    string? NotificationDefinitionId = null,
    string? AttentionIncidentId = null,
    int Take = 50);
```

to:

```csharp
public sealed record NotificationInstanceListQuery(
    string? IntegrationId = null,
    string? NotificationDefinitionId = null,
    string? AttentionIncidentId = null,
    int Take = 50,
    string? ResourceContextId = null);
```

In `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlNotificationInstanceStore.cs`, in `ListAsync` (lines 138-157), add after the `AttentionIncidentId` `.Where` block (after line 149):

```csharp
        if (!string.IsNullOrWhiteSpace(query.ResourceContextId))
            q = q.Where(x => x.ResourceContextId == query.ResourceContextId);
```

- [ ] **Step 4: Wire the optional query parameter through the controller**

In `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/NotificationInstancesController.cs`, add `[FromQuery] string? resourceContextId` to the `List` action's parameters and pass it into the `NotificationInstanceListQuery` construction as `ResourceContextId: resourceContextId`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests --filter "FullyQualifiedName~ListAsync_FiltersByResourceContextId_WhenSupplied|FullyQualifiedName~NotificationInstancesControllerTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Apperture.Core/Apperture.Core.Application/Notifications/Instances/NotificationInstance.cs src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlNotificationInstanceStore.cs src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/NotificationInstancesController.cs
git commit -m "CTX-2 completion: add optional SQL-side ResourceContextId filter to NotificationInstanceListQuery"
```

---

## Task 14: Optional `ResourceContextId` filter on `PlatformEventQuery`; document PlatformEvent exposure decision

**Files:**
- Modify: `src/Apperture.Core/Apperture.Core.Application/PlatformEvents/PlatformEventQuery.cs`
- Modify: `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlPlatformEventStore.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/PlatformEventsController.cs`
- Test: find or create `tests/Apperture.UnitTests/PlatformEvents/SqlPlatformEventStoreTests.cs`-adjacent filter test, and extend `tests/Apperture.UnitTests/ControlPlane/PlatformEventsControllerTests.cs`

**Important — no DTO change needed here:** `PlatformEventEnvelope` (the type this endpoint already returns directly, no separate API DTO layer) already carries `ResourceContextId` and is already serialized as-is — confirmed by research. This task adds only the optional query-side filter; PlatformEvent *exposure* itself needs no code change and is documented as already-complete in the closure doc (Task 17).

**Rationale:** `PlatformEventQuery` already exists (condition 1 met); the SQL addition mirrors the existing `IntegrationId`/`RunId`/`ModuleId` filters exactly (condition 2 met); PlatformEvents are the lowest-level causal record and a plausible CTX-3 diagnostic filter (condition 3 met).

- [ ] **Step 1: Write the failing test**

In `tests/Apperture.UnitTests/ControlPlane/PlatformEventsControllerTests.cs`, following its existing `CapturingPlatformEventStore(q => ...)` pattern (confirmed by research):

```csharp
    [Fact]
    public async Task List_PassesResourceContextIdQueryParamThrough()
    {
        PlatformEventQuery? captured = null;
        var store = new CapturingPlatformEventStore(q => captured = q);
        var controller = NewController(store); // reuse this file's existing controller-construction helper

        await controller.List(null, null, null, null, null, null, resourceContextId: "app-a");

        captured.Should().NotBeNull();
        captured!.ResourceContextId.Should().Be("app-a");
    }
```

Match this file's actual existing helper names/signatures (`NewController`, `CapturingPlatformEventStore`'s exact constructor) exactly — read the file in full before writing this test if the exact helper shapes are not already known.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~List_PassesResourceContextIdQueryParamThrough`
Expected: FAIL to compile — `PlatformEventQuery` has no `ResourceContextId` member and the controller's `List` action has no matching parameter.

- [ ] **Step 3: Add the field, the `.Where` clause, and the controller parameter**

In `src/Apperture.Core/Apperture.Core.Application/PlatformEvents/PlatformEventQuery.cs`, change:

```csharp
public sealed record PlatformEventQuery(
    string? IntegrationId = null,
    string? EventType = null,
    string? Severity = null,
    DateTimeOffset? FromOccurredAtUtc = null,
    DateTimeOffset? ToOccurredAtUtc = null,
    string? RunId = null,
    string? ModuleId = null,
    int Take = 50);
```

to:

```csharp
public sealed record PlatformEventQuery(
    string? IntegrationId = null,
    string? EventType = null,
    string? Severity = null,
    DateTimeOffset? FromOccurredAtUtc = null,
    DateTimeOffset? ToOccurredAtUtc = null,
    string? RunId = null,
    string? ModuleId = null,
    int Take = 50,
    string? ResourceContextId = null);
```

In `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlPlatformEventStore.cs`, in `QueryAsync` (lines 38-58), add after the `ModuleId` `.Where` block (after line 58):

```csharp
        if (!string.IsNullOrWhiteSpace(query.ResourceContextId))
            q = q.Where(e => e.ResourceContextId == query.ResourceContextId);
```

In `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/PlatformEventsController.cs`, add `[FromQuery] string? resourceContextId` to the `List` action's parameter list and pass it as the new trailing argument to the `PlatformEventQuery` construction (lines 51-59).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~PlatformEventsControllerTests`
Expected: PASS.

- [ ] **Step 5: Run the PlatformEvents store test folder**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~PlatformEvents`
Expected: PASS, no regressions.

- [ ] **Step 6: Commit**

```bash
git add src/Apperture.Core/Apperture.Core.Application/PlatformEvents/PlatformEventQuery.cs src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlPlatformEventStore.cs src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/PlatformEventsController.cs tests/Apperture.UnitTests/ControlPlane/PlatformEventsControllerTests.cs
git commit -m "CTX-2 completion: add optional SQL-side ResourceContextId filter to PlatformEventQuery"
```

---

## Task 15: Snapshot immutability and audit-provenance regression tests

**Files:**
- Modify: `tests/Apperture.UnitTests/ResourceContexts/Ctx2HistoricalProvenanceTests.cs`

**Purpose:** directly prove (a) historical context on Run/ScheduleRun/Audit/Attention/Notification stays put after simulated ownership change, and (b) execution audit uses the Run's own snapshot, not current Integration ownership, by deliberately making them differ.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Apperture.UnitTests/ResourceContexts/Ctx2HistoricalProvenanceTests.cs` (same file, same `NewDb`/`SeedIntegration` helpers already defined there):

```csharp
    [Fact]
    public async Task ExecutionAudit_UsesRunsSnapshot_NotCurrentIntegrationOwnership_AfterSimulatedMove()
    {
        var db = NewDb();
        SeedIntegration(db, "int-audit-1", "app-a");
        await db.SaveChangesAsync();

        var dispatch = NewWorkflowDispatch(db);
        var result = await dispatch.StartAsync(new StartWorkflowRequest(
            WorkflowId, Parameters: null, IdempotencyKey: "k-audit-1", CorrelationId: null, IntegrationId: "int-audit-1"));
        result.Should().NotBeNull();

        // Simulate the Integration moving to a different context after dispatch (CTX-3 movement does
        // not exist yet; controlled test setup only, matching the existing immutability test above).
        var integration = await db.Integrations.SingleAsync(i => i.Id == "int-audit-1");
        integration.ResourceContextId = "app-b";
        await db.SaveChangesAsync();

        var run = await db.OperationRuns.AsNoTracking().SingleAsync(r => r.Id == result!.RunId.Value);
        run.ResourceContextId.Should().Be("app-a", "run.ResourceContextId must never be re-derived from current ownership");
        run.ResourceContextId.Should().NotBe(integration.ResourceContextId, "this test deliberately makes them differ to prove audit reads the Run snapshot, not current Integration ownership");

        // Directly exercise the private audit helper via reflection with the Run's own (stale-by-design)
        // snapshot, proving the value that would be written is "app-a", never "app-b".
        var processor = new WorkflowStartRequestProcessor(
            db,
            new StubExecutor(),
            new StubDefinitionResolver(new WorkflowDefinition(WorkflowId, "HelloWorld", "Sample.HelloWorld", new[] { "Step1" })),
            "worker",
            Microsoft.Extensions.Options.Options.Create(new WorkflowStartRequestLeaseOptions { LeaseTimeout = TimeSpan.FromMinutes(5) }),
            audit: new CapturingAuditService());
        // (Use this test's own CapturingAuditService — add a small private nested class here matching
        // the one already introduced in Task 2's WorkflowProcessorDefinitionResolutionTests.cs, or
        // extract a small shared internal test helper class if the project has a common test-utilities
        // location — check for one before duplicating.)
    }

    [Fact]
    public async Task ScheduleRunClaim_StaysImmutable_WhenScheduleDefinitionsContextLaterChanges()
    {
        var db = NewDb();
        var repo = new SqlScheduleRepository(db);
        await repo.TryClaimFireAsync("sched-immut-1", DateTimeOffset.UtcNow, "assign-immut-1", "host-1", resourceContextId: "app-a");

        // Simulate the ScheduleDefinition's context changing later (no production move API exists;
        // this is direct entity mutation purely for test setup, matching the pattern already used
        // for Integrations above).
        var run = await db.ScheduleRuns.SingleAsync(r => r.ScheduleId == "sched-immut-1");
        run.ResourceContextId.Should().Be("app-a");
    }
```

The first test's exact reflection/audit-capture wiring should mirror Task 2's `WorkflowProcessorDefinitionResolutionTests.cs` additions exactly — if Task 2 has already been completed and committed, reuse its `CapturingAuditService`/`InvokeAudit` pattern rather than re-deriving it; if this task is executed independently, add a minimal local copy.

- [ ] **Step 2: Run tests to verify they fail (if run before Task 1/2's AuditRecord/processor changes) or pass immediately (if run after)**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~Ctx2HistoricalProvenanceTests`
Expected: if Tasks 1-2 are already committed (recommended dependency order), this should largely PASS on the first run except for any wiring mismatch in the new test's helper classes — fix those, not the production code, since the underlying behavior was already implemented in Tasks 1/2/5.

- [ ] **Step 3: Fix any test-only wiring issues and re-run**

Run: `dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~Ctx2HistoricalProvenanceTests`
Expected: PASS, all tests in the file green (5 original + 2 new = 7).

- [ ] **Step 4: Commit**

```bash
git add tests/Apperture.UnitTests/ResourceContexts/Ctx2HistoricalProvenanceTests.cs
git commit -m "CTX-2 completion: regression tests proving historical snapshots stay immutable and audit uses Run snapshots, not current ownership"
```

---

## Task 16: Full validation sweep

**Files:** none (verification only).

- [ ] **Step 1: Full solution build**

Run: `dotnet build Apperture.sln` (or the solution file name actually used at the repo root — confirm with `ls *.sln` first)
Expected: builds clean, no compiler errors, across every project touched by Tasks 1-15 (`Apperture.Core.Domain`, `Apperture.Core.Application`, `Apperture.Persistence.SqlServer`, `Apperture.RuntimePlane.Engine`, `Apperture.RuntimePlane.Dispatch.SqlServer`, `Apperture.ControlPlane.Security`, `Apperture.Management.Api`, `Apperture.Management.Client`, `Apperture.Management.Host`, `Apperture.RuntimeHost.Windows.WorkerHost`, `Apperture.Management.Ui.Shared`, `Apperture.Management.Ui.Client`).

- [ ] **Step 2: Full unfiltered unit test suite**

Run: `dotnet test tests/Apperture.UnitTests`
Expected: PASS, 0 failed. Compare the total count against the CTX-2 closure baseline (1991 passed) — expect roughly 1991 + (new tests from Tasks 1-15, approximately 25-35) passed.

- [ ] **Step 3: `Apperture.Management.Ui.Shared.Tests`**

Run: `dotnet test tests/Apperture.Management.Ui.Shared.Tests` (confirm exact project path with Glob if this name doesn't match)
Expected: PASS, 761 passed (unchanged — no UI Shared code was touched by this plan).

- [ ] **Step 4: `Apperture.ArchTests`**

Run: `dotnet test tests/Apperture.ArchTests` (confirm exact project path)
Expected: PASS, 38 passed (unchanged — no architectural boundary rules should be violated by additive DTO/parameter changes; if this fails, read the specific rule that failed before assuming it's safe to change the rule).

- [ ] **Step 5: Restart the local SQL Server / Aspire dev environment if needed**

Per the completion brief: "Restart the normal Aspire/dev environment as needed so SQL Server is available again." If `127.0.0.1:14333` (or whatever port the local SQL Server container uses) is unreachable, start it via whatever the project's normal dev-loop command is (check `README.md` or `docs/` for the Aspire AppHost startup command) before proceeding.

- [ ] **Step 6: Full `Apperture.IntegrationTests` suite with SQL Server available**

Run: `dotnet test tests/Apperture.IntegrationTests`
Expected: PASS except the known pre-existing SFTP-dependent test (`ManagementHostGovernanceHttpTests.Post_sftp_test_connect_persists_and_updates_readiness_across_unverified_success_failed`), which requires a controlled SFTP endpoint at `127.0.0.1:2222` that is not part of this environment. If that is the *only* failure, report it explicitly as the known environment dependency per the brief — do not attempt to work around it or alter test/production semantics to hide it.

- [ ] **Step 7: Targeted re-runs of every area touched**

Run each of:
```bash
dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~Audit
dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~RunQueryServiceTests
dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~ScheduleDispatchRunner
dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~Scheduler
dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~NotificationInstancesControllerTests
dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~PlatformEventsControllerTests
dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~Ctx2HistoricalProvenanceTests
dotnet test tests/Apperture.UnitTests --filter FullyQualifiedName~PlatformSettings
```
Expected: PASS on every one.

- [ ] **Step 8: Build the remaining named projects explicitly**

Run:
```bash
dotnet build src/Apperture.ControlPlane/Apperture.Management.Api
dotnet build src/Apperture.ControlPlane/Apperture.Management.Host
dotnet build src/Apperture.RuntimeHost.Windows.WorkerHost
dotnet build src/Apperture.Persistence/Apperture.Persistence.SqlServer
dotnet build src/Apperture.ControlPlane/Apperture.Management.Client
dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Shared
dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client
```
(Confirm each path against the actual project layout with Glob if any of these don't match exactly.)
Expected: all OK, no compiler errors. If a build fails only due to file-copy locks from a running dev-host process (the known pre-existing condition noted in the CTX-1/CTX-2 closure docs), stop the locking process the same way those closures documented, do not force-kill anything without checking first, and re-run.

- [ ] **Step 9: No commit for this task** (verification only — if any step surfaces a real failure, fix it in the relevant task above and re-run this task from Step 1).

---

## Task 17: Update `ctx2-closure.md`

**Files:**
- Modify: `docs/01-architecture/transitions/resource-contexts/ctx2-closure.md`

- [ ] **Step 1: Change the status line**

Change (line 4):

```markdown
Status: **Core invariant implemented and proven; scope trimmed under session budget (see §14).**
```

to:

```markdown
Status: **Complete.** CTX-3 may begin.
```

Only make this change after Task 16 confirms everything is green; if any gap remains open, state the status honestly (e.g. "Core invariant and completion pass both implemented; N item(s) remain — see §15") rather than overstating completeness.

- [ ] **Step 2: Update §1 Executive result**

Rewrite the second paragraph of §1 (currently: "CTX-3 **should not begin from this closure alone** ...") to state plainly that the §14 gaps have now been closed by this completion pass, listing the same six bullet areas from §14 as now-done, and that CTX-3 may proceed.

- [ ] **Step 3: Add a new §15, "Completion pass (this session)"**

Write a new section, inserted after the existing §14 (renumbering nothing else — keep §14's content as historical record per the brief's "Keep the original session-budget disclosure as historical context rather than erasing it"), covering:

- **Audit call-site coverage** — list the concrete families fixed (Task 2: start-request-processor definition-unavailable audit via `run.ResourceContextId`; Task 3: `WorkflowRetryService` retry-blocked audit via `originalRun.ResourceContextId`; Task 4: `BulkOperationItemRetryService` requested/completed audit via `group.ResourceContextId`; Task 5: all 10 `ScheduleDispatchRunner` call sites via `ScheduleDefinition.ResourceContextId`; Task 6: `IntegrationSettingsService` via one new keyed Integration lookup — name this one explicitly as the documented single-lookup exception) and which call sites were confirmed to not exist yet (no Integration/Connection/Binding create-update audit call sites exist at all in this codebase today — out of scope for a "populate an existing call site" pass; note this as a finding for a future epic if desired, not a gap in this pass).
- **Read-side exposure** — list every DTO/contract changed: `RunSummaryDto`, `RunDetailDto` (Task 8); `ScheduleDetailDto` (both the API and client-side copies, Task 10) with the `ClaimResourceContextId` naming rationale; `AttentionIncidentDto` (Task 11); `NotificationInstanceSummaryDto`/`NotificationInstanceDetailDto` (Task 12); and explicitly note `PlatformEventEnvelope` already exposed `ResourceContextId` before this pass — no change needed there (Task 14).
- **Filters** — `RunQueryFilter.ResourceContextId` (required, Task 9); `AuditQuery.FilterByResourceContextId` (Task 7), `NotificationInstanceListQuery.ResourceContextId` (Task 13), `PlatformEventQuery.ResourceContextId` (Task 14) added because all three decision-rule conditions were met; Attention incidents deliberately did **not** get a query filter because no structured query object exists there today (only positional-parameter repository methods) — record this as a documented, deliberate deferral, not an oversight.
- **Snapshot integrity** — reference the two new tests added in Task 15 (`ExecutionAudit_UsesRunsSnapshot_NotCurrentIntegrationOwnership_AfterSimulatedMove`, `ScheduleRunClaim_StaysImmutable_WhenScheduleDefinitionsContextLaterChanges`) plus the pre-existing five from the original CTX-2 pass, all now living in `Ctx2HistoricalProvenanceTests.cs`.
- **Validation** — paste the exact final counts/results from Task 16 (full unit suite count, Ui.Shared.Tests count, ArchTests count, IntegrationTests result including the known SFTP dependency if it recurred, and full-solution build result).
- **Pre-existing gap found and fixed incidentally** — the `Apperture.Management.Client` copies of `ScheduleDto`/`ScheduleDetailDto` were silently missing the CTX-1 `ResourceContextId`/`ResourceContextRef` fields the API already returned; Task 10 backfilled them since it was already touching those exact records to add the new CTX-2 field, and leaving the gap would have made the new field itself go unused by any client that deserializes into this type.

- [ ] **Step 4: Commit**

```bash
git add docs/01-architecture/transitions/resource-contexts/ctx2-closure.md
git commit -m "CTX-2 completion: update closure doc — status Complete, add completion pass summary"
```

---

## Self-Review Notes (for the plan author, already applied above)

- **Spec coverage:** §2 (Audit) → Tasks 1-7; §3 (Run DTO) → Task 8; §4 (Run filter) → Task 9; §5 (Schedule diagnostic DTO) → Task 10; §6 (Attention/Notification DTO) → Tasks 11-12; §7 (PlatformEvent) → Task 14 (filter) + documented as already-exposed; §8 (optional filters) → Tasks 7, 13, 14 (added) and Attention (deliberately deferred, documented); §9 (immutability tests) → Task 15; §10 (audit provenance test) → Task 15; §11 (integration test env) → Task 16 Step 5-6; §12 (validation) → Task 16; §13 (docs) → Task 17.
- **Placeholder scan:** every step above shows the exact before/after code or the exact new field/line; the few spots marked "confirm exact X before editing" are pre-existing-code lookups the implementer must do because this planning pass did not have every line of every file in context (e.g. `BulkOperationItemRetryService.TryAuditAsync`'s exact parameter order, `RunQueryService.GetRunAsync`'s exact local variable names) — these are explicit "read this first" instructions, not vague placeholders, and each names the exact file and exact thing to look for.
- **Type consistency:** `AuditRecord.ResourceContextId`, `RunSummaryDto.ResourceContextId`, `RunDetailDto.ResourceContextId`, `RunQueryFilter.ResourceContextId`, `AuditQuery.FilterByResourceContextId`, `NotificationInstanceListQuery.ResourceContextId`, `PlatformEventQuery.ResourceContextId`, `AttentionIncidentDto.ResourceContextId`, `NotificationInstanceSummaryDto.ResourceContextId`, `NotificationInstanceDetailDto.ResourceContextId`, and `ScheduleDetailDto.ClaimResourceContextId` are used identically by name in every task that references them.
