# UIX-2 — Breadcrumb and shell rendering stabilization

**Status:** Corrective passes complete (operator smoke still required to close)  
**Date:** 2026-08-04  
**Related:** [Epic 101](../02-decisions/Epic101-Top-Bar-Breadcrumbs-and-Parent-Context-Links.md), [pre-release product roadmap](../00-overview/pre-release-product-roadmap.md) Track 1 UIX arc

## Executive summary

UIX-2 addressed six related failures in the Management UI shell:

1. **Breadcrumb renderer desync** — click-intercepted anchors + `@key` remount of orientation.
2. **Stale breadcrumbs** — removing `@key` without an explicit location resolve path.
3. **Cold unauthenticated startup** — shell mounted while `Unauthenticated`, then torn down by login redirect.
4. **Post-login AuthLayout→MainLayout race** — gate unblocked on `/login` while authenticated.
5. **Login `forceLoad` vs gate SPA race + root bootstrap** — `Session.RefreshAsync` before `NavigateTo(forceLoad)` mounted MainLayout then destroyed the circuit mid-flight.
6. **Sticky shell + deferred menus** — gate unmount / mid-await menu load still produced `parentNode` then orphaned components after menus logged.

## Pass 7 — Sync-context + first-paint races (after pass 6)

### Evidence (after pass 6)

- Still: `parentNode` **before** menu log, then orphaned component IDs, then menus, then more orphans.
- Sticky + deferred menus did not stop first-paint desync.

### Root cause

1. `SessionStateService.RefreshAsync` used `ConfigureAwait(false)` then `Notify()` — gate `UpdateBlocking` raced the renderer off-context.
2. Gate/caps continuations mutated block/commit flags off the Blazor sync context.
3. `PageHeader` → `ShellOrientation.Changed` → `TopBarOrientation` `StateHasChanged` during sibling first paint.
4. `PersonalNotificationMenu` awaited HTTP in `OnInitializedAsync` (with `ConfigureAwait(false)`) during shell mount.

### Fix

| Change | Intent |
|--------|--------|
| Session `GetMeAsync` stays on sync context | Notify/gate updates on renderer thread |
| Gate marshals mutations via `InvokeAsync` | Caps completion cannot race render |
| Gate markup: loading only when `block && !committed` | Login still works; committed shell not unmounted |
| `TopBarOrientation` defers page-context subscribe | After first `OnAfterRenderAsync` |
| Menus: MainLayout kickoff only; deferred `Changed` | No dual OnAfterRender load + sync SHC |
| Notification badge count after first paint | No init-await during shell mount |

## Pass 8 — Seeded menus + single Changed owner (after pass 7)

### Evidence (after pass 7)

- Fewer errors. Remaining: `parentNode` before menu log; then `Loaded 6 domain menus`; then orphaned components **14 / 15 / 18**.
- Async `setTimeout` / mono background exec — delayed updates against disposed DOM.

### Root cause

1. First NavRail paint built from **empty** cache → API filled 6 domains → `Changed` inserted many `@key` items while dashboard/body still mounting.
2. **MainLayout + NavRail** both `StateHasChanged` on `Changed` → parent layout re-diff raced child rail rebuild → orphaned IDs.
3. Cache-hit path still set `notify = true` (unnecessary SHC).
4. `DashboardHome` awaited HTTP in `OnInitializedAsync` during first shell paint (`parentNode` before menu log).

### Fix

| Change | Intent |
|--------|--------|
| Seed `DomainMenuService` with `GetDefaultMenus()` | Stable middle-domain `@key`s on first paint |
| `_hasFetchedFromSource`; no notify on real cache hit | Second callers / hamburger do not re-SHC |
| MainLayout does **not** subscribe to `Changed` | NavRail sole rebuild owner; layout SHC only if domain name changes |
| `DashboardHome` load in `OnAfterRenderAsync` | Body swap after chrome first paint |

## Pass 9 — Do not await shell mount inside App OnAfterRender (after pass 8)

### Evidence (after pass 8)

- Only two console lines of substance: `parentNode`, then menus log, then orphaned **component 15**.
- Debugging hotkey ignored.

### Root cause

1. `App.razor` **awaited** `Session.RefreshAsync()` inside `OnAfterRenderAsync`. Session notify mounts `AuthRouteGate` → `MainLayout` while App was still mid-callback, then continued TZ/auth awaits.
2. MainLayout / Dashboard / notifications **awaited HTTP inside OnAfterRender*** — layout held across menu fetch while Body SHC.
3. Seeded menus still raised `Changed` when API returned the same middle domains → NavRail SHC → orphan 15.
4. Soft session refresh always `Notify()` even when Me payload unchanged → MainLayout full SHC.

### Fix

| Change | Intent |
|--------|--------|
| `App` fire-and-forget `BootstrapAsync` from sync `OnAfterRender` | Session mount not nested under App after-render |
| Soft refresh skip notify when state/user unchanged | No redundant shell SHC |
| MainLayout / Dashboard / notifications kickoff without awaiting in after-render | Parent not mid-lifecycle during child SHC |
| `MiddleDomainsEqual` + NavRail domain SequenceEqual | No rail SHC when seeded keys match API |

## Pass 10 — PageHeader / TopBar orientation first-paint (after pass 9)

### Evidence (after pass 9)

- Orphans gone. One remaining: `parentNode` before menu log.
- Dashboard still OK visually.

### Root cause

1. Dashboard `PageHeader` subtitle **differed** from route orientation subtitle → `SetPageContext` → `TopBarOrientation` `StateHasChanged` during first paint while Body/menus also updating.
2. `PageHeader` / `TopBarOrientation` still **awaited** `Task.Yield` inside `OnAfterRenderAsync`.
3. Notification badge always `StateHasChanged` after count refresh even when count unchanged.

### Fix

| Change | Intent |
|--------|--------|
| Align `/dashboard` route subtitle with PageHeader copy | Publish becomes no-op on home |
| PageHeader skip `SetPageContext` when matching route | No TopBar SHC for list/home pages |
| Fire-and-forget orientation accept/publish | No held after-render |
| Notification count SHC only when value changes | No utility-bar SHC on zero→zero |

## Pass 11 — Serialize page data after menus; stop layout SHC every nav (after pass 10)

### Evidence (after pass 10)

- Dashboard still: one `parentNode` (no orphan storm).
- Nav to Control Center: orphaned components on `onGlobalEvent` (click) — shell re-render during navigation.

### Root cause

1. `MainLayout` **always** `StateHasChanged` on `LocationChanged` while Router swaps `@Body` → click handler sees disposed nodes.
2. `ControlCenter` awaited HTTP in `OnInitializedAsync` during that same nav wave.
3. Dashboard/notification HTTP raced the first menu fetch SHC window.
4. Closed `ControlPanel` still SHC on every location change.
5. Control Center PageHeader subtitle still differed from route orientation.

### Fix

| Change | Intent |
|--------|--------|
| MainLayout SHC on location only when domain changes | Router owns Body; rail owns highlight |
| NavRail `LocationChanged` SHC | Active domain without full layout remount |
| `DomainMenuService.WhenFetchedAsync` | Dashboard/notifications wait for menu wave |
| ControlCenter post-paint kickoff | No init-await during click nav |
| ControlPanel skip location SHC when closed | Less chrome churn |
| Align Control Center route subtitle | No TopBar publish SHC on land |
| Display TZ stay on sync context | No off-context `Changed` |

## Pass 12 — No layout SHC on nav; defer chrome past click (after pass 11)

### Evidence (after pass 11)

- Dashboard: still one `parentNode`.
- Control Center click: still 4 orphans (`onGlobalEvent`) — domain change Dashboard→Operations still triggered MainLayout SHC.

### Root cause

1. Domain-changing navigations still remounted the whole layout during the click that disposed Dashboard.
2. NavRail / TopBarOrientation SHC ran synchronously in the same click turn.
3. Summary cards used plain enhanced-nav `<a href>` inside the disposing page.
4. Gate shell mount SHC nested inside caps completion without a yield.

### Fix

| Change | Intent |
|--------|--------|
| MainLayout never SHC on `LocationChanged` | NavRail `ActiveDomain` from URI |
| Yield before NavRail/TopBar/ControlPanel location SHC | Past `onGlobalEvent` |
| `ManagementSummaryCard` preventDefault + `NavigateTo` | Safer card navigation |
| Gate defer shell-mount SHC one yield | Cleaner first mount |
| No TopBar first-accept SHC | Route titles already painted |

## Pass 13 — Serialize shell mount; leave click before NavigateTo (after pass 12)

### Evidence (after pass 12)

- Dashboard: still one `parentNode` **before** menus log (startup async chain).
- Control Center card click: 3 orphans (was 4); one still **sync** on `onGlobalEvent`.

### Root cause

1. Caps-ready gate could mount `MainLayout` while `App.BootstrapAsync` was still resolving TZ/sign-in options — first-paint raced bootstrap continuations.
2. `ThemeService.InitializeAsync` used `ConfigureAwait(false)`, dropping sync context into `Session.RefreshAsync`.
3. Summary cards called `NavigateTo` **inside** the click handler that Blazor was still dispatching → dispose mid-`onGlobalEvent`.

### Fix

| Change | Intent |
|--------|--------|
| `ShellBootstrapState` + gate `appBootstrapReady` | Mount shell only after App theme/session/TZ/options |
| Theme stays on sync context | No off-context Notify after init |
| Card `NavigateTo` after `Task.Yield` | Past originating click before Body dispose |

## Pass 14 — Event-exit navigation + render probes (option A)

### Evidence (after pass 13)

- Console unchanged: `parentNode` before menus; 3 orphans on Control Center click via `onGlobalEvent`.
- SHC-deferral treadmill exhausted; architecture discussion chose diagnostic path A.

### Root cause (confirmed for orphans)

`async Task` `@onclick` handlers keep Blazor’s event dispatcher open until the Task completes. `await Task.Yield()` then `NavigateTo` still runs inside `onGlobalEvent`.

### Fix

| Change | Intent |
|--------|--------|
| Summary card / empty-state: **sync void** click + fire-and-forget navigate | Event exits before Body dispose |
| `ShellRenderProbe` (`ErrorBoundary`) around NavRail / TopBar / Body | Log which subtree faults on `parentNode` |

## Pass 15 — Chrome-first Body + button cards (option C after A)

### Evidence (after pass 14 / option A)

- Console unchanged for `parentNode` / orphans; **no** `UIX-2 shell render fault` lines.
- Component IDs shifted (15→21, 27→33) — new tree loaded; `ErrorBoundary` does not catch JS renderer `parentNode`.
- Sync orphan on `onGlobalEvent` remained — `<a href>` enhanced navigation likely still raced click.

### Fix

| Change | Intent |
|--------|--------|
| MainLayout holds `@Body` until menus + yield (`_bodyReady`) | Chrome paints alone; no Body/PageHeader race |
| Summary cards / empty links are `<button>` (no href) | No enhanced-nav mid-click |
| NavRail logo/item clicks: sync void + deferred core | Same event-exit rule |

## Pass 16 — ControlPanel closed = unmounted; macrotask nav

### Evidence (after pass 15)

- IDs shifted again (21/33 → 19/30); symptoms unchanged.
- `parentNode` still before menus while `_bodyReady` is false → fault is **chrome**, not Body/Dashboard.
- `ErrorBoundary` probes never logged (JS renderer fault); removed.
- Sync orphan on `onGlobalEvent` remained despite button + Yield.

### Fix

| Change | Intent |
|--------|--------|
| Mount `ControlPanel` only when open | No off-screen nav tree on first paint |
| Card/rail nav: `InvokeAsync` + `Task.Delay(1)` | Leave click turn before NavigateTo |
| Drop `ShellRenderProbe` | Probes did not catch JS `parentNode` |

## Pass 17 — Disable WASM prerender (root cause)

### Evidence

- 16 Client-side SHC/nav passes; symptoms unchanged aside from component ID churn.
- `parentNode` fires during `mono_run_main` before menus — hydration window.
- Host uses `render-mode="WebAssemblyPrerendered"` on `App` (`_Host.cshtml`).

### Root cause

Server prerender emits a full component tree; WASM then attaches and re-applies DOM. That desync is the standard Blazor `parentNode` / orphaned-component failure mode. Client-only yield/SHC fixes cannot repair a bad hydrate.

### Fix

| Change | Intent |
|--------|--------|
| `_Host.cshtml`: `WebAssembly` (not `WebAssemblyPrerendered`) | No server HTML to fight |
| `_Layout.cshtml`: `#app` is a `div`, not `<body>` | Boot script not under Blazor root |

**Operator must rebuild/restart `Apperture.Management.Ui.Server` (not Client alone) and hard-refresh.**

## Pass 18 — Single App root (double-mount)

### Evidence (debug session `4bd3d8`)

- Host was already non-prerender (`type":"webassembly"` marker only; `hasWebAssemblyPrerenderedAttr:false`).
- `bootstrap-start` ×2 and `layout-init` ×2 on cold start → two `App` / `MainLayout` instances.
- Cause: `_Host` `<component render-mode="WebAssembly">` **plus** `RootComponents.Add<App>("#app")`.

### Fix

| Change | Intent |
|--------|--------|
| `_Host.cshtml`: loading placeholder only (no `<component>`) | One mount via Client `RootComponents.Add` |

## Pass 6 — Sticky shell + deferred menus

### Evidence (after pass 5)

- Fewer errors, but cold authenticated mount still: `parentNode` → menu log → orphaned components.
- Pattern: Router child disposed while menus awaited before first paint.

### Fix

| Change | Intent |
|--------|--------|
| `AuthShellAccess` sticky commit | Once protected shell shown, do not re-block for Loading / cap re-warm |
| `AuthRouteGate` `_shellCommitted` | Never clear `_capabilitiesReady` after commit |
| Menu load in `OnAfterRenderAsync` | First paint before HTTP |
| `DomainMenuService.Changed` | NavRail/MainLayout rebuild after menus arrive |

## Pass 5 — Root cause (systematic)

### Evidence

- Crit errors are Blazor DOM desync, not the `Shift+Alt+D` info line.
- Local login did: `RefreshAsync()` → gate SPA-mounted shell → `forceLoad` tore WASM mid-mount.
- Soft refresh Loading notify + root `/` MainLayout bounce amplified remounts.

### Fix

| Change | Intent |
|--------|--------|
| Local login: no `RefreshAsync` before `forceLoad` | New circuit loads session; old circuit never SPA-mounts shell |
| `SessionStateService` soft refresh | No `Loading` notify when already Authenticated/ForbiddenUnlinked |
| Root `/` always blocked + gate redirect | Never mount MainLayout on bootstrap path |
| Gate `StateHasChanged` only when block flag changes | Fewer Router remount pressures |
| `PageHeader` orientation publish after `Task.Yield` | Avoid ancestor SHC during OnAfterRender cascade |

## Passes 1–4 (short)

See git history / prior sections: plain breadcrumb anchors; single location owner in `TopBarOrientation`; gate blocks unauthenticated protected paths; gate blocks authenticated-on-public.

## Final implementation

| Piece | Role |
|-------|------|
| `AuthShellAccess` | Block + sticky commit/release; redirects; `appBootstrapReady` |
| `LoginRouteHelper.IsRootBootstrapPath` | `/` detection |
| `ShellBootstrapState` | App marks ready after theme/session/TZ/options |
| `AuthRouteGate` | Sticky shell; waits bootstrap + caps; root + login redirects |
| `MainLayout` | Menu kickoff; chrome-first `_bodyReady`; **no** location SHC |
| `NavRail` | `ActiveDomain` from URI; ControlPanel only when open; event-exit nav |
| `ManagementSummaryCard` | Button + sync void; `Delay(1)` then NavigateTo |
| `DomainMenuService` | Seeded defaults; `WhenFetchedAsync`; skip `Changed` when middle domains match |
| `ControlPanel` | Location SHC only when open (deferred) |
| `ControlCenter` / `DashboardHome` | Post-paint loads; dashboard waits for menus |
| `PageHeader` | Skip shell publish when matching route; fire-and-forget first publish |
| `TopBarOrientation` | Deferred location SHC; no first-accept SHC |
| `App` | Fire-and-forget bootstrap; `MarkReady` after TZ/options |
| `App` / `_Host` | Client-only `WebAssembly` (no prerender) |
| `ThemeService` | Init stays on sync context |
| `LocalLoginFormSection` | Cookie + `forceLoad` only |
| `SessionStateService` | Soft refresh; skip notify when unchanged |

## Automated test results

```text
dotnet test ... --filter "FullyQualifiedName~Uix2|FullyQualifiedName~AuthShell|FullyQualifiedName~LoginRoute|FullyQualifiedName~Epic101ManagementBreadcrumb"
  → Passed: 135, Failed: 0
```

## Runtime smoke (operator)

### A. Cold start unauthenticated

- [ ] Incognito → root → redirect login; no shell flash; no Symbol/parentNode

### B. Local sign-in → dashboard

- [ ] Sign in → full reload to `/dashboard` (brief loading) → dashboard
- [ ] No Symbol/parentNode/orphaned components after menus load
- [ ] Network: menus + dashboard after session, not mid-torn circuit

### C. Authenticated nav

- [ ] Dashboard → Runs → breadcrumbs → Integrations → Config; Back/Forward/refresh clean

**Do not mark UIX-2 closed until A–C pass.**

## Handoff — UIX-3 / UIX-4

UIX-3 and UIX-4 are implemented (see their audit docs). Shell invariants below remain mandatory.

Unchanged. Do not restore orientation `@key` remounts, dual crumb location subscribers, click-intercepted crumbs, or `RefreshAsync` before login `forceLoad`. Do not unmount the protected Router after first commit for Loading/cap flicker. Do not re-subscribe MainLayout to `DomainMenuService.Changed` alongside NavRail. Do not await `Session.RefreshAsync` inside `App` `OnAfterRenderAsync`. Keep dashboard/Control Center PageHeader copy aligned with route orientation (or skip publish when they match). Do not `StateHasChanged` MainLayout on `LocationChanged`. Do not mount MainLayout before `ShellBootstrapState.MarkReady`. Do not `NavigateTo` from an `async Task` click handler (event stays open); use sync void + macrotask deferral. Do not mount `ControlPanel` while closed. Do not restore `WebAssemblyPrerendered` on `_Host.cshtml` without a dedicated hydrate-safe design. Do not re-add `<component type="typeof(App)" …>` alongside `RootComponents.Add<App>("#app")`. UIX-4 contextual crumbs must keep plain `<a href>` anchors and path+query observation without remounting the shell.
