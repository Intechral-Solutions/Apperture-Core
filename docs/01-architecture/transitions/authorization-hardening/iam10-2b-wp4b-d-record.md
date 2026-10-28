# IAM-10.2b WP4B-D: bootstrap writers under the global administrator-set gate (M-P1)

| | |
|---|---|
| Package | IAM-10.2b, work package 4B-D ([decision record](iam10-2b-wp4b-decision-record.md) §7.2, §8, §9, §13), on the verified [WP4B-A](iam10-2b-wp4b-a-record.md) foundation (the gate-only entry). |
| Branch | `feat/iam-10.2b-administrator-viability`, from `c5062baf` (the synchronisation with `origin/master`, which carries the .NET 10.0.13 dependency remediation). Not pushed, not merged. |
| Status | Implemented and validated (§8). Awaiting independent security review. |
| Closes | **M-P1**: two concurrent first-administrator requests with different names both created an administrator. |
| Not in scope | WP4B-B (attestation), WP4B-C (designate/revoke/reset, MFA lifecycle, Recovery Access provisioning, L-P2, L-P3), WP5, IAM-10.4. No schema, migration, package or CI change. |

## 1. Root cause

`FirstAdminBootstrapService` decided "no user exists yet" (`GetUserCountAsync`) inside its audited unit, but nothing serialised two requests. The unit's operation lock is `apperture.audit-op.<operationEventId>` and the event id is generated per call, so two requests never contend on it. Under locking READ COMMITTED the count is a shared-lock read that is released at once; under READ_COMMITTED_SNAPSHOT it is a version read. Either way both requests saw zero users, both inserted a user, a link, a credential and the role assignment, and both committed. A uniqueness constraint did not help when the two names differed, and the early count added in WP4B-A is by design only a reason to answer early.

The deferred regression, run against unchanged code with the skip removed, failed on both modes with two successes (red run before any production change).

## 2. The correction

The first-administrator writer takes the **global administrator-set gate** (`IAdministratorViabilityCoordinator.EnterGateOnlyAsync`, the WP4B-A entry) as the first statement of its audited attempt, before the decisive `GetUserCountAsync`, the role read, the identity lookup and every write. The gate is a transaction-owned `sp_getapplock` held until the unit ends and is taken again by every attempt (a rerun after a confirmed rollback is a new transaction). A second request therefore waits for the first to commit or roll back and then reads the committed state: one `Success`, one `AlreadyExists`, which is the existing refusal.

Unchanged by design:

- The early `GetUserCountAsync` before the unit stays, and answers `AlreadyExists` for a set-up installation **without the gate or the unit**, so an anonymous caller cannot queue on the gate once a user exists (decision record §7.2). A test and a source pin hold that.
- Lock order is the operation lock, then the gate, then (for the other writers) row locks. No new lock, no new resource name, no second gate.
- `Refuse` still declares the attempt a no-op, so a loser commits nothing and writes no audit row.

## 3. Writer coverage

| Writer | Creates or raises an administrator? | Status |
|---|---|---|
| `FirstAdminBootstrapService.TryCreateFirstAdminAsync` (anonymous `POST /api/bootstrap/first-admin`) | Creates the first user with the role | **Now gate-only.** Cannot reduce: it writes a new account only when no account exists. |
| `BootstrapPlatformAdminAssignment.ExecuteAsync` (configured administrator, host start) | Creates a user with the role, or assigns the role to an existing user | **Now gate-only**, once per principal unit, before the lookup that chooses between creating and assigning. Cannot reduce: creates a new account or adds the role to one that lacks it. |
| `RealBootstrapRecoveryService.ExecuteRecoveryAsync` (restore platform access) | Assigns the role to a target | **Now gate-only**, before the target is read. An already-holding target is still a no-op. Has no production caller (dormant interface, unchanged and pinned). |
| `DevTestUserBootstrapHostedService` | Creates/assigns in Development | **Excluded: approved departure E-1**, unchanged. It runs only in the Development environment, outside an audited unit, as decided. |
| `EmergencyAccessService` provisioning | Creates a designated user | **Excluded: WP4B-C**, classified as a gated raiser of that package, as in the decision record. |
| `UserProvisioningService`, `UserManagementService` role assignment | Assign roles | Already under the gate (WP2). |

The writer guard moves the three writers to integrated package `WP4B-D`: each has a method-level order sequence (gate, then the reads and writes), each is in the closed `GateOnlyCallers` set with its `// Cannot reduce:` reason, and the hosted service that resolves the coordinator is listed with the coordinator users.

## 4. Contention, cancellation and failure

| Condition | Result |
|---|---|
| Gate held past its bound (15 s default) | `PlatformAdministratorSetBusyException`, nothing written. First administrator: the controller answers **409 `bootstrap.busy`** with neutral wording and the unchanged `initialized` flag; it is not `bootstrap.already_initialized`, records no refusal row, and says nothing about administrator access. Configured administrator at start: the exception propagates and **the start fails**, like every startup step that did not complete; a configured administrator is never silently skipped. Recovery: propagates to its caller. |
| Cancellation while waiting | `OperationCanceledException`; neither contention nor a refusal; the waiting request disappears from the lock list and the gate is free. |
| Lock call fails for another reason | `PlatformAdministratorSetGateException`, not mapped, reaches the host's handler (500). |
| Missing coordinator | `InvalidOperationException` before any write: an unserialised first administrator is the defect being closed. |
| Database failure inside the unit | Unchanged: the unit rolls back, the gate is released with the transaction. |

## 5. Audit behaviour

Unchanged. The winner commits one `bootstrap.first_admin.created` row (actor `anonymous`) with its user, link, credential and role. A loser changes nothing and records nothing in its unit; the controller's existing anonymous refusal row for `AlreadyInitialized` is unchanged. Contention records nothing.

## 6. Test construction

Real SQL Server (the disposable `apperture-test-sql` instance), throwaway catalogs, a `Theory` over locking READ COMMITTED and READ_COMMITTED_SNAPSHOT (the fixture asserts the catalog really runs under the mode). Nothing is timing-based: the first request is parked by a command interceptor **after** the decisive count read inside its unit, holding the gate; the test then waits until the server's own lock list shows the second request waiting on an `APPLICATION` lock (and not on a row), and only then releases the first.

`FirstAdministratorConcurrencySqlServerTests` (12 cases, 6 shapes x 2 modes): different names; the same name; gate held past the bound (typed busy, nothing written, retry succeeds); cancellation while waiting (nothing written, wait gone, gate free, next request succeeds); an already-set-up installation never waits for a gate another change holds; a refusal under the gate (reserved name) releases the gate and leaves the sequential contract intact. The race cases assert one user, one link, one credential, one role assignment, the winner's ids, exactly one success audit row, the loser's username absent, and the gate free afterwards.

`BootstrapWritersGateSqlServerTests` (12 cases, 6 shapes x 2 modes): the configured administrator (create, assign to an existing user, busy) and the recovery restore (assign, busy) each wait for a gate held by another session and write nothing until it is free; a configured administrator waits for a first-administrator request that holds the gate and is decided after it.

Unit tests (12 new, plus fakes updated): gate before the decisive read, never touched for a set-up installation, refusal still a no-op, busy and non-contention failures, missing coordinator, per-principal gate, no gate for an entry skipped before any unit, controller mapping (409 `bootstrap.busy` neutral; gate failure and cancellation unmapped), and guard pins (order sequences, closed gate-only set, early answer outside gate and unit).

**Red/green.** The first-administrator case failed on the unchanged code on both modes; the configured-administrator and recovery cases failed on the unchanged code (no waiting); all pass after the change.

**Mutation checks** (each restored byte-for-byte, hash verified): M1 gate removed from the first administrator (4 unit, 8 SQL fail); M2 gate taken after the count (4 unit incl. guard, 2 SQL fail); M3 early exit removed (7 unit, 2 SQL fail); M4 gate removed from the configured administrator (3 unit, 8 SQL fail); M5 gate removed from the recovery restore (2 guard, 4 SQL fail); M6 controller busy mapping removed (1 unit fails); M7 full viability entry instead of the gate-only entry (3 guard fail).

## 7. Known limitations

1. The anonymous endpoint remains open on an empty installation (OPS-1.4); this package serialises creation, it does not authenticate the caller. Between the first request committing and the second being answered the second sees `AlreadyExists`, as required.
2. The configured administrator at start waits up to the gate bound (15 s) per principal behind another change; a longer hold fails the start (by design, §4).
3. `DevTestUserBootstrapHostedService` (E-1) and Recovery Access provisioning (WP4B-C) are not under the gate; see §3.
4. The gate-only proof is by construction (a new or role-lacking account). WP4B-C must still address `UserManagementService` serialisation with a designation (L-P3) and `MarkEnrolledAsync` (L-P2).
5. The first-administrator HTTP endpoint is validated through the service on real SQL; no new Management Host HTTP test sends two simultaneous anonymous POSTs, since the existing host harness suite (`ManagementHostBootstrapHttpTests`) is part of the run and unchanged.

## 8. Validation

| Run | Result |
|---|---|
| Clean `--no-incremental` build of `Apperture.slnx` | 0 errors, 71 warnings (same as the post-sync baseline), 10 min |
| UnitTests | **5,800 passed** (5,788 before), 0 failed, 0 skipped |
| ArchTests | 38 passed |
| Ui.Shared.Tests / Ui.Client.Tests / AppHost.Diagnostics.Tests | 1,497 / 529 / 7 passed |
| Focused integration set (the 40-class baseline, plus the new gate class, the Management Host bootstrap HTTP class and the startup-order class; both isolation modes) | **1,361 passed, 0 failed, 0 skipped** in 38 min 43 s (baseline 1,337 passed, 1 skipped); the M-P1 case now executes and passes on both modes |
| SQL execution proof | Target `apperture-test-sql` (`127.0.0.1,1433`, the `.env` value; no environment override); throwaway catalogs plus the shared fixtures: `Apperture_BootstrapHttpTests` writes 27,201 to 31,132 and `Apperture_ManagementGovernanceTests` 7,955 to 9,102 during the run; the development SQL (`:14333`) was not contacted |

The known intermittent `ConnectionToolInvocationTests.I2_a_third_concurrent_request...` failure on `master` was not touched and did not occur in the final run. The full two-hour IntegrationTests project and the browser suite were not run.

## 9. Remaining IAM-10.2b work

WP4B-B (attestation), WP4B-C (designate/strict revoke/bare reset, replacement candidate and switch, provision/enrol/regenerate as gated raisers, command-line mapping, L-P2, L-P3, writer guard to integrated) and WP5 remain unstarted. M-P1 is closed by this package, subject to its independent review; L-P2 and L-P3 stay with WP4B-C.
