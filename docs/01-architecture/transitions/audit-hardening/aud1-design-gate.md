# AUD-1: Audit hardening design gate

Date: 2026-10-06 (final; revision 3)

Status: **AUD-1.0 design gate. Approved by the product owner on 2026-10-06, with conditions.**
- The conditions are four corrections (AC-1 to AC-4), the T-B database-protection ruling (OR-13) and the benchmark ruling (OR-14). §3.2 records all of them, and this revision incorporates them.
- Every design section is binding on AUD-1.1 onward.
- **AUD-1.1 is next and has not started.** No AUD-1 implementation has started, and no implementation branch exists. Each package starts only on an explicit owner instruction (program gate §9).

Branch: `aud-1.0-design-gate`, from `origin/master` at `b065765` (merge of PR #13).

Governing contract: [rc1-program-gate.md](../../rc1-program-gate.md):
- RC1 requirement 5 (§2)
- D-02, D-04, D-05, D-13 (§3)
- §5 sequence and hard dependencies
- §6.5 and §8

Inherited gates:
- [IAM-9 design gate](../identity-foundation/iam9-design-gate.md): R14 as amended by OR-2, R17, R18, §6, §9.5, §16, §19, §20.2, §23, §26, §29.3.
- [LST-1 design gate](../list-query-foundation/lst1-design-gate.md): §9.2, §13, §18, §28, §30.1.

Closure input: [IAM-9 closure audit](../../../audits/iam9-identity-foundation-closure.md) findings F-3, F-4, F-8, N-8.

Evidence: the [Part 1 audit](../../../audits/rc1-architecture-audit-and-program-plan.md) (§H, §I, §K) and its [supplemental addendum](../../../audits/rc1-supplemental-reconciliation-addendum.md) (§C.4). Both are working records. Where they differ, the program gate, the approved design gates and the owner rulings in §3 govern.

Labels used throughout:

- **Code fact.** Verified in the repository at `b065765`.
- **Approved (inherited).** Binding, from the program gate or an earlier approved design gate. The source is cited.
- **Owner ruling (OR-n) or approval condition (AC-n).** A product-owner ruling of 2026-10-06 (§3). Binding.
- **Design.** The contract this gate specifies. Binding on implementation since approval.
- **Implementation-stage (I-n).** A question the implementing package answers, with stated validation. Not a product decision (§25).

Path abbreviations, as in the LST-1 gate:

| Abbreviation | Path |
|---|---|
| `SEC` | `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/` |
| `API` | `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/` |
| `HOST` | `src/Apperture.ControlPlane/Apperture.Management.Host/` |
| `UI` | `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/` |
| `US` | `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/` |
| `MC` | `src/Apperture.ControlPlane/Apperture.Management.Client/` |
| `PERS` | `src/Apperture.Persistence/Apperture.Persistence.SqlServer/` |
| `APP` | `src/Apperture.Core/Apperture.Core.Application/` |
| `RT` | `src/Apperture.RuntimePlane/` |

---

## 1. Status

| Item | State |
|---|---|
| RC1 program contract | Approved (2026-10-03). Governs |
| IAM-9 | Complete, closure accepted. OR-2 amends R14 additively. **This does not reopen IAM-9 or its closure** |
| LST-1.0 | Approved with amendments. **LST-1.1 has not started.** That blocks AUD-1.5b only (§26) |
| AUD-1.0 | **Approved 2026-10-06 with conditions (§3.2)** |
| AUD-1.1 | **Implemented on branch `aud-1.1-audit-foundation`, awaiting review and merge** ([implementation record](aud1-1-foundation-record.md)). Status at approval: next, not started |
| AUD-1.2 to AUD-1.6, AUD-1.5b | Not started (§22) |
| Product code, migrations, tests or CI changed by AUD-1.0 | None |

This gate does not close, accept or reassign any finding owned by IAM-10, IAM-11 or RCV-1 (§28). No risk acceptance is granted. RC1 is not complete.

## 2. Baseline and method

- **Revision 1** traced every production writer of `AuditLogs` and `SecretAudits`, plus the schema, read path, UI surfaces, startup data-step template and tests.
- **Revision 2** applied OR-1 to OR-12. Its additional source checks were:
  - the execution strategy: `PERS/ServiceCollectionExtensions.cs:36,62`;
  - the per-request user read: `SEC/SqlPrincipalToUserResolver.cs:43`;
  - the Entra principal fields;
  - the startup migration wiring: `HOST/Program.cs:44-46`;
  - every outcome literal;
  - the history of the writers that record the wrong actor.
- **Revision 3** applies AC-1 to AC-4, OR-13 and OR-14. It added two source checks:
  - the dispatcher's state update: `RT/Apperture.RuntimePlane.Dispatch.SqlServer/ScheduleDispatchRunner.cs:336-342` and `PERS/Entities/ScheduleStateEntity.cs`;
  - the commit that added the Epic 37 migration: `baa2f763`, 2026-03-22.

Line numbers are at `b065765`.

## 3. Owner rulings and approval

### 3.1 Rulings of 2026-10-06 (OR-1 to OR-12)

| Id | Ruling | Implemented in |
|---|---|---|
| OR-1 | Every required administrative or operator database mutation persists atomically with its audit row, **including schedule and integration deletes**. This supersedes the LFC-1 and CTX-3B delete exceptions. **Scope:** every required mutation writer is converted, not only new coverage or touched files. Mutations, security events, attempts and telemetry are classified explicitly. **Guarantee:** stated precisely as confirmed commit, confirmed rollback or unknown outcome. **Mechanics:** a bounded retry and commit-verification contract, with no platform-wide idempotency framework; external effects have their own semantics; the `Stage` unit of work is defined precisely | §11.6, §13, §14, §21, §24 |
| OR-2 | An `external` actor kind for authenticated external identities with no platform user. An Entra identity is provider, tenant id and object id. Email and UPN are not keys. Actor and target stay distinct. Unverified claims never become external attribution. **IAM-9 R14 is amended**; the IAM-9 closure stays accepted | §10.4, §10.5, §10.7, §15.2, §27 |
| OR-3 | Attribution provenance is part of the read contract. Original evidence is preserved, and no corrected actor is invented. Defects and uncertainty are visible to readers. Rules are bounded and evidence-supported; where a row cannot be shown to be wrong, it is "potentially affected". A merge timestamp is not a deployment cutoff. Revision 1's Q9 is resolved. The `unknown` protection stays, and it is distinct from any claim that every `unknown` was a placeholder | §10.6, §15.3, §17.1 |
| OR-4 | Event-time username evidence is retained alongside the current-username presentation. The snapshot source is defined, and a stale session claim is never treated as authoritative. No session redesign. No invented historical usernames | §10.2, §10.3, §15.2 |
| OR-5 | Category is the subject area. Visibility is separate from severity and is outcome-aware. The policy applies consistently to writes, history, filters, indexes and the dashboard. Hiding rows is not reducing stored volume | §11.3, §11.4, §15.4, §16 |
| OR-6 | Legacy outcomes are normalized only where equivalence is established. Raw values are kept. Unknown outcomes are exposed, with defined filter behaviour | §11.5 |
| OR-7 | Action-specific permitted fields and controlled producers. The limits of validation are stated, and sensitive exclusions are kept. The parsing exceptions are named. An explicit overflow policy applies | §12 |
| OR-8 | Column-level deny is not append-only enforcement. Evaluate separating privileged migration access from runtime access, covering every evidence column. Keep permission enforcement distinct from tamper-evidence, and ordinary access distinct from owner access. A mandatory credential overhaul needs a separate owner choice | §19 |
| OR-9 | Keep batching, committed progress, restart convergence, preview and readiness. Add multi-host coordination and conditional updates. Classification uses the evidence available when it runs. "Unresolved is final" binds automatic reruns only. Keep the million-row measurement. No background classification without approval. A missing release tag is only an observation | §15 |
| OR-10 | No automatic purge in RC1, with storage growth and backup implications documented. Configurable retention is deferred | §20 |
| OR-11 | The `UserRef` split and AUD-1.5b are approved. AUD-1.5b offers actor filtering from a row without LST-1.3. Directory selection requires both the selector and `user.read`. IAM-10 design planning may proceed independently; IAM-10 implementation starts when the AUD-1 contracts it needs are delivered and verified | §18, §22, §26, §27 |
| OR-12 | Coverage enforcement is tied to the writer inventory and transaction tests, and each mechanism states what it proves. A staged rollout defines what unconverted writers emit, how their rows are classified and rendered, the guarantees during each package, and when conversion is complete | §14, §21 |

### 3.2 Approval and conditions (2026-10-06)

The product owner approved AUD-1.0 subject to the conditions below. Revision 3 incorporates each one.

| Id | Condition or ruling | Incorporated in |
|---|---|---|
| AC-1 | **Close the commit-verification retry gap.** The verification event id is stable across retries. Verification uses a fresh authoritative connection. The event is checked before validation reruns after an uncertain commit. Apparent no-op, not-found and refusal results are reconciled. Only an `EventId` duplicate is a candidate for prior success, and the matching event is verified first. The identifying event of a multi-row transaction is defined. A single negative lookup does not prove rollback. `outcome_unknown` is preserved. This is server-side handling only, not HTTP idempotency | §13.5, §13.6; A1.1-10, A1.1-14 to A1.1-18 |
| AC-2 | **Freeze completed classification as a whole.** Recorder rows are complete and immutable from insert, including intentional nulls. An unclassified row gets one atomic classification transition, after which the whole set is frozen. The completion state and conditional update are explicit. `Basis` is an evidence column. T-B runtime credentials have no classification-update authority | §11.8, §15.1, §15.6, §19.1, §19.2; A1.1-13, A1.1-19 |
| AC-3 | **Keep historical blocked events visible.** No suppression of repeated historical rows. Transition-only writing is prospective (AUD-1.5), with defined state tracking and restart behaviour. Visibility is not volume | §11.4, §15.4, §22 AUD-1.5; A1.5-4, A1.5-8, A1.5-9 |
| AC-4 | **Resolve legacy outcome provenance.** Writer-recorded and migration-derived outcomes are distinguished by available evidence; a matching string is not proof. Where provenance is indistinguishable, a conservative action-specific mapping or `unknown` applies. Raw values are untouched. Tests cover the ambiguous cases | §11.5, §12.6 P-4; A1.1-20 |
| OR-13 | **T-B selected.** The integrity guards ship with AUD-1.1. The optional privileged migration and data-step credential, and the restricted runtime access, ship with AUD-1.6. Default installations stay on T-A, with its limits stated. If a configured privileged phase fails, startup stops, with no fallback. Privileged credentials are used only for startup migrations and data steps. The threat boundary is stated accurately. Grants and completed-record protections are documented and tested | §19.3 to §19.5; AUD-1.1, AUD-1.6 |
| OR-14 | **Five minutes per million rows is a provisional reference-machine benchmark.** Hardware, workload, duration, readiness and service-start behaviour are recorded. Exceeding it requires investigation and a concrete recommendation. It does not automatically require background classification or invalidate the design. Background classification needs approval | §15.6; A1.1-12 |

Revision 1's open questions are resolved as follows:

| Revision 1 question | Resolved by |
|---|---|
| Q1, Q2 | OR-1 |
| Q3 | OR-5 |
| Q4 | OR-4 |
| Q5 | OR-2, replacing the revision 1 recommendation of `anonymous` |
| Q6 | OR-8 and OR-13 |
| Q7 | OR-10 |
| Q8 | OR-11 |
| Q9 | OR-3 |

Revision 2's open question R-1 is resolved by OR-13.

**Approval check.** None of the conditions conflicts with an approved requirement or needs a new product decision:
- AC-1 to AC-4 tighten guarantees that this gate states.
- OR-13 selects an option this gate presented.
- OR-14 relaxes a threshold this gate proposed.

## 4. Approved requirements AUD-1 inherits

| Id | Requirement | Source | Package, criterion (§22) |
|---|---|---|---|
| T-01 | Audit attributes every administrative and operator action to a stable user identity | Program gate §2 req. 5; D-02 | 1.1 to 1.4; 1.6 (A1.6-1, A1.6-4) |
| T-02 | Queryable target | §2 req. 5; D-13 | 1.1 (A1.1-3), 1.5 (A1.5-3) |
| T-03 | Routine noise separated from significant events | §2 req. 5; OR-5 | 1.1 (A1.1-4), 1.5 (A1.5-4, A1.5-5) |
| T-04 | Stable actor, target, category and severity vocabulary, structured details | D-13 | 1.1 |
| T-05 | Decommission does not destroy attribution | D-04, D-05 | 1.1 (A1.1-2) |
| T-06 | A reserved data-access category | §6.5 | 1.1 (A1.1-4) |
| T-07 | No searched value or digest of one in audit details | §6.5; IAM-9 §16 | 1.1 (A1.1-6), 1.6 (A1.6-2) |
| T-08 | Correct the keyset cursor | §6.5; LST-1 §18 | 1.5 (A1.5-1) |
| T-09 | Optional per-row route | §6.5 | 1.5b (A1.5b-4) |
| T-10 | Filter state follows the LST-1 URL convention | §6.5; LST-1 §13 | 1.5b (A1.5b-2, A1.5b-3) |
| T-11 | Actor kinds `user`, `system`, `anonymous`, `worker`, `unresolved` plus the original string; embeds `UserReferenceDto`. **Amended by OR-2: adds `external`** | IAM-9 R14 | 1.1 (A1.1-1), 1.5 (A1.5-6) |
| T-12 | Audit actor columns are backfilled with the IAM-9.2a rules. `Actor` is never rewritten | IAM-9 R17; §6 | 1.1 (A1.1-7, A1.1-8) |
| T-13 | Unresolved values never map to a user. Literals render as non-human. Unresolved renders as "Unrecognized actor" | IAM-9 R18, §15 | 1.1 (A1.1-8), 1.5b (A1.5b-1) |
| T-14 | Rule order shared through `HistoricalActorClassifier` | IAM-9 §17 | 1.1 (A1.1-7) |
| T-15 | Historical displays show the current username | IAM-9 §9.5 | 1.5 (A1.5-6), 1.5b (A1.5b-1) |
| T-16 | AUD-1.1 is in the same release as IAM-9 | IAM-9 §17, §26; F-4 | §26 P-4 |
| T-17 | Structured previous and new username, without parsing the free text | IAM-9 §29.3; program gate §8; F-3 | 1.1 (A1.1-9) |
| T-18 | Per-page `UserRef` split with LST-1.3 | Program gate §8; F-8; OR-11 | §18 |
| T-19 | `IUserReferenceResolver` consumers | N-8 | 1.5 (A1.5-6) |
| T-20 | AUD-1 owns the Audit page's adoption of the LST-1 primitives, the `(At, Id)` cursor, malformed-token behaviour and the audit SQL tests | LST-1 §18.3, §18.4, §30.1 | 1.5, 1.5b |
| T-21 | `cursor_invalid` for malformed and legacy cursors; opaque, versioned cursors | LST-1 §9.2, §18.3 | 1.5 (A1.5-2) |
| T-22 | IAM-10 needs AUD-1's recorder for grant diffs. IAM-11 needs IAM-9, AUD-1 and IAM-10 | Program gate §5 | §22, §26 |
| T-23 | No secrets. Parameter names but not values | Part 1 §K, §I; D-24 | 1.1 (A1.1-6), 1.3 |
| T-24 | Search terms never audited | IAM-9 §16, §21 | 1.1 (A1.1-6) |
| T-25 | No internal development metadata in runtime-visible content | `AGENTS.md` non-negotiable 6 | 1.1, 1.5b |

**Not inherited.** Part 1's column names, recorder shape, write-mode rule, noise rule and package contents are working-record proposals. This gate restates each on its merits under OR-1 to OR-14 and AC-1 to AC-4.

## 5. Current state (code facts)

### 5.1 Schema

`AuditLogs` (`PERS/Entities/AuditLogEntity.cs`; configuration `PERS/AppertureDbContext.cs:307-337`):

- **Base columns:** `Id` (bigint identity), `At` (caller-supplied), `Actor` (nvarchar 256), `Action` (nvarchar 128), `Details` (nvarchar max).
- **Epic 37** (`20260322240000_AddAuditFirstClassFields`, commit `baa2f763`) added `Outcome`, `ModuleId`, `ScheduleId` and `RunId`. It backfilled them by **parsing `Details` text** in SQL (`:82-136`), and it changed the writers to stop merging the outcome into `Details`.
- **Later additions:** CTX-2 added `ResourceContextId`; CTX-3B added `CorrelationId`.
- **Indexes:** `(At desc, Id desc)` at `:328`, plus filtered indexes on `Outcome`, `ModuleId`, `ScheduleId`, `RunId` and `ResourceContextId`.
- **Absent:** no actor-kind, actor-user, target, category, severity, visibility, basis or event-id column.
- **`EFMigrationsHistory`** records no application time, so no database records when Epic 37's backfill ran.

`SecretAudits` is written by `PERS/SqlSecretStore.cs` and read by nothing. There is no permission script, trigger or `DENY`. `docs/03-operations/backups-and-retention.md` is a placeholder.

### 5.2 Write path

- **The writer saves on its own.** `SqlAuditService.AppendAsync` (`PERS/SqlAuditService.cs:20-48`) adds the row and calls `SaveChangesAsync` itself. Its context is scoped and shared with the caller's repositories.
- **Retries can repeat a transaction.** `EnableRetryOnFailure` is active in production (`PERS/ServiceCollectionExtensions.cs:36,62`). Explicit transactions run inside `strategy.ExecuteAsync`, and none verifies a commit. A retried commit after a lost acknowledgement can therefore repeat a whole transaction.
- **Resource is not stored usably.** `Resource` is serialized as `ResourceDescriptor { ... }` text (`:22-24`).
- **`AuditRecordId` is discarded.** It is generated at about 70 sites.

There are four write modes:

| Mode | Behaviour | Where |
|---|---|---|
| (a) | Inside the mutation's transaction | Connection create; ownership moves; Application archive; `UsernameBackfill`; runtime processors |
| (b) | After commit, with the exception propagating | About 24 files, including all of `SEC/UserManagementService.cs` (rename at `:504-509`), account services, connection update, retire and verify, integration and schedule delete, settings, `RuntimeControlService`, provisioning, access requests, module registry, secret store, `AuthController` and `BootstrapController` successes, `EnrollController`, `ConfigEntraTenantsController`, Entra callback |
| (c) | Best effort | Recovery Access, MFA, Entra step-up, association and primary policy, bulk retry, attention, workflow retry, recovery, start-blocked, scheduler |
| (d) | Before the mutation | Break-glass marker (`SEC/UserManagementService.cs:911-926`), recovery retry-requested, bulk retry-requested, legacy schedule warning |

**Example consequence of mode (b).** A rename commits and its audit fails, so the API returns 500. A retry with the same name returns without auditing, so the rename stays permanently unattributed.

**Superseded by OR-1.** LFC-1 §5 step 6 and the CTX-3B integration-delete write mode chose mode (b) on purpose. Neither delete catches the audit exception.

### 5.3 Actor values

- **Literals are hard-coded.** No writer uses `ActorLiterals`.
- **A placeholder looks like a username.** `"unknown"` (`SEC/BulkOperationItemRetryService.cs:155`) is not a reserved username.
- **The session username can be stale.** The resolver reads the user row on each request (`SEC/SqlPrincipalToUserResolver.cs:43`) but returns only the id. The session's username claim is not refreshed on rename.
- **Entra principals carry tenant and object id**, yet two writers record only the object id as actor (`SEC/EntraAccessRequestService.cs:247-257`, `SEC/EntraIdentityAssociationService.cs:366`).
- **Wrong or missing actors:** listed in §15.3.

### 5.4 Read path and presentation

These are unchanged from revision 1:

- **Cursor:** `Id`-only, and malformed tokens are silently ignored.
- **Category:** derived from the action prefix.
- **Audit page:** ignores `actionPrefix`, its load-more window moves, and a load-more error replaces the table.
- **Integration activity panel:** queries by `moduleId`.
- **Dashboard:** counts every row.
- **Authorization:** `audit.read` at instance scope.
- **`UserRef`:** has no non-human mode and is pinned to three Governance pages.
- **`IUserReferenceResolver`:** no consumer.
- **Tests:** no cursor test; AU8 pins the schema; `AuditCorrelationIdSqlServerTests` soft-skips (RCV-1 N-1).

### 5.5 Database access in the supported topology

The Management Host registers one connection from the bootstrap profile (`HOST/Program.cs:44-45`) and applies migrations at every start (`:46`). So the runtime login holds schema-change rights.

### 5.6 Schedule state

`ScheduleStates` holds `LastOutcome`, `LastOutcomeReason`, `LastAttemptedUtc`, `LastSuccessfulDispatchUtc` and `NextDueUtc` (`PERS/Entities/ScheduleStateEntity.cs`).

The dispatcher updates the state after its best-effort audit append. The update does not depend on whether the audit row was written (`ScheduleDispatchRunner.cs:333-342`). So the operational state cannot tell which transitions were audited.

## 6. Problems being solved

1. Actors are strings with three historical spellings, and nothing resolves them.
2. Committed changes can exist with no audit row, and commit retries can repeat a transaction.
3. Some rows name the wrong actor.
4. Targets are not queryable.
5. Details are ambiguous text.
6. Routine automation hides significant events.
7. Outcome and category have no closed vocabulary, and some legacy outcomes are text-derived.
8. The cursor can skip or repeat rows.
9. There is no tamper or retention position.

## 7. Explicit non-goals

- No export, hash chaining or other tamper-evidence, and no retention or purge feature (OR-10).
- No permission change and no context-scoped audit read.
- No audit of successful reads. `data_access` is reserved.
- No rewrite of evidence columns; no reconstruction of rows that were never written; no corrected actors; no correction tool. A future correction process needs separate authorization (AC-2).
- No identity matching beyond IAM-9 R17.
- No platform-wide idempotency framework and no HTTP idempotency keys (OR-1, AC-1).
- No mandatory deployment-credential change (OR-13 keeps it optional).
- No session redesign. No background classification without approval.
- No historical suppression of repeated blocked events (AC-3).
- No invented deployment cutoff (AC-4).
- No merge of `SecretAudits`.
- No general browser-test cleanup and no general SQL soft-skip fix (RCV-1).
- No personal-data erasure (D-05).

## 8. Design summary

| Id | Design | Section |
|---|---|---|
| A1 | Actor columns, including `external`, the username snapshot, provenance and defect fields. `Actor` is never rewritten | §10 |
| A2 | `AuditActorDto` with kind, references, provenance and issue | §10.7 |
| A3 | `EventId`, `OperationEventId` and `Producer` | §13, §14 |
| A4 | Target, category, severity, visibility, outcome code, outcome provenance and basis, as one frozen classification set | §11 |
| A5 | Per-action detail schemas, typed producers and an overflow policy | §12 |
| A6 | Write classes, the precise guarantee, the unit of work, and commit verification with an operation lock | §13 |
| A7 | Staged rollout through `Producer` and `ClassificationState` | §14 |
| A8 | Startup classification as one atomic transition per row | §15 |
| A9 | Query contract with outcome-aware visibility | §16 |
| A10 | `ActorRef` with provenance and defect notices | §17 |
| A11 | Integrity guard (T-A) and optional privileged credential (T-B) | §19 |
| A12 | Coverage enforcement mechanisms | §21 |

## 9. Relationship to Part 1's AUD-1 plan

| Part 1 text | This gate | Reason |
|---|---|---|
| `ActorDisplay` "username snapshot" vs IAM-9 R17 "original display" | `Actor` is the original. `ActorUsername` is the event-time username | OR-4 |
| Noise via `Category = Routine` | `Visibility`, outcome-aware | OR-5 |
| "No rewrite of all 45 files" | Every required writer converted | OR-1 |
| Remove `AuditRecordId` (also ID-1's plan) | Persisted as `EventId` | AC-1 |
| Deny update and delete | Integrity guard plus optional privileged credential | OR-8, OR-13 |
| AUD-1.5 includes the UI | AUD-1.5 server; AUD-1.5b UI | OR-11 |
| Permission diffs in both AUD-1.2 and IAM-10.2 | AUD-1.2 records diffs; IAM-10.2 keeps refused-grant and repair events | Program gate §5 |

## 10. Actor identity

### 10.1 Actor columns

| Column | Meaning |
|---|---|
| `Actor` | Verbatim recorded value. Never rewritten. New rows keep writing it: a user id, a literal, or the canonical external key (§10.4) |
| `ActorKind` | One of `user`, `external`, `system`, `anonymous`, `worker`, `unresolved` |
| `ActorUserId` | For `user` only |
| `ActorUsername` | Event-time username (§10.3). Same length as the ledger column |
| `ActorExternalProvider`, `ActorExternalTenantId`, `ActorExternalSubjectId` | For `external` only. All three are present together |
| `ActorAttribution` | Provenance (§10.6) |
| `AttributionIssue`, `AttributionIssueCertainty` | Known defect (§15.3) |

All of these except `Actor` belong to the classification set (§11.8). That set is frozen as a whole once complete.

### 10.2 Display versus evidence

- **Display** shows the current username and display name (T-15).
- **Evidence** keeps `ActorUsername` alongside it, unchanged after write (OR-4).
- The row shows "Recorded as `name`" when the two differ.

### 10.3 Event-time username: source and semantics (OR-4)

**Source.**
- For a recorder row with `ActorKind = user`, the recorder reads `Users.Username` for `ActorUserId` in the same transaction that inserts the row, at `Stage` or `WriteAsync` time.
- It never uses the session's username claim.
- It may reuse the resolver's per-request read only if AUD-1.1 shows that read is in the same transaction (I-1). Otherwise it reads again.

**Semantics.**
- `ActorUsername` is the actor account's username as read by the transaction that recorded the event.
- If another administrator's rename commits before that read, the row holds the new name. If the rename commits after, the row holds the old name.
- When an administrator renames their own account, the read follows the rename statement and returns the new name. The required fields carry both names.
- `append` rows perform the same read inside their own write.

**Historical rows.**
- `ActorUsername` is set only when the recorded `Actor` was itself a username matched through the ledger (rule 3, §15.2). That value is the recorded sign-in name, copied verbatim.
- In every other historical case it stays null. No historical row acquires an invented event-time username.
- Reader copy calls it the "recorded sign-in name".

### 10.4 External actors (OR-2)

- **Definition.** `external` is an identity authenticated by an external provider in the current request, for which no platform user is the actor.
- **Key.** For Entra: provider `entra`, tenant id and object id, all taken from the validated authentication result. Object id alone is never sufficient. Email, UPN and display name are not keys. A provider display name may be recorded as the presentation-only `externalNameHint` field.
- **Canonical `Actor` value.** `entra/{tenantId}/{objectId}`. The recorder writes it only when the verified triple exists. The `/` cannot occur in a username or an `ActorLiterals` value.
- **Verified only.** The triple comes from the validated principal: the OIDC callback, or the authenticated Entra principal the access-request flow holds. It never comes from a request body, query string, header or typed value. Failed or unvalidated authentication is recorded as `anonymous`.
- **Unverified claims** never fill the external columns. AUD-1 adds no typed identifiers to failed-attempt rows. IAM-11.4 owns sign-in failure records.
- **Historical rows are never classified `external`.** At most they record an object id without a tenant. They follow IAM-9 R17, and the shared classifier is unchanged.

### 10.5 Actor versus target (OR-2)

| Situation | Actor | Target |
|---|---|---|
| Administrator provisions an Entra identity | The administrator | The created user, or `external_identity` if provisioning fails first |
| Administrator invites a user | The administrator | The invited user |
| Authenticated Entra identity with no user requests access | `external` (the requester) | `entra_access_request` |
| Administrator approves or rejects a request | The administrator | The request; the created user as a related reference |
| User signs in with a linked Entra identity | That user | The identity link, where relevant |
| Invited user completes setup | That user | That user |

### 10.6 Attribution provenance (OR-3)

| `ActorAttribution` | Meaning | Set by |
|---|---|---|
| `recorded` | The recorder identified the actor at event time from the authenticated context | Recorder |
| `recorded_user_id` | A stored user id, confirmed as one | `append` insert; classification |
| `recorded_literal` | A non-human literal | `append` insert; classification |
| `inferred_username` | A recorded username matched through the ledger | Classification |
| `inferred_entra_object_id` | A recorded object id matched to exactly one Entra link | Classification |
| `unresolved` | No rule identified a user | Classification; `append` insert |
| `ambiguous` | Rules pointed at more than one user | Classification; `append` insert |

`AttributionIssue` and `AttributionIssueCertainty` (§15.3) are exposed through the read contract (§10.7) and rendered (§17).

### 10.7 Read contract

```csharp
// Server: APP/Services. Client copy in MC.
public sealed record AuditActorDto(
    string Kind,                          // user | external | system | anonymous | worker | unresolved | unclassified
    UserReferenceDto? User,               // Kind = user, current identity
    AuditExternalIdentityDto? External,   // Kind = external
    string? Label,                        // product label for non-human kinds
    string Recorded,                      // verbatim Actor
    string? UsernameAtEvent,              // ActorUsername
    string Attribution,                   // §10.6, or "not_yet_classified"
    AuditAttributionIssueDto? Issue);     // §15.3

public sealed record AuditExternalIdentityDto(string Provider, string TenantId, string SubjectId, string? NameHint);
public sealed record AuditAttributionIssueDto(string Code, string Certainty);   // definite | possible
```

- `unclassified` is a read-model value only (§14). It is never stored.
- References are composed server-side in one `IUserReferenceResolver` batch per page (R14). A `user` row whose id does not resolve degrades to `unresolved`.
- Operational read models that can show a non-human actor reuse this DTO (§18).

### 10.8 Kinds and labels

| Recorded value | Kind | Runtime-visible label |
|---|---|---|
| User id | `user` | The user's `UserRef` |
| `entra/{tenant}/{object}` (recorder only) | `external` | "Microsoft Entra identity", with any name hint marked as provided by Entra |
| `system`, `system:` subtypes | `system` | "System", "Scheduler", "Module lifecycle" |
| `recovery` | `system` | "Recovery tool" |
| `emergency-cli` | `system` | "Emergency access command line" |
| `system-worker` | `worker` | "Worker" |
| `anonymous` | `anonymous` | "Not signed in" |
| Unresolved | `unresolved` | "Unrecognized actor" plus the string (Approved) |

- `ActorLiterals` gains this label map, and converted writers use its constants.
- The recorder takes the actor from `IPrincipalContext`. Non-human actors are passed only through named entries (`AuditActor.System(subtype)`, `AuditActor.Worker`, `AuditActor.Anonymous`).

## 11. Target, category, severity, visibility, outcome, basis

### 11.1 Target

`TargetType` and `TargetId` record one primary target. `ModuleId`, `ScheduleId`, `RunId` and `ResourceContextId` remain as related references.

Closed vocabulary:

| Area | Values |
|---|---|
| Identity and governance | `user`, `role`, `identity_link`, `external_identity`, `entra_access_request`, `entra_tenant_registration`, `auth_policy` |
| Configuration | `resource_context`, `integration`, `connection`, `schedule`, `platform_setting`, `integration_setting`, `notification_definition`, `notification_subscription`, `managed_contact`, `operational_team`, `module` |
| Operations | `workflow_run`, `operation_run`, `operation_item`, `runtime_instance` |
| Other | `secret`, `instance` |

- `external_identity` ids use the §10.4 canonical form.
- A new value needs a gate amendment.
- A deleted target keeps its id. Its name snapshot goes in `targetName`.

### 11.2 Category

Category is the subject area only. Values: `identity`, `governance`, `configuration`, `operations`, `secrets`, `system`, `data_access` (reserved, no RC1 writer), and `other` (legacy rows with an unknown action).

### 11.3 Severity

| Value | Meaning |
|---|---|
| `standard` | Ordinary change or refusal |
| `elevated` | Change to access, credentials, authentication policy, secrets or Recovery Access, and refusals of these |
| `critical` | Recovery Access sign-in and use; changes IAM-10 later classifies as critical |

Severity is independent of visibility.

### 11.4 Visibility (OR-5, AC-3)

`Visibility` is either `default` or `routine`.

**Rule.** A row is `routine` only when both of these hold:
- its action is routine-eligible in the catalog;
- its normalized outcome is one that action declares routine, normally `succeeded`.

Every other row is `default`. That includes every `failed`, `denied`, `rejected`, `blocked` or `unknown` outcome, and every state-transition record.

| Family | Routine when | Default when |
|---|---|---|
| Scheduled dispatch | Dispatched successfully | Failed; blocked; claim recovered; unknown outcome |
| Blocked dispatch, historical and new | Never | Always |
| Unblocked transition (new action, AUD-1.5) | Never | Always |
| Module discovery | Succeeded | Anything else |
| Own avatar and notification preferences | Succeeded | Rejected uploads, failures |
| Startup composition invalid | Never | Always |

**Historical blocked rows (AC-3).** Every historical blocked dispatch row stays `default`. Two consecutive recorded blocked rows do not prove the schedule stayed blocked between them, because the scheduler's audit writes were best-effort and intervening rows may be missing. So no historical row is reclassified as a repeat.

**Prospective transition-only writing (AUD-1.5).**
- **State.** `ScheduleStates` gains `AuditedBlockState`: null (unknown), `unblocked`, or `blocked:{reason}`. It is the last block state for which a transition row was **successfully written**. It is separate from the operational `LastOutcome`, which the dispatcher updates whether or not the audit row was written (§5.6).
- **Cycle.** Each cycle computes the current block state for the assignment. If it differs from `AuditedBlockState`, the dispatcher writes the transition row and updates `AuditedBlockState` **in one transaction**.
  - Entering blocked writes a blocked row. A change of reason while blocked writes a new blocked row. Leaving blocked writes a `scheduler.dispatch.unblocked` row.
  - The update is conditional on the expected previous value (compare-and-set), so concurrent dispatchers write each transition once.
- **Failure.** If the write fails, neither the row nor the state changes, and dispatch continues (W4). The next cycle sees the same difference and tries again. A failure can delay a transition row, but it cannot hide one.
- **Restart and upgrade.** The state is persisted, so a restart repeats nothing. When `AuditedBlockState` is null (first run after upgrade, or a new assignment), the current state is treated as a transition. The worst case is one extra row per blocked schedule after upgrade, and no transition is hidden.

**Consistency across uses:**
- Writes and classification apply the same rule.
- Filters exclude only `routine` rows by default.
- The default-timeline index covers `default` rows.
- The dashboard counts `default` rows, including historical blocked rows.

**Volume.** Visibility hides rows; it does not reduce stored volume. Only prospective transition-only writing reduces new writes. Every historical row stays stored and visible.

### 11.5 Outcome and outcome provenance (OR-6, AC-4)

**New rows** use `succeeded`, `failed`, `denied`, `rejected`, `blocked` or `requested`, plus `unknown` for an external attempt whose result was not observed (§13.3).

**Legacy rows** keep `Outcome` (raw) untouched and visible. Classification sets two values:

- **`OutcomeProvenance`**:
  - **`recorded`:** the outcome was written by a writer. Evidence is either:
    1. the row has a non-null `Producer` (written after AUD-1.1), or
    2. the action was first written by code that already contained the Epic 37 migration. Every binary that can write such an action applies that migration before writing, so a row of that action cannot predate the backfill. This is code containment, not a deployment cutoff. The catalog records, for each action, the commit that introduced it and whether that commit contains `baa2f763`. AUD-1.1 verifies each entry against git history (I-6).
  - **`indeterminate`:** every other legacy row. These actions existed before Epic 37, so the outcome may be writer-recorded or migration-derived. No database records when the migration ran (§5.1), and a raw value that matches a known string does not show who wrote it.
- **`OutcomeCode`**, assigned as follows:
  - If provenance is `recorded`: use the action-and-raw-value table below.
  - If provenance is `indeterminate`: map only where the action is **era-invariant**. That means git history shows its writer emitted the same outcome value with the same meaning both before Epic 37 (as the first segment of `Details`, which the migration copied) and after (in the column). In that case either provenance gives the same meaning, and the table entry applies. Otherwise `OutcomeCode = unknown`.

| Raw value | Writer evidence | `OutcomeCode` (when provenance is `recorded`, or the action is era-invariant) |
|---|---|---|
| `succeeded`, `failed`, `rejected`, `denied` | Any writer | Same |
| `blocked` | Scheduler blocked; module lifecycle | `blocked` |
| `Blocked` | `execution.*_start.blocked` (`API/WorkflowsController.cs:126`, `API/OperationsController.cs:237`); `execution.workflow_retry.blocked` (`SEC/WorkflowRetryService.cs:125`) | `blocked` |
| `Blocked` | `execution.*_run.failed_definition_unavailable` (`RT/.../WorkflowStartRequestProcessor.cs:729`, `OperationStartRequestProcessor.cs:582`) | `unknown` |
| `success` | Scheduler dispatched (`ScheduleDispatchRunner.cs:324,426`) | `succeeded` |
| `recovered` | Scheduler claim recovered (`:205`) | `succeeded` |
| `requested` | Recovery retry requested; bulk retry requested | `requested` |
| `dispatched` | Recovery retry dispatched (`API/RecoveryController.cs:359`) | `requested` |
| `refused` | Recovery retry refused (`:337`) | `rejected` |
| `completed` | Recovery run cycle (`:424`), written even when the cycle threw (`:230`) | `unknown` |
| `warning` | Legacy schedule without integration (`ScheduleDispatchRunner.cs:549`) | `unknown` |
| `Degraded` | `runtime.composition.invalid_at_startup` (`PERS/ModuleCompositionBootstrapHostedService.cs:64`) | `unknown` |
| Category-like values (`auth-admin`, `account`, `auth`, `recovery`, `bootstrap`, `platform-settings`, `integration-settings`, `resource-context-management`) | Not outcomes | `unknown`, except where the action constant itself states the result (for example `.failed`), confirmed per entry |
| Null; anything not listed | | `unknown` |

**Filters.**
- `outcome=` matches `OutcomeCode`. `outcome=unknown` ("Outcome not recorded") returns exactly the unknown rows.
- An `unknown` row never matches `succeeded`, `failed` or any other asserted outcome, whatever its raw value or provenance.
- `outcomeRaw=` matches the raw value exactly.
- The expanded row shows the raw value and the provenance.

### 11.6 Event basis (OR-1)

| Basis | The row asserts | It does not assert |
|---|---|---|
| `change` | A database state change committed together with the row | Anything outside the database |
| `request` | A durable request was accepted and persisted. Outcome is `requested`; execution is a later event | That the request was executed |
| `external_attempt` | An external action was attempted and what the platform observed (accepted, refused, error, or no answer as `unknown`) | That the external effect happened. For example, SMTP acceptance is not delivery |
| `confirmed_result` | A result confirmed afterwards, such as a worker applying shutdown | |
| `security_event` | An authentication or authorization result about to be delivered (W2) | |
| `attempt` | A refused, denied or failed attempt that changed nothing | |
| `telemetry` | Routine automation | |
| `unspecified` | Legacy rows only: the action is not in the catalog, so nothing is asserted | Anything |

**External effects.** A transaction never makes an external effect atomic. The database change and its `change` row commit first. The external call happens after the commit, outside the retryable transaction. A separate `external_attempt` row records what was observed.

Example: an invitation's issued token is a `change` row with the administrator as actor. The mail send is an `external_attempt` row. AUD-1.2 restructures today's single post-send row (`SEC/AccountInvitationService.cs:81-87`) this way.

`Basis` is stored per row (§11.8). It is assigned from the catalog at insert, or by classification.

### 11.7 Action catalog

Each action name is declared once, with:
- category, severity, target type and basis;
- write class (§13.1);
- whether it is routine-eligible, and for which outcomes;
- its detail schema (§12);
- the commit that introduced it, whether that commit contains the Epic 37 migration, and whether the action is era-invariant (§11.5).

The catalog covers every action name written today. The recorder refuses an undeclared action. AUD-1.6 enumerates the catalog (§21).

### 11.8 The classification set and its completion state (AC-2)

**The classification set** is these columns:
- `ClassificationState`
- `ActorKind`, `ActorUserId`, `ActorUsername`
- `ActorExternalProvider`, `ActorExternalTenantId`, `ActorExternalSubjectId`
- `ActorAttribution`, `AttributionIssue`, `AttributionIssueCertainty`
- `TargetType`, `TargetId`
- `Category`, `Severity`, `Visibility`, `Basis`
- `OutcomeCode`, `OutcomeProvenance`

**`ClassificationState`** is either null (unclassified) or `complete`. A row is always in exactly one of two states:

- **Unclassified:** `ClassificationState` and every other column of the set are null.
- **Complete:** `ClassificationState = complete`, and every column of the set holds its final value. Some columns are **intentionally null**: `ActorUserId` on an external row, the external columns on a user row, the target on a row without one, the issue on a row without one.

**Rules:**
1. **Recorder rows are inserted complete.** The whole set, intentional nulls included, is immutable from insert.
2. **`append` rows are inserted complete or unclassified, never partially classified.** If insert-time classification succeeds, the row is inserted complete. If any lookup cannot run, the whole set is inserted null.
3. **One transition only.** An unclassified row is completed by exactly one atomic `UPDATE` that sets the whole set and `ClassificationState = complete` in one statement. The update is conditional on `ClassificationState IS NULL`.
4. **Frozen once complete.** No later update may change any column of the set, including writing a value into an intentionally null column. A user row cannot gain external columns. An unresolved row cannot gain a user id. A row without a target cannot gain one.
5. **No partial update.** An update that changes part of the set, or that leaves the state null while changing any set column, is refused.
6. **Batch and concurrency safety.** Interrupted batches and concurrent classifiers are safe by construction. A failed batch transaction rolls back whole rows. A second classifier's conditional update affects zero rows that are already complete.

The §19 guard enforces rules 4 and 5 in the database. Corrections to completed rows need a separately authorized future process. None is built.

## 12. Structured details (OR-7)

### 12.1 Per-action schemas and controlled producers

Each catalog action declares its permitted fields, each with a kind, required flag, maximum length and truncatable flag.

| Field kind | Content |
|---|---|
| `id` | Identifier, at most 160 characters |
| `enum` | Closed set |
| `count`, `flag`, `timestamp` | Number, boolean, time |
| `username` | Username |
| `name_snapshot` | Copied stored name. Truncatable |
| `operator_text` | Operator-typed text in a declared field |
| `id_list` | Bounded list of ids, such as a permission diff |

- **Producers are typed factory methods**, one per action, for example `AuditEvents.UsernameChanged(UserId target, string previous, string next)`. There is no public method that takes an arbitrary field name.
- Recorder rows have `DetailsFormat = 'json/1'`.
- `append` and legacy rows have a null `DetailsFormat` and render as "Recorded details".

### 12.2 What validation can and cannot prove

**It can prove that:**
- only declared fields of declared kinds are written;
- no field kind accepts a credential, token, secret or secret-reference type;
- search parameters have no field kind;
- every field is bounded in length.

**It cannot prove that free text contains no secret.** A field-name block list cannot inspect content, so an operator who types a password into a reason box or a role name will have it recorded.

**Mitigation:**
- free text appears only in declared, bounded fields;
- free text is never taken from inputs designated sensitive at source;
- free text is documented as operator-controlled content;
- no content scanning.

### 12.3 Sensitive exclusions

Never recorded:
- passwords, hashes, tokens, MFA secrets, recovery codes, secret values or references, connection strings and key material;
- search or filter text, or any digest of it (T-07, T-24);
- parameter values (names only);
- values of `IsSensitive` settings;
- email addresses and UPNs, except the subject address of an email-change event.

Usernames may appear (IAM-9 §21).

### 12.4 Free-text fields

- `operator_text` fields are declared per action and never required.
- Each is bounded by the operation's own input limit, for example `SEC/BulkOperationItemRetryService.cs:150`. An operation without a limit gains one first (I-9).
- `operator_text` is never truncated.

### 12.5 Overflow policy

- **Measurement.** The UTF-8 byte length of the serializer's JSON, checked at `Stage` or `WriteAsync`. Limit: 32 KB.
- **Truncation.** Over the limit, `name_snapshot` fields are truncated, longest first, each to its own budget, and listed in `_truncated`. Nothing else is ever truncated: required fields, `username`, `id`, `id_list`, `operator_text` and `enum`. Permission diffs and username changes always stay intact.
- **Still over after truncation.** For W1 and W2, the write throws and the operation rolls back. This indicates a schema defect. For W3 and W4, the row is written with `{"_omitted":"size"}` and a warning is logged.
- **Tests** confirm that legitimate maximums fit: a diff adding every catalog permission, maximum-length usernames and maximum-length reasons. No blob storage is added.

### 12.6 Legacy text and the parsing exceptions

Legacy text is never rewritten. No authoritative actor, target or outcome fact is derived from ambiguous text.

| Exception | Purpose | Trust limit |
|---|---|---|
| P-1 `AuditDetailParser.ParseRelatedReferences` | Presentation-only "possibly related" links on non-recorder rows | Regex over unescaped text. Never persisted, filtered on, or used for target, actor or outcome |
| P-2 `AuditDetailParser.ParseOutcome` | **Retired.** `OutcomeCode` comes only from §11.5 | |
| P-3 `UsernameBackfill.TryParseCanonicalization` (`PERS/UsernameBackfill.cs:297-314`) | Approved IAM-9.1 step, which runs only before `Iam9_UsernameRequired` | Unchanged; AUD-1 does not use it |
| P-4 The Epic 37 migration backfill (applied, immutable) | Filled `Outcome`, `ModuleId`, `ScheduleId` and `RunId` from text on rows that existed when it ran | Those values stay visible as related references and existing filters. They are never used for a target. Because they cannot be told apart from writer-recorded values on rows of pre-existing actions, those rows get `OutcomeProvenance = indeterminate` and `OutcomeCode` only through an era-invariant mapping, otherwise `unknown` (§11.5). This replaces revision 2's "unless the value exactly matches" wording, which treated a matching string as sufficient |
| P-5 Typed JSON families (`ConnectionAuditDetails`, `ConnectionToolAuditDetails`, `ResourceMoveAuditDetails`, `ResourceMoveRejectedAuditDetails`) | Deserialized with their own record types | May supply a target for legacy rows of those actions, only when deserialization succeeds exactly |

Nothing else parses details.

## 13. Write guarantees (OR-1, AC-1)

### 13.1 Write classes

| Class | Applies to | Guarantee |
|---|---|---|
| **W1 Required mutation** | Every catalog-required administrative or operator database mutation: bases `change` and `request`, and `confirmed_result` when it changes state | Mutation and row commit in one transaction (§13.2), with commit verification (§13.5) |
| **W2 Required security event** | Sign-in success (local, Entra callback, break-glass); step-up success; connection structure browse | The row commits before the result is delivered. If the write fails or its outcome is unknown, the result is not delivered. A rare unknown commit can leave a row for an undelivered result, as its basis states |
| **W3 Attempt** | Refusals, denials, failed sign-ins, validation rejections, `external_attempt` rows; **sign-out**, which must always succeed | Own short transaction. On failure, a warning log; the response is unchanged |
| **W4 Telemetry** | Scheduler activity, recovery cycles, module discovery, startup composition, runtime processors | Best effort, logged. Never blocks the runtime. Exception: a blocked or unblocked transition row commits atomically with `AuditedBlockState` (§11.4), and its failure still never blocks dispatch |

**Classification of today's writers** (AUD-1.1 builds the catalog from this table; a test pins it):

| Writer family | Class |
|---|---|
| User, role, assignment, permission, identity-link and credential administration; provisioning; access-request create, approve and reject; account services; Recovery Access administration; MFA; Entra primary-auth policy; Entra tenant registration and policy; bootstrap first administrator | W1 |
| Connections create, update, rotate and retire; connection verify (W1 with basis `external_attempt`) | W1 |
| Integration and schedule lifecycle, **including delete**; settings; Applications and ownership moves; notification definitions, subscriptions, contacts and routing; module lifecycle state changes; secret store operations (a recorder row in `AuditLogs`; `SecretAudits` unchanged) | W1 |
| Run start, cancel, retry, park and resume; item retry; bulk retry (basis `request`); attention acknowledge and reopen; runtime shutdown request; enrollment invitation and enrollment | W1 |
| Sign-in successes; step-up success; connection browse | W2 |
| Sign-in failures; audit-read denials; `auth_admin.blocked.*`; role mutation rejections; insufficient strong authentication; schedule delete rejected; ownership move rejected (may stay in its transaction); start blocked; workflow retry blocked; recovery refused or denied; forgotten-password suppressed; step-up failures; Entra link failures; upload rejected; mail delivery attempts; sign-out | W3 |
| Scheduler dispatch, blocked, unblocked, failed, claim recovered, legacy warning; recovery cycle; module discovery; startup composition; runtime processors' definition unavailable | W4 |
| `UsernameBackfill` raw SQL insert (atomic in its own transaction); `AuditClassification` | Listed exceptions |

### 13.2 The guarantee

For a W1 operation, the caller observes exactly one of these outcomes:

1. **Confirmed commit.** The change and the row both committed, and the platform established this.
2. **Confirmed rollback.** Neither committed, and the platform established this (§13.5).
3. **Outcome unknown.** The platform could establish neither. Either both committed or neither did, never one without the other. The response is `outcome_unknown`.

The platform never says nothing changed unless it confirmed a rollback, and it never advises blind retry.

### 13.3 External effects

External effects are outside every transaction (§11.6). An unobserved result is recorded as `unknown` in the attempt row. If the attempt row itself fails to write, a warning is logged and the `change` row stands. The `change` row never claims delivery.

### 13.4 The unit of work behind `Stage`

**Boundary.** An audited unit of work is one explicit transaction on the request's scoped `AppertureDbContext`. It is opened by a recorder-owned helper (working name `IAuditedTransaction`) inside the execution strategy. It spans:
1. the operation lock (§13.5);
2. every read and validation the decision depends on;
3. every mutation;
4. every `Stage` call;
5. the commit.

Hosted services and the worker open their own scope and use the same helper.

**Opening.** The unit is opened with its operation identity (§13.5) and with `Expect(primaryAction)`, the action whose row identifies the operation.

**`Stage(event)` preconditions.** Each failure throws `InvalidOperationException`, as a programming error:
1. A transaction opened by this unit is current on this context.
2. The action is in the catalog as W1.
3. The event passes its schema and the overflow policy.
4. Its `EventId` is not already staged in this unit.

**Effect of `Stage`.** It adds the row to the change tracker, complete (§11.8), with `OperationEventId` set. It reads `ActorUsername` in this transaction.

**Commit postconditions:**
- The unit saves and checks that every staged row was inserted.
- A unit whose `Expect(primaryAction)` has no matching staged row fails before commit. The exception is an attempt that legitimately decided to change nothing, which declares itself a no-op through the helper.

**Enforcement:**
- An EF `SaveChanges` interceptor rejects any `Producer = recorder` row saved when no transaction is current.
- `WriteAsync` (W2, W3) and `TryWriteAsync` (W4) use a context from `IDbContextFactory`, so they never flush a caller's pending changes.
- Repositories that open their own transaction (for example `PERS/SqlUserRepository.cs:184-195`) join a current transaction when one exists. AUD-1.1 lists each one it changes.
- Raw ADO steps manage their own `SqlTransaction`. They are listed exceptions.

### 13.5 Commit verification and retry (AC-1)

This section is bounded to retries **within one server-side operation**. It is not HTTP idempotency and adds no idempotency keys.

**Operation identity.**
- When a W1 operation starts, before entering the execution-strategy delegate, the service generates one **operation event id**. It is the `EventId` of the operation's **primary row**, the row of the `Expect(primaryAction)` action.
- It stays the same across every execution-strategy retry and every verification step of that operation.
- Every row the operation stages carries it in `OperationEventId`.
- Secondary rows in the same transaction get their own `EventId`, generated per attempt. They are never used for verification.
- A transaction that stages several rows (for example an ownership move's moved-out and moved-in rows) is identified by its primary row alone.

**Operation lock.** The first statement of every W1 transaction is `sp_getapplock` with:
- resource `apperture.audit-op.{operationEventId}`;
- mode exclusive;
- owner transaction.

The lock is held until that attempt commits or rolls back.

**When verification runs.** Verification runs whenever the commit call throws, whatever the exception type and whether or not the execution strategy considers it transient. It runs **before** any retry reruns business reads or validation.

**Verification procedure.** It uses a fresh connection to the same authoritative database, never a cached context:
1. Request the operation lock with owner session and a bounded wait.
2. If the lock is not granted within the wait, the earlier attempt may still be in progress or unresolved. Result: **outcome unknown**.
3. If granted, read the primary row with a locking read: `WITH (READCOMMITTEDLOCK)` on `EventId = operationEventId`. Row versioning is never used here.
4. **Row found**, and its `Action` and `OperationEventId` match this operation: **confirmed commit**.
5. **Row absent: confirmed rollback.** The earlier attempt held the lock for its entire transaction, so the grant proves that transaction has ended. An attempt that never acquired the lock did nothing it could commit, because the client abandoned the connection. That is why one negative lookup is enough here, and it is the only case in which a negative lookup counts as proof.
6. Release the lock. Any connection or timeout failure during verification: **outcome unknown**.

**After verification:**

| Verification result | Effect |
|---|---|
| Confirmed commit | No rerun. Return the reconstructed result (below) |
| Confirmed rollback | The strategy may rerun the delegate with the same operation event id. The rerun reads and validates afresh, and its no-op, not-found or refusal results are genuine |
| Outcome unknown | Return `outcome_unknown` with the operation event id. No rerun |

**Reconciling apparent no-op, not-found and refusal results.**
- Within an operation that has had an uncertain commit, no attempt may report "unchanged", "not found", a conflict or a validation refusal unless verification has first confirmed a rollback.
- Verification always runs before a rerun, so these results come only from attempts after a confirmed rollback, or from a first attempt that had no uncertain commit.
- A rename that appears unchanged, or a delete whose target appears missing, after a confirmed commit is reported as success.

**Duplicate keys.**
- Only a violation of the **`AuditLogs.EventId` unique index** is a candidate for prior success, and only when the duplicated value is this operation's primary `EventId`. The service then runs the verification procedure, and treats the operation as succeeded only if it returns confirmed commit for the matching event. Otherwise the result is outcome unknown.
- Any other unique violation keeps its normal meaning. For example, the username ledger key still means `username_taken` and returns 409.
- A duplicate on a secondary row's `EventId` is not a success signal. It is an internal error.

**Result reconstruction (no framework).** Each W1 operation declares a reconstruction that builds its success response from committed state, without rerunning business logic:

| Operation | Reconstructed response |
|---|---|
| Rename | The primary row's recorded previous and new names. Current state is read for the returned user |
| Delete | Success, from the primary row alone |
| Create | The created entity read by the primary row's `TargetId` |
| Update | The entity read by `TargetId` |
| Request-basis operations | The request read by `TargetId` |

- If current state has changed since the commit (a later edit), the response reports success with the current state. It never reports the operation as a no-op or not-found.
- If the target can no longer be read (a later delete), the response is success with the primary row's recorded fields.

**`outcome_unknown` in the UI.** The UI follows the rename precedent (`UsernameRenameException.OutcomeUnknown`). It reloads state and never resubmits automatically. A user resubmission is a new operation with a new operation event id (D-19).

**W2, W3 and W4.** These single-row writes reuse their `EventId` on retry. A duplicate on that `EventId` after a lost acknowledgement means the row exists. They take no operation lock: no business change depends on them, so a duplicate or absent row cannot desynchronize state.

**Existing atomic transactions.** The connection create, ownership move and Application archive transactions gain this verification when converted.

### 13.6 Deletes (OR-1)

Schedule and integration deletes are W1. This supersedes LFC-1 §5 step 6 and the CTX-3B integration-delete write mode (§27).

Possible outcomes:
- If the audit row cannot be written, the delete rolls back.
- After a lost acknowledgement, verification returns either confirmed commit, which is reported as a successful delete even though the target now appears missing, or confirmed rollback.
- Otherwise the outcome is `outcome_unknown`.

## 14. Transitional compatibility and staged rollout (OR-12)

`Producer` is `recorder`, `append`, or null (rows written before AUD-1.1).

**Before AUD-1.1.** Writers produce today's rows. Rows are unclassified and render the raw actor and details. Writers keep their §5.2 modes.

**After AUD-1.1 merges:**
- **What unconverted writers emit.** `Producer = append`, `EventId` set to the caller's `AuditRecordId`, `OperationEventId` set to the same value, no `Resource=` suffix, and the details text as given.
- **How their rows are classified.**
  - Each row is inserted **complete** (§11.8) when insert-time classification succeeds. Category, severity, visibility, basis, `OutcomeCode` and `OutcomeProvenance` (`recorded`) come from the catalog. The actor is classified by the §15.2 rules, with the `unknown` protection and never as `external`. §15.3 defect rules apply.
  - If any lookup cannot run, the whole classification set is inserted null and the row is completed at the next start.
- **How they render.** As "Recorded details". The actor goes through `ActorRef`, with provenance and any notice. An unclassified row renders as "Not yet classified".
- **Guarantees in force.** Each writer keeps its §5.2 mode until it is converted. Recorder rows get W1 with verification for the two actions AUD-1.1 converts.

**After AUD-1.2, 1.3 and 1.4 merge (each independently).**
- Converted families write `Producer = recorder`: complete at insert, structured details, target, `external` where applicable, corrected actors, and verification.
- Defect rules stop applying to their new rows.
- These families get W1 to W4 per §13.1. Unconverted families are unchanged.

**After AUD-1.6 merges.**
- No W1 or W2 action is written through `AppendAsync`, and this is enforced (§21).
- **This completes the required-writer conversion.** W1 and W2 hold for every required action.

AUD-1.5's read changes apply to every row. **RC1 requirement 5 is met only after AUD-1.6 has merged and passed acceptance.**

## 15. Historical data (OR-3, OR-9, AC-2, AC-4)

### 15.1 Scope

`AuditClassification` is a re-runnable startup data step. It applies the single transition of §11.8 to rows where `ClassificationState IS NULL`. It never writes an evidence column (§19.1).

### 15.2 Actor rules

- IAM-9 R17 applies in its approved order through `HistoricalActorClassifier`.
- The classifier gains an additive field reporting which rule matched. IAM-9.2a outcomes are unchanged.

| Classifier result | Kind | User id | `ActorUsername` | Attribution |
|---|---|---|---|---|
| Literal | §10.8 | null | null | `recorded_literal` |
| Already a user id | `user` | `Actor` | null | `recorded_user_id` |
| Ledger match (rule 3) | `user` | resolved | `Actor` (the recorded sign-in name) | `inferred_username` |
| One Entra link (rule 4) | `user` | resolved | null | `inferred_entra_object_id` |
| Unresolved | `unresolved` | null | null | `unresolved` |
| Ambiguous | `unresolved` | null | null | `ambiguous` |

**`unknown` protection (audit only).**
- The value `unknown` is classified `unresolved` without a ledger lookup, on every audit row and at `append` insert.
- This makes no claim that every recorded `unknown` was a placeholder. That notice applies only where §15.3 establishes it.
- The shared classifier and IAM-9.2a are unchanged.

There is no historical `external` (§10.4), and no worker-id data source is needed.

### 15.3 Known attribution defects (OR-3)

**Evidence rules:**
1. A defect applies only to rows **not written by the recorder** (`Producer` null or `append`). The row's `Producer`, not a merge or release time, separates corrected rows from uncorrected ones.
2. A defect is `definite` only when every producer of the action name has always recorded the defective actor. The evidence is the writer source at `b065765`, git history showing the line has been unchanged since it was introduced, and a single producer of the action.
3. Otherwise the defect is `possible`.
4. AUD-1.1 re-verifies each entry (I-6). Entries it cannot confirm are downgraded or removed.
5. Originals are never changed. No corrected actor is recorded.

| Code | Rows | Certainty | Evidence | Reader notice |
|---|---|---|---|---|
| `subject_recorded_as_actor` | `account.invitation.sent`, `account.invitation.resent` | definite | `SEC/AccountInvitationService.cs:164-177` records the invited user as actor. Since `016a4060`, unchanged. Only caller: the administrator path (`SEC/UserManagementService.cs:465-485` ← `API/UsersController.cs:251`) | "This entry recorded the invited account as the actor. The administrator who sent the invitation was not recorded." |
| `subject_recorded_as_actor` | `emergency.auth.blocked.unsafe_operation` | definite | `SEC/EmergencyAccessService.cs:245-249` records the target. Since `f3418f25`, unchanged | "This entry recorded the protected account as the actor. The person who attempted the change was not recorded." |
| `subject_possibly_recorded_as_actor` | `emergency.access.designated` | possible | `:106-110` falls back to the target when no actor is available | "This entry may have recorded the designated account instead of the person who designated it." |
| `subject_possibly_recorded_as_actor` | `account.security_mail.delivery_failed` | possible | Shared by the invitation path (`AccountInvitationService.cs:87`) and paths where the user acted | "This entry may have recorded the affected account instead of the person who acted." |
| `initiator_not_recorded` | `execution.workflow_retry.blocked` with `Actor = system` | definite | `SEC/WorkflowRetryService.cs:120-125`. Since `82e2dea0`. Callers are operator endpoints only (`API/RunsController.cs:347`, `API/RecoveryController.cs:146`) | "The person who requested this retry was not recorded." |
| `initiator_not_recorded` | `audit.read.denied` with `Actor = system`; the scheduler operations endpoints' rows (`RecoveryAuditActionNames.RunCycleDenied`, `.SchedulesRunCycleRequested`) with `Actor = system:unknown` | definite | `API/AuditController.cs:125` and `API/SchedulerOperationsController.cs:147` write these values only as a fallback for an unmapped caller | "The person who made this request was not recorded." |
| `initiator_possibly_misrecorded` | `emergency.access.canonicalized` | possible | `:199-203` always records `emergency-cli`, including when the hosted service runs it (`SEC/EmergencyAccessCanonicalizationHostedService.cs:31`) | "This entry may name the emergency access command line when the change was made automatically by the system." |
| `possible_placeholder` | `execution.operation_item.bulk_retry.*` with `Actor = unknown` | possible | Placeholder written when no actor resolved (`SEC/BulkOperationItemRetryService.cs:155`, since `9c133457`). A pre-IAM-9.2 user named `unknown` would record the same string | "The person who requested this may not have been recorded." |

Any other fallback literal AUD-1.1 finds is added under the same rules, not assumed. Notices are shown on the row (§17), and the ops documentation describes them.

### 15.4 Category, severity, visibility, basis, outcome and target for legacy rows

- **Category, severity and basis** come from the catalog by exact action name. Unknown actions get `other`, `standard` and basis `unspecified`.
- **Visibility** follows §11.4. Historical blocked rows are always `default` (AC-3).
- **`OutcomeProvenance` and `OutcomeCode`** follow §11.5.
- **Targets** come only from P-5 typed JSON. Otherwise the target is null, which is intentional and frozen.

### 15.5 Evidence at classification time

- Resolution uses the users, ledger and Entra links as they exist when the step runs. That can differ from event time: for example, a link removed since leaves an object id unresolved. Provenance shows how each user was identified.
- Completed rows are frozen (§11.8). Automatic reruns never reclassify, and a later automatic run cannot turn an unresolved row into a resolved one.
- This does not prohibit a separately authorized correction process in future. None is built. One would need its own ruling and privileged access (§19).

### 15.6 Execution, coordination, failure and benchmark

- **When it runs.** On every Management Host start, after migrations and the IAM-9.2a step, before readiness, using the `PersistenceMigrationHostedService` template. Under T-B it runs in the privileged startup phase (§19.4). A dry-run preview reports counts.
- **Batches.** Batches by `Id` range, each its own transaction, committing whole rows. Progress persists, and the next start resumes.
- **Conditional transition.** Each row is completed by one `UPDATE` that sets the whole classification set where `ClassificationState IS NULL` (§11.8 rule 3). A row completed meanwhile by another host, or inserted complete by the recorder or `append`, is not touched. The guard (§19) independently refuses any change to a completed row.
- **Multi-host coordination.**
  - A named application lock (`sp_getapplock`, session owner, its own resource name) keeps to one classifier at a time. It does not use the ownership mutation gate (`APP/ResourceContexts/IResourceOwnershipMutationGate.cs:32`).
  - A host that cannot acquire the lock within its wait proceeds with its own conditional pass. Correctness depends on the conditional transition and the guard, not on the lock.
- **Failure.** Unresolved and ambiguous values are results. An execution failure fails startup and keeps committed batches.
- **Readiness.**
  1. With no unclassified rows, the step adds at most 1 second to start, measured. It is a filtered-index existence check.
  2. During a first run the host reports not ready (the `migrations` health check) and logs progress per batch.
- **Benchmark (OR-14).**
  - **Five minutes per one million unclassified rows on the reference development machine** is a provisional benchmark.
  - AUD-1.1 records:
    - the hardware (CPU, memory, storage type) and SQL Server edition and version;
    - the workload: row count; actor value mix (user ids, usernames, object ids, literals, unresolved); ledger and link table sizes; batch size;
    - the duration;
    - readiness behaviour, and Windows Service start behaviour in the supported topology (I-4).
  - The release note states the per-million figure.
  - Exceeding the benchmark requires investigation and a concrete recommendation. It does not by itself invalidate the design or require background classification. **Background classification is not adopted without the owner's approval.**
- **Release and deployment evidence.**
  - The missing release tag at `b065765` is only an observation. It does not show that no environment ran an IAM-9.2 or later build, or that no Entra link was removed since.
  - In such an environment, pre-IAM-9.2 object-id actors whose link was removed classify as `unresolved`.
  - **The requirement stands:** AUD-1.1 must be in the first release that contains IAM-9.2 (T-16). Whether a deployment already ran IAM-9.2 without it is operator knowledge.

### 15.7 Verification (A1.1-8)

SQL Server tests that fail without SQL:
- evidence columns are byte-identical before and after classification;
- every `ActorUserId` exists;
- unresolved and ambiguous rows have no user id;
- a rerun changes zero rows;
- an interrupted run converges;
- concurrent classifiers do not overwrite each other;
- the `unknown` protection holds against a user named `unknown`;
- §15.3 rules apply to non-recorder rows only;
- historical blocked rows stay `default`;
- provenance and outcome codes match §11.5.

The handback reports counts by kind, attribution, issue and provenance. AUD-1.1 also corrects `deploy/README.md:151`.

## 16. Queries, API and authorization

**`GET /api/control/audit` parameters:**
- New: `actorUserId`, `actorKind`, `targetType` with `targetId`, `category` and `severity` (both repeatable), `includeRoutine` (default **false**), `outcome` (matches `OutcomeCode`, including `unknown`), `outcomeRaw`.
- Kept: `fromUtc`, `toUtc`, `actor` (exact), `actionPrefix`, `moduleId`, `scheduleId`, `runId`, `resourceContextId`.
- Excluding routine rows by default is a documented behaviour change. Historical blocked rows remain in the default result.

**`GET /api/control/audit/{id}`** returns any row.

**Response additions:**
- `ActorRef`, `Target`
- `Category`, `Severity`, `Visibility`, `Basis`
- `OutcomeCode`, `OutcomeProvenance`, `OutcomeRaw`
- `DetailsFormat` plus `Fields`
- `CorrelationId`, `EventId`, `OperationEventId`

`Actor` remains, marked obsolete in the client.

**Cursor** (Approved, LST-1 §18.3): ordered by `At desc, Id desc`. The token is an opaque, versioned `(At, Id)` pair. Malformed or legacy tokens return `cursor_invalid`. Page size defaults to 50, maximum 100. No total.

**Indexes:**
- unique on `EventId`;
- `(ActorUserId, At desc, Id desc)` and `(TargetType, TargetId, At desc, Id desc)`, each filtered to non-null;
- `(At desc, Id desc)` filtered on `Visibility = 'default'`;
- `Id` filtered on `ClassificationState IS NULL`;
- query plans in the handback (I-8).

**Authorization:**
- `audit.read` at instance scope, unchanged.
- References are composed regardless of `user.read` (R14). Links appear only with `user.read` (R15). This inherits F-1 unchanged.
- A read denial with no actor is recorded as `anonymous`.
- Successful reads stay unaudited.

## 17. Presentation

### 17.1 `ActorRef`

`UI/Molecules/ActorRef.razor` renders by kind:

| Kind | Rendering |
|---|---|
| `user` | `UserRef` |
| `unresolved` | `UserRef` with `UnresolvedActor` |
| `external` | "Microsoft Entra identity", name hint marked as provided by Entra, tenant and object id in `TechnicalDetails`, never linked |
| `system`, `anonymous`, `worker` | Label with non-person treatment |
| `unclassified` | Recorded string with "Not yet classified" |

**Provenance text:**
- `inferred_username`: "Identified from the sign-in name recorded at the time."
- `inferred_entra_object_id`: "Identified from the Microsoft Entra account recorded at the time."
- `ambiguous`: "More than one account matched the recorded value."

**Defect notices** (§15.3) appear inline: compact in the table and in full in the expanded row. They are never hover-only.

`UserRef` is unchanged. Adopting pages and `ActorRef.razor` join its allow-list.

### 17.2 Audit timeline (AUD-1.5b)

- **Actor column:** compact `ActorRef`.
- **Expanded row:** structured fields or "Recorded details"; raw outcome, outcome code and provenance; basis, category, severity and visibility; target link; "Recorded as" when the username differs; provenance and defect notices; the verbatim actor in `TechnicalDetails`.
- **Filters:** time, category, severity, "Include routine activity", outcome (including "Outcome not recorded"), actor, target, context.
- **Navigation:** URL state per LST-1 §13, including `actionPrefix`; the LST-1 primitives; a per-row route.

### 17.3 Other surfaces

- Activity panels use `ActorRef`. The Integration panel queries by target.
- The dashboard counts `default` rows.
- Inventory Activity is unchanged by AUD-1. LST-1.6's scope copy states that routine rows are excluded.

## 18. `UserRef` ownership (OR-11)

**Ownership rule:**
- AUD-1 owns surfaces showing the actor of an audited or run event that can be non-human or unresolved.
- LST-1.3 owns surfaces whose value is always a platform user id after IAM-9.2a.
- LST-1.4 keeps the team table.

| # | Surface (`b065765`) | Value | Owner |
|---|---|---|---|
| 1 | `UI/Pages/Control/AuditTimeline.razor:164` | `AuditLogs.Actor` | **AUD-1.5b** |
| 2 | `UI/Organisms/EntityRecentActivityPanel.razor:46` | `AuditLogs.Actor` | **AUD-1.5b** |
| 3 | `UI/Pages/Runs/RunDetail.razor:474` | Mixed worker and user ids | **AUD-1.5b** (data from AUD-1.3) |
| 4 | `UI/Pages/Runs/RunDetail.razor:605` | `WorkflowExternalControlRequests.RequestedBy` | **AUD-1.5b** (data from AUD-1.3) |
| 5 | `RunDetail.razor:343`, `:607` | Worker ids | No adoption |
| 6 | `UI/Organisms/RunOperationItemizationPanel.razor:421` | Acknowledging user | **LST-1.3** |
| 7 | `UI/Pages/Integrations/IntegrationScheduleDiagnostics.razor:250` | Acknowledging user | **LST-1.3** |
| 8 | `UI/Pages/Runtimes/RuntimeShutdownRequests.razor:46` (`:48` worker: no adoption) | Requesting user | **LST-1.3** |
| 9 | `UI/Pages/Runtimes/RuntimeShutdownRequestsList.razor:47` (`:49` worker: no adoption) | Requesting user | **LST-1.3** |
| 10 | `UI/Organisms/Notifications/NotificationRecipientResolutionDisplay.razor:46` | Recipient | **LST-1.3** |
| 11 | `UI/Organisms/Notifications/NotificationRecipientSnapshotDisplay.razor:96` | Recipient in the frozen snapshot | **LST-1.3** |
| 12 | `UI/Pages/Config/Contacts/ManagedContactEditor.razor:97` | `LinkedUserId` | **LST-1.3** |
| 13 | `UI/Pages/Attention/AttentionContactGroupDetail.razor:90` | Team member | **LST-1.4** (LST-1 §28) |
| 14 | `UI/Pages/Config/Notifications/NotificationDefinitionDetail.razor:560` picker | `UserListDto` | **LST-1.3** |
| 15 | `UI/Pages/Integrations/IntegrationNotifications.razor:419` picker | `UserListDto` | **LST-1.3** |
| 16 | `NotificationDefinitionDetail.razor:289` | Subscription `RecipientRef` (not on the IAM-9 list) | **LST-1.3** |
| 17 | `UI/Pages/Integrations/IntegrationNotifications.razor:230` | Subscription `RecipientRef` (not on the IAM-9 list) | **LST-1.3** |
| 18 | `AuthOverview.razor:126`, `AccountProfile.razor:197`, `UserDetail.razor:112`, `UserEffectiveAccess.razor:72` | `TechnicalDetails` | No adoption |
| 19 | `UI/Pages/Connections/ConnectionDetail.razor:278` | Owner id | No adoption |
| 20 | Inventory Activity; dashboard cards | No actor | None |

**Dependencies and actor filtering:**
- LST-1.3 depends on no AUD-1 package.
- AUD-1.5b depends on LST-1.1 (rows 1 and 2) and on AUD-1.3 (rows 3 and 4).
- AUD-1.5b ships actor filtering from a row, which needs neither LST-1.3 nor `user.read`.
- Directory selection through `UserSelector` appears only when LST-1.3 has delivered the selector **and** the viewer holds `user.read`. AUD-1.5b does not wait for LST-1.3.

## 19. Database protection (OR-8, OR-13, AC-2)

### 19.1 Evidence-bearing columns

**Immutable after insert:** `Id`, `EventId`, `OperationEventId`, `At`, `Actor`, `Action`, `Details`, `DetailsFormat`, `Outcome`, `ModuleId`, `ScheduleId`, `RunId`, `ResourceContextId`, `CorrelationId`, `Producer`.

**Classification set** (§11.8): either inserted complete or completed by one transition, then frozen as a whole, intentional nulls included. Its columns are `ClassificationState`; `ActorKind`, `ActorUserId`, `ActorUsername`, `ActorExternalProvider`, `ActorExternalTenantId`, `ActorExternalSubjectId`; `ActorAttribution`, `AttributionIssue`, `AttributionIssueCertainty`; `TargetType`, `TargetId`; `Category`, `Severity`, `Visibility`, **`Basis`**; `OutcomeCode`, `OutcomeProvenance`.

### 19.2 Integrity guard (T-A; ships with AUD-1.1)

AUD-1.1's migration adds two triggers to `AuditLogs`.

**`UPDATE` trigger.** It refuses the whole statement if any affected row:
- changes an immutable column;
- has `deleted.ClassificationState = 'complete'`, so any change to a completed row is refused, including writing a value into an intentionally null column;
- has `deleted.ClassificationState IS NULL` and either leaves `inserted.ClassificationState` null while changing a set column, or sets it to anything other than `complete`. Partial and invalid transitions are refused.

**`INSTEAD OF DELETE` trigger.** It refuses every delete.

The guard protects every evidence column against application defects and ordinary DML from any principal that cannot alter the table.

### 19.3 What the default (T-A) does not protect

In a default installation the Management Host uses one login for migrations and runtime (§5.5). That login can alter the table, so it can disable or drop the guard.

T-A is therefore an **integrity guard, not access control**:
- It stops the application's own defects and accidental DML.
- It does not stop a compromise of the Management Host's database login, a `db_owner` member or `sysadmin`.
- It is not tamper-evidence.

The supported-topology documentation says so.

### 19.4 Optional privileged credential (T-B; ships with AUD-1.6)

- **Configuration.** An optional second connection profile, the *privileged startup connection*. When it is absent, the installation is T-A and behaves as today.
- **Use when configured.**
  - It is used **only** for the startup phase: EF migrations, the IAM-9.1 username backfill path, the IAM-9.2a normalization and `AuditClassification`.
  - Normal requests, hosted services, the scheduler and the worker use the runtime connection.
  - The privileged connection is opened for the startup phase and disposed before the host reports ready. No runtime service can resolve it.
- **Failure.** If the privileged startup phase fails, for example because the credential is invalid or lacks rights, **startup stops** with a clear error. There is no fallback to running the phase on the runtime connection, and no fallback to privileged runtime operation.
- **Runtime login grants (documented script):**

  | Area | Grant |
  |---|---|
  | Application tables | Data access as today (select, insert, update, delete), except below |
  | `AuditLogs` | `SELECT` and `INSERT` only; `UPDATE` and `DELETE` denied |
  | Schema | No `ALTER`, `CONTROL`, `db_owner` or `db_ddladmin`. No permission to alter the table, its triggers or permissions |
  | Locks | `EXECUTE` on `sp_getapplock` and `sp_releaseapplock` (public) |

  The runtime login has **no classification-update authority**. Rows that `append` inserted unclassified are completed at the next privileged startup phase.
- **Privileged login.** It holds the rights migrations need. The guard still applies to its DML, but it can alter the table. It is used only by the startup phase.
- **Threat boundary.** T-B protects audit evidence against access through the **restricted runtime principal**, including a compromise limited to that principal. It does **not** protect against a compromise that also obtains the privileged startup credential, `db_owner` membership or `sysadmin`. It is not tamper-evidence.
- **Documentation and tests** (AUD-1.6):
  - install steps for creating the two logins and applying the grant script;
  - a SQL Server test with two real logins showing that:
    - the runtime login can insert audit rows;
    - it cannot update or delete them, cannot alter the table or its triggers, and cannot run classification;
    - the privileged login runs migrations and classification;
    - the completed-record protections of §19.2 hold for both logins;
  - a startup test showing that a failing privileged phase stops the host.

### 19.5 Not chosen

- **T-C**, mandatory separation for every installation.
- **Column-level `DENY` alone**, which protects only part of the evidence and is bypassed in the default topology.

Permission enforcement is not tamper-evidence. Ordinary application access is not database-owner access.

## 20. Retention (OR-10)

RC1 keeps audit rows indefinitely. There is no automatic purge, and configurable retention is deferred.

`docs/03-operations/backups-and-retention.md` will state:
- the retention position;
- expected growth by source: dispatch rows dominate, and prospective transition-only blocked rows reduce new writes, but historical rows remain and visibility does not reduce storage;
- that backups carry the whole audit log;
- that the classification step's first run belongs in upgrade timing;
- that any future retention feature needs its own ruling before it deletes non-routine rows.

## 21. Coverage enforcement (OR-12)

| Mechanism | Proves | Does not prove |
|---|---|---|
| M1 Catalog completeness (AUD-1.1): every action used by a recorder producer or `AppendAsync` is in the catalog | Declared semantics for every written action | That every mutation writes an action |
| M2 Writer path (AUD-1.6): no W1 or W2 action reaches `AppendAsync`, checked by a source scan and `Producer` assertions | Required actions use the atomic path | Correct transaction placement |
| M3 Writer inventory (§13.1, pinned in AUD-1.6): HTTP, hosted-service, worker, startup and CLI mutation paths | The known surface is enumerated and reviewed | Paths nobody listed |
| M4 Controller guard (AUD-1.6) | HTTP entry points declare their audit | Background or service-level mutations; that the declared action is actually written |
| M5 Audited-entity interceptor (AUD-1.6, test-time): fails a transaction that changes an audited entity (with declared property exclusions) without a staged recorder row | No exercised path changes an audited entity unaudited | Unexercised paths; raw SQL |
| M6 Transaction and verification tests (AUD-1.1, then each conversion): audit failure, lost acknowledgement with verification, operation-lock timeout, `EventId` duplicate, unrelated unique violation | Atomicity and the AC-1 contract per family | Coverage |
| M7 Per-action content tests | Actor, target, outcome, visibility and excluded fields | |
| M8 Guard tests (AUD-1.1, AUD-1.6): the §19.2 transitions and refusals; the §19.4 grants | Classification freeze and evidence immutability in the database | Protection against privileged, `db_owner` or `sysadmin` access |

Handbacks state which mechanisms support each coverage claim.

## 22. Package decomposition

```
AUD-1.0 (approved 2026-10-06 with conditions)
   |
AUD-1.1  schema, catalog, recorder + unit of work + verification, append bridge, actor contract,
   |     classification, integrity guard (T-A)     <- NEXT, not started; in the first release containing IAM-9.2 (T-16)
   +-- AUD-1.2  identity and governance conversion and coverage   --> with 1.1, the contracts IAM-10 implementation needs
   +-- AUD-1.3  operator action attribution
   +-- AUD-1.4  configuration lifecycle conversion and coverage
   +-- AUD-1.5  query contract, cursor, visibility, transition-only blocked writing (server)
   |      +-- AUD-1.5b  timeline UI and actor presentation  <- also LST-1.1, AUD-1.3
   +-- AUD-1.6  enforcement (M2 to M5), T-B optional privileged credential, retention doc  <- after 1.2 to 1.4
```

Each package has its own branch, and each branch needs the owner's confirmation.

### AUD-1.1 Foundation (next; not started)

**Scope:**
- A migration adding the §10.1, §11.8, §13 and §14 columns (`EventId`, `OperationEventId`, `Producer`, the classification set including `ClassificationState`, `Basis` and `OutcomeProvenance`, `DetailsFormat`), the §16 indexes and the §19.2 guard.
- The catalog for every action, with the §11.5 outcome and provenance data and the §15.3 defects.
- The recorder: `IAuditedTransaction`, `Stage`, `WriteAsync` and `TryWriteAsync`, typed producers, the overflow policy, the interceptor, the operation lock and verification, and result reconstruction for the converted actions.
- The `append` bridge, `AuditActorDto` and the label map.
- `AuditClassification`: preview, application lock, single conditional transition.
- Conversion of `governance.user.username.changed` and of `emergency.access.canonicalized`, the latter with its corrected actor.
- Replace AU8, citing this gate. Correct `deploy/README.md`.

**Acceptance:**

| Id | Criterion |
|---|---|
| A1.1-1 | A recorder row is complete at insert: kind, user id, `ActorUsername` read in-transaction, attribution `recorded` |
| A1.1-2 | A later rename leaves the row unchanged. A rename by another administrator during a live session yields the in-transaction name, not the session claim |
| A1.1-3 | Target persisted. No `Resource=` text |
| A1.1-4 | Category, severity, visibility, basis and outcome come from the catalog. `data_access` declared and unused |
| A1.1-5 | No actor literal outside `ActorLiterals` on converted paths |
| A1.1-6 | No arbitrary fields; sensitive kinds refused; injection cases (role `x; runId=1`; a username containing `=` and a space); the §12.5 overflow tests |
| A1.1-7 | Classifier rule reporting, with IAM-9.2a unchanged |
| A1.1-8 | §15.7 |
| A1.1-9 | JSON previous and new username from the transaction result |
| A1.1-10 | Audit insert failure leaves no change committed |
| A1.1-11 | `append` rows carry `Producer`, `EventId` and `OperationEventId`, and are complete or wholly unclassified |
| A1.1-12 | The OR-14 benchmark is recorded with hardware, workload, duration, readiness and service-start behaviour. If exceeded, an investigation and recommendation follow |
| A1.1-13 | The guard allows the legitimate transitions: insert complete; insert unclassified, then one complete transition |
| A1.1-14 | **Lost acknowledgement, then rename appears unchanged:** verification confirms the commit, the response is success with the recorded previous and new names, and there is exactly one audit row |
| A1.1-15 | **Lost acknowledgement, then delete appears missing** (a delete harness at this stage; the production deletes in AUD-1.4): success, not 404, and exactly one audit row |
| A1.1-16 | **Several rows in one transaction:** after a lost acknowledgement, verification uses the primary row only. A confirmed commit means no rerun and exactly the original rows. A confirmed rollback means one rerun and exactly one set of rows |
| A1.1-17 | **Unrelated unique violation** (a rename to another user's name) in an attempt: normal 409 `username_taken`, never treated as prior success. A duplicate on the primary `EventId` is treated as success only after verification confirms the matching event |
| A1.1-18 | **Operation lock not granted within the wait,** or a verification connection failure: `outcome_unknown`, no rerun |
| A1.1-19 | **Frozen classification:** attempts to fill a previously null column on a completed `user` row (external columns), a completed `external` row (`ActorUserId`), and a completed `unresolved` row (`ActorUserId`, target) are refused. So are a partial update, a second classification and any delete. A legitimate initial classification succeeds |
| A1.1-20 | **Outcome provenance:** a legacy row of a pre-Epic-37, non-era-invariant action with raw `failed` gets `indeterminate` and `unknown`, and does not match `outcome=failed`. The same raw value on a post-Epic-37 action gets `recorded` and `failed`. An era-invariant action maps. Raw values are unchanged and visible |

**Validation:**
- unit tests;
- SQL Server suites that fail without SQL, with execution markers in the handback;
- fault injection through a test interceptor;
- one full CI checkpoint.

**Recommended model:** Opus, high, with senior review.

### AUD-1.2 Identity and governance

**Scope:**
- Convert every identity and governance W1 and W2 writer.
- Record `external` actors for access requests and association flows.
- Fix the actors that are wrong today.
- Mark rows `viaRecoveryAccess` instead of writing a pre-mutation marker.
- Add new coverage: sign-out (W3), Entra remote failure, tenant registration, sign-in policy, enrollment.
- Split invitation and mail into separate rows.
- Record permission diffs.

**Acceptance:**
- M7 for each action.
- M6 for one path per service, including lost acknowledgement.
- An external actor carries the full triple and never comes from request input.
- A rename whose audit fails leaves the old name, and a retry is audited.

**Validation:** the SEC-1 browser suite stays green in CI. No new browser tests.

**Recommended model:** Sonnet, high, with senior review.

### AUD-1.3 Operator action attribution

**Scope:**
- Part 1 AUD-1.3 actions as W1 with their request rows.
- Replace the `system` and `unknown` actors.
- Record real outcomes in `RecoveryController`.
- Add actor columns on request rows where they are missing.

*Ownership clarification (product owner, 2026-10-07).* Bulk item retry and failed-item attention acknowledge and reopen are W1 in §13.1 but were named by no package. They are assigned to AUD-1.3. This assigns ownership only: their requirements are those §13 sets for every W1 writer, unchanged. Bulk retry is orchestration over item retries, which are already AUD-1.3 actions; partial results and per-item `outcome_unknown` are kept. Row cardinality follows the catalog (§11.7) and is recorded in the [AUD-1.3 record](aud1-3-operator-action-record.md).

**Acceptance:**
- M7 for each action.
- The request row and the audit row share the actor.
- Durable dispatch is unchanged.

**Recommended model:** Sonnet, high.

### AUD-1.4 Configuration lifecycle

**Scope:**
- Convert the existing configuration writers, including both deletes, with verification.
- Add Part 1 AUD-1.4 coverage.
- Route secret-store events through the recorder.
- Handle the runtime-shutdown duplicate case.
- Team events stay in LST-1.4.

*Owner rulings (product owner, 2026-10-07).* Four clarifications approved at the start of AUD-1.4. They assign ownership and mechanism only; the requirements of §11, §12 and §13 are unchanged. Detail is in the [AUD-1.4 record](aud1-4-configuration-lifecycle-record.md).
- **Secret audit identifiers.** A secret-store event is recorded under the Secrets category with the owning configuration entity as its target (for example the connection, setting or tenant registration). It never records a secret value or a secret reference, consistent with §12.3. In this codebase a secret's store id is its reference, so the `secret` target type of §11.1 stays in the vocabulary and is not used.
- **Attention routing and contact groups.** AUD-1.4 owns the attention-routing rules: instance defaults, and the integration override upsert and clear. Attention contact groups are stored as operational teams, so their create, update and membership changes are team events and stay in LST-1.4.
- **Secret-store transaction participation.** The SQL secret store joins a transaction already open on its context, such as an audited unit's, instead of opening and committing its own (§13.4). Secret changes then commit or roll back with the owning change and its rows. Outside a transaction it keeps standalone behaviour. This amends the AUD-1.2 secret sequencing (record D35).
- **Integration delete locking.** When integration delete is converted, it acquires the integration row lock inside the audited unit before its blocker checks, which serializes it with binding creation and attached-schedule creation.

**Acceptance:**
- M7 for each action.
- M6 for each delete, including a lost acknowledgement followed by an apparently missing target.
- No sensitive setting value is recorded.

**Recommended model:** Sonnet, high.

### AUD-1.5 Query contract, cursor and visibility (server)

**Scope:**
- The cursor, the §16 parameters and the DTO.
- The first `IUserReferenceResolver` consumer.
- Prospective transition-only blocked writing (§11.4): the `ScheduleStates.AuditedBlockState` migration, compare-and-set, and the `scheduler.dispatch.unblocked` action.
- Module discovery as routine.
- The dashboard count.
- The denial actor.

**Acceptance:**

| Id | Criterion |
|---|---|
| A1.5-1 | Cursor SQL tests: `At` order differs from `Id` order, equal `At`, combined with filters |
| A1.5-2 | `cursor_invalid` |
| A1.5-3 | Target query |
| A1.5-4 | The default view excludes routine rows. A failed dispatch and historical blocked rows are visible |
| A1.5-5 | The dashboard counts `default` rows, historical blocked rows included |
| A1.5-6 | Current username after a rename, with `UsernameAtEvent` unchanged. External, literal and unresolved kinds are kept |
| A1.5-7 | `outcome=unknown` and `outcomeRaw` |
| A1.5-8 | Transition tracking: one row per block, reason change and unblock. A failed transition write leaves the state unchanged, and the transition is written on a later cycle. A restart writes no duplicate. A null state after upgrade writes one transition. Two concurrent dispatchers write one row |
| A1.5-9 | No historical row's visibility or volume changes |

**Validation:** SQL tests that fail without SQL; query plans; a full CI checkpoint (schema change).

**Recommended model:** Sonnet, high.

### AUD-1.5b Timeline UI and actor presentation

**Prerequisites:** AUD-1.5; LST-1.1 (B-1); AUD-1.3 for the Run detail rows. Not LST-1.3.

**Scope:** §17, and the AUD-1 rows of §18, including actor filtering from a row. `UserSelector` appears only when available and the viewer holds `user.read`.

**Acceptance:**

| Id | Criterion |
|---|---|
| A1.5b-1 | Rendering by kind, with provenance and defect notices visible without hover |
| A1.5b-2 | Refresh restores state |
| A1.5b-3 | Filter changes replace history |
| A1.5b-4 | Per-row route gated by `audit.read` |
| A1.5b-5 | A load-more failure keeps the rows |
| A1.5b-6 | No raw user id outside `TechnicalDetails` |
| A1.5b-7 | The actor filter works without `user.read` |

**Validation:** bUnit, and at most one gated browser flow.

**Recommended model:** Sonnet, high.

### AUD-1.6 Enforcement and T-B

**Prerequisites:** AUD-1.2 to 1.4.

**Scope:**
- M2 to M5.
- The T-B optional privileged startup connection (§19.4): configuration, host wiring, the no-fallback startup failure, the grant script, install documentation and its SQL proof.
- The retention document.

**Acceptance:**

| Id | Criterion |
|---|---|
| A1.6-1 | An unaudited mutating endpoint fails M4 |
| A1.6-2 | A search or secret field fails a test |
| A1.6-3 | The two-login test of §19.4 |
| A1.6-4 | M2 shows zero W1 or W2 actions on `AppendAsync`, which completes the conversion (§14) |
| A1.6-5 | A failing privileged phase stops startup, with no fallback |
| A1.6-6 | With T-B configured, no runtime service can obtain the privileged connection |

**Recommended model:** Sonnet, high.

### Validation, proportionate

- Each package runs its own and affected tests. CI runs on each PR.
- Explicit full CI checkpoints: AUD-1.1 and AUD-1.5.
- New SQL suites fail rather than soft-skip without SQL.
- RCV-1 keeps N-1.

## 23. Traceability: inherited findings

| Finding | Where |
|---|---|
| F-3 | §12.1; A1.1-9 |
| F-4 | §15; A1.1-7, A1.1-8; T-16 |
| F-8 | §18 (OR-11) |
| N-8 | A1.5-6 |
| Part 1 §K P1 rows | AUD-1.2 to 1.5 |
| Addendum §C.4 | T-06 to T-09 |

## 24. Contradictions and supersession records

| # | Finding | Handling |
|---|---|---|
| C-1 | IAM-9 R17 "original display" versus Part 1 "username snapshot" | `Actor` is the original; `ActorUsername` is the snapshot (OR-4). No IAM-9 text change is needed |
| C-2 | Four documents gave different or no owners for operational `UserRef` adoption | §18 (OR-11). Pointers recorded (§27) |
| C-3 | LFC-1 §5 step 6; CTX-3B integration-delete write mode | **Superseded by OR-1.** Notes recorded; historical text preserved |
| C-4 | Part 1's "not all 45 files" | Superseded by OR-1. Part 1 not edited |
| C-5 | A deny-based tamper stance would block classification and cannot bind the runtime login | §19 (OR-8, OR-13) |
| C-6 | Part 1's `Category = Routine` | Superseded by OR-5 |
| C-7 | R14 lacked a verified external kind | **Amended by OR-2.** Note recorded in IAM-9 §14 |
| C-8 | `unknown` placeholder versus grandfathered usernames | §15.2 protection; §15.3 placeholder notice only where established |
| C-9 | Permission diffs placed in two packages | §9 |
| C-10 | `deploy/README.md:151` | AUD-1.1 |
| C-11 | Part 1 and ID-1's plan remove `AuditRecordId` | Reversed: persisted as `EventId`. ID-1's plan item is superseded for audit records. Part 1 not edited |
| C-12 | Revision 1: "a W1 failure means nothing changed; retry is safe" | Withdrawn (§13.2) |
| C-13 | Revision 1: "new rows carry structured details" | Withdrawn; recorder rows only (§14) |
| C-14 | Revision 1: unresolved results are "the evidence closest to the event" | Withdrawn (§15.5) |
| C-15 | Revision 1's inference from the missing release tag | Corrected (§15.6) |
| C-16 | Integration activity panel; ignored `actionPrefix`; LST-1 line-reference drift | AUD-1.5b; informational |
| C-17 | Revision 2's verification could misread a rerun that found an already-applied change, and treated one negative lookup as proof of rollback | **Corrected by AC-1** (§13.5): operation lock, verification before rerun, reconciliation, duplicate-key rule, reconstruction |
| C-18 | Revision 2's per-column write-once rule let completed rows gain values in intentionally null columns, and left `Basis` outside the evidence inventory | **Corrected by AC-2** (§11.8, §19) |
| C-19 | Revision 2 demoted repeated historical blocked rows to routine, but missing intervening rows mean consecutive blocked rows do not prove continuity | **Withdrawn by AC-3** (§11.4) |
| C-20 | Revision 2 said Epic 37-derived outcomes map only on exact match (§12.6 P-4) while the §11.5 table mapped by raw value. A matching string does not show who wrote it | **Corrected by AC-4** (§11.5 provenance, §12.6 P-4) |
| C-21 | Revision 2 proposed a hard 5-minute threshold that would trigger an owner decision | **Replaced by OR-14** (provisional benchmark) |

No approved RC1 Decision is changed. OR-1 supersedes two epic-level rulings. OR-2 amends one IAM-9 ruling additively. OR-11 clarifies sequencing within the program gate's dependencies. No condition required a new product decision.

## 25. Decisions and implementation-stage questions

### 25.1 Product decisions

None open. Revision 2's R-1 is resolved by OR-13 (T-B).

### 25.2 Implementation-stage questions

| # | Question | Package | Planned validation |
|---|---|---|---|
| I-1 | Can the resolver's user read be the `ActorUsername` source, or must the recorder read in-transaction? | 1.1 | A1.1-2 |
| I-2 | Do repositories on converted paths join an ambient transaction correctly? | 1.1 to 1.4 | M6 per family; repository tests with and without an ambient transaction |
| I-3 | Explicit verification loop or EF's `ExecuteInTransactionAsync`, given that verification must run on every commit-phase exception (§13.5); the operation-lock wait budget; how `outcome_unknown` is surfaced per endpoint | 1.1 | A1.1-14 to A1.1-18; an HTTP test for the problem code |
| I-4 | Windows Service start and readiness during a long startup step | 1.1 | Benchmark run with the host as a service; health-check timeline (OR-14) |
| I-5 | EF Core with triggers (`HasTrigger`) and insert behaviour | 1.1 | SQL tests for insert, classification transition, and refused update and delete |
| I-6 | Confirm each §15.3 defect entry, each §11.5 outcome entry, each action's introducing commit, and the era-invariance claims | 1.1 | One test per entry; unconfirmed entries downgraded to `possible`, `indeterminate` or `unknown` |
| I-7 | Audited-entity list and property exclusions for M5 | 1.6 | A deliberately unaudited mutation fails |
| I-8 | Index choice for the default visible timeline | 1.1, 1.5 | Query plans on the seeded table |
| I-9 | Does every reason-bearing operation bound its input? | 1.2 to 1.4 | Validation tests before the field is declared |
| I-10 | How each dispatcher computes the current block state and reason key for `AuditedBlockState` | 1.5 | A1.5-8 |
| I-11 | Configuration surface and naming for the privileged startup connection in the bootstrap profile, and how its disposal before readiness is enforced | 1.6 | A1.6-5, A1.6-6 |

## 26. Blockers and prerequisites

| # | Item | Blocks | Status |
|---|---|---|---|
| P-1 | Gate approved | AUD-1.1 | **Done** (2026-10-06, with conditions incorporated) |
| P-2 | R-1 answered | AUD-1.1 guard; AUD-1.6 | **Done** (OR-13) |
| P-3 | IAM-10 sequencing (OR-11): IAM-10.0 design may proceed now. IAM-10 implementation that records grant changes begins once AUD-1.1 and AUD-1.2 are delivered and verified. It does not wait for AUD-1.3 to 1.6 or 1.5b. IAM-11 needs IAM-9, AUD-1 and IAM-10 | IAM-10 implementation timing | Ruled |
| P-4 | AUD-1.1 is in the first release containing IAM-9.2 (T-16, §15.6) | Release | Constraint |
| **B-1** | **LST-1.1 not started** | **AUD-1.5b only** | Real blocker |
| P-5 | Explicit start instruction, and a dedicated branch with the owner's confirmation, for each package | Every package, AUD-1.1 first | Standing rule. **AUD-1.1 has not been started** |

## 27. Amendments recorded in authoritative documents

| Document | Note |
|---|---|
| [IAM-9 design gate](../identity-foundation/iam9-design-gate.md) §14 | R14 amended by OR-2 (`external`). IAM-9 closure stays accepted |
| [IAM-9 design gate](../identity-foundation/iam9-design-gate.md) §23 | "LST-1.3 or AUD-1" resolved; pointer to §18 |
| [LST-1 design gate](../list-query-foundation/lst1-design-gate.md) §28 | LST-1.3 surfaces per §18; the "AUD-1 UI adoption" node is AUD-1.5b |
| [LFC-1 design gate](../connection-schedule-lifecycle/lfc1-design-gate.md) §5 | Step 6 superseded by OR-1 |
| [CTX-3B closure](../resource-contexts/ctx3b-closure.md) §6 P2 | Write mode superseded by OR-1; scope unchanged |
| [Program gate](../../rc1-program-gate.md) §5, §8 | IAM-10 sequencing clarification with the hard dependency preserved; F-8 split approved; AUD-1.0 approved |
| Roadmap, pre-release roadmap, `.ai/memory/decisions-index.md`, transitions index | AUD-1.0 approved; AUD-1.1 next, not started |
| This gate, §22 AUD-1.3 | Ownership clarification of 2026-10-07: bulk item retry and attention acknowledge and reopen assigned to AUD-1.3; requirements unchanged |
| This gate, §22 AUD-1.4 | Owner rulings of 2026-10-07: secret audit identifiers (owning entity, never a secret reference), attention routing rules in AUD-1.4 and contact groups in LST-1.4, secret-store transaction participation (amends AUD-1.2 D35), integration delete locking; requirements unchanged |

Not edited: the IAM-9 closure audit and Part 1, which are historical or working records. The `deploy/README.md` correction is AUD-1.1 work.

## 28. Boundaries preserved

- **IAM-9** stays complete, closure accepted. OR-2 is additive.
- **IAM-10** findings stay open:
  - F-1: the governance-read capability checks `user.read` on Role. AUD-1.5b inherits this.
  - F-2: `role.manage` implies broad governance privileges. **This is a known escalation path that conflicts with RC1 requirement 4.** It closes only when IAM-10.2, or IAM-10.0's replacement, ships and is verified, with the security and product owners' disposition recorded before RCV-1 closes. Recording permission diffs in AUD-1.2 is attribution, not a fix and not a disposition. No risk acceptance has been requested or granted.
- **IAM-11** keeps F-5, F-6 and F-7.
- **RCV-1** keeps F-9, F-10 and N-1, including `AuditCorrelationIdSqlServerTests`.
- **RC1 is not complete.**
