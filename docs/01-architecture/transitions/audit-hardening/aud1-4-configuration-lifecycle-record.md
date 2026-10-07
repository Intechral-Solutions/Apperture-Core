# AUD-1.4 configuration lifecycle: implementation record

Date: 2026-10-07

Status: **In progress on branch `aud-1.4-configuration-lifecycle`; not pushed, no PR.** Slice 0 (this record and the gate clarifications) is done. Slice 1 (secret-store transaction participation) is in progress. Slices 2 to 8 have not started. This record grows with each slice; nothing below marks a later slice complete.

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
| 1 | Secret-store transaction participation (D-3); the Secrets-category catalog and producer convention (D-1); the Entra registration flows adopting both | **In progress** |
| 2 | Platform and integration settings: one unit per set, clear and reset; the live-secret-first defect; an in-transaction reset that integration delete can call | Not started. Independent review of slices 1 and 2 before slice 3 |
| 3 | Schedule delete and integration delete as W1 with verification (M6 each, including lost acknowledgement with a missing target); D-4 | Not started. Independent review |
| 4 | Integration create, update, activate, deactivate, retire; bindings create and delete; schedule create (with its assignment), update, enable, disable | Not started |
| 5 | Connections create, update, rotate, retire, verify (`external_attempt`), browse (W2); the connection rotation live-secret defect; resource contexts create, update, archive, unarchive; ownership moves gain verification | Not started |
| 6 | Notification definitions (create, update, enable, delete), subscriptions, managed contacts, diagnostics live sends (W3 `external_attempt`) | Not started |
| 7 | Attention routing rules (D-2); module lifecycle (W1, with W3 for blocked attempts); the runtime shutdown request and its duplicate case | Not started |
| 8 | Closure: the converted-writer scan test, catalog pins, `docs/04-api/contracts.md`, integrated validation and independent review | Not started |

## 4. Slice 1: secret-store transaction participation

*To be completed with the implementation, files, transaction boundaries, Entra assessment and results.*

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
| `SqlSecretResolver` echoes an invalid ref value in its exception text; the `PlatformSettingUpdatePayload` record's generated `ToString` would print its secret value; the SMTP test recipient is logged | RCV-1 or optional cleanup; guarded here only where a slice touches it |
| `docs/04-api/contracts.md` calls the shutdown audit action `runtime.shutdown.request`; the catalog name is `governance.runtime.shutdown.request.created` | Corrected in slice 7 |

IAM-9 stays closed. The IAM-10, IAM-11, OPS-1 and RCV-1 findings stay open. RC1 is not complete.
