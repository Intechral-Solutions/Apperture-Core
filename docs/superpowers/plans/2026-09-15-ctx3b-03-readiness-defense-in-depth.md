# CTX-3B Plan 3 — Readiness Defense-in-Depth (`binding_cross_context`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the `binding_cross_context` readiness issue so that if a cross-context-invalid binding ever exists — through old data, a direct DB edit, a bug, or a future regression — the affected Integration is reported not runnable, independent of whatever created the inconsistency.

**Architecture:** One additional check inside `IntegrationDispatchReadinessEvaluator.EvaluateAsync`, applied uniformly over every binding with a `ConnectionId` regardless of which of the two existing branches (SDK-6C configuration-based, or legacy slot-binding) produced the rest of the issues — cross-context validity is a platform invariant, not a module-configuration concern. This should be unreachable through the normal APIs after Plans 1-2, so it is tested by seeding the invalid state directly.

**Verified against the evaluator at review time:** `_connectionRepository` is already a constructor dependency (parameter 5); `bindings` is already a parameter of `EvaluateAsync` (line 69), so it is in scope at the insertion point; `runnable` at line 138 requires `blockingIssueCount == 0`, so a blocking issue genuinely prevents execution; and ownership is read from the persisted `Integrations.ResourceContextId` / `Connections.ResourceContextId` columns, never from request-scoped view state — which is what makes this check catch directly-injected invalid data.

**Performance note — this plan must not add an N+1.** `EvaluateLegacySlotBindingsAsync` already performs a per-binding `_connectionRepository.GetByIdAsync` (line 213), and readiness is evaluated **per Integration by the fleet list** that backs `/integrations` (design gate R21 already flags that N+1). Adding a second per-binding read in the legacy path plus a fresh N+1 in the configuration path would be a real regression on a hot list endpoint. Task 1 therefore loads each binding's Connection **once** into a dictionary at the top of `EvaluateAsync` and has both the new check and the legacy path read from it — a net improvement over today, not a regression.

**Tech Stack:** C#/.NET 9, xUnit, FluentAssertions, EF Core InMemory (no SQL Server needed — readiness evaluation is pure read/compute, no locking, no gate).

**Spec:** `docs/01-architecture/transitions/resource-contexts/ctx3-design-gate.md` §8.7 ("Defense in depth"), §10.2 (B3), §12 R16, §14 CTX-3B acceptance criteria (readiness bullet).

## Global Constraints

- The check applies to **every** binding with a `ConnectionId`, in both the SDK-6C configuration-evidence path and the legacy slot-binding path — do not scope it to only one branch.
- This is defense-in-depth: it must never be reachable by the normal move/bind/attach APIs after Plans 1-2. One test proves the backstop works by seeding the invalid row directly via `AppertureDbContext`, bypassing every service; a companion test proves a **successful move leaves readiness clean**, so the check cannot start firing on legitimate state.
- Reuse `ResourceContextReferenceRule.CanReference` — do not write a second copy of the reachability rule.
- **Load each binding's Connection exactly once per evaluation.** No new N+1, and the existing one in the legacy path is folded into the same lookup.
- **The issue message must be actionable on its own.** This plan may land before Plan 5's movement UI exists, so the message states both the problem and what the operator can do about it, without assuming a move dialog is reachable.
- Do not add a new "fix this" mutation endpoint in this plan; the operator-facing "Fix-now path in the hub Connections tab" (§8.7) is UI copy/link work that belongs with Plan 5's movement UI, once the move dialog it links to actually exists.

---

## File Structure

**Modified files:**
- `src/Apperture.Core/Apperture.Core.Application/Integration/IntegrationReadinessModels.cs` — add `IntegrationReadinessIssueCodes.BindingCrossContext`.
- `src/Apperture.Core/Apperture.Core.Application/Integration/IntegrationDispatchReadinessEvaluator.cs` — add the cross-context check.

**New files:**
- `tests/Apperture.UnitTests/Integration/BindingCrossContextReadinessTests.cs`

**Interfaces produced for later plans:**
- `IntegrationReadinessIssueCodes.BindingCrossContext = "binding_cross_context"` — Plan 5's UI reads this code to render the "Fix now" link/copy on the hub's Connections tab.

---

### Task 0: Preflight — does any cross-context binding already exist?

Before merging this plan, run the following against a representative database (dev, and any reachable production-shaped snapshot):

```sql
SELECT b.Id AS BindingId, b.SlotName,
       b.IntegrationId, i.ResourceContextId AS IntegrationContext,
       b.ConnectionId,  c.ResourceContextId AS ConnectionContext
FROM   [Bindings] b
JOIN   [Integrations] i ON i.Id = b.IntegrationId
JOIN   [Connections]  c ON c.Id = b.ConnectionId
WHERE  b.ConnectionId IS NOT NULL
  AND  c.ResourceContextId <> i.ResourceContextId
  AND  c.ResourceContextId <> 'shared';
```

- [ ] **Step 1: Run it and record the count**

**Expected: 0.** CTX-1 backfilled every row to Shared, CTX-3A shipped no move operation, and CTX-3A's in-flow Connection creation constrains the choice to the Integration's own Context or Shared.

**If the count is not 0**, this plan makes those Integrations *not runnable the moment it deploys*. Stop, report the rows, and decide deliberately whether to correct the data first (once CTX-3B movement exists, moving one of the two resources is the correct remedy) before merging. Do not discover this in production; do not weaken the check to avoid it.

---

### Task 1: Add the issue code and the evaluator check

**Files:**
- Modify: `src/Apperture.Core/Apperture.Core.Application/Integration/IntegrationReadinessModels.cs`
- Modify: `src/Apperture.Core/Apperture.Core.Application/Integration/IntegrationDispatchReadinessEvaluator.cs`
- Test: `tests/Apperture.UnitTests/Integration/BindingCrossContextReadinessTests.cs`

**Interfaces:**
- Consumes: `ResourceContextReferenceRule.CanReference`, `IConnectionRepository.GetByIdAsync` (already injected into the evaluator).
- Produces: `IntegrationReadinessIssueCodes.BindingCrossContext`.

- [ ] **Step 1: Write the failing test**

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
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Integration;

/// <summary>
/// Design gate §8.7 "Defense in depth": a cross-context-invalid binding should be unreachable through
/// the normal APIs after CTX-3B Plans 1-2, but if one exists anyway (old data, a direct DB edit, a bug),
/// the Integration must be reported not runnable. Seeds the invalid row directly, bypassing every
/// service, exactly as the design gate specifies.
/// </summary>
public sealed class BindingCrossContextReadinessTests
{
    [Fact]
    public async Task Seeded_cross_context_binding_makes_integration_not_runnable()
    {
        using var db = new AppertureDbContext(
            new DbContextOptionsBuilder<AppertureDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        db.ResourceContexts.Add(new ResourceContextEntity
        {
            Id = ResourceContextIds.SharedValue, Name = "Shared", Slug = "shared",
            Kind = ResourceContextKinds.Shared, State = ResourceContextStates.Active, IsReserved = true,
            CreatedAtUtc = DateTimeOffset.UnixEpoch, UpdatedAtUtc = DateTimeOffset.UnixEpoch
        });
        var resourceContexts = new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System);
        var appA = await resourceContexts.CreateApplicationContextAsync("App A");
        var appB = await resourceContexts.CreateApplicationContextAsync("App B");

        db.Integrations.Add(new IntegrationEntity
        {
            Id = "xctx-int", DisplayName = "Cross Context Integration", ModuleId = "Sample.Mod",
            ResourceContextId = appA.Id.Value, State = 0, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        db.Connections.Add(new ConnectionEntity
        {
            Id = "xctx-conn", ConnectorDefinitionId = "test.connector", Name = "Cross Context Connection",
            ConfigJson = "{}", SecretRefsJson = "{}", ResourceContextId = appB.Id.Value, State = 0
        });
        db.Bindings.Add(new BindingEntity
        {
            Id = "xctx-binding", IntegrationId = "xctx-int", ModuleId = "Sample.Mod", SlotName = "primary",
            ContractType = "test.contract", ConnectionId = "xctx-conn", State = 0
        });
        await db.SaveChangesAsync();

        var integrationRepository = new SqlIntegrationRepository(db);
        var connectionRepository = new SqlConnectionRepository(db);
        var bindingRepository = new SqlBindingRepository(db);
        var evaluator = new IntegrationDispatchReadinessEvaluator(
            integrationRepository,
            bindingRepository,
            new SqlScheduleRepository(db),
            new EmptySlotRegistry(),
            connectionRepository,
            new BindingEligibilityService(connectionRepository, new PermissiveRegistry()),
            new AlwaysOkVerificationPolicy(),
            new NoModuleConfigurationRegistry(),
            new IntegrationConfigurationEvidenceBuilder(new NoModuleConfigurationRegistry()),
            TimeProvider.System);

        var projection = await evaluator.EvaluateForDispatchAsync(new IntegrationId("xctx-int"), procedureId: null);

        projection!.IsRunnable.Should().BeFalse();
        var issue = projection.Issues.Should().ContainSingle(i => i.Code == IntegrationReadinessIssueCodes.BindingCrossContext).Subject;
        issue.Message.Should().Contain("Remove this binding",
            "the message must tell the operator what to do, because this plan can land before Plan 5's move UI exists");
    }

    [Fact]
    public async Task A_successful_move_leaves_readiness_clean()
    {
        // The complement of the test above: the backstop must fire on genuinely invalid data and must NOT
        // fire on the state a legitimate CTX-3B move produces. Without this, a too-eager check would make
        // every moved Integration unrunnable and nothing would catch it.
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var sharedConnection = await h.CreateConnectionAsync(ResourceContextIds.Shared);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, sharedConnection.Id);

        var moved = await h.Service.MoveIntegrationAsync(integration.Id, target.Id);
        moved.CanMove.Should().BeTrue();

        var projection = await h.BuildReadinessEvaluator().EvaluateForDispatchAsync(integration.Id, procedureId: null);

        projection!.Issues.Should().NotContain(i => i.Code == IntegrationReadinessIssueCodes.BindingCrossContext,
            "a Shared connection stays reachable from every context, so a legitimate move must not trip the backstop");
    }

    private sealed class EmptySlotRegistry : IModuleSlotRegistry
    {
        public IReadOnlyList<ModuleSlotDescriptor> GetSlotsForModule(string moduleId) => [];
    }

    private sealed class AlwaysOkVerificationPolicy : IConnectionVerificationReadinessPolicy
    {
        public IntegrationReadinessIssueSeverity ResolveSeverity(string moduleId, string slotName, ConnectionVerificationStatus status) =>
            IntegrationReadinessIssueSeverity.Advisory;
    }

    private sealed class NoModuleConfigurationRegistry : IIntegrationConfigurationRegistry
    {
        public bool TryGetModuleConfiguration(string moduleId, out ModuleConfigurationDeclaration configuration)
        {
            configuration = null!;
            return false;
        }
    }

    private sealed class PermissiveRegistry : IConnectorDefinitionRegistry
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

Adjust the exact constructor parameters of `IIntegrationConfigurationRegistry`/`ModuleConfigurationDeclaration`/`IntegrationConfigurationEvidenceBuilder`/`IModuleSlotRegistry`/`ModuleSlotDescriptor`/`IConnectionVerificationReadinessPolicy` to their real current shapes if they differ from the above at implementation time — these are read from `IntegrationDispatchReadinessEvaluator`'s constructor signature (already confirmed present in the codebase); this plan's job is to add one new check, not to re-verify every collaborator's exact shape, so the implementer should read the real interfaces before finalizing this test file rather than trust this sketch verbatim.

Two supporting notes for the second test:

- It calls the real `MoveIntegrationAsync`, so **this plan depends on Plan 2 having landed** — which is the epic's sequence anyway (1 → 2 → 3). If Plan 3 is ever pulled ahead of Plan 2, drop this one test and keep the seeded-invalid-data test, which has no such dependency.
- Add a small `BuildReadinessEvaluator()` factory to `ResourceOwnershipMoveTestHarness` that constructs `IntegrationDispatchReadinessEvaluator` over the harness's existing repositories with the same permissive collaborators the first test uses, so the two tests share one wiring rather than duplicating a ten-argument constructor.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~BindingCrossContextReadinessTests"`
Expected: FAIL — `IsRunnable` is currently `true` (no such check exists yet) and/or `IntegrationReadinessIssueCodes.BindingCrossContext` does not compile.

- [ ] **Step 3: Add the issue code**

In `IntegrationReadinessModels.cs`, add to `IntegrationReadinessIssueCodes`:

```csharp
    /// <summary>
    /// Design gate §8.7: a binding whose Connection is neither in the Integration's own ResourceContext
    /// nor Shared. Unreachable through the supported move/bind/attach APIs after CTX-3B; this exists as
    /// defense-in-depth against old data, direct DB edits, and future regressions.
    /// </summary>
    public const string BindingCrossContext = "binding_cross_context";
```

- [ ] **Step 4: Load each binding's Connection once, at the top of `EvaluateAsync`**

Immediately after `var issues = new List<IntegrationReadinessIssue>();` (line 74), add a single lookup that both the new check and the existing legacy path consume:

```csharp
        // One read per distinct bound Connection, for the whole evaluation. Readiness is evaluated per
        // Integration by the fleet list behind /integrations (design gate R21), so a per-binding repository
        // call inside each branch multiplies across the whole fleet. This lookup replaces the existing
        // per-binding read in EvaluateLegacySlotBindingsAsync as well, so the net query count goes DOWN.
        var boundConnections = new Dictionary<string, Connection?>(StringComparer.Ordinal);
        foreach (var connectionId in bindings
                     .Select(b => b.ConnectionId)
                     .Where(id => id is not null)
                     .Select(id => id!.Value)
                     .Distinct(StringComparer.Ordinal))
        {
            boundConnections[connectionId] = await _connectionRepository
                .GetByIdAsync(new ConnectionId(connectionId), cancellationToken)
                .ConfigureAwait(false);
        }
```

Then change `EvaluateLegacySlotBindingsAsync` to take `IReadOnlyDictionary<string, Connection?> boundConnections` and replace its `await _connectionRepository.GetByIdAsync(connId, cancellationToken)` at line 213 with a dictionary lookup. Its `ConnectionMissing` branch is unchanged — a `null` value in the dictionary means the same thing the `null` result meant.

- [ ] **Step 5: Add the check to the evaluator**

In `IntegrationDispatchReadinessEvaluator.EvaluateAsync`, after the existing `foreach (var s in schedules)` loop and before computing `blockingIssueCount`, add:

```csharp
        // Design gate §8.7 "Defense in depth": applies uniformly regardless of which branch above
        // produced the rest of the issues -- cross-context validity is a platform invariant, not a
        // module-configuration concern. Ownership is read from persisted columns, so this fires on
        // invalid data however it got there: old rows, a direct DB edit, or a future regression.
        foreach (var binding in bindings.OrderBy(b => b.Id.Value, StringComparer.Ordinal))
        {
            if (binding.ConnectionId is not { } connectionId)
                continue;

            if (!boundConnections.TryGetValue(connectionId.Value, out var connection) || connection is null)
                continue; // already covered by ConnectionMissing where applicable.

            if (!Apperture.Core.Application.ResourceContexts.ResourceContextReferenceRule.CanReference(integration.ResourceContextId, connection.ResourceContextId))
            {
                issues.Add(NewBlockingIssue(
                    IntegrationReadinessIssueCodes.BindingCrossContext,
                    $"Binding for slot '{binding.SlotName}' references connection '{connection.Name}' in ResourceContext " +
                    $"'{connection.ResourceContextId.Value}', which this integration (in '{integration.ResourceContextId.Value}') " +
                    $"cannot reach. Remove this binding, or move the integration or the connection so they share a context.",
                    binding.SlotName,
                    binding.Id.Value,
                    connectionId.Value));
            }
        }
```

The closing sentence is deliberate and load-bearing: this plan can land before Plan 5's movement UI exists, so the message must name the remedies (unbind, or move either side) without assuming a move dialog is reachable. Plan 5 later adds the "Fix now" link on top of this text; it does not replace it.

- [ ] **Step 6: Run to verify it passes**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~BindingCrossContextReadinessTests"`
Expected: PASS — both the seeded-invalid-data test and the successful-move-stays-clean test.

- [ ] **Step 7: Regression check on existing readiness tests**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~Readiness"`
Expected: all pre-existing readiness tests stay green — every existing fixture has same-context or Shared bindings, so the new check adds zero new issues for them. The `EvaluateLegacySlotBindingsAsync` signature change is internal to the evaluator, so `ConnectionMissing`/`InactiveBinding`/`MissingSlotBinding`/`BindingContractMismatch` behavior must be byte-identical; if any of those tests move, the refactor was wrong.

- [ ] **Step 8: Full UnitTests run**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj`
Expected: full project green.

- [ ] **Step 9: Commit**

```bash
git add src/Apperture.Core/Apperture.Core.Application/Integration/IntegrationReadinessModels.cs \
        src/Apperture.Core/Apperture.Core.Application/Integration/IntegrationDispatchReadinessEvaluator.cs \
        tests/Apperture.UnitTests/Integration/BindingCrossContextReadinessTests.cs
git commit -m "CTX-3B: add binding_cross_context readiness defense-in-depth"
```

---

## Out of scope for this plan

- No "Fix now" UI link/copy — that is Plan 5, once the move dialog exists to link to. The issue message is already actionable without it.
- No automatic remediation of a cross-context binding. Readiness only reports; it never mutates.
- No change to `BindingService`/`ResourceOwnershipMoveService` — Plans 1-2 already prevent this state through normal APIs; this plan only adds the backstop.
- No ownership mutation gate interaction. Readiness is pure read/compute and must never acquire the gate; a readiness evaluation on the fleet list must not serialize behind an operator's in-flight move.

## Validation for this plan

- **Task 0's preflight has been run and its count recorded** before this plan is merged.
- `dotnet build Apperture.slnx` — 0 errors.
- `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj` — full project green, including both new tests and all pre-existing readiness tests.
- Confirm the query count did not increase: the `boundConnections` lookup replaces the per-binding read in the legacy path rather than adding to it.
- No `IntegrationTests`/`ArchTests`/UI test changes expected; run `dotnet test Apperture.slnx` once at the end to confirm no incidental breakage.
