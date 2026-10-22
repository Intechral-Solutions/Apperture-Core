# IAM-10.2b WP4B: owner decisions and design reconciliation

| | |
|---|---|
| Package | IAM-10.2b, work package 4B (Recovery Access, MFA, bootstrap writers, the emergency command line, F-1, D-6, R-2), on the verified foundation of [WP1](iam10-2b-wp1-review-record.md), [WP2](iam10-2b-wp2-review-record.md), [WP3](iam10-2b-wp3-record.md) and [WP4A](iam10-2b-wp4a-record.md). Design record only: no production code or test changed. |
| Branch | `feat/iam-10.2b-administrator-viability`, from `873da13`. Not pushed, not merged. |
| Status | The owner ruled on the WP4B design reconnaissance. The rulings below are ratified. Nothing here reopens I-VIABLE, I-FALLBACK, the two views, the WP3 safe post-state proof, the single global gate, C-13-R, C-14, E-1, E-2 or the audited-unit and refusal contracts. |
| Implementation | WP4B-A is implemented: [iam10-2b-wp4b-a-record.md](iam10-2b-wp4b-a-record.md) (handoff: [iam10-2b-wp4b-a-handoff.md](iam10-2b-wp4b-a-handoff.md)); its evidence is summarised in §12. WP4B-D, B and C are not started. |
| Owner clarification (WP4B-A) | Exceptional recovery for RC1: supported procedures first; a controlled manual database intervention only as a documented last resort; no direct-repair logic, bypass flag or `--local-sign-in` override; automated offline recovery is a possible post-RC1 improvement. §3.6 is amended accordingly and supersedes any earlier wording here. No new owner decision. |

## 1. What the reconnaissance found, in one paragraph

Every Recovery Access and MFA writer (designate, revoke, provision, set-password, enrol, reset, regenerate-codes) is reachable only from the emergency command line; the host exposes a read-only posture endpoint and a rename at start. The command line reads its own `appsettings.json` and environment, registers no secret resolver, cannot observe the running host, and its Now view is an estimate copied from its Restart view. `LocalAuth:Enabled` defaults to false and no checked-in configuration sets it. The command line and a host share a machine in practice (both protect TOTP secrets with DPAPI at LocalMachine scope), and a second Management Host is explicitly deferred. The designation flag is clobbered by the generic user update (F-1). The framework offers no way to look at the OpenID Connect options without building them, which is the R-2 constraint on anything that runs at start.

## 2. Reconciliation: the previous proposal and the rulings

| Subject | Reconnaissance proposal | Owner ruling | Effect |
|---|---|---|---|
| Evidence for the command line | Host attestation, with an operator assertion `--local-sign-in` as an outage fallback for revoke and bare reset | Host attestation is authoritative. **The assertion fallback is not approved.** An operator value is diagnostic only and never turns unknown configuration into trusted viability | The ladder has one authoritative rung. Revoke and bare reset can be unavailable during an outage; §3.6 states the consequence |
| Hosts | Combine several live hosts pessimistically | RC1 supports **one** Management Host. Conflicting evidence from several hosts is **unavailable**, not a promise of multi-host support | No merge rule. Two live hosts are a conflict |
| Heartbeat | Fresh within a TTL | Bounded evidence with an explicit stale-observation risk, not instantaneous liveness | §3.5 |
| Entra Now | Established or not yet built | A flag is not proof of a usable handler; an uninitialised handler is **Unknown**; attestation must not force the options to be built | §3.3 |
| Audit | Field `configurationEvidence` | Approved with the canonical values `attested`, `not_required`, `unavailable`; no new action | §4 |
| MFA rotation | One atomic unit (reset and enrol together) | **Two-phase, non-destructive**: stage a candidate, verify possession, then a short atomic switch under the gate; never hold a transaction or the gate across operator input | §5 |
| `UpsertPendingAsync` | Make it conditional | Correct it in WP4B-A | §5.5, handoff |
| F-1 | Remove the flag from `UpdateAsync`, add a compare-and-set setter | Approved, and additionally: examine stale generic writers against the system-managed restrictions | §6 |
| R-2 | Startup order, gate-only bootstrap raisers, build-before-ready record | Approved. A startup attestation may report trusted Local while reporting Entra Now as not yet established | §7 |
| Order | A, D, B, C | One active workstream at a time: **A, D, B, C**; focused security reviews after A and B, full review after C | §9 |

## 3. D-6: Management Host attestation

### 3.1 What is being decided, and what is not

The evaluation reads the deployment's authentication configuration through one interface (`IDeploymentAuthenticationConfiguration`, ruling D-6). In the Management Host that interface reads the host's own running options. In the emergency command line it will read an **attestation the Management Host wrote to the database**, and nothing else. The command line's own `appsettings.json` and environment no longer count as evidence about the installation. They name no installation.

### 3.2 Security boundary (read this before relying on the mechanism)

**What an attestation establishes.** That a Management Host process, using a database principal that can write the attestation table, recorded these readings about itself at a time the database clock can judge, for the database it was connected to, and that no other live host recorded a conflicting reading.

**What it does not establish.**

- It is not proof against a database writer. Anyone who can write the table can forge a row, and anyone who can do that can already edit users, roles and credentials directly. The mechanism protects against **operator error, stale configuration and a command line pointed at the wrong installation**. It is not a security boundary against a database administrator.
- It is not proof that Microsoft sign-in works. A configuration flag is not proof of a usable handler (§3.3).
- It is not instantaneous liveness. A host can stop or reload between its last refresh and the command (§3.5).
- It does not prove the host's configuration files are what a later restart will read. The Restart view is the persisted effective configuration as last attested, and it says so.

### 3.3 Evidence model

Each attestation carries, at least (the shape is for WP4B-B to settle under review, §10):

- **Source**: the value `management_host`, and a schema version. A version the reader does not know is unavailable.
- **Identity**: a stable host id, a process instance id, the time the process started.
- **Binding**: a fingerprint of the database the host was connected to (server and database). The command line compares it with its own connection. A mismatch is unavailable.
- **Freshness**: a refresh time written by the database's clock (never the host's), and a stopped marker written on a graceful stop.
- **Now and Restart, separately.** For each: whether Local sign-in is enabled; the Entra readings; and for each reading its state.

**States of the Entra Now reading** (a flag never stands in for these):

| State | Meaning | Counts as |
|---|---|---|
| Not established | The host has not built its Entra and OpenID Connect options yet. Nothing can be said about what the handler will accept | **Unknown** |
| Degraded | The options were built while the registration or its secret could not be read (WP3 R-1 record) | **Unknown** |
| Established, usable or unusable | The options were built completely; the handler binding was read | Usable or not, with the accepted tenant |

**Local configuration** is a deployment fact the host can read without building anything (`LocalAuth`), so a startup attestation may report it as trusted while Entra Now is still not established (§7).

**The attestation writer must not build the Entra or OpenID Connect options.** It records the reading when the options are built by real traffic, and "not established" until then. A flag from configuration (`Entra:Enabled`) is recorded as a configured value, never as the reading of the handler.

### 3.4 How the command line uses the evidence

Evidence is read **inside the audited unit, after the gate is held**, like every other viability read. This needs an asynchronous reading seam beside the synchronous one the host uses; the interface stays the single point (D-6).

| Evidence | Result for an operation that depends on the input |
|---|---|
| Missing (no row), or not for this database | Unavailable |
| Stale (older than the freshness window by the database's clock), or marked stopped | Unavailable |
| Unknown schema version or an unreadable row | Unavailable |
| Two live hosts (RC1 supports one) or live hosts that disagree | Unavailable |
| Fresh and consistent, input established | Attested |

An operation that does not depend on the missing input proceeds, and its evidence class is `not_required` (§4).

**Operations and the inputs they depend on** (the guard's existing behaviour is kept; this says what must be established for each):

| Command | Depends on | If the input is not attested |
|---|---|---|
| Revoke (C-13-R) | Local sign-in, per view. Entra is not needed (`EvaluateRevocation`) | Refused as `administrator_viability_unavailable` |
| Bare reset of the authenticator | Local sign-in, per view, in the cases where it can reduce | Refused, **unless** the change cannot reduce: if Recovery Access is already not healthy, a reset changes neither the posture nor any administrator, so it is allowed as `not_required` |
| Designate | Local and Entra, per view | Refused, unless a narrowly established proof applies (the WP3 safe post-state proof needs Local established; it is kept as it is) |
| MFA replacement switch (§5) | Nothing beyond the posture, if the proof in §10 holds | See §10 |
| Provision, set-password, enrol (first enrolment), regenerate-codes, bootstrap restore, status | Nothing | Always available |

The WP3 safe post-state proof and equivalent narrowly established Local-only proofs are preserved. They need no Entra reading, and they need Local established; with Local unknown nothing but "inputs unchanged" and the `not_required` cases above can proceed.

### 3.5 Freshness and the stale-observation risk

The host refreshes its attestation about every 60 seconds. A reading is fresh while the database clock says it was refreshed within the window (proposed three intervals; WP4B-B fixes the numbers under review). The risk, stated plainly: a host can stop, restart with different configuration, or have its configuration reloaded **after** its last refresh. A command run inside the window can rely on a reading that is no longer true. The window bounds the staleness; it does not remove it. A graceful stop closes the window at once. A crash leaves it open for at most the window. Nothing in this design makes a reducer safe against a host that changes after the command's read; the existing guard already accepts that for the Now view.

### 3.6 Outage consequence, and recovery

If no host has a fresh attestation, **revoke, bare reset and designate may be unavailable** until trustworthy evidence is restored. This is an accepted operational limitation for RC1. Restoratives are never blocked and never fabricate evidence: provision, set-password, enrolment, regeneration of recovery codes, bootstrap restore and status, each where its own safety and operational prerequisites are met.

**Supported procedures.** The supported way to resolve an outage never edits the database and never uses an operator's assertion of the configuration (there is no `--local-sign-in` override and none is to be added). In this order:

1. Use the supported restorative command-line operations.
2. Repair the deployment configuration, or restore Management Host availability: start or restart the host. It writes its attestation at start (Local established, Entra Now not established). For a reducer that needs Entra Now, let the host serve one request that builds the options, so the reading becomes established (or degraded, which stays unknown until a restart that can read the registration).
3. Verify that another administrator can sign in, and run the normal command line again.

An operator-provided value of the configuration may be shown in the command's diagnostic output so an operator can compare it with the evidence. It is never an input of a decision.

**Exceptional manual maintenance (a last resort, not a procedure of the product).** The owner prioritises secure, maintainable core functionality over automating rare emergency scenarios. If none of the supported procedures can resolve a situation, a **controlled manual database intervention** is permitted as an extraordinary last resort. It is **not** an ordinary application operation and must never be presented as equivalent to a supported command-line mutation: it bypasses the application's locking, validation and audit. The earlier statement of this record that recovery never involves database intervention is withdrawn for this reason; what remains true is that no supported command, flag or automated path edits the database or accepts an operator-asserted configuration. An exceptional intervention requires, before it is made:

- an **offline maintenance window** (the Management Host and every other writer stopped);
- a **verified database identity** (the server and database are the installation's, confirmed independently of the command line's own configuration);
- a **recoverable backup**, restored and checked, taken immediately before;
- an **assessment of administrator viability** before and after (who can sign in, through which method, under which configuration);
- **independent review** of the proposed change by a second person before it is run;
- **integrity verification** afterwards (the filtered unique designation index, the user and role rows, the credential and recovery-code rows, the username ledger), and
- an **external incident record** that names the change, who made and who reviewed it, and the evidence above, because the application's audit table will not hold it.

Nothing in WP4B implements direct database repair logic, a bypass flag or an automated offline recovery. **Automated offline incident recovery is a possible post-RC1 improvement and not a commitment of this plan.** The written procedure for the exceptional case is still to be produced before RC1 (see the WP4B-A record, §Documentation still needed).

### 3.7 Multiple hosts

RC1 supports one Management Host. A second live attestation is a conflict and the evidence is unavailable. This is a safety behaviour, not a statement that two hosts are supported. During a restart of the single host the old and new processes overlap briefly; the same host id means the new process replaces the old row, and a different host id is a conflict until the old row expires.

## 4. Audit: `configurationEvidence`

An additive detail on the success and refusal events of the operations that can reduce (designate, revoke, bare reset, and the replacement switch where it applies). No new action, no schema change to the event table.

| Value | Meaning |
|---|---|
| `attested` | The decision used a fresh, consistent attestation |
| `not_required` | The decision did not depend on host configuration: inputs unchanged, a posture change that cannot reduce, or a post-state proof that needs no missing input |
| `unavailable` | The decision needed host configuration and none could be trusted; the refusal is the existing `administrator_viability_unavailable` |

Rules: the value names the evidence **actually used**, including on a refusal for missing evidence; it is the only configuration detail in the event; no host id, fingerprint, tenant, option value or secret enters the event; the operator's diagnostic value never appears. Events of restorative operations do not carry the detail. Operator-visible messages and event values use product wording and carry no package, ruling or finding labels.

## 5. MFA replacement: the non-destructive lifecycle

### 5.1 Why

Today a reset removes the active authenticator and its recovery codes in one unit, and the enrolment is a second unit that waits for the operator's code. With no Entra-independent administrator the guard refuses the reset (it removes the sole recovery path), and a failed or cancelled enrolment after a reset leaves Recovery Access unhealthy. The replacement workflow never removes the active enrolment until its successor is proven.

### 5.2 Phases

1. **Stage** (no gate, no change to the active enrolment or active recovery codes). Create a protected replacement candidate for the account. The secret is protected as the active one is. The candidate has its own identity, an expiry, and a status.
2. **Verify possession.** The operator presents a code from the new authenticator; it is checked against the candidate's secret and the candidate is marked verified, recording the matched step so a code cannot be replayed. Nothing about the active enrolment changes.
3. **Switch** (the only step that takes the gate; short; no operator input inside it). In one audited unit: take the gate; re-read the active enrolment and the candidate; require that the candidate is verified, unexpired, the one the operator verified, and that the active enrolment is the one that existed when the candidate was staged; replace the active secret and the active recovery codes with the candidate's secret and a new set whose hashes were computed before the unit; delete the candidate; verify that the posture after the switch is what the account needs (healthy, or no worse than before); commit with its audit row.
4. **Show the new recovery codes once**, from the result of the committed unit.

A transaction or the gate is **never held across interactive input**. Phase 1 and phase 2 hold neither.

### 5.3 Candidate: data-model and lifecycle requirements (implemented in WP4B-C, not now)

- A separate store for pending rotation candidates; it never shares a row with the active enrolment.
- At most one live candidate per account (a filtered unique index). Staging a new candidate supersedes the old one in one short statement; a superseded candidate can never be switched.
- Fields: account, candidate id, protected secret and protector key id, created and expiry times, status (staged, verified, superseded, expired), the matched step of the verification, and the version of the active enrolment it was staged against.
- Expiry is enforced at verification and at the switch, by the database clock. An expired or abandoned candidate is inert: it never disables the active authenticator. Purging is lazy (the next stage) and never touches the active enrolment.
- Competing candidates and competing switches: the switch consumes the candidate by id and version with a compare-and-set; the second attempt fails with a typed, retryable conflict and changes nothing.
- A change to the active enrolment between staging and the switch (another switch, a reset, a regeneration of the codes) invalidates the candidate.
- The candidate and its codes appear in no audit detail, log or message.

### 5.4 Interaction with the other contracts

- The switch is judged on its **net** effect. Its posture-only proof (§10) must hold: the account stays healthy, or was not healthy before.
- C-14: consumption of the last recovery code during sign-in takes no gate and is not blocked by a switch in progress. If a code is consumed between the switch's reads, the same attribution rule as the Entra-primary policy applies (the established contract is kept; no false last-administrator claim).
- A **bare reset** remains a reducer under the common invariants and the evidence rules of §3.4. It is not a step of the replacement.
- If the operator's process dies after the switch committed and before the codes were shown, the codes are lost; regenerating them is a restorative command and is always available.

### 5.5 `UpsertPendingAsync` (WP4B-A)

`UpsertPendingAsync` rewrites an existing row and sets `IsEnrolled=false`; `BeginEnrollmentAsync` checks first and writes later, outside any unit. A completed enrolment can therefore be silently un-enrolled by a concurrent begin, which reduces the recovery posture without any guard. WP4B-A makes the pending write conditional on there being no enrolled row (a single atomic statement), so it can never reduce. First enrolment keeps its flow. Replacement of an enrolled authenticator is the lifecycle above, not an upsert.

## 6. F-1: the designation flag

### 6.1 Approved correction

- `SqlUserRepository.UpdateAsync` stops writing the designation.
- One dedicated conditional setter: set the designation to a value only when the stored value equals the expected one, as a single statement, reporting whether a row changed. It is the only way the designation of an **existing** account changes; the other place the designation is ever stored is the row of a **new** account that Recovery Access provisioning adds already designated, arbitrated by the filtered unique index. Called only by the Recovery Access service, under the global gate, inside its audited unit. A `false` result is an audit invariant failure.
- The filtered unique index (`UX_Users_EmergencyAccess`) stays.
- The writer guard stops carrying the three generic-update services as findings, pins that the generic update never names the designation, and pins the single caller of the setter.

### 6.2 Stale generic writers and the system-managed restrictions

The restrictions on a designated account (no display-name, contact-email or avatar change; no password reset; no rename; no ordinary Entra link) are decided from a copy of the user read earlier in the same operation. Removing the flag write stops the **overwrite** but leaves the **bypass**: a copy read before a designation can still pass the check and persist a change to an account that is designated at write time.

| Site | Check made on | Write | Window |
|---|---|---|---|
| `AccountProfileService`, `AccountContactService`, `AccountInvitationService` | A copy read at the start of the unit | `UpdateAsync` of the whole record | Spans password verification and token work |
| `UsernameService.RenameWithResultAsync` | A copy read **outside** any transaction | `ChangeUsernameAsync` | Wide |
| `ForgottenPasswordService` (reset completion) | A read in the unit | Token consumption, then the password write | Short |
| `EntraIdentityAssociationService` (link completion, E-2) | A read in the unit | The link write | Short |
| `UserManagementService` reducers | A read after the gate | Under the gate | **Serialised with a designation only once designation takes the gate (WP4B-C).** The persisted-designation recheck is not what protects these writers |

**Requirement.** An established restriction is enforced against the **persisted** designation at the point of the write, inside the same transaction, with the user's row held against a concurrent designation until the write completes (an update lock on the user row, taken in the check; the designation setter's update conflicts with it, and no writer waits for the gate after holding the row, so no cycle forms). The mechanism for the generic update is: read the row under that lock, compare the designation with the caller's copy, and refuse a mismatch with a typed, non-sensitive conflict. A matching designation proceeds exactly as today, so ordinary profile updates are unchanged. The refusal reuses the established refusal of the site where the account is designated at write time; where the account was revoked in between, the update is refused as stale and is retryable. The same recheck is applied to the rename and the password-reset completion. For the Entra link completion the limitation is recorded and the recheck is added only if it is a local change; E-2 is not widened.

This does not expand the Recovery Access policy: no new restriction, no new code, no changed message for a case that was already refused.

### 6.3 Test requirements (F-1)

Deterministic (statement-level pauses, both isolation modes, real SQL):

- A stale generic update parked after its read, a designate committed, the update resumed: the designation is not cleared, and the update to the now-designated account is refused with the established restriction.
- The same with a revoke committed: the designation is not restored, and the update is refused as stale.
- Designate against revoke: exactly one wins; the filtered unique index is never violated; no audit row for the loser.
- The conditional setter reports `false` on an unexpected stored value and the unit fails as an invariant failure with nothing committed.
- An ordinary profile, contact and invitation update of an undesignated account behaves exactly as before.
- The rename and the password-reset completion each lose a race with a designation that commits between their read and their write.
- Source pins: the generic update does not name the designation; the setter has one caller; the three services no longer appear as findings.
- Mutation checks: restore the flag write; drop the comparison; drop the lock; make the setter unconditional.

## 7. R-2: startup

### 7.1 Sequence (approved)

1. Configuration and service composition; no database access.
2. Bootstrap connection validation (existing).
3. Persistence migrations and data steps (existing; a failure fails the start).
4. Secret-protector validation.
5. Governance startup steps (canonicalisation, module registration, role repair, configured-administrator bootstrap, development bootstrap). **None builds the Entra or OpenID Connect options.**
6. The web host starts listening.
7. The first request builds the options through the post-configure with the registration and its secret, and records the build complete or degraded (R-1).
8. Registration writers prepare the options before opening their unit (R-1, unchanged).

### 7.2 Acceptance requirements

- **Order is pinned.** A test enumerates the hosted services of the real composition and fails unless migrations come before secret validation, which comes before every governance startup step, and the web host service is last. (Both rest today on framework defaults and registration order.)
- **No early build.** A host started against a fresh database, and against an upgraded one, has built no Entra or OpenID Connect options before the first request; the build-health record is empty until then.
- **Build-before-ready is degraded.** A build that happens while the migration state is incomplete is recorded as degraded, so a host composed in another order reads Entra as unknown instead of optimistic.
- **Fresh install is not degraded.** An empty registration table gives a complete build.
- **Unavailable secret store.** A store that cannot be read at the first request gives a degraded build (Entra unknown) until a restart that can read it; startup writers are unaffected because they read nothing.
- **Gate-only is allowed only where proven.** A bootstrap writer is gate-only when the affected user holds no administrator facts before the change (a new user, or a user who does not yet hold the role). Such a writer takes the gate and reads no viability state, calls no `PrepareAsync`, and the writer guard has a class for it with a method-level pin. A writer for which that cannot be shown keeps the capture-and-assert form.
- **Startup attestation.** The attestation written at start reports Local as trusted (a configuration fact) and Entra Now as not established. It never reports Entra as initialised because the process started.
- **Dormant interfaces stay dormant.** The real bootstrap recovery service is registered in the host but has no production caller, and the command line binds the stub (`recovery-unlock` is deferred). Integration of its role assignment adds no caller, registers nothing new in the command line, and a test pins the callers of `IBootstrapRecoveryService`.
- **First-administrator endpoint.** The anonymous endpoint exits lock-free when a user exists, before the unit, so an anonymous caller cannot queue on the global gate.

## 8. Writer classification (as the rulings leave it)

| Writer | Class | Package |
|---|---|---|
| Designate; revoke (strict); bare reset of the authenticator | Reducer, evidence-dependent | C |
| MFA replacement switch | Gated, net-judged | C |
| Provision; enrolment (first); regeneration of the codes | Gated raiser | C |
| Staging and verifying a replacement candidate | No gate; changes no posture input | C |
| `BeginEnrollmentAsync`/`UpsertPendingAsync` | Made non-reducing | A |
| Configured administrator; first administrator; bootstrap recovery | Gated raiser, gate-only where proven | D |
| Generic user update; rename; password-reset completion | Restriction recheck | A |
| Consumption of a recovery code | C-14, unchanged | none |
| Development bootstrap; Entra link addition and proof | E-1, E-2, unchanged | none |

## 9. Work packages and reviews

One active implementation workstream at a time, one commit per package, no push.

| Order | Package | Content | Depends on | Review |
|---|---|---|---|---|
| 1 | **WP4B-A** | F-1 (setter, generic update, stale-writer rechecks, guard); `UpsertPendingAsync`; R-2 (order pin, no early build, build-before-ready record, the gate-only class in the writer guard) | WP4A | **Focused independent security review** |
| 2 | **WP4B-D** | Bootstrap writers: configured administrator, first administrator (lock-free exit), bootstrap recovery's role assignment; no dormant interface enabled | A | Part of the review after B or a short check by the reviewer of A |
| 3 | **WP4B-B** | The attestation: table, host writer, reader, asynchronous seam, evidence classification, `configurationEvidence`; no reducer integrated | A | **Focused independent security review** (provenance, freshness, boundary) |
| 4 | **WP4B-C** | Designate and strict revoke; bare reset; the replacement candidate and switch; provision, enrolment, regeneration as gated raisers; command-line mapping of busy, unavailable, conflict and unknown outcomes, exit codes and a test seam; the writer guard flips WP4B to integrated | A, B | **Full WP4B review** (C-13-R, C-14, the pair matrix on both isolation modes, mutation checks) |

WP5 and the IAM-10.4 presentation are not part of this plan.

## 10. Details left to security review inside WP4B-B (not owner decisions)

These are design details the rulings leave to the work package and its review. None changes a ruling.

1. The refresh interval and the freshness window, and how a graceful stop and a crash are told apart.
2. The attestation table's shape and key; how the host id is derived; the database fingerprint and how the command line computes its own.
3. The handling of overlap during a single host's restart, and of a restored or copied database that still holds old rows.
4. How the "built completely" and "handler binding read" readings are captured without building the options (the hook at the end of a real build), and what a reload does to an established reading.
5. Whether the command line recomputes its Restart view from the database plus the attested deployment base, and how the secret store is read from the command line without a resolver; where that cannot be established, Entra stays unknown.
6. The exact posture-only proofs behind `not_required` (a bare reset of an unhealthy posture; the replacement switch that leaves the account healthy): the predicate, the facts it may read, and the test that it cannot be used for a change that touches any other input.
7. The asynchronous reading seam's shape and the timing of the read relative to the gate.
8. Who may write the table (the host's database principal) and the minimum permission the command line needs to read it.
9. Behaviour when the attestation schema or the reader's version changes in an upgrade.

## 11. Preserved

I-VIABLE and I-FALLBACK in Now and Restart; the WP3 safe post-state proof; the global gate, its order and its busy mapping; C-13-R; C-14; E-1; E-2; the audited-unit, refusal and unknown-outcome contracts; the WP4A policy and its attribution; the legacy deployment-secret fallback (unchanged).

## 12. WP4B-A evidence

The implementation and its full evidence are in the [WP4B-A record](iam10-2b-wp4b-a-record.md) (§9 holds the commands, the numbers, the mutation table and the limits). In short:

- **Builds:** clean, no incremental state, `--artifacts-path artifacts/wp4ba-final`, SDK 10.0.402; unit, integration and command-line host projects, 0 errors.
- **Unit project:** 5,778 passed, 0 failed, 0 skipped (5,741 before). **Focused integration set** (the WP4A filter plus the five new classes, both isolation modes): 1,243 passed, 0 failed, 1 skipped (1,183 before); the skip is the first-administrator concurrency case, skipped on purpose and **not validated** (record §6). The five new classes on the final clean build: 62 passed, 1 skipped. Architecture tests: 38 passed. The full IntegrationTests suite and the Browser/UI suites were not run.
- **Mutation checks:** 14 mutations of production source plus a baseline, each with unaffected controls in the same run, the SQL container shown running before and after, and the source restored byte-identically. All are caught; one (the invitation completion's recheck) first survived the SQL suite and is caught after one more case (record §9.4, M12 and M12b).
- **Findings:** (1) pre-existing and not closed here: two concurrent first-administrator requests with different names both create an administrator, on both isolation modes, until the writer takes the global gate and rechecks under it (WP4B-D); (2) `UserManagementService` is serialised with a designation only once designation takes the gate (WP4B-C); (3) the ratified assumption that the web host starts last held in the real host, with no discrepancy.
- **Tree hashes:** `src` `2e619c72714b9c9e349fc220b0a9caf69e64f178` and `tests/Apperture.UnitTests` `1d85eca6289fc31adcfa00b8d4469e83ac87fa89` throughout validation; `tests/Apperture.IntegrationTests` `7b1e1532…` (full focused run), `f4f1cdce…`, `454a16f4…` (test-only changes after it; the five new classes rerun on each clean rebuild). The commit's own tree is in its message.

## 13. WP4B-A review: findings carried forward

The independent review of WP4B-A (Opus 5.5) found no confirmed defect in the core changes and asked for two low-severity corrections, closed in the remediation commit (see the [WP4B-A record](iam10-2b-wp4b-a-record.md) §13). It also confirmed findings that this package neither introduced nor may close. Each has an owner and a point by which it must be closed. None is implemented before its owner.

| Id | Severity | Finding | Owner | Mandatory closure |
|---|---|---|---|---|
| **M-P1** | Medium (existed before WP4B-A) | Two concurrent first-administrator requests with different names both create an administrator, on both isolation modes: the unit's operation lock is per operation, so it does not serialise them | **WP4B-D** | **Must close before the IAM-10.2b merge.** The committed skipped regression (`FirstAdministratorConcurrencySqlServerTests`) must become an executed, passing test on both isolation modes once the first-administrator writer takes the global gate and rechecks the count under it. Until then an empty installation must not be exposed to concurrent anonymous bootstrap requests |
| **L-P2** | Low (existed before; independently verified) | `MarkEnrolledAsync` is unconditional. A begin that lands between an enrolment's code check and its write leaves the account enrolled on a secret nobody verified; the row version does not cover that window | **WP4B-C** | Establish an atomic correspondence between the verified authenticator candidate and the committed enrolment: a conditional update, a version or a candidate identity, so a stale verification cannot enrol an unverified secret. Deterministic SQL tests on both isolation modes. This is **in addition to** the approved non-destructive replacement workflow (§5) |
| **L-P3** | Low (documented) | `UserManagementService` reducers (role, credential, link, set-password, username paths) are serialised with a designation only once designation takes the global gate | **WP4B-C** | Designate and revoke take the gate (and the viability decision). Until then §6.2's "closed" applies to the persisted-designation recheck only, not to serialisation with the gate-holding writers |

Wording corrections made with the remediation: the designation has **one setter for an existing account**; provisioning also stores the designation of a **new** account it adds already designated (arbitrated by the filtered unique index) (§6.1); the gate-only entry still needs the database (the gate is a database application lock) and is distinguished from the Entra options and the secret store (§7); the operator-facing reason for a degraded options build no longer blames only an unreadable registration or secret.

## 14. Next checkpoint

After the WP4B-A remediation is independently accepted and **before WP4B-D begins**, the program takes a pause for (1) a general Apperture Core progress report and (2) an operational smoke test of a **running** application and representative end-to-end workflows (not a repeat of the unit and integration suites). Neither is part of the remediation. The smoke environment must be isolated, and it must not expose an empty installation to concurrent anonymous bootstrap requests (M-P1 stays open until WP4B-D). The repository state it needs is preserved: the branch at the remediation commit, the built validation outputs under `artifacts/`, and the stash and worktree.
