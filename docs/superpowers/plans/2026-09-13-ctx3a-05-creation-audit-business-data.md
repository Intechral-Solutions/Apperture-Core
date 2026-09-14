# CTX-3A Plan 5 — Creation Flows, Audit Filter & Business Data Banner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the UI from silently relying on the API's Shared default when Applications exist — require an explicit Application choice under All, preselect under a specific view, restate the destination near submit — for Integration, Connection (including in-flow binding creation), and Schedule; fix the ScopeKey/Application copy confusion (fact F7); add Audit's own Application filter (A7); and add the Business Data transitional banner (§11).

**Architecture:** Every creation page reads `ResourceContextViewState.Current` (Plan 3) to compute its preselection, and sends `ResourceContextId` on the already-extended client request records (Plan 1). Audit gains one new query parameter threaded through three layers (`AuditController` → `AuditFilterCriteria` → `AuditTimelineQueryService` → the already-`ResourceContextId`-aware `AuditQuery`) plus a client parameter and a page-level select. Business Data gets one new conditional message, following the existing `DemoCustomerUiHelpers` constant pattern.

**Tech Stack:** Blazor WebAssembly, ASP.NET Core, xUnit + FluentAssertions.

**Spec:** `docs/01-architecture/transitions/resource-contexts/ctx3-design-gate.md` — this plan implements design-gate §13 CTX-3A steps 9–11 (`§7` creation semantics in full, fact F7, `§5.4` Audit, `§10.1` row A7, `§11` Business Data, `§14` "Creation" and "Business Data" acceptance criteria).

**Depends on:** `2026-09-13-ctx3a-01-contracts-and-domain.md` and `2026-09-13-ctx3a-03-config-ui-and-shell-selector.md` (both must be merged first).

## Global Constraints

- Under `All`, Integration/Connection create **require** an explicit Application choice before submit — the UI never falls back to the API's Shared default (design gate §7.1, R7). This is a UI-only rule; the API's Shared default (CTX-1) is untouched, for compatibility.
- With **no** active Application anywhere in the instance, the Application field is hidden entirely and the request omits `ResourceContextId` — today's behavior is unchanged byte-for-byte (design gate §7.2, R19).
- In-flow Connection creation during binding offers **only** the owning Integration's Application or Shared — never the full Application list (design gate §7.3).
- No standalone Schedule creation path is added, anywhere, in this plan (design gate §7.5, fact F3) — Schedule creation always shows the inherited Application read-only.
- ScopeKey's *behavior* is unchanged — only its label, help text, and disclosure placement change (design gate §7.2, fact F7, R13).
- Audit stays `Unscoped` by the shell View — its own Application filter is independent, defaulting to All, with an opt-in "Use current view" chip that applies nothing automatically (design gate §5.4).
- Business Data is **never** filtered by View — the new copy is purely informational, shown only under a specific Application/Shared view, never under All (design gate §11). No Customer is ever assigned to Shared or any Application.

---

## File Structure

**Modified files:**
- `Pages/Integrations/IntegrationNew.razor` — Application select, restated destination, ScopeKey moved under Advanced and relabeled.
- `Pages/Connections/ConnectionNew.razor` — Application select, same principles.
- `Pages/Integrations/IntegrationConnections.razor` — in-flow Connection creation restricted to Integration's Application or Shared.
- `Pages/Integrations/IntegrationSchedules.razor` — read-only inherited Application row.
- `Pages/Schedules/ScheduleCreate.razor` — Integration picker filtered by view, "Show all integrations" toggle, read-only Application row once an Integration is chosen.
- `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/AuditController.cs:45-75` — `resourceContextId` query parameter.
- `src/Apperture.Core/Apperture.Core.Application/Services/AuditTimelineModels.cs:36-46` (`AuditFilterCriteria`) — add `ResourceContextId`.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/Audit/AuditTimelineQueryService.cs:18-40` — map the new criteria field into `AuditQuery.FilterByResourceContextId`.
- `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs:138-149` (`GetAuditTimelineAsync`) — add `resourceContextId` parameter.
- `src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs:495-525` — build the new query segment.
- `Pages/Control/AuditTimeline.razor` — Application select, "Use current view" chip.
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/DemoCustomerUiHelpers.cs:8-18` — new transitional-message constant.
- `Pages/BusinessData/BusinessDataLanding.razor`, `Pages/Demo/DemoCustomersList.razor` — render the new message under a specific view.

**No new files.**

---

## Task 1: Integration create — Application select, destination restatement, ScopeKey copy fix (§7.2, F7)

**Files:**
- Modify: `Pages/Integrations/IntegrationNew.razor` (metadata step, lines ~76-103, 255-274)

**Interfaces:**
- Consumes: `ResourceContextViewState.Current`, `.KnownActiveApplications`, `.SelectorVisible` (Plan 3); `CreateIntegrationRequest.ResourceContextId` (Plan 1); `ManagementAdvancedSection` (existing).

- [ ] **Step 1: Add the Application select, positioned before Display Name**

```razor
@if (ViewState.SelectorVisible)
{
    <div class="md:col-span-2">
        <label class="block text-sm font-medium text-heading">Application (required)</label>
        <select class="ui-input mt-1 w-full" @bind="_selectedResourceContextId">
            <option value="" disabled selected="@(_selectedResourceContextId == "")">Choose…</option>
            <option value="shared">Shared</option>
            @foreach (var app in ViewState.KnownActiveApplications)
            {
                <option value="@app.Id">@app.Name</option>
            }
        </select>
    </div>
}
```
Place this block immediately before the existing Display Name field (line ~77).

- [ ] **Step 2: Compute the preselection**

```csharp
protected override void OnInitialized()
{
    if (ViewState.Current.IsAll)
    {
        _selectedResourceContextId = string.Empty; // "Choose…", submit disabled until set
    }
    else
    {
        _selectedResourceContextId = ViewState.Current.ResourceContextId!;
    }
}
```
If a `?context=` query value was present on `/integrations/new` (design gate §4.5: "Each scoped list's 'Create …' button links with the current view"), it flows through `ViewState.Current` the same way any other page reads it — confirm `/integrations/new` is classified `Unscoped` by `ResourceContextViewRoutes` (per Plan 1's Task 7 test table) yet still needs to read an incoming `?context=` param for preselection purposes distinct from the shell's tab-state resolution. Since create routes are intentionally `Unscoped` (they don't participate in the shell's URL-authoritative rewriting), read the incoming query parameter directly here via `[SupplyParameterFromQuery(Name="context")]` rather than relying on `ResourceContextViewState.Current` for the *link-driven* preselection case — but still fall back to `ResourceContextViewState.Current` (the current tab's view) when no query parameter is present, per design gate §7.2 ("Preselection comes from `?context=`, else a specific tab view, else nothing").

- [ ] **Step 3: Restate the destination near submit**

```razor
@if (!string.IsNullOrWhiteSpace(_selectedResourceContextId))
{
    <div class="mt-2 text-sm text-muted">Creates the integration in <strong>@DestinationLabel()</strong>.</div>
}
```
```csharp
private string DestinationLabel() =>
    _selectedResourceContextId == "shared" ? "Shared" :
    ViewState.KnownActiveApplications.FirstOrDefault(a => a.Id == _selectedResourceContextId)?.Name ?? _selectedResourceContextId;
```

- [ ] **Step 4: Hide the field entirely with no active Applications**

The `@if (ViewState.SelectorVisible)` guard in Step 1 already achieves this — confirm `ViewState.SelectorVisible` is `false` precisely when `KnownActiveApplications.Count == 0` (Plan 3's definition), and that in that case `_selectedResourceContextId` stays `null`/unset so the submit path (Step 6) omits `ResourceContextId` entirely, preserving today's behavior byte-for-byte.

- [ ] **Step 5: Move ScopeKey under Advanced and fix the copy (fact F7)**

Replace the current ScopeKey block (lines 89-95):
```html
<div class="md:col-span-2">
    <label class="block text-sm font-medium text-heading">Scope key (optional)</label>
    <input class="ui-input mt-1 w-full text-sm font-mono" @bind="_scopeKey" placeholder="instance (default if empty)" />
    <div class="mt-0.5 text-xs text-muted">Environment/context; leave empty for instance scope.</div>
</div>
```
with:
```razor
<ManagementAdvancedSection Title="Advanced" InitiallyOpen="false">
    <label class="block text-sm font-medium text-heading">Authorization scope key (optional)</label>
    <input class="ui-input mt-1 w-full text-sm font-mono" @bind="_scopeKey" placeholder="instance (default if empty)" />
    <div class="mt-0.5 text-xs text-muted">Advanced authorization scope. Unrelated to the application. Leave empty for instance scope.</div>
</ManagementAdvancedSection>
```

- [ ] **Step 6: Submit with `ResourceContextId`**

Change `SubmitAsync` (lines 255-274) from:
```csharp
var request = new CreateIntegrationRequest(_displayName.Trim(), _moduleId.Trim(), string.IsNullOrWhiteSpace(_scopeKey) ? null : _scopeKey.Trim());
```
to:
```csharp
var request = new CreateIntegrationRequest(
    _displayName.Trim(), _moduleId.Trim(),
    string.IsNullOrWhiteSpace(_scopeKey) ? null : _scopeKey.Trim(),
    ResourceContextId: string.IsNullOrWhiteSpace(_selectedResourceContextId) ? null : _selectedResourceContextId);
```
Also disable submit while `ViewState.SelectorVisible && string.IsNullOrWhiteSpace(_selectedResourceContextId)` (Application not yet chosen under All), matching R7's "required explicit choice under All."

- [ ] **Step 7: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 8: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationNew.razor
git commit -m "CTX-3A: require explicit Application choice on Integration create, fix ScopeKey copy (§7.2, F7, R7)"
```

---

## Task 2: Connection create — same principles (§7.3)

**Files:**
- Modify: `Pages/Connections/ConnectionNew.razor:37-59, 138-184`

**Interfaces:**
- Consumes: same as Task 1; `CreateConnectionRequest.ResourceContextId` (Plan 1).

- [ ] **Step 1: Add the Application select, preselection, destination restatement**

Apply the identical shape from Task 1 Steps 1-4 to `ConnectionNew.razor`, positioned before the Connector definition select (the page's current first field, per research). Note this page has **no** existing ScopeKey field at all (unlike Integration create) — do not add one; this task only adds the Application select.

- [ ] **Step 2: Submit with `ResourceContextId`**

Change `CreateAsync` (lines 138-184) to add `ResourceContextId` to the constructed `CreateConnectionRequest`:
```csharp
var created = await Client.CreateConnectionAsync(
    new CreateConnectionRequest(
        _connectorDefinitionId,
        _connectionName.Trim(),
        cfg,
        SecretRefs: new Dictionary<string, string>(),
        SecretValues: secrets.Count > 0 ? secrets : null,
        ResourceContextId: string.IsNullOrWhiteSpace(_selectedResourceContextId) ? null : _selectedResourceContextId));
```
(Verify the exact positional-vs-named argument order needed given `CreateConnectionRequest`'s full parameter list from Plan 1, Task 2 — `ResourceContextId` was appended last, so a named argument is safe regardless of the other optional parameters' positions.)

- [ ] **Step 3: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 4: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Connections/ConnectionNew.razor
git commit -m "CTX-3A: require explicit Application choice on Connection create (§7.3, R7)"
```

---

## Task 3: In-flow Connection creation during binding — restricted options (§7.3)

**Files:**
- Modify: `Pages/Integrations/IntegrationConnections.razor:140-184, 469-521`

**Interfaces:**
- Consumes: the hosting Integration's `ResourceContextId`/`ResourceContext` (already loaded on this page), `CreateConnectionRequest.ResourceContextId`.

- [ ] **Step 1: Add the restricted Application/Shared choice to the in-flow create panel**

Inside the `_addingConnectionInFlow` panel (lines 140-184), add:
```razor
@if (_integration!.ResourceContextId == "shared")
{
    <div class="text-sm text-muted">Application: Shared <span class="text-xs">(fixed — this integration is Shared)</span></div>
}
else
{
    <div>
        <label class="block text-sm font-medium text-heading">Application</label>
        <select class="ui-input mt-1 w-full" @bind="_inFlowResourceContextId">
            <option value="@_integration.ResourceContextId">@(_integration.ResourceContext?.Name ?? _integration.ResourceContextId) <span>Only this application's integrations can use it.</span></option>
            <option value="shared">Shared <span>Integrations in any application can use it.</span></option>
        </select>
    </div>
}
```
(Native `<select><option>` cannot render a nested `<span>` sublabel — express the "Only this application's integrations can use it."/"Integrations in any application can use it." copy as a `<div class="text-xs text-muted">` beneath the select that updates based on the current selection, rather than trying to embed it inside `<option>` tags.)

- [ ] **Step 2: Default to the Integration's own Application**

```csharp
private string _inFlowResourceContextId = string.Empty;

private void OnAddingConnectionInFlowOpened()
{
    _inFlowResourceContextId = _integration!.ResourceContextId ?? "shared";
}
```
Call `OnAddingConnectionInFlowOpened()` wherever `_addingConnectionInFlow` is set to `true` (locate that call site and add this alongside it).

- [ ] **Step 3: Submit with the chosen `ResourceContextId`**

Change `CreateConnectionInFlowAsync` (lines 469-521) to include:
```csharp
var created = await Client.CreateConnectionAsync(new CreateConnectionRequest(
    _inFlowConnectorDefinitionId,
    _inFlowConnectionName.Trim(),
    _inFlowConfigValues.Where(...).ToDictionary(...),
    SecretRefs: new Dictionary<string, string>(),
    SecretValues: secrets.Count > 0 ? secrets : null,
    ResourceContextId: _integration!.ResourceContextId == "shared" ? "shared" : _inFlowResourceContextId));
```

- [ ] **Step 4: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 5: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationConnections.razor
git commit -m "CTX-3A: restrict in-flow Connection creation to the Integration's Application or Shared (§7.3)"
```

---

## Task 4: Schedule creation — inherited Application, filtered Integration picker (§7.4, F3)

**Files:**
- Modify: `Pages/Integrations/IntegrationSchedules.razor:85-123`
- Modify: `Pages/Schedules/ScheduleCreate.razor:58-72, 217, 284-350`

**Interfaces:**
- Consumes: the hosting Integration's `ResourceContextId`/`ResourceContext` (hub Schedules tab); `IManagementClient.GetIntegrationsAsync(contextId, ct)` (Plan 1) for the filtered picker.

- [ ] **Step 1: Add the read-only Application row to the hub Schedules tab**

In `IntegrationSchedules.razor`'s "Add schedule" panel (lines 85-123), add:
```razor
<div class="text-sm text-muted">Application: @(_integration?.ResourceContext?.Name ?? "Shared") (from integration)</div>
```
No submission change is needed here — `CreateScheduleAsync` (line 209-244) already omits `ResourceContextId` and lets the server inherit it (fact F3, unchanged).

- [ ] **Step 2: Filter the Integration picker on `/schedules/new` by the current view, with a "Show all integrations" toggle**

In `ScheduleCreate.razor`, change the Integration list fetch (line 217, `Client.GetIntegrationsAsync()`) to:
```csharp
private bool _showAllIntegrations;

private async Task LoadIntegrationsAsync()
{
    var contextFilter = ViewState.Current.IsAll || _showAllIntegrations ? null : ViewState.Current.ToApiFilter();
    _integrations = await Client.GetIntegrationsAsync(contextFilter);
}
```
Add the toggle, shown only under a specific view (design gate §7.4: "not shown under All"):
```razor
@if (!ViewState.Current.IsAll)
{
    <label>
        <input type="checkbox" @bind="_showAllIntegrations" @bind:after="LoadIntegrationsAsync" /> Show all integrations
    </label>
}
```
Default `_showAllIntegrations` to `false` under a specific view (per design gate: "off by default under a specific view").

- [ ] **Step 3: Label picker options with their Application under All**

```razor
<option value="@i.Id">@i.DisplayName @(ViewState.Current.IsAll ? $"— {(i.ResourceContext?.Name ?? "Shared")}" : "")</option>
```

- [ ] **Step 4: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 5: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationSchedules.razor src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/ScheduleCreate.razor
git commit -m "CTX-3A: show inherited Application read-only on schedule creation, filter integration picker by view (§7.4, F3)"
```

---

## Task 5: Audit gets its own Application filter (A7, §5.4)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/AuditController.cs:45-75`
- Modify: `src/Apperture.Core/Apperture.Core.Application/Services/AuditTimelineModels.cs:36-46`
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/Audit/AuditTimelineQueryService.cs:18-40`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs:138-149`, `ManagementClient.cs:495-525`
- Modify: `Pages/Control/AuditTimeline.razor:30-79, 203-235, 253-326`
- Test: `tests/Apperture.UnitTests/ResourceContexts/AuditResourceContextFilterTests.cs`

**Interfaces:**
- Consumes: `AuditQuery.FilterByResourceContextId` (already exists server-side, already wired into `SqlAuditService` per Plan-independent research — no persistence change needed).
- Produces: `AuditFilterCriteria.ResourceContextId : string?`, `GET api/control/audit?resourceContextId=`, client `GetAuditTimelineAsync(..., resourceContextId, ...)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task List_WithResourceContextId_FiltersToThatContextsAuditRecords()
{
    var harness = await AuditTestHarness.CreateAsync();
    await harness.SeedAuditRecordAsync(resourceContextId: "canvas-id");
    await harness.SeedAuditRecordAsync(resourceContextId: "other-id");

    var response = await harness.Client.GetAsync("api/control/audit?resourceContextId=canvas-id");
    var page = await response.Content.ReadFromJsonAsync<AuditTimelinePage>();

    page!.Entries.Should().ContainSingle();
}

[Fact]
public async Task List_WithoutResourceContextId_ReturnsAllRecordsIncludingNullContext()
{
    var harness = await AuditTestHarness.CreateAsync();
    await harness.SeedAuditRecordAsync(resourceContextId: null);
    await harness.SeedAuditRecordAsync(resourceContextId: "canvas-id");

    var response = await harness.Client.GetAsync("api/control/audit");
    var page = await response.Content.ReadFromJsonAsync<AuditTimelinePage>();

    page!.Entries.Should().HaveCount(2);
}
```
Follow whatever existing `AuditController`/`AuditTimelineQueryService` test harness already exists (locate it first — the class name `AuditTestHarness` above is illustrative, not necessarily what already exists).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~AuditResourceContextFilterTests"`
Expected: FAIL — `resourceContextId` isn't accepted by the controller yet.

- [ ] **Step 3: Add `ResourceContextId` to `AuditFilterCriteria`**

In `AuditTimelineModels.cs`, append a trailing optional parameter to `AuditFilterCriteria` (lines 36-46):
```csharp
string? ResourceContextId = null);
```

- [ ] **Step 4: Thread it through `AuditTimelineQueryService.QueryAsync`**

In `AuditTimelineQueryService.cs` (lines 18-40), add `FilterByResourceContextId: criteria.ResourceContextId` to the constructed `AuditQuery` (currently missing per research, even though the field exists on `AuditQuery` itself).

- [ ] **Step 5: Add the query parameter to `AuditController.List`**

In `AuditController.cs` (lines 45-56), add `[FromQuery] string? resourceContextId = null` to the `List` action's parameter list, and pass it into the constructed `AuditFilterCriteria` (lines 63-73).

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~AuditResourceContextFilterTests"`
Expected: PASS

- [ ] **Step 7: Add the client parameter**

In `IManagementClient.cs:138-149`, add `string? resourceContextId = null` to `GetAuditTimelineAsync`'s parameter list. In `ManagementClient.cs:495-525`, add `Add(pairs, "resourceContextId", resourceContextId);` alongside the other `Add(pairs, ...)` calls.

- [ ] **Step 8: Add the Application select and "Use current view" chip to the Audit page**

In `AuditTimeline.razor`, add a new filter control alongside the existing Time range/Category/Outcome/Actor filters (lines 30-79):
```razor
<div>
    <label class="text-sm font-medium text-heading">Application</label>
    <select class="ui-input mt-1" @bind="_resourceContextFilter" @bind:after="LoadAsync">
        <option value="">All</option>
        <option value="shared">Shared</option>
        @foreach (var app in ViewState.KnownContexts.Where(c => c.Kind == "Application"))
        {
            <option value="@app.Id">@app.Name@(app.State == "Archived" ? " (archived)" : "")</option>
        }
    </select>
    <div class="mt-0.5 text-xs text-muted">Only activity recorded against an application. Platform and account activity has no application.</div>
</div>
@if (!ViewState.Current.IsAll && _resourceContextFilter != ViewState.Current.ResourceContextId)
{
    <button class="ui-link" @onclick="() => { _resourceContextFilter = ViewState.Current.ResourceContextId!; return LoadAsync(); }">
        Filter to @CurrentViewLabel()
    </button>
}
```
Note `ViewState.KnownContexts` must expose **archived** contexts too here (design gate §5.4: "Include archived contexts in the Audit filter because historical records still reference them") — confirm `ResourceContextViewState` exposes a full list including archived ones (not just `KnownActiveApplications`, which Plan 3 defined as active-only); add a `KnownContexts` (all, including archived) property to `ResourceContextViewState` in this task if it doesn't already have one suitable for this purpose.

Update `LoadAsync`/`LoadMoreAsync` (lines 253-326) to pass `resourceContextId: string.IsNullOrWhiteSpace(_resourceContextFilter) ? null : _resourceContextFilter` into `Client.GetAuditTimelineAsync(...)`, and add `_resourceContextFilter` to the `_dataLoadKey` composition (lines 226-235) so changing it retriggers a load.

- [ ] **Step 9: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 10: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/AuditController.cs src/Apperture.Core/Apperture.Core.Application/Services/AuditTimelineModels.cs src/Apperture.ControlPlane/Apperture.ControlPlane.Security/Audit/AuditTimelineQueryService.cs src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Control/AuditTimeline.razor tests/Apperture.UnitTests/ResourceContexts/AuditResourceContextFilterTests.cs
git commit -m "CTX-3A: add Audit's own Application filter with 'Use current view' chip (A7, §5.4)"
```

---

## Task 6: Business Data transitional banner (§11)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/DemoCustomerUiHelpers.cs:8-18`
- Modify: `Pages/BusinessData/BusinessDataLanding.razor`
- Modify: `Pages/Demo/DemoCustomersList.razor`

**Interfaces:**
- Consumes: `ResourceContextViewState.Current`.

- [ ] **Step 1: Add the new constant**

In `DemoCustomerUiHelpers.cs`, add alongside `DemoScopeBanner`:
```csharp
public const string ApplicationScopeTransitionalNotice =
    "Customers is a demo collection for the whole instance. It isn't organized by application yet, so your view doesn't filter it.";
```
(Note: this is a **new, third** string, distinct from both `DemoScopeBanner` — sandbox-data provenance — and `BusinessDataLanding.razor`'s own inline subtitle — "not owned by the integrations that process them." Do not conflate or replace either existing string; this plan adds a fourth piece of copy that appears only conditionally.)

- [ ] **Step 2: Render it conditionally on both pages**

In both `BusinessDataLanding.razor` and `DemoCustomersList.razor`, add (near the existing `PageMessage`/subtitle, inject `ResourceContextViewState` if not already injected):
```razor
@if (!ViewState.Current.IsAll)
{
    <PageMessage Kind="PageMessageKind.Info" Message="@DemoCustomerUiHelpers.ApplicationScopeTransitionalNotice" />
}
```
Confirm neither page currently injects `ResourceContextViewState` (research found neither has any context-handling today) — add the `@inject` directive.

- [ ] **Step 3: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 4: Manual verification of the "never under All" rule**

Since this is purely conditional-rendering markup with no state mutation, no automated test is strictly required beyond the build — but add a bUnit test if Plan 3's Task 9 established a working bUnit project:
```csharp
[Fact]
public void BusinessDataLanding_UnderAll_DoesNotShowTransitionalNotice()
{
    // render with FakeViewState.WithScope(ResourceContextViewScope.All), assert message absent
}

[Fact]
public void BusinessDataLanding_UnderASpecificView_ShowsTransitionalNotice()
{
    // render with FakeViewState.WithScope(ResourceContextViewScope.Of("canvas-id")), assert message present
}
```

- [ ] **Step 5: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/DemoCustomerUiHelpers.cs src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/BusinessData/BusinessDataLanding.razor src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Demo/DemoCustomersList.razor
git commit -m "CTX-3A: add Business Data transitional notice under a specific view (§11)"
```

---

## Self-Review Notes

- **Spec coverage:** §7.1-§7.5 (Tasks 1-4), fact F7's ScopeKey copy fix (Task 1), §5.4/A7 (Task 5), §11 (Task 6) are all covered. §7.5's "no standalone Schedule creation" is respected by omission — Task 4 touches only the two existing, already-Integration-required creation paths.
- **Type consistency:** every task reuses `ResourceContextViewState`/`ResourceContextViewScope` from Plan 3 and the `ResourceContextId` fields from Plan 1 verbatim; no new DTO shapes are introduced in this plan except the one new `AuditFilterCriteria` field, which mirrors the existing `AuditQuery.FilterByResourceContextId` name closely enough to avoid confusion (kept as `ResourceContextId`, not renamed).
- **No placeholders:** the one open question (whether `ResourceContextViewState` needs a new `KnownContexts`-including-archived property for the Audit filter, beyond Plan 3's active-only `KnownActiveApplications`) is flagged as a concrete, small addition to make in Task 5 rather than assumed to already exist.
