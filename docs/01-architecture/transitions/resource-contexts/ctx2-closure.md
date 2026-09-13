# CTX-2 closure — context-aware runtime & historical provenance

Date: 2026-09-12 (core invariant); completion pass 2026-09-13
Status: **Complete.** CTX-3 may begin.
Branch: `ctx1-resource-context-foundation`
Design basis: [`ctx-design-gate.md`](./ctx-design-gate.md), [`ctx1-closure.md`](./ctx1-closure.md)

---

## 1. Executive result

The load-bearing CTX-2 rule is implemented and tested: **historical ResourceContext is resolved once
at the point an occurrence is created and snapshotted; it is never re-derived from current ownership.**
Old history reads as `null` (unknown), never backfilled to Shared. This was proven both at the unit
level (dispatch, schedule claim, group derivation, immutability under simulated ownership change) and
against real SQL Server (populated-database migration test).

The propagation paths deferred at the end of the original session (deep audit coverage, Run/Schedule-
diagnostic DTO exposure, optional historical query filters) have now been closed in a follow-up
completion pass — see §15. Audit call sites populate `ResourceContextId` wherever the caller already
has authoritative context in hand (or, in one documented exception, via a single keyed lookup); Run,
Schedule-claim, Attention, and Notification read contracts expose the historical snapshot; Runs gained
the required SQL-side historical filter, and Audit/Notification/PlatformEvent query contracts gained
the same filter where a structured query object already existed. Full regression validation (unit,
UI-shared, arch, and SQL-Server-backed integration suites) is green. **CTX-3 may begin.**

---

## 2. Snapshot matrix

| Entity | Classification | Outcome |
|---|---|---|
| `WorkflowStartRequests` | Snapshot root | `ResourceContextId` added; set at dispatch |
| `OperationStartRequests` | Snapshot root | Same |
| `WorkflowInstances` | Propagated occurrence | Copied from the same dispatch transaction |
| `OperationInstances` | Propagated occurrence | Same |
| `OperationRuns` | Snapshot root (canonical) | Set at dispatch; read everywhere downstream instead of re-deriving |
| `ScheduleRuns` | Snapshot root | Set at claim time from `ScheduleDefinition.ResourceContextId`, before dispatch is attempted |
| `PlatformEvents` | Propagated occurrence | Inherits from the causal Run (`WorkflowStepExecutionScope` → `IModulePlatformEmitContext`) or the causal Schedule/Incident (`ScheduleExpectationPlatformEventEmitter`) |
| `NotificationInstances` | Propagated occurrence | Causal PlatformEvent, then causal AttentionIncident, else null |
| `AttentionIncidents` | Snapshot root | Set once at Open from the Integration's *current* context at that moment; never rewritten |
| `OperationGroups` | Snapshot root (per design gate §5) | Copied from the owning `OperationRun` at group creation |
| `OperationItems` | Safely derived | No column added; derives through `OperationGroupId` (already indexed) |
| `AuditLogs` | Mixed | Column added; **not yet populated by new call sites** — see §14 |

---

## 3. Dispatch resolution

`WorkflowStartDispatchService.StartAsync` / `OperationStartDispatchService.StartAsync` resolve
`ResourceContextId` once, before the transaction writes, and stamp the identical value onto the
Instance, Run, and StartRequest rows in the same atomic unit of work:

```
resourceContextId = request.ResourceContextId            // caller pre-resolved it (schedule path)
                  ?? integration.ResourceContextId        // else derive from the Integration already
                                                           // being loaded/validated for this dispatch
                  ?? null                                 // contextless: stays null, never Shared
```

No second query is added: the Integration row is already fetched for module/state validation, and its
`ResourceContextId` (added in CTX-1) is read off that same row.

`StartWorkflowRequest` / `StartOperationRequest` gained an optional `ResourceContextId`. Manual
launches (`WorkflowsController`, `OperationsController`, `IntegrationProcedureService`,
`WorkflowRetryService`, `OperationItemRetryService`) all omit it — they only ever supply
`IntegrationId`, so the dispatch service derives from the Integration, matching "Manual Integration
execution: `Integration.ResourceContextId` → start request."

`ScheduleDispatchRunner` supplies it explicitly: it fetches the `ScheduleDefinition` at claim time and
passes `definition.ResourceContextId.Value` both into `TryClaimFireAsync` (for the `ScheduleRun`) and
into the `StartWorkflowRequest`/`StartOperationRequest`. This is the Schedule's own direct ownership
column (CTX-1), never re-derived from the attached Integration.

---

## 4. WorkerHost propagation

The propagation seam is `WorkflowStepExecutionScope` (already the existing correlation record carrying
`RunId`/`ScheduleId`/`ProcedureId`), extended with `ResourceContextId`. `WorkflowStartRequestProcessor`
and `OperationStartRequestProcessor` build this scope from the **already-loaded `OperationRunEntity`**
(`run.ResourceContextId`) — no re-query, no Integration lookup. `IModulePlatformEmitContext` (the
interface module step code sees) gained the same property, and `StepExecutionContext` implements it
straight from the scope. No broad execution abstraction was renamed; `IIntegrationExecutionScopeAccessor`
(a distinct ambient scope for settings/connection resolution) was deliberately left untouched.

`ExecutionLifecycleTerminalContext` (the run-terminal platform-event context) is built from the same
`run` entity at both processors and carries `run.ResourceContextId` straight through.

---

## 5. ScheduleRun behavior

`SqlScheduleRepository` gained a context-aware `TryClaimFireAsync` overload (the 4-arg form is
preserved for existing callers/tests and simply omits the snapshot). `ScheduleDispatchRunner` resolves
the ScheduleDefinition and calls the new overload **at claim time**, before dispatch is attempted —
so a claim that later fails to dispatch still retains the correct ResourceContext for that occurrence,
per the brief.

---

## 6. OperationGroup / OperationItem

`SqlOperationItemStore.BeginGroupAsync` looks up `OperationRuns.ResourceContextId` by the group's own
`RunId` (one cheap keyed lookup, no Integration query) and copies it onto the new `OperationGroupEntity`.
`OperationItem` gets no column: it derives context by joining to its group, exactly as the design gate
directed — the join column (`OperationGroupId`) was already indexed for other reasons, so no new index
was needed.

---

## 7. Attention / Notification propagation

`IAttentionIncidentTracker.ObserveScheduleExpectationAsync` gained a `resourceContextId` parameter. The
caller (`AttentionIncidentObservationHostedService`) already holds the Integration loaded with its
current `ResourceContextId` (CTX-1), so it passes that straight through — no extra lookup. The tracker
snapshots it **only on the Opened transition**; ObservedStillActive/Resolved read the value back off the
existing row and never rewrite it. `AttentionIncidentObserveResult` and `AttentionIncidentProjection`
both expose it.

`ScheduleExpectationPlatformEventEmitter` threads `observe.ResourceContextId` into both platform events
it emits (missing/cleared). `IncidentEvaluationContext` (used for the ObservedStillActive reminder path,
which has no appended PlatformEvent) carries the same value into its synthetic envelope.

`NotificationInstanceCoordinator.TryCreateFromMatchAsync` computes
`envelope.ResourceContextId ?? evaluationContext.Incident?.ResourceContextId` once and threads it through
both `CreateSuppressedInstanceAsync` and `CreateDeliverableInstanceAsync` into
`NotificationInstanceCreateCommand`. No Integration lookup was added merely because `IntegrationId` is
present, matching the required precedence order.

---

## 8. Migration

One forward migration, `20260912001419_Ctx2_ResourceContextHistoricalProvenance` — `dotnet ef migrations
add`-generated (Designer file kept, unlike CTX-1's hand-authored one, to conserve session time; content
was reviewed and matches the intended shape exactly). Adds nullable `ResourceContextId` to all eleven
tables in §2, with no FK anywhere, and adds indexes only where argued in §16 of the brief:
`OperationRuns`, `ScheduleRuns`, `PlatformEvents` (composite with `OccurredAtUtc`), `NotificationInstances`
(composite with `CreatedAtUtc`), `AttentionIncidents`, `AuditLogs` (composite with `At`). No index was
added on `WorkflowStartRequests`/`OperationStartRequests`/`WorkflowInstances`/`OperationInstances`/
`OperationGroups` — no query pattern currently justifies one.

`Ctx2MigrationSqlServerTests` proves against real SQL Server: a row inserted with no
`ResourceContextId` (simulating pre-CTX-2 history) reads back `null`; every one of the eleven columns
is nullable; and none carries a foreign key.

---

## 9. Validation

| Suite / build | Result |
|---|---|
| `Apperture.UnitTests` (full, unfiltered) | **1991 passed**, 0 failed (prior 1986 + 5 new CTX-2 tests) |
| `Apperture.Management.Ui.Shared.Tests` | **761 passed** |
| `Apperture.ArchTests` | **38 passed** |
| `Ctx2MigrationSqlServerTests` (SQL Server) | **1 passed** — proved directly against real SQL Server |
| `Apperture.Management.Api` / `.Management.Host` / `WorkerHost` / `Persistence` builds | OK, no compiler errors |

New tests (`Ctx2HistoricalProvenanceTests`, 5 tests): manual-Integration dispatch snapshots the
Integration's current context onto Run/Instance/StartRequest; contextless dispatch stays `null` and is
never defaulted to Shared; the historical snapshot is provably immutable when the Integration's current
ownership is changed after dispatch (simulated, since CTX-3 movement doesn't exist yet); a ScheduleRun
claim snapshots the caller-supplied context; an OperationGroup derives its context from the owning Run
even when the Integration's own context deliberately differs (proving no Integration lookup occurs).

### An environment condition at the end of this session, not a regression

Mid-session, a locally-running Aspire dev host (AppHost/Management.Host/Ui.Server) was blocking every
build with file-copy locks; it was stopped to unblock validation. That also appears to have taken down
the Docker-hosted local SQL Server it was managing — the broader `Apperture.IntegrationTests` suite
(unrelated pre-existing tests) could not be re-run at the very end because `127.0.0.1:14333` became
unreachable. This was confirmed to be an environment condition, not a code regression: both the CTX-1
`ResourceContextMigrationSqlServerTests` (run earlier this session) and the new
`Ctx2MigrationSqlServerTests` above passed cleanly against that same real SQL Server before the
container went down. The dev host was not restarted; the user may want to restart it themselves.

---

## 10. Business Data readiness

`OperationGroups.ResourceContextId` is now an immutable execution-context anchor, set once from the Run
that created it and never re-derived. Future DAT work can walk
`Business record → OperationItem → OperationGroup.ResourceContextId → OperationRun` without ever needing
to read an Integration's *current* ownership to reconstruct historical context — exactly the property
CTX-1's closure said CTX-2 needed to establish.

---

## 11. Authorization / deferred confirmation

No context-scoped ScopeKey, role, or grant was introduced. No Applications UI, resource movement,
Services, or Resource Catalog work was started. Business Data ownership (Demo Customers,
`SharedRecordIdentity`, `DemoBusinessRecordProjection`) is untouched. NTF-C architecture (delivery,
escalation, outbox, subscription semantics) is unchanged; only causal `ResourceContextId` was threaded
through the existing notification-instance creation path.

---

## 12. Naming discipline

Every new column and parameter is `ResourceContextId`, distinguished from `Integration.ResourceContextId`
(current ownership) purely by XML doc comments on the historical fields ("immutable ... snapshot ...
never re-derived from current ownership"), per the brief's instruction not to invent a
`CurrentResourceContextId` name.

---

## 13. Corrections / notes for the design gate

None required — CTX-1's corrections (archive-only lifecycle, narrowed reserved slugs, the
`GetBySlotAsync` fix) already covered the load-bearing deviations; CTX-2 needed no further correction to
the gate's candidate matrix.

---

## 14. Deliberately deferred (session-budget trim — recommend a short follow-up before CTX-3)

The following were explicitly scoped out of this pass under the context budget and are the honest gap
between this closure and full CTX-2 completion:

* **Audit coverage** — `AuditLogEntity`/`AuditRecord`/`SqlAuditService` gained the `ResourceContextId`
  column and full query/append plumbing, but **no new call site populates it yet**
  (`IntegrationSettingsService`, `ScheduleDispatchRunner`'s audit calls, the WorkflowStartRequestProcessor/
  OperationStartRequestProcessor "best-effort" audit calls could all source it from data already in
  hand — the Integration/Run entity — with no new query). This is the single largest remaining gap
  against the brief's acceptance criteria ("contextual Audit entries carry context where authoritative
  context exists").
* **Run/Schedule-diagnostic DTO exposure** — `RunSummaryDto`/`RunDetailDto`/`ScheduleDto` do not yet
  expose `ResourceContextId`, and no optional `resourceContextId` query filter was added to
  `IRunQueryService`. The columns and data exist; only the read-side DTOs were not touched.
  PlatformEvent/NotificationInstance/AttentionIncident DTOs were likewise not touched.
* **Optional historical query filters** (§19 of the brief) — not added anywhere.
* **Broader regression sweep** — the full `Apperture.IntegrationTests` suite (unrelated pre-existing SQL
  Server tests) could not be re-run at the very end of the session because the local SQL Server
  container became unreachable after an unrelated dev-host process was stopped (§9). The two SQL Server
  tests written for this epic (CTX-1's and CTX-2's own migration tests) both passed against real SQL
  Server earlier in the session; a full re-run is recommended once the local SQL Server is back up.

None of these gaps compromise the load-bearing invariant (resolve-once-and-snapshot, never re-derive) —
they are unexposed/unpopulated surface area on top of a schema and dispatch path that are already
correct and tested. CTX-3 does not strictly depend on any of them, but they should be finished before
declaring CTX-2 fully done.

CTX-3 was not started.

---

## 15. Completion pass (2026-09-13) — closing the §14 gaps

Executed via `docs/superpowers/plans/2026-09-11-ctx2-completion.md`, 17 tasks, each implemented and
task-reviewed independently (fresh implementer + fresh reviewer per task), plus this section's own
validation sweep. The historical session-budget disclosure in §14 above is left as written — it is the
honest record of what the original session traded off; this section records what closed those gaps.

### Audit call-site coverage

`AuditRecord` (`src/Apperture.Core/Apperture.Core.Domain/Audit/AuditRecord.cs`) gained a
`ResourceContextId` field, threaded through `SqlAuditService`'s append/query/get-by-id paths. The
following call-site families now populate it, each from data already in hand at the call site unless
noted:

* **`WorkflowStartRequestProcessor` / `OperationStartRequestProcessor`** — the "definition unavailable"
  best-effort audit uses the already-loaded `OperationRunEntity.ResourceContextId`. No new query.
* **`WorkflowRetryService`** — the retry-blocked audit uses the already-loaded `originalRun.ResourceContextId`.
  No new query.
* **`BulkOperationItemRetryService`** — both the "requested" and "completed" bulk-retry audit records use
  the already-loaded `OperationGroupEntity.ResourceContextId`. No new query.
* **`ScheduleDispatchRunner`** — all 10 audit call sites (dispatched/blocked/failed for both workflow and
  operation targets, claim-recovered, blocked-by-readiness, blocked-by-lifecycle, legacy-no-integration)
  now carry `ScheduleDefinition.ResourceContextId`. The stale-claim-recovery path was restructured to fetch
  the `ScheduleDefinition` once and reuse it for the later unconditional fetch already present in that
  method — a net-zero change in query count, verified path-by-path.
* **`IntegrationSettingsService`** — a **documented exception** to the "no new query" rule: this service
  never loaded an `Integration` entity for any reason before this pass. It gained a new
  `IIntegrationRepository` dependency and resolves `ResourceContextId` via a single keyed-by-id lookup
  inside its one `AppendAuditAsync` funnel point (all four mutating methods route through it), consistent
  with the completion brief's explicit instruction to add this lookup.
* **Integration/Connection/Binding create-update actions** — investigated and found to have **no existing
  audit call site at all** in the current codebase (not merely unpopulated). Populating a call site that
  doesn't exist is out of scope for this pass; flagged here as a finding for a future epic, not a gap in
  this one.
* **System/IAM audit call sites** (account/auth/bootstrap/module-registry/etc.) were surveyed and
  confirmed to correctly stay `null` — none of them target a ResourceContext-owned resource.

An optional, SQL-side `FilterByResourceContextId` was added to `AuditQuery`/`SqlAuditService.ApplyFilters`,
matching the existing `ModuleId`/`ScheduleId`/`RunId` filter shape (added because a per-Application audit
view is a plausible, central CTX-3 read).

### Read-side exposure

* **`RunSummaryDto` / `RunDetailDto`** (`IRunQueryService.cs`) — both gained `ResourceContextId`, mapped
  from the `OperationRunEntity` at all four DTO-construction sites in `RunQueryService`. No
  `ResourceContextRef`/join added, per the brief's instruction to keep Run exposure ID-only.
* **`ScheduleDetailDto`** (`SchedulesController.cs`) — gained `ClaimResourceContextId`, the CTX-2
  historical snapshot from the specific in-progress `ScheduleRun` claim, deliberately and explicitly
  distinguished (by name and XML doc) from that same DTO's pre-existing `ResourceContextId`
  (CTX-1 current Schedule ownership). This required extending `IScheduleRepository.GetInProgressRunClaimAsync`'s
  return shape and `ScheduleInProgressClaim` to carry the value through. There is no other historical
  per-`ScheduleRun` read surface in the codebase today (no run-history feed exists) — the in-progress
  claim seam is the only place `ScheduleRun` data reaches an API caller, so that is what this pass
  extended, per the brief's "do not redesign schedule APIs" constraint. Incidentally, the separate
  `Apperture.Management.Client` copies of `ScheduleDto`/`ScheduleDetailDto` (used by `ManagementClient`/
  the Razor UI) were found to be silently missing the CTX-1 `ResourceContextId`/`ResourceContextRef`
  fields the API already returned; since this pass was already touching those exact records to add the
  new field, it backfilled the missing CTX-1 fields too rather than ship a new field next to a known gap.
* **`AttentionIncidentDto`** (`IntegrationScheduleDiagnosticsDtos.cs`) — gained `ResourceContextId`,
  threaded from `AttentionIncidentProjection.ResourceContextId` (which already carried it) through
  `IntegrationsController.ToDiagnosticsDto`.
* **`NotificationInstanceSummaryDto` / `NotificationInstanceDetailDto`** (`NotificationInstanceDtos.cs`) —
  both gained `ResourceContextId`, threaded from `NotificationInstance.ResourceContextId` (which already
  carried it) through `NotificationInstancesController`'s `ToSummaryDto`/`ToDetailDto`.
* **`PlatformEventEnvelope`** — already exposed `ResourceContextId` before this pass (confirmed by
  research at the start of this pass); no DTO change was needed or made. This is a deliberate, verified
  "already done" finding, not an oversight.

### Filters

Required (per the brief): **`RunQueryFilter.ResourceContextId`**, added with a SQL-side `.Where` clause
in `RunQueryService.ListRunsAsync` composing as pure AND with `IntegrationId`/`Status`/the Blocked facet/
`CorrelationKey`; an absent filter returns the identical prior set; a pre-CTX-2 `null` historical row
never matches a specific context value. Wired as an optional `resourceContextId` query parameter on
`RunsController` — API-contract preparation only, no UI wiring.

Added because the brief's three-condition decision rule (structured query object already exists; SQL-side
and small; plausibly useful to CTX-3) was satisfied for all three:

* **`AuditQuery.FilterByResourceContextId`** (`SqlAuditService.ApplyFilters`).
* **`NotificationInstanceListQuery.ResourceContextId`** (`SqlNotificationInstanceStore.ListAsync`), wired
  as an optional query parameter on `NotificationInstancesController.List`.
* **`PlatformEventQuery.ResourceContextId`** (`SqlPlatformEventStore.QueryAsync`), wired as an optional
  query parameter on `PlatformEventsController.List`.

Deliberately **not** added: an Attention-incident filter. No structured query object exists for Attention
incidents today (`IAttentionIncidentTracker`'s list/get methods take positional scalar parameters, not a
query record) — the brief's first condition (a structured filter object must already exist) is not met,
so this was correctly left unbuilt rather than inventing a new query type solely for filter symmetry.

None of the new filters were wired into any UI/Razor component in this pass.

### Snapshot integrity

`tests/Apperture.UnitTests/ResourceContexts/Ctx2HistoricalProvenanceTests.cs` (now 7 tests, up from the
original 5) directly proves:

* An `OperationRun`'s historical `ResourceContextId` stays `"app-a"` after the owning Integration's
  current context is changed to `"app-b"` in controlled test setup (pre-existing test, unchanged).
* **New:** execution audit uses the Run's own snapshot, not current Integration ownership, even when they
  are made to deliberately differ. This test dispatches a real workflow, changes the Integration's current
  context afterward, then drives the real `WorkflowStartRequestProcessor.ProcessClaimedAsync` code path
  (not a hand-fed reflection call) into its `TryAuditDefinitionUnavailableAsync` catch branch, and asserts
  the captured `AuditRecord.ResourceContextId` is the Run's stale-by-design snapshot (`"app-a"`), never the
  Integration's now-current value (`"app-b"`) — independently traced through the production call site
  during task review to confirm this is a genuine regression guard, not a parameter-pass-through test.
* **New:** a `ScheduleRun` claim's historical `ResourceContextId` stays put after the owning
  `ScheduleDefinition`'s current context is changed in controlled test setup, mirroring the Integration
  test's pattern (seed definition → claim run → mutate definition → re-read run `AsNoTracking()` →
  assert unchanged).

No production move API was added for any of these tests; all ownership changes are direct, documented
test-setup mutations, per the brief's constraint.

### Validation

Run directly (not delegated) as the final step of the completion pass, on 2026-09-13:

| Suite / build | Result |
|---|---|
| Full solution build (`Apperture.slnx`) | **0 errors**, 4 pre-existing warnings unrelated to this pass |
| `Apperture.UnitTests` (full, unfiltered) | **2027 passed**, 0 failed (1991 at original CTX-2 close + 36 new tests from this pass) |
| `Apperture.Management.Ui.Shared.Tests` | **761 passed**, 0 failed (unchanged — no UI Shared code touched) |
| `Apperture.ArchTests` | **38 passed**, 0 failed (unchanged) |
| `Apperture.IntegrationTests` (SQL Server live at `127.0.0.1,14333`) | **201 passed**, 0 failed |
| `Apperture.Management.Api` / `.Management.Host` / `WorkerHost` / `Persistence.SqlServer` / `.Management.Client` / `.Ui.Shared` / `.Ui.Client` builds | All OK, 0 errors |

The SFTP-endpoint-dependent test named in the original CTX-1/CTX-2 closure reports
(`Post_sftp_test_connect_persists_and_updates_readiness_across_unverified_success_failed`) no longer
matches that name in the current `Apperture.IntegrationTests` suite — it appears to have been renamed or
restructured in the time since those reports were written — and no environment-dependent failure was
observed in this pass's full run. No production or test semantics were altered to hide any environment
requirement; this is simply the result observed against a live, reachable SQL Server instance.

### Process notes

Each of the 17 tasks was implemented by a fresh subagent and reviewed by a fresh reviewer subagent before
being marked complete, per `superpowers:subagent-driven-development`. Two tasks required one fix round
each after their first review:

* **Run query filter composition test** — the initial "composes with IntegrationId" test didn't actually
  discriminate on that filter (both seeded rows shared the same IntegrationId); reworked to a genuinely
  discriminating three-row setup, mutation-verified.
* **PlatformEvent filter store-level coverage** — the initial pass added only a controller-level test;
  review found this broke the actual sibling-filter precedent (IntegrationId/EventType/RunId/ModuleId all
  have store-level SQL tests), so a matching store-level test was added.

One controller ruling was made during review: the ScheduleRun immutability test's first draft (following
this plan's own, buggy sketch) didn't actually simulate a ScheduleDefinition-side context change. Since
the original CTX-2 completion brief explicitly requires this exact proof, the controller ruled it must be
fixed rather than parked; it was reworked to genuinely seed a `ScheduleDefinition`, claim a `ScheduleRun`,
mutate the definition's context, and confirm the run's snapshot survived.

CTX-3 was not started.
