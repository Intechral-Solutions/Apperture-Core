# CTX-3B Plan 1 — Move Contracts and Preview Validator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the CTX-3B move contracts (`ResourceMovePreview`/`ResourceMoveBlocker`/`ResourceMoveEffect`) and a fully unit-tested, read-only `IResourceOwnershipMoveService` preview validator for Integration, Connection and Schedule moves — no locking, no mutation, no audit, no HTTP surface yet.

**Architecture:** One shared, pure validation method per resource family (`ValidateIntegrationMoveAsync`, `ValidateConnectionMoveAsync`, `ValidateScheduleMoveAsync`) inside `ResourceOwnershipMoveService`, called by the `Preview*Async` methods in this plan and reused unchanged by `Move*Async` once CTX-3B Plan 2 adds locking and mutation around it. This is the mechanism that keeps preview and move from drifting (design gate §8.2: "Move re-runs the identical validation inside the transaction and never trusts a prior preview").

**No temporary production stubs.** `IResourceOwnershipMoveService` grows one `Preview*Async` member per task, added in the same commit as the real implementation of that member. This plan declares **no** `Move*Async` members at all — Plan 2 adds all three, interface member and implementation together, when a real implementation exists. Nothing can invoke movement before Plan 2, so there is no compile-time reason for a `NotSupportedException` placeholder, and every task in this plan builds green at its own commit boundary.

**Tech Stack:** C#/.NET 9, EF Core (InMemory provider for these tests — no SQL Server needed in this plan), xUnit, FluentAssertions.

**Spec:** `docs/01-architecture/transitions/resource-contexts/ctx3-design-gate.md` §8.1–§8.6, §8.2 (shape), §8.3 (common rules), §9.3 (no new authorization), §13 CTX-3B step 1, §14 CTX-3B acceptance criteria (preview/validator bullets).

## Global Constraints

- Movement is never a field on `UpdateIntegrationRequest`/`UpdateConnectionRequest`. This plan adds a reflection test asserting it (§8.2).
- The service lives in `Apperture.ControlPlane.Security` (§8.2), the interface and DTOs in `Apperture.Core.Application` (mirrors `ResourceContextReferenceRule`'s layering — `Apperture.Core.Application.ResourceContexts`).
- Moving into Shared is a normal target; every resource state (Retired, Inactive, Disabled) is movable (§8.3). Do not add a state guard.
- `source_archived` is explicitly **not** a blocker (§8.3) — never add a blocker code for it.
- Authorization uses the existing resource-family permission (`integration.update`, `connection.update`, `schedule.manage`), never `resource_context.manage` (§9.3). Do not add any `context:{id}` scope key or context-scoped permission.
- The validator only reads. It must not call any repository `UpdateAsync`/`AddAsync`/`DeleteAsync` method, and must not touch `AppertureDbContext` directly — that arrives in Plan 2 for the locked mutation path only.
- `IIntegrationRepository.UpdateAsync`/`IConnectionRepository.UpdateAsync` deliberately do not rewrite `ResourceContextId` (see `SqlIntegrationRepository.UpdateAsync`'s comment: "CTX-1 has no move operation, so UpdateAsync deliberately does not rewrite ResourceContextId"). Do not attempt to route any future mutation through those methods; note it here for Plan 2's benefit — this plan performs no mutation at all.
- **No temporary production stubs of any kind.** No `NotSupportedException` placeholder method, no throwing private validator stub, no "unblock the build for this commit only" step. If a member cannot be implemented yet, it is not declared yet.
- **Blocker and effect ordering must be deterministic.** Verified at review time: `SqlBindingRepository.GetByIntegrationIdAsync` (line 39), `SqlBindingRepository.GetByConnectionIdAsync` (line 56) and `SqlScheduleRepository.GetByIntegrationIdAsync` (line 45) all issue `.Where(...).ToListAsync()` with **no `ORDER BY`**, so SQL Server row order is unspecified. The validator must therefore sort its own inputs (`OrderBy(x => x.Id.Value, StringComparer.Ordinal)`) before building blockers and effects, so the same state always produces the same list in the same order for both the API response and the UI dialog. Do not "fix" this by adding `ORDER BY` to shared repository methods used by unrelated callers; sort at the point where determinism is contractually required.

---

## File Structure

**New files:**
- `src/Apperture.Core/Apperture.Core.Application/ResourceContexts/ResourceMoveModels.cs` — `ResourceMovePreview`, `ResourceMoveBlocker`, `ResourceMoveEffect`, `ResourceMoveBlockerCodes`, `ResourceMoveEffectCodes`, `ResourceMoveResourceTypes`.
- `src/Apperture.Core/Apperture.Core.Application/ResourceContexts/IResourceOwnershipMoveService.cs` — the preview interface. Created in Task 2 with **one** member and grown by Task 5 and Task 6, each adding its member in the same commit as that member's implementation.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceOwnershipMoveService.cs` — implementation (preview only in this plan; `Move*Async` does not exist here at all — Plan 2 introduces those members).
- `tests/Apperture.UnitTests/ResourceContexts/ResourceOwnershipMoveTestHarness.cs` — test harness, modeled on `ResourceContextOwnershipTestHarness`.
- `tests/Apperture.UnitTests/ResourceContexts/IntegrationMovePreviewTests.cs`
- `tests/Apperture.UnitTests/ResourceContexts/ConnectionMovePreviewTests.cs`
- `tests/Apperture.UnitTests/ResourceContexts/ScheduleMovePreviewTests.cs`
- `tests/Apperture.UnitTests/ResourceContexts/ResourceMoveDtoImmutabilityTests.cs`

**Modified files:**
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Integration.cs` — register `IResourceOwnershipMoveService`.

**Interfaces produced for later plans:**
- `IResourceOwnershipMoveService` with the three `Preview*Async` members. **Plan 2 adds the three `Move*Async` members** (interface declaration and implementation in the same commit); Plan 5 calls all six from controllers.
- `ResourceOwnershipMoveService.ValidateIntegrationMoveAsync/ValidateConnectionMoveAsync/ValidateScheduleMoveAsync` — `private` in this plan, and Plan 2 depends on their signatures. **Frozen signatures, with one deliberate Plan-2-driven shape for the Integration validator:**
  - `ValidateIntegrationMoveAsync(DomainIntegration integration, ResourceContextId targetId, CancellationToken ct) : ValueTask<IntegrationMoveValidation>`, where `IntegrationMoveValidation` is a **private nested record on `ResourceOwnershipMoveService`** (declared in Task 4), `sealed record IntegrationMoveValidation(ResourceMovePreview Preview, IReadOnlyList<ScheduleDefinition> AttachedSchedules)`. It stays private because only this service and its Plan 2/Plan 4 continuations consume it; it is never part of the API or client contract. The attached-schedule set is returned rather than re-read, because Plan 2 mutates exactly that set and Plan 4 audits exactly that set — all three must describe the same schedules from one authoritative read inside one transaction (review finding: a second `GetByIntegrationIdAsync` after mutation can disagree with the `schedules_move_with_integration` effect).
  - `ValidateConnectionMoveAsync(Connection connection, ResourceContextId targetId, CancellationToken ct) : ValueTask<ResourceMovePreview>`
  - `ValidateScheduleMoveAsync(ScheduleDefinition schedule, ResourceContextId targetId, CancellationToken ct) : ValueTask<ResourceMovePreview>`
- `ResourceMovePreview`/`ResourceMoveBlocker`/`ResourceMoveEffect` — consumed as-is by Plan 2 (mutation), Plan 4 (audit `Details` payload), and Plan 5 (HTTP response bodies, and mirrored code constants at the client boundary).

---

### Task 1: Move contracts (data only, no TDD)

**Files:**
- Create: `src/Apperture.Core/Apperture.Core.Application/ResourceContexts/ResourceMoveModels.cs`

**Interfaces:**
- Produces: `ResourceMovePreview`, `ResourceMoveBlocker`, `ResourceMoveEffect`, `ResourceMoveBlockerCodes`, `ResourceMoveEffectCodes`, `ResourceMoveResourceTypes`.

This task is a data-contract file with no behavior, so there is nothing to red/green — write it directly and verify the solution builds.

- [ ] **Step 1: Write the contracts file**

```csharp
using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.Core.Application.ResourceContexts;

/// <summary>
/// Advisory result of validating (preview) or attempting (move) a resource ownership change.
/// Preview and move share this exact shape (design gate §8.2) so a UI can render either
/// identically, and so move's 409 response body is literally this type.
/// </summary>
public sealed record ResourceMovePreview(
    string ResourceType,
    string ResourceId,
    ResourceContextRef Source,
    ResourceContextRef Target,
    bool CanMove,
    IReadOnlyList<ResourceMoveBlocker> Blockers,
    IReadOnlyList<ResourceMoveEffect> Effects);

/// <summary>
/// A reason a move cannot proceed. <see cref="RelatedResourceType"/>/<see cref="RelatedResourceId"/>/
/// <see cref="RelatedDisplayName"/>/<see cref="RelatedResourceContextId"/> are populated when the
/// blocker concerns a specific other resource (e.g. an unreachable Connection), and are all null for
/// the common, resource-less blockers (§8.3).
/// </summary>
public sealed record ResourceMoveBlocker(
    string Code,
    string Message,
    string? RelatedResourceType,
    string? RelatedResourceId,
    string? RelatedDisplayName,
    string? RelatedResourceContextId);

/// <summary>
/// A non-blocking consequence of a move the operator should see before submitting (e.g. "3 schedules
/// move with this integration"), or an advisory note the move does not make worse (§8.4 step 2).
/// </summary>
public sealed record ResourceMoveEffect(
    string Code,
    string Message,
    string? RelatedResourceType = null,
    IReadOnlyList<string>? RelatedResourceIds = null);

/// <summary>Stable resource-type discriminators for <see cref="ResourceMovePreview.ResourceType"/> and blocker/effect related-resource fields.</summary>
public static class ResourceMoveResourceTypes
{
    public const string Integration = "Integration";
    public const string Connection = "Connection";
    public const string Schedule = "Schedule";
    public const string ResourceContext = "ResourceContext";
    public const string Binding = "Binding";
}

/// <summary>Stable blocker codes (API and tests). See design gate §8.3–§8.6.</summary>
public static class ResourceMoveBlockerCodes
{
    /// <summary>Common (§8.3): the target ResourceContext id does not resolve to any row.</summary>
    public const string TargetNotFound = "target_not_found";

    /// <summary>Common (§8.3): the target ResourceContext exists but is not Active.</summary>
    public const string TargetArchived = "target_archived";

    /// <summary>Common (§8.3): source and target are the same ResourceContext.</summary>
    public const string AlreadyInTarget = "already_in_target";

    /// <summary>Integration move (§8.4): a bound Connection would become unreachable from the target.</summary>
    public const string BindingConnectionUnreachable = "binding_connection_unreachable";

    /// <summary>Integration move (§8.4): an attached Schedule's current context does not match the Integration's source context.</summary>
    public const string ScheduleContextInconsistent = "schedule_context_inconsistent";

    /// <summary>Connection move (§8.5): a bound Integration (including Retired/Inactive) would no longer be able to reference this Connection.</summary>
    public const string BindingIntegrationUnreachable = "binding_integration_unreachable";

    /// <summary>Connection move (§8.5): a legacy/global binding with no owning Integration requires the Connection to stay Shared.</summary>
    public const string UnownedBindingRequiresShared = "unowned_binding_requires_shared";

    /// <summary>Schedule move (§8.6): an Integration-attached Schedule cannot move independently.</summary>
    public const string ScheduleAttachedToIntegration = "schedule_attached_to_integration";
}

/// <summary>Stable effect codes (API and tests). See design gate §8.4–§8.5.</summary>
public static class ResourceMoveEffectCodes
{
    /// <summary>Integration move (§8.4 step 4): schedules that move atomically with the Integration.</summary>
    public const string SchedulesMoveWithIntegration = "schedules_move_with_integration";

    /// <summary>Integration move (§8.4 step 2): a binding references a Connection row that no longer exists; advisory, never a blocker.</summary>
    public const string BindingReferencesMissingConnection = "binding_references_missing_connection";

    /// <summary>Connection move (§8.5): "Used by" blast radius, shown even when CanMove is true.</summary>
    public const string UsedByIntegration = "used_by_integration";
}
```

- [ ] **Step 2: Build the solution**

Run: `dotnet build src/Apperture.Core/Apperture.Core.Application/Apperture.Core.Application.csproj`
Expected: build succeeds (new file only adds types; nothing references them yet).

- [ ] **Step 3: Commit**

```bash
git add src/Apperture.Core/Apperture.Core.Application/ResourceContexts/ResourceMoveModels.cs
git commit -m "CTX-3B: add resource move contracts (preview/blocker/effect models)"
```

---

### Task 2: `IResourceOwnershipMoveService` interface — Integration preview member only (data only, no TDD)

**Files:**
- Create: `src/Apperture.Core/Apperture.Core.Application/ResourceContexts/IResourceOwnershipMoveService.cs`

**Interfaces:**
- Consumes: `ResourceMovePreview` (Task 1).
- Produces: `IResourceOwnershipMoveService` with **one** member. Task 5 adds `PreviewConnectionMoveAsync`, Task 6 adds `PreviewScheduleMoveAsync`, and **Plan 2 adds all three `Move*Async` members** — each in the same commit as its implementation, so the interface is never ahead of the class and no stub is ever needed.

- [ ] **Step 1: Write the interface**

```csharp
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.Core.Application.ResourceContexts;

/// <summary>
/// Validates and performs explicit resource-ownership moves (design gate §8). Movement never happens
/// as a side effect of an ordinary update — only through this service, backed by
/// <c>GET .../ownership/move-preview</c> and <c>POST .../ownership/move</c> (§8.2).
/// </summary>
/// <remarks>
/// <para>
/// Every <c>Preview*</c> method is advisory and mutates nothing. Every <c>Move*</c> method (CTX-3B Plan 2)
/// re-runs the identical validation inside a transaction, behind the ownership mutation gate, and returns
/// the resulting <see cref="ResourceMovePreview"/> whether or not the move succeeded — callers distinguish
/// success from a blocked attempt via <see cref="ResourceMovePreview.CanMove"/> plus which blocker codes are
/// present (see design gate §8.3 for the 400-vs-409 HTTP mapping the controller applies over this result).
/// </para>
/// <para>
/// This interface is grown one member at a time, each alongside its real implementation. It deliberately
/// never declares a member that throws <c>NotSupportedException</c>.
/// </para>
/// </remarks>
public interface IResourceOwnershipMoveService
{
    ValueTask<ResourceMovePreview> PreviewIntegrationMoveAsync(IntegrationId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Build and commit**

```bash
dotnet build src/Apperture.Core/Apperture.Core.Application/Apperture.Core.Application.csproj
git add src/Apperture.Core/Apperture.Core.Application/ResourceContexts/IResourceOwnershipMoveService.cs
git commit -m "CTX-3B: add IResourceOwnershipMoveService interface"
```

---

### Task 3: Test harness

**Files:**
- Create: `tests/Apperture.UnitTests/ResourceContexts/ResourceOwnershipMoveTestHarness.cs`

**Interfaces:**
- Consumes: `ResourceOwnershipMoveService` (Task 4), `IIntegrationRepository`/`IConnectionRepository`/`IScheduleRepository`/`IBindingRepository`/`IResourceContextService` (existing), the same fake-actor/always-allow pattern as `ResourceContextOwnershipTestHarness`.
- Produces: `ResourceOwnershipMoveTestHarness` with `Service`, `Db`, `ResourceContexts`, `Integrations`, `Connections`, `Schedules`, `Bindings` properties, plus `CreateApplicationAsync(string name)` and `ArchiveAsync(ResourceContextId)` helpers, plus the `grantedAction`/`grantedResourceType` constructor arguments used by the per-family authorization tests in Tasks 4-6.

This harness has no independent behavior to red/green — it is test infrastructure. Write it directly; Task 4's tests are what exercise it.

**Commit boundary note:** this task is not committed on its own. The harness is uncompilable until `ResourceOwnershipMoveService` exists, so it is written here and committed as part of **Task 4 Step 7**, together with the service and the first test file. Every *commit* in this plan builds green; this task is an intermediate step inside Task 4's commit.

- [ ] **Step 1: Write the harness**

```csharp
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Moq;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// Wires a real <see cref="ResourceOwnershipMoveService"/> over real management services and
/// repositories (EF Core InMemory), following the shape of <see cref="ResourceContextOwnershipTestHarness"/>.
/// Authorization is always-allow by default; use <see cref="WithAuthorization"/> to exercise the denied
/// path with a specific granted permission (mirrors <c>ManagementServiceTestHarness</c>).
/// </summary>
public sealed class ResourceOwnershipMoveTestHarness : IAsyncDisposable
{
    private readonly AppertureDbContext _db;
    private int _nextSequence;

    public ResourceOwnershipMoveTestHarness(string? grantedAction = "any", string? grantedResourceType = "any")
    {
        _db = new AppertureDbContext(
            new DbContextOptionsBuilder<AppertureDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        _db.ResourceContexts.Add(new ResourceContextEntity
        {
            Id = ResourceContextIds.SharedValue,
            Name = "Shared",
            Slug = "shared",
            Kind = ResourceContextKinds.Shared,
            State = ResourceContextStates.Active,
            IsReserved = true,
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch
        });
        _db.SaveChanges();

        var resourceContextRepository = new SqlResourceContextRepository(_db);
        ResourceContexts = new ResourceContextService(resourceContextRepository, TimeProvider.System);
        IntegrationRepository = new SqlIntegrationRepository(_db);
        ConnectionRepository = new SqlConnectionRepository(_db);
        BindingRepository = new SqlBindingRepository(_db);
        ScheduleRepository = new SqlScheduleRepository(_db);

        var authz = grantedAction is null
            ? (IAuthorizationEvaluationService)new DenyAllAuthorizationEvaluationService()
            : new PermissionMatchingAuthorizationEvaluationService(grantedAction, grantedResourceType);

        Integrations = new IntegrationManagementService(
            new FixedPrincipalContext(),
            new AlwaysAllowAuthorizationEvaluationService(),
            IntegrationRepository,
            ResourceContexts,
            _db);

        var connectorRegistry = new PermissiveConnectorDefinitionRegistry();
        Connections = new ConnectionManagementService(
            new FixedPrincipalContext(),
            new AlwaysAllowAuthorizationEvaluationService(),
            ConnectionRepository,
            connectorRegistry,
            new NoOpConnectionVerificationService(),
            new ConnectionSecretMaterialProvisioner(new Mock<ISecretStore>().Object),
            ResourceContexts);

        Bindings = new BindingService(
            new FixedPrincipalContext(),
            new AlwaysAllowResourceAuthorizationService(),
            BindingRepository,
            ConnectionRepository,
            new BindingEligibilityService(ConnectionRepository, connectorRegistry),
            IntegrationRepository);

        Schedules = new SqlScheduleManagementService(
            ScheduleRepository,
            new StubClaimOptions(),
            TimeProvider.System,
            ResourceContexts,
            IntegrationRepository);

        Service = new ResourceOwnershipMoveService(
            new FixedPrincipalContext(),
            authz,
            IntegrationRepository,
            ConnectionRepository,
            ScheduleRepository,
            BindingRepository,
            ResourceContexts);
    }

    public IResourceOwnershipMoveService Service { get; }

    /// <summary>The harness's own <see cref="AppertureDbContext"/>, exposed so fixtures can seed state
    /// the management services deliberately refuse to write (pre-existing ownership drift, cross-context
    /// bindings) and so Plan 2 can pass the same instance into the service under test.</summary>
    public AppertureDbContext Db => _db;

    public IResourceContextService ResourceContexts { get; }
    public IIntegrationRepository IntegrationRepository { get; }
    public IConnectionRepository ConnectionRepository { get; }
    public IBindingRepository BindingRepository { get; }
    public IScheduleRepository ScheduleRepository { get; }
    public IIntegrationManagementService Integrations { get; }
    public IConnectionManagementService Connections { get; }
    public IBindingService Bindings { get; }
    public IScheduleManagementService Schedules { get; }

    /// <summary>Creates and returns a new Active Application ResourceContext.</summary>
    public async Task<ResourceContext> CreateApplicationAsync(string name, CancellationToken ct = default) =>
        await ResourceContexts.CreateApplicationContextAsync(name, cancellationToken: ct);

    public async Task<ResourceContext> ArchiveAsync(ResourceContextId id, CancellationToken ct = default) =>
        await ResourceContexts.ArchiveAsync(id, ct);

    public async Task<DomainIntegration> CreateIntegrationAsync(
        ResourceContextId contextId,
        IntegrationState state = IntegrationState.Active,
        CancellationToken ct = default)
    {
        var created = await Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest($"Integration {NextSequence()}", "Sample.Mod", ResourceContextId: contextId),
            ct);
        if (state != IntegrationState.Active)
            created = await Integrations.UpdateIntegrationAsync(created.Id, new UpdateIntegrationRequest(State: state), ct);
        return created;
    }

    public async Task<Connection> CreateConnectionAsync(
        ResourceContextId contextId,
        ConnectionState state = ConnectionState.Active,
        CancellationToken ct = default)
    {
        var created = await Connections.CreateConnectionAsync(
            new CreateConnectionRequest(
                new ConnectorDefinitionId($"test.connector.{NextSequence()}"),
                $"Connection {NextSequence()}",
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                ResourceContextId: contextId),
            ct);
        if (state == ConnectionState.Retired)
            await Connections.RetireConnectionAsync(created.Id, ct);
        return created;
    }

    /// <summary>
    /// Binds <paramref name="connectionId"/> into <paramref name="integrationId"/> at a fresh slot name,
    /// bypassing eligibility (test-only direct repository write) so cross-context fixtures can be built
    /// without fighting eligibility filtering.
    /// </summary>
    /// <param name="integrationId">
    /// Nullable: pass <c>null</c> to build the legacy/global unowned binding that
    /// <c>GetUnownedBySlotAsync</c> resolves, which is what the <c>unowned_binding_requires_shared</c>
    /// fixtures need. <see cref="Binding.IntegrationId"/> is genuinely <c>IntegrationId?</c> in the domain,
    /// so this parameter must be nullable too — never pass <c>null!</c> through a non-nullable parameter.
    /// </param>
    public async Task<Binding> AddDirectBindingAsync(
        IntegrationId? integrationId,
        string moduleId,
        ConnectionId? connectionId,
        CancellationToken ct = default)
    {
        var binding = new Binding(
            new BindingId($"b-{NextSequence()}"),
            integrationId,
            moduleId,
            $"slot-{NextSequence()}",
            "test.contract",
            connectionId,
            null,
            BindingState.Active);
        await BindingRepository.AddAsync(binding, ct);
        return binding;
    }

    public Task<ScheduleDefinition> CreateAttachedScheduleAsync(
        IntegrationId integrationId,
        ResourceContextId? explicitContextId = null,
        CancellationToken ct = default)
    {
        var definition = new ScheduleDefinition(
            new ScheduleId($"s-{NextSequence()}"),
            "Nightly",
            "0 0 * * *",
            "UTC",
            Enabled: true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            ExecutionPolicyRef: null,
            Version: 1,
            integrationId) with
        { ResourceContextId = explicitContextId };
        return Schedules.CreateScheduleAsync(definition, ct).AsTask();
    }

    public Task<ScheduleDefinition> CreateStandaloneScheduleAsync(ResourceContextId contextId, CancellationToken ct = default)
    {
        var definition = new ScheduleDefinition(
            new ScheduleId($"s-{NextSequence()}"),
            "Standalone",
            "0 1 * * *",
            "UTC",
            Enabled: true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            ExecutionPolicyRef: null,
            Version: 1,
            IntegrationId: null) with
        { ResourceContextId = contextId };
        return Schedules.CreateScheduleAsync(definition, ct).AsTask();
    }

    /// <summary>Directly rewrites a Schedule's persisted <c>ResourceContextId</c> without going through the management service, to seed pre-existing drift for <c>schedule_context_inconsistent</c> tests.</summary>
    public async Task SetScheduleResourceContextDirectlyAsync(ScheduleId id, ResourceContextId newContextId, CancellationToken ct = default)
    {
        var entity = await _db.ScheduleDefinitions.FindAsync([id.Value], ct)
            ?? throw new InvalidOperationException($"Schedule '{id.Value}' not found.");
        entity.ResourceContextId = newContextId.Value;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Test-only direct ownership rewrite for an Integration, standing in for CTX-3B's own Integration
    /// move until Plan 2 lands it for real. Used to build fixtures where a bound Integration is already
    /// in the Connection move's target context. Once Plan 2 exists, prefer the real
    /// <c>MoveIntegrationAsync</c> in new tests; this helper stays for Plan 1's preview-only fixtures.
    /// </summary>
    public async Task MoveIntegrationDirectlyForFixtureAsync(IntegrationId id, ResourceContextId newContextId, CancellationToken ct = default)
    {
        var entity = await _db.Integrations.FindAsync([id.Value], ct)
            ?? throw new InvalidOperationException($"Integration '{id.Value}' not found.");
        entity.ResourceContextId = newContextId.Value;
        await _db.SaveChangesAsync(ct);
    }

    private int NextSequence() => System.Threading.Interlocked.Increment(ref _nextSequence);

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private sealed class FixedPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new FixedActor());

        private sealed class FixedActor : IActor
        {
            public ActorId Id => new("test-actor");
            public ActorKind Kind => ActorKind.Human;
            public string? DisplayName => "Test Actor";
        }
    }

    private sealed class AlwaysAllowAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class AlwaysAllowResourceAuthorizationService : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor, PermissionDescriptor permission, ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class DenyAllAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }

    /// <summary>Grants only when the requested permission's action and resource type exactly match what was configured.</summary>
    private sealed class PermissionMatchingAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        private readonly string _grantedAction;
        private readonly string? _grantedResourceType;

        public PermissionMatchingAuthorizationEvaluationService(string grantedAction, string? grantedResourceType)
        {
            _grantedAction = grantedAction;
            _grantedResourceType = grantedResourceType;
        }

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            if (string.Equals(_grantedAction, "any", StringComparison.Ordinal))
                return ValueTask.FromResult(true);
            var matches = string.Equals(context.Permission.Action, _grantedAction, StringComparison.Ordinal)
                && string.Equals(context.Permission.Resource?.ResourceType, _grantedResourceType, StringComparison.Ordinal);
            return ValueTask.FromResult(matches);
        }
    }

    private sealed class NoOpConnectionVerificationService : IConnectionVerificationService
    {
        public ValueTask<ConnectionVerificationResult> VerifyAsync(Connection connection, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ConnectionVerificationResult(ConnectionVerificationStatus.NeverTested, DateTimeOffset.UnixEpoch, null));
    }

    private sealed class StubClaimOptions : ISchedulerClaimOptions
    {
        public int ClaimLeaseMinutes => 5;
    }

    private sealed class PermissiveConnectorDefinitionRegistry : IConnectorDefinitionRegistry
    {
        private readonly Dictionary<string, ConnectorDefinition> _definitions = new(StringComparer.Ordinal);
        public void Register(ConnectorDefinition definition) => _definitions[definition.Id.Value] = definition;
        public IReadOnlyList<ConnectorDefinition> GetAll(string? moduleId = null) => _definitions.Values.ToList();
        public ConnectorDefinition? Get(ConnectorDefinitionId id)
        {
            if (!_definitions.TryGetValue(id.Value, out var definition))
            {
                definition = new ConnectorDefinition(id, id.Value, null, new Dictionary<string, string>());
                _definitions[id.Value] = definition;
            }
            return definition;
        }
    }
}
```

- [ ] **Step 2: Build (will fail until Task 4's service class exists — that's expected, and nothing is committed here)**

Run: `dotnet build tests/Apperture.UnitTests/Apperture.UnitTests.csproj`
Expected: FAIL — `ResourceOwnershipMoveService` does not exist yet. This is the harness's "red" state; Task 4 makes it compile and commits it. Do **not** commit at the end of this task.

---

### Task 4: `ResourceOwnershipMoveService` — Integration preview validation

**Files:**
- Create: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceOwnershipMoveService.cs`
- Test: `tests/Apperture.UnitTests/ResourceContexts/IntegrationMovePreviewTests.cs`

**Interfaces:**
- Consumes: `IIntegrationRepository.GetByIdAsync`, `IBindingRepository.GetByIntegrationIdAsync`, `IConnectionRepository.GetByIdAsync`, `IScheduleRepository.GetByIntegrationIdAsync`, `IResourceContextService.GetAsync`, `ResourceContextReferenceRule.CanReference`.
- Produces: `ResourceOwnershipMoveService.PreviewIntegrationMoveAsync` (public), `ValidateIntegrationMoveAsync` (private, signature frozen for Plan 2).

- [ ] **Step 1: Write the failing tests**

```csharp
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Domain.Integration;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

public sealed class IntegrationMovePreviewTests
{
    [Fact]
    public async Task Target_not_found_is_a_blocker()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var integration = await h.CreateIntegrationAsync(source.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, new(Guid.NewGuid().ToString("N")));

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.TargetNotFound);
    }

    [Fact]
    public async Task Target_archived_is_a_blocker()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        await h.ArchiveAsync(target.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.TargetArchived);
    }

    [Fact]
    public async Task Same_target_is_a_blocker()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var integration = await h.CreateIntegrationAsync(source.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, source.Id);

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.AlreadyInTarget);
    }

    [Fact]
    public async Task Archived_source_is_allowed_cleanup_not_a_blocker()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id, Apperture.Core.Domain.Integration.IntegrationState.Retired);
        await h.ArchiveAsync(source.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        preview.Blockers.Should().BeEmpty("moving out of an archived Application is legitimate cleanup, not a blocker");
    }

    [Fact]
    public async Task No_bindings_no_schedules_moves_cleanly()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        preview.Source.Id.Should().Be(source.Id.Value);
        preview.Target.Id.Should().Be(target.Id.Value);
    }

    [Fact]
    public async Task Connection_already_in_the_target_context_does_not_block()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var connection = await h.CreateConnectionAsync(target.Id); // already where the Integration is going
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        preview.Blockers.Should().BeEmpty(
            "the rule is evaluated against the TARGET context, not the source -- a connection already in the target is reachable after the move");
    }

    [Fact]
    public async Task Connection_left_behind_in_the_source_context_blocks()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var connection = await h.CreateConnectionAsync(source.Id); // stays behind in source
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeFalse(
            "CanReference(target, source) is false -- the connection must be moved or unbound first");
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.BindingConnectionUnreachable);
    }

    [Fact]
    public async Task Shared_connection_binding_never_blocks()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var sharedConnection = await h.CreateConnectionAsync(Apperture.Core.Domain.ResourceContexts.ResourceContextIds.Shared);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, sharedConnection.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeTrue();
    }

    [Fact]
    public async Task Unreachable_target_binding_blocks()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var connection = await h.CreateConnectionAsync(thirdParty.Id); // neither source nor target
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        var blocker = preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.BindingConnectionUnreachable).Subject;
        blocker.RelatedResourceId.Should().Be(connection.Id.Value);
    }

    [Fact]
    public async Task Moving_to_shared_requires_every_bound_connection_to_be_shared()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var sameContextConnection = await h.CreateConnectionAsync(source.Id); // not Shared
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, sameContextConnection.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, Apperture.Core.Domain.ResourceContexts.ResourceContextIds.Shared);

        preview.CanMove.Should().BeFalse("Shared cannot reach an Application-owned connection");
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.BindingConnectionUnreachable);
    }

    [Fact]
    public async Task Retired_connection_binding_evaluated_the_same_way()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var retiredConnection = await h.CreateConnectionAsync(source.Id, Apperture.Core.Domain.Connector.ConnectionState.Retired);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, retiredConnection.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeFalse("a Retired connection is still evaluated by the reference rule, not skipped");
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.BindingConnectionUnreachable);
    }

    [Fact]
    public async Task Binding_with_missing_connection_is_advisory_not_a_blocker()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, new(Guid.NewGuid().ToString("N"))); // no such Connection row

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        preview.Effects.Should().ContainSingle(e => e.Code == ResourceMoveEffectCodes.BindingReferencesMissingConnection);
    }

    [Fact]
    public async Task Attached_schedules_move_effect_lists_schedule_count()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        await h.CreateAttachedScheduleAsync(integration.Id);
        await h.CreateAttachedScheduleAsync(integration.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        var effect = preview.Effects.Should().ContainSingle(e => e.Code == ResourceMoveEffectCodes.SchedulesMoveWithIntegration).Subject;
        effect.RelatedResourceIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task Inconsistent_schedule_context_blocks()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var drifted = await h.CreateApplicationAsync("Drifted");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var schedule = await h.CreateAttachedScheduleAsync(integration.Id);
        await h.SetScheduleResourceContextDirectlyAsync(schedule.Id, drifted.Id); // simulate pre-existing corruption

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.ScheduleContextInconsistent);
    }

    [Fact]
    public async Task Retired_and_inactive_integrations_remain_movable()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var retired = await h.CreateIntegrationAsync(source.Id, IntegrationState.Retired);
        var inactive = await h.CreateIntegrationAsync(source.Id, IntegrationState.Inactive);

        (await h.Service.PreviewIntegrationMoveAsync(retired.Id, target.Id)).CanMove.Should().BeTrue();
        (await h.Service.PreviewIntegrationMoveAsync(inactive.Id, target.Id)).CanMove.Should().BeTrue();
    }

    [Fact]
    public async Task Not_authorized_throws()
    {
        using var h = new ResourceOwnershipMoveTestHarness(grantedAction: null);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);

        var act = async () => await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Integration_update_permission_is_required_not_resource_context_manage()
    {
        using var h = new ResourceOwnershipMoveTestHarness(
            grantedAction: Apperture.Core.Domain.Governance.ActionConstants.ResourceContextManage,
            grantedResourceType: Apperture.Core.Domain.Governance.ResourceTypeConstants.ResourceContext);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);

        var act = async () => await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "holding resource_context.manage must not be enough -- integration.update is the required permission (§9.3)");
    }

    [Fact]
    public async Task Multiple_unreachable_connections_each_produce_their_own_blocker()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var first = await h.CreateConnectionAsync(thirdParty.Id);
        var second = await h.CreateConnectionAsync(source.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, first.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, second.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        var unreachable = preview.Blockers
            .Where(b => b.Code == ResourceMoveBlockerCodes.BindingConnectionUnreachable)
            .ToList();
        unreachable.Should().HaveCount(2,
            "every offending connection must be reported -- collapsing to the first one hides half the work the operator has to do");
        unreachable.Select(b => b.RelatedResourceId)
            .Should().BeEquivalentTo([first.Id.Value, second.Id.Value]);
        unreachable.Should().OnlyContain(b => b.RelatedDisplayName != null && b.RelatedResourceContextId != null,
            "each blocker carries its own related resource, not a shared summary");
    }

    [Fact]
    public async Task Blocker_order_is_deterministic_across_repeated_previews()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var integration = await h.CreateIntegrationAsync(source.Id);
        for (var i = 0; i < 5; i++)
        {
            var connection = await h.CreateConnectionAsync(thirdParty.Id);
            await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);
        }

        var first = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);
        var second = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        first.Blockers.Should().HaveCount(5);
        first.Blockers.Select(b => b.RelatedResourceId)
            .Should().Equal(second.Blockers.Select(b => b.RelatedResourceId),
                "blocker order must be stable so the API response and the UI dialog do not reshuffle between calls");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~IntegrationMovePreviewTests"`
Expected: FAIL (compile error — `ResourceOwnershipMoveService` does not exist).

- [ ] **Step 3: Write the service (Integration preview only — no other members exist yet)**

The class implements the one-member interface from Task 2, so it compiles and satisfies the interface exactly. `PreviewConnectionMoveAsync`/`PreviewScheduleMoveAsync` are added by Tasks 5/6; `Move*Async` by Plan 2. Nothing throws `NotSupportedException` anywhere.

```csharp
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Validates and performs explicit resource-ownership moves (design gate §8). CTX-3B Plan 1: preview
/// validation only. <c>Move*Async</c> mutation and locking land in Plan 2; both call the same
/// <c>Validate*MoveAsync</c> methods this plan defines, so preview and move cannot drift (§8.2).
/// </summary>
public sealed class ResourceOwnershipMoveService : IResourceOwnershipMoveService
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IIntegrationRepository _integrationRepository;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IScheduleRepository _scheduleRepository;
    private readonly IBindingRepository _bindingRepository;
    private readonly IResourceContextService _resourceContexts;

    public ResourceOwnershipMoveService(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IIntegrationRepository integrationRepository,
        IConnectionRepository connectionRepository,
        IScheduleRepository scheduleRepository,
        IBindingRepository bindingRepository,
        IResourceContextService resourceContexts)
    {
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _integrationRepository = integrationRepository;
        _connectionRepository = connectionRepository;
        _scheduleRepository = scheduleRepository;
        _bindingRepository = bindingRepository;
        _resourceContexts = resourceContexts;
    }

    /// <inheritdoc />
    public async ValueTask<ResourceMovePreview> PreviewIntegrationMoveAsync(
        IntegrationId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default)
    {
        var integration = await _integrationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Integration {id.Value} not found.");
        await AuthorizeAsync(ActionConstants.IntegrationUpdate, integration.GetIdentity(), cancellationToken).ConfigureAwait(false);
        var validation = await ValidateIntegrationMoveAsync(integration, targetResourceContextId, cancellationToken).ConfigureAwait(false);
        return validation.Preview;
    }

    /// <summary>
    /// The Integration validator's full result: the operator-facing preview, plus the authoritative
    /// attached-Schedule set it was computed from. Plan 2 mutates exactly this set and Plan 4 audits
    /// exactly this set, so all three describe the same schedules from one read inside one transaction.
    /// Private: never part of the API or client contract.
    /// </summary>
    private sealed record IntegrationMoveValidation(
        ResourceMovePreview Preview,
        IReadOnlyList<ScheduleDefinition> AttachedSchedules);

    /// <summary>
    /// Shared validator for Integration moves (§8.4). Called by preview here and, unchanged, by the
    /// gated transaction in Plan 2 -- this is the mechanism that prevents preview/move drift (§8.2).
    /// </summary>
    private async ValueTask<IntegrationMoveValidation> ValidateIntegrationMoveAsync(
        DomainIntegration integration, ResourceContextId targetId, CancellationToken cancellationToken)
    {
        var sourceId = integration.ResourceContextId;
        var sourceRef = await ResolveRefAsync(sourceId, cancellationToken).ConfigureAwait(false);
        var target = await _resourceContexts.GetAsync(targetId, cancellationToken).ConfigureAwait(false);

        var blockers = new List<ResourceMoveBlocker>();
        var effects = new List<ResourceMoveEffect>();

        if (target is null)
        {
            blockers.Add(new ResourceMoveBlocker(
                ResourceMoveBlockerCodes.TargetNotFound, $"ResourceContext '{targetId.Value}' does not exist.",
                null, null, null, null));
            return new(Build(integration.Id.Value, sourceRef, await ResolveRefAsync(targetId, cancellationToken), blockers, effects), []);
        }
        if (!target.IsActive)
        {
            blockers.Add(new ResourceMoveBlocker(
                ResourceMoveBlockerCodes.TargetArchived, $"'{target.Name}' is archived and cannot receive new ownership.",
                ResourceMoveResourceTypes.ResourceContext, target.Id.Value, target.Name, target.Id.Value));
            return new(Build(integration.Id.Value, sourceRef, target.ToRef(), blockers, effects), []);
        }
        if (string.Equals(sourceId.Value, targetId.Value, StringComparison.Ordinal))
        {
            blockers.Add(new ResourceMoveBlocker(
                ResourceMoveBlockerCodes.AlreadyInTarget, "The integration is already owned by the requested context.",
                null, null, null, null));
            return new(Build(integration.Id.Value, sourceRef, target.ToRef(), blockers, effects), []);
        }

        // Deterministic ordering (Global Constraints): the repository methods below have no ORDER BY, so
        // SQL Server row order is unspecified. Sorting here is what makes the blocker/effect lists stable
        // for the API response, the UI dialog, and the audit payload.
        var bindings = (await _bindingRepository.GetByIntegrationIdAsync(integration.Id, cancellationToken).ConfigureAwait(false))
            .OrderBy(b => b.Id.Value, StringComparer.Ordinal)
            .ToList();

        foreach (var binding in bindings)
        {
            if (binding.ConnectionId is not { } connectionId)
                continue; // capability binding: no Connection to check.

            var connection = await _connectionRepository.GetByIdAsync(connectionId, cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                effects.Add(new ResourceMoveEffect(
                    ResourceMoveEffectCodes.BindingReferencesMissingConnection,
                    $"Binding for slot '{binding.SlotName}' references a missing connection; the move does not make this worse.",
                    ResourceMoveResourceTypes.Binding, [binding.Id.Value]));
                continue;
            }

            if (!ResourceContextReferenceRule.CanReference(targetId, connection.ResourceContextId))
            {
                blockers.Add(new ResourceMoveBlocker(
                    ResourceMoveBlockerCodes.BindingConnectionUnreachable,
                    $"Connection '{connection.Name}' is owned by '{connection.ResourceContextId.Value}' and would become unreachable if this integration moved to '{target.Name}'.",
                    ResourceMoveResourceTypes.Connection, connection.Id.Value, connection.Name, connection.ResourceContextId.Value));
            }
        }

        // The authoritative attached-Schedule set. Read exactly once; returned to the caller so Plan 2's
        // mutation and Plan 4's audit describe the same schedules this preview described.
        var schedules = (await _scheduleRepository.GetByIntegrationIdAsync(integration.Id, cancellationToken).ConfigureAwait(false))
            .OrderBy(s => s.Id.Value, StringComparer.Ordinal)
            .ToList();

        // NOTE (verified at review time): SqlScheduleRepository.ToDefinition coalesces a null/whitespace
        // ResourceContextId column to Shared, so `s.ResourceContextId` is never null coming out of the
        // repository. The null test below is retained only as a defensive guard against a future mapper
        // change -- it is not the mechanism that catches legacy NULL rows. Legacy NULL rows present as
        // Shared and therefore land in the `!= sourceId` branch. See this plan's "Legacy schedule
        // ownership drift" note and Plan 2's preflight (product decision P3).
        var inconsistent = schedules
            .Where(s => s.ResourceContextId is null || !string.Equals(s.ResourceContextId.Value, sourceId.Value, StringComparison.Ordinal))
            .ToList();
        if (inconsistent.Count > 0)
        {
            foreach (var s in inconsistent)
            {
                blockers.Add(new ResourceMoveBlocker(
                    ResourceMoveBlockerCodes.ScheduleContextInconsistent,
                    $"Schedule '{s.Name}' does not currently match this integration's context and must be corrected before it can move.",
                    ResourceMoveResourceTypes.Schedule, s.Id.Value, s.Name, s.ResourceContextId?.Value));
            }
        }
        else if (schedules.Count > 0)
        {
            effects.Add(new ResourceMoveEffect(
                ResourceMoveEffectCodes.SchedulesMoveWithIntegration,
                $"{schedules.Count} schedule(s) move with this integration.",
                ResourceMoveResourceTypes.Schedule, schedules.Select(s => s.Id.Value).ToList()));
        }

        return new(Build(integration.Id.Value, sourceRef, target.ToRef(), blockers, effects), schedules);
    }

    private async ValueTask AuthorizeAsync(string action, IGovernedResourceIdentity identity, CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

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

    // AuthorizeScheduleAsync is added in Task 6, alongside PreviewScheduleMoveAsync -- the only member
    // that needs it. Nothing unused is written ahead of its caller.

    private async ValueTask<ResourceContextRef> ResolveRefAsync(ResourceContextId id, CancellationToken cancellationToken)
    {
        var ctx = await _resourceContexts.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return ctx?.ToRef() ?? new ResourceContextRef(id.Value, id.Value, id.Value);
    }

    private static ResourceMovePreview Build(
        string resourceId, ResourceContextRef source, ResourceContextRef target,
        List<ResourceMoveBlocker> blockers, List<ResourceMoveEffect> effects) =>
        new(ResourceMoveResourceTypes.Integration, resourceId, source, target, blockers.Count == 0, blockers, effects);
}
```

Note: `Build`'s hardcoded `ResourceMoveResourceTypes.Integration` is correct for this task's method; Task 5 and Task 6 add sibling private `Build`-style construction for Connection/Schedule with their own resource type (do not try to generalize `Build` across families in this task — keep it simple until all three exist, then Task 6's self-review may fold them if it reads cleanly, but do not force it).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~IntegrationMovePreviewTests"`
Expected: PASS (all cases in Step 1).

- [ ] **Step 5: Register the service in DI**

Modify `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Integration.cs`, inside `AddAppertureIntegrationWorkflows`:

```csharp
        services.AddScoped<IIntegrationManagementService, IntegrationManagementService>();
        services.AddScoped<IResourceContextManagementService, ResourceContextManagementService>();
        services.AddScoped<Apperture.Core.Application.ResourceContexts.IResourceOwnershipMoveService, ResourceOwnershipMoveService>();
```

- [ ] **Step 6: Build the full solution**

Run: `dotnet build Apperture.slnx`
Expected: **0 errors, no stub required.** The interface declares exactly the one member the class implements. There is no `Step 6a` unblock hack in this plan.

- [ ] **Step 7: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceOwnershipMoveService.cs \
        src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Integration.cs \
        tests/Apperture.UnitTests/ResourceContexts/ResourceOwnershipMoveTestHarness.cs \
        tests/Apperture.UnitTests/ResourceContexts/IntegrationMovePreviewTests.cs
git commit -m "CTX-3B: implement Integration move preview validator with full blocker-code coverage"
```

---

### Task 5: Connection preview validation

**Files:**
- Modify: `src/Apperture.Core/Apperture.Core.Application/ResourceContexts/IResourceOwnershipMoveService.cs` (add `PreviewConnectionMoveAsync`)
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceOwnershipMoveService.cs` (add `PreviewConnectionMoveAsync` + `ValidateConnectionMoveAsync` — there is no stub to replace)
- Test: `tests/Apperture.UnitTests/ResourceContexts/ConnectionMovePreviewTests.cs`

**Interfaces:**
- Consumes: `IBindingRepository.GetByConnectionIdAsync`, `IIntegrationRepository.GetByIdAsync`, `ResourceContextReferenceRule.CanReference`.
- Produces: `IResourceOwnershipMoveService.PreviewConnectionMoveAsync` and the private `ValidateConnectionMoveAsync(Connection connection, ResourceContextId targetId, CancellationToken) : ValueTask<ResourceMovePreview>` (frozen signature for Plan 2).

**Interface member and implementation land in the same commit.** Add this to `IResourceOwnershipMoveService` in Step 3, not earlier:

```csharp
    ValueTask<ResourceMovePreview> PreviewConnectionMoveAsync(ConnectionId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default);
```

and the matching public method to the service:

```csharp
    /// <inheritdoc />
    public async ValueTask<ResourceMovePreview> PreviewConnectionMoveAsync(
        ConnectionId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Connection {id.Value} not found.");
        await AuthorizeAsync(ActionConstants.ConnectionUpdate, connection.GetIdentity(), cancellationToken).ConfigureAwait(false);
        return await ValidateConnectionMoveAsync(connection, targetResourceContextId, cancellationToken).ConfigureAwait(false);
    }
```

- [ ] **Step 1: Write the failing tests**

They fail with a compile error (`PreviewConnectionMoveAsync` does not exist), which is the honest red state — not a `NotSupportedException` thrown from a stub that had to be written first.

```csharp
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

public sealed class ConnectionMovePreviewTests
{
    [Fact]
    public async Task No_bindings_moves_cleanly()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeTrue();
    }

    [Fact]
    public async Task All_bindings_compatible_moves_cleanly_and_shows_used_by()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);
        var integration = await h.CreateIntegrationAsync(source.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);

        // Put the bound Integration in the target first, so the Connection's move into the target leaves
        // every reference valid. (Fixture-only rewrite; Plan 2 replaces this with the real move.)
        await h.MoveIntegrationDirectlyForFixtureAsync(integration.Id, target.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        preview.Blockers.Should().BeEmpty();
        var usedBy = preview.Effects.Should().ContainSingle(e => e.Code == ResourceMoveEffectCodes.UsedByIntegration).Subject;
        usedBy.RelatedResourceIds.Should().ContainSingle().Which.Should().Be(integration.Id.Value,
            "blast radius is shown even when CanMove is true (§8.5 confirmation UX)");
    }

    [Fact]
    public async Task One_incompatible_integration_blocks()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var connection = await h.CreateConnectionAsync(source.Id);
        var boundIntegration = await h.CreateIntegrationAsync(thirdParty.Id); // not source, not target
        await h.AddDirectBindingAsync(boundIntegration.Id, boundIntegration.ModuleId, connection.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        var blocker = preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.BindingIntegrationUnreachable).Subject;
        blocker.RelatedResourceId.Should().Be(boundIntegration.Id.Value);
    }

    [Fact]
    public async Task Inactive_and_retired_integration_bindings_still_count()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var connection = await h.CreateConnectionAsync(source.Id);
        var retiredIntegration = await h.CreateIntegrationAsync(thirdParty.Id, IntegrationState.Retired);
        await h.AddDirectBindingAsync(retiredIntegration.Id, retiredIntegration.ModuleId, connection.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeFalse("a Retired Integration's binding still counts toward blast radius (§8.5)");
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.BindingIntegrationUnreachable);
    }

    [Fact]
    public async Task Global_unowned_binding_requires_shared_target()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(ResourceContextIds.Shared);
        await h.AddDirectBindingAsync(integrationId: null!, "Sample.Mod", connection.Id); // legacy global binding has no IntegrationId

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.UnownedBindingRequiresShared);
    }

    [Fact]
    public async Task Moving_to_shared_is_never_blocked_by_bindings()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var elsewhere = await h.CreateApplicationAsync("Elsewhere");
        var connection = await h.CreateConnectionAsync(source.Id);
        var boundIntegration = await h.CreateIntegrationAsync(elsewhere.Id); // would normally block a move to `elsewhere`
        await h.AddDirectBindingAsync(boundIntegration.Id, boundIntegration.ModuleId, connection.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, ResourceContextIds.Shared);

        preview.CanMove.Should().BeTrue("every context can reach Shared, so moving to Shared is never blocked by bindings");
    }

    [Fact]
    public async Task Binding_with_missing_integration_is_advisory_not_a_blocker()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);
        await h.AddDirectBindingAsync(new IntegrationId(Guid.NewGuid().ToString("N")), "Sample.Mod", connection.Id); // no such Integration row

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        preview.Effects.Should().ContainSingle(e => e.Code == ResourceMoveEffectCodes.BindingReferencesMissingIntegration,
            "symmetric with the Integration validator's missing-connection advisory -- a dangling edge is surfaced, never silently skipped");
    }

    [Fact]
    public async Task Multiple_incompatible_integrations_each_produce_their_own_blocker()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var connection = await h.CreateConnectionAsync(source.Id);
        var first = await h.CreateIntegrationAsync(thirdParty.Id);
        var second = await h.CreateIntegrationAsync(source.Id);
        await h.AddDirectBindingAsync(first.Id, first.ModuleId, connection.Id);
        await h.AddDirectBindingAsync(second.Id, second.ModuleId, connection.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        var unreachable = preview.Blockers
            .Where(b => b.Code == ResourceMoveBlockerCodes.BindingIntegrationUnreachable)
            .ToList();
        unreachable.Should().HaveCount(2, "every affected Integration is listed -- this is the blast radius the operator must see");
        unreachable.Select(b => b.RelatedResourceId).Should().BeEquivalentTo([first.Id.Value, second.Id.Value]);
    }

    [Fact]
    public async Task Not_authorized_throws()
    {
        using var h = new ResourceOwnershipMoveTestHarness(grantedAction: null);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);

        var act = async () => await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Connection_update_permission_is_required_not_resource_context_manage()
    {
        using var h = new ResourceOwnershipMoveTestHarness(
            grantedAction: Apperture.Core.Domain.Governance.ActionConstants.ResourceContextManage,
            grantedResourceType: Apperture.Core.Domain.Governance.ResourceTypeConstants.ResourceContext);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);

        var act = async () => await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "holding resource_context.manage must not be enough -- connection.update is the required permission (§9.3)");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ConnectionMovePreviewTests"`
Expected: FAIL — compile error, `PreviewConnectionMoveAsync` does not exist yet.

- [ ] **Step 3: Add the interface member, the public preview method, and the validator**

Add the interface member and the public `PreviewConnectionMoveAsync` shown in this task's **Interfaces** section above, then add the validator to `ResourceOwnershipMoveService.cs`. Also add the new effect code to `ResourceMoveEffectCodes` (Task 1's file):

```csharp
    /// <summary>Connection move (§8.5): a binding references an Integration row that no longer exists; advisory, never a blocker. Mirror of <see cref="BindingReferencesMissingConnection"/>.</summary>
    public const string BindingReferencesMissingIntegration = "binding_references_missing_integration";
```

```csharp
    private async ValueTask<ResourceMovePreview> ValidateConnectionMoveAsync(
        Connection connection, ResourceContextId targetId, CancellationToken cancellationToken)
    {
        var sourceId = connection.ResourceContextId;
        var sourceRef = await ResolveRefAsync(sourceId, cancellationToken).ConfigureAwait(false);
        var target = await _resourceContexts.GetAsync(targetId, cancellationToken).ConfigureAwait(false);

        var blockers = new List<ResourceMoveBlocker>();
        var effects = new List<ResourceMoveEffect>();

        if (target is null)
        {
            blockers.Add(new ResourceMoveBlocker(ResourceMoveBlockerCodes.TargetNotFound, $"ResourceContext '{targetId.Value}' does not exist.", null, null, null, null));
            return BuildConnectionPreview(connection.Id.Value, sourceRef, await ResolveRefAsync(targetId, cancellationToken), blockers, effects);
        }
        if (!target.IsActive)
        {
            blockers.Add(new ResourceMoveBlocker(ResourceMoveBlockerCodes.TargetArchived, $"'{target.Name}' is archived and cannot receive new ownership.", ResourceMoveResourceTypes.ResourceContext, target.Id.Value, target.Name, target.Id.Value));
            return BuildConnectionPreview(connection.Id.Value, sourceRef, target.ToRef(), blockers, effects);
        }
        if (string.Equals(sourceId.Value, targetId.Value, StringComparison.Ordinal))
        {
            blockers.Add(new ResourceMoveBlocker(ResourceMoveBlockerCodes.AlreadyInTarget, "The connection is already owned by the requested context.", null, null, null, null));
            return BuildConnectionPreview(connection.Id.Value, sourceRef, target.ToRef(), blockers, effects);
        }

        // Deterministic ordering (Global Constraints): GetByConnectionIdAsync has no ORDER BY.
        var bindings = (await _bindingRepository.GetByConnectionIdAsync(connection.Id, cancellationToken).ConfigureAwait(false))
            .OrderBy(b => b.Id.Value, StringComparer.Ordinal)
            .ToList();

        var usedByIntegrationIds = new List<string>();
        foreach (var binding in bindings)
        {
            if (binding.IntegrationId is { } integrationId)
            {
                usedByIntegrationIds.Add(integrationId.Value);
                var integration = await _integrationRepository.GetByIdAsync(integrationId, cancellationToken).ConfigureAwait(false);
                if (integration is null)
                {
                    // Mirror of the Integration validator's missing-connection advisory: surface the
                    // dangling edge, never silently skip it. The move cannot make this worse.
                    effects.Add(new ResourceMoveEffect(
                        ResourceMoveEffectCodes.BindingReferencesMissingIntegration,
                        $"Binding for slot '{binding.SlotName}' references a missing integration; the move does not make this worse.",
                        ResourceMoveResourceTypes.Binding, [binding.Id.Value]));
                }
                else if (!ResourceContextReferenceRule.CanReference(integration.ResourceContextId, targetId))
                {
                    blockers.Add(new ResourceMoveBlocker(
                        ResourceMoveBlockerCodes.BindingIntegrationUnreachable,
                        $"Integration '{integration.DisplayName}' (in '{integration.ResourceContextId.Value}') would no longer be able to reference this connection.",
                        ResourceMoveResourceTypes.Integration, integration.Id.Value, integration.DisplayName, integration.ResourceContextId.Value));
                }
            }
            else if (!ResourceContextIds.IsShared(targetId))
            {
                blockers.Add(new ResourceMoveBlocker(
                    ResourceMoveBlockerCodes.UnownedBindingRequiresShared,
                    "A platform-level binding uses this connection; it must stay in Shared.",
                    ResourceMoveResourceTypes.Binding, binding.Id.Value, null, null));
            }
        }

        if (usedByIntegrationIds.Count > 0)
        {
            effects.Add(new ResourceMoveEffect(
                ResourceMoveEffectCodes.UsedByIntegration,
                $"Used by {usedByIntegrationIds.Count} integration(s).",
                ResourceMoveResourceTypes.Integration, usedByIntegrationIds));
        }

        return BuildConnectionPreview(connection.Id.Value, sourceRef, target.ToRef(), blockers, effects);
    }

    private static ResourceMovePreview BuildConnectionPreview(
        string resourceId, ResourceContextRef source, ResourceContextRef target,
        List<ResourceMoveBlocker> blockers, List<ResourceMoveEffect> effects) =>
        new(ResourceMoveResourceTypes.Connection, resourceId, source, target, blockers.Count == 0, blockers, effects);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ConnectionMovePreviewTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceOwnershipMoveService.cs \
        tests/Apperture.UnitTests/ResourceContexts/ResourceOwnershipMoveTestHarness.cs \
        tests/Apperture.UnitTests/ResourceContexts/ConnectionMovePreviewTests.cs
git commit -m "CTX-3B: implement Connection move preview validator with full blocker-code coverage"
```

---

### Task 6: Schedule preview validation, and DTO immutability guard

**Files:**
- Modify: `src/Apperture.Core/Apperture.Core.Application/ResourceContexts/IResourceOwnershipMoveService.cs` (add `PreviewScheduleMoveAsync`)
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceOwnershipMoveService.cs`
- Test: `tests/Apperture.UnitTests/ResourceContexts/ScheduleMovePreviewTests.cs`
- Test: `tests/Apperture.UnitTests/ResourceContexts/ResourceMoveDtoImmutabilityTests.cs`

**Interfaces:**
- Produces: `IResourceOwnershipMoveService.PreviewScheduleMoveAsync` and the private `ValidateScheduleMoveAsync(ScheduleDefinition schedule, ResourceContextId targetId, CancellationToken) : ValueTask<ResourceMovePreview>` (frozen for Plan 2).

**Interface member, public method, and its authorization helper all land in this task's commit:**

```csharp
    // IResourceOwnershipMoveService
    ValueTask<ResourceMovePreview> PreviewScheduleMoveAsync(ScheduleId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default);
```

```csharp
    // ResourceOwnershipMoveService
    /// <inheritdoc />
    public async ValueTask<ResourceMovePreview> PreviewScheduleMoveAsync(
        ScheduleId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default)
    {
        var schedule = await _scheduleRepository.GetDefinitionAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Schedule {id.Value} not found.");
        await AuthorizeScheduleAsync(cancellationToken).ConfigureAwait(false);
        return await ValidateScheduleMoveAsync(schedule, targetResourceContextId, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask AuthorizeScheduleAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.ScheduleManage, new ResourceDescriptor(ResourceTypeConstants.Schedule, ScopeKeyConstants.Instance)),
                null),
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to move schedules.");
    }
```

- [ ] **Step 1: Write the failing tests**

```csharp
using Apperture.Core.Application.ResourceContexts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

public sealed class ScheduleMovePreviewTests
{
    [Fact]
    public async Task Attached_schedule_blocks_with_link_to_integration()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var schedule = await h.CreateAttachedScheduleAsync(integration.Id);

        var preview = await h.Service.PreviewScheduleMoveAsync(schedule.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        var blocker = preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.ScheduleAttachedToIntegration).Subject;
        blocker.RelatedResourceType.Should().Be(ResourceMoveResourceTypes.Integration);
        blocker.RelatedResourceId.Should().Be(integration.Id.Value);
    }

    [Fact]
    public async Task Standalone_schedule_moves()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var schedule = await h.CreateStandaloneScheduleAsync(source.Id);

        var preview = await h.Service.PreviewScheduleMoveAsync(schedule.Id, target.Id);

        preview.CanMove.Should().BeTrue();
    }

    [Fact]
    public async Task Not_authorized_throws()
    {
        using var h = new ResourceOwnershipMoveTestHarness(grantedAction: null);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var schedule = await h.CreateStandaloneScheduleAsync(source.Id);

        var act = async () => await h.Service.PreviewScheduleMoveAsync(schedule.Id, target.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Schedule_manage_permission_is_required_not_resource_context_manage()
    {
        using var h = new ResourceOwnershipMoveTestHarness(
            grantedAction: Apperture.Core.Domain.Governance.ActionConstants.ResourceContextManage,
            grantedResourceType: Apperture.Core.Domain.Governance.ResourceTypeConstants.ResourceContext);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var schedule = await h.CreateStandaloneScheduleAsync(source.Id);

        var act = async () => await h.Service.PreviewScheduleMoveAsync(schedule.Id, target.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "holding resource_context.manage must not be enough -- schedule.manage is the required permission (§9.3)");
    }
}
```

```csharp
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Connector;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// Design gate §8.2: movement must never become a field on an ordinary update DTO. The assertions cover
/// both the application-layer request records **and** the API request bodies, because the API body is the
/// boundary an operator actually reaches — an ownership field added there would be reachable even if the
/// application-layer record stayed clean.
/// </summary>
public sealed class ResourceMoveDtoImmutabilityTests
{
    [Fact]
    public void UpdateIntegrationRequest_has_no_ResourceContextId()
    {
        typeof(UpdateIntegrationRequest).GetProperty("ResourceContextId").Should().BeNull();
    }

    [Fact]
    public void UpdateConnectionRequest_has_no_ResourceContextId()
    {
        typeof(UpdateConnectionRequest).GetProperty("ResourceContextId").Should().BeNull();
    }

    [Fact]
    public void UpdateIntegrationRequestBody_has_no_ResourceContextId()
    {
        typeof(UpdateIntegrationRequestBody).GetProperty("ResourceContextId").Should().BeNull();
    }

    [Fact]
    public void UpdateConnectionRequestBody_has_no_ResourceContextId()
    {
        typeof(UpdateConnectionRequestBody).GetProperty("ResourceContextId").Should().BeNull();
    }

    /// <summary>
    /// The Schedule body gets both assertions. <c>ResourceContextId</c> for the same reason as the other
    /// two families; <c>IntegrationId</c> because <c>SqlScheduleRepository.UpdateDefinitionAsync</c>
    /// genuinely does write <c>e.IntegrationId</c> while deliberately never writing
    /// <c>ResourceContextId</c> — so a body field that re-attached a standalone Schedule to an Integration
    /// would create permanent <c>schedule_context_inconsistent</c> drift that nothing can repair and that
    /// permanently blocks moving that Integration. Verified at review time: the body has neither today.
    /// </summary>
    [Fact]
    public void UpdateScheduleRequestBody_has_neither_ResourceContextId_nor_IntegrationId()
    {
        typeof(UpdateScheduleRequestBody).GetProperty("ResourceContextId").Should().BeNull();
        typeof(UpdateScheduleRequestBody).GetProperty("IntegrationId").Should().BeNull(
            "re-attaching a Schedule through an ordinary update would rewrite IntegrationId without rewriting ResourceContextId");
    }
}
```

Note on project references: `UpdateIntegrationRequestBody`/`UpdateConnectionRequestBody`/`UpdateScheduleRequestBody` live in `Apperture.Management.Api.Controllers`. Confirm `Apperture.UnitTests` already references `Apperture.Management.Api`; if it does not, place these three assertions in whichever test project does, rather than adding a new project reference for a reflection test.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ScheduleMovePreviewTests"`
Expected: FAIL — compile error, `PreviewScheduleMoveAsync` does not exist yet. The DTO immutability tests should already PASS (nothing has touched those DTOs) — run them too to confirm they pass today, proving the assertion is meaningful going forward, not just untested.

- [ ] **Step 3: Add the interface member, the public preview method, `AuthorizeScheduleAsync`, and the validator**

```csharp
    private async ValueTask<ResourceMovePreview> ValidateScheduleMoveAsync(
        ScheduleDefinition schedule, ResourceContextId targetId, CancellationToken cancellationToken)
    {
        var sourceId = schedule.ResourceContextId
            ?? throw new InvalidOperationException($"Schedule {schedule.Id.Value} has no resolved ResourceContext.");
        var sourceRef = await ResolveRefAsync(sourceId, cancellationToken).ConfigureAwait(false);
        var target = await _resourceContexts.GetAsync(targetId, cancellationToken).ConfigureAwait(false);

        var blockers = new List<ResourceMoveBlocker>();
        var effects = new List<ResourceMoveEffect>();

        if (schedule.IntegrationId is { } integrationId)
        {
            var integration = await _integrationRepository.GetByIdAsync(integrationId, cancellationToken).ConfigureAwait(false);
            blockers.Add(new ResourceMoveBlocker(
                ResourceMoveBlockerCodes.ScheduleAttachedToIntegration,
                "Move the integration instead; its schedules move with it.",
                ResourceMoveResourceTypes.Integration, integrationId.Value, integration?.DisplayName, integration?.ResourceContextId.Value));
            return BuildSchedulePreview(schedule.Id.Value, sourceRef, target?.ToRef() ?? await ResolveRefAsync(targetId, cancellationToken), blockers, effects);
        }

        if (target is null)
        {
            blockers.Add(new ResourceMoveBlocker(ResourceMoveBlockerCodes.TargetNotFound, $"ResourceContext '{targetId.Value}' does not exist.", null, null, null, null));
            return BuildSchedulePreview(schedule.Id.Value, sourceRef, await ResolveRefAsync(targetId, cancellationToken), blockers, effects);
        }
        if (!target.IsActive)
        {
            blockers.Add(new ResourceMoveBlocker(ResourceMoveBlockerCodes.TargetArchived, $"'{target.Name}' is archived and cannot receive new ownership.", ResourceMoveResourceTypes.ResourceContext, target.Id.Value, target.Name, target.Id.Value));
            return BuildSchedulePreview(schedule.Id.Value, sourceRef, target.ToRef(), blockers, effects);
        }
        if (string.Equals(sourceId.Value, targetId.Value, StringComparison.Ordinal))
        {
            blockers.Add(new ResourceMoveBlocker(ResourceMoveBlockerCodes.AlreadyInTarget, "The schedule is already owned by the requested context.", null, null, null, null));
            return BuildSchedulePreview(schedule.Id.Value, sourceRef, target.ToRef(), blockers, effects);
        }

        return BuildSchedulePreview(schedule.Id.Value, sourceRef, target.ToRef(), blockers, effects);
    }

    private static ResourceMovePreview BuildSchedulePreview(
        string resourceId, ResourceContextRef source, ResourceContextRef target,
        List<ResourceMoveBlocker> blockers, List<ResourceMoveEffect> effects) =>
        new(ResourceMoveResourceTypes.Schedule, resourceId, source, target, blockers.Count == 0, blockers, effects);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ScheduleMovePreviewTests|FullyQualifiedName~ResourceMoveDtoImmutabilityTests"`
Expected: PASS.

- [ ] **Step 5: Self-review — spec coverage pass**

Re-read design gate §8.3–§8.6 and confirm every blocker/effect code has at least one test:
`target_not_found`, `target_archived`, `already_in_target`, `binding_connection_unreachable`, `schedule_context_inconsistent`, `binding_integration_unreachable`, `unowned_binding_requires_shared`, `schedule_attached_to_integration`, `schedules_move_with_integration`, `binding_references_missing_connection`, `binding_references_missing_integration`, `used_by_integration`. All twelve are covered above.

Also confirm, by reading the finished files rather than by memory:

- **No production stub exists anywhere in this plan's output.** `grep -rn "NotSupportedException" src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceOwnershipMoveService.cs src/Apperture.Core/Apperture.Core.Application/ResourceContexts/` must return nothing.
- **No `Move*Async` member exists yet** — on the interface or the class. `grep -n "MoveIntegrationAsync\|MoveConnectionAsync\|MoveScheduleAsync"` over both files must return nothing. Plan 2 introduces them.
- **Each of the three `Preview*Async` members was added in the same commit as its implementation** (`git log --oneline -4` and inspect the three commits).
- **Per-family authorization coverage is complete:** each of the three families has both a `Not_authorized_throws` test and a `resource_context.manage`-alone-is-insufficient test. Nine authorization assertions across six tests.
- **Determinism:** both multi-blocker tests assert every offending resource is reported with its own related-resource fields, and `Blocker_order_is_deterministic_across_repeated_previews` asserts stable ordering. Note that the InMemory provider returns insertion order, so that test is a regression guard for the sort call rather than proof against SQL Server; the sort in the validator is what makes the guarantee, and it is provider-independent.

- [ ] **Step 6: Full plan-scope test run**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceContexts"`
Expected: all pre-existing CTX-1/CTX-2/CTX-3A ResourceContexts tests plus this plan's new tests PASS (no regressions).

- [ ] **Step 7: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceOwnershipMoveService.cs \
        tests/Apperture.UnitTests/ResourceContexts/ScheduleMovePreviewTests.cs \
        tests/Apperture.UnitTests/ResourceContexts/ResourceMoveDtoImmutabilityTests.cs
git commit -m "CTX-3B: implement Schedule move preview validator and DTO immutability guard"
```

---

## Legacy schedule ownership drift — preflight owed to Plan 2 (product decision P3)

`ValidateIntegrationMoveAsync` treats **any** attached Schedule whose persisted context differs from its Integration's as a hard blocker (`schedule_context_inconsistent`), and deliberately does not repair it. There is no schedule-context repair operation anywhere in the platform, and an attached Schedule cannot move independently, so an Integration with drifted schedules is **permanently unmovable** through the supported API.

That is the intended semantics — a move must never silently auto-repair ownership drift — but it is only safe if the drift set is empty. **Plan 2 Task 1 runs the preflight query** for `ScheduleDefinitions` whose persisted ownership disagrees with their attached Integration, before movement ships:

- **Count = 0:** keep `schedule_context_inconsistent` as a strict hard blocker, add no repair operation. This is the expected outcome (CTX-1 backfilled every row to Shared, and `SqlScheduleRepository.ToDefinition` coalesces a NULL column to Shared).
- **Count > 0:** stop, inspect the actual rows, and report them before implementation continues. Prefer a one-time support/DBA repair over building a generalized operator repair feature, unless the evidence shows such a feature is genuinely needed.

This plan needs no change either way — it is recorded here so the blocker's consequences are understood by whoever implements the validator.

## Out of scope for this plan

- No locking, no ownership mutation gate, no `UPDLOCK`, no transactions, no mutation of `ResourceContextId` anywhere (Plan 2).
- No `Move*Async` members at all — not on the interface, not on the class. Plan 2 declares and implements all three together.
- No readiness issue code (Plan 3).
- No audit records (Plan 4).
- No HTTP controllers, no client methods, no UI (Plan 5).
- No Integration hard-delete referential-lifecycle work (Plan 6a).

## Validation for this plan

- `dotnet build Apperture.slnx` — 0 errors.
- `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj` — full project green, including all new tests.
- No `Apperture.IntegrationTests`, `Apperture.ArchTests`, `Ui.Shared.Tests`, or `Ui.Client.Tests` changes in this plan; their existing baselines (204/204 IntegrationTests, etc.) are unaffected and should still be run once at the end of this plan to confirm no incidental breakage: `dotnet test Apperture.slnx`.
