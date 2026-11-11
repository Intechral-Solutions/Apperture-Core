# IAM-10.2b WP5: security and concurrency closure

| | |
|---|---|
| Package | IAM-10.2b, work package 5 ([WP0 reconciliation](iam10-2b-wp0-reconciliation.md) §10): the full pair matrix on both isolation modes, the guard enforced for every writer, mutation checks and this closure record, over the integrated implementation (WP1 to WP4B). |
| Branch | `feat/iam10-2b-wp5`, linked worktree `F:\Code\Apperture\wt-iam10-2b-wp5`, from the accepted IAM head `1c8067cd5ae11fc29285d21853a18c32d52beac1` (root tree `06c88a1d`, `src` `c83ce3ca`, `tests` `aa7b74e7`). **Local only**: not pushed, no pull request. The protected `feat/iam-10.2b-administrator-viability` is unchanged at `1c8067cd`. |
| Code head | `acf928418a8c4e59de3e5c115d33ae9d4d088080` (root tree `f7e72a9a`, `src` `19d6c1b1d962136292799863a8a710afe955b593`, `tests` `138b07b2ac00832e792301205417af5dbb898709`). The documentation commits that carry this record, the review brief and the owner's rulings follow it and change neither tree. |
| Status | Implemented and validated (§9). **Not independently reviewed**; the brief is [iam10-2b-wp5-review-brief.md](iam10-2b-wp5-review-brief.md). The owner's rulings of 2026-11-10 are recorded in §10.1.1 to §10.4: D-C and D-D Option A for RC1; no change to refusal rows; the constructor clean-up deferred, **contingent on the independent review confirming** that the production host cannot silently bypass MFA; the three production changes retained, subject to that review. Three wording points in the rulings are reported for the owner, not reinterpreted (§10.1.1). |
| Not in scope | IAM-10.3, IAM-10.4 (how the UI presents a refusal or a deferral), IAM-10.5, IAM-11, OPS-1 servicing, development-environment work, refactoring of accepted code, the constructor clean-up of §7 (deferred), any new audit action, log requirement, counter or throttle (§10). |

## 1. The WP5 contract, reconciled

WP0 defines WP5 as "the full pair matrix on both isolation modes; the guard enforced for every writer; mutation checks; the closure record", with an independent security review of the integrated implementation. The binding acceptance is the design gate's A10.2b.1 to A10.2b.17 ([design gate](iam10-design-gate.md) §7), including its preamble: every concurrency test runs on a locking READ COMMITTED catalog and a READ_COMMITTED_SNAPSHOT catalog, uses a deterministic interleaving, and must fail with the gate removed. These items were assigned to WP5 or found by it:

| Source | Item | Disposition here |
|---|---|---|
| WP0 §10, design gate §7 | The full pair matrix, both modes | §3: the matrix and its gaps, closed with new cases |
| WP1 review §2 | Writer-guard hardening ("WP2, then WP5") | §5.1 and §5.7: probes, hardening, re-probes |
| WP1 review §2.1 | R-2 resolved by a test before the WP4B review and "recorded in the WP5 closure record" | §6, item 8: recorded |
| WP2 review §5, WP3 §7 | Optional reason enhancement for refusal rows ("left for WP5 consideration; no owner decision is needed now") | §10.2: considered, not implemented, offered to the owner |
| WP4A §7 | D-A: the policy against activation and switch | §3, new cases (`DA_*`) |
| WP4A §7 | D-B: the policy twice for one user | §3, new cases (`DB_*`) |
| WP4A §7 | D-C, D-D: measure, then an owner ruling before this record | §4 measured; §10.1.1 the owner's ruling, recorded 2026-11-10 |
| WP4A §7 | D-E: presentation of multiple Entra links; "WP5 records that enforcement does not use the presentation" | §6, item 9: recorded as a known limitation until IAM-10.4 |
| DEV-1 integration record §11 | The above, restated | as above |
| Services record §15.7, CLI record §1 | SI-1: a busy gate on the administrators' set-password action returns 500 | §5.2: a deviation from A10.2b.15, closed |
| CLI final acceptance (2026-11-10), L-1 | The staged key's one display is claimed, not asserted | §5.4: the assertion added |
| Consolidation audit (2026-11-10) | The optional `INativeMfaService` of `AuthController` and `UsersController` | §7: production composition shown safe; the hazard of a dropped registration demonstrated and pinned by a sign-in test (§5.9); clean-up deferred |
| **WP5 evidence mapping** of A10.2b.1 to A10.2b.17 against the integrated tree | Clauses no package had exercised: A10.2b.12's presentation (**a defect**) and its regeneration clause; A10.2b.17 and A10.2b.15 for the WP4B-C units, provisioning's initial role and the sign-in policy; A10.2b.5 for setting a password and proving a link; A10.2b.10's "every"; A10.2b.16's generated corpus; C-14 against the switch through the services | §2; §5.5; §5.8 |
| **WP5 pre-hand-over review** (an independent reading of the diff `1c8067cd..6f779f93`) | C1 to C3 (ways around the writer guard), C4 (the set-password endpoint maps an audit invariant failure to 400 or 404), I1 (most concurrent cases detect a removed gate through the harness, not through outcomes), I2 (an unasserted "no refusal row") | §5.6, §5.7, §3.3 |

### 1.1 Classification

| Class | Items |
|---|---|
| **Satisfied by accepted earlier work** (re-run on the integrated tree, §9) | A10.2b.2, A10.2b.3, A10.2b.4, A10.2b.6, A10.2b.7, A10.2b.14 (with WP0's E-1 and E-2 departures and WP3 §7.1); the parts of A10.2b.1, A10.2b.5, A10.2b.8 to A10.2b.11, A10.2b.13, A10.2b.15 to A10.2b.17 that §2 attributes to WP2 to WP4B; the WP4B-C service and command-line contracts (D-1 to D-4, U1 to U7, S-1 to S-5, SC-1 to SC-5, SR-1, L-P2, L-P3, the recovery-code fix) |
| **Needed new WP5 implementation or evidence** | The pairs never run (§3); the writer-guard hardening and its second round (§5.1, §5.7); SI-1 (§5.2); **the A10.2b.12 presentation defect** (§5.5); review C4 (§5.6); the acceptance clauses of §5.8; the D-C and D-D measurement (§4); L-1 (§5.4); the `INativeMfaService` composition evidence and its sign-in control (§7, §5.9); mutation checks on the integrated tree (§8) |
| **Deferred RC1 operational checks** | §11 |
| **Genuine owner decisions** | §10 |

## 2. Requirements to evidence

Every test named here ran on the integrated tree in the full assembly run of §9, on both isolation modes unless marked U (unit), UI (rendered component) or G (source guard).

| # | Evidence | Origin | Status |
|---|---|---|---|
| A10.2b.1 | One commit and one refusal (or deferral) with its existing row, and the same two in sequence: person × person `AdministratorViabilityWp2SqlServerTests.A1_*`; registration × person `EntraRegistrationViabilityWp3SqlServerTests.A1_*` and the administrator-first twin; policy × person `EntraPrimaryPolicyViabilitySqlServerTests.X1_*`, × registration `X2_*` and `DA_*`, × Entra link `EL_*`; registration × registration and × Entra link `W5_*` | WP2, WP3, WP4A; twins and new families WP5 | met |
| A10.2b.2 | `…Wp2…A2_the_sequential_hole_is_closed_…` | WP2 | met |
| A10.2b.3 | `…Wp2…A3_two_assigned_administrators_when_only_one_can_sign_in_…` | WP2 | met |
| A10.2b.4 | `…Wp2…A4_recovery_access_is_never_counted_…`; asserted again in every WP5 family's world ("Recovery Access is never counted") | WP2, WP5 | met |
| A10.2b.5 | `…Wp2…A5_when_nobody_can_sign_in_…`, `A5_a_no_op_for_the_last_viable_administrator_is_silent_…`, `A5_changes_that_can_only_raise_viability_are_never_refused_…` (enable, assign, add a link); **setting a password (host and command line) and proving a link**: `AdministratorViabilityPairMatrixSqlServerTests.A5_setting_a_password_and_proving_a_link_are_never_refused_…` | WP2, **WP5** | met |
| A10.2b.6 | `…Wp2…A6_*` (Entra and Local link of the last viable administrator) | WP2 | met |
| A10.2b.7 | `…Wp3…A7_*` (refused with a typed 409 beside the `sign_in_lockout` refusal of `ValidateNoLockout`, evaluated on the proposed configuration), host `EntraRegistrationViabilityHostHttpTests` | WP3 | met |
| A10.2b.8 | `…Wp2…A8_*` (one Entra-independent administrator: credential, Local link, role); "with two, the first allowed and the second refused" for all three reductions: the sequential twins of `F1_*` | WP2, WP3, **WP5** | met |
| A10.2b.9 | `F1_*` (person × person under I-FALLBACK), `…A9_*` (the policy for two administrators), `DB_*` (the policy twice for one), `F2_*` (designation as a reduction) | WP4A, **WP5** | met |
| A10.2b.10 | `Healthy_recovery_access_satisfies_I_FALLBACK_…` (one administrator); **every administrator**: `A10_with_healthy_recovery_access_the_policy_disables_…_including_the_last` and `A10_with_recovery_access_not_healthy_…_defers_that_one` | WP4A, **WP5** | met |
| A10.2b.11 | The WP4B-C refusals (C-13-R, the missing flag first, one W3 row, the command line's exit and message); races with every EI reduction: `F3_*`, `F4_*`, `F5_*`, and the Recovery Access writers against each other `F7_*` | WP4B-C, **WP5** | met |
| A10.2b.12 | Never blocked, no gate, the existing row: `F8_*`, `A12_*`; **the presentation says the codes are exhausted and names the remedy** (a defect, fixed in §5.5): `EmergencyCommandRunnerTests.The_status_says_the_recovery_codes_are_exhausted_…` (U), `AuthOverviewRecoveryCodesRenderTests` (UI); **regeneration in that state restores health, and I-FALLBACK governs before and after**: `A12_the_last_recovery_code_is_used_without_the_gate_…` (the status and the regeneration through the command line's production container) | **WP5** | met (defect fixed) |
| A10.2b.13 | `F9_*` (a host writer, the policy at a real Entra sign-in, and the command line's production container on its own connection serialize on the one gate, whichever holds it); `A13_*` (a writer that takes only row locks never waits for the gate); disjoint lock sets: §3.3 | WP2, WP3, **WP5** | met |
| A10.2b.14 | `AdministratorViabilityWriterCoverageGuardTests` (G), hardened twice in WP5 with 18 probe shapes (§5.1, §5.7); `A14_*` (the gate is the first lock after the operation lock, before any authority row lock or write); `No_production_code_uses_snapshot_transaction_isolation` (G) | WP1 to WP4B, **WP5** | met |
| A10.2b.15 | Busy 409 with `retry`, nothing changed, the retry succeeds: `…Wp2…A15_*`, `…Wp3…A15_*`, host `A_busy_gate_is_409_…_for_every_endpoint_…` and the registration and policy endpoints, the SI-1 host case, the command line's `A_busy_gate_is_a_retryable_conflict_…` and `Regeneration_with_a_busy_gate_…`, the bootstrap and first-administrator writers; **every covered WP4B-C unit**: `U15_*`. Return codes: 0 and 1 by every case (1 is "granted after waiting", which every waiting second writer receives; M19), -1 (`PlatformAdministratorSetGateSqlServerTests`), -3 (`A_deadlock_victim_waiting_for_the_gate_…`), -2 and -999 by the mapping (U) | WP2 to WP4B, **WP5** | met; -2 and -999 at U only (§2.1) |
| A10.2b.16 | `AdministratorViabilitySqlServerTests.Recovery_access_health_agrees_with_the_existing_posture_over_every_combination_of_its_inputs` (72 combinations); the snapshot and `IsUsable`: the recorded difference (WP0 F-2, `Now_reports_when_the_snapshot_and_is_usable_disagree_…`, U) and **a generated corpus of 3,456 running options**: `Over_a_generated_corpus_the_snapshot_and_is_usable_differ_only_on_an_invalid_authentication_context_…` (U) | WP1, **WP5** | met |
| A10.2b.17 | M6 for the WP2 units (role removal and assignment, credential disable, Local link removal and addition), the WP3 units (registration create, edit, delete, activate), the policy (WP4A); a refusal after the write (`M6_a_refusal_made_after_the_write_…`, `A17_*`); **the seven covered WP4B-C units** (`U17_*`) and **provisioning's initial role and the sign-in policy** (added to the WP2 and WP3 theories) | WP2 to WP4A, **WP5** | met |

### 2.1 Known limits of the evidence

- **`sp_getapplock` results -2 and -999** (cancelled, error) cannot be produced on demand by SQL Server; their mapping is pinned by the unit tests of `PlatformAdministratorSetLock`, and a cancelled wait is shown to surface as the caller's cancellation (`A15_cancelling_the_wait_…`).
- **The command line's `set-password`** is an audited unit but not a covered one: setting a password never changes whether a credential is disabled, so the writer inventory classes it out of category and it takes no gate (the Management Host's set-password takes the gate only for its designation recheck). A10.2b.17 is therefore not run for it; A10.2b.5's case shows it is never refused.
- **Statement order at SQL** is captured for the WP2, WP3 and WP4A writers (`A14_*`, `L1_*`) and for the WP4B-C candidate path (`MfaReplacementCandidateLockOrderSqlServerTests`); for the other WP4B-C units the order (gate, decision, write, verify) is pinned statically by the guard's sequence checks and behaviourally by `U17_*` (the gate taken once per attempt and again on a rerun).
- **E-2 and C-14** take no gate by ruling; they are covered as raisers (E-2) and as a non-refusable change (C-14), not as paired reducers.
- **The writer guard is lexical** (§5.7, residual).

## 3. The concurrency pair matrix

### 3.1 The writers and what they can reduce

| Code | Writer (composition) | Can reduce |
|---|---|---|
| RR | Remove the administrator role (host) | VOA, EI |
| CD | Disable a Local credential (host) | VOA, EI |
| LL | Remove a Local link (host) | VOA, EI |
| EL | Remove an Entra link (host) | VOA |
| EP | Entra-primary policy, at sign-in or self-link (host, system-driven) | VOA, EI (defers instead of refusing) |
| RU, RD, RS, RT | Registration edit or disable, delete, activate or switch, change of tenant (host) | VOA (never EI: WP3 §1) |
| DS | Recovery Access designation (command line) | VOA, EI (the designee leaves both; D-2) |
| RV | Strict revocation (command line) | the strict rule C-13-R (an Entra-independent administrator other than the revoked account must remain) |
| BR | Bare reset of the authenticator (command line) | `RecoveryHealthy` |
| SW | Replacement switch (command line) | nothing (judged on its net posture effect; I-R3-1) |
| C14 | Recovery-code consumption at sign-in | `RecoveryHealthy`; **takes no gate and is never refused** (ruling C-14) |

Raisers (role assignment, credential re-enable, link addition, provisioning's initial role, registration create, Recovery Access provisioning, first enrolment, regeneration, the bootstrap writers) and the sign-in policy cannot reduce anything; they take the gate and assert raise-only (WP2 to WP4B-D, and `U15_*`, `U17_*` here). They are not paired: no pair of them with anything can leave a violation that the reducer alone would not. E-2 (link addition and proof at sign-in) and C-14 are ungated by ruling.

### 3.2 The matrix

"Both" means both orders, on both isolation modes, each concurrently (the first writer parked after its decision, holding the gate; the second shown waiting on the application lock and on no row; the second decided on what the first committed) and as a sequential twin, unless stated.

| Family | Scenario | Pairs | Coverage | Tests | Origin |
|---|---|---|---|---|---|
| Person × person, I-VIABLE | Recovery Access healthy | {RR, CD, LL, EL}², 16 ordered | both | `AdministratorViabilityWp2SqlServerTests.A1_*` | WP2 |
| **Person × person, I-FALLBACK (A10.2b.9)** | Recovery Access designated, **not healthy**, never counted; two Entra-independent administrators who also sign in through Microsoft; an Entra-only one keeps I-VIABLE satisfied | {RR, CD, LL}², 9 ordered | both | `AdministratorViabilityPairMatrixSqlServerTests.F1_*` (36) | **WP5** |
| Registration × person | One Entra-only administrator on R1, one Local | {RU, RD, RS, RT} × {RR, CD, LL} | both; the administrator-first twin added | `EntraRegistrationViabilityWp3SqlServerTests.A1_*` | WP3, **twin WP5** (24) |
| **Registration × registration, registration × Entra link** | A on R1 (Tenant), B Entra-only on the inactive R2 (another tenant) | RS × {RU of R2, RD of R2, EL of B}; RT × EL of B | both | `EntraRegistrationViabilityWp3SqlServerTests.W5_*` (32) | **WP5** |
| Policy × policy, two users (A10.2b.9) | No Recovery Access, two both-ways administrators | EP × EP | both | `EntraPrimaryPolicyViabilitySqlServerTests.A9_*` | WP4A |
| **Policy × policy, one user (D-B)** | The policy twice for the same user, directly and through a real Entra sign-in | EP × EP | concurrent, both modes; twin | `…DB_*` (6) | **WP5** |
| Policy × person (I-FALLBACK) | | EP × {RR, CD, LL} | both; twins added | `…X1_*` | WP4A, **twins WP5** (12) |
| Policy × registration | Recovery Access healthy | EP × {RU disable, RD} | both; twins added | `…X2_*` | WP4A, **twins WP5** (8) |
| **Policy × registration (D-A)** | Recovery Access healthy | EP × {RS, RT} | both | `…DA_*` (16) | **WP5** |
| **Policy × Entra link removal** | X the only administrator, both ways | EP × EL (same user) | both | `…EL_*` (8) | **WP5** |
| Designation × credential disable, I-VIABLE | two Local-only administrators | DS × CD | concurrent, both orders and modes | `RecoveryAccessViabilityServiceSqlServerTests.A_designation_and_the_disablement_…` | WP4B-C |
| **Designation as an EI reduction (A10.2b.9, D-2)** | nobody designated; A, B both ways; E Entra-only | DS(A) × {RR, CD, LL, EP}(B) | both | `AdministratorViabilityPairMatrixSqlServerTests.F2_*` (32) | **WP5** |
| **Strict revocation × EI reduction (A10.2b.11, C-13-R)** | healthy Recovery Access; A the one Entra-independent administrator; E Entra-only | RV × {RR, CD, LL, EP}(A) | both | `…F3_*` (32) | **WP5** |
| **Bare reset × EI reduction (A10.2b.11)** | as above | BR × {RR, CD, LL, EP}(A) | both | `…F4_*` (32) | **WP5** |
| **Switch × EI reduction (A10.2b.11)** | as above, a verified candidate staged | SW × {RR, CD, LL, EP}(A) | both | `…F5_*` (32) | **WP5** |
| Recovery Access × Recovery Access | | DS→RV, SW→RV, DS→BR, SW×SW (one candidate), set-password→DS | one order each, concurrent, both modes | `NativeMfaServiceViabilitySqlServerTests`, `RecoveryAccessViabilityServiceSqlServerTests`, `RecoveryAccessDesignationSqlServerTests` | WP4B-C |
| **Recovery Access × Recovery Access, the other orders** | healthy Recovery Access; A and Next both ways | RV→DS, BR→DS, RV→BR, BR→RV, SW→BR, BR→SW, SW→DS, DS→SW, RV→SW | both modes, concurrent and sequential | `…F7_*` (36) | **WP5** |
| C-14 × policy | | C14 between the policy's reads | both modes | `EntraPrimaryPolicyViabilitySqlServerTests.L1_*` | WP4A |
| **C-14 × administrator writer** | one unused code left | C14 while CD holds the gate (after its decision); C14 between CD's reads; C14 then CD | both modes | `…F8_*` (6) | **WP5** |
| **C-14 × switch** | a verified candidate staged | C14 with an old code while SW holds the gate; C14 then SW | both modes | `…C14_*` (4) | **WP5** |
| **One gate across compositions (A10.2b.13)** | | CD (host) + EP (a real Entra sign-in) + BR (the command line's production container, its own connection): the host writer holds; the command line holds | both modes | `…F9_*` (4) | **WP5** |

**Expected results, and why each is the serial one.** F1: the second reduction is refused `entra_independent_fallback_required` with its writer's `auth_admin.blocked.*` row. F2: whichever of the designation and the reduction is decided second is refused `entra_independent_fallback_required` (designation: `emergency.auth.blocked.administrator_viability`, operation `recovery_access.designate`), or the policy defers with its attempt row. F3 is order-dependent and both orders are serial outcomes: revocation first leaves the former Recovery Access account an ordinary Entra-independent administrator (D-1), so the reduction of A then commits too; the reduction first leaves no Entra-independent administrator other than the account being revoked, so the strict revocation is refused (operation `recovery_access.revoke`). F4: the second is refused, or the policy defers. F5: both commit in both orders, the switch decided on the posture alone (`not_required`), never refused `administrator_access_changed`, and Recovery Access stays healthy. F7: each second change is decided on the first's committed state (a reset of an account no longer Recovery Access needs no evidence; a switch after a reset is `authenticator_not_enrolled` and writes nothing). F8: the sign-in completes while the gate is held (C-14 takes no gate); a disable decided before the consumption commits, giving the state of the serial order "disable, then sign-in", which C-14 accepts; a consumption between the disable's two reads makes the disable refuse, as the serial order "sign-in, then disable" would. C-14 × switch: a sign-in with an old code while the switch holds the gate asks for no gate (no application-lock waiter); it waits, if at all, on a code row the switch replaced, and once the switch commits the code is no longer valid (the serial order "switch, then sign-in"); used before the switch, the code is consumed and audited and the switch replaces the set. F9: whichever composition holds the gate, the other two queue on it (two application-lock waiters, no row waiter) and each is decided on what was committed before it.

### 3.3 What a concurrent case shows, and the gate-removed controls

Every concurrent case asserts that the first writer holds the gate (`sp_getapplock` probe returns -1) and that the second waits on an application lock and on no row, so a result cannot come from disjoint row locks by accident.

**How a removed gate is detected (pre-hand-over review I1).** With `_gate.AcquireAsync` removed (M01), the concurrent cases fail on those harness assertions (the parked writer no longer holds the gate), not on their outcomes: under locking READ COMMITTED most pairs would give the same outcomes without the gate, because the second writer's viability reads wait on the first writer's uncommitted rows, and F5, some F7 sequences and the raced W5 edit give the same outcome in both modes. The harness assertion is a valid control of "the writer takes the gate", but the **outcome-level** evidence that the gate is what serializes each family is a separate set of controls. Before WP5 it was one pair; WP5 adds one per family:

| Control (gate removed, READ_COMMITTED_SNAPSHOT) | Result |
|---|---|
| `Control_without_the_gate_a_bare_reset_and_the_disable_…` (BR × CD; both modes) | under row versioning both commit and EI is empty with Recovery Access unhealthy; under locking READ COMMITTED the disable waits on a row (not the gate) and is refused |
| `Control_without_the_gate_under_row_versioning_…(TwoDisables)` (F1) | both commit; EI empty; Recovery Access not healthy |
| `…(DesignationThenDisable)` (F2) | both commit; EI empty; the designee's Recovery Access not healthy |
| `…(DisableThenRevocation)` (F3) | both commit; no Entra-independent administrator other than the revoked account (the state C-13-R excludes) |
| `…(PolicyThenDisable)` (policy × person) | both commit; EI empty |

The WP2 (three pairs), WP3 (three) and WP4A (policy × policy) controls are unchanged.

**Three behaviours asserted as they are (not defects).**

1. **D-B under locking READ COMMITTED.** The policy's lock-free first read (the steady-state exit) waits on the first application's uncommitted credential row, so the second application is serialized by that row before it reaches the gate; under row versioning it reads the committed (enabled) credential and queues on the gate. Either way it then finds the credential disabled (`AlreadyDisabled`, no row).
2. **A registration edit raced by a switch of the same registration.** Under locking READ COMMITTED the edit's advisory existence read, made before its unit (the 404 check), waits on the row the parked switch changed, before the gate. Under row versioning it reaches the gate, and once the switch has committed the edit carries a row version its operator loaded before the switch: it is the established `409` "modified elsewhere" (`DbUpdateConcurrencyException` inside the unit; nothing written, and **no refusal row, now asserted**, review I2). Run one after the other, the edit is decided on viability (`409 last_viable_sign_in_method`). Both are serial outcomes; the edit requires the operator's row version, so the stale edit is refused correctly.
3. **Disjoint lock sets (A10.2b.13).** Two viability writers can never hold disjoint read sets under locking READ COMMITTED, because each reads every administrator's facts; the per-actor and per-target locks of P-5 are shown not to serialize by the row-versioning controls above (both writers commit without the gate).

## 4. D-C and D-D: measurement

`EntraPrimaryPolicyBusyVolumeSqlServerTests` drives successful Entra sign-ins exactly as the host completes them (`EntraIdentityAssociationService.CompleteSignInAsync`: the proven link's proof refreshed, then the policy applied; this is the per-sign-in caller, the resolver applies the policy only when it proves a pending link) on both isolation modes, and writes the figures to the test output. Measured at `b1f2e6d3`:

| Case | Locking READ COMMITTED | READ_COMMITTED_SNAPSHOT |
|---|---|---|
| 8 concurrent sign-ins with a Local credential still to disable, gate held, a 1 s bound | 8 warnings ("another change to administrator access is in progress"), 0 audit rows, 0 failed sign-ins; each held 1,098 to 1,173 ms | 8 warnings, 0 rows, 0 failed; each held 1,042 to 1,049 ms |
| The next sign-in of each, gate free | applied (8 success rows), 0 warnings | the same |
| The same 8, nothing left to disable, gate held | 0 warnings, at most 40 ms (the lock-free exit never asks for the gate) | 0 warnings, at most 41 ms |
| One sign-in against a gate held past the **production bound (15 s)** | succeeded after 15,032 ms with one warning | 15,031 ms, one warning |
| 8 administrators' sign-ins with the Local setting unknown (D-D) | 8 warnings ("the sign-in configuration could not be established"), 0 rows, 0 failed; a second round: 8 warnings again | the same |

What this says for the ruling: the volume is exactly one warning per affected sign-in, and only a sign-in that still has a Local credential to disable is affected (a transitional population: once applied, a user's sign-ins never touch the gate). Nothing durable is written. The material cost is **latency**: while another change to administrator access holds the gate, an affected sign-in waits up to the bound (15 s) before it proceeds. The repetition is bounded by sign-in frequency and stops once the policy applies (D-C) or once the configuration is established (D-D).

**Caveats** (pre-hand-over review I3, which tried five broken variants of the policy and found each caught): the D-C sign-ins are of non-administrators (the D-D case uses administrators); the warnings are counted on the caller's logger, where the outcome logging writes them; "no row" is checked for the two actions the policy could write; the 1 s case has no upper bound on the hold (a policy that retried the busy gate would pass it, and the 15 s case's 30 s cap catches one retry); the steady-state bound (under 1 s) is wide of the measured 41 ms.

## 5. Changes

### 5.1 Writer-guard hardening (test-only, `03b9fe6f`)

**Probes.** Nine probe files, each placed alone in `src` and run against the guard from the already-built unit assembly (no rebuild, so a detection can only come from the probe), before and after. Before: the control (P00, a link removal through `_userRepository`) and P04 (the designation setter through a parameter named `users`, caught by its single-caller pin) were detected; **seven escaped the guard and the whole unit assembly** (6,380 passed): P01 a link removal through a store named `_users`; P02 a link addition through a parameter; P03 an authenticator reset through stores named otherwise; P05 an EF bulk `ExecuteUpdate` of `LocalCredentials` outside the persistence layer; P06 a raw SQL delete of `UserRoles`; P07 a tracked removal of `IdentityLinks`; P08 `var` locals resolved from a service provider. No production file has such a writer today (`git grep`).

**Hardening.** (1) Writes through the user, authenticator and recovery-code stores are counted by the receiver's declared type (contract or implementation), by `var` locals initialized from the type (`GetService`, `GetRequiredService`, `CreateInstance`, `new`, plain or namespace-qualified) and by the conventional names as before; the file-level scan and the method-level order checks share these counters. (2) A new fact: nothing outside the persistence layer touches a viability table's set on a context, writes through the users set, names a viability entity, or holds a SQL statement that writes a viability table. (3) A new fact: every member of every viability store contract is pinned and classified, so a write method added to a store fails until it is counted. (4) A self-test with every probe shape and the non-writer shapes. Every inventory count is unchanged.

**After.** All nine probes detected, each failure naming the probe file. The first version of the hardening missed P08's namespace-qualified generic argument; the re-probe found it and the pattern was corrected before the commit.

### 5.2 SI-1 (production, `07fe0b54`)

`UsersController.SetLocalPassword` now maps the typed governance outcomes through `GovernanceFailureResults`, as the role, credential and identity-link endpoints already did. Setting a password has taken the administrator-set gate since WP4B-C (L-P3), but its endpoint did not catch `PlatformAdministratorSetBusyException`, so a held gate reached the host's exception handler as a **500**; P-10 and A10.2b.15 require **409 `governance_operation_busy`** with `retry: true` and nothing changed. Every other gate-taking endpoint was checked and already maps busy (§6, item 1). `docs/04-api/contracts.md` names the endpoint. Host test, both modes: `AdministratorViabilityHostHttpTests.A_busy_gate_on_setting_a_local_password_is_409_…` (409 with `retry`, password and audit unchanged, then the same request is 204 with one row). The same action's mapping of an audit invariant failure is §5.6.

### 5.3 Tests added in the first round (no production change)

The pair matrix (§3): `AdministratorViabilityPairMatrixSqlServerTests` (212 cases), 50 cases in `EntraPrimaryPolicyViabilitySqlServerTests` (`DA_`, `DB_`, `EL_`, the X1 and X2 twins), 56 in `EntraRegistrationViabilityWp3SqlServerTests` (`W5_`, the administrator-first twin), all on both isolation modes. The measurement (§4): `EntraPrimaryPolicyBusyVolumeSqlServerTests` (6). The SI-1 host test (2). L-1 (§5.4).

### 5.4 CLI review L-1 (`6f779f93`)

`NativeMfaServiceTests.The_enrol_command_over_the_real_service_…` now captures the staged key and requires exactly the two lines of its one display on standard output (the otpauth URI that carries it, and the manual-key line) and none on standard error, so a change that echoed the key on an error path fails it (§8, M12).

### 5.5 A10.2b.12: the presentation of exhausted recovery codes (production, `79e96109`)

**The defect.** Ruling C-14 makes the consumption of the last recovery code non-refusable and requires the resulting unhealthy posture to be surfaced: "the presentation must say that recovery codes are exhausted and name the remedy" (design gate §3.2, A10.2b.12). No accepted package implemented or tested that clause. `emergency status` and the Emergency Access section of the authentication overview showed only `Healthy: False` / `Not healthy` and `Recovery codes: 0 available`.

**The change.** Both presentations add one statement when the designated account's authenticator is enrolled and no unused code remains: *Recovery codes are exhausted: every code has been used, so Recovery Access is not healthy. Run 'emergency regenerate-codes' to get a new set* (the overview says "in the Emergency Access operator tooling"). With codes left, or with no authenticator (whose remedy is enrolment, which issues codes), nothing changes. The existing count line is kept, so nothing that reads it changes. Product wording only, no internal identifiers.

**Tests.** `EmergencyCommandRunnerTests.The_status_says_the_recovery_codes_are_exhausted_and_names_the_remedy_only_when_…` (four states, U); `AuthOverviewRecoveryCodesRenderTests` (the real page rendered with its client and capabilities, four cases, UI); `A12_…` against SQL Server (§5.8). Controls M20 and M21 (§8).

### 5.6 Review C4: set-password and an audit invariant failure (production, `276584f7`)

Since SI-1 the set-password endpoint is a governance endpoint, but its `catch (InvalidOperationException)` had no filter: an `AuditInvariantException` (an `InvalidOperationException`) was answered 400, or 404 when its message said "not found", where P-37 (A10.2.13) requires the internal error it is. The catch now excludes it, exactly as the credential endpoint beside it already did, and the action declares the 409 it can return. An ordinary not-found and an invalid request keep their 404 and 400. Unit test with the controller's stub service: `UsersControllerIdentityLinkTests.An_internal_audit_failure_is_not_mapped_to_a_client_error_by_the_password_endpoint`; control M22. A failed unit rolls back, so nothing partial was ever left; only the status was wrong.

### 5.7 Writer guard, second round (test-only, `9f553ad5`)

The pre-hand-over review found writes of viability inputs the hardened guard still did not count:

| Finding | Shape | Closed by |
|---|---|---|
| C2 (Medium) | Registration and sign-in policy writes found only through a name declared with the contract (a `var` local or the concrete type missed) | Both stores found by type, like the others |
| C3 (Low) | A store resolved inline (`sp.GetRequiredService<T>().Method(`); a null-conditional receiver (`_users?.`); a context made by a context factory; the designation changed on a tracked read of the users set or by a bulk update of a users query; SQL with a lower-case table, an alias (`UPDATE c … FROM LocalCredentials c`, `DELETE ur FROM UserRoles ur`) or `INSERT` without `INTO`; a further overload of a classified store member; the generic-update fact reading only the first declaration | Each rule extended; the member pin counts signatures per name (four `IUserRepository` names have two each, both of the same kind); every declaration of the generic update is checked |
| C1 (Medium) | The persistence layer and the migrations were outside the guard: a new store contract and its implementation, a concrete-only write method on a store, or a data migration could write a viability table unseen | Every persistence file that touches a viability table is classified (stores, the facts reader, the model and entities, the audit actor evidence reader, `UsernameBackfill` as a justified exception); the store and reader files' declared types are pinned; a store implementation may expose nothing beyond its classified contracts and implement no further application contract; the migrations that change rows of a viability table are enumerated with their reasons (the default sign-in policy seed, the credential-id backfill) |

**Probes.** Nine more probe files, P09 to P17 (P09 a registration delete through a `var`-resolved store; P10 an inline-resolved recovery-code store; P11 a null-conditional receiver; P12 a factory context removing roles; P13 the designation cleared on a tracked users read; P14 SQL with an alias; P15 a new persistence store that disables credentials, placed in the persistence project; P16 a data migration that disables credentials, placed in `Migrations`; P17 SQL with a lower-case table). At `acf92841` (as at `3ddea2bf`) **all eighteen probes (P00 to P17) are detected**, each failure naming the probe file, and `src` is clean after each. The review established the escape of the C1 to C3 shapes by analysis; P09 to P17 were not re-run against the earlier guard. A concrete-only method and a new type in a store file need a rebuild, so they are controls M23 and M24 (§8).

**Residual (stated).** The guard is lexical. A writer reached through reflection or dynamic dispatch, a receiver that is a member of another object (`stores.Users.…`) or a lambda parameter typed by inference, and SQL assembled from fragments are not seen. The behavioural tests (the matrix, `U15_*`, `U17_*`) and the switch's run-time precondition (SR-1) remain the backstop.

### 5.8 Acceptance coverage added (test-only, `3ddea2bf`)

| Clause | Cases (both modes unless U) |
|---|---|
| A10.2b.17 and A10.2b.15, the seven covered WP4B-C units (designate, revoke, provision, bare reset, enrolment, regeneration, switch): audit-insert failure, lost acknowledgement (the gate once, no rerun), confirmed rollback (the gate taken again), unknown outcome (wholly committed or absent), operation lock not granted (never reaches the gate), and a gate held past its bound (the typed busy outcome, nothing changed or recorded, no refusal row, then the retry succeeds) | `AdministratorViabilityPairMatrixSqlServerTests.U17_*` (70), `U15_*` (14) |
| A10.2b.17, provisioning a person with the administrator role and the sign-in policy (added to the WP2 and WP3 theories) | 10 + 10 |
| A10.2b.12 end to end (§5.5) | `A12_*` (2) |
| C-14 × switch | `C14_*` (4) |
| A10.2b.5, set-password and link proof | `A5_*` (2) |
| A10.2b.10, every administrator | `A10_*` (4) |
| Outcome-level gate controls, by family (§3.3) | `Control_without_the_gate_under_row_versioning_*` (4) |
| The raced registration edit records no refusal row (I2) | asserted in `W5_*` |
| A10.2b.16, generated corpus | 1 (U) |

### 5.9 The MFA composition control (test-only, `acf92841`)

The mutation run at `3ddea2bf` caught M07 (the `INativeMfaService` registration removed) only through the arrangement of `Recovery_access_needs_its_second_factor_…`. That test's set-up resolves the emergency service, which needs the registration, so it failed with *"Unable to resolve service for type 'INativeMfaService' while attempting to activate 'EmergencyAccessService'"* before its sign-in ran. Under the rule of §8 that does not catch the wrong behaviour M07 introduces, a sign-in without its second factor.

`ManagementHostSessionIdentityHttpTests.A_password_alone_never_issues_a_session_to_an_account_with_an_enrolled_authenticator` closes that gap. It enrols an ordinary account's authenticator through an MFA service built by `ActivatorUtilities` from the host's own stores, protector, gate and audited unit, not through the host's registration, and then signs in:

- a password alone returns `RequiresMfa` and no session;
- `/me` is 401;
- the right code then issues a session with the native TOTP mechanism.

M07 and M08 now run this test, and its assertion on the sign-in catches both (§7, §8). The test changes the `tests` tree, so the whole mutation run and the probes were repeated at `acf92841` (§8).

## 6. Security invariants, audit and transactions

1. **The global gate protects the required writers.** Every writer in the inventory is integrated: it takes the gate inside its audited unit, after the operation lock and before any authority lock or write, and calls the guard (A10.2b.14, G, hardened twice, eighteen probes). Every gate-taking Management Host endpoint maps a busy gate to 409 with `retry` (the role, credential, link, set-password, registration, policy and provisioning endpoints; SI-1 closed the one exception). Behaviourally: the matrix; `U15_*` for each WP4B-C unit; the outcome-level controls (§3.3); M01 (the coordinator without the gate) and M25 (a covered unit without the gate) (§8).
2. **Decisions are made at the correct point.** Each decision is taken under the gate on the state read inside the unit, the change applied, the state after read in the same transaction, and a violation refused after the write with a rollback (apply-verify-refuse); a refusal is recorded after the rollback with the operation's existing action (`M6_a_refusal_made_after_the_write_…`, `A17_*`). Statement order is captured at SQL for the WP2 to WP4A writers and the candidate path (§2.1).
3. **Attempt-scoped attestation.** The command line's decisions read the host's attestation once per attempt and decide on that one reading: `AdministratorViabilityAttemptSqlServerTests` (50 cases, accepted in WP4B-C) on the integrated tree; control M15 (the decision's read made from a reading of its own).
4. **MFA changes are atomic, replay-safe and audited.** Each authenticator change commits with its one row or not at all: `U17_*` for reset, enrolment, regeneration and the switch (audit-insert failure, lost acknowledgement, rerun, unknown outcome, operation lock); the switch is a conditional update over a verified, versioned, unexpired candidate staged against the active credential (superseded and stale candidates do not switch; the WP4B-C candidate tests); a rerun takes the gate and decides again.
5. **Recovery-code concurrency.** A code is consumed by a conditional update (the recovery-code fix): two sign-ins with one code succeed once (WP4B-C); the last code's consumption takes no gate and is never refused (`F8_*`, `A12_*`); against the switch through the services (`C14_*`).
6. **Unknown outcomes are not confirmed successes.** An outcome the recorder cannot verify is `AuditOutcomeUnknownException`, wholly committed or wholly absent, never reported as success: the unknown-outcome theories for every covered unit (WP2, WP3, the policy, and `U17_*` here); the command line maps it to its own exit status and shows no codes (WP4B-C; control M18).
7. **Production callers cannot bypass the service and transaction boundaries.** The only writers of viability inputs are the stores the guard counts, reached from integrated writers; nothing outside the persistence layer touches a viability table, and inside it every file is classified (§5.7). The switch refuses to run without its caller's transaction (SR-1, `A_switch_without_the_callers_transaction_…`; control M16). Not pinned: callers of the guard's evaluation and the viability reading outside a unit (a read there cannot write; it would be an advisory check only).
8. **R-2 (WP1 review §2.1), recorded as that record requires.** A startup writer must not build the OpenID Connect handler's options before the migrations and the registration state are ready. Resolved in WP4B-A (§7 of its record) and proven against the real Management Host on a new installation, an upgrade and a restart, on both isolation modes: `ManagementHostStartupOrderSqlServerTests` (the hosted services start in registration order with the web host last; no startup step builds the options; a build made before the migrations complete is recorded degraded and the viability reading says Microsoft sign-in is unknown). Run again in §9.
9. **D-E (WP4A §7).** When a person has several Entra links, enforcement does not use the presentation; the presentation of multiple links is IAM-10.4's.
10. **No snapshot isolation** in any viability unit (G); every family runs on both isolation modes.

## 7. The optional `INativeMfaService` of `AuthController` and `UsersController`

**The question.** Both controllers take `INativeMfaService? nativeMfa = null` (and `AuthController` also `IPendingMfaLoginTicketService?`), partly for older tests. ASP.NET Core activates a controller with `ActivatorUtilities`, which passes a parameter's default value when the service is **not registered**. In `AuthController.LocalLogin` the second factor is decided by `var mfaRequired = _nativeMfa is not null && await _nativeMfa.IsRequiredAsync(user, ...)`: with no service, a correct password would sign in a Recovery Access account (or any enrolled account) **without its second factor**. `LocalMfa` fails closed without the service (401), and `UsersController` uses it only to describe enrolment on the user detail page.

**Production composition (static).**

| Fact | Evidence |
|---|---|
| The only production host that serves `Apperture.Management.Api` controllers is the Management Host: it is the only `src` project referencing `Apperture.Management.Api`, and the only `AddControllers()` in `src` | `git grep` over `*.csproj` and `src` |
| `Program.cs` calls `AddAppertureGovernance()` unconditionally | `src/Apperture.ControlPlane/Apperture.Management.Host/Program.cs` |
| `AddAppertureGovernance` registers `INativeMfaService` (scoped `NativeMfaService`) and `IPendingMfaLoginTicketService` unconditionally | `ServiceCollectionExtensions.Governance.cs` |
| Nothing in `src` removes or replaces either registration | `git grep` for `RemoveAll<…>`, `Replace(…)` |
| A registered service whose own dependencies cannot be built makes activation throw (the request is a 500, no session); only an **unregistered** service becomes the default `null` | the container's semantics; M07 below shows both (the emergency service cannot be built; the sign-in receives no MFA service) |

**Behavioural evidence.** Two tests drive the real Management Host composition. Their factory replaces only the secret protector, the bootstrap connection profile and the test authentication schemes, never the MFA registrations.

- `ManagementHostSessionIdentityHttpTests.Recovery_access_needs_its_second_factor_and_then_gets_a_user_id_session`:
  - a correct password returns `RequiresMfa: true` and **no session cookie**;
  - `/me` is 401;
  - a wrong code issues no session;
  - the right code issues the user-id session with the native TOTP mechanism.
- `A_password_alone_never_issues_a_session_to_an_account_with_an_enrolled_authenticator` (§5.9) checks the same for an ordinary account. Its authenticator was enrolled outside the host's registration, so its sign-in is reached even when the registration is missing.

**Negative controls (§8).** M07 removes the `INativeMfaService` registration from `AddAppertureGovernance`; M08 makes `AuthController` ignore the injected service. At the final head, the second test's assertion on the sign-in catches both: *"Expected … RequiresMfa to be True because the account has an enrolled authenticator, but found False"*. In each case the password request itself succeeded (200).

So with the registration removed, the host does **not** fail closed: it signs an enrolled account in on its password alone. The Recovery Access test also fails under both mutants:

- under M08, on the same assertion;
- under M07, earlier, in its arrangement, because `EmergencyAccessService` cannot be built without the registration.

At `3ddea2bf` that arrangement failure was M07's only catch, which is why the second test was added (§5.9).

**A latent hazard, demonstrated.** If a later change dropped the registration, the host would still **start**. The one startup consumer of the emergency service, `EmergencyAccessCanonicalizationHostedService`, catches and logs every exception, so the missing dependency of `EmergencyAccessService` does not fail the start. `LocalLogin` would then skip the second factor:

- for an account with an enrolled authenticator, as M07 shows;
- by the same line of code, for Recovery Access.

Today this cannot happen, because the registration is unconditional (the table above), and the second test fails if it ever does. The structural remedy is to make both parameters of `AuthController` required, so that a missing registration fails activation (fail closed) instead of defaulting to `null`.

**Disposition.**

- Production composition is demonstrably safe, and **no authentication bypass is reachable**.
- No production change was made.
- The hazard of a dropped registration now has a regression test that fails on the sign-in itself (§5.9).
- The constructor clean-up (required parameters, and removal of the test-compatibility defaults in both controllers) is **deferred** to a separate hygiene change (§10.3), as the brief directs. The owner's deferral is contingent on the independent review confirming that the production host cannot silently bypass MFA (§10.3, ruling).

## 8. Negative controls and mutation results

**Method.** The harness is `artifacts/wp5-logs/tools/mutate.ps1`, and the mutants are in `mutants-final.json`, built by `make-final-mutants.ps1`; all three are in the evidence archive.

- The harness works on a disposable clone of the committed code head `acf92841` (`git clone --no-local`, a real `.git` directory, never the worktree).
- Each mutant is one or more exact text edits, and each edit must match exactly once.
- For each mutant, the clone's unit, integration or UI test project is rebuilt (`-m:1`, no shared compilation, no build servers). The mutant's own filters then run to TRX against the dedicated SQL container, whose state is recorded before and after.
- Each edited file is restored with `git checkout`. Its blob hash is compared with `HEAD`, and its bytes with the hash taken before the edit.
- A green baseline of every distinct filter runs first, and a green final pass (rebuilt) runs after the last restore.

**What counts.** The harness counts a failure as a catch unless its message looks like infrastructure (a connection, login, missing-file or "cannot run against SQL Server" failure); such failures are flagged and not counted. Each catch was then read, and a mutant counts only where a failing assertion is on the wrong behaviour the mutant introduces. The table names that assertion.

**Earlier runs.**

- **At `6f779f93`:** stopped during its baselines, before any mutant was applied, because tests were then added. Mutation evidence belongs to the final commit.
- **At `3ddea2bf`:** the first launch ran all 25 distinct baselines green, applied, ran and restored M01, and then stopped on an error in the harness's own report line (a PowerShell null-conditional on a variable under strict mode). The clone was confirmed clean, the line was corrected, and all 26 mutants ran with those baselines (`-SkipBaseline`). All were caught and the rebuilt final pass was green.
  - Reading each catch showed that M07's was an arrangement failure, not an assertion on the sign-in (§5.9).
  - The test added for it changed the `tests` tree, so this run is superseded. Its logs stay in the archive.
- **At `acf92841`, M07 and M08 only:** a focused run (baseline, both mutants, final pass) confirmed the new catch before the full run.

**The final run** is at `acf92841`: every baseline, all 26 mutants and the rebuilt final pass. M26 is an unused number: no mutant was ever defined under it.

- **Baselines and final pass.** All 25 distinct filters were green before the first mutant and green again after the last restore and the rebuild: 25 of 25, 0 failures.
- **Restores.** Every restored file matched its `HEAD` blob and its bytes from before the edit.
- **SQL container.** Not restarted during the run (started 2026-11-04T19:42:56Z before and after each mutant).
- **Edits.** The hash of each mutated file equals the earlier runs', so the same edits were applied.
- **Result.** **All 26 mutants are caught by a valid assertion on the wrong behaviour they introduce**:

| Mutant | Property (what the mutant breaks) | Failed / ran | The catching assertion |
|---|---|---|---|
| M01 | A10.2b preamble: the coordinator no longer takes the gate | SQL 184/374 | The concurrent cases' gate probe: "expected -1 because the first writer holds the administrator-set gate while it is parked, but found 0". U15: the typed busy outcome expected, none thrown. U17: the gate's acquisitions on a rerun. F9: the command no longer waits. The survivors are in the table below |
| M02 | A10.2b.11: the bare reset acts on a refusing decision | SQL 18/36 | F4: the second writer's outcome is `applied`, expected `refused:entra_independent_fallback_required`. F9: the command line's exit status |
| M03 | A10.2b.11, C-13-R: the strict revocation acts on a refusing decision | SQL 16/32 | F3: the second outcome is `applied`, expected the refusal |
| M04 | A10.2b.9, D-2: the designation acts on a refusing decision | SQL 16/32 | F2: the second outcome is `applied`, expected the refusal |
| M05 | A10.2b.1, D-A: the Entra-primary policy ignores a refusing verdict | SQL 16/56 | DA, EL, F2: the policy is `Disabled`, expected `DeferredLastViablePlatformAdministrator` or `DeferredLastEntraIndependentPlatformAdministrator` |
| M06 | SI-1, A10.2b.15: set-password maps a busy gate to 409 (the fix reverted) | SQL 2/2 | 500, expected 409 "because a gate held past its bound is a transient conflict" |
| M07 | MFA composition: the host registers `INativeMfaService` (the registration removed) | SQL 2/2 | §5.9's test: "`RequiresMfa` to be True because the account has an enrolled authenticator, but found False": the password alone signs in. The Recovery Access test fails in its arrangement (§5.9), which is not counted |
| M08 | MFA at sign-in: the local login uses the injected service (ignored) | SQL 2/2 | Both sign-in tests: `RequiresMfa` is false |
| M09 | C-14: the consumption of a recovery code takes no gate (it takes it here) | unit 1/50, SQL 4/6 | F8: the consumption while a disable holds the gate fails busy (`PlatformAdministratorSetBusyException`) instead of completing. The guard pin `Recovery_code_consumption_stays_non_refusable_and_takes_no_gate` |
| M10 | D-C: a sign-in with nothing left to disable never touches the gate (the lock-free exit removed) | SQL 2/2 | DC: those sign-ins wait about 1 s on the held gate, not answering "before the gate is ever asked for" |
| M11 | D-C: a busy gate is `NotAppliedBusy` with one warning (not caught here) | SQL 2/2 | DC: no single warning naming the account and "another change to administrator access is in progress" |
| M12 | CLI L-1: the staged key is shown once and never on an error path (echoed to standard error) | unit 1/46 | Standard error contains the staged key |
| M13 | Guard: a write through a user store is found by the store's type (names only here) | unit 1/50 | The guard's self-test: the writers found only by type are reported as "CLASSIFIED BUT NOT FOUND" |
| M14 | Guard: a viability table written through a context outside the persistence layer is found (contexts ignored) | unit 1/50 | The self-test: a bulk `ExecuteUpdate` of `LocalCredentials` through a context is not found |
| M15 | Attempt-scoped attestation (the decision makes a reading of its own) | unit 25/41, SQL 24/50 | The attestation store is read twice where once is required, and decisions change with the second reading |
| M16 | SR-1: the switch refuses without its caller's transaction (precondition disabled) | SQL 2/2 | `InvalidOperationException` expected, none thrown |
| M17 | Recovery-code concurrency: a code is consumed only while unused (unconditional here) | SQL 4/6 | Two sign-ins with one code both succeed: `{True, True}`, expected `{True, False}` |
| M18 | Unknown outcomes: the command line never reports an unconfirmed commit as done (mapped to success) | unit 6/138 | The exit statuses of unknown outcomes (switch, bare reset, regeneration, the mapper) are success |
| M19 | A10.2b.15: lock result 1 ("granted after waiting") means the caller holds the gate (mapped as a timeout) | SQL 18/36 | F1: the writer that waited throws `PlatformAdministratorSetBusyException` instead of deciding |
| M20 | A10.2b.12: the status says the codes are exhausted and names the remedy (the line removed) | unit 1/4, SQL 2/2 | The status has no "exhausted" line: in the unit test, and end to end through the real command line in `A12_*` |
| M21 | A10.2b.12: the authentication overview says so (never shown) | UI 1/4 | The rendered overview does not contain "Recovery codes are exhausted" |
| M22 | Review C4: an audit invariant failure on set-password is an internal error (the filter removed) | unit 1/1 | `AuditInvariantException` expected to reach the host's handler, none thrown: it was mapped to a client error |
| M23 | Review C1: a store implementation offers nothing beyond its classified contract (a concrete-only bulk disable added) | unit 1/1 | An extra member, `DisableEveryCredentialOfAsync`, is found |
| M24 | Review C1: a store file declares only its classified types (a purge class added) | unit 1/1 | An extra type, `RecoveryCodePurge`, is found |
| M25 | A10.2b.15, A10.2b.17: a covered Recovery Access unit takes the gate in its unit (regeneration ungated) | SQL 6/84 | U15: no busy outcome while the gate is held. U17: regeneration's gate acquisitions |
| M27 | A10.2b.16: the snapshot and `IsUsable` differ only as recorded (the snapshot also requires an absolute authority) | unit 1/1 | The corpus finds a difference that is not the invalid authentication context |

**Reading the catches.** `catch-messages.ps1` groups every failed result by the shape of its message (`catches-M01-M09.txt`, `catches-M10-M27.txt` in the run's folder). No failure looks like infrastructure. The one failure that is not an assertion on the wrong behaviour is the Recovery Access test's arrangement under M07, explained in §5.9.

**What survives the gate's removal (M01, 184 of 374 cases fail; `m01-groups.ps1`, the same at `3ddea2bf` and `acf92841`).** Every concurrent case fails, and every sequential twin passes. So does every case that does not need the gate to hold:

| Group (both modes) | Fail | Pass | Why the passes pass |
|---|---|---|---|
| F1, F2, F3, F4, F5, F7, DA, EL, W5: concurrent | 128 | 0 | |
| the same families: sequential twins | 0 | 128 | one writer at a time; the gate is not exercised |
| DB (the policy twice for one user) | 4 | 2 | the sequential twins |
| F8 (C-14 × disable) | 4 | 2 | the sequential order (consumption, then disable) |
| C14 × switch | 2 | 2 | the sequential order |
| F9 (across compositions) | 4 | 0 | |
| U15 (busy, per WP4B-C unit) | 14 | 0 | |
| U17 (fault matrix, per WP4B-C unit) | 28 | 42 | the audit-failure, unknown-outcome and operation-lock theories do not count gate acquisitions (the lost-acknowledgement and rerun theories do, and fail) |
| A5, A10, A12 | 0 | 8 | sequential clauses (A12 holds the gate with a raw session; consumption takes none either way) |
| Control_ (the six gate-removed controls) | 0 | 6 | they run without the gate in any case |

**Probe controls of the guard.** There are eighteen probe files (§5.1, §5.7), and all are detected at `acf92841`, as they were at `3ddea2bf`.
- They run against the final validation's unit build (no rebuild, so a detection can only come from the probe).
- P15 is placed in the persistence project and P16 in `Migrations`.
- Each failure names the probe file, and `src` is clean after each.
- The self-test fact holds each shape.

**Outcome-level controls of the gate**: §3.3.

## 9. Validation

**Run.** At the code head `acf928418a8c4e59de3e5c115d33ae9d4d088080` (root tree `f7e72a9a`, `src` `19d6c1b1`, `tests` `138b07b2`), Release, SDK 10.0.402, by `final-validation.ps1` from the worktree on 2026-11-10 between 20:22 and 23:30 UTC.

- Before starting, the script checked that `src` and `tests` equal `HEAD`. The only untracked files were this record and the review brief, which no test reads.
- Each step ran under the watchdog `run-step.ps1` (memory floor 150 MB), and none was stopped. The lowest available memory was 174 MB, during the integration run.

| Step | Result |
|---|---|
| Build: `dotnet build Apperture.slnx -c Release --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers --artifacts-path artifacts/wp5-final` | Exit 0, 0 errors, 73 warnings (72 distinct; the DEV-1 integration's build also gave 73), none in a file WP5 changed; 8 min |
| `Apperture.UnitTests` (`dotnet test --no-build`, like every suite below) | 6,393 results: 6,392 passed, 0 failed, 1 not executed. That one is the opt-in password-timing diagnostic (`PasswordEvaluationDesktopTimingDiagnostics.Records_desktop_zxcvbn_timings_…`, "measures and asserts nothing"), not executed by design |
| `Apperture.ArchTests` | 38 of 38 passed |
| `Apperture.Management.Ui.Shared.Tests` | 1,497 of 1,497 passed |
| `Apperture.Management.Ui.Client.Tests` | 536 of 536 passed |
| `Apperture.AppHost.Diagnostics.Tests` | 7 of 7 passed |
| **`Apperture.IntegrationTests`, the whole assembly, no filter**, against the dedicated SQL container (`127.0.0.1,1433`, required-SQL mode) | **4,361 discovered and executed: 4,360 passed, 1 failed, 0 skipped, 0 not executed** (2 h 57 min). Isolation modes, counting the mode anywhere in the name: 1,541 `rcsi: False` and 1,541 `rcsi: True` cases, **none failed in either mode**; 1,279 cases have no mode |
| The one failure | `IntegrationTestSecretProtectorTests.Factory_replaces_the_production_protector_and_does_not_share_the_browser_harness`: "Repository root (.git) not found". The test looks for a `.git` directory, and in a linked worktree `.git` is a file. This is the honesty allow-list's one `requires-git-directory` entry. WP5 did not change it (last changed in `1d3e5487`), and the DEV-1 integration had the same failure. In a clone of `acf92841`, which has a `.git` directory, the class passes 4 of 4 against the same SQL container (the mutation clone after its rebuilt final pass; `final/clone-check/`). The honesty run below passes it too |

**SQL execution proof.** By `scripts/dev/validation/Get-SqlEvidence.ps1`. The container was not restarted: it was started at 2026-11-04T19:42:56Z both before and after the run.

- **Write markers** were taken before (20:32:36Z) and after (23:30:09Z) over the 24 `Apperture_*` catalogs. The four persistent catalogs the assembly uses wrote during the run:

  | Catalog | User updates | Last write |
  |---|---|---|
  | `Apperture_IntegrationTests` | 1,618 → 2,376 | 23:29:31Z |
  | `Apperture_BootstrapHttpTests` | 34,132 → 38,194 | 23:29:58Z |
  | `Apperture_ManagementGovernanceTests` | 20,870 → 25,259 | 23:29:50Z |
  | `Apperture_BootstrapFirstAdminTests` | 2,819 → 3,144 | 23:29:34Z |

  The other 20 catalogs are left over from earlier runs and did not change. Total user updates went from 74,697 to 84,231.
- **Window** (20:32:36Z to 23:30:06Z): 1,262 "Starting up database" entries in the server's log, the first at 20:34:55Z and the last at 23:29:21Z. These are the disposable catalogs that SQL cases create.

**The SQL honesty check, separately.** It is not integration coverage. It runs the same assembly against an unreachable endpoint and proves that no SQL case can pass without SQL Server.

`scripts/dev/Test-SqlExecutionHonesty.ps1 -Configuration Release` ran in a fresh `git clone --no-local` of the branch at `acf92841`, which has a `.git` directory. It was built by the script itself (not `-NoBuild`) in enforce mode. Result: **PASS, exit 0, authoritative.**

| Check | Result |
|---|---|
| Provenance | Built by the script; HEAD `acf92841`, tree `f7e72a9a`; no tracked change and no untracked build input; assembly SHA-256 `E859E594…917E`, newer than every build input |
| Discovery and execution | 4,361 discovered and 4,361 executed, the same count as the SQL run above; nothing skipped or left not executed |
| Passes | 227, all on the allow-list. They include the `requires-git-directory` case, which passes here; nothing was tolerated under a declared condition |
| Failures | 4,134, all "unavailable" |
| Violations | No unexpected pass, no allow-list entry missing or failing, no must-fail violation |
| Lists and script | Unchanged: the allow-list (227 entries, `8798D5A9…442E`), the must-fail list (188, `17D8A73D…73E3`) and the script (`A75F53F6…E513`) hash as in the persistence remediation record |

**Not run here.** `Apperture.BrowserTests` belongs to the hosted `browser-security` check (§11). The mutation runs and the guard probes are in §8.

**Evidence.** The `final/` folder of the archive (§12) holds:
- `identity.json` and `final-log.txt`;
- each step's output, error and status files;
- the six TRX files and their summaries;
- the SQL markers and the window.

## 10. Owner decisions

Nothing below was implemented. WP5 adds no audit action, log line, counter, rate limit or policy. The owner ruled on each item on 2026-11-10, after the hand-over at `7d3bf808`; each ruling is quoted as given and set against the option or recommendation it decides. Recording them changed no code and no test, and needed no new validation.

### 10.1 D-C and D-D (binding: a ruling before this record is final)

The deferral text requires "a measurement and a ruling before the WP5 closure record". The measurement is §4. The DEV-1 integration review explicitly did not accept the earlier recommendation, so no ruling exists. The two passages it noted still pull apart: WP4A §5 leaves its audit questions "the owner's", while WP4A §7 says "the earlier rulings stand: a busy gate and an unavailable configuration do not need a new durable audit event".

| Option | What it means | Consequence |
|---|---|---|
| **A. Keep the behaviour as measured** | One warning per affected sign-in; no row; the policy repeats at every sign-in until it applies | Nothing durable reaches the audit trail; an operator sees repetition only in logs. A busy gate holds an affected sign-in for up to the gate's bound (15 s, §4) |
| **B. Add a counter** (a metric or a structured log field) for `NotAppliedBusy` and `DeferredAdministratorViabilityUnavailable` | Observability without a durable row; no rate limit | Small additive change in the policy's callers; needs its own review; makes the repetition measurable in production |
| C. Rate-limit the warning | Fewer log lines | Hides the repetition the measurement exists to show; **not recommended** |
| D. A new attempt action with a reason (`last_viable`, `last_entra_independent`, `configuration_unavailable`, `busy`) | Durable audit for deferrals | An audit-catalog change (schema, producers, catalog guards G1/G2); sign-in load would write rows for a busy gate |
| E. Shorten the policy's own wait for the gate (for example to under a second) | A busy administrator change no longer holds a sign-in for 15 s | More `NotAppliedBusy` outcomes during short holds; the policy still applies at the next sign-in; a behaviour change that needs its own review |

**Recommendation.** A for RC1 (the accepted rulings stand: no durable event for busy or unavailable), with **B** as a small post-RC1 observability change, and E considered only if the 15 s hold is judged a real user problem. Record the ruling in this section; this record is complete once it is recorded.

#### 10.1.1 The ruling (owner, 2026-11-10)

> **D-C:** Approve Option A for RC1. Retain the measured gate-contention behavior. No additional contention mechanism or speculative throttling is authorized.
>
> **D-D:** Approve Option A for RC1. Retain the existing operational warning without introducing a new durable contention audit event. Option B, an operational counter, is a post-RC1 enhancement candidate, not an RC1 requirement.

**What it decides, against the options above and the deferral text** (WP4A record §7: D-C "decide whether the warning needs a rate limit or a counter"; D-D "it leaves no audit row … the only record is a warning per sign-in").

| Option | Disposition for RC1 | After RC1 |
|---|---|---|
| A, keep the behaviour as measured | **Adopted** for D-C and for D-D. One warning per affected sign-in, no audit row, the policy repeating at each sign-in until it applies or the configuration is established, and a busy gate holding an affected sign-in for up to its 15 s bound (§4) | — |
| B, a counter | Not adopted for RC1 | A post-RC1 enhancement candidate, as the ruling states it under D-D (see point 1 below) |
| C, rate-limit the warning | Not adopted ("no … speculative throttling is authorized") | Not ruled on |
| D, a durable attempt action with a reason | Not adopted: Option A writes no row for either outcome | Not ruled on |
| E, shorten the policy's wait for the gate | Not adopted: retaining the measured behaviour keeps the 15 s bound | Not ruled on |

The tension the DEV-1 integration record (§6) asked the ruling to settle, between WP4A §5 (its audit questions "remain the owner's") and WP4A §7 ("the earlier rulings stand: a busy gate and an unavailable configuration do not need a new durable audit event"), is settled **for RC1** in the direction of §7. The ruling does not decide a durable event after RC1.

**Reported, not reinterpreted.** Three points of wording differ from the option definitions. None changes what RC1 does, which follows from Option A's definition; each is left for the owner to confirm or correct.

1. **Which outcome the counter candidate covers.** The ruling names Option B only under D-D. Option B above covers both outcomes (`NotAppliedBusy`, which is D-C's, and `DeferredAdministratorViabilityUnavailable`, which is D-D's), and the deferral text makes the counter a D-C question. It is recorded as given: a post-RC1 candidate stated for D-D; for D-C's busy outcome the ruling says nothing about a counter after RC1.
2. **"Contention" under D-D.** The D-D ruling refers to "a new durable contention audit event". D-D is the unavailable-configuration outcome, not gate contention. Option A's definition (no row) governs either way, so no durable event is added for the unavailable configuration or for a busy gate.
3. **The MFA contingency of §10.3.** It is recorded as an independent confirmation that the production host **as composed at the reviewed commit** cannot silently bypass MFA, which is what the evidence of §7 can establish and what the brief asks (question 9). It cannot establish that a later change will never drop the registration. The deferred clean-up gives that structural guarantee, and until then the sign-in test of §5.9 guards it. If the owner meant the structural reading, the clean-up cannot be deferred on this evidence.

### 10.2 The optional reason on refusal rows

Left for WP5 consideration by the WP2 review ("no owner decision is needed now"), with the related WP4A §5 ambiguities 1 (one attempt row for two invariants) and 3. The refusal rows (`auth_admin.blocked.*`, `entra.tenant_registration.change_refused`, the policy's attempt row) do not say which invariant decided; the stable code is in the API response and the command line's output, and `emergency.auth.blocked.administrator_viability` already carries the code and the evidence class. **Recommendation:** no change in IAM-10.2b; if wanted, one additive `reason` detail through a separate audit-catalog change, ideally alongside the IAM-10.4 presentation of refusals.

**Ruling (owner, 2026-11-10).** "Preserve existing reason and evidence semantics. No expansion authorized." This matches the recommendation's first part: no change in IAM-10.2b, and the existing code and evidence class of `emergency.auth.blocked.administrator_viability` stay as they are. The recommendation's conditional second part (an additive `reason` detail through a separate audit-catalog change) is not authorized by this ruling and would need its own decision.

### 10.3 Constructor clean-up (§7)

Make `INativeMfaService` and `IPendingMfaLoginTicketService` required in `AuthController` (and `INativeMfaService` in `UsersController`), re-basing the older unit tests that construct the controllers without them.

**Consequence of deferring.** A change that dropped the registration would make the sign-in skip the second factor rather than fail; M07 demonstrates this. The sign-in test of §5.9 fails on exactly that change. The clean-up would turn the silent skip into a failed activation.

**Recommendation:** a small hygiene change after IAM-10.2b merges. It is not a security fix, because production composition is safe today and the regression test is in place.

**Ruling (owner, 2026-11-10).** "Defer cleanup, contingent on independent confirmation that the production host cannot silently bypass MFA." The deferral matches the recommendation. Its condition is open until the independent review answers question 9 of the brief, which now asks for that confirmation explicitly. If the review does not confirm it, the condition fails and the clean-up returns to the owner. For how the condition is read, see §10.1.1, point 3.

### 10.4 Changes made here, for the owner to confirm or overrule

| Change | Why it was made in WP5 | To undo |
|---|---|---|
| SI-1 (`07fe0b54`, §5.2) | Deferred as informational by the services review; A10.2b.15 and P-10 specify the busy outcome for every gate-taking writer, set-password became one in WP4B-C (L-P3), and the change is the established mapping of its sibling endpoints | revert `07fe0b54` |
| The A10.2b.12 presentation (`79e96109`, §5.5) | A binding clause of an owner ruling (C-14) that no package implemented; the wording is new operator-facing text | revert `79e96109` (and record the clause as open) |
| Review C4 (`276584f7`, §5.6) | P-37's contract on the route SI-1 brought under 10.2b; the neighbouring endpoint's established filter | revert `276584f7` |

**Ruling (owner, 2026-11-10).** "Retain the three corrections from `07fe0b54`, `79e96109` and `276584f7`, subject to independent security review." All three stay in the tree as validated at `acf92841`. Each is still subject to the review (brief questions 5 and 8), and a finding against one returns it to the owner.

### 10.5 Carried, not WP5 decisions

SO-1 (a fourth evidence value; dormant until a host composition calls designate, revoke, bare reset or the switch), the legacy deployment-secret fallback (WP3 §5, only if a change is wanted), and the RC1 program gate's last-administrator row, which closes when IAM-10.2b ships with A10.2b.1 to A10.2b.17 passing (an owner and merge action).

## 11. RC1 operational obligations (not WP5)

These are release and operations checks, not part of WP5's contract; none blocks the WP5 review.

| Item | Source |
|---|---|
| Interactive Windows smoke of the emergency command line with a running Management Host: prompt text, three attempts, Ctrl+C, signing in with a replaced authenticator; and `emergency status` showing exhausted codes | CLI record §13.4; §5.5 |
| Host sign-in with an authenticator the command line enrolled (both protect with DPAPI at LocalMachine scope: the command line must run on the host's machine) | CLI review |
| The packaged executable and the PowerShell module | CLI review |
| Linux and the hosted CI run of the new process cases (CI runs on ubuntu-24.04) | CLI record §13.3 |
| The unhandled exit of `bootstrap` and the bare overview on a malformed settings file | CLI record §13.4 (pre-release gate) |
| The written procedure for an exceptional manual database intervention | WP4B-A record, decision record §3.6 |
| L-D1: anonymous invalid first-administrator requests take the gate before refusal | OPS-1.4, WP4B-D record §7 |
| The test SQL container is published on `0.0.0.0:1433` | owner action recorded 2026-11-07 |
| Development databases that applied the pre-remediation candidate migration lack its unfiltered index (error 8622 on candidate statements) | persistence closure, I-1 |
| One Management Host only: two live attestations are a conflict (evidence unavailable) | decision record §3.7, ADR-0045 |
| Observation-age limits of the attestation (D-4, R-B2, R-B8): evidence is judged at decision time; a backward database clock step can revive an expired row | decision record §15, WP4B-B record §10 |
| Hosted CI on the merged IAM-10.2b tree, including `browser-security` | the merge |
| The `transitions/README.md` index row for `authorization-hardening/` still says nothing is implemented; WP0 §4 still names the refusal action `unsafe_operation` (superseded by the WP4B-C services record) | documentation drift, informational |

## 12. Evidence and recovery

- **Archive:** `G:\Backup\Apperture-Core-worktree-evidence\2026-11-10\wt-iam10-2b-wp5-evidence`, a copy of the worktree's `artifacts/wp5-logs`.
  - It holds 1,275 files (312 MB) plus `SHA256SUMS.txt` (CRLF, backslash paths: verify with PowerShell `Get-FileHash`). The files were verified one by one, twice (by the script and again independently), with 0 mismatches and no unlisted file. Manifest SHA-256: `25F8DC1B56F4027DB56ED41AF4C5078B1066F02ECA1B1ECACBB67F57521F3395`.
  - It was copied after a scan that found the test SQL password (searched for as a literal) in no file and no other secret-looking material: connection-string passwords, a `sa` login, account keys, bearer tokens, private keys or client secrets. The password reaches a test process only through its environment.
  - The scan has a positive control. A temporary hard link to the primary checkout's `.env` was reported for the literal password, the connection-string password and the `sa` login, printing only the file, pattern and count, and the link was removed.
  - Contents:
    - `final/`: the validation of §9.
    - `honesty/`: the honesty report, its TRX and logs (§9).
    - `mutation-final-acf92841/`: the final mutation run of §8, with its log, summary, and each mutant's builds, runs and TRX files.
    - `mutation-final/`: the superseded run at `3ddea2bf`.
    - `mutation-m07-check/`: the focused M07 and M08 run.
    - `mutation/`: the run at `6f779f93`, stopped during its baselines.
    - `probes/`: the eighteen probe files (`guard/`) and every probe run, the last at `acf92841` (`final-acf92841/`).
    - `tools/`: the harness and the mutant definitions (§8), the catch reader and the M01 grouping, the validation, archive and bundle scripts, and the TRX summarisers.
    - `base/`, `dev/`, `dev-unit/`, `closure/`, `m07-test/`: the baseline at `1c8067cd` and the development runs behind §3 to §5.
- **Bundle:** `G:\Backup\Apperture-Core-git-bundles-integration\feat-iam10-2b-wp5-<head>.bundle`, the branch at its final commit (the commit that records the owner's rulings). The bundle of the first hand-over, `feat-iam10-2b-wp5-7d3bf808.bundle`, stays beside it unchanged.
  - Its SHA-256 is in `SHA256SUMS-feat-iam10-2b-wp5.txt` in that folder (one line per bundle) and in the hand-over message, because a commit cannot name the hash of the bundle that contains it.
  - It was verified with `git bundle verify`, then restored into a bare clone on a short path, where the head, the root, `src` and `tests` trees and `git fsck --full` were checked; the clone was then removed.
  - It carries no `HEAD`: clone it with `git clone -b feat/iam10-2b-wp5`.
- **Working copy:**
  - the worktree `F:\Code\Apperture\wt-iam10-2b-wp5`, with `artifacts/wp5-logs`;
  - the builds in `artifacts/wp5-dev` and `artifacts/wp5-final`;
  - the mutation clone `artifacts/wp5-mut/clone`, with its build in that clone's `artifacts/mb`.

  - the honesty run's clone `F:\h5` (1.9 GB, at `acf92841`), which is disposable. The session's tooling does not delete a folder at a drive root, so its removal is left to the owner.

  Nothing was cleaned up.
- **Unchanged:** nothing was pushed and no pull request was opened. The IAM branch is unchanged at `1c8067cd`, and every earlier archive and bundle is untouched.
