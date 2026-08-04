# UIX-2 — Breadcrumb and shell rendering stabilization

**Status:** Complete  
**Date:** 2026-08-04  
**Related:** [Epic 101](../02-decisions/Epic101-Top-Bar-Breadcrumbs-and-Parent-Context-Links.md), [pre-release product roadmap](../00-overview/pre-release-product-roadmap.md) Track 1 UIX arc

## Executive summary

Ordinary route-derived breadcrumbs in the Management UI top bar were unreliable: clicks often failed and Blazor WebAssembly logged renderer exceptions (`Symbol()`, `parentNode`, then cascading “No element is currently associated with component”). UIX-2 restores plain URL-driven breadcrumb anchors, stops force-remounting the orientation subtree on every navigation, hardens shell lifecycle disposal, and documents a minimal resolver overload for later drill-through context — without introducing breadcrumb history or navigation-state stores.

## Original symptoms

- Breadcrumb links only rarely navigated successfully.
- Console on initial load and after breadcrumb clicks:

  - `TypeError: Cannot read properties of undefined (reading 'Symbol()')`
  - `TypeError: Cannot read properties of undefined (reading 'parentNode')`
  - Repeated `Error: No element is currently associated with component N`

- Cascading association errors followed the first renderer failure (secondary).

## Confirmed root cause

**Primary:** Epic 101’s corrective navigation pattern on `ManagementBreadcrumbs` — `@onclick` + `@onclick:preventDefault` + `NavigationManager.NavigateTo` on parent `<a href>` elements — combined with `TopBar` remounting `TopBarOrientation` via `@key="@_orientationKey"` on every path change.

Sequence:

1. Operator clicks a breadcrumb `<a>`.
2. Blazor intercepts the click (`preventDefault`), calls `NavigateTo`.
3. `LocationChanged` clears shell page context and updates `_orientationKey`.
4. `@key` destroys and recreates the orientation/breadcrumb subtree while the click/navigation event is still resolving.
5. The renderer and browser DOM fall out of sync → `Symbol()` / `parentNode` → orphaned component IDs.

**Contributing:** `ShellOrientation.ResetPageContext()` raised `Changed` during `LocationChanged`, scheduling an extra `StateHasChanged` alongside the location handler’s own re-render.

Breadcrumb resolution itself (static `ManagementBreadcrumbResolver`) was not the failure source.

## Why the renderer and DOM became unsynchronized

Blazor owns the DOM for the top-bar orientation region. Destroying that subtree mid-event (keyed remount) while also suppressing native anchor navigation left the framework tracking components whose DOM nodes were already gone or half-updated. Later updates then reported missing element associations.

## Files and components involved

| Area | Path |
|------|------|
| Shell layout | `MainLayout.razor` |
| Top bar | `Organisms/TopBar.razor` |
| Orientation | `Molecules/TopBarOrientation.razor` |
| Breadcrumbs | `Molecules/ManagementBreadcrumbs.razor` |
| Resolver | `ManagementBreadcrumbResolver.cs` |
| Models | `BreadcrumbItem.cs`, `ManagementBreadcrumbNavigationContext.cs` |
| Page title publish | `ShellOrientationService.cs`, `PageHeader.razor` |
| Schedule plane JS | `wwwroot/js/control-panel-resize.js` (panel width only; not breadcrumb DOM) |

## Implementation changes

1. **`ManagementBreadcrumbs`** — Plain `<a href>` for linked parents; no `NavigationManager`, no `@onclick` / `preventDefault`. `@key="item.StableKey"`. `aria-current="page"` on current text segments. Uses `BreadcrumbItem.IsLinked`.
2. **`TopBar`** — Removed `@key` remount of `TopBarOrientation`. Safer disposed guards on `LocationChanged` / `Changed` callbacks with unsubscribe on dispose.
3. **`ShellOrientationService`** — `ResetPageContext` clears silently so route change does one coordinated re-render; `SetPageContext` still raises `Changed` and skips no-op publishes.
4. **`ManagementBreadcrumbResolver`** — Strips query/fragment (and absolute URIs) in `SplitPath`. Overloads `Resolve` / `ResolveOrientation` accept optional `ManagementBreadcrumbNavigationContext` (ignored in UIX-2; seam for UIX-3/4).
5. **`BreadcrumbItem`** — `IsLinked`, `StableKey` helpers.
6. **`EntityShellTitles.NormalizePath`** — Strips query/fragment for shell matching.

## Alternatives considered

| Alternative | Why not |
|-------------|---------|
| Keep `NavigateTo` but drop `@key` | Still fights the router; epic requires no JS dependency for normal breadcrumb nav |
| Keep `@key` but drop click intercept | Remount still unnecessary and riskier on fast nav |
| Breadcrumb history service | Explicitly excluded; trails must stay URL-derived |
| Full contextual crumb framework | Deferred to UIX-3/4 |

## Investigation checklist results

| Check | Result |
|-------|--------|
| Manual `RenderTreeBuilder` / custom `BuildRenderTree` on shell path | Not present on breadcrumb path |
| JS mutating top-bar / breadcrumb DOM | Ruled out — resize script only mutates panel CSS var / temporary measure node |
| Framework package mismatch | `Microsoft.AspNetCore.Components.WebAssembly` **10.0.7** aligned with other AspNetCore 10.0.7 packages (central `Directory.Packages.props`) |
| Browser extensions | Not runtime-verified in this pass; root cause reproduced from code path without requiring extension interference. Recommend smoke in Incognito when validating locally. |
| Lifecycle unsubscribe | Present before; strengthened disposed guards |

## Test results

```text
dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj
  → Build succeeded (0 errors; preexisting nullable warnings unrelated to UIX-2)

dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj
  → Passed: 419, Failed: 0

dotnet test tests/Apperture.ArchTests/Apperture.ArchTests.csproj
  → Passed: 34, Failed: 0
```

Coverage added in `Uix2ManagementBreadcrumbStabilizationTests.cs`: list/detail/nested routes, query/fragment ignore, malformed ids, incomplete/unknown fallbacks, navigation-context seam no-op, `StableKey`/`IsLinked`, source inspection that breadcrumbs stay plain anchors and TopBar does not key-remount orientation.

## Runtime smoke-test results

Automated/browser smoke against a live Aspire stack was **not fully executed in the agent environment** for this closure. Recommended operator checklist (post-deploy):

| Route | Direct load | Breadcrumb click | Refresh | Back/forward |
|-------|-------------|------------------|---------|--------------|
| `/dashboard` | ☐ | ☐ | ☐ | ☐ |
| `/runs` → `/runs/{id}` | ☐ | ☐ | ☐ | ☐ |
| `/integrations` → detail | ☐ | ☐ | ☐ | ☐ |
| `/schedules` → detail | ☐ | ☐ | ☐ | ☐ |
| Config hub | ☐ | ☐ | ☐ | ☐ |
| `/demo/customers` → detail | ☐ | ☐ | ☐ | ☐ |

Pass criteria: no `Symbol()` / `parentNode` / “No element is currently associated” / “Unhandled exception rendering component” tied to top-bar breadcrumbs.

Distinguish unrelated preexisting warnings (e.g. third-party or auth noise) from breadcrumb/shell failures.

## Known limitations

- Terminal page title still comes from resolver / `PageHeader` publish — not duplicated in the breadcrumb trail (by design).
- `ManagementBreadcrumbNavigationContext` is a seam only; no query contracts yet.
- Nav rail / control panel still use their own `preventDefault` + `NavigateTo` patterns (out of UIX-2 scope unless they prove to cause the same class of failure).
- Capability-filtered crumbs and entity display names remain deferred (Epic 101 deferred list).

## Handoff — UIX-3 / UIX-4

- **UIX-3:** Affected-record anchors and return context via URL parameters (`from` / similar). Feed validated context into `Resolve(path, navigationContext)` without a session stack.
- **UIX-4:** Contextual breadcrumb replacement and link integrity; keep trails deterministic from URL + optional validated context.
- **UIX-5:** Panel/overlay pattern audit — do not fold into breadcrumb state.

Do **not** reintroduce click-intercepted breadcrumb anchors or keyed remount of the orientation subtree without a proven isolated DOM boundary.
