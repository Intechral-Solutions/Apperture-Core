# IAM-10.2b WP3: Entra registration and sign-in policy writers

| | |
|---|---|
| Package | IAM-10.2b, work package 3 ([WP0 reconciliation](iam10-2b-wp0-reconciliation.md) §7, rows marked WP3; §8 F-5), on the [WP1](iam10-2b-wp1-review-record.md) foundation and the [WP2](iam10-2b-wp2-review-record.md) writers. |
| Branch | `feat/iam-10.2b-administrator-viability`, from `1883d87`. Not pushed, not merged. |
| Status | Implemented and validated; awaiting the independent security review. Nothing here reopens a decision of the [design gate](iam10-design-gate.md) or a ruling D-1 to D-6. |
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

`entra_independent_fallback_required` is never produced by these writers: a registration does not change who can sign in locally, so it cannot reduce the Entra-independent set (tested).

**Order** (A10.2b.14), checked method by method by the writer guard: authorization and request validation (400, 403, 404 unchanged); the running options are built (`PrepareAsync`, §3); then inside the audited unit: the operation lock, **the gate**, the registration read for the decision, the no-op (`DeclareNoOp`, before the invariants), the state before (under the gate, before the first write), the secret stored if one was supplied, the change applied through the repository, the applied change decided (instance-level rule, then viability), and only then audit rows staged and the replaced or deleted registration's secret retired. A refusal declares the attempt a no-op: the unit rolls back the registration update, the stored secret and the retirement together, and the refusal row is written after the rollback on a context of its own (a failure to write it is logged and never changes what the caller sees).

**Create** reads no viability state. A new registration is never the active one, so it cannot change what the sign-in accepts; the writer asserts that of its own effect (the persisted active registration before and after, `RegistrationRaiseOnlyAssertion`), not of the whole system. A configuration reload, a Recovery Access recovery code consumed at sign-in, or an unrelated change in the posture can therefore never fail or refuse it (the lesson of the WP2 review, M-2). The gate is taken first, so a busy gate stores nothing.

**Sign-in policy.** The chooser flags are not an input of administrator viability: the direct Local login route is available whenever Local sign-in is enabled, whatever the chooser shows, and no production endpoint is restricted by these flags. The writer takes the gate (it is serialized with every other writer of the sign-in configuration) and reads no viability state. Its only new outcome is a busy gate (409 `governance_operation_busy`, `retry: true`). Tests show both views and Recovery Access health identical before and after every flag combination, and the Local administrator viable and Entra-independent with the chooser hiding the Local route. Request validation (400 for an unsatisfiable policy), row-version conflicts (409 with the existing body), the no-op and authorization are unchanged.

## 2. The instance-level rule and the viability invariants

`AuthenticationConfigurationGuard.ValidateNoLockout` ("some sign-in path remains", and a chooser policy that stays satisfiable) is kept, as a 400, and is now asked of the **persisted state the change produced** (the Restart reading of the effective sign-in configuration), through the same two facts it has always read (`ValidateNoLockout(bool localSignInEnabled, bool entraSignInUsable, policy)`; the options form delegates to it). The hand-built simulation (`SimulateEffectiveEntraAfterUpdate`, the legacy-only clone for activation and deletion) is removed: it read only the deployment secret, so it called an edit of a registration that holds its own stored secret a lockout (WP0 F-5). Two refinements, both in the direction of "an already-invalid pre-state is never a reason to refuse an unrelated or repairing change":

- the rule refuses only a change that **newly** violates it (satisfied before, violated after). An instance that already has no path no longer refuses a rename of an inactive registration;
- a reading whose Local or Entra input is not established asserts nothing; administrator viability, which fails closed, decides then.

Viability is evaluated by `AdministratorViabilityGuard.Evaluate` over `AdministratorViabilityScope.CaptureBeforeAsync` and `VerifyAfterAsync`, unchanged. The instance-level rule is checked first (400), then viability (409). A verdict is recorded with the operation's existing refusal row (`sign_in_lockout`); the audit vocabulary is not changed, so the row does not say which invariant was violated (as in WP2; the code is in the response).

## 3. R-1: initializing the running Entra options

**The hazard, shown on both isolation modes** (`EntraOptionsInitializationSqlServerTests.Control_*`). The Entra options are built on first use and cached by the framework, and the build reads the persisted registration and its secret through a scope and a connection of its own (`EntraAuthOptionsDatabasePostConfigure`). Inside a unit that has written a registration row: under locking READ COMMITTED the build **waits for the unit's own uncommitted row** (the unit would wait on itself until the command timeout, after which the build swallows the error and caches the options without the registration); under READ_COMMITTED_SNAPSHOT it reads **the row as it was before the unit** and the framework keeps that. The first operation being a configuration-management request is exactly when this would happen.

**Resolution.**

1. `IAdministratorViabilityCoordinator.PrepareAsync` builds, **outside any transaction and before the unit opens**, everything a viability reading is made from (`IDeploymentAuthenticationConfiguration.EnsureInitialized`: the endpoints' options, the handler's options). Every registration and policy writer calls it before `Unit.ExecuteAsync`; the writer guard checks the order. A failure to build is not swallowed into a reading.
2. **The build is recorded, not hidden** (`IEntraOptionsBuildHealth`, a singleton note against each options instance, holding no value and no secret). A build that could not read the registration or its stored secret still leaves the deployment configuration in place and still does not throw (the production behavior is unchanged), but it is recorded as degraded. The Now view then reports the **Entra reading as unknown** ("the running sign-in options were built while the Microsoft registration or its client secret could not be read; what the host accepts now is not established until the host is restarted"): a change that could reduce viability fails closed with `administrator_viability_unavailable`, and a restart makes the reading settled again. A degraded build is therefore never an optimistic viability result.
3. **Rejected: initializing at host start.** A start-up initializer would pin the options before the registration state is guaranteed ready, which is the risk R-2 (WP4B) tracks; and it would not help the case that matters (a first request after a database that was unavailable at start). Rejected: reading the persisted registration for the Now view instead of the held options (an independently maintained authentication snapshot, which the design forbids).

**Residual, stated.** An options **rebuild** on a configuration reload that coincides with an in-flight registration write is the same hazard for the monitor's rebuild; it is recorded as degraded (reading unknown, fail closed) and logged, not prevented. Restart-dependent behavior is explicit: the Now view follows the options the host holds; the Restart view follows the persisted registration; `EntraRegistrationHostSqlServerTests` shows a registration deleted on a fresh host leaves the host's held options on the old tenant while the Restart view has none, and that a restart makes them agree.

## 4. Secret resolution: confirmed absence versus failure

`EntraClientSecretAccess.ResolveAsync` returns a status with the value the sign-in uses; `TryResolveAsync` returns its value (unchanged) and no longer swallows cancellation.

| Status | Meaning | How it is reached |
|---|---|---|
| `Resolved` (`SecretStore`) | the registration's stored secret resolved | |
| `Resolved` (`DeploymentSecret`) | the deployment secret the registration asks for (`UsesDeploymentSecret`) is present | no stored reference, or the store confirmed it holds nothing for it |
| `Absent` | no secret, and that is established | the store answered "no such secret" (`SecretNotFoundException`, new, derived from `InvalidOperationException` with the existing message; or `SecretUnavailableException`; or a secret with no value) and no deployment secret applies |
| `Unavailable` | the store could not be read, so the secret the registration would use is not established | any other resolver failure, or a stored reference with no resolver. The value is still the deployment secret where the registration asks for it (the sign-in falls through, as always) |

The viability reading classifies `Unavailable` as **Entra unknown, whatever the deployment secret can stand in for**, because the secret the sign-in would then use is not the one the registration names. Cancellation is rethrown when the caller cancelled. The resolution, the audit rows and the API responses never carry the reference, the secret or the resolver's error text (`ToString` of the resolution carries only the classification).

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
