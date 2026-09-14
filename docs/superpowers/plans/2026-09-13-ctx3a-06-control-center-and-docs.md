# CTX-3A Plan 6 — Control Center Partial Scoping & Documentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Thread the shell's View into Control Center's contextual sections (run-derived attention/activity), split its summary pills into "In view" / "Platform — all applications" groups, and close out CTX-3A with the required documentation: `control-panel-ui.md` updates, an ADR-0040 confirming note, and the `ctx3a-closure.md` closure report that explicitly states CTX-3A is an internal milestone, not a shipped feature.

**Architecture:** `RunQueryFilter.ResourceContextId` (already server-side per CTX-2, already client-side per Plan 1) is threaded through `ControlCenterController` → `ControlCenterQueryService` → `IControlActivityQueryService.ListRecentActivityAsync`/`IControlAttentionQueryService.ListAttentionAsync`, both of which already build a `RunQueryFilter` internally for their run-derived halves. The one genuinely new piece of plumbing is the rejected-external-controls join to `OperationRuns.ResourceContextId`, since no FK/navigation exists between `WorkflowExternalControlRequests` and `OperationRuns` today. This is explicitly the one CTX-3A slice the design gate allows to be cut to a banner without breaking anything else (§13 step 12) — this plan implements it in full, but Task 1's design keeps the cut option cheap if validation surfaces a blocker.

**Tech Stack:** ASP.NET Core, EF Core LINQ joins, Blazor WebAssembly, xUnit + FluentAssertions, Markdown documentation.

**Spec:** `docs/01-architecture/transitions/resource-contexts/ctx3-design-gate.md` — this plan implements design-gate §13 CTX-3A steps 12–13 (`§5.1` Control Center Option B in full, `§10.1` row A6, `§14` "Surfaces" Control Center criterion, `§13` step 13 docs, `§15` D1 launch-status statement).

**Depends on:** `2026-09-13-ctx3a-01-contracts-and-domain.md` through `2026-09-13-ctx3a-05-creation-audit-business-data.md` — this is the final CTX-3A plan and its closure document (Task 6) summarizes the validation results of all five prior plans, so it should be executed last, after the others are merged and their own test suites are green.

## Global Constraints

- System/platform sections (worker runtime, scheduler operations, stuck-shutdown attention items) remain **always** instance-wide and are **clearly labeled as such** — never silently scoped (design gate §5.1, R10). `runtimeShutdownQueuedStuck` specifically has no `ResourceContextId` column anywhere to filter by (confirmed: `RuntimeControlRequestEntity` has none) and must always be shown, tagged "Platform".
- A number must never change semantic meaning merely because scope changed without clear labeling (design gate §5.1, closing line, R10).
- The pre-CTX-2-history note is mandatory whenever a specific view is selected, on both Control Center and Runs (design gate §5.1, §5.3, R11) — Plan 4 already added the Runs copy; this plan must use the **identical wording** on Control Center so the two surfaces read as one consistent product, not two independently-worded features.
- If Task 1's rejected-controls join proves materially harder than expected (e.g. requires a schema change), this plan may fall back to the design gate's own escape hatch: Control Center renders `Unscoped` with the banner "Control Center shows all applications," and that fallback — not a silent partial implementation — must be what's recorded in the closure document's "Deliberately deferred" section (design gate §5.1, "If cut, Control Center renders `Unscoped`... It can be cut without breaking anything else").
- The closure document must explicitly state, near the top: *"CTX-3A is an internal Application-workspaces milestone. The Application feature is not considered fully shipped until CTX-3B Resource Movement is complete."* (design gate §15 D1, and the user's original task instruction, verbatim requirement).
- Do not rewrite `ctx3-design-gate.md` as if its review history never happened — the closure document is a new file that references it, not an edit to it (design gate §28 instruction).

---

## File Structure

**Modified files:**
- `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ControlCenterController.cs:20-31` — `resourceContextId` query parameter.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IControlCenterQueryService.cs`, `ControlCenterQueryService.cs:116-123` — thread the parameter through.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IControlActivityQueryService.cs`, `ControlActivityQueryService.cs:44-56` — `ListRecentActivityAsync` gains the parameter, passed into its internal `RunQueryFilter`.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IControlAttentionQueryService.cs`, `ControlAttentionQueryService.cs:48-168` — `ListAttentionAsync` gains the parameter; the failed-executions branch (lines 48-76) passes it into its `RunQueryFilter`; the rejected-controls branch (lines 124-168) gains the new join to `OperationRuns`; the stuck-shutdown branch (lines 78-122) is explicitly left unfiltered.
- `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs:114`, `ManagementClient.cs:431-437` — `GetControlCenterAsync` gains `resourceContextId`.
- `Pages/Control/ControlCenter.razor` — scope-aware subtitle, pill grouping, "Blocked work (all applications)" relabel/regroup, pre-history note.
- `docs/01-architecture/control-panel-ui.md` — new "View selector" section, Config menu list update.
- `docs/02-decisions/ADR-0040-operator-information-architecture.md` — confirming note appended under `### Neutral`.

**New files:**
- `docs/01-architecture/transitions/resource-contexts/ctx3a-closure.md` — the CTX-3A closure report.
- `tests/Apperture.UnitTests/ResourceContexts/ControlCenterScopingTests.cs`

---

## Task 1: Thread `resourceContextId` through Control Center's run-derived sections (A6)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ControlCenterController.cs:20-31`
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IControlCenterQueryService.cs`, `ControlCenterQueryService.cs:116-123`
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IControlActivityQueryService.cs`, `ControlActivityQueryService.cs:44-56`
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IControlAttentionQueryService.cs`, `ControlAttentionQueryService.cs:48-168`
- Test: `tests/Apperture.UnitTests/ResourceContexts/ControlCenterScopingTests.cs`

**Interfaces:**
- Consumes: `RunQueryFilter.ResourceContextId` (server-side, pre-existing since CTX-2, confirmed applied in `RunQueryService.cs:70-71`); `OperationRunEntity.ResourceContextId` (the CTX-2 snapshot column, confirmed nullable, doc-commented "Null for pre-CTX-2 rows and for legitimately contextless (System) runs").
- Produces: `IControlCenterQueryService.GetControlCenterAsync(DateTimeOffset, int, int, string? resourceContextId, CancellationToken)`, `IControlActivityQueryService.ListRecentActivityAsync(DateTimeOffset, int, string? resourceContextId, CancellationToken)`, `IControlAttentionQueryService.ListAttentionAsync(DateTimeOffset, int, string? resourceContextId, CancellationToken)` — all three gain the parameter as a new, optional (defaulted-null) argument inserted before `CancellationToken`, so existing callers that omit it keep today's behavior exactly (design gate §5.1: "Callers that omit it keep today's behavior").

- [ ] **Step 1: Write the failing tests**

Using this repo's existing Control Center test harness (locate the file exercising `ControlActivityQueryService`/`ControlAttentionQueryService` today, following whichever EF Core InMemory pattern it already uses):
```csharp
[Fact]
public async Task ListAttentionAsync_WithResourceContextId_FiltersFailedExecutionsBySnapshot()
{
    var harness = await ControlCenterTestHarness.CreateAsync();
    await harness.SeedFailedRunAsync(resourceContextId: "canvas-id");
    await harness.SeedFailedRunAsync(resourceContextId: "other-id");

    var items = await harness.AttentionService.ListAttentionAsync(DateTimeOffset.MinValue, take: 10, resourceContextId: "canvas-id", CancellationToken.None);

    items.Should().ContainSingle(i => i.Kind == "executionFailed");
}

[Fact]
public async Task ListAttentionAsync_StuckShutdowns_AreAlwaysIncludedRegardlessOfResourceContextId()
{
    var harness = await ControlCenterTestHarness.CreateAsync();
    await harness.SeedStuckShutdownRequestAsync();

    var itemsUnderCanvas = await harness.AttentionService.ListAttentionAsync(DateTimeOffset.MinValue, take: 10, resourceContextId: "canvas-id", CancellationToken.None);
    var itemsUnderAll = await harness.AttentionService.ListAttentionAsync(DateTimeOffset.MinValue, take: 10, resourceContextId: null, CancellationToken.None);

    itemsUnderCanvas.Should().Contain(i => i.Kind == "runtimeShutdownQueuedStuck");
    itemsUnderAll.Should().Contain(i => i.Kind == "runtimeShutdownQueuedStuck");
}

[Fact]
public async Task ListAttentionAsync_RejectedControls_FilterThroughJoinToOperationRunsResourceContextId()
{
    var harness = await ControlCenterTestHarness.CreateAsync();
    var runInCanvas = await harness.SeedOperationRunAsync(resourceContextId: "canvas-id");
    var runInOther = await harness.SeedOperationRunAsync(resourceContextId: "other-id");
    await harness.SeedRejectedExternalControlAsync(runId: runInCanvas.Id);
    await harness.SeedRejectedExternalControlAsync(runId: runInOther.Id);

    var items = await harness.AttentionService.ListAttentionAsync(DateTimeOffset.MinValue, take: 10, resourceContextId: "canvas-id", CancellationToken.None);

    items.Should().ContainSingle(i => i.Kind == "rejectedExternalControl" || i.TargetId == runInCanvas.Id);
}

[Fact]
public async Task ListRecentActivityAsync_WithResourceContextId_FiltersRunDerivedRowsBySnapshot()
{
    var harness = await ControlCenterTestHarness.CreateAsync();
    await harness.SeedOperationRunAsync(resourceContextId: "canvas-id");
    await harness.SeedOperationRunAsync(resourceContextId: "other-id");

    var activity = await harness.ActivityService.ListRecentActivityAsync(DateTimeOffset.MinValue, take: 10, resourceContextId: "canvas-id", CancellationToken.None);

    activity.Should().HaveCount(1);
}
```
Adjust field/method names (`i.Kind`, `i.TargetId`, exact attention-item shape) once `ControlAttentionItemDto` is read in full — the research did not enumerate its exact properties, only the three query branches that populate it.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ControlCenterScopingTests"`
Expected: FAIL — none of the three interfaces accept `resourceContextId` yet.

- [ ] **Step 3: Add the parameter through the interface chain**

`IControlCenterQueryService.cs`: add `string? resourceContextId = null` before `CancellationToken` on `GetControlCenterAsync`. `IControlActivityQueryService.cs`: same on `ListRecentActivityAsync`. `IControlAttentionQueryService.cs`: same on `ListAttentionAsync`.

- [ ] **Step 4: Wire `ControlCenterQueryService`**

Change lines 116-123 to pass the new parameter through to both sub-service calls:
```csharp
var activity = await _activity.ListRecentActivityAsync(createdFromUtc, takeRecentActivity, resourceContextId, cancellationToken).ConfigureAwait(false);
var attention = await _attention.ListAttentionAsync(createdFromUtc, takeAttention, resourceContextId, cancellationToken).ConfigureAwait(false);
```

- [ ] **Step 5: Wire `ControlActivityQueryService.ListRecentActivityAsync`**

Change the run-derived branch (lines 44-56 area) to construct `new RunQueryFilter(CreatedFrom: createdFromUtc, ResourceContextId: resourceContextId)` instead of the current parameterless-context construction. Leave the runtime-control-requests branch (lines 140-185) untouched — it is instance-scoped data with no context column, per design gate §5.1's "always shown, tagged Platform" rule for the *attention* service's equivalent item; confirm whether `ListRecentActivityAsync`'s runtime-activity rows need an explicit "Platform" tag added at this layer or whether that's purely a UI-layer label applied in Task 3 — prefer doing it in the UI layer (Task 3) unless the DTO itself needs a new field to carry the distinction, in which case add a `IsPlatformWide : bool` (or similar) property to whatever DTO type `ListRecentActivityAsync` returns.

- [ ] **Step 6: Wire `ControlAttentionQueryService.ListAttentionAsync`**

Failed-executions branch (lines 48-76): construct `new RunQueryFilter(Status: WorkflowLifecycleStatusNames.Failed, CreatedFrom: createdFromUtc, ResourceContextId: resourceContextId)`.

Stuck-shutdowns branch (lines 78-122): **do not** add any context filter — leave the existing `_db.RuntimeControlRequests` query exactly as-is, and add a code comment explaining why (no `ResourceContextId` column exists on this entity, and design gate §5.1 requires this item to always show regardless of scope).

Rejected-controls branch (lines 124-168): add the join. Since no FK/navigation exists between `WorkflowExternalControlRequests` and `OperationRuns` (confirmed by research), build it as an explicit LINQ join:
```csharp
var rejectedControlsQuery = _db.WorkflowExternalControlRequests.AsNoTracking()
    .Where(x => x.Status == WorkflowExternalControlStatusNames.Rejected && x.RequestedAt >= createdFromUtc);

if (!string.IsNullOrWhiteSpace(resourceContextId))
{
    rejectedControlsQuery = rejectedControlsQuery
        .Join(_db.OperationRuns.AsNoTracking(), c => c.RunId, r => r.Id, (c, r) => new { Control = c, Run = r })
        .Where(x => x.Run.ResourceContextId == resourceContextId)
        .Select(x => x.Control);
}

var rejectedControls = await rejectedControlsQuery
    .Select(x => new { x.Id, x.RunId, x.WorkflowDefinitionId, x.ControlKind, x.RequestedAt })
    .ToListAsync(cancellationToken).ConfigureAwait(false);
```
This conditionally joins only when a filter is requested, so the unfiltered (All) path's query shape and performance are unchanged from today — matching design gate §5.1's "Callers that omit it keep today's behavior" for this specific query too, not just at the controller boundary.

- [ ] **Step 7: Wire `ControlCenterController`**

Add `[FromQuery] string? resourceContextId = null` to `GetCenter` (lines 20-31), pass it into `_center.GetControlCenterAsync(from, takeRecentActivity, takeAttention, resourceContextId, cancellationToken)`.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ControlCenterScopingTests"`
Expected: PASS

- [ ] **Step 9: Run the full existing Control Center test suite for regressions**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ControlCenter|FullyQualifiedName~ControlActivity|FullyQualifiedName~ControlAttention"`
Expected: PASS — every pre-existing test that omits the new parameter must still pass unchanged, since it's optional and defaulted.

- [ ] **Step 10: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ControlCenterController.cs src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IControlCenterQueryService.cs src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ControlCenterQueryService.cs src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IControlActivityQueryService.cs src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ControlActivityQueryService.cs src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IControlAttentionQueryService.cs src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ControlAttentionQueryService.cs tests/Apperture.UnitTests/ResourceContexts/ControlCenterScopingTests.cs
git commit -m "CTX-3A: thread resourceContextId through Control Center's run-derived attention/activity (A6, §5.1)"
```

---

## Task 2: Client `GetControlCenterAsync` gains `resourceContextId`

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs:114`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs:431-437`

- [ ] **Step 1: Update the interface**

```csharp
Task<ControlCenterDto> GetControlCenterAsync(string? resourceContextId = null, CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Update the implementation**

```csharp
public async Task<ControlCenterDto> GetControlCenterAsync(string? resourceContextId = null, CancellationToken cancellationToken = default)
{
    var path = string.IsNullOrWhiteSpace(resourceContextId) ? "control/center" : $"control/center?resourceContextId={Uri.EscapeDataString(resourceContextId)}";
    using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
    await EnsureSuccessOrThrowAsync(response, cancellationToken).ConfigureAwait(false);
    var dto = await response.Content.ReadFromJsonAsync<ControlCenterDto>(cancellationToken: cancellationToken).ConfigureAwait(false);
    return dto ?? new ControlCenterDto([], [], []);
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Client/Apperture.Management.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 4: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs
git commit -m "CTX-3A: add resourceContextId to the client GetControlCenterAsync (A6)"
```

---

## Task 3: Control Center UI — scope-aware subtitle, "In view"/"Platform" pill grouping, pre-history note (§5.1)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Control/ControlCenter.razor` (lines 13-15 subtitle, 39-56 pills, 59-63 Blocked work card, 79-116 Action needed, 697-772 load pipeline)

**Interfaces:**
- Consumes: `ResourceContextViewState.Current` (Plan 3), `IManagementClient.GetControlCenterAsync(resourceContextId, ct)` (Task 2).

- [ ] **Step 1: Make the subtitle scope-aware**

Change line 13-15's `PageHeader Subtitle="What needs attention now — blocked runs, failures, and recent outcomes."` to:
```razor
<PageHeader Eyebrow="Control" Title="Control Center" Subtitle="@Subtitle()" />
```
```csharp
private string Subtitle() =>
    ViewState.Current.IsAll
        ? "What needs attention now — blocked runs, failures, and recent outcomes."
        : $"What needs attention in {CurrentViewLabel()} — blocked runs, failures, and recent outcomes.";
```

- [ ] **Step 2: Split the summary pills into "In view" / "Platform — all applications" groups**

Replace the flat pill row (lines 39-56) with two labeled groups:
```razor
<div class="ui-pill-group">
    <div class="text-xs font-medium text-muted uppercase">In view</div>
    <div class="flex flex-wrap gap-2">
        <StatusPill Label="@($"Attention: {_dto?.AttentionItems.Count ?? 0}")" />
        <StatusPill Label="@($"Recent activity: {_dto?.RecentActivity.Count ?? 0}")" />
    </div>
</div>
<div class="ui-pill-group mt-3">
    <div class="text-xs font-medium text-muted uppercase">Platform — all applications</div>
    <div class="flex flex-wrap gap-2">
        <StatusPill Label="@($"Blocked work (all applications): {BlockedWorkCount}")" />
        @if (_capabilities?.CanViewRuntimeHealth == true)
        {
            <StatusPill Label="@($"Platform: {PlatformHealthLabel}")" />
            <StatusPill Label="@($"Worker: {WorkerStatusLabel}")" />
            <StatusPill Label="@($"Scheduler: {SchedulerStatusLabel}")" />
        }
    </div>
</div>
```
Move the existing "Blocked work" `ManagementSummaryCard` (lines 59-63) to sit under/near this Platform group rather than immediately after the pills as an independent top-level card, and relabel its title from "Blocked work" to "Blocked work (all applications)" to match design gate §5.1's exact instruction.

- [ ] **Step 3: Add the pre-history note under a specific view**

In the "Action needed" section (lines 79-116) and the "Recent activity" section (lines 404-480), add, only when `!ViewState.Current.IsAll`:
```razor
<div class="text-xs text-muted mb-2">Some older runs don't have a recorded context. Runs without a recorded context appear only under All.</div>
```
Use this **exact wording**, matching what Plan 4 already put on the Runs page — do not paraphrase.

- [ ] **Step 4: Thread the context filter into the load call**

Change `LoadAsync` (line 709) from `Client.GetControlCenterAsync(cancellationToken)` to `Client.GetControlCenterAsync(ViewState.Current.ToApiFilter(), cancellationToken)`. Subscribe to `ViewState.Changed` (inject `ResourceContextViewState`) and re-trigger `KickoffLoadAsync`/`LoadAsync` when it fires, matching whatever refresh pattern this page already uses for its periodic/manual refresh.

- [ ] **Step 5: "Control targets and diagnostics" stays untouched**

Confirm no change is made to the `ManagementAdvancedSection` at lines 482-484 and its contents (lines 485-621) — these are module/definition-level, not owned by any ResourceContext, per design gate §5.1's explicit "Unchanged" ruling. This step is a verification, not a code change: run a diff after Steps 1-4 and confirm this section's markup is byte-identical to before.

- [ ] **Step 6: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 7: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Control/ControlCenter.razor
git commit -m "CTX-3A: scope-aware Control Center subtitle, In view/Platform pill grouping, pre-history note (§5.1, R10)"
```

---

## Task 4: `control-panel-ui.md` — View selector section, Config menu update

**Files:**
- Modify: `docs/01-architecture/control-panel-ui.md`

- [ ] **Step 1: Add the "View selector" section**

Insert a new `## View selector` section after `## Layout` (line 15) and before `## Terminology` (line 17), describing: placement (top bar, leading edge of the orientation area, before the breadcrumb/title area), the label "View" (never Workspace/Application/Context), its three-layer persistence model (URL on scoped pages, tab state, per-user localStorage — no server preference), visibility rule (hidden until the first active Application exists), and a cross-reference to `docs/01-architecture/transitions/resource-contexts/ctx3-design-gate.md` §3-§4 for the full behavioral spec. Keep this section a concise operator/architecture-facing summary, not a restatement of the entire design gate.

- [ ] **Step 2: Update the Config menu sentence**

In `## Current top-level domains` (line 42), change:
```text
**Config** (gear): **Overview**, **Inventory**, **Governance**, **Attention** (...), **System** (...). Create/detail routes stay off-menu.
```
to insert **Applications** between **Inventory** and **Governance**:
```text
**Config** (gear): **Overview**, **Inventory**, **Applications**, **Governance**, **Attention** (...), **System** (...). Create/detail routes stay off-menu.
```
Add one sentence noting Applications manages ResourceContexts (Application workspaces + the read-only Shared context), with a cross-reference to the design gate's §6.

- [ ] **Step 3: Commit**

```bash
git add docs/01-architecture/control-panel-ui.md
git commit -m "CTX-3A: document the View selector and Config > Applications in control-panel-ui.md"
```

---

## Task 5: ADR-0040 confirming note

**Files:**
- Modify: `docs/02-decisions/ADR-0040-operator-information-architecture.md`

- [ ] **Step 1: Append a dated bullet under `### Neutral`**

After line 55 (the sentence listing "ResourceContext, Resource Catalog... Applications/Services... explicitly out of scope"), append:
```markdown
- **2026-09-13 (CTX-3A):** Application workspaces (ResourceContext-backed) landed as an organizational *view* — a top-bar "View" selector plus a Config → Applications management page — with no change to this ADR's rail (still four domains) or Control Panel structure. The selector is shell chrome, not a fifth domain; see `docs/01-architecture/transitions/resource-contexts/ctx3-design-gate.md` §1 and §3.1, and the CTX-3A closure report.
```

- [ ] **Step 2: Commit**

```bash
git add docs/02-decisions/ADR-0040-operator-information-architecture.md
git commit -m "CTX-3A: add confirming note to ADR-0040 — rail unchanged, View selector is shell chrome"
```

---

## Task 6: CTX-3A closure report

**Files:**
- Create: `docs/01-architecture/transitions/resource-contexts/ctx3a-closure.md`

**This task must be executed last, after Plans 1-5 and this plan's Tasks 1-5 are all merged and their test suites are green.** Its content is a factual report of what was actually built and validated — do not draft this file with assumed/aspirational results before the work exists.

- [ ] **Step 1: Run the full validation suite named in the design gate §27/§14, and capture real results**

Run, in order, capturing pass/fail and any environmental failures separately (do not fold a failing suite into a "green" summary):
```bash
dotnet build   # full solution build
dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj
dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj
dotnet test tests/Apperture.ArchTests/Apperture.ArchTests.csproj
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj
```
Also individually confirm the Management API, Management Host, WorkerHost, Persistence, `Apperture.Management.Client`, UI Shared, and UI Client projects build (per the original task's §27 checklist) — run `dotnet build` on each `.csproj` if the full-solution build doesn't already surface per-project status clearly enough to report each one.

- [ ] **Step 2: Perform the browser smoke walkthrough, or state plainly that it wasn't possible**

Follow the exact 25-step walkthrough from the original CTX-3A task brief (§26): no Applications → no selector; Config → Applications; create first Application; selector appears; All/Shared/Application switching; refresh; back/forward; new tab/direct scoped URL; Integration create under All; Integration create inside an Application view; Connection create; Shared Connection from an Application Integration; Schedules; Runs historical filtering; Control Center; detail mismatch notice; Business Data transitional notice; archive refusal; retire/disable live resources; archive succeeds; selector falls back to All; archived Application remains manageable/history-resolvable; dark appearance; light appearance; narrow-width layout.

If browser tooling is unavailable in the executing environment, the closure report **must** say so explicitly and provide this exact 25-step list as a manual checklist for the user to run themselves before CTX-3B begins — per the original task's explicit instruction, this is not optional.

- [ ] **Step 2: Write `ctx3a-closure.md`**

Follow `ctx2-closure.md`'s structure (front-matter block, then an opening "Executive result" section, since — per this plan's Global Constraints — the non-shipped status must be unmissable up front, matching ctx2's convention of leading with the load-bearing statement rather than diving straight into schema detail as ctx1 did). Required sections, each populated with real content from Step 1/2, not placeholders:

```markdown
# CTX-3A closure — Application Workspaces

Date: <actual date of this closure>
Status: **Complete as an internal engineering milestone. CTX-3B may begin.**
Branch: <actual branch/commit range this work landed on — per the user's chosen "work directly on master" strategy, this may instead read "landed as a sequence of commits on master between <first commit> and <last commit>">
Design basis: [`ctx3-design-gate.md`](./ctx3-design-gate.md)

---

## 1. Executive result

CTX-3A is an internal Application-workspaces milestone. The Application feature is not
considered fully shipped until CTX-3B Resource Movement is complete. [Then: what was proven,
in 1-2 paragraphs, referencing the actual validation results from Step 1/2 — not aspirational text.]

## 2. View model

[Summarize ResourceContextViewScope, ResourceContextViewRoutes, and the four-layer
URL/tab-state/localStorage resolution algorithm actually implemented — reference Plan 1
Tasks 6-7 and Plan 3 Task 6 by what they actually produced, confirmed against the real
merged code, not the plan's sketch.]

## 3. Application management

[Summarize the lifecycle: create, rename/describe/re-slug, archive with the refined
"nothing live" rule, the reactivation guard, and what the reactivation-guard test suite
(Plan 1 Tasks 10-12) actually proved once run.]

## 4. Scoped surfaces

[State exactly which surfaces obey View (Integrations, Connections, Schedules, Runs,
Control Center's contextual half) and which remain deliberately unscoped (Audit — own
filter instead; Business Data; Config Inventory; Execute; Notifications; Account) —
mirror design gate §5's table, confirmed against what actually shipped.]

## 5. Creation

[Summarize the explicit-choice-under-All / preselect-under-specific-view rule for
Integration and Connection, the in-flow binding-creation restriction, and Schedule's
read-only inherited display — confirmed against Plan 5's actual merged behavior.]

## 6. Authorization

[Confirm resource_context.manage is seeded and repaired into Platform Administrator,
surfaced as CanManageResourceContexts, and that no context-scoped IAM (no context:{id}
scope key, no context-scoped role/grant) was introduced — cite the specific non-change
tests (ResourceContextAuthorizationNonChangeTests) and their actual pass/fail state.]

## 7. Validation

[The exact, real results of Step 1's test/build run and Step 2's browser smoke —
including any environmental failures, reported separately from a "green" summary,
per the original task's explicit instruction not to hide filtered-only validation
behind a green summary.]

## 8. Deferred

Explicitly list, per the original task brief:
- CTX-3B resource movement (preview/move endpoints, IResourceOwnershipMoveService,
  row-lock serialization, two-sided move audit, binding_cross_context readiness code)
- Context-scoped authorization (never planned; explicitly out of scope by design)
- Connection/Schedule lifecycle cleanup (delete/hard-retire — separate roadmap work)
- Business Data collection architecture (DAT)
- Services
- Resource Catalog
[Plus anything this plan's own execution found itself unable to complete — e.g. if
Task 1's Control Center rejected-controls join was cut to the Unscoped-banner fallback
per this plan's Global Constraints, record that here explicitly, with the reason.]

## 9. Acceptance criteria

[A flat criterion/status table mirroring design gate §14's CTX-3A bullet list exactly,
one row per criterion, ✅/❌/⚠️ against what was actually verified in Step 1/2 — follow
ctx1-closure.md §14's table format.]
```

- [ ] **Step 3: Commit**

```bash
git add docs/01-architecture/transitions/resource-contexts/ctx3a-closure.md
git commit -m "CTX-3A: add closure report — internal milestone, CTX-3B required before the Application feature ships"
```

---

## Self-Review Notes

- **Spec coverage:** A6 and §5.1's full UI rule set (Tasks 1-3), `control-panel-ui.md`/ADR-0040 documentation (Tasks 4-5), and the closure report with its mandatory non-shipped statement (Task 6) are all covered.
- **Type consistency:** the `resourceContextId` parameter name and position (optional, before `CancellationToken`) is identical across all three interface layers in Task 1, and matches the naming convention already used by every other CTX-3A optional-context-filter parameter in Plans 1, 4, and 5.
- **No placeholders:** Task 6 is explicitly sequenced last and explicitly instructed to contain only real, run-and-observed results — it names the exact commands to run and the exact 25-step manual walkthrough to perform (or to hand to the user) rather than inventing a result. The one legitimate escape hatch in this plan (Task 1's join, falling back to the Unscoped-banner per the design gate's own allowance) is documented as a conditional, not silently assumed either way.
