# Epic 36 — Multi-Host Scheduler Coordination and Claiming

## Status

Implemented (including corrective pass: guarantee wording, claim lifecycle, audit scope)

## Context

Epic 28 implemented a real scheduler with a single-host assumption. If a host claimed a fire (INSERT into ScheduleRuns) and crashed before `CompleteRunAsync`, the row stayed `Outcome = "InProgress"` forever and the work was permanently stuck. Epic 36 adds multi-host-safe claim ownership and stale-claim recovery.

---

## Explicit guarantee level (corrective pass)

The implementation provides **durable, lease-based claim coordination** in SQL Server for the same `(ScheduleId, FireTimeUtc)` firing:

1. **Normal path**: At most **one host** successfully inserts or recovers the claim for that firing before dispatch; the unique index on `(ScheduleId, FireTimeUtc)` enforces a single row per fire.
2. **Dispatch**: Under that model, **at most one successful new workflow/operation start** is intended per fire in the steady state; idempotency keys on start requests further bound duplicate work if a rare race or recovery path re-enters dispatch.
3. **Not guaranteed**: **Universal exactly-once** across every crash clock skew, partial failure, and external system behavior is **not** claimed. After a crash between dispatch and `CompleteRunAsync`, recovery may re-invoke dispatch; the platform relies on start-request idempotency and honest completion of the `ScheduleRuns` row.

**Plain-language summary:** Multiple scheduler-capable hosts sharing one plane and database can coordinate so the same scheduled fire is not routinely double-dispatched; stale in-progress claims can be taken over after a configured lease window. This is **not** a distributed consensus or global exactly-once guarantee.

---

## Claim lifecycle semantics (corrective pass)

| Phase | Meaning |
|-------|---------|
| **Active claim** | A `ScheduleRuns` row exists for `(ScheduleId, FireTimeUtc)` with `Outcome = "InProgress"`. The row is the durable claim; `ClaimedByHostId` and `StartedAtUtc` identify the current claimant and lease clock. |
| **Claim no longer active** | After `CompleteRunAsync`, `Outcome` is no longer `InProgress` (e.g. `Dispatched`, `BlockedByLifecycle`, `Failed`). The claim is **released** in the sense that no host holds an active in-progress claim for that fire; historical `ClaimedByHostId` on the row reflects who last owned the claim before completion, but **operator/API “active claim” projections intentionally do not surface completed runs as a current claim**. |
| **Stale claim** | Active claim where `StartedAtUtc` is older than `now - ClaimLeaseMinutes` (server time). Another host may run `TryRecoverStaleClaimAsync` and become the new claimant. `IsClaimStale` on operator projections is **true** only for this active in-progress + past-lease case. |
| **Operator projections** | `ScheduleDetailDto` / `ScheduleDiagnosticEntry` claim fields (`ClaimedByHostId`, `ClaimedAtUtc`, `IsClaimStale`) apply **only when there is an in-progress run** for the assignment (`GetInProgressRunClaimAsync`). They do **not** show “who completed the last run” or historical claim owners for terminal runs. |

---

## Coordination audit scope (corrective pass)

**Audited today (Epic 36):**

| Action name | When |
|-------------|------|
| `scheduler.claim.recovered` | A host successfully runs `TryRecoverStaleClaimAsync` (takes over a stale in-progress claim). |

**Not audited (intentional / noise / existing Epic 28 coverage):**

| Event | Rationale |
|-------|-----------|
| **New claim** (`TryClaimFireAsync` success) | High volume every poll cycle; would dominate audit. Claim truth is in `ScheduleRuns`. |
| **Claim lost / duplicate prevented** (INSERT unique violation, recovery lost race) | Normal contention; no separate audit line. |
| **Dispatch under claim** | Already covered by Epic 28 actions (`scheduler.schedule.dispatched`, `blocked_by_lifecycle`, `failed`). |

**Boundary:** Coordination-specific audit is intentionally **narrow**—stale recovery is the operator-meaningful cross-host handoff worth a dedicated event.

---

## Decision (implementation summary)

### 1. Claim model

- **ClaimedByHostId** (nullable, max 128): On `ScheduleRuns`; set on INSERT and on stale recovery UPDATE.
- **Lease staleness**: `StartedAtUtc < now - ClaimLeaseMinutes` ⇒ recoverable.
- **Stale recovery**: Conditional `UPDATE` on ScheduleRuns; one winner.

### 2. Host identity

- **RuntimeId** (`RuntimeHostIdentityOptions`) or **SchedulerHostId** in `ScheduleDispatchOptions`.

### 3. Scheduler loop

1. `hostId`, `staleCutoffUtc`.
2. `GetDueAsync` + `GetRecoverableStaleCandidatesAsync`, merged without duplicate `(ScheduleId, FireTimeUtc)`.
3. `TryClaimFireAsync` then `TryRecoverStaleClaimAsync` if needed.
4. Lifecycle guard, dispatch, `CompleteRunAsync`, `UpdateStateAsync`.

### 4. Operator visibility

- **Schedule detail / runtime health**: Active in-progress claim only; see lifecycle section above.

### 5. Configuration

- **ScheduleDispatch:ClaimLeaseMinutes** (default 5).
- **ScheduleDispatch:SchedulerHostId** (optional).

### 6. Limitations

- Clock skew uses server time.
- Recovery races: atomic UPDATE; idempotency on start requests.
- Pre-migration rows may have null `ClaimedByHostId`.

## Consequences

- Multiple hosts can share a plane/database without routine double-dispatch of the same fire.
- Stale in-progress work can be recovered after the lease window.
- Operators see **current** claim/stale state only for in-progress runs.

## References

- Epic 28 — Real Scheduler Implementation
- Plan: Epic 36 Multi-Host Scheduler Coordination
