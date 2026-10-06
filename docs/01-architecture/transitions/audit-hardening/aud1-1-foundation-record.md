# AUD-1.1 audit foundation: implementation record

Date: 2026-10-06

Status: **Implemented on branch `aud-1.1-audit-foundation`, not yet reviewed or merged.** The full CI checkpoint the gate requires for AUD-1.1 (§22) runs on the pull request.

Base: `origin/master` at `34b9cd7` (merge of PR #14, the approved AUD-1.0 gate; post-merge CI run 37527637902 green on that commit).

Contract: [AUD-1.0 design gate](aud1-design-gate.md), revision 3, §22 "AUD-1.1 Foundation". Nothing here changes the contract. Where the gate told AUD-1.1 to verify an entry and downgrade or remove what it could not confirm (I-6), the results are in §4.

## 1. Delivered scope

| Gate scope item | Where |
|---|---|
| Migration: §10.1, §11.8, §13 and §14 columns, §16 indexes, §19.2 guard | `PERS/Migrations/20261006200000_Aud1_AuditFoundation.cs`; model in `PERS/AppertureDbContext.cs`; names in `PERS/AuditLogSchema.cs` |
| Catalog for every action, with §11.5 outcome and provenance data and §15.3 defects | `APP/Audit/AuditActionCatalog.cs`, `AuditActionCatalogEntries.cs` (147 actions) |
| Recorder: unit of work, `Stage`, `WriteAsync`, `TryWriteAsync`, typed producers, overflow policy, interceptor, operation lock, verification, reconstruction | `APP/Audit/IAuditedUnitOfWork.cs`, `AuditEvent.cs`, `AuditDetailSchema.cs`; `PERS/Audit/SqlAuditedUnitOfWork.cs`, `SqlAuditEventWriter.cs`, `AuditRecorderRowGuardInterceptor.cs` |
| `append` bridge | `PERS/SqlAuditService.cs` (`AppendAsync`), `PERS/Audit/AuditActorEvidence.cs` |
| `AuditActorDto` and the label map | `APP/Services/AuditActorDto.cs` (server, with the projection), `MC/IManagementClient.cs` (client copy), `APP/Services/ActorLiterals.cs` |
| `AuditClassification`: preview, application lock, single conditional transition | `PERS/Audit/AuditClassification.cs`, `APP/Audit/AuditRowClassifier.cs`; wired in `PERS/PersistenceMigrationHostedService.cs` |
| Classifier rule reporting (additive) | `APP/Services/HistoricalActorClassifier.cs` (`ClassifyReportingRule`) |
| Conversion of `governance.user.username.changed` | `SEC/UserManagementService.cs` (`RenameUsernameAsync`, `ReconstructRename`); `outcome_unknown` in `API/UsersController.cs` |
| Conversion of `emergency.access.canonicalized`, with its corrected actor | `SEC/EmergencyAccessService.cs` (`EnsureCanonicalIdentityAsync(AuditActor)`); System from the hosted service, the emergency command line from the CLI |
| Replace AU8 | `tests/Apperture.UnitTests/ControlPlane/ConnectionLifecycleAuditTests.cs` |
| Correct `deploy/README.md` | "Who-did-what history after upgrading" and the new "Audit log classification after upgrading" |

Operator documentation for the attribution notices: [audit-attribution.md](../../../03-operations/audit-attribution.md).

## 2. Acceptance criteria

All SQL Server suites below use `AuditSqlScratch`, which **fails** when no SQL Server is reachable (`SqlServerTestConfiguration.RequireConnectionString`) and writes an `EXECUTED against SQL Server catalog …` marker.

| Id | Evidence |
|---|---|
| A1.1-1 | `AuditRecorderSqlServerTests.A_rename_commits_with_one_complete_recorder_row_holding_structured_previous_and_new_names` |
| A1.1-2 | `…The_actor_username_is_read_in_the_transaction_and_a_later_rename_never_changes_the_row` (a stale session name is supplied and ignored); `…A_self_rename_records_the_name_the_transaction_sees_after_its_own_rename` |
| A1.1-3 | First A1.1-1 test (`TargetType`/`TargetId`, no `Resource=`); bridge test `An_append_row_carries_its_producer_and_ids_keeps_its_details…` |
| A1.1-4 | `AuditActionCatalogTests` (classes, mappings, routine eligibility, `data_access` reserved and unused) |
| A1.1-5 | `AuditRowClassifierTests.Named_non_human_actors_record_only_ActorLiterals_values…`, `Converted_paths_pass_no_actor_literal_outside_ActorLiterals` |
| A1.1-6 | `AuditDetailSchemaTests` (undeclared fields, sensitive names, both injection cases, truncation, omission, legitimate maximums) |
| A1.1-7 | `AuditRowClassifierTests.The_shared_classifier_reports_which_rule_decided…`; the IAM-9.2a suite (`HistoricalActorClassifierTests`) is unchanged and passes |
| A1.1-8 | `AuditClassificationSqlServerTests` (all §15.7 bullets) |
| A1.1-9 | First A1.1-1 test; `AuditDetailSchemaTests.A_username_containing_an_equals_sign_and_a_space…` |
| A1.1-10 | `AuditRecorderSqlServerTests.When_the_audit_insert_fails_the_rename_rolls_back_with_it` |
| A1.1-11 | `AuditAppendBridgeSqlServerTests` (4 tests) |
| A1.1-12 | §5 below |
| A1.1-13 | `AuditIntegrityGuardSqlServerTests.A_recorder_row_inserts_complete_and_a_legacy_row_inserts_unclassified_then_completes_once` |
| A1.1-14 | `…A_rename_whose_commit_acknowledgement_is_lost_is_verified_and_reported_from_its_recorded_names` |
| A1.1-15 | `…A_delete_whose_commit_acknowledgement_is_lost_is_reported_as_done_not_as_not_found` (delete harness; production deletes are AUD-1.4) |
| A1.1-16 | `…A_multi_row_operation_with_a_lost_acknowledgement…`, `…A_multi_row_operation_whose_commit_rolled_back_reruns_once…` |
| A1.1-17 | `…A_rename_to_another_users_name_is_username_taken…`, `…A_duplicate_on_the_primary_event_id_is_success_only_after_verification…`, `…that_records_another_action_is_outcome_unknown`, `…A_duplicate_on_a_secondary_event_id_is_an_internal_error…` |
| A1.1-18 | `…When_the_operation_lock_is_not_granted_to_verification…`, `…When_the_verification_connection_fails…`, `…When_an_attempt_cannot_take_its_own_operation_lock…` |
| A1.1-19 | `AuditIntegrityGuardSqlServerTests` (frozen user, external and unresolved rows; partial, invalid and second classifications; evidence by case and trailing space; delete) |
| A1.1-20 | `AuditRowClassifierTests` (three provenance cases) and the classification SQL test |

Targeted mutation proof: with commit verification disabled, the six lost-acknowledgement, rollback and unknown-outcome tests fail; with the repository's ambient-transaction join disabled, the seven rename-path tests fail (including the audit-insert rollback). Both mutations were reverted and the suite is green.

## 3. Implementation-stage questions

| # | Answer |
|---|---|
| I-1 | The resolver's read happens during authorization, before the unit's transaction opens, so it is not the source. The recorder reads `Users.Username` for the actor inside the transaction at `Stage` (and inside the write for `WriteAsync` and `append`). A1.1-2 proves both orders. |
| I-2 | `SqlUserRepository.ChangeUsernameAsync` is the one repository changed: with a current transaction it joins it and leaves commit to the owner; without one it behaves as before. Proven with and without an ambient transaction (`…A_repository_joins_the_unit_transaction…` and the existing IAM-9 suite). The reads on the two converted paths open no transaction. |
| I-3 | An explicit loop around the execution strategy: transient failures before the commit call rerun inside the strategy (nothing can have committed); any exception from the commit call goes to verification and never to the strategy. Waits: attempt lock 5 s, verification lock 15 s, at most 2 reruns after a confirmed rollback (`AuditRecorderOptions`). `outcome_unknown` surfaces on the rename endpoint as HTTP 500 with `code = "outcome_unknown"` and the operation event id, which the existing client already treats as "outcome unknown, reload" (`UsersControllerRenameUsernameTests`). |
| I-4 | See §5: readiness measured in-process; the Windows Service part could not be performed. |
| I-5 | `HasTrigger` is declared for both guard triggers. SQL Server refuses a bare `OUTPUT` only when the table has a trigger for the same action, and the guard has no insert trigger, so inserts work with and without the declaration. An earlier program version can therefore still write audit rows after a program-only rollback; they are classified at the next start (`…An_earlier_program_versions_insert_still_works…`). The check constraints are written as `CASE WHEN … THEN 1 ELSE 0 END = 1`, because a bare rule lets UNKNOWN (NULL) comparisons through; the first test run caught this. |
| I-6 | §4. |
| I-8 | The §16 indexes exist and are pinned by the model snapshot. The default-timeline query that uses `IX_AuditLogs_DefaultVisible_At_Id` is AUD-1.5's; its plans belong there. The classification step uses `IX_AuditLogs_Unclassified_Id` (one seek when nothing is pending; §5). |

## 4. I-6 verification results

Each §15.3 and §11.5 entry was re-checked against writer source and git history, and every action's introducing commit and Epic 37 containment was established with `git log -S` and `git merge-base --is-ancestor baa2f763`.

**Inventory.** 147 action names are written at `34b9cd7` (88 W1, 7 W2, 40 W3, 11 W4, one listed exception). 44 predate the Epic 37 migration. Of those, 40 are era-invariant and 4 are not: `credential.password_set` (two writers with different outcome labels), both `…failed_definition_unavailable` actions (`Failed`, then `Blocked`), and `recovery.schedules.run_cycle_requested` (`completed` and `requested`). Every `AuditRecordId` is a fresh GUID, so it is safe as `EventId`.

**§15.3 changes:**

| Entry | Result |
|---|---|
| Invitation sent and resent; Recovery Access blocked change; workflow retry blocked as `system`; audit read denied as `system`; `recovery.run_cycle.denied` as `system:unknown`; canonicalization as `emergency-cli` | Confirmed as stated. The invitation has two administrator entry points (create-with-invitation as well as the setup invitation); both always recorded the invitee |
| `emergency.access.designated` | **Removed.** The fallback to the target was never reachable: the only caller has always passed the command-line actor |
| `execution.operation_item.bulk_retry.*` with `unknown` | **Removed.** The fallback is unreachable: the service throws first when no actor exists |
| Scheduler endpoints' `recovery.schedules.run_cycle_requested` with `system:unknown` | **Removed.** Written only after an authorization check that requires an actor |
| Recovery endpoints with `system` (`recovery.retry.denied`, `recovery.run_cycle.denied`, `recovery.blocked_work.retry_*`, `recovery.schedules.run_cycle_requested`) | **Added**, definite, under the gate's "any other fallback literal" rule |
| `auth.entra.login.success` with `unknown` (a writer removed in `7756093d`) | **Added**, possible |
| `credential.password.reset_requested` not recorded as `anonymous` | **Added**, possible |
| `auth.mfa.enrolled`, `auth.mfa.recovery_codes.generated` (the command line records the Recovery Access user) | **Added**, possible |

**§11.5 changes:** bulk retry requested records `success`, not `requested` (mapped to `requested`); bulk retry completed `success` also covers partial failure (mapped to `unknown`) and `failure` maps to `failed`; attention acknowledged and reopened record `success` (`succeeded`); `cancelled` and `notification` were unlisted (`unknown`). Category-like values map only where the action name states the result (listed in `AuditActionCatalogEntries`); `entra.access_request.rejected` is an administrator's successful decision and stays `unknown`.

## 5. Benchmark

OR-14 provisional benchmark: five minutes per million unclassified rows on the reference development machine.

| | |
|---|---|
| Hardware | AMD Ryzen 7 2700X (8 cores, 16 threads), 32 GB RAM (about 1 GB free during the run), SATA and NVMe SSDs |
| SQL Server | SQL Server 2022 Developer Edition 16.0.4250.1 (RTM-CU24-GDR) on Linux, in Docker Desktop (16 CPUs, 16 GB to the VM) on the same machine |
| Workload | 1,000,000 unclassified rows. Actors: 400,000 user ids, 350,000 literals, 120,000 current or retired usernames, 50,000 Entra object ids, 50,000 unresolvable values, 30,000 `unknown`. Ten action families, 3% typed connection JSON. 2,000 users, 2,500 ledger rows, 400 Entra links. Batch size 5,000 |
| Duration | **1 min 36.0 s** for classification (200 batches); 1 min 36.3 s for the whole startup step. About 96 seconds per million rows |
| Readiness | The `migrations` health check reported Unhealthy from 0.02 s and Healthy at 96.3 s, when the step finished. Progress was logged per batch |
| Nothing to classify | 743 ms for the classification pass on the next start |
| Harness | `tests/Apperture.IntegrationTests/Audit/AuditClassificationBenchmark.cs` (opt-in: `APPERTURE_AUDIT_BENCHMARK=1`) |

The run is within the benchmark, so no investigation or recommendation is needed. Background classification was not adopted.

**Limitation (I-4).** Windows Service start behaviour was not measured. The Management Host has no Windows Service hosting today (no `UseWindowsService` or `AddWindowsService` in `src`); that is OPS-1's supported-topology work. The startup step runs inside a hosted service's `StartAsync` before readiness, as measured above. How the service control manager's start timeout interacts with a long first start must be measured when the host can run as a service. Nothing here substitutes another host mode for that measurement.

## 6. Guarantees in force after AUD-1.1 (§14)

- `governance.user.username.changed` and `emergency.access.canonicalized`: W1. The change and its recorder row commit together, with commit verification; the result is a confirmed commit, a confirmed rollback, or `outcome_unknown`.
- Every other writer: unchanged write mode (§5.2 of the gate), through the `append` bridge. Their rows carry `Producer = append`, the caller's `AuditRecordId` as `EventId` and `OperationEventId`, and the details exactly as given. They are complete or wholly unclassified at insert. They are atomic with their change only where the writer already shares one transaction with it. **They are not atomic merely because of this package.**
- Rows written before AUD-1.1, or by an earlier version after a program-only rollback, are classified once at the next start and then frozen.
- The recorder break-glass marker on rename (`governance.break_glass.admin_operation`) still goes through the bridge before the change; AUD-1.2 replaces it with `viaRecoveryAccess`.

## 7. Left to later packages

- **AUD-1.2 to AUD-1.4:** every other W1 and W2 writer, the actors that are wrong today (the §15.3 writers, the Entra object id actors, the command-line MFA rows), invitation and mail as separate rows, permission diffs, and the production deletes with verification.
- **Found while building the inventory, for those packages:**
  - Actions the gate expects with no writer today: sign-out, scheduler unblocked, notification definition create, update and enable, subscriptions, contacts and routing, integration and schedule lifecycle other than delete, secret-store operations, run start, cancel, park and resume, single-item retry.
  - One name with two meanings: `credential.password_set`, `credential.password.reset_requested`, `identity_link.entra.tenant_mismatch`, `identity_link.entra.linked`, `module.lifecycle.*`, `recovery.schedules.run_cycle_requested`.
  - `RecoveryController.cs:152` passes the original run id where the definition id belongs.
  - Historical action names no longer written (`role.assigned`, `role.removed`, `auth.break_glass.admin_operation`, `credential.enabled`, the notification cut-over sign-off actions) classify as `other`.
- **AUD-1.5:** query contract, cursor, visibility filters and the dashboard count; transition-only blocked writing; the default-timeline query plans (I-8).
- **AUD-1.5b:** timeline UI and `ActorRef`.
- **AUD-1.6:** enforcement M2 to M5, the T-B optional privileged startup credential, and the retention document.

IAM-9 stays closed. The IAM-10, IAM-11 and RCV-1 findings stay open. RC1 is not complete.
