# CTX-3A Plan 4 — Scoped Lists & Detail Indicators Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire `/integrations`, `/connections`, `/schedules`, `/runs` to the shell's selected View through a URL-authoritative `?context=`, add the Context chip and quiet mismatch notice to every detail/hub route (design gate §4.6, the scoped-surface matrix §5), and add Config Inventory's own Context column and filter (§5.5), independent of the shell View.

**Architecture:**

- **Scoped lists (Tasks 2–5) load from the URL, not from view state.** Each page builds its API filter from its own validated `?context=` value through a new pure helper, `ScopedListContextFilter` (Task 2), and reloads when that value changes — the pattern `RunsList.razor` already uses for its other URL filters. `ResourceContextViewState` (Plan 3) still owns resolution, URL rewriting and localStorage; pages never read `ViewState.Current` to build a request and never reload on `ViewState.Changed`. Reason: first load is already safe (`App.razor` awaits `ViewState.InitializeAsync()` before `ShellBootstrap.MarkReady()`, and `AuthRouteGate.razor` blocks page rendering until then), but on in-app navigation `ResourceContextViewState.OnLocationChanged` awaits a JS call before it updates `Current`. A page reading `Current` could send the previous view's request first — for example after following `/connections?context=shared` from Task 3's own notice.
- **The fleet endpoint gap (Task 1).** The one server-side list gap left after Plan 1 is the Integrations fleet endpoint (fact F2/A3), done in Task 1 because the design gate groups it with the scoped Integrations list (§13 step 7.1).
- **One display path for context names (Task 2 Step 1).** `ResourceContextViewState` gains `KnownContexts`, `EnsureContextsKnownAsync` and `DescribeContext`. Every operator-facing context name goes through them: active and archived contexts resolve by id; an id missing from the cache triggers one refresh of the active+archived list; an id still missing renders "Unknown context". `KnownActiveApplications` stays for selector and picker options only.
- **Detail pages (Tasks 6–7)** get a new `OwnershipChip` atom (no existing chip to reuse) and a `MismatchNotice` molecule built on the existing `PageMessage`. Both take only an id and resolve the name themselves.
- **Config Inventory (Task 9)** is `Unscoped`. It uses `ResourceContextViewState` only as the context directory (`KnownContexts`, `EnsureContextsKnownAsync`, `DescribeContext`) — never `Current`, `SelectorVisible` or the shell's `?context=`. Classification derives at query time from existing `ResourceContextId` columns, an Integration lookup for integration-scoped NotificationDefinitions, and one shared audit action-prefix list. The Context predicate is applied inside each capped source query. No new persistence. If sharing `ResourceContextViewState` this way becomes awkward (for example, if Inventory would need view-state initialization it does not otherwise use), extract a small shared context-directory/display resolver that both use, rather than coupling Inventory to View semantics.

**Tech Stack:** Blazor WebAssembly, xUnit + FluentAssertions, EF Core InMemory. There is no bUnit in this repo: `Apperture.Management.Ui.Client.Tests` tests `ResourceContextViewState` directly as a plain C# service with hand-rolled `NavigationManager`/`IJSRuntime` doubles, and `Apperture.Management.Ui.Shared.Tests` tests pure UI helpers. Page-level reload behavior is covered by testing those two layers plus the closing manual smoke at the end of this plan.

**Spec:** `docs/01-architecture/transitions/resource-contexts/ctx3-design-gate.md` — this plan implements design-gate §13 CTX-3A steps 7–8 (`§3.3` ownership-label visibility, `§4.2`/`§4.3` URL-authoritative scoped pages, `§4.6` detail-page ruling and display resolution, `§5` surface matrix rows for Integrations/Connections/Schedules/Runs, `§5.2` Connections ownership browsing, `§5.3` Runs historical snapshot, `§5.5` Config Inventory, `§10.1` rows A3/A8/A9/A12/A13/A14, `§14` "Surfaces", the archived-resolution part of "Management", and "Detail mismatch" acceptance criteria).

**Depends on:** `2026-09-13-ctx3a-01-contracts-and-domain.md` and `2026-09-13-ctx3a-03-config-ui-and-shell-selector.md` (both merged). Plan 5 depends on this plan for `AuditFilterCriteria.ResourceContextId` and `ResourceContextViewState.KnownContexts`/`DescribeContext`.

## Global Constraints

- Under `All`, every scoped list sends **no** context filter and matches today's unfiltered result set exactly (design gate §14 "Surfaces").
- **Scoped collection pages are URL-authoritative** (`/integrations`, `/connections`, `/schedules`, `/runs`; design gate §4.2, §14). The API filter comes only from the page's validated `?context=` value via `ScopedListContextFilter`. No request is sent while the URL has no `?context=` yet or names an id that is still unknown after one refresh. The page reloads whenever the value changes. A page never builds a request from `ViewState.Current` and never reloads on `ViewState.Changed`. A request for one context followed by a corrective request for another is a defect.
- **Context display resolution** (design gate §4.6). Every ResourceContext id shown to an operator is rendered with `ResourceContextViewState.DescribeContext(id)` after `EnsureContextsKnownAsync(ids)`: active and archived contexts resolve to their name (archived ones suffixed " (archived)"); a missing id triggers one refresh of the active+archived list; an id still missing renders "Unknown context". Never `KnownActiveApplications` for an id→name lookup, never a raw id, never blank text. An unresolved owner shows "Unknown context", suppresses "Switch view", and is never presented as Shared or System.
- **Ownership labels follow the selector** (design gate §3.3). On operational pages, All-view Context columns, Context chips, "Context not recorded", mismatch notices and binding/"Used by" Context labels render only when `ViewState.SelectorVisible` (at least one active Application). With no active Application, those pages look exactly as they did before Applications. Config → Inventory is the one explicit exception (§5.5).
- **Terminology.** Operator copy says "Context" wherever the value can be Shared, System, an Application or Not recorded (column headers, chip text "Context: CampusNexus", "Context not recorded", "Unknown context", the Inventory filter). "Application" is kept for Application management (Config → Applications, New Application, Archive Application). The selector stays "View". "ResourceContext" never appears in operator copy.
- `/connections` under a specific Application view shows **only** that Application's owned Connections — Shared is never merged in (design gate §5.2, R3). The mitigation is a one-line "N Shared connections" notice linking to `?context=shared`.
- `/runs?context=X` filters on `OperationRuns.ResourceContextId`, the CTX-2 dispatch-time snapshot — **never** the Integration's current ownership (design gate §5.3). Task 5's regression test proves it. Under a specific context, Runs shows "Some older runs don't have a recorded context. Runs without a recorded context appear only under All." — the same wording Control Center uses (design gate §5.1).
- Detail/hub routes **never** filter their own data by the View, never redirect, and never auto-switch the View (design gate §4.6, R2). "Switch view to …" is the only action that changes it. Under `All` there is never a mismatch notice.
- **Config Inventory** (design gate §5.5). Independent of the shell View: it may use `ResourceContextViewState` only as a context directory (known contexts and names), never `Current`, `SelectorVisible` or the shell's `?context=`. Filter values: `all`, `system`, `shared`, `notRecorded` (Activity mode only), or a real active or archived ResourceContext id; anything else — including `notRecorded` in Resources mode — returns 400, and the page offers "Reset to All". Switching from Activity to Resources resets the filter to All first. The Context column and filter are always present, even with zero Applications, and never make the shell selector visible. Resources use current ownership, or System when structurally non-contextual; Activity uses the context recorded at occurrence. Runs: a recorded snapshot is Shared/Application, a null one is Not recorded; Runs are never System and never use action-name classification. Audit events: id-first; a null context is System only for the explicitly listed `AuditSystemActionPrefixes`, otherwise Not recorded — one rule shared by display classification and SQL filtering. NotificationDefinitions: integration-scoped with an existing Integration take that Integration's current context; Platform/Module-scoped are System; integration-scoped whose Integration no longer exists are `unknown` ("Unknown context"), shown only under All and never System or Not recorded. The Context predicate is applied inside every capped source query (Runs, audit events, NotificationDefinitions) before its cap. No new persistence.
- No canonical ID route changes (design gate §14).
- Do not filter any list by the operator's current authorization context — View scope is organizational only (design gate §12).
- Existing filters on each list (fleet filters, run status/date filters, Inventory type/status/search) remain AND-composed with the context filter (design gate §12).

---

## File Structure

**New files:**
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ScopedListContextFilter.cs` — pure `?context=` → request-filter resolution for scoped lists (Task 2).
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Atoms/OwnershipChip.razor` (Task 6).
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Molecules/MismatchNotice.razor` (Task 7).
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Molecules/ResourceContextViewNotices.razor` — displays Plan 3's view notices with the updated copy (Task 2).
- `src/Apperture.Core/Apperture.Core.Application/Services/AuditSystemActionPrefixes.cs` — the one System-by-design audit prefix list, shared by classification and SQL filtering (Task 9).
- `tests/Apperture.Management.Ui.Shared.Tests/ScopedListContextFilterTests.cs` (Task 2).
- `tests/Apperture.UnitTests/ResourceContexts/IntegrationFleetContextFilterTests.cs` (Task 1).
- `tests/Apperture.UnitTests/ResourceContexts/RunHistoricalSnapshotFilteringTests.cs` (Task 5).
- `tests/Apperture.UnitTests/ResourceContexts/InventoryContextClassificationTests.cs` (Task 9).

**Modified files (server):**
- `Apperture.Management.Api/Controllers/IntegrationsController.cs:102-143` — `Fleet` gains `contextId` (Task 1, A3).
- `Apperture.Core.Application/Integration/IntegrationFleetModels.cs:34-44, 97-111` — `ResourceContextId` on query and row (Task 1).
- `Apperture.ControlPlane.Security/IntegrationReadinessService.cs:157-161` — filtered `ListIntegrationsAsync` overload (Task 1).
- `Apperture.Management.Api/Controllers/IntegrationBindingsController.cs:82-108` — eligible connections carry `ResourceContextId` (Task 8, A8).
- `Apperture.Management.Api/Controllers/ConnectionsController.cs` — `ConnectionUsedInEntry.IntegrationResourceContextId` (Task 8, A9).
- `Apperture.ControlPlane.Security/IInventoryQueryService.cs`, `InventoryQueryService.cs`, `Apperture.Management.Api/Controllers/InventoryController.cs` — Context filter, validation, classification, source filtering (Task 9, A12).
- `Apperture.Core.Application/Services/AuditTimelineModels.cs`, `AuditQuery.cs`, `Apperture.ControlPlane.Security/Audit/AuditTimelineQueryService.cs`, `Apperture.Management.Api/Controllers/AuditController.cs` (entry→DTO mapping only), `Apperture.Persistence.SqlServer/SqlAuditService.cs` — audit read-side `ResourceContextId` and context-classification filter (Task 9, A13/A14).
- `Apperture.ControlPlane.Security/IRunQueryService.cs`, `RunQueryService.cs` — `RunQueryFilter.ResourceContextIdIsNull` (Task 9, A14).
- `Apperture.Core.Application/Notifications/INotificationDefinitionStore.cs`, `Apperture.Persistence.SqlServer/SqlNotificationDefinitionStore.cs`, `INotificationDefinitionService` and its implementation — owner-context and system-scope list filters, batched Integration owner lookup (Task 9, A14).

**Modified files (client):**
- `Apperture.Management.Client/ManagementClient.cs:643-669` — `BuildIntegrationFleetPath` appends `contextId` (Task 2).
- `Apperture.Management.Client/ManagementClient.cs:439-463`, `InventoryDtos.cs`, `IManagementClient.cs` — Inventory `context` and classification fields; `AuditTimelineEntryDto.ResourceContextId`; client `RunQueryFilter.ResourceContextIdIsNull` twin for the A2 parity test; `EligibleConnectionDto`/`ConnectionUsedInEntry` fields (Tasks 8–9).
- `Apperture.Management.Ui.Client/Services/ResourceContextViewState.cs` — `KnownContexts`, `EnsureContextsKnownAsync`, `DescribeContext` (Task 2).
- `Apperture.Management.Ui.Client/Layout/MainLayout.razor` hosts `ResourceContextViewNotices`; `Apperture.Management.Ui.Shared/ResourceContextViewResolver.cs` gets doc-comment copy updates only (Task 2).

**Modified files (UI pages):**
- `Pages/Integrations/IntegrationsList.razor` — URL-authoritative load, Context column, scoped empty state (Task 2).
- `Pages/Connections/ConnectionsPage.razor` — URL-authoritative load, Shared-available notice (Task 3).
- `Pages/Schedules/SchedulesList.razor` — URL-authoritative load (Task 4).
- `Pages/Runs/RunsList.razor` and `Apperture.Management.Ui.Shared/Epic33UiDisplayHelpers.cs:148-170` — URL-authoritative context, `BuildRunsListUrl` context, Context column, pre-history note (Task 5).
- `Pages/Integrations/IntegrationDetail.razor`, `IntegrationSettings.razor`, `IntegrationConnections.razor`, `IntegrationSchedules.razor`, `IntegrationRuns.razor`, `IntegrationNotifications.razor`, `IntegrationScheduleDiagnostics.razor`, `Pages/Connections/ConnectionDetail.razor`, `Pages/Schedules/ScheduleDetail.razor`, `Pages/Runs/RunDetail.razor` — ten pages get the chip and mismatch notice (Task 7).
- `Pages/Integrations/IntegrationConnections.razor`, `Pages/Connections/ConnectionDetail.razor` — binding-picker and "Used by" Context labels (Task 8).
- `Pages/Config/Inventory.razor` — Context filter and column, invalid-filter reset (Task 9).

**Modified test files:**
- `tests/Apperture.Management.Ui.Client.Tests/ResourceContextViewStateTests.cs` — display-path cases and URL rewrite on `/integrations`, `/connections`, `/schedules` (Task 2).
- `tests/Apperture.UnitTests/ControlPlane/Epic33CrossNavigationTests.cs` — `context` round-trip (Task 5).

**Interfaces produced for later plans:**
- `ResourceContextViewState.KnownContexts`, `EnsureContextsKnownAsync`, `DescribeContext` — Plan 5's Audit Context filter uses these for options and names instead of adding its own list or lookup.
- `AuditFilterCriteria.ResourceContextId` threaded to `AuditQuery.FilterByResourceContextId` — Plan 5 Task 5 consumes it and adds only the controller parameter, client parameter and UI.
- `<OwnershipChip ResourceContextId="..." ShowNotRecorded="..." />` and `<MismatchNotice OwnerResourceContextId="..." ResourceKindLabel="..." Historical="..." ExtraCopy="..." />` — the idiom any future detail page follows.
- `ScopedListContextFilter` — the loading rule any future scoped list follows.

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

## Task 2: Context display path, scoped-list filter helper, and `/integrations` (§4.2, §4.6, §5, R6)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Services/ResourceContextViewState.cs`
- Create: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Molecules/ResourceContextViewNotices.razor`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Layout/MainLayout.razor` (hosts the notices)
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ResourceContextViewResolver.cs` (doc-comment copy only)
- Create: `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ScopedListContextFilter.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationsList.razor:1, 378-482`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs:643-669` (`BuildIntegrationFleetPath`)
- Test: `tests/Apperture.Management.Ui.Client.Tests/ResourceContextViewStateTests.cs`
- Test: `tests/Apperture.Management.Ui.Shared.Tests/ScopedListContextFilterTests.cs`

**Interfaces:**
- Consumes: `ResourceContextViewState` (Plan 3), `ResourceContextViewScope.Parse` (Plan 1 — returns `null` for missing/blank input, `All` for `all`, and otherwise a specific scope without checking that the id exists), `IntegrationFleetQueryParams.Context` (Plan 1).
- Produces: `ResourceContextViewState.KnownContexts`, `EnsureContextsKnownAsync`, `DescribeContext`, `UnknownContextLabel`; `ScopedListContextFilter.Resolve`; the page-loading pattern Tasks 3–5 copy.

- [ ] **Step 1: Add the shared context display path to `ResourceContextViewState`**

Add to `ResourceContextViewState.cs`:
```csharp
public const string UnknownContextLabel = "Unknown context";

private readonly HashSet<string> _refreshAttemptedFor = new(StringComparer.Ordinal);

/// <summary>Every known context: Shared plus active and archived Applications.</summary>
public IReadOnlyList<ResourceContextDto> KnownContexts => _knownContexts;

/// <summary>
/// Refreshes the active+archived context list once if any id is missing from it, then raises
/// <see cref="Changed"/>. An id that stays missing is not refreshed again for the life of the tab.
/// Does not re-resolve the view or navigate.
/// </summary>
public async ValueTask EnsureContextsKnownAsync(IEnumerable<string?> ids)
{
    var missing = ids
        .Where(id => !string.IsNullOrWhiteSpace(id) && FindContext(id!) is null && !_refreshAttemptedFor.Contains(id!))
        .Select(id => id!)
        .Distinct(StringComparer.Ordinal)
        .ToList();
    if (missing.Count == 0)
        return;

    foreach (var id in missing)
        _refreshAttemptedFor.Add(id);

    await RefreshKnownContextsOnlyAsync();
    Changed?.Invoke();
}

/// <summary>Operator-facing name for a context id: its name, suffixed " (archived)" when archived, or <see cref="UnknownContextLabel"/>.</summary>
public string DescribeContext(string resourceContextId) =>
    FindContext(resourceContextId) is { } context
        ? string.Equals(context.State, ResourceContextStates.Archived, StringComparison.Ordinal)
            ? $"{context.Name} (archived)"
            : context.Name
        : UnknownContextLabel;
```
In `RefreshContextsAsync` (called after Application management changes), clear `_refreshAttemptedFor` before re-fetching, so a context created later in the tab can still be resolved. Leave `ViewSelector.CurrentLabel()` as it is.

Add tests to `ResourceContextViewStateTests.cs`, following that file's `TestNavigationManager`/`FakeJsRuntime`/Moq pattern:
```csharp
[Fact]
public async Task EnsureContextsKnownAsync_MissingId_RefreshesOnce_ThenDescribesArchivedName()
{
    var client = new Mock<IManagementClient>();
    client.SetupSequence(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
        .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[])
        .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[Application("app-a", "App A", archived: true)]);
    var nav = new TestNavigationManager("https://localhost/", "https://localhost/config/inventory");
    var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));
    await state.InitializeAsync();

    await state.EnsureContextsKnownAsync(["app-a"]);

    state.DescribeContext("app-a").Should().Be("App A (archived)");
    client.Verify(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()), Times.Exactly(2));
}

[Fact]
public async Task EnsureContextsKnownAsync_StillMissing_DescribesUnknownContext_AndDoesNotRefreshAgain()
{
    var client = new Mock<IManagementClient>();
    client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
        .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[]);
    var nav = new TestNavigationManager("https://localhost/", "https://localhost/config/inventory");
    var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));
    await state.InitializeAsync();

    await state.EnsureContextsKnownAsync(["gone"]);
    await state.EnsureContextsKnownAsync(["gone"]);

    state.DescribeContext("gone").Should().Be(ResourceContextViewState.UnknownContextLabel);
    client.Verify(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()), Times.Exactly(2)); // InitializeAsync + one refresh
}
```

- [ ] **Step 2: Add `ScopedListContextFilter`**

Create `ScopedListContextFilter.cs` in `Apperture.Management.Ui.Shared`:
```csharp
namespace Apperture.Management.Ui.Shared;

public enum ScopedListContextState
{
    /// <summary>No usable ?context= yet; ResourceContextViewState will rewrite the URL. Send nothing.</summary>
    AwaitingContext,
    Ready,
    /// <summary>?context= names an id that is not in the known context list. Send nothing.</summary>
    UnknownContext
}

public sealed record ScopedListContextResolution(ScopedListContextState State, string? ApiFilter, string? QueryValue)
{
    public bool IsAll => State == ScopedListContextState.Ready && ApiFilter is null;
}

public static class ScopedListContextFilter
{
    public static ScopedListContextResolution Resolve(string? queryContext, Func<string, bool> isKnownContext)
    {
        var scope = ResourceContextViewScope.Parse(queryContext);
        if (scope is null)
            return new(ScopedListContextState.AwaitingContext, null, null);
        if (scope.IsAll)
            return new(ScopedListContextState.Ready, null, scope.ToQueryValue());

        var id = scope.ToApiFilter()!;
        return isKnownContext(id)
            ? new(ScopedListContextState.Ready, id, scope.ToQueryValue())
            : new(ScopedListContextState.UnknownContext, null, scope.ToQueryValue());
    }
}
```
Test it in `ScopedListContextFilterTests.cs`, one case each: missing → `AwaitingContext`; whitespace → `AwaitingContext`; `all` → `Ready` with `ApiFilter == null`; known id → `Ready` with that id; unknown id → `UnknownContext` with `ApiFilter == null`.

- [ ] **Step 3: Load `/integrations` from the URL**

Tasks 3–5 copy this pattern. In `IntegrationsList.razor`, add `@inject ResourceContextViewState ViewState` and:
```csharp
[SupplyParameterFromQuery(Name = "context")]
public string? QueryContext { get; set; }

private ScopedListContextResolution _context = new(ScopedListContextState.AwaitingContext, null, null);
private string? _loadedContextValue;

protected override async Task OnParametersSetAsync()
{
    _context = ScopedListContextFilter.Resolve(QueryContext, id => ViewState.FindContext(id) is not null);
    if (_context.State == ScopedListContextState.UnknownContext)
    {
        await ViewState.EnsureContextsKnownAsync([_context.QueryValue]);
        _context = ScopedListContextFilter.Resolve(QueryContext, id => ViewState.FindContext(id) is not null);
    }

    if (_context.State != ScopedListContextState.Ready || _context.QueryValue == _loadedContextValue)
        return;

    _loadedContextValue = _context.QueryValue;
    await LoadAsync();
}
```
Remove the initial `LoadAsync()` call from `OnInitializedAsync` (line ~391); keep the rest of initialization. In `LoadAsync()` (lines 394-420), pass `Context = _context.ApiFilter` into `IntegrationFleetQueryParams`. The page's existing filter-change reloads (lines ~453, 468, 482) keep calling `LoadAsync()` and therefore keep the current context. Do not subscribe to `ViewState.Changed` to reload data.

Rendering: while `AwaitingContext`, show the page's existing loading state. For `UnknownContext`, render no list and leave the message to the view notice host (Step 3b); never show the loading state indefinitely.

- [ ] **Step 3b: Display Plan 3's view notices with the updated copy**

Plan 3's `ResourceContextViewResolver` produces `ArchivedRemembered`, `ArchivedExplicit` and `UnknownId` notices, but no UI consumer was found in `src/Apperture.ControlPlane` — only doc comments mention the copy. Confirm that, then add `Molecules/ResourceContextViewNotices.razor`, rendered once in `MainLayout` above the page body, subscribed to `ViewState.Changed`, and shown only on `Scoped` routes (`ResourceContextViewRoutes`):
- `ArchivedRemembered` → a one-time info toast, taken via `ViewState.ConsumeNotice()`: "{name} is archived. Showing all."
- `ArchivedExplicit` → a page banner while the current scope is still that context: "{name} is archived." with **View all** (`?context=all`).
- `UnknownId` → a page message while the URL still names that id: "That context doesn't exist." with **View all**.

Names come from `ViewState.DescribeContext`. Update the two doc comments in `ResourceContextViewResolver.cs` (lines ~28 and ~40) to the new wording.

- [ ] **Step 4: Wire `BuildIntegrationFleetPath` to include `contextId`**

In `ManagementClient.cs`, inside `BuildIntegrationFleetPath`, following the existing idiom:
```csharp
if (!string.IsNullOrWhiteSpace(queryParams.Context))
    parts.Add($"contextId={Uri.EscapeDataString(queryParams.Context)}");
```

- [ ] **Step 5: Add the Context column under All**

Render the column only when `ViewState.SelectorVisible && _context.IsAll` (design gate §3.3: with no active Application the page is unchanged). Header: **Context**. At the end of a successful `LoadAsync`, call `await ViewState.EnsureContextsKnownAsync(rows.Select(r => r.ResourceContextId))`. Cell: `ViewState.DescribeContext(row.ResourceContextId)`, or "—" if a row has no id.

- [ ] **Step 6: Add the scoped empty state (R6)**

When `_context.State == Ready`, `!_context.IsAll` and the result is empty, render "No integrations in {ViewState.DescribeContext(_context.ApiFilter!)}." with **View all** (navigate to `?context=all`) and — only when that context is active, not archived — **Create integration in {name}** (`/integrations/new?context={id}`). Never show the unscoped "No integrations are configured yet" in this case.

- [ ] **Step 7: Add URL rewrite coverage for the three View-driven list routes**

Add to `ResourceContextViewStateTests.cs`:
```csharp
[Theory]
[InlineData("/integrations")]
[InlineData("/connections")]
[InlineData("/schedules")]
public async Task SetExplicitAsync_OnScopedListPage_RewritesContextInTheUrl(string path)
{
    var client = new Mock<IManagementClient>();
    client.Setup(c => c.GetResourceContextsAsync(true, It.IsAny<CancellationToken>()))
        .ReturnsAsync((IReadOnlyList<ResourceContextDto>)[Application("app-a", "App A")]);
    var nav = new TestNavigationManager("https://localhost/", $"https://localhost{path}?context=all");
    var state = new ResourceContextViewState(new FakeJsRuntime(), nav, client.Object, AnonymousSession(client));
    await state.InitializeAsync();
    nav.Navigations.Clear();

    await state.SetExplicitAsync(ResourceContextViewScope.Of("app-a"));

    nav.Uri.Should().Be($"https://localhost{path}?context=app-a");
    nav.Navigations.Should().ContainSingle(n => n.Replace == false);
}
```
With Step 2's tests, this covers what can be automated here: a View change rewrites `?context=`, and a `?context=` value maps to exactly one request filter. That each page actually reloads on the parameter change is checked in the closing manual smoke (there is no bUnit).

- [ ] **Step 8: Verify build and tests**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Run: `dotnet test tests/Apperture.Management.Ui.Client.Tests/Apperture.Management.Ui.Client.Tests.csproj --filter "FullyQualifiedName~ResourceContextViewStateTests"`
Run: `dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj --filter "FullyQualifiedName~ScopedListContextFilterTests"`
Expected: SUCCEEDS / PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Services/ResourceContextViewState.cs src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Molecules/ResourceContextViewNotices.razor src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Layout/MainLayout.razor src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ResourceContextViewResolver.cs src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ScopedListContextFilter.cs src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationsList.razor src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs tests/Apperture.Management.Ui.Client.Tests/ResourceContextViewStateTests.cs tests/Apperture.Management.Ui.Shared.Tests/ScopedListContextFilterTests.cs
git commit -m "CTX-3A: add the context display path and URL-authoritative /integrations loading (§4.2, §4.6, §5, R6)"
```

---

## Task 3: `ConnectionsPage.razor` — URL-authoritative loading, Shared-available notice (§5.2)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Connections/ConnectionsPage.razor:1, 144-203`

**Interfaces:**
- Consumes: `ScopedListContextFilter` and the `ResourceContextViewState` display path (Task 2), `IManagementClient.GetConnectionsAsync(contextId, ct)` (Plan 1), `IManagementClient.GetResourceContextSummaryAsync` (Plan 3).

- [ ] **Step 1: Load from the URL**

Apply Task 2 Step 3's pattern verbatim: `QueryContext`, `_context`, `_loadedContextValue`, and the same `OnParametersSetAsync`. Remove the initial `LoadAsync()` from `OnInitializedAsync` (line ~183). In `LoadAsync` (line ~203), call `Client.GetConnectionsAsync(_context.ApiFilter)`. `ConnectionsPage` switches tabs with `SetTab`, which navigates — confirm the URL it builds keeps `context`; if it rebuilds the query from scratch, add `context` so a tab change does not drop the view.

- [ ] **Step 2: Add the Shared-available notice under a specific Application**

```razor
@if (_context.State == ScopedListContextState.Ready && !_context.IsAll
     && !ResourceContextIds.IsShared(_context.ApiFilter) && _sharedConnectionCount > 0)
{
    <PageMessage Kind="PageMessageKind.Info">
        Integrations in @ViewState.DescribeContext(_context.ApiFilter!) can also use
        <a href="/connections?context=shared" class="ui-link">@_sharedConnectionCount Shared connections</a>.
    </PageMessage>
}
```
Source `_sharedConnectionCount` from the ownership summary for Shared without a new HTTP call per page load (design gate §5.2: "Count N from the already-cached summary endpoint"). If `ResourceContextViewState` does not cache summaries yet, add that cache there in this task rather than a second cache in the page.

- [ ] **Step 3: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS. (URL rewrite on `/connections` is covered by Task 2 Step 7.)

- [ ] **Step 4: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Connections/ConnectionsPage.razor
git commit -m "CTX-3A: load /connections from ?context= with ownership-only scoping and Shared-available notice (§5.2, R3)"
```
If Step 2 added a summary cache, include `ResourceContextViewState.cs` in the commit.

---

## Task 4: `SchedulesList.razor` — URL-authoritative loading (§5 surface matrix)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/SchedulesList.razor:1, 229-244`

**Interfaces:**
- Consumes: `ScopedListContextFilter` (Task 2), `IManagementClient.GetSchedulesAsync(contextId, ct)` (Plan 1).

- [ ] **Step 1: Load from the URL**

Apply Task 2 Step 3's pattern verbatim. Remove the initial `LoadAsync()` from `OnInitializedAsync` (line ~233). In `LoadAsync` (line ~244), pass `_context.ApiFilter` to `GetSchedulesAsync`.

- [ ] **Step 2: Note on attached vs. legacy standalone schedules**

No special-casing: `ScheduleDto.ResourceContextId` mirrors the owning Integration for attached schedules (CTX-1 invariant) and carries the schedule's own value for legacy standalone rows, so the single `contextId` filter is correct for both. Do not add a "legacy only" filter path.

- [ ] **Step 3: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS. (URL rewrite on `/schedules` is covered by Task 2 Step 7.)

- [ ] **Step 4: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/SchedulesList.razor
git commit -m "CTX-3A: load /schedules from ?context= (§5)"
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

- [ ] **Step 5: Wire `RunsList.razor` to the URL-authoritative context**

Add `[SupplyParameterFromQuery(Name = "context")] public string? QueryContext { get; set; }` alongside the existing query parameters (lines 258-271). At the start of `OnParametersSetAsync` (line ~316), resolve `_context` with `ScopedListContextFilter` and do the one-refresh retry, exactly as in Task 2 Step 3. If `_context.State != Ready`, return without loading. Add `_context.QueryValue` to `BuildLoadKey` (lines 325-326) so a context change reloads through the page's existing load-key check. In `LoadAsync()` (lines 341-377), pass `ResourceContextId: _context.ApiFilter` into `RunQueryFilter` — `all` has already become `null`, so the literal `"all"` is never sent.

In `OnStatusFacetChanged` (line ~333), pass `context: QueryContext` to `BuildRunsListUrl` so the status facet keeps the view (design gate §5.3).

- [ ] **Step 6: Add the Context column under All and the pre-history note**

Render the column only when `ViewState.SelectorVisible && _context.IsAll`. Header: **Context**. After loading, call `await ViewState.EnsureContextsKnownAsync(runs.Select(r => r.ResourceContextId))`. Cell: `ViewState.DescribeContext(id)` for a recorded snapshot, "Not recorded" for a null one. This is the historical snapshot, never a current-Integration lookup.

Under a specific context, add the muted note, worded exactly as in design gate §5.1 so it matches Control Center (Plan 6): *"Some older runs don't have a recorded context. Runs without a recorded context appear only under All."*

- [ ] **Step 7: Run tests**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~RunHistoricalSnapshotFilteringTests|FullyQualifiedName~Epic33CrossNavigationTests"`
Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: PASS / SUCCEEDS.

- [ ] **Step 8: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/Epic33UiDisplayHelpers.cs src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunsList.razor tests/Apperture.UnitTests/ResourceContexts/RunHistoricalSnapshotFilteringTests.cs tests/Apperture.UnitTests/ControlPlane/Epic33CrossNavigationTests.cs
git commit -m "CTX-3A: scope /runs by historical snapshot from ?context=, preserve context through status facets (§5.3, R11)"
```

---

## Task 6: `OwnershipChip` atom (§4.6)

**Files:**
- Create: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Atoms/OwnershipChip.razor`

**Interfaces:**
- Consumes: `ResourceContextViewState` display path (Task 2).
- Produces: `<OwnershipChip ResourceContextId="@id" ShowNotRecorded="false" />`. The chip resolves the name itself; callers never pass a name.

- [ ] **Step 1: Build the component**

```razor
@implements IDisposable
@using Apperture.Management.Ui.Client.Services
@inject ResourceContextViewState ViewState

@if (_ready && ViewState.SelectorVisible)
{
    if (string.IsNullOrWhiteSpace(ResourceContextId))
    {
        if (ShowNotRecorded)
        {
            <span class="ui-ownership-chip ui-ownership-chip--muted">Context not recorded</span>
        }
    }
    else if (ViewState.FindContext(ResourceContextId) is null)
    {
        <span class="ui-ownership-chip ui-ownership-chip--muted">@ResourceContextViewState.UnknownContextLabel</span>
    }
    else
    {
        <a href="@($"/config/applications/{Uri.EscapeDataString(ResourceContextId)}")" class="ui-ownership-chip" title="Owning context">
            Context: @ViewState.DescribeContext(ResourceContextId)
        </a>
    }
}

@code {
    [Parameter] public string? ResourceContextId { get; set; }

    /// <summary>Run detail only: render "Context not recorded" for a null historical snapshot.</summary>
    [Parameter] public bool ShowNotRecorded { get; set; }

    private bool _ready;

    protected override void OnInitialized() => ViewState.Changed += OnViewStateChanged;

    protected override async Task OnParametersSetAsync()
    {
        await ViewState.EnsureContextsKnownAsync([ResourceContextId]);
        _ready = true;
    }

    private void OnViewStateChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => ViewState.Changed -= OnViewStateChanged;
}
```
`_ready` avoids a brief "Unknown context" while a missing id is being refreshed. Confirm `/config/applications/shared` opens Plan 3's read-only Shared detail page; if Shared uses a different route, link there for Shared. Style `.ui-ownership-chip` after `StatusPill`'s class-mapping approach, checked against `management-ui-design-system.md` before finalizing classes.

- [ ] **Step 2: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 3: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Atoms/OwnershipChip.razor
git commit -m "CTX-3A: add OwnershipChip atom with archived-safe context names (§4.6)"
```

---

## Task 7: Context chip + mismatch notice on ten detail/hub pages (§4.6)

**Files:**
- Create: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Molecules/MismatchNotice.razor`
- Modify: `Pages/Integrations/IntegrationDetail.razor:17-37`, `IntegrationSettings.razor`, `IntegrationConnections.razor`, `IntegrationSchedules.razor`, `IntegrationRuns.razor`, `IntegrationNotifications.razor` — the six hub sections. None uses `PageHeader`'s `ChildContent` today, so each gets that slot.
- Modify: `Pages/Integrations/IntegrationScheduleDiagnostics.razor` (route `/integrations/{IntegrationId}/schedule-diagnostics`) — a deep link, not a hub tab ("Diagnostics is a deep link, not a hub section"). It already uses `ChildContent` for the raw Integration id; append to it.
- Modify: `Pages/Connections/ConnectionDetail.razor:15-20` — already uses `ChildContent`; append.
- Modify: `Pages/Schedules/ScheduleDetail.razor:13-16` — add a `ChildContent` block.
- Modify: `Pages/Runs/RunDetail.razor:14-18` — new ownership display.

That is ten consuming pages: six hub sections, schedule-diagnostics, and Connection, Schedule and Run detail.

**Interfaces:**
- Consumes: `OwnershipChip` (Task 6), `PageMessage` (existing), `ResourceContextViewState` display path (Task 2).
- Produces: `<MismatchNotice OwnerResourceContextId="@id" ResourceKindLabel="integration" Historical="false" ExtraCopy="@null" />`.

- [ ] **Step 1: Build `MismatchNotice`**

```razor
@implements IDisposable
@using Apperture.Management.Ui.Client.Services
@using Apperture.Management.Ui.Shared
@inject ResourceContextViewState ViewState

@if (ShowMismatch)
{
    <PageMessage Kind="PageMessageKind.Info">
        @OwnerSentence Your view is @ViewState.DescribeContext(ViewState.Current.ResourceContextId!).
        @ExtraCopy
        @if (OwnerKnown)
        {
            <button type="button" class="ui-link" @onclick="SwitchAsync">Switch view to @ViewState.DescribeContext(OwnerResourceContextId!)</button>
        }
    </PageMessage>
}

@code {
    [Parameter] public string? OwnerResourceContextId { get; set; }
    [Parameter] public string ResourceKindLabel { get; set; } = "resource";

    /// <summary>Run detail: phrase the owner as where the run happened (historical snapshot).</summary>
    [Parameter] public bool Historical { get; set; }

    [Parameter] public string? ExtraCopy { get; set; }

    private bool _ready;

    private bool ShowMismatch =>
        _ready
        && ViewState.SelectorVisible
        && !ViewState.Current.IsAll
        && !string.IsNullOrWhiteSpace(OwnerResourceContextId)
        && !string.Equals(ViewState.Current.ResourceContextId, OwnerResourceContextId, StringComparison.Ordinal);

    private bool OwnerKnown => ViewState.FindContext(OwnerResourceContextId!) is not null;

    private string OwnerSentence
    {
        get
        {
            var owner = OwnerKnown ? ViewState.DescribeContext(OwnerResourceContextId!) : "an unknown context";
            return Historical ? $"This {ResourceKindLabel} happened in {owner}." : $"This {ResourceKindLabel} belongs to {owner}.";
        }
    }

    protected override void OnInitialized() => ViewState.Changed += OnViewStateChanged;

    protected override async Task OnParametersSetAsync()
    {
        await ViewState.EnsureContextsKnownAsync([OwnerResourceContextId, ViewState.Current.ResourceContextId]);
        _ready = true;
    }

    private async Task SwitchAsync() =>
        await ViewState.SetExplicitAsync(ResourceContextViewScope.Of(OwnerResourceContextId!));

    private void OnViewStateChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => ViewState.Changed -= OnViewStateChanged;
}
```
The owner and current-view names both go through `DescribeContext`. An owner still unknown after one refresh gets the informational sentence and no Switch action, because switching to an unknown id would only land on a not-found state.

- [ ] **Step 2: Add the chip + notice to the six Integration hub sections**

On each of the six pages, inside `PageHeader`'s `ChildContent`: `<OwnershipChip ResourceContextId="@_integration?.ResourceContextId" />`. Directly below the header: `<MismatchNotice OwnerResourceContextId="@_integration?.ResourceContextId" ResourceKindLabel="integration" />`. Confirm each page's loaded `IntegrationDto` carries `ResourceContextId` (Plan 1 added it to the client DTO); no extra fetch.

- [ ] **Step 2b: Add the chip + notice to Integration schedule-diagnostics**

Same markup as Step 2, appended to the existing `ChildContent` block and placed below the header, sourced from this page's own loaded Integration data. Confirm what this page loads; do not assume it shares state with the hub sections.

- [ ] **Step 3: Add the chip + notice to Connection detail**

Append `<OwnershipChip ResourceContextId="@_connection?.ResourceContextId" />` to the existing `ChildContent` (use the page's actual loaded-connection field). Add `MismatchNotice` with `ResourceKindLabel="connection"`. When the Connection is Shared and the view is an Application, pass the design gate's extra line: `ExtraCopy="@(ResourceContextIds.IsShared(_connection?.ResourceContextId) && !ViewState.Current.IsAll ? $"Integrations in {ViewState.DescribeContext(ViewState.Current.ResourceContextId!)} can use it." : null)"`.

- [ ] **Step 4: Add the chip + notice to Schedule detail**

Add a `ChildContent` block with the chip, and `MismatchNotice` with `ResourceKindLabel="schedule"`.

- [ ] **Step 5: Add the chip + notice to Run detail from the historical snapshot**

`<OwnershipChip ResourceContextId="@_run?.ResourceContextId" ShowNotRecorded="true" />` and `<MismatchNotice OwnerResourceContextId="@_run?.ResourceContextId" ResourceKindLabel="run" Historical="true" />`, both from `RunDetailDto.ResourceContextId` — never a current-Integration lookup. A null snapshot renders "Context not recorded" and, by construction, no mismatch notice (design gate §4.6).

- [ ] **Step 6: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 7: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Molecules/MismatchNotice.razor src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/*.razor src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Connections/ConnectionDetail.razor src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/ScheduleDetail.razor src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunDetail.razor
git commit -m "CTX-3A: add Context chip and mismatch notice to all ten detail/hub pages, including schedule-diagnostics (§4.6)"
```

---

## Task 8: Binding-picker and "Used by" Context labels — A8/A9

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationBindingsController.cs:82-108` (`GetEligibleConnections`)
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ConnectionsController.cs` (`ToDtoAsync`, `ConnectionUsedInEntry`)
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs:1156, 1162-1167` (`EligibleConnectionDto`, `ConnectionUsedInEntry`)
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationConnections.razor` (binding picker)
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Connections/ConnectionDetail.razor` ("Used by" rows)

**Interfaces:**
- Consumes: `ResourceContextViewState` display path (Task 2).
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
Fill in the arrange steps from the existing test harness for `IntegrationBindingsController`/`ConnectionsController` (locate and follow their setup helpers).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~EligibleConnections|FullyQualifiedName~ConnectionUsedInEntry"`
Expected: FAIL.

- [ ] **Step 3: Add `ResourceContextId` to the eligible-connections response**

`GetEligibleConnections` currently returns an anonymous type (`new { Id = c.Id.Value, c.Name, ConnectorDefinitionId = c.ConnectorDefinitionId.Value }`). Replace it with a named record:
```csharp
public sealed record EligibleConnectionResponseRow(string Id, string Name, string ConnectorDefinitionId, string ResourceContextId);
// ...
return Ok(connections.Select(c => new EligibleConnectionResponseRow(c.Id.Value, c.Name, c.ConnectorDefinitionId.Value, c.ResourceContextId.Value)).ToList());
```

- [ ] **Step 4: Add `ResourceContextId` to the client `EligibleConnectionDto`**

In `IManagementClient.cs:1156`:
```csharp
public sealed record EligibleConnectionDto(string Id, string Name, string ConnectorDefinitionId, string? ResourceContextId = null);
```

- [ ] **Step 5: Add `IntegrationResourceContextId` to `ConnectionUsedInEntry` (both sides)**

Server (`ConnectionsController.cs`, lines ~206-216 and ~246-251): the `integration` is already loaded in the loop — add `integration?.ResourceContextId.Value` as a trailing constructor argument. Client (`IManagementClient.cs:1162-1167`): the matching trailing optional parameter.

- [ ] **Step 6: Render Context labels in the binding picker and "Used by" rows**

Only when `ViewState.SelectorVisible` (design gate §3.3). After loading, call `EnsureContextsKnownAsync` with the rows' ids. In `IntegrationConnections.razor`'s binding-picker options, suffix each label with `({ViewState.DescribeContext(option.ResourceContextId)})` — "(Shared)" for Shared, the Application's name otherwise. In `ConnectionDetail.razor`'s "Used by" rows, show the same label for `IntegrationResourceContextId`. A null id gets no suffix.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~EligibleConnections|FullyQualifiedName~ConnectionUsedInEntry"`
Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: PASS / SUCCEEDS.

- [ ] **Step 8: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationBindingsController.cs src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ConnectionsController.cs src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationConnections.razor src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Connections/ConnectionDetail.razor
git commit -m "CTX-3A: label binding picker and 'Used by' rows with their Context (A8, A9)"
```

---

## Task 9: Config Inventory — Context filter and column (§5.5, A12, A13, A14)

**Files:**
- Create: `src/Apperture.Core/Apperture.Core.Application/Services/AuditSystemActionPrefixes.cs`
- Modify: `src/Apperture.Core/Apperture.Core.Application/Services/AuditTimelineModels.cs:6-15, 36-45` (`AuditTimelineEntry.ResourceContextId`; `AuditFilterCriteria.ResourceContextId` and `ContextClassification`)
- Modify: `src/Apperture.Core/Apperture.Core.Application/Services/AuditQuery.cs:10-22` (`FilterByContextClassification`)
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/Audit/AuditTimelineQueryService.cs:18-39, 49-66`
- Modify: `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlAuditService.cs:104-122` (`ApplyFilters`)
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/AuditController.cs` (entry→DTO mapping only; the `resourceContextId` query parameter belongs to Plan 5)
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IRunQueryService.cs:58-68` and `RunQueryService.cs:70-71` (`RunQueryFilter.ResourceContextIdIsNull`)
- Modify: `src/Apperture.Core/Apperture.Core.Application/Notifications/INotificationDefinitionStore.cs:5-11` (`NotificationDefinitionListQuery.OwnerResourceContextId`, `SystemScopedOnly`; `GetIntegrationOwnerContextIdsAsync`), `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlNotificationDefinitionStore.cs:13-48`, and `INotificationDefinitionService` plus its implementation (pass-through)
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IInventoryQueryService.cs:43-60`, `InventoryQueryService.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/InventoryController.cs:21-50`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/InventoryDtos.cs`, `ManagementClient.cs:439-463`, `IManagementClient.cs` (`GetInventoryAsync` `context` parameter, `AuditTimelineEntryDto.ResourceContextId`, client `RunQueryFilter.ResourceContextIdIsNull`)
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Inventory.razor`
- Test: `tests/Apperture.UnitTests/ResourceContexts/InventoryContextClassificationTests.cs`

**Interfaces:**
- Consumes: `AuditRecord.ResourceContextId` and `AuditQuery.FilterByResourceContextId` (CTX-2, already applied in `SqlAuditService`), `RunQueryFilter.ResourceContextId` (already applied in `RunQueryService`), the filtered `ListIntegrationsAsync`/`ListConnectionsAsync`/`ListDefinitionsAsync` overloads, the `IResourceContextService.GetAsync` lookup `IntegrationsController.ResolveContextFilterAsync` already uses, and the `ResourceContextViewState` display path (Task 2).
- Produces: `GET api/inventory?context=` (400 on invalid input), `InventoryItemDto.ResourceContextId`/`ResourceContextClassification`, `AuditFilterCriteria.ResourceContextId` (consumed by Plan 5 Task 5), `AuditSystemActionPrefixes`.

**Semantics (design gate §5.5 is authoritative):**

| Filter token | Resources mode | Activity mode |
|---|---|---|
| missing / `all` | everything | everything |
| `system` | Modules; Platform/Module-scoped NotificationDefinitions | audit events with no context and a System prefix |
| `shared` or a real id | owned Integrations/Connections/Schedules; integration-scoped NotificationDefinitions whose Integration is in that context | Runs and audit events recorded in that context |
| `notRecorded` | **400** | Runs with no context; audit events with no context and a non-System prefix |
| anything else | **400** | **400** |


Integration-scoped NotificationDefinitions whose Integration no longer exists (Integration hard delete leaves them behind today) classify `unknown` and show "Unknown context". They appear only under `all`; no filter value matches them, and CTX-3A adds no Unknown filter option.

Runs are never System. Audit events classify id-first: a non-null `ResourceContextId` gives Shared or Application regardless of the action (this covers `resource_context.*`); only a null value is tested against `AuditSystemActionPrefixes`. `AuditCategoryResolver` is not used for classification — its `other` category mixes a context-eligible family (`integration.settings.*`) with System-shaped families it does not recognize (`platform.settings.*`, `emergency.*`, `entra.access_request.*`).

**Truncation.** Inventory fetches Modules, Integrations, Connections and Schedules in full. Runs (100 newest), audit events (100 newest within the existing 24-hour window) and NotificationDefinitions (first 100 by display name) are capped in their own source query. Type, Status and search are then applied in memory before sorting and the page `Take`. For every non-`all` Context value, each capped source receives the Context predicate inside its query, so the cap applies only to rows that already match. Uncapped sources use the filtered overload where one exists, or are skipped when they can never match. The in-memory `Matches` check stays as a cross-check. The existing in-memory Type/Status behavior is unchanged by this task.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Resources_ClassifyByCurrentOwnership_NeverNotRecorded()
{
    var harness = await InventoryTestHarness.CreateAsync();
    var app = await harness.CreateApplicationContextAsync("A");
    await harness.CreateIntegrationAsync(app.Id, "Integration A");
    await harness.CreateConnectionAsync(ResourceContextIds.Shared, "Shared connection");
    await harness.CreateModuleAsync();

    var result = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Resources));

    result.Items.Single(i => i.Name == "Integration A").ResourceContextClassification.Should().Be("application");
    result.Items.Single(i => i.Name == "Shared connection").ResourceContextClassification.Should().Be("shared");
    result.Items.Single(i => i.Type == InventoryTypeTokens.Module).ResourceContextClassification.Should().Be("system");
    result.Items.Should().NotContain(i => i.ResourceContextClassification == "notRecorded");
}

[Fact]
public async Task NotificationDefinition_IntegrationScoped_DerivesFromItsIntegration_EvenWithoutIntegrationViewCapability()
{
    var harness = await InventoryTestHarness.CreateAsync(canViewIntegrations: false);
    var app = await harness.CreateApplicationContextAsync("A");
    var integration = await harness.CreateIntegrationAsync(app.Id, "Integration A");
    await harness.CreateNotificationDefinitionAsync("Integration def", NotificationDefinitionScopes.Integration, integrationId: integration.Id);
    await harness.CreateNotificationDefinitionAsync("Platform def", NotificationDefinitionScopes.Platform);
    await harness.CreateNotificationDefinitionAsync("Module def", NotificationDefinitionScopes.Module, moduleId: "Sample.HelloWorld");

    var result = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Resources));

    result.Items.Single(i => i.Name == "Integration def").ResourceContextId.Should().Be(app.Id.Value);
    result.Items.Single(i => i.Name == "Platform def").ResourceContextClassification.Should().Be("system");
    result.Items.Single(i => i.Name == "Module def").ResourceContextClassification.Should().Be("system");
}

[Fact]
public async Task Audit_NullContext_ClassifiesByActionPrefix_NotByCategory()
{
    var harness = await InventoryTestHarness.CreateAsync();
    await harness.SeedAuditRecordAsync("platform.settings.updated", resourceContextId: null);
    await harness.SeedAuditRecordAsync("emergency.access.designated", resourceContextId: null);
    await harness.SeedAuditRecordAsync("entra.access_request.approved", resourceContextId: null);
    await harness.SeedAuditRecordAsync("auth.login", resourceContextId: null);
    await harness.SeedAuditRecordAsync("module.lifecycle.enabled", resourceContextId: null);
    await harness.SeedAuditRecordAsync("recovery.retry.requested", resourceContextId: null);
    await harness.SeedAuditRecordAsync("execution.completed", resourceContextId: null);
    await harness.SeedAuditRecordAsync("integration.settings.updated", resourceContextId: null);

    var result = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Activity));

    Classification(result, "platform.settings.updated").Should().Be("system");
    Classification(result, "emergency.access.designated").Should().Be("system");
    Classification(result, "entra.access_request.approved").Should().Be("system");
    Classification(result, "auth.login").Should().Be("system");
    Classification(result, "module.lifecycle.enabled").Should().Be("system");
    Classification(result, "recovery.retry.requested").Should().Be("notRecorded");
    Classification(result, "execution.completed").Should().Be("notRecorded");
    Classification(result, "integration.settings.updated").Should().Be("notRecorded");
}

[Fact]
public async Task Audit_WithContext_ClassifiesFromIdRegardlessOfAction()
{
    var harness = await InventoryTestHarness.CreateAsync();
    var app = await harness.CreateApplicationContextAsync("A");
    await harness.SeedAuditRecordAsync("resource_context.archived", resourceContextId: app.Id.Value);
    await harness.SeedAuditRecordAsync("integration.settings.updated", resourceContextId: ResourceContextIds.SharedValue);

    var result = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Activity));

    Classification(result, "resource_context.archived").Should().Be("application");
    Classification(result, "integration.settings.updated").Should().Be("shared");
}

[Fact]
public async Task Run_NullContext_IsNotRecorded_AndSystemFilterNeverReturnsRuns()
{
    var harness = await InventoryTestHarness.CreateAsync();
    await harness.SeedOperationRunAsync(resourceContextId: null);

    var all = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Activity));
    var system = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Activity, Context: InventoryContextFilter.System));

    all.Items.Single(i => i.Type == InventoryTypeTokens.Run).ResourceContextClassification.Should().Be("notRecorded");
    system.Items.Should().NotContain(i => i.Type == InventoryTypeTokens.Run);
}

[Fact]
public async Task ContextFilter_ExcludesNonMatchingRows_NoMerging()
{
    var harness = await InventoryTestHarness.CreateAsync();
    var appA = await harness.CreateApplicationContextAsync("A");
    await harness.CreateIntegrationAsync(appA.Id, "In A");
    await harness.CreateConnectionAsync(ResourceContextIds.Shared, "Shared connection");

    var underA = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Resources, Context: InventoryContextFilter.ForContext(appA.Id.Value)));

    underA.Items.Should().ContainSingle(i => i.Name == "In A");
    underA.Items.Should().NotContain(i => i.Name == "Shared connection");
}

[Theory]
[InlineData("context")]
[InlineData("notRecorded")]
[InlineData("system")]
public async Task CappedActivitySources_KeepMatchingRowsDiscoverable_BeyondTheFirst100(string kind)
{
    // Each case puts 150 newer non-matching rows in front of one older matching row in a source capped
    // at 100. Without the Context predicate inside the source query, the matching row would be cut
    // before classification and the result would be falsely empty.
    var harness = await InventoryTestHarness.CreateAsync();
    var target = await harness.CreateApplicationContextAsync("Target");
    var noise = await harness.CreateApplicationContextAsync("Noise");
    var now = harness.Clock.GetUtcNow();
    InventoryContextFilter filter;

    switch (kind)
    {
        case "context":
            await harness.SeedOperationRunAsync(resourceContextId: target.Id.Value, createdAt: now.AddHours(-2), name: "match");
            for (var i = 0; i < 150; i++) await harness.SeedOperationRunAsync(resourceContextId: noise.Id.Value, createdAt: now.AddMinutes(-i));
            filter = InventoryContextFilter.ForContext(target.Id.Value);
            break;
        case "notRecorded":
            await harness.SeedOperationRunAsync(resourceContextId: null, createdAt: now.AddHours(-2), name: "match");
            for (var i = 0; i < 150; i++) await harness.SeedOperationRunAsync(resourceContextId: noise.Id.Value, createdAt: now.AddMinutes(-i));
            filter = InventoryContextFilter.NotRecorded;
            break;
        default:
            await harness.SeedAuditRecordAsync("auth.login", resourceContextId: null, at: now.AddHours(-2), summary: "match");
            for (var i = 0; i < 150; i++) await harness.SeedAuditRecordAsync("execution.completed", resourceContextId: noise.Id.Value, at: now.AddMinutes(-i));
            filter = InventoryContextFilter.System;
            break;
    }

    var result = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Activity, Context: filter, Take: 100));

    result.Items.Should().Contain(i => i.Name.Contains("match"));
}

[Fact]
public async Task NotificationDefinitions_ContextFilter_ReachesBeyondTheFirst100ByName()
{
    var harness = await InventoryTestHarness.CreateAsync();
    var target = await harness.CreateApplicationContextAsync("Target");
    var integration = await harness.CreateIntegrationAsync(target.Id, "Target integration");
    for (var i = 0; i < 150; i++)
        await harness.CreateNotificationDefinitionAsync($"A platform def {i:D3}", NotificationDefinitionScopes.Platform);
    await harness.CreateNotificationDefinitionAsync("Z target def", NotificationDefinitionScopes.Integration, integrationId: integration.Id);

    var result = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Resources, Context: InventoryContextFilter.ForContext(target.Id.Value)));

    result.Items.Should().Contain(i => i.Name == "Z target def");
}

[Fact]
public async Task NotificationDefinition_WhoseIntegrationWasDeleted_IsUnknownContext_OnlyUnderAll()
{
    var harness = await InventoryTestHarness.CreateAsync();
    var app = await harness.CreateApplicationContextAsync("A");
    var integration = await harness.CreateIntegrationAsync(app.Id, "Deleted integration");
    await harness.CreateNotificationDefinitionAsync("Orphan def", NotificationDefinitionScopes.Integration, integrationId: integration.Id);
    await harness.HardDeleteIntegrationRowAsync(integration.Id); // removes only the Integrations row, as DeleteIntegrationAsync does today

    var all = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Resources));
    var system = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Resources, Context: InventoryContextFilter.System));
    var underA = await harness.Inventory.QueryAsync(new InventoryQuery(Mode: InventoryModes.Resources, Context: InventoryContextFilter.ForContext(app.Id.Value)));

    all.Items.Single(i => i.Name == "Orphan def").ResourceContextClassification.Should().Be("unknown");
    system.Items.Should().NotContain(i => i.Name == "Orphan def");
    underA.Items.Should().NotContain(i => i.Name == "Orphan def");
}

private static string Classification(InventoryQueryResult result, string action) =>
    result.Items.Single(i => i.Name.Contains(action)).ResourceContextClassification;
```
Add controller tests next to the existing `InventoryController` tests (locate them): unknown id → 400; `notRecorded` with `mode=resources` → 400; `shared` → 200; an archived Application's id → 200.

`InventoryTestHarness` and its seed helpers are new for this task — build them on `ResourceContextOwnershipTestHarness`'s pattern (Task 5) rather than inventing another harness shape. Audit seed rows must be inside `InventoryQueryService.AuditWindow` (24 hours), as the discoverability cases are. `NotificationDefinitionScopes` is the existing `Platform`/`Module`/`Integration` constant set.

EF Core InMemory does not prove SQL translation. Add one test for `SqlAuditService`'s context-classification filter and one for `SqlNotificationDefinitionStore`'s owner filter in whichever test project already runs those stores against SQL Server (locate it — likely `Apperture.IntegrationTests`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~InventoryContextClassificationTests"`
Expected: FAIL — `InventoryQuery.Context` and `InventoryItemDto.ResourceContextClassification` don't exist yet.

- [ ] **Step 3: Audit read side and source filters (A13, A14)**

Create `AuditSystemActionPrefixes.cs`:
```csharp
namespace Apperture.Core.Application.Services;

/// <summary>Audit action prefixes that never carry a ResourceContext by design (design gate §5.5).</summary>
public static class AuditSystemActionPrefixes
{
    public static readonly IReadOnlyList<string> All =
    [
        "auth.", "user.", "credential.", "identity_link.", "auth_admin.", "account.",
        "governance.", "audit.read.", "module.lifecycle.",
        "platform.settings.", "emergency.", "entra."
    ];

    public static bool Matches(string? action) =>
        action is not null && All.Any(p => action.StartsWith(p, StringComparison.Ordinal));
}

public enum AuditContextClassificationFilter { None, System, NotRecorded }
```
- `AuditTimelineEntry`: trailing `string? ResourceContextId = null`; `MapToEntry` passes `r.ResourceContextId`. Mirror it on `AuditTimelineEntryDto` and in `AuditController`'s entry→DTO mapping.
- `AuditFilterCriteria`: trailing `string? ResourceContextId = null, AuditContextClassificationFilter ContextClassification = AuditContextClassificationFilter.None`. `AuditQuery`: trailing `AuditContextClassificationFilter FilterByContextClassification = AuditContextClassificationFilter.None`. `AuditTimelineQueryService.QueryAsync` passes `FilterByResourceContextId: criteria.ResourceContextId` and `FilterByContextClassification: criteria.ContextClassification`.
- `SqlAuditService.ApplyFilters`, after the existing `FilterByResourceContextId` clause:
```csharp
if (query.FilterByContextClassification != AuditContextClassificationFilter.None)
{
    q = q.Where(x => x.ResourceContextId == null);
    if (query.FilterByContextClassification == AuditContextClassificationFilter.System)
    {
        q = q.Where(AnyActionPrefix(AuditSystemActionPrefixes.All));
    }
    else
    {
        foreach (var prefix in AuditSystemActionPrefixes.All)
            q = q.Where(x => !x.Action.StartsWith(prefix));
    }
}
```
EF Core cannot translate `All.Any(p => x.Action.StartsWith(p))`, so build the System predicate as an `OrElse` chain of `StartsWith` calls:
```csharp
private static Expression<Func<AuditLogEntity, bool>> AnyActionPrefix(IEnumerable<string> prefixes)
{
    var x = Expression.Parameter(typeof(AuditLogEntity), "x");
    var action = Expression.Property(x, nameof(AuditLogEntity.Action));
    var startsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
    Expression body = Expression.Constant(false);
    foreach (var prefix in prefixes)
        body = Expression.OrElse(body, Expression.Call(action, startsWith, Expression.Constant(prefix)));
    return Expression.Lambda<Func<AuditLogEntity, bool>>(body, x);
}
```
Use the actual audit entity type name from `SqlAuditService`.

- [ ] **Step 4: Run and NotificationDefinition source filters (A14)**

- Server `RunQueryFilter`: trailing `bool ResourceContextIdIsNull = false`. In `RunQueryService.ListRunsAsync`, next to the existing `ResourceContextId` clause: `if (filter?.ResourceContextIdIsNull == true) query = query.Where(r => r.ResourceContextId == null);`. Add the same trailing property to the client `RunQueryFilter` twin so the A2 parity test stays green.
- `NotificationDefinitionListQuery`: trailing `string? OwnerResourceContextId = null, bool SystemScopedOnly = false`. In `SqlNotificationDefinitionStore.ListAsync`, before ordering and `Take`:
```csharp
if (!string.IsNullOrWhiteSpace(query.OwnerResourceContextId))
{
    var owner = query.OwnerResourceContextId;
    q = q.Where(x => x.Scope == NotificationDefinitionScopes.Integration
        && _db.Integrations.Any(i => i.Id == x.IntegrationId && i.ResourceContextId == owner));
}
if (query.SystemScopedOnly)
    q = q.Where(x => x.Scope != NotificationDefinitionScopes.Integration);
```
- Add `ValueTask<IReadOnlyDictionary<string, string>> GetIntegrationOwnerContextIdsAsync(IReadOnlyCollection<string> integrationIds, CancellationToken ct)` to the store — one query over `_db.Integrations` returning id → `ResourceContextId` — and a pass-through on `INotificationDefinitionService`. It reads ownership from the Integrations table directly, not through `IIntegrationManagementService`, so classification does not depend on whether the operator can view Integrations. It returns only context ids for Integrations already referenced by definitions the operator can see.

Match the entity property names and id types in `AppertureDbContext`; adjust the snippets if `IntegrationId`/`ResourceContextId` are not plain strings.

- [ ] **Step 5: Inventory models, validation and classification (A12)**

In `IInventoryQueryService.cs`:
```csharp
public static class InventoryContextClassifications
{
    public const string System = "system";
    public const string Shared = "shared";
    public const string Application = "application";
    public const string NotRecorded = "notRecorded";
    public const string Unknown = "unknown";
}

public enum InventoryContextFilterKind { All, System, NotRecorded, Context }

public sealed record InventoryContextFilter(InventoryContextFilterKind Kind, string? ResourceContextId = null)
{
    public static InventoryContextFilter All { get; } = new(InventoryContextFilterKind.All);
    public static InventoryContextFilter System { get; } = new(InventoryContextFilterKind.System);
    public static InventoryContextFilter NotRecorded { get; } = new(InventoryContextFilterKind.NotRecorded);
    public static InventoryContextFilter ForContext(string resourceContextId) => new(InventoryContextFilterKind.Context, resourceContextId);
}
```
`InventoryQuery` gains trailing `InventoryContextFilter? Context = null` (null means All). `InventoryItemDto` gains trailing `string? ResourceContextId, string ResourceContextClassification`.

`InventoryController` injects the same ResourceContext service `IntegrationsController` uses, and `Get` gains `[FromQuery] string? context = null`, resolved before querying:
```csharp
private async Task<(InventoryContextFilter? Filter, string? Error)> ResolveContextAsync(string? context, string mode, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(context) || string.Equals(context, "all", StringComparison.OrdinalIgnoreCase))
        return (InventoryContextFilter.All, null);
    if (string.Equals(context, InventoryContextClassifications.System, StringComparison.OrdinalIgnoreCase))
        return (InventoryContextFilter.System, null);
    if (string.Equals(context, InventoryContextClassifications.NotRecorded, StringComparison.OrdinalIgnoreCase))
        return string.Equals(mode, InventoryModes.Activity, StringComparison.OrdinalIgnoreCase)
            ? (InventoryContextFilter.NotRecorded, null)
            : (null, "Context 'notRecorded' applies only to Activity.");

    var id = ResourceContextIds.IsShared(context) ? ResourceContextIds.SharedValue : context;
    var resolved = await _resourceContexts.GetAsync(new ResourceContextId(id), ct);
    return resolved is null
        ? (null, $"Context '{context}' not found.")
        : (InventoryContextFilter.ForContext(id), null);
}
```
Return `BadRequest(new { error })` on an error, like the fleet endpoint. Confirm `GetAsync` returns archived contexts (the fleet endpoint relies on the same call for archived ids, design gate §4.3); if it filters them out, use the variant that includes archived.

In `InventoryQueryService`, classify:
```csharp
private static (string? Id, string Classification) ClassifyContextId(string resourceContextId) =>
    ResourceContextIds.IsShared(resourceContextId)
        ? (resourceContextId, InventoryContextClassifications.Shared)
        : (resourceContextId, InventoryContextClassifications.Application);

private static (string? Id, string Classification) ClassifyRun(string? resourceContextId) =>
    resourceContextId is null ? (null, InventoryContextClassifications.NotRecorded) : ClassifyContextId(resourceContextId);

private static (string? Id, string Classification) ClassifyAudit(string? resourceContextId, string action) =>
    resourceContextId is not null
        ? ClassifyContextId(resourceContextId)
        : (null, AuditSystemActionPrefixes.Matches(action) ? InventoryContextClassifications.System : InventoryContextClassifications.NotRecorded);
```
- `MapModule` → `(null, System)`.
- `MapIntegration`/`MapConnection`/`MapSchedule` → `ClassifyContextId(row.ResourceContextId.Value)`.
- `MapNotificationDefinition`: Platform or Module scope → `(null, System)`. Integration scope → look up the owner in the dictionary from one `GetIntegrationOwnerContextIdsAsync` call per request (ids of the integration-scoped rows fetched) → `ClassifyContextId(owner)`. If the referenced Integration no longer exists — it can be hard-deleted today, leaving its definitions behind — classify `(null, Unknown)`, never System or Not recorded.
- `MapRun` → `ClassifyRun(row.ResourceContextId)`. `MapAudit` → `ClassifyAudit(row.ResourceContextId, row.Action)`.

- [ ] **Step 6: Apply the Context predicate at each source**

In `CollectResourcesAsync`/`CollectActivityAsync`, by `request.Context` kind:

| Source | All | System | NotRecorded | Context(id) |
|---|---|---|---|---|
| Modules | fetch | fetch | — | skip |
| Integrations / Connections / Schedules | unfiltered overload | skip | — | `…Async(new ResourceContextId(id), ct)` |
| NotificationDefinitions (capped) | unfiltered | `SystemScopedOnly: true` | — | `OwnerResourceContextId: id` |
| Runs (capped) | unfiltered | skip | `ResourceContextIdIsNull: true` | `ResourceContextId: id` |
| Audit (capped) | unfiltered | `ContextClassification: System` | `ContextClassification: NotRecorded` | `ResourceContextId: id` |

"—" cannot occur: `notRecorded` is rejected in Resources mode. Keep the existing capability checks around each source.

Then add the cross-check at the top of `Matches`:
```csharp
if (request.Context is { Kind: not InventoryContextFilterKind.All } filter)
{
    var matches = filter.Kind switch
    {
        InventoryContextFilterKind.System => item.ResourceContextClassification == InventoryContextClassifications.System,
        InventoryContextFilterKind.NotRecorded => item.ResourceContextClassification == InventoryContextClassifications.NotRecorded,
        _ => string.Equals(item.ResourceContextId, filter.ResourceContextId, StringComparison.Ordinal)
    };
    if (!matches)
        return false;
}
```

- [ ] **Step 7: Wire the controller and client**

`InventoryController.Get`: pass the resolved filter into `new InventoryQuery(mode, type, q, status, sort, take, contextFilter)`; map `ResourceContextId`/`ResourceContextClassification` in `ToClient`. Client `InventoryDtos.cs`: matching fields. `ManagementClient.GetInventoryAsync` (lines 439-463): add `string? context = null` and `Add(pairs, "context", context);`. `GetFromJsonAsync` throws `HttpRequestException` with `StatusCode` set on a 400; the page relies on that below.

- [ ] **Step 8: Add the Context filter and column to `Inventory.razor`**

- Inject `ResourceContextViewState` and use it only as the context directory: `KnownContexts`, `EnsureContextsKnownAsync`, `DescribeContext`. Never read `Current` or `SelectorVisible`. Inventory's own `context` query parameter is local to this page and unrelated to the shell's `?context=`.
- The **Context** filter and column always render, even with zero Applications. Options: All, System, Shared, Not recorded (Activity mode only), then each context in `KnownContexts` whose `Kind` is Application (active and archived; Shared is already listed), labelled with `DescribeContext`.
- Read `context` from the page's query string on init, like `mode`/`type`/`status`/`sort`. When switching to Resources while Not recorded is selected, reset the filter to All before reloading.
- Column values: "System", "Not recorded", "Unknown context" (for `unknown`), or `DescribeContext(item.ResourceContextId)` for Shared/Application rows. Call `EnsureContextsKnownAsync` with the row ids after loading; an id still missing shows "Unknown context".
- Catch `HttpRequestException { StatusCode: HttpStatusCode.BadRequest }` while the filter is not All: show "This Context filter is no longer valid." with a **Reset to All** button that clears the filter and reloads. Never render an empty Inventory for this case.

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~InventoryContextClassificationTests|FullyQualifiedName~Inventory|FullyQualifiedName~ContractParity"`
Run the SQL-translation tests added in Step 1 in their project.
Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: PASS / SUCCEEDS.

- [ ] **Step 10: Commit**

Stage the files listed under **Files** above (plus the SQL-translation test file), check `git status` shows nothing else, then:
```bash
git commit -m "CTX-3A: add Config Inventory's Context filter and column with source-level filtering (§5.5, A12-A14)"
```

**Known limitations:**
- `recovery.*` audit events are written today without a `ResourceContextId` (`RecoveryController` omits it), so they classify Not recorded even when new. Threading the id through `RecoveryController` is follow-up work outside CTX-3A (design gate §5.5).
- Inventory's existing 24-hour audit window and its in-memory Type/Status behavior are unchanged; this task's discoverability guarantee is about the Context filter.
- Orphaned integration-scoped NotificationDefinitions (left behind by Integration hard delete) show as Unknown context. Fixing the delete lifecycle is the CTX-3B pre-closure follow-up in design gate §13, not part of this task.

---

## Closing manual smoke (controller, against the real dev database, after the final review)

1. With no active Application: `/integrations`, `/connections`, `/schedules`, `/runs` and the detail pages look as before (no Context column, chip or notice). Config → Inventory shows the Context column and filter with All, System and Shared (plus Not recorded in Activity).
2. Create an Application and an Integration in it. On each of the four lists, switch the View while on the page: the URL changes, the list reloads without a manual refresh, and the browser network panel shows one request with the new filter — no request with the previous view first.
3. From `/connections?context={app}`, follow the "N Shared connections" link: one request with `contextId=shared`.
4. Open a Shared Connection under the Application view: "Context: Shared" chip and the mismatch notice with "Integrations in … can use it."
5. Archive an Application that still has historical Runs: its name, marked archived, still appears in the Runs Context column, the Run detail chip and Inventory.
6. Open Inventory with a made-up `context` query value: "This Context filter is no longer valid." with Reset to All.
7. In Inventory Activity mode, select Not recorded, then switch to Resources: the filter resets to All and no error appears.
8. On a disposable test Integration that has an Integration-scoped Notification Definition and no bindings or schedules, delete the Integration: Inventory shows the definition as Unknown context under All only.

---

## Self-Review Notes

- **Spec coverage:** A3 (Task 1); URL-authoritative scoped Integrations/Connections/Schedules/Runs (Tasks 2–5); the Run-snapshot regression test (Task 5); the Context chip and mismatch notice on all ten detail/hub pages, including the schedule-diagnostics deep link (Tasks 6–7); A8/A9 (Task 8); Config Inventory with A12–A14 (Task 9). Existing filters stay AND-composed everywhere.
- **Consistent names:** `OwnershipChip` and `MismatchNotice` take ids only and are used the same way on all ten pages. Classification tokens `system`/`shared`/`application`/`notRecorded` are identical in the API, `Matches`, the column and the tests.
- **Loading model:** all four scoped lists use `ScopedListContextFilter` and reload on a `?context=` change; none builds a request from `ViewState.Current`. This closes the stale-request gap on in-app navigation; bootstrap was already safe.
- **Display resolution:** every context name goes through `DescribeContext` after `EnsureContextsKnownAsync` — chips, mismatch owner and current-view labels, Run detail, the Connections notice, binding and "Used by" labels, list Context columns and Inventory. Archived contexts show their name; ids that cannot be resolved show "Unknown context".
- **Zero-Applications rule:** operational Context columns, chips, notices and binding labels are gated on `SelectorVisible`; Inventory is the documented exception.
- **Unknown context:** an unresolved id renders "Unknown context" with no "Switch view" action; orphaned integration-scoped NotificationDefinitions classify `unknown` in Inventory and appear only under All.
- **Copy:** Runs and Control Center share "Some older runs don't have a recorded context. Runs without a recorded context appear only under All."; Plan 3's notices read "That context doesn't exist." and "{name} is archived. Showing all."
- **Inventory correctness:** every non-`all` Context value reaches the capped sources (Runs, audit events, NotificationDefinitions) inside their queries. Discoverability tests cover a real id, `notRecorded` and `system` on capped Activity sources, plus the owner filter on NotificationDefinitions. The new audit and NotificationDefinition SQL predicates are tested against SQL Server, not only InMemory.
- **Open confirmations for implementers (checks, not product decisions):** that no UI consumer of Plan 3's view notices exists yet (Task 2 Step 3b); whether `ConnectionsPage.SetTab` preserves `context`; the route for Shared's detail page; whether `IResourceContextService.GetAsync` includes archived contexts; entity property names and types in the new SQL predicates.
