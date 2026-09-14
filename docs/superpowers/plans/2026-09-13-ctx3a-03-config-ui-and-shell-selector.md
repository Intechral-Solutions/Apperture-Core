# CTX-3A Plan 3 — Config UI & Shell View Selector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `Config → Applications` management pages (list with a distinct Shared card, create, detail/edit/archive/unarchive) and the shell-level "View" selector with its URL/tab-state/localStorage resolution algorithm — the operator-facing surface that makes Applications visible and switchable for the first time.

**Architecture:** A new `Apperture.Management.Client` method group wraps Plan 2's `ResourceContextsController`. Three new Razor pages under `ConfigLayout` follow the existing `Pages/Config/Governance/UserNew.razor`/`UserDetail.razor`/`GovernanceLanding.razor` conventions. The shell selector is a new `ResourceContextViewState` scoped DI service (mirroring `ThemeService`'s IJSRuntime-mediated localStorage pattern) plus a new `ViewSelector.razor` component (mirroring `ThemeMenu.razor`'s trigger/panel/`TopBarMenuCoordinator` pattern, with `SearchableList.razor` reused for the >7-Applications search case), inserted into `TopBar.razor` before `TopBarOrientation`.

**Tech Stack:** Blazor WebAssembly, `IJSRuntime` interop, bUnit for component tests, xUnit + FluentAssertions for the pure resolution-algorithm tests.

**Spec:** `docs/01-architecture/transitions/resource-contexts/ctx3-design-gate.md` — this plan implements design-gate §13 CTX-3A steps 5–6 (`§3` shell selector in full, `§4` persistence of selection in full, `§6.1`–`§6.4`, `§6.6` Application management UI, `§14` "Management" and "Selector visibility" acceptance criteria).

**Depends on:** `2026-09-13-ctx3a-01-contracts-and-domain.md` (`ResourceContextViewScope`, `ResourceContextViewRoutes`) and `2026-09-13-ctx3a-02-authorization-and-management-api.md` (`ResourceContextsController`, `OperatorCapabilitiesDto.CanManageResourceContexts`) — both must be merged first.

## Global Constraints

- The selector's label is **"View"**, never "Workspace", "Application", or "Context" (design gate §3.1, R12).
- The selector is **hidden** until at least one active Application exists; before then, ownership labels stay hidden too and existing installations look unchanged (design gate §3.3, D3, R19).
- `All` is never written to the URL as an empty/missing parameter's implicit meaning — every scoped page's URL explicitly carries `?context=all` or `?context=<id>` once resolution completes (design gate §4.2, §4.3).
- An explicit `?context=` arriving by URL **never** writes `localStorage`; only an explicit selector click does (design gate §4.2, §4.3, R1).
- `localStorage` key is `apperture.view.{userId}` — user-keyed, so a shared browser never leaks one operator's view to another (design gate §4.1).
- No server-side `UserPreferences` storage is used for the view selection — this is a deliberate, explained non-change (design gate §4.1).
- The shell does not subscribe to `storage` events — one tab/window never retargets another (design gate §4.3, R5).
- Slug stays under an "Advanced" disclosure on the create form — it is never promoted to a primary field, since no slug-addressed route exists yet (design gate §6.6).
- Shared's detail page offers no rename/slug/description edit or archive action; the API already 409s on mutation attempts (Plan 2) — this plan's UI must not attempt those calls for Shared, not merely hide the buttons as decoration.
- No new rail domain, no nesting under Applications (design gate §1, §6.6 "No parent field. No nesting.").

---

## File Structure

**New files:**
- `src/Apperture.ControlPlane/Apperture.Management.Client/ResourceContextDtos.cs` — client `ResourceContextDto`, `CreateResourceContextRequest`, `UpdateResourceContextRequest`, `ResourceContextOwnershipSummaryDto` (mirroring Plan 2's controller response shapes) plus the six client method signatures added to `IManagementClient`/`ManagementClient`.
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Applications/ApplicationsList.razor` — `/config/applications`.
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Applications/ApplicationNew.razor` — `/config/applications/new`.
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Applications/ApplicationDetail.razor` — `/config/applications/{id}`.
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Services/ResourceContextViewState.cs` — the tab/localStorage-aware view-state service.
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Molecules/ViewSelector.razor` — the top-bar selector component.
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/wwwroot/js/apperture-shell.js` — extend with `getViewPreference`/`setViewPreference` (modify, not create).
- `tests/Apperture.Management.Ui.Shared.Tests/ResourceContextViewStateResolutionTests.cs` — pure resolution-algorithm tests (the logic that decides URL vs. tab-state vs. localStorage vs. All, extracted as a pure function so it's testable without a real `IJSRuntime`/`NavigationManager`).
- `tests/Apperture.Management.Ui.Client.Tests/ViewSelectorTests.cs` (or wherever this repo's bUnit component tests live — confirm the project name/convention by locating an existing bUnit test for `ThemeMenu.razor` or similar, if one exists; if no bUnit test project exists yet for this client, note that as a gap rather than inventing a new test project ad hoc).

**Modified files:**
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ControlPanelMenuDefinitions.cs` — add "Applications" after "Inventory".
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ManagementBreadcrumbResolver.cs` — add three `Match...` rules for `/config/applications*`.
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Services/TopBarMenuCoordinator.cs` — add `Ids.View`.
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/TopBar.razor` — insert `<ViewSelector />` before `<TopBarOrientation />`.

**Interfaces produced for later plans:**
- `IManagementClient.GetResourceContextsAsync(bool includeArchived, ct)`, `.GetResourceContextAsync(id, ct)`, `.CreateResourceContextAsync(request, ct)`, `.UpdateResourceContextAsync(id, request, ct)`, `.ArchiveResourceContextAsync(id, ct)`, `.UnarchiveResourceContextAsync(id, ct)`, `.GetResourceContextSummaryAsync(ct)` — consumed by Plans 4, 5, 6 wherever a page needs the context list or ownership summaries.
- `ResourceContextViewState` (scoped DI service): `ResourceContextViewScope Current { get; }`, `event Action? Changed`, `ValueTask InitializeAsync()`, `ValueTask SetExplicitAsync(ResourceContextViewScope scope)`, `IReadOnlyList<ResourceContextDto> KnownContexts { get; }`, `bool SelectorVisible { get; }` — consumed by every scoped/detail page in Plans 4-6.
- `<ViewSelector />` component — consumed only by `TopBar.razor` in this plan; later plans don't re-embed it, they only read `ResourceContextViewState`.

---

## Task 1: Client contract for `ResourceContextsController` (wraps Plan 2's API)

**Files:**
- Create: `src/Apperture.ControlPlane/Apperture.Management.Client/ResourceContextDtos.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs` (add six method signatures, near the other resource-family method groups)
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs` (implement them)
- Test: `tests/Apperture.UnitTests/ResourceContexts/ManagementClientResourceContextTests.cs`

**Interfaces:**
- Produces (client-side, mirroring Plan 2's controller DTOs exactly by name):
  ```csharp
  public sealed record ResourceContextDto(string Id, string Name, string Slug, string Kind, string State, string? Description, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
  public sealed record CreateResourceContextRequest(string Name, string? Slug, string? Description);
  public sealed record UpdateResourceContextRequest(string? Name, string? Slug, string? Description);
  public sealed record ResourceContextOwnershipSummaryDto(int? LiveIntegrations, int? RetiredIntegrations, int? ActiveConnections, int? RetiredConnections, int? EnabledSchedules, int? DisabledSchedules);
  ```
  And on `IManagementClient`:
  ```csharp
  Task<IReadOnlyList<ResourceContextDto>> GetResourceContextsAsync(bool includeArchived = false, CancellationToken cancellationToken = default);
  Task<ResourceContextDto?> GetResourceContextAsync(string id, CancellationToken cancellationToken = default);
  Task<ResourceContextDto> CreateResourceContextAsync(CreateResourceContextRequest request, CancellationToken cancellationToken = default);
  Task<ResourceContextDto> UpdateResourceContextAsync(string id, UpdateResourceContextRequest request, CancellationToken cancellationToken = default);
  Task<ResourceContextDto> ArchiveResourceContextAsync(string id, CancellationToken cancellationToken = default);
  Task<ResourceContextDto> UnarchiveResourceContextAsync(string id, CancellationToken cancellationToken = default);
  Task<IReadOnlyDictionary<string, ResourceContextOwnershipSummaryDto>> GetResourceContextSummaryAsync(CancellationToken cancellationToken = default);
  ```
  `ArchiveResourceContextAsync` must let a 409 response surface as a typed, catchable exception carrying the `ResourceContextOwnershipSummaryDto` body — follow whichever exception pattern `ManagementClient` already uses for other typed 409s (e.g. check how `EnsureSuccessOrThrowAsync` is used elsewhere and whether a `ManagementClientConflictException<T>`-style type already exists; if not, add one here rather than swallowing the body into a generic message string, since Plan 3's Task 6 (archive refusal UI) needs the structured counts).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task GetResourceContextsAsync_DefaultsToExcludingArchived()
{
    string? capturedUri = null;
    var client = ManagementClientTestFactory.Create(req => { capturedUri = req.RequestUri!.ToString(); return EmptyJsonArrayResponse(); });

    await client.GetResourceContextsAsync();

    capturedUri.Should().Contain("includeArchived=False").Or.NotContain("includeArchived=True");
}

[Fact]
public async Task ArchiveResourceContextAsync_On409_ThrowsWithOwnershipSummary()
{
    var client = ManagementClientTestFactory.Create(req => ConflictResponse(new
    {
        liveIntegrations = 2, retiredIntegrations = 0, activeConnections = 1, retiredConnections = 0, enabledSchedules = 0, disabledSchedules = 0
    }));

    var act = () => client.ArchiveResourceContextAsync("canvas-id");

    var ex = await act.Should().ThrowAsync<ManagementClientConflictException<ResourceContextOwnershipSummaryDto>>();
    ex.Which.Conflict.LiveIntegrations.Should().Be(2);
}
```
Adjust `ManagementClientTestFactory`/`EmptyJsonArrayResponse`/`ConflictResponse` to whatever test-double helper already exists in this test project for `ManagementClient` (created or confirmed in Plan 1 Task 4) — reuse it, do not duplicate.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ManagementClientResourceContextTests"`
Expected: FAIL — types/methods don't exist.

- [ ] **Step 3: Add the DTOs**

Create `ResourceContextDtos.cs` with the four records shown above, in the `Apperture.Management.Client` namespace, following the file-header/using conventions of `ScheduleDtos.cs`/`IntegrationFleetDtos.cs`.

- [ ] **Step 4: Add the interface methods and implementations**

Add the seven method signatures to `IManagementClient.cs` near the other resource-family groups (e.g. after the Schedule methods, before Control Center). Implement in `ManagementClient.cs`:
```csharp
public async Task<IReadOnlyList<ResourceContextDto>> GetResourceContextsAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
{
    var path = includeArchived ? "resource-contexts?includeArchived=true" : "resource-contexts";
    var result = await _http.GetFromJsonAsync<ResourceContextDto[]>(path, cancellationToken).ConfigureAwait(false);
    return result ?? [];
}

public async Task<ResourceContextDto?> GetResourceContextAsync(string id, CancellationToken cancellationToken = default)
{
    using var response = await _http.GetAsync($"resource-contexts/{Uri.EscapeDataString(id)}", cancellationToken).ConfigureAwait(false);
    if (response.StatusCode == HttpStatusCode.NotFound) return null;
    await EnsureSuccessOrThrowAsync(response, cancellationToken).ConfigureAwait(false);
    return await response.Content.ReadFromJsonAsync<ResourceContextDto>(cancellationToken: cancellationToken).ConfigureAwait(false);
}

public async Task<ResourceContextDto> CreateResourceContextAsync(CreateResourceContextRequest request, CancellationToken cancellationToken = default)
{
    using var response = await _http.PostAsJsonAsync("resource-contexts", request, cancellationToken).ConfigureAwait(false);
    await EnsureSuccessOrThrowAsync(response, cancellationToken).ConfigureAwait(false);
    return (await response.Content.ReadFromJsonAsync<ResourceContextDto>(cancellationToken: cancellationToken).ConfigureAwait(false))!;
}

public async Task<ResourceContextDto> UpdateResourceContextAsync(string id, UpdateResourceContextRequest request, CancellationToken cancellationToken = default)
{
    using var response = await _http.PatchAsJsonAsync($"resource-contexts/{Uri.EscapeDataString(id)}", request, cancellationToken).ConfigureAwait(false);
    await EnsureSuccessOrThrowAsync(response, cancellationToken).ConfigureAwait(false);
    return (await response.Content.ReadFromJsonAsync<ResourceContextDto>(cancellationToken: cancellationToken).ConfigureAwait(false))!;
}

public async Task<ResourceContextDto> ArchiveResourceContextAsync(string id, CancellationToken cancellationToken = default)
{
    using var response = await _http.PostAsync($"resource-contexts/{Uri.EscapeDataString(id)}/archive", null, cancellationToken).ConfigureAwait(false);
    if (response.StatusCode == HttpStatusCode.Conflict)
    {
        var summary = await response.Content.ReadFromJsonAsync<ResourceContextOwnershipSummaryDto>(cancellationToken: cancellationToken).ConfigureAwait(false);
        throw new ManagementClientConflictException<ResourceContextOwnershipSummaryDto>(summary!);
    }
    await EnsureSuccessOrThrowAsync(response, cancellationToken).ConfigureAwait(false);
    return (await response.Content.ReadFromJsonAsync<ResourceContextDto>(cancellationToken: cancellationToken).ConfigureAwait(false))!;
}

public async Task<ResourceContextDto> UnarchiveResourceContextAsync(string id, CancellationToken cancellationToken = default)
{
    using var response = await _http.PostAsync($"resource-contexts/{Uri.EscapeDataString(id)}/unarchive", null, cancellationToken).ConfigureAwait(false);
    await EnsureSuccessOrThrowAsync(response, cancellationToken).ConfigureAwait(false);
    return (await response.Content.ReadFromJsonAsync<ResourceContextDto>(cancellationToken: cancellationToken).ConfigureAwait(false))!;
}

public async Task<IReadOnlyDictionary<string, ResourceContextOwnershipSummaryDto>> GetResourceContextSummaryAsync(CancellationToken cancellationToken = default)
{
    var result = await _http.GetFromJsonAsync<Dictionary<string, ResourceContextOwnershipSummaryDto>>("resource-contexts/summary", cancellationToken).ConfigureAwait(false);
    return result ?? new Dictionary<string, ResourceContextOwnershipSummaryDto>();
}
```
If `ManagementClientConflictException<T>` does not exist yet anywhere in the client project, add it as a small new type in `ResourceContextDtos.cs` (or wherever other client-side exception types for this project already live):
```csharp
public sealed class ManagementClientConflictException<TConflict>(TConflict conflict) : Exception
{
    public TConflict Conflict { get; } = conflict;
}
```
Confirm `HttpClient.PatchAsJsonAsync` is available (it is a `System.Net.Http.Json` extension, present since .NET 5) — no package addition needed.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ManagementClientResourceContextTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Client/ResourceContextDtos.cs src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs tests/Apperture.UnitTests/ResourceContexts/ManagementClientResourceContextTests.cs
git commit -m "CTX-3A: add ResourceContexts client methods (list/get/create/update/archive/unarchive/summary)"
```

---

## Task 2: Config menu item and breadcrumbs for Applications

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ControlPanelMenuDefinitions.cs:59-147`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ManagementBreadcrumbResolver.cs`
- Test: locate and extend whatever test file already asserts the Config menu's item list (search `tests/Apperture.Management.Ui.Shared.Tests` for `ControlPanelMenuDefinitions`), and `tests/Apperture.Management.Ui.Shared.Tests/ManagementBreadcrumbResolverTests.cs`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void ConfigMenu_HasApplicationsDirectlyAfterInventory()
{
    var menus = ControlPanelMenuDefinitions.GetDefaultMenus();
    var configItems = menus[SectionOwnerConstants.Config].Items;

    var inventoryIndex = configItems.FindIndex(i => i.Text == "Inventory");
    var applicationsIndex = configItems.FindIndex(i => i.Text == "Applications");

    applicationsIndex.Should().Be(inventoryIndex + 1);
    configItems[applicationsIndex].Href.Should().Be("/config/applications");
    configItems[applicationsIndex].RequiredCapability.Should().BeNull();
}
```
```csharp
[Theory]
[InlineData(new[] { "config", "applications" }, new[] { "Config", "Applications" })]
[InlineData(new[] { "config", "applications", "new" }, new[] { "Config", "Applications", "New" })]
[InlineData(new[] { "config", "applications", "abc123" }, new[] { "Config", "Applications", "Application" })]
public void Breadcrumbs_ResolveForApplicationsRoutes(string[] segments, string[] expectedLabels)
{
    ManagementBreadcrumbResolver.Resolve(segments, out var crumbs).Should().BeTrue();
    crumbs!.Select(c => c.Label).Should().BeEquivalentTo(expectedLabels, o => o.WithStrictOrdering());
}
```
Match the exact static-method signature/return shape `ManagementBreadcrumbResolver` actually exposes (the research described an ordered array of `PathMatcher` delegates and a `Config(...)` helper, but the public entry point's exact name/signature must be confirmed by reading the file) before finalizing this test.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj --filter "FullyQualifiedName~ControlPanelMenuDefinitions|FullyQualifiedName~ManagementBreadcrumbResolver"`
Expected: FAIL on the two new tests.

- [ ] **Step 3: Add the menu item**

In `ControlPanelMenuDefinitions.cs`, in the `Config` domain's `Items` list, insert immediately after the `Inventory` entry:
```csharp
new DomainMenuItem { Text = "Applications", Href = "/config/applications" },
```

- [ ] **Step 4: Add the breadcrumb rules**

In `ManagementBreadcrumbResolver.cs`, add three new `Match...` methods mirroring the Governance Users trio (`MatchConfigGovernanceUsersList`/`...UserNew`/`...UserDetail`), inserted into the `Rules` array before the generic `MatchConfigLanding`/`MatchConfigInventory` entries:
```csharp
private static bool MatchConfigApplicationsList(string[] s, out IReadOnlyList<BreadcrumbItem>? crumbs)
{
    if (!Matches(s, "config", "applications")) { crumbs = null; return false; }
    crumbs = Config(("Applications", null));
    return true;
}

private static bool MatchConfigApplicationNew(string[] s, out IReadOnlyList<BreadcrumbItem>? crumbs)
{
    if (!Matches(s, "config", "applications", "new")) { crumbs = null; return false; }
    crumbs = Config(("Applications", "/config/applications"), ("New", null));
    return true;
}

private static bool MatchConfigApplicationDetail(string[] s, out IReadOnlyList<BreadcrumbItem>? crumbs)
{
    if (!Matches(s, "config", "applications", "{id}")) { crumbs = null; return false; }
    crumbs = Config(("Applications", "/config/applications"), ("Application", null));
    return true;
}
```
Confirm the exact `Matches(...)` helper signature (whether it takes a literal `"{id}"` wildcard token or expects a different placeholder convention, e.g. a predicate or `null`) by reading a couple of existing three-segment `Match...` methods in the file before writing these three.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj --filter "FullyQualifiedName~ControlPanelMenuDefinitions|FullyQualifiedName~ManagementBreadcrumbResolver"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ControlPanelMenuDefinitions.cs src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ManagementBreadcrumbResolver.cs
git commit -m "CTX-3A: add Config > Applications menu item and breadcrumbs (§6.1)"
```

---

## Task 3: Applications list page — Shared card + Active/Archived list (§6.3)

**Files:**
- Create: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Applications/ApplicationsList.razor`

**Interfaces:**
- Consumes: `IManagementClient.GetResourceContextsAsync`, `.GetResourceContextSummaryAsync` (Task 1); `IOperatorCapabilitiesService`/`OperatorCapabilitiesDto.CanManageResourceContexts` (Plan 2) to gate the `[New application]` button.

- [ ] **Step 1: Build the page**

Route `@page "/config/applications"`, `@layout ConfigLayout`, modeled on `Pages/Config/Governance/GovernanceLanding.razor`'s `ManagementSummaryCard`/`ui-summary-card-grid` pattern for the Shared card, and `Pages/Config/Inventory.razor`'s load/filter/`LoadingState`/`ErrorState`/`EmptyState` pattern for the Applications list beneath it:

```razor
@page "/config/applications"
@layout ConfigLayout
@inject IManagementClient Client
@inject IOperatorCapabilitiesService Capabilities
@inject NavigationManager Nav

<PageHeader Eyebrow="Config" Title="Applications" Subtitle="Group integrations, connections and schedules by the application they serve.">
    @if (_canManage)
    {
        <ActionButton DefaultText="New application" OnClickAsync="() => { Nav.NavigateTo(\"/config/applications/new\"); return Task.CompletedTask; }" />
    }
</PageHeader>

@if (_loading)
{
    <LoadingState />
}
else if (_error is not null)
{
    <ErrorState Message="@_error" />
}
else
{
    <ManagementSection Title="Shared">
        <ManagementSummaryCard
            Title="Shared"
            Description="Resources that aren't specific to one application. Integrations in any application can use Shared connections."
            Href="/config/applications/shared"
            Detail="@SharedCountsLabel()" />
    </ManagementSection>

    <ManagementSection Title="Applications">
        <div class="flex gap-2 mb-3">
            <button class="ui-tab @(!_showArchived ? "ui-tab--active" : "")" @onclick="() => SetShowArchived(false)">Active</button>
            <button class="ui-tab @(_showArchived ? "ui-tab--active" : "")" @onclick="() => SetShowArchived(true)">Archived</button>
        </div>

        @if (VisibleApplications.Count == 0)
        {
            <EmptyState Message="@(_showArchived ? "No archived applications." : "No applications yet.")" />
        }
        else
        {
            @foreach (var app in VisibleApplications)
            {
                <div class="ui-list-row">
                    <span>@app.Name</span>
                    <span class="text-muted text-sm">@CountsLabel(app.Id)</span>
                    @if (app.State == "Archived") { <StatusPill Label="Archived" /> }
                    <a href="@($"/config/applications/{app.Id}")" class="ui-link">Open</a>
                </div>
            }
        }
    </ManagementSection>
}

@code {
    private bool _loading = true;
    private string? _error;
    private bool _showArchived;
    private bool _canManage;
    private IReadOnlyList<ResourceContextDto> _allContexts = [];
    private IReadOnlyDictionary<string, ResourceContextOwnershipSummaryDto> _summaries = new Dictionary<string, ResourceContextOwnershipSummaryDto>();

    private IReadOnlyList<ResourceContextDto> VisibleApplications =>
        _allContexts.Where(c => c.Kind == "Application" && (_showArchived ? c.State == "Archived" : c.State != "Archived"))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    protected override async Task OnInitializedAsync()
    {
        var caps = await Capabilities.GetCapabilitiesAsync();
        _canManage = caps.CanManageResourceContexts;

        try
        {
            _allContexts = await Client.GetResourceContextsAsync(includeArchived: true);
            _summaries = await Client.GetResourceContextSummaryAsync();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private void SetShowArchived(bool value) => _showArchived = value;

    private string SharedCountsLabel()
    {
        var shared = _summaries.GetValueOrDefault("shared");
        return shared is null ? "—" : CountsLabelFromSummary(shared);
    }

    private string CountsLabel(string contextId)
    {
        var summary = _summaries.GetValueOrDefault(contextId);
        return summary is null ? "—" : CountsLabelFromSummary(summary);
    }

    private static string CountsLabelFromSummary(ResourceContextOwnershipSummaryDto s)
    {
        var parts = new List<string>();
        if (s.LiveIntegrations is { } i) parts.Add($"{i} integration{(i == 1 ? "" : "s")}");
        if (s.ActiveConnections is { } c) parts.Add($"{c} connection{(c == 1 ? "" : "s")}");
        if (s.EnabledSchedules is { } sc) parts.Add($"{sc} schedule{(sc == 1 ? "" : "s")}");
        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }
}
```
Verify `IOperatorCapabilitiesService`'s actual method name (`GetCapabilitiesAsync` is an assumption — confirm against how another Config page already calls it, e.g. `UserNew.razor`), and verify `ManagementSummaryCard`'s actual parameter names (`Title`/`Description`/`Href` confirmed by research; a `Detail`/count-label slot may not exist on that component today — check `Molecules/ManagementSummaryCard.razor` and either use its existing `ChildContent` slot for the counts line, or extend the component with a new optional parameter if none fits, in which case add that as a preceding sub-step here).

- [ ] **Step 2: Manual verification (no automated test for this step — Razor markup correctness is verified in Task 9's bUnit test and Task "Browser smoke" in the final CTX-3A plan)**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS (compiles).

- [ ] **Step 3: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Applications/ApplicationsList.razor
git commit -m "CTX-3A: add Config > Applications list page with Shared card (§6.3)"
```

---

## Task 4: Applications create page (§6.6)

**Files:**
- Create: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Applications/ApplicationNew.razor`

**Interfaces:**
- Consumes: `IManagementClient.CreateResourceContextAsync`; `ResourceContextSlug.TryNormalize`/`.IsReserved` (`Apperture.Core.Domain.ResourceContexts` — this is a **server-side** domain type; confirm whether it's referenceable from the WASM client project without pulling in server-only dependencies. If `Apperture.Core.Domain` is already referenced by the Blazor client project for other value types, reuse `ResourceContextSlug.TryNormalize` directly for the live preview. If not, write a small client-local slug-preview helper that mirrors its normalization rules for display purposes only — the **authoritative** validation still happens server-side on submit either way).
- Consumes: `Molecules/ManagementAdvancedSection.razor` for the Slug disclosure.

- [ ] **Step 1: Build the page**

Route `@page "/config/applications/new"`, `@layout ConfigLayout`, modeled on `Pages/Config/Governance/UserNew.razor`'s capability-gate → form → `ActionButton` → toast → `Nav.NavigateTo(detail)` pattern:

```razor
@page "/config/applications/new"
@layout ConfigLayout
@inject IManagementClient Client
@inject IOperatorCapabilitiesService Capabilities
@inject IToastService Toast
@inject NavigationManager Nav

<PageHeader Eyebrow="Config" Title="New application" Subtitle="Group integrations, connections and schedules under one application." />

@if (!_canManage)
{
    <ForbiddenState />
}
else
{
    <div class="ui-form-grid">
        <label class="block text-sm font-medium text-heading">Name (required)</label>
        <input class="ui-input mt-1 w-full" @bind="_name" @bind:event="oninput" />

        <label class="block text-sm font-medium text-heading mt-3">Description</label>
        <textarea class="ui-input mt-1 w-full" @bind="_description"></textarea>

        <ManagementAdvancedSection Title="Advanced" InitiallyOpen="false">
            <label class="block text-sm font-medium text-heading">Slug</label>
            <input class="ui-input mt-1 w-full font-mono text-sm" @bind="_slug" @bind:event="oninput" placeholder="@SlugPreview()" />
            <div class="mt-0.5 text-xs text-muted">Used only in future slug-addressed routes. Safe to leave as the derived value.</div>
        </ManagementAdvancedSection>

        <FormValidationFeedback Errors="_errors" />

        <ActionButton DefaultText="Create application" ActiveText="Creating…" SuccessText="Created" ErrorText="Failed" OnClickAsync="SubmitAsync" Disabled="string.IsNullOrWhiteSpace(_name)" />
    </div>
}

@code {
    private bool _canManage;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _slug = string.Empty;
    private IReadOnlyList<string> _errors = [];

    protected override async Task OnInitializedAsync()
    {
        var caps = await Capabilities.GetCapabilitiesAsync();
        _canManage = caps.CanManageResourceContexts;
    }

    private string SlugPreview() => ResourceContextSlug.TryNormalize(_name, out var normalized) ? normalized : string.Empty;

    private async Task SubmitAsync()
    {
        _errors = [];
        try
        {
            var effectiveSlug = string.IsNullOrWhiteSpace(_slug) ? SlugPreview() : _slug.Trim();
            var created = await Client.CreateResourceContextAsync(new CreateResourceContextRequest(
                _name.Trim(), string.IsNullOrWhiteSpace(effectiveSlug) ? null : effectiveSlug,
                string.IsNullOrWhiteSpace(_description) ? null : _description.Trim()));

            Toast.ShowSuccess($"Created '{created.Name}'.");
            Nav.NavigateTo($"/config/applications/{created.Id}");
        }
        catch (Exception ex)
        {
            _errors = [ex.Message];
            throw;
        }
    }
}
```
Confirm `ResourceContextSlug.TryNormalize`'s actual signature (`bool TryNormalize(string input, out string normalized)` is assumed — verify against the domain type in Plan 1's research, which cited `Normalize`/`TryNormalize` without the exact signature) and whether `Apperture.Core.Domain` is a valid project reference from `Apperture.Management.Ui.Client` before relying on it directly; if the client project cannot reference it (e.g. it pulls in EF Core or server-only dependencies transitively), extract just the normalization *algorithm* into a small duplicate helper inside `Apperture.Management.Ui.Shared` instead — do not add a heavyweight project reference purely for a cosmetic live-preview.

- [ ] **Step 2: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 3: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Applications/ApplicationNew.razor
git commit -m "CTX-3A: add Config > Applications create page with live slug preview under Advanced (§6.6)"
```

---

## Task 5: Applications detail page — edit/archive/unarchive, Shared read-only variant (§6.4)

**Files:**
- Create: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Applications/ApplicationDetail.razor`

**Interfaces:**
- Consumes: `IManagementClient.GetResourceContextAsync`, `.UpdateResourceContextAsync`, `.ArchiveResourceContextAsync` (catching `ManagementClientConflictException<ResourceContextOwnershipSummaryDto>`), `.UnarchiveResourceContextAsync`, `.GetResourceContextSummaryAsync`.

- [ ] **Step 1: Build the page**

Route `@page "/config/applications/{Id}"`, `@layout ConfigLayout`. Load the context by `Id`; if `Id == "shared"`, render the read-only variant (no edit/archive controls, explanatory copy, "Shared can't be renamed or archived."). For an Application, render Name/Description/Slug-under-Advanced as editable fields (each saved independently via `UpdateResourceContextAsync` with only the changed field non-null, matching `ResourceContextManagementService.UpdateAsync`'s partial-update contract from Plan 2), plus Archive/Unarchive buttons gated on `_canManage` and current `State`.

For the archive action, catch the structured conflict and render the exact refusal copy from design gate §6.5:
```razor
private async Task ArchiveAsync()
{
    try
    {
        _context = await Client.ArchiveResourceContextAsync(Id);
        Toast.ShowSuccess($"Archived '{_context.Name}'.");
    }
    catch (ManagementClientConflictException<ResourceContextOwnershipSummaryDto> ex)
    {
        _archiveBlocker = ex.Conflict;
    }
}
```
```razor
@if (_archiveBlocker is not null)
{
    <PageMessage Kind="PageMessageKind.Warning" Title="@($"{_context!.Name} can't be archived yet.")">
        It still has @DescribeBlocker(_archiveBlocker). Retire or disable them first.
        <ChildContent>
            @if (_archiveBlocker.LiveIntegrations > 0) { <a href="@($"/integrations?context={Id}")" class="ui-link">View active integrations</a> }
            @if (_archiveBlocker.ActiveConnections > 0) { <a href="@($"/connections?context={Id}")" class="ui-link">View connections</a> }
            @if (_archiveBlocker.EnabledSchedules > 0) { <a href="@($"/schedules?context={Id}")" class="ui-link">View schedules</a> }
        </ChildContent>
    </PageMessage>
}
```
Write `DescribeBlocker` to render "2 active integrations, 1 active connection and 3 enabled schedules" style text from the non-null, non-zero fields — mirror the exact phrasing style from design gate §6.5's example copy block.

Also include, for an active (non-archived, non-Shared) Application, the two §6.6 post-create actions when reached from elsewhere (they're primarily surfaced right after create, but the detail page should still offer them at any time): "Create an integration in {Name}" (`/integrations/new?context={Id}`) and "Switch view to {Name}" (calls `ResourceContextViewState.SetExplicitAsync(ResourceContextViewScope.Of(Id))` — this wires to Task 6's service, so this exact button wiring should be added or revisited once Task 6 exists; if Task 6 isn't done yet when this task is executed, stub the button as disabled with a `TODO` comment is **not allowed** per the no-placeholders rule — instead, sequence this task after Task 6, or land the button in a follow-up commit within this same task once Task 6 lands. Recommended: do Task 6 before finishing this task's "Switch view" button, and note that ordering dependency explicitly if executing tasks out of the listed order.

- [ ] **Step 2: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 3: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Applications/ApplicationDetail.razor
git commit -m "CTX-3A: add Config > Applications detail page with archive refusal UX and Shared read-only variant (§6.4, §6.5)"
```

---

## Task 6: `ResourceContextViewState` — the resolution algorithm as a pure, testable function (§4.2)

**Files:**
- Create: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Services/ResourceContextViewState.cs`
- Create: `tests/Apperture.Management.Ui.Shared.Tests/ResourceContextViewStateResolutionTests.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/wwwroot/js/apperture-shell.js` — add `getViewPreference(userId)`/`setViewPreference(userId, value)`, mirroring the existing `getPreference`/`setPreference` theme functions exactly but keyed by `apperture.view.{userId}` instead of `apperture.theme`.

**Interfaces:**
- Produces a **pure** resolution function, extracted so it's unit-testable without a real `IJSRuntime`/`NavigationManager`:
  ```csharp
  namespace Apperture.Management.Ui.Client.Services;

  public enum ResourceContextViewResolutionAction { UseAsIs, RewriteUrl }

  public readonly record struct ResourceContextViewResolution(
      ResourceContextViewScope Scope,
      ResourceContextViewResolutionAction Action,
      bool WriteLocalStorage,
      string? Notice);

  public static class ResourceContextViewResolver
  {
      public static ResourceContextViewResolution Resolve(
          string? urlQueryValue,
          ResourceContextViewScope? tabState,
          string? localStorageValue,
          IReadOnlyCollection<string> activeContextIds,
          IReadOnlyCollection<string> archivedContextIds);
  }
  ```
  This encodes design gate §4.2's four-branch algorithm plus §4.3's archived/unknown-id handling, entirely as pure data in/data out — the stateful `ResourceContextViewState` class (Step 4 below) is a thin wrapper that calls this function and then performs the actual `NavigationManager`/`IJSRuntime`/event-raising side effects.

- [ ] **Step 1: Write the failing resolution tests**

```csharp
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public class ResourceContextViewStateResolutionTests
{
    private static readonly string[] Active = ["canvas-id", "admissions-id"];
    private static readonly string[] Archived = ["retired-id"];

    [Fact]
    public void UrlSaysAll_UsesAllAndDoesNotWriteLocalStorage()
    {
        var r = ResourceContextViewResolver.Resolve("all", tabState: null, localStorageValue: null, Active, Archived);

        r.Scope.IsAll.Should().BeTrue();
        r.Action.Should().Be(ResourceContextViewResolutionAction.UseAsIs);
        r.WriteLocalStorage.Should().BeFalse();
    }

    [Fact]
    public void UrlNamesAnActiveId_UsesItAndDoesNotWriteLocalStorage()
    {
        var r = ResourceContextViewResolver.Resolve("canvas-id", tabState: null, localStorageValue: null, Active, Archived);

        r.Scope.ResourceContextId.Should().Be("canvas-id");
        r.WriteLocalStorage.Should().BeFalse();
    }

    [Fact]
    public void UrlSilent_TabStateSet_RewritesUrlToTabState()
    {
        var r = ResourceContextViewResolver.Resolve(null, tabState: ResourceContextViewScope.Of("canvas-id"), localStorageValue: null, Active, Archived);

        r.Scope.ResourceContextId.Should().Be("canvas-id");
        r.Action.Should().Be(ResourceContextViewResolutionAction.RewriteUrl);
        r.WriteLocalStorage.Should().BeFalse();
    }

    [Fact]
    public void UrlSilent_TabStateUnset_ReadsValidLocalStorage()
    {
        var r = ResourceContextViewResolver.Resolve(null, tabState: null, localStorageValue: "canvas-id", Active, Archived);

        r.Scope.ResourceContextId.Should().Be("canvas-id");
        r.Action.Should().Be(ResourceContextViewResolutionAction.RewriteUrl);
    }

    [Fact]
    public void NothingValidAnywhere_DefaultsToAll()
    {
        var r = ResourceContextViewResolver.Resolve(null, tabState: null, localStorageValue: null, Active, Archived);

        r.Scope.IsAll.Should().BeTrue();
        r.Action.Should().Be(ResourceContextViewResolutionAction.RewriteUrl);
    }

    [Fact]
    public void RememberedLocalStorage_NamesAnArchivedContext_FallsBackToAllWithNotice()
    {
        var r = ResourceContextViewResolver.Resolve(null, tabState: null, localStorageValue: "retired-id", Active, Archived);

        r.Scope.IsAll.Should().BeTrue();
        r.Notice.Should().Contain("archived");
        r.WriteLocalStorage.Should().BeTrue("the stored value must be rewritten to 'all'");
    }

    [Fact]
    public void ExplicitUrl_NamesAnArchivedContext_IsHonouredWithBannerNotice()
    {
        var r = ResourceContextViewResolver.Resolve("retired-id", tabState: null, localStorageValue: null, Active, Archived);

        r.Scope.ResourceContextId.Should().Be("retired-id");
        r.Notice.Should().Contain("archived");
        r.WriteLocalStorage.Should().BeFalse();
    }

    [Fact]
    public void ExplicitUrl_NamesAnUnknownId_ReturnsNotFoundNotice()
    {
        var r = ResourceContextViewResolver.Resolve("does-not-exist", tabState: null, localStorageValue: null, Active, Archived);

        r.Notice.Should().Contain("doesn't exist");
    }

    [Fact]
    public void RememberedLocalStorage_NamesAnUnknownId_FallsBackToAllSilently()
    {
        var r = ResourceContextViewResolver.Resolve(null, tabState: null, localStorageValue: "does-not-exist", Active, Archived);

        r.Scope.IsAll.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj --filter "FullyQualifiedName~ResourceContextViewStateResolutionTests"`
Expected: FAIL — type doesn't exist. (Note: since `ResourceContextViewResolver` lives in `Apperture.Management.Ui.Client.Services` per its interface sketch above but the test sits in `Apperture.Management.Ui.Shared.Tests`, either move the resolver into `Apperture.Management.Ui.Shared` instead — the more consistent choice, since that project is specifically for pure/unit-testable logic per Plan 1's Task 6/7 precedent — or create/confirm a `Apperture.Management.Ui.Client.Tests` project for this test. **Recommended: put `ResourceContextViewResolver` in `Apperture.Management.Ui.Shared`** alongside `ResourceContextViewScope`/`ResourceContextViewRoutes`, and have `ResourceContextViewState` (the stateful wrapper, in the Client project) call into it. Adjust the namespace in both the production code and the test file accordingly before proceeding.)

- [ ] **Step 3: Implement `ResourceContextViewResolver`**

```csharp
namespace Apperture.Management.Ui.Shared;

public enum ResourceContextViewResolutionAction { UseAsIs, RewriteUrl }

public readonly record struct ResourceContextViewResolution(
    ResourceContextViewScope Scope,
    ResourceContextViewResolutionAction Action,
    bool WriteLocalStorage,
    string? Notice);

public static class ResourceContextViewResolver
{
    public static ResourceContextViewResolution Resolve(
        string? urlQueryValue,
        ResourceContextViewScope? tabState,
        string? localStorageValue,
        IReadOnlyCollection<string> activeContextIds,
        IReadOnlyCollection<string> archivedContextIds)
    {
        var fromUrl = ResourceContextViewScope.Parse(urlQueryValue);
        if (fromUrl is { } urlScope)
        {
            if (urlScope.IsAll) return new(urlScope, ResourceContextViewResolutionAction.UseAsIs, false, null);

            if (activeContextIds.Contains(urlScope.ResourceContextId!))
                return new(urlScope, ResourceContextViewResolutionAction.UseAsIs, false, null);

            if (archivedContextIds.Contains(urlScope.ResourceContextId!))
                return new(urlScope, ResourceContextViewResolutionAction.UseAsIs, false, $"This application is archived.");

            return new(ResourceContextViewScope.All, ResourceContextViewResolutionAction.UseAsIs, false, "That application doesn't exist.");
        }

        if (tabState is { } tab)
            return new(tab, ResourceContextViewResolutionAction.RewriteUrl, false, null);

        var fromStorage = ResourceContextViewScope.Parse(localStorageValue);
        if (fromStorage is { } storedScope)
        {
            if (storedScope.IsAll || activeContextIds.Contains(storedScope.ResourceContextId!))
                return new(storedScope, ResourceContextViewResolutionAction.RewriteUrl, false, null);

            if (archivedContextIds.Contains(storedScope.ResourceContextId!))
                return new(ResourceContextViewScope.All, ResourceContextViewResolutionAction.RewriteUrl, true, "This application is archived. Showing all applications.");

            return new(ResourceContextViewScope.All, ResourceContextViewResolutionAction.RewriteUrl, false, null);
        }

        return new(ResourceContextViewScope.All, ResourceContextViewResolutionAction.RewriteUrl, false, null);
    }
}
```
Note the branch differences carefully: an **explicit URL** naming an archived context is honoured (not replaced) with a banner and does not touch localStorage; a **remembered** (tab-state-absent, localStorage-sourced) archived context falls back to All and *rewrites* localStorage to `all` (per design gate §4.3's exact row: "Rewrite the stored value to `all`."). An unknown id from **localStorage** falls back to All silently (no notice) since it's not information the operator explicitly asked for — only the explicit-URL unknown case gets the "doesn't exist" notice, matching the design gate's distinct rows for these two cases.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj --filter "FullyQualifiedName~ResourceContextViewStateResolutionTests"`
Expected: PASS

- [ ] **Step 5: Add the JS interop functions**

In `apperture-shell.js`, add alongside the existing `getPreference`/`setPreference`:
```javascript
window.appertureShell.getViewPreference = function (userId) {
    try { return localStorage.getItem('apperture.view.' + userId); } catch { return null; }
};
window.appertureShell.setViewPreference = function (userId, value) {
    try { localStorage.setItem('apperture.view.' + userId, value); } catch { /* ignore */ }
};
```

- [ ] **Step 6: Implement the stateful `ResourceContextViewState` wrapper**

```csharp
namespace Apperture.Management.Ui.Client.Services;

public sealed class ResourceContextViewState(
    IJSRuntime js,
    NavigationManager nav,
    IManagementClient client,
    ISessionStateService session /* or however the current userId is obtained — confirm against AppearancePreferenceCoordinator's pattern */
) : IAsyncDisposable
{
    private const string QueryParamName = "context";
    private ResourceContextViewScope? _tabState;
    private IReadOnlyList<ResourceContextDto> _knownContexts = [];

    public ResourceContextViewScope Current { get; private set; } = ResourceContextViewScope.All;
    public string? Notice { get; private set; }
    public event Action? Changed;

    public IReadOnlyList<ResourceContextDto> KnownActiveApplications =>
        _knownContexts.Where(c => c.Kind == "Application" && c.State != "Archived")
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public bool SelectorVisible => KnownActiveApplications.Count > 0;

    public async ValueTask InitializeAsync()
    {
        _knownContexts = await client.GetResourceContextsAsync(includeArchived: true);
        await ResolveForCurrentPathAsync();
        nav.LocationChanged += OnLocationChanged;
    }

    private async void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        if (ResourceContextViewRoutes.Resolve(new Uri(e.Location).AbsolutePath) != ResourceContextViewMode.Scoped)
            return;

        await ResolveForCurrentPathAsync();
        Changed?.Invoke();
    }

    private async Task ResolveForCurrentPathAsync()
    {
        var uri = new Uri(nav.Uri);
        var query = QueryHelpers.ParseQuery(uri.Query);
        var urlValue = query.TryGetValue(QueryParamName, out var v) ? v.ToString() : null;
        var userId = session.CurrentUserId; // confirm actual property name
        var storedValue = await js.InvokeAsync<string?>("appertureShell.getViewPreference", userId);

        var activeIds = _knownContexts.Where(c => c.State != "Archived").Select(c => c.Id).ToList();
        var archivedIds = _knownContexts.Where(c => c.State == "Archived").Select(c => c.Id).ToList();

        var resolution = ResourceContextViewResolver.Resolve(urlValue, _tabState, storedValue, activeIds, archivedIds);

        Current = resolution.Scope;
        Notice = resolution.Notice;
        _tabState = resolution.Scope;

        if (resolution.WriteLocalStorage)
            await js.InvokeVoidAsync("appertureShell.setViewPreference", userId, resolution.Scope.ToQueryValue());

        if (resolution.Action == ResourceContextViewResolutionAction.RewriteUrl)
        {
            var newUri = QueryHelpers.AddQueryString(uri.GetLeftPart(UriPartial.Path), QueryParamName, resolution.Scope.ToQueryValue());
            nav.NavigateTo(newUri, replace: true);
        }
    }

    public async ValueTask SetExplicitAsync(ResourceContextViewScope scope)
    {
        _tabState = scope;
        Current = scope;
        Notice = null;
        var userId = session.CurrentUserId;
        await js.InvokeVoidAsync("appertureShell.setViewPreference", userId, scope.ToQueryValue());

        if (ResourceContextViewRoutes.Resolve(new Uri(nav.Uri).AbsolutePath) == ResourceContextViewMode.Scoped)
        {
            var uri = new Uri(nav.Uri);
            var newUri = QueryHelpers.AddQueryString(uri.GetLeftPart(UriPartial.Path), QueryParamName, scope.ToQueryValue());
            nav.NavigateTo(newUri, replace: false); // an explicit selector change is a real navigation, not a silent replace
        }

        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        nav.LocationChanged -= OnLocationChanged;
        await ValueTask.CompletedTask;
    }
}
```
This sketch has several points that must be confirmed against the real codebase before finalizing, all flagged inline: (a) `ISessionStateService`/`CurrentUserId` — confirm the actual service/property used by `AppearancePreferenceCoordinator` for identifying the current user, and use that exact type; (b) `Microsoft.AspNetCore.WebUtilities.QueryHelpers` — research found **no existing usage** of this class anywhere in the codebase (query strings are hand-built with `Uri.EscapeDataString` everywhere else); either introduce it here (it's a standard ASP.NET Core package, likely already transitively available) or hand-roll the query-string read/write to match house style — prefer matching house style (hand-rolled) for consistency unless `QueryHelpers` turns out to already be in use somewhere not surfaced by the research pass; (c) whether `NavigateTo(..., replace: false)` on an explicit selector change is correct per design gate §4.2's "Explicit selector changes do write localStorage" combined with normal Blazor navigation semantics — a `replace: false` here means the operator's previous page becomes a back-button target, which seems correct (switching the view is a real, back-button-undoable action), but re-verify against §4.3's Q1 ("persists across ordinary navigation... Rail/menu hrefs stay static") to be sure this doesn't conflict.

- [ ] **Step 7: Register `ResourceContextViewState` in DI**

Add to the WASM client's `Program.cs` (or wherever `ThemeService`/`AppearancePreferenceCoordinator` are registered):
```csharp
builder.Services.AddScoped<ResourceContextViewState>();
```

- [ ] **Step 8: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ResourceContextViewResolver.cs tests/Apperture.Management.Ui.Shared.Tests/ResourceContextViewStateResolutionTests.cs src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Services/ResourceContextViewState.cs src/Apperture.ControlPlane/Apperture.Management.Ui.Client/wwwroot/js/apperture-shell.js
git commit -m "CTX-3A: add ResourceContextViewState with pure resolution algorithm (§4.2, §4.3)"
```

---

## Task 7: `ViewSelector` component and `TopBar` wiring (§3.1–§3.3)

**Files:**
- Create: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Molecules/ViewSelector.razor`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Services/TopBarMenuCoordinator.cs` — add `Ids.View`.
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/TopBar.razor:19` — insert `<ViewSelector />`.

**Interfaces:**
- Consumes: `ResourceContextViewState` (Task 6), `TopBarMenuCoordinator` (existing, `ThemeMenu.razor`'s pattern), `SearchableList.razor` (existing, reused for the >7-Applications case).

- [ ] **Step 1: Add `Ids.View` to `TopBarMenuCoordinator`**

Add a fourth constant alongside `Theme`, `Notifications`, `Account`:
```csharp
public const string View = "view";
```
(exact casing/placement to match the other three constants' declaration style in that file).

- [ ] **Step 2: Build `ViewSelector.razor`**

Modeled directly on `ThemeMenu.razor`'s trigger/panel structure, changed to `role="listbox"`/`aria-haspopup="listbox"` per design gate §3.2, with sections for All/Shared/Applications and a conditional `SearchableList` above 7 active Applications:

```razor
@inject ResourceContextViewState ViewState
@inject TopBarMenuCoordinator Menus

@if (ViewState.SelectorVisible)
{
    <div class="ui-view-selector relative">
        <button
            aria-haspopup="listbox"
            aria-expanded="@IsOpen"
            aria-label="@($"View: {CurrentLabel()}")"
            title="Choose which application's resources lists show. This organizes pages; it doesn't change what you have access to."
            @onclick="Toggle">
            View: @CurrentLabel()
        </button>

        @if (IsOpen)
        {
            <div role="listbox" aria-label="View" @onkeydown="OnKeyDown">
                <div role="option" aria-selected="@ViewState.Current.IsAll" @onclick="() => SelectAsync(ResourceContextViewScope.All)">
                    All <span class="text-muted text-xs">Everything you can see</span>
                </div>
                <div role="option" aria-selected="@(!ViewState.Current.IsAll && ViewState.Current.ResourceContextId == "shared")" @onclick='() => SelectAsync(ResourceContextViewScope.Of("shared"))'>
                    Shared <span class="text-muted text-xs">Not specific to one application</span>
                </div>
                <hr />
                @if (ActiveApplications.Count > 7)
                {
                    <SearchableList Items="ActiveApplications" Filter="(a, q) => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)"
                                    Render="a => a.Name" OnSelect="a => SelectAsync(ResourceContextViewScope.Of(a.Id))" />
                }
                else
                {
                    @foreach (var app in ActiveApplications)
                    {
                        <div role="option" aria-selected="@(ViewState.Current.ResourceContextId == app.Id)" @onclick="() => SelectAsync(ResourceContextViewScope.Of(app.Id))">
                            @app.Name
                        </div>
                    }
                }
                <hr />
                <a href="/config/applications" role="option">Manage applications</a>
            </div>
        }
    </div>
}

@code {
    private bool IsOpen => Menus.IsOpen(TopBarMenuCoordinator.Ids.View);
    private IReadOnlyList<ResourceContextDto> ActiveApplications => ViewState.KnownActiveApplications;

    private void Toggle() => Menus.Toggle(TopBarMenuCoordinator.Ids.View);

    private string CurrentLabel()
    {
        if (ViewState.Current.IsAll) return "All";
        if (ViewState.Current.ResourceContextId == "shared") return "Shared";
        var app = ActiveApplications.FirstOrDefault(a => a.Id == ViewState.Current.ResourceContextId);
        return app?.Name ?? $"{ViewState.Current.ResourceContextId} (archived)";
    }

    private async Task SelectAsync(ResourceContextViewScope scope)
    {
        Menus.Close(TopBarMenuCoordinator.Ids.View);
        await ViewState.SetExplicitAsync(scope);
    }

    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape") Menus.Close(TopBarMenuCoordinator.Ids.View);
        // Arrow/Home/End navigation: mirror ThemeMenu.razor's existing keyboard handling exactly if it implements this already;
        // if ThemeMenu delegates keyboard nav to a shared helper, reuse that helper here rather than reimplementing it.
    }
}
```
Read `ThemeMenu.razor` in full before finalizing this component — copy its exact open/close/keyboard-handling wiring (including whatever `TopBarMenuCoordinator.Changed` subscription pattern it uses to re-render on state change) rather than approximating it; the sketch above is structural, not a byte-for-byte copy.

- [ ] **Step 3: Insert into `TopBar.razor`**

At line 19 (inside `.ui-topbar__orientation`, before `<TopBarOrientation />`):
```razor
<ViewSelector />
<TopBarOrientation />
```
Confirm this placement reads correctly against the "leading edge of the orientation area" requirement — if the orientation div's flex layout puts `TopBarOrientation` first by default, an explicit `order` CSS or a wrapping `<div class="flex items-center gap-3">` around both may be needed; verify visually once the dev server is running (this is exactly the kind of check the final CTX-3A plan's mandatory browser smoke covers).

- [ ] **Step 4: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 5: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Molecules/ViewSelector.razor src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Services/TopBarMenuCoordinator.cs src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/TopBar.razor
git commit -m "CTX-3A: add the View selector to the top bar (§3.1-§3.3)"
```

---

## Task 8: One-time selector hint (§3.3)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Molecules/ViewSelector.razor`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/wwwroot/js/apperture-shell.js`

**Interfaces:**
- Produces a dismissible hint anchored to the selector, shown once when it first becomes visible, dismissal stored in localStorage under a new key (e.g. `apperture.view.hintDismissed`, instance-wide, not user-keyed, since the hint is about UI discovery, not a per-operator preference — this is a reasonable default; if a reviewer wants it user-keyed instead, that's a one-line key change).

- [ ] **Step 1: Add the hint markup and dismissal wiring**

```razor
@if (ViewState.SelectorVisible && !_hintDismissed)
{
    <div class="ui-hint-bubble" role="status">
        Use View to focus lists on an application.
        <button @onclick="DismissHintAsync" aria-label="Dismiss">×</button>
    </div>
}
```
```csharp
private bool _hintDismissed = true;

protected override async Task OnInitializedAsync()
{
    _hintDismissed = await Js.InvokeAsync<bool>("appertureShell.isViewHintDismissed");
}

private async Task DismissHintAsync()
{
    _hintDismissed = true;
    await Js.InvokeVoidAsync("appertureShell.dismissViewHint");
}
```
Add to `apperture-shell.js`:
```javascript
window.appertureShell.isViewHintDismissed = function () {
    try { return localStorage.getItem('apperture.view.hintDismissed') === 'true'; } catch { return true; }
};
window.appertureShell.dismissViewHint = function () {
    try { localStorage.setItem('apperture.view.hintDismissed', 'true'); } catch { /* ignore */ }
};
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 3: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Molecules/ViewSelector.razor src/Apperture.ControlPlane/Apperture.Management.Ui.Client/wwwroot/js/apperture-shell.js
git commit -m "CTX-3A: add one-time dismissible hint for the View selector (§3.3)"
```

---

## Task 9: bUnit component tests for `ViewSelector` visibility (§14 "Selector visibility" criteria)

**Files:**
- Create: the bUnit test file at whatever path/project this repo uses for Razor component tests (confirm existence of a bUnit test project before writing this task's tests; if none exists, note this explicitly in the plan's closure notes as a gap rather than introducing a new test project mid-plan without the user's sign-off — creating a new test project is a bigger decision than this plan should make unilaterally).

**Interfaces:**
- None new — this task only verifies Task 7's component against the acceptance criteria enumerated in design gate §14 "Selector visibility": no Application → hidden; first active Application → visible; archived-only → hidden; `ForbiddenUnlinked` → hidden.

- [ ] **Step 1: Confirm whether a bUnit test project exists**

Run: `grep -r "bunit" --include=*.csproj -il src/ tests/` (or equivalent `Glob`/`Grep` tool call) to check for an existing bUnit package reference.

- [ ] **Step 2a: If a bUnit project exists, write the tests**

```csharp
[Fact]
public void NoActiveApplications_SelectorIsHidden()
{
    using var ctx = new TestContext();
    ctx.Services.AddSingleton(FakeViewState.WithNoApplications());
    var cut = ctx.RenderComponent<ViewSelector>();

    cut.FindAll(".ui-view-selector").Should().BeEmpty();
}

[Fact]
public void OneActiveApplication_SelectorIsVisible()
{
    using var ctx = new TestContext();
    ctx.Services.AddSingleton(FakeViewState.WithOneActiveApplication("Canvas"));
    var cut = ctx.RenderComponent<ViewSelector>();

    cut.Find(".ui-view-selector").Should().NotBeNull();
}

[Fact]
public void OnlyArchivedApplications_SelectorIsHidden()
{
    using var ctx = new TestContext();
    ctx.Services.AddSingleton(FakeViewState.WithOnlyArchivedApplications());
    var cut = ctx.RenderComponent<ViewSelector>();

    cut.FindAll(".ui-view-selector").Should().BeEmpty();
}
```
`FakeViewState` is new test scaffolding needed for this task — since `ResourceContextViewState` depends on `IJSRuntime`/`NavigationManager`/`IManagementClient`, either make its `SelectorVisible`/`KnownActiveApplications`/`Current` members virtual and subclass for the fake, or extract a small `IResourceContextViewState` interface for testability (recommended: extract the interface now, since Plans 4-6 will also need to fake this service in their own component tests).

- [ ] **Step 2b: If no bUnit project exists, skip this task and record the gap**

Add a note to this plan's final self-review (and to the CTX-3A closure document in Plan 6) that `ViewSelector`'s visibility rules are verified only by the manual browser-smoke checklist, not by an automated component test, and that introducing bUnit is a follow-up worth proposing separately.

- [ ] **Step 3: Commit (only if Step 2a executed)**

```bash
git add <bUnit test file path>
git commit -m "CTX-3A: add ViewSelector visibility tests (§14 Selector visibility criteria)"
```

---

## Self-Review Notes

- **Spec coverage:** §6.1-§6.6 (Tasks 2-5), §3.1-§3.3 (Tasks 7-8), §4.1-§4.4 resolution algorithm (Task 6) are all covered. §3.4's route-mode participation and §3.5's "load context list once per tab" are respected by `ResourceContextViewState.InitializeAsync` loading the list exactly once and `OnLocationChanged` only re-resolving (not re-fetching) on scoped-route navigation.
- **Type consistency:** `ResourceContextViewResolver.Resolve`'s parameter/return shape (Task 6) is used identically by `ResourceContextViewState` (same task) — no drift introduced. `ResourceContextDto`/`ResourceContextOwnershipSummaryDto` (Task 1) are reused verbatim across Tasks 3-7, never redefined.
- **No placeholders:** every flagged uncertainty (exact `ManagementSummaryCard` parameters, `ResourceContextSlug` project-reference feasibility, `ISessionStateService`'s real shape, whether `QueryHelpers` or hand-rolled query parsing fits house style, bUnit project existence) is called out as "confirm by reading X before finalizing," not silently assumed — several tasks explicitly sequence "read the real file first" as their first action.
