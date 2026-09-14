# CTX-3A Plan 4 — Scoped Lists & Detail Indicators Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire `/integrations`, `/connections`, `/schedules`, `/runs` to the shell's selected View via `?context=`, and add the ownership chip + quiet mismatch notice to every detail/hub route — the "D with a C indicator" rule (design gate §4.6) plus the full scoped-surface matrix (§5).

**Architecture:** Each list page reads `ResourceContextViewState.Current` (Plan 3) and threads `ToApiFilter()` into the client call added in Plan 1 (`GetIntegrationsAsync`/`GetConnectionsAsync`/`GetSchedulesAsync`/`RunQueryFilter.ResourceContextId`). The one server-side gap remaining after Plan 1 is the Integrations **fleet** endpoint (fact F2/A3) — that server plumbing is done here, in Task 1, since it belongs with "scoped Integrations list" in the design gate's own step ordering (§13 step 7.1), not with the earlier client-contract-parity step. Detail pages get a small new `OwnershipChip` atom (no existing chip component to reuse — confirmed by research) and reuse the existing `PageMessage` component for the mismatch notice.

**Tech Stack:** Blazor WebAssembly, xUnit + FluentAssertions, EF Core InMemory for the Run-snapshot regression test.

**Spec:** `docs/01-architecture/transitions/resource-contexts/ctx3-design-gate.md` — this plan implements design-gate §13 CTX-3A steps 7–8 (`§4.6` detail-page ruling, `§5` surface matrix rows for Integrations/Connections/Schedules/Runs, `§5.2` Connections ownership browsing, `§5.3` Runs historical snapshot, `§10.1` rows A3/A8/A9, `§14` "Surfaces" and "Detail mismatch" acceptance criteria).

**Depends on:** `2026-09-13-ctx3a-01-contracts-and-domain.md` and `2026-09-13-ctx3a-03-config-ui-and-shell-selector.md` (both must be merged first — this plan consumes `ResourceContextViewState`, `ResourceContextViewScope`, and the Plan 1 client DTO fields).

## Global Constraints

- Under `All`, every scoped list sends **no** context filter and matches today's unfiltered result set exactly (design gate §14 "Surfaces").
- `/connections` under a specific Application view shows **only** that Application's owned Connections — Shared is never merged in (design gate §5.2, R3). The mitigation is a one-line "N Shared connections also available" notice linking to `?context=shared`, not inclusion in the list.
- `/runs?context=X` filters on `OperationRuns.ResourceContextId`, the CTX-2 immutable dispatch-time snapshot — **never** derived from the Integration's current ownership (design gate §5.3). This plan's Task 5 test is the specific regression the design gate names: a run snapshotted under A must still list under A after the owning Integration is changed to B in test setup.
- Detail/hub routes **never** filter their own data by the current View, never redirect away, and never auto-switch the View on open (design gate §4.6, R2). The mismatch notice is informational only, with an explicit "Switch view to …" action as the only thing that changes the View.
- Under `All`, there is never a mismatch notice (design gate §4.6).
- No canonical ID route changes (design gate §11/§29 acceptance: "no canonical ID routes change").
- Do not filter any list by the operator's current authorization context — View scope is organizational only (design gate §12, "Do not filter by current authorization context").
- Existing filters on each list (fleet filters, run status/date filters) remain AND-composed with the new context filter, not replaced by it (design gate §12).

---

## File Structure

**New files:**
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Atoms/OwnershipChip.razor` — the new chip atom (no existing `*Chip*` component to extend, per research).
- `tests/Apperture.UnitTests/ResourceContexts/RunHistoricalSnapshotFilteringTests.cs` — the named regression test.
- `tests/Apperture.UnitTests/ResourceContexts/IntegrationFleetContextFilterTests.cs`

**Modified files (server):**
- `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationsController.cs:102-143` (`Fleet` action gains `contextId`).
- `src/Apperture.Core/Apperture.Core.Application/Integration/IntegrationFleetModels.cs:34-44, 97-111` (`IntegrationFleetQuery`, `IntegrationFleetRowProjection` gain `ResourceContextId`).
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationReadinessService.cs:157-161` (`ListFleetAsync` calls the filtered `ListIntegrationsAsync` overload; maps `ResourceContextId` onto each row).
- `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationBindingsController.cs:82-108` (`GetEligibleConnections` projects `ResourceContextId`, A8).
- `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ConnectionsController.cs` (`ToDtoAsync`, A9 — `ConnectionUsedInEntry` gains `IntegrationResourceContextId`).

**Modified files (client):**
- `src/Apperture.ControlPlane/Apperture.Management.Client/IntegrationFleetDtos.cs` (`IntegrationFleetQueryParams.Context` already added in Plan 1 — this plan wires it into the query-string builder).
- `src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs:643-669` (`BuildIntegrationFleetPath` appends `contextId`).
- `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs` (`EligibleConnectionDto`, `ConnectionUsedInEntry` gain `ResourceContextId`/`IntegrationResourceContextId`).

**Modified files (UI pages):**
- `Pages/Integrations/IntegrationsList.razor` — `[SupplyParameterFromQuery(Name="context")]`, Application column under All, scoped empty state.
- `Pages/Connections/ConnectionsPage.razor` — same query param wiring, Shared-available notice.
- `Pages/Schedules/SchedulesList.razor` — same query param wiring.
- `Pages/Runs/RunsList.razor` — same query param wiring, Application column under All, pre-history note.
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/Epic33UiDisplayHelpers.cs:148-170` (`BuildRunsListUrl` gains `context`).
- `Pages/Integrations/IntegrationDetail.razor`, `IntegrationSettings.razor`, `IntegrationConnections.razor`, `IntegrationSchedules.razor`, `IntegrationRuns.razor`, `IntegrationNotifications.razor` (ownership chip in `PageHeader`'s `ChildContent`; mismatch notice).
- `Pages/Connections/ConnectionDetail.razor`, `Pages/Schedules/ScheduleDetail.razor`, `Pages/Runs/RunDetail.razor` (ownership chip; mismatch notice; Run detail uses the historical snapshot, not current ownership).
- `Pages/Integrations/IntegrationConnections.razor` (binding picker and "Used by" labels using A8/A9).

**Interfaces produced for later plans:**
- `<OwnershipChip ResourceContextId="..." ResourceContextName="..." />` — reused identically by Plan 5's creation-flow confirmation pages if needed, and by Plan 6's Control Center if it wants to show ownership on activity rows.
- `MismatchNotice` helper/partial pattern established across all detail pages — Plan 6 does not need it (Control Center is Partial mode, not Detail), but this establishes the one reusable idiom other future detail pages should follow.

---

## Task 1: Scoped Integrations fleet endpoint (A3, fact F2)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationsController.cs:102-143`
- Modify: `src/Apperture.Core/Apperture.Core.Application/Integration/IntegrationFleetModels.cs:34-44, 97-111`
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationReadinessService.cs:157-161`
- Test: `tests/Apperture.UnitTests/ResourceContexts/IntegrationFleetContextFilterTests.cs`

**Interfaces:**
- Consumes: `IIntegrationManagementService.ListIntegrationsAsync(ResourceContextId? resourceContextId, CancellationToken)` — the already-existing filtered overload (Plan 1's research confirmed this exists and is production-overridden in `IntegrationManagementService.cs:43-62`, calling `_integrationRepository.GetVisibleAsync(scopeKey, resourceContextId, ct)`).
- Produces: `GET api/integrations/fleet?contextId=` — 400 on an unknown id (reuse `IntegrationsController`'s existing `ResolveContextFilterAsync` helper, lines 85-97, already used by the plain `List` action); each fleet row carries `ResourceContextId`.

- [ ] **Step 1: Write the failing test**

Using this repo's existing `IntegrationReadinessService`/fleet test harness (locate the existing test file exercising `ListFleetAsync` first — likely `tests/Apperture.UnitTests/.../IntegrationReadinessServiceTests.cs` or similar), add:
```csharp
[Fact]
public async Task ListFleetAsync_WithResourceContextId_ReturnsOnlyThatContextsIntegrations()
{
    var harness = await IntegrationFleetTestHarness.CreateAsync();
    var contextA = await harness.CreateApplicationContextAsync("A");
    var contextB = await harness.CreateApplicationContextAsync("B");
    await harness.CreateIntegrationAsync(contextA.Id, "Integration A");
    await harness.CreateIntegrationAsync(contextB.Id, "Integration B");

    var query = new IntegrationFleetQuery(ResourceContextId: contextA.Id.Value);
    var result = await harness.ReadinessService.ListFleetAsync(query, CancellationToken.None);

    result.Should().ContainSingle(r => r.DisplayName == "Integration A");
    result.Single().ResourceContextId.Should().Be(contextA.Id.Value);
}

[Fact]
public async Task ListFleetAsync_WithoutResourceContextId_ReturnsEverything()
{
    var harness = await IntegrationFleetTestHarness.CreateAsync();
    var contextA = await harness.CreateApplicationContextAsync("A");
    await harness.CreateIntegrationAsync(contextA.Id, "Integration A");

    var result = await harness.ReadinessService.ListFleetAsync(new IntegrationFleetQuery(), CancellationToken.None);

    result.Should().Contain(r => r.DisplayName == "Integration A");
}
```
And a controller-level test (following whichever pattern `IntegrationsController`'s existing `List` 400-on-unknown-context test uses):
```csharp
[Fact]
public async Task Fleet_WithUnknownContextId_Returns400()
{
    var client = Factory.CreateAuthenticatedClient();

    var response = await client.GetAsync("api/integrations/fleet?contextId=does-not-exist");

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~IntegrationFleetContextFilterTests"`
Expected: FAIL — `IntegrationFleetQuery` has no `ResourceContextId` constructor parameter yet.

- [ ] **Step 3: Add `ResourceContextId` to `IntegrationFleetQuery` and `IntegrationFleetRowProjection`**

In `IntegrationFleetModels.cs`, append a trailing optional parameter to both records:
```csharp
// IntegrationFleetQuery, after Sort
string? ResourceContextId = null);
// IntegrationFleetRowProjection, after the existing trailing member
string? ResourceContextId = null);
```

- [ ] **Step 4: Fix `ListFleetAsync` to use the filtered overload and map the new field**

In `IntegrationReadinessService.cs`, change line 161 from:
```csharp
var integrations = await _integrationManagement.ListIntegrationsAsync(cancellationToken);
```
to:
```csharp
var resourceContextId = string.IsNullOrWhiteSpace(query.ResourceContextId) ? null : new ResourceContextId(query.ResourceContextId);
var integrations = await _integrationManagement.ListIntegrationsAsync(resourceContextId, cancellationToken);
```
Then, wherever each `IntegrationFleetRowProjection` is constructed further down in the method, add `ResourceContextId: i.ResourceContextId.Value` (matching whatever the loop variable for the current `DomainIntegration` is named).

- [ ] **Step 5: Thread `contextId` through the controller**

In `IntegrationsController.cs`, change the `Fleet` action (lines 102-143) to accept and validate `contextId`, reusing `ResolveContextFilterAsync`:
```csharp
[HttpGet("fleet")]
public async Task<ActionResult<...>> Fleet(
    [FromQuery] string? contextId,
    [FromQuery] string? state, /* ...existing params unchanged... */
    CancellationToken cancellationToken)
{
    var (contextFilter, contextError) = await ResolveContextFilterAsync(contextId, cancellationToken);
    if (contextError is not null) return BadRequest(new { error = contextError });

    var query = new IntegrationFleetQuery(/* ...existing fields..., */ ResourceContextId: contextFilter?.Value);
    var rows = await _readinessService.ListFleetAsync(query, cancellationToken);
    return Ok(rows.Select(ToFleetDto).ToList());
}
```
Add `ResourceContextId` to the `IntegrationFleetRowDto` mapping inside `ToFleetDto` (line ~541, per research).

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~IntegrationFleetContextFilterTests"`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationsController.cs src/Apperture.Core/Apperture.Core.Application/Integration/IntegrationFleetModels.cs src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationReadinessService.cs tests/Apperture.UnitTests/ResourceContexts/IntegrationFleetContextFilterTests.cs
git commit -m "CTX-3A: scope the Integrations fleet endpoint by contextId (A3, fact F2)"
```

---

## Task 2: `IntegrationsList.razor` — wire to the View, Application column, scoped empty state (§5, R6)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationsList.razor:1, 394-420`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs:643-669` (`BuildIntegrationFleetPath` appends `contextId`)

**Interfaces:**
- Consumes: `ResourceContextViewState.Current` (Plan 3), `IntegrationFleetQueryParams.Context` (Plan 1, now actually wired).

- [ ] **Step 1: Wire `BuildIntegrationFleetPath` to include `contextId`**

In `ManagementClient.cs`, inside `BuildIntegrationFleetPath`, add (matching the existing `parts.Add($"key=value")` idiom used for the other filters):
```csharp
if (!string.IsNullOrWhiteSpace(queryParams.Context))
    parts.Add($"contextId={Uri.EscapeDataString(queryParams.Context)}");
```

- [ ] **Step 2: Add `[SupplyParameterFromQuery(Name = "context")]` and inject `ResourceContextViewState`**

In `IntegrationsList.razor`, add `@inject ResourceContextViewState ViewState` and:
```csharp
[SupplyParameterFromQuery(Name = "context")]
public string? QueryContext { get; set; }
```
In `LoadAsync()` (lines 394-420), pass `Context = ViewState.Current.ToApiFilter()` into the `IntegrationFleetQueryParams` constructed at line 401. Since this page is `Scoped` mode (per `ResourceContextViewRoutes`), `ResourceContextViewState` has already resolved `Current` from the URL by the time this page's `OnParametersSetAsync` runs (Plan 3's `OnLocationChanged` handling covers scoped routes) — subscribe to `ViewState.Changed` in `OnInitialized`/dispose it, and re-run `LoadAsync()` when it fires, mirroring whatever pattern this page already uses for its other filter-changed reloads.

- [ ] **Step 3: Add the Application column under All**

In the results table, add a column (visible only when `ViewState.Current.IsAll`) rendering each row's owning Application name/"Shared", resolved from `ViewState.KnownActiveApplications` plus the `shared` sentinel — if a row's `ResourceContextId` is not in the active list (e.g. it belongs to an archived Application), fall back to showing the raw id or "—", consistent with how other pages handle unknown/archived ids.

- [ ] **Step 4: Add the scoped empty state (R6)**

Replace the existing unscoped `EmptyState` branch with a conditional: when `ViewState.Current` is a specific scope and the result set is empty, render `"No integrations in {ApplicationName}."` with a `[View all]` action (`NavigateTo` to `?context=all`) and `[Create integration in {ApplicationName}]` (`/integrations/new?context={id}`) — never the generic "No integrations are configured yet" message in that case.

- [ ] **Step 5: Verify build and run existing page-load tests**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Run any existing bUnit/bench tests for this page if they exist (search first).
Expected: SUCCEEDS / PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationsList.razor src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs
git commit -m "CTX-3A: wire /integrations to the shell View, Application column, scoped empty state (§5, R6)"
```

---

## Task 3: `ConnectionsPage.razor` — ownership-only scoping, Shared-available notice (§5.2)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Connections/ConnectionsPage.razor:1, 203`

**Interfaces:**
- Consumes: `IManagementClient.GetConnectionsAsync(contextId, ct)` (Plan 1), `ResourceContextViewState`, `IManagementClient.GetResourceContextSummaryAsync` (Plan 3, for the Shared-available count — "no extra query is needed" per design gate §5.2, since the summary is already cached).

- [ ] **Step 1: Wire the query param and client call**

Add `[SupplyParameterFromQuery(Name = "context")]`, inject `ResourceContextViewState`, and change line 203 from `_connections = await Client.GetConnectionsAsync();` to `_connections = await Client.GetConnectionsAsync(ViewState.Current.ToApiFilter());`.

- [ ] **Step 2: Add the Shared-available notice under a specific Application view**

```razor
@if (!ViewState.Current.IsAll && ViewState.Current.ResourceContextId != "shared" && _sharedConnectionCount > 0)
{
    <PageMessage Kind="PageMessageKind.Info">
        Integrations in @_currentApplicationName can also use <a href="/connections?context=shared" class="ui-link">@_sharedConnectionCount Shared connections</a>.
    </PageMessage>
}
```
Source `_sharedConnectionCount` from the already-fetched `ResourceContextOwnershipSummaryDto` for `"shared"` (via a shared/cached summary lookup, not a new HTTP call — confirm `ResourceContextViewState` exposes the summary dictionary, or fetch it once here and cache alongside the view state if it doesn't; design gate explicitly says "Count N from the already-cached summary endpoint... No extra query is needed," so if `ResourceContextViewState` doesn't already cache summaries, extend it in this task rather than adding a second cache).

- [ ] **Step 3: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 4: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Connections/ConnectionsPage.razor
git commit -m "CTX-3A: wire /connections to ownership-only scoping with Shared-available notice (§5.2, R3)"
```

---

## Task 4: `SchedulesList.razor` — wire to the View (§5 surface matrix)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/SchedulesList.razor:1, 244`

**Interfaces:**
- Consumes: `IManagementClient.GetSchedulesAsync(contextId, ct)` (Plan 1), `ResourceContextViewState`.

- [ ] **Step 1: Wire the query param and client call**

Same shape as Task 3: `[SupplyParameterFromQuery(Name = "context")]`, `ViewState.Current.ToApiFilter()` passed to `GetSchedulesAsync`.

- [ ] **Step 2: Note on attached vs. legacy standalone schedules**

No special-casing is needed here: the server's `ScheduleDto.ResourceContextId` already mirrors the owning Integration's context for attached schedules (a CTX-1 invariant) and carries the schedule's own value for legacy standalone rows — the single `contextId` filter parameter is already correct for both. Do not add a second "legacy only" filter path.

- [ ] **Step 3: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 4: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/SchedulesList.razor
git commit -m "CTX-3A: wire /schedules to the shell View (§5)"
```

---

## Task 5: Scoped Runs — historical snapshot filtering, `BuildRunsListUrl`, pre-history note (§5.3)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/Epic33UiDisplayHelpers.cs:148-170` (`BuildRunsListUrl`)
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunsList.razor:258-271, 325-362`
- Test: `tests/Apperture.UnitTests/ResourceContexts/RunHistoricalSnapshotFilteringTests.cs`
- Test: extend `tests/Apperture.UnitTests/ControlPlane/Epic33CrossNavigationTests.cs:105-143` for the new `context` argument.

**Interfaces:**
- Consumes: `RunQueryFilter.ResourceContextId` (Plan 1, client-side), server-side `RunQueryService.ListRunsAsync` (already filters on `OperationRuns.ResourceContextId` — no server change needed here, confirmed by Plan 1's research).

- [ ] **Step 1: Write the named regression test first**

This is the exact test the design gate names in §5.3 and §14. Using a real `AppertureDbContext` over EF Core InMemory (following `ResourceContextOwnershipTestHarness`'s pattern):
```csharp
[Fact]
public async Task RunSnapshottedUnderA_StillListsUnderA_AfterIntegrationsCurrentOwnershipBecomesB()
{
    await using var harness = await ResourceContextOwnershipTestHarness.CreateAsync();
    var contextA = await harness.ResourceContexts.CreateApplicationContextAsync("A");
    var contextB = await harness.ResourceContexts.CreateApplicationContextAsync("B");
    var integration = await harness.CreateIntegrationAsync(contextA.Id, state: IntegrationState.Active);

    // Simulate a run dispatched while the integration was owned by A (CTX-2 snapshot semantics).
    await harness.SeedOperationRunAsync(integrationId: integration.Id, resourceContextId: contextA.Id);

    // Controlled test setup: directly rewrite the Integration's current ownership to B,
    // bypassing any CTX-3B movement machinery (which does not exist yet) — this is purely
    // to prove the Run's own snapshot column, not the Integration's current column, drives the filter.
    await harness.SetIntegrationResourceContextDirectlyAsync(integration.Id, contextB.Id);

    var runsUnderA = await harness.RunQueryService.ListRunsAsync(new RunQueryFilter(ResourceContextId: contextA.Id.Value));
    var runsUnderB = await harness.RunQueryService.ListRunsAsync(new RunQueryFilter(ResourceContextId: contextB.Id.Value));

    runsUnderA.Should().ContainSingle();
    runsUnderB.Should().BeEmpty();
}

[Fact]
public async Task NullSnapshotRuns_AppearOnlyUnderAll()
{
    await using var harness = await ResourceContextOwnershipTestHarness.CreateAsync();
    await harness.SeedOperationRunAsync(integrationId: null, resourceContextId: null);

    var underAll = await harness.RunQueryService.ListRunsAsync(new RunQueryFilter());
    var underShared = await harness.RunQueryService.ListRunsAsync(new RunQueryFilter(ResourceContextId: "shared"));

    underAll.Should().ContainSingle();
    underShared.Should().BeEmpty();
}
```
`SeedOperationRunAsync` and `SetIntegrationResourceContextDirectlyAsync` are new harness helpers for this task — the latter must write directly via the `DbContext` (`_db.Integrations.Find(...).ResourceContextId = ...; await _db.SaveChangesAsync();`), explicitly bypassing `IntegrationManagementService`, since no supported API changes ownership yet (that's CTX-3B) and the test's whole point is to prove the Run filter is immune to exactly this kind of direct/future ownership change.

- [ ] **Step 2: Run tests to verify they fail or pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~RunHistoricalSnapshotFilteringTests"`
Expected: Likely PASS immediately, since `RunQueryService.ListRunsAsync` already applies `ResourceContextId` server-side per Plan 1's research (`RunQueryService.cs:70-71`). If it fails, the server-side filter has a bug this task must then fix — do not skip investigating a failure here by assuming the test is wrong.

- [ ] **Step 3: Add `context` to `BuildRunsListUrl`**

In `Epic33UiDisplayHelpers.cs`, change (lines 148-170):
```csharp
public static string BuildRunsListUrl(
    string? correlationKey = null, string? status = null, string? createdFrom = null,
    string? integrationId = null, string? moduleId = null)
```
to:
```csharp
public static string BuildRunsListUrl(
    string? correlationKey = null, string? status = null, string? createdFrom = null,
    string? integrationId = null, string? moduleId = null, string? context = null)
```
and add `Add("context", context);` alongside the other `Add(...)` calls in the method body.

- [ ] **Step 4: Update the existing `Epic33CrossNavigationTests` and add a `context`-preserving case**

Read `tests/Apperture.UnitTests/ControlPlane/Epic33CrossNavigationTests.cs:105-143` and add a test proving `context` round-trips through `BuildRunsListUrl` alongside the existing params, following that file's exact assertion style.

- [ ] **Step 5: Wire `RunsList.razor`**

Add `[SupplyParameterFromQuery(Name = "context")] public string? QueryContext { get; set; }` alongside the existing five query params (lines 258-271). In `LoadAsync()` (lines 356-362), pass `ResourceContextId: QueryContext` into the constructed `RunQueryFilter` — note this requires `QueryContext` to already be `all`-aware or `null`-aware: since `RunQueryFilter.ResourceContextId` maps directly to the API filter (no `all` sentinel exists server-side), convert via `ResourceContextViewScope.Parse(QueryContext)?.ToApiFilter()` before passing it, so `all` correctly becomes `null` rather than being sent literally as the string `"all"` to the server (which would 400, since `"all"` is not a real ResourceContext id).

In `OnStatusFacetChanged` (line 333) and `BuildLoadKey` (line 325-326), add `context: QueryContext` to the `BuildRunsListUrl` call and to the load-key composition, exactly as the design gate specifies (§5.3, "so the status facet dropdown preserves it").

- [ ] **Step 6: Add the Application column under All and the pre-history note**

Under `ViewState.Current.IsAll`, add a column resolving each run's `ResourceContextId` to an Application name via `ViewState.KnownActiveApplications` (null → "—"). Under a specific view, add the same muted pre-history line used in Control Center (Plan 6 introduces the canonical copy — for this task, use the design gate's exact wording so both surfaces match verbatim): *"Runs recorded before applications were introduced aren't associated with an application and appear only under All."*

- [ ] **Step 7: Run tests to verify everything passes**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~RunHistoricalSnapshotFilteringTests|FullyQualifiedName~Epic33CrossNavigationTests"`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/Epic33UiDisplayHelpers.cs src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunsList.razor tests/Apperture.UnitTests/ResourceContexts/RunHistoricalSnapshotFilteringTests.cs tests/Apperture.UnitTests/ControlPlane/Epic33CrossNavigationTests.cs
git commit -m "CTX-3A: scope /runs by historical ResourceContext snapshot, preserve context through status facets (§5.3, R11)"
```

---

## Task 6: `OwnershipChip` atom (§4.6)

**Files:**
- Create: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Atoms/OwnershipChip.razor`

**Interfaces:**
- Produces: `<OwnershipChip ResourceContextId="@dto.ResourceContextId" ResourceContextName="@resolvedName" />` — an inline link-styled span reading the Application/Shared name, linking to `/config/applications/{id}`, hidden entirely when `ViewState.SelectorVisible` is `false` (design gate §3.3: "It is hidden while no Application exists").

- [ ] **Step 1: Build the component**

```razor
@inject ResourceContextViewState ViewState

@if (ViewState.SelectorVisible && !string.IsNullOrWhiteSpace(ResourceContextId))
{
    <a href="@($"/config/applications/{ResourceContextId}")" class="ui-ownership-chip" title="Owning application">
        @DisplayName()
    </a>
}

@code {
    [Parameter] public string? ResourceContextId { get; set; }
    [Parameter] public string? ResourceContextName { get; set; }

    private string DisplayName() =>
        ResourceContextId == "shared" ? "Shared" : (ResourceContextName ?? ResourceContextId ?? "—");
}
```
Style `.ui-ownership-chip` following `StatusPill`'s CSS-class-mapping approach (a small span/pill with a link affordance) — since `StatusPill` itself doesn't render a link, this component's markup is new but its visual weight should match the existing pill vocabulary (confirm against the design system doc, `management-ui-design-system.md`, before finalizing exact classes).

- [ ] **Step 2: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 3: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Atoms/OwnershipChip.razor
git commit -m "CTX-3A: add OwnershipChip atom for detail-page headers (§4.6)"
```

---

## Task 7: Ownership chip + mismatch notice on Integration hub, Connection, Schedule, Run detail (§4.6)

**Files:**
- Modify: `Pages/Integrations/IntegrationDetail.razor:17-37`, `IntegrationSettings.razor`, `IntegrationConnections.razor`, `IntegrationSchedules.razor`, `IntegrationRuns.razor`, `IntegrationNotifications.razor` — six hub pages, each has its own `PageHeader`.
- Modify: `Pages/Connections/ConnectionDetail.razor:15-20` (already uses `ChildContent`, per research).
- Modify: `Pages/Schedules/ScheduleDetail.razor:13-16` (needs a `ChildContent` block added — doesn't use one today).
- Modify: `Pages/Runs/RunDetail.razor:14-18` (net-new ownership display — none exists today per research).

**Interfaces:**
- Consumes: `OwnershipChip` (Task 6), `PageMessage` (existing, `PageMessageKind.Info`), `ResourceContextViewState.Current`.

- [ ] **Step 1: Add a small shared mismatch-notice partial pattern**

Since seven+ pages need near-identical mismatch markup, extract a tiny shared component rather than duplicating the conditional seven times:
```razor
@* MismatchNotice.razor, in Atoms or Molecules — confirm the right folder by this project's existing size convention *@
@if (ShowMismatch)
{
    <PageMessage Kind="PageMessageKind.Info">
        This @ResourceKindLabel belongs to @OwnerLabel. Your view is @CurrentViewLabel.
        @ExtraCopy
        <ChildContent>
            <button class="ui-link" @onclick="SwitchAsync">Switch view to @OwnerLabel</button>
        </ChildContent>
    </PageMessage>
}

@code {
    [Parameter] public string? OwnerResourceContextId { get; set; }
    [Parameter] public string OwnerLabel { get; set; } = "";
    [Parameter] public string ResourceKindLabel { get; set; } = "resource";
    [Parameter] public string? ExtraCopy { get; set; }
    [Inject] public ResourceContextViewState ViewState { get; set; } = default!;

    private bool ShowMismatch =>
        !ViewState.Current.IsAll && !string.IsNullOrWhiteSpace(OwnerResourceContextId) &&
        !string.Equals(ViewState.Current.ResourceContextId, OwnerResourceContextId, StringComparison.Ordinal);

    private string CurrentViewLabel() => /* resolve ViewState.Current to a display name, mirroring ViewSelector's CurrentLabel() logic */;

    private async Task SwitchAsync() =>
        await ViewState.SetExplicitAsync(ResourceContextViewScope.Of(OwnerResourceContextId!));
}
```

- [ ] **Step 2: Add the chip + notice to each of the six Integration hub pages**

For each of `IntegrationDetail.razor`, `IntegrationSettings.razor`, `IntegrationConnections.razor`, `IntegrationSchedules.razor`, `IntegrationRuns.razor`, `IntegrationNotifications.razor`: add `<OwnershipChip ResourceContextId="@_integration?.ResourceContextId" ResourceContextName="@_integration?.ResourceContext?.Name" />` inside the existing `PageHeader`'s `ChildContent` (adding that slot if a given page doesn't use it yet — the research confirmed none of the six currently uses `ChildContent` on their own `PageHeader`, so this is new plumbing on every one of the six, not just an addition), plus `<MismatchNotice OwnerResourceContextId="@_integration?.ResourceContextId" OwnerLabel="@(_integration?.ResourceContext?.Name ?? "Shared")" ResourceKindLabel="integration" />` immediately below the header. This requires each page's `_integration` load to actually populate `ResourceContextId`/`ResourceContext` — confirm the `IntegrationDto` deserialization already carries these fields now that Plan 1 added them to the client DTO; no extra fetch should be needed.

- [ ] **Step 3: Add the chip + notice to Connection detail**

`ConnectionDetail.razor` already uses `ChildContent` (per research, lines 18-20, for the connection id) — append the chip alongside the existing content rather than replacing it. Add `MismatchNotice` with `ResourceKindLabel="connection"`.

- [ ] **Step 4: Add the chip + notice to Schedule detail**

`ScheduleDetail.razor` does not use `ChildContent` today — add the slot. `ResourceKindLabel="schedule"`.

- [ ] **Step 5: Add the chip + notice to Run detail using the historical snapshot**

`RunDetail.razor` has no ownership display today. Add the chip sourced from `RunDetailDto.ResourceContextId` (Plan 1 added this client-side) — **not** any current-Integration lookup. When `ResourceContextId` is `null`, render "Application not recorded" instead of the chip, and suppress the mismatch notice entirely in that case (design gate §4.6: "A null snapshot shows 'Application not recorded' and no mismatch").

- [ ] **Step 6: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 7: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/*.razor src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Connections/ConnectionDetail.razor src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/ScheduleDetail.razor src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunDetail.razor
git commit -m "CTX-3A: add ownership chip and quiet mismatch notice to every detail/hub route (§4.6)"
```

---

## Task 8: Binding-picker and "Used by" labels — A8/A9

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationBindingsController.cs:82-108` (`GetEligibleConnections`)
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ConnectionsController.cs` (`ToDtoAsync`, `ConnectionUsedInEntry`)
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs:1156, 1162-1167` (`EligibleConnectionDto`, `ConnectionUsedInEntry`)
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationConnections.razor`

**Interfaces:**
- Produces: `EligibleConnectionDto.ResourceContextId : string?`, `ConnectionUsedInEntry.IntegrationResourceContextId : string?`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task GetEligibleConnections_IncludesResourceContextIdOnEachRow()
{
    // ... arrange an integration and an eligible connection in the same context ...
    var response = await client.GetAsync($"api/integrations/{integrationId}/bindings/eligible-connections?slotName=x&contractType=y");
    var rows = await response.Content.ReadFromJsonAsync<List<EligibleConnectionResponseRow>>();
    rows.Should().ContainSingle(r => r.ResourceContextId == expectedContextId);
}

[Fact]
public async Task ConnectionUsedInEntry_IncludesIntegrationResourceContextId()
{
    // ... arrange a connection bound to an integration in context A ...
    var connectionDto = await GetConnectionDtoAsync(connectionId);
    connectionDto.UsedIn.Should().ContainSingle(e => e.IntegrationResourceContextId == "A-context-id");
}
```
Fill in the arrange steps against whatever existing test harness `IntegrationBindingsController`/`ConnectionsController` already have test coverage from (locate and follow their existing setup helpers).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~EligibleConnections|FullyQualifiedName~ConnectionUsedInEntry"`
Expected: FAIL.

- [ ] **Step 3: Add `ResourceContextId` to the eligible-connections response**

In `IntegrationBindingsController.cs`, the `GetEligibleConnections` action currently returns an anonymous type (lines 82-108, per research: `new { Id = c.Id.Value, c.Name, ConnectorDefinitionId = c.ConnectorDefinitionId.Value }`). Change to a named record (add it near the controller, or reuse `EligibleConnectionDto` if the server already has an equivalent named type — research suggests it doesn't, the server uses an anonymous type here) so the shape is explicit:
```csharp
public sealed record EligibleConnectionResponseRow(string Id, string Name, string ConnectorDefinitionId, string ResourceContextId);
// ...
return Ok(connections.Select(c => new EligibleConnectionResponseRow(c.Id.Value, c.Name, c.ConnectorDefinitionId.Value, c.ResourceContextId.Value)).ToList());
```

- [ ] **Step 4: Add `ResourceContextId` to the client `EligibleConnectionDto`**

In `IManagementClient.cs:1156`, change:
```csharp
public sealed record EligibleConnectionDto(string Id, string Name, string ConnectorDefinitionId);
```
to:
```csharp
public sealed record EligibleConnectionDto(string Id, string Name, string ConnectorDefinitionId, string? ResourceContextId = null);
```

- [ ] **Step 5: Add `IntegrationResourceContextId` to `ConnectionUsedInEntry` (both sides)**

Server (`ConnectionsController.cs`, lines ~206-216 and ~246-251): the `integration` object is already loaded in the loop — add `integration?.ResourceContextId.Value` as a new trailing constructor argument. Client (`IManagementClient.cs:1162-1167`): append the matching trailing optional parameter.

- [ ] **Step 6: Add "This application"/"Shared" labels in `IntegrationConnections.razor`**

In the binding-picker dropdown (sourced from `_eligibleConnections`), render each option's label suffixed with `(Shared)` when `ResourceContextId == "shared"`, or the resolved Application name otherwise, using `ViewState.KnownActiveApplications` for the lookup (falling back to the raw id for archived contexts). In the "Used by" rows on Connection detail (sourced from `ConnectionUsedInEntry`), do the same for `IntegrationResourceContextId`.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~EligibleConnections|FullyQualifiedName~ConnectionUsedInEntry"`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationBindingsController.cs src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ConnectionsController.cs src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationConnections.razor
git commit -m "CTX-3A: label binding picker and 'Used by' rows with owning Application/Shared (A8, A9)"
```

---

## Self-Review Notes

- **Spec coverage:** A3 (Task 1), scoped Integrations/Connections/Schedules/Runs (Tasks 2-5), the named Run-snapshot regression test (Task 5), ownership chip + mismatch notice across all detail/hub routes (Tasks 6-7), A8/A9 (Task 8) are all covered. §5's "do not filter by current authorization context" and "maintain existing filters as AND-composed" are respected by construction — every task adds `contextId`/`context` as one more parameter alongside existing filters, never replacing them.
- **Type consistency:** `OwnershipChip`/`MismatchNotice` component parameter names are used identically across all seven consuming pages in Task 7; `ResourceContextId`/`ResourceContextName` naming matches Plan 1's DTO field names exactly, so no page needs to remap field names.
- **No placeholders:** the one open question (exact CSS class naming for `OwnershipChip`, matched against `management-ui-design-system.md`) is flagged as "confirm before finalizing," not silently assumed; the Run-snapshot regression test's "controlled test setup" (direct DB write, bypassing any real API) is called out explicitly as intentional and CTX-3B-anticipating, not a shortcut to hide.
