# CTX-3B Plan 6b — Closure

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run the whole-branch validation, confirm every CTX-3B acceptance criterion against real evidence, and write the CTX-3B closure report.

**Scope: validation and documentation only.** This plan changes no production code. If it discovers a gap, the fix belongs in the plan that owns it — reopen that plan rather than patching here.

**Prerequisite:** Plans 1, 2, 3, 4, 5 and 6a are all implemented and their own validation has run and passed.

**Spec:** `ctx3-design-gate.md` §13 CTX-3B step 8, §14 CTX-3B acceptance criteria, §15 D1. Mirror `ctx3a-closure.md`'s structure and tone.

---

## Global Constraints

- **The closure states real results, not projected ones.** Every number in it comes from a witnessed run in the closure session. No filtered runs, no assumed passes, no environmental failures quietly folded into a summary. `ctx3a-closure.md` set this standard explicitly and this closure keeps it.
- **A soft-skipped SQL Server test is not a pass.** Every SQL-backed test in this epic returns early when no endpoint is reachable, which means CTX-3B's entire concurrency guarantee can silently not run inside a green suite. The closure must record executed-vs-skipped counts for the named tests below.
- **Do not revise `ctx3-design-gate.md` or any prior plan document.** This is a new report referencing them, per the design gate's own instruction that review history is preserved, not rewritten to look obvious in hindsight.
- Do not claim a criterion is met because a plan said it would be. Check the code and the test output.

---

## File Structure

**New files:**
- `docs/01-architecture/transitions/resource-contexts/ctx3b-closure.md`

---

### Task 1: Whole-branch validation

- [ ] **Step 1: Full build and every suite**

```bash
dotnet build Apperture.slnx
dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj
dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj
dotnet test tests/Apperture.Management.Ui.Client.Tests/Apperture.Management.Ui.Client.Tests.csproj
dotnet test tests/Apperture.ArchTests/Apperture.ArchTests.csproj
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj
```

Record the real counts. CTX-3A's post-stabilization baseline was UnitTests 2170, Ui.Shared 831, Ui.Client 19, ArchTests 38, IntegrationTests 204.

- [ ] **Step 2: Prove the SQL Server tests actually executed**

Run each by name against a reachable SQL Server and record executed/skipped for every one:

| Test class | Plan | Proves |
|---|---|---|
| `ResourceOwnershipMutationGateSqlServerTests` | 2 | the gate serializes, times out cleanly as `ownership_operation_busy`, and refuses to run outside a transaction |
| `ResourceOwnershipLockingSqlServerTests` | 2 | `UPDLOCK` row locks are real; `SetResourceContextIdAsync` writes one column |
| `ResourceOwnershipMoveConcurrencySqlServerTests` | 2 | all six required interleavings, with no deadlock observed |
| `AuditCorrelationIdSqlServerTests` | 4 | `CorrelationId` round-trips through the real schema |
| `ResourceMoveAuditFilteringSqlServerTests` | 4 | source/target/global filtering through the real `SqlAuditService` query path, and real transactional rollback on audit failure |

If the `apperture-test-sql` container has drifted again (it did once during CTX-3A — see `ctx3a-closure.md` §8), recover it before running, and record that you did.

- [ ] **Step 3: Confirm the preflights were run and their results recorded**

- Plan 2 Task 1 Step 0 — schedule ownership drift (product decision P3). Record the count and what was decided.
- Plan 3 Task 0 — pre-existing cross-context bindings. Record the count.

- [ ] **Step 4: Browser smoke**

Walk Plan 5 Task 5 Step 5 (six movement scenarios) and Plan 5 Task 6 Step 2 (archive-after-move). Record which steps were performed and by whom. If browser automation is not available against a confirmed-correct running instance, say so plainly and list the unperformed steps — CTX-3A's closure did exactly this, and the manual walkthrough it mandated then found six real defects.

---

### Task 2: Acceptance-criteria audit

- [ ] **Step 1: Check each design gate §14 CTX-3B criterion against the code and the test output**

| Criterion (design gate §14, CTX-3B) | Where it is proven |
|---|---|
| No update DTO accepts `ResourceContextId`; movement exists only as the §8.2 endpoints | `ResourceMoveDtoImmutabilityTests` (Plan 1), extended to the API request bodies and to `UpdateScheduleRequestBody.IntegrationId` |
| Integration move: `binding_connection_unreachable`, `schedule_context_inconsistent`; schedules move atomically; bindings/settings/definitions/routings/secrets unchanged | `IntegrationMovePreviewTests`, `ResourceOwnershipMoveMutationTests`, `ResourceMoveHistoricalImmutabilityTests` |
| Connection move: `binding_integration_unreachable` incl. Retired/Inactive, `unowned_binding_requires_shared`; move to Shared never blocked by bindings | `ConnectionMovePreviewTests` |
| Schedule move: attached rejected, standalone moves | `ScheduleMovePreviewTests`, `ResourceOwnershipMoveMutationTests` |
| Preview and move share one validator; move revalidates in-transaction; 409 carries the preview body | one `Validate*MoveAsync` per family called by both paths; `ResourceOwnershipMoveControllerTests` |
| **No historical row changes on any move** | `ResourceMoveHistoricalImmutabilityTests` — every family in Plan 2 Task 7's table, on both the successful and the blocked path |
| The concurrency test proves an interleaved move and bind cannot persist a cross-context binding | `ResourceOwnershipMoveConcurrencySqlServerTests`, six interleavings, **executed** |
| `binding_cross_context` readiness makes a seeded cross-context binding not runnable | `BindingCrossContextReadinessTests` |
| Two audit records sharing a correlation id; one `move_rejected`; source/target filtering each returns its record | `ResourceMoveAuditTests` + `ResourceMoveAuditFilteringSqlServerTests` |
| Move dialogs show effects and blockers with links, always show "Used by" for Connections, never switch the view | `MoveOwnershipDialogModelTests`, `MovementUiSourceTests`, browser smoke |
| The Integration hard-delete referential inventory is complete; every current-state dependent is guarded or intentionally cleaned up, with a test; historical records survive | Plan 6a's inventory doc + `IntegrationDeleteReferentialLifecycleTests` |
| **Every blocker CTX-3B adds is clearable by an operator** | `Every_blocker_this_plan_adds_can_be_cleared_by_an_operator_operation` and `No_undeletable_definition_can_ever_block_integration_delete` (Plan 6a Task 4), plus the inventory table's "Clearable by" column |

- [ ] **Step 2: Verify the cross-cutting properties the review called out**

Check each by reading the finished code, not by trusting a plan:

1. **No temporary production stubs anywhere.** `grep -rn "NotSupportedException" src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceOwnershipMoveService.cs src/Apperture.Core/Apperture.Core.Application/ResourceContexts/` returns nothing. No test-only hook exists in production code except `MidTransactionHook`, which is `internal` and exists solely so the concurrency tests can be deterministic.
2. **No concurrency invariant depends on undocumented lock ordering.** All six ownership-sensitive paths acquire `IResourceOwnershipMutationGate` as the first statement after opening their transaction, before any ownership-sensitive read. Verify by reading all six, not by grepping for the call.
3. **Preview and move share one validator**, and the move's re-read happens inside the gate.
4. **Historical ownership is never rewritten** — the only writes to `ResourceContextId` in the whole epic go through the three narrow `SetResourceContextIdAsync` methods, and `UpdateAsync`/`UpdateDefinitionAsync` still carry their "deliberately does not rewrite ResourceContextId" comments (updated to name the new path).
5. **The referential-lifecycle decisions match the real dependency graph** — in particular the subscription blocker's stated rationale is the independent nullable `IntegrationId`, not "independently meaningful resource."
5a. **No blocker introduced by CTX-3B has an impossible remediation path.** Walk every blocker in the epic and name the operator operation that clears it. The full audit, performed during planning:

| Blocker | Remediation | Status |
|---|---|---|
| `target_not_found` / `already_in_target` | choose a different target | ✅ |
| `target_archived` | unarchive the target, or choose another | ✅ (CTX-3A unarchive) |
| `binding_connection_unreachable` | move the Connection, or delete the binding | ✅ (`DELETE` binding exists) |
| `binding_integration_unreachable` | move the Integration, or delete the binding | ✅ |
| `unowned_binding_requires_shared` | keep the Connection in Shared, or delete the platform-level binding | ✅ |
| `schedule_attached_to_integration` | move the Integration instead — which the message says | ✅ |
| `binding_cross_context` (readiness) | unbind, or move either side — which the message says | ✅ (Plan 3) |
| Integration delete: bindings | delete the binding | ✅ |
| Integration delete: NotificationDefinitions | `DELETE api/notification-definitions/{id}` | ✅ **added by Plan 6a Task 2** |
| Integration delete: NotificationSubscriptions | existing subscription removal | ✅ |
| Integration delete: IntegrationAttentionRoutings | `DELETE api/integrations/{id}/attention-routing` | ✅ **added by Plan 6a Task 3** |
| Application archive: live resources | retire/disable, **or move them out** | ✅ (CTX-3B is what adds the move option) |
| **`schedule_context_inconsistent`** | none | ⚠️ **known, gated** — see below |
| **Integration delete: ScheduleDefinitions** | none | ⚠️ **known, pre-existing** — see below |

Two blockers have no remediation operation, both deliberately and both recorded:

- **`schedule_context_inconsistent`** is unrepairable by design — a move must never silently auto-repair ownership drift, and no schedule-context repair operation exists. This is handled by *preventing the state from existing* rather than by a remediation path: Plan 2 Task 1 Step 0's preflight must return zero drifted rows before movement ships (product decision P3), and if it does not, implementation stops and the rows are inspected. Confirm in this closure that the preflight ran and returned zero.
- **`ScheduleDefinitions` blocking Integration delete** is pre-existing CTX-1 behavior that CTX-3B did not introduce and deliberately did not widen into. Recorded as named debt in §7.

No other blocker in the epic lacks a path.
6. **No bUnit assumption remains** anywhere in the plans or the tests.
7. **No Context/Application terminology drift** in operator-visible copy. `MovementUiSourceTests` asserts it for five razor files; check any toast, exception message or help text those tests do not cover.
8. **No speculative global disabling of test parallelism** was introduced.

---

### Task 3: Write the closure report

**Files:**
- Create: `docs/01-architecture/transitions/resource-contexts/ctx3b-closure.md`

- [ ] **Step 1: Write it**

Required content:

```markdown
# CTX-3B Closure — Resource Movement

Date: <actual date>
Status: <state it honestly>
Branch: `resource-contexts`
Design basis: [`ctx3-design-gate.md`](./ctx3-design-gate.md), building on [`ctx3a-closure.md`](./ctx3a-closure.md)

## 1. Executive result

## 2. What CTX-3B delivered

- Explicit `ownership/move-preview` / `ownership/move` endpoints for Integration, Connection and
  Schedule (§8.2), backed by one shared validator per family so preview and move cannot drift.
- **One ownership mutation gate.** A single transaction-owned SQL Server application lock
  (`apperture:resource-ownership`) acquired first by all six ownership-sensitive mutation paths:
  Integration move, Connection move, standalone Schedule move, binding creation, attached-Schedule
  creation, and Application archive. Row-level `UPDLOCK, ROWLOCK` is retained on mutated rows as defense
  in depth, but correctness does not depend on cross-entity lock ordering.
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

[State the model plainly, and state why single-row locking was rejected: it leaves Integration move and
Connection move entirely unserialized because they never touch the same row, and the naive repair
(locking counterparties in each path) produces an ABBA deadlock between the two moves. Name the six
participating paths, the bounded timeout, the `ownership_operation_busy` surface, and the fact that the
gate is database-scoped and deliberately serializes these low-frequency operator mutations.]

## 4. Statements this closure makes

- **Resource movement is complete.**
- **CTX-3A and CTX-3B together complete the initial Application-workspace rollout.** Per design gate §15
  D1, a merged CTX-3A with no CTX-3B was an internal milestone, not a release state; that milestone is
  now closed out.
- **Historical CTX-2 provenance remains immutable.** No move alters any CTX-2 historical-snapshot table;
  `ResourceMoveHistoricalImmutabilityTests` asserts this directly against the production move methods,
  on both the successful and the blocked path. [Only make this claim if that suite exists and is green.]
- **Cross-context references are transactionally protected.** The move/bind, move/move, move/attach and
  move/archive races are closed by the ownership mutation gate, proven by deterministic SQL Server
  interleaving tests, and backstopped by `binding_cross_context` readiness for any path that is not the
  supported API.
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

[Real numbers from Task 1. Include a dedicated row per SQL Server test class with executed-vs-skipped,
because a soft-skipped green suite is not evidence. Include the two preflight results.]

## 6. Product decisions recorded

- **P1 — one ownership mutation gate** rather than ordered row locks.
- **P2 — `integration.deleted` audit**, narrow by design; broader Integration lifecycle audit deferred.
- **P3 — schedule ownership drift**: preflight result and what followed from it.

## 7. Known deferred work and named debt

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
- Connection true-delete eligibility, Connection retirement cleanup, Schedule retired state, Schedule
  delete eligibility, and broader historical cleanup semantics.
- Context-scoped IAM; Business Data architecture; Services / Resource Catalog; dashboard panels.

## 8. Acceptance criteria

[The Task 2 table, with a real status per row.]

---

*Historical note: this closure does not revise `ctx3-design-gate.md` or any prior plan document — it is a
new report referencing them, per the design gate's own instruction that review history is preserved, not
rewritten to look obvious in hindsight.*
```

- [ ] **Step 2: Commit**

```bash
git add docs/01-architecture/transitions/resource-contexts/ctx3b-closure.md
git commit -m "CTX-3B: write closure report"
```

---

## Out of scope for this plan

- Any production code change. If validation finds a gap, reopen the owning plan.
- Revising the design gate or any earlier plan or closure document.

## Validation for this plan

- Every number in the closure came from a run performed in the closure session.
- Every SQL Server test class in Task 1 Step 2 is recorded as executed, with counts.
- Both preflight results are recorded.
- Browser smoke coverage is stated honestly, including anything not performed.
