# AUD-1.4 configuration lifecycle: implementation record

Date: 2026-10-07

Status: **In progress. Slices 0 to 3 are done and merged to `master` (PR #18, merge commit `54ae117`, 2026-10-11); slice 3 is CLOSED (§14). Slice 4 is done and merged to `master` (PR #23, merge commit `b38f1fd`, 2026-10-12, post-merge CI succeeded); it is CLOSED (§15.8). Slice 5 is done and merged to `master` (PR #24, merge commit `ce7d516`, 2026-10-12); it is CLOSED (§16.11). Slice 6 is done and merged to `master` (PR #25, merge commit `30e760e`, 2026-10-12, post-merge CI succeeded); it is CLOSED (§17.11). Slice 7 is in progress on its branch (§18). Slice 8 has not started.** Slice 0 (this record and the gate clarifications) and slice 1 (secret-store transaction participation) were implemented, validated (§4.7), independently reviewed, and corrected for the review's bounded findings (§4.10). Slice 2 (platform and integration settings, §8) was independently reviewed and corrected for the review's findings (§9), and closed (§9.10). Slice 3 (§10 to §13: the schedule delete, the integration row locks, the H1 mechanisms, the audited integration delete, and the remediation and closing record of work package 5) was accepted by the owner together with work package 5 and merged with PR #18, and its post-merge CI passed (§14). Work packages 1 and 2 were independently reviewed and accepted; work packages 3, 4a and 4b were independently reviewed together with the verdict READY TO ACCEPT (§13.1). This record grows with each slice; nothing below marks a later slice complete.

Base: `origin/master` at `cbe0bdb` (merge of PR #17, AUD-1.3). Post-merge CI run 37694556687 succeeded on that commit. The merge tree equals the reviewed AUD-1.3 head `cd964b6`. Before PR #18 merged, the branch took `origin/master` (`0314419`, the licensing and third-party notice merges) in a normal merge, `539d907`, which changed no file under `src/` or `tests/` and left the 109-file AUD-1.4 change set unchanged (§14).

Contract: [AUD-1.0 design gate](aud1-design-gate.md), revision 3, §22 "AUD-1.4 Configuration lifecycle", with the owner rulings of 2026-10-07 recorded there. Builds on the [AUD-1.1 foundation](aud1-1-foundation-record.md), the [AUD-1.2 identity and governance record](aud1-2-identity-governance-record.md) and the [AUD-1.3 operator action record](aud1-3-operator-action-record.md). Governing program contract: [rc1-program-gate.md](../../rc1-program-gate.md), RC1 requirement 5.

Labels: **Owner ruling** (approved by the product owner, binding); **Contract** (gate or earlier record text); **Package decision** (P-n, made here within the contract, open to review); **Finding** (outside this package, with an owner).

## 1. Scope

The gate's AUD-1.4 entry:

- Convert the existing configuration writers, including both deletes (schedule and integration), with verification.
- Add Part 1 AUD-1.4 coverage: integration create, update, activate, deactivate and retire; schedule create, update, enable and disable; binding changes; notification definition create, update and enable; subscriptions; managed contacts; attention routing; notification diagnostics live sends.
- Route secret-store events through the recorder.
- Handle the runtime-shutdown duplicate case.
- Team events stay in LST-1.4.

Acceptance: M7 for each action; M6 for each delete, including a lost acknowledgement followed by an apparently missing target; no sensitive setting value recorded.

The earlier records hand these families to AUD-1.4 as well (AUD-1.2 record §1, D11; AUD-1.3 record §1, §9): `resource_context.*`, ownership moves, the runtime shutdown request and its `RequestedBy`, settings, secret store, module lifecycle, connections and notification definitions, and the platform and integration setting secrets' "activate a new version on the live secret before the change" pattern (AUD-1.2 record §8).

The deletes stay hard deletes. AUD-1.4 changes their write mode (gate §13.6), not their lifecycle: the API contract keeps "no tombstone and no undelete" (`docs/04-api/contracts.md`, schedules).

Out, with owner:

| Not in AUD-1.4 | Owner |
|---|---|
| Attention contact groups (stored as operational teams) and their membership | LST-1.4 (owner ruling 2) |
| Operational teams and membership | LST-1.4 (gate §22) |
| Query contract, cursor, visibility, module discovery as routine, the denial actor | AUD-1.5 |
| Timeline and run-page actor presentation, `UserRef` adoption on AUD-1 surfaces | AUD-1.5b |
| Raw `RequestedBy` on the runtime shutdown request pages | LST-1.3 (gate §18 rows 8 and 9) |
| Enforcement M2 to M5, the T-B privileged credential, the retention document; broad `InvalidOperationException` catches on the AUD-1.2 endpoints; the unit not refusing pending changes already tracked when it opens | AUD-1.6 |
| The three run-runtime findings of the AUD-1.3 record §12.1 | Unowned runtime correctness |
| IAM-10, IAM-11, OPS-1 and RCV-1 findings | Their epics |

## 2. Owner rulings (2026-10-07)

The product owner approved the four orientation recommendations. They are recorded in the gate (§22 AUD-1.4, §27) and change no requirement.

| # | Ruling | Consequence here |
|---|---|---|
| D-1 | **Secret audit identifiers.** A secret-store event is a Secrets-category recorder row whose target is the owning configuration entity. Never recorded: secret values, secret references, credential material, connection strings, tokens, private keys, sensitive setting values | Gate §12.3 already excludes references, and a store id is the reference a configuration row holds, so no secret id appears in a target, a field or a name. The `secret` target type stays in the vocabulary (§11.1) and is not used; a catalog test pins that no action declares it. `SecretAudits` stays separate (gate §7) |
| D-2 | **Attention routing and contact groups.** AUD-1.4 owns the routing rules: instance defaults, integration override upsert and clear. Contact groups, stored as `OperationalTeams`, and their membership are LST-1.4 | Slice 7 converts `AttentionRoutingManagementService` only |
| D-3 | **Secret-store transaction participation.** The SQL secret store joins an open transaction instead of committing its own, Entra included, with standalone behaviour kept outside a transaction. AUD-1.2 D35 may be amended | Slice 1 (§4). D35 is amended in the AUD-1.2 record by a dated note; its historical text is kept |
| D-4 | **Integration delete locking.** Integration delete acquires the integration row lock inside the unit before its blocker checks | Slice 3. Closes the race in which a binding or attached schedule created between the blocker count and the delete is orphaned. Recorded now, implemented with the delete |

## 3. Plan

| Slice | Scope | Status |
|---|---|---|
| 0 | This record; the gate's §22 and §27 rulings; the AUD-1.2 D35 amendment note | **Done** |
| 1 | Secret-store transaction participation (D-3); the Secrets-category catalog and producer convention (D-1); the Entra registration flows adopting both | **Done.** Implemented, validated, independently reviewed; bounded remediation applied (§4.10) |
| 2 | Platform and integration settings: one unit per set, clear and reset; the live-secret-first defect; an in-transaction reset that integration delete can call | **Done.** Implemented and validated (§8); independently reviewed, ready after bounded remediation (§9); closed (§9.10) |
| 3 | Schedule delete and integration delete as W1 with verification (M6 each, including lost acknowledgement with a missing target); D-4 | **CLOSED** (§14): work package 5 accepted by the owner, merged with PR #18 as `54ae117` (2026-10-11), post-merge CI passed. Work packages 1 and 2 (the schedule delete) were independently reviewed and accepted; work package 3 (the integration row lock, §10), 4a (the H1 mechanisms, §11) and 4b (the integration delete as one audited unit, §12) were independently reviewed together, READY TO ACCEPT (§13.1); work package 5 (§13) remediated the review's findings and wrote the contracts and this closing record |
| 4 | Integration create, update, activate, deactivate, retire; bindings create and delete; schedule create (with its assignment), update, enable, disable | **CLOSED** (§15.8): implemented, independently reviewed, the schedule `PATCH` finding fixed (§15.7); merged with PR #23 as `b38f1fd` (2026-10-12), post-merge CI passed |
| 5 | Connections create, update, rotate, retire, verify (`external_attempt`), browse (W2); the connection rotation live-secret defect; resource contexts create, update, archive, unarchive; ownership moves gain verification | **CLOSED** (§16.11): implemented, independently reviewed, secret-reference ownership enforced (§16.10) |
| 6 | Notification definitions (create, update, enable, delete), subscriptions, managed contacts, diagnostics live sends (W3 `external_attempt`) | **CLOSED** (§17.11): implemented, independently reviewed, bounded fixes applied; merged with PR #25 as `30e760e` (2026-10-12), post-merge CI passed |
| 7 | Attention routing rules (D-2); module lifecycle (W1, with W3 for blocked attempts); the runtime shutdown request and its duplicate case | **In progress** (§18) |
| 8 | Closure: the converted-writer scan test, catalog pins, `docs/04-api/contracts.md`, integrated validation and independent review | Not started |

## 4. Slice 1: secret-store transaction participation

### 4.1 The store before and after

`SqlSecretStore` takes the request's scoped `AppertureDbContext`, the same instance the audited unit uses (pinned by `SecretStoreSharesTheUnitContextTests` with the hosts' registrations). Before this slice:

| Operation | Before |
|---|---|
| Create, add version, disable, revoke version | Two saves: the change, then the `SecretAudits` row. A failure between them left a change without its row |
| Activate version | Its own transaction inside the execution strategy, always. Inside an audited unit this was an error (a transaction was already open), which is why AUD-1.2 stored Entra secrets before the unit (D35). The `SecretAudits` row was written after that transaction committed |
| Any failure | Whatever the operation had added or changed stayed tracked on the shared context, so a later save could apply it |

After:

| | Behaviour |
|---|---|
| Change and evidence | Each operation adds its `SecretAudits` row before it saves, and saves once, so the change and the row commit together |
| A transaction is open on the context (an audited unit's, or any caller's) | The store **joins** it. It never begins, commits, rolls back or retries a transaction. The owner commits, and the unit verifies an uncertain commit and reruns only after a confirmed rollback (gate §13.5). There is no nested transaction and no nested execution strategy |
| No transaction is open | Every mutating operation runs in a transaction of its own inside the execution strategy (previously only activation did). The behaviour callers see is unchanged, with one refinement made in review (§4.10): the save does not accept changes until the commit succeeds, as EF's own save does, so other pending changes on the context are neither lost nor treated as saved when a commit fails |
| Failure | The store stops tracking every entity the operation added, loaded or changed, so neither a later save nor a rerun applies it; a rerun reloads. Other pending changes on the context are neither discarded nor accepted |
| Non-relational provider (in-memory test contexts) | Runs without a transaction |

Files: `PERS/SqlSecretStore.cs` (`ChangeAsync`, `TrackedAsync`, `AddAudit`).

### 4.2 Recorder events (D-1)

The store does not know who owns a secret or who acted, so it records nothing in `AuditLogs`. The **owning change** stages one Secrets-category row per owner-level secret change, as a secondary row of its own unit beside its primary row:

| Action | Category, severity | Target | Fields | Staged when |
|---|---|---|---|---|
| `entra.tenant_registration.client_secret.stored` | `secrets`, `elevated`; W1, basis `change` | `entra_tenant_registration` | `registrationId`; `change`: `registration_created` or `registration_updated` | A supplied client secret was stored and the registration names it |
| `entra.tenant_registration.client_secret.retired` | as above | as above | `registrationId`; `reason`: `replaced` or `registration_deleted` | The registration's own secret was disabled because the change stopped naming it |

- Neither row carries the secret, its store id (the reference the registration holds), a version id or any free text. The schemas allow ids, closed values, flags and counts only, and a catalog test (`Recorder_secret_events_target_their_owner_and_can_carry_only_ids_and_closed_values`) holds every recorder Secrets action to that. No action targets `secret` (`No_action_targets_a_secret_…`).
- The primary rows are unchanged: `entra.tenant_registration.updated` still lists `clientAuthentication` among the changed settings and `created` still carries `clientAuthenticationReplaced`.
- Version-level detail (add version, activate) stays in `SecretAudits`, the store's own evidence; the audit log records the owner-level change.
- Producers: `APP/Audit/AuditEvents.Secrets.cs`. Names: `AuditActionNames`. Schemas and catalog entries: `AuditActionCatalogEntries.Schemas.cs`, `AuditActionCatalogEntries.cs`. Later slices add their families' secret actions the same way (settings in slice 2, connections in slice 5).
- **Owners of secret operations.** Every production caller of the store has a configuration owner: Entra registrations, platform settings, integration settings (also reached from integration delete) and connections. `ISecretStore` has no API or other caller, so there is no standalone secret operation without an owner, and no secret-reference exception was needed.

### 4.3 Entra registrations (D35 amended)

Before (AUD-1.2 D35): a supplied secret was stored as a secret of its own **before** the unit, and afterwards exactly one secret was retired, best effort and outside the unit: the replaced one after a verified commit, the new one when nothing committed, neither after an unknown outcome.

After: everything happens **inside** the unit (`ConfigEntraTenantsController`, `EntraClientSecretMaterialProvisioner`).

| Flow | Inside the unit, in order |
|---|---|
| Create | Store the supplied secret under the registration id chosen before the unit; add the registration naming it; stage `created` and, with a secret, `client_secret.stored` |
| Update | Read and check (row version, change list, lockout guard); only if they pass, store the supplied secret as a new secret of its own; update the registration to name it; stage `updated` (and `policy.changed` if it applies); with a secret, stage `client_secret.stored`, disable the previous owned secret and stage `client_secret.retired` (`replaced`) |
| Delete | Check the guard; delete; stage `deleted`; disable the owned secret and stage `client_secret.retired` (`registration_deleted`) |
| Activate | Unchanged (no secret involved) |

How each D35 guarantee is kept:

| D35 guarantee | Now |
|---|---|
| A secret is never added as a version of the secret the live registration names | Kept: still a secret of its own (package decision P2) |
| The registration resolves its current secret until the row naming the new one commits | Kept, by the transaction instead of by ordering |
| Refusal, conflict, audit failure, confirmed rollback: the new secret is retired | **Stronger:** a refusal or conflict is decided before anything is stored, and any failure rolls the stored secret back, so no unused secret is left behind |
| Verified commit: the replaced secret is retired | Kept, and now in the same transaction (no best-effort window) |
| Unknown outcome: neither secret retired | **Stronger:** either the new secret, the row and the retirement all committed, or none did |
| Create keeps a secret whose commit was verified | Kept: reconstruction reads the committed registration |
| Delete retires the secret after the delete commits, best effort | Now atomic with the delete. A failure to retire it fails the delete instead of leaving an orphan (P4) |
| Rows record that client authentication was replaced, never the secret or its ref | Kept, plus the Secrets-category rows, which carry neither |

A rerun after a confirmed rollback stores a fresh secret; the rolled-back attempt's secret never existed.

### 4.4 Callers left outside a unit until their slices

Integration delete through settings reset (slice 3) and connections (slice 5) still call the store outside any transaction (platform and integration settings were converted in slice 2, §8; integration delete still resets its settings one unit at a time until slice 3). They get the standalone path: each store operation is now atomic with its `SecretAudits` row, and otherwise behaves as before. Their known defects, the live secret activated before the owning change and secrets orphaned when the owning change fails, remain until their slices convert them.

### 4.5 Package decisions

| # | Decision | Basis |
|---|---|---|
| P1 | Secret events are secondary rows staged by the owning change, one per owner-level change (stored, retired). The store writes no audit-log row; `SecretAudits` keeps version-level detail and its `system` actor unchanged | D-1; gate §7 (`SecretAudits` not merged) and §13.4; the prompt's "no duplicate primary events from the low-level store" |
| P2 | An Entra replacement stays a new secret of its own, not a new version of the live one | Keeps D35's isolation and lets a rerun mint a fresh secret without touching the live one |
| P3 | An update stores its secret only after every guard has passed | A refused or conflicting update then stores nothing at all |
| P4 | Delete retires the owned secret in its unit; a database failure while retiring fails the delete | D-3; gate §13.2 (a change and its rows commit together) |
| P5 | `TryDisableOwnedSecretAsync` reports whether it disabled a secret; a retirement row is staged only then | No row for a registration on the deployment secret, or naming a missing, foreign or already retired secret |
| P6 | Outside a transaction every store mutation runs in its own transaction inside the execution strategy; on failure the store stops tracking what it touched | Atomic change and evidence; no stale tracked state on the shared context |

### 4.6 Tests

| Test | What it shows |
|---|---|
| `SecretStoreTransactionSqlServerTests` (new, SQL Server, 14 after the review remediation of §4.10) | Standalone lifecycle (create, add, activate, replace, revoke, disable), each change with its `SecretAudits` row; a refused activation changes nothing; a save that fails after staging persists neither change nor evidence and leaves no staged entry (§4.10); a failed commit leaves no trace and neither discards nor accepts other pending changes (§4.10); a lost commit acknowledgement outside a unit is reported as it is and not rerun (§4.10); inside a caller's transaction the store begins none of its own (one transaction counted), and rolls back or commits only with the caller, with nothing visible to another connection before the commit; inside an audited unit the secret change, its store evidence and the audit row commit together; an audit insert failure leaves the prior secret live and no store evidence; a confirmed-rollback rerun activates only its own version and records it once; a lost acknowledgement is verified without a rerun; an unknown outcome leaves the secret change and its audit row together committed or together absent; a store on **another** context escapes the unit's rollback (the negative case of the join); a secret-looking sentinel appears in no audit column, no `SecretAudits` column, no descriptor name, no captured unit log line and no exception message, and no audit column holds the secret's store id |
| `AuthConfigurationAuditSqlServerTests` (amended, SQL Server, 15) | The D35 cases under the new sequencing: replacement commits with `stored` and `retired` rows sharing the update's operation event id; refused and conflicting updates store no secret; an update whose audit fails rolls back with its secret and its store evidence; a lost acknowledgement keeps the new secret and retires the old one with one transaction; an unknown outcome is wholly committed or wholly unchanged; create with a secret records its storage, and a create whose audit fails stores nothing; delete retires the secret in its unit, and a delete whose audit fails keeps the registration and the secret |
| `SecretStoreSharesTheUnitContextTests` (new, unit, 2) | With the persistence, recorder and secrets registrations the hosts use, the store and the unit resolve one context per scope, and separate scopes get separate contexts |
| `AuditActionCatalogTests` (3 changed or new) | The two AUD-1.4 actions, the catalog count, no action targets `secret`, and recorder Secrets actions are W1, elevated, owner-targeted and limited to ids, closed values, flags and counts |
| `EntraClientSecretMaterialProvisionerTests` (2 new) | `TryDisableOwnedSecretAsync` reports true only when it disabled the registration's own usable secret |

### 4.7 Results

All runs on the tree committed as `7198cda` (§4.9). Builds used `--artifacts-path artifacts/aud14 -m:1`. SQL Server: the local `apperture-test-sql` container; credentials were read from `.env` into process environment only.

| Check | Result |
|---|---|
| Full solution build (`Apperture.slnx`) | Succeeded, 0 errors |
| `Apperture.ArchTests` | 38 / 38 passed |
| `Apperture.UnitTests` (full) | 3936 / 3936 passed (3927 at the AUD-1.3 head, plus 9 new) |
| `SecretStoreTransactionSqlServerTests`, `AuthConfigurationAuditSqlServerTests`, `ConnectionLifecycleAuditSqlServerTests`, `ManagementHostGovernanceHttpTests`, `BrowserRequestTrustHttpTests` | 97 / 97 passed. The two secret suites wrote 26 `EXECUTED against SQL Server catalog …` markers (one per test); the connection suite fails rather than skips without SQL Server; the real-host suites wrote to `Apperture_ManagementGovernanceTests` during the run (`last_user_update` 23:58 UTC, run ended about 00:01 UTC), and they resolve the changed controller from the host's dependency graph |

The first SQL run had three failures, all in the new tests: two opened a caller transaction outside the execution strategy, which EF refuses for any save when retry on failure is on (production units open theirs inside it); one assertion expected a single `client_secret.stored` row although the arranged create writes one too. Corrected in the tests; no product change.

Not run, with the reason: the full `Apperture.IntegrationTests` run (the affected suites above cover the store, its one in-unit caller and the host graph; PR CI runs the whole suite); the UI test projects and the SEC-1 browser suite (no UI, route or response changed); mutation runs of product code (not authorized for this package; the negative-case test above shows the join's dependence on the shared context without editing product code).

### 4.8 Remaining risks for review

- **Shared-context dependence.** Atomicity holds because the store and the unit share one scoped context. A caller that builds a store on another context, or a future change to either lifetime, would silently move secret changes outside the unit. `SecretStoreSharesTheUnitContextTests` pins the hosts' registrations; nothing prevents a hand-built store.
- **Joined saves flush everything tracked.** Inside a unit the store's save also writes anything else the attempt has tracked, including staged audit rows. That is correct inside the unit (one transaction), and the unit's own postcondition still counts staged rows, but it means an audit insert failure can surface from a store call rather than from the unit's final save.
- **Standalone retry.** Outside a transaction the execution strategy may rerun a store operation after a transient commit failure, as EF already did for these saves; there is no verification there, so an ambiguous commit is reported as the failure it was, never as a rollback (§4.10). The callers left on that path are converted in slices 2, 3 and 5.
- **Atomicity depends on the store implementation.** An owner stores and retires its secret in its unit only because `SqlSecretStore` joins the owner's SQL transaction. The Key Vault and Vault stores are placeholders that implement none of this, and a store whose effects are external would run an external effect inside an audited attempt. `ISecretStore` and `SqlSecretStore` now say so (§4.10). No other provider is implemented here.
- **Delete is now stricter.** An Entra registration delete fails if retiring its secret fails, where before it succeeded and logged. This needs a database failure on the same connection.
- **Unconverted callers keep their defects** (§4.4) until their slices.

### 4.9 Commits

| Commit | Content |
|---|---|
| `f71a57c` | Slice 0: the gate's §22 and §27 rulings, the AUD-1.2 D35 amendment note, this record |
| `7198cda` | Slice 1 code: `SqlSecretStore`, the Secrets catalog entries, names, schemas and producers, the Entra provisioner and controller, and the tests |
| `3f54596` | This section's results |
| The remediation commit that follows | Independent review remediation (§4.10) |

### 4.10 Independent review and remediation

Independent review of slices 0 and 1 at `3f54596`: **READY AFTER BOUNDED REMEDIATION**. No critical or high findings. Contract compliance with D-1 and D-3, the store's transaction ownership, the Entra sequencing, the Secrets recorder rows and the ambiguity handling were found sound. The review found one blocking item and three limited ones, all corrected here with no change to a ruling or a requirement.

| Finding | What it was | Resolution |
|---|---|---|
| M1 (medium, blocking) | The test named as proof of the change-tracker cleanup refused the activation before the store modified anything, and every entity involved was already tracked and unmodified, so it passed with the cleanup removed. No test failed a store write after staging | That test is renamed to what it shows (`Outside_a_transaction_a_refused_activation_changes_nothing`). New `Outside_a_transaction_a_save_that_fails_after_staging_persists_nothing_and_leaves_no_staged_entry_to_save_later` adds a version through a standalone store and fails the first `INSERT INTO [SecretAudits]` statement. At that moment the change tracker is recorded and must hold the added version, the modified descriptor and the added evidence row. Afterwards the database holds no new version and the same active version, descriptor `UpdatedAt` and evidence rows as before; the context holds no added, modified or deleted entry; a save writes nothing (0 rows); and the next operation on the same context persists only itself (two versions, not three). Deterministic: the interceptor fails one statement once, and the test asserts one transaction started, so nothing was rerun |
| L1 (low) | The standalone path saved with the default acceptance of changes before the explicit transaction committed. Other pending changes on the context were then treated as saved, so a failed commit followed by a rerun or a later save lost them | Standalone mode now stages, saves with `acceptAllChangesOnSuccess: false`, commits, and accepts changes only after the commit, the order EF's own save uses inside its execution strategy. The joined mode still saves with default acceptance: the owner decides the outcome, and the unit's own checks need the staged rows accepted. The entities the store touched are still detached on any failure, a commit failure included, so a rerun reloads and cannot insert a failed attempt's row again |
| L2 (low) | Confidentiality coverage stopped at plaintext in exception text, and the Entra suite checked one GUID form for references; the store's own exception messages embed a secret id | Coverage extended (below). The exception-message exposure is a separate finding (§7) |
| L3 (low) | The atomic in-unit secret operations depend on a store that joins the owner's SQL transaction, and the placeholder stores do not | Documented on `ISecretStore`, on `SqlSecretStore` and in §4.8. No provider is implemented |

**L1 transaction reasoning.** With acceptance deferred, the cases are:

| Case | Outcome |
|---|---|
| Save and commit succeed | Changes accepted after the commit; behaviour as before |
| Save fails | EF leaves every entry as it was before the save. The touched entries are detached; anything else stays pending |
| Commit fails and the server rolled back | The touched entries are detached; other pending changes stay pending, so a later save still writes them. Tested with a commit that the server rolls back after every statement ran |
| Commit fails with an ambiguous outcome (acknowledgement lost) | The failure propagates unchanged. Outside a unit nothing can establish whether it committed, so the store does not claim a rollback and does not rerun; the execution strategy retries only failures it classes as transient, and a retry re-stages from reloaded state because the touched entries are detached. Tested: the change committed once, nothing is left that could save it again, one evidence row. Other pending changes stay pending, as under EF's own save. A caller that must know goes through an audited unit, which verifies |
| Inside a transaction (joined mode) | Unchanged: no commit, and the save accepts changes as any save does |

**L2 coverage.** In the store suite, a secret-looking sentinel and the live secret's reference in both GUID forms (`D` and `N`) are asserted absent from every `AuditLogs` column, from every `SecretAudits` column other than the id that evidence is keyed by, from descriptor names, and from exception text and the unit's captured log lines in the audit-insert failure, unknown-outcome and standalone failure tests. In the Entra suite the same holds, with both GUID forms, for the registration's audit rows, the store's evidence and the exceptions of the create, update and delete whose audit insert fails. D-1 is unchanged: no secret value or reference enters recorder evidence.

**Tests added or changed.** `SecretStoreTransactionSqlServerTests` 11 to 14 (the M1 test, the failed-commit test and the lost-acknowledgement test added; the refusal test renamed). `AuthConfigurationAuditSqlServerTests` unchanged in count (15), with stronger confidentiality helpers. No unit test changed.

**Mutation evidence** (product code edited temporarily, restored byte for byte, never committed). Removing the detach loop failed the M1 test, the failed-commit test and the lost-acknowledgement test. Reverting L1 to default acceptance failed the failed-commit test only. All 14 pass with the shipped code.

**Validation** (real SQL Server, `apperture-test-sql`; `APPERTURE_REQUIRE_SQL_TESTS=1`; credentials read from `.env` into the process environment only; `--artifacts-path artifacts/aud14 -m:1`):

| Check | Result |
|---|---|
| `SecretStoreTransactionSqlServerTests`, `AuthConfigurationAuditSqlServerTests`, `ConnectionLifecycleAuditSqlServerTests`, `ManagementHostGovernanceHttpTests`, `BrowserRequestTrustHttpTests`, `IntegrationTestSecretProtectorTests` | 104 / 104 passed |
| Focused unit tests: `SecretStoreSharesTheUnitContextTests`, `SqlSecretsRegistrationTests`, `EntraClientSecretMaterialProvisionerTests`, `AuditActionCatalogTests` | 80 / 80 passed |
| `Apperture.ArchTests` | 38 / 38 passed |
| Build | The integration test project and its dependencies built with 0 errors |

Not rerun: the full unit and integration suites. The remediation changes one store method, two comments, tests and this record, and the store's behaviour for its other callers is covered by the suites above.

**Readiness.** With M1 corrected, L1 to L3 handled and the validation above, slice 1 is **ready for slice 2**. Slice 2 has not started.

### 4.11 Slice 2 watch list

- **Compensation after a failed upsert (DF1 of the review). Resolved in slice 2 (§8.6).** `PlatformSettingsService` and `IntegrationSettingsService` store a replacement secret, then save the setting row, and on failure retire the new secret as compensation. The repository does not detach the failed row, so the compensating save sends it again, fails, and leaves the new secret orphaned. This predates slice 1 and is unchanged by it. Slice 2 replaces the compensation with the audited unit and the joining store, which removes the case; its tests must prove that a failed setting save stores no secret.
- The live-secret-first defect and the in-transaction reset that integration delete calls stay in slice 2's scope (§3).
- The store's exception messages (§7) are touched only if slice 2 changes a path that surfaces them.

**Next step:** slice 2 (platform and integration settings), then independent review of slices 1 and 2 before slice 3 (§3).

## 5. Rules every slice follows

**Actor.** The unit's actor, resolved once before the first attempt from the authenticated context (`AuditActor.CurrentPrincipal`), or a named non-human entry. A request row that stores who asked writes `transaction.Actor.RecordedValue`. Nothing a caller supplies is used (AUD-1.3 D2). Refusals and denials are W3 rows of the same principal, or `anonymous` when the request maps to no session (AUD-1.3 D10).

**Target.** One primary target from the closed vocabulary (§11.1): the configuration entity changed. A deleted target keeps its id and its name snapshot. Secret-store events target the owning entity (D-1).

**Never recorded** (gate §12.3, D-1): secret values or references, credential material, connection strings, tokens, keys, values of `IsSensitive` settings, email addresses (except where gate §12.3 as amended permits them: the subject address of an email-change event, and the values of a setting whose definition declares an operational email field), search or filter text. Settings and fields are named, never their sensitive values. Free text only in declared, bounded `operator_text` fields.

**Write classes** (gate §13.1). W1: every configuration mutation, including both deletes, secret-store events, module lifecycle state changes, the runtime shutdown request (basis `request`) and connection verify (basis `external_attempt`). W2: connection structure browse. W3: refusals, validation rejections, `schedule.delete_rejected`, ownership-move rejections, notification diagnostics sends (`external_attempt`). W4: module discovery and other telemetry, unchanged here.

**Transactions** (gate §13.4, §13.5). One audited unit per W1 operation: the operation lock first; every read the decision depends on, every mutation and every `Stage` inside it; then the commit. Units do not nest: a service another converted service calls offers a variant that joins the current transaction. The ownership mutation gate, where a path takes it, is acquired inside the unit after the operation lock. A lost race on a named unique index is a no-op that reports the winner (AUD-1.3 D18). A request that changes nothing records nothing.

**Ambiguous commits.** Verification on a fresh connection under the operation lock, then reconstruction from the primary row, or `outcome_unknown` (HTTP 500 with `code` and `operationEventId`), or `committed_result_unavailable` (AUD-1.3 D20). A delete verified as committed reports success, never "not found" (gate §13.6). Converted endpoints exclude `AuditInvariantException` from any `InvalidOperationException` mapping (AUD-1.3 D19).

**External effects** (gate §11.6, §13.3) follow the commit and are recorded as `external_attempt` rows.

## 6. Required validation

- **M7** for every converted action: actor, target, outcome, category, severity, visibility, basis and excluded fields.
- **M6** for each family, and for each delete: audit insert failure leaves nothing; lost acknowledgement verified (for deletes, with the target now missing, reported as success with one row); unknown outcome; operation lock not granted.
- Real SQL Server suites that fail, never skip, without SQL Server, with execution markers.
- Secret-looking sentinel values asserted absent from `AuditLogs`, `SecretAudits` and captured logs.
- Deterministic race tests on the named indexes the converted paths use.

## 7. Findings outside AUD-1.4

Recorded at orientation; not changed by this package.

| Finding | Owner |
|---|---|
| The integration-level attention-routing write routes (`PUT` and `DELETE /api/integrations/{id}/attention-routing`) are authorized by integration read only; the routing service checks nothing itself | Security defect. **Provisionally assigned to AUD-1.4 slice 7** (which converts `AttentionRoutingManagementService`), pending security triage (slice 3 closeout, 2026-10-11). Triage may route it to IAM-10 or an earlier owner-directed fix instead. Slice 7 will attribute these writes; attributing them does not fix their authorization, and nothing has changed in the routes' authorization. **Authorization remediated separately (owner direction, 2026-10-11, branch `fix/integration-attention-routing-authorization`):** both routes now require `integration.update` on the integration in addition to the read check, evaluated before the write and again under the integration row lock; the clear (`ClearIntegrationRoutingAsync`) now takes that row lock like the upsert. The routes still write no audit row, which stays with slice 7. |
| A worker with no configured runtime id applies the oldest queued shutdown request of any runtime | Runtime package, unowned |
| The worker's `applied` shutdown transition has no audit row (gate §11.6 cites it only as an example of `confirmed_result`) | Unassigned; for the AUD-1.6 writer inventory |
| A diagnostic pipeline test that dispatches immediately processes the whole pending outbox batch | Existing defect, unowned |
| `SqlSecretStore` exception messages embed the secret id (`Secret {id} not found.`, `Version … not found for secret {id}.`), which is the reference its owner holds. Pre-existing, found in the slice 1 review. The Entra flows cannot reach them in practice (the id comes from the registration's own row) and no recorder row carries a message | RCV-1 or optional cleanup; unchanged here because no AUD-1.4 invariant covers exception text and no path converted by slice 1 surfaces it |
| `SqlSecretResolver` echoes an invalid ref value in its exception text; the `PlatformSettingUpdatePayload` record's generated `ToString` would print its secret value; the SMTP test recipient is logged | RCV-1 or optional cleanup; guarded here only where a slice touches it |
| `docs/04-api/contracts.md` calls the shutdown audit action `runtime.shutdown.request`; the catalog name is `governance.runtime.shutdown.request.created` | Corrected in slice 7 |
| **Audited units can deadlock in the recorder's own verification count (AUD-1.1; found by the slice 3 independent review).** After saving its audit rows, `SqlAuditedUnitOfWork` counts the rows with its operation's event id (`AuditLogs.OperationEventId`, line 169 of the unit) to check that every staged row was inserted. The column has no supporting index, so under locking READ COMMITTED the count scans the table and waits on another audited unit's uncommitted audit rows. Two units that have each inserted a row can then wait on each other, and one is a deadlock victim (SQL Server error 1205). The reviewer reproduced it in a SQL Server tempdb model with the table shape (2 of 2 trials). It has **not been observed in production** and its product-level rate is not established. Beyond the model, the slice 3 pair case (§13.2), which releases two real deletes at the instant both have inserted their audit rows, saw it in the shipped unit on a locking catalog: the deadlock victim's statement was this count and the retry then committed. That shows the mechanism is real under forced simultaneity; it is not evidence of how often requests collide. The unit's execution strategy retries a deadlock victim, so correctness holds and the unit stays atomic; the retry still costs a rerun and latency, and under heavy concurrency could exhaust its retries. It exists in every audited unit since AUD-1.1; slice 3 neither introduced nor widened it, and removing the post-delete recount (§13.2) is unrelated to it. Not changed here | **Deferred; assigned to proposed AUD-1.6** (slice 3 closeout, 2026-10-11; the reviewer suggested it). The design gate's AUD-1.6 entry is not amended by this record, so AUD-1.6 planning must take it up from here; no epic was created or reordered. **Proposed work:** an index on `AuditLogs.OperationEventId` (a migration, so the AUD-1.6 owner's call) or a count that does not scan. **Validation to ask for:** a real-SQL-Server test, on locking READ COMMITTED and READ_COMMITTED_SNAPSHOT catalogs, in which two audited units each insert a row and verify at once and neither is a deadlock victim, with the plan showing a seek; and the slice 3 pair case (`Two_deletes_of_different_integrations_…`), which reports its deadlock victims, showing none |

IAM-9 stays closed. The IAM-10, IAM-11, OPS-1 and RCV-1 findings stay open. RC1 is not complete.

## 8. Slice 2: platform and integration settings

Implemented on this branch after the slice 1 remediation (`d403c2d`). Not independently reviewed yet. Slice 3 has not started. *(Status when written. Slice 2 was then independently reviewed and remediated, §9, and closed, §9.10. Slice 3 followed and is closed, §14.)*

### 8.1 Scope and writer inventory

Source inspection found exactly the writers the orientation inventory listed, and no others. `SqlPlatformSettingValueRepository.UpsertAsync` and `DeleteScopedAsync` are called only by the two services, and the resolvers only read.

| # | Writer | Controller | Before | Now |
|---|---|---|---|---|
| 1 | `PlatformSettingsService.SetAsync`, ordinary | `PUT /api/config/platform-settings/{key}` | Upsert, then a legacy `IAuditService` record built from text; skipped when no actor | One audited unit, `platform.settings.set` |
| 2 | `PlatformSettingsService.SetAsync`, sensitive replace | same | Secret added and activated on the live secret (or a new one created) in the store's own transactions, then upsert, then the legacy record; a failed upsert was compensated | One audited unit; `platform.settings.set` and `platform.settings.secret.stored` |
| 3 | `PlatformSettingsService.SetAsync`, sensitive clear | same | Row deleted, then the secret disabled, then the legacy record | One audited unit; `platform.settings.set` (clear) and, when a secret was disabled, `platform.settings.secret.retired` |
| 4 | `PlatformSettingsService.ResetAsync` | `DELETE /api/config/platform-settings/{key}` | As clear, with the `reset` action | One audited unit; `platform.settings.reset` and, for a sensitive setting that owned a secret, `.secret.retired` |
| 5 to 8 | `IntegrationSettingsService.SetAsync` (ordinary, replace, clear) and `ResetAsync` | `PUT` and `DELETE /api/integrations/{integrationId}/settings/{key}` | The same, with the owning resource context read for the legacy record | The same four units with the `integration.settings.*` actions |
| 9 | `IntegrationManagementService.DeleteIntegrationAsync` calling `IIntegrationSettingsService.ResetAsync` per key | integration delete (not an HTTP setting route) | One reset per key, outside any transaction | Unchanged in shape: still one reset per key, now one audited unit each. Conversion of the delete, and its use of the in-transaction reset (§8.8), is slice 3 |

Not writers: `SetAsync` with a blank sensitive secret operation (answers the current value), and the option, list and get routes.

Authorization is unchanged: the controllers check `platformSettings.manage` and the integration manage permission before the service runs, and the services authorize nothing. The trusted actor now comes from the unit (§8.10).

### 8.2 Transaction boundaries

Every genuine mutation is one `IAuditedUnitOfWork.ExecuteAsync` with `AuditActor.CurrentPrincipal`. Before the unit: only validation that needs no stored state (the definition exists and is editable, the scope applies, the payload parses, a sensitive setting refuses a plain value, a replace carries a secret value). Inside the attempt, in this transaction: the option provider's allowed values, the notification safety check (platform), the override row and its row version, the secret store calls, the row write, the staged audit rows. The unit takes the operation lock first, as for every W1 writer. After the unit: only the response, which reads the resolved setting.

The services take the unit as an optional last constructor argument and refuse to change anything without it, with an `AuditInvariantException`. The controllers exclude that exception from their `InvalidOperationException` to 400 mapping, and the `DELETE` routes map a lost race to 409 as `PUT` always did.

### 8.3 Platform settings

- **Ordinary set:** a changed value upserts the override and stages `platform.settings.set` with `operation=set`, the key, `sensitive=false`, the previous display value when an override existed and the new display value. A value equal to the stored one declares a no-op and records nothing; a stale row version is still a conflict first.
- **Sensitive replace, clear:** §8.5.
- **Reset:** a missing override is a no-op. Otherwise `platform.settings.reset` is staged, the override deleted and, for a sensitive setting that owned a usable secret, the secret disabled and `platform.settings.secret.retired` staged.

### 8.4 Integration settings

The same four operations on the integration scope. The target is the integration setting (`{integrationId}:{key}`), the fields carry `integrationId` and `key`, and the integration's owning resource context is recorded as the row's related resource context, as the previous records did. An integration that cannot be read leaves the context null, as before.

### 8.5 Secret lifecycle

| Case | Inside the unit |
|---|---|
| Replace, no override or an override whose secret is missing, disabled or revoked | A new secret of its own is created, a version added and activated, the row written naming it; `.secret.stored` with `change=created` |
| Replace, override names a usable secret | A new version is added and activated **on that secret**, the row is rewritten with the same reference; `.secret.stored` with `change=replaced`. This keeps the existing versioning model (the reference is stable) and is safe only because the version is live when the unit commits and not before |
| Clear, reset | The row is deleted, the secret the setting owns is disabled; `.secret.retired` with `reason=cleared` or `reset` only when one was disabled (a name and provider check keeps config-supplied references untouched) |

This differs from Entra (§4.3), where a replacement is a new secret of its own. There the registration row names the secret and the previous one is retired in the same unit; here the setting row keeps its reference and the secret carries its versions, so a rollback removes the new version and the previous active version stays active.

### 8.6 DF1 and the live-secret-first defect

Both are removed by construction, not by compensation:

- **Live-secret-first.** The version used to be added and activated through the store's own transactions before the row was saved, so a refused, conflicting or failed change still changed the credential the setting resolves. Now the store joins the unit's transaction, so the version is live only if the unit commits.
- **DF1.** A failed upsert used to leave the new secret stored and the failed row tracked; the compensating disable then saved the failed row again, failed, and orphaned the secret. There is no compensation any more. A failed upsert rolls the unit back (new secret, versions, active-version change, store evidence and audit rows), the unit clears the change tracker, and the repository detaches the row of a failed first insert.

### 8.7 Audit events, targets and row cardinality

| Action | Category, severity | Target | Role | Fields |
|---|---|---|---|---|
| `platform.settings.set` | configuration, standard | `platform_setting`: key | primary | `key`, `operation` (`set`, `replace`, `clear`), `sensitive`, `previousValue`, `newValue` (ordinary only, bounded display text, truncatable), `previousConfigured` (sensitive only) |
| `platform.settings.reset` | configuration, standard | `platform_setting`: key | primary | `key`, `sensitive`, `previousValue` (ordinary), `previousConfigured` (sensitive) |
| `integration.settings.set` | configuration, standard | `integration_setting`: `{integrationId}:{key}` | primary | as platform, plus `integrationId` and `optionProviderKey` |
| `integration.settings.reset` | configuration, standard | as above | primary | as platform reset, plus `integrationId` |
| `platform.settings.secret.stored`, `.retired` | secrets, elevated | `platform_setting`: key | secondary | `key`, `change` (`created`, `replaced`) or `reason` (`cleared`, `reset`) |
| `integration.settings.secret.stored`, `.retired` | secrets, elevated | `integration_setting` | secondary | `integrationId`, `key`, `change` or `reason` |

All are W1 with basis `change`. The four set and reset actions were declared by AUD-1.0 as legacy-bridge entries and are converted here (schemas and typed producers added, no new names); the four secret actions are the only catalog additions (`IntroducedIn` AUD-1.4). Cardinality per operation: an ordinary set or reset writes one row; a sensitive replace writes two (`set`, `.stored`); a clear or reset writes one or two (`.retired` only when a secret was disabled). The primary row carries the operation event id as its event id; a secondary row shares the operation event id. The producers refuse a value for a sensitive setting. No field carries a secret, a version id or the store's reference, and the Secrets-category rows target the setting (D-1). `SecretAudits` is unchanged and separate, and now commits in the same transaction (§4.1). Severity is per action, so `set` stays standard even for a sensitive replace; the elevated `.stored` and `.retired` rows mark the secret change.

### 8.8 The in-transaction integration reset

`IIntegrationSettingResetInTransaction.ResetInTransactionAsync(IAuditedTransaction, integrationId, key, ct)`, implemented by `IntegrationSettingsService`. The contract is on the interface; in short:

- It joins the transaction of the unit whose `IAuditedTransaction` the caller passes. It opens no unit, begins, commits and rolls back nothing, and saves nothing outside that transaction.
- It stages its rows (`integration.settings.reset`, and `.secret.retired` when a secret was disabled) as secondary rows of the caller's unit **before** it changes anything, so a transaction that is not current fails the staging, with an `AuditInvariantException`, before any delete is saved. This is tested with the transaction of a finished unit.
- With no override it changes and stages nothing and returns false (the caller's unit may already hold rows, so it is not declared a no-op there). An override removed by another request between the helper's read and delete is a concurrency conflict that rolls the caller back.
- It authorizes nothing, as `ResetAsync` does not; the caller has authorized the larger change. The interface is `internal` to the services assembly and registered only as itself, so no controller or other assembly can resolve it and it is not a way to reset a setting from a request.
- It takes no lock. Slice 3's integration delete acquires the integration row lock and the ownership gate in its own unit before its blocker checks (D-4), then calls this for each scoped key, then removes the integration.

### 8.9 Concurrency and ambiguous commits

| Case | Behaviour |
|---|---|
| Stale row version | Conflict (`DbUpdateConcurrencyException`, 409) before any secret is stored, checked even when the value would be unchanged. The repository's own check remains |
| First-insert race | Two requests set a setting with no override; the other commits its insert first. The repository recognises the violation of `PK_PlatformSettingValues` or `IX_PlatformSettingValues_Key_ScopeKind_ScopeId` on its own insert, detaches the row and reports the concurrency conflict. It is never borrowed as the winner's success, and nothing it stored survives. Any other duplicate keeps its meaning |
| Audit insert failure, failed upsert, confirmed rollback | Nothing persists. A confirmed rollback of an uncertain commit reruns the attempt (the unit's rule); the rolled-back attempt's version does not exist |
| Lost acknowledgement of a commit | Verified from the committed primary row; the attempt is not run again, so no second version. The response is built from the committed state |
| Verification unavailable | `outcome_unknown`; the secret change, the row and the audit rows are together committed or together absent |
| Already completed or unchanged | No-op, no row |

The reconstruction result of a set or reset carries no data beyond "applied": the evidence that the operation committed is the primary row with this operation's event id, and the response is the current resolved setting, as for Entra update.

### 8.10 Actor and authorization

The unit's actor is the authenticated principal resolved once before the first attempt. `UpdatedByPrincipalId` on the setting row is that actor's recorded value, so the row and the audit row share it by construction (AUD-1.3 D2). Previously the audit record was skipped when no actor resolved; now a change without a resolvable actor fails in the unit instead of going unrecorded. Authorization is unchanged. `SecretAudits` keeps its `system` actor (§4.5 P1).

### 8.11 Intentional behaviour changes

| Change | Why |
|---|---|
| Setting an ordinary override to the value it already holds writes nothing and records nothing (the row version no longer changes) | "A request that changes nothing records nothing" (§5). A stale row version is still a conflict |
| A change with no actor fails instead of succeeding unrecorded | Mandatory evidence is never skipped |
| A lost first-insert race and a reset that loses to another reset are 409 conflicts (before: an unhandled database error, or a silent success) | D18: a lost race is reported, not borrowed |
| `DELETE` settings routes map a concurrency conflict to 409 | A converted route cannot surface it as an unhandled error |
| Audit details are structured fields on a target; the legacy `key=... oldValue=...` text and its `platform-settings` and `integration-settings` outcome labels are gone | Gate §12 |
| The two legacy action-name classes in the services assembly are removed; the names are in `AuditActionNames` | One source for the names |

### 8.12 Tests

| Test | What it shows |
|---|---|
| `SettingsAuditSqlServerTests` (new, SQL Server, 18) | Platform ordinary set, change, unchanged, reset, with actor, target, category and operation ids; platform sensitive replace (created), replace (replaced, same reference, two versions), clear and reset with the stored and retired rows sharing the operation and no secret or reference in any audit column or store evidence; the live-secret-first case (audit insert fails: the previous secret still resolves, version count, active version, row value and version, audit and evidence counts all unchanged, nothing tracked); DF1 (the setting insert fails on a first replacement: no secret, no version, no evidence, no row, nothing tracked, a later save writes nothing); stale row version stores nothing; the first-insert race for an ordinary and a sensitive setting; lost acknowledgement (one attempt, exactly one new version); confirmed rollback (two attempts, only the rerun's version); unknown outcome committed and not committed; no-ops write nothing; integration ordinary and sensitive lifecycles and a failed integration replacement; the in-transaction reset committing with the caller's unit and beginning no transaction, rolling back with it, and refusing a finished unit's transaction before changing anything |
| `SettingsAuditTests` (new, unit, 12) | Which rows the services record, the unit's actor on the row, no-ops, stale version before no-op, lost acknowledgement without a second run, unknown outcome, the missing unit as an invariant error, sensitive producers refusing a value, closed secret fields, the in-transaction reset joining the caller's unit with no unit of its own and staging nothing without an override, and the helper's interface being internal |
| Changed | The existing unit tests of both services now run on the recording unit and assert recorder events instead of legacy text; `SensitivePlatformSettingsTests` replaced its compensation tests (they asserted the old live-secret-first and compensation behaviour) with a stale-version test and a no-compensation test; the catalog test lists the four new actions |

**Mutation evidence** (product code edited temporarily, restored byte for byte, never committed): storing the secret before and outside the unit (the old shape) failed the live-secret, DF1, stale-version, rerun, lost-acknowledgement and unknown-outcome tests; removing the first-insert classification failed both race cases; deleting before staging in the in-transaction reset failed the finished-transaction test. All 18 pass with the shipped code.

### 8.13 Validation

Real SQL Server (`apperture-test-sql`), `APPERTURE_REQUIRE_SQL_TESTS=1`, credentials read from `.env` into the process environment only, builds with `--artifacts-path artifacts/aud14 -m:1`:

| Command | Result |
|---|---|
| `dotnet build Apperture.slnx --artifacts-path artifacts/aud14 -m:1` | 0 errors |
| `dotnet test tests/Apperture.UnitTests --artifacts-path artifacts/aud14 -m:1` (full project) | 3948 / 3948 passed (3936 before, 12 new) |
| `dotnet test tests/Apperture.ArchTests ...` | 38 / 38 passed |
| `dotnet test tests/Apperture.IntegrationTests --no-build --filter FullyQualifiedName~SettingsAuditSqlServerTests` | 18 / 18 passed |
| `dotnet test tests/Apperture.IntegrationTests --no-build` filtered to the `IntegrationTests.Audit` namespace (every audit SQL suite, including the secret store and Entra suites), `ManagementHostIntegrationConfigurationHttpTests`, `ResourceOwnershipMoveConcurrencySqlServerTests`, `ConnectionLifecycleAuditSqlServerTests`, `ManagementHostGovernanceHttpTests`, `BrowserRequestTrustHttpTests`, `IntegrationTestSecretProtectorTests` | 290 / 290 passed, none skipped |

The SQL suites fail rather than skip without SQL Server, and the new suite writes one `EXECUTED against SQL Server catalog` marker per test.

Not run, with the reason: the full `Apperture.IntegrationTests` project (the affected suites above cover every consumer of the changed services, the store and the host graph; PR CI runs all of it), the UI test projects and the SEC-1 browser suite (no UI, route or response shape changed; the only response change is a 409 on the two `DELETE` routes).

### 8.14 Remaining risks, findings and decisions for the owner

- **Decision: ordinary setting values stay in the audit rows. Resolved by the owner's ruling of 2026-10-08 (§9.1): values are recorded as the setting's definition declares.** An ordinary setting's previous and new values are recorded as bounded display text, as before and as `platform-settings-registry.md` provides ("old/new value or redacted summary"). An ordinary setting that holds something sensitive without being marked sensitive would be recorded; the definition is then the defect. Gate §12.3 excludes only `IsSensitive` values. If the owner prefers names only (as for Entra), the `previousValue` and `newValue` fields come out of two schemas.
- **Decision: validation refusals are not recorded.** Shape and definition errors (unknown key, not editable, bad payload, an option not allowed, the notification safety check) are unchanged: they refuse before anything changes and write no row, as before. The gate's W3 rule for state-guard refusals could apply to the notification safety check; no catalog action exists for it and none was invented.
- **Integration delete is still not atomic with its settings** until slice 3: it resets each key in its own unit, then removes the integration in a separate save. A failure between them leaves a deleted-settings integration, as before.
- **`ReplaceAsync` adds a version to any usable secret the override names, owned or not.** The adjacent comment says config-only references are not rotated in place, but the code does not check ownership. An override row only ever holds a reference this provisioner created, so no path reaches it; recorded, unchanged.
- **Reconstruction after a verified commit returns the current setting**, which a later writer may have changed; the evidence of this operation is the primary row.
- **A repository-level concurrency conflict is a 409 for `DELETE` routes now;** clients that treated the old 500 as a failure see a conflict.
- Findings outside the package from §7 are unchanged.

### 8.15 Changed files

Source: `AuditActionNames.cs`, `AuditActionCatalogEntries.cs`, `AuditActionCatalogEntries.Schemas.cs`, `AuditEvents.Settings.cs` (new); `PlatformSettingsService.cs`, `IntegrationSettingsService.cs`, `PlatformSettingSecretMaterialProvisioner.cs`, `IIntegrationSettingResetInTransaction.cs` (new), `ServiceCollectionExtensions.PlatformSettings.cs`; `SqlPlatformSettingValueRepository.cs`; `ConfigPlatformSettingsController.cs`, `IntegrationSettingsController.cs`; `PlatformSettingsAuditActionNames.cs` and `IntegrationSettingsAuditActionNames.cs` removed. Tests: `SettingsAuditSqlServerTests.cs`, `SettingsAuditTests.cs` (new) and the adapted tests listed in §8.12. Docs: this record, the README row and `platform-settings-registry.md`.

**Next step (superseded by §9):** independent review of slices 1 and 2, then slice 3 (schedule delete and integration delete, D-4).

## 9. Slice 2 independent review and bounded remediation

Independent review of slice 2 at `fa784f0` returned **ready after bounded remediation**: no critical or high findings, one medium finding (M1), and low findings L1 to L3 and L5, with L4 and DF1 to DF5 carried forward. The owner resolved the audit-data policy question the review raised (§9.1). This section records the ruling, the corrections and their validation. Slice 3 has not started.

### 9.1 Owner ruling (2026-10-08) and the gate amendment

Email addresses are not categorically prohibited audit data; the blanket exclusion in gate §12.3 was broader than its purpose. The approved policy, now in gate §12.3 as an amendment:

1. An operational email address may be recorded when directly relevant to the audited action and explicitly permitted by a structured audit schema.
2. It must not enter generic audit messages, request snapshots, arbitrary metadata or unrelated fields. There is no general permission to record arbitrary addresses.
3. Sensitive configuration values remain prohibited.
4. Secrets, credentials, tokens, connection strings, private keys and secret references remain prohibited without exception.
5. An SMTP username is a credential-related identifier: its changes are audited by setting name and safe metadata only, never its value, even when it is formatted as an email address.
6. The general permission to record bounded non-sensitive setting values remains, subject to 2 to 5.

The rule for the subject address of an email-change event is unchanged. No runtime setting sensitivity and no secret-storage behaviour changed to implement this.

### 9.2 Findings and resolutions

| Finding | Resolution |
|---|---|
| M1 (medium): `Notifications.Email.FromAddress` and `Notifications.Email.SmtpUser` are ordinary settings whose values were recorded | §9.3: a per-definition audit value policy applied in one place. `FromAddress` records both values; `SmtpUser` records the change by name and action only. Neither's classification changed |
| L1: the response read ran outside the reconstruction delegate, so a verified commit followed by a failed read was a generic 500 | §9.4 |
| L2: resetting a malformed ordinary override failed because the previous value could not be displayed | §9.5 |
| L3: nothing pinned that the services, repository, secret store and unit share one context | §9.6: `SettingsServicesShareTheUnitContextTests` |
| L5: documentation | §9.7 |
| L4: the narrow window between the reset's read and its delete | Deferred, unchanged. The delete reports a lost race as a 409 and nothing is recorded for a reset this request did not make |
| DF1: secret ownership is identified by a shared name (`platform-setting:{key}`) and provider | Deferred, unchanged |
| DF2: integration delete with setting keys that have no definition, or are unscoped | Deferred. **Required input to slice 3 planning** |
| DF3: concurrent settings writes do not take the integration-delete lock | Deferred. **Required input to slice 3 planning** |
| DF4: a possible W3 refusal for the notification safety check (§8.14) | Deferred; needs a separate owner decision. No W3 action added |
| DF5: replace-versus-clear deadlock and transient retry behaviour | Deferred, unchanged |

### 9.3 M1: the audit value policy

`PlatformSettingDefinition.AuditValue` (`PlatformSettingAuditValuePolicy`) declares what a setting's audit rows may carry, and `PlatformSettingAuditValues` is the only code that turns it into a value. Both services call it for the previous and the new value of a set and for the previous value of a reset, so there is no key comparison in either service and nothing scans a value for an email-shaped string.

| Policy | Effect | Used by |
|---|---|---|
| `ByShape` (default) | Recorded only when the shape bounds the value: boolean, integer, duration, or a string or array validated against declared or provider options. Free text is omitted | Every definition that does not declare one, including every module-declared text setting |
| `Record` | Bounded free text that is not an address | `Notifications.Email.SmtpHost`, `Scheduling.DefaultTimeZoneId` |
| `RecordOperationalEmail` | An explicitly approved operational email field | `Notifications.Email.FromAddress` only |
| `Omit` | Name and action only | `Notifications.Email.SmtpUser`, `Notifications.Email.FromDisplayName` |

A sensitive setting records no value whatever it declares (`IsSensitive` wins), and the producers still refuse a value for a sensitive setting. The schemas are unchanged: the same closed, bounded `previousValue` and `newValue` fields, absent when omitted.

**Other settings that can carry an address.** Every free-text platform setting was inspected: `SmtpHost`, `SmtpUser`, `FromAddress`, `FromDisplayName`, `Scheduling.DefaultTimeZoneId` and the demo text setting. `FromDisplayName` (a name that may contain an address) is now `Omit`, which is a change from slice 2 (its values were recorded). The demo text setting falls to the default and is omitted. A unit test fails if a new free-text platform setting does not declare a policy, and another if any setting other than `FromAddress` declares `RecordOperationalEmail`.

**Module-declared settings.** `IntegrationSettingDefinition` has no audit declaration, so a module's String, MultilineString, Uri, TimeZone and Decimal settings now map to `ByShape` and their values are omitted; Boolean, Integer, Duration and Choice (declared options) are still recorded. This is the fail-closed reading of the ruling: the platform cannot tell a module's host name from a module's contact address. **Gap and proposed minimal extension:** let a module declare, per setting, `Record` or `Omit` (never `RecordOperationalEmail`, which only the platform registry grants), defaulting to omit, and review the declaration with the module. Nothing else in the design needs to change. Not implemented here because it widens the module SDK contract and needs an owner decision on who reviews a module's declaration.

### 9.4 L1: the response read after a commit

`SettingAuditedChange` runs every setting change and its response read for both services. When the unit had to verify an uncertain commit, the response is read inside the unit's `reconstruct` delegate, so a failed read surfaces as `AuditCommittedResultUnavailableException` (HTTP 500, `committed_result_unavailable`). When the unit acknowledged the commit directly, the read follows it, and a failure there is reported the same way, with the operation's event id, instead of as a generic error. A change declared a no-op committed nothing, so its read failure is an ordinary failure. Nothing is retried and no secret version is added.

The three facts stay apart: the **proof of commit** is the unit's confirmation or its verification of the primary row; the **reconstruction** is the response built from committed state; and the response is the setting's **current state when it is read**, which is not necessarily the state this operation produced. A concurrent later writer's value can appear in it (§8.9, §8.14). The tests pin this: a write by another request between the commit and the read is returned, while the audit row still records what this request did.

### 9.5 L2: a malformed override can still be reset

`PlatformSettingAuditValues.DisplayStoredOrNull` returns no value for a stored value that cannot be parsed, as well as for a definition that records none. The reset therefore commits with its primary row and without `previousValue`; valid resets keep theirs. Sensitive settings are unchanged (they never read the stored value for display). A set over a malformed integration override records only its new value.

**Observation, not changed.** A platform *set* over a malformed override still fails, before the unit stages anything: the notification safety check lists every resolved setting, and `PlatformSettingsResolver` throws on a malformed stored value. The reset is the repair path and works. Recorded under §9.9.

### 9.6 L3: the shared context pin

`SettingsServicesShareTheUnitContextTests` registers the persistence, audit recorder, SQL secrets and Management host settings registrations the hosts use, resolves both settings services in one scope and follows each service's own dependencies by reflection to the `AppertureDbContext` it holds: its audited unit, its settings repository, the secret store behind its provisioner, and (integration) the integration repository. Every one must be the scope's single context, and two scopes must not share one. The Slice 1 `SecretStoreSharesTheUnitContextTests` is unchanged. The behavioural proof that this makes secret changes atomic remains the SQL suite (§8.12).

### 9.7 L5: corrections to earlier statements

- `UpdatedByPrincipalId` on a setting row stores the normalized user identifier of the actor (`AuditActor.RecordedValue`), consistent with IAM-9.2a. §8.10 says the row stores the actor's recorded value; this states what that value is.
- The response of a set or reset is the current resolved state, including a later writer's value, not an exact snapshot of the original mutation (§9.4). §8.9's wording that the response "is built from the committed state" means the current state at the time of the read.
- A `DELETE` concurrency conflict returns 409, an identical-value `PUT` and a reset of an absent override are 200 no-ops that record nothing, and the other outcome semantics are in `docs/04-api/contracts.md` (Platform and integration settings API).
- The values of an ordinary setting are recorded as its definition declares (§9.3), not as "ordinary values stay in the audit rows" (§8.14, §8.7).

### 9.8 Tests and validation

New or changed tests:

| Test | What it shows |
|---|---|
| `SettingAuditValuePolicyTests` (unit, 44 cases across 19 methods) | The policy table; every free-text platform setting declares a policy; only `FromAddress` is an approved email field; no sensitive setting in either registry records a value; each module kind maps as §9.3 says; for both services: `FromAddress` values recorded on set and reset, `SmtpUser` by name and action only (email-formatted values absent from every field), an undeclared free-text setting given an address records nothing, a bounded setting still records, a sensitive setting stays value-free even when its definition declares a recording policy, identical values recorded for the same definition by both services; malformed override reset (platform, integration, sensitive), valid reset keeps its value; failed response read after a commit (direct and lost acknowledgement, set and reset) is `committed_result_unavailable` with one staged operation, after a no-op it is an ordinary failure; the response includes a later writer's value |
| `SettingsServicesShareTheUnitContextTests` (unit, 2) | §9.6 |
| `SettingsAuditSqlServerTests` (SQL, 18 to 21) | `FromAddress` recorded and `SmtpUser` omitted against SQL Server, with the email-formatted username absent from every audit column; a malformed override reset with its row and primary row; a lost acknowledgement followed by a failed response read: `committed_result_unavailable`, one attempt, exactly one new secret version (no duplicate), the secret resolves to the committed value |
| `RecordingAuditedUnitOfWork` (test double) | Now wraps a failed reconstruction as the SQL unit does |
| `SettingsAuditSqlServerTests` integration text setting | Declares `Record`, since it stands for a reviewed text setting |

Real SQL Server (`apperture-test-sql`), `APPERTURE_REQUIRE_SQL_TESTS=1`, connection string read from `.env` into the process environment only, `--artifacts-path artifacts/aud14r -m:1`:

| Command | Result |
|---|---|
| `Apperture.UnitTests`, filtered to the new files, `SettingsAuditTests`, `Epic112*`, `Epic118*`, `SensitivePlatformSettingsTests`, `PlatformSettingsServiceValidationTests`, `PlatformSettingDefinitionRegistryTests`, `SecretStoreSharesTheUnitContextTests` and every `Audit` test (catalog and detail schemas included) | 404 / 404 passed |
| `Apperture.IntegrationTests`, `SettingsAuditSqlServerTests` | 21 / 21 passed |
| `Apperture.IntegrationTests`, the `IntegrationTests.Audit` namespace (every audit SQL suite), `ManagementHostIntegrationConfigurationHttpTests`, `ResourceOwnershipMoveConcurrencySqlServerTests`, `ConnectionLifecycleAuditSqlServerTests`, `ManagementHostGovernanceHttpTests`, `IntegrationTestSecretProtectorTests` | 272 / 272 passed, none skipped |
| `Apperture.ArchTests` | 38 / 38 passed |
| `dotnet build Apperture.slnx` | succeeded, 0 errors |

Not rerun: the full unit and integration projects (the filtered runs cover every consumer of the changed services, definitions and recording double), the UI test projects and the browser suite (no UI, route or response shape changed).

### 9.9 Remaining findings and observations

- L4 and DF1 to DF5 as in §9.2. **DF2 and DF3 must be inputs to slice 3's plan**, which resets an integration's settings inside its delete: an undefined or unscoped key is left alone by the helper, and a concurrent settings write does not take the delete's lock.
- Module-declared free-text settings are not audited by value until the SDK extension in §9.3 exists.
- A platform set over a malformed stored override fails in the notification safety check because the resolver throws on it; reset repairs it. A resolver that tolerates a malformed row is a separate, unowned change.
- `Uri` settings declared by a module could carry credentials in their userinfo; they are now omitted from audit values, which also closes that exposure for the audit rows, but the value is still readable and listed by the settings APIs as before.
- `FromDisplayName` stopped recording values (§9.3).

### 9.10 Closure

Slice 2 is **closed** for AUD-1.4 once this remediation is on the branch: M1, L1, L2, L3 and L5 are corrected and validated; L4 and DF1 to DF5 are carried forward without code. Slice 3 (schedule delete and integration delete, D-4) was not started when this section was written; its progress is in §10.

## 10. Slice 3: schedule and integration delete

Slice 3 is implemented in work packages from the planning report approved on this branch. §10 to §12 record each package as it was when built; the review of 3, 4a and 4b, the remediation, and the closing record are §13, which also states where a later finding corrects an earlier paragraph. The owner approved ruling H1 (§11.1) for the cleanup of overrides whose definition is unavailable; work package 4a (§11) built the mechanisms the delete needs and work package 4b (§12) converted the integration delete (D-4). Work packages 1 and 2 were independently reviewed and accepted; work packages 3, 4a and 4b were reviewed together (READY TO ACCEPT, §13.1).

| Work package | Scope | Status |
|---|---|---|
| 1 | Catalog schemas and typed producers for `schedule.deleted` (W1) and `schedule.delete_rejected` (W3) | **Done** (§10.1) |
| 2 | Schedule hard-delete as one audited unit; typed not-found; controller exception mapping | **Done** (§10.2 to §10.4) |
| 3 | The integration row lock in the integration settings writers and in the three blocker writers | **Done** (§10.5 to §10.7) |
| 4a | The mechanisms the integration delete needs: `integration.deleted` schema and producer, the persisted-override and surviving-reference inventory, the H1 classifier, and transactional secret retirement | **Done** (§11). Implemented and validated; no delete is converted |
| 4b | The integration delete as one audited unit: the lock, the blockers, the coordinated removal using the 4a primitives, M6 | **Done** (§12). Implemented and validated |
| 3, 4a, 4b review | One combined independent review | **READY TO ACCEPT**, no critical, high or medium finding; two LOW findings (§13.1) |
| 5 | Remediation of the review's findings, `docs/04-api/contracts.md` for both deletes, reconciliation of the related records, and the closing record | **Done** (§13). Implemented and validated; awaiting the owner's review |

### 10.1 Catalog and producers

Both actions were declared by AUD-1.0 as legacy-bridge entries. They now carry detail schemas and typed producers (`AuditEvents.ScheduleDeleted`, `AuditEvents.ScheduleDeleteRejected`); no action was added and the action names moved into `AuditActionNames`.

| Action | Class | Target | Related | Fields |
|---|---|---|---|---|
| `schedule.deleted` | W1, primary, Configuration, standard, basis `change` | `schedule`: the schedule id | the schedule, and its resource context | `name` (name snapshot), `integrationId` (optional), `assignmentsRemoved` (count) |
| `schedule.delete_rejected` | W3 attempt, Configuration, standard, outcome `rejected` | `schedule`: the schedule id | as above | `name`, `integrationId` (optional), `reason` (closed set: `enabled`) |

The recurrence, the target configuration and any free text have no field. A resource context id longer than the audit column is left out of the related reference instead of failing the row. *That omission is defensive code and is unreachable for persisted schedule data (clarified in work package 5, §13.4): `ScheduleDefinitions.ResourceContextId` is required, 64 characters, and a restricted foreign key to `ResourceContexts.Id` (also 64), and the audit column it is copied to (`AuditLogs.ResourceContextId`) is 64, so a stored schedule's context id always fits. The branch would run only for a value that never came from a stored schedule; the same bound applies to `integration.deleted` (§11.3), whose source column is also required and 64.* The legacy `Details` text and its `succeeded` and `rejected` outcome labels are gone from this path.

### 10.2 The delete as an audited unit

`SqlScheduleManagementService.DeleteScheduleAsync` is one `IAuditedUnitOfWork.ExecuteAsync` with `AuditActor.CurrentPrincipal`. The unit owns the execution strategy and the transaction; the service no longer opens either, and no longer appends a legacy record after the commit. Inside the attempt, in this order: the unit's operation lock, the ownership gate (as before, so it still serializes with enable), the schedule row lock (`GetByIdForUpdateAsync`), the removal of the states, assignments and definition, then the primary row. Unchanged: the controller's `schedule.manage` check, the service's refusal of a missing actor before anything is read, the physical deletion, and the retention of history (`ScheduleRuns`, start-request idempotency keys, notification instances and earlier audit rows keep the dangling schedule id).

| Case | Behaviour |
|---|---|
| Deleted | The graph removal and `schedule.deleted` commit together. HTTP 204 |
| Missing | The attempt declares a no-op and stages nothing. `ScheduleNotFoundException`, HTTP 404 |
| Enabled | The attempt declares a no-op, so nothing changes. A best-effort W3 `schedule.delete_rejected` row of the same principal is then written on its own context; a failure to write it is logged and never changes the refusal. `ScheduleDeleteRejectedException`, HTTP 409 |
| Audit insert fails, or any failure inside the attempt | Everything rolls back: the definition, assignments and states remain and no row exists |
| Lost acknowledgement after a real commit | The unit verifies the commit from the primary row (`EventId` equal to the operation event id) under the operation lock and reconstructs success from that row alone. The schedule is gone by design, and its absence is never read as a failed or missing delete. The attempt is not run again, so there is no false 404 |
| Verified rollback | One rerun with the same operation event id. Its not-found and refusal results are genuine |
| Verification unavailable, lock not granted | `outcome_unknown` (HTTP 500 with `code` and `operationEventId`); the delete and its row are together committed or together absent |
| Two independent deletes of one schedule | The second waits on the gate and the row lock, then finds nothing: a genuine 404. One primary row |

There is no response read after a schedule delete, so `committed_result_unavailable` cannot occur here. Without the unit the service refuses a delete with `AuditInvariantException` rather than deleting unaudited.

### 10.3 Exception mapping

`SchedulesController.Delete` no longer catches `InvalidOperationException`. It maps `UnauthorizedAccessException` to 403, `ScheduleDeleteRejectedException` to 409 and the new `ScheduleNotFoundException` (a subclass of `InvalidOperationException`, so base-type callers still fail closed) to 404. Anything else, an `AuditInvariantException`, a transaction or infrastructure failure, or an unknown or unconfirmed commit, reaches the host's handler (AUD-1.3 D19), where the audit outcomes already carry their codes. Before this change any such failure read as a false 404.

### 10.4 Tests and validation

| Test | What it shows |
|---|---|
| `ScheduleDeleteAuditEventTests` (unit, 9) | Catalog class, basis, category, target and fields of both actions; no field can carry the recurrence or free text; producer output and related references; what the schema refuses; the longest stored values fit; an oversized context id is left out |
| `ScheduleDeleteTests` (unit, adapted, plus 5 new) | The previous behaviours on the recording unit, with the event fields; the gate is taken inside the unit and before anything is staged; a lost acknowledgement runs the attempt once; an unknown outcome; no unit is an audit invariant; a failed rejection row leaves the 409 unchanged |
| `ScheduleServiceSharesTheUnitContextTests` (unit, 3) | With the production registrations (`AddApperturePersistence`, `AddAppertureAuditRecorder`, `AddSchedulePersistence`, `AddAppertureIntegrationWorkflows`, `AddAppertureGovernance`), the schedule service's unit, repository, ownership gate and integration repository each hold the scope's one `AppertureDbContext`; the W3 writer builds its own context from options; scopes do not share |
| `SchedulesControllerTests` (unit, adapted, plus 2 new) | 204, 403, 404 for the typed not-found, 409; an audit invariant, a transaction or infrastructure error, `outcome_unknown`, `committed_result_unavailable` and `operation_not_completed` are never turned into a 404 |
| `ScheduleDeleteAuditSqlServerTests` (SQL Server, 12) | Full graph removal with history preserved and exactly one primary row (actor, category, target, schedule and context references, correlation); the primary row carries the operation event id; an enabled schedule gives one rejection row and removes nothing; an unwritable rejection row leaves the 409; a typed not-found records nothing; audit insert failure rolls everything back; a lost acknowledgement is reported as success with one attempt and a missing schedule; a confirmed rollback reruns once; unknown outcome committed and not committed; the operation lock held by another session; two concurrent deletes commit one row and the loser is a 404 |
| `ScheduleDeleteSqlServerTests` (SQL Server, 8, existing) | The previous graph, history and race tests, now through the real unit with the actor seeded |

Validation (real SQL Server, `apperture-test-sql`, `APPERTURE_REQUIRE_SQL_TESTS=1`, credentials read from `.env` into the process environment only, `--artifacts-path artifacts/aud14s3 -m:1`):

| Command | Result |
|---|---|
| `Apperture.UnitTests` filtered to `Scheduling`, `ResourceContexts`, `Audit` (catalog and schemas included), `SchedulesController`, `Epic36MultiHostScheduler` and `IntegrationDeleteReferentialLifecycle` | 668 / 668 passed |
| `Apperture.IntegrationTests` filtered to `ScheduleDeleteAuditSqlServerTests`, `ScheduleDeleteSqlServerTests`, `ResourceOwnershipMoveConcurrencySqlServerTests`, `ResourceOwnershipMutationGateSqlServerTests` and `ResourceOwnershipLockingSqlServerTests` | 35 / 35 passed |
| `Apperture.ArchTests` | 38 / 38 passed |
| `dotnet build Apperture.slnx` | succeeded, 0 errors |

Not rerun: the full unit and integration projects. Not changed: the integration delete, the integration settings writers, the blocker writers and secret ownership.

### 10.5 Work package 3: the integration row lock in the writers

Nothing has a foreign key from an integration setting, a notification definition, a notification subscription or an attention-routing override to its integration. An integration delete counts its blockers and then removes the row, so any of these writers committing after the count would leave a row that refers to a deleted integration (and, for a sensitive setting, a live secret nobody can reach). Each writer now takes the integration row lock (`GetByIdForUpdateAsync`, `UPDLOCK, ROWLOCK`, the statement an ownership move and a binding create already use) inside the transaction that writes its row, requires the integration to exist, and keeps the lock to the commit. A delete that holds the row makes the writer wait and then refuse; a row that committed first is visible to the delete's blocker count.

| Writer | Lock position | Integration missing |
|---|---|---|
| `IntegrationSettingsService.SetAsync` (ordinary, sensitive replace, sensitive clear) and `ResetAsync` | First act of the audited unit's attempt, after the unit's operation lock and before the provider's options, the setting row and the secret | `KeyNotFoundException`; nothing is changed, staged or provisioned. The controller returns 404 (the host handler maps the same exception to 404) |
| `SqlNotificationDefinitionStore.CreateUserDefinitionAsync` | First statement after the store's own execution-strategy transaction begins; the insert and the commit follow | `InvalidOperationException` (the contract the definition create already had); 400 |
| `SqlNotificationSubscriptionStore.AddAsync` | As above, only when the command names an integration. A subscription without one takes no lock and keeps its previous behaviour (the operator self-subscription is unchanged) | `InvalidOperationException`; 400. A platform- or module-scoped definition with an integration id on the subscription previously had no existence check at all |
| `AttentionRoutingManagementService.UpsertIntegrationRoutingAsync` | First statement of one execution-strategy transaction that holds the lock through the team check, the insert-or-update decision, the write and the commit | `InvalidOperationException`; 404 (the controller's existing mapping) |

None of these writers was converted to an audited unit, and no ownership gate dependency was added. The in-transaction reset used by the future integration delete (`IIntegrationSettingResetInTransaction`) is unchanged and takes no lock: its caller holds the row.

**Authorization and ownership moves.** Each writer's authorization depends on the integration's governed identity, which includes its owning resource context, and was decided before the transaction. An ownership move that committed in between would have been acted on stale. The lock is the point at which the identity stops moving (a move holds the same row lock), so each caller can pass an optional `reauthorize` delegate that the writer runs once the lock is held: the settings controller re-runs `RequireIntegrationManageAsync`, the definition service the same check it made at the top, the subscription service the definition-access check (only for an integration-scoped definition: a platform-scoped one is authorized by the definition alone), and the routing endpoint the integration read check it made. The policy is the same checks as before; only their timing moved. A refusal at that point is `UnauthorizedAccessException` (403) with nothing stored. The settings writers could not simply authorize inside the service, because the integration delete (`integration.delete`) calls the reset today, and a delete-only role must not need `integration.update`.

**Lock order.** Audited operation lock (settings only), integration row, then the setting row or the dependent row, then the secret. The writers take no ownership gate, so they cannot invert with a mover that holds the gate and then the row.

**Transaction safety.** Each store and the routing service use the execution strategy and a transaction of their own, with no ambient unit around them. A failed attempt detaches only the row it added or loaded; nothing else on the context is cleared.

### 10.6 Tests and validation of work package 3

| Test | What it shows |
|---|---|
| `IntegrationRowLockSqlServerTests` (SQL Server, 58) | For each of the seven writer paths, on a locking READ COMMITTED catalog and on a READ_COMMITTED_SNAPSHOT catalog: a second session holds the row, the server's own wait list shows the writer waiting on `Integrations`, nothing is changed while it waits, and it completes when the integration survives; when the competing session deletes the integration the writer refuses with the documented exception and leaves no row, no secret version, no store evidence and no audit row; a failing re-run authorization changes nothing; an ownership move that commits while the writer waits is what the re-run check reads; an integration-scoped definition's subscription is authorized twice; routing insert then update keeps one row; a subscription without an integration is not held up by the row; a sensitive replace commits its row, secret and both audit rows together; five writers racing for one integration over six rounds produce no deadlock victim; a mover holding the ownership gate does not hold a writer up |
| Unit tests, adapted and new | The settings service locks before the provider and the setting read; a missing integration is `KeyNotFoundException` for set and reset; re-run authorization; both stores and the routing service refuse a missing integration and run the re-check; the definition service checks the integration twice |
| Existing fixtures | Synthetic integration ids now have an integration row (the Slice 2 settings SQL suite) or a mock that answers the locking read (the settings unit tests). Assertions were not changed |

The wait-and-refuse cases were also run with the lock temporarily replaced by a plain read at all three lock sites: all 28 failed ("the writer did not wait"). The code was restored.

Validation (real SQL Server, `APPERTURE_REQUIRE_SQL_TESTS=1`, credentials read from `.env` into the process only, `--artifacts-path artifacts/aud14w3 -m:1`):

| Command | Result |
|---|---|
| `Apperture.UnitTests` (all) | 4028 / 4028 passed |
| `Apperture.IntegrationTests` (all, includes the Slice 2 settings audit suite and the schedule delete suites) | 626 / 626 passed |
| `Apperture.ArchTests` | 38 / 38 passed |
| `dotnet build Apperture.slnx` | succeeded, 0 errors |

### 10.7 Boundary and carried findings

These tests prove that the writers take and respect the lock. The race between an integration delete and each writer, in both orders, needs the audited integration delete and is proven by its own tests (work package 4b); no temporary delete was written to run them early.

- The provider's option lookup for a provider-backed integration setting now runs while the integration row is held (it was already inside the unit). A provider that makes a slow external call lengthens the hold. Not changed here.
- The attention-routing endpoint is authorized by integration read only (section 7); the re-run keeps that policy and does not fix it.
- `ClearIntegrationRoutingAsync`, subscription removal and definition update do not create a reference and take no lock.
- A commit whose acknowledgement is lost is retried by the connection's execution strategy; the definition and subscription ids are fixed across attempts, so a retry after a real commit fails on the key instead of creating a second row.

## 11. Work package 4a: the mechanisms for the integration delete

Implemented on this branch after work package 3, and independently reviewed with work packages 3 and 4b afterwards (READY TO ACCEPT, §13.1). The rest of this paragraph describes the package as it stood when built, before 4b. This package prepares the mechanisms work package 4b needs and converts nothing: `IntegrationManagementService.DeleteIntegrationAsync` and its controller are unchanged, and nothing emits `integration.deleted` yet. The blocker counts, the coordinated deletion under the integration row lock, M6 (including a lost acknowledgement followed by a missing integration) and the controller's exception mapping are work package 4b. The closing documentation and the deferred LOW findings of work packages 1 and 2 stay with work package 5.

### 11.1 Owner ruling H1 (approved): refined conservative option B

During integration deletion:

A. Remove every persisted integration-owned setting override, even when its definition is missing, was reclassified, or is no longer integration-scoped.

B. Retire an associated settings-provisioned secret only when all of the following can be established safely: credible provenance shows the secret belongs to the integration's settings lifecycle; the secret's originating setting key corresponds to the historical override; no known surviving database reference requires the secret; and the secret store's lifecycle rules permit retirement.

C. If credible evidence of a usable settings-provisioned secret exists but ownership or safe disposition cannot be determined, refuse the whole integration deletion, atomically.

D. Never expose secret values or secret references in audit details, ordinary logs or API responses.

E. Do not claim exhaustive global referential knowledge. The supported evidence model has known limits (§11.9).

F. Avoid elaborate remediation infrastructure until real-world evidence establishes a need.

This settles DF2 of the slice 2 review (§9.2: keys with no definition, or not integration-scoped) as policy; work package 4a builds the mechanism and 4b applies it. Work package 4b takes its override list and its secret decisions from the 4a plan. **The statement in §8.8 and on `IIntegrationSettingResetInTransaction` that the delete will call the in-transaction reset for each scoped key is superseded for settings whose secret the plan governs**: that helper ignores keys with no definition and disables whatever secret a sensitive setting's name matches without looking for surviving references, which is exactly what H1 forbids. The interface now says so. LFC-1 §2.4 and the CTX-3B inventory still describe the old cascade and are for work package 5.

### 11.2 What was added

| Symbol | Assembly | Role |
|---|---|---|
| `AuditActionNames.IntegrationDeleted`, `AuditDetailSchemas.IntegrationDeleted`, `AuditEvents.IntegrationDeleted` | Application | The `integration.deleted` W1 contract and typed producer (§11.3) |
| `AuditEvents.IntegrationDeletedSecretRetirementReason` and the `integration_deleted` value of the `integration.settings.secret.retired` reason | Application | The Secrets-category row of a retirement the delete makes |
| `SecretReferenceReader` (internal) | Security | Reads references as GUIDs (§11.5) |
| `IntegrationSettingSecretInventory` | Security | Read-only inventory: persisted overrides, surviving references (§11.4, §11.5) |
| `IntegrationSettingSecretClassifier` | Security | The H1 classifier; returns an `IntegrationSettingDeletionPlan` (§11.6) |
| `IntegrationSettingDeletionPlan`, `IntegrationOverrideDisposition`, `PlannedSecretRetirement`, `IntegrationSettingDispositionRefusal`, `IntegrationSettingOverride`, `SurvivingSecretReference` and the outcome enums | Security | Result types. No `ToString` and no public member shows a secret, a reference or a stored value; the secret ids and stored values are `internal` |
| `IntegrationSettingSecretRetirement` | Security | Transactional retirement of what a plan allows (§11.7) |
| `IntegrationSettingDispositionRefusedException`, `IntegrationSettingSecretRetirementException` | Security | Typed failures with messages that name no secret |
| Registrations in `AddApperturePlatformSettingsManagementHost` | Security | The three services, scoped, on the request's one context |

### 11.3 The `integration.deleted` contract

Declared by AUD-1.0 as a legacy-bridge entry; it now has a schema and a producer. No action was added.

| | |
|---|---|
| Class | W1, `RequiredMutation`, basis `change`, Configuration, standard severity |
| Target | `integration`: the integration id |
| Related | `ResourceContextId` (the integration's owning context, left out when it exceeds the column) and `ModuleId` |
| Fields | `name` (name snapshot, required, truncatable), `moduleId` (optional id), `settingsRemoved` (count, required: the number of persisted integration-scoped overrides the delete removed, whether or not a definition for each still exists) |
| Never recorded | A setting key, value, secret or reference. The overrides are counted, not listed. A secret the delete retired has its own Secrets-category row, `integration.settings.secret.retired` with reason `integration_deleted`, targeting `{integrationId}:{key}` (D-1) |

A field named for a secret, credential or token cannot be declared (the schema refuses such names), which is why the count is `settingsRemoved` and not a count of secrets. The producer refuses a negative count and a name over 256 characters, and the schema refuses any undeclared field. The Secrets-category reason enum gained `integration_deleted`; the other two values are unchanged. The legacy `IAuditService` append in the current delete is untouched and still runs until 4b.

### 11.4 Persisted overrides

`IntegrationSettingSecretInventory.ListOverridesAsync` returns every `PlatformSettingValues` row whose scope is the integration (`ScopeKind = Integration`, `ScopeId` the integration id), in key order, untracked. It consults no definition, provider, registry or schema, applies no allow-list and sits under no query filter (there are none on this table). A removed key, a key that is now applicable at the instance scope only, and a current key are all returned. The set to remove is the set the delete's own predicate selects, so the inventory and the removal cannot disagree. The plan's `Overrides` lists them all; `Retirements` lists only the secrets that passed the rules of §11.6. These are separate questions and are answered separately.

### 11.5 Surviving references and GUID comparison

`FindSurvivingReferencesAsync` reads three known locations in full, untracked and with only the needed columns projected, and compares by GUID value:

| Location | Read as |
|---|---|
| `PlatformSettingValues`, every row outside the deleting integration (every scope, other integrations included) | `ValueJson`, a JSON document; every string in it |
| `Connections.SecretRefsJson` | A JSON object of field key to reference; the values (the names are field keys) |
| `EntraInstanceRegistrations.ClientSecretRef` | A plain text |

A reference is a text that `Guid.TryParse` accepts after trimming, the way the provisioners and `SqlSecretResolver` parse it: formats `D`, `N`, `B`, `P` and `X`, either case. The comparison is on the `Guid`; nothing compares JSON or raw text, and no query is narrowed by text, so no format can be missed by a prefilter. A value that is not what its column should hold is still searched: invalid JSON, an array where a text belongs, and a text with a GUID embedded in other characters all report every GUID-looking token (`D` or 32 hex digits, braces and parentheses irrelevant). That can only make a secret be retained, never retired. A malformed value that cannot mention the candidate blocks nothing. The limit: a format `X` GUID is read when it is the whole value or a whole JSON string, not when embedded in other text.

References held by this integration's own overrides are not survivors: the delete removes them. A reference from another integration, a platform or other setting, a connection or an Entra registration is. The scan is O(rows) over configuration data, runs only when a credible candidate exists, tracks nothing, saves nothing and opens no transaction. It joins the caller's transaction when there is one.

### 11.6 The H1 classifier

Read-only; run it inside the caller's transaction after the caller took its integration row lock.

**Evidence.** The override row alone proves nothing: a setting that holds a reference does not own the secret it names. The secret store records no owner id; its provenance is the provider (`platform-setting`) and the name (`platform-setting:{key}`) the settings lifecycle gave it. That says which setting key a secret was made for, not which integration owns it (two integrations' overrides of one key have secrets with the same name). Ownership by this integration is therefore established by three facts together: the secret is settings-provisioned and usable, its name matches the key of the one override that refers to it, and nothing outside this integration refers to it.

**A definition is a hint, never a gate.** A definition that declares the setting sensitive raises the bar: a value on it that is not one readable reference is ambiguous. Without a definition, or with an ordinary one, any GUID the value holds is still looked up in the store, and a settings-provisioned secret found is treated exactly as for a sensitive setting. A missing or reclassified definition therefore cannot hide a credential and cannot authorize its retirement.

| Outcome | When |
|---|---|
| `NoApplicableSecret` | No reference; the reference resolves to nothing; the secret has another provider (never retired here); the secret is already disabled or revoked |
| `RetireAfterOverrideRemoval` | A usable settings-provisioned secret, exactly one override refers to it, its name is `platform-setting:{that key}`, and no surviving reference exists |
| `RetainSharedReference` | A usable settings-provisioned secret that a surviving reference still holds |
| `Ambiguous` | `UnreadableReference` (a sensitive setting's value is not one readable reference); `OwnershipNotEstablished` (the secret's name carries no setting key); `SettingKeyMismatch` (the name is for another key, or several overrides refer to the secret) |

Several overrides referring to one secret share **one** disposition and the secret is planned for retirement at most once. A secret that one override names twice, in different formats, is retired once. Any ambiguous override refuses the whole delete: the plan carries a typed `Refusal` (the keys and reasons), `ThrowIfRefused` raises `IntegrationSettingDispositionRefusedException` (an `InvalidOperationException`, like the delete's other blockers, with a message that names no secret, reference, key or metadata), and a refused plan carries **no** retirement, so the secret that was safe is not retired either. Ambiguity is never reported as a successful no-op.

**Package decision P-4a-1.** A credible secret that a surviving reference still holds is *retained* even when its ownership is unclear. The disposition is then decided (leave it alone), and leaving it alone is safe whoever owns it. Ambiguity refuses only when the choice between retiring and retaining cannot be made safely.

**Package decision P-4a-2.** An unreadable value is ambiguous only on a setting whose definition declares it sensitive. On any other setting a value that names no GUID is simply no reference, so a malformed value unrelated to a credential does not block a delete.

*Corrected in work package 5 (§13.3).* This paragraph first said the remedy for a refused delete is to reset the setting. That holds for one case only: a key that is still defined and still applicable at integration scope, where `DELETE /api/integrations/{id}/settings/{key}` removes the override (and disables the secret named for that key without looking for other references, so it is an operator's judgement, not a safe automatic step). It is **not** a remedy for the two historical cases H1 exists for: a key with no definition (the reset finds none and answers 404) and a key reclassified so that it no longer applies at integration scope (the reset's scope check rejects it with 400). Some refused deletes therefore have no supported self-service remedy, and a stored credential the instance can no longer find is not recoverable by this package. Resolving them needs a controlled investigation of the integration's stored rows by someone with database access; nothing here builds that.

### 11.7 Transactional retirement and atomic refusal

`IntegrationSettingSecretRetirement.RetireAsync(transaction, plan, resourceContextId)` retires what a non-refused plan allows, inside the caller's audited unit.

- It commits, begins and rolls back nothing. The SQL secret store joins the unit's transaction on the shared context (slice 1; `SettingsServicesShareTheUnitContextTests` pins that the inventory, classifier and retirement hold the scope's one context and store), so a secret's disabling, the store's own evidence and the retirement row commit or roll back with the caller's delete. A caller that refuses or fails afterwards rolls them all back. It never declares a no-op, and because it stages a row for each secret it retires, the unit refuses a no-op declared after it (a partly changed unit cannot become one).
- Classify first, as a read, then act: a refused plan carries no retirement, and `RetireAsync` throws before reading or staging if given one. Nothing is mutated before every credible ambiguity has been evaluated.
- Each secret is re-read in the caller's transaction (skipped without a row if it is no longer a usable settings-provisioned secret of that key), deduplicated, staged *before* it is changed (so a transaction that is not current fails before any change), then disabled. One Secrets-category row each, a secondary row of the caller's unit, reason `integration_deleted`, carrying neither secret nor id.
- A store failure whose text names the secret (the store's messages embed its id) is reported as `IntegrationSettingSecretRetirementException`, whose message names nothing and which carries the setting key and the failure's type name. The store's exception is deliberately not attached. Any other failure (a staged row that cannot be saved) propagates unchanged.
- The order of the caller's work stays the caller's: the overrides are removed before the retirements run, so a secret is never retired while the row that named it remains.

### 11.8 Concurrency and consistency

The primitives take no lock, open no transaction and assume the caller holds the integration row lock (WP3), taken first. They add no lock-order edge: the lock order stays the operation lock, the integration row, the setting row, then the secret.

What the known writers guarantee, from the code:

| Writer of a reference | Coordinated with the delete |
|---|---|
| This integration's settings writers; notification definition create, admin subscription add, attention-routing upsert | Yes, by the integration row lock (WP3). The setting writers cannot add a reference to this integration's overrides while the delete holds the row |
| Another integration's settings writer, and platform settings | No lock in common. They create or version a secret of their own, and no path copies a reference between rows |
| Connection create and update | **No.** They take no integration lock and accept operator-supplied references (`ValidateConnectionMaterial` checks only that a reference parses as a GUID, not that it exists or whose it is) |
| Entra registration writers | No. They store a secret of their own |

The scan is a read, not a lock, and is not stable under either isolation mode. Under locking READ COMMITTED it waits on a writer's uncommitted row and then sees it, but releases its read locks as it goes, so a reference committed after the read is not seen. Under READ_COMMITTED_SNAPSHOT it reads the last committed version and does not see an uncommitted reference at all. `Under_locking_read_committed_a_reference_being_written_makes_the_scan_wait_but_under_snapshot_isolation_it_is_not_seen` shows both against SQL Server. No assurance that a read-only scan is stable under RCSI is claimed. A connection written to name a candidate secret between the scan and the commit therefore survives the delete with a reference to a retired secret; H1 accepts this limit (§11.1 E).

**Gap for 4b.** A connection write is the one known writer that can race the scan and is not coordinated. Work package 4b should state this as accepted, or decide whether a connection reference to a settings-provisioned secret should be refused at the connection writer; that is not decided or done here. The delete-versus-writer race suite for the WP3 writers is 4b's.

### 11.9 Known limits of the evidence model

- Three database locations only. A reference in configuration files, in an external system, or in any table or column added later is invisible.
- Provenance is a provider and a name pattern, not an owner id. A secret created by hand with the settings provider and a conforming name is indistinguishable from a provisioned one.
- A settings-provisioned secret that no override of this integration names is not found at all (there is nothing to start from). Orphaned secrets from earlier defects are out of scope.
- The scan is neither locked nor stable (§11.8).
- Embedded format `X` GUIDs in malformed text (§11.5).

### 11.10 Dependencies for work package 4b

*Addressed by work package 4b (§12), with these departures: the overrides are removed by their stored row id, not through `DeleteScopedAsync`, which rebuilds the id from the key and would miss a row stored under another id shape (a correction of the second bullet); the primary row is staged before the removals; the H1 refusal is a 409 with a code, not the blockers' 400 (§12.7); and the connection-reference gap is accepted as bounded (§12.8), not closed.*

- Acquire the integration row lock first, then the blocker checks, then `ClassifyAsync`, all inside the unit; call `plan.ThrowIfRefused()` before any change.
- Remove **every** override in `plan.Overrides` (the repository's `DeleteScopedAsync` works without a definition), then `RetireAsync`, then stage `integration.deleted` with `settingsRemoved = plan.Overrides.Count` (staging the primary row first is also fine; the helper only needs a current transaction).
- Do not call `IIntegrationSettingResetInTransaction` for a setting the plan governs (§11.1). Per-key `integration.settings.reset` rows for the removed overrides are 4b's decision; the count on `integration.deleted` is the required evidence.
- Map `IntegrationSettingDispositionRefusedException` like the delete's other blockers (a refusal, with nothing changed) and exclude `IntegrationSettingSecretRetirementException` and `AuditInvariantException` from any refusal mapping.
- Decide the connection-reference gap (§11.8).
- Replace the legacy `IAuditService` append with the staged event and update the tests that assert it.

### 11.11 Tests and validation

| Test | What it shows |
|---|---|
| `IntegrationSettingSecretDispositionSqlServerTests` (SQL Server, 81 cases) | Each case on a locking READ COMMITTED catalog and on a READ_COMMITTED_SNAPSHOT catalog, with an execution marker each, and the delete's steps run in a real audited unit by a harness that is not the delete. An ordinary historical override; a removed definition and a reclassified one, each with an owned secret still retired; overrides with no definition, a reclassified definition and a current one all removed and counted. An owned secret with a matching key and no survivor retired with `integration.deleted` and its Secrets row as one operation, with the store's `Disable` evidence and no secret, id or plaintext in any audit or store column. Retained for another integration's override, a platform setting, a connection's references and an Entra registration. GUID equivalence: formats `D`, `N`, `B`, `P`, `X`, upper case, against the four survivor kinds (19 combinations per isolation mode; `X` is longer than the Entra column), with the override itself written in padded upper-case `N`. Unexpected shapes: a connection's invalid JSON that mentions the secret retains it, one that cannot mention it blocks nothing. Two overrides naming one secret in different formats get one ambiguous disposition and retire nothing; one override naming its secret twice retires it once. A name with no key, a name for another key, and an unreadable credential value refuse; an unreadable ordinary value does not. A surviving reference decides over unclear ownership. Another provider's secret, a dangling reference and a disabled secret are never retired. A refused delete changes nothing, not even the secret that was safe, and its exception names no secret. An audit failure after the retirement rolls back the secret, the overrides, the store evidence and every row. A rolled-back commit reruns and retires once. Classification is read-only: nothing tracked, nothing saved, no transaction, only `SELECT` statements. The scan waits on an uncommitted writer under locking READ COMMITTED and does not see it under snapshot isolation |
| `IntegrationSettingSecretDispositionTests` (unit, 8) | The classifier's logic on an in-memory context: the plan, the redaction of every printed form, a refused plan retiring nothing, staging before the change, once-only retirement, a secret that stopped being usable, a store failure reported without the secret's id, and non-credential and unreadable values |
| `SecretReferenceReaderTests` (unit, 18) | Every format, case and padding, as JSON text and as plain text; a connection's references by value and not by name; arrays; embedded and malformed text; what is not a reference |
| `IntegrationDeleteAuditEventTests` (unit, 10) | The catalog class, basis, category, target and fields; no field for a value, secret, reference or free text; the producer and its related references; the serialized details; refused payloads; the longest stored values; the `integration_deleted` retirement reason |
| `SettingsServicesShareTheUnitContextTests` (unit, 1 new) | The inventory, classifier and retirement hold the scope's one context and secret store |

**Mutation evidence** (product code edited temporarily, restored byte for byte, never committed): not scanning connections failed 19 cases; never refusing on ambiguity failed 10; ignoring surviving references failed every retain case; accepting only the `D` text format failed the format matrix. All pass with the shipped code.

Real SQL Server (`apperture-test-sql`), `APPERTURE_REQUIRE_SQL_TESTS=1`, credentials read from `.env` by the test process (never on a command line), builds with `--artifacts-path artifacts/aud14w4a -m:1`:

| Command | Result |
|---|---|
| `dotnet build Apperture.slnx` | succeeded, 0 errors |
| `dotnet test tests/Apperture.UnitTests` (all) | 4065 / 4065 passed (4028 before, 37 new) |
| `dotnet test tests/Apperture.ArchTests` | 38 / 38 passed |
| `dotnet test tests/Apperture.IntegrationTests --filter IntegrationSettingSecretDispositionSqlServerTests` | 81 / 81 passed |
| `dotnet test tests/Apperture.IntegrationTests` filtered to the `IntegrationTests.Audit` namespace (every audit SQL suite, including this one, the settings, secret store, Entra and schedule delete suites and the classification benchmark), `ManagementHostIntegrationConfigurationHttpTests`, `ResourceOwnershipMoveConcurrencySqlServerTests`, `ScheduleDeleteSqlServerTests`, `ManagementHostGovernanceHttpTests` and `IntegrationTestSecretProtectorTests` | 429 / 429 passed, none skipped (21 minutes, the benchmark included) |

Not run, with the reason: the full `Apperture.IntegrationTests` project (the filtered run covers every consumer of the changed catalog entry, retirement reason and registrations; PR CI runs all of it), the UI test projects and the SEC-1 browser suite (no UI, route or response changed).

### 11.12 State after work package 4a

*As it stood when 4a was built.* Work package 4a is implemented and validated. The integration delete is exactly as it was: it still resets each setting in its own unit, removes the integration in a separate save, and appends the legacy audit record afterwards. Work package 4b converts it with the primitives above; work package 5 closes the slice. Slice 3 is not complete.

## 12. Work package 4b: the audited integration delete

Implemented on this branch after work package 4a, and independently reviewed with work packages 3 and 4a afterwards (READY TO ACCEPT, §13.1). Nothing here claims AUD-1.4 complete.

### 12.1 The delete as one audited unit

`IntegrationManagementService.DeleteIntegrationAsync` is one `IAuditedUnitOfWork.ExecuteAsync` with `AuditActor.CurrentPrincipal` and primary action `integration.deleted`. The unit owns the execution strategy and the transaction; the service opens neither and commits nothing itself. The legacy `IAuditService` append after the commit, the per-key `IIntegrationSettingsService.ResetAsync` calls in units of their own, and the separate save of the integration row are gone, and the service no longer depends on `IAuditService` or `IIntegrationSettingsService`.

| Step | Where | What |
|---|---|---|
| 0 | Before the unit | A missing actor is refused (403). The integration is read and `integration.delete` evaluated as an early answer (404, 403) before a unit is opened. This decides nothing: steps 2 and 3 repeat it |
| 1 | Unit | The unit's own operation lock (`sp_getapplock` on `apperture.audit-op.{operationEventId}`: per operation, so it serializes nothing between operations) |
| 2 | Attempt | The integration row lock: `GetByIdForUpdateAsync` (`UPDLOCK, ROWLOCK`, untracked), held to the commit or rollback. Missing: `IntegrationNotFoundException`, nothing staged |
| 3 | Attempt | `integration.delete` evaluated again against the row as locked |
| 4 | Attempt | The five blockers, counted (§12.2) |
| 5 | Attempt | `IntegrationSettingSecretClassifier.ClassifyAsync` on the unit's context and transaction, then `plan.ThrowIfRefused()`, before any stage or change |
| 6 | Attempt | Stage `integration.deleted` (`settingsRemoved` = the plan's override count) |
| 7 | Attempt | Remove every override in the plan by row id (§12.4). Originally followed by a recount of the integration's overrides; removed in work package 5 (§13.2) |
| 8 | Attempt | `IntegrationSettingSecretRetirement.RetireAsync`: each safely owned, unshared secret disabled once, each with its Secrets-category row (reason `integration_deleted`) |
| 9 | Attempt | Remove the integration row |
| 10 | Unit | Save, check every staged row was inserted, commit; on an uncertain commit, verify (§12.6) |

**Lock order:** operation lock, integration row, then the rows the attempt reads and writes (blocker tables, setting rows, secret descriptors). **No ownership gate.** The mover (`ResourceOwnershipMoveService.MoveIntegrationAsync`), binding create and attached-schedule create take the gate and then this row; a delete that took the gate after the row could deadlock with them, and a delete moves no ownership and makes nothing archive-blocking, so it needs none. The WP3 writers take no gate either. The delete therefore adds no edge to the lock graph: everything that contends with it does so on the integration row, which is first for both.

### 12.2 The five blockers under the lock

The CTX-3B referential-lifecycle inventory's five current-state blockers, counted inside the transaction after the row lock, in this order: `Bindings`, `ScheduleDefinitions`, `NotificationDefinitions` (integration-scoped), `NotificationSubscriptions` by their own `IntegrationId` (so an integration-associated subscription under a platform- or module-scoped definition counts; one with no integration id is not a blocker), and `IntegrationAttentionRoutings`. A blocker is `IntegrationDeleteBlockedException` (typed `Blocker` and `Count`, the operator-facing message unchanged), and the attempt rolls back with nothing staged or changed.

**Why a plain count is enough under both isolation modes.** Every writer that can add a blocker row holds this same row lock from before its insert to its commit: binding create and attached-schedule create (already), notification definition create, integration-associated subscription add and attention-routing upsert (WP3). So when the delete holds the row, no uncommitted blocker row of this integration can exist, and every one that committed did so before the delete's lock was granted. Under locking READ COMMITTED the count reads committed rows; under READ_COMMITTED_SNAPSHOT each count statement reads the data committed before it started, which includes all of them. A writer that arrives later waits on the row and, if the delete commits, wakes to a missing integration and refuses. No count is taken before the lock and none decides anything outside the transaction.

Graph: no table has a foreign key to `Integrations` (the model was inspected), so the integration row and its `PlatformSettingValues` are the whole delete graph; nothing cascades. Historical `IntegrationId` columns (runs, start requests, workflow instances, platform events, notification instances, attention incidents, audit rows) are untouched and dangle, as CTX-2 established. `ScheduleAssignments` and `ScheduleStates` are covered by the schedule blocker.

### 12.3 Authorization and ownership moves

`integration.delete` is evaluated against `integration.GetIdentity()` of the row **as locked**, so a move or any identity change that committed while the delete waited is what the decision reads, and none can commit after it until the delete ends. A refusal there is `UnauthorizedAccessException` (403) with nothing changed. The delete's internal removal of settings and secrets authorizes nothing further: deleting never needs `integration.update`. `IIntegrationManagementService.DeleteIntegrationAsync` is the only entry point (the controller calls it); the 4a primitives are building blocks that change nothing without a caller's unit and a plan, and the in-transaction settings reset is not called.

Today's policy for `integration.delete` does not itself read the owning resource context (the identity carries the scope key); the post-lock evaluation still matters for role and identity changes, and the SQL test proves its timing with a context-sensitive policy: a move that commits while the delete waits is the owner the second check reads, and the delete is refused.

### 12.4 Historical overrides and H1

The plan's `Overrides` (WP4a §11.4) is every persisted override of the integration, found by ownership alone. The delete removes them **by their stored row id**, tracked and saved in the unit, so a key with no definition, a reclassified key and a row whose id predates today's `{kind}:{scope}:{key}` shape are all removed. The row count must match the plan (otherwise `DbUpdateConcurrencyException`, rollback). *Work package 5 removed the check this paragraph first described, a recount of the integration's overrides after their removal (§13.2): it read the whole settings table and could deadlock two deletes. The lock protocol, the plan's ownership predicate, the row-count match and the per-row version check carry the same guarantee.* The legacy `IIntegrationSettingResetInTransaction` is not used (WP4a §11.1).

Classification runs on the request's scoped context, which is the unit's: the inventory's queries, the store's descriptor reads and the reference scan join the open transaction (`SettingsServicesShareTheUnitContextTests` and `IntegrationServiceSharesTheUnitContextTests` pin the context; the SQL tests run them in the real unit). It runs after the blockers and immediately before the mutations, so the read-to-retirement window is the rest of one attempt. An ambiguous plan refuses before anything is staged; the refusal is never caught, never declared a no-op. A secret retained because a surviving reference holds it stays active and its override is still removed.

**WP4a review of the interpretation boundaries (no correction needed).** A missing or reclassified definition is only a hint and does not suppress store evidence; a malformed value on an ordinary or undefined setting still reports any GUID token it holds, which is then judged by the store's provenance; a secret named by several overrides is one disposition and is retired at most once, and a name that matches one of several keys is still a mismatch (refusal unless a survivor retains it); a retained-by-survivor outcome needs a confirmed surviving reference, and the absence of one is never presented as global proof (§11.9). The SQL suite exercises each through the delete path. **No WP4a code was changed in 4b** apart from adding the refusal's machine code (`IntegrationDeleteRefusalCodes`).

### 12.5 Audit evidence

| Row | Role | Content |
|---|---|---|
| `integration.deleted` | primary, W1, Configuration, standard, basis `change` | Target `integration:{id}`; related resource context (the owner as locked) and module; `name` (snapshot), `moduleId`, `settingsRemoved` (every override removed, defined or not). No key, value, secret or reference |
| `integration.settings.secret.retired` | secondary, Secrets, elevated | One per secret retired; target `{integrationId}:{key}`; reason `integration_deleted`; no secret or id |

Both carry the unit's actor and share the operation event id; the primary row's event id is that id. **Package decision P-4b-1: no per-key `integration.settings.reset` rows.** The gate requires none for a delete (§13.6 and §22 list the delete's own row), a removed key with no definition has no sensitivity to record, and a reset row describes an operator resetting one setting, not the removal of an integration; the count on the primary row is the evidence. Before this package the delete wrote one reset row (and possibly one retirement row with reason `reset`) per key in separate units, plus a legacy text record; all of that is replaced by the rows above, written once, in the delete's own transaction.

### 12.6 Ambiguous commits (M6)

The unit's verification is unchanged and reused: on an uncertain commit it takes the operation lock on a fresh connection and reads the primary row by **this operation's event id**. Present: a verified commit, and the delete reports success (204) through a reconstruction that returns success from that row alone and never reads the integration; the attempt is not rerun, so there is no second primary row, retirement row or `Disable`. Absent: a confirmed rollback, and the attempt reruns once with the same event id (one of everything). Lock not granted or verification failed: `outcome_unknown` (500 with code and event id). Success is never inferred from the integration being absent: inside the attempt a missing integration is `IntegrationNotFoundException` (404), whether it never existed or another delete committed first, and that operation's own primary row is the only success evidence.

### 12.7 HTTP mapping

| Outcome | Exception | Status |
|---|---|---|
| Deleted, or verified commit after a lost acknowledgement | none | 204 |
| No actor, or `integration.delete` denied before or under the lock | `UnauthorizedAccessException` | 403 |
| Integration does not exist | `IntegrationNotFoundException` (new, typed) | **404** (was 400) |
| A blocker | `IntegrationDeleteBlockedException` (new, typed) | 400 with the same `{ error }` message as before |
| Undetermined credential disposition (H1) | `IntegrationSettingDispositionRefusedException` | **409** `{ error, code: "integration_setting_credential_undetermined" }` (new) |
| An override changed by a writer outside the protocol during the delete | `DbUpdateConcurrencyException` | 409 |
| Audit invariant, retirement failure, transaction or infrastructure failure, `outcome_unknown`, `committed_result_unavailable`, `operation_not_completed` | as thrown | The host handler (500, with the audit codes); never a 4xx |

The controller no longer maps every `InvalidOperationException` to 400 (AUD-1.3 D19). Mapping is by type, never by message text. The 404 for a missing integration is an intentional change from the previous 400, made to match the schedule delete; the management client surfaces any non-success with its `{ error }` message, so the UI is unaffected. Blockers keep 400 so the existing client and UI contract (`ManagementClientIntegrationDeleteTests`) is unchanged. The refusal's message names no secret, reference, key or classification detail; its keys are on the typed refusal and are not returned. A retirement failure's message names nothing and does not carry the store's exception (whose message embeds the secret id), so the host handler's error log of the exception cannot carry the id either.

### 12.8 Connection references (bounded risk, unchanged)

Verified: connection create and update take no integration lock and accept operator-supplied GUID references (§11.8). The delete performs its inventory inside its audited transaction, after the lock and the blockers and immediately before the mutations, so every reference committed before the scan is seen and the window is not widened. A connection that comes to name a candidate secret after the scan and before the commit is not seen under either isolation mode; the delete may then retire a secret that connection names. This is the limit owner ruling H1 accepts (§11.1 E, §11.9); it is not closed here, no connection writer was changed, and nothing claims the scan is serializable or snapshot-stable.

### 12.9 Tests

| Test | What it shows |
|---|---|
| `IntegrationRowLockSqlServerTests` (+52, SQL Server, both isolation modes) | **Writer first** (the writer paused just after its own locked read, the delete shown by the server's lock-wait list to be waiting on `Integrations`, then the writer released), for all seven WP3 writers: a definition, an integration-associated subscription and a routing override make the delete refuse with the matching typed blocker and nothing committed; an ordinary set, a sensitive replace, a clear and a reset are followed by a delete that removes whatever they left (`settingsRemoved` 1, 1, 0, 0) and retires the replace's new secret. **Delete first** (the delete paused just after its locked read, the writer shown waiting): the writer wakes to a missing integration and refuses (`KeyNotFoundException` or `InvalidOperationException`), leaving no override, definition, subscription, routing, secret, version or setting audit row; the prepared secret of the clear case is retired by the delete. **Delete first and refused** (a pre-existing blocker): the writer then completes normally. An ownership move committed while the delete waits is what the delete's second check reads (refused, nothing changed). A mover holding the ownership gate waits for the delete's row and then finds the integration gone, with no deadlock. Five writers and a delete racing for one integration over six rounds: no deadlock victim, every failure a protocol refusal, and either the integration survives with no `integration.deleted` row or nothing refers to it and no setting secret it owned is still usable. A subscription with no integration is not held up and does not block |
| `IntegrationSettingSecretDispositionSqlServerTests` (+36, SQL Server, both isolation modes) | Through the real delete: ordinary, undefined and reclassified overrides and a legacy-id row are removed and counted (`settingsRemoved` 4), no per-key reset rows; owned secrets of an undefined and a reclassified key retired; an owned secret retired with both rows in one operation, actor, category, target, context, module, event ids, no key in the primary details, no secret material anywhere; retained for each of the four survivor kinds with the override still removed; ambiguous ownership and a key mismatch with two overrides refuse with nothing changed (including the safe secret) and a message naming nothing; a secret named twice retired once; an audit insert failure and a failure of the final integration delete each roll back the integration, overrides, secret, store evidence and every row; a lost acknowledgement is one success with one of everything and no rerun; a confirmed rollback reruns once; an unconfirmed commit is `outcome_unknown` and nothing reads as done; a second delete and a row that vanished before the lock are 404 with one primary row at most; authorization is checked twice, only `integration.delete`, and a refusal under the lock changes nothing. GUID formats `D`, `N`, `B`, `P`, upper and lower case, appear in the overrides and survivors |
| `IntegrationsControllerDeleteTests` (unit, 7) | 204, 403, 404, 400 with the message, 409 with the code and no key, secret or classification, 409 for a concurrency conflict; audit invariants, retirement failures, plain `InvalidOperationException` and the three audit outcomes reach the host handler unchanged |
| `IntegrationDeleteReferentialLifecycleTests` (unit, adapted and 3 new) | The previous behaviours on the recording unit with typed exceptions; one staged primary row and no legacy record; a refused delete commits nothing; a missing integration is typed; a sensitive setting's retirement is recorded under the delete with no reset rows; the delete refuses without its unit |
| `IntegrationServiceSharesTheUnitContextTests` (unit, new) | With the Management host's registrations the delete's unit, repository, classifier inventory, secret stores and retirement each hold the scope's one context |
| Adapted | Test harnesses that construct `IntegrationManagementService` without deleting no longer pass the removed dependencies; `ScheduleDeleteTests` asserts the staged row |

A further race, **two deletes**: the second passes its early check, waits on the row inside its unit while the first commits, and is then `IntegrationNotFoundException` with one primary row in all (2 cases, both modes; part of the 52 race cases counted below).

**Mutation evidence** (product code edited temporarily, restored byte for byte and rebuilt, never committed):

| Mutation | Result |
|---|---|
| The delete reads the integration row without the lock | 49 failures across the race and delete-path suites (the waits are never observed; blockers committed after the count; post-lock authorization never sees the move) |
| Overrides removed by a key-built id instead of the stored row id | 2 failures (the legacy-id row survives and the delete rolls back) |
| H1 refusal skipped, in the delete and in the retirement's own guard | 4 failures (both refusal cases delete). With only the delete's call removed the retirement's guard still refuses and the unit rolls back, so that single mutation is not observable by design |
| The primary row not staged in the delete's transaction | 24 failures (the unit refuses to commit without it) |
| A missing integration under the lock read as success (a no-op returning 204) | 4 failures: the row that vanished before the lock and the two-delete race |

**Validation** (real SQL Server `apperture-test-sql`, `APPERTURE_REQUIRE_SQL_TESTS=1`, credentials read from `.env` by the test process and never on a command line, `--artifacts-path artifacts/aud14w4b -m:1`):

| Command | Result |
|---|---|
| `git diff --check` | clean |
| `dotnet build Apperture.slnx` | succeeded, 0 errors |
| `dotnet test tests/Apperture.UnitTests` (all) | 4077 / 4077 passed (4065 before, 12 new) |
| `dotnet test tests/Apperture.ArchTests` | 38 / 38 passed |
| `dotnet test tests/Apperture.IntegrationTests --filter IntegrationRowLockSqlServerTests` | 110 / 110 passed (58 WP3, 52 new) |
| `dotnet test tests/Apperture.IntegrationTests --filter IntegrationSettingSecretDispositionSqlServerTests` | 117 / 117 passed (81 WP4a, 36 new) |
| `dotnet test tests/Apperture.IntegrationTests` filtered to the `IntegrationTests.Audit` namespace (every audit SQL suite: the two above, settings, secret store, Entra, schedule delete, the classification benchmark), `ManagementHostIntegrationConfigurationHttpTests`, `ResourceOwnershipMoveConcurrencySqlServerTests`, `ScheduleDeleteSqlServerTests`, `ManagementHostGovernanceHttpTests` (the real Management host's dependency graph, which now builds the converted service) and `IntegrationTestSecretProtectorTests` | 517 / 517 passed, none skipped (29 minutes) |

Not run, with the reason: the full `Apperture.IntegrationTests` project (the filtered run covers every consumer of the changed service, constructor and catalog entry; PR CI runs all of it), the UI test projects and the SEC-1 browser suite (no UI changed; the API change is the status of a missing id and the new 409).

### 12.10 Residual risks and findings

- **Connection references** (§12.8): accepted, bounded, documented; not closed.
- **Status change**: deleting a missing integration is now 404 (was 400); a credential refusal is a new 409. `docs/04-api/contracts.md` now specifies both deletes (work package 5, §13.5).
- **No W3 refusal row** for a blocked or H1-refused integration delete. Before, a blocked delete wrote no audit row either; the catalog has no `integration.delete_rejected` action and none was invented (the schedule delete's W3 row exists because the gate names it).
- **An actor that maps to no user** now fails in the unit (as every converted writer does) instead of deleting with a legacy record carrying the raw actor id.
- **`IIntegrationSettingResetInTransaction`** has no production caller any more; it is kept, with its documentation corrected (work package 5 kept it, §13.5).
- The early check before the unit is advisory; its 404 or 403 can only be stricter than the unit's own decision a moment later, never more permissive.
- The WP1/WP2 optional LOW findings were left for work package 5 (§13.4). Findings outside AUD-1.4 (§7) are unchanged.

### 12.11 State after work package 4b

The integration hard-delete is converted. AUD-1.4 is not complete; slices 4 to 8 have not started. (Review, remediation and the state of slice 3: §13.)

## 13. Work package 5: review, remediation and the close of slice 3

Written after the combined independent review of work packages 3, 4a and 4b. This package changed one line of product behaviour (§13.2), documentation and tests; it reopened no ruling and did not repeat the review. **The owner has not yet reviewed or accepted work package 5, and nothing here approves a merge.** *(Status when written. The owner accepted work package 5 and merged PR #18; see §14.)*

### 13.1 The combined review

| | |
|---|---|
| Reviewer and verdict | Independent review (Opus 5.5, high effort) of work packages 3, 4a and 4b together: **READY TO ACCEPT**. No critical, high or medium finding |
| Checked and found sound | Transaction atomicity of the delete; no secret-safety violation found; no supported authorization bypass (every current production call site of the row-locked writers authorizes again after the lock); the M6 committed-success reconstruction is built from the committed primary row |
| Independent test evidence | 102 of 102 focused unit tests; 227 of 227 SQL Server tests (110 integration-row-lock and 117 H1 and delete-path), each on a locking READ COMMITTED catalog and a READ_COMMITTED_SNAPSHOT catalog |
| Model reproductions (not product incidents) | The post-removal recount deadlocked two deletes of different integrations in a tempdb model of the table and index shape: error 1205 in 3 of 3 trials. The audited unit's verification count deadlocked in a tempdb model: 2 of 2 trials |

| Finding | Disposition |
|---|---|
| **L-1** cross-integration delete deadlock in the post-removal recount | **Fixed** (§13.2) |
| **L-2** the H1 refusal's remediation guidance (P-4a-2) is wrong for undefined and reclassified keys | **Documentation corrected**; fail-closed behaviour unchanged; no new diagnostic (§13.3) |
| Optional: the trailing `reauthorize` parameters are a future-caller hazard | Contracts documented on the four interfaces (§13.5) |
| Optional: catalog `Writer` of `integration.settings.secret.retired` names one producer | Corrected (§13.5) |
| Optional: `IIntegrationSettingResetInTransaction` has no production caller | Kept, documentation made explicit (§13.5) |
| Observation: audited units can deadlock in the recorder's own verification count | **Tracked, not implemented**: §7 (deferred, proposed AUD-1.6) |
| WP1 and WP2 optional LOW findings (L1, L2) | **Resolved** (§13.4): L1 verified, L2 by documentation |

### 13.2 L-1: the recount removed

**Mechanism.** After removing the integration's overrides, `RemoveOverridesAsync` counted the table's rows for that integration (`ScopeKind` and `ScopeId`). `PlatformSettingValues` has a unique index that begins with `Key`, and nothing that begins with the scope, so the count read the whole table. Under locking READ COMMITTED it waited on the rows another integration's delete had removed and still held, while that delete's own count waited on this one's: a cycle between two deletes of different integrations.

**Decision: remove the count.** It checked nothing the transaction does not already guarantee:

- the plan was read after the integration row lock, with the predicate that selects an integration's overrides, and the removal takes the plan's rows by their stored ids, so no override the plan saw is left;
- every writer of an integration-scoped override (the four settings changes) takes that same row lock first, so none can commit a new one between the plan and the commit; the platform settings writer stores instance-scope rows only, and the one migration-time writer updates a column on existing rows;
- the number of rows loaded must equal the plan's, and each row is deleted by primary key and row version, so a row that vanished or changed after the plan fails the save and rolls the delete back.

What the count additionally caught was a writer outside the lock protocol, and none exists. It is not claimed to be caught now. H1 inventory completeness is unchanged: the plan, the classifier and the surviving-reference scan are untouched. No index and no migration were added: an index on the scope would have fixed this scan, but it would be a migration to keep a redundant check. The classifier's whole-table reads come before this delete removes anything, so while they may wait for another delete to commit they hold nothing that delete could wait on.

**Evidence** (real SQL Server, both isolation configurations; `IntegrationRowLockSqlServerTests.CrossIntegrationDeletes.cs`, 6 cases):

| Case | Shows |
|---|---|
| `Control_the_removed_recount_waits_on_another_integrations_removed_rows_only_under_locking_read_committed` (2) | The statement the delete used to run waits on another integration's removed rows under locking READ COMMITTED (lock timeout 1222) and does not under snapshot isolation, so the next two cases are able to fail |
| `A_delete_finishes_while_another_integrations_removed_override_rows_are_held_locked` (2) | The real delete finishes, with one attempt, while another session holds a different integration's removed override rows. The holder takes no audit row, so a wait on a settings row is the only wait the delete could have; the server's own wait list is polled, not a delay |
| `Two_deletes_of_different_integrations_with_historical_overrides_commit_and_none_deadlocks_on_the_settings_table` (2) | Two real deletes, each with three historical overrides (two with no definition), both classified, both having removed their rows and holding them, then both released: both commit, no override survives, one primary row each, a bystander integration's overrides untouched, and no deadlock victim was running a statement that reads the settings table |

**Red before green.** With the previous service code restored (the test project unchanged), the isolated case failed on the locking catalog ("removing an integration's overrides must not read another integration's rows") and the pair case failed there too: the deadlock victim's statement was exactly `SELECT COUNT(*) FROM [PlatformSettingValues] … WHERE [ScopeKind] = N'Integration' AND [ScopeId] = @integrationId`, with one delete on a second attempt. Both pass on the snapshot catalog under either code, as they should (readers do not wait on writers). With the fix, all six pass. The pair case reports its victims and attempts in its output.

**What this does not show.** It does not show that audited units cannot deadlock. The staged audit row is saved by the same statement batch that removes the overrides, so in the pair case the two deletes have each inserted their audit row when released, and the recorder's own count of an operation's rows (§7, AUD-1.1) is unindexed. On the locking catalog that count produced the deadlock victim in every validation run, four of four (`SELECT COUNT(*) FROM [AuditLogs] … WHERE [OperationEventId] = @operationEventId`, attempts 1 and 2, both deletes committed). The case therefore allows a retry whose failed statement is on the audit table and fails only on one that reads the settings table; it does not assert zero retries. Eliminating L-1 is not evidence about the AUD-1.1 mechanism.

### 13.3 L-2: the refusal's diagnostics and remediation guidance

The refusal is correct and unchanged: HTTP 409 with `code: integration_setting_credential_undetermined`, the whole delete refused atomically, nothing changed and nothing recorded. The typed refusal carries the affected setting keys and reasons; the public response and the ordinary logs do not, and **no restricted diagnostic mechanism was added or used**. That was optional and none exists that is small and safe: a key is operator-visible configuration, but the refusal exists because the key's classification is in doubt, and a response that lists keys is a step towards describing the classification the contract says it must not.

The correction is to P-4a-2 (§11.6, marked there) and to the contract. Whether an operator can clear the refusal depends on the key:

| The refused override's key | `DELETE /api/integrations/{id}/settings/{key}` | So |
|---|---|---|
| Still defined and applicable at integration scope | Removes the override (and disables the secret named for that key without looking for other references) | A self-service remedy, with that caveat |
| No definition any more | 404: the reset finds no definition | **No self-service remedy** |
| Defined, but no longer applicable at integration scope | 400: the reset's scope check rejects it | **No self-service remedy** |

For the last two the integration cannot be deleted through the API until someone with database access has investigated the stored rows under the owner's procedures. That is a limitation of owner ruling H1 (F: avoid elaborate remediation infrastructure until real-world evidence establishes a need), not a defect to build around here: no repair API, no bypass, no operator-visible classification. If real deployments meet it, that is the evidence H1 F asks for before any remediation is designed; the keys stay on the typed refusal for a future restricted channel.

### 13.4 The earlier WP1 and WP2 LOW findings

The work package 5 brief carried the findings only as "L1: test exception assertion" and "L2: documentation clarification". The original review text was recovered afterwards by the architectural planner and is applied below. Both findings are **resolved**.

- **L1 (`ScheduleDeleteAuditSqlServerTests.cs:168`): resolved and verified.** Original finding: the rollback test accepts any `Exception` and would still pass if an audit insert failure surfaced as `ScheduleNotFoundException`; recommendation: add a negative assertion excluding `ScheduleNotFoundException` and the rejected-delete exception. Work package 5 had replaced the broad assertion (applied then as a best-evidence candidate, since the finding was not yet recovered). The current assertion is `ThrowAsync<DbUpdateException>()` with an inner exception of exactly type `InvalidOperationException` whose message starts with `Injected:`. That meets the recommendation and exceeds it: `ScheduleNotFoundException` and `ScheduleDeleteRejectedException` derive from `InvalidOperationException`, not `DbUpdateException`, so neither can satisfy the outer assertion, and nothing but the injected audit-insert failure can satisfy the inner one. A negative assertion would add nothing, so the test was not changed again. It passed in the work package 5 validation (§13.7, 12 of 12 in `ScheduleDeleteAuditSqlServerTests`).
- **L2 (`AuditEvents.Schedules.cs:57`, §10.1): resolved through documentation.** Original finding: §10.1 describes the W1 resource-context id omission without saying it cannot happen with real data; recommendation: note in the closing record that it is unreachable, no code change. §10.1 now says so. Verified against the current model: `ScheduleDefinitions.ResourceContextId` is required with a maximum length of 64 and a restricted foreign key to `ResourceContexts.Id` (also 64), and the audit column the value is copied to, `AuditLogs.ResourceContextId`, is 64. A persisted schedule's context id therefore always fits, and the omission in `BoundedId` is defensive code that real data does not reach. The same holds for `integration.deleted` (`Integrations.ResourceContextId` is required, 64, with the same foreign key). Reachable behaviour is unchanged: the producer still omits an id over 64 characters rather than failing the row, and a unit test pins that defensive behaviour. No application code changed.

`docs/04-api/contracts.md` also now states the schedule delete's 500 `outcome_unknown` outcome (§13.5); that is a separate correction and is not claimed to be either finding. The accepted schedule delete implementation was not reopened.

### 13.5 Contracts, interface documentation and catalog metadata

- **`docs/04-api/contracts.md`**: a new "Integration delete" section and table row for `DELETE /api/integrations/{id}`, written from the controller and the host's exception handler: the permission (`integration.delete`, checked before the unit and again under the row lock), the outcomes (204 including a verified lost acknowledgement; 403; 404; 400 for the five blocker families; the two 409s, one with `code: integration_setting_credential_undetermined`; 500 with `outcome_unknown` or `operation_not_completed` and the event id, or any other failure), the messages and ways to clear the five blockers, the settings and secret rules with their limits, the audit rows, and what changed from the earlier endpoint (a missing integration was 400; every `InvalidOperationException` was 400; the settings were reset one transaction at a time and the audit record was a text line appended afterwards). It says plainly that a repeated `DELETE` is a new request, not a replay: after a 204 it is a 404. The schedule delete row gained its 500 `outcome_unknown` outcome.
- **Related records**: dated supersession notes on the CTX-3B inventory's settings cascade ("not atomic, and deliberately so", `ResetAsync` per key) and the LFC-1 gate §2.4 (the cascade and the after-commit audit line), pointing here and to the contract. The AUD-1.0 gate needed no change: both deletes now have the three outcomes of its §13.6 (an audit failure rolls the delete back; a lost acknowledgement is verified and reported as success though the target is gone; otherwise `outcome_unknown`), and §27 already records that the write mode of CTX-3B's delete is superseded.
- **`reauthorize` on the four row-locked writers' interfaces** (`IIntegrationSettingsService.SetAsync` and `ResetAsync`, `INotificationDefinitionStore.CreateUserDefinitionAsync`, `INotificationSubscriptionStore.AddAsync`, `IAttentionRoutingManagementService.UpsertIntegrationRoutingAsync`): the parameter documentation now says when the delegate is required (whenever the caller's authorization depends on the integration or its owner: it must repeat that decision against the integration as locked), when null is legitimate (a platform- or module-scoped definition's subscription, whose authorization is instance-scoped, or a caller that made no integration-dependent decision), and that a new integration-sensitive caller must not omit it to skip the revalidation. No signature, default or behaviour changed, and no framework was added. Every current production caller passes it where required, as the review confirmed.
- **Catalog**: `integration.settings.secret.retired` now names its producers: `IntegrationSettingsService.cs` (reset, clear) and `IntegrationSettingSecretRetirement.cs` (integration delete). `Writer` is free text, not consumed by any test.
- **`IIntegrationSettingResetInTransaction`**: kept. It has no production caller; the slice 2 tests still pin its transactional behaviour, and removing it would touch the settings service and its fixtures. Its documentation says that it is unused, why it must not be used for historical-override cleanup, and that a new caller must not use it for a setting whose secret an H1 plan governs. It was not reintroduced into the delete.

### 13.6 Accepted limitations carried forward

These are the owner's approved limits, not defects, and none was changed or weakened:

- Connection references can race the secret inventory scan: connection writers take no integration lock (§12.8).
- The known reference inventory covers three database locations (`PlatformSettingValues` outside the integration, `Connections.SecretRefsJson`, `EntraInstanceRegistrations.ClientSecretRef`) and promises neither serializable nor globally complete evidence (§11.5, §11.9).
- A format `X` GUID embedded in malformed text is not extracted (§11.5).
- Secret provenance rests on the provider and the naming convention, not an immutable owner id (§11.6, §11.9).
- No per-key reset audit row is written when an integration is deleted; the count on `integration.deleted` is the evidence (P-4b-1).
- No W3 audit event is written for a blocked or H1-refused integration delete (§12.10).
- H1's atomic refusal stands, with the consequence in §13.3.

### 13.7 Tests and validation

Real SQL Server (`apperture-test-sql`), `APPERTURE_REQUIRE_SQL_TESTS=1`, the connection read from `.env` into the test process only (never on a command line), `--artifacts-path artifacts/aud14w5 -m:1`.

| Command | Result |
|---|---|
| `dotnet build Apperture.slnx` | succeeded, 0 errors (4 existing warnings) |
| `git diff --check` | clean |
| `dotnet test tests/Apperture.IntegrationTests` filtered to `IntegrationRowLockSqlServerTests`, `IntegrationSettingSecretDispositionSqlServerTests`, `ScheduleDeleteAuditSqlServerTests` and `ScheduleDeleteSqlServerTests` (both isolation configurations throughout) | **253 / 253 passed**, none skipped: 116 row-lock (110 earlier, 6 new), 117 H1 and delete-path, 12 schedule audit (including the tightened assertion), 8 schedule delete |
| The 6 new cases, repeated three more times (`--no-build`) | 6 / 6 each time. On the locking catalog the pair case reported one deadlock victim every time, on the audit recorder's count and never on the settings table |
| `dotnet test tests/Apperture.UnitTests` filtered to the audit catalog and schemas, the integration delete and its controller, settings audit, the shared-context pins, the management client delete, the schedule delete, definition create lock, disposition, reference reader, attention routing and subscription tests | **475 / 475 passed** |
| `dotnet test tests/Apperture.ArchTests` | **38 / 38 passed** |

**Mutation evidence** (the change reverted temporarily, restored byte for byte and rebuilt, never committed): with the previous `RemoveOverridesAsync` and recount in place, the isolated case and the pair case failed on the locking catalog as described in §13.2, and passed on the snapshot catalog; with the fix, all six pass. Failure to reach SQL Server fails these suites rather than skipping them (`AuditSqlScratch`), and each run created and dropped its own throwaway catalogs.

**Not run, with the reason.** The full `Apperture.UnitTests` and `Apperture.IntegrationTests` projects and the broader audit namespace sweep: the changes are one service method, documentation comments, one free-text catalog string and test code, and the focused runs cover every consumer of the changed method, catalog entry and interfaces; the PR's CI runs all of it. The 227-case suite of the independent review was not repeated beyond the 117 and 110 (now 116) cases that are part of the run above. UI and browser suites: no UI, route or response changed. No shared audited-unit infrastructure was touched, so no broader validation boundary applied.

### 13.8 Slice 3 work packages and the proposed verdict

*The verdict below is the proposal made before the owner's decision. It is kept as written. Both conditions it lists were then met, and slice 3 was closed (§14).*

| Work package | Status |
|---|---|
| 1 `schedule.deleted` and `schedule.delete_rejected` contracts | Done; independently reviewed and accepted |
| 2 Schedule hard-delete as one audited unit | Done; independently reviewed and accepted |
| 3 Integration row lock in the writers | Done; combined review READY TO ACCEPT |
| 4a H1 mechanisms (`integration.deleted`, inventory, classifier, retirement) | Done; combined review READY TO ACCEPT |
| 4b Integration hard-delete as one audited unit | Done; combined review READY TO ACCEPT |
| 5 Remediation, contracts, record reconciliation | Done (this section); awaiting the owner's review |

**Proposed verdict: Slice 3 is READY TO CLOSE, pending the owner's acceptance of work package 5 and CI on the pull request.** Both deletes are one audited unit with verified commit (M6) and the lost-acknowledgement-then-missing-target case; the integration delete takes the integration row lock first (D-4) and removes every historical override under owner ruling H1, refusing atomically where a credential's disposition cannot be established; an independent review found no critical, high or medium defect; its two LOW findings are dispositioned (§13.2, §13.3); both deletes are specified in `docs/04-api/contracts.md`; and the focused validation in §13.7 passed. This is a proposal for the owner. It is not an acceptance of work package 5 and not approval to merge.

**Remaining before the owner can accept:**

1. The owner's review of work package 5: the removed recount (§13.2), the corrected remediation text (§13.3), the contract and the new tests.
2. CI on the pull request, which runs the full unit and integration projects that were not run here (§13.7).

The WP1 and WP2 findings L1 and L2 are resolved (§13.4); nothing from the earlier reviews is outstanding. Every LOW finding of the combined review (§13.1) and of the WP1 and WP2 review is resolved or explicitly accepted: L-1 fixed, L-2 corrected in documentation with H1's refusal accepted as designed, the WP1 and WP2 findings resolved, and the audited-unit deadlock observation tracked as a deferred item (§7).

**Not part of this verdict.** The audited-unit deadlock mechanism (§7) is tracked, not fixed, and its placement is the owner's decision. AUD-1.4 as a whole is **not complete**: slices 4 to 8 have not started. RC1 requirement 5 is met only after AUD-1.6 has merged and passed acceptance (design gate §14), and nothing here changes that.

## 14. Slice 3 closeout

Date: 2026-10-11. Written after PR #18 merged. It records the owner's decision and the merge evidence and changes no code, ruling, contract or limitation.

### 14.1 Owner acceptance

The owner accepted work package 5 and approved and merged PR #18 (GitHub records the merge by `tennysonhull` at 2026-10-11 17:27:37 UTC). That settles the first of the two conditions in §13.8. Work package 5 itself was not independently re-reviewed (§13); the independent review verdicts stand as recorded in §13.1 and §13.4.

### 14.2 The merge

| | |
|---|---|
| Pull request | #18, `aud-1.4-configuration-lifecycle` into `master` |
| Merge commit | `54ae11781b4ec5b099e270920b9c3d05e7589f84`, parents `0314419` (`master`) and `539d907` (the branch) |
| Reviewed head | `a8d6b9b`, the head the independent reviews and work package 5 closed on |
| Branch update | `539d907`, a normal merge of `origin/master` (`0314419`) into the branch, made before the merge. `master` had advanced through the licensing and contributor governance merge (PR #19, `d07577a`) and the third-party notice merge (PR #20, `0314419`). No file overlapped, nothing was resolved, no file under `src/` or `tests/` changed, and the 109-file AUD-1.4 change set against `master` was identical before and after |
| Result | The merge commit's tree (`4e5507b4ad14`) equals the tree of `539d907`: `master` holds exactly the reviewed implementation plus the governance and notice files |

### 14.3 CI

The first run on the pull request (run 37911882582, head `a8d6b9b`) never executed: GitHub did not start either job because of an account billing restriction, so it is not a test result and is not counted. After the restriction cleared:

| Run | Commit | Result | Jobs |
|---|---|---|---|
| 38146637969 (pull request) | `539d907` | success | `build-and-test` and `browser-security` both executed and passed |
| 38159760341 (post-merge push to `master`) | `54ae117` | **success** | `build-and-test`: 13 of 13 steps, 16 minutes. `browser-security`: 15 of 16 steps (the failure-only artifact upload is skipped by design), 18 minutes |

Both runs report the same totals: **7,332 tests passed, 0 failed, 67 skipped**. The skips are the opt-in browser suite that `build-and-test` does not enable (62, by design and pinned by a CI guard test) and the five browser tests that need `APPERTURE_UI_EVIDENCE` (5). The full unit and integration projects that §13.7 recorded as not run locally ran in these jobs. That settles the second condition in §13.8.

### 14.4 State of the slices

| Slice | State |
|---|---|
| 0 to 2 | Done (§3) |
| 3 | **CLOSED.** Work package 5 accepted, merged, post-merge CI passed |
| 4 to 8 | **Not started.** On `master` the only audited operation in `IntegrationManagementService` and `SqlScheduleManagementService` is each delete; the catalog holds no integration, binding or schedule lifecycle action beyond `integration.deleted` and `schedule.deleted` and `schedule.delete_rejected`; the connection, resource-context and attention-routing services have no audited operation; no AUD-1.4 commit follows the merge |

### 14.5 Findings carried out of slice 3

- **`AuditLogs.OperationEventId` verification-count deadlock (§7):** assigned to proposed AUD-1.6. The design gate's AUD-1.6 entry is not amended here, so AUD-1.6 planning has to take it up from this record.
- **Attention-routing write routes authorized by integration read only (§7):** provisionally assigned to slice 7, pending security triage. Triage may move it to IAM-10 or to an earlier owner-directed fix. *Update 2026-10-11: triaged, and the authorization defect fixed ahead of slice 7 as a separate change (§7); only the audit attribution stays with slice 7.*
- The other §7 findings keep their owners. The accepted limitations in §13.6 are carried forward unchanged.

AUD-1.4 as a whole is **not complete**. RC1 requirement 5 is met only after AUD-1.6 has merged and passed acceptance (design gate §14), and nothing here changes that.




## 15. Slice 4: integration, binding and schedule lifecycle

Date: 2026-10-12. Implemented on branch `aud-1.4-slice-4-lifecycle` in four work packages and a UI follow-up, from the orientation report and the owner and planner rulings that followed it. Nothing here claims AUD-1.4 complete: slices 5 to 8 have not started.

### 15.1 Operations converted

Every writer below is one audited unit (operation lock first; reads, mutations and staging inside; the ownership gate, where taken, after the operation lock and before any row). No-op requests declare themselves and record nothing. The actor is the unit's trusted principal.

| Operation | Service method | Primary row (W1, Configuration, standard, basis change) | Locks inside the attempt, in order |
|---|---|---|---|
| Integration create | `IntegrationManagementService.CreateIntegrationAsync` | `integration.created` | gate, context resolve, insert |
| Integration update, activate, deactivate, retire | `UpdateIntegrationAsync` and its three wrappers | `integration.updated`, or `integration.activated` / `deactivated` / `retired` when the state changes (alternate primary actions declared on the operation) | gate when the request names Active or Inactive, integration row, `integration.update` again on the locked row |
| Binding create | `BindingService.CreateBindingAsync` | `binding.created` | gate, integration row, authorization, connection row |
| Contextless capability binding | `CreateCapabilityBindingAsync` | `binding.created` (no integration or context) | none |
| Binding delete | `DeleteBindingAsync` (both routes) | `binding.deleted` | integration row (when owned), binding row (new `GetByIdForUpdateAsync`); no gate |
| Schedule create with its assignment | `SqlScheduleManagementService.CreateScheduleWithAssignmentAsync` (new; both create routes); `CreateScheduleAsync` stays for no-assignment callers | `schedule.created` (`assignmentsCreated`) | gate and integration row when attached |
| Schedule update, patch, enable, disable | `UpdateScheduleAsync`, `PatchScheduleAsync` (new; the `PATCH` route, 15.6), `EnableAsync`, `DisableAsync` | `schedule.updated`, `schedule.enabled`, `schedule.disabled` | gate when the request may enable, schedule row |
| Standalone assignment | `AssignToRuntimePlaneAsync` | `schedule.assigned` (twelfth action, added for this existing writer) | schedule row |

Action fields name entities and changed field names only. Recorded: names (snapshots), module, slot, integration, connection or capability ids (entity ids, not secret references), enabled flag, runtime plane id, counts, state names, and for updates the names of the changed fields. Never recorded: integration settings keys or values, schedule recurrence, time zone, target configuration, execution policy reference, policy overrides, or anything a connection holds. Catalog entries, schemas and typed producers: `AuditActionNames`, `AuditActionCatalogEntries`, `AuditEvents.Integrations/Bindings/Schedules`. IntroducedIn is `AUD-1.4 slice 4`.

### 15.2 Decisions and corrections

| # | Decision |
|---|---|
| S4-1 | **`binding` joins the closed target vocabulary** (gate section 11.1 amended). **Correction to the orientation report:** the report said the vocabulary lived in code and documents only. It is also spelled out in the `AuditLogs` classification check constraint (`CK_AuditLogs_ClassificationSet`), so the amendment needs a migration. `Aud14_BindingTargetType` replaces the constraint with the AUD-1.1 text plus one named substitution (pinned by a test); every stored row still satisfies it, and `Down` restores the old constraint and fails while a `binding` row exists, since the table is append-only. This is the one schema change of the slice; no table, column or index changed, and no new unique index was added (none exists on bindings, assignments or schedules, so there is no named-index race to handle: concurrency is by the lock protocol). |
| S4-2 | The gate decision comes from the request, not from the stored state, because the state is unknown before the row is locked and taking the gate after the row would invert the order every ownership writer uses. Integration update takes the gate when the request names Active or Inactive; a direct schedule update takes it when the proposed flag is Enabled, a schedule `PATCH` only when its body carries `Enabled: true`, and enable always does. |
| S4-3 | A state change and other changes in one request record one row under the transition's action with the other changed field names. |
| S4-4 | An integration update merges over the locked row from the request's own optional fields (removes the stale-snapshot lost update). A schedule update applies only the fields a PATCH can change over the locked schedule; target, execution policy and ownership are never rewritten. The `PATCH` route calls `PatchScheduleAsync`, which applies only the fields the body carries to the row it has locked, so an omitted field is never restored from an earlier read (finding M1, fixed on owner decision; see 15.6). `UpdateScheduleAsync` keeps its semantics for direct callers: the four values its definition carries are applied. |
| S4-5 | `AssignToRuntimePlaneAsync` is audited rather than left as a reachable unaudited writer; production reaches assignment only through the atomic create. `CreateCapabilityBindingAsync` has no route and no caller and is audited. |
| S4-6 | Binding delete keeps its HTTP behavior: a missing binding is the same quiet return, and a binding another request deleted first is a no-op that records nothing. |
| S4-7 | The attention-routing editor and Clear button follow `integration.update` (the server requirement from the earlier security fix) and no longer `schedule.manage`; viewing the effective routing is unchanged. |

### 15.3 Concurrency and ambiguous commits

- Integration row: all integration writers, binding create and delete, and attached-schedule create take it; the integration delete takes it first and counts its blockers under it, so a binding or attached schedule that commits first blocks the delete and one that loses waits and refuses (the Slice 3 evidence gap for these two writers is closed by deterministic tests in both orders).
- Schedule row: update, enable, disable and assignment take the row a delete also takes; a change that waits for a delete finds the schedule gone (`ScheduleNotFoundException`) and records nothing.
- Nothing is read before a schedule update, patch, enable or disable unit opens, so a request that must wait for the gate waits there first. The `PATCH` route no longer reads the schedule before the call.
- Ambiguous commits use the existing verification: confirmed rollback reruns once; a lost acknowledgement is reconciled from the committed primary row (create and update read the entity back by the primary row's target; a delete is a success from the row alone); an unknown outcome or an operation lock not granted is `outcome_unknown`; a failed read-back after a verified commit is `committed_result_unavailable`. The routes exclude `AuditInvariantException` from their `InvalidOperationException` mapping.
- The AUD-1.6 deadlock in the recorder's own verification count (section 7) is unchanged and not fixed here. The pair races in this slice are across different locks and one integration at a time; no new deadlock was observed.

### 15.4 Intentional behavior changes

| Change | Why |
|---|---|
| A request that changes nothing writes nothing: `UpdatedAt`, lifecycle timestamps and the row version no longer move | "A request that changes nothing records nothing" (section 5) |
| Schedule create and its initial assignment commit together | A failure between them left a schedule that was never dispatched |
| A schedule update no longer rewrites target or execution policy fields a direct caller of the service supplied | Those fields are immutable by the HTTP contract and were not audited |
| Enabling an already enabled schedule in an archived context no longer refuses (it is a no-op) | Nothing becomes live |
| A schedule `PATCH` applies only the fields its body carries, over the locked row; an omitted field keeps the stored value instead of being reapplied from a read made before the call | Finding M1: a rename overlapping a disable re-enabled the schedule (15.6) |
| The schedule `PATCH` response is the committed schedule, not the body merged over the earlier read | It could report a state that was never stored |
| A schedule deleted while a `PATCH` waits answers 404 (was 400); a `PATCH` that does not carry `Enabled: true` no longer takes the ownership gate, even for an enabled schedule | The pre-read is gone, so not-found comes from the locked read; only an explicit enable can make anything live |
| The two integration-schedule and integration-binding create routes, the five integration routes and the four schedule routes no longer turn an audit invariant into 400 or 404 | AUD-1.3 D19 |

### 15.5 Tests

New: `Slice4LifecycleAuditEventTests`, `IntegrationLifecycleAuditTests`, `BindingLifecycleAuditTests`, `ScheduleLifecycleAuditTests`, `BindingServiceSharesTheUnitContextTests` (unit); `IntegrationLifecycleAuditSqlServerTests`, `BindingLifecycleAuditSqlServerTests`, `ScheduleLifecycleAuditSqlServerTests` (SQL Server: M7 per operation, atomicity, the M6 matrix through the shared `AuditedOperationM6Runner`, concurrency); the integration-row-lock suite gains integration update, binding create and attached-schedule create against the integration delete in both orders on locking and snapshot-isolation catalogs; the UI source tests follow the new permission. The ownership concurrency fixture runs its set-up through a transaction-only unit so the gate races stay about ownership.
### 15.6 Independent review, findings and limits

One combined independent review (Opus 5.5, high) of the integrated slice found no critical or high finding, and one medium.

| Finding | Disposition |
|---|---|
| M1 (medium): the schedule PATCH route reapplies all four patchable fields from a read made before the call, so an overlapping PATCH can re-enable a schedule an operator just disabled (or lose a concurrent rename), and the audit row then names that write. Not a regression: the old code overwrote the same way, now with a row | **Fixed** (owner decision after the handback: fix before merging, within slice 4). The `PATCH` route now calls `PatchScheduleAsync`, which applies only the fields the body carries to the locked row; see 15.7 |
| L1: the ownership gate is taken more often (any integration update naming Active or Inactive, any PATCH of an enabled schedule), and it is one instance-wide lock with a 15 s timeout whose busy exception is not mapped by these routes | Accepted trade-off of S4-2 (correctness first). Narrowed for schedules by the M1 fix: a `PATCH` takes the gate only when its body carries `Enabled: true`. A refinement exists: lock the row without the gate and rerun with the gate only when the change really enables or reactivates |
| L2: the contract omitted that enabling an enabled schedule in an archived context is now 200; the list of changed routes omitted two create routes | Fixed (contract and 15.4) |
| L3: the two binding race tests did not force their interleaving | Fixed: both now hold the row with `LockedRowHolder`, wait on the server's lock view, then release |
| L4: the ownership races run set-up through a transaction-only unit, so no race of the real audited unit against a move or archive is exercised | Accepted for this slice: the real unit is raced against the integration delete and the settings writers; the gate-first order is pinned by the delete races and the ownership fixture. A real-unit race against a move belongs with slice 5, which converts the mover |
| I1: `CreateScheduleWithAssignmentAsync` is a default interface method (composes two calls) kept so test doubles compile; production overrides it | Noted; a future decorator must override it |
| I2: the migration adds the constraint WITH CHECK, which scans `AuditLogs` under a schema lock | Noted: budget for it on large instances |

The AUD-1.6 deadlock in the recorder's own verification count (section 7) is untouched and was not seen as a new defect.

### 15.7 Remediation of M1: the schedule `PATCH` applies only what it carries

Owner decision (2026-10-12, after the slice 4 handback): fix M1 before PR #23 merges, as one bounded commit on this branch, keeping the `PATCH` contract (four optional fields; omitted or null is no change; a value replaces).

**Shape.** `IScheduleManagementService.PatchScheduleAsync(ScheduleId, SchedulePatch)` returns the committed `ScheduleDefinition`. `SchedulePatch` carries the four optional values and `ApplyTo(current)`. The production method runs the same `ChangeAsync` unit as update, enable and disable, with `patch.ApplyTo` as the proposal, so the patch is applied to the row read under `UPDLOCK, ROWLOCK` and a field the patch leaves out takes the locked row's value: it is neither compared nor written. `ChangeAsync` now returns the definition it wrote (the locked row for a no-op); after a verified commit whose acknowledgement was lost, the patch reads the latest committed schedule back without a lock, so a later writer's change may show (as with the integration and settings read-backs, §9.4; a failed read-back is `committed_result_unavailable`), while update, enable and disable keep returning nothing and read nothing back. The interface default (read, apply, `UpdateScheduleAsync`) exists so test doubles compile; it is not atomic and production overrides it, as with `CreateScheduleWithAssignmentAsync` (I1).

**Order and gate.** Unchanged order: the operation lock, then the ownership gate, then the schedule row. The gate is decided from the patch alone, before any read: only `Enabled: true` takes it, since a patch without that value keeps the locked row's flag and cannot make anything live. The reactivation guard runs, as before, only when the locked row is disabled and the proposal is enabled, against the locked row's owning context.

**Evidence and no-ops.** Changed field names are computed against the locked row, so a value another request committed while the patch waited is not reported as changed, and a patch that matches the locked row declares a no-op and records nothing. Action selection, the lifecycle merge and the M6 behavior are those of `ChangeAsync`, unchanged.

**Route.** `SchedulesController.Update` reads nothing before the call, maps `ScheduleNotFoundException` to 404 (before its `InvalidOperationException` mapping, which still excludes audit invariants), keeps 409 for an archived owning context, and answers with the committed schedule.

**The management UI.** The schedule edit form (`ScheduleDetail.razor`) loaded all four values when it opened and always sent them back, so a rename saved after another operator's disable carried `Enabled: true` and the server, rightly, treated it as an explicit enable: the M1 race survived for UI users. The form now sends only the fields the operator changed against the schedule it loaded and `null` for the rest. No contract change; the client request type already allowed nulls.

**Direct callers.** `UpdateScheduleAsync` is unchanged: a direct caller's definition still supplies all four values (pinned by `A_direct_update_still_applies_the_enabled_flag_its_definition_carries`).

**Tests.**

| Suite | What it proves |
|---|---|
| `SchedulePatchSqlServerTests` (new, SQL Server, 20: 10 cases on a locking READ COMMITTED catalog and on a READ_COMMITTED_SNAPSHOT catalog) | A rename, and a recurrence and time-zone change, each shown by the server's lock view to be waiting on the schedule row while another session disables it and commits, leave the schedule disabled, return the committed schedule and record only their own fields; a rename and the production disable queued on the row keep both, whichever the server grants first (the outcome is the same for both orders; which order ran is not controlled); an explicit enable waiting while a rename commits enables, keeps that rename and records no changed field; an explicit enable under an archived context is refused and changes nothing, while the same rename without it is allowed; an enable and a disable queued on the row leave the flag of whichever audit row is last; two patches of different fields queued on the row keep both; two patches of the same field leave the value of the last audit row; a patch whose value was committed while it waited is a no-op with no row; the recorded changed fields are only those that differ from the locked row |
| `ScheduleLifecycleAuditSqlServerTests` (extended) | `PatchRename` and `PatchEnable` join the M7 and M6 matrix (audit insert failure, lost acknowledgement verified and read back, confirmed rollback rerun, unknown outcome committed and not committed); a rename waiting for a delete finds the schedule gone and records nothing |
| `SchedulePatchTests` (new, unit, 17) | The semantics above in the in-memory harness, the gate taken only for `Enabled: true` whatever the stored flag, no-op and empty patches, the lost-acknowledgement read-back, the typed not-found, the direct `UpdateScheduleAsync` caller, `ApplyTo` |
| `SchedulesControllerPatchTests` (new, unit, 4) | The body becomes a patch with omitted fields left out and nothing read first; the response is the committed schedule; not found is 404; an audit invariant is not mapped to a refusal |
| `SchedulePatchUiSourceTests` (new, UI source, 1) | The edit form sends `null` for each field the operator did not change, the enabled flag included |
| `ScheduleGateOrderingTests`, `SchedulesControllerReactivationGuardTests` (changed) | An enabling patch takes the gate before the reactivation read; the 409 and 400 mappings now go through `PatchScheduleAsync` |

`LockedRowHolder.WaitUntilBlockedAsync` now counts requests waiting on the exact key the holder was granted rather than requests whose `blocking_session_id` is the holder: a second request queued on the same key is reported as blocked by the first waiter, so the old query never saw two waiters (no earlier test asked for two). The binding and schedule suites that use it pass with the change.

**Validation run.** Focused unit tests (the new suites and every existing schedule controller, service, gate, guard and audit suite): 111 of 111. UI source tests touching the schedule pages: 69 of 69, and the UI client builds. SQL Server: `SchedulePatchSqlServerTests` (20), `ScheduleLifecycleAuditSqlServerTests` and `BindingLifecycleAuditSqlServerTests`, all passing on the final service code (the first run passed everything except the 8 two-waiter cases, which failed on the `LockedRowHolder` count described above and passed, with the binding and delete-wait cases, 55 of 55, once it was corrected; the later review fixes changed only comments and the UI). The suites fail rather than skip without SQL Server. No mutation run was made for this remediation: the race cases are built so the competing change commits after the patch is already waiting, which is the interleaving the old route got wrong, but their failure under the old route was not demonstrated.

**Review.** A short targeted review (Opus 5.5, high) of this remediation only (lock order, gate, concurrent enable and disable, audit evidence, API compatibility, test strength): **approve with nits**, no critical or high finding.

| Finding | Disposition |
|---|---|
| R1 (medium, not a regression): the management UI's edit form always sent all four loaded values, so the M1 race remained for UI users | Fixed: the form sends only changed fields (above), pinned by `SchedulePatchUiSourceTests` |
| R2 (low): the ownership gate and reactivation guard documentation still named `UpdateScheduleAsync` as the `PATCH` path | Fixed: both name `PatchScheduleAsync` and its `Enabled = true` condition |
| R3 (nit): the lost-acknowledgement read-back is unlocked and may show a later writer's change | Accepted, as for the other read-backs; the remarks and this record say so |

The reviewer found lock order, the gate decision (no patch without `Enabled: true` can make a schedule live, and dropping the gate for such a patch does not weaken archive, since it changes neither the flag nor ownership and takes only the row lock), the guard, audit evidence and the status mapping sound, and the `LockedRowHolder` change correct for its existing callers. One reviewer note does not hold: a missing schedule with an invalid body answered 400 before this change too, because validation already ran before the removed read.

### 15.8 Closure

Slice 4 was accepted by the owner and merged to `master` with PR #23 as `b38f1fd787f8eb464e3713e289002b4ade7fde5e` (2026-10-12; the branch head was `7e1ca14`, the schedule `PATCH` remediation of §15.7). The CI run on the pull request head and the post-merge CI run on `master` both succeeded. Slice 4 is closed. Its carried-forward items go to slice 5: the real-unit race of the audited unit against an ownership move or an Application archive (finding L4, §15.6) and the connection writers' integration-row-lock limit (§12.8).

## 16. Slice 5: connections, resource contexts and ownership moves

Date: 2026-10-12. Branch `aud-1.4-slice-5-connections-contexts`, from `origin/master` at `b38f1fd`. Authorized by the owner after the slice 4 merge. Nothing here claims AUD-1.4 complete: slices 6 to 8 have not started.

### 16.1 Orientation findings (source-backed, before any change)

| Area | State on `master` |
|---|---|
| Connection writers | No writer uses an audited unit. `ConnectionManagementService` appends legacy audit text after its saves; create alone has a hand-rolled gated transaction. The six `connection.*` catalog entries are legacy-shaped (no detail schema, no typed producer, names kept in `ConnectionAuditActionNames`) |
| Create | Operator-supplied secret values are stored and activated **before** any transaction (`ConnectionSecretMaterialProvisioner.ProvisionForCreateAsync`), so a failed or rolled-back create orphans live secrets |
| Update and rotation | There is no rotate route: rotation is `SecretValues` (or a changed secret reference) on `PATCH`. `ApplySecretValuesForUpdateAsync` runs first, outside any transaction, adding and **activating** a version on the secret id the connection already names, so bound integrations resolve the new credential before validation or the row save (**the live-secret-first defect**). A failure afterwards leaves the new version active and appends a best-effort `failed` row. The row save writes the whole row from an unlocked read (no gate, no row lock, no version check) |
| Retire | Whole-row write from an unlocked read, then a legacy row after the save; retiring a retired connection writes another row |
| Verify | Reads the connection, calls the connector's verifier (a remote probe), writes the whole row with the three verification columns from the stale snapshot, then appends a legacy row. A concurrent edit during the probe is overwritten |
| Structure browse | Authorizes (read **and** browse) after an unlocked load, calls the remote browser, then appends the legacy row with `CancellationToken.None`; a failed append withholds the result (the intended "no audit, no data"). Failed and cancelled attempts are best-effort rows |
| Resource contexts | Create, update and unarchive have no transaction, gate or lock; each commits and then appends a legacy row. Update is up to three independent committed saves with one row in a `finally`, so a partial update is durable and audited. Archive is already one gated transaction, with a legacy row. No context row lock exists. `UX_ResourceContexts_Slug` loses a create race as an unmapped 500 |
| Ownership moves | Three hand-rolled gated transactions (gate, locked row, validation, narrow `SetResourceContextIdAsync`), legacy `moved_out` and `moved_in` rows and, on a rejection, a legacy `move_rejected` row committed in the same transaction. Integration and connection moves authorize on the locked row (deliberately); the schedule move authorizes before the read (no existence oracle) |
| Controllers | Every mutation maps a bare `InvalidOperationException` to 400, 404 or 409, so an `AuditInvariantException` would be misreported; archive does not map `ResourceOwnershipBusyException` |
| Tests | The real unit has never been raced against a move or archive: `ResourceOwnershipMoveConcurrencySqlServerTests` runs its set-up through a transaction-only unit that stages nothing (finding L4) |

### 16.2 Decisions

| # | Decision |
|---|---|
| S5-1 | **Connection rotation joins the unit.** The secret store already joins the transaction open on its context (§4.1). Create and update now provision and rotate secrets **inside** the connection's unit, after the row lock and validation inputs are read, so the credential becomes live only if the unit commits. No compensating workflow is added. |
| S5-2 | **Update, rotate and retire lock the connection row without the ownership gate.** None of them can make a connection live or change ownership (retire only removes an archive blocker), so there is nothing the gate protects; a move holds the gate and then the same row lock, so an update waits behind a move and re-authorizes against the new owner. Authorization is evaluated on the locked row's governed identity. |
| S5-3 | **Connection verify is one external call and one unit.** The probe runs first, outside any transaction (a transaction never makes an external effect atomic, gate §11.6). One unit then locks the row, writes **only** the three verification columns (never a whole row) and stages `connection.verified` (basis `external_attempt`), which states only what the verifier answered. A verifier that throws is staged as a failed attempt and rethrown. |
| S5-4 | **Structure browse is W2.** The success row is written with the required-event writer before the result is delivered (a failure withholds the data); failed and cancelled attempts are best-effort rows of the same principal, as before. |
| S5-5 | **A rejected ownership move is recorded after its unit.** A unit cannot stage a refusal. The move declares itself a no-op, returns the preview, and a W3 `*.ownership.move_rejected` row of the same principal follows (the schedule delete's precedent, §10.2). The gate allows this ("may stay in its transaction", §13.1). |
| S5-6 | **A context update is one unit.** It was up to three committed saves with a row in a `finally`; it is now all or nothing, with one `resource_context.updated` row naming the fields actually changed. This is a correctness consequence of the atomic unit, called out in 16.4. |
| S5-7 | **A context row lock is added** (`GetByIdForUpdateAsync`), taken after the ownership gate by archive and alone (no gate) by update and unarchive. Neither can create a cycle: the gated paths only read the context row and update takes no gate. |

### 16.3 Work packages

| # | Package | Status |
|---|---|---|
| WP1 | Audit contracts: typed actions, producers and schemas for the connection, context and move families | **Done** (16.5) |
| WP2 | Connection create, update (rotation) and retire as audited units | **Done** (16.5) |
| WP3 | Connection verify and structure browse | **Done** (16.5) |
| WP4 | Resource context create, update, archive and unarchive | **Done** (16.6) |
| WP5 | Ownership moves (integration, connection, schedule) | **Done** (16.7) |
| WP6 | Real-unit concurrency coverage, validation, independent review, closure | **Done** (16.8 to 16.10) |

### 16.4 Intentional behavior changes

| Change | Why |
|---|---|
| A connection create, update, rotation and retire is one audited unit; the secret material the request supplies is stored inside it | The live-secret-first defect and the secrets orphaned by a failed create (16.5) |
| Retiring a retired connection, and an update that changes nothing, answer as before (204, 200) but write nothing and record nothing | "A request that changes nothing records nothing" (section 5). A second retirement used to write another row |
| A connection `PATCH` that supplies `SecretValues` no longer leaves a "failed" `connection.secret_rotated` row when it then fails | The rotation is rolled back with the request, so there is no failed rotation to record; the refusal is the response |
| A connection update locks the row and merges over it, instead of writing the whole row from a read made earlier | The whole-row overwrite lost a concurrent edit, retirement or verification (the lesson of finding M1) |
| Verification writes only the three verification columns, over the row as locked after the probe | A rename, rotation or retirement made while the remote host was being probed was overwritten, and a retired connection could be written back as Active |
| A structure browse that threw or was cancelled is the new attempt action `connection.structure_browse_failed` (W3); a browse that returned a result, a remote refusal included, is `connection.structure_browsed` (W2) | A required event cannot be withheld from a result that was never delivered |
| `connection.secret_rotated` records counts of the fields it changed; the keys are on `connection.updated` and on the per-field `connection.secret.stored` rows | The catalog rule that a Secrets-category event carries ids, closed values, flags and counts only |
| An Application `PATCH` is all or nothing: it used to commit a rename and then audit it even when a later re-slug was refused | S5-6. The three saves are one unit now, so a refused slug change fails the whole operation and leaves nothing changed |
| A create or re-slug that loses the race on `UX_ResourceContexts_Slug` is the 400 "slug already in use" | It was an unmapped 500 |
| Archiving while the ownership gate is busy is a 409 with `code: ownership_operation_busy` | It was an unmapped 500; every other ownership route already answers 409 |
| A blocked ownership move is recorded after its unit, as a best-effort attempt row, instead of inside the transaction | A unit cannot stage a refusal (S5-5). The answer to the caller is unchanged and the move still changes nothing |
| The ownership gate is no longer the first statement of a move or an archive: the unit's own operation lock (unique to the operation, so never contended) comes first | The unit's contract (gate section 13.4). It adds no edge to the lock order |

### 16.5 Connections (work packages 1 to 3)

**Contracts (WP1).** The six declared `connection.*` actions gain schemas and typed producers (`AuditEvents.Connections`); `connection.secret.stored` (Secrets, elevated, secondary row) and `connection.structure_browse_failed` (W3, basis `external_attempt`) are new. Nothing can carry a config value, a secret value or reference, a connection string, the verifier's summary or what the remote system returned. The legacy `ConnectionAuditActionNames` and detail records are deleted: nothing writes them, and old rows are classified from their own JSON by the catalog's legacy target rule.

**Create.** Gate, owning context, the operator-supplied secret material (the store joins the unit's transaction), the material check, then `connection.created` and one `connection.secret.stored` row per stored field, then the row. The identity is chosen before the unit. A refusal, a failed save or a failed audit insert rolls the secrets back with the connection (before: stored and live before any transaction, orphaned by any failure after).

**Update and rotation.** An early read and `connection.update` check answer a missing or forbidden request; inside the unit the row is locked and `connection.update` is evaluated again against it, so an ownership move that committed while the request waited is what the decision reads. The change is computed against the locked row; a request that changes nothing is a declared no-op. The material is added and activated through the store inside the unit, after the request is known to change something. The primary row is `connection.secret_rotated` when any secret field changed (material rotated, or a reference added, removed or pointed elsewhere) and `connection.updated` otherwise. No ownership gate (S5-2). The credential a connection resolves therefore changes only if the unit commits: the previous credential stays live after a refusal, a failed row save, a failed audit insert, a failed material check and an unknown outcome that was not committed.

**Retire.** Row lock, `connection.delete` again on the locked row, state change, `connection.retired`. Secrets and bindings are untouched, as before.

**Verify.** The probe is authorized by an early `connection.read` check and runs first, outside any transaction. One unit then records it: it locks the row, writes only the verification columns over the row as locked, and stages `connection.verified`, which states what the verifier answered (`succeeded` for a success, `failed` for a failure answer or a verifier that threw, `unknown` for an unavailable answer or for a probe the caller cancelled before it answered, because nothing was observed) and never the summary. The unit runs with no caller token, so a request cancelled during the probe leaves its evidence, and the verifier's own exception is rethrown unchanged. No second authorization is made inside the unit (the probe is done and cannot be undone). If the unit cannot commit, the attempt is unrecorded and the caller is told.

**Browse.** The success row (a result was delivered, including a remote refusal) is written with the required-event writer before the result is returned: a failed write withholds the data. A request that threw or was cancelled is a best-effort `connection.structure_browse_failed` row of the same principal; a failure to write it never replaces the original exception.

**Not changed.** Retire still does not disable the connection's secrets, and an update that points a field at another secret leaves the old one in the store (no secret is retired by a connection change). Secret ownership is now enforced, not conventional: see the ownership rule in 16.10.

**Tests.** `Slice5AuditEventTests` (unit: catalog classes, schemas, every producer, bounds), `ConnectionLifecycleAuditTests` (unit: staged rows per operation, no-ops, authorization on the locked row, ambiguous commits, verification order and statuses), `ConnectionServiceSharesTheUnitContextTests` (the unit, repository, secret store and gate hold one scoped context), `ConnectionToolAccessTests` and `ConnectionToolInvocationTests` (browse over a recording event writer); SQL Server: `ConnectionLifecycleSqlServerTests` (66: M7 for six operations on both isolation modes; the M6 matrix; every way a rotation can fail, each proving the previous credential still resolves and nothing of the new one exists; queued rotations, rotation with retirement, rename with config change, rotation across a move) and `ConnectionVerifyBrowseSqlServerTests` (37: the committed record, the probe outside any transaction, a rename, config change, rotation or retirement made during the probe kept, the record waiting for the row, the M6 matrix, the browse's required row withholding the result when it cannot be written, attempt rows, a broken attempt writer not masking the exception). Seeding writes no audit row (the audit table is append-only); each case that needs an empty audit table takes a catalog of its own.

### 16.6 Resource contexts (work package 4)

**Create, update, archive, unarchive** are audited units around the unchanged `ResourceContextService` rules. `ResourceContextService` keeps saving through the repository; run inside the unit, those saves are inside its transaction, so no in-transaction variant was needed.

| Operation | Inside the attempt, in order |
|---|---|
| Create | The service chooses the identity, so a verified commit after a lost acknowledgement is reconstructed from the primary row's target; the slug availability read and the insert; a lost race on `UX_ResourceContexts_Slug` is recognised and answered as the slug-in-use refusal; `resource_context.created` |
| Update | The context row, locked (`GetByIdForUpdateAsync`, new); the up-to-three changes the request may carry; the changed fields counted by comparing before and after each delegate (a re-slug that normalizes to the stored slug saves nothing); `resource_context.updated` naming them, or a declared no-op |
| Archive | The ownership gate, then the context row (locked), then the "nothing live" evaluation and the state change; `resource_context.archived`. Archiving an archived context is a declared no-op. The summary read for the 409 body stays outside the unit, after the refusal |
| Unarchive | The context row (locked), the state change; `resource_context.unarchived`, or a declared no-op for an active context |

**Lock order (S5-7).** The gate-first order of every ownership writer is unchanged. The new context row lock is taken after the gate by archive, and alone (no gate) by update and unarchive: neither can make anything live or move anything, and a creation into the context still takes the gate and reads the state it finds. No cycle is possible: the gated paths only read the context row, and update and unarchive wait for nothing but that row. A test pins that update and unarchive are not held up by a mover holding the gate while archive waits for it.

**Authorization** is the unchanged instance-scoped `resource_context.manage` check, evaluated before the unit opens. The controller still reads the context first, so an unknown id answers 404 before authorization (an existing property, not changed here).

**Tests.** `ResourceContextManagementServiceTests` (unit, 27: staged rows per operation, names not values, no-ops, the refused re-slug, ambiguous commits, the gate observed for archive and not for update or unarchive, the missing unit), `ResourceContextsControllerAuditMappingTests` (3), the extended `ManagementServiceTestHarness`; SQL Server `ResourceContextSqlServerTests` (40: M7 for four operations on both isolation modes; the M6 matrix; the refused re-slug rolling the rename back; no-op requests leaving the row version alone; the slug race resolved by an uncommitted insert held in another session; an archive refused for live ownership changing nothing; update and unarchive not held by the gate while archive waits; two updates queued on the row both kept).

### 16.7 Ownership moves (work package 5)

**One unit per move** replacing the hand-rolled gated transaction of each family. Inside the attempt, in order: the ownership gate; the moved row, locked; authorization (`integration.update` or `connection.update` against the row as locked, so a move that committed while this one waited is what the decision reads; `schedule.manage` before the read for a schedule, deliberately, so the exception type cannot reveal whether a schedule exists); the validator preview uses, unchanged; then, for a committed move, the pair of rows and every update: the moved row and, for an integration, each attached schedule from the validator's one authoritative read.

| Row | Class | Target | Related context | Fields |
|---|---|---|---|---|
| `*.ownership.moved_out` | primary, W1, basis `change` | the moved resource | the source | name snapshot, source and target context ids, and for an integration `schedulesMoved` (exact) and `scheduleIds` (first 500) |
| `*.ownership.moved_in` | secondary, W1 | the same | the target | the same |
| `*.ownership.move_rejected` | W3 attempt, after the unit | the same | the source | name, source and target ids, the sorted distinct blocker codes |

The pair shares the operation's event id as its correlation (they were two separate random ids). A blocked move declares a no-op, so nothing is staged; its rejection is written after the unit with the recorder's single-row writer as a best-effort row of the same principal, and a failure to write it never changes the preview returned (S5-5). A move that committed with a lost acknowledgement returns a result rebuilt from the primary row and the current state: both contexts, an integration's moved schedules, a connection's using integrations; the preview of a move that has already happened cannot be recomputed.

**Locks.** Unchanged: gate, then the moved row. The attached schedules are updated by the narrow ownership update without their own locks, as before, so a schedule writer holding a schedule row simply makes the move wait for it; no writer of a schedule waits for the integration row while holding a schedule row, so no cycle exists. The move still reads bindings, connections and integrations for validation with plain reads under the gate; every writer that could change what those reads decide takes the gate first.

**Tests.** `ResourceMoveAuditTests` (unit, rewritten for staged rows, the recorded rejection, a lost acknowledgement, an unknown outcome, the missing unit, and the DI pin that the unit, repositories and gate share one context while the rejection writer has its own), and the existing move preview, mutation and gate suites unchanged; SQL Server `OwnershipMoveSqlServerTests` (36, replacing the earlier filtering suite that ran over the legacy bridge: M7 for four kinds of move on both isolation modes, the filtering semantics, exactly two rows however many schedules move, a failure after the integration row moved rolling back the integration and its schedules together, a blocked move recorded at the source and nowhere else, a rejection that cannot be written not changing the answer, a move into an archived context, the M6 matrix, a lost acknowledgement returning the rebuilt result) and the earlier `ResourceOwnershipMoveConcurrencySqlServerTests` (10) now running through the converted services.

### 16.8 Real-unit concurrency evidence (work package 6)

Slice 4 left one gap (finding L4): the ownership race suite runs its set-up through a unit that opens a transaction and stages nothing, so no race of the **real** audited unit against an ownership move or an Application archive had been exercised. `OwnershipRealUnitRaceSqlServerTests` closes it. Every racer is a real service on a context of its own with the real unit, the real gate and the real repositories, as two HTTP requests would be, on a locking READ COMMITTED catalog and on a READ_COMMITTED_SNAPSHOT catalog.

**How a race is made deterministic.** A third session holds the ownership gate. The first writer is started and the server's own lock view is read until it is seen waiting for an application lock; only then is the second started and seen waiting in turn; then the gate is released. The lock manager grants a queue of exclusive requests in arrival order, so which writer commits first is fixed by the test, and **both orders** run. A writer that did not wait for the gate fails the test (it did not take the gate). The row-queued variant (a connection move and a rotation, which both need the connection row) does the same with a held row. No sleeps, no test hooks.

| Race | Order 1 | Order 2 | Invariant checked in both |
|---|---|---|---|
| Integration move into T, archive of T | Move commits; the archive is refused (T owns a live integration) | Archive commits; the move is blocked (`target_archived`) and recorded at the source | An archived Application never owns a live integration |
| Integration move, binding creation to a connection staying in the source | Move commits; the binding is refused | Binding commits; the move is blocked (`binding_connection_unreachable`) | No integration in the target holds a binding to a connection that stayed in the source |
| Integration move, attached schedule creation | Create after the move: the schedule inherits the target | Create before the move: the move carries it (`schedulesMoved` 1) | Every schedule is in its integration's context; the rows name the owner each writer locked |
| Integration move, activation | Both applied; the activation row names the target | Both applied; the activation row names the source | The integration ends moved and active |
| Application archive, connection creation in it | Archive commits; the creation is refused (`ResourceContextArchivedException`) | Creation commits; the archive is refused | An archived Application never owns a live connection |
| Two moves of one connection | The second pair starts where the first ended | (the other pair first) | The writer that committed last owns the result |
| Connection move, secret rotation (queued on the row) | Both applied; the rotation row names the target | Both applied; the rotation row names the source | The credential is the rotated one; moving contexts moved no secret |

28 cases (7 races x 2 orders x 2 isolation modes). The carried-forward integration delete races stay with their own suites (slice 3); the secret races (queued rotations, rotation with retirement, a rotation across a move, a change made during a probe) are in `ConnectionLifecycleSqlServerTests` and `ConnectionVerifyBrowseSqlServerTests` (16.5).

**What these do not prove.** They prove the order the gate and the row locks impose, and that the units' decisions read what the previous writer committed. They do not prove behaviour when the gate times out (15 s) under sustained load, and they do not search for interleavings beyond the queue positions chosen. No mutation run was made: the tests assert on the server's wait list before the gate is released, so a writer that stopped taking the gate fails them by not waiting.

### 16.9 Independent review (one combined review)

One independent review of the whole slice (the six commits' diff, the record and the contracts) returned **ready after bounded fixes** and no blocking finding. Dispositions:

| # | Finding | Disposition |
|---|---|---|
| 1 | The verify, ownership-move and test-connect routes still turned an audit invariant failure into a 404 (the controllers' `InvalidOperationException` catch) | **Fixed.** The four remaining catches exclude `AuditInvariantException`, as create, update and retire already did; it now reaches the 500 handler instead of being reported as "not found" |
| 2 | A request supplying `SecretValues` for a field whose working reference is any GUID adds a version to that secret without checking it belongs to the connection | **Fixed in a follow-up remediation commit on this branch, on the owner's ruling (16.10).** |
| 3 | A connector whose default is applied to the incoming config (an empty `AuthMode`) made a request that changes nothing compare as a change against a legacy row that never stored the default | **Fixed.** The stored config gets the same defaults before the comparison, so such a request is still a declared no-op |
| 4 | The comment on the connection producers implied the keys of every rotated field were on the unit's rows | **Fixed (wording).** The keys of material that was stored are on `connection.secret.stored`; a reference change and the fields of a rotation that also changed config are counted on `connection.secret_rotated`, as the catalog rule for the Secrets category requires |
| 5 | A probe cancelled by the caller was recorded as a failed verification although no answer was observed | **Fixed.** It is `unknown` with no status; a verifier that threw is still `failed` |
| 6 | After a lost acknowledgement a rebuilt integration move counted the (capped) listed ids instead of the exact `schedulesMoved` | **Fixed.** `AuditCommittedOperation.GetCount` reads the exact count; the ids stay the first 500 |

### 16.10 Residual risks and limits

#### Secret reference ownership (owner ruling: fix before merge)

**Vulnerability (pre-existing, found by the review).** A principal authorized to update Connection A could point one of A's secret fields at any parseable GUID, and a request supplying `SecretValues` for that field added and activated a version of whatever secret the reference named. With another connection's reference that overwrote its live credential (and A would then resolve it); the same worked against any other configuration secret. Create accepted arbitrary references. A parseable GUID was the only check.

**The rule.** A reference is acceptable for field `F` of connection `C` if and only if the secret store holds that secret with provider `platform-connection` and the name `connection:{C.id}:{F}`, compared exactly (ordinal, so another case, a longer name, another field or another provider all fail). The proof is the secret's own stored descriptor, never the connection row. A missing secret, another provider, another owner and another field are refused with the same message, which names only the declared field key and says nothing of the reference, the owner or which condition failed (`ConnectionSecretMaterialProvisioner.EnsureReferenceOwnedAsync`). Nothing in the platform lets a caller choose a secret's name or provider (the three secret creators use fixed prefixes; `SqlSecretStore` cannot rename a descriptor), and a connection id is minted server-side, so the rule cannot be satisfied by forging a name.

| Operation | Where the rule applies |
|---|---|
| Create | Every supplied `SecretRefs` entry, inside the unit. The id is minted by the service, so no secret can already be owned by the new connection: any supplied reference is refused. Credentials come in through `SecretValues`, which still creates the connection's own secret |
| Update | Every reference the request adds or points elsewhere, against the row as locked; and, when the request changes config, every reference. Config decides where a credential is sent, so a reference that is not the connection's own must not travel with a new host or base URL |
| Rotation (`SecretValues`) | Every reference that would receive a version, **before the first secret write**, whatever the row says. A reference already on the row is not proof; a poisoned row cannot rotate the secret it names, however the references were set |
| Metadata-only update (rename; config and references left as stored) | Not re-proved, so a row written before the rule stays renamable. It can never be rotated or pointed at a new destination |
| Repair | A request that drops the reference (`SecretRefs` without the key) and supplies `SecretValues` for the field gets a new secret named for the connection; the foreign secret is never touched |

**Legacy and imported references.** No code path ever produced a legitimate reference that fails the rule (the naming is stable and the only creator uses the connection's own id), so a row that fails it was poisoned or written by hand. Cross-resource secret sharing is not a supported use (the design gates treat secrets as per-connection), so no owner decision was needed. Runtime resolution and verification are unchanged (below).

**Behavior changes for callers.** `POST /api/connections` with a non-empty `secretRefs` is now a 400 (supply `secretValues`). A `PATCH` that sets a reference the connection does not own is refused; a `PATCH` that changes config on a connection whose stored reference is not its own is refused until the reference is dropped or replaced by the connection's own. The update route maps this refusal to 404 like its other `InvalidOperationException` refusals (pre-existing mapping; create answers 400).

**Evidence.** Unit (`ConnectionLifecycleAuditTests`, the fixture now has two secret fields so a request can mix owned and unowned): creation naming another connection's secret, a secret that does not exist, and with material for the same field; update replacing a reference with a foreign one, with material in the same request, and rotating in a later request; rotation of a poisoned and of a dangling stored reference; one unowned reference among several fields refused before any field is stored (the check ordering); a provider/name theory (a different field, another case of field or prefix, a longer name, another connection's same-named field, another provider) and a GUID-spelling theory; a config change refused on a poisoned row and unaffected on an owned one; blank material on a poisoned row a no-op that never reads the store; a metadata-only edit still working; repair by dropping the reference; the valid rotation adding a version only to the owned secret; first-time provisioning creating the secret under provider `platform-connection` and the exact name; and no exception message containing the reference, the value or the owner. Real SQL Server (`ConnectionLifecycleSqlServerTests`, both isolation modes, real `SqlSecretStore`): five request shapes naming a victim's secret (create, create with material, replace, replace and rotate, another spelling) leave the whole snapshot (connections, descriptors, versions, secret audits) and the audit table unchanged, the victim still resolving its own credential with one version; a poisoned row refusing rotation and a config change, accepting a rename, and repaired by dropping the reference; wrong provider or wrong field name refused with the lookalike secret untouched; first-time provisioning naming the secret for the connection and field, then rotating it as an ordinary second version. The earlier "reference to a different secret" matrix case now points at a secret the connection owns.

**Targeted independent review (one pass, Opus 5.5 High, ownership path only).** Verdict **ready after bounded fixes**, with no defect in the production rule: one writer of `SecretRefs`, no forgeable names, check-then-mutate safe under READ COMMITTED and snapshot, the refusal rolling the unit back, no oracle. Its findings were about tests and hardening, all applied: the check-ordering test (a second secret field and a mixed request), a misnamed create test, a stray-audit-row assertion for refused creates, an ownership test that had become vacuous (now a reference written on the row), a dangling-reference rotation case (before the fix the store's own "not found" message echoed the id), and config edits and blank material on poisoned rows. Its hardening suggestion to re-prove every reference when config changes was adopted (above); its suggestion to refuse create references before the unit was not (same behavior, one extra lookup on a path that is always refused).

**Remaining risks (outside this remediation).**
- **A row poisoned before the rule still resolves the foreign secret at run time and in verification** (`ResolvedConnectionMaterialBuilder`, the Sftp and CampusNexus verifiers), and the rule only stops it being sent to a new destination or rotated. A one-time inventory of connections whose references fail the rule (similar to the integration-setting secret inventory), or the same check at resolution time, would close it; not done here because it changes resolution behavior.
- **The platform-setting provisioner (`PlatformSettingSecretMaterialProvisioner.ReplaceAsync`) adds a version to a reference already stored on the setting row without an ownership check.** Same pattern, exploitable only if a setting row can be poisoned; its secret name is not scoped per integration. Not touched (separate surface).
- `GET` connection returns the raw secret references to a principal with `connection.read` (unnecessary exposure of ids; harmless now that ownership is enforced).
- **No mutation run.** The race tests assert on the server's wait list before the gate is released, so a writer that stops taking the gate fails them by not waiting; a mutation tool was not run over the slice.
- **Authorization inside the unit proves the row, not the policy.** The decisions re-evaluated against the locked row (update, retire, moves) are proven to read what the previous writer committed; the policy engine itself is the existing one and was not re-tested.
- **A connection has no owning-context field in its audit identity beyond the related context.** The related context on each connection row is the context at the time of the operation; the move pair records both ends.
- **Scratch catalogs.** The SQL suites create per-case catalogs (the append-only audit table cannot be cleaned up). A run killed part-way leaves catalogs behind on the development server; they are inert and can be dropped by name.
- **Carried to the audit-resilience slice, not touched here:** the `AuditLogs.OperationEventId` verification-count deadlock.

### 16.11 Closure

Slice 5 was accepted by the owner and merged to `master` with PR #24 as `ce7d5169cc0b702d0cab797b18d62132dd574cf8` (2026-10-12; the branch head was `7d6bd1e`, the secret-reference ownership remediation of §16.10). The CI run on the pull request head succeeded (`build-and-test` and `browser-security`).

**The first post-merge CI run failed and its rerun succeeded; the cause is a flaky test, not a product defect.** Run `38289139311`, attempt 1: `build-and-test` ended with 1 failure in 1223 tests, `PlatformSqlStructureBrowserSqlServerTests.M6_cancelling_in_the_middle_of_opening_a_real_connection_stops_promptly_with_a_cancellation` ("Expected a `System.OperationCanceledException` to be thrown, but no exception was thrown", failing after 30 ms; `browser-security` passed). Attempt 2 of the same run, on the same commit, passed in full. The test opens a SQL connection to a local TCP listener that accepts and never answers and cancels the caller 200 ms later; it depends on the SQL client holding that handshake open on the runner. Neither the test nor `PlatformSqlStructureBrowser` was changed by slice 5 (the diff of both between `b38f1fd` and `ce7d516` is empty), the same tree passed twice on the pull request, and the failure is outside every audited path (the browse unit tests and the connection SQL suites passed in the same run). Disposition: transient, proceed. The test is timing- and client-dependent and could be hardened (assert on the outcome of the cancelled call, not on the client keeping the handshake open); that is test hygiene outside AUD-1.4 and was not done here.

Carried forward from slice 5, unresolved and not marked resolved here: the audit verification-count deadlock (AUD-1.6); the inventory and disposition of legacy connections whose stored secret references are not their own (pre-RC1 security validation, §16.10); the platform-setting secret ownership gap and the unnecessary exposure of secret references on connection reads (pre-RC1 security validation, §16.10); attention routing, module lifecycle and the runtime shutdown request (slice 7).

## 17. Slice 6: notification definitions, subscriptions, managed contacts and diagnostic live sends

Date: 2026-10-12. Branch `aud-1.4-slice-6-notifications`, from `origin/master` at `ce7d516`. Authorized by the owner after the slice 5 merge. Nothing here claims AUD-1.4 complete: slices 7 and 8 have not started.

### 17.1 Writer inventory (source-backed, before any change)

Three read-only inventories (definitions and subscriptions, managed contacts, diagnostic sends) were made with `git grep` over `src`, `tests` and `docs`. **No writer of these tables exists outside the services below**: no seeder, import, clone, bulk path or startup sync writes a notification definition, subscription, opt-out, preference or managed contact (built-in definitions are inserted by migrations only; `HistoricalActorNormalization` rewrites actor ids at start as IAM maintenance). Controllers call services only.

| Family | Writer (before) | Authorization | Transaction and locking (before) | Audited (before) |
|---|---|---|---|---|
| Definition | `NotificationDefinitionService.CreateAsync` | `notification.definition.manage` and `integration.update`, before the store | Store transaction with the integration row lock; the name check was outside it | No |
| Definition | `UpdateAsync`, `SetEnabledAsync` | as above | None: a tracked read and a write, last writer wins | No |
| Definition | `DeleteAsync` (cascade: subscriptions, user opt-outs, per-user preferences) | as above | One `SaveChanges`; no lock | After the commit, through the legacy bridge, with a free-text sentence |
| Subscription | `NotificationSubscriptionService.AddRecipientAsync` (optionally creating a managed contact first) | definition manage | Own transaction only when it names an integration; the contact was committed before it | No |
| Subscription | `RemoveAsync`, `PatchAsync`, `SetUserOptOutAsync`, `RemoveUserOptOutAsync` | definition manage | None | No |
| Managed contact | `ManagedContactManagementService`: `CreateCustomAsync`, `UpdateCustomAsync`, `SetEnabledAsync`, `LinkToUserAsync`, `UnlinkUserAsync` | `managed_contact.manage` (and `user.read` to link) | None; address uniqueness a check-then-insert with no index | No |
| Managed contact | `ImportEntraUsersAsync`, `ImportEntraGroupsAsync`; `ManagedContactRecipientProvisioner` (the contact a subscription creates) | `managed_contact.import`; manage on the creating branch | None; each user saved on its own | No |
| Diagnostics | `NotificationPipelineTestService.SendSmtpTestEmailAsync`, `RunPlatformPipelineTestAsync` (live) | `notification.definition.manage` (and integration read) | No transaction across the send; the immediate dispatch claimed the oldest pending rows of the whole system | No |

Already converted and not touched: the account's own notification preferences (`account.notification.*`, AUD-1.2), which also write subscription rows. Out of scope by the owner's ruling: attention routing and the attention contact groups, which are operational teams with external-email members (slice 7 and LST-1.4); module lifecycle; the dispatch and evaluation hosted services (a delivery pipeline, not a configuration change).

### 17.2 Decisions

| # | Decision |
|---|---|
| S6-1 | **Lock order for every definition and subscription writer: the integration row, then the definition row, then the email lock, then the rows written.** The integration row is the one an ownership move, an integration delete and every earlier integration writer take, so authorization (which depends on the integration's governed identity) is run again once it is held. The definition row is taken by the definition's own writers and by every admin subscription writer, so a subscription cannot attach to a definition that is being deleted and a delete counts what it removes. A platform- or module-scoped definition takes no integration lock for its own sake (its authorization is instance-wide); a subscription that names an integration still locks it. |
| S6-2 | **Managed contact address uniqueness stays an application rule, made reliable by one application lock** (`apperture:managed-contact-email`, owned by the transaction) that every writer of an address takes before it checks. No index or migration is added: existing data was not inspected for duplicates, and a unique index is a recommended follow-up. A directory import still never merges into an unlinked contact. |
| S6-3 | **A contact created for a subscription is part of the subscription's unit.** `ManagedContactRecipientProvisioner.ResolveOrCreateAsync` runs inside it, under the email lock, and stages `managed_contact.created` (via `subscription`) beside the subscription row. The permission is still checked only on the creating branch. |
| S6-4 | **A user import is one unit per user**, so a batch keeps its partial results; a group import is one unit per group but has no per-item failure channel, so a failure aborts the rest of that request (as before). A user whose outcome cannot be established is reported as `importFailed` with the operation id, not as a success and not by failing the whole request. A user whose contact already matches the directory declares a no-op; its link's sync time is refreshed afterwards, outside the unit, touching no contact field. |
| S6-5 | **Nothing of the person is recorded**: a contact row has an id, the source, how it was created, whether it has an address and the enabled flag; an update names the changed fields. This follows gate section 12.3 (an address is recorded only where a schema names an operational field, and a third party's address is not one). The definition's name is recorded as a snapshot, as every other configuration name is; its rule and template (free text with subjects, bodies and links) never. |
| S6-6 | **A group import is recorded as `managed_contact.group_imported`, targeting the imported group's directory row.** The target vocabulary is closed and the group table is metadata only, so no new target type was added; the owner may prefer a dedicated one. |
| S6-7 | **A diagnostic send is recorded once, afterwards, as a best-effort `external_attempt`.** The row says what the transport reported: `accepted` (the server took the message; never delivery), `rejected`, `failed`, or `no_answer` (cancelled, timed out or lost after the session began: outcome unknown). A request that never reached the transport (no recipient, an address or credential that could not be used, email not configured) is a separate `test_refused` attempt row. A dry run records nothing. No database transaction is open across the send. The transport result carries a disposition so that a cancelled call can be told from a refusal; a channel that does not state one is read from its success flag. |
| S6-8 | **The immediate dispatch of a pipeline test sends only the rows of the instance it created** (a bounded fix of the defect recorded in section 7: it claimed the oldest pending rows of the whole system, so the operator's row would have attributed other people's sends to a test). The counts in the audit row are the dispatcher's own tally for those rows. |
| S6-9 | **A change that changes nothing declares a no-op** (the stored values, the current enabled state, the subscription already at that mode, the user already linked, an opt-out that already exists): nothing is recorded and the row's update time does not move. |

### 17.3 Work packages

| WP | Scope | State |
|---|---|---|
| WP1 | Audit contracts: 17 new catalog actions, the converted `notification.definition.deleted`, schemas, typed producers, catalog pins | Done |
| WP2 | Definition create, update, enable, disable, delete as units; the store joins the unit; controller refusals exclude an audit invariant | Done |
| WP3 | Subscription add, remove, patch and user opt-out set and remove as units | Done |
| WP4 | Managed contact create, update, enable, link, unlink, user and group import, and the subscription provisioner as units; the email lock | Done |
| WP5 | Diagnostic SMTP and pipeline tests as external attempts; the transport disposition; the scoped dispatch | Done |
| WP6 | Real SQL Server validation; independent review; closure | Done (17.7 to 17.10) |

### 17.4 Behavior changes

| Change | Why |
|---|---|
| Adding a recipient who is already subscribed, and renaming a definition onto another definition's name, are `400` refusals | They were an unmapped `500` from the unique index; both are checked under the locks that serialize the writers |
| A subscription and the managed contact it creates commit or roll back together | The contact used to be committed first and orphaned by any later failure |
| A change that changes nothing no longer moves `UpdatedAtUtc` and records nothing | S6-9 |
| A subscription cannot be added to a definition that is being deleted | S6-1; before, it left an orphan row that blocked the integration delete and could not be removed through the API |
| Update, enable and delete of a definition, and every subscription writer, re-check authorization against the locked integration | An ownership move that commits while the request waits is what the check reads |
| The definition delete is audited in the same transaction with the cascade counts, and the legacy free-text sentence is gone | W1 |
| The immediate dispatch of a pipeline test sends only its own instance's rows | S6-8 |
| A failed or unknown directory import item is that item's result, with the operation id when the outcome is unknown | S6-4 |

### 17.5 Definitions and subscriptions

**Definition create** takes the integration row (requiring it to exist), re-checks the management permission against it, checks the name (every writer of an integration's definition names takes that row, so the check cannot race), inserts the row and stages `notification.definition.created` in the integration's owning context. The identity is minted before the unit, so a lost acknowledgement is reconstructed from the row. **Update and enable** add the definition row lock, validate the documents against the row as locked, compute the change against it and stage `updated` (naming `displayName`, `description`, `rule`, `template`, `enabled`, `severity`, `toastDefault`) or `enabled` and `disabled`. A built-in definition keeps its immutable rule and template and its mutable flags. **Delete** takes the same locks, refuses built-ins, removes the cascade and stages `deleted` with `subscriptionsRemoved`, `userOptOutsRemoved` and `userPreferencesRemoved`; a definition deleted meanwhile changes nothing, and a verified commit after a lost acknowledgement reports success.

**Subscriptions** take the definition's integration, the subscription's own integration when it differs, and the definition row, then the contact (add), then the subscription or opt-out row. A removed subscription keeps its id on its row. A `PATCH` carrying both an enabled flag and a delivery mode still applies only the enabled flag. The account's own opt-in and toast writers do not take the definition lock (they are the AUD-1.2 units); see 17.10.

### 17.6 Managed contacts and diagnostics

**Contacts.** Create, update, enable, link and unlink are units under the email lock and the contact row lock; link refuses an unknown user and unlink of an unlinked contact is a no-op. A re-import compares the contact and its directory link before and after the upsert and names what differed (`displayName`, `email`, `entraSync`); a manual disable survives a sync as before. **Diagnostics.** See S6-7 and S6-8. `notification.diagnostics.pipeline_test_attempted` records what the run persisted (instances and outbox rows created, for every definition the synthetic event matched) and, when it dispatched, how many sends were attempted and how each ended; its outcome is `requested` when no send was made, `unknown` when any send had no answer, `failed` when any was refused or failed, and `succeeded` only when every send made was accepted. A request cancelled during the dispatch still leaves its evidence (the tally is kept as the sends happen, and the row is written with no caller token).

### 17.7 Validation evidence

**Unit** (EF InMemory with a recording unit, M7 at service level): `Slice6AuditEventTests` (the contracts), `NotificationDefinitionLifecycleAuditTests` (15), `NotificationSubscriptionLifecycleAuditTests` (18), `ManagedContactLifecycleAuditTests` (22, replacing the earlier mock-based import tests), `NotificationPipelineTestServiceTests` (the diagnostics, including a cancelled dispatch and a failed evidence write not changing the response), `NotificationServicesShareTheUnitContextTests` (the unit, every store and repository hold the request's one context), `Aud14Slice6ConvertedWritersTests` (no bridge use, no controller maps an audit invariant to a refusal), and the catalog pins.

**Real SQL Server** (`NotificationDefinitionSqlServerTests`, `NotificationSubscriptionSqlServerTests`, `ManagedContactSqlServerTests`, `NotificationDiagnosticsSqlServerTests`; locking READ COMMITTED and READ_COMMITTED_SNAPSHOT shared catalogs for M7, races and no-ops; a catalog of its own per M6 case; they fail, never skip, without SQL Server): M7 for every action; M6 (audit insert failure leaves nothing, a lost acknowledgement is verified without rerunning, a confirmed rollback reruns once, an unknown outcome is wholly committed or wholly absent, an operation lock that is not granted changes nothing) for a create, an update and a delete of a definition, an addition, an addition that creates a contact, a removal and an opt-out of subscriptions, and a create, an update, a link, a user import and a group import of contacts, with bespoke M6 cases for the import because its failures are per-item results; the delete verified with its target now missing; no-ops and refusals changing nothing, not even the update time; deterministic races with the writers queued behind the same lock in both orders (two renames to one name; an update against a delete; a subscription against a definition delete, leaving no orphan; two subscriptions creating the same contact in different integrations; two creates of one address; a directory import against a manual create; an ownership move committing while a definition update waits, the row naming the owner the unit locked); the real claim sending only an instance's own rows while older rows of another instance wait, and two dispatchers racing for the same rows sending each at most once; the evidence rows as the real writer stores them and a failed write reported false. The existing integration-delete race suite (which drives the converted subscription writer) and the account suites pass unchanged.

### 17.8 Carried forward, not marked resolved

The audit verification-count deadlock (AUD-1.6); the inventory and disposition of legacy connections with foreign secret references and the platform-setting secret ownership and reference exposure (pre-RC1 security validation); attention routing, module lifecycle and the runtime shutdown request (slice 7); attention contact groups and operational team membership (LST-1.4); previously accepted runtime and infrastructure findings under their owners.

### 17.9 Independent review (one integrated pass, Opus 5.5 High)

Verdict **ready after bounded fixes**, no blocking finding. It confirmed that every store write joins the unit's transaction, reruns start from a cleared tracker, nothing external runs inside an attempt, `DeclareNoOp` is never called after a row was staged, the lock order has no cycle across the slice's writers, no other production writer of these tables exists, the account's own opt-in and toast writers cannot race a definition delete (a deletable definition is never self-subscribable), and the outbox claim keeps its at-most-once behavior. Dispositions:

| # | Finding | Disposition |
|---|---|---|
| 1 | The Entra no-op sync-time refresh was a full, unaudited, unlocked upsert that could write an older directory value back over a newer audited update | **Fixed.** `TouchEntraLinkSyncAsync` updates only the link's times and leaves the tracker clean on failure; a repository test pins that no contact field moves |
| 2 | An unsupported `deliveryMode` on subscription add surfaced as an audit-schema error returned as a `400`, a silent behavior change | **Fixed.** Validated before the unit with the same rule `PATCH` uses; documented; tested |
| 3 | Some send outcomes were recorded wrongly: a timeout or a connection lost after the session began was `failed`; an unusable address or credential was an attempt | **Fixed.** `no_answer` (renamed from `cancelled`) covers cancellation, timeout and lost connections; an address the platform cannot build and an unresolvable credential are `NotAttempted` and recorded as `test_refused`; the channel no longer throws on a credential failure |
| 4 | An admin add can race the account's own opt-in of the same platform-wide row (no definition lock on the account path) and hit the unique index | **Fixed for the admin side.** The unique violation is the same refusal as a duplicate. The account writer is unchanged (17.10) |
| 5 | A group import has no serialization and no per-item results | **Documented, not changed** (S6-4 narrowed; 17.10): two concurrent first imports of one group make the loser fail with nothing partial for that group, as before |
| 6 | A verified commit of an update, enable or patch whose row was deleted afterwards was reported as `404` | **Fixed.** The reconstruct throws, so the unit reports `committed_result_unavailable` |
| - | `recipientRef` is length-checked only; an address typed there would be recorded | **Fixed.** A reference containing `@` is refused |
| - | Test gaps: in-unit re-authorization pinned for only some writers; no classification, invalid mode or refresh tests | **Fixed.** Re-check counts for patch, remove, both opt-out writers and the definition delete; the classification and refusal cases; the invalid mode; the repository refresh |
| - | Dangling references to 17.10 | **Fixed** |

### 17.10 Residual risks and limits

- **Managed contact address uniqueness is still an application rule**, now serialized by one lock that every writer in the services takes. A writer outside the services would bypass it (none exists). A unique index is the durable fix and needs a duplicate-data check first.
- **The account's own opt-in and toast writers (AUD-1.2) take no definition lock.** A deletable (user-created) definition is never self-subscribable, so they cannot race a delete; they can race an admin add of the same platform-wide recipient on a built-in definition, which is now a refusal. Giving them the definition lock would be a small follow-up.
- **A group import fails as a whole on a lost first-import race** (unique index) or an unknown outcome, as it always did; nothing partial is left for the failed group.
- **A subscription added by direct recipient reference does not check that the recipient exists** (unchanged); only the reference's shape is checked, so an address cannot be recorded as one.
- **A pipeline test creates events, instances and outbox rows for every definition the synthetic event matches**; those rows are sent later by the background dispatcher, unattributed to the operator. The audit row counts what the run created and what it sent.
- **A request cancelled during a dispatch can leave a claimed row in `delivering`** (existing dispatcher behavior, assigned to its owner); the audit row still records the sends made.
- **The SMTP test recipient is still logged by the transport** (a known separate item, not changed here).
- **W3 evidence is best-effort**: a failure to write it loses the record of a real send and is logged as a warning (by design, gate section 13.3).
- Pre-existing and unchanged: linking a contact to a user deleted meanwhile fails on the foreign key; the subscription remove route does not map a missing definition.
- **No mutation run** was made over the slice; the race tests assert on the server's lock view before releasing, so a writer that stopped taking a lock fails them by not waiting.

### 17.11 Closure

Slice 6 was accepted by the owner and merged to `master` with PR #25 as `30e760e732228e95619a7edcbfde8607f4673ca2` (2026-10-12; the branch head was `23db9b3`, the bounded fixes of the independent review, §17.9). The owner confirmed that the post-merge CI run succeeded. The carried-forward items of 17.8 and 17.10 stay open under their owners; none is marked resolved here.

## 18. Slice 7: attention routing, module lifecycle and the runtime shutdown request

Date: 2026-10-12. Branch `aud-1.4-slice-7-routing-modules-runtime`, from `origin/master` at `30e760e`. Authorized by the owner after the slice 6 merge. Nothing here claims AUD-1.4 complete: slice 8 has not started.

### 18.1 Writer inventory (source-backed, before any change)

`git grep` over `src` for every write to `InstanceAttentionRoutings`, `IntegrationAttentionRoutings`, `ModuleInstallationStates` and `RuntimeControlRequests`, and for every caller of the writers below. Migrations seed the instance routing row; no seeder, import, startup sync or bulk path writes these tables otherwise.

| Family | Writer (before) | Callers | Authorization | Transaction and locking (before) | Audited (before) |
|---|---|---|---|---|---|
| Attention routing | `AttentionRoutingManagementService.UpsertInstanceDefaultRoutingAsync` | `PUT api/attention-routing/defaults` | Schedule governance manage (instance-wide), in the controller | None: a tracked read and a save, last writer wins | No |
| Attention routing | `UpsertIntegrationRoutingAsync` (insert or update of an override) | `PUT api/integrations/{id}/attention-routing` | `integration.read` and `integration.update`, before and again under the integration row lock (PR #22) | Own transaction: integration row lock, team check, read, write | No |
| Attention routing | `ClearIntegrationRoutingAsync` | `DELETE api/integrations/{id}/attention-routing` | as above | Own transaction with the integration row lock | No |
| Module lifecycle | `SqlModuleRegistryService.InstallModuleAsync`, `EnableModuleAsync` (installs a module that is not installed), `DisableModuleAsync`, `UninstallModuleAsync` | `ModulesController` through `ModuleWorkflowListingService`; a Management Host test harness at start | `module.configure` (instance-wide), in the listing service | None: reads, a save, then a legacy append that **saves the same context**; no lock; the conflict check reads all installations | Legacy free text after the save (`module.lifecycle.*`, outcome `succeeded` or `blocked`), the actor falling back to `system:module-lifecycle` |
| Module discovery | `DiscoverModulesAsync` | Host start, listing | n/a | No write but the row | Legacy telemetry row (W4, AUD-1.5; **not converted**) |
| Runtime shutdown | `RuntimeControlService.RequestShutdownAsync` | `POST api/runtime-instances/{id}/controls/shutdown/request` | `runtime.control.shutdown.request` on the runtime scope, existence and capability, in the controller | A queued-request check, then an insert outside any transaction; a unique violation (any 2601 or 2627) re-reads the winner; then a legacy append under the request's name **whether or not this request inserted** | Legacy, after the save |

**Pre-existing defects found (fixed in this slice).**
- *A blocked enable could commit a change.* The enable conflict check sets `Enabled = true` on the installation row it loaded, which is tracked, and the legacy append then saves the same context. A blocked enable of an installed, disabled module therefore persisted `Enabled = true` under a `blocked` row; a blocked enable of a module that was not installed persisted the implicit installation it had added.
- *A losing duplicate shutdown request recorded a success.* The loser of the queued-request race and any request that found nothing new to insert still appended `governance.runtime.shutdown.request.created` under its own actor.
- `docs/04-api/contracts.md` names the shutdown action `runtime.shutdown.request` (section 7); the catalog name is `governance.runtime.shutdown.request.created`.

### 18.2 Decisions

| # | Decision |
|---|---|
| S7-1 | **Attention routing actions** (W1, Configuration, standard, basis `change`): `attention_routing.default.updated` (target `instance`), `attention_routing.override.created`, `.updated` and `.cleared` (target the integration, related context its owning context, its name as a snapshot). Rows carry the condition key, as a closed enum (`expected_run_missing`, with any other key recorded as `other`, because overrides accept unvalidated keys and a caller's text must not enter an id field), and for an update the names of the fields that changed. Team ids, durations, flags' values, recipients and addresses are never recorded. An upsert's primary action depends on the row it reads under the lock, so the unit declares `created` and `updated` as alternates. |
| S7-2 | **Routing locks.** The override writers keep the integration row lock as the first lock in the unit (slice 3 coordination with the integration delete) and the in-unit re-authorization of PR #22. The instance default takes its own row with an update lock, so the change is computed against the row it writes. A request that changes nothing declares a no-op (the rows have no update time). Clearing an absent override is a no-op returning false, as before. |
| S7-3 | **Module lifecycle actions.** The four existing `module.lifecycle.install`, `.enable`, `.disable` and `.uninstall` entries become converted W1 actions with schemas (persisted version, previous and resulting persisted state, the restart effect the transition reports); their outcome map is kept so legacy `blocked` rows still classify. Blocked attempts are the new W3 attempt actions `module.lifecycle.install_blocked`, `.enable_blocked`, `.disable_blocked` and `.uninstall_blocked` (basis `attempt`, outcome `blocked`), naming the blocker codes and never the blocker messages, written after the unit as best-effort rows of the same actor. Discovery stays a legacy W4 row (AUD-1.5). |
| S7-4 | **One module lifecycle lock.** Every lifecycle writer takes one transaction-owned application lock (`apperture:module-lifecycle`) inside its unit before it reads anything, because the contribution-conflict decision of one module depends on the enabled state of every other module. Blocker evaluation works on untracked copies, so evaluating a candidate state can never be saved. A lost race on `PK_ModuleInstallationStates` (reachable only by a writer that bypasses the lock; none exists) reruns the decision once against the winner's row instead of failing. |
| S7-5 | **Implicit installation.** Enabling a module that is not installed inserts its installation, enabled, in the same unit, and stages a secondary `module.lifecycle.install` row beside the primary `module.lifecycle.enable` row, which flags that the request installed it. A blocked or failed enable leaves no installation. |
| S7-6 | **Module no-ops and refusals.** Installing an installed module, enabling an enabled module at the discovered version, and disabling a disabled module are declared no-ops (success as before, nothing recorded, no update time). Blocker evaluation keeps its existing order relative to these checks, so every answer the API gave before is unchanged. Disabling or uninstalling a module that is not installed stays a refusal (`not_installed`) and is recorded as a blocked attempt. An undiscovered module is the existing 400, with no row. |
| S7-7 | **Module actor.** The authenticated principal when there is one (every HTTP path is authorized by the listing service first); otherwise the named `system:module-lifecycle` actor, the established literal, for host-start callers. The persisted state is the only claim: a row never says a module is active in any runtime process, and the restart effect is recorded as the transition reports it. |
| S7-8 | **Runtime shutdown request** (`governance.runtime.shutdown.request.created`, converted: W1, basis `request`, outcome `requested`, target the runtime instance's scope key, fields the runtime id, the request id and the control kind). Inside one unit: the runtime identity row with an update lock (so concurrent requests for one runtime queue, and the existence and capability decision is read under the lock), the queued-request check, then the insert, whose `RequestedBy` is the unit's actor. An existing queued request is returned unchanged as a no-op (no row, not re-attributed). The insert is saved inside the attempt; a violation of `IX_RuntimeControlRequests_RuntimeId_ControlKind` (and no other) is a no-op returning the winner (AUD-1.3 D18). A verified commit after a lost acknowledgement returns the request read by the id the primary row records, with its original actor. The capability and permission checks before the unit are unchanged. |

### 18.3 Work packages

| WP | Scope | State |
|---|---|---|
| WP1 | Audit contracts for the three families: catalog entries, action names, schemas, producers, catalog pins; the contracts correction | Pending |
| WP2 | Runtime shutdown request as an audited unit; unit and real SQL Server evidence | Pending |
| WP3 | Attention routing writers as audited units; unit and real SQL Server evidence | Pending |
| WP4 | Module lifecycle writers as audited units, the lifecycle lock, blocked attempts; unit and real SQL Server evidence | Pending |
| WP5 | Converted-writer pins, the record, independent review, bounded remediation, the pull request | Pending |
