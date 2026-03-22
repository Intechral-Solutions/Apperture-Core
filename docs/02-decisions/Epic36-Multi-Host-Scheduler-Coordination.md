# Epic 36 — Multi-Host Scheduler Coordination and Claiming

## Status

Implemented

## Context

Epic 28 implemented a real scheduler with a single-host assumption. If a host claimed a fire (INSERT into ScheduleRuns) and crashed before `CompleteRunAsync`, the row stayed `Outcome = "InProgress"` forever and the work was permanently stuck. Epic 36 adds multi-host-safe claim ownership and stale-claim recovery.

## Decision

### 1. Claim model

- **ClaimedByHostId** (nullable, max 128): Added to `ScheduleRunEntity` to record which host owns the claim.
- **Lease staleness**: `StartedAtUtc < now - ClaimLeaseMinutes` ⇒ claim is recoverable by another host.
- **Stale recovery**: Conditional `UPDATE` on ScheduleRuns to transfer claim; only one host can win.

### 2. Host identity

- **RuntimeId** (from `RuntimeHostIdentityOptions`) or optional **SchedulerHostId** override in `ScheduleDispatchOptions`.
- One identity per host process; used for claim ownership.

### 3. Guarantee (explicit)

> **At-most-once dispatch** per (ScheduleId, FireTimeUtc) under lease-based host coordination within the supported topology. If a host crashes after claiming but before dispatch, another host can recover after the lease expires. **No exactly-once guarantee** across all failure modes.

### 4. Scheduler loop integration

1. `hostId` and `staleCutoffUtc = asOfUtc - ClaimLeaseMinutes` are computed.
2. `GetDueAsync` + `GetRecoverableStaleCandidatesAsync` yield candidates (no overlap).
3. For each candidate: `TryClaimFireAsync`; if false, `TryRecoverStaleClaimAsync`; if still false, skip.
4. On successful claim (new or recovered): proceed with lifecycle guard, dispatch, `CompleteRunAsync`.

### 5. Audit events

- **scheduler.claim.recovered**: Emitted when a stale claim is recovered (includes schedule, fire, recovering host).

### 6. Operator visibility

- **ScheduleDetailDto**: `ClaimedByHostId`, `ClaimedAtUtc`, `IsClaimStale` when an in-progress run exists.
- **ScheduleDiagnosticEntry** (runtime health): Same claim fields for schedules with in-progress runs.
- **Schedule detail UI**: Shows claim owner, claim time, and "Stale" badge when past lease.

### 7. Configuration

- **ScheduleDispatch:ClaimLeaseMinutes**: Default 5. Time after which a claim is considered stale.
- **ScheduleDispatch:SchedulerHostId**: Optional override; if null, uses `RuntimeHostIdentity:RuntimeId`.

### 8. Limitations

- **Clock skew**: Stale cutoff uses server time; minor skew may cause early or late recovery.
- **Recovery race**: Two hosts recovering the same stale claim; UPDATE is atomic; one wins. Idempotency key on start request prevents double dispatch.
- **Migration**: `ClaimedByHostId` nullable; existing rows remain valid.

## Consequences

- Multiple scheduler hosts can run against the same database; only one host dispatches each fire.
- Crashed hosts' claims can be recovered after lease expiry.
- Operators see claim ownership and stale status in schedule detail and runtime health.

## References

- Epic 28 — Real Scheduler Implementation (single-host assumption removed)
- Plan: Epic 36 Multi-Host Scheduler Coordination
