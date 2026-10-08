# AUD-1.4 configuration lifecycle: implementation record

Date: 2026-10-07

Status: **In progress on branch `aud-1.4-configuration-lifecycle`; not pushed, no PR.** Slice 0 (this record and the gate clarifications) and slice 1 (secret-store transaction participation) are implemented, validated (§4.7), independently reviewed, and corrected for the review's bounded findings (§4.10). Slice 1 is ready for slice 2, which has not started. Slices 2 to 8 have not started. This record grows with each slice; nothing below marks a later slice complete.

Base: `origin/master` at `cbe0bdb` (merge of PR #17, AUD-1.3). Post-merge CI run 37694556687 succeeded on that commit. The merge tree equals the reviewed AUD-1.3 head `cd964b6`.

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
| 2 | Platform and integration settings: one unit per set, clear and reset; the live-secret-first defect; an in-transaction reset that integration delete can call | Not started. Independent review of slices 1 and 2 before slice 3. Watch list: §4.11 |
| 3 | Schedule delete and integration delete as W1 with verification (M6 each, including lost acknowledgement with a missing target); D-4 | Not started. Independent review |
| 4 | Integration create, update, activate, deactivate, retire; bindings create and delete; schedule create (with its assignment), update, enable, disable | Not started |
| 5 | Connections create, update, rotate, retire, verify (`external_attempt`), browse (W2); the connection rotation live-secret defect; resource contexts create, update, archive, unarchive; ownership moves gain verification | Not started |
| 6 | Notification definitions (create, update, enable, delete), subscriptions, managed contacts, diagnostics live sends (W3 `external_attempt`) | Not started |
| 7 | Attention routing rules (D-2); module lifecycle (W1, with W3 for blocked attempts); the runtime shutdown request and its duplicate case | Not started |
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

Platform settings and integration settings (slice 2), integration delete through settings reset (slice 3) and connections (slice 5) still call the store outside any transaction. They get the standalone path: each store operation is now atomic with its `SecretAudits` row, and otherwise behaves as before. Their known defects, the live secret activated before the owning change and secrets orphaned when the owning change fails, remain until their slices convert them.

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

- **Compensation after a failed upsert (DF1 of the review).** `PlatformSettingsService` and `IntegrationSettingsService` store a replacement secret, then save the setting row, and on failure retire the new secret as compensation. The repository does not detach the failed row, so the compensating save sends it again, fails, and leaves the new secret orphaned. This predates slice 1 and is unchanged by it. Slice 2 replaces the compensation with the audited unit and the joining store, which removes the case; its tests must prove that a failed setting save stores no secret.
- The live-secret-first defect and the in-transaction reset that integration delete calls stay in slice 2's scope (§3).
- The store's exception messages (§7) are touched only if slice 2 changes a path that surfaces them.

**Next step:** slice 2 (platform and integration settings), then independent review of slices 1 and 2 before slice 3 (§3).

## 5. Rules every slice follows

**Actor.** The unit's actor, resolved once before the first attempt from the authenticated context (`AuditActor.CurrentPrincipal`), or a named non-human entry. A request row that stores who asked writes `transaction.Actor.RecordedValue`. Nothing a caller supplies is used (AUD-1.3 D2). Refusals and denials are W3 rows of the same principal, or `anonymous` when the request maps to no session (AUD-1.3 D10).

**Target.** One primary target from the closed vocabulary (§11.1): the configuration entity changed. A deleted target keeps its id and its name snapshot. Secret-store events target the owning entity (D-1).

**Never recorded** (gate §12.3, D-1): secret values or references, credential material, connection strings, tokens, keys, values of `IsSensitive` settings, email addresses (except the subject address of an email-change event), search or filter text. Settings and fields are named, never their sensitive values. Free text only in declared, bounded `operator_text` fields.

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
| The integration-level attention-routing write routes (`PUT` and `DELETE /api/integrations/{id}/attention-routing`) are authorized by integration read only; the routing service checks nothing itself | Security defect, unowned (candidate IAM-10 or an owner-directed fix). AUD-1.4 will attribute these writes; that does not fix their authorization |
| A worker with no configured runtime id applies the oldest queued shutdown request of any runtime | Runtime package, unowned |
| The worker's `applied` shutdown transition has no audit row (gate §11.6 cites it only as an example of `confirmed_result`) | Unassigned; for the AUD-1.6 writer inventory |
| A diagnostic pipeline test that dispatches immediately processes the whole pending outbox batch | Existing defect, unowned |
| `SqlSecretStore` exception messages embed the secret id (`Secret {id} not found.`, `Version … not found for secret {id}.`), which is the reference its owner holds. Pre-existing, found in the slice 1 review. The Entra flows cannot reach them in practice (the id comes from the registration's own row) and no recorder row carries a message | RCV-1 or optional cleanup; unchanged here because no AUD-1.4 invariant covers exception text and no path converted by slice 1 surfaces it |
| `SqlSecretResolver` echoes an invalid ref value in its exception text; the `PlatformSettingUpdatePayload` record's generated `ToString` would print its secret value; the SMTP test recipient is logged | RCV-1 or optional cleanup; guarded here only where a slice touches it |
| `docs/04-api/contracts.md` calls the shutdown audit action `runtime.shutdown.request`; the catalog name is `governance.runtime.shutdown.request.created` | Corrected in slice 7 |

IAM-9 stays closed. The IAM-10, IAM-11, OPS-1 and RCV-1 findings stay open. RC1 is not complete.
