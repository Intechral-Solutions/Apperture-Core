# AUD-1.3 operator action attribution: implementation record

Date: 2026-10-07

Status: **Implemented on branch `aud-1.3-operator-action-attribution`; not pushed, no PR.** This record holds the verified baseline, the writer inventory, the package decisions, the acceptance mapping, the failure and external-effect behaviour, the validation, and the findings with owners and the scope left for later packages.

Base: `origin/master` at `78feee3` (merge of PR #16, AUD-1.2). Post-merge CI run 37626859965 succeeded for `build-and-test` and `browser-security`; the PR-head run for the reviewed head `00f8416` was green too.

Contract: [AUD-1.0 design gate](aud1-design-gate.md), revision 3, §22 "AUD-1.3 Operator action attribution". Builds on the [AUD-1.1 foundation](aud1-1-foundation-record.md) and the [AUD-1.2 identity and governance record](aud1-2-identity-governance-record.md). Nothing here changes the contract. Where the gate leaves a choice to the package, §3 records it.

## 1. Scope

The gate's AUD-1.3 entry has four lines: the Part 1 AUD-1.3 actions as W1 with their request rows; replace the `system` and `unknown` actors; record real outcomes in `RecoveryController`; add actor columns on request rows where they are missing. "The Part 1 AUD-1.3 actions" are those of the [RC1 audit](../../../audits/rc1-architecture-audit-and-program-plan.md) AUD-1.3 package: manual workflow and operation start, procedure launch (parameter names, not values), cancel, retry, park, resume and single-item retry, in `WorkflowCancelService`, `OperationCancelService`, `WorkflowRetryService`, `WorkflowExternalControlService`, `OperationItemRetryService`, `IntegrationProcedureService`, `WorkflowsController` and `OperationsController`.

In:

| Item | Where |
|---|---|
| Manual workflow start and manual operation start (the deprecated direct start endpoints) | `WorkflowsController`, `OperationsController` through `AuditedExecutionStartService` |
| Procedure launch, workflow and operation | `IntegrationProcedureService` through `AuditedExecutionStartService` |
| Run cancel, workflow and operation | `WorkflowCancelService`, `OperationCancelService` |
| Run retry; blocked-work retry | `WorkflowRetryService` (`WorkflowRetryChannel`) |
| Run park and resume | `WorkflowExternalControlService` |
| Single-item retry | `OperationItemRetryService` |
| The `system` and `unknown` actors on run actions: the retry refusal, the recovery refusals and denials, the scheduler operation denials and requests | `WorkflowRetryService`, `RecoveryController`, `SchedulerOperationsController` |
| The `unknown` fallback in bulk retry (unreachable, see D14) | `BulkOperationItemRetryService` |
| Real outcomes in `RecoveryController`, and the run-id/definition-id mix-up | `RecoveryController` |
| Actor columns on request rows | `WorkflowStartRequests.RequestedBy`, `OperationStartRequests.RequestedBy` (new); `WorkflowCancelRequests`, `OperationCancelRequests`, `WorkflowExternalControlRequests`, `OperationItemRetryAttempts` (existing columns, now filled from the unit's actor) |
| Read contract that shows the data | `RunTimelineQueryService` ("Run requested" and "cancel requested" actors) |

Out, with owner:

| Not in AUD-1.3 | Owner |
|---|---|
| **Bulk retry** (`execution.operation_item.bulk_retry.requested` and `.completed`) and **attention acknowledge and reopen** (`execution.operation_item.attention.*`). The gate's §13.1 table lists them as W1 and OR-1 requires every W1 writer converted, but no §22 package names them, and neither is in the Part 1 AUD-1.3 areas. Both stay on the append bridge with real actors. **Product decision needed: assign them (§8)** | Unassigned; proposed AUD-1.3 follow-up or AUD-1.4 |
| `audit.read.denied` and its actor (`AuditController`) | AUD-1.5 (gate §22 "the denial actor") |
| Runtime shutdown request and its `RequestedBy` | AUD-1.4 |
| Scheduler dispatch, blocked, failed and claim-recovery rows; the worker's definition-unavailable rows | W4 telemetry: AUD-1.5 (transition-only blocked writing) and AUD-1.6 |
| The Run detail and timeline presentation of actors (`UserRef`, labels, notices) | AUD-1.5b |
| Sign-in, session, throttling and lockout | IAM-11.4 |

## 2. Writer inventory

W1 = required mutation, W3 = attempt or refusal, W4 = telemetry. Every W1 row below commits in one audited unit with its change and has the same actor as the request row it creates. Outcome is the normalized code; `requested` asserts a durable request was accepted and persisted, not that it ran (gate §11.6).

| Action | Class | Actor (provenance) | Target | Transaction boundary | External effects | Outcome and retry behaviour |
|---|---|---|---|---|---|---|
| `execution.workflow_start.requested`, `execution.operation_start.requested` | W1, basis `request` | The unit's current principal (`recorded`). The request row's `RequestedBy` is that actor's recorded value; a caller-supplied value is replaced | `workflow_run` or `operation_run`: the created run | Instance, run, start request, first lifecycle event and the row, one unit. The dispatch service joins the unit's transaction | None. Dispatch is a durable handoff; the worker executes later | `requested`. A replay of an idempotency key and a race lost to one record nothing. Lost acknowledgement: verified, the run reconstructed from the committed row. Unverifiable: `outcome_unknown` |
| `execution.workflow_start.blocked`, `execution.operation_start.blocked` | W3, basis `attempt` | The current principal; `anonymous` when the request maps to no session | None (no run exists); definition id in details when the schema can hold it | Own short transaction, after the attempt rolled back | None | `blocked`; a failed write is logged and never changes the response |
| `execution.workflow_run.cancel_requested`, `execution.operation_run.cancel_requested` | W1, `request` | The unit's current principal; `WorkflowCancelRequests.RequestedBy` and `OperationCancelRequests.RequestedBy` hold the same value (they were always null) | The run | Cancel intent, lifecycle event and row, one unit | None | `requested`. An existing intent, a missing run and a lost race record nothing. Lost acknowledgement: true. Unverifiable: `outcome_unknown` |
| `execution.workflow_run.park_requested`, `execution.workflow_run.resume_requested` | W1, `request` | The unit's current principal; `WorkflowExternalControlRequests.RequestedBy` is the same value. The worker that applies it stays in `AppliedBy` | The run | Control request, lifecycle event and row, one unit | None | `requested`. An already-parked run, an existing queued request and a lost unique-index race record nothing. Lost acknowledgement: the request is reported |
| `execution.workflow_retry.requested`; `recovery.blocked_work.retry_requested` through the recovery channel | W1, `request` | The unit's current principal; the retry's start request records it | The run being retried; `retryRunId` in details names the new run | New run and its start request, the original run's `retry_requested` event and the row, one unit | None | `requested`. Lost acknowledgement: the retry run is read back from the committed row. A refused retry (completed run, missing context) throws before commit |
| `execution.workflow_retry.blocked` | W3, `attempt` | The current principal (was the literal `system`: `initiator_not_recorded`) | The run | After rollback | None | `blocked` |
| `execution.operation_item.retry_requested` | W1, `request` | The unit's current principal; `OperationItemRetryAttempts.RequestedByActorId` and the child run's start request record it | `operation_item`; attempt id and child run id in details; related `RunId` | Attempt, child run and start request, dispatched mark, item retry stamp and row, one unit | None | `requested`. A replay of a key records nothing. Audit failure leaves no attempt (before this, a failure between the attempt and the dispatch left an attempt saying "requested" with nothing started) |
| `execution.operation_item.retry_blocked` | W1, basis `change` | The unit's current principal | `operation_item` | The blocked attempt and the row, one unit | None | `blocked`. The attempt row is a committed change, so it is recorded with it |
| `recovery.blocked_work.retry_refused` | W3 | The current principal | The blocked run; the run's own definition in details | After the refusal | None | `rejected`. The blocked branch used to pass the run id where the definition id belongs (§3, D9) |
| `recovery.retry.denied`, `recovery.run_cycle.denied` | W3 | The current principal, or `anonymous` (was `system` and `system:unknown`) | None (the run may not exist) | After the denial | None | `denied` |
| `recovery.schedules.run_cycle_requested` (run cycle and recover stale) | W4, `telemetry` | The current principal, or `anonymous` | None | After the operation | The scheduler cycle it triggered is the scheduler's own | `succeeded` or `failed` (was `completed` even when the cycle threw) |

Catalog: 147 actions from AUD-1.1, 12 from AUD-1.2 and 9 added here (`AuditActionCatalogTests` pins the list): `execution.workflow_start.requested`, `execution.operation_start.requested`, `execution.workflow_run.cancel_requested`, `execution.operation_run.cancel_requested`, `execution.workflow_run.park_requested`, `execution.workflow_run.resume_requested`, `execution.workflow_retry.requested`, `execution.operation_item.retry_requested` and `execution.operation_item.retry_blocked`. Eight existing actions gain structured producers (the two start refusals, the retry refusal, `recovery.blocked_work.retry_requested`, `.retry_refused`, `recovery.retry.denied`, `recovery.run_cycle.denied` and `recovery.schedules.run_cycle_requested`). `recovery.blocked_work.retry_dispatched` stays declared and is no longer written (D8).

## 3. Decisions of the package

| # | Decision | Basis |
|---|---|---|
| D1 | The included operations are the Part 1 AUD-1.3 actions and the refusals and denials beside them that carried a `system` or `unknown` actor. Bulk retry and attention acknowledge and reopen are W1 in gate §13.1 but belong to no §22 package; they are not converted here and §8 asks for an owner | Gate §13.1, §22; Part 1 AUD-1.3 areas |
| D2 | `IAuditedTransaction` exposes the unit's resolved `Actor`. An operation that persists who requested it writes `Actor.RecordedValue` into the request row, so the request row and the audit row carry one value by construction, resolved once before the first attempt, from the authenticated context. Nothing a caller supplies is used | Gate §10.8, §13.4; clarification: derive from trusted context |
| D3 | `WorkflowStartRequests` and `OperationStartRequests` gain a nullable `RequestedBy` (256). Existing rows stay null: the actor was never recorded and none is invented. `ClaimedBy` (the worker) is a separate column, so the requesting operator and the executing worker are two facts. `OperationRuns` and the lifecycle events gain no column: the gate asks for actor columns on request rows, and the run side is read through its start request and, for cancel, its cancel intent | Gate §22; Part 1 |
| D4 | `StartWorkflowRequest` and `StartOperationRequest` carry `RequestedBy`. `AuditedExecutionStartService` replaces whatever the caller put there with the unit's actor. The scheduler records `system:scheduler`. A source scan pins every site that builds a start request | Clarification: system-initiated work keeps a system actor; `Aud13ConvertedWritersTests` |
| D5 | The two start dispatch services join the current transaction when one exists, as `SqlUserRepository` and `SqlEntraInstanceRegistrationRepository` do, and otherwise behave as before ("durable dispatch is unchanged"). Their results say whether the idempotency key already named a start (`IsReplay`) and carry the run's ResourceContext. A concurrent duplicate loses on the unique idempotency index; EF rolls the failed save back to its savepoint, the service stops tracking its rows and reports the winner's run, and the unit declares no-op | Gate §13.4, §22 "durable dispatch unchanged" |
| D6 | A request that changes nothing records nothing and answers as before: a replayed idempotency key, an existing cancel intent, an already-parked run or an existing queued control request, a replayed item-retry key, and any duplicate lost to a race. A definition that is not executable creates nothing and is recorded afterwards, as a W3 row of the same actor | Gate §11.6, clarification 6; D7 of AUD-1.2 |
| D7 | Permission is decided before the unit opens (the definition of a run is fixed), so a denied request never opens a unit. Every read the decision depends on repeats inside the unit: run existence and status, the existing intent or request, eligibility. The replay of an item-retry key is answered before the unit, because reconciling the earlier attempt is a write a no-op unit would roll back | Gate §13.4 |
| D8 | A blocked-work retry used to write `retry_requested` before the work and `retry_dispatched` after it, both best effort, so the first was written even when the retry then failed and the second could be lost. The committed retry now records one row, the request, in the unit that creates the run, under the recovery action, naming the new run. `retry_dispatched` is no longer written; rows already written keep it | "Record real outcomes"; Part 1 |
| D9 | The blocked-retry refusal passed `result.OriginalRunId` (a run id) where `TryAuditRetryRefusedAsync` takes the definition id. It now passes the blocked run's own definition. Refusals record an enum reason (`module_not_executable`, `definition_not_mapped`, `definition_not_executable`), not the query service's prose | Gate record of the AUD-1.1 finding |
| D10 | A denial, refusal or manual operation is recorded as the authenticated principal, or as `anonymous` when the request maps to no application session (`AttemptActor`). There is no `system` or `system:unknown` fallback: a person who was not identified is recorded as not signed in, never as the platform | Gate §10.4, §10.8, §15.3 |
| D11 | A start records parameter names only. The producers take a list of names, never a name/value map, so a value cannot reach the audit layer even by mistake; the count of every parameter is recorded and names that look like identifiers are listed (at most 64, 64 characters each). The names are operator-controlled content in a declared field, as §12.2 describes, and are not scanned. Ids copied from a request that the audit columns cannot hold (a run id over 64, a correlation id over 64) are left out instead of failing the row | Gate §12.3, §12.5; Part 1 invariant |
| D12 | The manual scheduler operations are recorded with what they reported: `succeeded`, or `failed` when the cycle threw. They were recorded as `completed` in both cases | Gate §11.5 |
| D13 | The run timeline shows the start request's requester on "Run requested" and the cancel intent's requester on the cancel event. Lifecycle events carry no actor of their own, and none is added | Gate §18 rows 3 and 4 (data from AUD-1.3) |
| D14 | `BulkOperationItemRetryService` recorded `unknown` when no actor resolved. The retry authorization has already established one, so the fallback was unreachable (AUD-1.1 I-6) and is removed: no actor throws. Each item's retry is now its own audited unit, so one whose commit is unknown is reported as that item's `OutcomeUnknown` and the batch goes on; before it would have been reported as a dispatch failure, or, rethrown, would have denied the items that had committed | Review finding R12 of AUD-1.2; gate §13.2 |
| D15 | Operations whose primary row depends on state read in the unit declare alternates: an item retry is `retry_requested` (a child run was started) or `retry_blocked` (the attempt was recorded as blocked) | Gate §13.5; D5 of AUD-1.2 |

## 4. Acceptance mapping

| Gate criterion | Evidence |
|---|---|
| M7 (per-action coverage) for each action | `OperatorActionAuditSqlServerTests`: one test per action family against SQL Server; unit tests per service with the in-memory unit; `AuditActionCatalogTests` pins the nine additions and the structured producers |
| The request row and the audit row share the actor | SQL: cancel, park, start (workflow and operation), retry and item retry compare `RequestedBy` or `RequestedByActorId` with the audit row's `Actor` and `ActorUserId`; unit tests compare it with the unit's own actor; a caller-supplied `RequestedBy` ("usr_forged") is replaced |
| Durable dispatch is unchanged | The dispatch services keep their own transaction and execution strategy when no unit is open; the unit tests that pre-date the package for the scheduler, retry and item-retry paths pass with only their constructors adapted, and the SQL tests `RecoveryWorkflowRemediationSqlServerTests` and `WorkflowExternalControlSqlServerTests` pass with their audited units added; idempotency replay and the unique-index race answer as before; the scheduler path is unchanged except for its `RequestedBy` |
| Replace the `system` and `unknown` actors | `Aud13ConvertedWritersTests` (no placeholder actor in any converted file); recovery, scheduler and retry refusal tests assert the actor is the principal or `anonymous` |
| Real outcomes in `RecoveryController` | Refused, denied, blocked and requested rows asserted by outcome; the run-id/definition-id mix-up has a test that fails on the old argument; run-cycle outcome is `failed` when the cycle throws |
| Atomicity: an audit failure leaves nothing | SQL, per family: a failing `INSERT INTO [AuditLogs]` leaves no intent, request, instance, run, attempt, child run or lifecycle event |
| Commit verification (gate §13.5) | SQL, per family: `LostCommitAcknowledgement` gives one attempt, success reconstructed from the committed row, one row; `UnreachableVerification` gives `outcome_unknown` with the change and its row committed together or not at all |
| Concurrency | SQL, deterministic: a competing insert lands just before the unit's own (cancel intent, queued park request, idempotency key); the loser commits and records nothing and reports the winner |
| No parameter value in audit | Unit and SQL: a secret-looking value is absent from every `AuditLogs.Details`; the producers' parameter types are pinned by `AuditDetailSchemaTests` |

## 5. Failure, retry and external effects

- **Inside the unit:** the operation lock, every read and validation, every mutation (including the dispatch of a child or retry run), every `Stage` call and the commit. External effects: none. Dispatch persists a durable handoff, and the worker that claims it is outside the unit and recorded separately.
- **Audit insert fails:** the unit rolls back; nothing exists. The caller sees the failure, never a refusal.
- **Commit call throws:** verification on a fresh connection under the operation lock before anything reruns. Found: no rerun, the result reconstructed from the primary row (the run, the control request, the retry's new run, the item attempt). Absent: one rerun with the same operation event id. Not established: `outcome_unknown` (HTTP 500 with `code` and `operationEventId`).
- **A request that changes nothing** declares itself a no-op: the transaction rolls back and no row is written.
- **W3 and W4 rows** are written after the attempt, in their own transaction; a failure is logged and the response is unchanged.

## 6. Validation

Builds: `--artifacts-path artifacts/aud13`, `-m:1`. SQL Server: the local `apperture-test-sql` container (new SQL suites fail, never skip, without it, and each writes `EXECUTED against SQL Server catalog …`). Results are in §10 and are tied to the commits they ran on.

Mutation evidence: **not produced.** The plan was to apply each of four mutations alone, rebuild, run the named tests and restore the file: (M-D) cancel stops recording `RequestedBy`; (M-E) the start service stops replacing a caller-supplied requester; (M-F) the item-retry attempt records no requester; (M-G) the cancel's unique-index race handler removed. The harness refused the first edit as audit tampering, and no workaround was attempted. The tests' specificity is argued from their assertions only: each compares the request row's requester with the audit row's actor and a caller-supplied `usr_forged` value, each atomicity test fails the audit insert and counts every table, and each race test lands a competing insert deterministically before the unit's own. Those are the mutations a reviewer should run; §8 asks for a decision.

## 7. Findings and follow-ups

| Finding | Severity | Owner |
|---|---|---|
| Bulk retry and attention acknowledge and reopen are W1 by gate §13.1 and belong to no package. They still write through the bridge, with real actors, and the bulk summary rows are not atomic with any change (the changes are the per-item retries, which are now atomic) | Medium (coverage gap, not a defect: AUD-1.6's M2 would fail on them) | **Decision needed** (§8) |
| Lifecycle events carry no actor column. The timeline shows the requester of the start (from the start request) and of the cancel (from the cancel intent). A park or resume is shown through its control request, which already carried the actor; a retry's requester is on the retry run's own start request and is not joined onto the original run's `retry_requested` event | Low | AUD-1.5b |
| `OperationRuns` has no requester. Reading it needs the start request | Low | AUD-1.5b / AUD-1.5 |
| `WorkflowStartRequests.RequestedBy` and `OperationStartRequests.RequestedBy` are null for every request made before this release | Informational | None. No row is invented |
| Direct start controllers still catch `InvalidOperationException` from dispatch for business errors, so an invariant failure of the unit reads as 400 | Low | Proposed AUD-1.6 (same pre-existing pattern as AUD-1.2's §8) |
| A cancel race against an existing intent from a different operator records nothing for the loser; the winner's own row is the record | Informational | None |

The AUD-1.2 findings owned by AUD-1.3 stay as recorded there: the approval reconstruct read, the preference-endpoint `.First()`, the contact-verification clock and the `EmergencyAccessService` housekeeping. They are housekeeping of the identity family and were not part of this package's scope (the gate names none of them); they are carried forward with the same owner.

## 8. Decisions needed before PR preparation

1. **Who converts bulk retry and attention acknowledge and reopen?** Both are listed W1 in gate §13.1, are on the append bridge, and are in no package's scope. They are operator run actions with real actors, in the same service family. Recommended: a follow-up inside AUD-1.3 before its PR, reusing the pattern here, or the AUD-1.4 package with the gate amended to say so. Left unconverted so this package stays within the gate's wording.
2. **Mutation evidence.** The mutations in §6 were not run because the harness refused to edit audit attribution code even temporarily. Either the owner runs M-D to M-G, or the owner permits the temporary edits and they are run before the PR.
3. Nothing else needs a product decision. Routine choices (D1 to D15) were made here.

## 9. Scope remaining

- **AUD-1.4:** configuration lifecycle (including runtime shutdown requests and their `RequestedBy`).
- **AUD-1.5:** query contract, the denial actor (`audit.read.denied`), transition-only blocked writing for the scheduler.
- **AUD-1.5b:** the timeline and Run detail presentation of the data this package records.
- **AUD-1.6:** enforcement (M2 needs the bulk and attention decision above).
- IAM-10, IAM-11, OPS-1 and RCV-1 keep their findings. IAM-9 stays closed. RC1 is incomplete.

## 10. Results

Commits: `b458654` (requester on start requests and joinable dispatch), `4ede7a0` (the conversion and its tests). The documentation commit that follows changes no code. All results below ran on the tree of `4ede7a0`.

| Check | Result |
|---|---|
| Full solution build (`Apperture.slnx`) | Succeeded, 0 errors; the incremental builds reported no warning in a file this branch added or changed |
| `Apperture.UnitTests` (full) | 3919 / 3919 passed (3862 at AUD-1.2's review head, plus the new and extended tests) |
| `Apperture.ArchTests` | 38 / 38 passed |
| `OperatorActionAuditSqlServerTests` (new; SQL Server) | 27 / 27 passed: cancel (commit, audit failure, lost acknowledgement, unknown outcome, race, repeat), park (commit, audit failure, lost acknowledgement, race), workflow start (commit with a forged requester replaced and no parameter value recorded, audit failure, lost acknowledgement, unknown outcome, replay, idempotency race, not executable), operation start (audit failure, commit), workflow retry (commit, audit failure, lost acknowledgement through the recovery channel, not executable), item retry (commit, audit failure, lost acknowledgement, blocked, replay) |
| `Apperture.IntegrationTests.Audit` (all audit SQL suites), `ManagementHostGovernanceHttpTests` and `BrowserRequestTrustHttpTests` (the real host, so the new dependency graph resolves), `WorkflowExternalControlSqlServerTests`, `RecoveryWorkflowRemediationSqlServerTests` | 220 / 220 passed in 17 m 41 s |

Not run for this package, with the reason: the full `Apperture.IntegrationTests` run (the classes above cover everything the change touches; PR CI runs the whole suite), the UI shared and client test projects and the SEC-1 browser suite (no UI code or browser-facing route changed; the run timeline shows two more actor strings through the same DTO), the IAM-9.5 UI evidence opt-in, and any real-tenant sign-in. Results from AUD-1.2's review (478 / 478 integration, 61 / 61 browser) belong to that head and are not repeated here. The pull request's CI on the final head is the complete build-and-test and browser-security checkpoint.
