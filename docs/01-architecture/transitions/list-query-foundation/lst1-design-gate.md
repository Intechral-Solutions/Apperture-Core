# LST-1 — List and query foundation — design gate

Date: 2026-10-03
Status: **LST-1.0 design gate. Approved by the product owner (2026-10-03) with amendments.** Section 30 records the answers to Q1 to Q9 and section 30.1 the five amendments. Every ruling is binding on LST-1.1 onward. No list or query product code has been written, and no LST-1 implementation branch exists.
Branch at writing: `rc1-foundation` (documentation-only; may be cherry-picked onto the dedicated LST-1 branch)
Baseline HEAD: `228d782` (`docs: make workspaces the primary cross-context product direction`), clean working tree when the investigation started.
Governing contract: [rc1-program-gate.md](../../rc1-program-gate.md), decisions D-16, D-17, D-18, D-19, D-20, and section 6.1.

This gate does not reopen any RC1 decision. Section 23 records that none needs amendment, and lists the two places where this gate clarifies wording.

Labels:

- **Code fact** — verified in the repository at the baseline HEAD.
- **Ruling** — LST-1.0 decision, binding on implementation packages.
- **Contract** — what an implementation package must build and prove.
- **Recommendation** — advice that needs an owner answer before it binds.

Paths are relative to the repository root. Abbreviations: `UI` is `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/`, `US` is `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/`, `MC` is `src/Apperture.ControlPlane/Apperture.Management.Client/`, `SEC` is `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/`, `API` is `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/`, `PERS` is `src/Apperture.Persistence/Apperture.Persistence.SqlServer/`.

---

## 1. Status

| Item | State |
|---|---|
| RC-0 program contract | Present and authoritative (`d8cc7e5`) |
| IAM-9.0 design gate | Approved with amendments (`4f628b9`). LST-1 depends on its `UserReferenceDto` (§14) and user search (§16) only for LST-1.3 onward |
| Workspace direction | Committed (`228d782`). Not part of LST-1 |
| LST-1.0 | This document. Approved with amendments (§30, §30.1). Closed |
| LST-1.1 onward | Not started. Section 28 decomposes them. LST-1.2a is added by the owner review. Each awaits an explicit start instruction. No LST-1 implementation branch exists |
| Product code changed by LST-1.0 | None |

## 2. Baseline

- Branch `rc1-foundation`, HEAD `228d782`. Ancestry includes `d8cc7e5` and `4f628b9`. The Workspace correction is its own committed change, so nothing is isolated or pending.
- Ui.Client targets `net10.0` (`UI/Apperture.Management.Ui.Client.csproj:3`; `global.json` pins SDK `10.0.100`). It is standalone Blazor WebAssembly, with no server interactivity (`UI/Program.cs:8-10`). `NavigationManager.NavigateTo(..., replace: true)` and `GetUriWithQueryParameters` are available. The repository already calls `replace: true` for redirects (`UI/Services/ResourceContextViewState.cs:391,403`; `UI/Pages/Config/Inventory.razor:241`).
- SQL test behaviour without a reachable SQL Server is **mixed**. Some tests return early and report as passed (`AuditCorrelationIdSqlServerTests.cs:23`, "soft-skip"). Others throw and fail (`AuditTimelineSqlServerTests.cs:28-31`). Every package below that needs SQL proof must say which tests ran against SQL Server, and must report any that returned early as skipped, not passed. Unit tests that use the EF InMemory provider (`RunQueryServiceTests`) cannot prove ordering, cursor predicates or index behaviour, so they do not count as SQL proof.

## 3. Governing RC1 decisions

| Decision | Effect on this gate |
|---|---|
| D-16 | Runs are chronological by default. The server order is the canonical order. The client must not re-sort a paged list |
| D-17 | The URL is the durable navigation state. Browser Back and Forward own traversal. This gate's URL rules follow from it |
| D-18 | LST-1 is the RC1 layer. LST-2 is post-RC1. Section 23 draws the line |
| D-19 | Refresh keeps content visible and marks the surface busy. This gate defines the list side (§22) |
| D-20 | "All" describes the unfiltered scope. "Load more" and paging describe retrieval. "Show All" never loads every row (§16) |
| Section 6.1 | The LST-1 scope list, and the binding Runs requirement (§17) |
| Section 6.5 | AUD-1 corrects the audit cursor and follows the LST-1 URL convention (§18, §12) |
| Section 6.9 | RCV-1 validates that no list shows an empty state while a query is pending or rows are unsearched. §4 of this gate is its precise statement |

## 4. Repository findings

### 4.1 The fifteen supplemental findings, checked against source

| # | Claim | Verdict | Evidence and correction |
|---|---|---|---|
| 1 | Table styling exists, but no shared table, column or header component | **True** | `.ui-data-table` is CSS only (`UI/wwwroot/css/management-ui-primitives.css:1158-1206`). 49 `<table` elements in 39 `.razor` files. Zero files use `aria-sort` |
| 2 | State components are shared, but pages hand-write the branch chain | **True** | `LoadingState` 69 files, `ErrorState` 61, `ForbiddenState` 48, `EmptyState` 43. Counts re-verified. Each is a thin wrapper over `UI/Molecules/PageMessage.razor` |
| 3 | Several incompatible paging patterns | **True** | Keyset token (audit, Entra directory), offset with total (users, demo customers, operation items), offset with has-more (customer activity), `take` with no paging (inbox, instances, definitions, Components, Inventory, Integration Runs), and none |
| 4 | Several hand-written "Load more" controls | **True, with a count correction** | Five controls: `AuditTimeline.razor:213`, `DemoCustomersList.razor:163`, `DemoCustomerActivity.razor:161`, `RunOperationItemizationPanel.razor:548`, `UserFromEntra.razor:126`. They sit over three data contracts, not four. `UsersList.razor:91` is a Previous and Next pager |
| 5 | Debounced search is duplicated | **True** | Two copies of the timer pattern: `ComponentsList.razor:158,344-362` and `ApplicationResources.razor:117,226-246`. Neither passes a token to the HTTP call |
| 6 | Stale-response handling has competing approaches | **Partly true** | `NavigationLoadGuard` is a generation counter only. It has **no cancellation source** (`US/NavigationLoadGuard.cs:1-13`); two pages pair it with their own `_loadCts`. `_requestSequence` is in **three** files, not four. `ScopedLoadGate` is a load-once-per-key gate and is not a stale guard. Runs, Audit, Inventory, Integration Runs and Customer Activity have **no guard at all** |
| 7 | Several lists silently cap | **True** | Inbox 100, instances 50, definitions 100, Integration Runs 200, Inventory 100 (and 100 plus 100 in Activity), user pickers 200. No notice on any |
| 8 | Inventory Activity can show a false "no results" | **True** | `SEC/InventoryQueryService.cs:253-304,323-361` takes 100 runs and 100 audit rows from the last 24 hours, then applies `q`, `type` and `status` in memory. The UI then says "Nothing to show" (`UI/Pages/Config/Inventory.razor:99-103`) |
| 9 | Customer activity search can let a stale response win | **True, and worse** | `DemoCustomerActivity.razor:78-82,208-213` loads on every keystroke with no delay, token or guard. The `finally` at 312-316 clears `_loading` when any request ends, even if a newer one is pending. A "Load more" still in flight when a filter changes appends old-filter rows |
| 10 | Runs applies its cap before authorization | **True, but narrower than stated** | The cap exists only when `Take` is set (`SEC/RunQueryService.cs:80-81`). The global Runs page sends no `Take` (`UI/Pages/Runs/RunsList.razor:429-436`), so it is **unbounded**. The Integration Runs tab sends `Take: 200` (`IntegrationRuns.razor:157`) and is the live under-fill defect. Two filters are applied after the cap as well (N2) |
| 11 | The audit cursor may not match its ordering | **True** | Order is `At desc, Id desc` (`PERS/SqlAuditService.cs:53-55`). The cursor is the last `Id` and the predicate is `Id < lastId` (`:77,148-151`). `At` is supplied by the caller (`:28`); `Id` is a database identity (`AppertureDbContext.cs:303`). They can diverge |
| 12 | Only Runs meaningfully persists filters in the URL, with imperfect history semantics | **True** | `RunsList.razor:401-411` writes with a plain push (`:404`). Inventory writes back only a slug rewrite of `context` (`Inventory.razor:237-241`). The shell writes `context` (`ResourceContextViewState.cs:391,403,441`) |
| 13 | Some pages read URL state and never write it back | **True** | Audit (`AuditTimeline.razor:228-261`), Inventory (read once in `OnInitializedAsync`, no `OnParametersSet`, `:201-231`), Components (`kind`, `:265-282`), notification instances and definitions |
| 14 | State is lost on refresh and on a round trip | **True** | Every list except Runs and Inventory keeps filter and page state in component fields |
| 15 | No virtualization and no sortable headers | **True** | Zero `<Virtualize` and zero `aria-sort` in the UI project |

### 4.2 Findings not in the audits

| # | Finding | Evidence |
|---|---|---|
| N1 | The global Runs list is unbounded. It has never had a page limit | `RunsList.razor:429-436`; `RunQueryService.cs:80-81` |
| N2 | `ExecutionKind` and `ModuleId` filters on Runs are applied in memory, after the cap | `RunQueryService.cs:304,323-331` |
| N3 | Run read authorization is a function of the run kind and `DefinitionId` only, evaluated by async in-process services. It cannot be written as a SQL predicate today. A run whose kind cannot be resolved (not exactly one start-request row) is silently dropped | `RunQueryService.cs:92-94,306,333`; `OperationRunReadAuthorizationService.cs`; `WorkflowRunReadAuthorizationService.cs`; `SEC/IRunKindResolver.cs:35-55,69-74` |
| N4 | Runs order by `CreatedAt` alone, with no tiebreak. `OperationRuns` has indexes on `RetryOfRunId` and `ResourceContextId` only. Nothing indexes `CreatedAt` or `IntegrationId`. The key is `nvarchar(64)` | `RunQueryService.cs:79`; `AppertureDbContext.cs:110-133` |
| N5 | Runs re-sorts loaded rows by triage rank on the client, filters "Needs attention" on the client, and labels its summary "Counts from loaded rows". All three break once Runs is paged | `RunsList.razor:347-360,99`; `US/RunScheduleOperatorUiHelpers.cs:26` |
| N6 | The Runs empty branch ignores `_loading`. A prior empty result stays on screen while the next query is pending | `RunsList.razor:60-84` |
| N7 | Audit "Load more" reads the **live** filter fields and recomputes `fromUtc = UtcNow - hours`, so the window moves between pages. A load-more failure replaces the whole table with `ErrorState`. The client passes `cancellationToken: default` | `AuditTimeline.razor:329-334,352-355,304,342` |
| N8 | The `(At desc, Id desc)` index already exists, so the corrected audit predicate needs no new index | `AppertureDbContext.cs:320` |
| N9 | Every one of 239 `IManagementClient` methods accepts a `CancellationToken`. About 26 of about 343 UI call sites pass one (heuristic count) | `MC/IManagementClient.cs`; shared-infrastructure audit |
| N10 | bUnit is **not** in the repository. Component tests use `HtmlRenderer`, which cannot dispatch events. Part 1 and the IAM-9 gate both plan "bUnit" coverage | `Directory.Packages.props`; `tests/Apperture.Management.Ui.Client.Tests`; `iam9-design-gate.md` §24 test matrix (the `UserRef` row) |
| N11 | User pickers on two notification pages load the first 200 users ordered by `Id`, ignore `TotalCount`, and omit break-glass users. Users past 200 cannot be chosen | `NotificationDefinitionDetail.razor:556-561,617-618`; `IntegrationNotifications.razor:415-419,520-521` |
| N12 | The notification-definition component provider takes 200 definitions before the catalog applies `q`, so the Components search is partial at the source | `SEC/NotificationDefinitionComponentCatalogProvider.cs:~39-44` |
| N13 | Users paging advances `_skip` before the load and does not roll it back on error. A page past the end renders "No users found." with no Previous button | `UsersList.razor:173-176,53-55,91` |
| N14 | The operational-team service requires `schedule.manage` for **every** method including list and get, so the page's read-only branch is unreachable. `ReplaceMembers` rebuilds all rows, resets every member to enabled, and does not de-duplicate | `SEC/OperationalTeamManagementService.cs:30,40,60,81,96-160,171`; `AttentionContactGroupDetail.razor:58-66,161-166,232-260` |
| N15 | A composite `(CreatedAtUtc, Id)` cursor already exists in the notification toast poll, with a "both or neither" guard. The inbox list beside it orders by `CreatedAtUtc` alone | `PERS/SqlNotificationUserInboxStore.cs` (`ListToastDeltaAsync`; `ListForUserAsync` clamps `take` to 1-200) |
| N16 | `SearchableList` is the only listbox. It is synchronous and in-memory, has no debounce, selects on every arrow press, and has no `aria-activedescendant`. It cannot be the base of an async combobox | `UI/Molecules/SearchableList.razor:99-117,176-198` |
| N17 | Constraints on new interaction markup: no new modal, drawer or overlay markup (build on `ManagementDialog`); no arbitrary z-index; no `.razor` file named `InspectionPanel`, `Drawer`, `SidePanel`, `Popover` or `Accordion` | `.ai/playbooks/anti-patterns.md`; `Uix6InteractionSystemTests.cs:329-346` |
| N18 | Source-string tests pin the current duplicated debounce and sequence patterns. Converging them means updating those tests | `ComponentCatalogUiSourceTests.cs:66-72`; `ApplicationResourcesUiSourceTests.cs:65-70` |
| N19 | Callers of `IRunQueryService.ListRunsAsync` beyond the Runs API: dashboard cards, control activity, control attention, Inventory, operations controller. Some pass `Take` (Inventory 100); the dashboard window query and control activity are unbounded. The interface comment states the defect outright: `Take` "caps rows after SQL filters and ordering and before per-run authorization. The returned list may be shorter". The CLI calls the client `GetRunsAsync` | `SEC/IRunQueryService.cs:54-57`; `SEC/Dashboard/ExecutionsDashboardCardProvider.cs:29-32`; `ControlActivityQueryService.cs:55`; `ControlAttentionQueryService.cs:50`; `InventoryQueryService.cs:276`; `Cli.Core/CliCommandService.cs:22` |
| N20 | Several complete-set list queries have **no `ORDER BY`**: Connections, Schedules, Integrations and Roles. Their order is whatever the database returns, and Connections, Roles and Schedules-with-no-triage show it as is. Connections also loops one binding query per connection | `PERS/SqlConnectionRepository.cs:31-48`; `SqlScheduleRepository.cs:29-41`; `SqlIntegrationRepository.cs:40-62`; `SqlRoleRepository.cs:32-36`; `API/ConnectionsController.cs:68-73` |
| N21 | A malformed audit continuation token is silently ignored and returns the first page again, which repeats rows. No test anywhere exercises `ContinuationToken` or `NextContinuationToken` | `PERS/SqlAuditService.cs:148-151`; grep over `tests/` |
| N22 | The Demo customer list loads **every** matching operation item into memory and filters, sorts and pages there. Customer activity does the same, and runs the full load again for its existence check on every request. These are sample-module surfaces, but the "offset with total" label hides an unbounded load | `SEC/DemoBusinessRecords/DemoBusinessRecordProjectionService.cs:95-141,268`; `DemoBusinessRecordProjector.cs:180-213` |
| N23 | The fleet signal queries use the same cap-then-authorize loop as Runs (`BatchMaxRows = 1500`), so fleet failure counts can under-report when unauthorized runs dominate the newest 1500. Inventory Activity's runs source inherits the Runs defect too (`Take: 100`) | `RunQueryService.cs:102-173,175-279`; `APP/Integration/IntegrationFleetModels.cs:15` |
| N24 | Existing page types disagree on names: `TotalCount` versus `TotalMatching`, `NextContinuationToken` versus `ContinuationToken`. `PlatformEventQueryPage(Items, HasMore)` already uses the `take + 1` probe. There is no shared paged type | `APP/PlatformEvents/PlatformEventQuery.cs:14`; `APP/Services/AuditTimelineModels.cs:28`; `APP/Services/IEntraProvisioningDirectoryService.cs:37` |
| N25 | Existing Runs tests cover filters only. None covers `Take`, ordering, or the cap-then-authorize interaction, and the unit suite uses the EF InMemory provider. One SQL test exists for Runs (`status=blocked`) | `tests/Apperture.UnitTests/ControlPlane/RunQueryServiceTests.cs`; `tests/Apperture.IntegrationTests/RunsBlockedFacetSqlServerTests.cs:28` |

## 5. Current list and query taxonomy

| Family | Meaning | Surfaces today |
|---|---|---|
| **A. Complete** | Server returns the whole authoritative set. No cap | Integrations fleet, Connections, Schedules, Roles, Modules, Applications, Operational Teams, access requests |
| **B. Offset with total** | `skip` and `take`, exact count | Users, Demo customers, operation items |
| **C. Offset with has-more** | `skip` and `take`, no usable total | Customer activity |
| **D. Keyset** | Opaque continuation, no total | Audit, Entra directory search |
| **E. Capped, with total** | A hard `take`, total reported | Components (200), Application Resources (500) |
| **F. Capped, silent** | A hard `take`, no total, no notice | Runs (Integration Runs tab, 200), inbox (100), instances (50), definitions (100), Inventory (100) |
| **G. Windowed merge** | Several sources, each capped, then filtered in memory | Inventory Activity |
| **H. Unbounded and uncapped by mistake** | Complete in shape, unbounded in growth | Global Runs list |

Families A and E are correct for the data they serve. B, C and D are correct but inconsistent. F, G and H are the defects.

## 6. Problems being solved

1. A list can say "no results" when it has not finished asking, or has not asked about everything (N6, finding 8, finding 9).
2. A page can be filled short, or empty, because a cap is applied before authorization (finding 10, N2).
3. A cursor can skip or repeat rows (finding 11).
4. A stale response can overwrite a newer one, and in-flight HTTP calls are never cancelled (findings 5, 6, 9, N9).
5. A load-more or refresh failure can destroy content that was on screen (N7).
6. Capped lists look complete (finding 7).
7. Search and filter state is lost on refresh and on a round trip (findings 12 to 14).
8. Selecting a user is impossible beyond the first 200 users, and team membership is a text grammar (N11, N14).
9. The same branch chain, debounce, stale guard and load-more footer are written by hand many times (findings 2, 4, 5, 6).

## 7. Explicit non-goals

- No universal `SmartTable`, no table component, no column schema.
- No sortable headers, quick or advanced filter controls, default row-action convention, local instant pre-filter, virtualization or continuous loading. These are LST-2 (§23).
- No change to team authorization, and no change to `user.read`.
- No new generic query language, filter DSL or declarative data-grid.
- No change to mutation behaviour. ACT-1 owns it.
- No platform search. SRCH-1 owns it.
- No conversion of lists RC1 does not otherwise touch, beyond the stated adopters.
- No retrofit of existing DTO shapes for the sake of uniformity.

## 8. Core rulings

| # | Ruling |
|---|---|
| R1 | **Two wire paging contracts**, keyset and offset, each only for endpoints that genuinely page that way. A capped result that exposes a total is not an offset page. It adapts into `PageFetch` with no cursor and `Truncated`. There is no third generic contract and no universal result type (§9) |
| R2 | **No runtime query-capability record in LST-1.** One runtime fact is carried instead: result completeness, set explicitly by each adapter on `PageFetch` (§9.3, §9.4, §10) |
| R3 | **One non-visual query lifecycle helper**, composed into a page, not inherited (§10) |
| R4 | **The empty-state invariant** is the exact rule in §11, enforced by one decision table in the list-state view (§14) |
| R5 | **Authoritative search is server-side for any list that is partial.** Client-side filtering is allowed only over a result that is `Complete` (§12) |
| R6 | **List state in the URL**: search text and dataset-defining filters, written with replace. Cursors, load-more position and in-flight text are not in the URL (§13) |
| R7 | **View versus filter** is recorded as a convention and verified against code. No tab is redesigned (§14) |
| R8 | **One list-state view** composes the existing four state components. It is not a table (§14) |
| R9 | **One pager footer.** Numbered pagination is not needed for RC1 (§15) |
| R10 | **No silent truncation.** Exact totals are computed only where they are cheap and meaningful (§16) |
| R11 | **Runs use keyset paging, with authorization applied by a memoized fill loop**, because authorization cannot be pushed into SQL today (§17) |
| R12 | **A keyset cursor encodes every field of the list's stable ordering.** AUD-1 corrects the audit cursor to `(At, Id)` (§18) |
| R13 | **`AsyncSearchSelect` is a single-select ARIA combobox** over a caller-supplied search delegate (§19) |
| R14 | **The team selector seam**: the selector never calls a hard-wired endpoint. Which endpoint a team editor uses is decided with IAM-10 (§20) |
| R15 | **LST-1 supplies cancellation only to read and query operations.** It makes no ruling on mutation cancellation semantics. ACT-1 owns that contract (§10.5) |
| R16 | **Load-more and refresh failures never replace content on screen** (§22) |

## 9. Paging contracts

### 9.1 Ruling

Two wire shapes, each for endpoints that genuinely page that way. Presentation never receives either one. It receives a client-side adapter output (§9.3).

```csharp
// Offset: for genuinely offset-pageable endpoints. The client can ask for the next skip.
public sealed record OffsetPage<T>(IReadOnlyList<T> Items, int TotalCount, int Skip, int Take);

// Keyset: for genuinely keyset-pageable endpoints. HasMore is NextCursor is not null.
public sealed record KeysetPage<T>(IReadOnlyList<T> Items, string? NextCursor);
```

These are illustrative names. LST-1.1 confirms the names and the project they live in. They sit beside the existing list DTOs in `MC`, and server-side query results stay application types that the controller maps.

`OffsetPage<T>` deliberately matches the existing `OperationItemPageDto(Items, TotalCount, Skip, Take)` so adoption is a rename, not a redesign.

**A capped result is not an offset page.** An existing capped DTO that exposes a total (Components, Application Resources, Inventory) does not become `OffsetPage<T>` because it carries `TotalMatching`. Its endpoint cannot advance. It adapts directly into `PageFetch` with `NextCursor = null` and `Completeness = Truncated` when more matches exist than it returned (§9.3). A capped DTO that does not know whether more exist adapts through the `take + 1` probe the endpoint is changed to provide (§16.2). No generic query-capabilities record is introduced.

### 9.2 Rules

1. **Existing DTOs are not retrofitted.** `ResourceContextResourcesResult`, `ComponentCatalog` results and `OperationItemPageDto` keep their wire shape. Each gets a one-line adapter. A DTO is changed only when its endpoint is changed for another reason in this epic. Existing names disagree (`TotalCount` and `TotalMatching`, `NextContinuationToken` and `ContinuationToken`, N24). The new records standardize on `TotalCount` and `NextCursor`. Existing types are not renamed to match. The `take + 1` probe that `PlatformEventQueryPage` already uses for `HasMore` is the precedent for capped endpoints that cannot afford a total (§16.2).
2. **Offset ordering must be total.** The `ORDER BY` ends in a unique key. Offset lists drift when rows are inserted, so the client de-duplicates on the row key when it appends.
3. **Offset `take` is clamped on the server.** Users has no clamp today (`API/UsersController.cs:48-56`). IAM-9 §16 adds one (1 to 100).
4. **Keyset cursors are opaque.** A cursor is a short string the client never parses. It is versioned. An invalid or unreadable cursor returns 400 with problem code `cursor_invalid`. The client restarts from the first page and shows a short notice. A cursor is valid only with the filters it was issued for. Changing a filter resets the cursor.
5. **Keyset sort keys are immutable.** `CreatedAt` and audit `At` qualify. A mutable field such as `Status` or a last-updated time does not. If a row's filter membership changes between pages (a run moves from running to failed while the failed filter is active), it can be missed until the next refresh. That is accepted and disclosed in §29.
6. **Page size**: server default 50, maximum 100. Callers send `take`. The pager never shows a page size.
7. **Keyset has no total.** Do not count history tables to fill a footer (§16).
8. **A keyset page may be short or empty and still continue.** `Items.Count < Take` with a non-null `NextCursor` is valid. The fill loop in §17 produces it. It means "not finished", never "no more".

### 9.3 Client adapter

```csharp
// Client-side only. Presentation and the lifecycle helper see this and nothing else.
public sealed record PageFetch<T>(
    IReadOnlyList<T> Items,
    int? Total,
    string? NextCursor,
    ListCompleteness Completeness);
```

Exact names remain LST-1.1 implementation candidates. The shape is binding: **completeness is an explicit field set by the endpoint-specific adapter, not derived by the helper.** `Items`, `Total` and `NextCursor` cannot always determine it. `ScopeLimited` is knowledge about the endpoint and the query. A capped result with a `TotalMatching` knows that more rows exist and offers no continuation. Each adapter sets `Completeness` honestly:

| Source | `NextCursor` | `Completeness` |
|---|---|---|
| Keyset page with a cursor | passed through | `MoreAvailable` |
| Keyset page without a cursor | null | `Complete` |
| Offset page, `skip + Items.Count < TotalCount` | the next `skip` as a string | `MoreAvailable` |
| Offset page, everything loaded | null | `Complete` |
| Complete-set endpoint, no cap | null | `Complete` |
| Capped DTO, `TotalMatching > Items.Count` | **null** | **`Truncated`** |
| Capped DTO, `HasMore` from a `take + 1` probe, true | null | `Truncated` |
| Capped DTO, nothing more exists | null | `Complete` |
| Endpoint authoritative only over a stated window or source caps (Inventory Activity) | null | `ScopeLimited` |

An adapter never reports `Complete` for a capped result it cannot show to be whole. The lifecycle helper stores `NextCursor` and hands it back on "Load more". It never inspects it. This is how presentation stays independent of `Skip`, of `CreatedAt` and of the database.

### 9.4 Result completeness

`Completeness` is the one piece of "capability" LST-1 carries at runtime.

| Value | Meaning |
|---|---|
| `Complete` | The server returned the entire authoritative set for the applied query, with no cap. Nothing further exists to load |
| `MoreAvailable` | **A continuation exists.** Unloaded rows exist and a cursor will fetch them |
| `Truncated` | A cap was reached and **no continuation exists.** More matches may exist. A total may or may not be known |
| `ScopeLimited` | The query is authoritative only over a stated window, source cap or time range (Inventory Activity). The scope is named in the message |

## 10. Query lifecycle helper

### 10.1 Query capability metadata: ruling

The addendum proposed a minimal record: paging kind, has-exact-total, server search, server sort keys, URL parameter names. **LST-1 does not build it.** Evidence:

- Paging kind and has-total are implied by which DTO the page consumes, and are carried into `PageFetch` and completeness.
- Server search is implied by whether the page has a search box.
- Nothing in RC1 reads server sort keys. Sort headers do not exist until LST-2.
- URL parameter names are page-local constants (§13).

A record with no reader is speculative. It belongs to LST-2 as a requirement: a sortable header must know whether sort is server-authoritative, because client sort over a partial load is wrong. §23 records that. It does not belong in shared wire contracts at all. When it arrives, it is UI composition.

### 10.2 Shape: ruling

Composition, not inheritance. A plain C# class, owned as a field by the component, created in `OnInitialized` and disposed in `Dispose`. It lives with the other framework-neutral helpers in `US` so it can be unit-tested without rendering, as `NavigationLoadGuard` and `ScopedLoadGate` are today.

Two layers, illustrative:

```csharp
// Single-result queries: selector search, detail panels, complete lists, capped lists.
sealed class QueryRunner<TRequest, TResult> : IDisposable
{
    QueryRunner(Func<TRequest, CancellationToken, Task<TResult>> fetch,
                Action onChanged, TimeProvider? time = null, TimeSpan? debounce = null);
    QueryStatus Status { get; }
    TResult? Result { get; }
    Task RunAsync(TRequest request);        // immediate: selects, toggles, first load
    void Schedule(TRequest request);        // debounced: live text search
    Task RefreshAsync();                    // same request, content stays
    void Dispose();                         // cancels in-flight work and the pending timer
}

// Accumulating lists: load-more over either contract.
sealed class PagedListQuery<TItem, TRequest> : IDisposable
{
    PagedListQuery(Func<TRequest, string? /*cursor*/, CancellationToken, Task<PageFetch<TItem>>> fetch,
                   Func<TItem, string> key, Action onChanged, ...);
    IReadOnlyList<TItem> Items { get; }
    QueryStatus Status { get; }
    LoadMoreStatus More { get; }            // idle | loading | failed(message)
    Task RunAsync(TRequest request);
    void Schedule(TRequest request);
    Task LoadMoreAsync();
    Task RefreshAsync();                    // re-fetches from the first page, keeps Items until it lands
}
```

The helper returns state and calls `onChanged` (which the component maps to `StateHasChanged`). It does not take callbacks that render. It does not know what a row is, what a cursor means, or which endpoint it calls.

### 10.3 Semantics: contract

1. **Latest wins.** Each `RunAsync`, `Schedule` fire and `RefreshAsync` increments a generation (the same primitive as `NavigationLoadGuard`, which the helper reuses internally) and cancels the previous request's `CancellationTokenSource`. A completion whose generation is not current is discarded without touching state. This converges the three existing conventions on one.
2. **Debounce** applies only to `Schedule`. Default 300 ms, which both existing copies already use. The delay uses `TimeProvider` so tests can drive time. `Schedule` cancels any pending timer. A `RunAsync` cancels a pending timer too.
3. **Cancellation is not an error.** An `OperationCanceledException` from a superseded or disposed request is swallowed. It never sets `Failed`.
4. **`Dispose` cancels everything** and makes later completions inert. No state change and no `onChanged` after dispose.
5. **`LoadMoreAsync` is single-flight and bound to the request that produced the cursor.** If the request changes while a load-more is in flight, the load-more is cancelled and its page is discarded. This closes the append race in finding 9.
6. **The applied request is a snapshot.** `LoadMoreAsync` always uses the request that produced the first page, never the current form fields. Audit's `UtcNow - hours` recomputation (N7) cannot happen: the absolute time bounds are part of the snapshot.
7. **Appending de-duplicates by the supplied key.**
8. **First load versus refresh.** `Loading` with no content is a first load. A request that replaces existing content is a query change (`IsStale` until it lands). `RefreshAsync` re-fetches the same request and leaves content as it is.
9. **Content is preserved on failure.** A failed refresh or query change leaves `Items` in place, sets `Failed`, and keeps `IsStale`. Only a failure with no content produces an error-only view (§22).
10. **Forbidden is its own outcome.** An HTTP 403 sets `IsForbidden`, not `Failed`, so the view can render `ForbiddenState`.
11. **Auto-continue of empty continuable pages** (§17.6) is a `PagedListQuery` option, bounded, off by default.

### 10.4 Status

```csharp
enum QueryPhase { NotStarted, Loading, Settled, Failed }

sealed record QueryStatus(
    QueryPhase Phase,
    bool IsBusy,            // a request is in flight
    bool HasContent,        // there are rows or a result to show
    bool IsStale,           // content belongs to a superseded request, or the last refresh failed
    bool IsForbidden,
    string? ErrorMessage,
    ListCompleteness Completeness);
```

### 10.5 Cancellation and mutations

LST-1.1 makes cancellation normal for read queries by routing every list and selector read through the helper, which always passes its token to the `IManagementClient` call. All 239 client methods already accept a token, so no client change is needed. A page that calls a read outside the helper keeps its current behaviour until it is touched.

**LST-1 supplies cancellation only to read and query operations. It makes no ruling on mutation cancellation semantics; ACT-1 owns that contract.**

The helper never treats mutation work as a superseded query. Its tokens exist for reads that a newer read replaces. It is not given mutations to run, and a page must not route a mutation through it. Whether a mutation may receive a caller or component-lifetime token, and what a cancelled mutation means to the operator, is ACT-1's decision. The helper's `RefreshAsync` is the seam ACT-1 calls to re-read after a mutation.

### 10.6 What the helper does not own

- `ScopedLoadGate` stays. It answers "have I already loaded this canonical key". The helper answers "which response is current". A page may use both.
- `NavigationLoadGuard` stays as the generation primitive. `ControlCenter` and `IntegrationDetail` use it for whole-page loads and are not changed.
- Capabilities. The page loads capabilities and passes an access state to the view (§14).

## 11. Empty-state invariant

### 11.1 Ruling

A list **must not** display an authoritative empty state ("no results", "nothing here") unless all of these hold:

1. No request is in flight for the applied query (`!IsBusy`).
2. The last settled result belongs to the applied query, not a superseded one (`!IsStale`).
3. The result is `Complete`: the server has authoritatively searched the whole searchable scope for that query and nothing further exists.

If a list has zero rows and any of these fails, the list shows a non-empty-state view. By completeness:

| Zero items and | Renders |
|---|---|
| `Complete` (and 1 and 2 hold) | An authoritative `EmptyState` |
| `MoreAvailable` | **Never `EmptyState`.** The continuing "Still searching" state (§17.6) |
| `Truncated` | A scoped message that names the cap |
| `ScopeLimited` | A scoped message that names the search scope |

`MoreAvailable` means a continuation exists, so unloaded rows exist and the list has not been fully searched. Neither `Truncated` nor `ScopeLimited` may imply that no matching record exists outside the stated cap or scope. Copy says what was searched, never "no matching X exists".

### 11.2 Definitions

| Term | Definition |
|---|---|
| **First load** | The first request for a list, or the first after the dataset changed (a view switch). There is no content to keep. Shows `LoadingState` |
| **Refresh** | The same request fetched again. Content stays and the surface is marked busy |
| **Query change** | A different request replaces content. Old content stays, marked stale and busy, until the new result settles |
| **Search pending** | A query change is debounced or in flight. Never an empty state |
| **Loaded empty** | Settled, zero rows, `Complete`. The only state that earns "No X found" |
| **Continuable empty** | Settled, zero rows, a continuation exists. Not empty. Shown as "still searching" (§17.6) |
| **Truncated** | A cap was reached with no continuation. Content shows with a truncation notice (§16). If zero rows, the empty message names the limit and does not say nothing exists beyond it |
| **Partial or capped** | `Truncated` or `ScopeLimited`. An empty result is scoped, never absolute |

### 11.3 Current violations

Runs (N6), Inventory Activity (finding 8), Customer Activity (finding 9), Integration Runs (cap before authorization), Users page past the end (N13), and **Components search (N12)**: the notification-definition provider takes 200 definitions before the catalog applies `q`, so a Components search can report no match for a definition outside the first 200. Each is fixed in the package named in §24. Components is a required correctness fix, not an optimization (§28, LST-1.6).

## 12. Search semantics

1. **Server-authoritative for partial lists.** Any list whose result is not `Complete` sends its search text to the server. The client does not filter loaded rows by that text.
2. **Debounce for live text search.** `Schedule`, 300 ms. Superseded HTTP calls are cancelled. Latest wins.
3. **Explicit-apply is equally valid.** Inventory, Audit's actor box, Demo customers and the notification admin lists apply on Enter or an Apply button. A list uses live search or explicit apply, not both on one control. Both go through the helper, so both cancel and both are latest-wins. LST-1 does not force live search on any page.
4. **Client-side filtering only over `Complete`.** The Integrations "needs attention" toggle, the Schedules "needs attention" checkbox and Applications Active and Archived filter a result the server returned in full. They stay. A filter that is client-side over a `Truncated` or `MoreAvailable` result is a correctness bug.
5. **Consequence for Runs.** "Needs attention" is a client filter over loaded rows today (N5). Under keyset it would produce a false "Nothing needs attention". LST-1.2 turns it into a server filter (`attention=true`) with the same semantic definition as the UI's attention ranking, kept as **one definition, not two** (§17.3).
6. **Instant local pre-filter is deferred to LST-2.** It is not reintroduced here, including as a convenience.
7. **Minimum query length** is a component option. Lists default to zero (an empty box means "all"). Selectors default to zero too, and open on the first bounded page.

## 13. URL state

### 13.1 Ruling

**In the URL:**

- Search text (`q`), as applied, not as typed.
- Dataset-defining filters: status, kind, type, module, integration, time bounds, and the "needs attention" toggle on server-filtered lists.
- Server sort key and direction, once a list has them.
- The view, where one page switches datasets without a route change (`mode` on Inventory).
- `context`, owned by the shell (below).
- The offset page index for offset lists with numbered navigation. RC1 has none (§15), so nothing in LST-1 writes it.

**Not in the URL (transient):**

- Keyset cursors and load-more position. Back from a detail page restores the query, not the scroll position or the accumulated pages.
- Text typed but not yet applied.
- Dialog, disclosure and menu state. The anti-pattern list already forbids these in the URL.
- Row selection and expanded rows.

### 13.2 Replace versus push

| Action | History |
|---|---|
| Typing, or applying a search | **Replace** |
| Changing a filter, including clearing filters | **Replace** |
| Normalizing an invalid or legacy parameter | **Replace** |
| Switching a view (route or `mode`) | **Push** |
| The shell's View selector changing `context` | Push (existing, `ResourceContextViewState.cs:441`) |
| Drill into a detail page | Push (ordinary navigation) |

Runs currently pushes on a status change (finding 12). LST-1.2 changes it to replace.

### 13.3 Behaviour

- **Refresh.** The page reads the URL on load and starts from that state.
- **Return from detail.** Browser Back lands on the list URL. The component loads from those parameters. For keyset lists this is the first page of the same query. For D-17 purposes the URL is the durable state and that is sufficient. Restoring position is LST-2 (§23).
- **Back and Forward after filter tweaks.** Because tweaks replace, Back leaves the list rather than undoing a filter. This is the approved Direction.
- **Unknown parameters** are ignored and preserved. The page does not throw and does not show an error.
- **Invalid values** for known parameters (for example `status=bogus`) are treated as absent and normalized away with replace. They do not produce an error banner. The exception is an entity reference such as `context`, where an unknown value keeps today's `UnknownContext` handling.
- **Self-write loop guard.** A page that writes its own state to the URL receives a parameter change back. It compares the URL-derived request with the applied request and reloads only if they differ. `Inventory.razor` has no `OnParametersSet` today (finding 13), so adopting this is a deliberate change there.

### 13.4 Helper, not framework

A small helper in `US` with two pure functions: build a URI from the current URI plus a dictionary of `name → value-or-null` (null removes), and parse a parameter set into typed values with defaults and a "was normalized" flag. Pages call `NavigateTo(uri, replace)`. It uses `NavigationManager.GetUriWithQueryParameters`, which has zero uses today. Three hand-rolled parsers exist (`UI/Services/NavigationQueryHelper.cs`, `US/AffectedRecordNavigation.cs`, `US/Auth/LoginRouteHelper.cs`). LST-1 adds nothing to them and does not merge them. Parameter names are constants in one file in `US`.

### 13.5 Vocabulary

Existing names are kept so no URL changes. `q` for search. `status`, `integrationId`, `moduleId`, `correlationKey`, `createdFrom`, `createdTo` on Runs. `mode`, `type`, `status`, `sort` on Inventory. `context` everywhere. New lists use `q`, `status`, `sort`. NAV-2.0's typed locators build list URLs and must use these names. The constants file is the shared reference.

### 13.6 Not a state-management framework

The URL holds the applied request. The helper reads and writes it. There is no store, no subscription model and no persistence layer.

## 14. Views versus filters, and list-state presentation

### 14.1 Views versus filters

**Convention.** A **view** is a materially different dataset or mode. A **filter** restricts the same dataset. A view that has its own page is a route. `view=` or the existing `mode=` is used only when one page swaps datasets. A view switch pushes history. A filter change replaces.

**Verified against code:**

| Control | Verdict | Evidence |
|---|---|---|
| Inventory Resources versus Activity | **View** (`mode`) | Different sources and different merge. `Inventory.razor:16-22` |
| Integration hub tabs | **View**, by route | Each tab is its own page (`US/IntegrationUiRoutes.cs:53-61`; `IntegrationHubNav.razor`) |
| Users sub-pages (access requests, from Entra, effective access) | **View**, by route | Separate pages. No tab component |
| Shell View selector (`context`) | **Scope**, shell-owned | Changes dataset for Integrations, Connections, Schedules and Runs. Pages never write `context` themselves |
| Runs status, execution kind, "Needs attention" | **Filter** | Same dataset |
| Notifications All, Unread and Read | **Filter** | Server `readFilter` |
| Components kind, origin, module | **Filter** | Server filters |
| Application Resources type | **Filter** | Server `type` |
| Applications Active and Archived | **Filter** | Client filter over one complete dataset (`ApplicationsList.razor:42,118`) |
| Integrations fleet selects and "needs attention" | **Filter** | Server selects, client toggle over a complete set |

No tab is converted. The approved distinction holds. LST-2 builds its controls on this record.

### 14.2 List-state view: ruling

LST-1.1 introduces one presentational component, `ListStateView`, that selects among the existing `LoadingState`, `ErrorState`, `ForbiddenState`, `EmptyState` and the page's content. It replaces the hand-written branch chain on about 20 pages as they are touched. It is not a table, it renders no rows, and it fetches nothing.

**Inputs.** An access state (`Unknown`, `Allowed`, `Forbidden`). A `QueryStatus` (§10.4). A `Retry` callback. Render fragments for content, and optionally for the empty and continuing views, with a default `EmptyState` built from a title and message. An optional scope note for `ScopeLimited`.

**Decision table.** First match wins.

| # | Condition | Renders |
|---|---|---|
| 1 | Access `Unknown` | `LoadingState`. Capabilities are not loaded. No forbidden flash. Existing design-system rule |
| 2 | Access `Forbidden`, or `IsForbidden` | `ForbiddenState`. Never labelled as empty |
| 3 | `!HasContent` and (`Phase` is `NotStarted` or `Loading`, or `IsBusy`) | `LoadingState`. **Never `EmptyState`** |
| 4 | `!HasContent` and `Phase = Failed` | `ErrorState` with a Retry action |
| 5 | `HasContent` | The content, in a wrapper with `aria-busy` equal to `IsBusy`. If `Phase = Failed`, an inline `role="alert"` banner above it with Retry. If `IsStale`, a short "Showing previous results" line and dimmed content |
| 6 | `!HasContent`, settled, `Complete` | `EmptyState` (authoritative empty) |
| 7 | `!HasContent`, settled, `MoreAvailable` | The continuing view: "Still searching" with a Continue action (§17.6). Not `EmptyState` |
| 8 | `!HasContent`, settled, `Truncated` or `ScopeLimited` | A scoped empty message that names the limit |

Row 3 is what fixes the Runs defect. Row 5 is the rule behind §22.

**Responsibilities.** It owns the branch order, the ARIA wrapper (`aria-busy`, the alert banner) and the choice of component. It does not own the copy, which pages supply, nor the filter bar, the toolbar or the pager. The footer is a separate component (§15). `PageMessage` remains the contextual-banner primitive.

**Placement.** Models and the decision function in `US`, so the table is unit-testable without rendering. The component in `UI/Molecules`. `LoadingState` and the other wrappers are unchanged.

## 15. Pager and footer

### 15.1 Ruling

One footer component, `ListPagerFooter`, in `UI/Molecules`. It is presentation-only and independent of the paging strategy.

**Inputs.** Loaded count. Optional total. Has-more. Loading-more. A load-more callback. An optional load-more error with a retry. A noun pair for copy ("run", "runs"). An optional truncation note (§16).

**It never sees** `skip`, a cursor, `CreatedAt`, `Id` or a database page size.

### 15.2 Rendering

| Situation | Footer |
|---|---|
| Keyset, more available | "Showing 50 runs" and a **Load more** button |
| Offset, total known, more available | "Showing 50 of 742 users" and **Load more** |
| Complete, or no more | "Showing 37 runs", or nothing when the list is short enough that the count adds no information. The page chooses |
| Loading more | The button is disabled, labelled "Loading…", `aria-busy`. The rows stay |
| Load-more failed | An inline `role="alert"` message and **Retry**. The rows stay (§22) |
| Truncated | The truncation note (§16). No Load more |

### 15.3 Numbered pagination: ruling

**Not needed for RC1.** Every list that pages today either accumulates (Load more) or is a small offset list with Previous and Next. Shared footer mechanics suffice. Numbered pages need a total on every list, which keyset lists cannot give, and they are a presentation convention that belongs to LST-2 if it is wanted.

`UsersList` keeps its Previous and Next pager. LST-1 does not rewrite it. When it is next touched after LST-1.1 lands, it may adopt `PagedListQuery` and the footer. IAM-9 adds a `q` box to Users and must not wait for LST-1.1. It adds search to the current pager and resets `skip` when the query changes.

### 15.4 Accessibility of the footer

The summary is a polite live region that is updated only when a load-more completes ("Loaded 50 more. Showing 100 of 742."), not on every render. The button remains in the DOM while there is more, so focus is not lost. When the last page arrives and the button is removed, focus moves to the footer summary, which has `tabindex="-1"`.

## 16. Truncation

### 16.1 Ruling

Silent truncation is not allowed. A capped list says it is capped, next to the list, every time the cap was reached.

### 16.2 Contract

1. **The server decides, not the client.** An endpoint with a cap returns either a `TotalMatching` or a `HasMore` computed by fetching `take + 1`. A client must never infer truncation from `Items.Count == cap`. Exactly-at-cap would otherwise look complete.
2. **Copy.**

| Known | Copy |
|---|---|
| Total known | "Showing 100 of 742. Refine the search or filters to narrow the list." |
| Total not known | "Showing the most recent 100 results. Refine the search or filters to narrow the list." |
| Window | "Searched the latest 100 runs and 100 audit events from the last 24 hours." (scope disclosure, not a count) |
| Scoped empty | "No matches in the recent activity searched (the latest 100 runs and 100 audit events from the last 24 hours). Older activity is not searched. Use Runs or Audit for full history." |

**Scope copy must be exact (owner ruling, Q2).** If a query searched only a time window, the newest N runs or the newest N audit entries, the scoped message says that, with the real figures, and does not imply that no matching historical activity exists outside it. The words come from what the query actually did, not from a fixed sentence. Until LST-1.2a lands, the run source can return fewer than its cap because it authorizes after capping (N19, N23), so the copy must not promise "the latest N runs you can read". It says "recent runs". Runs and Audit remain the authoritative history surfaces.

The same rule governs a **partial search or filter** on the notification inbox, instances and definitions lists (Q1): a search sent to the server is authoritative over the scope it claims, and a list capped at N says "showing the most recent N" and never "no results" for rows beyond the cap.

3. **When to compute an exact total.** When the count is cheap and meaningful: offset lists over bounded tables (Users, items of one run, demo customers), and capped lists whose server already computes it (Inventory `TotalMatching` is computed at `InventoryQueryService.cs:87` and dropped at `MC/InventoryDtos.cs:25-27`; Components; Application Resources).
4. **When not to.** History tables that grow without bound (Runs, Audit, notifications, platform events). A count is expensive, and for Runs it is impossible without authorizing every row.
5. **"Show All."** Per D-20 it never means "load every row". LST-1 adds no "Show All" control. The three existing controls that clear narrowing filters keep or improve their wording. For a capped list the honest control is the truncation note, which tells the operator to refine.
6. **Capped but complete-looking.** Covered by contract item 1.

## 17. Runs keyset design

This is the binding RC1 requirement of section 6.1. Keyset alone does not fix the defect, because authorization is applied after the row cap. This section specifies the design the package must implement.

### 17.1 Can authorization be pushed into the query today?

**No.** Code fact. Authorization for one run is decided by calling `IWorkflowRunReadAuthorizationService` or `IOperationRunReadAuthorizationService` with the run's `DefinitionId` (`RunQueryService.cs:306,333`). Each service loads the definition from a registry or resolver, then either evaluates the definition's `RequiredPermissions` through `IExecutablePermissionEvaluator`, or falls back to an RBAC check at module scope or instance scope. The run kind itself comes from the presence of exactly one row in `WorkflowStartRequests` or `OperationStartRequests` (`IRunKindResolver.cs:35-55`). None of that is a column predicate.

**But the decision is a pure function of `(run kind, DefinitionId)` for one principal within one request.** That is the property the design uses.

### 17.2 Ruling: memoized fill

Authorization stays exactly where it is, as the authority. The query adds a **fill loop** around it, with a per-request memo of authorization decisions keyed by `(kind, DefinitionId)`. SQL narrows what the loop has to look at. It never decides access.

```
cursor     = decode(request.cursor)            // (CreatedAt, Id) or none
want       = clamp(take, 1, 100) + 1           // one extra row to prove there is more
batchSize  = max(want, 200)
scanBudget = 1000 rows                          // tunable constant, tested
scanned    = 0;  authorized = []; exhausted = false

while authorized.Count < want and scanned < scanBudget:
    batch = rows matching filters, after cursor, ORDER BY CreatedAt DESC, Id DESC, TAKE batchSize
    if batch is empty: exhausted = true; break
    kinds = resolve kinds for batch (one query)
    for each row in batch:
        scanned++; cursor = key(row)
        decision = memo[(kind, row.DefinitionId)] ??= authorize(kind, row.DefinitionId)
        if decision: authorized.Add(row)
        if authorized.Count == want: break

if authorized.Count == want:
    page = first (want - 1) rows;  next = key(page.Last)         // a real next page exists
elif exhausted:
    page = authorized;  next = null                              // source exhausted
else:                                                            // budget spent before filling
    page = authorized;  next = protect(key(last scanned row))    // continuable, possibly empty
```

The scan budget bounds the cost of a principal who can read few of many rows. It is what makes "keep fetching until the page is full or the source is exhausted" safe on a large table.

### 17.3 Filters pushed into SQL

All existing filters already run in SQL. Two run in memory today (N2) and move into SQL in this package:

- **`ExecutionKind`**: an `EXISTS` against the matching start-request table.
- **`ModuleId`**: resolve the operation ids of the module from the registry, then `DefinitionId IN (...)`.

New filters: `attention`, `createdTo`, and the execution kind in the URL. The pseudo-status `blocked` already translates (`RunQueryService.cs:51-58`).

**`attention` definition (owner ruling).** It uses the same semantic definition as the existing UI ranking, and the two must not drift. Code fact: `RunNeedsAttention` is `RunAttentionRank < 4`, and the rank is a function of the trimmed, lower-cased status alone (`US/RunScheduleOperatorUiHelpers.cs:12-26`):

| Rank | Statuses | Needs attention |
|---|---|---|
| 0 | failed, faulted, error, rejected | yes |
| 1 | parked | yes |
| 2 | cancelled, canceled | yes |
| 3 | running, in_progress, inprogress, claimed, pending, queued, starting | yes |
| 4 | completed, succeeded, success | no |
| 5 | any other value | **no** |

Two consequences. The set is a **positive** status list, so a server filter written as "not completed" would wrongly include rank 5. And matching must be case-insensitive after trimming, as the client rank is.

Resolution, in order of preference: (1) put the status classification in one lower layer that both sides already reference, beside `WorkflowLifecycleStatusNames` in `Core.Domain` (`Core.Domain/Workflow/WorkflowLifecycleVocabulary.cs:7`). Code fact: `Ui.Shared` reaches `Core.Domain` through `Management.Client`, and `Security` references `Core.Domain` directly, so direct reuse does not invert a dependency. The UI helper then calls it. (2) If LST-1.2 finds a reason that reuse is undesirable, the server keeps its own status list and a test enumerates the full status vocabulary and pins that the server set equals `RunNeedsAttention` for every value. Either way the equivalence test exists, because the rank also drives sorting elsewhere.

### 17.4 Ordering and cursor

| Item | Ruling |
|---|---|
| Order | `ORDER BY CreatedAt DESC, Id DESC` |
| Cursor fields | `CreatedAt` as exact ticks plus offset, and `Id`. Both are needed, because `CreatedAt` repeats |
| Predicate | `CreatedAt < c OR (CreatedAt = c AND Id < i)` |
| Id | `nvarchar(64)`. Ordering and predicate are both evaluated in SQL under the same collation, so they agree. A test proves it |
| Encoding | Versioned, opaque, and **protected** with ASP.NET Data Protection (§17.5) |
| Duplicate timestamps | Handled by the `Id` tiebreak. A test places rows with identical `CreatedAt` across a page boundary |
| Deleted or changed rows between pages | Keyset tolerates deletion. `CreatedAt` is immutable. A status change can move a row in or out of a status-filtered view (§9.2 item 5). The client de-duplicates on `RunId` |
| Page size | Default 50, maximum 100. No exact total |

### 17.5 Cursor protection

In the budget-spent branch the cursor is the key of the last row **scanned**, which can be a run the caller is not authorized to read. An unprotected cursor would disclose that run's timestamp and id. Cursors for this endpoint are therefore protected (ASP.NET Core Data Protection, purpose-bound), which also prevents forgery. Code fact: `src` contains no explicit `AddDataProtection` or `IDataProtector` use. The Management Host relies on cookie authentication, which registers Data Protection implicitly, so a provider is available but its key persistence is whatever the host configures today. LST-1.2 confirms that, and states what a restart does. If keys do not survive a restart, outstanding cursors become unreadable and the client restarts from the first page through the `cursor_invalid` path (§9.2 item 4), which is benign. If that proves unacceptable, a keyed HMAC over the cursor payload is the fallback. Audit cursors need no protection because audit read is all-or-nothing at instance scope.

### 17.6 Continuable empty pages

When the budget is spent with zero authorized rows, the response is an empty page with a non-null cursor. That is "not finished", not "empty" (§11). `PagedListQuery` may auto-continue, bounded to three consecutive requests, while `Items.Count == 0` and a cursor exists. After that it stops and the view shows row 7 of the decision table ("Still searching. Continue") with a Continue action. The copy discloses no count and no row.

### 17.7 Indexes

| Index | Why |
|---|---|
| `(CreatedAt DESC, Id DESC)` | The global list and the cursor predicate |
| `(IntegrationId, CreatedAt DESC, Id DESC)` | Integration Runs tab and the integration filter |
| `(ResourceContextId, CreatedAt DESC, Id DESC)` | Replaces the single-column `IX_OperationRuns_ResourceContextId` for the context filter. LST-1.2 measures first and may keep both |

Status is low-cardinality. LST-1.2 adds a `(Status, CreatedAt DESC)` index only if the query plan shows it is needed. `OperationRuns` can be a large table. LST-1.2 reports build time and states whether the index can be built online on the target SQL Server edition, and hands that to OPS-1.

### 17.8 Surface and callers

- `GET /api/runs` returns `KeysetPage<RunSummaryDto>` and accepts `take` and `cursor`. The Runs API owns paging. The client method `GetRunsAsync` changes shape. Callers to update: `RunsList.razor:429`, `IntegrationRuns.razor:157`, `IntegrationDetail.razor:661`, `ModuleDetail.razor:420`, and `Cli.Core/CliCommandService.cs:22`, which today fetches the whole list and should take the first page. The OpenAPI document under `docs/04-api` is updated.
- `IRunQueryService.ListRunsAsync` keeps its signature for the internal callers (N19). The keyset method sits beside it.
- **The other cap-before-authorization consumers have a defined home: LST-1.2a (§28).** The three run-derived signal methods (`GetFleetOperationalRunSignalsAsync`, `GetLatestAuthorizedScheduleOriginRunsAsync`, `GetAuthorizedScheduleOriginRunsAsync`, `RunQueryService.cs:102-279`, N23) and the `Take` callers of `ListRunsAsync` use the same cap-then-authorize loop. LST-1.2 builds the fill and memoization as a **reusable internal component**, not inlined in the keyset method, so LST-1.2a can adopt it without redesign. LST-1.2 itself does not change those consumers, which keeps the authorization-sensitive Runs package bounded.

### 17.9 Client

- `RunsList` adopts `PagedListQuery`, `ListStateView` and `ListPagerFooter`.
- **Remove the client triage re-sort** (D-16, N5). The server order is canonical. QW-1 owns the ordering change. If QW-1 has not landed when LST-1.2 starts, LST-1.2 removes the re-sort itself.
- "Needs attention" becomes the server `attention` filter. The checkbox stays, and its "Show all runs" button keeps its D-20 meaning of clearing a filter.
- The Summary section's "Counts from loaded rows" is relabelled so it cannot be read as a total.
- Status, execution kind, attention, integration, module and context are in the URL and written with replace (§13).
- Footer: "Showing N runs", and Load more. No total.

## 18. Audit cursor invariant

### 18.1 General invariant

**A keyset cursor must encode every field required to reproduce the list's stable ordering, and its predicate must compare on exactly those fields in exactly that order.**

### 18.2 The defect

Code fact. The query orders by `At DESC, Id DESC` (`PERS/SqlAuditService.cs:53-55`). The token is `Id` alone (`:77`) and the next page filters `Id < lastId` (`:148-151`). That is only correct if `Id` order equals `At` order. `At` is whatever the caller supplied as the record timestamp (`:28`). `Id` is a database identity assigned at insert (`AppertureDbContext.cs:303`). A concurrent writer, a caller whose clock is behind, or a backfilled row breaks the equivalence, and "Load more" then skips or repeats rows.

### 18.3 The correction AUD-1 makes (AUD-1.5)

- Token encodes `(At, Id)` as exact ticks and offset plus the identity value. Opaque and versioned, matching §9.2.
- Predicate: `At < a OR (At = a AND Id < i)`, with the same `ORDER BY At DESC, Id DESC`.
- The existing index on `(At DESC, Id DESC)` (`AppertureDbContext.cs:320`) supports it. No schema change.
- A legacy numeric token, and any malformed token, is rejected as `cursor_invalid`. Today a malformed token is silently ignored and returns the first page again, which repeats rows (N21). An in-flight page after deploy restarts through the client's `cursor_invalid` path.
- Tests: rows whose `At` order differs from `Id` order across a page boundary; equal `At` across a boundary; filters combined with a cursor; a malformed token. No test of any continuation token exists today (N21), so these are new, and they must run against SQL Server.

### 18.4 What LST-1 contributes to the audit page

The Audit **page** adopts `PagedListQuery`, `ListStateView` and `ListPagerFooter` after LST-1.1. That fixes N7 by construction: the applied request is a snapshot (so the window does not move), load-more failure keeps the rows, and each call carries a token. AUD-1 owns that adoption and the URL convention for the timeline filters (section 6.5). The server cursor fix has no dependency on LST-1.1.

## 19. AsyncSearchSelect

### 19.1 Ruling

A **single-select ARIA combobox** over a caller-supplied search delegate. Not a universal combobox framework.

- **Single-select only for RC1.** Team membership adds one member at a time. Multi-select, grouping, creatable options and async preload of everything are later.
- **Source-agnostic.** `Func<string?, CancellationToken, Task<SearchResult<TItem>>> Search`, where `SearchResult<TItem>(Items, HasMore)`. The component never calls an endpoint of its own.
- **Value and display reference are generic.** The component binds the **selected item** (`TItem?`), not only a key, because a search result may not include the item that is already selected (an existing member who no longer appears in the first page of results). The caller supplies `Func<TItem, string>` for the key and a template for the label.
- **Bounded results.** The caller sends a `take` (default 20, ceiling 50). When `HasMore` is true the list ends with "Showing the first N. Keep typing to narrow." It never loads everything.

### 19.2 Behaviour

| Aspect | Contract |
|---|---|
| Lifecycle | Built on `QueryRunner`: 300 ms debounce, cancel superseded searches, latest wins |
| Opening | On focus or typing, show the first bounded page for the empty query (minimum query length is an option, default 0) |
| While searching | Keep the previous results, dimmed, with `aria-busy` on the listbox. Do not flash "No results" |
| Empty | "No matches" only when settled, the query is applied, and `HasMore` is false |
| Error | Inline "Search failed" with Retry. The selected value is untouched |
| Selected display | Shown in the input when closed. A clear control is available when the field is optional |
| Disabled items | `Func<TItem, string?> DisabledReason`. Needed so a user who is already a member, or is disabled, is shown and not selectable |
| Required parameters | An accessible label. The component has no unlabelled mode |

### 19.3 Keyboard and ARIA

WAI-ARIA 1.2 combobox with a listbox popup. Focus stays in the input.

- Input: `role="combobox"`, `aria-expanded`, `aria-controls`, `aria-autocomplete="list"`, `aria-activedescendant`.
- Options: `role="option"`, `aria-selected`, `aria-disabled` where applicable. Option ids are stable.
- Down and Up move the highlight and open the list. **Arrow keys never select.** Enter selects the highlighted option. Escape closes, then clears the query on a second press. Tab leaves without selecting. Home and End move the text caret and are not intercepted.
- A polite status region announces "Searching", "N results" and "No matches".

This differs deliberately from `SearchableList`, which selects on every arrow press and has no `aria-activedescendant` (N16). `SearchableList` is untouched and keeps serving the time-zone and view selectors.

### 19.4 Popup and design-system constraints

The popup is an in-flow panel anchored to the input. It uses the existing `.ui-menu-panel` styling and `--z-*` tokens, as `ViewSelector` does. It is not a new overlay primitive and no file may carry a name the UIX-6 guard forbids (N17). Rendering inside a `ManagementDialog` can clip an in-flow panel. LST-1.1 proves the component inside a dialog before LST-1.4 depends on it (§31).

## 20. Team-selector seam

### 20.1 What LST-1.0 reserves

1. The selector takes a search delegate. It never calls `GET /api/users` itself, so no package has to change the component to change the user source.
2. The item type for a user is `UserReferenceDto(UserId, Username, DisplayName)` from IAM-9 §14. The selector shows the username and the display name, and never the raw id.
3. Candidates exclude Recovery Access (IAM-9 already excludes it from search) and, once IAM-11 supplies status, disabled users, which appear as disabled options rather than missing ones.
4. `DisabledReason` exists on the selector (§19.2).

### 20.2 What later packages decide

- **Which endpoint a team editor calls: ruled (owner, Q5).** A narrow candidate-discovery endpoint. A caller holding `operational_team.manage` can perform the bounded candidate discovery needed to manage team membership without also receiving broad user-directory access through `user.read`. The endpoint:
  - requires `operational_team.manage`;
  - returns `UserReferenceDto` only;
  - supports bounded server search (a clamped `take`, a minimum of the same bounds as IAM-9 §16);
  - excludes Recovery Access;
  - respects user status once IAM-11 exists (disabled users are shown as unselectable, or excluded, as the IAM-11 status contract settles);
  - exposes no email, UPN, directory snapshot or unrelated profile data;
  - writes no search term to audit (program gate §6.5).

  `GET /api/users` authorization is **not** widened for this purpose. IAM-9 already leaves `user.read` as it is and defers this decision to LST-1.3 and IAM-10 (`iam9-design-gate.md` §16). **IAM-10 treats candidate discovery as part of the `operational_team.manage` capability semantics**, so the permission description and its repair arrays cover it. The endpoint itself is specified with IAM-10 and built in LST-1.4. Which package builds it is unchanged from §28.
- **Teams on user detail (LST-1.5).** The reverse lookup `GET /api/users/{id}/operational-teams` and its permission follow the same decision.
- **A user's view of their own teams** (D-08) is designed separately if needed.

### 20.3 What this gate does not change

Team authorization. Today every team method requires `schedule.manage`, including list and get (N14). LST-1.4 inherits that until IAM-10 adds `operational_team.manage` and its repair. LST-1.4 must also close two defects it will find in the member editor: `ReplaceMembers` resets every member to enabled, and it does not de-duplicate (N14).

## 21. Full-load lists

### 21.1 Which families are legitimately complete

Verified against code. None of these has a server cap today.

| Surface | Basis | Growth note |
|---|---|---|
| Connections | `GetConnectionsAsync`, no limit | Whole list serialized and rendered |
| Schedules | `GetSchedulesAsync`, no limit | Also makes one sequential readiness call per distinct integration, so load time grows with integrations (`SchedulesList.razor:307-329`) |
| Integrations fleet | `GetIntegrationFleetAsync`, no limit | Heavy per-row payload |
| Roles, Modules, Applications | No limit | Small by nature |
| Operational Teams | `ToListAsync` ordered by name | Small |
| Application Resources | Capped at 500, server search, total shown | Correctly a capped list (family E), not complete |

Components is also capped (200, server search, total shown).

### 21.2 Contract

- A list may **client-sort and client-filter only when the server returned the entire authoritative set**, which the page asserts by treating the result as `Complete`.
- An endpoint that is complete today and gains a cap later must return a truncation signal at the same time, or its client filters become correctness bugs (§12 item 4).
- A complete list must have a **deterministic order**, ending in a unique key. Connections and Roles have none today (N20) and render in database order. LST-1.6 adds a stable server order to those two.
- LST-1 does not impose paging on these lists for uniformity.
- A **growth tripwire** is recorded for RCV-1 and not built here: the Schedules sequential readiness calls and the fleet payload are the first to need attention if an instance grows large.

## 22. Error and refresh behaviour

| Case | Behaviour |
|---|---|
| First-load failure | `ErrorState` with Retry. No content to keep |
| Refresh failure, content present | Content stays. An inline alert banner with Retry. The content is marked stale |
| Search or filter failure, content present | The previous rows stay, dimmed, with a banner "Couldn't load results for the current search. Showing previous results." and Retry. A displayed filter that does not match the displayed rows is made visible rather than hidden |
| Load-more failure | The rows stay. The footer shows an inline error and Retry for the same cursor. **Never** `ErrorState` (fixes Audit `:352-355` and the operation-items panel at `:190`) |
| Forbidden | `ForbiddenState`. A 403 is not an error |
| Superseded or disposed request | Silent. Not an error and not a state change |
| Refresh after a mutation (ACT-1) | The page calls `RefreshAsync`. Content stays, `aria-busy` is set, and nothing is replaced by a loading panel (D-19) |
| Capabilities not loaded | `LoadingState`, never a forbidden flash |

Current destructive transitions to correct when each list is touched: Inventory blanks on every load, Application Resources blanks on every search, Customer Activity clears rows on every filter change, Integration Runs blanks on a status change, the notification inbox blanks after mark read. Only the lists in §24 are corrected in LST-1. Post-action blanking on the inbox, Integration Schedules and Integration Connections is ACT-1's refresh rule.

## 23. LST-1 and LST-2 boundary

### 23.1 LST-1 owns

Query correctness. The two paging contracts. The query lifecycle helper. Shared list state. The pager footer. `AsyncSearchSelect`. The URL-state convention. The correctness fixes. Truncation disclosure. User selection and team membership.

### 23.2 LST-2 owns

Column definitions and column schemas. Sortable headers. Quick and advanced filter controls. Default row-action conventions. Local instant pre-filtering. Virtualization. Continuous loading. Numbered pagination, if wanted. Restoring scroll position and accumulated pages on return from a detail page.

### 23.3 Seams LST-1 leaves for LST-2

- `PageFetch` and completeness carry what a virtualized or continuous list needs, so resource queries do not change.
- A sortable header must know whether sort is server-authoritative. That is the one runtime fact LST-2 will need beyond completeness. It is recorded here so the later record is built then, from a real consumer (§10.1).
- The view and filter convention (§14.1) is the foundation for filter controls.
- The helper's `Schedule` and `RunAsync` already model "instant local pre-filter, then authoritative server result". LST-2 adds the local half.

### 23.4 Does any RC1 decision need amendment?

**No.** Two clarifications, neither an amendment:

1. Section 6.1 and the audits say Runs "applies its row cap before authorization filtering". That is accurate only when `Take` is set. The global Runs page is unbounded (N1). The binding requirement is unchanged and §17 satisfies it.
2. The addendum describes `NavigationLoadGuard` as having a cancellation source. It has none (finding 6). It is a generation counter. The design here is unaffected.

The addendum's table lists keyset paging for the notification inbox and admin instances. The program gate does not bind that, and the owner ruled in Q1 that RC1 discloses truncation and adds no keyset or Load more there. No decision is amended.

**Confirmation after the owner review (2026-10-03): no RC1 program decision needs amendment.** The owner's Q-answers and Amendments 1 to 5 refine this gate only. Amendment 5 adds LST-1.2a as a package under D-18's LST-1 scope. It does not change D-18, section 6.1, or the program sequence beyond placing LST-1.2a after LST-1.2 and before RCV-1.

## 24. Surface classification matrix

Legend. **Load**: Full, Offset, Keyset, Capped. **Search**: Server, Client, none. **Sort**: Server, Client, Fixed. **Total**: exact total returned and shown.

| Surface | Load | Search | Sort | Total | URL state today | Known correctness problem | RC1 action |
|---|---|---|---|---|---|---|---|
| **Runs** (global) | Full, unbounded | None (status select) | Client triage re-sort | No | Writes six filters, push | Unbounded (N1); empty branch ignores pending (N6); client attention filter and triage sort break under paging (N5) | **Change.** Keyset (LST-1.2) |
| **Integration Runs tab** | Capped 200 | None | Fixed, `CreatedAt` | No | None | Cap before authorization; silent cap; blanks on status change | **Change.** Same endpoint and primitives (LST-1.2) |
| **Audit** | Keyset, 50 | Selects, actor on Enter | Fixed, `At` | No | Reads four, writes none | Cursor mismatch (finding 11); malformed token silently repeats page 1 and no cursor test exists (N21); live-filter load-more and moving window (N7); destructive load-more failure; passes no token | **Change.** AUD-1 cursor; adopt primitives after LST-1.1 |
| **Notifications inbox** | Capped 100 | None (`readFilter` select) | Fixed, `CreatedAtUtc` (no tiebreak) | No | None | Silent cap (finding 7); blanks after mark read | **Change** (truncation disclosure, LST-1.6). No keyset or Load more in RC1 (Q1). Revisit post-RC1 if the cap proves limiting |
| **Notification instances** | Capped 50 | Server (three text boxes, Apply) | Fixed | No | Reads three once | Silent cap | **Change** (truncation disclosure, LST-1.6). No keyset in RC1 (Q1) |
| **Notification definitions** | Capped 100 | Server (Apply) | Fixed | No | Reads one once | Silent cap | **Change** (truncation disclosure, LST-1.6). No keyset in RC1 (Q1) |
| **Inventory Resources** | Capped 100 | Server (Apply) | Server (`sort`) | Computed, dropped by DTO | Reads six once; writes slug rewrite | Silent cap; total dropped; blanks every load; no `OnParametersSet` | **Change** (LST-1.6): restore total, truncation note, keep content, URL write-back |
| **Inventory Activity** | Windowed merge | Server, applied after caps | Fixed | No | As above | **False "no results"** (finding 8); source dropped silently on authorization failure | **Change** (LST-1.6): remains a bounded recent-activity view (Q2). Search-before-cap where translatable. Its empty and scoped messages name exactly what was searched (§16.2) and never imply that no matching history exists outside it. Runs and Audit are the authoritative history |
| **Users** | Offset, 20 | None (IAM-9 adds `q`) | Fixed, `Id` (IAM-9 moves to `Username`) | Yes | None | `_skip` not rolled back; page-past-end empty (N13) | **Leave alone** in LST-1. IAM-9 adds search. Adopt primitives when next touched |
| **Roles** | Full | None | **Unordered** (no `ORDER BY`) | n/a | None | Order is database order (N20) | **Change** (LST-1.6, stable order only) |
| **Integrations fleet** | Full | None | Server `sort`; client attention rank | n/a | Reads `context` only | None | **Leave alone** |
| **Connections** | Full | None | **Unordered** (no `ORDER BY`) | n/a | Reads `context` only | Order is database order; one binding query per connection (N20) | **Change** (LST-1.6, stable order only) |
| **Schedules** | Full, N+1 readiness | None | Client triage | n/a | Reads `context` only | Load time grows with integrations; no forbidden branch | **Leave alone**; tripwire (§21.2) |
| **Applications** | Full | Client by name (sort) | Client by name | n/a | None | None | **Leave alone** |
| **Components** | Capped 200 | Server, debounced | Fixed, name | Yes ("Showing N of M") | Reads `kind`; no write | Duplicate debounce and sequence; provider caps at 200 before `q` (N12) | **Adopt primitive** (runner, LST-1.1). **Required correctness fix in LST-1.6:** the notification-definition provider must apply `q` before its cap, or otherwise be authoritative for the scope it claims (N12). Not optional |
| **Application Resources** | Capped 500 | Server, debounced | Fixed, name | Yes | None | Duplicate debounce and sequence; blanks on every search | **Adopt primitive** (runner, LST-1.1) |
| **Operational Teams** | Full | None | Fixed, name | n/a | None | Editor is a text grammar; `ReplaceMembers` resets and does not de-duplicate; requires `schedule.manage` to view (N14) | **Change** (LST-1.4, after IAM-9, IAM-10 and IAM-11) |
| **Demo customers** | Offset, 50 | Server (Enter or Apply) | Fixed | Yes | None | The server loads every matching item and pages in memory (N22). A sample surface, so noted and not fixed here | **Adopt primitive** (`PagedListQuery`, footer, LST-1.1) as the offset reference adopter |
| **Customer activity** | Offset, 25, has-more | Server, live | Fixed | In DTO, ignored | None | **Stale response overwrites** (finding 9); `_loading` cleared early; load-more append race; clears rows on filter change | **Change** (LST-1.6) |
| **Operation items panel** | Offset, total | Anchors only | Fixed | Yes | None | Load-more failure hides the table; reset blanks | **Later** (adopt when next touched) |
| **User pickers** (two notification pages) | First 200 users | None | `Id` | Ignored | n/a | Users past 200 unselectable; break-glass users omitted | **Change** (LST-1.3, `AsyncSearchSelect`) |

Business Data other than Demo customers has no list: `BusinessDataLanding.razor` links to Customers only.

**Not a list surface, but in this matrix because it has the same defect:** the run-derived signal queries behind the Integrations fleet and schedule diagnostics (`GetFleetOperationalRunSignalsAsync`, `GetLatestAuthorizedScheduleOriginRunsAsync`, `GetAuthorizedScheduleOriginRunsAsync`) and the `Take` callers of `ListRunsAsync`. They cap before authorization and can under-report when unauthorized rows dominate (N23). **Change** in LST-1.2a (§28). It reuses LST-1.2's fill and memoization and changes no authorization policy and no signal definition.

## 25. API and DTO implications

| Change | Package |
|---|---|
| `OffsetPage<T>`, `KeysetPage<T>` wire records (illustrative names) | LST-1.1 |
| `PageFetch<T>` with an explicit `Completeness` field, and `ListCompleteness` (client-side), plus the per-source adapters (§9.3) | LST-1.1 |
| `GET /api/runs`: `take`, `cursor`, `attention`, `createdTo`; returns `KeysetPage<RunSummaryDto>`; protected cursor; the `ExecutionKind` and `ModuleId` filters move into SQL | LST-1.2 |
| The run status classification for `attention` in one shared definition (§17.3) | LST-1.2 |
| `IManagementClient.GetRunsAsync` changes shape; five callers updated (§17.8); OpenAPI updated | LST-1.2 |
| Indexes on `OperationRuns` (§17.7) | LST-1.2 |
| The authorization fill and memoization built as a reusable internal component | LST-1.2 |
| Fleet signal and schedule-origin queries, and `ListRunsAsync(Take)`, adopt the fill. No signature change intended; a partial-evidence representation if budget exhaustion needs one (§28) | LST-1.2a |
| Inventory result gains `TotalMatching` and an exact scope disclosure for Activity | LST-1.6 |
| Inbox, instances and definitions return `HasMore` from a `take + 1` probe | LST-1.6 |
| Notification-definition component provider applies `q` before its cap | LST-1.6 |
| Candidate endpoint for team editors: `operational_team.manage`, `UserReferenceDto` only, bounded search, Recovery Access excluded, status-aware after IAM-11, no email or directory data, and `GET /api/users` authorization unchanged (§20.2) | LST-1.4, specified with IAM-10 |
| Team member add and remove endpoints; reverse lookup | LST-1.4, LST-1.5 (Part 1) |
| Audit cursor `(At, Id)` | AUD-1.5, not LST-1 |

No change to existing DTO shapes is made for uniformity alone.

## 26. Accessibility

- `ListStateView` wraps content with `aria-busy` equal to `IsBusy`. Failure banners are `role="alert"`. The loading and empty states keep their existing `role="status"`. The wrappers today have no `aria-live`, no `aria-busy` and no reduced-motion handling (shared-infrastructure audit); the view adds `aria-busy`. Any spinner the view or footer adds respects `prefers-reduced-motion`. `animate-spin` has no override today (`UI/wwwroot/css/app.css:584`), and the pending indicators are checked in RCV-1 (section 6.9).
- Dimmed stale content must keep contrast at the design-system minimum, and "stale" is conveyed by text as well as opacity.
- `ListPagerFooter`: polite live region updated only after a load-more completes. Focus rules in §15.4.
- `AsyncSearchSelect`: the combobox contract in §19.3, a polite status region, an accessible name that cannot be omitted, option ids that are stable, and disabled options that remain reachable by the highlight and are announced with their reason.
- Selected state in lists follows the design system (`data-state="on"` plus an `sr-only` "Selected", not a prose suffix).
- Missing values render as an em dash, per the interaction patterns document.

## 27. Test matrix

`HtmlRenderer`-based render tests exist today. They cannot dispatch events, so keyboard behaviour cannot be proven with them (N10). The owner adopted bUnit (Q9). §31 requires the .NET 10 compatibility check before it is added.

| Level | What | Package |
|---|---|---|
| **Unit** (in `US`, no rendering) | `QueryRunner` and `PagedListQuery` state machine: latest wins when an older response lands last; debounce coalesces N `Schedule` calls to one fetch (driven by a fake `TimeProvider`); `Schedule` then `RunAsync` cancels the timer; cancelled request never sets `Failed`; dispose cancels and silences; load-more bound to its request; load-more discarded when the request changes; appended rows de-duplicated; failed refresh keeps content and sets stale; failed load-more keeps rows; 403 sets forbidden | 1.1 |
| Unit | `ListStateView` decision function, every row of §14.2, with emphasis on: pending with no content never yields empty; stale empty is not shown while busy; `MoreAvailable` empty yields the continuing view | 1.1 |
| Unit | Footer rules: copy for each §15.2 row; truncation note copy; no total shown for keyset | 1.1 |
| Unit | URL helper: build with replace-style parameter set, null removes, unknown preserved; parse with defaults and normalization flag; invalid value normalized; round trip | 1.1 |
| Unit | Every adapter row of §9.3 sets `Completeness` as stated: keyset with and without a cursor, offset partial and whole, complete set, a capped DTO with `TotalMatching` greater than the items (`Truncated`, no cursor), a capped DTO that is whole (`Complete`), and a windowed endpoint (`ScopeLimited`). An adapter never reports `Complete` for a result it cannot show to be whole | 1.1 |
| **SQL Server** | Runs keyset: order with equal `CreatedAt` across a page boundary; no gaps or repeats over a seeded table; cursor `cursor_invalid` handling | 1.2 |
| SQL Server | Runs authorization before the page boundary: the caller is authorized for 5 old runs and 500 newer runs are not, the page returns the 5, not an empty page; budget spent returns a continuable empty page; fill exhausts and returns no cursor | 1.2 |
| SQL Server | `ExecutionKind` and `ModuleId` filter in SQL: a match older than the newest N is found | 1.2 |
| Unit and SQL Server | `attention` equivalence: for every status in the lifecycle vocabulary plus case and whitespace variants and an unknown value, the server filter includes exactly the statuses `RunNeedsAttention` includes (§17.3) | 1.2 |
| SQL Server | **Run-derived signals** (1.2a): with unauthorized runs filling the newest `BatchMaxRows`, an older authorized failed run is still counted; `GetLatestAuthorizedScheduleOriginRunsAsync` returns the newest authorized schedule-origin run when the newest overall is unreadable; existing signal outputs are unchanged when every run is authorized; budget exhaustion is reported as defined, never as a clean zero; the authorization policy is not touched | 1.2a |
| SQL Server and HTTP | **Components search** (1.6): a notification definition beyond the first 200 is found by `q`; the `TotalMatching` and the truncation message are correct when the set exceeds the cap; a search that finds nothing within a stated cap names the cap | 1.6 |
| SQL Server | Cursor opacity: a tampered cursor is rejected; a cursor from one principal does not disclose a row | 1.2 |
| SQL Server | Audit cursor: `At` order differs from `Id` order across a boundary; equal `At`; combined filters | AUD-1.5 |
| SQL Server and HTTP | Inventory Activity search finds a match outside the newest 100 runs or outside the cap; totals and `HasMore` correct on capped endpoints | 1.6 |
| SQL Server and HTTP | Inbox, instances and definitions `HasMore` at exactly cap, at cap plus one | 1.6 |
| **Component** (bUnit) | `ListStateView`: content stays and `aria-busy` during refresh; no empty state while pending; banner on failed refresh | 1.1 |
| Component | `ListPagerFooter`: Load more disabled while loading; error and Retry; focus after the last page | 1.1 |
| Component | `AsyncSearchSelect`: arrows highlight without selecting; Enter selects; Escape closes then clears; Tab does not select; `aria-activedescendant` tracks the highlight; stale result never replaces newer; no "No matches" while pending; disabled option announced; works inside `ManagementDialog` | 1.1 |
| Component | Runs page: no false empty state while pending; attention filter is server-driven | 1.2 |
| Component | Customer activity: stale response does not overwrite; `_loading` not cleared by a superseded request | 1.6 |
| **Source guards** | Update `ComponentCatalogUiSourceTests` and `ApplicationResourcesUiSourceTests` to pin the helper instead of the hand-written timer. Add a guard that list pages pass the helper's token to the client | 1.1 |
| **Browser** (sparingly, Playwright, gated) | One flow: apply a filter, refresh, and the list restores from the URL; browser Back after a detail drill returns to the same query. One flow: select a user through `AsyncSearchSelect` and add to a team | 1.2, 1.4 |

No browser coverage is required for every list. Skipped SQL tests are reported as skipped, not passed.

## 28. Implementation decomposition

The package sequence refines the existing one. Names follow the Part 1 program so no reference elsewhere changes. The 1.3 to 1.5 content follows Part 1 (`rc1-architecture-audit-and-program-plan.md` §I LST-1.3 to 1.5), not the working shape in the LST-1.0 brief: 1.3 is `UserRef` adoption outside Governance with a `UserSelector`, 1.4 is team membership, 1.5 is teams on user detail. The generic selector is built earlier, in 1.1.

```
LST-1.0 (this gate)
   |
   +-- LST-1.1  Core primitives -------+-- LST-1.2  Runs keyset --- CI checkpoint --> LST-1.2a  Run-derived signals
   |                                   |
   |                                   +-- LST-1.6  Correctness
   |                                   |
   |                                   +-- AUD-1 UI adoption (after 1.1)
   |
   +-- LST-1.3 (after IAM-9) --> LST-1.4 (after IAM-9, IAM-10, IAM-11) --> LST-1.5
```

*Note (product-owner ruling, 2026-10-06; [AUD-1.0 design gate](../audit-hardening/aud1-design-gate.md) §18).* The "AUD-1 UI adoption" node above is the AUD-1.5b package. The surfaces LST-1.3 adopts (scope below: "the surfaces IAM-9 §23 assigns to this package") are those AUD-1.0 §18 assigns to LST-1.3, including two notification-subscription recipient columns IAM-9 §23 did not list. LST-1.3 depends on no AUD-1 package. The team-members table stays with LST-1.4.

Program rule retained: LST-1.0, 1.1, 1.2, 1.2a and 1.6 run alongside NAV-2 and wait for nothing in Track A. 1.3 to 1.5 keep their IAM dependencies (program gate §5). **LST-1.2a is RC1 work. It must land before RCV-1.** It depends on LST-1.2 and on nothing else.

### LST-1.1 Core primitives

- **Objective.** The shared, tested building blocks, proven on three existing lists.
- **Prerequisites.** This gate approved (done). bUnit adopted (Q9), after confirming compatibility with the repository's .NET 10 target. Whichever of LST-1.1 and IAM-9.4 lands first adds the dependency, and the other reuses it. No competing component-test infrastructure is created. No IAM dependency.
- **Scope.** The two paging records, `PageFetch` with its explicit `Completeness` field and the per-source adapters of §9.3, `ListCompleteness`. `QueryRunner` and `PagedListQuery`. `QueryStatus` and the `ListStateView` decision function and component. `ListPagerFooter`. `AsyncSearchSelect`, built and proven but not adopted by a product page. The URL helper and parameter constants. Adopters: `ComponentsList` and `ApplicationResources` onto the runner (behaviour preserved, except Application Resources no longer blanks on search); `DemoCustomersList` onto `PagedListQuery` and the footer as the offset reference.
- **Non-goals.** No Runs, Audit or Inventory change. No `UserSelector`. No sort, columns or filters. No migration of other pages. No change to `ControlCenter` or `IntegrationDetail`.
- **Repository areas.** `US` (helpers, models), `UI/Molecules` (components), `UI/wwwroot/css`, `MC` (wire records), `tests/Apperture.Management.Ui.Shared.Tests`, `tests/Apperture.Management.Ui.Client.Tests`.
- **API and data.** None beyond the wire records.
- **Invariants.** §10.3 items 1 to 11. §11. §14.2 decision table. §19.3 keyboard contract.
- **Tests.** The unit and component rows of §27 for 1.1, plus the source-guard updates.
- **Handback evidence.** Test names and counts. The decision-table test. A screenshot or render of the footer states. A note on the `AsyncSearchSelect` inside `ManagementDialog` proof. A list of source-guard tests changed and why. Any divergence from the illustrative signatures.
- **Recommended model.** Sonnet, high reasoning. The concurrency semantics are small but exact. Senior review of the helper and the select's keyboard model.

### LST-1.2 Runs keyset paging

- **Objective.** Close the Runs defect with keyset paging and authorization before the page boundary.
- **Prerequisites.** LST-1.1. QW-1 Runs ordering if landed, otherwise this package removes the client re-sort.
- **Scope.** §17 in full: fill loop with memo, protected cursor, SQL for `ExecutionKind` and `ModuleId`, the new filters, indexes, `KeysetPage` response, the five callers, `RunsList` and `IntegrationRuns` adoption, URL write with replace, the footer, OpenAPI. The `attention` filter on one shared status definition (§17.3). The fill and memoization built as a **reusable internal component** so LST-1.2a can adopt it without redesign (§17.8).
- **Non-goals.** No total count. No change to run authorization rules. No change to run detail. No virtualization. **No change to the fleet signal queries or to the `Take` callers of `ListRunsAsync`; LST-1.2a owns them.**
- **Repository areas.** `SEC/RunQueryService.cs`, `API/RunsController.cs`, `MC`, `PERS` (migration), `UI/Pages/Runs`, `UI/Pages/Integrations/IntegrationRuns.razor`, `docs/04-api`, SQL tests.
- **API and data.** §25. A migration adding the indexes in §17.7.
- **Invariants.** §9.2, §11, §17.2 to §17.6, §13.2.
- **Tests.** The SQL Server and Runs component rows of §27, plus one gated browser flow.
- **Handback evidence.** Query plan for the global and the integration query. Index build time and online-build statement for OPS-1. The authorization-before-boundary test output. Any skipped SQL tests reported as skipped. The caller list as changed. A full CI checkpoint, as Part 1 requires after Runs paging.
- **Recommended model.** Opus-class or Sonnet at the highest reasoning available, with senior review. It is authorization-sensitive, concurrency-sensitive and a schema change. Part 1 already requires senior review of this package.

### LST-1.2a Run-derived signal authorization-fill correctness

Added by the owner review of this gate. N23 is an RC1 correctness defect and has this package as its home.

- **Objective.** Unauthorized runs cannot consume a candidate cap and create false-negative operational signals.
- **Prerequisites.** LST-1.2 merged, so the fill and memoization semantics are settled and reusable. No IAM dependency. Must land before RCV-1.
- **Why a separate package.** LST-1.2 is the most authorization-sensitive package in the epic. The signal queries are aggregates, not pages, and their completeness semantics differ (below). Folding them into LST-1.2 would widen a package that is already reviewed at the highest level. The decision to split is recorded here so the defect is scheduled, not noted.
- **Scope.** The three run-derived signal methods: `GetFleetOperationalRunSignalsAsync`, `GetLatestAuthorizedScheduleOriginRunsAsync` and `GetAuthorizedScheduleOriginRunsAsync` (`RunQueryService.cs:102-279`), whose consumers are `IntegrationReadinessService` and `IntegrationScheduleDiagnosticsService`. Also the `Take` callers of `ListRunsAsync`: the dashboard window query, control activity, control attention, the Inventory Activity run source and the operations controller (N19), if they pass a cap.
  - Each query replaces "newest N candidates, then authorize" with the settled fill: the cap counts **authorized** rows, with the per-request `(kind, DefinitionId)` memo and a bounded scan budget.
  - `GetLatestAuthorizedScheduleOriginRunsAsync` takes the newest schedule-origin run per integration and then authorizes it. If that run is unreadable, the integration gets no evidence even when an older readable run exists. The fill is per integration.
  - **Budget exhaustion must be defined and must never read as clean.** The signal types have no partial flag today. LST-1.2a decides the minimal representation (for example an incomplete-evidence indicator the readiness consumer treats as "could not be fully evaluated") and does not report a zero count as healthy when the budget was spent. If the representation would materially change the readiness contract, the package stops and surfaces it rather than widening.
- **Non-goals.** No change to any authorization service, permission or rule. No change to a signal definition, the 7-day lookback, `RepeatedFailureThreshold` or `BatchMaxRows`. No redesign of how the global row cap is shared across integrations (a noisy integration crowding others is existing behaviour, separate from authorization). No UI. No paging.
- **Repository areas.** `SEC/RunQueryService.cs`, `SEC/IRunQueryService.cs`, the shared fill component from LST-1.2, `SEC/IntegrationReadinessService.cs` and `IntegrationScheduleDiagnosticsService.cs` only if a partial-evidence indicator is added, SQL tests.
- **API and data.** No schema change. No endpoint change intended.
- **Invariants.** An authorization decision is made by the same services as today and is not duplicated. Unauthorized rows never count against a cap. With every run authorized, the outputs equal today's outputs.
- **Tests.** The 1.2a row of §27. A before and after comparison on a seeded scenario.
- **Handback evidence.** The seeded scenario with before and after counts. A diff showing the authorization services are untouched. The defined budget-exhaustion behaviour and its test. The list of consumers and what each does with incomplete evidence. Skipped SQL tests reported as skipped.
- **Recommended model.** Sonnet, high reasoning, with senior review. It reuses a primitive that LST-1.2 has already settled.

### LST-1.3 User selector and `UserRef` adoption outside Governance

- **Objective.** Replace raw user ids and the truncated pickers.
- **Prerequisites.** LST-1.1. IAM-9.3 (search and `UserReferenceDto`) and IAM-9.4 (`UserRef`).
- **Scope.** A thin `UserSelector` over `AsyncSearchSelect` and `GET /api/users?q=`. `UserRef` adoption on the surfaces IAM-9 §23 assigns to this package. Replace the two notification pickers (N11). The managed-contact link.
- **Non-goals.** No team editor. No change to `user.read`. No candidate endpoint (that is built in LST-1.4, §20.2).
- **Repository areas.** `UI/Molecules`, `UI/Pages/Config/Notifications`, `UI/Pages/Integrations/IntegrationNotifications.razor`, `UI/Pages/Config/Contacts`, the surfaces in IAM-9 §23.
- **Invariants.** Raw ids are not shown. A search term is never audited (section 6.5).
- **Tests.** Component tests for the selector wrapper and for each replaced picker. HTTP test for 403 without `user.read`.
- **Handback evidence.** Before and after of each surface. Confirmation no page lists users by id.
- **Recommended model.** Sonnet, high.

### LST-1.4 Operational Team membership

- **Objective.** Operators select a user and add or remove members.
- **Prerequisites.** LST-1.1 and 1.3. IAM-9. IAM-10 (the `operational_team.manage` permission, D-08). IAM-11.1 (user status). The Q5 ruling is made: a narrow candidate endpoint (§20.2), specified with IAM-10 and built here.
- **Scope.** Member add and remove endpoints. `UserSelector` plus the existing contact and email entry. Resolved names in the member table. Team audit events. Close the `ReplaceMembers` defects (N14). Optional team delete if Part 1's D-10 ruling allows.
- **Non-goals.** No change to team authorization beyond adopting the IAM-10 permission. No self-view of own teams.
- **Repository areas.** `SEC/OperationalTeamManagementService.cs`, `API/OperationalTeamsController.cs`, `MC/OperationalTeamDtos.cs`, `UI/Pages/Attention/AttentionContactGroupDetail.razor`.
- **Invariants.** Concurrent member adds do not lose rows. Disabled members stay disabled. Duplicates are refused.
- **Tests.** Service and controller tests (none exist). A concurrency test for simultaneous adds. One gated browser flow: select a user, add to team.
- **Handback evidence.** API diff. The concurrency test output. Audit event list.
- **Recommended model.** Sonnet, high reasoning.

### LST-1.5 Teams on user detail

- **Objective.** User detail shows the user's teams and can add the user to one.
- **Prerequisites.** LST-1.4.
- **Scope.** Reverse lookup endpoint. A Teams section on `UserDetail.razor`. An add-to-team control gated by the same permission as LST-1.4. Disabled and decommissioned users are flagged.
- **Non-goals.** No team creation from user detail.
- **Recommended model.** Sonnet.

### LST-1.6 Correctness and truncation

- **Objective.** Fix the live false-empty defects and disclose every silent cap.
- **Prerequisites.** LST-1.1. No IAM dependency.
- **Scope.**
  - **Components search (required, N12).** The notification-definition component provider caps at 200 before the catalog applies `q`, so a Components search can report no match for a definition outside the first 200. Fix it so the search is server-authoritative for the scope it claims. The implementation chooses the smallest repository-appropriate correction: apply `q` (and the other catalog filters the provider can) before its cap, or return a provider-level truncation signal that the catalog surfaces through `TotalMatching` and the truncation note. No keyset paging and no list redesign is required. This is a correctness fix, not an optimization, and it ships in RC1.
  - Inventory Activity per §11, §16.2 and §24: stays a bounded recent-activity view (Q2) with exact scope copy, search-before-cap where translatable.
  - Restore the Inventory total and truncation note, keep content during load, write URL state back and re-read it.
  - Customer activity: debounce, cancel, latest wins, content kept.
  - Truncation disclosure with a `HasMore` probe on inbox, instances and definitions (Q1). No Load more.
  - A stable server order for Connections and Roles (N20).
- **Non-goals.** No keyset conversion of inbox, instances or definitions in RC1 (Q1). No change to Runs or Audit. No Components list redesign.
- **Repository areas.** `SEC/InventoryQueryService.cs`, `SEC/NotificationDefinitionComponentCatalogProvider.cs`, `SEC/ComponentCatalogService.cs`, `MC/InventoryDtos.cs`, `UI/Pages/Config/Inventory.razor`, `UI/Pages/Demo/DemoCustomerActivity.razor`, `UI/Pages/Me/PersonalNotifications.razor`, `UI/Pages/Config/Notifications`.
- **Invariants.** §11, §16.
- **Tests.** The Inventory SQL and HTTP rows, the cap-boundary rows, the **Components search row** and the customer-activity component rows of §27.
- **Handback evidence.** Before and after of the Inventory Activity false empty with a seeded match outside the cap. **Before and after of a Components search for a definition beyond the first 200.** A list of every cap now disclosed.
- **Recommended model.** Sonnet, high.

### Branches

`rc1-foundation` stays documentation and program contract only. LST-1.1 and LST-1.6 each go on a dedicated branch. LST-1.2 goes on its own dedicated branch (a migration, an authorization-sensitive query, and a full CI checkpoint). LST-1.3 to 1.5 go on later dedicated branches, after the IAM packages they depend on have merged. Creating a branch needs the owner's confirmation each time.

## 29. Risks

| Risk | Mitigation |
|---|---|
| The fill loop is slow for a principal who can read few rows of a large table | The scan budget bounds one request. The memo makes cost proportional to distinct definitions, not rows. The query plan is a handback item. A later move to an authorized-definition prefilter fits the single authorized-contexts contract IAM-10.0 must rule on (program gate §6.3 question 6) |
| A continuable empty page confuses operators | Bounded auto-continue, then a plain "Still searching" view with Continue. No counts are disclosed |
| Cursor disclosure of an unreadable row | Protected cursors (§17.5). A test proves a tampered cursor is rejected |
| Index build on a large `OperationRuns` takes locks | LST-1.2 reports build time and online capability for the target SQL Server edition. OPS-1 owns the operational procedure |
| A row moves in or out of a status-filtered view between pages | Accepted and disclosed. Keys are immutable. The client de-duplicates. A refresh reconciles |
| Back from a detail page restores the query but not the position | Accepted for RC1 (§13.3). Restoring position is LST-2 |
| Changing the `GET /api/runs` response shape breaks a caller | The five in-repo callers are listed (§17.8). The CLI is updated in the same package. No external consumer is known. Pre-release |
| Replacing the duplicated debounce breaks the source-string tests that pin it | They are updated in LST-1.1 and the change is a handback item |
| bUnit is adopted but not yet in the repository, and its .NET 10 compatibility is unconfirmed | §31 requires the check before the dependency is added. If it fails, stop and surface it. Gated Playwright is the slower, sparse fallback for keyboard proof, and a second component-test stack is not introduced |
| An in-flow combobox popup clips inside a dialog | LST-1.1 proves it inside `ManagementDialog`. LST-1.4 depends on that result |
| The helper grows into a framework | The non-goals in §7, the two-class limit in §10.2, and the rule that nothing is added without a second real consumer |
| A client filter later sits over a list that gains a cap | §21.2 requires a truncation signal at the same time. §12 item 4 names it a correctness bug |
| `AsyncSearchSelect` search load on the user directory | Bounded take, 300 ms debounce, cancellation, `user.read` or the narrow endpoint. IAM-9.3 (IAM-9 §25) requires a query plan note for the search query |
| Fleet signals could be left under-reporting if LST-1.2a slips | It is RC1 scope with a hard position before RCV-1 (§28). The risk is a missed schedule, not a missing design. A budget-exhaustion rule that never reads as clean is part of the package |
| Splitting LST-1.2a delays the fix for signal undercount behind LST-1.2 | Accepted. LST-1.2 is the most authorization-sensitive package, and its fill component is what 1.2a reuses. Doing both at once would widen the riskiest review in the epic |
| Authorization-hidden Inventory sources are dropped silently (`TryCollectAsync`) | Treated as not-truncation. The scope disclosure names the window and caps, not the hidden sources. Revisit if an owner wants a "some sources hidden" note |

## 30. Product-owner questions and answers

All nine were answered in the product-owner review of 2026-10-03. There are no open questions.

| # | Question | Answer (2026-10-03) |
|---|---|---|
| Q1 | Notification inbox and admin instances: truncation notice only, or also a Load more? | **Accepted default.** Honest truncation disclosure for the inbox and the instance lists (the definitions list follows the same rule). No keyset or Load more solely for RC1. Keyset may be reconsidered post-RC1 if the cap proves operationally limiting. Any partial search or filter remains server-authoritative over the scope it claims (§16.2) |
| Q2 | Inventory Activity: is a stated window acceptable? | **Accepted default.** Inventory Activity may remain a clearly bounded recent-activity view. Its UI describes the actual scope precisely. If a query searched only a time window, the newest N runs or the newest N audit entries, its empty and scoped message must not imply that no matching historical activity exists outside that scope. Runs and Audit remain the authoritative history surfaces (§16.2) |
| Q3 | Users keeps Previous and Next in RC1? | **Accepted.** Users keeps its existing paging convention in RC1. IAM-9 does not wait for LST-1 primitives to replace it (§15.3) |
| Q4 | Back from detail on keyset lists? | **Accepted.** Returning from a detail page restores the durable list query from the URL. It does not restore accumulated keyset pages or exact scroll position. That richer restoration remains LST-2 (§13.3) |
| Q5 | Team-editor user source? | **Narrow endpoint chosen.** `operational_team.manage`, `UserReferenceDto` only, bounded server search, Recovery Access excluded, status-aware once IAM-11 exists, no email, UPN, directory snapshot or unrelated profile data. `GET /api/users` authorization is not widened. IAM-10 treats candidate discovery as part of the `operational_team.manage` capability semantics (§20.2) |
| Q6 | Runs page size? | **Accepted.** Default 50, maximum 100 (§9.2, §17.4) |
| Q7 | Continuable empty pages? | **Accepted.** The client may continue automatically at most three consecutive empty-but-continuable pages. Then it stops automatic requests, presents the neutral "Still searching" state, and offers an explicit Continue. No unauthorized row count or density is disclosed (§17.6) |
| Q8 | Runs "Needs attention"? | **Accepted.** A server-authoritative Runs filter with the same semantic definition as the existing UI attention ranking. Two drifting definitions are avoided: one shared definition where architecturally appropriate (it is, §17.3), with an equivalence test either way |
| Q9 | bUnit? | **Accepted.** Adopt bUnit for interactive component tests, after confirming compatibility with the repository's .NET 10 target. Whichever of LST-1.1 and IAM-9.4 lands first may add the dependency. No competing component-test infrastructure (§31) |

### 30.1 Amendments recorded by the review

| Amendment | Where applied |
|---|---|
| 1. `PageFetch` carries an explicit `Completeness`. Capped DTOs are not `OffsetPage<T>`. They adapt with no cursor and `Truncated`. No capabilities record | §8 R1 and R2, §9.1, §9.3, §9.4, §27 |
| 2. The `MoreAvailable` contradiction is removed. `MoreAvailable` with zero items never renders `EmptyState` | §11.1, §11.2 |
| 3. LST-1 supplies cancellation only to reads and queries and makes no ruling on mutation cancellation. ACT-1 owns that contract | §8 R15, §10.5 |
| 4. Components partial search (N12) is a required LST-1.6 correctness fix | §11.3, §24, §28 LST-1.6, §25, §27 |
| 5. Fleet signal undercount (N23) has a defined RC1 home: **LST-1.2a**, after LST-1.2, before RCV-1 | §17.8, §24, §28, §25, §27, §29 |
| Audit boundary approved unchanged. LST-1 defines the invariant. AUD-1 owns the `(At, Id)` cursor, malformed-token behaviour, audit SQL tests and Audit page adoption | §18 |
| Runs design approved as specified. No other change | §17 |

## 31. Prerequisites for LST-1.1

1. This gate approved. **Done** (2026-10-03, with the amendments in §30.1). Answers to Q1 to Q9 are recorded in §30.
2. bUnit (Q9): confirm it supports the repository's .NET 10 target, then add the dependency in whichever of LST-1.1 and IAM-9.4 lands first. If a compatibility problem is found, stop and surface it. Do not substitute a second component-test stack.
3. A dedicated branch from `rc1-foundation`, created with the owner's confirmation. **Not created.** The owner has said not to create an LST-1 implementation branch yet.
4. Confirmation that the package builds with the repository-local `artifacts/` path and not a temporary scratch directory, per the validation build hygiene already in use, and that Aspire does not hold a lock on the output.
5. Confirmation that the NAV-2 work in flight does not touch `UI/Molecules/LoadingState.razor`, `ErrorState.razor`, `ForbiddenState.razor`, `EmptyState.razor` or `PageMessage.razor`. LST-1.1 composes them and does not change them.
6. LST-1.1 does not add or rename any `.razor` file with a name the UIX-6 guard forbids (N17).
7. No internal planning label (epic id, package name, migration name) in any runtime-visible copy, per the anti-patterns list. The copy in §15 and §16 is the product wording.
