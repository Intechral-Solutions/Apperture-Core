# IAM-10.2b WP4B-B: the Management Host's configuration attestation

| | |
|---|---|
| Package | IAM-10.2b, work package 4B-B ([decision record](iam10-2b-wp4b-decision-record.md) §3, §4, §9, §10), on the accepted [WP4B-A](iam10-2b-wp4b-a-record.md) and [WP4B-D](iam10-2b-wp4b-d-record.md). |
| Branch | `feat/iam-10.2b-administrator-viability`, from `478ae7a` (WP4B-D, independently accepted). Not pushed, not merged. |
| Status | Implemented and validated (§12). Awaiting focused independent security review (provenance, freshness, boundary), as the decision record's §9 requires. |
| Delivers | The attestation table, the host writer, the reader, the asynchronous reading seam, the evidence classification and the `configurationEvidence` audit detail. **No reducer is integrated.** |
| Not in scope | WP4B-C (designate, strict revoke, bare reset, the replacement candidate and switch, provision/enrol/regenerate as gated raisers, command-line mapping of outcomes and exit codes, L-P2, L-P3), WP5, IAM-10.4, OPS-1.4 (L-D1), OPS-1 servicing. No package, CI or dependency change. One additive migration. |

## 1. The approved contract

Everything below is ratified in the [decision record](iam10-2b-wp4b-decision-record.md); this section restates it only where the implementation depends on it, and does not reopen it.

1. **What is attested** (§3.2, §3.3). That a Management Host process, using a database principal that can write the attestation table, recorded readings about itself, at a time the database clock can judge, for the database it was connected to, and that no other live host recorded a conflicting reading. It is evidence about the host, not proof that Microsoft sign-in works, not instantaneous liveness, not a defence against a database writer.
2. **Who produces authoritative evidence** (§3.1). The Management Host writes it. The emergency command line reads it and **nothing else**: its own `appsettings.json` and environment name no installation. There is no operator assertion, no `--local-sign-in` override and none is to be added (§2, §3.6).
3. **Consumers** (§3.4). The viability evaluation (the guard, over the effective sign-in configuration) in the emergency command line, read **inside the audited unit after the administrator-set gate is held**. The command-line reducers that will consume it (designate, strict revoke, bare reset, the replacement switch) are WP4B-C. The Management Host does not consume it: it reads its own options.
4. **Freshness** (§3.5). Refreshed about every 60 seconds; fresh while the **database clock** says it was refreshed within the window (proposed: three intervals); a graceful stop closes the window at once; a crash leaves it open for at most the window. The window bounds the staleness; it does not remove it.
5. **Missing, stale, stopped, wrong database, unknown version, unreadable, two live hosts** (§3.4): all **unavailable**. A fresh, consistent attestation with the input established is **attested**. An operation that does not depend on the missing input proceeds, and its class is `not_required`.
6. **What refuses, defers or stays available** (§3.4, §3.6). Revoke, bare reset and designate may be unavailable until trustworthy evidence is restored (an accepted RC1 limitation). Restoratives are never blocked and never fabricate evidence. This package does not wire the reducers, so it changes none of them; it makes the unavailable outcome real for them.
7. **Multiple hosts** (§3.7). RC1 supports one Management Host. A second live attestation is a conflict and the evidence is unavailable. This is a safety behaviour, not a statement that two hosts are supported.
8. **Startup** (§3.3, §7.2). The attestation written at start reports Local as trusted (a configuration fact) and Entra Now as not established. The writer must not build the Entra or OpenID Connect options.
9. **Audit** (§4). Additive detail `configurationEvidence` with exactly the values `attested`, `not_required`, `unavailable`; no new action; the only configuration detail in an event.

The earlier planning figure of a **180-second** lifetime and fail-closed behaviour agrees with the ratified text (three 60-second intervals; every failure is unavailable). It was verified against the record, not taken from the work package request.

## 2. Details the record left to this package (§10), and how each is settled

None changes a ruling. They are proposals for the focused review.

| # | Detail | Settled as |
|---|---|---|
| 1 | Refresh interval, window, stop versus crash | 60 s and 180 s, fixed in code (`HostConfigurationAttestationTiming`); no configuration knob, because a knob could only lengthen the window. A row is live when it is not stopped and its age (database time minus last refresh) is **not greater than** the window: age equal to the window is fresh, one tick more is stale. A graceful stop writes `StoppedUtc`; a crash writes nothing and the row ages out. A row dated after the database's clock is never believed (`ClockInconsistent`). |
| 2 | Table shape, key, host id, binding | `HostConfigurationAttestations`, one row per host, primary key `HostId` = the first 32 hex digits of SHA-256 over machine name, content root and application name (stable across restarts of one host, not configurable). The binding is `SHA2_256(ServerName \| DB_NAME())` **computed by the server** in every statement that writes or reads, so a writer and a reader with different clocks and differently spelled connection strings agree. It guards operator error; it is not a cryptographic identity of a database. |
| 3 | Restart overlap; restored or copied database | The same host id means the new process **claims** (replaces) the row. The older process, still running, is **superseded**: its refresh and its stop are conditional on `ProcessInstanceId`, so it can neither overwrite nor stop the newer process, and it stops publishing. A different host id is a conflict until the old row is stopped or ages out. A row copied into another database (or restored on another server) fails the binding. A row restored in place keeps its old database time and ages out unless restored within the window of being written (§10, R-B3). |
| 4 | "Built completely" and "handler binding read" without building | The handler configurer notes that the handler's options were built (`IEntraOptionsBuildHealth.OpenIdConnectOptionsBuilt`), which only real traffic through the authentication middleware causes. Until then the payload says **not established**: the writer never asks for the options. After it, the writer takes the host's own reading, so the attested Now view is the one the host evaluates (including its build quality, `Complete` or `Degraded`). A reload of the Entra options changes neither the framework's caches nor the reading, as in the earlier work. |
| 5 | Does the command line recompute Restart? | Yes: from the deployment's **non-secret** Entra options as attested (an explicit allow-list; the secret is only "present") over the **live** registration the command line reads from the database itself. The command line cannot read the host's secret store, so it reports `AuthoritativeAboutSecrets = false`: a registration whose usability depends on a secret the command line cannot see leaves Entra **unknown**, never usable. |
| 6 | The posture-only proofs behind `not_required` | **Handed to WP4B-C**, where the reducers that use them exist. This package supplies the class and the audit detail, and pins, with the existing guard, what an unusable attestation allows and refuses (unchanged inputs; a result proven safe without Entra; everything else refused). A predicate for "bare reset of an unhealthy posture" or "a switch that leaves the account healthy" cannot be written or tested without the operation. |
| 7 | The asynchronous seam and when it is read | `IDeploymentAuthenticationConfiguration.ReadAsync` (a default member that answers with `Read()`), asked by the effective sign-in configuration provider, which the viability reads call after the gate is held and inside the unit. The attestation store joins the unit's open transaction. |
| 8 | Who writes, who reads | The host's database connection writes; the command line reads through the connection profile it already uses. This repository has one application database principal and no principal separation, so no new grant or role is introduced; narrowing the command line to `SELECT` is a deployment hardening for OPS-1. |
| 9 | Version change in an upgrade | `SchemaVersion` is stored with every row and checked by the reader: a version or source it does not know is unavailable, and the payload parser rejects any member or name it does not know (a newer host is not half-read). The migration is additive and starts empty. |

## 3. What the host attests

`AttestedAuthenticationPayload` (schema version 1), a JSON document in the row:

- `localNow`: Local sign-in as the running host's options say (Local options that fail their own validation are reported **off**: a smaller set of viable administrators can only make a decision refuse more).
- `localRestart`: Local sign-in as the host's configuration says.
- `entraBase`: the deployment's own Entra options before any registration is overlaid, non-secret fields only, plus `hasClientSecret`.
- `entraNow`: **absent while not established.** Once established: the running endpoints' options (same allow-list), their build quality, and the handler binding (state, authority, client id, present-or-absent secret, build quality).

There is no field that can carry a secret value. A reader rebuilds the options with a fixed marker where the host holds a secret; the marker is not a secret and goes nowhere. A flag from configuration (`Entra:Enabled`) is only ever recorded as a configured value inside `entraBase`.

## 4. Producers and consumers

| Part | Role |
|---|---|
| `HostConfigurationAttestationPublisher` (hosted service, registered last in the Management Host's `Program`) | **Producer.** Claims at start, refreshes each 60 s, marks stopped on a graceful stop. A failure to write is logged and retried; the host starts and runs whatever the state of the evidence. |
| `HostAttestationPayloadSource` | Makes the payload without building options. |
| `SqlHostConfigurationAttestationStore` | The only writer and reader of the table. Every time and the binding are stamped on the server. |
| `HostConfigurationAttestationClassifier` | **Pure judge**: rows, the database's time and the window in; a verdict out. Reads no clock. |
| `AttestedDeploymentAuthenticationConfiguration` | **Consumer.** The emergency command line's `IDeploymentAuthenticationConfiguration`; registered in `AddAppertureEmergencyOperatorServices` in place of the reader of the command line's own configuration (which remains in the code for tests and is no longer composed in production). |
| `EffectiveSignInConfigurationProvider` | Unchanged logic, now asking `ReadAsync`; carries the evidence class in `ConfigurationTrust.Evidence`. |
| Reducers (designate, strict revoke, bare reset, switch) | **Not integrated** (WP4B-C). |

Three distinct things are kept apart: *configuration validation* (the host's own options validators, untouched), *attestation freshness* (the classifier, by the database clock) and *viability evaluation* (the guard and the evaluator, untouched). The attestation never turns a failed validation into a usable sign-in method: a host whose options fail validation attests the running Entra options as not established, which is unknown.

## 5. Freshness and failure behaviour

| Condition | Verdict | Reading given to the evaluation |
|---|---|---|
| One live row, this database, known version, readable | `Fresh` (attested) | The host's reading, rebuilt; Local trusted; Entra trusted only if the host's own reading was (not established, a degraded build or an unusable handler make it unknown) |
| No row | `Missing` | Local unknown, Entra unknown, class `unavailable` |
| Newest row older than the window | `Stale` | as above |
| Newest row stopped | `Stopped` | as above |
| Live row for another database | `DatabaseMismatch` | as above |
| Live row of an unknown version or source | `UnknownSchema` | as above |
| Live row that cannot be read (bad JSON, unknown member or name, missing part, over 64 KB) | `Unreadable` | as above |
| Two or more live rows (agreeing or not) | `Conflict` | as above |
| A row dated after the database's clock | `ClockInconsistent` | as above |

Stale and stopped rows of **other** hosts are ignored when one live row exists, so a host that moved leaves nothing behind that is believed. The reason shown to the operator is product wording that names no host, database, tenant or value.

## 6. Interaction with I-VIABLE and I-FALLBACK

- **No bypass through staleness or absence.** Unusable evidence yields `Unknown` for Local and for Entra. The existing guard then refuses any change to a stored fact that it cannot prove safe (`ConfigurationUnavailable`), refuses a revocation (`EvaluateRevocation` needs Local known), and does not turn an unknown reading into a proof. The cases are tested directly (§9).
- **No indiscriminate lock-out.** A change that leaves every stored fact unchanged is still allowed with no attestation, and a result that is proven safe without Entra (an Entra-independent ordinary administrator in both views) is still allowed with Local established and Entra unknown. Remediation from an already-unsafe state through such a result is allowed; one that needs the missing input is refused, not guessed.
- **A fresh attestation is not a verdict.** It describes the configuration the host reported. Whether the configuration works is the evaluation's reading of it, exactly as for a running host.
- **Recovery Access never counts as an ordinary administrator**; nothing here changes the evaluator.
- **The startup attestation** reports Local trusted and Entra not established, so a restart of the host re-enables Local-only reducers within one write and Entra-dependent ones only after real traffic builds the options.

## 7. Concurrency and transactions

- **No new lock, no second gate.** The publisher takes no administrator lock and waits for none. Tested: with the gate held by another session, the publisher claims, refreshes and stops, and the server's lock list shows nobody waiting on an application lock; and a unit that holds the gate and has read the attestation does not block a refresh.
- **Lock order unchanged.** Operation lock, then the gate, then authority rows, then other rows. The attestation read is an ordinary read made after the gate, in the unit's own transaction. It takes at most a shared row lock under locking READ COMMITTED (released at once) and a version read under READ_COMMITTED_SNAPSHOT.
- **Ownership by process.** Claim replaces; refresh and stop are conditional on `ProcessInstanceId`; concurrent claims of one host id leave exactly one row owned by exactly one process (tested with eight).
- **A decision is made on a value that can already be stale.** The read is not repeated at commit. The window and the host's own unchanged-since-refresh behaviour bound the risk (R-B2). This is the position the record already accepts for the Now view.

## 8. Persistence and audit

- **Migration** `20261029120000_Iam102b_HostConfigurationAttestation`: one new table, no existing table touched, `Down` drops it. The model snapshot is updated and the existing model-consistency test holds.
- **The table is operational evidence, not audited state**: it records no user action, is written only by the host's publisher, and is not an authority over configuration. The audit closure guard, which governs state mutated through the audited unit, is unaffected.
- **`configurationEvidence`** is an optional enumerated field on `emergency.access.designated`, `emergency.access.revoked` and `auth.mfa.reset`, with the producers taking an optional `ConfigurationEvidence`. Nothing sets it yet: the reducers are WP4B-C, which also decides the value for each outcome (including the refusal row for `administrator_viability_unavailable`, which is the existing W3 refusal path and is not changed here). The event carries the class and no host id, fingerprint, tenant, option value or secret.

## 9. Tests and negative controls

Unit (`HostConfigurationAttestationTests`, `HostConfigurationAttestationPublisherTests`): the numbers; the freshness edge to the tick (equal to the window fresh, one tick inside, one tick outside, any older stale, a different window moves the edge); every unavailable kind including a row in the future of the database's clock; a stopped row not resurrected by being fresh; a crash leaving the window open for at most the window; binding mismatch; unknown version and source; unreadable documents (empty, not JSON, wrong shape, unknown member, unknown or numeric state, oversize, missing part); one host versus two (agreeing or not, a stale or stopped row of another host ignored, a different host id in conflict until the old row ages out); reasons that name nothing and carry no planning label; no field that can carry a secret and a marker where the host holds one; the round trip into the reading the host made; the reader's fail-closed shape and its single constructor dependency (no fallback to the command line's configuration); the evaluation over an attestation (Local trusted, a startup attestation unknown for Entra, a degraded build unknown, a configured flag not a usable handler, unusable evidence unknown for both); the guard with unusable evidence (changed inputs refused, unchanged allowed, revocation refused, no proof from a Local flag, remediation allowed only when proven safe without Entra); the canonical audit values and the three events validating against the catalog; the publisher with a manual clock (claim at start, refresh each interval and not one tick before, retry after a failed write, claim again after the row went, superseded stops publishing and never marks the newer row, stop marks this process only, nothing written after stop, no build of options and no call to the host's reading before real traffic, the host's own reading after it, Local options that fail validation attested off, host id stable across restarts and differing between hosts); source pins (the attestation code names none of the option types it must not build; the host's reading is asked only after the build-record check; the attestation is registered last and the command line composes the attested reader and not the configured one; the evaluation asks asynchronously; the store stamps on the server and keys its writes on the owning process).

Real SQL Server, both isolation modes (`HostConfigurationAttestationSqlServerTests`, `ManagementHostAttestationSqlServerTests`): times stamped by the database (a 1999 host clock changes nothing); refresh moves only the refresh time and the reading; a newer process replaces the row and the older one can neither refresh nor stop it; a graceful stop closes the window and a restart clears it; a host with no row is told so; eight concurrent claims leave one row; the freshness edge on values the server returned (170, 181 and future-dated); a row copied into another database fails the binding; the store joins an open transaction (rolled back, nothing remains); the real provider over a stored attestation, and over each way the evidence can fail through the real store, ending with one live host attested again; a change on the host visible only after its next refresh; the publisher against the real store with the gate held by another session; a unit holding the gate reading the attestation while the host refreshes. **The real Management Host, composed by its own `Program`:** it attests at start before any request with the options unbuilt and Local established; real traffic builds the handler's options and the next refresh attests the host's own reading; a graceful stop marks the row stopped; the attestation is the last hosted service before the web host and a restart replaces the row.

**What the tests do not exercise.** No test runs the emergency command line process; the command line's composition is pinned by the registration test and the reader is exercised through the real provider and the real store. The refresh loop's timer is tested with a manual clock, and the host-level tests call the publisher directly rather than waiting a minute.

**Negative controls** (production source mutated, restored byte for byte, hash verified): see §12.

## 10. Residual risks (for the focused review)

| Id | Risk | Position |
|---|---|---|
| R-B1 | After real traffic has built the handler's options, the host's own reading may be the first reader of `IOptions<EntraAuthOptions>`, and then builds it, from the same sources by the same code, after the migrations. | The ratified rule is that the writer does not build the options **before** real traffic; the build record is the gate. Reading the monitor's cache instead would drop the endpoints-versus-handler comparison the Now view is built on. Not closed; stated. |
| R-B2 | Stale observation: a host can stop, restart with other configuration or reload after its last refresh; a command run inside the window relies on a reading that is no longer true. | Accepted in the record (§3.5). Bounded by 180 s; a graceful stop closes it at once. |
| R-B3 | A backup restored in place within the window of being taken can present a row that looks fresh. | Needs a restore of the installation's own database inside three minutes of the backup; the restore is an offline maintenance act (§3.6). |
| R-B4 | A writer of the table can forge a row. | Out of scope by the record: it protects against operator error, stale configuration and a command line pointed at the wrong installation, not against a database administrator. |
| R-B5 | The command line cannot read the host's secret store, so an Entra registration that needs a stored secret leaves Entra unknown, and Entra-dependent reducers are unavailable until the host (not the command line) can establish it. | Intended and conservative (§10 item 5 of the record: where it cannot be established, Entra stays unknown). |
| R-B6 | Two processes started from the same folder share a host id; the second replaces the first's row, and the first stops publishing. | One Management Host is the supported topology. Two hosts in different folders or on different machines are detected as a conflict. |
| R-B7 | The publisher awaits one database write at start (bounded by the command timeout) before the host starts listening. | The migrations have just succeeded, so the database is reachable; a failure is logged and retried, never fatal. |
| R-B8 | A database clock stepped forward makes every row stale (fail closed); stepped back makes a row future-dated (`ClockInconsistent`, fail closed). | Fails closed; the next refresh repairs it. |
| R-B9 | `ConfiguredDeploymentAuthenticationConfiguration` remains in the code, unused in production composition. | Kept for the tests that exercise the evaluation with it; removing it is a clean-up for WP4B-C once the command line is mapped. |

## 11. Remaining IAM-10.2b work (not begun)

- **WP4B-C**: designate and strict revoke, bare reset, the replacement candidate and switch, provision/enrol/regenerate as gated raisers, command-line mapping of busy, unavailable, conflict and unknown outcomes (and the operator's view of the `Reasons` this package produces), exit codes and a test seam, the choice of `configurationEvidence` per outcome, the posture-only proofs behind `not_required` (§2 item 6), L-P2, L-P3, writer guard to integrated. Full WP4B review.
- **WP5**.
- **Elsewhere, unchanged**: L-D1 (OPS-1.4, recorded in the [program plan](../../../audits/rc1-architecture-audit-and-program-plan.md)), the intermittent `ConnectionToolInvocationTests` failure, OPS-1 servicing, Enterprise transition planning and FluentAssertions licensing.

## 12. Validation

| Run | Result |
|---|---|
| Clean `--no-incremental` build of `Apperture.slnx` | 0 errors, **71 warnings (the baseline)**, 6 min. A first clean build had 73: two nullability warnings in the new test file, fixed |
| UnitTests | **5,895 passed** (5,800 before, +95 new), 0 failed, 0 skipped |
| ArchTests | 38 passed |
| Ui.Shared.Tests / Ui.Client.Tests / AppHost.Diagnostics.Tests | 1,497 / 529 / 7 passed |
| New SQL classes (28 service-level cases + 8 production-wired host cases, both isolation modes) | all passed |
| Focused integration set, full (the 40-class baseline plus the WP4B-D classes plus the two new classes), run once on the pre-mutation build | 1,397 run: **1,395 passed, 2 failed** (`ManagementHostStartupOrderSqlServerTests`, both modes). The two failures were real: the publisher was registered by factory, so the test's start recorder could not see it. Fixed by registering it by type, as every other startup step is |
| After the fix, on the final clean build: the new classes, the start-order, bootstrap HTTP, first-administrator, bootstrap-writers, viability, registration, handler, options-initialisation, designation and recorder classes | **472 passed, 0 failed, 0 skipped** in 19 min |
| SQL execution proof | Target `apperture-test-sql` (`127.0.0.1,1433`, the `.env` value; no environment override), throwaway catalogs created per case under the requested isolation mode. The development SQL (`:14333`) was not contacted and no migration was run against it |

**Not re-run after the registration fix:** the full 1,397-test focused set (42 min). The fix touched only the registration of the publisher; the affected classes (start order, the new host class, bootstrap HTTP) and all unit tests were re-run on the final clean build. The full IntegrationTests project (about two hours) and the browser suites were not run.

**Negative controls**, 16 mutations of production source, each restored byte for byte (hash verified), each caught:

| # | Mutation | Caught by |
|---|---|---|
| B1 | Freshness edge made exclusive (`age < window`) | 4 unit |
| B2 | Stopped rows ignored | 6 unit |
| B3 | Database binding check removed | 4 unit, 2 SQL (the copied-row case, both modes) |
| B4 | Two live hosts allowed | 7 unit |
| B5 | A row dated after the database clock believed | 3 unit |
| B6 | Window lengthened to 600 s | 3 unit |
| B7 | Reader fabricates a trusted reading when the evidence is unavailable | 13 unit, 2 SQL |
| B8 | Refresh not owned by the process | 4 SQL (ownership, concurrent claims) |
| B9 | Stop not owned by the process | 2 SQL |
| B10 | The claim stamped from the host's clock | 12 SQL (unit tests cannot see it) |
| B11 | The payload source builds before real traffic | 11 unit, 4 SQL (the host cases) |
| B12 | The command line composes its own configuration | 2 unit |
| B13 | The provider reads synchronously | 5 unit, 6 SQL |
| B14 | The handler configurer stops noting its build | 2 SQL (the production-wired host case) |
| B15 | The attestation registered before governance | 1 unit (source pin) |
| B16 | A superseded process keeps publishing | 1 unit |

What the controls show, stated rather than hidden: B12 and B15 are caught by registration and source pins, not by behaviour of a running command line or a differently ordered host (the existing start-order test pins the framework's order but does not name the attestation's position, which the source pin does). B10 is caught only by SQL, as it must be.

Scratch outputs (build logs, mutation logs, results) are in the git-ignored `artifacts/wp4b-b-logs/`.
