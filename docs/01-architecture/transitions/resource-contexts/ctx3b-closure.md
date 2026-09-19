# CTX-3B Closure — Resource Movement

Date: 2026-09-17 (core closure); manual browser smoke, merge to `master`, and post-merge validation completed 2026-09-18 — see §9.
Status: **Complete.** Engineering, automated-test validation, manual browser smoke (11 scenarios, §5), the fast-forward merge into `master`, and post-merge validation all finished. See §9.
Branch: `resource-contexts` (this closure session); merged into `master` via fast-forward 2026-09-18, then deleted along with its obsolete safety branches — see §9.
Design basis: [`ctx3-design-gate.md`](./ctx3-design-gate.md), building on [`ctx3a-closure.md`](./ctx3a-closure.md)

---

## 1. Executive result

CTX-3B delivers the movement capability that CTX-3A's own closure said the Application feature could not ship without: Integration, Connection and Schedule owners can now be moved between Applications and Shared through explicit `ownership/move-preview` / `ownership/move` endpoints, with a single shared validator per resource family so preview and the real mutation can never drift apart. A database-scoped application-lock gate (`apperture:resource-ownership`) now serializes every ownership-sensitive mutation — the two move families, standalone Schedule move, binding creation, attached-Schedule creation, and Application archive — closing the cross-entity races that per-row locking alone could not close without deadlocking the two move paths against each other. Every move is captured as a two-sided, correlated audit pair (or a single rejection record), and `binding_cross_context` readiness now backstops any binding that slips past the supported API. Separately, but required by the design gate before CTX-3B could be considered closed (§13 CTX-3B step 7), the Integration hard-delete referential-lifecycle gap CTX-3A's own closure flagged as open debt (§9 of that closure) has been inventoried and fixed: every current-state dependent Integration delete can orphan is now either a delete-blocking guard or a deliberate cleanup, every blocker CTX-3B itself adds has a real operator remediation path (two small new lifecycle operations — NotificationDefinition delete and attention-routing-override clear — exist specifically to make those blockers reachable), and historical rows survive a delete with a dangling `IntegrationId` by design.

This closure session (2026-09-17) ran the full validation the plan requires: all five automated test suites green at counts equal to or above the CTX-3A baseline, all five CTX-3B SQL-Server-backed test classes individually confirmed as real executions (not soft-skips) by both duration evidence and, for two of the five, by design (they hard-fail rather than soft-skip when SQL Server is unreachable), both product-decision preflights re-run live against real Aspire dev data, and the CTX-era migration chain confirmed consistent end-to-end. It did **not** perform browser smoke at the time — no browser-automation tool was available in that session, and this app requires real Entra ID sign-in with no local bypass, so eleven scenarios (seven from CTX-3B's own movement/archive-after-move flows, plus four carried forward from Plan 6a's UI work, which was automated-test-validated but never browser-smoked before this closure) were left as an explicit, named open item rather than a claimed pass. **That gap has since closed**: the operator completed all 11 scenarios manually on 2026-09-18, `resource-contexts` was fast-forward-merged into `master`, and post-merge validation plus a runtime sanity check both passed. See §9.

**Per design gate §15 D1, CTX-1 + CTX-2 + CTX-3A + CTX-3B together complete the initial Application/ResourceContext rollout.** This closure does not claim Services, Resource Catalog, Business Data's final architecture, dashboard-panel/dashboard-builder work, or the workflow designer are complete — all remain future work, unaffected by CTX-3B, and are named again in §7 so this closure does not silently drop them.

## 2. What CTX-3B delivered

- Explicit `ownership/move-preview` / `ownership/move` endpoints for Integration, Connection and Schedule (§8.2), backed by one shared validator per family so preview and move cannot drift.
- **One ownership mutation gate.** A single transaction-owned SQL Server application lock
  (`apperture:resource-ownership`) acquired first by all 11 operator/API-reachable ownership-sensitive
  mutation entry points — Plan 2's original six (Integration move, Connection move, Schedule move,
  binding creation, attached-Schedule creation, Application archive) plus five more closed by a
  post-Plan-2 archive-integrity completion pass (Integration creation, blocker-producing Integration
  update/reactivation, Connection creation, Schedule enable, and blocker-producing Schedule update) —
  see §3 for the full inventory. Row-level `UPDLOCK, ROWLOCK` is retained on mutated rows as defense in
  depth, but correctness does not depend on cross-entity lock ordering. Standalone Schedule creation
  remains deliberately ungated because it is not operator/API reachable today; the design gate records
  that it must join the gate if that ever changes.
- `binding_cross_context` readiness defense-in-depth.
- A two-sided movement audit (`moved_out`/`moved_in` sharing one `CorrelationId`, or a single
  `move_rejected`), with `CorrelationId` now genuinely persisted (previously domain-only, never written).
- Movement UI on the Integration hub, Connection detail and Schedule detail, plus updated
  archive-refusal copy (§6.5 D2).
- The Integration hard-delete referential-lifecycle inventory and fix, including secret-safe cleanup of
  Integration-scoped setting values and an `integration.deleted` audit record.
- **Two small lifecycle operations that make the new delete blockers reachable**: explicit deletion of
  user-created NotificationDefinitions (with their subordinate subscriptions and user overrides, history
  preserved), and clearing an Integration attention-routing override back to the instance default. Both
  reuse existing authorization; neither introduces a new permission.

## 3. Serialization model

The gate is one database-scoped SQL Server application lock (`sp_getapplock` on resource name
`apperture:resource-ownership`), acquired inside the caller's own transaction, before any ownership-sensitive
read. **11 operator/API-reachable mutation entry points participate** — re-audited against current
production code in this closure session, not carried forward from the original Plan 2 count:

1. `ResourceOwnershipMoveService.MoveIntegrationAsync`
2. `ResourceOwnershipMoveService.MoveConnectionAsync`
3. `ResourceOwnershipMoveService.MoveScheduleAsync`
4. `BindingService.CreateBindingAsync`
5. Integration-attached Schedule creation (`SqlScheduleManagementService.CreateScheduleAsync`, attach path)
6. ResourceContext/Application archive (`ResourceContextManagementService`)
7. Integration creation (`IntegrationManagementService.CreateIntegrationAsync`)
8. Blocker-producing Integration update/reactivation — Retired → Active, Retired → Inactive
   (`IntegrationManagementService.UpdateIntegrationAsync`)
9. Connection creation (`ConnectionManagementService.CreateConnectionAsync`)
10. Schedule enable (`SqlScheduleManagementService.EnableAsync`)
11. Blocker-producing Schedule update — Disabled → Enabled (`SqlScheduleManagementService.UpdateScheduleAsync`)

Participants 1–6 are Plan 2's original set. Participants 7–11 were closed by a **post-Plan-2
archive-integrity completion pass**: a new Active Integration or Connection, a Schedule transitioning
Disabled → Enabled, or an Integration transitioning Retired → {Active, Inactive} is an archive-blocking
resource entering or newly blocking a context, and each of these can race a concurrent Archive evaluating
that same context's ownership summary without seeing the incoming or newly-live resource. Each of the five
added call sites carries an in-code comment naming itself a "CTX-3B follow-on" closing that specific
create-vs-archive or reactivation-vs-archive race that Plan 2's own final review flagged but did not fix.
Participants 10 and 11 share one physical `AcquireAsync` call site inside a private helper
(`SqlScheduleManagementService.ApplyGatedScheduleTransitionAsync`), because both are the same underlying
Disabled → Enabled transition reached through two different public entry points (`EnableAsync` and
`UpdateScheduleAsync`); they are counted separately here as two distinct operator/API-reachable entry
points, not as two physical gate-acquisition call sites.

**Standalone Schedule creation remains deliberately ungated.** It has no owning Integration, so it does not
participate in the binding cross-context invariant the gate primarily protects — but an `Enabled: true`
standalone Schedule created into a context does count toward that context's `BlocksArchive` calculation, so
a concurrent standalone-create-into-T race against archiving T is a real, named, pre-existing gap, not a
closed one. It is left ungated because it is not operator/API reachable today (no production path creates a
standalone Schedule); the design gate records that it must join the gate if that path is ever exposed. This
closure does not widen scope to fix it.

A bounded acquisition timeout means a second holder waits briefly and then fails with
`ResourceOwnershipBusyException` (carrying a `BusyCode`), surfaced to the operator as
`ownership_operation_busy` rather than a raw SQL Server error or a timeout that looks like a hang —
`Gate_timeout_surfaces_as_a_human_readable_busy_conflict` proves the thrown message never leaks
`sp_getapplock`. Because the lock is a single named resource rather than per-row, all 11 participants are
serialized against each other pairwise and globally, not just against themselves.

**Single-row locking was rejected** because it does not actually serialize the two paths that matter most
against each other. Integration move and Connection move never touch the same row directly — a Connection
move only reads Integration rows to compute the `binding_integration_unreachable` blocker, it does not lock
them — so per-row `UPDLOCK` alone leaves an interleaved Integration-move-and-Connection-move race entirely
unserialized: the row-lock discipline that protects "move vs. bind" and "move vs. move-on-the-same-entity"
says nothing about "move Integration A into a context while a concurrent Connection move changes what that
context's binding picture looks like." The naive repair — having each move path additionally take a
row-level lock on its counterparty's rows before proceeding — produces a classic ABBA deadlock: an
Integration move can lock Integration-then-Connection rows while a concurrent Connection move locks
Connection-then-Integration rows, and SQL Server's deadlock monitor would have to arbitrate a victim on every
such interleaving instead of the operation simply queuing. A single database-scoped gate sidesteps this by
giving all 11 participants one shared choke point with one lock order (there is only one lock), at the cost
of serializing these operations against each other even when they don't logically conflict — an accepted
trade-off given movement, creation, reactivation and archive are low-frequency operator actions, not a hot
path. Row-level `UPDLOCK, ROWLOCK` is kept as defense in depth for anything that reads or writes ownership
outside the gate's own eleven call sites, but correctness no longer depends on it or on any cross-entity
lock ordering.

The design was validated, not just argued: `ResourceOwnershipMoveConcurrencySqlServerTests` drives 10 real
interleavings against a real SQL Server scratch database (six required by the design gate's "move vs. bind"
races, plus three CTX-3B archive-concurrency-integrity additions and one schedule-attach case) and asserts,
for every one, that no SQL error 1205 deadlock occurs and that the loser genuinely blocks behind the gate
rather than racing ahead. All 10 passed in this closure session (§5).

## 4. Statements this closure makes

- **Resource movement is complete.**
- **CTX-3A and CTX-3B together complete the initial Application-workspace rollout.** Per design gate §15
  D1, a merged CTX-3A with no CTX-3B was an internal milestone, not a release state; that milestone is
  now closed out.
- **Historical CTX-2 provenance remains immutable.** No move alters any CTX-2 historical-snapshot table;
  `ResourceMoveHistoricalImmutabilityTests` (6 facts, part of the green `Apperture.UnitTests` run) asserts
  this directly against the production `MoveIntegrationAsync`/`MoveConnectionAsync`/`MoveScheduleAsync`
  methods, on both the successful and the blocked path, across all seven historical families the design gate
  names (Run, ScheduleRun, OperationGroup, PlatformEvent, AttentionIncident, NotificationInstance, AuditLog)
  plus four more the test harness additionally captures.
- **Cross-context references are transactionally protected.** The move/bind, move/move, move/attach and
  move/archive races are closed by the ownership mutation gate, proven by 10 deterministic SQL Server
  interleaving tests (§3, §5), and backstopped by `binding_cross_context` readiness for any path that is not
  the supported API. (Test-parallelism note: exactly two `[CollectionDefinition(..., DisableParallelization =
  true)]` classes exist in the solution — `SqlServerIntegrationCollection` and `BootstrapHttpCollection` —
  both pre-dating CTX-3B by more than two months; CTX-3B's two new SQL-Server test classes join the existing
  `SqlServerIntegrationCollection` rather than introducing a new disable.)
- **Settings, notification definitions, attention routings and secrets are unchanged by a move — a
  behavioral guarantee, not a type-level one.** `ResourceOwnershipMoveService` holds a full
  `AppertureDbContext` in addition to its narrower repositories, so it is not *architecturally* incapable of
  writing other tables — the guarantee instead comes from reading every line of `MoveIntegrationAsync`,
  `MoveConnectionAsync` and `MoveScheduleAsync` and confirming the only writes any of the three ever perform
  are `Integrations.ResourceContextId`, `ScheduleDefinitions.ResourceContextId`, and the movement's own audit
  rows. This was verified by code-reading in Task 2 of this closure plan, not by a dedicated
  seed-move-assert-byte-identical behavioral test analogous to `ResourceMoveHistoricalImmutabilityTests`; a
  future pass could add one, but the code-reading evidence is direct and unambiguous.
- **The Integration-delete referential lifecycle has been audited and fixed, and every blocker CTX-3B
  adds is clearable by an operator.** The reachability rule — no blocker without a remediation path —
  was applied after the review found that neither `NotificationDefinitions` nor
  `IntegrationAttentionRoutings` had one, which would have repeated CTX-1's unreachable archive rule
  (design gate F4).
- **Context-scoped IAM remains deferred.** No `context:{id}` scope key, grant or role was introduced
  anywhere in CTX-3B; movement uses only the pre-existing `integration.update`/`connection.update`/
  `schedule.manage` permissions.
- **Business Data architecture remains deferred.** Unchanged since CTX-3A.
- **Services / Resource Catalog / dashboard-panel work remains later**, unaffected by CTX-3B.
- **Connection/Schedule broader lifecycle cleanup remains later.** Only Integration hard-delete was in
  scope.

## 5. Test baseline at closure

All numbers below are from real, witnessed runs performed in this closure session (2026-09-17), against
current `resource-contexts` HEAD, working tree clean at session start. `dotnet` SDK 10.0.401.

### Build and the five suites

| Check | Result | CTX-3A baseline (for comparison) |
|---|---|---|
| Full solution build (`dotnet build Apperture.slnx`) | ✅ 0 errors, 1 warning (pre-existing Aspire CLI-bundle advisory, unrelated to CTX-3B) | 0 errors, 44 warnings |
| `Apperture.UnitTests` | ✅ 2343/2343 passed, 0 failed, 0 skipped (9.14s) | 2170/2170 |
| `Apperture.Management.Ui.Shared.Tests` | ✅ 880/880 passed, 0 failed, 0 skipped (2.02s) | 831/831 |
| `Apperture.Management.Ui.Client.Tests` | ✅ 19/19 passed, 0 failed, 0 skipped (1.23s) | 19/19 |
| `Apperture.ArchTests` | ✅ 38/38 passed, 0 failed, 0 skipped (3.69s) | 38/38 |
| `Apperture.IntegrationTests` (real SQL Server wired in) | ✅ 226/226 passed, 0 failed, 0 skipped (3.79 min) | 204/204 |

Every suite count is at or above the CTX-3A baseline. The `Apperture.IntegrationTests` 226/226 result is
independently confirmed by the controller session directly from `tests/Apperture.IntegrationTests/TestResults/full-run.trx`:
exactly 226 `outcome="Passed"` entries, 0 failed, 0 skipped — matching Task 1's own count exactly.

### The five CTX-3B SQL Server test classes, individually — executed, not soft-skipped

Three of the five classes use a soft-skip pattern (`if (fixture is null) return;`, which xUnit reports as an
ordinary `Passed` indistinguishable by exit code alone from a real pass); two use a hard-fail pattern
(`fixture.Should().NotBeNull(...)`) that fails loudly rather than soft-skipping. Each class was run
individually by name in this session, and cross-checked against a full-suite `.trx` with per-test durations —
a soft-skip returns in under 1ms; every test below took 6–13 seconds, consistent with a real
create-scratch-database-and-migrate-and-exercise-and-drop lifecycle, and no leftover scratch catalogs remain
on the SQL Server container afterward.

| Test class | Plan | Result | Skip pattern | Evidence of real execution |
|---|---|---|---|---|
| `ResourceOwnershipMutationGateSqlServerTests` | 2 | 3/3 passed, 0 skipped | Soft-skip | Durations 7s, 7s, 12s |
| `ResourceOwnershipLockingSqlServerTests` | 2 | 2/2 passed, 0 skipped | Soft-skip | Durations 13s, 7s |
| `ResourceOwnershipMoveConcurrencySqlServerTests` | 2 | 10/10 passed, 0 skipped | Hard-fail (a Pass is itself conclusive) | Durations 7–12s; every interleaving's `AssertNoDeadlock` (SQL error 1205) passed |
| `AuditCorrelationIdSqlServerTests` | 4 | 2/2 passed, 0 skipped | Soft-skip | Durations 11s, 7s |
| `ResourceMoveAuditFilteringSqlServerTests` | 4 | 5/5 passed, 0 skipped | Hard-fail (a Pass is itself conclusive) | Durations 7–12s, incl. a real transactional-rollback test re-verified via a fresh connection |

**Total: 22/22 SQL Server tests executed for real and passed** (3+2+10+2+5), none soft-skipped — matching the
same 22 tests inside the 226-test `Apperture.IntegrationTests` total above. The controller session
independently confirmed the same 6–13-second-per-test pattern directly in `full-run.trx`.

### Migration chain

The only CTX-era migrations, in order, are `20260911120000_Ctx1_ResourceContextFoundation`,
`20260912001419_Ctx2_ResourceContextHistoricalProvenance`, and `20260916173420_Ctx3B_AuditCorrelationId` — the
last migration in the entire solution before the current model snapshot; nothing CTX-3B-era comes after it.
CTX-3A added no migration (no new persisted columns). Each of the three has its own SQL-Server-backed
migration-proof test — `ResourceContextMigrationSqlServerTests` (CTX-1), `Ctx2MigrationSqlServerTests` (CTX-2),
and `AuditCorrelationIdSqlServerTests` (CTX-3B, table above) — and all three ran and passed in this session.
The migration chain is consistent end-to-end with no gap between what the model snapshot expects and what the
three migrations produce.

### Preflight results (live re-check, this session)

Neither preflight was ever historically recorded in the repository: Plan 2 Task 1 Step 0 and Plan 3 Task 0
are both still unchecked plan checkboxes, the implementing commits (`3e178b9`, `336206c`) record only code
changes, and no prior CTX-3B closure doc existed to have recorded a result. This closure therefore reports a
**live re-check performed in this session**, not a historical citation of an implementation-time result that
was never written down.

Both queries were run verbatim from their owning plans, read-only, against the Aspire dev SQL Server instance
(`sql-9dfe7faf`, `127.0.0.1,14333`) — the branch's own accumulated local dev data (21 Integrations, 7
ScheduleDefinitions, 7 Bindings) — because no production or production-shaped snapshot was reachable from this
session. Both also came back 0 against the freshly-assembled `Apperture_IntegrationTests` catalog as a
secondary schema-validity check.

| Preflight | Plan | Query target | Result |
|---|---|---|---|
| Schedule ownership drift (P3) | Plan 2 Task 1 Step 0 | ScheduleDefinitions vs. owning Integration's context | **0 rows / DriftCount = 0** |
| Pre-existing cross-context bindings | Plan 3 Task 0 | Bindings vs. Integration/Connection context, excluding Shared | **0 rows / DriftCount = 0** |

**Honesty note:** this is genuine live evidence against real accumulated dev history, not a production check —
the best evidence obtainable in this session, but not identical in strength to a production check. See §6,
product decision P3.

### Browser smoke — not performed in this (2026-09-17) session; completed 2026-09-18, see §9

No browser-automation tool was available in this session (`ToolSearch` for browser-automation tooling
returned only `WebFetch`, a one-shot read-only fetch, unable to click, fill forms, or hold an authenticated
Blazor session). The controller session independently confirmed no AppHost process is running and no local
dev-auth bypass exists — this app requires real Entra ID OIDC sign-in. **Nothing below was performed as of
this session.** All 11 scenarios required a human with a browser against a running, authenticated AppHost
instance — the checklist below is preserved as the record of what was specified; **§9 records that all 11
were subsequently completed successfully by the operator on 2026-09-18.**

**CTX-3B's own movement/archive-after-move scenarios** (Plan 5 Task 5 Step 5 and Plan 5 Task 6 Step 2):

1. Open an Integration in an Application → "Move to another context…" → pick a target → the preview renders
   effects and blockers → submit a valid move → the URL does not change, the ownership chip updates, and a
   mismatch notice appears without switching the shell View if it differs.
2. Pick a blocked target → the Move button stays disabled and the blockers render with working links.
3. Switch targets rapidly → the dialog never shows a preview for a target other than the currently selected
   one.
4. Connection detail → "Used by" still renders alongside the new action; the preview's `used_by_integration`
   effect shows even when the move is allowed.
5. Schedule detail → an attached schedule shows the disabled action with a working link; a legacy standalone
   schedule's dialog works.
6. Move out of an archived source Application → allowed; archived Applications never appear as targets.
7. Create an Application, add a live Integration, attempt archive → refusal mentions "move them to another
   context" → follow "View integrations" → move the Integration elsewhere → return → archive now succeeds.
   (The underlying service-level mechanics of this scenario are covered by an automated test,
   `ArchiveAfterMoveTests`, which is part of the green `Apperture.UnitTests` run above — but that is a
   service-layer test, not the UI walkthrough, and does not substitute for it.)

**Plan 6a stabilization-era UI, carried forward as never browser-smoked before this closure session** (per
that plan's own stabilization note — these cover the delete-blocker-clearing UI, not CTX-3B's movement UI):

8. NotificationDefinition Delete button visibility: present for an eligible user-created definition, absent
   for a built-in definition.
9. Deleting an eligible NotificationDefinition succeeds and clears the corresponding Integration-delete
   blocker.
10. Attention-routing "Clear override" requires a confirmation dialog; Cancel leaves the override intact;
    Confirm removes the override, falls back to default routing, and clears the corresponding Integration-delete
    blocker.
11. Once every independent Integration-delete blocker is cleared, Integration delete succeeds and historical
    Run/Audit information remains visible afterward.

## 6. Product decisions recorded

- **P1 — one ownership mutation gate** rather than ordered row locks. Per-row locking alone leaves Integration
  move and Connection move entirely unserialized against each other (they never touch the same row), and the
  naive repair — each path additionally locking its counterparty's rows — produces an ABBA deadlock between
  the two moves. A single database-scoped application lock, acquired first by all 11 operator/API-reachable
  ownership-sensitive mutation entry points (§3) — Plan 2's original six plus five more closed by a
  post-Plan-2 archive-integrity completion pass — gives correctness a single lock order at the cost of
  serializing low-frequency operator mutations against each other even when they don't logically conflict.
  Validated by 10 real SQL Server interleaving tests, all passing, no deadlock observed on any interleaving
  (§3, §5).
- **P2 — `integration.deleted` audit**, narrow by design. Only the Integration hard-delete lifecycle action is
  audited by CTX-3B; create, update, activate, deactivate and retire remain unaudited, pre-existing and
  deliberately not broadened here (see §7).
- **P3 — ownership preflights (schedule drift and cross-context bindings)**: both preflights the plans
  required (Plan 2 Task 1 Step 0, Plan 3 Task 0) were never historically recorded in the repository. This
  closure session ran both live against real Aspire dev data and got 0 rows / DriftCount = 0 on each (§5).
  Per the plans' own stated rule for Count = 0, `schedule_context_inconsistent` stays a strict hard blocker
  with no repair operation, and no schedule-context repair/auto-correct operation exists in the codebase,
  consistent with the rule never having been triggered. This is the best evidence obtainable in this session
  (real dev-history data, not production), and is reported as such rather than as a stronger historical claim.

## 7. Known deferred work and named debt

**Already named at CTX-1/CTX-2/CTX-3A closure, still open:**

- **`ScheduleDefinitions` is the one Integration-delete blocker still without a remediation path.**
  Schedules have no delete operation anywhere (CTX-3A closure already records this), so an Integration
  that owns a schedule cannot be deleted and the operator cannot clear the blocker. This is
  **pre-existing CTX-1 behavior, not introduced by CTX-3B**, and CTX-3B deliberately did not widen into
  Schedule lifecycle. Apply the reachability rule to it when Schedule lifecycle work happens.
- **Integration lifecycle audit is incomplete.** `integration.deleted` is the only lifecycle action
  audited. Create, update, activate, deactivate and retire remain unaudited — pre-existing, deliberately
  not broadened by CTX-3B, and worth a future consistency pass.
- **Notification definition deletion has API support but no dedicated operator UI.** CTX-3B added the
  endpoint and client method needed to clear the delete blocker; whether the notification surfaces want
  a delete affordance of their own is a notification-epic question.
- **Connection lifecycle** — true-delete eligibility and retirement cleanup remain deferred.
- **Schedule lifecycle** — retirement/delete eligibility remains deferred; `ScheduleDefinitions` blocking
  Integration delete still has no general delete path (restated from above — the same gap, named twice
  because it is both a delete-blocker problem and a lifecycle-completeness problem).
- **Context-scoped IAM remains deferred.** No `context:{id}` scope key, grant or role exists.
- **Business Data final architecture remains deferred; current Business Data remains transitional.**
  Unchanged since CTX-3A — still an unscoped demo projection with an informational notice, not a designed
  collection architecture (DAT).
- **Services / module architecture remains future work**, unaffected by CTX-3B.
- **Resource Catalog remains future work.** Config Inventory is a diagnostic/ownership-classification
  surface, not the Resource Catalog described in ADR-0040 — the two are not the same thing and this
  closure does not conflate them.
- **Dashboard panels / dashboard builder remain future work**, unaffected by CTX-3B.

**Not previously named in a CTX closure, but real carried-forward debt/roadmap items surfaced by this
closure session's broader mandate:**

- **Shared wizard subsystem remains future work.**
- **Workflow designer remains future work.**
- **Notification subscription permission asymmetry — intentional, named as debt, not weakened for
  closure.** An Integration-scoped subscription attached to a Platform/Module-scoped definition may require
  instance-level `notification.definition.manage` to remove, rather than a narrower Integration-scoped
  permission. This is deliberate given the dependency graph (`ctx3b-integration-delete-referential-inventory.md`),
  not an oversight, and this closure does not propose relaxing it.
- **Setting-definition scope drift can surface an unpolished error.** If a module narrows or removes an
  Integration-scoped setting definition while persisted values for it remain, Integration delete may
  currently surface a raw `ArgumentException` rather than a polished operator-facing error. Named as debt,
  not fixed by this closure.
- **Sensitive-setting tripwire — confirmed still clear in this session; watch this if it changes.**
  `IntegrationSettingValueKind.SensitiveText` is referenced only in the ModuleSdk's own type definitions
  (`IntegrationSettingValueKind.cs`, `IntegrationSettingDefinition.cs`, `IntegrationConfigurationDeclarations.cs`)
  — no shipped module currently registers an actual Integration-scoped setting with `ValueKind: SensitiveText`.
  Re-confirmed by grep across `src/` in this closure session. If a module ever does register one, the
  sensitive-settings secret-disposal path exercised by `IntegrationDeleteReferentialLifecycleTests` needs to
  be re-checked against that real case, not just the synthetic one the tests currently seed.
- **Audit `Resource`/`Details` behavior is a known audit-system quirk, not fixed by CTX-3B.**
  `SqlAuditService.AppendAsync` appends a `Resource: <value>` fragment into the `Details` string when
  `Resource` is non-null. Movement deliberately passes `Resource: null` on every record it writes so the
  structured JSON payload in `Details` stays parseable — a correct workaround for this closure's own purposes,
  but the underlying behavior (that any non-null `Resource` corrupts `Details`' JSON shape for other callers)
  is unchanged and remains audit-system debt.
- **Duplicate audit DI registration — re-investigated by HSK-A and closed as a false positive.** Plan 4
  previously noted a duplicate `services.AddScoped<IAuditService, SqlAuditService>()` registration in
  Governance composition, at
  `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Governance.cs:96`
  and `:214`, and this closure session (at the time) characterized it as harmless last-registration-wins
  cleanup debt. That characterization was **incorrect**: the two registrations live in two separate
  extension methods (`AddAppertureGovernance` and `AddAppertureEmergencyOperatorServices`), each called from
  a separate, non-overlapping composition root — the management host (`Apperture.Management.Host/Program.cs`)
  and the emergency operator CLI (`Apperture.Cli.Host/EmergencyCli.cs`) respectively — plus a third,
  independent registration in the Windows worker host
  (`Apperture.RuntimeHost.Windows.WorkerHost/Program.cs:87`). No host ever calls more than one of these
  paths, so "last registration wins" never applies within any single container, and removing either
  registration would leave its own host's `IAuditService` unresolved. HSK-A verified this by grepping every
  call site of both extension methods; no redundant registration remains to clean up. This debt item is
  closed.
- **General Integration update-vs-update concurrency remains separate, non-CTX debt, if still
  applicable.** Plan 2's review identified a generic same-Integration update lost-update class, unrelated to
  the ResourceContext archive invariant this epic protects. This closure did not re-verify whether it is
  still present in current code; it is carried forward as previously recorded debt, not re-confirmed here.

## 8. Acceptance criteria

Design gate §14, CTX-3B. All 11 criteria audited by Task 2 of this closure plan; two corrections from an
independent reviewer are applied below (the `IntegrationDeleteReferentialLifecycleTests.cs` fact count, and
the framing of the "unchanged by a move" guarantee — see §4).

| # | Criterion | Status | Evidence |
|---|---|---|---|
| 1 | No update DTO accepts `ResourceContextId`; movement exists only as §8.2 endpoints | ✅ Proven | `ResourceMoveDtoImmutabilityTests.cs` — reflection assertions across app-layer, API-layer and client-SDK update DTOs for Integration/Connection, plus `UpdateScheduleRequestBody`; a behavioral counterpart proves `SqlScheduleManagementService.UpdateScheduleAsync` silently ignores a tampered `ResourceContextId`. Movement exists only via `ResourceOwnershipMoveService`'s six `Preview*`/`Move*` methods, matching §8.2's six endpoints exactly. |
| 2 | Integration move: `binding_connection_unreachable`, `schedule_context_inconsistent`; schedules move atomically; bindings/settings/definitions/routings/secrets unchanged | ✅ Proven | `IntegrationMovePreviewTests.cs` (23 facts) covers both blocker codes, including retired/inactive integrations remaining movable. Atomic schedule move proven both directions (success and blocked) by `ResourceOwnershipMoveMutationTests.cs`. Settings/definitions/routings/secrets unchanged is a **behavioral guarantee verified by reading every line of the three `Move*Async` methods** (§4) — the only writes any of them perform are `Integrations.ResourceContextId`, `ScheduleDefinitions.ResourceContextId`, and audit rows. |
| 3 | Connection move: `binding_integration_unreachable` incl. Retired/Inactive, `unowned_binding_requires_shared`; move to Shared never blocked by bindings | ✅ Proven | `ConnectionMovePreviewTests.cs` (19 facts): inactive- and retired-integration bindings still count, unowned-binding-requires-Shared, and an explicit assertion that every context can reach Shared so moving to Shared is never blocked by bindings. |
| 4 | Schedule move: attached rejected, standalone moves | ✅ Proven | `ScheduleMovePreviewTests.cs` — attachment produces `ScheduleAttachedToIntegration` with the target still resolved for display; standalone schedules move. `ResourceOwnershipMoveMutationTests.cs` exercises the real mutation. |
| 5 | Preview and move share one validator; move revalidates in-transaction; 409 carries preview body | ✅ Proven | One `Validate{Integration,Connection,Schedule}MoveAsync` per family, called unchanged by both `Preview*Async` and `Move*Async`; the locked re-read happens only after the gate is acquired. `ResourceOwnershipMoveControllerTests.cs` proves a family blocker maps to 409 with the full preview body. |
| 6 | No historical row changes on any move — Run, ScheduleRun, OperationGroup, PlatformEvent, AttentionIncident, NotificationInstance, AuditLog byte-identical before/after | ✅ Proven | `ResourceMoveHistoricalImmutabilityTests.cs` (6 facts) runs the real move methods and asserts historical-provenance capture is byte-identical before/after, for both the successful and blocked path, across all seven required families plus four more the test harness also captures. `ResourceMoveAuditFilteringSqlServerTests.cs` additionally proves append-only behavior against a **real** SQL Server `AuditLogs` table. |
| 7 | The concurrency test proves an interleaved move and bind cannot persist a cross-context binding | ✅ Proven — confirmed executed in this session | `ResourceOwnershipMoveConcurrencySqlServerTests.cs` — 10 facts (more than the six interleavings the design gate names), each driving a real interleaving against a real SQL Server scratch database, asserting no deadlock, that the loser blocks behind the gate, and that no cross-context binding persists. Ran and passed in this closure session (§5); a hard-fail-on-no-fixture pattern makes a Pass here conclusive of real execution. |
| 8 | `binding_cross_context` readiness makes a seeded cross-context binding not runnable | ✅ Proven | `BindingCrossContextReadinessTests.cs` — seeds a cross-context binding directly (bypassing every service) and proves `IsRunnable == false` with issue code `BindingCrossContext`, via both the legacy slot-binding branch and the SDK-6C configuration-evidence branch, plus a complementary "legitimate move stays clean" test. |
| 9 | Two audit records sharing a correlation id; one `move_rejected`; source/target filtering each returns its record | ✅ Proven | `ResourceMoveAuditFilteringSqlServerTests.cs` — real `SqlAuditService`, real SQL Server: source query returns exactly `moved_out`, target exactly `moved_in`, global returns both, correlation ids match, and `Details` JSON deserializes cleanly. The blocked path writes exactly one `move_rejected` record at the source and none at the target. Exactly 2 records regardless of how many schedules attach. Ran and passed in this closure session (§5). |
| 10 | Move dialogs show preview effects and blockers with links, always show "Used by" for Connections, never switch the view | ✅ Proven | `MoveOwnershipDialog.razor` renders effects and blockers generically with explicit links for the three link-bearing blocker codes. `MovementUiSourceTests.cs` asserts the dialog and all three detail pages never call the shell-view-switching API. "Used by" is the `UsedByIntegration` effect rendered by the same generic loop; `ConnectionMovePreviewTests.cs` proves its content. `MoveOwnershipDialogModelTests.cs` (17 facts) proves the client-side state machine independent of markup (no bUnit in this repo). |
| 11 | Integration hard-delete referential inventory complete; guarded-or-cleaned-up, tested, historical survives | ✅ Proven | `docs/01-architecture/transitions/resource-contexts/ctx3b-integration-delete-referential-inventory.md` — full table of every entity reachable via `IntegrationId`/`ScopeKind+ScopeId`, each with a Decision and a "Clearable by" entry. `IntegrationDeleteReferentialLifecycleTests.cs` (**12 facts**) proves each blocker fires, each is clearable through the real operator-facing service, the coincidence the inventory relies on holds, historical rows survive with a dangling `IntegrationId`, and the sensitive-settings secret-disposal cleanup actually disables the backing secret, not just deletes the row. |

**11/11 acceptance criteria: proven.**

## 9. Post-closure completion (2026-09-18) — browser smoke, merge to `master`, and validation

The one item this closure named as genuinely open (§5) has since closed, in a separate session on
`resource-contexts` immediately following this closure's own commit. Recorded here rather than by editing
the §5 narrative above, per this document's own historical-note convention (§8's precedent in
`ctx3a-closure.md`) — the original session's findings stand as written; this section adds what happened next.

### Browser smoke — all 11 scenarios completed by the operator

The operator (not this session) walked all 11 scenarios listed in §5 against a live, authenticated AppHost
instance and reported success. Two items surfaced follow-up questions during the walkthrough, both resolved:

- **Scenario 2** (blocked move): initially unclear how to construct a blocking condition; resolved once the
  exact `binding_connection_unreachable` reproduction was explained (a Connection scoped to a specific,
  non-Shared Application, with the target being a *different* Application — not Shared, not the Connection's
  own). Confirmed working as designed once reproduced correctly.
- **Scenario 8** (NotificationDefinition Delete visibility): the operator found Delete is reachable only from
  the definition's own Detail page, not directly from the Integration Notifications panel. Confirmed this
  matches the shipped code exactly (`NotificationDefinitionDetail.razor`'s `_canDeleteDefinition` gate) — not
  a defect, just a different screen than a one-line summary implied. The operator accepted this as a future
  UI-polish candidate, not blocking work.

No other findings. All 11 scenarios pass as specified.

### Merge to `master`

A full Git topology audit (branch heads, merge-base, ancestor proof, unique-history check on every local
branch) confirmed `master` was a true ancestor of `resource-contexts` with zero unique commits on `master`'s
side, making a fast-forward safe. Executed:

```
git checkout master
git merge --ff-only resource-contexts
```

Result: **fast-forward**, `master` moved from `20d4b1ce` to `8ed656fa` (`resource-contexts`'s tip),
`git diff resource-contexts..master` empty, zero tree drift. No merge commit, no rebase, no history rewrite.

### Post-merge validation (on `master`, commit `8ed656fa`)

Re-run in full from `master`, not assumed to match the pre-merge numbers:

| Check | Result |
|---|---|
| `dotnet build Apperture.slnx` | ✅ 0 errors, 31 warnings (pre-existing) |
| `Apperture.UnitTests` | ✅ 2343/2343 |
| `Apperture.Management.Ui.Shared.Tests` | ✅ 880/880 |
| `Apperture.Management.Ui.Client.Tests` | ✅ 19/19 |
| `Apperture.ArchTests` | ✅ 38/38 |
| `Apperture.IntegrationTests` | ✅ 226/226 |
| 5 named SQL Server test classes | ✅ 22/22 executed for real (6–13s durations each), 0 soft-skipped |

Identical to the pre-merge baseline — no discrepancy. (The first build attempt failed only on stray locked
processes left running from an earlier dev session; killing them, no repo/git state touched, produced a
clean rebuild.)

### Runtime sanity

No browser-automation tool was available in the validation session either, so this final check was performed
manually by the operator: AppHost/Aspire resources healthy, Entra authentication working, Control Center
rendering, the View selector rendering, and the Integration hub loading — all confirmed.

### Branch cleanup

After the merge and validation, three branches were confirmed to have zero commits unreachable from `master`
(`resource-contexts`, `backup/pre-resource-context-rebranch`, `nav1-stabilization`) and were deleted via safe
`git branch -d` (which refuses any branch with unmerged unique commits — none did). `experimental/foundational-kernel-and-domain`
was independently confirmed to hold one unrelated, unreachable commit (a 2026-03-09 scaffolding experiment
predating this epic) and was left untouched. No remote is configured; nothing was pushed.

**CTX-1 + CTX-2 + CTX-3A + CTX-3B are now fully landed on `master`, validated, and manually smoke-tested.**

---

*Historical note: this closure does not revise `ctx3-design-gate.md` or any prior plan document — it is a
new report referencing them, per the design gate's own instruction that review history is preserved, not
rewritten to look obvious in hindsight.*
