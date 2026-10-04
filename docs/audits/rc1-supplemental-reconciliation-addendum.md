# RC1 Supplemental Reconciliation Addendum

| | |
|---|---|
| Date | 2026-10-03 |
| Baseline | `master` at `5cfa901`. Part 1 is [`rc1-architecture-audit-and-program-plan.md`](rc1-architecture-audit-and-program-plan.md). |
| Status | Working planning record. Not an authoritative roadmap or implementation contract. **Rulings were made on 2026-10-03; the contract is [`rc1-program-gate.md`](../01-architecture/rc1-program-gate.md), which governs where this record differs.** In particular: the business-key wording is replaced by the gate's D-24; "Show All" has no universal meaning (D-20); the statement about SQL Server full-text availability is unverified outside knowledge and is not an architectural fact; Runs paging must filter authorization before the page boundary (gate section 6.1). |
| Input reconciled | The post-IAM-8 product and architecture reorientation handoff, which was omitted from Part 1's input |

**What this is.** Part 1 remains a proposed program and a body of repository evidence. This addendum reconciles the omitted handoff against it. Sections A to T of Part 1 are not rewritten. Where this addendum and Part 1 differ, this addendum governs.

**Status correction to Part 1.** Part 1 section R is titled "Decisions this audit resolves". Read every row there as a **decision recommendation awaiting product-owner approval**. Nothing in Part 1 or here is a Decision until you approve it. Part 1 section T's recommendation to begin RC-0 was paused for this work. Section L below replaces it.

**Evidence standard.** Six focused read-only investigations, each followed by an independent verifier that tried to refute its key claims against source. Of 85 key claims, 65 were confirmed and 20 were partly wrong. The corrections are folded in below. No claim was found wrong outright. Nothing was built or run. One fact rests on outside knowledge, not the repository, and is marked where it appears.

---

## A. Coverage Gap Assessment

| Handoff theme | Part 1 coverage | Result of this reconciliation |
|---|---|---|
| List and query experience | Partially investigated (E.7, LST-1) | Part 1's recommendation was right in direction and too narrow. LST-1 splits into an RC1 layer and a post-RC1 layer. |
| Async action feedback | Partially investigated (E.2, ACT-1) | ACT-1 expands. Two live defects found. Broad optimistic mutation is not justified. |
| Platform-wide search | Not investigated | Post-RC1. The candidate central projection does not fit the current architecture as a first step. |
| Protected exact lookup | Not investigated | Post-RC1 at the earliest. None of its building blocks exist. One existing leak path found. |
| Shared platform calendar | Not investigated | Horizon. No forcing use case exists in the repository. |
| Cross-context grouping | Investigated as a roadmap item only | Deferral retained. No forcing workflow. |
| Context-scoped IAM | Investigated (E.5, IAM-10) | Still post-RC1. Part 1 understated the design problem; the IAM-10 design gate grows. |
| Status model | Materially changed by the handoff | Part 1's "decisions" are relabelled as recommendations. Section D gives the status of each omitted theme. |

---

## B. Repository Findings (new evidence only)

Path shorthand as in Part 1: `UI/`, `Shared/`, `Api/`, `Sec/`, `Pers/`.

### B.1 Lists and query

**What exists as shared presentation.**

- Styling only: `.ui-data-table` CSS. There is no table, column or header component. 49 hand-written tables in 39 files.
- State components are already shared and widely used: `LoadingState` (69 files), `ErrorState` (61), `ForbiddenState` (48), `EmptyState` (43). The branch chain that chooses between them is hand-written on every list page.
- `SegmentedControl<TValue>` is the only shared filter widget (5 uses).
- There is **no sortable column header anywhere**, no `<Virtualize>`, and no list that filters loaded rows instantly. Sort exists only as a dropdown on Inventory and the Integrations fleet.

**What is repeated by hand.**

| Pattern | Instances |
|---|---|
| Loading, error, forbidden, empty branch chain | About 20 list pages |
| "Load more" control | 5, over four different paging mechanisms |
| Debounced search | 2 near-identical copies; the timer is cancelled but the HTTP request never is |
| Stale-response guard | 3 competing conventions: `NavigationLoadGuard` with a cancellation source (2 pages), a `_requestSequence` counter (4 files), a context-string compare (4 files) |
| Clickable row plus trailing link | 10 files |
| Toolbar Refresh button | 24 files |

**Two live defects that show a false "no results".**

1. **Inventory, Activity mode.** Runs and audit rows are fetched with a cap of 100 per source, and the search text is applied in memory afterwards (`Sec/InventoryQueryService.cs:18,73,271-297,354-360`). A match older than the newest 100 returns "Nothing to show".
2. **Customer activity search.** It fires a request on every keystroke with no debounce, cancellation or sequence guard (`UI/Pages/Demo/DemoCustomerActivity.razor:78-82,208-213,290-300`). A stale empty response can overwrite a newer result.

**Silent truncation.** Several history lists are capped with no "more" control and no notice: the personal notification inbox (100), admin notification instances (50), the integration Runs tab (200), Inventory (100; the client DTO drops the server's total). Four other pages already show "n of total", so the honest pattern exists.

**URL state.** Only Runs writes filter state to the URL. Inventory, Audit, Components and notification instances read query parameters on arrival and never write them back, so the URL goes stale. Every other list keeps filter, sort and page state in component fields and loses it on refresh and on a round trip to a detail page. No list writes filter state with history replacement; Runs pushes a history entry per status change.

**Paging mechanics per surface.** Runs applies its row cap before authorization filtering (`Sec/RunQueryService.cs:79-96`), so a capped page can come back short and an exact total would be costly. The audit timeline is keyset-paged, but its cursor is defective: rows are ordered by time then id, while the cursor filters on id alone (`Pers/SqlAuditService.cs:53-54,148-151`). If time and id order ever diverge, "Load more" can skip or repeat rows.

**Views versus filters.** Only three controls switch to a genuinely different dataset: Inventory Resources versus Activity, the Users sub-pages, and the integration hub tabs. Everything else is a filter over one dataset: Application Resources type, Applications active or archived, Notifications all or unread, Runs status, Components kind, the "Needs attention" toggles. Connections has no tabs; two code comments that mention a `SetTab` are stale.

**"Show all" today.** The three existing "Show all" controls clear narrowing filters. None loads past a page cap.

### B.2 Async action feedback

- **`ActionButton` state is caller-driven.** A click only invokes the handler. The page must call `SetStateAsync` through a component reference before and after its await. Every adopter repeats that block.
- **A compact pending state half-exists.** The default pending icon is already a spinner. All 19 non-navigation usages override the pending text, so nothing relies on the default "Working…".
- **Accessibility gaps.** The button sets no `aria-busy`. The spinner is the only animation in the client and has no reduced-motion handling.
- **Cancel-on-hover is unreachable.** The button is disabled in the pending state, and a disabled button does not receive clicks, so the cancel branch cannot fire. No caller wires it.
- **Integration Activate, Deactivate and Retire have no pending state and no double-click guard**, on the detail page and on list rows (`UI/Pages/Integrations/IntegrationDetail.razor:815-867`, `IntegrationsList.razor:554-580`). The buttons are disabled only by a flag that is set later, during the reload. Retire also has no confirmation.
- **Reload after an action blanks the page.** Integration, Schedule and the notification inbox swap the whole body for a loading panel on every post-action reload. Module detail and the Components list already keep stale content visible, so the better pattern exists.
- **Busy handling is fragmented.** 13 pages use `ActionButton`; about 44 files use hand-written busy flags.
- **Optimistic updates.** One exists (theme), with no rollback. Notification preference checkboxes are effectively optimistic with a re-read on failure. Notification read and unread is server-first with a page-wide lock and a two-or-three-request reload that blanks the list.
- **Cancellation.** All 239 client methods accept a cancellation token. Three of 83 calling files pass one.
- **Documentation conflict.** `docs/01-architecture/management-ui-design-system.md` lines 342 and 422 say not to replace the Run, Schedule and Integration toolbar commands with `ActionButton` "in this phase". Part 1's ACT-1 does exactly that, so ACT-1's gate must supersede those lines explicitly.

### B.3 Platform-wide search

- **Every search-like feature today is an authorized per-family read followed by an in-memory substring filter.** Inventory, Application Resources and Components all follow this federated pattern and omit families the caller cannot read.
- **No full-text search exists.** There is one SQL `LIKE` (managed contacts) and one SQL-translated substring search (operation items within one run group).
- **There is no change feed to maintain a projection.**
  - `PlatformEvents` is a signal channel for four platform event types plus module-declared signals. Emission is best-effort and option-gated. No event exists for configuration changes.
  - The notification outbox is delivery-specific.
  - There are no `SaveChanges` interceptors, and many writes bypass repositories.
  - Hard deletes (integration, schedule, role, notification definition) are invisible to watermark polling.
  - `AGENTS.md` rule 1 forbids designing around a message bus, which rules out a publish-and-subscribe indexer.
- **SQL Server full-text is not available where this repository runs SQL.** Aspire, compose and CI all use the stock `mcr.microsoft.com/mssql/server:2022-latest` image, and tests fall back to LocalDB. *Outside knowledge, not verified in the repository:* the stock Linux image does not include the full-text component, and LocalDB does not support full-text search. Creating a full-text index also cannot run inside a transaction, no migration here has ever suppressed the transaction, and migrations run at host start, so a missing component would stop the host.
- **Module-owned data is out of reach.** The control plane does not read module tables. One proof module writes to a database chosen at run time through an integration connection.
- **No record model exists to index.** Business data is operation-item history with a shared identity `(RecordType, BusinessKey)`. There is no record-type registry and no field declaration. This is the same missing "record anchor" Part 1 assigned to POL-1.
- **Locators.** Components use `(Kind, NativeId)`. Records use `SharedRecordIdentity`. Inventory builds href strings on the server, duplicating the UI's route helpers. An audit row can be fetched by id through the API, but the UI has no per-row route.
- **Run authorization can be pre-filtered.** It depends only on the run's definition and kind, so a caller's set of authorized definitions can be computed once. This corrects the investigator's first reading.
- **Retention.** No retention or purge code exists anywhere.

### B.4 Protected exact lookup

- **No keyed hash exists.** HMAC appears only inside password hashing and TOTP.
- **No instance key, no versioned key, no rotation.** The key-id columns beside ciphertext always hold a constant.
- **The only working secret protector is machine-scoped DPAPI.** A key written on one machine cannot be read on another. Secret resolution is not audited.
- **No per-actor query throttle exists.**
- **No data classification exists.** "Sensitive" applies to settings and connection fields only.
- **An existing leak path.** `SubjectKey`, `DisplayLabel` and `SharedBusinessKey` on operation items are cleartext and indexed, and `SubjectKey` and `DisplayLabel` are substring-searched (`Pers/SqlOperationItemStore.cs:240-243`, `AppertureDbContext.cs:1148-1151`). Shared-record items force `SubjectKey` to equal the business key. A module that uses a national identifier as its business key puts it in ordinary searchable text today.
- **A module permission can be declared today** at `module:{id}`, but it carries no name, sensitivity or step-up flag, and step-up is control-plane only.
- **Precedent for search-by versus view.** `ConnectionToolAccess` requires both `connection.read` and `connection.browse`, refuses with one uniform message, and drops the result if the success audit cannot be written. Its limits: denied attempts are not audited, and a missing connection is distinguishable from a refusal.
- **Boundary gap.** Two real modules reference connector projects that reference the secrets assembly, and the architecture test's prohibited list does not name that assembly. Whether transitive references are disabled centrally was not checked.

### B.5 Calendar and temporal primitives

- **Scheduling generates instants from cron expressions.** It has no interval type, no overlap query, no all-day or local-date notion, and no blackout, holiday or exclusion concept.
- **No date-range or period value type exists.** `DateOnly` is used once, in a sample module's row validation.
- **Time is stored as UTC instants; timezones are display-only.**
- **The word "calendar" already has three deferred meanings in the documentation:** calendar or date-period schedule authoring with a schedule designer; a module "calendar / event signal SDK"; and a UI temporal-filter convention. None is a shared store of business events.
- **No module has date-window logic.** The Unisa payment-plan module's settings are empty and its daily processing is a scaffold. The only academic-period vocabulary is two connector DTOs with no dates that nothing references.
- **No cross-integration read exists in the SDK.** Step-time reads are limited to the current integration's settings and connections.
- **A new context-owned resource family is expensive.** It must be added to the move service, archive rules, guards, inventory, the Application resource explorer and about ten further files.
- **An unused hook exists.** `ScheduleDefinition.ExecutionPolicyRef` is stored and round-tripped, the UI always sends null, and nothing consumes it.

### B.6 Grouping and context-scoped authorization

- **Nothing in the code behaves like an organizational grouping.** The All view is a filter. Shared is an ownership bucket. Operational Teams group people for delivery and are in no authorization path.
- **What a guest can be confined to today.** A custom role can limit a guest by capability and by module. It cannot hide one Application from a guest of another: integration, connection, schedule, audit and business-data reads all authorize at instance scope, and the list of Applications needs only authentication. A guest holding only module-scoped grants is refused the integration and connection lists entirely, so the choice today is every Application or none.
- **`context:{id}` is not a drop-in scope form.** The scope key is one hierarchy with prefix containment. A grant at `context:X` would fail every module-targeted check (ten authorization services target `module:{id}`) and every instance-targeted list. Context is orthogonal to module.
- **Governed identities carry no context.** `IGovernedResourceIdentity` has no `ResourceContextId`, and schedules have no identity at all.
- **Tests and rulings pin the current position.** `ResourceContextAuthorizationNonChangeTests.NoContextScopeKeyFormExists` fails if any member of `ScopeKeyConstants` contains "Context". Three rulings say the same (CTX-3 gate section 9.3, IAM-8 ruling R6, ADR-0044).
- **A precedent for an orthogonal scope exists.** `environment:{id}` is already a second top-level hierarchy.
- **The later schema change is additive** unless one user may hold the same role at two scopes, in which case the `(UserId, RoleId)` key must be rebuilt.

---

## C. Reconciliation With Existing Recommendations

### C.1 List and query architecture

**Was "three small primitives, not a list framework" correct?** Yes in direction, and too narrow in one respect. Part 1 treated the problem as response shapes and a selector. The repeated pain is the query lifecycle and the paging footer, and two of the gaps are user-visible defects.

**Revised position.** LST-1 becomes two layers.

| Layer | Contents | Why |
|---|---|---|
| RC1 (LST-1) | Paging contracts; `AsyncSearchSelect`; URL convention; one non-visual query-lifecycle helper; a list state view; a pager footer; the two false-empty fixes; truncation notices | Each has repeated hand-written instances or fixes a defect. Applied only to lists RC1 already touches. |
| Post-RC1 (LST-2) | Column definitions; sortable headers; quick and advanced filter controls; default row action; instant local pre-filter; virtualization; continuous loading | Zero existing instances of sortable headers or virtualization. Building them now would be invention, not extraction. |

**Answers to the handoff's questions.**

1. **Can shared presentation coexist with query-specific data strategies?** Yes. The five existing "Load more" controls already look the same over four paging mechanisms. A pager footer needs only loaded count, optional total, has-more, loading-more and a callback. It must never see a skip value or a cursor. One coupling stays explicit: a sortable header must know whether sort is server-authoritative, because client sort over a partial load is wrong.
2. **One paging contract or two?** Two. Part 1 proposed a single `PagedResult<T>`. Evidence supports an offset shape with a total and a keyset shape with has-more and no total.
3. **Strategy per surface.**

| Surface | Strategy |
|---|---|
| Runs, notification inbox, notification instances, platform events | Keyset, no exact total, load more |
| Audit | Keyset (keep), with the cursor corrected |
| Users, operation items, business-data demo lists | Offset with total (keep) |
| Connections, Schedules, Integrations fleet, Roles, Applications, Modules, Catalog, Application Resources | Full load. Client sort is legitimate because the set is complete. |
| Inventory Resources | Full load with a truncation notice |
| Inventory Activity | Search before the cap, or label it "recent activity" |
| Virtualization, continuous scrolling | No surface warrants it for RC1 |

4. **What should "Show All" mean?** Clear the narrowing filters, which is what the three existing controls do. It never means "remove the row limit". For capped lists the honest control is "showing n of total, refine to see more".
5. **Search over partially loaded data.** The handoff's Direction has two parts. The invariant (never show "no results" while a query is pending or while unloaded rows are unsearched) is an RC1 requirement, because it is violated today. The instant local pre-filter has no existing instance and moves to LST-2. Until then, list search is debounced, server-authoritative and latest-wins.
6. **Views, quick filters, advanced filters.** The distinction holds against the code. No current tab needs converting to a filter; the one segmented control that is a true view (Inventory mode) is already separate. The filter-control conventions belong to LST-2.
7. **URL persistence.** Adopt one rule in LST-1: filter and search state is read from and written to the query string; filter tweaks and typing replace the history entry; switching a view pushes one. Runs currently pushes per status change and should follow the rule.

### C.2 Async action feedback

- **`ActionButton` should own a compact default pending state.** Make the pending text optional: when none is given, keep the label, show the spinner and set `aria-busy`. This is non-breaking, because every current caller supplies its own text.
- **`ActionButton` should be able to drive itself** from an awaited handler, which removes the hand-written state calls and the re-entrancy window. The reference-driven path stays for cases where two buttons move together.
- **Custom intermediate text remains right** where the verb or outcome carries information: Test Connection, invitation send, procedure launch, confirm-dialog labels.
- **Server-confirmed with compact pending** is the right mode for integration, schedule, module and run lifecycle actions.
- **Optimistic mutation has one justified case:** notification read and unread. It is per-user, idempotent and trivially reversible, and today's flow locks the page and blanks the list. Preference toggles are already effectively optimistic and only need formalizing. Everything behind a confirmation, anything returning authoritative state, and all lifecycle actions must stay server-confirmed.
- **One rule is shared with list UX:** a reload keeps the current content visible and marks it busy. It does not swap to a loading panel.
- **What is not shared:** cancellation and debounce are query concerns and belong to LST-1. Mutations are never cancelled on navigation, because the server may already have applied them.
- **Animation assets.** Settle the component contract first. The spiral and ripple assets are a later choice that needs a licence check. The contract must include reduced-motion behaviour.

### C.3 IAM principal and scope model

Part 1 said IAM-10.0 should "reserve" the `context:{id}` and `integration:{id}` scope forms. That was too light in two ways.

1. **No constant can be reserved in code.** A pinned test forbids it. The reservation lives in the design document only.
2. **The design problem is scope composition, not a new string.** IAM-10.0 must rule on six points before RC1 so later features do not need a second security system:
   - **Scope algebra:** how context composes with module under a prefix-only evaluator (a composite target, or two dimensions evaluated together), with instance grants remaining universal.
   - **Target-scope derivation:** whether governed identities gain a context dimension, and whether history rows use current ownership or their snapshot. Name the tests and rulings that would be deliberately superseded.
   - **Assignment shape:** scope on the assignment or on the role grant; the `UserRoles` key consequence; the role-provider return shape.
   - **Principal kinds:** User, ServiceAccount, Integration, Agent; the fate of the dormant actor kinds and the unpersisted `ServiceAccount` type.
   - **`integration:{id}` semantics:** a containment scope, or a principal and owner identity. Notes authority is a data-ownership check plus a distinct shared-data permission, not containment.
   - **One list-filtering contract:** a single "authorized contexts for this principal and permission" query that lists, search, dashboards and calendar would all call. It also covers directory and count endpoints.

Implementation stays post-RC1. Nothing in RC1 forecloses it.

### C.4 Audit

- AUD-1.0 should reserve a **data-access** category and state one rule: a searched value, and any digest of it, never appears in audit details.
- AUD-1 should correct the audit keyset cursor. It is the reference keyset implementation and it is subtly wrong.
- A per-row audit route in the UI is a small addition that makes audit entries linkable. The API already supports it.
- Protected lookups and calendar mutations create no other RC1 audit-contract change. The target, category and actor columns AUD-1 already adds are sufficient.

### C.5 Navigation provenance and typed locators

Three separate lines of work now want the same thing: a typed identity that resolves to a route in the UI. Navigation provenance (NAV-3), metric drill-through (Part 1 section Q) and search results all need it. Components already model it as `(Kind, NativeId)` and records as `SharedRecordIdentity`. The recommendation is one typed locator convention owned by the navigation registry, with routes resolved client-side. No server-built href strings should be added in RC1.

### C.6 Procedure and operation model

Unchanged. Nothing in the omitted themes needs a new launch path. Re-indexing a protected lookup after key rotation would be an ordinary integration procedure.

### C.7 Module contracts

- No search or calendar seam needs building in SDK-8.
- Three things should not be closed off: keep `ModuleDescriptor.Contributions` additive; do not treat free-text scanning of `MetadataJson` as a search contract; do not add direct database reads of business data in the control plane.
- SDK-8.5 guidance should state that business keys, subject keys and display labels are cleartext and searchable, so sensitive identifiers must not be used for them.
- SDK-8.3 should add the secrets assembly to the module architecture test.

---

## D. Status Register

| Theme | Status coming in | Status after this work | Notes |
|---|---|---|---|
| Shared composable list primitives, query-specific data strategies | Direction | **Direction, confirmed by evidence.** Recommendation awaiting approval: split into LST-1 and LST-2. | Presentation and data strategy separate cleanly. |
| "Show All" means chunked or continuous loading | Direction (unsettled) | **Recommendation awaiting approval:** it means clear filters; never unlimited rows. | Continuous loading is deferred. |
| Instant local filter, then authoritative server search | Direction | **Split.** The no-false-empty invariant: recommendation for RC1. The instant local pre-filter: Deferred to LST-2. | |
| Sorting on column headers | Direction | **Deferred to LST-2.** | No existing instance. |
| Default row action | Direction | **Deferred to LST-2,** coordinated with ACT-1. | |
| Views, quick filters, advanced filters | Direction | **Direction, confirmed.** Conventions deferred to LST-2. | No tab needs converting now. |
| Server-confirmed pending as the default for lifecycle actions | Direction | **Recommendation awaiting approval** (ACT-1). | |
| `ActionButton` compact default pending | Direction | **Recommendation awaiting approval** (ACT-1). | |
| Optimistic notification read and unread | Direction | **Recommendation awaiting approval,** optional for RC1. | The only justified optimistic case. |
| Broad optimistic mutation | Candidate | **Rejected.** | No concrete benefit elsewhere. |
| Spiral and ripple animation assets | Direction | **Deferred.** Contract first, licence check later. | |
| Platform-wide search | Candidate | **Candidate, post-RC1.** | |
| Central search projection as the first architecture | Candidate | **Not recommended as the first step.** Recommendation: federated providers first; a projection only for business records, after POL-1. | Awaiting approval as a Direction. |
| SQL Server full-text as first backend | Direction | **Not recommended now.** | Not available in any SQL instance the repository defines. |
| `ISearchIndex` provider boundary | Candidate | **Rejected for now.** | No second implementation pressure. |
| Integrations declare what is searchable | Direction | **Direction, confirmed.** No build. | |
| Protected exact lookup by keyed hash | Candidate | **Candidate, post-RC1,** as a facility separate from search. | Needs a real customer case and a security gate. |
| List search and global search stay separate backends | Direction | **Direction, confirmed by evidence.** | Section H. |
| Shared platform calendar | Candidate | **Deferred (horizon).** | No forcing use case. |
| Cross-context grouping | Deferred | **Deferred, retained.** | No forcing workflow. |
| Context-scoped IAM | Deferred | **Deferred, retained.** Design content of IAM-10.0 expanded. | Guest confinement is the only real trigger. |

---

## E. RC1 Classification Changes

Only new or changed rows. Classification key as in Part 1.

| ID | Requirement | Evidence | Class | Destination |
|---|---|---|---|---|
| S-01 | Query lifecycle helper, list state view, pager footer | B.1 repetition counts | R1 | LST-1.1 |
| S-02 | Fix false "no results" on Inventory Activity and customer activity search | B.1 | R1 | LST-1.6 |
| S-03 | Truncation notice on capped lists; restore the Inventory total | B.1 | R3 | LST-1.6 |
| S-04 | URL convention for list state, including replace versus push | B.1 | R1 | LST-1.0, 1.1 |
| S-05 | Correct the audit keyset cursor | `Pers/SqlAuditService.cs:53-54,148-151` | R1 | AUD-1.5 |
| S-06 | `ActionButton` compact pending, `aria-busy`, reduced motion, self-driven handler | B.2 | R1 | ACT-1.1 |
| S-07 | Pending state and double-click guard on integration lifecycle actions; confirm on Retire | B.2 | R1 | ACT-1.4 |
| S-08 | Reload keeps content visible | B.2 | R3 | ACT-1.0 rule, applied during adoption |
| S-09 | Optimistic notification read and unread | B.2 | R3 | ACT-1.6 (optional) |
| S-10 | Column definitions, sortable headers, filter controls, row action, virtualization | B.1 | R4, with an R2 note in LST-1.0 | LST-2 gate |
| S-11 | Platform-wide search | B.3 | R4, with R2 seams | SRCH-1 gate |
| S-12 | Protected exact lookup | B.4 | R4 or R5, with R2 notes | PLK-1 gate |
| S-13 | Guidance: no sensitive identifiers as business keys | B.4 | R3 | SDK-8.5 |
| S-14 | Secrets assembly in the module architecture test | B.4 | R3 | SDK-8.3 |
| S-15 | Shared platform calendar | B.5 | R5 | None; naming note in RC-0 |
| S-16 | Per-row audit route in the UI | B.3 | R3 | AUD-1.5 |
| S-17 | Typed locator convention | C.5 | R2 | NAV-2.0 |
| X-07 (changed) | Runs paging | Authorization is applied after the row cap | R1, now specifically keyset without an exact total | LST-1.2 |
| IAM-10.0 (changed) | Principal and scope design | C.3 | R2, design content expanded | IAM-10.0 |
| M-01 (changed) | External-organization access statement | B.6 | R2 | RC-0: say guests are confined by capability and module, not by Application |
| N-25 (unchanged) | Drill-through breadcrumbs | | R2 design, R4 build | NAV-3 |

Nothing moves into "must implement before RC1" from search, protected lookup or calendar.

---

## F. Epic Impact Matrix

| Epic | Impact | What changes |
|---|---|---|
| RC-0 | **Expanded** | See section L. |
| IAM-9 | Unchanged | |
| AUD-1 | **Expanded** (small) | Gate reserves a data-access category and the no-value-no-digest rule. Fix the keyset cursor. Optional per-row UI route. Timeline filter state follows the LST-1 URL convention. |
| IAM-10 | **Expanded** (design only) | IAM-10.0 covers the six points in C.3. Sensitivity and step-up flags must apply to module-declared permissions. No reserved scope constant in code. Implementation scope is unchanged. |
| IAM-11 | Unchanged | |
| QW-1 | Unchanged | The existing "Show all runs" control stays; it clears a filter. |
| NAV-2 | **Expanded** (small) | NAV-2.0 owns the typed locator convention. |
| ACT-1 | **Expanded** | `ActionButton` changes, pending and re-entrancy on integration actions, the non-blanking reload rule, explicit supersession of the design-system document's lines 342 and 422, removal or repair of cancel-on-hover, optional optimistic notification read. `EntityToolbar` renders `ActionButton`. |
| LST-1 | **Split and expanded; partly reordered** | RC1 layer as in C.1. Two paging contracts. Runs is keyset. Packages 1.0 to 1.2 and 1.6 no longer wait for IAM-9 and can start alongside NAV-2. Packages 1.3 to 1.5 keep their IAM-9 and IAM-11 dependencies. |
| SDK-8 | **Expanded** (small) | S-13 and S-14. Keep contributions additive. |
| ID-1 | Unchanged | |
| OPS-1 | Unchanged | The recommended search direction adds no component and no SQL feature. If full-text were ever chosen, the supported-topology statement would have to name the SQL edition and a custom image. |
| RCV-1 | **Expanded** (small) | Validate that no list shows an empty state while a query is pending or rows are unsearched. Reduced-motion check on the spinner. Review sample and proof modules for sensitive identifiers used as business keys. |

**Revised LST-1 packages.**

- **LST-1.0 Gate.** Two paging contracts; the URL convention; the minimal capability record (paging kind, has-total, server search, server sort keys, URL parameters); the team permission ruling; a note that LST-2 owns columns, headers and filters.
- **LST-1.1 Primitives.** The two paging contracts; a query-lifecycle helper (debounce, cancel superseded requests, latest-wins, never-empty-while-pending); a list state view; a pager footer; `AsyncSearchSelect` built on the helper. Converge the three stale-response conventions on one.
- **LST-1.2 Runs paging.** Keyset on creation time and id, no exact total, load more, indexes.
- **LST-1.3 to 1.5.** As in Part 1.
- **LST-1.6 Correctness.** The two false-empty fixes; truncation notices; restore the Inventory total.

---

## G. New Proposed Bodies

| Body | Kind | When | Trigger |
|---|---|---|---|
| **LST-2** List presentation layer | Post-RC1 design gate | After LST-1 and ACT-1 have shipped | The owner's Direction. Pilot on Runs, Inventory and Connections. |
| **SRCH-1** Platform search | Post-RC1 design gate | After POL-1 (record anchor) and AUD-1 | The owner's interest. Starts federated. |
| **PLK-1** Protected lookup | Post-RC1 security design gate | Only with a concrete customer case | Needs key management, a per-actor throttle and step-up for module permissions first. |
| **CAL-1** Platform calendar | Research spike | Only when a real consumer exists | For example, real due-date or term logic in a module, or a blackout requirement on schedules. |

No new RC1 epic is required. No pre-RC1 research spike is required. The full-text availability question does not need a spike, because full-text is not being chosen.

---

## H. Explicit Architectural Separations

### H.1 List and query infrastructure versus global search

**Recommendation: keep them separate.** The evidence supports the handoff's Direction.

| Share | Keep separate |
|---|---|
| The query-lifecycle convention: debounce, cancellation, latest-wins, no false empty | Each resource list's query API and filters |
| Loading, empty and error presentation | The search backend, whether federated providers or a projection |
| Search text and filter vocabulary in the URL | Paging mechanism (each list's own; search results are ranked and capped) |
| Typed locators and result navigation | Authorization: a list uses its family's read check; search resolves each hit through that family's read service |

A list answers "show me this resource type, filtered". Search answers "find anything matching these words". Forcing one backend would make every list depend on an index that cannot be kept fresh today.

### H.2 Runtime ScheduleDefinitions versus a platform calendar

A ScheduleDefinition is a recurrence rule that produces instants at which a workflow start is enqueued. A calendar entry would be an interval of business time. Scheduling has no interval, overlap or local-date concept and should not acquire one. If a calendar is ever built, schedules could read it (for example to skip a blackout), but the two would remain different resources. "Schedule" keeps its single meaning.

### H.3 ResourceContext ownership versus organizational grouping

ResourceContext answers who owns a resource and where it is listed. Nothing in the reviewed requirements needs a second axis. Workspaces are presentation. Teams group people. Search and calendar scope by the existing context filter. The grouping layer stays deferred, with no model and no product term.

### H.4 Permission system versus any policy layer

Unchanged from Part 1 and reinforced. There is one authorization system. A "policy" is a human-readable permission definition, and later a named permission set. Protected lookup, calendar authority and notes authority all become permissions plus, where needed, a data-ownership check. None needs a policy engine.

### H.5 Ordinary global search versus protected exact lookup

**Recommendation: separate facilities.** Ordinary search is substring or ranked matching over text that is safe to copy. Protected lookup is exact matching on a keyed digest, with its own permission, step-up, per-actor throttling and an audit that fails closed on every outcome. Mixing them would weaken both. They share only the result shape and navigation.

**Direct answers on protected lookup.**

1. **Is exact-match keyed lookup appropriate?** Yes, it is the right shape. Its limits must be accepted knowingly. Anyone who can read the index learns which rows share a value. An identifier space of about a billion values can be enumerated quickly by anyone holding the key, so a leaked key equals disclosure of every indexed value. An authorized caller can script lookups unless throttled.
2. **Separate search-by from view?** Yes. Search-by should also require view of the record type, with one uniform refusal.
3. **Special audit treatment?** Yes. Fail closed on success, denial and failure. Record actor, identifier type, scope, outcome, hit count and key version. Never the value or its digest.
4. **Rotation without breaking the index?** Only with a key version on every row and a period in which both keys are tried. Re-deriving digests needs the raw values, which only the owning integration can read. Rotation is therefore an integration operation the platform schedules.
5. **Inside global search or separate?** Separate.

---

## I. Changes to RC1 Target State

Part 1 section C stands. Three additions to what RC1 does:

- **Lists never show an empty state while a query is pending or while unloaded rows are unsearched,** and capped lists say they are capped.
- **Lifecycle actions show a pending state and cannot be double-submitted.** Reloads keep content visible.
- **List filter state on the lists RC1 touches survives refresh and Back.**

Additions to what RC1 is ready for but does not do: sortable column-based tables, platform search, protected lookup, a platform calendar, context-confined guests.

---

## J. Open Product-Owner Decisions (new or changed only)

| ID | Question | Default if unanswered |
|---|---|---|
| Q-16 | Do you accept splitting list work into LST-1 (RC1: lifecycle, state, pager, correctness) and LST-2 (post-RC1: columns, sortable headers, filter controls)? | Yes |
| Q-17 | Do you accept "Show All" meaning "clear filters", with capped lists showing "n of total"? | Yes |
| Q-18 | Should optimistic notification read and unread be in RC1 or after? | After, unless ACT-1 has room |
| Q-19 | For platform search, do you accept starting federated over existing authorized reads, with a projection only for business records once a record model exists? | Yes, as a Direction |
| Q-20 | Is there a real customer need for lookup by a protected identifier? If so, which identifier, which integration, and who may search? | Treat as no current need |
| Q-21 | Is there a concrete first use for a shared calendar, such as academic terms for a named integration? | Treat as none; keep on the horizon |
| Q-22 | The word "calendar" already names three deferred items. Do you want a distinct product term for the business-time calendar before anything is written about it? | Decide when CAL-1 is triggered |
| Q-11 (changed) | Guests can be confined by capability and module today, not by Application. A module-confined guest cannot see the integration or connection lists at all. Is that acceptable for the first external-organization use? | Acceptable for RC1; state it in the external-access ADR |
| Q-23 | Have all accumulated TODOs now been provided? The handoff expected more input before sequencing. I have treated the objectives list you sent earlier as that input. | Proceed on that basis |

---

## K. Revised Sequencing

The three-track structure holds. Two adjustments:

```
RC-0  (gate, expanded per section L)
  |
  +-- Track A (security)    IAM-9 --> AUD-1 --> IAM-10 --> IAM-11          unchanged
  |
  +-- Track B (experience)  QW-1 --> NAV-2 --> ACT-1 --> LST-1.3 to 1.5
  |                         LST-1.0 to 1.2 and 1.6 start alongside NAV-2   moved earlier
  |
  +-- Track C (platform)    SDK-8 --> ID-1 --> OPS-1                       unchanged
  |
RCV-1
  |
Post-RC1 gates: NAV-3, POL-1, LST-2, MUI-1, MOD-1, CLI-2, then SRCH-1 after POL-1.
PLK-1 and CAL-1 only on a trigger.
```

- **LST-1's primitives and correctness fixes move earlier.** They depend on nothing in Track A and fix live defects.
- **SRCH-1 is ordered after POL-1.** A search projection for business data built before a record model would become the record model by accident.

---

## L. RC-0 Go or No-Go

**Has enough investigation now been completed to begin RC-0 and final roadmap reconciliation?**

**Yes, on two conditions.**

1. You confirm that no further accumulated TODOs are outstanding (Q-23).
2. You settle the statuses: approve, amend or reject the recommendations in Part 1 section R and in section D here. RC-0 records Decisions. It must not manufacture them.

No focused investigation remains open. Every theme in the handoff has been investigated to the point where its RC1 classification is clear.

**What changes in RC-0 because of this addendum.**

- **Record statuses, not just outcomes.** The roadmap should carry Candidate, Direction, Decision and Deferred labels for the themes in section D.
- **External-access ADR wording.** State what guest confinement is and is not possible in RC1.
- **Calendar naming note.** Record the three existing meanings of "calendar" so a fourth is not introduced silently.
- **Add the post-RC1 bodies** LST-2, SRCH-1, PLK-1 and CAL-1 to the deferred list, each with its trigger.
- **Roadmap categories.** Use the handoff's categories: current and next; near-term core platform; UI and platform consistency; data, query and search; shared platform services; deferred architectural directions; pre-release and UAT; later product expansion.
- **Retain the grouping and context-scoped IAM deferrals explicitly,** with guest confinement named as the trigger for the latter.
- **Do not add a context scope constant anywhere** as part of documentation clean-up.

---

## M. Final Soundness Check

1. **Is the original RC1 program still architecturally sound?** Yes. Nothing in the omitted material invalidates the track structure, the epic set or the ordering of the security work.

2. **What changed.**
   - LST-1 is split, gains a lifecycle helper, a state view and a pager, fixes two defects, and uses keyset paging for Runs.
   - ACT-1 gains the `ActionButton` changes and closes a double-submit gap on integration actions.
   - IAM-10.0's design content grows from "reserve two scope forms" to six explicit rulings.
   - AUD-1 gains a category reservation and a cursor fix.
   - Part 1's "decisions" are recommendations until you approve them.
   - RC-0 is expanded and was correctly paused.

3. **What did not change.** The RC1 definition; IAM-9, IAM-11, QW-1, ID-1 and OPS-1; the tombstone, credential-recovery and escalation recommendations; the navigation registry and the Services ruling; the SDK preview position; the conclusion that no new policy engine, operation abstraction or list framework is needed.

4. **Deliberately post-RC1.** Column-based sortable tables and filter panels; platform search; protected lookup; the platform calendar; cross-context grouping; context-scoped authorization; optimistic mutation beyond notifications; animation assets.

5. **Seams RC1 must preserve.**
   - Presentation primitives never see paging internals.
   - Per-family authorized read services remain the only read path; no new direct database reads of business data.
   - `(Kind, NativeId)` and `SharedRecordIdentity` stay stable as locators; no new server-built hrefs.
   - Module contributions stay additive.
   - `BusinessKey`, `SubjectKey` and `DisplayLabel` are non-sensitive operational identifiers and labels; modules must not place secrets, protected or regulated identifiers, or sensitive personal data in them (gate decision D-24).
   - The IAM-10.0 design covers scope composition before anything is built on scopes.
   - A data-access audit category is reserved.
   - "Schedule" keeps one meaning.

6. **Recommended immediate next step.** Answer Q-23 and review the status register in section D together with Part 1 section R. Once statuses are settled, run RC-0 as amended in section L. QW-1 and the IAM-9.0 design gate remain the first work after RC-0, and LST-1.0 can now start alongside them.
