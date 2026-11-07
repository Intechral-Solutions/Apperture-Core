# IAM-10.2b WP4B-C Foundation 1A: attestation hardening (L-B1, L-B2)

| | |
|---|---|
| Package | IAM-10.2b, work package 4B-C, Foundation 1A. Resolves the two entry obligations the [integration record](iam10-2b-dev1-integration-record.md) §5 carried for WP4B-C: **L-B1** and **L-B2**. |
| Branch | `feat/iam10-2b-wp4bc-attestation` (worktree `wt-iam10-2b-attestation`), from `c3e9813`. Local only: not pushed, no PR, not merged into the IAM branch. |
| Code commit | `0f550b5d5df1df78c2f693cdff4e0747fe29e9a6` (tree `ddfcdc87f0c16db34faa12255c36d4a965b2ddf7`). This record is a documentation-only commit on top; `src` and `tests` are identical to the code commit. |
| Status | Implemented and validated by the implementer (§4, §5). **Not independently reviewed.** |
| Not in scope | The coordinator, the guard, attempt-scoped readings, posture-only predicates, `EmergencyAccessService`, `NativeMfaService`, the emergency CLI (Foundation 1B + 1C is `b5c7caf`, untouched and not merged), the WP4B-C reducers, WP5, L-B3 (documentation corrections), moving the claim to application start, development-environment hardening. |

## 1. Scope and acceptance criteria

1. A Management Host process that was superseded by another process of the same host can take its row back, **only** when the database shows that the holder has stopped or has been silent for longer than the freshness window, or that the row is gone. A live holder is never displaced.
2. Of any number of simultaneous candidates for a free row, at most one obtains it.
3. A refresh leaves the stored database binding equal to the binding the write arrived in, and cannot be used to take a row or to vouch for a row the caller does not own.
4. The existing fail-closed behaviour (wrong database, conflict, stale, stopped, unknown or unreadable evidence) is unchanged, and `DatabaseNowUtc` and `RefreshedUtc` keep their meanings.

## 2. Design

### 2.1 What the existing contract already supported

`ClaimAsync` is an unconditional upsert by host id (a process that starts replaces the process it succeeds, WP4B-B §2 row 3). `RefreshAsync` and `MarkStoppedAsync` are conditional on owning the row. Nothing let a process that had been replaced ask for the row back safely: calling `ClaimAsync` again would have displaced a live holder, and `RefreshAsync` correctly refuses a non-owner. So one new store member was necessary (below); the publisher's existing timer is reused and no polling, table, column or migration was added.

### 2.2 L-B1: `ReclaimAsync`

`IHostConfigurationAttestationStore.ReclaimAsync(write)` returns `Reclaimed` or `Held`. In SQL, one statement decides and writes, against the database's clock (`SYSUTCDATETIME()`) and one shared window:

| Existing row for the host | Result |
|---|---|
| none | inserted for the caller (`WHERE NOT EXISTS (... WITH (UPDLOCK, HOLDLOCK))`, the pattern `ClaimAsync` uses); a lost insert race answers `Held` |
| the caller's own, not stopped | `Reclaimed`, idempotent: claim time and start time are kept, so a retry after a lost acknowledgement changes nothing but the refresh time |
| the caller's own, stopped | `Held`: a process that stopped publishes nothing more (as a refresh already says) |
| another process's, stopped | `Reclaimed`; the stop mark is cleared, the row carries the caller's reading |
| another process's, `RefreshedUtc` strictly older than the window | `Reclaimed` |
| another process's, live (not stopped, age not greater than the window) | `Held`, nothing written, not even the refresh time |
| another process's, dated after the database clock | `Held` (its age cannot be judged; the reader treats it as unavailable too) |

"Older than the window" is the line the reader draws: the classifier accepts `age <= window`, the store reclaims at `age > window`. The window is now one definition, `HostConfigurationAttestationLiveness.FreshnessWindow` (Core.Application, 180 s, whole seconds); `HostConfigurationAttestationTiming.FreshnessWindow` is assigned from it. The caller passes no time and no window: the host cannot make another host look silent.

**Publisher.** `_superseded` is no longer terminal. A superseded publisher keeps its existing 60-second tick; each tick is one `ReclaimAsync`. When a refresh first reports `Superseded` the publisher asks in the same tick, because the holder may already have stopped. Only the first write of a process is an unconditional `ClaimAsync`; it is marked attempted before the write, so a start whose write failed (or committed without an acknowledgement) never claims unconditionally again and cannot take the row back from a newer process. A row found gone is also taken through `ReclaimAsync`. A superseded process still never writes to the row and never marks the holder's row stopped (`StopAsync` is unchanged). If `StopAsync` cancels a reclaim whose statement had already committed, the process believes it is superseded, writes no stop mark, and the row it owns ages out in the window: fail safe.

### 2.3 L-B2: the refresh re-stamps the binding

`RefreshAsync` now sets `DatabaseFingerprint` to the server-computed fingerprint in the same `UPDATE` that stamps `RefreshedUtc`. The `WHERE` is unchanged (host id, process id, not stopped). Decision on a mismatch, as the assignment asked:

* **The owner's refresh re-stamps (authorised refresh).** Ownership is proved by the process id, which only that process holds; the fingerprint is computed by the server from the connection the write arrived on, never named by the host. A row an owner keeps alive therefore always names the database the owner is writing to, which is what a rename, a failover or a repointed connection needs. A mismatch is not left to wait for a restart.
* **A non-owner changes nothing.** A superseded or stopped process's refresh matches no row and so cannot rebind the row or vouch for it.
* **A live row for another database is neither believed nor displaced.** The reader reports `DatabaseMismatch`; `ReclaimAsync` answers `Held` while that row is live. Once it is stopped or silent, a reclaim replaces it and binds it to this database.
* Database identity validation is not weakened: the reader's comparison is unchanged, and a copy of a row into another database is still not believed there.

## 3. Interface change and compatibility with Foundation 1B + 1C (`b5c7caf`)

Added, all in `Apperture.Core.Application.AdministratorViability` (file `IHostConfigurationAttestationStore.cs`):

* `ValueTask<HostAttestationReclaimOutcome> ReclaimAsync(HostConfigurationAttestationWrite write, CancellationToken cancellationToken = default)` on `IHostConfigurationAttestationStore`;
* `enum HostAttestationReclaimOutcome { Reclaimed, Held }`;
* `static class HostConfigurationAttestationLiveness` with `FreshnessWindow`.

No existing member, record or enum changed. `HostConfigurationAttestationRow`, `HostConfigurationAttestationSnapshot` and `HostAttestationRefreshOutcome` are untouched, and the classifier is not touched here, so the coordinator branch's `EvidenceRemaining = window - age` still sees the same `DatabaseNowUtc` and `RefreshedUtc`. A reclaim stamps `RefreshedUtc` with the database time of the reclaim, so a reclaimed row has the whole window remaining at that moment, like a claim.

**Required updates on the coordinator branch when it takes this change.** Two test fakes implement the interface and stop compiling until they have the new member (read from `b5c7caf`, not compiled together here):

| Fake | File | Add |
|---|---|---|
| `ScriptedStore` | `tests/Apperture.UnitTests/AdministratorViability/AdministratorViabilityAttemptReadingTests.cs` (line 38) | `public ValueTask<HostAttestationReclaimOutcome> ReclaimAsync(HostConfigurationAttestationWrite write, CancellationToken cancellationToken = default) => throw new NotSupportedException();` |
| `CountingStore` | `tests/Apperture.IntegrationTests/Audit/AdministratorViabilityAttemptSqlServerTests.cs` (line 112) | `public ValueTask<HostAttestationReclaimOutcome> ReclaimAsync(HostConfigurationAttestationWrite write, CancellationToken cancellationToken = default) => _inner.ReclaimAsync(write, cancellationToken);` |

No coordinator `src` change is needed. A read-only `git merge-tree --write-tree` of this branch with `feat/iam10-2b-wp4bc-coordinator` reports no conflict (the only overlap in the shared files is the coordinator's four-line classifier edit, which this branch does not touch).

## 4. Validation

Identity: HEAD `0f550b5`, tree `ddfcdc8`, tracked tree clean. Build: `dotnet build Apperture.slnx -c Release --no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers --artifacts-path artifacts\att-build1`, 0 errors, 7 min 3 s, started after the last source edit and from the tree that was then committed unchanged. Configuration C not used; the DEV-1 toolkit not used; no `AUTHORITATIVE` label is claimed or relied on.

SQL: the disposable `apperture-test-sql` container at `127.0.0.1,1433`, connection string in the process environment only (the tests were told to ignore `.env`), target checked before each run; the development database on 14333 was never involved.

| Run | Result |
|---|---|
| UnitTests, whole project | 5,989 passed, 0 failed, 1 not executed (the opt-in password-timing diagnostic, `PasswordEvaluationDesktopTimingDiagnostics`, unchanged and unrelated) |
| ArchTests / Ui.Shared.Tests / Ui.Client.Tests / AppHost.Diagnostics.Tests | 38 / 1,497 / 532 / 7 passed, 0 failed |
| Focused real-SQL set: `HostConfigurationAttestationSqlServerTests`, `ManagementHostAttestationSqlServerTests`, `ManagementHostStartupOrderSqlServerTests`, every `AdministratorViability` class | **411 of 411 passed**, 0 skipped, 0 not executed, 15 min 22 s |
| `HostConfigurationAttestationSqlServerTests` alone | 56 tests: 28 with `rcsi: False`, 28 with `rcsi: True`, all passed. 14 of the tests (28 cases, 14 per mode) are new in this package, and the existing refresh test is extended |
| `scripts/dev/Test-SqlExecutionHonesty.ps1 -Configuration Release -NoBuild` | **PASS**, exit 0: 3,507 discovered and 3,507 executed, 226 passed (all on the reviewed allow-list), 3,281 failed with SQL unreachable, none skipped. Provenance `reused-existing-build`, tracked changes from HEAD: none. The allow-list, the must-fail list, the script and its pin are unchanged |
| Publisher unit tests (`HostConfigurationAttestationPublisherTests`, `HostConfigurationAttestationTests`) | 100 of 100 passed |

**SQL execution proof.** Every new SQL test creates its own catalog with `AuditIsolation.CreateAsync(..., rcsi)` and reads the state back with SQL the test sends itself on its own connection (`RawAsync`: process id, stopped, fingerprint of the one row), so the store under test is not its own witness; the honesty run shows each test fails when SQL is unreachable.

**One failed attempt, reported.** The first focused run (before the commit) finished 62 of 64 with two failures: `Concurrent_claims_of_one_host_leave_exactly_one_row...` (an existing test) and one of the new tests. Both were SQL client "pre-login handshake" timeouts of 15 s while the host had about 0.7 GB free memory and the run took 14 min; no assertion failed. The same two tests passed on a re-run, and the clean full run above, on the committed tree, passed in full. The stall is the known memory-pressure behaviour of the test container; this is noted, not excused.

### New and changed tests

* **Unit (publisher, fake store).** A superseded publisher writes nothing while the store refuses and keeps asking each tick; it asks in the same tick it learns it was superseded; it owns the row again, refreshes and stops it only after the store agrees; a failed reclaim is retried; a start whose first write failed asks conditionally and, if refused, becomes superseded without ever claiming unconditionally; a gone row is taken through the conditional path; the reader and store share one whole-second window; source pins for the refresh re-stamp and the reclaim predicate.
* **Real SQL, both modes.** Live holder not displaced and its row unchanged; stopped holder replaced and the old process refreshes again while the replaced one is superseded; silent holder replaced at 181 s, held at 170 s and when dated ahead of the clock; idempotent owner reclaim and a stopped process refused; absent row created, another host's row untouched; **8 simultaneous candidates for a silent or stopped holder's row, 6 rounds per mode, exactly one wins** and the rest are refused and find themselves superseded; 8 simultaneous candidates for an absent row, exactly one row and one winner; a refresh by the owner replaces an outdated binding (read back with the test's own SQL) and the record is believed again; a non-owner or stopped owner cannot rebind or take; a live row for another database is held, a silent one replaced and rebound; conflicting hosts stay a conflict; the real publisher takes the row back after a graceful stop of the holder, after a crashed holder has been silent past the window and not before, and a publisher that missed its refreshes is not evidence until it writes again and a stopped publisher never is. The existing refresh test also asserts the owner, claim time, start time and unchanged binding of a valid same-host refresh.

## 5. Mutation and regression-catch evidence

Disposable clone of `0f550b5` under `artifacts\att-mut` (never the worktree under test). Baseline of the unmutated clone green first (unit 100/100, SQL class 56/56). Each mutant: one exact-string edit that had to match exactly once, rebuild of both test projects, run of the tests that should notice, restore with `git checkout`, restore proven by blob hash and a clean status. 13 of 13 caught; every restore was byte-identical; the restored clone was rebuilt successfully. M01, M07 and M13 re-create the original defects; the new tests cannot be compiled against `c3e9813` (the new member does not exist there), so these mutants are the old behaviour.

| Mutant | Reproduces | Caught by |
|---|---|---|
| M01 the publisher never accepts a reclaim | **original L-B1** | unit 4; SQL 4 (2 tests × 2 modes) |
| M13 the loop ends when superseded | **original L-B1** (loop half) | unit 3 |
| M07 refresh no longer re-stamps the binding | **original L-B2** | unit 1 (source pin); SQL 2 |
| M02 a live holder can be displaced | live-holder rule | unit 1 (pin); SQL 12 |
| M03 a stopped holder is ignored | stopped rule | unit 1 (pin); SQL 6 |
| M04 a silent holder is ignored | stale rule | unit 1 (pin); SQL 8 |
| M05 the window collapses to zero | window | SQL 4 |
| M06 a future-dated record counts as silent | clock-ahead rule | SQL 2 |
| M08 refresh without the ownership condition | refresh cannot take a row | SQL 4 (**SQL-only**: unit passed) |
| M09 a stopped process may take its row back | stopped owner refused | SQL 2 (**SQL-only**) |
| M10 a reclaim does not re-stamp the binding | binding after reclaim | SQL 2 (**SQL-only**) |
| M11 check-then-write instead of one statement (with a 150 ms gap) | **simultaneous candidates** | SQL 2 (both modes; **SQL-only**) |
| M12 a retry after a failed start claims unconditionally | no unconditional retry | unit 2 |

M05, M06 and M11 were run against the SQL tests only. Counts are failed tests; a SQL test appears once per isolation mode.

## 6. Evidence and checksums

Working evidence is under `artifacts\att-logs` in the worktree (ignored): build log, the evidence script and its TRX files (`ev\trx`), the first focused run and its re-run, the mutation script with per-mutant logs and `mut\summary.md`, and the honesty report. A copy with `SHA256SUMS.txt` is archived beside the other work-package evidence; its location and manifest are in the hand-back that accompanies this record. Do not `git worktree remove` this worktree before that copy is confirmed: removal deletes ignored files.

## 7. Independent review

Not requested by this package. Suggested focus for the reviewer: the single-statement atomicity of `ReclaimAsync` under both isolation modes (M11 and the 8-way race tests are the controls), the three residuals in §8, and the two fake updates in §3.

## 8. Remaining risks and open points

1. **The start-up claim is still unconditional.** A new process claims before its web host binds; if it then fails to bind and dies without a graceful stop, a live older process is superseded until the window passes (at most about 180 s, then it takes the row back). This is the accepted restart-overlap rule (WP4B-B §2 row 3). The reviewer's suggestion to claim at application start rather than at host start is not implemented here: it changes start order and is larger than the smallest correction.
2. **A rebinding refresh is silent.** The store cannot tell a rename or failover from a database that was restored elsewhere and pointed at by the same live process, and does not need to: the owner is the only writer and the binding is whatever database its write reached. No log or outcome marks the rebinding. If the owner wants to see rebinding, a new `HostAttestationRefreshOutcome` member is a contract change for the guard and the fakes; not made here.
3. **A record dated after the database clock blocks a reclaim.** It is held until the clock passes it and the window runs out. Combined with a backward step of the database clock (R-B8) a superseded process can wait for the size of the step plus the window. Fail closed.
4. **A reclaim cancelled after commit** leaves the row owned by a process that thinks it is superseded and will not stop it; the record ages out within the window.
5. **L-B3 is not done** (documentation overstatements at `HostConfigurationAttestationPublisher.cs` line 17, the WP4B-B component table and R-B8). The publisher's remarks were extended for ownership only.
6. The IAM-10.2b integration record still lists L-B1 and L-B2 as open entry obligations; it was not edited here.

## 9. Pull request and closure

None. The package stops at the committed checkpoint: not pushed, no PR, not merged into the IAM branch. The protected IAM branch (`c3e9813`), the stash, the archive tag, the other worktrees and the recovery bundles were not touched.
