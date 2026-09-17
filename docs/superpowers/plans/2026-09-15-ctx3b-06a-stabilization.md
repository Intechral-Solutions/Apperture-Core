# CTX-3B Plan 6a — Stabilization Pass (operator UI for the two new remediation paths)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Plan 6a's backend referential-lifecycle work (already reviewed and merged, commits `7b31c88..2609ba1` on `resource-contexts`) added two new remediation operations — `INotificationDefinitionService.DeleteAsync` and `IAttentionRoutingManagementService.ClearIntegrationRoutingAsync` — plus three new Integration-delete blockers backed by them. Neither new client method (`ManagementClient.DeleteNotificationDefinitionAsync`, `ManagementClient.ClearIntegrationAttentionRoutingAsync`) has an operator UI caller. This pass wires the smallest coherent UI action for each into the existing management surfaces, corrects the Integration-delete blocker copy (now stale post-Plan-6a), and fixes the two new audit records' empty `Outcome` values. **Do not redesign notification management, routing, or Integration delete. Do not add bulk deletion. Do not weaken authorization.**

**Tech stack:** Blazor WebAssembly (Ui.Client), source-text tests in `Apperture.Management.Ui.Shared.Tests` (NOT bUnit), C#/.NET 10.

**Global Constraints:**
- NotificationDefinition delete eligibility in the UI: `Origin == "userCreated"` (exact string, case-insensitive compare matching existing usage), gated the same way `NotificationDefinitionDetail.razor` already gates its Edit action (`_canManageDefinitions && (IntegrationId is null || _canUpdateIntegrations)`), but keyed on `Origin`, not `Mutability`.
- Attention-routing "Clear override" is additive next to the existing Save form — it is a genuinely new operation, not a relabeling of "select Instance default + Save" (that flow still leaves a zero-valued override row, which still blocks Integration delete).
- Use `ConfirmActionDialog` (`Organisms/ConfirmActionDialog.razor`) for both new destructive/state-changing actions, mirroring `IntegrationDetail.razor`'s existing Integration-delete dialog usage exactly (its `OnConfirm` throws to keep the dialog open and show the server's real error text; completing normally closes it).
- No new IAM. No new capability flags. Reuse `OperatorCapabilitiesDto`'s existing `CanManageNotificationDefinitions`/`CanUpdateIntegrations`/`CanManageSchedules` fields exactly as their sibling actions on the same pages already use them.
- Tests: source-text style only (`tests/Apperture.Management.Ui.Shared.Tests/`, `ReadRazor`/`ExtractMethodBody` helpers, xUnit + FluentAssertions, asserting on markup/code strings) — mirror `ResourceContexts/MovementUiSourceTests.cs`'s shape. Do not introduce bUnit.
- Do not fix, redesign, or broaden: ScheduleDefinitions' missing delete path, Connection lifecycle, the NotificationSubscription permission asymmetry (named debt, not a bug to fix), setting-definition scope drift, sensitive-setting tripwire, platform-wide audit Outcome normalization, context IAM.

---

### Task 1: NotificationDefinition delete UI

**File:** `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Notifications/NotificationDefinitionDetail.razor`
**Test:** new file in `tests/Apperture.Management.Ui.Shared.Tests/` (e.g. `Notifications/NotificationDefinitionDeleteUiSourceTests.cs`)

- [ ] Add a computed `_canDeleteDefinition => _canManage && _detail is not null && string.Equals(_detail.Origin, "userCreated", StringComparison.OrdinalIgnoreCase)` (reuse the existing `_canManage` field this file already computes in `LoadAsync()` — it already combines `CanManageNotificationDefinitions` with the `CanUpdateIntegrations` check for Integration-scoped definitions).
- [ ] Add a "Delete" button in the existing toolbar (next to "Edit definition"), visible only when `_canDeleteDefinition`, `id="notification-definition-delete-trigger"` (stable id, matching `IntegrationDetail.razor`'s `integration-delete-trigger` convention), styled as a destructive action (match the existing toolbar button classes used for destructive/danger actions elsewhere in this project, e.g. `ui-toolbar-btn--danger`).
- [ ] Add a `ConfirmActionDialog` (state field `_confirmDeleteOpen`), `Emphasis="ConfirmActionEmphasis.Destructive"`, `Title="Delete notification definition?"`, `ChildContent` naming the definition by display name and stating this cannot be undone, `Details` noting its subscriptions/overrides are removed with it (matching what the backend actually does — cascades `NotificationSubscriptions`/`NotificationSubscriptionUserOverrides`, preserves historical `NotificationInstances`).
- [ ] `OnConfirm` handler calls `await Client.DeleteNotificationDefinitionAsync(_detail!.Id)` with no local try/catch (let `ConfirmActionDialog` surface the real server error text on failure — a 409 "Built-in notification definitions cannot be deleted." must reach the operator verbatim, matching the CTX-3A regression-fix precedent this whole plan is built on).
- [ ] `OnConfirmed` navigates back to the list: `Nav.NavigateTo("/config/notifications/definitions")` (the definition no longer exists, so staying on this page is not viable — there is no "reload in place" precedent to follow instead).
- [ ] Test: source-text assertions that (a) the delete trigger button exists and is gated behind `_canDeleteDefinition`, (b) `_canDeleteDefinition` computation references `Origin` and `"userCreated"` and does NOT reference `Mutability`, (c) the `ConfirmActionDialog`'s `OnConfirm` calls `DeleteNotificationDefinitionAsync` with no local `catch` around it (use `ExtractMethodBody` scoped to the handler method to make this precise, not a whole-file `Contains`), (d) `OnConfirmed` navigates to the definitions list route.
- [ ] Build + run the new test file + run this project's full existing suite (no regression). Commit.

---

### Task 2: Integration attention-routing "Clear override" UI

**File:** `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationScheduleDiagnostics.razor`
**Test:** new file in `tests/Apperture.Management.Ui.Shared.Tests/` (e.g. `Integrations/IntegrationAttentionRoutingClearUiSourceTests.cs`)

- [ ] In the existing "Attention routing (expected_run_missing)" section (the one with the Primary/Escalation team `<select>`s and the "Save integration override" button, gated on `_capabilities?.CanManageSchedules == true`), add a second button: **"Clear override"** (or **"Use default routing"** — pick the clearer of the two; do not label it "Delete routing", per the plan's explicit semantic requirement that this is a fallback-to-default action, not a destructive delete of unique configuration). Same `CanManageSchedules` gate as Save — no new/different authorization boundary (mirrors the backend's own GET/PUT/DELETE-share-one-boundary rule).
- [ ] Button click calls `Client.ClearIntegrationAttentionRoutingAsync(integrationId, conditionKey)` directly (this operation is safe/idempotent — the backend returns 204 whether or not an override row existed, so no confirmation dialog is required for it, unlike the destructive NotificationDefinition delete in Task 1; use a lightweight busy/error pattern mirroring this same section's existing `_routingSaveError`/save-busy fields — add sibling `_routingClearError`/`_routingClearBusy` fields, `catch (HttpRequestException ex) { _routingClearError = ex.Message; }`).
- [ ] After a successful clear, re-run the existing `LoadAsync()` (same as Save already does) so the effective-routing display visibly updates to show instance/default routing is now in effect — do not hand-patch local state.
- [ ] Fix the existing misleading copy at this section ("Leave as 'Instance default' to clear the override for that role") — selecting Instance default in both dropdowns and clicking Save does NOT clear the row (it still blocks Integration delete); rewrite to point at the new Clear button instead, e.g. "Use 'Clear override' below to remove this Integration's override entirely and fall back to the instance default."
- [ ] Test: source-text assertions that (a) a Clear button exists in this section with `CanManageSchedules` gating matching the Save button's gate exactly, (b) its handler calls `ClearIntegrationAttentionRoutingAsync` (not `PutIntegrationAttentionRoutingAsync`), (c) it does NOT wrap the call in a confirmation dialog (idempotent, no dialog required — assert absence of a new `ConfirmActionDialog` tied to this button, to keep the scope from silently growing), (d) the misleading "Leave as Instance default" copy string is gone / replaced.
- [ ] Build + run the new test file + run this project's full existing suite. Commit.

---

### Task 3: Integration-delete blocker presentation

**File:** `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationDetail.razor`
**Test:** new file in `tests/Apperture.Management.Ui.Shared.Tests/` (e.g. `Integrations/IntegrationDeleteBlockerUiSourceTests.cs`)

The server error text itself (`"Cannot delete integration: N notification definition(s)/subscription(s)/attention routing override(s) reference it. …"`) already reaches the operator verbatim via `ConfirmActionDialog`'s existing error-surfacing (CTX-3A precedent) — this task is about the dialog's own static `Details` copy, which is now stale, and optionally a navigation aid.

- [ ] Rewrite the `ConfirmActionDialog`'s `Details` copy on the Integration-delete dialog (currently: *"Bindings, schedules, and settings that belong to this integration are removed with it."* — factually wrong post-Plan-6a: Bindings and Schedules are, and always were, blockers, not cascaded; the three new blockers aren't mentioned at all). Replace with something naming what's actually cascaded vs. blocked, e.g.: *"Integration-scoped settings are cleaned up automatically. Bindings, schedules, notification definitions, notification subscriptions, and attention-routing overrides must be removed first — this delete is blocked while any exist."*
- [ ] If this page already has, or can trivially add, links/tabs to the relevant remediation surfaces (notifications page for definitions/subscriptions, schedule diagnostics page for the routing override), add simple navigation — a link, not a wizard — so an operator reading the blocker error can get to the right page in one click. Keep this to what's genuinely trivial; if wiring a link requires new routing/plumbing beyond a plain `<a href>`/`NavLink`, skip it and note why in your report rather than expanding scope.
- [ ] For the subscription-blocker case specifically: do not add any messaging implying an Integration owner can always personally clear it. The existing NotificationSubscription removal surface already has its own authorization (`notification.definition.manage` + `integration.update` for Integration-scoped definitions, `notification.definition.manage` alone i.e. instance-level for Platform/Module-scoped ones) — do not touch that. If you add a link to that surface, it is just a link; that surface's own existing authorization already correctly hides/shows the actual remove action. Do not add new copy speculating about who can or cannot act — the linked page's own behavior is the correct source of truth.
- [ ] Test: source-text assertions that the `Details` copy no longer contains the old "Bindings, schedules, and settings... removed with it" claim, and does name notification definitions/subscriptions/attention-routing overlays as things that must be cleared first (not cascaded).
- [ ] Build + run the new test file + run this project's full existing suite. Commit.

---

### Task 4: Audit Outcome cleanup

**Files:**
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationManagementService.cs` (line ~351, the `integration.deleted` `AuditRecord` construction)
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/NotificationDefinitionService.cs` (line ~297, the `notification.definition.deleted` `AuditRecord` construction)
- `tests/Apperture.UnitTests/Integration/IntegrationDeleteReferentialLifecycleTests.cs`
- `tests/Apperture.UnitTests/Notifications/NotificationDefinitionDeleteTests.cs`

- [ ] Change `Outcome: ""` to `Outcome: "succeeded"` in both `AuditRecord(...)` constructions. This matches Plan 4's own established movement-audit convention (`ResourceOwnershipMoveService.cs`: `"succeeded"` for successful outcomes, `"rejected"` for blocked ones) — not a new invention, a house convention that Plan 6a should have followed and didn't.
- [ ] Add one assertion to each of `Delete_writes_exactly_one_integration_deleted_audit_record` (in `IntegrationDeleteReferentialLifecycleTests.cs`) and the equivalent audit test in `NotificationDefinitionDeleteTests.cs`, checking `record.Outcome.Should().Be("succeeded")`. Neither test currently asserts on `Outcome` at all — this is a genuinely new assertion, not a changed one.
- [ ] Do not touch any other audit record's `Outcome` value in this codebase — this is scoped to exactly the two Plan-6a-introduced records, not a platform-wide normalization.
- [ ] Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~IntegrationDeleteReferentialLifecycleTests|FullyQualifiedName~NotificationDefinitionDeleteTests"` — expect all previously-passing tests still green plus the two new assertions passing.
- [ ] Commit.

---

## Validation for this plan

- Targeted: the four new/updated test files above, plus the two updated backend test files.
- `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj` — full project green.
- `dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj` (or wherever the source-text tests actually build from — confirm exact project path) — full project green, including the new tests.
- `dotnet test tests/Apperture.ArchTests/Apperture.ArchTests.csproj` — no new violations.
- `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj` — no regressions (only if the audit Outcome change or any other touched file is exercised there; otherwise note not affected).
- `dotnet build Apperture.slnx` — 0 errors.
- Manual browser smoke per the 13-step checklist the requesting session specified (create/use an Integration with a user-created definition and a routing override; verify both blockers; clear each through the new UI; verify blockers clear; verify built-in definitions show no Delete action; verify error copy stays human-readable).
