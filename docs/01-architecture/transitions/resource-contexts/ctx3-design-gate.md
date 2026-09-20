# CTX-3 design gate — Application Context Experience

Date: 2026-09-13
Status: **Design approved; product decisions recorded in §15.** No production code changed, no migration authored, CTX-3 not started.
Audience: the agent implementing CTX-3, and the reviewer approving this direction.

Evidence base: code on `master` at `aa1ad8c`, read this pass. That includes `ResourceContextService`, `SqlResourceContextRepository`, the Integrations/Connections/Schedules/Runs/Audit/NotificationInstances/PlatformEvents/ControlCenter/Inventory controllers, `ControlCenterQueryService`, `ControlActivityQueryService`, `ControlAttentionQueryService`, `InventoryQueryService`, `IntegrationReadinessService.ListFleetAsync`, `OperatorCapabilitiesQueryService`, `PlatformPermissionSeeder`, `PlatformAdminRolePermissionRepair`, `MenusController`, `ControlPanelMenuDefinitions`, `MainLayout`, `TopBar`, `AppearancePreferenceCoordinator`, `UserPreferences`, the `Apperture.Management.Client` contracts, and the Integrations/Connections/Schedules/Runs/ControlCenter/Inventory/BusinessData pages. It also draws on [`ctx-design-gate.md`](./ctx-design-gate.md), [`ctx1-closure.md`](./ctx1-closure.md), [`ctx2-closure.md`](./ctx2-closure.md), ADR-0040, `control-panel-ui.md`, and the HUB-1 closure.

---

## 0. Code facts that change the brief

The brief is mostly accurate. Seven verified facts change the design, and every later section depends on them.

| # | Fact (verified) | Consequence |
|---|---|---|
| F1 | **The UI's client contracts do not carry ownership.** In `Apperture.Management.Client`, `IntegrationDto`, `ConnectionDto`, `RunSummaryDto`, `RunQueryFilter`, `CreateIntegrationRequest`, `CreateConnectionRequest` and `IntegrationFleetRowDto` have no `ResourceContextId`. `GetIntegrationsAsync`, `GetConnectionsAsync` and `GetSchedulesAsync` take no `contextId`. Only the server-side DTOs were extended in CTX-1/CTX-2. CTX-2 already found and fixed the same drift for the Schedule DTOs. | CTX-3's first slice is client-contract parity. Without it, "wire existing filters into the UX" cannot start. |
| F2 | **The Integrations list uses `GET /api/integrations/fleet`, not `GET /api/integrations`.** The fleet endpoint has no `contextId`. `ListFleetAsync` calls the unfiltered `ListIntegrationsAsync(ct)`. | One small API addition is needed. The context-filtered overload of `ListIntegrationsAsync` already exists. |
| F3 | **Standalone Schedule creation is not a supported product path.** `POST /api/schedules` returns 400 when `IntegrationId` is missing ("Create schedules under POST /api/integrations/{integrationId}/schedules"). `/schedules/new` always calls `CreateIntegrationScheduleAsync`. Standalone schedules exist only as legacy rows, and the API emits a legacy warning header for them. | CTX-3 must not design or build standalone-schedule creation. A Schedule's context is always inherited in the UI. |
| F4 | **The CTX-1 archive rule cannot be satisfied by any Application that has been used.** `CountOwnedResourcesAsync` counts every row regardless of state. Connections can only be retired, never deleted (`DELETE /api/connections/{id}` calls `RetireConnectionAsync`). Schedules have no delete operation anywhere. An Integration can be hard-deleted only when it has no bindings and no schedules. | Archive is unreachable after an Application owns one Connection or one Schedule, even with movement. CTX-3 must refine the archive rule (§6.5). |
| F5 | **Control Center's contextual sections are built entirely from `IRunQueryService.ListRunsAsync(RunQueryFilter)`**, and `RunQueryFilter` already supports `ResourceContextId` server-side (CTX-2). Stuck runtime shutdowns, worker/scheduler status and runtime health are instance data. | Scoping Control Center's contextual half costs one query parameter threaded through two services. That makes Option B affordable in CTX-3 (§5.1). |
| F6 | **`AuditController` does not expose `resourceContextId`**, although `AuditQuery.FilterByResourceContextId` exists. Runs, NotificationInstances and PlatformEvents do expose it. | One small API addition, if Audit gets an Application filter. |
| F7 | **The Integration create wizard labels `ScopeKey` "Environment/context; leave empty for instance scope."** | Once "Application" appears on the same form, this copy invites exactly the ScopeKey/ResourceContext confusion the CTX gate warned about (R10). It needs a copy fix in CTX-3. |

---

## 1. Executive recommendation

**The direction is sound: Applications and Shared as a shell-level view, managed under Config, with no fifth rail domain.** Proceed, with five corrections:

1. **Split the work.** **CTX-3A** delivers Application workspaces: management, the view selector, scoped browsing, create defaults, and scoped Control Center. **CTX-3B** delivers resource movement. Movement brings a transactional multi-aggregate write, race conditions against binding creation, preview APIs, a two-sided audit model and three UI flows. Keeping it separate gives 3A a clean, read-mostly review boundary. **Decided (D1, §15):** the two epics are built and merged separately but roll out as one product change. The feature is not launched until CTX-3B lands, because until then existing (backfilled-to-Shared) resources cannot be placed into Applications.
2. **Make the selector URL-authoritative, not hidden state.** On scoped collection pages the view lives in the query string (`?context=`). The shell remembers the last explicit choice per browser and per user, and applies it by *rewriting* the URL. Links and refreshes are therefore always self-describing (§3, §4).
3. **Detail pages are never scoped.** They show the resource's owning Application in the header. When a specific view is selected and differs, they show a non-blocking "outside current view" indicator. Opening a resource never switches the view (§4.6).
4. **Refine the archive rule (fact F4).** Archive is allowed when the Application owns no *live* resources: no non-retired Integrations, no active Connections, no enabled Schedules. While archived, owned resources cannot be made live again (§6.5).
5. **Show the selector only once an Application exists.** Installations that never create an Application see no change at all, and Shared labels stay hidden until they carry meaning.

ADR-0040 stays unchanged. The four-domain rail can support this direction because Applications are an organizational *dimension* across existing domains, not a new kind of resource with its own pages to browse. The only page Applications need is administrative, and Config already owns administration.

---

## 2. Operator scope model

### 2.1 Two concepts, kept apart

| Concept | What it is | Where it lives | Values |
|---|---|---|---|
| **Ownership** | Which ResourceContext a resource belongs to *now* (CTX-1) or belonged to *when it happened* (CTX-2) | Domain + persistence: `ResourceContextId` | Always a real ResourceContext id; `null` only for pre-CTX-2 history or system occurrences |
| **View scope** | Which subset of owned things an operator's page is showing | UI only: URL + client state | `All`, or exactly one ResourceContext id |

View scope never reaches the domain. The API receives either no filter or one real id.

### 2.2 `ResourceContextViewScope`

Add a pure value type in `Apperture.Management.Ui.Shared`, which is unit-testable like `IntegrationUiRoutes`:

```csharp
public readonly record struct ResourceContextViewScope
{
    public static ResourceContextViewScope All { get; }
    public static ResourceContextViewScope Of(string resourceContextId);

    public bool IsAll { get; }
    public string? ResourceContextId { get; }      // null only when IsAll

    public const string AllToken = "all";           // URL token only
    public static ResourceContextViewScope Parse(string? queryValue);  // null/empty → Unspecified (see §4)
    public string ToQueryValue();                   // All → "all", else the id
    public string? ToApiFilter();                   // All → null, else the id
}
```

Rules:

- **`All` is not a ResourceContext.** There is no row, no `ResourceContextIds.All`, and the value is never sent to an API. `ToApiFilter()` maps All to "omit the parameter", which the CTX-1 list APIs already treat as "no filter".
- **`null` is never used to mean All in state.** Parsing distinguishes three cases: the URL says `all`, the URL names an id, or the URL says nothing (*unspecified*, which triggers the resolution step in §4.2).
- **Why the URL token `all` is safe:** generated ids are 24 hex characters and Shared's id is `shared`, so no id can equal `all`. Add `all` to `ResourceContextSlug`'s reserved set anyway. CTX-1's rule is to "reserve a term once a route defines it", and CTX-3 defines it. The cost is one entry and one test.
- **Shared is a normal `Of("shared")`.** No special case is needed in filtering.
- **Kind-agnostic.** The type knows nothing about Applications. Rendering maps `Kind` to a label (§3.6).

### 2.3 Default

**The default is All.** Confirmed. CTX-1 backfilled every resource to Shared, so a Shared default looks correct on day one. It becomes wrong the moment the first Application is created, because it silently hides that Application's resources from anyone who never touched the selector. All preserves the pre-context mental model: "show me everything I'm allowed to see."

---

## 3. Shell selector

### 3.1 Placement and label

- **Placement:** the top bar, at the leading edge of the orientation area (`TopBar.razor` → before `<TopBarOrientation />`). It does not go in the rail, which ADR-0040 keeps, or in the Control Panel, which changes per domain.
- **Label:** **"View"**. The trigger reads `View: All`, `View: Shared` or `View: CampusNexus`.
  - Rejected: "Workspace" (implies isolation and separate data), "Application" (All and Shared are not applications), and "Context" (architecture vocabulary).
- **Tooltip/description (verbatim intent):** *"Choose which application's resources lists show. This organizes pages; it doesn't change what you have access to."*

### 3.2 Menu

```text
View
  ● All                       Everything you can see
  ○ Shared                    Not specific to one application
  ─ Applications ─
  ○ Admissions
  ○ Canvas
  ○ CampusNexus
  [Search applications…]      (only when > 7 active applications)
  ─
  Manage applications         (Config → Applications)
```

- Active Applications are sorted by name. Archived Applications never appear as options.
- The search box appears when there are more than 7 active Applications. It filters by name and slug with a case-insensitive contains match. There are no recents or favourites in CTX-3.
- "Manage applications" is always shown, because reading is not permission-gated (§9). Create and archive controls on the target page follow `CanManageResourceContexts`.
- Accessibility: a button with `aria-haspopup="listbox"` and a listbox with `aria-selected` options. Arrow keys, Home/End and type-ahead work; Escape closes the menu and restores focus to the trigger. The trigger's accessible name includes the current value ("View: CampusNexus").

### 3.3 Visibility

| Installation state | Selector |
|---|---|
| No active Application exists | **Hidden.** View is forced to All. Ownership labels are also hidden in lists, because showing "Shared" on every row is noise. |
| ≥ 1 active Application exists | Visible on every shell page |
| Operator's session not linked (`ForbiddenUnlinked`) | Hidden |

When the first Application is created, show a one-time dismissible hint anchored to the selector: "Use View to focus lists on an application." Store the dismissal in local storage.
> **HSK-A**: The one-time hint was removed after the discovery phase matured.

Visibility depends **only** on the active-Application count, never on what the current page is displaying. With no active Application, the selector stays hidden even while an explicit URL naming an archived Application is honoured (§4.3 Q5); that page's own archived banner is the indication, and ownership labels stay hidden too. Config → Inventory's Context column and filter are the one exception to the ownership-label rule (§5.5), and they do not make the selector appear.

### 3.4 Per-page participation

A pure route table, `ResourceContextViewRoutes.Resolve(path)`, returns one of the modes below. It follows the `IntegrationUiRoutes.ResolveSectionKey` / `RouteToSectionResolver` pattern.

| Mode | Selector behavior | Pages |
|---|---|---|
| `Scoped` | Active; changing it rewrites this page's `?context=` | §5 "Scoped" rows |
| `Partial` | Active; the page labels which sections follow it | Control Center |
| `Detail` | Shows the selection; the page shows an ownership comparison indicator (§4.6) | Resource detail/hub routes |
| `Unscoped` | Rendered muted with the tooltip "This page isn't filtered by application". Changing it updates the tab's view without affecting the page. | Everything else |

The route table is the single source of truth for Sonnet's tests. A page can never "accidentally" obey or ignore the view.

### 3.5 Switching cost

- The context list is loaded **once per tab** at shell start (`GET api/resource-contexts`, which returns a small table).
- It is refreshed after any management mutation, and whenever a URL names an id that is not in the cached list.
- Switching the view never refetches that list. It only changes the current page's data query.

### 3.6 Future kinds

The selector groups options by `Kind` through a Kind→group-label map: `Shared` is rendered as its single special row, and `Application` is rendered under "Applications". **A Kind without a label is not rendered.** When Department contexts arrive, adding a label is a one-line change. No speculative "Other" group is added now.

---

## 4. Persistence of selection

### 4.1 Layers

| Layer | Holds | Lifetime | Authority |
|---|---|---|---|
| URL `?context=` | The view for *this scoped page* | The history entry | **Highest**, when present |
| `ResourceContextViewState` (scoped DI service in the WASM client) | The tab's current view | The tab | Used when the URL is silent |
| `localStorage["apperture.view.{userId}"]` | The last view the operator *explicitly chose* in this browser | The browser | Seeds a new tab's state |
| Server `UserPreferences` | **Not used** | — | — |

**Why not server preferences.** `UserPreferences` is deliberately "closed" and typed ("values a user cannot actually change … do not belong here"). A last-used filter is navigation state, not a preference. Syncing it across devices creates exactly the stale-preference surprise the brief warns about: an operator is filtered to Canvas on a laptop because of something done on a desktop last week. The shell already persists per-browser UI state in local storage (`control-panel-resize.js`, the theme boot script), so this follows house precedent. A cross-device sync can be added later without changing the model.

**Why the storage key includes the user id.** On a shared browser, one operator's view must never seed another's.

### 4.2 Resolution on a scoped page

```text
1. URL has ?context=all|<id>          → use it; set tab state; do NOT write localStorage
2. URL silent, tab state set          → NavigateTo(same path + ?context=<tab state>, replace: true)
3. URL silent, tab state unset        → read localStorage; validate (§4.4); set tab state; replace URL
4. Nothing valid anywhere             → All; replace URL with ?context=all
```

The `replace: true` rewrite means the browser history never holds an unscoped entry for a scoped page. Back/forward therefore restores the exact view each entry had.

Explicit selector changes **do** write localStorage. This is the principle "explicit choices are remembered; links are not." Following a colleague's link to `?context=canvas-id` shows Canvas in that tab, but does not change what a new tab opens with tomorrow.

### 4.3 Required answers

| Question | Answer |
|---|---|
| 1. Persists across ordinary navigation? | **Yes.** Tab state carries it. Rail/menu hrefs stay static (`/integrations`), and step 2 above applies the view on arrival. |
| 2. Survives refresh? | **Yes.** On scoped pages the URL carries it. On other pages, tab state is lost on refresh but re-seeded from localStorage (step 3). |
| 3. Survives sign-out/sign-in? | **Yes, in the same browser**, via the user-keyed localStorage entry. Sign-out does not clear it. It does not follow the operator to another device, where the view starts at All. |
| 4. Encoded in the URL on scoped collection pages? | **Yes, always**, as `?context=all` or `?context=<id>`. The id is used, not the slug, because ids are canonical and survive rename and re-slug. Slug-addressed URLs remain a later decision. |
| 5. Selected Application is archived? | **From localStorage or tab state:** fall back to All and show an info toast once: "CampusNexus is archived. Showing all." Rewrite the stored value to `all`. **From an explicit URL:** honour it (the list APIs accept archived ids, and viewing what an archived Application still owns is legitimate). Show a page banner, "CampusNexus is archived", with a "View all" action. The selector trigger reads `View: CampusNexus (archived)` but does not list it as an option. |
| 6. Direct link to a resource outside the selected view? | Detail pages are not scoped. The resource renders normally, and the view is not changed (§4.6). |
| Unknown id in URL | Refresh the context list once. If still unknown, show "That context doesn't exist." with a "View all" action. Do not fall back silently. This mirrors the API's 400 on an unknown `contextId`. |
| New tab | Seeds from localStorage. A link opened in a new tab carries `?context=` on scoped pages, so it opens exactly as linked. |
| Two windows wanting different views | Supported. Tab state is per tab. localStorage is only read at tab start, and the shell does not subscribe to `storage` events, so one window never retargets another. |

> **HSK-C** (2026-09-19): superseded for the URL token in §4.2, §4.3 (Q4, unknown id) and §4.5. Scoped
> pages now emit the ResourceContext **slug** (`?context=development`; `shared` for Shared; `all` unchanged)
> and still accept the canonical id, which is rewritten to the slug with a history replace. The id stays
> canonical everywhere else — API filters, localStorage and tab state, ownership. See
> [ADR-0041 §Implementation status](../../../02-decisions/ADR-0041-human-readable-identity-policy.md#implementation-status).

### 4.4 Validation

The stored value must parse, and the id must be in the loaded list and Active. Otherwise it falls back to All. Validation runs after the context list loads. Until then scoped pages show their existing loading state, which avoids the unscoped-then-scoped flash.

### 4.5 Create links

Each scoped list's "Create …" button links with the current view, for example `/integrations/new?context=<id>`. The create page treats that value as a *preselection* only (§7). A create URL never silently decides ownership.

### 4.6 Detail-page ruling

**Adopt D with a C indicator.** Detail and hub routes ignore the view for data. The resource header owns the context display.

- **Header:** a Context chip beside the title, "Context: Canvas" or "Context: Shared", linking to that context's page under `/config/applications`. It is hidden while no active Application exists (§3.3).
- **Display resolution:** every context name shown in the UI resolves by id from the shell's cached list of active and archived contexts; archived contexts show their name, marked archived. An id missing from the cache triggers one refresh of that list. If it is still missing, the UI shows "Unknown context", with no link and no switch action. Operator UI never shows a raw id or blank text for a context.
- **Mismatch indicator:** when the view is a specific context that differs from the resource's owner, show a quiet inline notice under the header: *"This integration belongs to Canvas. Your view is CampusNexus."* It offers an action, **Switch view to Canvas**, which changes tab state and localStorage as an explicit choice.
  - Under **All** there is never a mismatch.
  - Shared resources under an Application view also count as a mismatch, and the notice adds "Integrations in CampusNexus can use it." for Connections.
- **Never auto-switch.** Opening a resource is reading, not choosing a workspace. Auto-switching would retarget every subsequent list, including in the operator's head, based on which link they last clicked.
- Hub sub-routes (`/integrations/{id}/runs` and the others) are `Detail` mode. They stay keyed to the Integration, and the shell view does not affect their contents.

Pages showing ownership comparison: Integration hub (all six sections plus schedule-diagnostics), Connection detail, Schedule detail, and Run detail. On Run detail the comparison uses the **historical** snapshot ("This run happened in Canvas"). A null snapshot shows "Context not recorded" and no mismatch.

---

## 5. Surface matrix

| Surface | Scoped by view? | Own filter? | Global? | CTX-3A notes |
|---|---|---|---|---|
| **Control Center** | **Partial** | No | System sections | §5.1 |
| **Integrations** `/integrations` | **Yes, current ownership** | Existing fleet filters compose (AND) | — | Needs fleet `contextId` (F2). "Context" column under All, only while an active Application exists (§3.3). |
| **Integration hub** `/integrations/{id}/*` | No (Detail) | — | — | Context chip + mismatch indicator; bound Connections are labelled with their Context |
| **Connections** `/connections` | **Yes, ownership only** | — | — | §5.2 |
| **Connection detail** | No (Detail) | — | — | "Used by" rows show each Integration's Context |
| **Schedules** `/schedules` | **Yes, Schedule ownership** | — | — | Attached schedules match their Integration by invariant; legacy standalone rows filter by their own column |
| **Schedule create** `/schedules/new` | Integration picker filtered by view | "Show all integrations" toggle | — | Context is inherited; shown read-only |
| **Runs** `/runs` | **Yes, historical snapshot** | Existing URL filters compose | — | §5.3 |
| **Run detail** | No (Detail) | — | — | Shows the historical Context |
| **Execute** `/execute` | No | — | Unscoped | Integration pickers show Context labels; defer filtering |
| **Audit** `/control/audit` | **No** | **Yes: "Context" filter**, default All, plus a "Use current view" chip | Unscoped | §5.4. Needs API param (F6). |
| **Notifications**: Config instances/definitions | No | Deferred | Unscoped | API filter exists; UI filter is a later NTF/CTX follow-up |
| **Notifications**: hub tab | No (Detail) | — | — | Integration-keyed as today |
| **Personal inbox** `/me/notifications` | No | — | Unscoped | Personal, not organizational |
| **Platform events** | No UI | — | — | API filter exists; diagnostic only |
| **Config Inventory** | **No** | **Yes: local Context filter (§5.5)** | Unscoped (admin explorer, All) | Independent of the shell view. Adds its own Context column and filter (All / System / Shared / each Application / Not recorded in Activity), always present even with no Applications. |
| **Business Data** | **No** | — | Unscoped, with a transitional banner | §11 |
| **Config: Applications** | No | Active/Archived filter | Unscoped | §6 |
| **Config: Governance, Attention, System, Modules, Settings, Auth, Developer** | No | — | Unscoped | — |
| **Account** | No | — | Unscoped | — |

### 5.1 Control Center: Option B

Contextual sections follow the view. System sections are always instance-wide and **say so**.

| Section | Source (verified) | Under a specific view |
|---|---|---|
| Action needed → `executionFailed` | `ListRunsAsync(Status=Failed, CreatedFrom)` | Filtered by run snapshot |
| Action needed → `runtimeShutdownQueuedStuck` | `RuntimeControlRequests` | **Always shown**, tagged "Platform" |
| Action needed → rejected external controls | `WorkflowExternalControlRequests` by `RunId` | Filtered through a join to `OperationRuns.ResourceContextId` |
| Recent problems / Recent activity | `ListRunsAsync(CreatedFrom)` | Filtered by run snapshot |
| Summary pill "Blocked work" | Runtime health snapshot (instance) | Moved into the Platform group, labelled "Blocked work (all applications)" |
| Platform health, Worker runtime, Scheduler operations | Runtime health / scheduler status | Unchanged, under a group heading **"Platform — all applications"** |
| Control targets (advanced) | Operation/workflow definitions, runtimes | Unchanged (definitions are module-level, not owned) |

UI rules:

- The page subtitle reflects the view: "What needs attention in **CampusNexus** — …". Under All it is unchanged.
- **Honest history note:** when a specific view is selected, show one muted line in the activity sections: *"Some older runs don't have a recorded context. Runs without a recorded context appear only under All."* CTX-2 snapshots are null for old history, and those runs silently vanish under a specific view without this note.
- Summary pills are split into two labelled groups, "In view" and "Platform". A number never changes meaning without its label changing.

API: `GET api/control/center?resourceContextId=` is threaded into `ListRecentActivityAsync` and `ListAttentionAsync` as an optional parameter mapped to `RunQueryFilter.ResourceContextId`. Callers that omit it keep today's behavior. **This is reasonable for CTX-3A.** It is the last slice of 3A, and it can be cut without breaking anything else (§13). If cut, Control Center renders `Unscoped` with the banner "Control Center shows all applications."

### 5.2 Connections: ownership browsing

Under a specific Application view, `/connections` shows **only Connections owned by that Application** (option A). Shared is not merged in, for three reasons:

- An ownership list that silently includes Shared rows makes "what belongs to CampusNexus?" unanswerable. That is the question archive, movement and future Services all need answered.
- The place where Shared availability matters is binding, and the binding picker already applies the reference rule server-side (CTX-1).
- Merging would teach operators that Shared resources "belong" to every Application. That is the dumping-ground failure mode (R3).

Usability mitigation (C-lite): under an Application view, show a one-line notice above the list: *"Integrations in CampusNexus can also use **N Shared connections**."* Link it to `/connections?context=shared`. Count N from the already-cached summary endpoint (§10). No extra query is needed.

### 5.3 Runs: historical snapshot, never current ownership

`/runs?context=<id>` sends `RunQueryFilter.ResourceContextId`. This is the CTX-2 snapshot on `OperationRuns`. It must **not** be translated into "runs of Integrations currently owned by X". A test must prove that a run snapshotted under A still appears under A after its Integration's current owner is changed to B (in controlled test setup).

- Under All, while an active Application exists (§3.3), add a "Context" cell resolved as in §4.6. A null snapshot renders "Not recorded".
- The URL filter summary in "Filters and diagnostics" lists the Context.
- `Epic33UiDisplayHelpers.BuildRunsListUrl` gains a `context` argument, so the status facet dropdown preserves it. This was the class of bug NAV-1 stabilization fixed for `integrationId`.
- Include the same pre-history note as Control Center when a specific view is selected.

### 5.4 Audit: its own filter, not the shell's

Most audit rows are System/IAM and legitimately carry a null context (CTX-2 §15). If the shell view scoped Audit, CampusNexus would appear to have "almost no audit trail", which is misleading. Instead:

- Audit stays `Unscoped`.
- It adds a **Context** select (All / Shared / active + archived Applications), sent as `resourceContextId`.
- When the shell view is a specific Application, show a chip: "Filter to CampusNexus". One click applies the filter; nothing is applied automatically.
- The filter's help text: "Only activity recorded against a context. Platform and account activity has no context."

### 5.5 Config Inventory: its own Context filter, not the shell's

Config → Inventory is `Unscoped`. It never reads the shell view or the shell's `?context=`. It has its own **Context** column and filter. Because Inventory is an administrative cross-platform explorer, the column and filter are always present — including when no Application exists, which is an intentional exception to §3.3's ownership-label rule. They never make the shell View selector appear.

**Filter values**

| API token | Label | Modes |
|---|---|---|
| `all` (default) | All | Resources, Activity |
| `system` | System | Resources, Activity |
| `shared` | Shared | Resources, Activity |
| `notRecorded` | Not recorded | Activity only |
| an active or archived `ResourceContextId` | the Application's name (archived ones marked) | Resources, Activity |

Any other token, an id that does not exist, or `notRecorded` in Resources mode is invalid input, and the API returns 400. The page then says the Context filter is no longer valid and offers **Reset to All**; it never shows an empty Inventory for an invalid filter. Applications are added to the options when they exist.

**Classification of each row**

- **Shared or an Application** — the row has a `ResourceContextId`. Integrations, Connections and Schedules use their current ownership. An integration-scoped NotificationDefinition (`Scope` = `Integration`) uses its Integration's current ownership, derived at query time, as long as that Integration still exists. Runs and audit events use the context recorded when they occurred (§5.3), never current ownership.
- **System** — a UI-only classification, never a persisted `ResourceContext` row or id. It applies to Module records, NotificationDefinitions with `Scope` `Platform` or `Module`, and audit events with no `ResourceContextId` whose action name starts with a System-by-design prefix: `auth.`, `user.`, `credential.`, `identity_link.`, `auth_admin.`, `account.`, `governance.`, `audit.read.`, `module.lifecycle.`, `platform.settings.`, `emergency.`, `entra.`. The list is defined once in code and shared by classification and SQL filtering. Runs are never System.
- **Not recorded** — Activity only: a context-eligible occurrence for which no ResourceContext was recorded. That means any Run with no `ResourceContextId`, and any audit event with no `ResourceContextId` whose action is not System-by-design. Pre-CTX-2 history is the main case but not the only one: `recovery.*` audit events are still written without a context today (threading it through `RecoveryController` is follow-up work outside CTX-3A).
- **Unknown context** — an integration-scoped NotificationDefinition whose Integration no longer exists. Integration hard delete currently leaves such definitions behind (see §13 CTX-3B step 7). The row shows "Unknown context" (internal token `unknown`), appears only under All, and is never classified System or Not recorded. CTX-3A adds no Unknown filter option.

Audit events classify **id-first**: a non-null `ResourceContextId` decides Shared or Application regardless of the action name, which is what correctly classifies `resource_context.*` events. Only a null value is tested against the prefix list. `AuditCategoryResolver` categories are not used: its `other` category mixes a context-eligible family (`integration.settings.*`) with System-shaped families it does not recognize (`platform.settings.*`, `emergency.*`, `entra.access_request.*`).

**Filtering and truncation**

Today Inventory collects rows from each source, applies Type, Status and search in memory, sorts, and takes the page size. Modules, Integrations, Connections and Schedules are fetched in full. Three sources are capped in their own query: Runs (100 newest), audit events (100 newest within Inventory's 24-hour window) and NotificationDefinitions (first 100 by name). Filtering a capped source in memory can drop matching rows and produce a false empty result, so every non-All Context value is applied inside the source query:

- **A specific context:** Integrations, Connections, Schedules, Runs and audit events use their existing `ResourceContextId` filters; NotificationDefinitions are filtered by their existing Integration's ownership in the same query, so orphaned definitions never match; Modules are skipped.
- **System:** Modules are included; NotificationDefinitions are limited to Platform/Module scope in the query; audit events are limited to no context with a System prefix in the query; owned resources and Runs are skipped.
- **Not recorded:** Runs are limited to no context, and audit events to no context without a System prefix, in the query.

The in-memory match stays as a cross-check. Type and Status filtering keep today's in-memory behavior; CTX-3A does not change it.

**No new persistence.** Every classification derives at query time from existing columns and an Integration lookup. The new pieces are query options only (A12–A14).

---

## 6. Application management

### 6.1 Naming

- **Operator UI:** "Applications" and "Shared".
- **Code, API and docs:** `ResourceContext`.
- **Rail:** unchanged.
- **Config menu:** a new item, **Applications**, placed directly after Inventory. It needs no `RequiredCapability` (§9).

### 6.2 Routes

```text
/config/applications                 List (Active | Archived filter)
/config/applications/new             Create
/config/applications/{id}            Detail / edit          (id = "shared" renders Shared's read-only page)
```

All routes use `ConfigLayout`. Breadcrumbs: Config › Applications › {Name}.

### 6.3 List page

```text
Applications                                          [New application]

Shared
  Resources that aren't specific to one application.
  Integrations in any application can use Shared connections.
  4 integrations · 6 connections · 5 schedules                 [Open]

Applications                                   (Active ▾ | Archived)
  Admissions      2 integrations · 1 connection · 3 schedules   [Open]
  Canvas          1 integration  · 2 connections · 1 schedule   [Open]
  CampusNexus     3 integrations · 2 connections · 4 schedules  [Open]
```

Shared is a **distinct card above the list, not a row in it.** This confirms the original gate's ruling after re-evaluation. A row invites the expectation that it behaves like a row (rename, archive, sort position), and every such action would be disabled. The card also gives Shared's explanatory copy a natural home, which matters for the dumping-ground problem (§12, R3).

Counts are per family, using live counts only; retired or disabled resources appear in the detail page breakdown. Counts come from one summary call (§10). An empty state appears when no Applications exist: "Group integrations, connections and schedules by the application they serve." with a primary [New application] button for managers.

### 6.4 Detail page

- **Header:** Name, Kind label ("Application"/"Shared"), and State pill (Archived only).
- **Overview:** Description, plus Slug under an **Advanced** disclosure (§6.6).
- **Resources section:** three families, each with a live count, a retired/disabled count, and a link to the scoped list, for example "View integrations" → `/integrations?context=<id>`. **No embedded resource tables.** This is not the Resource Catalog (brief §31).
- **Recent activity:** the latest 5 runs by snapshot (`GET /api/runs?resourceContextId=&take=5`, the existing endpoint), with "View all runs" → `/runs?context=<id>`.
- **Actions for managers:** Edit (name, description, slug), Archive/Unarchive, and "Switch view to {Name}".
- **Shared's detail page:** the same layout, with no Edit or Archive actions. It shows the explanatory copy and the note "Shared can't be renamed or archived."

### 6.5 Lifecycle

| Operation | Application | Shared | API |
|---|---|---|---|
| Create | `resource_context.manage` | Never | `POST api/resource-contexts` |
| Rename / description / slug | `resource_context.manage` | 409 (`IsReserved`) | `PATCH api/resource-contexts/{id}` (any subset of the three) |
| Archive | `resource_context.manage`, subject to the refined rule below | 409 | `POST api/resource-contexts/{id}/archive` |
| Unarchive | `resource_context.manage` | n/a | `POST api/resource-contexts/{id}/unarchive` |
| Delete | **None** | None | No route |

**Refined archive rule (fact F4).** An Application may be archived when it owns:

- no Integration whose state is not `Retired`,
- no Connection whose state is `Active`, and
- no Schedule with `Enabled = true`.

Retired Integrations, retired Connections and disabled Schedules stay owned by the archived context. They remain visible under `?context=<id>` and keep their history.

**Guard while archived.** Any transition that would make an owned resource live again is rejected with 409: "{Application} is archived. Unarchive it first." This covers Integration activate, Connection un-retire if such a path exists, and Schedule enable.

**Enumeration completed (CTX-3B archive-concurrency integrity pass, post-Plan-2).** The full set of guarded, blocker-producing transitions, confirmed against `BlocksArchive` (`LiveIntegrations` counts Active **and** Inactive, since only `Retired` doesn't block):
- Integration: Retired → Active, **and Retired → Inactive** (the guard originally only covered the former; widened because `BlocksArchive` treats Inactive identically to Active, so this transition also flips a context from archivable to non-archivable). Active ↔ Inactive is unguarded — both already block, so neither transition changes archive eligibility.
- Connection: confirmed no un-retire/reactivate path exists anywhere (`ConnectionState` is a 2-value enum, `Active`/`Retired`; locked in by a dedicated test that intentionally breaks if this ever changes).
- Schedule: Disabled → Enabled, via both `EnableAsync` and the generic update path (`UpdateScheduleAsync`), guarded identically.

All of the above are additionally serialized against a concurrent Archive via `IResourceOwnershipMutationGate` (the same transaction-owned SQL Server application lock `ResourceContextManagementService.ArchiveAsync` itself acquires before evaluating the ownership summary), closing the race where a concurrent reactivation/creation could commit between Archive's summary read and its commit. Standalone Schedule creation is excluded from this gate: it has no operator/API-reachable path in the current system (`POST /api/schedules` requires a non-empty `IntegrationId`; the Integration-scoped route always supplies one), so there is no live concurrency risk to close, only a latent one if that surface is ever added.

Why this refinement is required rather than optional:

- Under the CTX-1 rule, an Application that has ever owned one Connection or one Schedule can **never** be archived. Connections only retire and Schedules cannot be deleted.
- Even CTX-3B movement cannot help an operator who wants to retire an Application's resources in place rather than re-home them.
- The guard keeps archive meaningful: an archived Application cannot start producing new runs.

Replace `CountOwnedResourcesAsync(id) → int` with:

```csharp
ValueTask<ResourceContextOwnershipSummary> GetOwnershipSummaryAsync(ResourceContextId id, CancellationToken ct);
ValueTask<IReadOnlyDictionary<string, ResourceContextOwnershipSummary>> GetOwnershipSummariesAsync(CancellationToken ct);

public sealed record ResourceContextOwnershipSummary(
    int LiveIntegrations, int RetiredIntegrations,
    int ActiveConnections, int RetiredConnections,
    int EnabledSchedules, int DisabledSchedules)
{
    public bool BlocksArchive => LiveIntegrations + ActiveConnections + EnabledSchedules > 0;
}
```

The plural form runs three `GROUP BY ResourceContextId, <state>` queries in total, regardless of how many contexts exist. There is no per-context N+1.

**Archive refusal UX.** Refusal returns 409 with the summary as its body. The detail page shows:

> **CampusNexus can't be archived yet.** It still has 2 active integrations, 1 active connection and 3 enabled schedules. Retire or disable them first.
> [View active integrations] [View connections] [View schedules]

The links go to scoped lists. When CTX-3B lands, the copy gains "…or move them to another application". Nothing is ever moved or retired automatically.

**While archived:**
- The Application is not selectable in the View menu.
- It is excluded from create pickers.
- It is listed under the Archived filter.
- It still resolves everywhere by id: ownership chips, historical Run cells, Audit filter options (which include archived Applications because history does).

### 6.6 Create semantics

Form fields:
- **Name**: required; duplicates are allowed, as CTX-1 permits.
- **Description**: optional.
- **Advanced ▸ Slug**: prefilled live from Name with the `ResourceContextSlug` normalizer, and editable. It is validated server-side (reserved, taken, unusable), and errors are shown inline.

Slug is kept under Advanced because no slug-addressed route exists. Promoting it would make operators think it matters more than it does.

**On success**, navigate to the new Application's detail page and show a success toast. **Do not switch the view automatically.** This challenges the brief: a new Application is empty by definition, and auto-switching makes every scoped list the operator visits next look empty, which reads as data loss. The detail page offers two primary actions instead:

- **Create an integration in {Name}**, which opens `/integrations/new?context=<id>`
- **Switch view to {Name}**

No parent field. No nesting.

---

## 7. Creation semantics

### 7.1 Principle

> In a specific view, **preselect** that context and show it plainly. In **All**, require an explicit choice. Never let the UI fall back to Shared silently.

The API default of Shared stays, for compatibility (CTX-1). The UI simply never relies on it once Applications exist.

### 7.2 Integration: `/integrations/new`, metadata step

- Add a **Context** select, positioned before Display Name. Options: Shared, then the active Applications. The field is **required**. Help text: "Choose the application this resource belongs to, or Shared."
- Preselection comes from `?context=`, else a specific tab view, else nothing ("Choose…", with submit disabled). Under All there is no preselection.
- A line under the submit button restates the choice: "Creates the integration in **CampusNexus**." This is the stale-state guard: the operator sees the destination at the moment of commit.
- If no active Application exists, the field is hidden and the request omits `ResourceContextId`. Today's behavior is unchanged.
- **Scope key copy fix (fact F7):** move the field under an "Advanced" disclosure and relabel it "Authorization scope key (optional)". Help text: "Advanced authorization scope, unrelated to the Context. Leave empty for instance scope." Its behavior is unchanged.
- The server re-validates (exists + Active) as today. A 400 for an archived context shows "That application was archived. Choose another."

### 7.3 Connection: `/connections/new`

The same rules and field as §7.2.

**In-flow creation during binding** (HUB-1 Connections tab, "create a connection while binding"): the choice is restricted by the reference rule.
- The field is labelled **Context**. The only options are **this Integration's Application** or **Shared**.
- The default is the Integration's Application, with the label "Only this application's integrations can use it." Shared carries the label "Integrations in any application can use it."
- When the Integration itself is Shared, the field is fixed to Shared and shown read-only.

### 7.4 Integration-attached Schedule: hub Schedules tab and `/schedules/new`

- There is **no context selector.** Show a read-only row, "Context: CampusNexus", with the note "Inherited from the integration." CTX-1 inherits the context server-side, and the request omits `ResourceContextId`. The row follows §3.3 and is hidden while no active Application exists.
- On `/schedules/new`, the Integration picker lists Integrations in the current view. A "Show all integrations" toggle is off by default under a specific view and not shown under All. Options are labelled with their Context under All, while an active Application exists.

### 7.5 Standalone Schedule

**None** (fact F3). Standalone rows remain legacy. They show their own ownership on detail and filter correctly in lists, and CTX-3 adds no way to create them.

---

## 8. Movement design (CTX-3B)

### 8.1 Decision: split

Movement is **CTX-3B**. CTX-3A is complete and coherent without it: new resources land in Applications, every list can be scoped, and archive is reachable through retire/disable (§6.5). The honest limitation is that resources backfilled to Shared stay in Shared until 3B. For that reason (D1, §15), CTX-3A is an internal milestone and not a release state. CTX-3B is built directly on it, and the feature launches with both.

### 8.2 Shape

Movement is an explicit operation per resource family, never a field on an update DTO. `UpdateIntegrationRequest` and `UpdateConnectionRequest` must continue to have no `ResourceContextId`. Add a test asserting this.

```text
GET  api/integrations/{id}/ownership/move-preview?targetResourceContextId=
POST api/integrations/{id}/ownership/move          { targetResourceContextId }
GET  api/connections/{id}/ownership/move-preview?targetResourceContextId=
POST api/connections/{id}/ownership/move           { targetResourceContextId }
GET  api/schedules/{id}/ownership/move-preview?targetResourceContextId=
POST api/schedules/{id}/ownership/move             { targetResourceContextId }
```

The service is `IResourceOwnershipMoveService` in `Apperture.ControlPlane.Security`, alongside the other authorizing management services. It needs authorization, the scoped `AppertureDbContext`, and audit.

```csharp
public sealed record ResourceMovePreview(
    string ResourceType, string ResourceId,
    ResourceContextRef Source, ResourceContextRef Target,
    bool CanMove,
    IReadOnlyList<ResourceMoveBlocker> Blockers,
    IReadOnlyList<ResourceMoveEffect> Effects);      // e.g. "3 schedules move with this integration"

public sealed record ResourceMoveBlocker(
    string Code, string Message,
    string? RelatedResourceType, string? RelatedResourceId, string? RelatedDisplayName,
    string? RelatedResourceContextId);
```

The preview is **advisory**. `Move` re-runs the identical validation inside the transaction and never trusts a prior preview.

### 8.3 Common rules

| Code | Condition | HTTP on move |
|---|---|---|
| `target_not_found` | Target id unknown | 400 |
| `target_archived` | Target not Active | 400 |
| `source_archived` | Source archived: allowed. Moving *out* of an archived Application is legitimate cleanup. **Not a blocker.** | — |
| `already_in_target` | Source == target | 400 |
| — | Not authorized: `integration.update`, `connection.update` or `schedule.manage` on the resource. **Not** `resource_context.manage`. | 403 |
| any family blocker | See below | **409**, with the full `ResourceMovePreview` as body |

Moving into **Shared** is a normal target. Every resource state is movable, including Retired, Inactive and Disabled, so archive cleanup works.

### 8.4 Integration move `I : S → T`

In one transaction:

1. `SELECT … WITH (UPDLOCK, ROWLOCK)` the Integration row.
2. For each binding `b` where `b.IntegrationId = I`, load the Connection `c`. Require `ResourceContextReferenceRule.CanReference(T, c.ResourceContextId)`. Otherwise add blocker **`binding_connection_unreachable`** (related: the Connection, its context).
   - Moving to Shared requires every bound Connection to be Shared.
   - Retired Connections are evaluated the same way.
   - A binding whose Connection row is missing adds the advisory effect "binding references a missing connection" and is not a blocker. The move cannot make it worse.
3. Load schedules where `IntegrationId = I`. If any has `ResourceContextId ≠ S`, add blocker **`schedule_context_inconsistent`**. Do not mask pre-existing corruption by "fixing" it in a move.
4. If there are no blockers: set `Integrations.ResourceContextId = T` and set `ScheduleDefinitions.ResourceContextId = T` for all of `I`'s schedules. Add effects "N schedules moved".
5. Write audit (§8.8). Commit.

**Not touched:**
- Bindings (edges; no column).
- Integration-scoped PlatformSettingValues, NotificationDefinitions and AttentionRoutings (derived via `IntegrationId`).
- Secrets.
- **Every CTX-2 historical row:** runs, instances, start requests, ScheduleRuns, groups, events, incidents, notification instances, audit.
- In-flight runs keep their dispatch-time snapshot. The next schedule claim snapshots `T`, because the definition is read at claim time (CTX-2 §3). An active AttentionIncident keeps its Open-time snapshot, since CTX-2 never rewrites it.

### 8.5 Connection move `C : S → T`

In one transaction:

1. Lock the Connection row with `UPDLOCK, ROWLOCK`.
2. Load **all** bindings where `ConnectionId = C`, including those of Retired and Inactive Integrations, using `GetByConnectionIdAsync` semantics under lock.
3. For each binding with `IntegrationId` set, load the Integration. Require `CanReference(integration.ResourceContextId, T)`. Otherwise add blocker **`binding_integration_unreachable`**, listing each affected Integration with its Application.
4. For each binding with `IntegrationId = null` (the legacy/global capability binding that `GetUnownedBySlotAsync` resolves): if `T ≠ shared`, add blocker **`unowned_binding_requires_shared`** with the message "A platform-level binding uses this connection; it must stay in Shared." An instance-global binding must not reference an Application-owned Connection.
5. If there are no blockers, update `Connections.ResourceContextId = T`, write audit, and commit.

Implications:
- Moving to Shared never produces step-3 blockers, since every context can reach Shared.
- Moving Shared → A succeeds only if every bound Integration is in A.
- Moving A → B succeeds only if every bound Integration is in B. In practice that means none, or the Integrations were moved first.

**Confirmation UX:** before submitting, the preview always lists "Used by" Integrations with their Applications, even when `CanMove`. Moving out of Shared is where blast radius must be seen (CTX gate R3).

### 8.6 Schedule move

- If `IntegrationId` is set, add blocker **`schedule_attached_to_integration`**: "Move the integration instead; its schedules move with it." The response includes a link to the Integration.
- If it is standalone (legacy): lock the row, set `ResourceContextId = T`, write audit, commit.

This is API plus a Schedule-detail action only. It is low priority, and legacy rows are expected to be rare.

### 8.7 Races

Movement creates one real race. A binding could be created between the move's validation and its commit. For example, an App-B Integration binds Connection C while C is being moved into App A, which would persist a cross-context binding.

Rule: **ownership-sensitive writes serialize on the owning row's update lock.**
- `BindingService.CreateBindingAsync` must read the Integration and the Connection rows with `UPDLOCK` inside its transaction before applying `ResourceContextReferenceRule`.
- `SqlScheduleManagementService.CreateScheduleAsync`, when attaching to an Integration, must read the Integration row with `UPDLOCK`.
- Move takes the same locks first (§8.4 step 1, §8.5 step 1).

Explicit transactions have house precedent in `SqlSecretStore` and `SqlEntraInstanceRegistrationRepository`, which use `BeginTransactionAsync`.

**Defense in depth.** Add the readiness issue code `binding_cross_context` (anticipated by the CTX gate §4.3) to `IntegrationDispatchReadinessEvaluator`. Any cross-context binding that exists for whatever reason makes the Integration not runnable, with a Fix-now path in the hub Connections tab. It should be unreachable, and it is cheap to have.

Tests: a concurrency test with two contexts that interleaves a move and a bind, proving exactly one succeeds and no cross-context binding is persisted. A readiness test that seeds a cross-context binding directly.

### 8.8 Movement auditing

**Two records per move, sharing one `CorrelationId`, with distinct actions:**

| Record | `Action` | `ResourceContextId` | `Details` (JSON) |
|---|---|---|---|
| Departure | `integration.ownership.moved_out` | **Source** | `{ resourceType, resourceId, displayName, sourceResourceContextId, targetResourceContextId, movedScheduleIds[] }` |
| Arrival | `integration.ownership.moved_in` | **Target** | the same payload |

The pattern repeats as `connection.ownership.moved_*` and `schedule.ownership.moved_*`. Blocked or failed attempts write a single `*.ownership.move_rejected` record with `ResourceContextId = source` and the blocker codes. 403 responses follow existing audit conventions for denied actions.

How this reads in each view:

| View | What the operator sees |
|---|---|
| Audit filtered to the source | "CampusNexus: Integration X moved out to Canvas." The source doesn't lose track of where it went. |
| Audit filtered to the target | "Canvas: Integration X moved in from CampusNexus" |
| Global audit | Two adjacent rows with the same correlation id. They are semantically different events, so this is not double counting. Collapsing by correlation is an optional later Audit UI nicety. |

Rejected alternatives:
- A single record in the target loses the event from the source's filtered view.
- A single record in the source is the reverse.
- A single record with a null context disappears from both.
- Rewriting any earlier record is forbidden.

Attached schedules are listed in `movedScheduleIds` rather than getting per-schedule records, to avoid a burst of N×2 rows. As a result, an Audit search by `ScheduleId` will not surface the move, which is acceptable because Schedule detail shows current ownership.

For the Integration/Connection identity field on the record, follow the shape `IntegrationSettingsService` already uses.

**Transactionality:** if `SqlAuditService` shares the request-scoped `AppertureDbContext`, append inside the move transaction. Otherwise append immediately after commit. **Never roll back a committed move because audit failed**; log the failure at Error. Sonnet should verify which applies.

### 8.9 Movement UI (3B)

- **Integration hub:** an overflow action "Move to another application…" opens a dialog. Pick the target (Shared + active Applications, excluding the current owner), load the preview, and show effects and blockers with links. Submit is enabled only when `CanMove`. The dialog carries a "History stays with {Source}" note.
- **Connection detail:** the same dialog, always showing "Used by".
- **Schedule detail:** for attached schedules, show a disabled action explaining "moves with its integration" and linking to it. For legacy standalone schedules, show the dialog.
- **Archive refusal copy** gains move links (§6.5).
- After a move, the page stays put. The ownership chip updates, and if the view now mismatches, the §4.6 indicator appears. The view does not switch.

---

## 9. Authorization boundary

### 9.1 Adds (instance-scoped only)

```csharp
ResourceTypeConstants.ResourceContext = "ResourceContext";
ActionConstants.ResourceContextManage = "resource_context.manage";   // create, rename, re-slug, describe, archive, unarchive
```

- Registered in `PlatformPermissionSeeder.SeedCore`: `ScopeKeyConstants.Instance`, `ResourceFamily.Administrative`.
- Added to `PlatformAdminRolePermissionRepair` as a new `RequiredResourceContextPermissions` set, applied in `ApplyAllRepairs`.
- Surfaced as `OperatorCapabilitiesDto.CanManageResourceContexts`, evaluated like its siblings in `OperatorCapabilitiesQueryService`. Update the positional `DenyAll` constructor.
- Naming uses `resource_context.manage` to follow the `managed_contact.*` multi-word convention.
- It does not reuse `platform.settings.manage`, which governs something else.

Authorization is enforced in a new `ResourceContextManagementService` in `ControlPlane.Security`. It evaluates the permission and writes audit, then delegates to `IResourceContextService`, which by design performs no authorization and keeps that separation.

Lifecycle audit actions: `resource_context.created`, `.updated` (details list changed fields), `.archived` and `.unarchived`, with `ResourceContextId` = the context itself.

### 9.2 Deliberately no read permission

`GET api/resource-contexts` (list/get) requires only an authenticated operator, like `GET api/menus`:

- Context names are needed to render any owned resource, the selector and ownership chips. An operator who can see an Integration but not its Application's name gains nothing and loses orientation.
- Ids are already exposed on every CTX-1/CTX-2 DTO.
- A `resource_context.read` permission would be one that every operator role must hold. That is a permission without a decision behind it.

**Counts are the exception.** The summary endpoint returns per-family counts only for families the caller can read: Integrations need `integration.read`, Connections need `connection.read`, and Schedules need `schedule.manage`, which is today's gate on `GET api/schedules`. Other families come back as `null` and render as "—". Counts must not leak the existence of resources an operator cannot list.

### 9.3 Explicitly not added

- No `context:{id}` scope key.
- No context-scoped role assignments, roles, grants or `OperatorCapabilities`.
- No list filtering by context authorization.
- View scope never enters any authorization call.
- Moving a resource requires the resource family's existing update permission, not `resource_context.manage`.

**Deliberate test change:** CTX-1's `ResourceContextAuthorizationNonChangeTests` asserts that no `ResourceContext` resource type exists. CTX-3 must update that single assertion **in a commit that says why**: the type now governs managing the context rows themselves. The companion assertions ("no `context:` scope-key form", "governed identities ignore ownership") must stay unchanged and green.

---

## 10. API and query changes

The list below is the complete set, and nothing beyond it is needed. There are no generic resource APIs.

### 10.1 CTX-3A

| # | Change | Where |
|---|---|---|
| A1 | **Client contract parity** (F1): add `ResourceContextId` + `ResourceContext` (`ResourceContextRef`) to client `IntegrationDto` and `ConnectionDto`; `ResourceContextId` to client `RunSummaryDto`, `RunQueryFilter`, `IntegrationFleetRowDto`, `CreateIntegrationRequest`, `CreateConnectionRequest`; `contextId` overloads on `GetIntegrationsAsync`/`GetConnectionsAsync`/`GetSchedulesAsync`; `context` on `IntegrationFleetQueryParams` | `Apperture.Management.Client` |
| A2 | **Contract parity test:** reflection test that every property on each server DTO in A1 exists on the client twin | `Apperture.UnitTests` |
| A3 | `GET api/integrations/fleet?contextId=`: 400 when unknown; passes the filter into `ListIntegrationsAsync(filter)`; fleet row gains `ResourceContextId` | `IntegrationsController`, `IntegrationFleetQuery`, `IntegrationReadinessService.ListFleetAsync` |
| A4 | `ResourceContextsController` at `api/resource-contexts`: `GET` (`includeArchived`, default false), `GET {id}`, `POST`, `PATCH {id}`, `POST {id}/archive` (409 + `ResourceContextOwnershipSummary`), `POST {id}/unarchive`, `GET summary` (all contexts' ownership summaries, permission-masked per §9.2) | New controller + `ResourceContextManagementService` |
| A5 | `GetOwnershipSummaryAsync` / `GetOwnershipSummariesAsync` replace `CountOwnedResourcesAsync`; archive rule refined; reactivation guards (§6.5) | `IResourceContextRepository`, `SqlResourceContextRepository`, `ResourceContextService`, Integration/Connection/Schedule management services |
| A6 | `GET api/control/center?resourceContextId=` threaded into `IControlActivityQueryService.ListRecentActivityAsync` and `IControlAttentionQueryService.ListAttentionAsync`; rejected-control items joined to `OperationRuns.ResourceContextId` | `ControlCenterController`, `ControlCenterQueryService`, activity/attention services, client `GetControlCenterAsync` |
| A7 | `GET api/control/audit?resourceContextId=` passed into `AuditFilterCriteria.ResourceContextId` (added and threaded to `AuditQuery.FilterByResourceContextId` by A13); client `GetAuditTimelineAsync` gains the parameter | `AuditController`, client |
| A8 | Integration eligible-connections response gains `ResourceContextId` (to label "This application"/"Shared" in the picker); client `EligibleConnectionDto` updated | `IntegrationBindingsController`, client |
| A9 | `ConnectionUsedInEntry` gains `IntegrationResourceContextId`, resolved from the already-loaded Integration and the once-per-request dimension map | `ConnectionsController`, client |
| A10 | `OperatorCapabilitiesDto.CanManageResourceContexts`; permission seeding and repair | §9.1 |
| A11 | `all` added to `ResourceContextSlug` reserved set | Domain + slug tests |
| A12 | `GET api/inventory?context=`: `all` (default) / `system` / `shared` / `notRecorded` (Activity only) / a real active or archived `ResourceContextId`; anything else, or `notRecorded` in Resources mode, returns 400. Each `InventoryItemDto` gains `ResourceContextId` and a classification (`system` / `shared` / `application` / `notRecorded` / `unknown`). The Context predicate is applied inside each capped source query (§5.5) | `InventoryController`, `InventoryQueryService`, `InventoryItemDto`, client `GetInventoryAsync` |
| A13 | `AuditTimelineEntry`/`AuditTimelineEntryDto` gain `ResourceContextId` from the existing `AuditRecord.ResourceContextId`. `AuditFilterCriteria` gains `ResourceContextId`, threaded into the existing `AuditQuery.FilterByResourceContextId`, which `SqlAuditService` already applies but `AuditTimelineQueryService` never passed. Read-side only; A7 consumes it | `AuditTimelineModels.cs`, `AuditTimelineQueryService`, `AuditController` DTO mapping, client |
| A14 | Query-only source filters for §5.5, no schema change: an `AuditQuery`/`AuditFilterCriteria` context-classification filter (no context, with or without a System action prefix, from one shared `AuditSystemActionPrefixes` list); `RunQueryFilter.ResourceContextIdIsNull` (server and client twin, for A2 parity); `NotificationDefinitionListQuery.OwnerResourceContextId` (Integration join) and `SystemScopedOnly`; a batched Integration owner-context lookup for integration-scoped NotificationDefinitions | `SqlAuditService`, `RunQueryService`, `SqlNotificationDefinitionStore`, notification definition service |

### 10.2 CTX-3B

| # | Change |
|---|---|
| B1 | Six `ownership/move-preview` + `ownership/move` endpoints (§8.2) and `IResourceOwnershipMoveService` |
| B2 | Row-lock serialization in `BindingService.CreateBindingAsync` and attached-schedule creation (§8.7) |
| B3 | Readiness issue code `binding_cross_context` |
| B4 | Move audit actions (§8.8) |

### 10.3 Not changed

Detail DTOs (they already carry `ResourceContextRef`); Runs/NotificationInstances/PlatformEvents filters (they already exist; A14 adds query-only options for Inventory, not new filter semantics for existing callers); `ScopeKey` semantics; ID-based routes; module persistence; Business Data projection.

---

## 11. Business Data transitional behavior

Current state (verified): `/business-data` states that "Collections are not owned by the integrations that process them". Customers is a demo projection with a Demo badge and scope banner. It is not ResourceContext-owned (CTX-1 §10).

CTX-3 behavior:

- Business Data routes are `Unscoped`. The selector is muted with the standard tooltip.
- When the view is a specific Application or Shared, both `/business-data` and `/business-data/customers` show one info message:
  > *"Customers is a demo collection for the whole instance. It isn't organized by application yet, so your view doesn't filter it."*
  Under All, no message is shown.
- **Not done:** assigning Demo Customers to Shared, filtering customers by the Runs/Groups that produced them, adding an Application column, or any `BusinessRecord` or collection entity.
- The DAT path stays intact and untouched: `Record ← OperationItem ← OperationGroup.ResourceContextId ← Run`.

Deriving "customers touched by CampusNexus" through OperationGroup snapshots was considered and rejected for CTX-3. It would present a *provenance* join as *ownership*, which is exactly the fake context behavior the brief forbids, and DAT will define the real semantics.

---

## 12. Red-team findings

| # | Risk | Mitigation |
|---|---|---|
| R1 | **Hidden global state**: lists silently filtered by something set last week | URL-authoritative on scoped pages (§4.2). The selector is always visible with its current value. Filtered empty states name the filter and offer "View all" (R6). |
| R2 | **Deep link opens somewhere unexpected** | Detail pages are never scoped and never auto-switch the view. A mismatch indicator appears instead (§4.6). Scoped-page links carry `?context=`. |
| R3 | **Shared becomes the old global namespace** | No silent Shared default in UI create (§7.1). Shared gets an explanatory card, not a row (§6.3). In-flow Connection creation defaults to the Integration's Application, not Shared (§7.3). Connection move previews always show "Used by" (§8.5). Lists never merge Shared into an Application (§5.2). Copy is neutral ("not specific to one application"), not "miscellaneous". |
| R4 | **Back/forward inconsistency** | `replace: true` on resolution, so every history entry of a scoped page is self-describing. The selector re-syncs from the URL on `LocationChanged` for `Scoped` routes. |
| R5 | **Context leaking across tabs** | Tab state is per tab, and localStorage is read only at tab start. There are no `storage` event listeners. |
| R6 | **"No integrations" misread as data loss** | A scoped empty state always reads "No integrations in CampusNexus", with actions [View all] and [Create integration in CampusNexus]. It is never the unscoped "No integrations are configured yet". |
| R7 | **Creation in the wrong Application from stale state** | Required explicit choice under All. A visible preselection under a specific view. The destination is restated beside the submit button. Server re-validation covers archived contexts (§7.2). |
| R8 | **Selected Application archived** | Fallback to All with a one-time toast for remembered state. An explicit URL is honoured with a banner (§4.3). Archived Applications cannot be chosen. |
| R9 | **All vs Shared ambiguity** | Separate menu rows with descriptions: "Everything you can see" vs "Not specific to one application". Shared has its own card and detail page. `All` is never a data value (§2.2). |
| R10 | **Control Center numbers change meaning silently** | Split pill groups ("In view" / "Platform — all applications"), a scope-aware subtitle, and the pre-history note (§5.1). |
| R11 | **Pre-CTX-2 runs vanish under a specific view** | An explicit note on Runs and Control Center (§5.1, §5.3). This is correct CTX-2 semantics, not backfilled to Shared, so it must be explained rather than "fixed". |
| R12 | **Selector read as security isolation** | The label is "View", not "Workspace". The tooltip says it doesn't change access. There is no lock iconography. Pages outside the view remain one click away and reachable by link (§3.1). |
| R13 | **ScopeKey vs Application confusion on create forms** (F7) | Scope key is moved under Advanced and relabelled "Authorization scope key" with unrelated-to-application help text (§7.2). |
| R14 | **Client/server DTO drift recurs** (F1; CTX-2 already hit it once) | Reflection-based contract parity test (A2). |
| R15 | **Archive impossible in practice** (F4) | Refined live-resource rule with a reactivation guard (§6.5). |
| R16 | **Move vs bind race** creates a cross-context binding | Row-lock serialization plus the `binding_cross_context` readiness code (§8.7). |
| R17 | **Too many Applications** | Search above 7 active Applications. The list is name-sorted. The summary endpoint costs O(1) queries. |
| R18 | **Flash of unfiltered data before the view resolves** | Scoped pages build their request only from a validated `?context=` value and send nothing until the URL carries one (§4.2, §4.4). They never build a request from in-memory view state, so in-app navigation cannot issue the previous view's request first. |
| R19 | **Existing single-team installations get new chrome for nothing** | The selector and ownership labels are hidden until the first Application exists (§3.3). |
| R20 | **Menu hrefs don't carry state** | Handled by resolution step 2 (§4.2). Menu definitions stay static, so `ControlPanelMenuDefinitions` and `MenusController` do not change for the view. |
| R21 | **Fleet endpoint N+1** (per-Integration binding/schedule reads in `ListFleetAsync`) | Pre-existing and not worsened. Scoping *reduces* it. It is noted for a later performance pass and is not a CTX-3 goal. |
| R22 | **CTX-1 non-change test now fails** | One deliberate assertion update with rationale; the companion assertions stay (§9.3). |

**Alternatives considered and rejected:**
- **Per-page filters only (no shell selector):** no hidden state, but the operator re-selects the Application on every page, and it never becomes an Application-centred product. The URL-authoritative selector keeps the explicitness and adds continuity.
- **Application-prefixed routes (`/apps/{slug}/integrations`):** fully explicit, but duplicates every collection route, competes with canonical ID routes, and binds URLs to mutable slugs with no alias table (CTX-1 §1). Revisit if slug addressing is ever adopted.
- **Applications as a fifth rail domain:** amends ADR-0040 to hold one administrative page plus duplicated scoped lists. There is no evidence the four domains can't carry a cross-cutting dimension.

---

## 13. Implementation sequence

### CTX-3A: Application workspaces

Each step is independently reviewable and green.

1. **Contracts and domain (no UI):** A1, A2 (client parity + test), A11 (reserve `all`), `ResourceContextViewScope` and its parser tests, and the `ResourceContextViewRoutes` route table with tests.
2. **Ownership summary and archive refinement:** A5, including reactivation guards and tests for each guarded transition. Update the CTX-1 archive tests to the refined rule.
3. **Authorization:** A10, `ResourceContextManagementService` (authorize + audit + delegate), and the deliberate update to `ResourceContextAuthorizationNonChangeTests`.
4. **Management API:** A4, with controller tests for list, get, create, patch, reserved-409, archive-409 body, unarchive, and permission-masked summary.
5. **Config → Applications UI:** menu item, list with Shared card, create, and detail/edit/archive/unarchive (§6).
6. **Shell view state and selector:** `ResourceContextViewState`, localStorage per user, resolution algorithm, selector component, visibility rule, one-time hint (§3, §4). Bunit/unit tests for resolution cases §4.3.
   > **HSK-A**: The one-time hint feature was removed after discovery matured.
7. **Scoped lists:**
   1. Integrations: A3 fleet filter, Application column, scoped empty state.
   2. Connections: Shared-available notice.
   3. Schedules.
   4. Runs: snapshot filter, `BuildRunsListUrl` context, pre-history note.
   5. Config Inventory: local Context filter and column (§5.5) — independent of the shell view, grouped here because it shares this step's ownership-classification work, not because it is view-scoped.
8. **Detail indicators:** Context chips and mismatch notices on the Integration hub (including schedule-diagnostics), Connection, Schedule and Run detail. A8/A9 Context labels in the binding picker and "Used by".
9. **Create flows:** Integration, Connection (including in-flow binding creation) and Schedule inherited display; ScopeKey copy fix (§7).
10. **Audit filter:** A7 and the page filter with "Use current view" chip (§5.4).
11. **Business Data banner** (§11).
12. **Control Center partial scoping:** A6 and the UI grouping (§5.1). This is last because it is the one slice that can be cut to a banner without affecting anything else.
13. **Docs:** update `control-panel-ui.md` (View selector, Config → Applications), and add a short ADR-0040 note confirming the rail is unchanged and the selector is shell chrome. Write the CTX-3A closure.

### CTX-3B: Resource movement

1. `IResourceOwnershipMoveService` preview logic, fully unit-tested per blocker code (§8.4–§8.6).
2. Transactional move with locks, plus the B2 serialization in binding and schedule creation, plus the concurrency test.
3. B3 readiness code.
4. B4 audit (two-record model) with source, target and global query tests.
5. B1 endpoints and client methods.
6. Move dialogs (§8.9) and the archive-refusal copy update.
7. **Integration hard-delete referential inventory** (must be finished before CTX-3B is closed). CTX-3A verified that `DeleteIntegrationAsync` blocks only bindings and schedules, and that `NotificationDefinitions.IntegrationId` has no foreign key, so deleting an Integration leaves its Integration-scoped NotificationDefinitions and subscriptions behind. List every current-state (non-historical) table that references `IntegrationId` and which of its rows a hard delete can orphan, including at least that NotificationDefinition/subscription case. Then decide per dependent, deliberately, between blocking delete while current-state dependents exist and cleaning them up, and preserve historical records that are meant to survive. Do not add a one-off NotificationDefinition-only guard before this inventory. Until then, Inventory shows orphaned definitions as "Unknown context" (§5.5).
8. CTX-3B closure.

---

## 14. Acceptance criteria

### CTX-3A

**Model and persistence**
- `ResourceContextViewScope` exists in `Ui.Shared`. `All` is never represented as a ResourceContext row, id, Shared, or ambiguous null. `ToApiFilter()` returns null for All. Tests cover parsing `all`, an id, empty, and a whitespace value.
- `all` is a reserved slug.
- With no active Application, the selector and ownership labels are not rendered, the view is All, and every existing page behaves exactly as before (existing UI tests unchanged and green). Config → Inventory's Context column and filter are the one exception (§5.5).
- On every `Scoped` route, the rendered URL always contains `?context=`, and back/forward restores the view of each entry.
- An explicit selector change writes `localStorage["apperture.view.{userId}"]`. A `?context=` arriving by URL does not.
- A remembered archived or unknown context falls back to All with a one-time notice. An explicit URL naming an archived context is honoured with a banner. An unknown URL context shows a not-found message with "View all".
- Opening any detail or hub route never changes the view. A mismatch indicator appears only when the view is a specific context different from the resource's owner. "Switch view" changes it.
- Every displayed context name resolves active and archived contexts by id. An id still missing after one refresh of the context list shows "Unknown context", never a raw id or blank text.

**Surfaces**
- `/integrations`, `/connections` and `/schedules` under a specific view request only that context's owned resources through the SQL-side filters. Under All they send no filter and match today's sets.
- Scoped collection pages build their request only from a validated `?context=` value and reload when it changes. A page never sends a request for one context and then a corrective one for another, including after in-app navigation.
- `/connections` under an Application view never includes Shared rows, and shows the Shared-available count link.
- `/runs?context=X` filters by `OperationRuns.ResourceContextId`. A test proves a run snapshotted in A still lists under A after its Integration's current ownership becomes B. Null-snapshot runs appear only under All, and the note is shown.
- The status facet change on `/runs` preserves `context`.
- Audit has its own Context filter, sends `resourceContextId`, and is not driven by the shell view.
- Business Data is never filtered and shows the transitional message under a specific view.
- Config Inventory is never filtered by the shell view. Its own Context column and filter are always present, even with no Applications (§5.5). Values: All, System, Shared, Not recorded (Activity only), and each active or archived Application; an unknown id or an inapplicable value returns 400 and the page offers "Reset to All". Selecting a value shows exactly the matching rows, with no merging, and the Context predicate reaches each capped source before its cap, so no value produces a false empty result. System is never a persisted `ResourceContext` row or id, and Runs are never System. Not recorded means a context-eligible occurrence with no recorded context. No new persistence is added. An integration-scoped NotificationDefinition whose Integration no longer exists shows "Unknown context", appears only under All, and is never System or Not recorded.
- Control Center under a specific view filters run-derived attention and activity by snapshot. It always shows stuck-shutdown items and all platform/worker/scheduler sections under a "Platform — all applications" heading, and its pills are grouped "In view" / "Platform". *(If step 12 is cut: Control Center is `Unscoped` with the banner, and this criterion is replaced by that.)*

**Management**
- `/config/applications` lists active Applications with live counts and shows Shared as a separate card. An Archived filter lists archived Applications.
- Create derives the slug live under Advanced, rejects reserved, taken and unusable slugs inline, lands on the detail page, and does **not** switch the view.
- Shared's detail page offers no rename, slug, description edit or archive. API attempts return 409.
- Archive succeeds when the Application has no non-retired Integration, no active Connection and no enabled Schedule. Otherwise it returns 409 with `ResourceContextOwnershipSummary`, and the UI shows counts and scoped links. Nothing is moved or retired automatically.
- While archived, activating an owned Integration, un-retiring an owned Connection (if that path exists) and enabling an owned Schedule are rejected with a message naming the Application.
- Archived Applications are absent from the selector and create pickers, and still resolve by id everywhere they are displayed.
- The summary endpoint issues a constant number of queries, independent of context count (asserted). It returns null counts for families the caller cannot read.

**Creation**
- Under All, Integration and Connection create require an explicit Context choice before submit. Under a specific view, it is preselected and changeable. The destination is restated near submit. With no Applications, no field is shown and the request omits `ResourceContextId`.
- In-flow Connection creation from an Integration offers only that Integration's Application or Shared, defaulting to the Integration's.
- Schedule creation shows the inherited Context read-only ("Context: CampusNexus", inherited from the Integration) and sends no `ResourceContextId`. No standalone schedule creation path is added.
- Scope key is under Advanced, labelled as an authorization scope, with unchanged behavior.

**Authorization and contracts**
- `resource_context.manage` on `ResourceContext` at instance scope is seeded, repaired into Platform Admin, and surfaced as `CanManageResourceContexts`. Every mutating ResourceContext endpoint enforces it. List/get require only authentication.
- No `context:` scope key, context-scoped role, grant or capability exists. The CTX-1 non-change tests stay green, except the one deliberately updated resource-type assertion.
- ResourceContext lifecycle operations write audit with `ResourceContextId` = that context.
- The client/server DTO parity test passes for all A1 types.
- Full unfiltered `Apperture.UnitTests`, `Apperture.Management.Ui.Shared.Tests`, `Apperture.ArchTests` and `Apperture.IntegrationTests` are green. A browser smoke walks: create Application → selector appears → create Integration in it → scoped Integrations/Runs → open a Shared Connection from the Application view (mismatch indicator) → archive refused → retire/disable → archive succeeds → selector falls back to All.

### CTX-3B

- No update DTO accepts `ResourceContextId` (asserted). Movement exists only as the §8.2 endpoints.
- Integration move: blocked with `binding_connection_unreachable` for every bound Connection not in the target or Shared, and with `schedule_context_inconsistent` on pre-existing drift. On success the Integration and all its schedules are in the target within one transaction; bindings, settings, notification definitions, routings and secrets are unchanged.
- Connection move: blocked with `binding_integration_unreachable` listing every affected Integration (including Retired/Inactive), and with `unowned_binding_requires_shared` for any `IntegrationId = null` binding when the target is not Shared. Moving to Shared is never blocked by bindings.
- Schedule move: attached schedules are rejected with `schedule_attached_to_integration`; legacy standalone schedules move.
- Preview and move share one validator. Move revalidates in-transaction and returns 409 with the preview body when blocked.
- **No historical row changes on any move.** Tests assert Run, ScheduleRun, OperationGroup, PlatformEvent, AttentionIncident, NotificationInstance and AuditLog snapshots are byte-identical before and after.
- The concurrency test proves an interleaved move and bind cannot persist a cross-context binding.
- `binding_cross_context` readiness makes an Integration with a seeded cross-context binding not runnable.
- Each successful move writes exactly two audit records (`moved_out` in the source, `moved_in` in the target) sharing a correlation id. A blocked move writes one `move_rejected` record in the source. Filtering Audit by source and by target each returns exactly its record.
- Move dialogs show preview effects and blockers with links, always show "Used by" for Connections, and never switch the view.
- The Integration hard-delete referential inventory (§13 CTX-3B step 7) is complete. Every current-state dependent it lists is either protected by a delete guard or cleaned up intentionally, with a test for each, and historical records survive.

---

## 15. Product decisions

The three open questions from the first draft of this gate were decided on 2026-09-13. None remain open.

**D1. CTX-3A and CTX-3B: separate engineering, one product rollout.**
- The engineering work is split, because movement carries materially more risk: transactions, locking, race prevention, preview validation, two-sided audit, and three move semantics.
- CTX-3A is merged and tested first. CTX-3B is built on it immediately afterwards.
- **The Application feature is not considered launched until CTX-3B is complete.** Existing installations should never be left where Applications exist but every pre-CTX resource is stranded in Shared.
- For implementers: a merged CTX-3A with no CTX-3B is an internal milestone, not a release state. CTX-3A closure must not describe the feature as shipped.

**D2. The relaxed archive rule (§6.5) is accepted.**
- "Nothing live" replaces "owns nothing". An archived Application keeping retired Integrations, retired Connections and disabled Schedules is semantically correct.
- **The reactivation guard is an equal, required half of the rule.** Owned resources cannot be reactivated or enabled until the Application is unarchived. Without it, archive is cosmetic.
- The inability to delete Connections and Schedules will be addressed in later work. CTX-3 must not add delete capabilities to work around it.

**D3. The View selector is hidden until the first active Application exists (§3.3).**
- This preserves today's experience for installations that never adopt Applications, and avoids a meaningless All/Shared choice before it matters.
- Before the first Application, discoverability comes from Config → Applications. When the first Application is created, the selector appears together with the one-time hint.
  > **HSK-A**: The one-time hint feature was removed after discovery matured.
