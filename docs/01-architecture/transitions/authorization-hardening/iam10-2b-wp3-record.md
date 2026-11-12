# IAM-10.2b WP3: Entra registration and sign-in policy writers

| | |
|---|---|
| Package | IAM-10.2b, work package 3 ([WP0 reconciliation](iam10-2b-wp0-reconciliation.md) §7, rows marked WP3; §8 F-5), on the [WP1](iam10-2b-wp1-review-record.md) foundation and the [WP2](iam10-2b-wp2-review-record.md) writers. |
| Branch | `feat/iam-10.2b-administrator-viability`, from `1883d87`. Not pushed, not merged. |
| Status | Implemented and validated; independently reviewed and accepted with bounded remediation (M-1, L-1, L-2), made and recorded in §11. Nothing here reopens a decision of the [design gate](iam10-design-gate.md) or a ruling D-1 to D-6. |
| Commits | WP3-A: configuration prerequisites (R-1, secret-resolution status, build record). WP3-B: registration and sign-in policy integration, the writer guard, the R-L1 correction and this record. |
| Not in scope | WP4A (Entra-primary policy), WP4B (bootstrap, Recovery Access, MFA and command-line writers, F-1, D-6, R-2), WP5, IAM-10.4 (the UI's presentation of a refusal). |

## 1. What is integrated

Every writer of the Entra registrations and of the sign-in policy takes the global administrator-set gate, decides on the **persisted configuration its own change produced** (read back in the writer's transaction, in the Now and the Restart view, by the one shared definition), and refuses or allows before anything is staged.

| Writer | Entry | Class | Refusal (stable code) | Audit row on refusal |
|---|---|---|---|---|
| Create | `ConfigEntraTenantsController.Create` | Gated raiser | none (asserted) | none |
| Edit, disable | `Patch` | Reducer | 409 `last_viable_sign_in_method`, `administrator_viability_unavailable` | `entra.tenant_registration.change_refused`, `update` |
| Delete | `Delete` | Reducer | same | same, `delete` |
| Activate, switch | `Activate` | Reducer | same | same, `activate` |
| Sign-in policy | `ConfigAuthUiPolicyController.Patch` | Gated, cannot reduce | none | none |

A registration change does not change who can sign in locally, so it cannot itself reduce the Entra-independent set, and in every sequential case tested `entra_independent_fallback_required` is never produced by these writers. That is not a claim about every interleaving. A Recovery Access recovery code consumed at sign-in is exempt from the gate (ruling C-14) and can make Recovery Access unhealthy between a writer's two reads. When no Entra-independent administrator exists and Recovery Access was healthy at the first read, the second read can then find I-FALLBACK newly violated, and the writer refuses with `entra_independent_fallback_required` although its own change moved nothing it depends on. The refusal is conservative (it is in the safe direction) and transient: the state is by then already unhealthy, so a retry reads the new state as the starting point and a change that does not itself reduce anything succeeds (§11).

**Order** (A10.2b.14), checked method by method by the writer guard: authorization and request validation (400, 403, 404 unchanged); the running options are built (`PrepareAsync`, §3); then inside the audited unit: the operation lock, **the gate**, the registration read for the decision, the no-op (`DeclareNoOp`, before the invariants), the state before (under the gate, before the first write), the secret stored if one was supplied, the change applied through the repository, the applied change decided (instance-level rule, then viability), and only then audit rows staged and the replaced or deleted registration's secret retired. A refusal declares the attempt a no-op: the unit rolls back the registration update, the stored secret and the retirement together, and the refusal row is written after the rollback on a context of its own (a failure to write it is logged and never changes what the caller sees).

**Create** reads no viability state. A new registration is never the active one, so it cannot change what the sign-in accepts; the writer asserts that of its own effect (the persisted active registration before and after, `RegistrationRaiseOnlyAssertion`), not of the whole system. A configuration reload, a Recovery Access recovery code consumed at sign-in, or an unrelated change in the posture can therefore never fail or refuse it (the lesson of the WP2 review, M-2). The gate is taken first, so a busy gate stores nothing.

**Sign-in policy.** The chooser flags are not an input of administrator viability: the direct Local login route is available whenever Local sign-in is enabled, whatever the chooser shows, and no production endpoint is restricted by these flags. The writer takes the gate (it is serialized with every other writer of the sign-in configuration) and reads no viability state. Its only new outcome is a busy gate (409 `governance_operation_busy`, `retry: true`). Tests show both views and Recovery Access health identical before and after every flag combination, and the Local administrator viable and Entra-independent with the chooser hiding the Local route. Request validation (400 for an unsatisfiable policy), row-version conflicts (409 with the existing body), the no-op and authorization are unchanged.

## 2. The instance-level rule and the viability invariants

`AuthenticationConfigurationGuard.ValidateNoLockout` ("some sign-in path remains", and a chooser policy that stays satisfiable) is kept, as a 400, and is now asked of the **persisted state the change produced** (the Restart reading of the effective sign-in configuration), through the same two facts it has always read (`ValidateNoLockout(bool localSignInEnabled, bool entraSignInUsable, policy)`; the options form delegates to it). The hand-built simulation (`SimulateEffectiveEntraAfterUpdate`, the legacy-only clone for activation and deletion) is removed: it read only the deployment secret, so it called an edit of a registration that holds its own stored secret a lockout (WP0 F-5). Two refinements, both in the direction of "an already-invalid pre-state is never a reason to refuse an unrelated or repairing change":

- the rule refuses only a change that **newly** violates it (satisfied before, violated after). An instance that already has no path no longer refuses a rename of an inactive registration;
- a reading whose Local or Entra input is not established asserts nothing; administrator viability, which fails closed, decides then.

Viability is evaluated by `AdministratorViabilityGuard.Evaluate` over `AdministratorViabilityScope.CaptureBeforeAsync` and `VerifyAfterAsync`, unchanged. The instance-level rule is checked first (400), then viability (409). A verdict is recorded with the operation's existing refusal row (`sign_in_lockout`); the audit vocabulary is not changed, so the row does not say which invariant was violated (as in WP2; the code is in the response).

## 3. R-1: initializing the running Entra options

**The hazard, shown on both isolation modes** (`EntraOptionsInitializationSqlServerTests.Control_*`). The Entra options are built on first use and cached by the framework, and the build reads the persisted registration and its secret through a scope and a connection of its own (`EntraAuthOptionsDatabasePostConfigure`). Inside a unit that has written a registration row: under locking READ COMMITTED the build **waits for the unit's own uncommitted row** (the unit would wait on itself until the command timeout, after which the build swallows the error and caches the options without the registration); under READ_COMMITTED_SNAPSHOT it reads **the row as it was before the unit**, **successfully**, so that build is recorded as complete (not degraded) and the framework keeps it. The first operation being a configuration-management request is exactly when this would happen, and under snapshot isolation nothing flags it: that is why the options are prepared before the unit opens (below), not by detecting a bad build afterwards.

**Resolution.**

1. `IAdministratorViabilityCoordinator.PrepareAsync` builds, **outside any transaction and before the unit opens**, everything a viability reading is made from (`IDeploymentAuthenticationConfiguration.EnsureInitialized`: the endpoints' options, the handler's options). Every registration and policy writer calls it before `Unit.ExecuteAsync`; the writer guard checks the order. A failure to build is not swallowed into a reading.
2. **The build is recorded, not hidden** (`IEntraOptionsBuildHealth`, a singleton note against each options instance, holding no value and no secret). A build that could not read the registration or its stored secret still leaves the deployment configuration in place and still does not throw (the production behavior is unchanged), but it is recorded as degraded. The Now view then reports the **Entra reading as unknown** ("the running sign-in options were built while the Microsoft registration or its client secret could not be read; what the host accepts now is not established until the host is restarted"): a change that could reduce viability fails closed with `administrator_viability_unavailable`, and a restart makes the reading settled again. A degraded build is therefore never an optimistic viability result.
3. **Rejected: initializing at host start.** A start-up initializer would pin the options before the registration state is guaranteed ready, which is the risk R-2 (WP4B) tracks; and it would not help the case that matters (a first request after a database that was unavailable at start). Rejected: reading the persisted registration for the Now view instead of the held options (an independently maintained authentication snapshot, which the design forbids).

**Residual, stated.** An options **rebuild** on a configuration reload that coincides with an in-flight registration write is the same hazard for the monitor's rebuild, and it is not prevented. Under locking READ COMMITTED the rebuild cannot read the row and is recorded as degraded (reading unknown, fail closed) and logged. Under READ_COMMITTED_SNAPSHOT it can read the previously committed registration successfully and be recorded as **complete**, so it is not detected: the Now view then follows options built from the registration as it was before the change until the host restarts, while the Restart view follows the persisted one. A running handler bound to a different directory than the persisted registration is still seen (no token is accepted until the host restarts). Restart-dependent behavior is explicit: the Now view follows the options the host holds; the Restart view follows the persisted registration; `EntraRegistrationHostSqlServerTests` shows a registration deleted on a fresh host leaves the host's held options on the old tenant while the Restart view has none, and that a restart makes them agree.

## 4. Secret resolution: confirmed absence versus failure

`EntraClientSecretAccess.ResolveAsync` returns a status with the value the sign-in uses; `TryResolveAsync` returns its value (unchanged) and no longer swallows cancellation.

| Status | Meaning | How it is reached |
|---|---|---|
| `Resolved` (`SecretStore`) | the registration's stored secret resolved | |
| `Resolved` (`DeploymentSecret`) | the deployment secret the registration asks for (`UsesDeploymentSecret`) is present | no stored reference, or the store confirmed it holds nothing for it |
| `Absent` | no secret, and that is established | the store answered "no such secret" (`SecretNotFoundException`, new, derived from `InvalidOperationException` with the existing message; or `SecretUnavailableException`; or a secret with no value) and no deployment secret applies |
| `Unavailable` | the store could not be read, so the secret the registration would use is not established | any other resolver failure, or a stored reference with no resolver. The value is still the deployment secret where the registration asks for it (the sign-in falls through, as always) |

The viability reading classifies `Unavailable` as **Entra unknown, whatever the deployment secret can stand in for**, because the secret the sign-in would then use is not the one the registration names. Cancellation is rethrown when the caller cancelled. A **database** failure is not an answer about the secret either, and the viability evaluation, which reads inside an audited unit, asks for it to propagate (`EntraSecretFailureHandling.PropagateDatabaseFailures`, §11); the sign-in and every other caller keep the containment described here. The resolution, the audit rows and the API responses never carry the reference, the secret or the resolver's error text (`ToString` of the resolution carries only the classification).

## 5. The deployment-secret fallback (no runtime change; owner decision if a change is wanted)

**Existing behavior** (unchanged, pinned by tests). When the active registration neither asks for the deployment secret (`UsesDeploymentSecret = false`) nor holds a stored one, the host keeps the deployment's `Entra:ClientSecret` in the options and enables Entra with it. The management API reports such a registration as having no secret (`ClientSecretConfigured: false`), and the directory search uses no secret for it.

**What WP3 did, without changing the runtime.** The viability reading applies the same rule the host applies, so it agrees with what the host runs; the overlay reports the **provenance** of the secret (`StoredSecret`, `DeploymentSecretRequested`, `DeploymentSecretLegacyFallback`), and the Restart availability carries a note when a registration is usable only through the fallback. It was **not** made "unknown" or "unusable" in the viability: such a registration is repairable only by saving it with the flag set (the API requires it), and an unknown pre-state would then refuse that very repair (a catch-22). The API can no longer create such a registration (it requires `usesDeploymentSecret`), so only data written before can be in this state.

**Owner decision, only if the behavior should change.**

| | |
|---|---|
| Existing behavior | above |
| Security consequence | Microsoft sign-in is enabled with a credential the registration does not own or name. If it belongs to another application the sign-in fails at token redemption, and the administrator is told the registration has no secret while it works; the viability reading counts it usable. |
| Compatibility consequence | Making the overlay honor `UsesDeploymentSecret` strictly disables Microsoft sign-in at the next restart for any legacy row that relies on the fallback. |
| Recommended correction | Use the deployment secret only where `UsesDeploymentSecret` is true, preceded by a data step that sets the flag on every legacy row that currently runs on the fallback, so nothing changes for them, and reports the rows it could not decide. The viability reading follows the overlay automatically (one rule). |
| Migration or deployment impact | One audited data step at start, an operator notice, a restart. |
| Tests needed | The overlay table of the unit suite with the flag cases flipped; a SQL case per legacy shape; the data step's idempotence; a differential test of the viability reading against the overlay. |

## 6. Findings made and fixed in this package

**The switch itself could fail.** `SqlEntraInstanceRegistrationRepository.SwitchActiveAsync` set the target active and the other registrations inactive in one save. The store allows one active registration (a filtered unique index) and writes the rows of one save in key order, so a target whose id sorts **before** the registration it replaces was written active beside it and the switch failed with a unique-index violation (a 500). Existing tests only activated from "none". Found because the switch cases ran with random ids. Fixed: the registration being replaced is deactivated and saved first, then the target is activated, both in the same transaction; every registration's `UpdatedUtc` and row version still move together as before. The first run of the matrix failed every Switch case whose randomly drawn ids happened to sort the wrong way (about half of them) with the unique-index violation; after the fix none does, and two regression cases pin both key orders (`Switching_*`).

## 7. Deviations from the approved text

1. **Gated "cannot reduce" for the sign-in policy** is implemented as the gate without a verdict: there is no viability state to read, because nothing it writes is an input. A10.2b.14's "calls the guard" is satisfied by the structural pin (no viability input reads a chooser flag) and the SQL cases that compare both views before and after.
2. **The instance-level rule refuses only a change that newly violates it** (§2). It is a refinement of request validation, not of authentication.
3. **A second refusal row vocabulary was not added** (as in WP2): a viability refusal of a registration change is recorded as `sign_in_lockout`.
4. `AuthenticationConfigurationGuard.ValidateDeleteActiveRegistration` has no production caller now and remains for its existing unit test.

## 8. Writer coverage

Integrated (WP2 and WP3): the administrator role (assign, remove, initial role), credential disable and enable, link add and remove, registration create, edit, delete, activate, the sign-in policy. Pending: the Entra-primary policy (WP4A); bootstrap, first administrator, bootstrap recovery, Recovery Access provisioning, designation, revoke, MFA reset and enrolment, and the command line (WP4B). Exceptions E-1, E-2 and C-14 are unchanged and still pinned. The guard now finds a registration or policy write by the **type** of the receiver (any name), pins the call count and the order in each method, fails on a direct write of the registration or policy tables outside the persistence layer, and fails if the removed simulation returns.

## 9. Remaining findings

F-1 (WP4B, mandatory), D-6 (owner decision before WP4B), R-2 (WP4B), the IAM-10.4 UI presentation of a refused change, the deployment-secret fallback decision (§5, only if a change is wanted).

Known until WP4A and WP4B land, and unchanged by this package: a writer that does not yet take the gate (the Entra-primary policy at sign-in, the bootstrap and Recovery Access writers) can change an administrator's contribution between a reducer's two reads. A reducer compares the whole system, so the effect is a refusal in the safe direction at worst (the WP2 review §2 states the same limitation for the raise-only writers).

## 10. Evidence

**Source.** Commits `8584e89` (WP3-A) and `2ec392a` (WP3-B). The binaries were built from `2ec392a` with a clean working tree before and after (`git status --porcelain` empty). Tree hashes of the built source: `src` = `fa7d0542ea1baa5da6a471c8f58b7ef2b80458ad`, `tests` = `1210430ef60bdb17eefe563870493f412adb442a` (`git rev-parse <commit>:src <commit>:tests`); the commit that adds this section changes documentation only. WP3-A was also built on its own in a temporary worktree (both test projects, 0 errors) and its 70 unit cases for the prerequisites and the guard passed there; its SQL cases ran in the development tree before the commit.

**Build** (clean: a new output directory, `--no-incremental`), from the repository root, .NET SDK 10.0.402, 9 minutes:

```
dotnet build tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --artifacts-path artifacts/wp3-final --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers -v:q -nologo
dotnet build tests/Apperture.UnitTests/Apperture.UnitTests.csproj             --artifacts-path artifacts/wp3-final --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers -v:q -nologo
```

Both: 0 errors. Tests were run with `dotnet test … --artifacts-path artifacts/wp3-final --no-build --logger trx`, only after that build of that tree.

| Run | Result |
|---|---|
| Whole unit project | 5632 passed, 0 failed, 0 skipped (5544 at WP2; 88 added) |
| Focused integration set (below) | 1025 passed, 0 failed, 0 skipped |

**Isolation modes**, read from the trx names: 424 cases carry `rcsi: False` (locking READ COMMITTED) and all passed; 424 carry `rcsi: True` (READ_COMMITTED_SNAPSHOT) and all passed; the remaining 177 take no isolation parameter (host suites and audit suites on a catalog of their own, in the default mode) and are counted toward neither. WP3 classes alone: `EntraRegistrationViabilityWp3SqlServerTests` 256 (128 and 128), `EntraOptionsInitializationSqlServerTests` 10 (5 and 5), `EntraRegistrationHostSqlServerTests` 6 (3 and 3), `EntraRegistrationViabilityHostHttpTests` 18 (9 and 9, each with its own host on the matching catalog). No SQL Server suite here skips: it fails without SQL Server.

**Focused integration set, by class:** the WP3 classes above; `AdministratorViabilityWp2SqlServerTests` 288 (including the corrected environment-change case), `AdministratorViabilityHostHttpTests` 12, `RoleAuthorityHostHttpTests` 19, `RoleAuthoritySqlServerTests` 148, `AdministratorViabilitySqlServerTests` 21, `PlatformAdministratorSetGateSqlServerTests` 14, `RunningHandlerViabilitySqlServerTests` 6, `IdentityGovernanceAuditSqlServerTests` 18, `EntraGovernanceAuditSqlServerTests` 12, `AuditRecorderSqlServerTests` 22, `PlatformAdminRoleRepairSqlServerTests` 28, `AccountServicesAuditSqlServerTests` 18, `AuthConfigurationAuditSqlServerTests` 20 (the existing registration and policy audit suite, on the new controllers), `EntraProbeTelemetrySqlServerTests` 24, `EntraRegistrationDiagnosticsMigrationSqlServerTests` 5, and the host suites `ManagementHostEntraProbeAuthorizationHttpTests` 6, `ManagementHostEntraLinkingHttpTests` 13, `ManagementHostSessionIdentityHttpTests` 9, `ManagementHostUserSearchHttpTests` 9, `ManagementHostUsernameRenameHttpTests` 15, `AuthorizationRemovalHostHttpTests` 7, `BrowserRequestTrustHttpTests` 21.

**What the WP3 cases cover.** Sequential matrix: every refusal and every allowed change of §1 for the four reducers, tenant change, switch with and without a second proven tenant, Recovery Access never counted, the fallback invariant not produced by any sequential case (not a claim about every interleaving, §1), an inactive registration, no active registration, no secret, a stored-only secret (activation raises, edit is judged on it), an invalid stored authentication context (activation refused, repair allowed), the legacy fallback, a secret-store outage (unknown, fail closed, message free of the reference and the resolver's text) beside a confirmed absence (known), unknown configuration (reducing refused, non-reducing allowed), no-ops (including when already unhealthy), 404, 403, the policy contract. Views: a switch refused in the Now view alone, a change refused in the Restart view alone, an inconsistent running view not stopping the repair, the simulated restart reconciling both views, tenant by authority only. Concurrency (deterministic, the first writer parked with everything it holds, the second shown waiting on the application lock and on no row): every registration reducer against every WP2 administrator reducer, in both orders (12 pairs each way on each mode) and sequentially, three controls with the gate removed (row versioning leaves nobody; locking READ COMMITTED shows a row-lock wait that is not the gate), a registration change unable to move the accepted tenant between an administrator writer's two reads, busy and cancellation for all four reducers. M6 for create, edit with a secret, delete and activate (audit-insert failure, lost acknowledgement, confirmed rollback with one secret stored, unknown outcome committed, operation lock not granted), a refusal after the write rolling back the new secret and the retirement together with a failing refusal row. Lock order: the gate is the first lock after the operation lock, before any registration or secret row, for create, edit, delete, activate and the policy.

**Mutation checks** (source edited in a working copy, a separate build directory, the file restored from a byte-identical backup and its hash compared; the build directory deleted afterwards).

| Mutation | Fails |
|---|---|
| The coordinator no longer takes the gate | 101 of the 256 WP3 SQL cases: all 48 concurrent pairs, the lock-order, busy, cancellation and M6 gate-count cases, the policy ordering cases |
| The verdict of an applied change is ignored | 64 of the 256: every refusal case, the Recovery Access, tenant, view, unknown-configuration and secret-rollback cases |
| `PrepareAsync` builds nothing | 2 unit and 6 SQL cases (the preparation, the options never read behind the unit's locks, the order test, the create reading nothing) |
| An unreadable secret store is read as an absence | 10 unit and 4 SQL cases |
| A degraded build is not distrusted | 3 unit and 6 SQL cases |
| M-2's whole-system comparison restored | the original fixture 10 of 20, the corrected 12 of 20 (see the [WP2 review record](iam10-2b-wp2-review-record.md) §8) |
| `Prepare` removed from the controller | the writer guard's order test |
| The switch as it was | the first run of the matrix, before the fix (§6) |

**Not executed, and limitations.** The full multi-hour integration suite was not run. No Browser or UI test was run. A row-lock deadlock victim rerun was not written for the registration units (the recorder's rerun is covered by the confirmed-rollback case, which takes the gate again and stores its secret once, and the deadlock rerun for the same recorder by the WP2 case). An HTTP case for a host whose Entra options fail validation cannot be built (such a host fails its own pipeline before the writer runs); it is covered at the SQL level. A rebuild of the options on a reload that coincides with an in-flight registration write is not prevented: under locking READ COMMITTED it is recorded as degraded and fails closed, under READ_COMMITTED_SNAPSHOT it can be recorded as complete and is not detected (§3). The deadlock rerun for the registration units' own secret read is the subject of §11.3.

## 11. Review remediation: M-1, L-1, L-2

The independent review accepted WP3 with three bounded findings and no design amendment. Nothing here reopens a ruling D-1 to D-6 or the design gate. The fallback policy is unchanged: no owner decision was needed, and the legacy deployment-secret behavior (§5) is untouched.

### 11.1 M-1: a change whose result proves a Local fallback needs no Microsoft reading

**Finding.** A persistent secret-store failure (an unresolved DPAPI key, secret reference or provider) leaves the Microsoft reading unknown, and the guard refused every change that altered something an invariant reads while either reading was unknown, even when an ordinary administrator who signs in locally remained. A registration repair, a deletion, a switch or the removal of a compromised administrator's role could therefore be blocked indefinitely.

**Rule.** `AdministratorViabilityGuard.Evaluate` now allows a change when `AdministratorViabilityGuard.ProvesSafeWithoutEntra(post)` holds for the state the change produced (read in the writer's transaction, under the gate). It runs after the unchanged-input shortcut and before the trust requirement. It holds only when all of these are true:

1. Local sign-in is established (`Trust.LocalKnown`).
2. The stored administrator facts and the Recovery Access posture inputs are present, as a state read from the database always carries them.
3. Applying the one definition of an Entra-independent administrator (`AdministratorViabilityEvaluator.Evaluate`: an ordinary administrator, not Recovery Access, with an enabled Local credential and Local link while Local sign-in is on) to those facts under each view's own availability yields at least one administrator in the Now view **and** at least one in the Restart view, and the state's own snapshots (VOA and EI) equal that derivation in both views. A snapshot that claims more or less than the facts prove is not believed.

**Why it is sound.** EI is a subset of VOA. Where EI has a member, VOA does too, so I-VIABLE cannot be newly violated in that view, and I-FALLBACK is satisfied whether or not Recovery Access is healthy. Both views are checked, so both invariants hold in the result. A change cannot move the system from a state that satisfies an invariant to one that does not if the result satisfies it, whatever the starting state and whatever Microsoft sign-in does. Nothing about Microsoft is trusted: the proof never reads the Entra trust, the pre-state, an aggregate count of role holders or Recovery Access health.

**What it does not change.** It is not a general exemption. These still fail closed exactly as before (each is a unit case and the SQL cases of §11.5): Local setting unknown or untrusted; Local sign-in off in either view; a Local credential disabled or a Local link missing; a result whose only administrators are Recovery Access or depend on Microsoft (a role-holder count of one or more is not a proof); the removal of the last Local administrator; facts, posture inputs or snapshots that are missing or disagree. Untouched: the WP2 unchanged-input shortcut and its order, `Detect`, the raise-only assertion, C-13-R (`EvaluateRevocation`, which still needs a distinct ordinary Local administrator), the Now and Restart distinction, the gate and operation-lock order, and the running authentication's own fallback behavior.

**Effect on earlier cases.** Four families of earlier tests arranged a Local administrator who remained and expected the refusal that the old rule gave (the WP2 reductions under an untrusted configuration, `R1`, the unchanged-facts case and its host test, and the host whose options were built degraded). Each now states what it was about: its fixture leaves only an administrator who depends on Microsoft (and healthy Recovery Access), so the unreadable Microsoft reading alone refuses it, and a twin case states the new allowed outcome with a Local administrator in place. No expectation was loosened to make a case pass.

### 11.2 A persistent secret-store failure: behavior and recovery

- **What the operator sees.** The registration's client secret cannot be read, so what Microsoft sign-in does is not established (`administrator_viability_unavailable` for a change that cannot be shown safe).
- **A restart alone does not repair it.** The options are rebuilt from the same unresolved DPAPI payload, reference or provider, so the running options and the persisted reading stay unknown until the cause is fixed. The sign-in itself keeps falling through to the deployment secret where the registration asks for it, as it always has.
- **With a proven Local fallback, guarded configuration repair is allowed.** While an ordinary administrator who signs in locally remains after the change, the writers may replace the registration's client secret, disable, delete or switch the registration, and remove a compromised administrator's role, each committing atomically with the secret it stores or retires and without a refusal row. A successful repair changes the persisted configuration; an orderly restart is needed before the running Entra handler uses a repaired secret. Immediate Microsoft recovery is not claimed and the tests do not show it.
- **Without one, unsafe changes keep being refused.** A change that moves something an invariant reads, and whose result has no proven Local administrator, is refused with `administrator_viability_unavailable` until trust is restored. A change that moves nothing an invariant reads (a rename, a replacement secret under an unchanged configuration) is still allowed.
- **Restoring trust** is done through an authorized recovery mechanism of the product: restore the secret provider the payload was protected under (the DPAPI scope or key, the reference's provider), or use the supported emergency path of the design (Recovery Access, design gate §3.8; the emergency writers arrive with WP4B). This record describes no direct modification of the database, and none is supported.

### 11.3 L-1: a database failure is the unit's, not a business refusal

**Finding.** A SQL deadlock or timeout while the viability evaluation resolved the secret inside an audited unit was swallowed and classified as `Unavailable`, so a transient failure of the unit's own transaction could become a business refusal instead of the unit's rollback and retry.

**Contract.** `EntraClientSecretAccess.ResolveAsync` and `EntraAuthOptionsOverlay.ApplyDetailedAsync` take an explicit `EntraSecretFailureHandling`. The default, `Contain`, is the behavior of every caller before: the sign-in's option build, the directory search and the rest. Only `EffectiveSignInConfigurationProvider`, which reads inside the writers' units, asks for `PropagateDatabaseFailures`. No retry was added around secret resolution.

| What the resolver raised | Result in the viability read | Then |
|---|---|---|
| A `DbException`, directly or anywhere in the inner-exception or aggregate chain (a deadlock victim, error 1205; a command timeout, error -2; any provider error) | propagates unchanged | the unit rolls back. The execution strategy reruns a deadlock victim: the gate is taken again and the viability decided again. A command timeout is not retried by the strategy, as for any other statement in the unit: the call fails and the unit rolls back. No refusal row in either case |
| `SecretNotFoundException`, `SecretUnavailableException`, or a secret with no value | `Absent` (a confirmed absence), as before | decided on |
| Any other failure: an unreachable store (I/O, timeout, HTTP), no resolver for a stored reference, a key that cannot be unprotected | `Unavailable`, so the Microsoft reading is unknown, as before | a refusal where a refusal is needed, never a rerun |
| Cancellation | rethrown | as before |

### 11.4 L-2: the two overclaims

Corrected in place: §1 (registration writers and `entra_independent_fallback_required`; the interleaving with recovery-code consumption and the conservative, retryable refusal), §3 (R-1 under READ_COMMITTED_SNAPSHOT: a rebuild inside a transaction can read the previously committed registration successfully and be recorded complete; the reason for preparing the options before the unit is retained), §10 (the coverage line) and the limitations paragraph of §10.

### 11.5 Evidence

**Source and provenance.** The remediation is one commit on top of `8d8575c`. Tree hashes of the final tree (`git rev-parse <commit>:src <commit>:tests`): `src` = `dd3a3e05bfcddf75e1da311a4c6745cddff3055d`, `tests` = `3e2a8252aea044b0dc3ef6d8ea159cfface8f001` (the tree before remediation: `fa7d0542ea1baa5da6a471c8f58b7ef2b80458ad` and `1210430ef60bdb17eefe563870493f412adb442a`). The binaries were built from the staged tree with the working tree otherwise clean (documentation aside). The integration project was built and run from a tree whose `tests/Apperture.IntegrationTests` subtree (`24e8a36fe9b0991f664b479e85a6c5e046b2710b`) is identical to the final one. One unit case was added afterwards, when a mutation survived (below), so the unit project was built clean and run again from the final tree. .NET SDK 10.0.402.

**Clean builds** (a new output directory, `--no-incremental`, from the repository root):

```
dotnet build tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --artifacts-path artifacts/wp3r-final  --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers -v:q -nologo
dotnet build tests/Apperture.UnitTests/Apperture.UnitTests.csproj             --artifacts-path artifacts/wp3r-final2 --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers -v:q -nologo
```

Both: 0 errors (integration 3 min 54 s, 48 warnings; unit 4 min 7 s, 91 warnings). Tests ran with `dotnet test … --no-build --logger trx` against those outputs. An earlier clean build of the unit project from the tree before the added case (`artifacts/wp3r-final`) ran 5680 of 5680.

| Run | Result |
|---|---|
| Whole unit project, final tree | 5681 passed, 0 failed, 0 skipped (5632 at WP3; 49 added: 21 for the guard's proof, 28 for the secret-failure modes) |
| Focused integration set (the 26 classes of §10) | 1095 passed, 0 failed, 0 skipped (1025 at WP3; 70 added) |

**Isolation modes**, read from the trx names: 459 cases carry `rcsi: False` (locking READ COMMITTED) and all passed; 459 carry `rcsi: True` (READ_COMMITTED_SNAPSHOT) and all passed; the other 177 take no isolation parameter (host and audit suites on catalogs of their own). The 70 added integration cases: 50 in `EntraRegistrationViabilityWp3SqlServerTests` (38 for M-1: allowed changes, refusals, atomic rollback of a replaced secret, Local-fallback trust boundaries, the removal of the last Local administrator; 12 for L-1), 2 host HTTP cases (`EntraRegistrationViabilityHostHttpTests`), and the twins of the re-based cases (16 for the WP2 reductions, 2 for the host whose options were built degraded). The existing host test of an untrusted configuration was extended in place, not counted.

**What the new cases show.**

- *M-1, sequential, both modes.* With a secret store that always fails (the resolver, and at the host a stored secret DPAPI rejects) and a Local administrator remaining: replace the registration secret (the new secret stored, the old one retired, one stored and one retired audit row), disable, delete (secret retired), switch, and remove a compromised administrator's role all commit, with no refusal row and the audit row of the success. With no Local administrator: disable, delete, switch and the role removal are refused with `administrator_viability_unavailable`, the refusal is recorded once for a registration change, everything rolls back, and healthy Recovery Access does not change that. A disable that supplies a replacement secret rolls the new secret and the retirement back together. Trust boundaries: Local setting unknown, Local sign-in off, Local credential disabled and Local link missing each stay refused; so does removing the last Local administrator although one existed before.
- *M-1 at the Management Host.* A stored secret DPAPI cannot unprotect: a delete is a typed 409 until an ordinary administrator signs in locally, then a 204. A host whose options were built degraded allows a change that leaves a Local administrator and still fails closed without one.
- *L-1.* Exceptions are real: a deadlock victim (1205) captured from a deadlock between two sessions, and a command timeout (-2) captured from SQL Server, thrown from the resolver at the first secret read inside the unit. A deadlock victim reruns the unit (2 attempts, the gate acquired twice, the registration activated, no refusal row, no row of the refusal action); an unreachable store is still an unknown reading, a refusal and one attempt; a command timeout fails the call, rolls back and is not retried; a failure that never clears leaves the state unchanged and surfaces the database error itself.
- *Unit.* The guard's proof against every boundary above, snapshot-against-facts disagreement, per-view derivation; the three resolution modes (database failure in any position of the chain, store outage, confirmed absence, cancellation) in the viability mode and the unchanged default.

**Mutation checks** (one line of source edited in the working tree, a separate output directory, the file restored from a byte-identical backup and its hash compared; each restored file's hash was identical). The runs were limited to the cases for the mutated behavior.

| Mutation | Fails |
|---|---|
| The safe-post-state proof never applies (A) | 10 of 51 unit cases and 28 of 58 integration cases (every M-1 allowed change, the twins, the host cases) |
| A database failure is swallowed again, as before (B) | 8 of 28 unit cases and 8 of 12 L-1 cases (the deadlock rerun, the timeout, the persistent failure) |
| The viability read asks for the default mode, so the failure is contained (F) | 4 unit cases and 8 of the 12 L-1 cases |
| The proof ignores whether the Local setting is established (C) | 2 unit cases and the 2 SQL cases for an unknown Local setting |
| The proof counts role holders and not Entra-independent administrators (D) | 4 unit cases (an administrator who depends on Microsoft is no proof) |
| The proof derives the Restart view under the Now view's availability (E) | none at first, because the snapshot cross-check hid it. One unit case was added (a snapshot that overstates one view against that view's own availability); with it the mutation fails 1 of 52 and the final tree passes it |

**Failures and corrections during the work.** The first focused run showed 24 earlier cases failing: they arranged a Local administrator that remained and expected the old refusal. They are the cases described in §11.1; each is re-based on a fixture that leaves only a Microsoft-dependent fallback, with a twin for the allowed outcome. Two of the new cases were mis-specified and corrected: a disable with Local sign-in off is a 400 of the instance-level rule (the case uses a switch), and the execution strategy does not retry a command timeout (the rerun case is the deadlock; the timeout has its own case).

**Not executed, and limitations.** The full multi-hour integration suite was not run. No Browser or UI test was run. The deadlock and the timeout are real exceptions captured from the server and thrown at the first secret read in the unit, not a deadlock arranged to strike that very statement (under READ_COMMITTED_SNAPSHOT a reader does not wait on a row lock, so such a deadlock cannot be arranged there). Whether a restart brings Microsoft sign-in back after a repair is not tested: the cases establish the decision and the persisted state, not the running handler. Earlier limitations of §10 stand. The dedicated test SQL Server container (`apperture-test-sql`) was stopped at the start and was started for these runs.
