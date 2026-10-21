# IAM-10.2b WP2: independent review record and remediation

| | |
|---|---|
| Package | IAM-10.2b, work package 2, after the independent security review of commit `1b7076f` ([WP2 record](iam10-2b-wp2-record.md)). |
| Verdict | **WP2 accepted with bounded remediation.** The gate, the lock order, the reducers and the refusal contract were assessed as sound. Two medium findings (M-1, M-2) and three low findings (L-1 to L-3) are remediated in the commit that adds this record. |
| Branch | `feat/iam-10.2b-administrator-viability`. Not pushed, not merged. |
| Not in scope | WP3, WP4A, WP4B, F-1, D-6, R-1 and R-2 production corrections, the deployment-secret fallback, the IAM-10.4 UI, and new audit actions solely for invariant-reason granularity (left for WP5 consideration; no owner decision is needed now). |

## 1. M-1: a change to an ordinary user was refused when the Entra configuration could not be trusted

**Root cause.** `AdministratorViabilityGuard.Evaluate` required a trusted configuration (both the Local setting and the Entra reading) before it looked at what the change did. A credential disable or a link removal for a user who holds no administrator role, with an unreadable OpenID Connect handler or invalid Entra options, was therefore refused with `administrator_viability_unavailable` although it cannot reduce anything an administrator-viability definition reads.

**Correction.** `AdministratorViabilityState` now carries the stored facts it was derived from (every holder of the administrator role with their Local link, credential, proven Entra tenants and Recovery Access flag, and the Recovery Access posture inputs). `Evaluate` first asks `AdministratorViabilityState.InputsUnchanged(pre, post)`: true only when the facts read under the gate before the change and the facts read in the same transaction after it are identical **and** the effective configuration (both availabilities and the trust levels) is identical. Then the change is allowed without a trusted configuration. Anything else goes on to the unchanged full evaluation, which fails closed.

| | Behavior |
|---|---|
| Ordinary user (no administrator role), credential disable, Local link removal, Entra link removal | Allowed under an unreadable handler or invalid Entra options; no refusal row. |
| Administrator target: role removal, credential disable, removal of a Local link or of a **proven** Entra link | A fact changes, so the full evaluation runs and fails closed: `administrator_viability_unavailable`. Unchanged. |
| Administrator's **unproven** (pending) Entra link removed | No fact a definition reads changes (a pending link is not a sign-in method under any configuration), so it is allowed. |
| Facts missing (a state built without them), or the configuration also changed between the reads (a registration or sign-in policy writer) | Not "unchanged"; fully evaluated. A configuration-changing writer is never short-circuited by few facts moving. |
| Order | Authorization, system-managed refusals and the no-op come first and are untouched; the gate and lock order are untouched; the comparison uses facts read under the gate in the writer's own transaction, never an earlier read (a user who becomes an administrator before the writer gets the gate is judged as the administrator they are). |

## 2. M-2: the raise-only assertion blamed the writer for changes it did not make

**Root cause.** `AssertRaiseOnlyAsync` compared two whole-system evaluations (`Detect(before, after)`). A configuration reload, a Recovery Access recovery code consumed at sign-in (ruling C-14), the designation flag, or another administrator's change by a writer not yet under the gate (WP3, WP4A, WP4B) between the two reads could fail a raise that only added viability, rolling it back as an internal error.

**Writer-attributed design.** A raise-only writer names the user it changes (`CaptureBeforeForRaiseAsync(userId)`). After its change, `RaiseOnlyAttribution.FindReduction` takes **that user's** stored facts before and after and applies the one definition (`AdministratorViabilityEvaluator`) under the **configuration captured before the change**, in both views. It fails only if the user was a viable ordinary administrator (VOA) or an Entra-independent administrator (EI) and no longer is. It ignores Recovery Access health, the Recovery Access flag after the fact (a designation is not this writer's act), other administrators, and any difference in configuration or trust. It needs no trusted configuration. A genuine reduction of the affected user's own contribution still raises `AuditInvariantException`, which rolls the unit back and is a 500. A reducer is unchanged. A state built without facts (a test double) is compared by the user's membership in each view; a state read from the database always carries facts.

**Stated limitation.** A concurrent writer that is not yet under the gate and reduces **the same affected user** between the two reads cannot be told apart from the raiser's own effect; it fails the raise (rolled back, safe direction). Gating those writers (WP3, WP4A, WP4B) removes the case, since they then serialize on the gate.

## 3. L-1: lock-order and gate-acquisition coverage on real SQL Server

The entry points, taken from the source inventory: `UserProvisioningService.ProvisionFromEntraDirectoryAsync` and `UserProvisioningService.ProvisionFromAccessRequestAsync` (initial role `role-platform-administrator`), and `UserManagementService.SetLocalCredentialDisabledAsync(…, disabled: false)`. Under both isolation modes each is shown to take the gate once per attempt with only the operation lock held at that moment, before any authority lock or write, and to read the state after the change on the unit's own transaction before the unit's closing audit row. The three raise-only entries of `UserManagementService` (role assignment, link addition, credential re-enable) are also checked for the same post-change read.

## 4. L-2: tracked risks

R-1 (WP3), R-2 (WP4B) and the IAM-10.4 UI refusal presentation now have definitions, owners and deadlines in [the WP1 review record](iam10-2b-wp1-review-record.md) §2.1. Neither R-1 nor R-2 is implemented here.

## 5. Findings not changed

F-1 (WP4B), D-6 (owner decision before WP4B), the secret-resolution uncertainty and the deployment-secret fallback (WP3). Refusal audit rows still do not say which invariant was violated; the reviewer found the existing vocabulary satisfies the approved contract, and an optional reason enhancement is left for WP5.

## 6. Validation of the remediated tree

Recorded in section 7, which names the source tree the binaries were built from.

## 7. Evidence for the remediated tree

**Source.** The remediation commit directly above the reviewed `1b7076f` (the binaries were built at `81cc0a8`, which differs from the final commit only by the text of this section). Tree hashes of the built source: `src` = `0b319d01f45afc1a3f56842cd5a1338f54448af7`, `tests` = `c3683724e723b0bcf78c598d9518d22937c50077`. A later documentation-only change cannot alter these, so a reader can check `git rev-parse <commit>:src <commit>:tests` against them. The working tree was clean (`git status --porcelain` empty) before and after the build.

**Build** (clean: a new output directory, `--no-incremental`), from the repository root, .NET SDK 10.0.402:

```
dotnet build tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --artifacts-path artifacts/wp2r-final --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers -v:q -nologo
dotnet build tests/Apperture.UnitTests/Apperture.UnitTests.csproj             --artifacts-path artifacts/wp2r-final --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers -v:q -nologo
```

Both: 0 errors. (An earlier attempt on a previous tree had one transient compiler crash, `csc.exe` exit code -1073741571, with the same tree compiling cleanly on a retry; and an earlier run on the previous tree showed one test-ordering flake, A13, now fixed as described below.) Tests were run with `dotnet test … --artifacts-path artifacts/wp2r-final --no-build`, only after that build of that tree, with `--logger trx`.

| Run | Result |
|---|---|
| Whole unit project | 5544 passed, 0 failed, 0 skipped |
| Focused integration set (below) | 652 passed, 0 failed, 0 skipped |

Focused integration set, by class: `AdministratorViabilityWp2SqlServerTests` 288 (the WP2 matrix, the interleavings, lock order, busy and cancellation, deadlock rerun, M6, and the remediation cases M-1, M-2 and L-1), `AdministratorViabilityHostHttpTests` 12, `RoleAuthorityHostHttpTests` 19, `RoleAuthoritySqlServerTests` 148 (IAM-10.2a), `AdministratorViabilitySqlServerTests` 21, `PlatformAdministratorSetGateSqlServerTests` 14, `RunningHandlerViabilitySqlServerTests` 6 (WP1), `IdentityGovernanceAuditSqlServerTests` 18, `EntraGovernanceAuditSqlServerTests` 12, `AuditRecorderSqlServerTests` 22, `PlatformAdminRoleRepairSqlServerTests` 28, `AccountServicesAuditSqlServerTests` 18, and the Entra-linking (13), session-identity (9), user-search (9) and username-rename (15) host suites.

**Isolation modes.** Read from the trx results: 267 cases whose name carries `rcsi: False` (locking READ COMMITTED), all passed; 267 carrying `rcsi: True` (READ_COMMITTED_SNAPSHOT), all passed. The remaining 118 cases take no isolation parameter (host suites and audit suites on a catalog of their own, in the default mode); they are not counted toward either mode. A SQL Server suite here fails, never skips, without SQL Server, and no case was skipped.

**A13 flake.** An earlier run on the previous tree failed `A13_a_writer_that_takes_only_row_locks_never_waits_for_the_gate (rcsi: True)` with a 30 second command timeout. Cause: the authority snapshot's key-range locks also cover the next key of the clustered `UserRoles` index, so a second writer whose random user id happened to be the neighbour of the first writer's was blocked by accident. It is a defect of the test's random ids, not of the gate or the production code. The test now uses fixed, separated id ranges for the two writers.

**Mutation checks** (a separate build directory, deleted afterwards; none of those binaries is in `artifacts/wp2r-final`). With M-1's short-circuit removed, 20 of the new SQL and HTTP cases fail (the ordinary-user cases under both untrusted configurations, the unproven-link case and the transactional-state case) and one unit case. With M-2's whole-system comparison restored, 8 SQL cases fail (the recovery-code, configuration-reload, environment-change, provisioning and genuine-violation cases) and one unit case. Both changes were restored with `git checkout`.

**Limitations.** The full multi-hour integration suite was not run. The HTTP case for invalid Entra options could not be built: a host with incomplete Entra options fails its own pipeline with a 500 before the viability code runs, so invalid configuration is covered at the SQL level (a reading whose Entra trust is untrusted) and the unreadable handler is covered at both levels. The same-user concurrent-ungated-writer limitation of M-2 is in section 2.
