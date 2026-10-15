# SEC-2 implementation record

| | |
|---|---|
| Date | 2026-10-13 |
| Contract | [SEC-2.0 design gate](sec2-configuration-secret-and-operator-boundary-design-gate.md), approved 2026-10-13. The gate is binding; this record states what was built against it and the evidence. It changes no ruling. |
| Baseline | `master` at `8f795f0` (PR #28, the SEC-2.0 merge) |
| State | **SEC-2.1 implemented and merged** (PR #29; F1 closed). **SEC-2.2 implemented and merged** (PR #30, `master` at `5e27206`; U-3 closed and the code side of U-1; **U-1 stays an RC1 blocker until the inventory of every persistent installation is recorded, A2.7, open**). **SEC-2.3 implemented** on branch `sec-2.3-setting-secret-ownership` (base `5e27206`), with an independent review (§3.11). It closes the code side of U-2 when its pull request merges and CI is green; **A2.7 stays an open rollout condition (§3.8).** **SEC-2.3 merged** (PR #31, `master` at `e1e9b2e`). **SEC-2.4 implemented** on branch `sec-2.4-entra-probe-telemetry` (base `e1e9b2e`), with an independent review (§4.8). F2 closes only when its pull request merges and the result is verified; **A2.7 and the persistent-installation part of A3.7 are untouched and stay open.** |

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
- **Second run** (`ee32ca2`, run 38519081730): `browser-security` passed; `build-and-test` failed on one of the new bUnit tests, `Acknowledging_sends_the_request_without_a_note_and_reloads_the_history`. It passed locally. The test verified the client call immediately after a click, but bUnit dispatches the click on the renderer, so on a slower runner the call had not been made yet. This was a timing assumption in the new test, not a product defect. The test now waits for the call, as the other tests of that class already did; the class passes six consecutive local runs.
- Final CI result: see the pull request.

---

## 2. SEC-2.2: connection secret ownership and read model (U-1, U-3)

### 2.1 What changed

| Area | Change |
|---|---|
| Ownership rule | `ConnectionSecretOwnership` (`Core.Application/Connector`) is the one definition: provider `platform-connection`, name `connection:{connectionId}:{fieldKey}`. The provisioner's create and rotate paths and every resolution use it. |
| Owned resolution | `SecretOwner` and `ISecretResolver.ResolveOwnedAsync`. `SqlSecretStore.TryGetOwnedPayloadAsync` reads the descriptor with the owner in the predicate, compares provider and name ordinally in C#, and only then reads the version row; the protector is reached only for an owned secret. `CompositeSecretResolver` forwards; `AzureKeyVaultSecretResolver` and the placeholder `DefaultSecretResolver` refuse (they cannot verify ownership). |
| Boundary | `IConnectionMaterialResolver` (`ConnectionMaterialResolver` in `ConnectorResolution`) replaces `ResolvedConnectionMaterialBuilder` and the loops the Sftp and Campus Nexus verifiers copied. Worker slot resolution (`DefaultConnectionResolver`), the settings options, the three verifiers and structure browse all call it. |
| Refusal | `ConnectionSecretUnavailableException` (an `InvalidOperationException`, so callers that mapped the earlier store miss keep mapping it), fixed message, field key only. `SqlSecretResolver` no longer echoes the reference. |
| Evidence | New catalog action `connection.secret.resolution_refused` (W3, category `secrets`, elevated, basis `attempt`, target the connection; fields `fieldKey` and `path` ∈ `run`, `verify`, `browse`, `settings_options`). One row per boundary call. |
| Worker Host | Now registers the audit recorder (`AddAppertureAuditRecorder`). Before, `IAuditEventWriter` was not registered there, so a boundary that writes its refusal row could not have been built in the worker. |
| Read model | `secretRefs` removed from every connection response; `secretFields: [{ key, configured }]` replaces it (client DTO and server DTO). |
| Write contract | `PATCH` with a `secretRefs` property is a 400. `UpdateConnectionRequest(Name, Config, SecretValues, ClearSecretFields)`. `clearSecretFields` clears any stored key. `secretValues` rotates an owned secret, or stores a new owned secret and repoints a field whose reference is not its own. |
| UI | The connection page reads `secretFields`; an undeclared legacy secret field shows a Remove action (offered with `CanUpdateConnections` once the connector definition was read). |
| Inventory | `scripts/sec2/Get-ConnectionSecretOwnershipInventory.sql`, read-only. |
| Guards | `ConnectionSecretBoundaryGuardTests` (identifier-based, with a self-test), a composition test for both hosts, and a source guard that nothing in `SqlSecretStore` assigns a descriptor's name or provider. |

### 2.2 Decisions taken inside the gate

| # | Decision |
|---|---|
| D1 | **Descriptor first, then version, as two reads.** The owner is in the first query's predicate (a cheap narrowing) and compared again ordinally in C#, because the catalog's collation folds case (a test proves it does here, so the case-variant refusals are for the right reason). The second read is by `(SecretId, Version)`. That is sound because no operation of the store, and nothing else in `src/`, assigns a descriptor's name or provider after it is created (a guard test pins it, and a SQL test checks the stored owner after create, add-version, activate, disable and revoke). The gate's "joins the first read" (P-12) is met: the check costs no round trip, which a test asserts by comparing the number of statements with the unchecked path (K-2). |
| D2 | **`ResolveOwnedAsync` is a required member**, not a default interface method, so every resolver declares what it does. Key Vault and the placeholder refuse; a future resolver cannot inherit a permissive default. |
| D3 | **Every unavailable case is the same exception** at the resolver (`SecretUnavailableException`) and at the boundary (`ConnectionSecretUnavailableException`). Other failures (a protector error, a cancellation) are not refusals: they propagate and write no refusal row. |
| D4 | **The path label follows the host.** `AddConnectorResolution(path)` registers what slot resolution means in that host: the Worker Host's default is `run`, the Management Host passes `settings_options` (its only slot consumer is the Campus Nexus campuses provider). Verify and browse are named by the verifiers and the browser. This avoids an ambient context. A future Management consumer of slot resolution would be labelled `settings_options` until it says otherwise. |
| D5 | **Actor:** `run` is `AuditActor.Worker`; every other path is `AuditActor.CurrentPrincipal`. |
| D6 | **The evidence write is bounded and cannot change the result.** `TryWriteAsync` with a 15-second budget; a `false`, an exception or a timeout is logged (the connection id, the field key, the path, and the exception type only) and the refusal stands. The refusal is decided before the write is attempted. |
| D7 | **Fields are resolved in ordinal key order**, so a connection with several poisoned fields always names the same first field, and the call aborts there. |
| D8 | **A blank reference or an empty key is a refusal**, not an `ArgumentException`. A key longer than the schema's id bound is recorded truncated; an empty key is recorded as `(blank)`. |
| D9 | **A typed `InvalidConnectionRequestException`** (an `InvalidOperationException`) carries the new request-shape errors, and the controller answers 400 for it. The existing mapping of other `InvalidOperationException`s to 404 on `PATCH` is unchanged. |
| D10 | **An update no longer normalizes or validates references it does not touch.** It used to throw on an undeclared stored key and on an unreadable stored reference, so a legacy row could not even be renamed. The config-change guard remains: while the request leaves a reference that is not the field's own, a config change is refused (config decides where the credential goes). A field the same request repairs or clears is excluded from that check. |
| D11 | **`secretFields` lists the connector's declared secret fields first (configured `false` when nothing is stored), then every other stored key.** A host that does not register the connector lists the stored keys only. |
| D12 | **Q-1 settled.** A failed step fails its run (`RunFailureCapture` records the exception message, which is now the fixed text). Nothing in the worker retries within the run, so each new attempt (a retry of the run, the next scheduled start) is one boundary call and writes one row. No throttle was added. |
| D13 | **Q-2 settled as the gate says.** An undeclared legacy key is repaired by clearing it; the API has no way to repoint it. |
| D14 | **K-13 settled.** The stale remark in `IntegrationSettingSecretInventory` is corrected. |
| D15 | **The `secretRefs` property stays on the `PATCH` body type** only so that a request still sending it can be answered with the 400. It is never read. |

### 2.3 Audit catalog entry

| Field | Value |
|---|---|
| Action | `connection.secret.resolution_refused` |
| Category, severity, basis | `secrets`, elevated, `attempt` |
| Target | `connection`; the owning resource context is the related reference |
| Write class | W3 (`Attempt`), outcome `rejected` |
| Schema | `fieldKey` (id), `path` (enum: `run`, `verify`, `browse`, `settings_options`) |
| Never recorded | The reference, the secret found, its owner, which condition failed |
| Introduced in, writer | `SEC-2.2`, `ConnectionMaterialResolver.cs` |

### 2.4 Acceptance matrix

| # | Status | Evidence |
|---|---|---|
| A2.1 | Met | All six paths call `IConnectionMaterialResolver` (`ConnectionSecretResolutionPathsSqlServerTests` drives each for real). `ConnectionSecretBoundaryGuardTests` fails a production file outside `ConnectionMaterialResolver.cs` that reads `SecretRefs` (or the `SecretRefsJson` column) next to a secret resolver type, or builds a `SecretRef` beside `SecretRefs`, or lets a connector project name a resolver at all; its self-tests feed it the loop the verifiers used to copy and the other shapes. The boundary file itself is pinned to make no `.ResolveAsync(` call under any receiver name. |
| A2.2 | Met | `A_poisoned_reference_is_refused_on_every_path_and_nothing_foreign_is_read_sent_or_changed`: 6 entry points × 11 classes (another connection's secret, another field's, another provider's, the name in another case, the provider in another case, dangling, unreadable, Key Vault style, blank, disabled owned, never-activated owned) × both isolation modes = 132 cases on real SQL Server. A recording protector shows nothing is unprotected, a command log shows a foreign secret's version table is never queried, a loopback listener shows nothing reaches the destination (a control case with an owned secret proves the listener counts), and the store snapshot is unchanged. |
| A2.3 | Met | The exception, its `ToString()`, the audit row, the verifier summary, the browse message, the run failure summary and the logs hold no reference, secret id, owner or foreign name (assertions in the same matrix, `ConnectionMaterialResolverTests` and `ConnectionSecretOwnershipSqlServerTests`). `SqlSecretResolver`'s two messages no longer echo the reference (a test pins it). |
| A2.4 | Met | One row per call at every entry point, including a connection with two poisoned fields (the first in ordinal order is named) and three attempts writing three rows; the right actor (the worker, with no principal context at all, or the operator) and `path`. With a writer that throws, one that reports failure, and the real writer pointed at a missing database: the refusal stands, nothing is sent, nothing is unprotected, and there is no row. |
| A2.5 | Met | An owned connection reaches its destination on every path and writes no refusal row. `For_a_secret_the_connection_owns_the_owned_call_agrees_with_the_unchecked_one_in_every_lifecycle_state` (active, pinned to an unknown version, disabled, revoked, a new version never activated) and the rotation and pinned-version tests show the lifecycle is unchanged; the protector receives the key id stored on the version row; the store runs the same two statements (descriptor, then version) as the unchecked path, asserted by statement count (K-2). |
| A2.6 | Met | `ConnectionSecretOwnershipInventorySqlServerTests`, both isolation modes: 28 seeded problem rows (every class; a retired connection; non-string and null values; an unreadable map; every GUID spelling `Guid.TryParse` accepts; padding; text after a GUID; the hexadecimal form; a name that differs only by trailing spaces) are returned exactly, with the right class. Owned secrets written in other spellings, a clean catalog and an empty one return nothing. It runs as a database user with SELECT on the three tables only, changes nothing, and is repeatable. |
| A2.7 | **Not met; a rollout requirement** | No persistent installation database is named in the deployment records in this repository (`deploy/` and `docs/03-operations/` hold only the development and example connection strings), so none was enumerated and none is invented. The script was run read-only against the four persistent **test-fixture** databases on the local test SQL container (`Apperture_BootstrapHttpTests`, `Apperture_BrowserTests`, `Apperture_IntegrationTests`, `Apperture_ManagementGovernanceTests`) and returned no row for each; they are fixtures, not installations, and are not evidence for A2.7. **U-1 therefore stays an RC1 blocker (gate §8) until each persistent installation's database has been inventoried and each listed row's disposition recorded here.** |
| A2.8 | Met | `ManagementHostConnectionSecretFieldsHttpTests` through the real Management host: list, detail, create (201), `PATCH` (rename and rotation) and test-connect carry no reference in any spelling, no `secretRefs` property and no secret value, and each carries `secretFields` with exactly `key` and `configured`. A declared field with nothing stored reads `configured: false`; an undeclared stored key is listed; a blank reference is `false`. No other API serializes a `Connection` (searched). |
| A2.9 | Met | `PATCH` with `secretRefs` (non-empty, empty, with other fields) is a 400 naming both replacements and changes nothing. `secretValues` on a foreign field creates and repoints (unit, SQL, HTTP, and the M6 matrix with a new `RepairForeign` operation: audit failure, lost acknowledgement, confirmed rollback rerun, unknown outcome, lock not granted); the foreign secret's descriptor, versions, payloads and `SecretAudits` count are identical before and after, and a unit test records that only its descriptor is read. `clearSecretFields` clears declared and undeclared keys, is idempotent, and rejects unknown keys, blanks, a cleared-and-set field and a required field with a 400; both operations need `connection.update` (403 otherwise). |
| A2.10 | Met | `ConnectionWorkspacePresentationTests`, `ConnectionWorkspaceRenderTests` and bUnit `ConnectionLegacySecretFieldUiTests`: Set and Not set from `secretFields`; an undeclared legacy field (and a stored secret that shares a key with a plain field) shown with a Remove action only for an operator who may update connections; the click sending exactly `ClearSecretFields` and reloading. `docs/04-api/contracts.md` carries the contract and the compatibility note. |

### 2.5 Test evidence

Counts are from this branch after the review remediation. Heavy suites were run one at a time (the machine is memory constrained). The SQL Server suites use throwaway catalogs created and dropped by the tests, and fail rather than skip without SQL Server.

| Suite | Result |
|---|---|
| `Apperture.UnitTests` (the whole project) | 4660 passed |
| `Apperture.Management.Ui.Shared.Tests` | 1496 passed |
| `Apperture.Management.Ui.Client.Tests` | 523 passed |
| `Apperture.ArchTests` | 38 passed |
| SEC-2.2 SQL Server and host suites together (ownership, six paths, inventory, connection lifecycle and M6, verify and browse, ownership race, structure browser, Management host connection tests; 423 tests, both isolation modes where the suite runs on both) | 423 passed |

**Test-first status.** The primitives (`SecretOwner`, the store check, the boundary) were written before their tests, so their evidence is mutation testing, as in §1.5, rather than red first. The update flow, the guard and the inventory were written against the finished contract.

**Mutation evidence** (product code edited temporarily and restored; `git diff` verified):

| Mutation | Caught by |
|---|---|
| The store ignores the owner (no predicate, no ordinal comparison) | 22 of 47 store-level tests and 60 of 132 path cases (every foreign class at every entry point, both modes) |
| The provisioner treats every parseable reference as owned (the repair path) | **Not run.** The auto-mode classifier declined the temporary edit, and it was not routed around. That path rests on assertions that fail without the change: a unit test records every store call that names the foreign secret, and the SQL and host tests compare its descriptor, versions, payloads and audit count. |

### 2.6 Deployment and other limitations

| # | Item |
|---|---|
| L1 | **A2.7 is open** (see the matrix). The inventory is a pre-upgrade step for each persistent installation. Enforcement does not wait on it (gate §6.3), so a connection with a poisoned reference fails after upgrade until it is repaired with `secretValues` or `clearSecretFields`. |
| L2 | **No persistent database was reset or modified.** The only databases created or dropped are the tests' uniquely named throwaway catalogs. The host HTTP tests write to `Apperture_ManagementGovernanceTests`, remove the connections and secrets they create, and leave only audit rows (append-only). The primary development database was not touched. |
| L3 | **The inventory needs compatibility level 130** (`OPENJSON`, `ISJSON`) and recognizes `Guid.TryParse`'s forms by pattern. Only the hexadecimal form is reported as `check_by_hand`. |
| L4 | **Settings and Entra secrets are not covered** (gate §1). `IntegrationSettingsAccessor`, the SMTP resolver and the Entra client secret still call the unchecked `ResolveAsync`; SEC-2.3 and K-3 own them. |
| L5 | **The settings-options endpoint's status codes are unchanged** (gate §5.3): a refused connection there is the same unhandled `InvalidOperationException` a missing secret was, now with a fixed message. |
| L6 | **`PATCH` with `"secretRefs": null` is accepted** as absent, because the body type cannot tell a null from a missing property. A non-null value, even `{}`, is refused. |
| L7 | **An unknown or non-secret key in `secretValues` is still a 404** on `PATCH`, as before. The new request errors and the ownership refusal are 400. |
| L8 | **The guard is identifier-based.** It cannot see a reference passed through a variable defined in another file, or a positional deconstruction of `Connection` that never names `SecretRefs`; the one-file allow-list, the ban on resolver types in the connector projects and the runtime tests are the compensating controls. Types that can reach material without being a resolver (`SqlSecretStore`, `ISecretProtector`) are not on its list, because legitimate code uses them. |

### 2.7 Independent review

An independent reviewer (a separate subagent run with the Opus model at high effort, as the assignment asked; given the approved gate and the code at `770f55c` and none of this record's conclusions) read the change against the gate and was asked for an explicit readiness verdict. It was read-only: it ran no build, test or database command.

**Verdict: READY WITH CONDITIONS**, no critical or high finding. The conditions were F-1 (fix the inventory's GUID parsing), F-2 (commit this section) and keeping U-1 open until A2.7 is recorded. All three are addressed below. It checked the six paths across all of `src/`, ownership before the version read, descriptor immutability, the audit and fail-closed behaviour, disclosure through every output, the repair, the inventory, the API and UI, and the tests.

| # | Finding | Severity | Disposition |
|---|---|---|---|
| F-1 | The inventory parsed the reference with SQL Server's `TRY_CONVERT`, which does not accept what `Guid.TryParse` accepts. A historical foreign reference in the 32-digit, parenthesised, whitespace-padded or hexadecimal form would be listed `unreadable` ("clear it") instead of as an incident; and a GUID followed by text would be converted anyway. | Medium | **Fixed, and the premise checked rather than assumed.** A query on the test SQL Server confirmed the 32-digit, parenthesised, tab-padded and hexadecimal forms return NULL, and a GUID followed by text returns the GUID. The script now trims whitespace and recognizes the hyphenated, braced, parenthesised and 32-digit forms by explicit pattern; anything else is `unreadable`, the hexadecimal form is `check_by_hand`. The header claim was corrected. The test seeds each form (foreign and owned), padding, trailing text and the hex form. |
| F-2 | This section was uncommitted at the reviewed commit. | Medium (process) | **Fixed.** Committed with the remediation. |
| F-3 | A config change while a kept reference is not the field's own answered 404 (a plain `InvalidOperationException`), although it is now the documented repair route. | Low | **Fixed.** The ownership refusal is `InvalidConnectionRequestException`, so `PATCH` answers 400 (create already did); a host test and the contract row cover it. Unknown or non-secret `secretValues` keys remain 404 (L7). |
| F-4 | The identifier guard missed a persistence helper reading `SecretRefsJson` next to a resolver, a boundary alias such as `var r = _secretResolver; r.ResolveAsync(...)`, positional deconstruction, and non-resolver material readers. | Low | **Partly fixed.** The `SecretRefsJson` case and any-receiver `ResolveAsync` are now guarded and self-tested. Deconstruction and non-resolver types are accepted and stated (L8). |
| F-5 | The M6 matrix did not cover the new create-and-repoint path. | Low | **Fixed.** A `RepairForeign` operation joins the matrix: audit failure, lost acknowledgement, confirmed rollback rerun, unknown outcome, and lock not granted. |
| F-6 | A stored secret whose key equals a declared plain field or a config key had no secret row, so the UI could not show or remove it. | Low | **Fixed.** It gets its own row (labelled "(stored secret)") with the Remove action; the `@key` of the rows is distinct. A presentation test pins it. |
| F-7 | `"secretRefs": null` is accepted. | Informational | **Accepted** (L6). |
| F-8 | The exact comparison in the inventory ignored trailing spaces. | Informational | **Fixed.** A `DATALENGTH` test is added; the test seeds a name differing by trailing spaces. |
| F-9 | `A_good_field_before_a_poisoned_one…` asserted only that the call throws. | Informational | **Fixed.** It now asserts the owned field was asked first, the poisoned one second, and one row names the poisoned field. |

Not verified by the reviewer: it could not run SQL Server (F-1's premise was verified afterwards, see above), run any suite, or confirm that any persistent installation holds poisoned rows (A2.7).

### 2.8 CI

Final CI result: see the pull request.

---

## 3. SEC-2.3: setting secret ownership and legacy remediation (U-2)

### 3.1 What changed

| Area | Change |
|---|---|
| Ownership rule | `SettingSecretOwnership` (`Core.Application/PlatformSettings`) is the one definition. An instance setting `K` owns provider `platform-setting`, name `platform-setting:K` (unchanged, so the SMTP password stays valid). Setting `K` of integration `I` owns provider `integration-setting`, name `integration-setting:I:K`, with `%` written `%25` and `:` written `%3A` in `I` and in `K`. Create, replace, clear, reset, retirement and every read take the owner from here. |
| Writes | `PlatformSettingSecretMaterialProvisioner` takes a `SecretOwner`. `ReplaceAsync` adds a version only to a usable secret whose stored descriptor has exactly that provider and name; in every other case it stores a new owned secret and the service repoints the row, leaving the referenced secret untouched. `TryDisableOwnedSecretAsync` disables only an owned, usable secret. |
| Reads | `ISettingSecretResolver` (`SettingSecretResolver`, `Core.Application`) resolves a row-sourced reference only through `ISecretResolver.ResolveOwnedAsync`, so the descriptor is checked before any version row or payload is read. Every refusal is one `SettingSecretUnavailableException` with a fixed message and the setting key only, and writes one W3 row. `IntegrationSettingsAccessor` and `EmailSmtpCredentialResolver` resolve through it. |
| Legacy | `IntegrationSettingSecretClassifier.FindLegacySecretToRetireAsync` applies H1's evidence to a secret an integration row has just released (cleared, reset or repointed): usable, the legacy shape named for that very setting, and nothing else in any scope refers to it. `IntegrationSettingsService` retires it on that evidence only. |
| Delete | The H1 classifier and retirement accept the integration-specific names next to the legacy shape. `PlannedSecretRetirement` carries the owner the classifier proved, and the retirement checks the descriptor against it again. |
| SMTP | `EmailNotificationOptions.SmtpPasswordRefSource` (`Configuration` by default, `SettingRow` when the platform-settings overlay applied the row's reference) travels with the reference. A row-sourced reference goes through the boundary; a configuration-sourced one is deployment-trusted input outside the row rule (P-22) and goes through the named `ResolveDeploymentConfiguredAsync`. |
| Evidence | Two new catalog actions (§3.3), and `replaced` joins the reasons of `integration.settings.secret.retired`. |
| Inventory | `scripts/sec2/Get-SettingSecretOwnershipInventory.sql`, read-only. |
| Guards | `SettingSecretBoundaryGuardTests` (identifier-based, with self-tests), `SettingSecretResolverCompositionTests`. |
| Operations | `docs/03-operations/configuration-secret-ownership-upgrade.md` (both inventories and the recovery procedure) and the contract in `docs/04-api/contracts.md`. |

### 3.2 Decisions taken inside the gate

| # | Decision |
|---|---|
| D1 | **Q-3 settled: ids and keys may contain `:`.** Neither `IntegrationId` nor a setting key is validated (an id is 24 hex characters only because the service generates it; keys are module-declared). So the two components are escaped, which makes the name unambiguous and reversible (`TryParseIntegrationSettingName`). An id or key with neither character has exactly the approved name. A name that would not fit the store's 256 characters is refused, never truncated, and a read treats it as a refusal. The row's own primary key (`{kind}:{scopeId}:{key}`) is not escaped and already collides for pairs such as `("a:b","c")` and `("a","b:c")`; that is existing behaviour and is not changed here. |
| D2 | **The instance identity is unchanged, so an instance row cannot be told from a legacy integration secret for the same key.** The legacy integration shape (`platform-setting:K`) equals the instance owner for `K`. An instance row that names it is treated as owned (P-18 accepts this). The reverse is handled: an integration row that names it is refused at read and is retired only on H1's evidence. |
| D3 | **One boundary in `Core.Application`**, because the Worker Host (the accessor) and the Management Host (the SMTP password) both need it and neither references the other. It is registered with `TryAddScoped` by every extension that needs it. The unchecked `ResolveAsync` appears once, in `ResolveDeploymentConfiguredAsync`, and a guard allows only the SMTP resolver to call that. |
| D4 | **Every `SecretUnavailableException` is a refusal**, including a blank reference, text that is not a GUID and a name that cannot fit the store (as SEC-2.2 D8 does for connections). Other failures (a protector error, a cancellation) are not refusals: they propagate and write no row. |
| D5 | **A module sees `missing_secret`** (the SDK's "not configured") for every refusal. A secret that resolves to blank text stays `invalid_secret_reference`, as before. `IsSecretConfigured` still reports only that a reference is stored; it makes no descriptor read (it is synchronous and used by readiness). |
| D6 | **Actor attribution.** One row per boundary call. An integration setting on `run` is attributed to the worker; an instance setting on `run` to `system:platform-settings`; on `management` to the signed-in principal, or `anonymous` when there is none (a recovery or invitation mail), so the row is always writable and never blames a person. The accessor uses `run` (the Worker Host is the only host that registers it); delivery uses `run` and account mail `management`. |
| D7 | **The row's related context** is the integration's owning resource context, read best-effort through the integration repository; its absence leaves the reference out and changes nothing else. |
| D8 | **Replace over a disabled secret of its own creates a new one**, as before. Replace over any other non-owned reference creates a new owned secret and never versions, disables or reads the old one. |
| D9 | **An owned secret is disabled by exact ownership alone** (no reference scan). A reference to it from another row is already refused at read, so the scan would add nothing; H1's evidence is used only for the legacy shape, which cannot prove an owner. |
| D10 | **Legacy retirement is optional wiring.** `IntegrationSettingsService` takes the classifier as an optional last constructor parameter; the host's container supplies it. Without it a legacy secret is simply kept, so there is no second, unsafe path. The new reason `replaced` records a retirement caused by re-entering a value. |
| D11 | **Q-4 settled: a read-only SQL script**, as for U-1. It classifies by what the secret store says, needs no module code, and takes the sensitive keys from a list at the top that an operator extends (a key that is not listed is still reported, as `ordinary_guid_text`, with the relation the secret has to the row). The list is a `VALUES` constructor inside the query, so the script keeps no state and the test that forbids `INSERT`, `DECLARE` and `INTO` holds. |
| D12 | **The delete's ambiguity rule is unchanged.** A secret named for another integration is retained while that integration's override holds it and otherwise refuses the delete (the operator resets the setting, then deletes). |
| D13 | **Readiness is unchanged.** It reports that a reference is stored, so an integration whose row names a foreign secret reads as configured and then fails closed at run, with the audited refusal, until the value is re-entered. The inventory exists to find those rows before the upgrade. |

### 3.3 Audit catalog entries

| Action | Category, severity, basis | Target | Write class | Schema | Never recorded |
|---|---|---|---|---|---|
| `integration.settings.secret.resolution_refused` | `secrets`, elevated, `attempt` | `integration_setting` (`{integrationId}:{key}`); the owning resource context is the related reference | W3 | `path` (enum: `run`, `management`) | The reference, the secret found, its owner, which condition failed |
| `platform.settings.secret.resolution_refused` | `secrets`, elevated, `attempt` | `platform_setting` | W3 | `path` (enum: `run`, `management`) | As above |

`integration.settings.secret.retired` gains the reason `replaced`. Introduced in `SEC-2.3`; writer `SettingSecretResolver.cs`.

### 3.4 Acceptance matrix

All SQL cases run on a locking READ COMMITTED catalog and a READ_COMMITTED_SNAPSHOT catalog (throwaway catalogs the tests create and drop).

| # | Status | Evidence |
|---|---|---|
| A3.1 | Met | SQL `Two_integrations_that_set_the_same_key_get_two_distinct_secrets_under_their_own_owner_and_the_instance_name_is_unchanged` (descriptor provider and name of two integrations and of the instance SMTP secret; each resolves for its own owner and a cross-owner resolve is refused), `A_second_replace_by_the_same_setting_versions_its_own_secret_and_keeps_the_reference`, and the race `Two_integrations_that_replace_the_same_key_at_once_each_end_with_a_secret_of_their_own`. Unit: `SettingSecretOwnershipTests` (format, delimiter and escape cases, round trip, length), `SettingSecretProvisionerOwnershipTests`, `SettingSecretOwnershipServiceTests`. |
| A3.2 | Met | SQL `A_replace_over_a_reference_that_is_not_the_integration_settings_own_repoints_it_and_leaves_the_victim_unchanged`: another integration's secret, an instance setting's, a connection's, an Entra registration's, another key of the same integration, the owner's name under another provider, a case variant of the owner's name, and a dangling reference. For each, the row is repointed to a secret under the owner and the victim's snapshot (descriptor, every version with its protected payload, and its evidence rows) is identical before and after. The instance counterpart covers six shapes. Unit: ten shapes at the provisioner. |
| A3.3 | Met | SQL `A_clear_by_B_never_disables_integration_A_s_secret…` and `A_reset_by_B…` (B's row names A's secret; A's snapshot is identical afterwards and no retirement row is written for B), `An_instance_clear_or_reset_never_disables_a_secret_it_does_not_own`, `A_clear_or_reset_of_the_settings_own_secret_still_retires_it_and_records_why`, and the race `A_clear_by_B_that_races_A_re_entering_its_value_never_disables_A_s_secret`. |
| A3.4 | Met | SQL `The_runtime_accessor_refuses_a_reference_that_is_not_owned_before_any_foreign_payload_is_read_and_the_module_sees_not_configured`, eight kinds on both modes: a recording protector shows nothing is unprotected, the command log shows no query touches `SecretVersions`, the victim's snapshot is unchanged, the module gets `missing_secret`, nothing in the error names the reference, and exactly one W3 row is written (category `secrets`, elevated, outcome `rejected`, target the integration setting, the worker as actor, `path` `run`, the integration's context, and no reference, owner or secret name in any cell). `A_refusal_stands_and_nothing_foreign_is_read_when_the_evidence_cannot_be_written` (a writer that throws). SMTP: `A_row_sourced_smtp_reference_to_a_foreign_secret_reads_as_no_password…` (the operator is the actor, `path` `management`), `…to_the_instance_settings_own_secret_resolves`, and `A_deployment_configured_smtp_reference_is_outside_the_row_rule_and_keeps_working_whatever_secret_it_names`. Unit: `SettingSecretResolverTests` (owner passed, one row per call, actor and path, evidence failure, no leakage), `IntegrationSettingsAccessorSecretTests`, `SmtpPasswordRefOwnershipTests` (including the overlay feeding the resolver). |
| A3.5 | Met | SQL `A_foreign_guid_stored_as_ordinary_text_is_refused_at_read_once_the_key_becomes_sensitive` (the key is read as ordinary text first, then declared sensitive), `…_is_not_versioned_by_a_replace_after_the_key_becomes_sensitive`, and `Text_that_is_not_a_guid_is_refused_at_read_…`. |
| A3.6 | Met | Unit `IntegrationSettingSecretOwnershipDispositionTests` (the plan accepts the integration-specific name, refuses another key's and another integration's, retains one the other integration still holds, never retires another provider's, and the retirement re-checks the proven owner). SQL `IntegrationSettingSecretDispositionSqlServerTests.OwnerNames` (seven cases) next to the existing suite, whose legacy fixtures pass unchanged. |
| A3.7 | Met for the code; **the persistent-installation part is a rollout condition** (§3.8) | `SettingSecretOwnershipInventorySqlServerTests`: the script returns exactly the seeded problem rows with the right class, owner details and H1 evidence (sole holder, other holders by kind), every spelling of a foreign reference, ids and keys that carry the delimiter and the escape character (clean, so the script's escaping equals the platform's), and nothing for a clean or empty catalog; it runs as a database user that has `SELECT` on the five tables and nothing else, changes nothing and is repeatable; a static test pins that it contains no write, `DECLARE`, `INTO`, temporary table or database name and never selects a payload. |
| A3.8 | Met | SQL `An_ambiguous_legacy_reference_is_refused_at_read_and_never_adopted_or_renamed`, `Re_entering_the_value_of_a_shared_legacy_row_creates_an_owned_secret_and_retains_the_legacy_one_until_the_last_holder_has_moved`, `…_of_a_sole_holder_legacy_row_retires_the_legacy_secret`, `A_legacy_secret_that_any_other_holder_still_refers_to_is_never_retired_by_a_re_entry` (instance row, connection, Entra registration), `Clearing_or_resetting_a_sole_holder_legacy_row_…`, `A_legacy_secret_named_for_another_key_is_never_retired_…`, and the race `Two_integrations_re_entering_a_shared_legacy_secret_at_once_…`. The configuration-sourced SMTP reference is pinned by `A_reference_that_deployment_configuration_supplied_stays_outside_the_row_rule_and_keeps_working` (unit) and the SQL case above. The recovery procedure is `docs/03-operations/configuration-secret-ownership-upgrade.md`. |
| M6 | Met | `SettingsAuditSqlServerTests` runs the five M6 obligations (audit insert failure leaves nothing; a lost acknowledgement is verified, not run again; a confirmed rollback reruns once; an unknown outcome is wholly committed or wholly absent; the operation lock is not granted) for three new operations: a replace over another integration's secret (repoint), a re-entry over a sole-holder legacy secret (new secret, repoint and retirement), and a clear of a sole-holder legacy row. Each asserts the setting row, both secrets, the store's own evidence and the audit rows. The existing integration secret cases now seed the owner the setting defines. |

### 3.5 Test evidence

Counts are from this branch at the final candidate. Heavy suites ran one at a time (the machine is memory constrained). The SQL Server suites use throwaway catalogs and fail rather than skip without SQL Server.

| Suite | Result |
|---|---|
| `Apperture.UnitTests` (the whole project, final code) | 4823 passed (4660 at the end of SEC-2.2) |
| `Apperture.Management.Ui.Shared.Tests` | 1496 passed |
| `Apperture.Management.Ui.Client.Tests` | 523 passed |
| `Apperture.ArchTests` | 38 passed |
| `Apperture.AppHost.Diagnostics.Tests`, `Apperture.ModuleSdk.Testing`, `Apperture.EndToEndTests` | 7, 6 and 1 passed |
| `Apperture.BrowserTests` (opt-in; CI runs it in its own blocking job) | 71 passed, 62 skipped (not enabled locally) |
| `Apperture.IntegrationTests`, the whole project, on the first candidate (`52386ff`) | 2071 passed, 1 failed: `AuditRecorderSqlServerTests.A_rename_commits_with_one_complete_recorder_row_…`, a rename test of the audit recorder that this package does not touch. It passed on its own and with its whole class (22 of 22) immediately afterwards; the two-hour run held the machine at about 490 MB free. Treated as a load flake, not a product defect |
| After the review remediation: every SQL suite that exercises the changed code: `SettingSecretOwnershipSqlServerTests`, `SettingSecretOwnershipInventorySqlServerTests`, `IntegrationSettingSecretDispositionSqlServerTests` (both partials), `SettingsAuditSqlServerTests`, `IntegrationRowLockSqlServerTests` (both partials), `IntegrationLifecycleAuditSqlServerTests`, `AttentionRoutingSqlServerTests` and the Management host tests | 872 passed, 0 failed (37 minutes) |

New unit suites: `SettingSecretOwnershipTests`, `SettingSecretProvisionerOwnershipTests`, `SettingSecretOwnershipServiceTests`, `IntegrationSettingSecretOwnershipDispositionTests`, `SettingSecretResolverTests`, `IntegrationSettingsAccessorSecretTests`, `SmtpPasswordRefOwnershipTests`, `SettingSecretBoundaryGuardTests`, `SettingSecretResolverCompositionTests`. New SQL suites: `SettingSecretOwnershipSqlServerTests`, `SettingSecretOwnershipInventorySqlServerTests` and the `OwnerNames` partial of the H1 disposition suite, plus three new operations in the M6 matrix of `SettingsAuditSqlServerTests`. Existing expectations were changed in four places, each because of an approved change and none weakening a guarantee: the catalog count and the secrets-category write-class pin (two new W3 actions), the reasons of `integration.settings.secret.retired` (`replaced`), and the M6 integration clear, which now seeds the secret the setting owns instead of the legacy shape.

**Test-first status.** The ownership definition and the provisioner were written test first: the tests were written against stubs, run, and failed for the right reason (a foreign secret was versioned and disabled) before the implementation. The same order held for the classifier's legacy method. The boundary, the accessor, the SMTP changes, the SQL suites and the inventory were written with or just after the code; for those the evidence is the mutation testing below.

### 3.6 Mutation evidence

Each mutation was applied to the committed code, built, run against the tests named, and reverted; `git diff` against the commit was empty afterwards.

| Mutation | Caught by |
|---|---|
| The provisioner treats every descriptor as the setting's own (no provider and name comparison) | 29 of 51 unit tests (every foreign replace and disable shape, B disabling A, the whitespace case, the instance and integration service cases) and 25 SQL cases (every foreign kind for replace, B's clear) |
| The boundary resolves by reference alone, with the unchecked `ResolveAsync` | 13 of 25 unit tests (including the guard that pins the boundary's one unchecked call) and 22 of 26 SQL cases (sixteen accessor refusals, the ambiguous legacy reference, the SMTP refusal, the reclassification refusal) |
| Legacy retirement ignores the other holders | 5 unit tests and 10 SQL cases (every other-holder kind, the shared re-entry sequence, clear and reset) |
| The delete classifier accepts any integration-owned secret | 3 unit tests and 4 SQL cases (another key's secret, another integration's secret) |
| The platform-settings overlay never marks a reference as row-sourced | 1 unit test at the time (`A_reference_overlaid_from_the_setting_row_is_marked_as_row_sourced`); the end-to-end pair `A_reference_the_overlay_took_from_the_row_is_checked_for_ownership_when_it_is_read` was added because of it |

### 3.7 Legacy inventory and recovery procedure

The U-2 inventory is `scripts/sec2/Get-SettingSecretOwnershipInventory.sql` (Q-4: a read-only script, as for U-1). It applies H1's evidence model (provider, name, sole holder, and the surviving-reference scan across setting rows, connections and Entra registrations) to every `PlatformSettingValues` row and lists each row that needs attention, with one class each:

| Class | Disposition |
|---|---|
| `legacy_integration` | Refused at read. Re-enter the value; the legacy secret is retired only when `SoleHolder` is 1 |
| `shared` | Each holder re-enters; nothing is adopted; the secret is retained |
| `key_mismatch`, `foreign_provider` | Refused at read; the other secret is never modified; `foreign_provider` is an incident |
| `ordinary_guid_text` | Reported only |
| `dangling`, `unreadable` | Re-enter or clear |

The recovery path for an operator is the one the gate approved (P-21, SR-14): inventory the setting; the row is refused safely at read; re-enter the value, which creates a newly owned secret and repoints the row; the legacy secret is retired only on H1's evidence. It, the sensitive-key list, the connection inventory and the disposition of every class are in `docs/03-operations/configuration-secret-ownership-upgrade.md`, and the contract is in `docs/04-api/contracts.md`.

### 3.8 A2.7 and U-1: deployment disposition (assessment, separate from SEC-2.3's code acceptance)

**Question.** Does any existing persistent installation require the U-1 (and now U-2) inventory?

**Evidence reviewed** (read-only, at `master` `5e27206`, and the repository's GitHub metadata):

| Source | What it shows |
|---|---|
| `deploy/` (`README.md`, `config/examples/*.example`, `docker/.env.example`, `docker-compose.dev.yml`) | Templates and development files only. Every connection string is a placeholder or a `localhost` development database. No installation, customer, host, server or catalog is named. |
| `deploy/README.md` (RC1 note, 2026-10-03) | The RC1 production topology is delivered by OPS-1 and "does not exist yet: there is no installer, no service hosting and no supported upgrade or backup procedure today". |
| `scripts/deployment/commands/Deploy-Environment.ps1` | A scaffold: it prints that the target's workflow "is not implemented". |
| `CHANGELOG.md`, git tags, GitHub | `[Unreleased]` only; one tag (`archive/foundational-kernel-and-domain`); zero releases, zero deployments and no environments on the repository. |
| `docs/00-overview/pre-release-product-roadmap.md` | Pre-release product development; the operator smoke runbooks and UAT are stated as not claimed passed. |
| `docs/01-architecture/transitions/audit-hardening/aud1-design-gate.md` (line 904) | Says that whether a deployment already ran IAM-9.2 without AUD-1.1 "is operator knowledge": the program itself does not treat the repository as a record of installations. |

**Assessment.** The evidence is consistent with there being no persistent installation: nothing in the repository, its releases or its deployment tooling describes one, and the supported upgrade path does not exist yet. It does not establish that there is none. The repository holds no deployment inventory to be complete or incomplete, and a hand-built UAT, pilot or demonstration database would leave no trace in it. Absence of names is not treated as absence of installations.

**Disposition: A2.7 and U-1 remain open RC1 rollout conditions, unchanged.** SEC-2.3 neither closes nor changes them.

**What would close them.** Either of:

1. The inventories (this package's U-2 script and SEC-2.2's U-1 script) run on each persistent installation database named in the deployment records, with the results and each row's disposition recorded here; or
2. The owner (or whoever holds the deployment records) records one statement: *"As of <date>, no persistent database exists other than disposable development and test databases; no installation has been upgraded or will be upgraded from a pre-SEC-2 build."* With that, A2.7 would be recorded not applicable for the first release, and the two inventories would stay as a documented upgrade requirement for any later installation ([the upgrade runbook](../../../03-operations/configuration-secret-ownership-upgrade.md)).

No database name was invented, nothing was marked closed, and no database was inspected beyond the throwaway catalogs the tests create.

### 3.9 Database safeguards

- No development, shared, persistent or production database was reset, reseeded, migrated or written to by hand. SR-17 was not exercised.
- The SQL Server suites create their own uniquely named catalogs (and drop them), as SEC-2.2 did. `SettingsAuditSqlServerTests`, the H1 disposition suites and the new suites each use such catalogs.
- The only other use of the shared test SQL container was read-only: a probe of two JSON behaviours of the engine (`SELECT` with literals, no table), with the credential forwarded through the environment and never on a command line. The existing `apperture-test-sftp` container, which was stopped, was started for the host tests and stopped again afterwards.
- The owner's verified backup of the primary development database was not needed and was not touched. The Aspire stack that is running on the machine was left running; nothing was built into its output folders (all builds used `artifacts/sec23`, which is git-ignored and removed at the end).

### 3.10 Limitations and other items

| # | Item |
|---|---|
| L1 | **A2.7 is open** (§3.8). Enforcement does not wait on the inventories (gate §6.3), so a setting row that names a foreign secret reads as not configured after the upgrade until its value is re-entered. |
| L2 | **An instance row that names the legacy integration shape for the same key is indistinguishable from an owned one** (D2; accepted by P-18). |
| L3 | **Readiness reports presence only** (D13): an integration whose row names a foreign secret reads as configured and fails closed at run, with an audited refusal. |
| L4 | **`IsSecretConfigured` is presence** (D5). |
| L5 | **The inventory needs compatibility level 130**, reads the other holders by text (the hexadecimal GUID form is not found), and treats an array or an object value as not a reference (D11). |
| L6 | **A retirement on re-entry reads the other holders in the change's transaction.** Under locking READ COMMITTED a competing uncommitted change is waited for; under snapshot isolation it is not seen, so a legacy secret can be kept longer than needed. It is never retired while a committed row names it (the concurrent re-entry test pins both modes). |
| L7 | **The Entra client secret and the other holders that resolve a reference are unchanged** (gate K-3). `EntraClientSecretAccess` is the one remaining production file outside the boundaries that builds a `SecretRef`, and the guard names it with that reason. |
| L8 | **The guard is identifier-based** and cannot see a reference passed through a variable defined in another file; the allow-list is three files and the runtime tests are the compensating control. |
| L9 | **`PlatformSettingValues` row ids are not escaped** (D1): `("a:b","c")` and `("a","b:c")` collide as primary keys, which is existing behaviour. It does not weaken the ownership rule, whose names are escaped. |
| L10 | **The sole-holder scan sees database locations only.** A reference that deployment configuration supplies (`Notifications:Email:SmtpPasswordRef`) is invisible to it. A legacy-shaped integration secret that the configuration also names could be retired by re-entering or clearing that integration's value. It needs a module to declare an integration setting with the same key as the instance one, plus a legacy row. The gap already existed in H1; the runbook now says to point the configuration at a secret nothing else owns first. |
| L11 | **A notification delivery or test send is recorded as the platform, not the operator.** `SmtpEmailNotificationChannel` resolves with path `run`, so a refusal there is attributed to `system:platform-settings` even when an operator triggered the send (D6). Account mail is `management`. |
| L12 | **An owner decision on readiness (D5, D13) is still wanted.** `IsSecretConfigured` and readiness report that a reference is stored, so a row that names another holder's secret reads as configured until the module asks for the secret, when it gets `missing_secret`. Making readiness read the descriptor is a larger change and was not done. |

### 3.11 Independent review

An independent reviewer (a separate subagent run with the Opus model at high effort, as the assignment asked; given the approved gate and the code at `52386ff` and none of this record's conclusions) read `5e27206..52386ff` against the gate and was asked for an explicit readiness verdict. It was read-only: it ran no build, test or database command.

**Verdict: READY WITH CONDITIONS.** No critical or high finding. Its conditions were (1) record the test evidence and the CI result, and (2) decide F-1. Both are addressed below. It confirmed that integration B's replace, clear, reset, delete or read cannot read, version, disable or adopt integration A's secret, and checked the owner rule and its escaping (including that the script's `REPLACE` order equals the C# one), the exact comparison, the provisioner, the three services and the in-transaction reset, the legacy disposition and its concurrency behaviour, the delete classifier and retirement, the read boundary and its audit shape, the SMTP source marker, read and write path completeness, the DI of every host, the inventory script, and whether the tests would fail if their defects returned.

| # | Finding | Severity | Disposition |
|---|---|---|---|
| F-1 | The owner name is built from the id spelling the caller supplied, not the integration's stored id. SQL finds the integration case-insensitively but the comparison is ordinal, so `PUT .../ABC.../settings/K` for an integration stored as `abc...` creates a secret the worker (which uses the stored id) is then refused, orphans it on a later re-entry, and a delete through that spelling is refused as ambiguous. No cross-integration exposure. | Low | **Fixed.** `LockIntegrationAsync` returns the stored id and every unit then uses it for the owner, the row's scope and the audit target; the delete classifies by the locked entity's id. Unit tests (replace, clear and reset through an upper-case route) and the SQL test `A_route_that_spells_the_integration_id_in_another_case_…` on both isolation modes (the row, the descriptor name and the worker's read). |
| F-2 | `IsConfigured` and `IsSecretConfigured` still report configured for a row that names a foreign or legacy secret; only `GetSecretAsync` reports not configured. | Low | **Accepted, owner decision requested** (D5, D13, L12). The gate's A3.4 and P-19 are met where the module asks for the secret. |
| F-3 | `ForIntegrationSetting` throws when the escaped name exceeds 256 characters, and clear and reset called it unguarded, so such a row (poisoned or historical) could never be cleared. | Low | **Fixed.** The owned disable treats that as "no owned secret" and the legacy path still runs. Unit tests for clear and reset of such a row. |
| F-4 | The guard's list of setting readers omitted the repository and the stored-row types, so a reader that gets a `SecretRef` from a helper and holds a resolver could pass. | Low | **Fixed.** `IPlatformSettingValueRepository`, `PlatformSettingValueEntity` and `PlatformSettingValues` are readers, with a self-test for each. No production file trips them. |
| F-5 | The sole-holder scan cannot see a deployment-configured reference. | Informational | **Documented** (L10, the runbook's limits). |
| F-6 | A notification test send or delivery started by an operator is recorded as `run` and a system actor. | Informational | **Accepted and documented** (L11). |
| F-7 | The script trims ASCII whitespace; the platform trims all Unicode whitespace, so such a padded reference would be reported unreadable. | Informational | **Documented** in the script's fidelity notes (it over-reports and never hides a row). |

Not verified by the reviewer, and checked afterwards: that `DeleteScopedAsync` and `UpsertAsync` save before the H1 scan reads (`SqlPlatformSettingValueRepository` calls `SaveChangesAsync` in both, inside the unit's transaction, and the sole-holder SQL cases depend on it); that account mail is never sent while an audited unit holds locks (`AccountSecurityMailDelivery` is documented and written as "never inside a transaction", sent only after the request committed, so its refusal row's separate connection cannot wait on the unit); and the catalog collation behind F-1 (the SQL regression test finds an integration through an upper-case id, so the test catalogs are case-insensitive, as SQL Server's default is). The reviewer could not run SQL Server, observe either isolation mode, or run any suite.

### 3.12 CI

- **First run** (`409f8c4`, run 38666014704): `browser-security` passed; `build-and-test` failed 150 tests, every one in the Management Host classes that use the governance fixture (`ManagementHostReliabilitySmokeTests`, `ManagementHostGovernanceHttpTests`, `BrowserRequestTrustHttpTests`, `ManagementHostConnectionToolsHttpTests`, `ManagementHostConnectionSecretFieldsHttpTests`, `ManagementHostIntegrationConfigurationHttpTests`, `ManagementHostAttentionIncidentAcknowledgementHttpTests`, `ManagementHostBreakGlassHttpTests`). Each reported that no SQL connection string was configured and that it had tried LocalDB. The other 1,924 integration tests passed, and the same classes passed in both of my full local runs.
- **Cause.** Nothing in SEC-2.3 reads or sets that variable. `SqlServerTestConfigurationTests` clears and sets process-wide environment variables (`APPERTURE_TEST_SQL` among them) and had no collection, so xUnit ran it in parallel with the governance fixture, which reads `APPERTURE_TEST_SQL` when it starts. The log has the two within 200 ms of each other at the start of the run (the configuration tests passing at 01:33:27.09, the first fixture failure at 01:33:27.27). It is a latent race that the added test classes' ordering most likely exposed. **It did not reproduce locally in three attempts**, so the explanation rests on the log and on reading the code, not on a red-then-green run.
- **Fix.** `SqlServerTestConfigurationTests` joins the existing serialized `SqlServerIntegration` collection, so it runs alone. It is one attribute, in a test file this package had not otherwise touched.
- Final CI result: see the pull request.

---

## 4. SEC-2.4: Entra probe telemetry (F2)

### 4.1 What changed

| Area | Change |
|---|---|
| Schema | New table `EntraRegistrationDiagnostics` (1:1 with `EntraInstanceRegistrations`, keyed and foreign-keyed by `RegistrationId`, `ON DELETE CASCADE`). It holds the eight fields that used to be columns of the registration row: the OpenID status, last validation time, summary and detail, and the directory status, last discovery time, summary and detail. The two status columns default to 1 (`NotValidated`, `NotAttempted`), as the old columns did. |
| Migration | `20261014120000_Sec24_EntraRegistrationDiagnostics` (hand-written with attributes and no designer file, like `Aud14_BindingTargetType`; the model snapshot is edited to match and `AppertureDbContextModelConsistencyTests` passes). `Up` creates the table, copies the eight values of every registration, then drops the old columns. `Down` restores the columns with their types and defaults, copies the values back and drops the table. |
| Writers | `SqlEntraInstanceRegistrationRepository.UpdateOpenIdValidationOutcomeAsync` and `UpdateDirectoryDiscoveryOutcomeAsync` each issue one set-based update of the diagnostics row. They no longer read, track or update the registration row, so `UpdatedUtc` and `rowversion` are untouched. A missing diagnostics row is created by a guarded `INSERT ... SELECT` that tolerates a racing probe and a concurrent delete. `AddAsync` creates the row with the registration in the same save; a delete removes it by cascade. `UpdateAsync` and `SwitchActiveAsync` never touch it. |
| Readers | Every read joins the diagnostics row and `ToDomain` maps a missing row to the defaults. `EntraInstanceRegistration`, `IEntraInstanceRegistrationRepository`, the controller, the DTOs, the Management client, the overview service and the UI are unchanged, so the list, the detail, the overview and the directory pages show what they showed. |
| Authorization | Unchanged. No permission, route, status code or response shape changed; the controller was not edited. |
| Audit | No catalog action. Probe outcomes are operational telemetry (P-7, P-10). |
| Guard | `Aud14ClosureGuardTests`: the new table is an in-scope set; the repository's `Writers` entry classifies the probe writes as operational telemetry; the probe members are pinned to write only that table (§4.4). |
| Existing tests | Two raw `INSERT`s and one entity read in existing tests followed the schema (they named the removed columns). |

### 4.2 Decisions taken inside the gate

| # | Decision |
|---|---|
| D1 | **The old columns are dropped in the same migration, after the copy.** Leaving them would be a second source of truth (the assignment forbids it) and would let a stale writer keep working. The migration is additive in the sense that matters for data: it creates, copies, and only then removes; `Down` returns the data. |
| D2 | **The domain model and repository interface are unchanged.** The diagnostics stay on `EntraInstanceRegistration` as read-only context for every reader, so no client needs a new endpoint and the test doubles of the interface needed no change. |
| D3 | **A probe is one set-based statement, not a read-modify-write.** The common path is `ExecuteUpdateAsync` on the diagnostics row alone: no tracked entity, no `SaveChanges` on a context that may hold other tracked changes, no read of the registration, and the two probe kinds cannot overwrite each other's columns. The last statement to arrive is the one recorded; there is no concurrency token on the diagnostics row (as before, a probe was last-writer-wins). |
| D4 | **The fallback insert exists because rows exist without a diagnostics row.** The migration gives every registration one and `AddAsync` creates one, but tests and any other direct insert do not. The fallback inserts only when the registration exists and has no row, sets the probe's own group and leaves the other at its defaults. It reads the registration (for its id) and never writes it. |
| D5 | **Three SQL errors are swallowed in the fallback, and only there:** duplicate key (2627, 2601: a concurrent probe created the row first) and foreign key (547: the registration was deleted between the check and the insert). In each case the outcome is recorded on the row that now exists, or nothing is recorded for a registration that is gone, which is what the old code did for a missing registration. Any other failure propagates. |
| D6 | **Remote text is bounded once, before both write paths.** The summary is cut to 512 characters and the detail to 4000, the column lengths. Before, an over-long message made the update fail (the column is bounded and the remote error text is not); a fixed-size parameter on the insert path would have cut it silently. Both paths now store the same text. |
| D7 | **The guard names the table and the members.** The diagnostics table joins `InScopeSets`, so no other file can write it unreviewed, and the repository's `Writers` entry lists it as operational telemetry. The registration table stays in scope with its audited path. A separate test pins the probe members (and every same-file member they call) to a write of the diagnostics table only (§4.4). |
| D8 | **`HasDefaultValue(1)` on the status columns.** It reproduces the old database defaults in the model. EF reads a CLR 0 (`Unknown`) as "not set" on an insert, so an EF insert must not carry `Unknown`; the only EF insert (`AddAsync`) already maps it, and the probes write with SQL. A comment says so. |

### 4.3 Acceptance matrix

All SQL cases run on a locking READ COMMITTED catalog and a READ_COMMITTED_SNAPSHOT catalog (throwaway catalogs the tests create and drop), except the migration cases, which are independent of isolation mode.

| # | Status | Evidence |
|---|---|---|
| A4.1 | Met | SQL `EntraProbeTelemetrySqlServerTests`, both modes: `A_probe_changes_nothing_on_the_registration_row_and_the_open_edit_form_still_saves` (an administrator opens the form with rowversion V; an OpenID validation and both directory searches run; every column of the registration row, including `UpdatedUtc` and the rowversion, is identical; the probes' outcomes are on the diagnostics row; the form saves with V and answers 200 with the probes' diagnostics; the edit leaves the telemetry alone); `A_genuine_competing_configuration_edit_still_conflicts_whatever_the_probes_do` (the second editor gets 409 after a real edit and a probe between); `A_probe_is_not_blocked_by_a_lock_held_on_the_registration_row_and_leaves_the_row_alone` (a transaction holds the registration row's exclusive lock; a control write to that row times out with 1222, so the lock is real; both probes finish); `Probes_and_a_configuration_edit_that_run_together_do_not_overwrite_each_other` (eight probes and an edit at once: the edit gets 200, and each group is the whole outcome of one probe). |
| A4.2 | Met | SQL `EntraRegistrationDiagnosticsMigrationSqlServerTests`: a catalog migrated to `Aud14_BindingTargetType` is seeded in the old schema with six registrations (never probed; both probes succeeded with values at the column limits and non-ASCII text; both failed, with NULL fields; edge statuses including a stored 0; OpenID only; directory only), then upgraded through `AppertureDatabaseMigrator`. All eight values of every registration are identical, the configuration rows are identical including `rowversion` and `UpdatedUtc`, the old columns are gone, and the new table's keys, lengths, nullability, defaults and cascade foreign key are as specified. The repository (list, detail, active), the controller's list and detail, and the authentication overview read the seeded values. After the upgrade a created registration has its row, a delete removes it, and a migrated registration is probed and edited normally. `Rolling_back_…` runs `Down` (columns, defaults and data restored, telemetry recorded after the upgrade carried back) and applies the upgrade again. Lifecycle on both modes: `The_diagnostics_row_is_created_with_the_registration_and_removed_with_it`, `A_registration_inserted_without_a_diagnostics_row_reads_the_defaults_and_racing_first_probes_create_one_row`, `A_probe_of_a_registration_that_does_not_exist_records_nothing`. |
| A4.3 | Met | `Aud14ClosureGuardTests` (§4.4): `InScopeSets` and the repository `Writers` entry name `EntraRegistrationDiagnostics` as operational telemetry; `G5_probe_telemetry_writes_only_the_diagnostics_table` (closure of the probe members, interface cross-check); its self-tests (a probe that writes the registration row by a tracked save, a set-based update, a raw update, or through a helper is detected); `G5_the_probe_fields_live_on_the_diagnostics_entity_and_not_on_the_registration_entity`; `G1_no_audit_action_records_an_entra_probe`. No existing entry was weakened (§4.4). |
| A4.4 | Met | Controller level on SQL, both modes: `A_caller_without_the_permission_is_refused_and_no_probe_runs_or_is_recorded` (no permission: all three refused, nothing runs; `auth.configuration.read`: searches succeed, validation refused; `auth.entra.manage`: validation succeeds and the searches are accepted). Real Management Host pipeline, `ManagementHostEntraProbeAuthorizationHttpTests`: a reader searches users and groups (200) and the searches update telemetry only, leaving the registration's rowversion and `UpdatedUtc`; a reader is refused `validate-openid` (403, the validator never runs, no telemetry row); a manager runs the validation and a search; a principal with neither permission gets 403 and an unauthenticated caller 401 on all three routes, nothing runs and no diagnostic state is created. `BrowserRequestTrustHttpTests` (the SEC-1B2 directory-search rules) passes unchanged except for reading the outcome from the new table. |
| No audit row, no search value | Met | `A_probe_writes_no_audit_row_and_stores_no_search_value`, both modes: `AuditLogs` is unchanged across a validation and two searches, and the search value reached the directory call and is in no diagnostics column and no audit row. |

### 4.4 Closure guard (A4.3)

- `InScopeSets` gains `EntraRegistrationDiagnostics`. The scanner therefore reports any file that writes the table, and the existing rule that a new writer file fails the test applies. The repository's `Writers` entry now lists both sets, with the reason: the registration set is audited by the controller's units; the diagnostics set is operational telemetry, unaudited by decision (P-7, P-10, SR-7).
- The set-level guard alone cannot stop a regression inside the repository file, which is allowed to write the registration table. `G5_probe_telemetry_writes_only_the_diagnostics_table` therefore reads the probe members and every member of that file they call, transitively and across overloads, runs the writer scanner over that text, and requires exactly `EntraRegistrationDiagnostics`. It also forbids `UpdatedUtc`, `RowVersion`, `SaveChanges`, `Entry(`, a registration-set access and the word `Audit` there, forbids reaching `UpdateAsync` or any other configuration member, and requires every member of `IEntraInstanceRegistrationRepository` to be either a listed configuration operation or a listed probe, so a new way to write must be reviewed.
- Self-tests feed the scanner a probe that updates the registration by a tracked save, by `ExecuteUpdate` and by raw SQL, a helper-based bypass with an overload, and the diagnostics `ExecuteUpdate` and the insert-select (a read of the registration is not a write).
- `G1_no_audit_action_records_an_entra_probe` pins that the catalog has no action for a probe.
- General audit coverage is not weakened: nothing was removed from `InScopeSets`, `Writers` or any other list, and the whole `Aud14ClosureGuardTests` class passes.

### 4.5 Test evidence

Heavy suites ran one at a time (the machine is memory constrained). The SQL Server suites use throwaway catalogs and fail rather than skip without SQL Server (`AuditSqlScratch`, the governance fixture's hard failure).

| Suite | Result |
|---|---|
| `Apperture.UnitTests` (the whole project, final code) | 4832 passed (4831 at the review; the guard's helper-bypass self-test was added) |
| `Aud14ClosureGuardTests` and `AppertureDbContextModelConsistencyTests` (final code) | 87 passed |
| `Apperture.ArchTests`, `Apperture.Management.Ui.Client.Tests`, `Apperture.Management.Ui.Shared.Tests` | 38, 523 and 1496 passed (run before the review remediation, which changed only the repository, the guard and SQL tests, none of which these projects reference) |
| `EntraProbeTelemetrySqlServerTests` | 24 passed (12 cases, each on both isolation modes) |
| `EntraRegistrationDiagnosticsMigrationSqlServerTests` | 5 passed |
| `ManagementHostEntraProbeAuthorizationHttpTests` | 4 passed |
| `BrowserRequestTrustHttpTests` (the SEC-1B2 directory-search cases read the outcome from the new table) | 21 passed |
| Broad SQL run: the whole `IntegrationTests.Audit` and `IntegrationTests.ManagementHost` namespaces, the migration suites and `UsernameFoundation`, on the code before the review remediation | 2020 selected; 1847 passed and 173 failed. **Every one of the 173 was an environmental failure and none an assertion**: from about 01:29 UTC the test SQL Server stopped answering (connection timeouts in the pre-login handshake and in fixture creation, with about 0.6 GB of the machine's 32 GB free), which failed 172 cases across eight classes, and the stopped `apperture-test-sftp` container failed the one SFTP governance case. After the stall the same eight classes were rerun in full: **177 of 177 passed**. `ManagementHostGovernanceHttpTests` with the SFTP container started: 48 of 48 passed. |
| New and neighbouring SQL classes after the review remediation (the four rows above, one run) | 54 of 54 passed. Earlier attempts on the recovering server failed on connection timeouts only. |

The whole `Apperture.IntegrationTests` project (2020+ cases, hours) was not run locally; the pull request's CI runs it. Everything this package can affect was.

### 4.6 Mutation evidence

Each mutation was applied to a committed tree, built, run against the tests and reverted with `git checkout`; the tree was verified clean after each.

| # | Mutation | Result |
|---|---|---|
| M1 | A probe also updates the registration's `UpdatedUtc` (the old behaviour) | Killed: `G5_probe_telemetry_writes_only_the_diagnostics_table`, `ManagementHostEntraProbeAuthorizationHttpTests.A_manager_…`, and eight SQL cases (the form-saves case, the first-probe-race case, the concurrent case, both modes; the lock case waits for its 20-second cancellation in both modes, which is the old blocking) |
| M2 | The migration copies no rows | Killed: four of the five migration cases (the fifth checks the schema only) |
| M3 | No fallback insert | Killed: the missing-row cases, two HTTP cases and the `BrowserRequestTrustHttpTests` directory-search case |
| M4 | The duplicate-key handling removed | **Survived the first version of the race test**, which rarely reached the collision. The test was replaced by a deterministic one (a probe held just before its insert, a concurrent first probe inserting in an uncommitted transaction, then a commit): under row versioning the loser's insert meets the key (one duplicate-key failure counted and recovered), under locking it waits and reads the committed row. The mutation then failed with the real `SqlException` under row versioning. |

### 4.7 Database safeguards

- No development, shared, persistent or production database was reset, reseeded or edited by hand. SR-17 was not exercised. The owner's backup of the primary development database was not needed.
- All new SQL cases create uniquely named throwaway catalogs and drop them, except the Management Host cases, which use the governance fixture's existing test catalog like their neighbours. That catalog is migrated forward by the fixture, which is the upgrade of an existing database in miniature, and the cases write only rows with unique ids.
- The Aspire stack was left alone; every build used `artifacts/sec24` (git-ignored, removed at the end). SQL credentials were read by the test configuration from the repository's `.env` and were never put on a command line.
- **Test infrastructure was touched twice, both times narrowly.** The `apperture-test-sftp` container was stopped at the start; it was started once to run the one governance case that needs it (unrelated to this package) and stopped again. During the broad run the machine ran out of memory headroom, Docker Desktop stopped answering and the test SQL Server (`apperture-test-sql`) hung; **that one container was restarted once** (`docker restart apperture-test-sql`; its data is on its volume and the test catalogs are disposable). Docker Desktop itself and the other containers (the portal, the Aspire SQL on port 14333 and the rest) were not touched.

### 4.8 Independent review

An independent reviewer (a separate subagent run with the Opus model at high effort, as the assignment asked; given the approved gate, the assignment's requirements and the code at `071ff1d`, and none of this record's conclusions) read `e1e9b2e..071ff1d` and was asked for an explicit readiness verdict. It was read-only: it ran no build, test or database command.

**Verdict: READY WITH CONDITIONS.** No blocker or major finding. Its two conditions were (1) add this record and the status updates, and (2) confirm that the Management Host HTTP cases ran green; both are met (this section; §4.5). It confirmed the migration's order and fidelity and the snapshot by reading, that no probe path touches the registration row, the upsert under both isolation modes, every reader and writer (enumerated independently with `git grep`, with no reference left to a removed column), authorization unchanged, no new log or audit of search values, and that the guard only adds entries.

| # | Finding | Severity | Disposition |
|---|---|---|---|
| M-1 | The A4.3 guard scanned only the three named members, so a probe that called a new helper, `UpdateAsync` or an unlisted overload would pass. | Minor | **Fixed.** The guard follows same-file calls transitively and across overloads, forbids reaching a configuration member, and cross-checks the repository interface (§4.4), with a self-test for the helper bypass. |
| M-2 | The swallowed foreign-key error (547) branch was never exercised. | Minor | **Fixed.** `A_first_probe_whose_registration_is_deleted_while_it_creates_the_row_records_nothing_and_does_not_fail`, both modes (a probe held before its insert, an uncommitted delete, then a commit). The reviewer's hypothesis held: under row versioning the insert reads the committed registration, inserts, and the foreign key check fails (one 547 counted and handled); under locking the insert waits and writes nothing. |
| M-3 | The fallback insert reads the registration, so it waits behind an in-flight change to it, and against a delete of the same registration it can deadlock (error 1205). The class summary overstated "not blocked". | Minor | **Accepted and documented** (L3); the summary is narrowed. Only a registration without a diagnostics row uses the path, which production data does not have. |
| M-4 | An over-long remote message was cut on the insert path and failed the update path. | Minor | **Fixed** (D6); `Over_long_remote_text_is_stored_cut_to_the_columns_on_both_write_paths`, both modes. |
| M-5 | The search value can still reach stored telemetry if Graph echoes its `$filter` in an error message that the service puts in `TechnicalDetail`. | Info, pre-existing | **Documented** (L4). Not introduced here; the new tests use a scripted discovery with fixed text, so they cannot detect it. |
| N-1 | A swallowed duplicate or foreign-key error still produces an EF error-level log line for the failed command. | Nit | Accepted. The line names no value but the registration id. |
| N-2 | `HasDefaultValue(1)` makes EF store 1 for a CLR 0 on an EF insert. | Nit | **Documented** by a comment (D8); no behaviour changes. |
| N-3 | Last writer wins by statement arrival, so an older, slower probe can overwrite a newer result. | Nit | Accepted: as before. |
| N-4 | The migration test compares `DateTimeOffset` by instant, not by offset. | Nit | Accepted: the column type does not change. |
| N-5 | `CreateAtMigrationAsync` leaked its catalog if the migration threw. | Nit | **Fixed.** |

Not assessed by the reviewer: it ran no build or test, so it could not confirm the snapshot against the model (`AppertureDbContextModelConsistencyTests`, which passes), or the lock and foreign-key behaviour behind M-2 and M-3 (now exercised by the new tests).

### 4.9 Limitations and other items

| # | Item |
|---|---|
| L1 | **A2.7 and the persistent-installation part of A3.7 are open and untouched.** SEC-2.4 shares nothing with them. They close only on the owner's applicability determination and evidence. |
| L2 | **`Down` replaces the `rowversion` of every registration row** (the copy-back is an update). `UpdatedUtc` is not changed. A rollback invalidates open edit forms, which is acceptable for a rollback. The upgrade does not replace the rowversion. |
| L3 | **The fallback insert is not independent of the registration row** (M-3). It exists only for a registration that has no diagnostics row. |
| L4 | **Stored remote text is unchanged in content** (M-5). Whatever Graph or the discovery service puts in the summary or detail is stored, now bounded to the column lengths. The directory service's treatment of that text is a separate matter. |
| L5 | **A configuration edit's read joins the diagnostics row.** Under locking READ COMMITTED it can wait for one in-flight single-statement probe update; it takes no lock on it afterwards. It cannot deadlock with a probe on the common path. |
| L6 | **`SwitchActiveAsync` still sets `UpdatedUtc` on every registration** (existing behaviour, a configuration operation inside the activate unit). It is not a probe and was not changed. |
| L7 | **Who may search the directory** stays an IAM-10.0 input (P-9, SR-9). No permission was added. |
| L8 | **The AUD-1.4 record's §19.7 row for F2 still reads "Unassigned"**; it is a historical statement of the finding, left as written like F1's row. |

### 4.10 CI

Final CI result: see the pull request.
