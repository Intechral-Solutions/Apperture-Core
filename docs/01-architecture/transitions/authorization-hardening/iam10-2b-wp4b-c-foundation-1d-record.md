# IAM-10.2b WP4B-C Foundation 1D: persistence and catalog foundation for non-destructive MFA replacement

| | |
|---|---|
| Status | `validated` (local, unreviewed, not integrated) |
| Owner / implementer | Claude Sonnet 5.5 (implementation session) |
| Scope source | Foundation 1D brief; owner-approved replacement guidance (non-destructive default, explicit destructive opt-in, no silent fallback); [decision record](iam10-2b-wp4b-decision-record.md) |

## 1. Scope and acceptance criteria

- **In scope:** candidate storage; candidate identity, ownership, expiry, versioning; atomic verify and activation conditions; protection against stale or superseded candidates; conditional enrollment replacing the unconditional mark-enrolled; migration and model snapshot; audit action catalog declarations for the refusal action and `auth.mfa.replaced`.
- **Out of scope / not changed:** the replacement service workflow, the CLI, the coordinator, attestation publisher and store, viability guard, `EmergencyAccessService`. No audit event is emitted by this package. One two-line edit in `NativeMfaService.CompleteEnrollmentAsync` was unavoidable because `MarkEnrolledAsync` changed signature (see 3). Not merged with the foundation branch; the IAM branch is not advanced; nothing is pushed.
- **Acceptance criteria:**
  1. A candidate has an owner, an id, a version, a status, a database-clock expiry and is bound to the credential it was staged against (`MfaReplacementCandidateSqlServerTests`).
  2. Verification and activation each apply only when every condition holds in one conditional statement; every refusal is reported and writes nothing (every-refusal theory, 9 reasons x 2 modes).
  3. A failed, expired, superseded, consumed, version-mismatched or foreign candidate never changes the active authenticator or recovery codes (same theory).
  4. Concurrent stage, verify, switch, reset and code regeneration serialise on the credential row with no deadlock and a defined end state (8 parked cases, `MfaReplacementCandidateConcurrencySqlServerTests`).
     > **Correction (2026-11-09, independent review of `072b7e94`, finding R-2).** This criterion was **not met** as stated, and the cases cited could not have shown it. They ran on retrying contexts, which retry a deadlock victim silently. The code regeneration, code removal and discard deleted candidate rows without first taking the credential row. The reviewer's non-retrying probes demonstrated SQL error 1205 in both isolation modes: staging against those writers for the same user, and staging against another user's removal (the only index on the owner was filtered, so the deletes scanned). Every victim rolled back cleanly, and no partial transition occurred. The defect is corrected and pinned by non-retrying cases in the [remediation record](iam10-2b-wp4b-c-persistence-remediation-record.md) (finding R-1). The text above is kept as it was written.
  5. Enrollment completes only for the pending secret that was verified (`ConditionalMfaEnrollmentSqlServerTests`, with a legacy-unconditional control).
  6. The migration upgrades an existing installation, backfills identities, rolls back and re-applies (`MfaReplacementCandidateMigrationSqlServerTests`); the snapshot matches the model.
  7. The two new actions are catalogued with typed producers and schemas (`MfaReplacementAuditContractTests`, catalog tests).

## 2. Identity of what was validated

| | |
|---|---|
| Base commit | `c3e9813a09748234228e3227b22cb906e8aedfad` (IAM branch, `src` tree `e98f43fa`) |
| Branch / worktree | `feat/iam10-2b-wp4bc-mfa-persistence` / `F:\Code\Apperture\wt-iam10-2b-mfa-persistence` |
| **Validated HEAD** | `7a74fe0755dbfbc19bc33965614819218c108fc9` (the record commit that follows touches docs only; its hash is in the handback) |
| Tree hash of the validated HEAD | `f830fdfe4ced868f0d090c780c8606fbb14676c5` |
| Working-tree state when validated | clean |
| Commits since base | `c8cf70d5` implementation; `9fe95dd9` seed-helper fix; `7a74fe07` test pins for two escaped mutants |

`src`, `tests/Apperture.UnitTests` and `tests/Apperture.ArchTests` trees are identical between `9fe95dd9` and `7a74fe07` (`6aac67e93d`, `00fb975d9c`, `4cf25ca6d4`); only `tests/Apperture.IntegrationTests` changed, so unit and arch results from `9fe95dd9` apply to `7a74fe07`.

## 3. Changes

28 files, +2471/-29 against the base (`git diff --stat c3e9813a..7a74fe07`).

**Schema.** `UserTotpCredentials.CredentialId nvarchar(64) NOT NULL`: the identity of the stored secret. A pending write and a switch assign a new value; completing enrollment does not. New table `UserTotpReplacementCandidates` (CandidateId, UserId, ProtectedSecret, ProtectorKeyId, StagedAgainstCredentialId, Status, Version, CreatedUtc, ExpiresAtUtc, VerifiedUtc, MatchedStep), cascade FK to Users, filtered unique index `UX_UserTotpReplacementCandidates_Live` on `UserId` where `[Status] < 2` (one live candidate per user, history allowed). Migration `20261108120000_Iam102b_MfaReplacementCandidates` (hand-written: nullable add, GUID backfill, alter to NOT NULL; `Down` restores the old schema).

**Contracts.**
- `INativeMfaReplacementCandidateRepository` (new): `StageAsync`, `GetAsync`, `MarkVerifiedAsync`, `SwitchAsync`, `DiscardAsync`; outcome enum `Applied, NoActiveEnrollment, NotFound, Superseded, Consumed, Expired, VersionMismatch, AlreadyVerified, NotVerified, ActiveEnrollmentChanged`.
- `INativeTotpCredentialRepository.MarkEnrolledAsync(UserId, string expectedCredentialId, long lastUsedStep, DateTimeOffset, ct)` now returns `ValueTask<bool>`; `NativeTotpStoredCredential` gained `CredentialId`. The method name is unchanged so the audit writer-coverage guard regex still matches.
- `DeleteAsync`, `ReplaceAllAsync`, `DeleteAllAsync` also remove staged candidates (documented on the interface).
- Audit: `AuditActionNames.AdministratorViabilityBlocked = "emergency.auth.blocked.administrator_viability"` (Attempt/Attempt, schema userId, operation enum incl. `mfa.replace`, code, required configurationEvidence) and `MfaReplaced = "auth.mfa.replaced"` (Identity/Elevated/User, RequiredMutation, `UserWithConfigurationEvidence`); typed producers `MfaReplaced(UserId, ConfigurationEvidence?)` and `AdministratorViabilityBlocked(...)` exist but are **not called**.
- DI: repository registered in the host composition and in `AddAppertureEmergencyOperatorServices`.

**Lifecycle (Staged 0 -> Verified 1 -> Consumed 3; Superseded 2).**
- *Stage:* lock the active credential row (`UPDLOCK, HOLDLOCK`), purge history (status >= 2), supersede the live candidate (version + 1, secret wiped), insert the new one with `SYSUTCDATETIME()` and `DATEADD(SECOND, lifetime)`. Lifetime 1 s to 24 h. No enrolled authenticator: nothing is written.
- *Verify:* one conditional UPDATE: status Staged, version equal, not expired by the database clock, owner equal, staged-against credential equal to the credential now active. Rows affected decides; only after a failure is a diagnosis read taken under locks. A failure never writes.
- *Switch:* one UPDATE joining credential and candidate (candidate row locked) with every condition (verified, version, expiry, staged-against equals current credential, same user). On one row: matched step becomes the last used step, secret replaced, new `CredentialId`, candidate consumed and wiped, recovery codes replaced in the same transaction (joins the caller's transaction, otherwise uses the retrying execution strategy). Otherwise the diagnosis precedence is NotFound, Superseded, Consumed, Expired, VersionMismatch, NotVerified/AlreadyVerified, ActiveEnrollmentChanged.
- *Lock order everywhere:* credential row, candidate rows, recovery-code rows. Reset and code regeneration remove staged candidates.
  - **Correction (2026-11-09).** This order was not true of the candidate removals in code regeneration, code removal and discard. They took no credential row, which caused finding R-1. Since the remediation it holds for every candidate writer.
- *Enrollment:* `MarkEnrolledAsync` is `ExecuteUpdate ... WHERE NOT IsEnrolled AND CredentialId = expected`; `NativeMfaService` throws if it returns false (the pending secret was replaced while the code was checked).

**Minimal caller edit.** `NativeMfaService.cs` passes the verified credential's id and checks the result; `NativeMfaServiceTests` fake and one test updated. Raw SQL seed helpers in `RoleAuthorityWorld` and `AdministratorViabilitySqlServerTests` supply the new NOT NULL column.

**Contracts and protections touched.** Audit action catalog count and catalog tests updated. No allow-list, honesty list, pin or CI file changed.

## 4. Validation

**Environment.** Windows 11, PowerShell 7, .NET SDK 10, SQL Server container at `127.0.0.1,1433`, connection string only in the process environment (never on a command line or in a log: checked). Build protocol: `--no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers` with a fresh repo-local `--artifacts-path`.

| Run | Executed | Passed | Failed | Skipped | Evidence |
|---|---|---|---|---|---|
| UnitTests at `9fe95dd9` (tree identical at HEAD) | 5996 | 5995 | 0 | 1 (opt-in diagnostic) | `artifacts/mfa-logs/final-unit/unit.trx` |
| ArchTests at `9fe95dd9` (tree identical at HEAD) | 38 | 38 | 0 | 0 | `artifacts/mfa-logs/final-arch/arch.trx` |
| Focused SQL set at `9fe95dd9` | 942 | 942 | 0 | 0 | `trx-focused2/focused2.trx` |
| **Focused SQL set at `7a74fe07`** (fresh build, `artifacts/mfa-final2`) | 944 | 944 | 0 | 0 | `trx-focused3/focused3.trx`, 25 m 40 s |
| Honesty script at `7a74fe07` (`-Release -NoBuild`) | 3566 discovered, 3566 executed | 226 | 3340 (all as expected without SQL) | 0 | `honesty-final/sql-honesty-report.json`, exit 0, PASS |

The focused set is the new classes plus every class that touches the affected tables or the audit catalog (PendingMfa, AuthenticationAudit, IdentityGovernanceAudit, AdministratorViability*, EntraPrimaryPolicyViability, EntraRegistrationViabilityWp3, RecoveryAccessDesignation, ManagementHostAttestation/StartupOrder, EntraRegistrationDiagnosticsMigration, RoleAuthorizationDataStep, AdministratorViabilityAttempt, HostConfigurationAttestation); the filter is `artifacts/mfa-focused-filter.txt`.

**Both isolation modes** (read-committed-snapshot, driven by `AuditIsolationCatalogs.For`): `rcsi: False` 452 passed / 0 failed; `rcsi: True` 452 passed / 0 failed; non-parameterised 40 passed. The first focused run (`9fe95dd9`) failed 508 tests because raw-SQL seeds lacked the NOT NULL column; fixed in `9fe95dd9`, rerun green, recorded here so the failure is not hidden.

**Migration and snapshot.** `HasPendingModelChanges` consistency test passes (in the focused set). Migration tests: existing credentials keep every value and each receives its own 32-character identity (pending ones included); column NOT NULL, `nvarchar(64)`; filtered unique index refuses a second live candidate (error 2601) and allows history; rollback removes the column and the table with credentials intact, and re-applies.

## 5. Mutation and regression-catch evidence

Run in a clone at `7a74fe07` (`artifacts/mfa-mut/clone`), baseline green (98/98) on the unedited tree, each edit restored and verified against the committed blob hash. Filter: the four candidate/enrollment SQL classes. Evidence: `artifacts/mfa-logs/mutation-final/` (`results.json`, per-mutant build and test logs).

| ID | Protected property | Outcome | Failing tests |
|---|---|---|---|
| M01 | enrollment conditional on the verified credential id | Caught | 6 |
| M02 | enrollment requires the credential to be pending | Caught | 2 |
| M03 | verify honours database-time expiry | Caught | 4 |
| M04 | switch honours database-time expiry | Caught | 4 |
| M05 | verify checks the version | Caught | 2 |
| M06 | switch checks the version | Caught | 4 |
| M07 | switch needs a verified candidate | Caught | 2 |
| M08 | switch needs the staged-against credential | Caught | 2 |
| M09 | credential row read under lock | Caught | 12 |
| M10 | staging supersedes the live candidate | Caught | 14 |
| M11 | switch records the matched step as last used | Caught (after fix) | 2 |
| M12 | switch replaces the old recovery codes | Caught | 8 |
| M13 | verify checks the candidate owner | Caught (after fix) | 2 |
| M14 | switch consumes the candidate | Caught | 10 |
| M15 | code regeneration removes the staged candidate | Caught | 6 |
| M16 | recovery-code removal hook removes the staged candidate | **ESCAPED, equivalent** | 0 |
| M17 | reset removes the staged candidate (both sites) | Caught | 4 |
| M18 | pending write assigns a new credential id | Caught | 6 |
| M19 | candidate lifetime is the requested one | Caught | 2 |
| M20 | switch assigns a new credential id | Caught | 6 |
| M21 | switch locks the candidate row | Caught | 1 |

- **Fixes after the first round (at `9fe95dd9`, M11 and M13 escaped).** M11: enrollment stores the current step as last used, so a switch that kept the old value was indistinguishable; the switch test now verifies at `CurrentStep() + 5` and asserts the stored last-used step equals it. M13: credential ids are unique, so the staged-against check masked the missing owner condition; a new test gives two users the same credential id and asserts the second user cannot verify the first user's candidate. Both now fail on the old behaviour (rows above). All 21 mutants were rerun at the final commit.
- **M16 is declared equivalent, not caught.** The hook it removes is redundant with the credential delete that the same operation performs; M17 removes both sites and is caught. It is left unpinned on purpose rather than adding a test that asserts an implementation detail.

## 6. Evidence and checksums

Local only, in `F:\Code\Apperture\wt-iam10-2b-mfa-persistence\artifacts\mfa-logs\` (git-ignored): `trx-focused3/focused3.trx` (`cee56d1e7ad7a051e1f13175616fb6349cd9c0ada6c8aa10734261b9b3c77174`), `honesty-final/sql-honesty-report.json` (`84fe96dcc30221b7701b1c9c1c39c6b5c0393de6fce02d6c7a90accc1f14fd11`), `mutation-final/results.json` (`8f7e8c04a35a05b3d0be37942b103c47b773f6f193d99214c850dc350dd3893b`), `trx-focused2/focused2.trx` (`aea2d244edfc4d340a98e30b99e4419394dcf1db9100fd9130b5e56687d3e9f6`), plus unit and arch TRX, logs and the earlier mutation round. **Not archived to another drive**: no archive was requested; the owner decides.

## 7. Independent review

Not yet reviewed (as written).

> **Update (2026-11-09).** Foundation 1D was reviewed independently as part of the persistence integration at `072b7e94` (2026-11-08) and was **NOT ACCEPTED**.
> - **B-1 (blocking).** `UpdateLastUsedAsync` wrote the TOTP step unconditionally. A real-SQL interleaving showed a sign-in with the retired secret succeeding after a switch, lowering the new credential's step, and the candidate's verification code being accepted again. Two concurrent sign-ins with one code both succeeded.
> - **R-1.** Same-user and cross-user deadlocks (criterion 4 above).
> - **R-2.** The evidence and records needed correcting.
> - **R-3.** The rollback statement needed correcting.
>
> All four are addressed in the [remediation record](iam10-2b-wp4b-c-persistence-remediation-record.md). Closure verification is the reviewer's.

## 8. Remaining risks and integration notes

- **Reset overlapping a switch.** The reset deletes the tracked credential with its RowVersion; if a switch commits first the delete throws `DbUpdateConcurrencyException`. The test asserts that conflict and that a retried reset succeeds. The service/CLI should retry or report it.
- **`ActiveEnrollmentChanged` is the single refusal** for "the authenticator changed after staging" (reset, regeneration of identity by a re-enrollment). The service decides how to present it.
- **Candidate verification does not prove possession.** `MarkVerifiedAsync` records a step the service has already checked; the service must verify the code against the candidate secret and apply replay protection against the active credential's last used step before calling it.
- **Recovery codes are mandatory for a switch** (at least one, all owned by the user); the service must generate them and show them once after `Applied`.
- **No audit emission, no attestation or viability check here.** The coordinator and viability guard integrate at the service layer; the switch can run inside the caller's audited unit and commits or rolls back with it (tested).
- **Merge conflict surface with the foundation branch:** `NativeMfaService.cs`, `INativeMfaRepositories.cs`, `AuditActionCatalogEntries*.cs`, `AuditEvents.*.cs`, `ServiceCollectionExtensions.Governance.cs` and the model snapshot (add-only blocks). A later integration must keep the migration timestamp ordering (`20261108120000` sorts after `20261029120000`).
- **Known limits of the evidence:** Linux not run; the focused SQL set is not the whole IntegrationTests suite (the honesty script runs the whole assembly without SQL only); the full SQL suite (about 2 h) was not run for this package.
