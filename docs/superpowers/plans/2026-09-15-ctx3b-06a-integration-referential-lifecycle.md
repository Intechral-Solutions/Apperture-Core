# CTX-3B Plan 6a — Integration Hard-Delete Referential-Lifecycle Audit and Fix

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the referential-lifecycle inventory the design gate requires before CTX-3B can close; make every Integration hard-delete blocker **both correct and clearable**; implement the one deliberate cascade cleanup; and add the `integration.deleted` audit record (product decision P2).

**Independence.** This plan has **no dependency on Plans 1-5**. It closes a pre-existing CTX-3A gap (`ctx3a-closure.md` §9) that happens to be gated on CTX-3B's closure. It can be executed at any point in the epic, including first, and it is reviewable entirely on its own. Only the closure report (Plan 6b) must genuinely come last.

## The reachability rule this plan is built on

> **Every blocker must have an operator-accessible remediation path.**

CTX-1 already shipped a blocker without one: the original archive rule ("owns nothing at all") became unreachable the moment an Application owned a single Connection or Schedule, because Connections only retire and Schedules have no delete path (design gate F4). CTX-3A had to refine the whole rule to escape it.

Two of this plan's blockers would repeat that mistake exactly, and both were confirmed by reading the code:

| Blocker | Clearable today? | Evidence |
|---|---|---|
| `NotificationDefinitions` | ❌ **No** | `SqlNotificationDefinitionStore` exposes `ListAsync`, `GetAsync`, `CreateUserDefinitionAsync`, `UpdateAsync`, `SetEnabledAsync`, `ExistsDisplayNameAsync`, `GetIntegrationOwnerContextIdsAsync` — **and no delete or remove method**. `INotificationDefinitionService` mirrors that surface. |
| `NotificationSubscriptions` | ✅ Yes | `SqlNotificationSubscriptionStore.RemoveAsync` exists. |
| `IntegrationAttentionRoutings` | ❌ **No** | `AttentionRoutingManagementService` exposes `GetEffectiveRoutingAsync`, `UpsertInstanceDefaultRoutingAsync`, `UpsertIntegrationRoutingAsync` — **upsert only, no remove**. The API has `GET` and `PUT api/integrations/{id}/attention-routing` and no `DELETE`. |

So this plan adds the two smallest deliberate clearing operations that make the approved blockers reachable (Tasks 2 and 3), then adds the blockers themselves (Task 4). **The blockers must not land before the clearing paths**, or the branch briefly ships exactly the trap this rule exists to prevent.

**Explicitly rejected alternative:** narrowing the NotificationDefinition blocker to *enabled* definitions. Disabling a definition does not make it safe to orphan — it is still independently meaningful operator configuration with its own rule, template, routing and subscriber list. A disabled definition is dormant, not disposable.

**Architecture:** `IntegrationManagementService.DeleteIntegrationAsync` already blocks on `Bindings` and `ScheduleDefinitions` row counts (verified, lines 205-211). This plan extends that same block-first pattern to the remaining current-state dependents, adds one deliberate cascade cleanup where a dependent has no independent meaning without its Integration (Integration-scoped `PlatformSettingValues`), and adds a single audit record for the destructive operation itself. Every CTX-2 historical/snapshot table is out of scope: none has a foreign key to Integration (CTX-1/CTX-2 established this deliberately), and a hard-deleted Integration is expected to leave a dangling id string in those rows forever, exactly as it already does today.

**Tech Stack:** C#/.NET 9, EF Core (InMemory for behavioral tests), xUnit, FluentAssertions, Markdown (the inventory doc).

**Spec:** `ctx3-design-gate.md` §13 CTX-3B step 7, §14 CTX-3B acceptance criteria (referential inventory bullet), `ctx3a-closure.md` §9. Product decision P2, and the final CTX-3B product ruling on blocker reachability.

---

## Global Constraints

- **Do not add a one-off guard for only `NotificationDefinitions`.** The design gate explicitly forbids solving that case without doing the inventory first. Task 1's inventory must be written and reviewed before any code changes.
- **Every CTX-2 historical/snapshot table stays untouched.** `NotificationInstances`, `AuditLogs`, `OperationRuns` and the rest keep their now-dangling `IntegrationId`/`NotificationDefinitionId` strings forever. That is correct behavior, not a bug, and it applies to definition deletion just as much as to Integration deletion.
- **Do not delete a `PlatformSettingValues` row while leaving an orphaned secret.** Integration-scoped setting values can hold an opaque `ISecretStore` reference; see Task 5. Row deletion alone is not sufficient cleanup.
- **Do not duplicate raw row removal for settings.** Delegate to the existing `IIntegrationSettingsService` lifecycle so secret disposal and per-key audit come from the one place that already owns them.
- **Definition deletion goes through the notification-management boundary.** `INotificationDefinitionService` → `NotificationDefinitionAuthorization` → `INotificationDefinitionStore`. Never raw-delete `NotificationDefinitions` rows from `IntegrationManagementService`, and never cascade them silently from Integration deletion — the operator deletes them explicitly, as a separate authorized act.
- **Reuse existing authorization conventions. Add no new permission.** Definition deletion uses `NotificationDefinitionAuthorization.RequireDefinitionAccessAsync(integrationId, manage: true, ct)` — the same `notification_definition.manage` + `integration.update` pair that already gates create and update. Attention-routing clearing mirrors `PutAttentionRouting`'s existing authorization exactly.
- **Do not expand into a general notification lifecycle redesign.** One delete operation, one clear operation, scoped to making the approved blockers reachable. No subscription-management redesign, no routing-model changes, no UI beyond what the existing error surfacing already provides.
- **Do not expand into Connection or Schedule lifecycle.** Their delete-eligibility, retirement cleanup and referential lifecycles are separate, later roadmap work.
- **Do not broaden into full Integration lifecycle auditing.** Exactly one new action, `integration.deleted` (P2), plus the definition-deletion audit that follows the notification module's own existing convention. Integration create/update/activate/deactivate/retire stay unaudited and are recorded as explicit debt in Plan 6b's closure.
- **Integration hard-delete is deliberately not a participant in Plan 2's ownership mutation gate**, and that is a considered decision rather than an oversight. Delete changes no `ResourceContextId` and creates no cross-context relationship: it removes the Integration entirely. A delete racing a move of the same Integration cannot produce an invalid persisted relationship — either the move's narrow `SetResourceContextIdAsync` finds no row and fails, or the delete's own guards run against state the move did not change. Do not add the gate here; if a future change makes delete rewrite ownership rather than remove it, revisit this.

---

## File Structure

**New files:**
- `docs/01-architecture/transitions/resource-contexts/ctx3b-integration-delete-referential-inventory.md` — the inventory deliverable.
- `tests/Apperture.UnitTests/Integration/IntegrationLifecycleTestHarness.cs`
- `tests/Apperture.UnitTests/Integration/IntegrationDeleteReferentialLifecycleTests.cs`
- `tests/Apperture.UnitTests/Notifications/NotificationDefinitionDeleteTests.cs`
- `tests/Apperture.UnitTests/Integration/IntegrationAttentionRoutingClearTests.cs`

**Modified files:**
- `src/Apperture.Core/Apperture.Core.Application/Notifications/INotificationDefinitionStore.cs` — add `DeleteAsync`.
- `src/Apperture.Core/Apperture.Core.Application/Notifications/INotificationDefinitionService.cs` — add `DeleteAsync`.
- `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlNotificationDefinitionStore.cs` — implement it, with its subordinate-data handling.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/NotificationDefinitionService.cs` — authorize and delegate.
- `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/NotificationDefinitionsController.cs` — `DELETE {id}`.
- `src/Apperture.Core/Apperture.Core.Application/Integration/IAttentionRoutingManagementService.cs` — add `ClearIntegrationRoutingAsync`.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/AttentionRoutingManagementService.cs` — implement it.
- `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationsController.cs` — `DELETE {id}/attention-routing`.
- `src/Apperture.ControlPlane/Apperture.Management.Client/` — client methods for both new operations.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationManagementService.cs` — the new blockers, the settings cleanup, the `integration.deleted` audit.
- `src/Apperture.Core/Apperture.Core.Domain/Governance/ActionConstants.cs` — one new action constant.

---

### Task 1: The referential-lifecycle inventory

**Files:**
- Create: `docs/01-architecture/transitions/resource-contexts/ctx3b-integration-delete-referential-inventory.md`

Documentation task, no TDD. **Write and review it before touching any code.**

- [ ] **Step 1: Re-derive the inventory from the code, not from this table**

Run both greps — the second is what catches dependents that reference an Integration through a generic string scope rather than a literally-named column, which is exactly the kind the design gate is worried about being missed:

```bash
grep -rln "IntegrationId" src/Apperture.Persistence/Apperture.Persistence.SqlServer/Entities/
grep -rn  "ScopeKind\|ScopeId" src/Apperture.Persistence/Apperture.Persistence.SqlServer/Entities/
```

The first returned **13 entity files** at review time. Reconcile your results against the table below and **add any entity the grep finds that is not listed** rather than assuming it is out of scope.

| Table | Reference | Classification | Decision | Clearable by |
|---|---|---|---|---|
| `Bindings` | `IntegrationId` | Current-state | Already blocked (existing) | `DELETE api/.../bindings/{id}` (exists) |
| `ScheduleDefinitions` | `IntegrationId` | Current-state | Already blocked (existing) | *No delete path — see follow-ups* |
| `NotificationDefinitions` | `IntegrationId` (no FK) | Current-state | **Block** (Task 4) | **`DELETE api/notification-definitions/{id}` — added by Task 2** |
| `NotificationSubscriptions` | `NotificationDefinitionId` **and** its own nullable `IntegrationId` | Current-state | **Block directly** (Task 4) | `SqlNotificationSubscriptionStore.RemoveAsync` (exists), plus Task 2's cascade |
| `IntegrationAttentionRoutings` | `IntegrationId` | Current-state | **Block** (Task 4) | **`DELETE api/integrations/{id}/attention-routing` — added by Task 3** |
| `PlatformSettingValues` | `ScopeKind = 'Integration'`, `ScopeId = <integrationId>` | Current-state, no independent meaning | **Cascade cleanup via the settings lifecycle** (Task 5) | n/a — cleaned automatically |
| `NotificationSubscriptionUserOverrides` | `NotificationDefinitionId`, `UserId`, `OperationalTeamId` — **no `IntegrationId`** | Current-state, downstream of `NotificationDefinitions` | No separate Integration-delete guard | Cascaded by Task 2's definition delete |
| `ScheduleAssignments` | `ScheduleDefinitionId` (no `IntegrationId`) | Current-state, **indirect** | No separate guard — transitively protected by the existing `ScheduleDefinitions` blocker | n/a |
| `ScheduleStates` | `ScheduleDefinitionId` (no `IntegrationId`) | Current-state, **indirect** | Same as `ScheduleAssignments` | n/a |
| `OperationRuns` | `IntegrationId` | **Historical (CTX-2)** | Not touched | — |
| `OperationStartRequests` | `IntegrationId` | **Historical (CTX-2)** | Not touched | — |
| `WorkflowInstances` | `IntegrationId` — **confirmed present** | **Historical (CTX-2)** | Not touched | — |
| `WorkflowStartRequests` | `IntegrationId` | **Historical (CTX-2)** | Not touched | — |
| `OperationItemizationEntities` | `IntegrationId` — **found during review; absent from the first draft** | **Historical (CTX-2)** | Not touched | — |
| `PlatformEvents` | `IntegrationId` | **Historical (CTX-2)** | Not touched | — |
| `NotificationInstances` | `IntegrationId` — a **literal column**, not join-derived as the first draft stated | **Historical (CTX-2)** | Not touched | — |
| `AttentionIncidents` | `IntegrationId` | **Historical (CTX-2)** | Not touched | — |
| `AuditLogs` | no `IntegrationId`; `ResourceContextId` snapshot only | **Historical (CTX-2)** | Not touched | — |

The **Clearable by** column is new and load-bearing: a blocker without an entry in it is a bug, not a policy.

- [ ] **Step 2: Write the document**

Structure: the confirmed gap; the reachability rule; the inventory table from Step 1; the Decisions section below; the follow-ups.

**Decisions to record:**

> **NotificationDefinitions — block delete, and make the blocker clearable.** An Integration-scoped NotificationDefinition is operator-created, independently meaningful configuration: its own rule, template, severity, routing and subscriber list. Deleting the Integration must not silently discard it, which is the same judgment already encoded in the existing Binding/Schedule block. But blocking on something with no delete path would make any Integration that ever acquired a definition permanently undeletable, so Task 2 adds an explicit, authorized definition-delete operation. **Narrowing the blocker to enabled definitions was considered and rejected:** disabling does not make a definition safe to orphan.
>
> **IntegrationAttentionRoutings — block delete, and make the blocker clearable.** `IntegrationAttentionRoutingEntity` carries escalation policy, reminder intervals and primary/escalation operational-team references — independent operator configuration with its own semantics. It is an *override* of the instance default, so the natural clearing operation is "remove the override and fall back to the instance default," which Task 3 adds.
>
> **NotificationSubscriptions — block delete, directly.** The rationale here is *not* "independently meaningful resource," and getting it right matters. `NotificationSubscriptionEntity` carries a required `NotificationDefinitionId` **and**, separately, its own **nullable `IntegrationId`**. A subscription can therefore be scoped to an Integration whose definition is *not* Integration-scoped — for example a Platform- or Module-scoped definition with a subscription narrowed to one Integration. Those rows are not covered by the NotificationDefinition blocker, so subscriptions need a blocker of their own. This is the dependency graph the code actually has, not an assumption.
>
> **PlatformSettingValues (Integration-scoped) — deliberate cascade cleanup, through the existing settings lifecycle.** An Integration-scoped setting value has no independent existence outside its Integration; unlike a NotificationDefinition it is not a named, independently addressable resource. Blocking delete on stray setting overrides would be friction with no operator benefit. **But row deletion alone is not sufficient cleanup** — see the sensitive-settings finding below.
>
> **Every CTX-2 historical/snapshot table — not touched.** These intentionally retain a now-possibly-dangling `IntegrationId` string forever, with no FK, exactly as CTX-1/CTX-2 established. `NotificationInstances` keeps its `NotificationDefinitionId` after a definition is deleted, for the same reason.

**Sensitive-settings finding to record verbatim** (this is the one that changes the implementation):

> Integration-scoped `PlatformSettingValues` **can** hold secrets by reference. `PlatformSettingDefinitionRegistry` states it plainly: a sensitive setting is *"Stored in the platform secret store; this setting retains only an opaque reference. Never stored as plaintext in PlatformSettingValues."* `IntegrationSettingsService` provisions those references through `PlatformSettingSecretMaterialProvisioner` at `ScopeKind = PlatformSettingScopeConstants.IntegrationKind`.
>
> The established lifecycle for removing such a value — visible in `IntegrationSettingsService`'s reset and clear paths — is three steps, in order: delete the scoped value, call `PlatformSettingSecretMaterialProvisioner.TryDisableOwnedSecretAsync(key, previousRef)`, and write the per-key audit record. `TryDisableOwnedSecretAsync` is deliberately conservative: it disables only secrets the platform owns (name matches `platform-setting:{key}` and provider matches), never an arbitrary or configuration-supplied GUID that might be shared.
>
> A raw `RemoveRange` on the setting rows would delete the **only pointer** to an **Active** secret in `ISecretStore`, permanently and silently. That is not acceptable cleanup. This plan therefore delegates to `IIntegrationSettingsService.ResetAsync` per key rather than removing rows directly.
>
> **Not atomic, and deliberately so.** The SQL row deletion and the external `ISecretStore` mutation are two different stores; they do not form one transaction and this plan does not pretend otherwise. It follows whatever failure behavior `IntegrationSettingsService` already has for reset, and tests that behavior rather than inventing a new one.

**Follow-ups to record (explicitly not this plan's scope):**

> - **`ScheduleDefinitions` blocks Integration delete and has no delete path either.** This is a pre-existing CTX-1 blocker, not one CTX-3B adds, and CTX-3A's closure already records "Schedules have no delete path at all" as deferred roadmap work. It is called out here because it is the one remaining Integration-delete blocker whose remediation path is missing, and because the reachability rule above should be applied to it when Schedule lifecycle work happens. CTX-3B deliberately does not widen into Schedule lifecycle.
> - Connection true-delete eligibility, Connection retirement cleanup, and their own referential lifecycles are separate, later roadmap work.
> - Whether the notification module wants an operator UI for definition deletion beyond the API added here is a notification-epic question, not a CTX-3B one.

- [ ] **Step 3: Commit**

```bash
git add docs/01-architecture/transitions/resource-contexts/ctx3b-integration-delete-referential-inventory.md
git commit -m "CTX-3B: write Integration hard-delete referential-lifecycle inventory"
```

---

### Task 2: NotificationDefinition deletion, through the notification-management boundary

**Files:**
- Modify: `src/Apperture.Core/Apperture.Core.Application/Notifications/INotificationDefinitionStore.cs`
- Modify: `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlNotificationDefinitionStore.cs`
- Modify: `src/Apperture.Core/Apperture.Core.Application/Notifications/INotificationDefinitionService.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/NotificationDefinitionService.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/NotificationDefinitionsController.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/` (interface + implementation)
- Test: `tests/Apperture.UnitTests/Notifications/NotificationDefinitionDeleteTests.cs`

**Eligibility rule, derived from existing house precedent.** `SqlNotificationDefinitionStore.UpdateAsync` already refuses to mutate rule or template JSON when `Origin is PlatformBuiltIn or ModuleBuiltIn` ("Built-in rule JSON is immutable."). Deletion follows the same line, one step stricter:

- **Deletable:** `Origin == NotificationDefinitionOrigins.UserCreated`.
- **Not deletable:** `PlatformBuiltIn` and `ModuleBuiltIn` — these are seeded by the platform and modules, and a delete would simply be re-seeded or break dispatch.

`CreateUserDefinitionAsync` sets `Scope = Integration` and `Origin = UserCreated` together, so in practice the definitions that block Integration delete and the definitions this operation accepts are the same set. **Do not rely on that coincidence** — Task 4's reachability test asserts it explicitly, so a future seed that carries an `IntegrationId` cannot silently recreate an unreachable blocker.

- [ ] **Step 1: Write the failing tests**

```csharp
using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications;

/// <summary>
/// CTX-3B Plan 6a Task 2. Integration hard-delete blocks on Integration-scoped NotificationDefinitions
/// (an intentional policy: they are independently meaningful operator configuration). This operation is
/// what makes that blocker clearable, so the blocker does not repeat CTX-1's unreachable-archive trap
/// (design gate F4).
/// </summary>
public sealed class NotificationDefinitionDeleteTests
{
    [Fact]
    public async Task User_created_integration_scoped_definition_can_be_deleted()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var definition = await h.CreateUserDefinitionAsync(integration.Id.Value);

        await h.Definitions.DeleteAsync(definition.Id.Value);

        (await h.Db.NotificationDefinitions.CountAsync(d => d.Id == definition.Id.Value)).Should().Be(0);
    }

    [Fact]
    public async Task Platform_built_in_definition_cannot_be_deleted()
    {
        using var h = new NotificationDefinitionTestHarness();
        var seeded = await h.SeedBuiltInDefinitionAsync(NotificationDefinitionOrigins.PlatformBuiltIn);

        var act = async () => await h.Definitions.DeleteAsync(seeded.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*built-in*");
        (await h.Db.NotificationDefinitions.CountAsync(d => d.Id == seeded.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Module_built_in_definition_cannot_be_deleted()
    {
        using var h = new NotificationDefinitionTestHarness();
        var seeded = await h.SeedBuiltInDefinitionAsync(NotificationDefinitionOrigins.ModuleBuiltIn);

        var act = async () => await h.Definitions.DeleteAsync(seeded.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Deleting_a_definition_removes_its_subscriptions_and_user_overrides()
    {
        // Subordinate current-state data, per the inventory's dependency graph: a subscription and a user
        // override are meaningless once their definition is gone, and nothing else can reach them.
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var definition = await h.CreateUserDefinitionAsync(integration.Id.Value);
        await h.AddSubscriptionAsync(definition.Id.Value, integration.Id.Value);
        await h.AddUserOverrideAsync(definition.Id.Value, userId: "user-1");

        await h.Definitions.DeleteAsync(definition.Id.Value);

        (await h.Db.NotificationSubscriptions.CountAsync(s => s.NotificationDefinitionId == definition.Id.Value)).Should().Be(0);
        (await h.Db.NotificationSubscriptionUserOverrides.CountAsync(o => o.NotificationDefinitionId == definition.Id.Value)).Should().Be(0);
    }

    [Fact]
    public async Task Deleting_a_definition_preserves_historical_notification_instances()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var definition = await h.CreateUserDefinitionAsync(integration.Id.Value);
        await h.AddNotificationInstanceAsync(definition.Id.Value, integration.Id.Value);

        await h.Definitions.DeleteAsync(definition.Id.Value);

        var instance = await h.Db.NotificationInstances.AsNoTracking()
            .SingleAsync(i => i.NotificationDefinitionId == definition.Id.Value);
        instance.NotificationDefinitionId.Should().Be(definition.Id.Value,
            "history records what happened; it keeps its dangling reference exactly as CTX-2 established");
    }

    [Fact]
    public async Task Deleting_a_definition_preserves_pre_existing_audit_rows_and_appends_one()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var definition = await h.CreateUserDefinitionAsync(integration.Id.Value);
        var auditBefore = h.Audit.Records.ToList();

        await h.Definitions.DeleteAsync(definition.Id.Value);

        h.Audit.Records.Take(auditBefore.Count).Should().BeEquivalentTo(auditBefore, "audit is append-only");
        h.Audit.Records.Should().HaveCount(auditBefore.Count + 1);
    }

    [Fact]
    public async Task Deleting_a_definition_requires_notification_manage_and_integration_update()
    {
        using var h = new NotificationDefinitionTestHarness(denyIntegrationUpdate: true);
        var integration = await h.CreateIntegrationAsync();
        var definition = await h.CreateUserDefinitionAsync(integration.Id.Value);

        var act = async () => await h.Definitions.DeleteAsync(definition.Id.Value);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "deletion reuses RequireDefinitionAccessAsync(manage: true) -- the same gate as create and update, no new permission");
    }

    [Fact]
    public async Task Deleting_a_missing_definition_is_a_no_op_result_not_a_crash()
    {
        using var h = new NotificationDefinitionTestHarness();

        var deleted = await h.Definitions.DeleteAsync("does-not-exist");

        deleted.Should().BeFalse();
    }
}
```

`NotificationDefinitionTestHarness` is new and small: real `NotificationDefinitionService` over `SqlNotificationDefinitionStore` on InMemory, a fixed actor, a configurable authorization double mirroring `NotificationDefinitionAuthorization`'s two gates, a recording `IAuditService`, and the seed helpers used above. Follow `ManagementServiceTestHarness`'s shape.

- [ ] **Step 2: Run to verify they fail**

Expected: FAIL — compile error, `DeleteAsync` does not exist on the service or the store.

- [ ] **Step 3: Add `DeleteAsync` to the store**

```csharp
    // INotificationDefinitionStore
    /// <summary>
    /// Deletes a user-created definition and its subordinate current-state data (subscriptions and
    /// subscription user overrides). Returns false when no such definition exists.
    /// </summary>
    /// <remarks>
    /// Refuses built-in definitions, mirroring <see cref="UpdateAsync"/>'s existing immutability rule for
    /// built-in rule/template JSON. Historical rows — <c>NotificationInstances</c> above all — keep their
    /// now-dangling <c>NotificationDefinitionId</c>, exactly as CTX-2 established for every other kind of
    /// current-state removal.
    /// </remarks>
    ValueTask<bool> DeleteAsync(NotificationDefinitionId id, CancellationToken cancellationToken = default);
```

```csharp
    // SqlNotificationDefinitionStore
    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(NotificationDefinitionId id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.NotificationDefinitions
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
            return false;

        if (entity.Origin is NotificationDefinitionOrigins.PlatformBuiltIn or NotificationDefinitionOrigins.ModuleBuiltIn)
            throw new InvalidOperationException("Built-in notification definitions cannot be deleted.");

        // Subordinate current-state data, per the CTX-3B referential inventory: both are meaningless once
        // the definition is gone and neither is reachable by any other route.
        var subscriptions = await _db.NotificationSubscriptions
            .Where(s => s.NotificationDefinitionId == id.Value)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var overrides = await _db.NotificationSubscriptionUserOverrides
            .Where(o => o.NotificationDefinitionId == id.Value)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        _db.NotificationSubscriptions.RemoveRange(subscriptions);
        _db.NotificationSubscriptionUserOverrides.RemoveRange(overrides);
        _db.NotificationDefinitions.Remove(entity);

        // Historical NotificationInstances are deliberately NOT touched.
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
```

Confirm the `NotificationSubscriptionUserOverrides` `DbSet` name against `AppertureDbContext` before writing it.

- [ ] **Step 4: Add `DeleteAsync` to the service, authorized**

```csharp
    // INotificationDefinitionService
    /// <summary>
    /// Deletes a user-created notification definition. Authorized exactly as create and update are:
    /// <c>notification_definition.manage</c> at instance scope, plus <c>integration.update</c> on the
    /// owning Integration for an Integration-scoped definition.
    /// </summary>
    ValueTask<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
```

```csharp
    // NotificationDefinitionService
    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var existing = await _store.GetAsync(new NotificationDefinitionId(id), cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return false;

        // Same gate as create/update. No new permission is introduced by CTX-3B.
        await _authorization.RequireDefinitionAccessAsync(existing.IntegrationId, manage: true, cancellationToken)
            .ConfigureAwait(false);

        var deleted = await _store.DeleteAsync(new NotificationDefinitionId(id), cancellationToken).ConfigureAwait(false);
        if (deleted)
        {
            // Follow this service's own existing audit convention for definition mutations -- read how
            // CreateAsync/UpdateAsync record theirs and mirror it rather than inventing a shape here.
            await AppendDefinitionAuditAsync("deleted", existing, cancellationToken).ConfigureAwait(false);
        }
        return deleted;
    }
```

Read `NotificationDefinitionService`'s existing create/update audit calls first; if that service does not currently audit, use the notification module's nearest convention and record the choice in the inventory doc rather than inventing a new action-naming scheme.

- [ ] **Step 5: Add the endpoint and the client method**

```csharp
    // NotificationDefinitionsController
    /// <summary>Deletes a user-created notification definition and its subscriptions (CTX-3B Plan 6a).</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        try
        {
            return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
```

Match the controller's existing route/response conventions. The client method uses the file's `EnsureSuccessOrThrowAsync` convention — **never bare `EnsureSuccessStatusCode()`**, so a 409 "Built-in notification definitions cannot be deleted." reaches the operator as real text (`ctx3a-closure.md` §8 item 4 is the precedent for why this matters).

- [ ] **Step 6: Run, build, commit**

```bash
dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~NotificationDefinitionDeleteTests"
dotnet build Apperture.slnx
git add src/Apperture.Core/Apperture.Core.Application/Notifications/ \
        src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlNotificationDefinitionStore.cs \
        src/Apperture.ControlPlane/Apperture.ControlPlane.Security/NotificationDefinitionService.cs \
        src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/NotificationDefinitionsController.cs \
        src/Apperture.ControlPlane/Apperture.Management.Client/ \
        tests/Apperture.UnitTests/Notifications/NotificationDefinitionDeleteTests.cs
git commit -m "CTX-3B: add NotificationDefinition deletion so the Integration-delete blocker is clearable"
```

---

### Task 3: Clear an Integration attention-routing override

**Files:**
- Modify: `src/Apperture.Core/Apperture.Core.Application/Integration/IAttentionRoutingManagementService.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/AttentionRoutingManagementService.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationsController.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/` (interface + implementation)
- Test: `tests/Apperture.UnitTests/Integration/IntegrationAttentionRoutingClearTests.cs`

An `IntegrationAttentionRoutingEntity` row is an **override** of the instance default (`InstanceAttentionRoutings`). So the clearing operation is not a destructive delete of unique configuration — it is "remove the override and fall back to the instance default," which is both the smallest correct operation and the one an operator already understands from the existing effective-routing view.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async Task Clearing_an_override_removes_the_row()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing, h.SampleCommand());

        var cleared = await h.Routing.ClearIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing);

        cleared.Should().BeTrue();
        (await h.Db.IntegrationAttentionRoutings.CountAsync(r => r.IntegrationId == integration.Id.Value)).Should().Be(0);
    }

    [Fact]
    public async Task Clearing_an_override_falls_back_to_the_instance_default()
    {
        using var h = new AttentionRoutingTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Routing.UpsertIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing, h.SampleCommand());
        await h.Routing.ClearIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing);

        var effective = await h.Routing.GetEffectiveRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing);

        effective.Should().NotBeNull("clearing an override reverts to the instance default, it does not remove routing entirely");
    }

    [Fact]
    public async Task Clearing_a_condition_leaves_other_conditions_overrides_intact()
    {
        // The operation is per (IntegrationId, ConditionKey), matching the unique index the schema already
        // has (IX_IntegrationAttentionRoutings_IntegrationId_ConditionKey).
    }

    [Fact]
    public async Task Clearing_when_no_override_exists_returns_false_and_changes_nothing()
    {
    }

    [Fact]
    public async Task Clearing_for_an_unknown_integration_throws_like_upsert_does()
    {
        // Mirrors UpsertIntegrationRoutingAsync's existing "Integration {id} was not found." behavior.
    }
```

- [ ] **Step 2: Implement `ClearIntegrationRoutingAsync`**

```csharp
    /// <summary>
    /// Removes an Integration's attention-routing override for one condition, reverting it to the instance
    /// default. Returns false when no override existed.
    /// </summary>
    /// <remarks>
    /// CTX-3B Plan 6a: Integration hard-delete blocks on attention-routing overrides, and before this
    /// operation existed there was no way for an operator to clear that blocker. The service previously
    /// exposed upsert only.
    /// </remarks>
    public async ValueTask<bool> ClearIntegrationRoutingAsync(
        IntegrationId integrationId,
        string conditionKey,
        CancellationToken cancellationToken = default)
    {
        if (await _integrations.GetByIdAsync(integrationId, cancellationToken).ConfigureAwait(false) is null)
            throw new InvalidOperationException($"Integration {integrationId.Value} was not found.");

        var row = await _db.IntegrationAttentionRoutings
            .FirstOrDefaultAsync(
                x => x.IntegrationId == integrationId.Value && x.ConditionKey == conditionKey,
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return false;

        _db.IntegrationAttentionRoutings.Remove(row);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
```

- [ ] **Step 3: Add the endpoint and the client method**

```csharp
    /// <summary>CTX-3B: clears an integration-level attention routing override, reverting to the instance default.</summary>
    [HttpDelete("{id}/attention-routing")]
    public async Task<IActionResult> DeleteAttentionRouting(
        string id, [FromQuery] string? conditionKey, CancellationToken cancellationToken)
    { /* mirror PutAttentionRouting's shape: resolve the default conditionKey, 404 on unknown Integration */ }
```

**Authorization: mirror `PutAttentionRouting` exactly.** Verified at review time, `IntegrationsController` carries a class-level `[Authorize]` and `PutAttentionRouting` applies no additional permission check. Clearing an override is strictly less privileged than setting one, so it takes the same gate and no more. **Do not invent a new permission here, and do not tighten one path without the other** — if this surface later gains a fine-grained permission, `GET`, `PUT` and `DELETE` get it together, as one deliberate change outside CTX-3B.

- [ ] **Step 4: Run, build, commit**

```bash
dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~IntegrationAttentionRoutingClearTests"
dotnet build Apperture.slnx
git add src/Apperture.Core/Apperture.Core.Application/Integration/IAttentionRoutingManagementService.cs \
        src/Apperture.ControlPlane/Apperture.ControlPlane.Security/AttentionRoutingManagementService.cs \
        src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationsController.cs \
        src/Apperture.ControlPlane/Apperture.Management.Client/ \
        tests/Apperture.UnitTests/Integration/IntegrationAttentionRoutingClearTests.cs
git commit -m "CTX-3B: add attention-routing override clear so the Integration-delete blocker is clearable"
```

---

### Task 4: Block delete on Notification/AttentionRouting dependents

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationManagementService.cs`
- Test: `tests/Apperture.UnitTests/Integration/IntegrationDeleteReferentialLifecycleTests.cs`
- Test: `tests/Apperture.UnitTests/Integration/IntegrationLifecycleTestHarness.cs`

**Interfaces:**
- Consumes: `AppertureDbContext.NotificationDefinitions`, `.NotificationSubscriptions`, `.IntegrationAttentionRoutings` — all existing `DbSet`s, and `IntegrationManagementService` already injects `AppertureDbContext` (verified).

**Tasks 2 and 3 must be complete before this task lands.** A blocker whose remediation path does not yet exist is the exact failure this plan's reachability rule prohibits, even transiently on the branch.

- [ ] **Step 1: Write the failing tests**

```csharp
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Integration;

/// <summary>
/// Design gate §13 CTX-3B step 7: the confirmed referential-lifecycle gap. Extends the existing
/// Binding/Schedule delete-block pattern to every current-state dependent the inventory
/// (<c>ctx3b-integration-delete-referential-inventory.md</c>) decided to block on, proves the one decided
/// cascade cleanup (PlatformSettingValues) both removes the row AND disposes of its secret, and proves
/// every blocker it adds is actually clearable by an operator.
/// </summary>
public sealed class IntegrationDeleteReferentialLifecycleTests
{
    [Fact]
    public async Task Delete_blocked_by_integration_scoped_notification_definition()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.AddUserNotificationDefinitionAsync(integration.Id.Value);

        var act = async () => await h.Integrations.DeleteIntegrationAsync(integration.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*notification definition*");
    }

    [Fact]
    public async Task Delete_blocked_by_an_integration_scoped_subscription_under_a_platform_scoped_definition()
    {
        // The case the NotificationDefinition blocker does NOT cover, and the actual reason subscriptions
        // get a blocker of their own: NotificationSubscriptionEntity carries its own nullable
        // IntegrationId independently of NotificationDefinitionId, so a subscription can be scoped to an
        // Integration while its definition is Platform-scoped and carries no IntegrationId at all.
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.AddPlatformScopedDefinitionAsync("nd-platform");
        await h.AddSubscriptionAsync("nd-platform", integrationId: integration.Id.Value);

        var act = async () => await h.Integrations.DeleteIntegrationAsync(integration.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*subscription*");
    }

    [Fact]
    public async Task Delete_blocked_by_attention_routing_override()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.AddAttentionRoutingAsync(integration.Id.Value);

        var act = async () => await h.Integrations.DeleteIntegrationAsync(integration.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*attention routing*");
    }

    [Fact]
    public async Task Every_blocker_this_plan_adds_can_be_cleared_by_an_operator_operation()
    {
        // The reachability invariant, end to end. This is the test that stops CTX-3B from repeating
        // CTX-1's unreachable-archive trap (design gate F4): set up all three new blockers at once, clear
        // each through the real operator-facing service, then delete successfully.
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        var definition = await h.AddUserNotificationDefinitionAsync(integration.Id.Value);
        await h.AddPlatformScopedDefinitionAsync("nd-platform");
        var subscription = await h.AddSubscriptionAsync("nd-platform", integrationId: integration.Id.Value);
        await h.AddAttentionRoutingAsync(integration.Id.Value);

        var blockedFirst = async () => await h.Integrations.DeleteIntegrationAsync(integration.Id);
        await blockedFirst.Should().ThrowAsync<InvalidOperationException>();

        // Clear each blocker the way an operator would -- through the real services, not by touching rows.
        await h.Definitions.DeleteAsync(definition.Id);                                   // Task 2
        await h.Subscriptions.RemoveAsync(new NotificationSubscriptionId(subscription.Id)); // pre-existing
        await h.Routing.ClearIntegrationRoutingAsync(integration.Id, AttentionConditionKeys.ExpectedRunMissing); // Task 3

        await h.Integrations.DeleteIntegrationAsync(integration.Id);

        (await h.Db.Integrations.CountAsync(i => i.Id == integration.Id.Value)).Should().Be(0,
            "every blocker CTX-3B adds must have an operator-accessible remediation path");
    }

    [Fact]
    public async Task No_undeletable_definition_can_ever_block_integration_delete()
    {
        // Guards the coincidence the inventory relies on: the definitions that block Integration delete
        // (IntegrationId set) and the definitions Task 2's operation accepts (Origin == UserCreated) are
        // the same set today, because CreateUserDefinitionAsync sets both together. If a future seed
        // carries an IntegrationId with a built-in Origin, this test fails rather than silently
        // recreating an unreachable blocker.
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.AddBuiltInDefinitionWithIntegrationIdAsync(integration.Id.Value);

        var blocking = await h.Db.NotificationDefinitions
            .Where(d => d.IntegrationId == integration.Id.Value)
            .Select(d => d.Origin)
            .ToListAsync();

        blocking.Should().OnlyContain(o => o == NotificationDefinitionOrigins.UserCreated,
            "a built-in definition carrying an IntegrationId would block Integration delete with no way to clear it");
    }

    [Fact]
    public async Task Delete_leaves_historical_operation_run_rows_untouched_with_the_dangling_id()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        h.Db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "run-1", WorkflowInstanceId = "wi-1", DefinitionId = "def-1",
            Status = "Completed", CreatedAt = DateTimeOffset.UtcNow, IntegrationId = integration.Id.Value,
            ResourceContextId = integration.ResourceContextId.Value
        });
        await h.Db.SaveChangesAsync();

        await h.Integrations.DeleteIntegrationAsync(integration.Id);

        var run = await h.Db.OperationRuns.AsNoTracking().SingleAsync(r => r.Id == "run-1");
        run.IntegrationId.Should().Be(integration.Id.Value,
            "the historical snapshot must survive, dangling, exactly as CTX-2 established");
        (await h.Db.Integrations.CountAsync(i => i.Id == integration.Id.Value)).Should().Be(0);
    }

    [Fact]
    public async Task Delete_still_succeeds_with_no_dependents_at_all()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();

        await h.Integrations.DeleteIntegrationAsync(integration.Id);

        (await h.Db.Integrations.CountAsync(i => i.Id == integration.Id.Value)).Should().Be(0);
    }

    [Fact]
    public async Task Delete_writes_exactly_one_integration_deleted_audit_record()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();

        await h.Integrations.DeleteIntegrationAsync(integration.Id);

        var record = h.Audit.Records.Should().ContainSingle(r => r.Action == ActionConstants.IntegrationDeleted).Subject;
        record.ResourceContextId.Should().Be(integration.ResourceContextId.Value,
            "the delete is attributable to the Context that owned the Integration");
        record.Details.Should().Contain(integration.Id.Value);
        record.Details.Should().Contain(integration.DisplayName);
    }

    [Fact]
    public async Task A_blocked_delete_writes_no_audit_record()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.AddAttentionRoutingAsync(integration.Id.Value);

        var act = async () => await h.Integrations.DeleteIntegrationAsync(integration.Id);
        await act.Should().ThrowAsync<InvalidOperationException>();

        h.Audit.Records.Should().BeEmpty("nothing was deleted, so nothing is recorded as deleted");
    }
}
```

Write `IntegrationLifecycleTestHarness` (new, same namespace) mirroring `ManagementServiceTestHarness`'s always-allow/fixed-actor shape, exposing `Db`, `Integrations` (`IIntegrationManagementService`), `Definitions` (`INotificationDefinitionService`), `Subscriptions` (the subscription store), `Routing` (`IAttentionRoutingManagementService`), `Settings` (`IIntegrationSettingsService`), `SecretStore` (`ISecretStore`), `Audit` (a recording `IAuditService` — if Plan 4 has already landed, reuse the double in its harness; if this plan runs first, write it here and let Plan 4 reuse it), plus the seed helpers used above and `CreateIntegrationAsync()`.

- [ ] **Step 2: Run to verify they fail**

Expected: FAIL for the three "blocked" tests, the reachability test, and both audit tests. The historical-row and no-dependents tests should already PASS — run them too, since they document behavior this plan must not change.

- [ ] **Step 3: Add the audit action constant**

In `ActionConstants.cs`, next to the existing `IntegrationDelete` permission constant:

```csharp
    /// <summary>
    /// CTX-3B (product decision P2): audit action for a completed Integration hard delete. Deliberately
    /// narrow. Integration create/update/activate/deactivate/retire remain unaudited today; broadening
    /// that is recorded as explicit later debt in the CTX-3B closure, not done here.
    /// </summary>
    public const string IntegrationDeleted = "integration.deleted";
```

- [ ] **Step 4: Extend `DeleteIntegrationAsync` with the three new blockers**

Insert after the existing schedule guard (line 211), before the entity removal:

```csharp
        // CTX-3B referential-lifecycle inventory (docs/.../ctx3b-integration-delete-referential-inventory.md):
        // none of these three has a foreign key, so nothing enforced them before. Block, matching the
        // existing Binding/Schedule precedent, rather than silently orphaning operator configuration.
        // Each blocker is clearable by an operator: definitions via DELETE api/notification-definitions/{id}
        // (Plan 6a Task 2), subscriptions via the existing subscription removal, and routing overrides via
        // DELETE api/integrations/{id}/attention-routing (Plan 6a Task 3).
        var notificationDefinitionCount = await _db.NotificationDefinitions.CountAsync(e => e.IntegrationId == id.Value, cancellationToken);
        if (notificationDefinitionCount > 0)
            throw new InvalidOperationException(
                $"Cannot delete integration: {notificationDefinitionCount} notification definition(s) reference it. Delete them first.");

        // Subscriptions carry their own nullable IntegrationId independently of NotificationDefinitionId,
        // so an Integration-scoped subscription can exist under a Platform- or Module-scoped definition and
        // is NOT covered by the blocker above. See the inventory's Decisions section.
        var notificationSubscriptionCount = await _db.NotificationSubscriptions.CountAsync(e => e.IntegrationId == id.Value, cancellationToken);
        if (notificationSubscriptionCount > 0)
            throw new InvalidOperationException(
                $"Cannot delete integration: {notificationSubscriptionCount} notification subscription(s) reference it. Remove them first.");

        var attentionRoutingCount = await _db.IntegrationAttentionRoutings.CountAsync(e => e.IntegrationId == id.Value, cancellationToken);
        if (attentionRoutingCount > 0)
            throw new InvalidOperationException(
                $"Cannot delete integration: {attentionRoutingCount} attention routing override(s) reference it. Clear them first.");
```

Each message names the remediation, following the same principle as Plan 3's readiness message: a blocker that does not say how to clear it is only half-implemented. These surface through the path CTX-3A's post-closure stabilization already fixed (`ctx3a-closure.md` §8 item 4), so no new UI work is needed.

- [ ] **Step 5: Run the three blocker tests and the reachability test**, then continue to Task 5 before committing — the delete path is not finished until the settings cleanup and the audit record are in.

---

### Task 5: Secret-safe `PlatformSettingValues` cleanup, and the delete audit

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationManagementService.cs`
- Test: `tests/Apperture.UnitTests/Integration/IntegrationDeleteReferentialLifecycleTests.cs`

**This is the task the review flagged as unsafe in the first draft.** A raw `_db.PlatformSettingValues.RemoveRange(...)` deletes the only pointer to an Active `ISecretStore` secret and orphans it permanently.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async Task Delete_succeeds_and_cleans_up_integration_scoped_platform_setting_values()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Settings.SetAsync(integration.Id.Value, "SomeSetting",
            new PlatformSettingUpdatePayload(Value: "x"), rowVersionBase64: null);

        await h.Integrations.DeleteIntegrationAsync(integration.Id);

        (await h.Db.PlatformSettingValues.CountAsync(
            v => v.ScopeKind == PlatformSettingScopeConstants.IntegrationKind && v.ScopeId == integration.Id.Value))
            .Should().Be(0);
        (await h.Db.Integrations.CountAsync(i => i.Id == integration.Id.Value)).Should().Be(0);
    }

    [Fact]
    public async Task Delete_disables_the_backing_secret_of_a_sensitive_integration_setting()
    {
        // The assertion that matters. Deleting the row alone would leave an ACTIVE secret in ISecretStore
        // with nothing pointing at it, forever. PlatformSettingDefinitionRegistry is explicit that a
        // sensitive setting "retains only an opaque reference", so row count reaching zero proves nothing
        // about the secret.
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Settings.SetAsync(integration.Id.Value, h.SensitiveSettingKey,
            new PlatformSettingUpdatePayload(SecretOperation: PlatformSettingSecretOperations.Replace, SecretValue: "hunter2"),
            rowVersionBase64: null);
        var secretId = h.LastProvisionedSecretId;

        await h.Integrations.DeleteIntegrationAsync(integration.Id);

        var descriptor = await h.SecretStore.GetAsync(secretId);
        descriptor!.State.Should().Be(SecretState.Disabled,
            "deleting the row without disposing of the owned secret would silently orphan it");
    }

    [Fact]
    public async Task A_blocked_delete_leaves_setting_values_and_their_secrets_intact()
    {
        using var h = new IntegrationLifecycleTestHarness();
        var integration = await h.CreateIntegrationAsync();
        await h.Settings.SetAsync(integration.Id.Value, h.SensitiveSettingKey,
            new PlatformSettingUpdatePayload(SecretOperation: PlatformSettingSecretOperations.Replace, SecretValue: "hunter2"),
            rowVersionBase64: null);
        var secretId = h.LastProvisionedSecretId;
        await h.AddAttentionRoutingAsync(integration.Id.Value);

        var act = async () => await h.Integrations.DeleteIntegrationAsync(integration.Id);
        await act.Should().ThrowAsync<InvalidOperationException>();

        (await h.SecretStore.GetAsync(secretId))!.State.Should().NotBe(SecretState.Disabled,
            "cleanup runs only after every blocker has passed -- a refused delete destroys nothing");
        (await h.Db.PlatformSettingValues.CountAsync(v => v.ScopeId == integration.Id.Value)).Should().BeGreaterThan(0);
    }
```

The harness needs a real (in-memory) `ISecretStore` rather than a `Mock<ISecretStore>`, plus a registered sensitive setting definition (`SensitiveSettingKey`) so `IntegrationSettingsService` takes its secret path. Reuse whatever in-memory secret store the existing secret tests use rather than writing a new one.

- [ ] **Step 2: Run to verify they fail**

Expected: FAIL — nothing cleans these rows up today, and the secret stays Active.

- [ ] **Step 3: Delegate the cleanup, then audit the delete**

Replace the tail of `DeleteIntegrationAsync`:

```csharp
        var entity = await _db.Integrations.FirstOrDefaultAsync(e => e.Id == id.Value, cancellationToken);
        if (entity is null)
            return;

        // Integration-scoped PlatformSettingValues have no independent meaning without the Integration
        // (unlike NotificationDefinitions/AttentionRoutings above, which are independently meaningful
        // operator configuration) -- clean them up rather than requiring separate manual deletion first.
        //
        // Delegated to IIntegrationSettingsService.ResetAsync rather than removing rows directly, because
        // a sensitive setting's row holds only an opaque ISecretStore reference: deleting the row alone
        // would orphan an Active secret permanently. ResetAsync is the established lifecycle -- delete the
        // scoped value, disable the owned secret via TryDisableOwnedSecretAsync, write the per-key audit --
        // and reusing it keeps secret disposal in the one place that owns it.
        //
        // Deliberately not atomic with the row delete below: the SQL store and ISecretStore are different
        // stores and do not share a transaction. This follows IntegrationSettingsService's existing reset
        // failure behavior rather than inventing new semantics; see the inventory doc.
        var scopedSettingKeys = await _db.PlatformSettingValues
            .Where(v => v.ScopeKind == PlatformSettingScopeConstants.IntegrationKind && v.ScopeId == id.Value)
            .Select(v => v.Key)
            .ToListAsync(cancellationToken);

        foreach (var key in scopedSettingKeys)
            await _integrationSettings.ResetAsync(id.Value, key, cancellationToken).ConfigureAwait(false);

        _db.Integrations.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);

        // Product decision P2: one audit record for the destructive operation itself. Records the owning
        // Context, the identity, and what was deliberately cleaned up alongside it.
        await _audit.AppendAsync(new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            actor.Id,
            ActionConstants.IntegrationDeleted,
            new ResourceDescriptor(ResourceTypeConstants.Integration, id.Value),
            Outcome: "",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            Details: $"Deleted integration '{existing.DisplayName}' ({id.Value}); cleaned up {scopedSettingKeys.Count} integration-scoped setting value(s).",
            ResourceContextId: existing.ResourceContextId.Value), cancellationToken).ConfigureAwait(false);
```

This adds two constructor dependencies to `IntegrationManagementService`: `IAuditService` and `IIntegrationSettingsService`. Update every manual construction (`grep -rn "new IntegrationManagementService(" src tests`). **Check for a DI cycle first** — if `IIntegrationSettingsService`'s own dependency graph reaches back to `IIntegrationManagementService`, inject `PlatformSettingSecretMaterialProvisioner` plus the settings store directly instead of the whole service, and replicate `ResetAsync`'s three steps in order (delete value → `TryDisableOwnedSecretAsync` → audit). Do not skip the secret step under any circumstances.

Note that `_audit.AppendAsync` here is **not** inside an explicit transaction, matching this method's existing shape. That is acceptable for a delete: the destructive act has already committed, and a lost audit row for a completed delete is strictly better than refusing the delete. This is a different situation from Plan 4's move audit, which is appended inside the move transaction precisely because an unaudited *successful move* would be worse than no move.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~IntegrationDeleteReferentialLifecycleTests"`
Expected: PASS, all thirteen tests.

- [ ] **Step 5: Full regression run**

```bash
dotnet build Apperture.slnx
dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj
```

Expected: 0 errors, full green. Existing Integration-delete tests elsewhere in the suite must stay green — this plan only adds new block/cleanup conditions and one audit record; it never changes the no-dependents success path's outcome.

- [ ] **Step 6: Commit**

```bash
git add src/Apperture.Core/Apperture.Core.Domain/Governance/ActionConstants.cs \
        src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationManagementService.cs \
        tests/Apperture.UnitTests/Integration/IntegrationLifecycleTestHarness.cs \
        tests/Apperture.UnitTests/Integration/IntegrationDeleteReferentialLifecycleTests.cs
git add -u
git commit -m "CTX-3B: block Integration delete on notification/routing dependents; secret-safe settings cleanup; integration.deleted audit"
```

---

## Out of scope for this plan

- Connection/Schedule broader lifecycle cleanup (explicitly later roadmap work), including the `ScheduleDefinitions` blocker's own missing remediation path — a pre-existing CTX-1 blocker, recorded in the inventory's follow-ups and in Plan 6b's closure.
- Any change to how historical CTX-2 tables are written or read.
- Full Integration lifecycle auditing — exactly one new action, `integration.deleted`.
- A general notification lifecycle redesign. Task 2 adds one delete operation with its subordinate-data handling and nothing more: no subscription-management redesign, no rule/template model change, no notification UI work.
- Any UI beyond the existing "Cannot delete integration: …" error surfacing, which already renders the server's real message after CTX-3A's post-closure fix.

## Validation for this plan

- `dotnet build Apperture.slnx` — 0 errors.
- `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj` — full project green, including all thirteen referential-lifecycle tests, the definition-delete tests, and the routing-clear tests.
- `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj` — no regressions against the 204/204 baseline (or whatever updated baseline Plans 1-5 established, if this plan runs after them).
- The inventory document exists, was written before the code changes, and its table was re-derived from a real grep rather than copied from this plan.
- **Every blocker has a non-empty "Clearable by" entry**, and `Every_blocker_this_plan_adds_can_be_cleared_by_an_operator_operation` passes.
- The sensitive-setting secret-disposal test passes — **row counts reaching zero is not sufficient evidence**.
