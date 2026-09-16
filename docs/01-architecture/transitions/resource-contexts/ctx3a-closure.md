# CTX-3A closure — Application Workspaces

Date: 2026-09-15 (post-closure stabilization addendum added same day — see §8)
Status: **Complete as an internal engineering milestone. CTX-3B may begin.**
Branch: `resource-contexts` — CTX-3A landed as a sequence of 71 commits between `94b19bfb` (design gate + plan) and `f8bd3ad` (this closure's last content commit). Six real defects were then found via the manual smoke checklist this closure itself mandated (§7) and fixed in four further commits, `a123896`..`a8f098a` — see §8.
Design basis: [`ctx3-design-gate.md`](./ctx3-design-gate.md)

---

## 1. Executive result

**CTX-3A is an internal Application-workspaces milestone. The Application feature is not considered fully shipped until CTX-3B Resource Movement is complete.** Existing resources backfilled to Shared by CTX-1 cannot yet be moved into an Application — that capability is CTX-3B's entire scope, and until it lands, an operator who creates an Application today gets an empty workspace that only fills up through new creation, not through reorganizing what already exists.

What CTX-3A proved, concretely: the shell View selector (All / Shared / per-Application) is wired end-to-end from URL/tab-state/localStorage resolution through every scoped list (Integrations, Connections, Schedules, Runs) and into Control Center's run-derived sections; Application lifecycle (create, rename, archive with the refined "nothing live" rule, reactivation guard) is implemented and authorization-gated; every operator-facing creation flow (Integration, Connection, in-flow Connection, Schedule) either requires an explicit Context choice or correctly inherits one, with no path that silently defaults new resources into Shared — including under a directory-load failure, which a dedicated stabilization pass (commit `9528035`) closed after Plan 5's final review surfaced it; Audit and Config Inventory each carry their own independent Context filter, deliberately not driven by the shell View; and Business Data remains transitionally unscoped with an explanatory notice. Full validation (five test suites, seven individual project builds, one full-solution build) is green — see §7.

## 2. View model

`ResourceContextViewScope` (in `Apperture.Management.Ui.Shared`) is the pure value type distinguishing `All` (never a persisted ResourceContext) from a concrete scope naming Shared or an Application id. `ResourceContextViewRoutes.Resolve(path)` classifies every route as `Scoped` (Integrations/Connections/Schedules/Runs — participate in URL-authoritative resolution and rewriting), `Partial` (Control Center — reads the current view but never rewrites its own URL), `Detail` (resource detail/hub pages — show an ownership chip and mismatch notice, never auto-switch), or `Unscoped` (everything else, including Audit, Inventory, Business Data, and every `/new` creation route).

`ResourceContextViewResolver.Resolve` implements the four-branch algorithm: an explicit `?context=` wins outright; a set tab-state scope is used next (as of the post-Plan-5 stabilization pass, `9528035`, this branch is now revalidated against the current active/archived directory the same way localStorage already was — a scope that goes stale mid-tab, e.g. its Application is archived elsewhere, now correctly falls back to All with a notice rather than resolving to a dead reference indefinitely); localStorage is read and validated last; with nothing valid anywhere, the result is All. `ResourceContextViewState` (in `Apperture.Management.Ui.Client.Services`, not `Ui.Shared` as the design gate's own file-location sketch names it — a confirmed, harmless drift) is the stateful wrapper performing the JS-interop/localStorage/`NavigationManager` side effects around that pure resolver, and additionally tracks `ResourceContextDirectoryStatus` (`Loading` / `Loaded` / `Unavailable`) — added by the stabilization pass specifically so a failed context-list fetch is never indistinguishable from "successfully loaded, zero Applications exist," closing a silent-Shared-fallback path in the three creation flows that gate on it.

## 3. Application management

`Config → Applications` provides the full lifecycle: create (name + optional description, slug live-derived and editable under Advanced), rename/describe/re-slug, and archive under the refined "nothing live" rule — an Application archives successfully once it owns no non-retired Integration, no active Connection, and no enabled Schedule, rather than CTX-1's original "owns nothing at all" rule, which was unreachable in practice once any Connection or Schedule had ever been created (Connections only retire, Schedules have no delete path). The reactivation guard is the rule's required other half: while archived, none of an owned Integration's activation, a Connection's un-retirement, or a Schedule's enable path succeed — verified by the reactivation-guard test suite, which is part of the green `Apperture.UnitTests` run in §7. `resource_context.manage` is the one new permission this epic introduces, seeded into Platform Administrator and surfaced as `CanManageResourceContexts`; list/get access requires only authentication, matching every other read surface's convention.

## 4. Scoped surfaces

Obey the shell View (via `?context=`, filtered server-side): **Integrations**, **Connections** (ownership-only, never merging in Shared), **Schedules**, **Runs** (filtered by the CTX-2 historical snapshot on `OperationRuns.ResourceContextId`, never by an Integration's current owner), and **Control Center's run-derived half** (recent activity, recent problems/failed executions, rejected external controls — the last via a conditional join to `OperationRuns`, added in Plan 6).

Deliberately unscoped, each for its own stated reason: **Audit** (its own independent Context filter, with an opt-in "Filter to \<view\>" chip that never auto-applies — most audit rows legitimately carry no context, so shell-scoping it would misleadingly read as "almost no audit trail"); **Config Inventory** (its own local Context filter and column, present even with zero Applications — the one intentional exception to the "hide chrome until Applications exist" rule); **Business Data** (transitional, unscoped, with an informational notice under a specific view); **Control Center's platform-global half** (worker/runtime infrastructure, scheduler/runtime health, stuck-shutdown attention items — always instance-wide, grouped and labelled "Platform — all applications," since `runtimeShutdownQueuedStuck` specifically has no `ResourceContextId` column to filter by at all); **Execute** and **Notifications** (unchanged, no CTX-3A scoping work done).

## 5. Creation

Integration and Connection creation require an explicit Context choice under the All view (submit stays disabled until one is chosen) and preselect under a specific view or an honoured `?context=` link; the destination is restated verbatim near the submit button ("Creates the integration/connection in **\<Context\>**."). With zero active Applications anywhere in the instance, the Context field is hidden entirely and the request omits `ResourceContextId` — today's pre-CTX-3A behavior, preserved byte-for-byte. In-flow Connection creation (from an Integration's binding panel) offers only that Integration's own Context or Shared, defaulting to the Integration's, with a genuinely non-interactive fixed row when the Integration itself is Shared. Schedule creation has no independent Context selector anywhere — both the Integration hub's Schedules tab and the standalone `/schedules/new` picker show a read-only "Context: \<name\> — Inherited from the integration." row, and `/schedules/new`'s Integration picker filters by the current view with a "Show all integrations" toggle (off by default, absent under All). The post-Plan-5 stabilization pass additionally ensures none of these three creation flows can silently omit `ResourceContextId` merely because a transient directory-load failure makes the Context field look identical to "confirmed zero Applications" — they now show an explicit "unavailable, retry" state and block submission instead.

## 6. Authorization

`resource_context.manage` is the sole new permission, scoped at the instance level (`ScopeKeyConstants.Instance`), seeded in `PlatformPermissionSeeder.SeedCore` and repaired into Platform Administrator via `PlatformAdminRolePermissionRepair`. No context-scoped IAM was introduced anywhere in CTX-3A: no `context:{id}` scope-key form, no context-scoped role, grant, or capability exists. `ResourceContextAuthorizationNonChangeTests` (part of the green `Apperture.UnitTests` run) carries one deliberately-updated assertion — the prior "no `ResourceContext` resource type exists at all" assertion was narrowed to reflect that the type now legitimately governs *managing the context rows themselves*, with a commit message recording why — while its companion assertions ("no `context:` scope-key form," "governed identities ignore ownership") remain unchanged and green.

## 7. Validation

Run 2026-09-15, on the current `resource-contexts` HEAD (`f8bd3ad`):

| Check | Result |
|---|---|
| Full solution build (`dotnet build` from repo root) | ✅ 0 errors, 44 warnings (all pre-existing, unrelated to CTX-3A) |
| `Apperture.UnitTests` (full, unfiltered) | ✅ 2159/2159 passed |
| `Apperture.Management.Ui.Shared.Tests` (full) | ✅ 831/831 passed |
| `Apperture.Management.Ui.Client.Tests` (full) | ✅ 19/19 passed |
| `Apperture.ArchTests` (full) | ✅ 38/38 passed |
| `Apperture.IntegrationTests` (full) | ✅ 204/204 passed |
| `Apperture.Management.Api` (individual build) | ✅ 0 errors, 0 warnings |
| `Apperture.Management.Host` (individual build) | ✅ 0 errors, 0 warnings |
| `Apperture.RuntimeHost.Windows.WorkerHost` (individual build) | ✅ 0 errors, 0 warnings |
| `Apperture.Persistence.SqlServer` (individual build) | ✅ 0 errors, 0 warnings |
| `Apperture.Management.Client` (individual build) | ✅ 0 errors, 0 warnings |
| `Apperture.Management.Ui.Shared` (individual build) | ✅ 0 errors, 0 warnings |
| `Apperture.Management.Ui.Client` (individual build) | ✅ 0 errors, 0 warnings |

**Total: 3251 tests across five suites, 0 failures.** No environmental failures were folded into this summary — every result above is a real, witnessed run from this closure session, not a filtered or assumed pass.

**Browser smoke: not performed.** Browser automation tooling (`claude-in-chrome`) is available in this environment, but the local dev host was not confirmed running the correct application at the time of this closure pass — a probe found an unrelated "Integration Center Setup" service on the usual local port, and this session's own subagents had twice stopped the actual dev host mid-session to unblock file-locked builds (both incidents disclosed to the user as they occurred). Rather than guess at restarting an environment this session does not own the launch command for, this closure documents the required manual walkthrough below for the user (or a session with a confirmed-correct running instance) to execute before CTX-3B begins.

### Manual browser smoke checklist (26 steps — perform before CTX-3B begins)

1. With zero Applications ever created: confirm no shell View selector appears anywhere.
2. Go to Config → Applications; confirm the empty state and its "New application" action.
3. Create the first Application.
4. Confirm the View selector now appears in the top bar, with a one-time dismissible hint.
5. Switch between All / Shared / the new Application; confirm list pages filter correctly each time.
6. Refresh the page on a scoped list under a specific view; confirm the URL and selected view survive.
7. Use browser back/forward across two different views; confirm each history entry restores its own view.
8. Open a new tab and a direct scoped URL (`/integrations?context=<id>`); confirm it opens exactly as linked.
9. Create an Integration under All; confirm Context is required before submit is enabled.
10. Create an Integration from inside a specific Application's view (or via a "Create integration in \<Name\>" link); confirm Context is preselected to that Application and the created Integration's Context chip confirms it.
11. Create a Connection the same way; confirm the same explicit-choice/preselect rules.
12. From an Application's Connections list, confirm the "N Shared connections available" notice and its link to `/connections?context=shared`.
13. From an Integration's binding panel, create a Connection in-flow; confirm only that Integration's Context or Shared are offered.
14. From the Integration hub's Schedules tab and from `/schedules/new`, confirm the read-only "Context: \<name\> — Inherited from the integration." row and that no Context selector exists anywhere in Schedule creation.
15. Confirm `/integrations`, `/connections`, `/schedules` correctly filter to the selected Application, and that Connections never silently include Shared rows.
16. Confirm `/runs?context=<id>` filters by the historical snapshot — a run recorded under Application A still appears under A even after its Integration's current owner changes to B.
17. Open a resource's detail page while a different view is selected; confirm the mismatch notice appears and offers "Switch view," and that opening it never silently changes the view itself.
18. Confirm an archived Application's name still resolves correctly (not a raw id, not "Unknown context") everywhere it's referenced, including in historical Run rows.
19. On Config → Inventory, confirm its own Context filter and column work independently of the shell View selector — including before any Application exists.
20. On `/control/audit`, confirm its own Context filter works independently of the shell View, and that switching the shell View never auto-applies to Audit.
21. On Business Data, confirm the transitional notice appears only under a specific Application/Shared view, never under All.
22. On Control Center, confirm the "In view" vs "Platform — all applications" pill grouping and section labelling read correctly, and that platform/system numbers never appear to change meaning when the view changes.
23. Attempt to archive an Application that still owns a live (non-retired) Integration, an active Connection, or an enabled Schedule; confirm the refusal names the counts and links to each.
24. Retire/disable those live resources, then archive the Application successfully.
25. Confirm the archived Application no longer appears in the View selector or any create-flow picker.
26. Select an Application in one tab, archive it from another tab, then refresh the first tab or navigate it to a scoped route (there are no cross-tab storage listeners or push updates by design, so an already-open tab does not update on its own — only the next view resolution corrects it); confirm it falls back to All with the archived-context notice rather than retaining the now-archived view (this exercises the post-Plan-5 tab-state revalidation fix).

Also confirm dark and light appearance render correctly throughout, and that a narrow (mobile-width) layout does not break the selector, scoped list filters, or Control Center's pill groups.

## 8. Post-closure stabilization (same day, commits `a123896`..`a8f098a`)

The manual browser smoke checklist mandated in §7 was subsequently performed and found six real, fixable defects, addressed in four commits on top of this closure's `f8bd3ad` baseline:

1. **First-schedule creation dead button** (`a123896`) — on an Integration with zero existing schedules, the empty-state's "Add schedule" button set internal state but the create form couldn't render, so the button did nothing. Widened the render gate to include the in-progress-adding state.
2. **Control Center not live-updating on View switch** (`a123896`) — the View-change event handler called `LoadAsync()` directly from a plain C# event, which never triggers Blazor's post-handler re-render; wrapped it in `InvokeAsync` with an explicit `StateHasChanged()`.
3. **Integration `Retired` state unreachable** (`e3cb98e`) — `IntegrationState.Retired` was real and already load-bearing (archive-blocking math, the reactivation guard) but no operator path could ever produce it: no dedicated service method, API route, client method, or UI control existed. Added `RetireIntegrationAsync` end-to-end (service → controller `POST /integrations/{id}/retire` → client → a Retire button/banner on the Integration detail page), reusing the existing `integration.update` authorization check. Reactivating a Retired Integration already worked via the existing Activate path and archived-context guard, so nothing there needed to change.
4. **Lifecycle conflict errors showed raw framework text** (`ac9fee8`) — `ActivateIntegrationAsync`/`DeactivateIntegrationAsync`/`RetireIntegrationAsync` on the management client called the bare `HttpResponseMessage.EnsureSuccessStatusCode()`, whose message strings are trimmed under Blazor WASM, so a 409 from the archived-Application reactivation guard rendered to the operator as the literal untranslated resource key (`net_http_message_not_success_statuscode_reason, 409, Conflict`) instead of the server's actual domain message. Switched all three to the file's own established `EnsureSuccessOrThrowAsync` convention, which was already used by ~30 other client methods and parses the API's `{ "error": ... }` body into the thrown exception's message. Confirmed by browser re-smoke: an archived Application's Retired Integration now shows the real domain message (e.g. "*... is archived. Unarchive it first before activating this integration.*") and remains correctly unactivated.
5. **Archive-blocker copy mislabeled Inactive Integrations as "active"** (`a8f098a`) — both the server exception message and the `ApplicationDetail.razor` blocker banner described the count of Integrations blocking archive as "N active integration(s)," but §6.5's rule is "no Integration whose state is not Retired" — an Inactive Integration blocks archive exactly like an Active one. Reworded to "N integration(s) not retired" in both places (no change to the archive rule itself), and the closing instruction now names the actual required actions instead of the vaguer "Retire or disable them first."
6. **Cross-tab archive smoke wording implied an automatic live update** — item 26 of the checklist below previously read as if an already-open tab flips to All immediately when another tab archives its selected Application. There are no cross-tab storage listeners or push updates by design (§4.3 of the design gate); a stale view is only corrected the next time it's resolved (refresh, or navigating to a scoped route). Reworded the step accordingly (already reflected in §7's checklist above).

Also recovered during this pass, unrelated to CTX-3A code: the dedicated `apperture-test-sql` integration-test SQL Server container (separate by design from Aspire's dev SQL Server, so tests can freely migrate/recreate schema without touching manually-curated dev data) had drifted to an unrecoverable local password and stale leftover data from a prior run. Recovered its real password via `docker inspect`, updated the local (gitignored) `.env`, documented it outside the repo, and dropped its three stale test databases so migrations recreate them fresh.

**Updated validation** (re-run after all six fixes, same day):

| Check | Result |
|---|---|
| `Apperture.UnitTests` (full, unfiltered) | ✅ 2170/2170 passed (+11 over this closure's original 2159: +1 pre-existing in `5f49ae7`, +10 new regression tests across the four stabilization commits) |
| `Apperture.Management.Ui.Shared.Tests` (full) | ✅ 831/831 passed (unchanged) |
| `Apperture.Management.Ui.Client.Tests` (full) | ✅ 19/19 passed (unchanged) |
| `Apperture.ArchTests` (full) | ✅ 38/38 passed (unchanged) |
| `Apperture.IntegrationTests` (full) | ✅ 204/204 passed (unchanged count; required recovering the local test-SQL environment described above first) |
| `dotnet build Apperture.slnx` (full solution) | ✅ 0 errors |

**Browser smoke, updated:** the six defects above were each confirmed fixed via targeted browser re-smoke (the specific broken flow, re-tested until it worked). The full 26-step checklist in §7 has not been separately re-walked end-to-end as one sitting since these fixes landed; nothing in the fixes above touches steps other than 23/26 (archive-blocker wording, cross-tab wording), so the rest of the checklist's original standing (never executed as a single pass) is unchanged. No further blocker is known.

## 9. Deferred

- **CTX-3B resource movement** — preview/move endpoints, `IResourceOwnershipMoveService`, row-lock serialization against concurrent binding creation, the two-sided move audit model, and the `binding_cross_context` readiness code. This is the epic's next and final phase; the Application feature does not ship until it lands.
- **Integration hard-delete referential-lifecycle audit** — confirmed, reproducible gap: `DeleteIntegrationAsync` blocks only on existing bindings and schedules, and `NotificationDefinitions.IntegrationId` carries no foreign key, so a hard delete can silently orphan Integration-scoped `NotificationDefinition`/subscription rows. Config Inventory already classifies these as "Unknown context" as an interim mitigation. The design gate requires a full referential-lifecycle inventory and a deliberate per-dependent decision (block vs. clean up) before CTX-3B can be considered closed — not before CTX-3A, and CTX-3A introduced no one-off guard for it, per the design gate's explicit instruction not to.
- **Context-scoped authorization** — never planned; explicitly out of scope by design throughout CTX-3A (§6 above).
- **Connection/Schedule lifecycle cleanup** (real delete/hard-retire paths) — separate roadmap work; CTX-1's original archive-rule problem (Connections only retire, Schedules have no delete path at all) was worked around by CTX-3A's refined "nothing live" archive rule, not solved at the resource-lifecycle level.
- **Business Data collection architecture (DAT)** — untouched; Business Data stays a transitional, unscoped demo projection.
- **Services** and **Resource Catalog** — both explicitly out of scope per ADR-0040's own Neutral section, unaffected by CTX-3A.
- **Dashboard Panels** — deferred, unaffected.
- No task in Plan 6 was cut to the design gate's own "Unscoped banner" escape hatch — the rejected-external-controls join (the one piece explicitly flagged as cuttable) was implemented in full and is covered by a passing test (`ListAttentionAsync_RejectedControls_FilterThroughJoinToOperationRunsResourceContextId`).

## 10. Acceptance criteria

| Criterion (design gate §14, CTX-3A) | Status |
|---|---|
| `ResourceContextViewScope` exists in `Ui.Shared`; `All` never a ResourceContext row/id/Shared/ambiguous null; `ToApiFilter()` returns null for All | ✅ |
| `all` is a reserved slug | ✅ |
| No active Application → selector/ownership labels hidden, view is All, existing pages behave exactly as before; Config Inventory is the one exception | ✅ |
| Every `Scoped` route's URL always carries `?context=`; back/forward restores each entry's view | ✅ |
| Explicit selector change writes localStorage; a `?context=` arriving by URL does not | ✅ |
| Remembered archived/unknown context falls back to All with correct notice; explicit URL to an archived context is honoured with a banner | ✅ |
| Opening a detail/hub route never changes the view; mismatch indicator appears only when genuinely mismatched | ✅ |
| Every displayed context name resolves active+archived by id; unresolvable shows "Unknown context," never a raw id | ✅ |
| Scoped collection pages filter server-side by the validated `?context=` value only, never a stale/corrective double-request | ✅ |
| `/connections` under an Application view never includes Shared rows; Shared-available count link shown | ✅ |
| `/runs?context=X` filters by the historical `ResourceContextId` snapshot, proven independent of current ownership | ✅ |
| Audit has its own independent Context filter, not driven by the shell view | ✅ |
| Business Data never filtered; transitional notice shown under a specific view only | ✅ |
| Config Inventory never filtered by shell view; own Context column/filter always present | ✅ |
| Control Center's contextual half filters by run snapshot; platform sections always instance-wide and clearly labelled; pills grouped "In view"/"Platform" | ✅ |
| `/config/applications` lists Active/Archived with live counts; Shared shown as its own card | ✅ |
| Create derives slug live under Advanced, rejects invalid slugs inline, lands on detail, never auto-switches the view | ✅ |
| Shared's detail page offers no rename/slug/description edit/archive; API attempts return 409 | ✅ |
| Archive succeeds under the refined "nothing live" rule; otherwise 409 with the ownership summary and scoped links | ✅ |
| Reactivation guard rejects activating/un-retiring/enabling an owned resource while archived | ✅ |
| Archived Applications absent from selector/create pickers, still resolve by id everywhere | ✅ |
| Summary endpoint issues a constant number of queries; null counts for unreadable families | ✅ |
| Integration/Connection create require explicit Context under All, preselect under a specific view, restate destination; no field/request change with zero Applications | ✅ |
| In-flow Connection creation offers only the Integration's Context or Shared, defaulting to the Integration's | ✅ |
| Schedule creation shows inherited Context read-only; no standalone Schedule creation path exists | ✅ |
| ScopeKey under Advanced, labelled as an authorization scope, behavior unchanged | ✅ |
| `resource_context.manage` seeded/repaired/surfaced correctly; every mutating endpoint enforces it; list/get require only auth | ✅ |
| No `context:` scope key/role/grant/capability exists; CTX-1 non-change tests green except the one deliberately updated assertion | ✅ |
| ResourceContext lifecycle ops write audit with `ResourceContextId` = that context | ✅ |
| Client/server DTO parity test passes for all Plan-1 types | ✅ |
| Full unfiltered `Apperture.UnitTests`, `Ui.Shared.Tests`, `ArchTests`, `IntegrationTests` green; browser smoke performed | ✅ — all four suites ✅ (§7, reconfirmed §8); the manual checklist's execution surfaced six real defects (§8), all fixed and each individually reconfirmed by re-smoke. No known blocker remains. |

---

*Historical note: this closure does not revise `ctx3-design-gate.md` or any prior plan document — it is a new report referencing them, per the design gate's own instruction that review history is preserved, not rewritten to look obvious in hindsight.*
