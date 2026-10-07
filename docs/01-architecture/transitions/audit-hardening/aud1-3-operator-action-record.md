# AUD-1.3 operator action attribution: implementation record

Date: 2026-10-07

Status: **Implemented on branch `aud-1.3-operator-action-attribution`, with the completion and independent review pass of §11 applied; not pushed, no PR.** This record holds the verified baseline, the writer inventory, the package decisions, the acceptance mapping, the failure and external-effect behaviour, the validation, and the findings with owners and the scope left for later packages. §1 to §10 describe the package as first implemented, amended where §11 changed it; §11 records the completion pass, the review, its fixes and the evidence, tied to the commits it ran on.

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
| Bulk item retry (orchestration over single-item retries), assigned by the owner on 2026-10-07 (§11.2) | `BulkOperationItemRetryService` |
| Failed-item attention acknowledge and reopen, assigned by the owner on 2026-10-07 (§11.2) | `OperationItemAttentionService` |
| The `system` and `unknown` actors on run actions: the retry refusal, the recovery refusals and denials, the scheduler operation denials and requests | `WorkflowRetryService`, `RecoveryController`, `SchedulerOperationsController` |
| The `unknown` fallback in bulk retry (unreachable, see D14) | `BulkOperationItemRetryService` |
| Real outcomes in `RecoveryController`, and the run-id/definition-id mix-up | `RecoveryController` |
| Actor columns on request rows | `WorkflowStartRequests.RequestedBy`, `OperationStartRequests.RequestedBy` (new); `WorkflowCancelRequests`, `OperationCancelRequests`, `WorkflowExternalControlRequests`, `OperationItemRetryAttempts` (existing columns, now filled from the unit's actor) |
| Read contract that shows the data | `RunTimelineQueryService` ("Run requested" and "cancel requested" actors) |

Out, with owner:

| Not in AUD-1.3 | Owner |
|---|---|
| ~~Bulk retry and attention acknowledge and reopen~~: unassigned at first implementation; **assigned to AUD-1.3 by the owner on 2026-10-07 and converted (§11.2)** | AUD-1.3 |
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
| `execution.operation_item.retry_blocked` | W1, basis `change` | The unit's current principal | `operation_item` | The blocked attempt and the row, one unit | None | `blocked`. The attempt row is a committed change, so it is recorded with it. A request that loses the race for its key records nothing (§11.3, R1) |
| Bulk item retry (orchestration) | Its W1 rows are the item rows above, one per item whose retry committed a change, each naming the batch as `bulkRequestId`. The batch writes no row of its own (D16) | The unit's current principal, per item | `operation_item`, per item | One unit per item | None | Per item: `requested` or `blocked`; a skipped item records nothing; an item whose commit cannot be established is reported `OutcomeUnknown` and the batch goes on |
| `execution.operation_item.attention.acknowledged` | W1, basis `change` | The unit's current principal; `OperationItemAttentionAcknowledgements.AcknowledgedByUserId` holds the same value | `operation_run`; the acknowledged item ids in details (D17) | Every acknowledgement of the request and the row, one unit | None | `succeeded`. Changing no item records nothing. A lost race for an item's one active acknowledgement reports that item `AlreadyAcknowledged` with the winner's acknowledgement and records nothing for it. Lost acknowledgement: reconstructed from the row's item list. Unverifiable: `outcome_unknown` |
| `execution.operation_item.attention.reopened` | W1, basis `change` | The unit's current principal; `ReopenedByUserId` holds the same value | `operation_run`; the reopened item ids in details | Every reopening of the request and the row, one unit | None | `succeeded`. The update applies only to a still-active acknowledgement, so a concurrent second reopen reports `NotAcknowledged`, overwrites nothing and records nothing |
| `recovery.blocked_work.retry_refused` | W3 | The current principal | The blocked run; the run's own definition in details | After the refusal | None | `rejected`. The blocked branch used to pass the run id where the definition id belongs (§3, D9) |
| `recovery.retry.denied`, `recovery.run_cycle.denied` | W3 | The current principal, or `anonymous` (was `system` and `system:unknown`) | None (the run may not exist) | After the denial | None | `denied` |
| `recovery.schedules.run_cycle_requested` (run cycle and recover stale) | W4, `telemetry` | The current principal, or `anonymous` | None | After the operation | The scheduler cycle it triggered is the scheduler's own | `succeeded` or `failed` (was `completed` even when the cycle threw) |

Catalog: 147 actions from AUD-1.1, 12 from AUD-1.2 and 9 added here (`AuditActionCatalogTests` pins the list): `execution.workflow_start.requested`, `execution.operation_start.requested`, `execution.workflow_run.cancel_requested`, `execution.operation_run.cancel_requested`, `execution.workflow_run.park_requested`, `execution.workflow_run.resume_requested`, `execution.workflow_retry.requested`, `execution.operation_item.retry_requested` and `execution.operation_item.retry_blocked`. Eight existing actions gain structured producers (the two start refusals, the retry refusal, `recovery.blocked_work.retry_requested`, `.retry_refused`, `recovery.retry.denied`, `recovery.run_cycle.denied` and `recovery.schedules.run_cycle_requested`). `recovery.blocked_work.retry_dispatched` stays declared and is no longer written (D8). The completion pass (§11) adds no action: `execution.operation_item.attention.acknowledged` and `.reopened` gain structured producers, and `execution.operation_item.bulk_retry.requested` and `.completed` stay declared, for the rows already written, and are no longer written (D16).

## 3. Decisions of the package

| # | Decision | Basis |
|---|---|---|
| D1 | The included operations are the Part 1 AUD-1.3 actions and the refusals and denials beside them that carried a `system` or `unknown` actor. Bulk retry and attention acknowledge and reopen were W1 in gate §13.1 but belonged to no §22 package. **Amended:** the owner assigned them to AUD-1.3 on 2026-10-07 (gate §22 ownership clarification), and §11.2 converts them | Gate §13.1, §22; Part 1 AUD-1.3 areas; owner decision 2026-10-07 |
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
| D16 | **Bulk retry row cardinality.** The catalog declares `bulk_retry.requested` (basis `request`) and `.completed` (basis `confirmed_result`), both on the run. The batch persists no request of its own: its required mutations are the item retries, and each must stay its own unit so that partial results and per-item `outcome_unknown` are kept (owner decision). A batch row could therefore commit with none of them. The `requested` row was written before any work (gate §5.2 mode (d)), and the `completed` summary changes no state (§13.1: `confirmed_result` is W1 only when it changes state). So a bulk retry records one W1 row per item whose retry committed a change (`retry_requested` or `retry_blocked`), each naming the batch as `bulkRequestId`, and no batch row. The two batch actions stay declared for the rows already written, as `retry_dispatched` does (D8). The batch id is set by the bulk service, never read from a request body | Gate §5.2, §11.6, §11.7, §13.1; owner decision 2026-10-07 |
| D17 | **Attention row cardinality.** The catalog declares acknowledge and reopen on the run, basis `change`. So one request is one unit: every acknowledgement (or reopening) it makes commits with one row on the run that lists the items it changed (`operationItemIds`, an id list bounded by the 100-item request limit and the 64-character item id), the requested and skipped counts, the disposition and the operator's reason (`operator_text`, bounded at 512 by the service). Skipped items changed nothing and are only counted. A request that changes no item records nothing. Before, each acknowledgement committed alone and the row was best effort afterwards, so a failure part way left some committed with an error response | Gate §11.1, §11.7, §12.4, §13.2 |
| D18 | **A lost race is reported, not borrowed.** The acknowledgement and retry-attempt stores gain a `TryCreateAsync` that returns null when a concurrent request committed first, instead of handing back the winner's row as if this request had made it. The reopen is a conditional update on a still-active acknowledgement. The caller then declares a no-op and reports the winner's row as a replay. Duplicates are matched on the named index or key that expresses the rule (cancel intent primary key, queued control request index, start idempotency index, active acknowledgement index); an item attempt matches either of its two unique indexes, because two requests with one key also compute one attempt index, but only when an attempt with this key now exists. Any other duplicate keeps its meaning | Gate §13.5 "duplicate keys"; review R1, R2 |
| D19 | **Audit invariant failures are not refusals.** The unit's and recorder's programming errors (gate §13.4 preconditions, a missing primary row, an undeclared action, an actor that cannot be recorded, a detail schema defect) throw `AuditInvariantException`, an `InvalidOperationException` as the gate states. The run action endpoints that map `InvalidOperationException` to 400 or 409 (direct starts, run retry, park, resume, blocked-work retry) exclude it, so it reaches the host's handler as a 500 | Gate §13.4; review R4 |
| D20 | **A confirmed commit is never reported as a refusal.** When verification confirms a commit and the operation's reconstruction then fails, the unit throws `AuditCommittedResultUnavailableException` (not an `InvalidOperationException`), which the host maps to 500 with `code: committed_result_unavailable` and the operation event id: the change is applied, and the client reloads instead of repeating it | Gate §13.5 reconstruction; review R3 |
| D21 | The run timeline names a cancel requester only on the lifecycle event of that same intent (the intent's `RequestedAt` equals the event's `OccurredAt`; both are written from one timestamp). An operation run's cancel intent is removed when the run completes (`OperationStartRequestProcessor`), and a later cancel writes a new one; the earlier event then shows no requester instead of the later one | Review R5 |
| D22 | Request-row lifetime. Start requests, workflow cancel intents, control requests, item attempts and acknowledgements are never deleted. Operation cancel intents are removed when the run completes (pre-existing). The durable attribution is the audit row, which the gate keeps indefinitely (§20); the request row's requester is operational state. No persistence change is made: none is required by the gate, and keeping operation cancel intents would change the worker's cancel semantics. The timeline shows no requester on such an event (D21); AUD-1.5b can read it from the run's audit rows if the presentation needs it | Gate §20, §22; review R6 |
| D23 | A blocked-work retry whose definition is not executable is recorded once, as the recovery channel's `recovery.blocked_work.retry_refused`. `WorkflowRetryService` writes `execution.workflow_retry.blocked` only on the run channel; before, both rows were written for one refusal | Review R7 |
| D24 | The bulk retry result summary in the UI no longer counts an `OutcomeUnknown` item as "could not be dispatched": it says the item's retry could not be confirmed and to reload before retrying it. The server counts it in `FailedCount`, unchanged | Review R8 |

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

Mutation evidence at first implementation: **not produced** (superseded by §11.5, where the mutations were run with the owner's authorization). The plan was to apply each of four mutations alone, rebuild, run the named tests and restore the file: (M-D) cancel stops recording `RequestedBy`; (M-E) the start service stops replacing a caller-supplied requester; (M-F) the item-retry attempt records no requester; (M-G) the cancel's unique-index race handler removed. The harness refused the first edit as audit tampering, and no workaround was attempted. The tests' specificity is argued from their assertions only: each compares the request row's requester with the audit row's actor and a caller-supplied `usr_forged` value, each atomicity test fails the audit insert and counts every table, and each race test lands a competing insert deterministically before the unit's own. Those are the mutations a reviewer should run; §8 asks for a decision.

## 7. Findings and follow-ups

As amended by the completion pass (§11). Resolved findings are listed in §11.3.

| Finding | Severity | Owner |
|---|---|---|
| ~~Bulk retry and attention acknowledge and reopen belong to no package~~ | | **Resolved:** assigned to AUD-1.3 and converted (§11.2) |
| Lifecycle events carry no actor column. The timeline shows the requester of the start (from the start request) and of the cancel (from the cancel intent of that same event, D21). A park or resume is shown through its control request, which already carried the actor; a retry's requester is on the retry run's own start request (linked from the original run's `retry_requested` event by `RelatedRunId`) and is not joined onto that event | Low | AUD-1.5b |
| The Run detail page renders the timeline actor as raw text (`RunDetail.razor:474`, gate §18 row 3), so operators now see user ids and `system:scheduler` where the cell was empty. The page already showed raw worker and control-request ids. The data is AUD-1.3's; its presentation is gate §18's AUD-1.5b (A1.5b-6) | Low | AUD-1.5b |
| `OperationRuns` has no requester. Reading it needs the start request | Low | AUD-1.5b / AUD-1.5 |
| `WorkflowStartRequests.RequestedBy` and `OperationStartRequests.RequestedBy` are null for every request made before this release | Informational | None. No row is invented |
| ~~Direct start controllers map an invariant failure of the unit to 400~~ | | **Resolved** for every run action endpoint this package converted (D19). The identity and governance endpoints of AUD-1.2 keep their broad `InvalidOperationException` catches; an audit invariant failure there still reads as 400 or 409 (pre-existing to this package) | AUD-1.6 (enforcement) |
| An operation run's cancel intent is removed when the run completes, so its requester survives only in the audit row (D22) | Informational | None; AUD-1.5b if the timeline must show it |
| Cancelling a run that has already finished is accepted and persists a cancel intent. **Pre-existing, not an AUD-1.3 regression** (§12.1) | Low | Open finding; runtime correctness, no owner assigned |
| Two concurrent retries of one failed run (no idempotency key) create two retry runs. **Pre-existing, not an AUD-1.3 regression** (§12.1) | Low | Open finding; runtime correctness, no owner assigned |
| Two item retries with different keys that race for one attempt index: the loser fails with a 500. **Pre-existing, not an AUD-1.3 regression** (§12.1) | Low | Open finding; runtime correctness, no owner assigned |
| An item retry does not check the dispatch result's `IsReplay`. A replay needs a start request with the item's dispatch key but no attempt with the item's key, which only partial pre-AUD-1.3 state could produce | Low | None now; reopen if such state is found |
| The unit of work does not refuse pending changes already tracked on the scoped context when it opens, so an earlier unsaved change in the same request would commit inside the unit (AUD-1.1 design; no converted path leaves one) | Low | AUD-1.6 (M5 audited-entity interceptor) |
| Parameter names on the direct start endpoints are the caller's own keys and are recorded as operator-supplied identifiers (bounded, identifier-shaped, at most 64). The gate records names, never values (§12.3) | Informational | None |
| A cancel race against an existing intent from a different operator records nothing for the loser; the winner's own row is the record | Informational | None |

The AUD-1.2 findings owned by AUD-1.3 stay as recorded there: the approval reconstruct read, the preference-endpoint `.First()`, the contact-verification clock and the `EmergencyAccessService` housekeeping. They are housekeeping of the identity family and were not part of this package's scope (the gate names none of them); they are carried forward with the same owner.

## 8. Decisions needed before PR preparation

Both questions first raised here were answered by the owner on 2026-10-07:

1. **Bulk retry and attention acknowledge and reopen** were assigned to AUD-1.3, with a narrow ownership clarification in the gate (§22) that leaves their requirements unchanged. Converted in §11.2; row cardinality in D16 and D17.
2. **Mutation evidence.** Temporary source mutations were authorized, provided they are reverted, never committed and touch no live audit data, and without overriding tool permission controls. Results in §11.5.

No product decision is open. Two package choices the owner may want to confirm at review: D16 (a bulk retry records its item rows and no batch row) and D22 (no persistence change for removed operation cancel intents).

## 9. Scope remaining

- **AUD-1.4:** configuration lifecycle (including runtime shutdown requests and their `RequestedBy`).
- **AUD-1.5:** query contract, the denial actor (`audit.read.denied`), transition-only blocked writing for the scheduler.
- **AUD-1.5b:** the timeline and Run detail presentation of the data this package records.
- **AUD-1.6:** enforcement. With bulk retry and attention converted, no AUD-1.3 W1 action is written through the append bridge.
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

## 11. Completion and independent review (2026-10-07)

### 11.1 Baseline and commits

Verified before editing: branch `aud-1.3-operator-action-attribution` at `fdef8fa`, base `origin/master` at `78feee3` (also the merge base), working tree clean apart from the untracked `bash.exe.stackdump`, which this pass leaves alone. Read: the RC1 program gate, the AUD-1.0 design gate (revision 3) and this record.

**`4ede7a0` to `fdef8fa`.** Documentation only: this record (149 lines added) and one line in the audit-hardening README. No code changed between the tested commit and the head, so §10's results stand for `fdef8fa`.

**"Vitest counts".** The word appears nowhere in the repository: not in this record, the gate, the API contracts, the commit messages or any project file. No Vitest suite exists (the only JavaScript test, `toast-gesture.test.mjs`, uses Node's built-in runner and is unrelated). The wording was in a handback summary, not in a repository document, so there is nothing to correct here; no Vitest result is or was claimed.

Commits of this pass:

| Commit | Content |
|---|---|
| `fa26897` | The bulk retry and attention conversions, the review fixes and their tests (code and tests only) |
| The documentation commit that follows | This section, the amendments to §1 to §9, the gate's §22 ownership clarification and §27 row, and `docs/04-api/contracts.md`. It changes no code |

### 11.2 Newly assigned conversions

The owner assigned bulk item retry and failed-item attention acknowledge and reopen to AUD-1.3 (gate §22 ownership clarification; requirements unchanged).

- **Acknowledge and reopen** (`OperationItemAttentionService`): one audited unit per request; every acknowledgement or reopening and one row on the run commit together (D17). The acknowledging user is the unit's actor. Permission and request validation stay before the unit; every per-item read (item, group, status, the active acknowledgement) repeats inside it. A request that changes no item declares a no-op. Lost acknowledgement: the result is rebuilt from the row's item list. The stores report a lost race instead of returning the winner (D18), and a reopen is a conditional update.
- **Bulk retry** (`BulkOperationItemRetryService`): orchestration only. Each item is the existing single-item unit, now naming the batch (`bulkRequestId`, set by the service, never by a body). The best-effort `bulk_retry.requested` row written before any work and the `bulk_retry.completed` summary are removed (D16). Per-item results, partial success and per-item `OutcomeUnknown` are unchanged; a SQL test shows an item whose unit fails leaves the committed item and its row in place.

Converted-writer inventory after this pass: start (workflow and operation, direct and procedure), cancel (workflow and operation), park, resume, workflow retry and blocked-work retry, single-item retry (requested and blocked), bulk item retry (through the single-item unit), attention acknowledge and reopen; W3 refusals and denials beside them; and the scheduler operations (W4). `Aud13ConvertedWritersTests` pins that none of the converted files uses the append bridge or names a placeholder actor, and lists every start-request construction site.

### 11.3 Review findings and resolutions

The review read the source against the gate rather than the earlier summary, with two independent read-only reviewers (atomicity and races; actor, target, persistence and timeline) whose findings were checked against the code before acting.

| # | Finding | Branch-introduced? | Resolution |
|---|---|---|---|
| R1 | **Blocked item retry race.** Two requests with one key: the loser's insert failed, the store returned the winner's attempt, and the guard (`status != Blocked`) could not tell a blocked winner from its own attempt, so the loser staged `retry_blocked` for the winner's attempt under its own name | Yes (before, only a response message) | `TryCreateAsync` reports the lost race; the loser declares a no-op and reports the winner as a replay (D18). SQL tests for the blocked and requested paths |
| R2 | Duplicate detection matched any 2601 or 2627 (cancel, park, the start dispatch services) | Yes for the joined paths | Matched on the named key or index (D18); cancel re-reads that the winning intent exists before answering `true`. The dispatch services also detach their losing rows on their own-transaction path, so nothing stale stays tracked |
| R3 | A reconstruction that throws after a confirmed commit would surface as a 400 or 409 (an `InvalidOperationException`) or a generic 500 without the operation id | Yes | `AuditCommittedResultUnavailableException`, mapped to 500 `committed_result_unavailable` with the operation event id (D20). SQL test |
| R4 | The unit's own invariant failures are `InvalidOperationException`s, which run retry, park, resume, blocked-work retry and the direct starts mapped to 409 or 400 | Yes (these paths are new to the unit) | `AuditInvariantException`, excluded by those endpoints (D19). The identity and governance endpoints of AUD-1.2 keep their broad catches (§7, owner AUD-1.6) |
| R5 | The operation-run timeline attributed every `cancel_requested` event to whichever intent existed, so after an intent was removed and another written, an earlier event showed the later requester | Yes | Matched by timestamp (D21). Unit test |
| R6 | Operation cancel intents are deleted when a run completes, so the request row's requester does not last | No (deletion pre-exists); the question is whether it is a required persistence change | Not required: the audit row is the durable record (gate §20); no persistence change, recorded as D22 for the owner to confirm |
| R7 | A blocked-work retry refused for a non-executable definition wrote two refusal rows | No (both rows pre-existed; this package gave them real actors) | Recorded once, by the recovery channel (D23) |
| R8 | The bulk retry summary counted `OutcomeUnknown` items as "could not be dispatched" | Yes (the category is new) | Summary says the retry could not be confirmed (D24). UI shared test |
| R9 | Bulk retry and attention on the append bridge | n/a | Converted (§11.2) |

Confirmed correct, with evidence in source and tests: the dispatch services join the unit's transaction and never commit; every losing path declares a no-op, so the unit rolls back and clears the change tracker; EF Core 10 takes a savepoint around each save inside the unit's transaction and a unique violation ends only the statement (no `XACT_ABORT`, no MARS), so the unit's transaction and its operation lock survive a lost race; the operation event id is generated once before the execution strategy; verification runs before any rerun; `outcome_unknown` reaches the host's handler on every converted endpoint; requesters come only from `transaction.Actor.RecordedValue` (a caller value is overwritten; the scheduler records `system:scheduler`; the worker's `ClaimedBy` and `AppliedBy` stay separate); no placeholder actor remains; historical `RequestedBy` stays null (the migration adds two nullable columns and backfills nothing); targets match the catalog, and the targetless refusals and denials (start blocked, recovery retry denied, scheduler operations) are rows about something that may not exist; starts record parameter names only.

### 11.4 Timeline and read contract

Exactly what changed for readers on this branch:

- `RunTimelineQueryService`: "Run requested" carries the start request's `RequestedBy`; a `cancel_requested` lifecycle event carries the requester of the intent written with it (D21). No other event gains an actor. No DTO, client model, API route or Razor component changed. `RunDetail.razor:474` renders the actor string as it always has, so operators now see user ids and `system:scheduler` there (§7; presentation is AUD-1.5b).
- Bulk retry: the `OutcomeUnknown` item category (first implementation) and the UI summary's wording for it (`OperationItemizationUiHelpers.FormatBulkRetryResultSummary`, this pass).
- Responses: 500 `outcome_unknown` on the converted actions (first implementation), 500 `committed_result_unavailable` (this pass), and a 500 instead of 400 or 409 for an audit invariant failure on the run action endpoints. Documented in `docs/04-api/contracts.md`.

No broader timeline redesign and no `UserRef` adoption were started.

### 11.5 Mutation evidence

Authorized by the owner on 2026-10-07: temporary source edits, each applied alone on top of `fa26897`, rebuilt, run against the named tests, then restored with `git checkout`; none was committed and none touched live audit data (every SQL test runs in its own scratch catalog). This time the tool accepted each edit; nothing was worked around. After the fourth, `git diff fa26897 -- src tests` was empty.

| Mutation | Edit | Intended assertion that failed |
|---|---|---|
| M-D: cancel stops recording its requester | `WorkflowCancelService`: `RequestedBy = null` | SQL `A_cancel_commits_its_intent_its_event_and_its_row_with_one_actor`: expected `WorkflowCancelRequests.RequestedBy` to be the user, found null. Unit `RequestCancelAsync_PersistsCancelIntent_Idempotently`: expected `usr_unit_test_actor`, found null |
| M-E: a start stops replacing a forged requester | `AuditedExecutionStartService`: `StartAsync(request)` instead of `request with { RequestedBy = … }` | SQL `A_workflow_start_commits_the_run_its_request_and_its_row_with_the_actor_the_unit_resolved`: expected `WorkflowStartRequests.RequestedBy` to be the user, found `usr_forged` |
| M-F: an item retry omits its requester | `OperationItemRetryService`: `RequestedByActorId: null` on the requested attempt | SQL `An_item_retry_commits_…_with_one_actor`: expected `RequestedByActorId` to be the user, found null. SQL `A_bulk_retry_commits_each_item_…`: expected 2 attempts by the user, found 0 |
| M-G: the cancel race handler removed | `WorkflowCancelService`: the duplicate filter made never to match | SQL `A_cancel_that_loses_a_race_for_the_one_intent_commits_nothing_and_records_nothing`: after the competing insert fired, the call under test threw `DbUpdateException` (`PK_WorkflowCancelRequests`) instead of answering `true` and recording nothing |

Every failure is the step the test exists for, after its setup completed; none is a setup failure. The unmutated tree passes the same tests (§11.6).

### 11.6 Validation

Builds: `Apperture.slnx`, `--artifacts-path artifacts/aud13`, `-m:1`. SQL Server: the local `apperture-test-sql` container; the SA password was read from the container's own environment into the test process, never placed on a command line. Every SQL test of `OperatorActionAuditSqlServerTests` writes `EXECUTED against SQL Server catalog …` (38 markers in the 38-test run) and fails, never skips, without SQL Server.

The results below ran on the tree committed as `fa26897` (the code is identical; the runs preceded the commit, and the mutation runs that followed were reverted to that tree).

| Check | Result |
|---|---|
| Full solution build | Succeeded, 0 errors |
| `Apperture.UnitTests` (full) | 3927 / 3927 passed |
| `Apperture.ArchTests` | 38 / 38 passed |
| `Apperture.Management.Ui.Shared.Tests` (full; the bulk retry summary changed) | 1490 / 1490 passed |
| `Apperture.Management.Ui.Client.Tests` (full; the itemization panel renders that summary) | 511 / 511 passed |
| `OperatorActionAuditSqlServerTests` (SQL Server) | 38 / 38 passed in 6 m 5 s: the 27 of §10 plus the item-retry race (blocked and requested), confirmed commit with a failing reconstruction, bulk retry (rows per item naming the batch with no batch row; a failing second item leaves the first), acknowledge (commit, audit failure, lost acknowledgement, unknown outcome, lost race) and reopen (commit, and a lost race that neither overwrites nor records) |
| `Apperture.IntegrationTests.Audit` (all audit SQL suites, including the above), `WorkflowExternalControlSqlServerTests`, `RecoveryWorkflowRemediationSqlServerTests`, `OperationItemizationSqlServerTests` (the retry attempt store), `ManagementHostGovernanceHttpTests` and `BrowserRequestTrustHttpTests` (the real host, so the changed dependency graph resolves) | 235 / 235 passed in 27 m 40 s |

After the mutations were restored, the tree checked out at `fa26897` was rebuilt (0 errors) and rerun: `Apperture.UnitTests` 3927 / 3927 and `OperatorActionAuditSqlServerTests` 38 / 38 passed. §12.3 adds three SQL tests (41 / 41).

Not run, with the reason: the full `Apperture.IntegrationTests` run and the SEC-1 browser suite (the classes above cover what this pass touched; no browser-facing route changed; PR CI runs both); the IAM-9.5 UI evidence opt-in; any real-tenant sign-in.

## 12. Pull request preparation (2026-10-07)

### 12.1 The three runtime findings of §11.3

The completion review surfaced three behaviours in the run runtime. Each was classified against the base (`78feee3`), the branch and the contract. None is a regression of this package, none is required by the AUD-1.3 contract, and none is an audit defect: the audit rows written for them are truthful. They stay open findings. They are not accepted risks, no product ruling is made here, and this package does not change them.

| Finding | Class | Evidence |
|---|---|---|
| Cancelling a run that has already finished is accepted | **B, pre-existing** | At base, `WorkflowCancelService` and `OperationCancelService` read the run, check authorization and an existing intent, and insert an intent and a lifecycle event with no status check at all. Unit tests that pin exactly this, `RequestCancelAsync_OnCompletedRun_PersistsCancelIntentWithoutRewritingTerminalStatus` and `…OnFailedRun…`, date from Epic 9 (`d1540f15`, 2026-03-17); the branch only adapted their constructors. The branch keeps the behaviour and now records it as a request (`requested`: a durable request was accepted, not that it was executed, gate §11.6), with the run's status at that moment in `runStatus`, so a reader can see a cancel was asked of a finished run. Nothing in the gate requires such a cancel to be refused |
| Two concurrent retries of one failed run create two retry runs | **B, pre-existing**, and a gate non-goal | At base, `WorkflowRetryService` passes a `null` idempotency key to dispatch and checks only that the original run is failed or cancelled; a second retry of the same run, concurrent or sequential, is eligible and creates another run. The branch passes the same `null` key. The gate states "no platform-wide idempotency framework and no HTTP idempotency keys" (§7, OR-1, AC-1; §13.5 "not HTTP idempotency"). Each retry now commits one run, one start request naming its own requester and one row, in its own unit with its own operation lock, so two retries are two truthfully attributed requests; before, neither was recorded |
| Two item retries with different keys that race for one attempt index: the loser returns 500 | **B, pre-existing** | At base, `SqlOperationItemRetryAttemptStore.CreateAsync` caught the unique violation, looked for an attempt with the request's own key, and rethrew when there was none (identical in `fdef8fa`; the store did not change until §11). `RunsController` does not catch `DbUpdateException`, so the loser returned 500 at base. The branch preserves that meaning (gate §13.5: "any other unique violation keeps its normal meaning"), so the outward result is the same 500 and nothing of the loser's is left. The only difference is structural: all of the loser's writes now sit in one transaction that rolls back. Real-SQL test `An_item_retry_that_loses_the_attempt_index_to_a_request_with_a_different_key_commits_nothing_and_never_adopts_the_winners_attempt` (blocked and requested paths) shows the loser fails on `IX_OperationItemRetryAttempts_OperationItemId_AttemptIndex`, commits no attempt, child run, stamp or row, and that the winner's attempt is never answered or recorded under the loser |

A recommended future owner for the three, if the owner wants them closed, is a runtime-correctness package (run lifecycle guard for cancel, an idempotency or single-flight rule for run retry, and a conflict response for the attempt-index race); that is not started and is not a precondition of this package.

### 12.2 Contract checks, without reopening AUD-1.2

| Check | Result |
|---|---|
| AUD-1.2 endpoints do not regress on `InvalidOperationException` | `AuditInvariantException` derives from `InvalidOperationException` and keeps every message, so an existing `catch (InvalidOperationException)` or message filter on an AUD-1.2 endpoint behaves exactly as before. The `when (ex is not AuditInvariantException)` exclusion is added only to the four run action controllers (`WorkflowsController`, `OperationsController`, `RecoveryController`, `RunsController`). A search of `src` finds no exact-type check on `InvalidOperationException`, and the full unit suite and the 235 related SQL and host tests pass |
| A removed cancel intent still leaves the audit evidence | New real-SQL test `An_operation_cancel_intent_the_worker_later_removes_leaves_the_audit_row_that_names_who_asked`: the intent is deleted the way `OperationStartRequestProcessor` removes it on completion, and the audit row is unchanged in `Id`, `EventId`, `Actor`, `ActorUserId`, `ActorUsername`, `Action`, `Details`, `OutcomeCode` and `Basis`. Workflow cancel intents are never deleted. Audit rows are protected from deletion by the AUD-1.1 guard |
| Requester is distinct from the worker | `ClaimedBy` and `AppliedBy` are separate columns the worker writes; the dispatch services create start requests with `ClaimedBy` null. Timeline unit test shows the requester and the claiming worker as two actors |
| Scheduler starts stay `system:scheduler` | `Aud13ConvertedWritersTests.The_scheduler_records_itself_as_the_requester_of_the_starts_it_dispatches` |
| A caller-supplied requester is never trusted | The start service replaces it with the unit's actor; no API request model carries a requester; mutation M-E (§11.5) |
| A losing race never attributes the winner's request to the loser | The race tests of §11.6, including the different-key item race above |
| Duplicate-key handling is limited to the intended index or key | Named-index matching in the cancel, control and dispatch services and the stores (D18); the different-key item race above is the negative case |
| Ambiguous commits follow lock, verification, reconstruction, `outcome_unknown` | The lost-acknowledgement and unverifiable-commit SQL tests of every converted family (§11.6) |
| Targets and targetless refusals | Unchanged since §11.3; the catalog and producers are pinned by `AuditActionCatalogTests` and `AuditDetailSchemaTests` |

### 12.3 Changes in this pass

Two real-SQL tests, no product code: the different-key item race (theory over the blocked and requested paths) and the removed operation cancel intent. `OperatorActionAuditSqlServerTests` is 41 / 41 against real SQL Server (41 execution markers), up from 38. `git diff fa26897 -- src` shows no product code change since the tested code commit; `git diff --check` over the whole branch is clean. The diff from `78feee3` is 85 files, all intended AUD-1.3 work: documentation, the audit and recorder code, the converted services and their stores, the start-request `RequestedBy` columns and migration (attributes in the file, no designer file, as `20261006200000_Aud1_AuditFoundation` does), and tests. The untracked `bash.exe.stackdump` is not part of it.
