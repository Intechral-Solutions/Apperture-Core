# SEC-2 implementation record

| | |
|---|---|
| Date | 2026-10-13 |
| Contract | [SEC-2.0 design gate](sec2-configuration-secret-and-operator-boundary-design-gate.md), approved 2026-10-13. The gate is binding; this record states what was built against it and the evidence. It changes no ruling. |
| Baseline | `master` at `8f795f0` (PR #28, the SEC-2.0 merge) |
| State | **SEC-2.1 implemented** on branch `sec-2.1-attention-acknowledgement`, with an independent review (§1.7). It closes F1 when the pull request merges and CI is green. **SEC-2.2, SEC-2.3 and SEC-2.4 have not started.** |

This record grows one section per package. Each package starts on the owner's instruction (gate §6.3).

---

## 1. SEC-2.1: attention incident acknowledgement (F1)

### 1.1 What changed

| Area | Change |
|---|---|
| Authorization | The route needs `integration.update` on the integration (it needed `integration.read`). It is checked before the unit and again inside it, once the integration row is locked. A principal with only `integration.read` gets **403** and nothing changes or is recorded. |
| Writer | `AttentionIncidentAcknowledgementService` (`Security/`) is the one writer of `AcknowledgedAtUtc`, `AcknowledgedBy` and `AcknowledgementNote`. It runs as one W1 audited unit. |
| Tracker | `IAttentionIncidentTracker` and `SqlAttentionIncidentTracker` no longer have `AcknowledgeAsync`. There is no unaudited path to the columns. |
| Audit | New catalog action `attention_incident.acknowledged`, schema `AttentionIncidentAcknowledged`, typed producer `AuditEvents.AttentionIncidentAcknowledged`. |
| API | 204 acknowledged or repeat, 403, 404, 409 `attention_incident_not_active`, 400 `attention_note_too_long`, 500 `outcome_unknown` / `committed_result_unavailable` from the host handler. Stated in `docs/04-api/contracts.md`. |
| UI and client | The Acknowledge action is offered only with `CanUpdateIntegrations`. A 409 and a 403 are reported beside the incident history, which is reloaded. `ManagementClient` now surfaces the server's message and status code. |
| Controller | `IntegrationsController` takes `IAttentionIncidentAcknowledgementService` in place of `IAttentionIncidentTracker`, and no longer takes `TimeProvider`, which only the old acknowledgement used. |

### 1.2 Decisions taken inside the gate

| # | Decision |
|---|---|
| D1 | **The writer is a service in `Security/`, not a tracker method.** It follows `AttentionRoutingManagementService`, which already owns the integration-row-lock pattern. Removing the tracker's method makes "no unaudited write" structural, and a guard test pins it. |
| D2 | **Lock order inside the unit:** operation lock, integration row (`UPDLOCK`), the caller's re-run authorization, incident row (`UPDLOCK`), then the state checks. The re-run authorization comes before the incident is read, so a refused caller learns nothing about the incident. |
| D3 | **`reauthorize` is a required parameter**, not optional as in the routing writers, so no caller can omit the in-lock check. A guard test pins that the controller is the only caller. |
| D4 | **Business results are an outcome enum, not exceptions.** `Acknowledged`, `AlreadyAcknowledged`, `NotFound`, `NotActive`, `NoteTooLong`. A note over 1024 characters (after trimming) is refused before any unit opens. |
| D5 | **State order:** resolved is checked before already-acknowledged, so a resolved incident that was acknowledged while active is a 409, not a silent 204. |
| D6 | **The incident is read with `UPDLOCK`.** The observation worker resolves incidents without the integration row, so the incident row is where an acknowledgement and a resolution meet. The worker's EF update writes only the columns it changes, so a resolution after an acknowledgement keeps it. |
| D7 | **`AcknowledgedBy` is `transaction.Actor.RecordedValue`**, the value the audit row carries as its actor. For a person that is the user id; the unit refuses any other kind of principal except the system. It always fits the 64-character column. |
| D8 | **Audit target and fields follow gate §5.1:** target the integration (a name snapshot), the owning context as the related reference, fields `name`, `incidentId`, `conditionKey` (`expected_run_missing` or `other`, as for routing), `incidentStartedAt`, `noteProvided`. |
| D9 | **A stable code was added to the 400** (`attention_note_too_long`) beside the 409's, so clients need not match message text. The gate fixes the status only. |
| D10 | **The 409 and 403 of the UI are shown in a notice beside the history**, not through the page's error state, which replaces the whole page and would hide the reloaded history. Other failures keep the existing error state. |
| D11 | **A named property carries the UI gate** (`CanAcknowledgeIncidents`), so the existing source test that pins exactly one literal `CanUpdateIntegrations == true` guard for the routing editor is unchanged. |

### 1.3 Audit catalog entry

| Field | Value |
|---|---|
| Action | `attention_incident.acknowledged` |
| Category, severity, basis | `operations`, standard, `change` |
| Target | `integration` (the closed vocabulary has no attention incident; gate §5.1 note, SR-5) |
| Write class | W1 (`RequiredMutation`) |
| Schema | `name` (name snapshot), `incidentId` (id), `conditionKey` (enum), `incidentStartedAt` (timestamp), `noteProvided` (flag) |
| Never recorded | The note text, recipients, notification content |
| Introduced in, writer | `SEC-2.1`, `AttentionIncidentAcknowledgementService.cs` |

### 1.4 Acceptance matrix

| # | Evidence |
|---|---|
| A1.1 | Unit: `AttentionIncidentAcknowledgementAuthorizationTests.A_read_only_operator_is_forbidden_and_nothing_is_written_or_recorded`, `…cannot_even_read…`. SQL: `A_caller_who_may_not_update_changes_nothing_and_records_nothing` (both isolation modes). Host: `ManagementHostAttentionIncidentAcknowledgementHttpTests.A_principal_with_only_integration_read_is_forbidden…` (a real `integration.read`-only principal through the Management host). |
| A1.2 | SQL: `An_ownership_move_that_commits_while_the_acknowledgement_waits_is_seen_by_the_recheck`, four cases (loses access or keeps it, both modes): the server lists the acknowledgement waiting on the integration row, a move commits, the re-run check runs once under the lock and sees the new owner. Unit: the ownership-move tests with the real authorization helper. |
| A1.3 | SQL: `A_first_acknowledgement_commits_the_columns_and_one_row_attributed_to_the_operator_and_never_records_the_note` and `The_recorded_actor_is_the_authenticated_user_whatever_the_session_claims`. Unit: `A_first_acknowledgement_writes_the_three_columns_and_stages_one_row`, `Sec21AuditEventTests`. |
| A1.4 | SQL: `A_repeated_acknowledgement_changes_nothing_…`; `Two_concurrent_acknowledgements_give_one_winner_and_one_row` in both arrival orders on both modes (two sessions queued on the integration row, the winner is the first to arrive and is not overwritten, one row). |
| A1.5 | SQL: `A_resolved_incident_is_not_active_…`; `An_acknowledgement_that_races_the_resolution_…` in both orders on both modes (acknowledgement first: acknowledged then resolved with the columns intact and one row; resolution first: 409 with nothing written). |
| A1.6 | SQL, both isolation modes: audit insert failure, lost acknowledgement (the generic runner and an explicit case that answers `Acknowledged` with the columns and one row after one attempt), confirmed rollback rerun, unknown outcome committed and absent, operation lock not granted. |
| A1.7 | Unit and SQL: exactly 1024 stored, 1025 refused before the database; blank stored as null. Host: 400. |
| A1.8 | bUnit `IntegrationAttentionAcknowledgeUiTests` (8, a real render): action offered only with `CanUpdateIntegrations`, hidden for read-only and for a failed capability lookup, 409 and 403 notices keep the reloaded history, 404 and other failures keep the existing error state. Source guards in `IntegrationAttentionAcknowledgeUiSourceTests`. The existing notification suppression suites (`Notifications.Instances.*`) and the routing UI source tests pass. |
| A1.9 | `Aud14ClosureGuardTests`: `AttentionIncidents` is an in-scope set; `AttentionIncidentAcknowledgementService.cs` is its audited writer; the tracker is listed as the documented runtime-state exclusion. G1 and G2 pass for the new action. A guard test pins that only the controller calls the service, that only the service assigns the three columns, and that the tracker has no acknowledgement method. `docs/04-api/contracts.md` states the statuses. |

### 1.5 Test evidence

All counts are from this branch at the implementation commit. Heavy suites were run one at a time (the machine is memory constrained).

| Suite | Result |
|---|---|
| `AttentionIncidentAcknowledgementSqlServerTests` (SQL Server, throwaway catalogs, both isolation modes) | 44 passed |
| `ManagementHostAttentionIncidentAcknowledgementHttpTests` (real Management host and SQL Server) | 7 passed |
| Unit: the acknowledgement service, authorization, audit event, closure guard and catalog suites | 119 passed |
| Unit: the broader focused run (also routing, notification coordinator suppression, the controller suites, ownership move, fleet filter) | 311 passed; the 2 failures were expectation errors in two of the new tests, fixed |
| bUnit `IntegrationAttentionAcknowledgeUiTests` | 8 passed |
| Razor source tests (`Integrations.*`) | 14 passed |
| `Apperture.ArchTests` | 38 passed |

The SQL Server suites fail rather than skip without SQL Server: the catalogs come from `AuditSqlScratch`, and the host tests assert availability.

**Red first, and where it was not.** The service's behavior was written test first: 17 unit tests failed against a stub with `NotImplementedException`, then passed with the implementation. The catalog entry and producer, the controller wiring and the SQL, host and UI suites were written alongside or after the code; for those the evidence is mutation testing instead.

**Mutation evidence** (product code edited temporarily, restored, the tree verified clean by `git diff`; each mutation was caught by the tests named):

| Mutation | Caught by |
|---|---|
| No re-run authorization under the lock | 5 unit tests (the check count, the move cases, the refused-caller cases) |
| Incident read without `UPDLOCK` | the acknowledgement-versus-resolution race, resolution first, both modes |
| Integration row read without `UPDLOCK` | all four concurrent-acknowledgement cases and all four ownership-move cases |
| Repeat overwrites the first acknowledgement | 2 unit tests, the repeat case and all four concurrent cases (both modes) |
| Acknowledger is not the unit's actor | 4 unit tests and the 4 SQL actor cases |
| A resolved incident can be acknowledged | 3 unit tests and 4 SQL cases (resolved states, resolution first) |
| The route does not require `integration.update` before the unit | 5 unit tests, including the read-only refusal and the source guard |

The SQL part of the first mutation was not executed (its edit pattern missed on line endings in the first pass, and the second pass ran the unit tests only); the unit tests caught it, and the SQL move cases cannot pass without a call to the re-run check.

### 1.6 Accepted limitations and deferred items

| # | Item |
|---|---|
| L1 | **No note entry in the UI.** The button still sends no note, as before. The API accepts and validates one. |
| L2 | **The platform-wide sensitive-name rule is unchanged.** Gate §5.1 says a field named for a note, recipient or notification content cannot be declared. The global regex cannot refuse `note` without refusing the approved `noteProvided`, and widening it is a platform change outside SEC-2.1. This action's exact field set is pinned by three tests instead (`Sec21AuditEventTests`, the authorization suite and the SQL suite's detail-name assertion). |
| L3 | **The host tests share the governance test database** (`Apperture_ManagementGovernanceTests`). Each case copies the seeded integration, uses it, and removes it and its incident; the audit rows it wrote stay (the audit table is append-only). The primary development database was not touched, reset or reseeded; the other SQL suites use throwaway catalogs. |
| L4 | **An acknowledgement is not reopened.** There is no reopen route (gate §1 non-goal). |
| L5 | **Dead stubs removed from three test doubles.** `AcknowledgeAsync` returned `true` in the stub incident trackers of the coordinator tests. No test method changed. |

### 1.7 Independent review

An independent reviewer (a separate model instance, given the approved gate and the code at `a685d1b` and none of this record's conclusions) read the whole change against the gate and was asked for an explicit readiness verdict. It did not build or run anything.

**Verdict: READY.** No critical, high or medium finding. It checked the authorization order and the in-lock re-check, the lock order against the observation worker, integration delete, ownership moves and the notification coordinator (no cycle found), the `UPDLOCK` read under both isolation modes, partial-column writes, atomicity and rerun safety, the reconstruct delegate, actor integrity and the 64-character column, the catalog row against §5.1, the status codes, the contract text, the UI wording against the no-internal-metadata rule, and whether each SQL test would fail if its defect returned.

| # | Finding | Severity | Disposition |
|---|---|---|---|
| R1 | The note sentinel was never checked against response bodies (403, 409, 400) | Low | **Fixed.** The host tests now assert no response echoes the note. The code cannot leak it: the error bodies are constants. |
| R2 | The host-level read-only test did not prove the principal could read, so a 403 from the read check would also pass | Low | **Fixed.** The test first asserts that principal gets 200 on `GET /api/integrations/{id}`. |
| R3 | The A1.2 SQL test uses a stub callback, not the production re-check | Low | **Accepted.** The routing precedent does the same. The production re-check reads the integration with `AsNoTracking` on the unit's own context and has no cache, and the controller path with the real authorization helper is covered over InMemory. |
| R4 | "A field named for one cannot be declared" is met by pinned tests, not by the schema | Low | **Accepted** as the reading recorded in L2. Any added field fails the pinned tests. An exact-name refusal in the schema, or a catalog-wide guard, is optional hardening and not part of SEC-2.1. |
| R5 | The single-writer guard's message overstated it: `HistoricalActorNormalization` also rewrites `AcknowledgedBy` at host start | Informational | **Fixed.** The message names the exception. Closure guard G5 already lists it as an excluded writer, and it leaves user ids the service writes alone. |
| R6 | `CanUpdateIntegrations` is instance-scoped, so an operator who may update integrations only within one context does not see the action | Informational | **Accepted.** It matches the gate (P-6) and the routing editor on the same page. The reverse case, the button shown and the server refusing, shows the 403 notice. |

Not verified by the reviewer, and not covered by a test: the 500 `outcome_unknown` and `committed_result_unavailable` bodies of this route at host level (they come from the global host handler other converted routes already exercise), and whether the host fixture's background observer could touch the seeded rows.

### 1.8 CI

- **First run** (`589a50a`, run 38516164828): `browser-security` passed; `build-and-test` failed on one test, `AuditDetailSchemaTests.No_field_kind_carries_a_credential_secret_or_search_value`. It is a catalog-wide guard I had not run locally (my focused runs did not include that class). It whitelists the parameter types of the typed audit producers, and the new producer takes a `DateTimeOffset` (the incident's start time, the approved `Timestamp` field kind). No other test failed in that run. The guard now allows `DateTimeOffset` with a note on why it carries no credential, secret or search value.
- After that fix the whole `Apperture.UnitTests.Audit` namespace (370 tests) passes locally.
- Final CI result: see the pull request.
