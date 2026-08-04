# UIX-2 — Breadcrumb and shell rendering stabilization

**Status:** Complete (with corrective pass for stale trails)  
**Date:** 2026-08-04  
**Related:** [Epic 101](../02-decisions/Epic101-Top-Bar-Breadcrumbs-and-Parent-Context-Links.md), [pre-release product roadmap](../00-overview/pre-release-product-roadmap.md) Track 1 UIX arc

## Executive summary

UIX-2 had two defects:

1. **Renderer desynchronization** — Epic 101 click-intercepted breadcrumb anchors plus a TopBar `@key` remount of the orientation subtree caused Blazor WASM `Symbol()` / `parentNode` / orphaned component errors.
2. **Stale breadcrumbs after navigation** (operator smoke) — Removing the `@key` remount fixed the renderer, but the remount had been the only reliable way breadcrumbs re-initialized. With the subtree left mounted, trails stayed stuck on the first loaded route until an explicit location-change resolve path was added.

The corrective pass makes breadcrumb (and title) resolution **location-aware** while keeping the shell mounted: no keyed remount, no click interception, no breadcrumb history store.

## Original symptoms (pass 1)

- Breadcrumb links only rarely navigated successfully.
- Console: `Symbol()`, `parentNode`, then cascading “No element is currently associated with component”.

## Operator regression (pass 2)

- Direct load showed the correct trail.
- Client-side navigation updated page content correctly.
- Breadcrumbs continued showing the **first** route’s trail.

## Confirmed root causes

### Pass 1 — renderer / DOM desync

Epic 101’s `@onclick` + `@onclick:preventDefault` + `NavigateTo` on parent `<a href>` elements, combined with `TopBar` remounting `TopBarOrientation` via `@key="@_orientationKey"` on every path change, destroyed the orientation DOM mid-navigation and desynchronized the Blazor render tree.

### Pass 2 — stale trails after removing `@key`

The `@key` remount had **masked** the absence of an explicit location-update path:

- `ManagementBreadcrumbs` was a pure parameter sink (`Items`).
- `TopBar` computed orientation only during its own render.
- After removing `@key`, the breadcrumb subtree persisted across routes.
- Without a dedicated `NavigationManager.LocationChanged` → re-resolve → `StateHasChanged` ownership on the breadcrumb/orientation components, trails did not refresh.

**Do not restore the keyed remount.** Location observation is the correct fix.

## Implementation (final)

### Pass 1 (retained)

- Plain `<a href>` breadcrumb links (no click interception).
- No `@key` remount of orientation.
- `ShellOrientationService.ResetPageContext` clears silently on route change (TopBar).
- Resolver strips query/fragment; optional `ManagementBreadcrumbNavigationContext` seam for UIX-3/4.
- `BreadcrumbItem.IsLinked` / `StableKey`.

### Pass 2 (corrective)

| Piece | Role |
|-------|------|
| `ManagementShellOrientationTracker` | Observes **path + query** (fragment ignored); resolver still path-only |
| `ManagementBreadcrumbLocationState` | Initialize / TryApplyLocation / Refresh / Dispose guards |
| `ManagementBreadcrumbs` | Subscribes to `LocationChanged`, re-resolves parents, `InvokeAsync` + disposed guards, unsubscribes on dispose |
| `TopBarOrientation` | Location-aware title/subtitle (route + `ShellOrientation` overrides); hosts `<ManagementBreadcrumbs />` |
| `TopBar` | Hosts orientation; on `LocationChanged` only resets page-context overrides |

Observation includes query so future UIX-3/4 context-only query changes can trigger reevaluation; UIX-2 resolver rules still ignore query/fragment for the trail itself.

## Alternatives considered

| Alternative | Why not |
|-------------|---------|
| Restore `@key` remount | Reintroduces renderer desync risk; masks missing location ownership |
| Keep `NavigateTo` click intercept | Fights the router; excluded by epic |
| Breadcrumb history / per-page `Set` | Excluded; trails stay URL-derived |
| Use domain-menu / orientation events as sole nav proxy | Fragile; location must be first-class |

## Investigation checklist

| Check | Result |
|-------|--------|
| JS mutating breadcrumb DOM | Ruled out |
| Framework package mismatch | WASM / AspNetCore **10.0.7** aligned |
| Browser extensions | Not required to explain either defect |
| Keyed remount as refresh mechanism | Confirmed masking effect for stale trails |

## Test results

```text
dotnet test ... --filter FullyQualifiedName~Uix2
  → Passed: 34, Failed: 0

dotnet build ...Management.Ui.Client.csproj
  → Build succeeded
```

`Uix2ManagementBreadcrumbStabilizationTests` covers resolver behavior, location-state init/nav/back-forward/query/dispose, and source inspection that breadcrumbs stay plain anchors, subscribe/unsubscribe to `LocationChanged`, and TopBar does not key-remount orientation.

## Runtime smoke-test results

**Pass 1:** Renderer errors addressed in code; full Aspire console smoke not completed in-agent.  
**Pass 2:** Operator smoke discovered stale trails; corrective implemented. Re-validate locally with Preserve Log:

| Flow | Breadcrumbs update | No renderer errors |
|------|--------------------|--------------------|
| Dashboard → Runs | ☐ | ☐ |
| Runs → Run detail | ☐ | ☐ |
| Detail crumb → Runs | ☐ | ☐ |
| Integrations → detail | ☐ | ☐ |
| Config → nested | ☐ | ☐ |
| Demo Customers → detail | ☐ | ☐ |
| Back / Forward | ☐ | ☐ |
| Refresh on detail | ☐ | ☐ |
| Three+ areas repeatedly | ☐ | ☐ |

## Known limitations

- Terminal title remains outside the crumb trail (by design).
- `ManagementBreadcrumbNavigationContext` is a seam only.
- Nav rail / control panel may still use `preventDefault` + `NavigateTo` (out of scope).
- In-agent Aspire smoke for pass 2 not recorded; operator re-validation required.

## Handoff — UIX-3 / UIX-4

- Feed validated query context into `Resolve(path, navigationContext)`.
- Observation already keys on path+query so context-only query changes can refresh.
- Do **not** reintroduce click-intercepted crumbs or orientation `@key` remounts.
