# SEC-2 implementation record

| | |
|---|---|
| Date | 2026-10-13 |
| Contract | [SEC-2.0 design gate](sec2-configuration-secret-and-operator-boundary-design-gate.md), approved 2026-10-13. The gate is binding; this record states what was built against it and the evidence. It changes no ruling. |
| Baseline | `master` at `8f795f0` (PR #28, the SEC-2.0 merge) |
| State | **SEC-2.1 implemented and merged** (PR #29; F1 closed). **SEC-2.2 implemented** on branch `sec-2.2-connection-secret-ownership` (base `8959b34`), with an independent review (§2.7). It closes U-3 and the code side of U-1 when its pull request merges and CI is green; **U-1 stays an RC1 blocker until the inventory of every persistent installation is recorded (A2.7, open).** **SEC-2.3 and SEC-2.4 have not started.** |

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
