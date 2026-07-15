# Epic SWM-8 — Scheduler Operations & Recovery Controls

**Status:** Delivered  
**Track:** Scheduling & Worker Management  
**Depends on:** SWM-6 / SWM-7 (visibility)  
**Delivered:** 2026-07-14

## Contract (v1)

1. **Manual run-cycle** evaluates due fires (and still recovers stale claims as part of the normal cycle). Paths:
   - `POST /api/control/recovery/schedules/run-cycle` (compat)
   - `POST /api/control/scheduler/run-cycle` (preferred alias)
2. **Stale-only recovery** recovers only stale `InProgress` claims: `POST /api/control/scheduler/recover-stale`.
3. Both are gated by **`recovery.manage`**.
4. Neither writes the SWM-6 WorkerHost **scheduler heartbeat** (`WritesHeartbeat: false`).
5. Neither restarts WorkerHost, scales workers, or changes schedule definitions.
6. Idempotency / unique `(ScheduleId, FireTimeUtc)` unchanged.

## Background timer vs manual cycle

| Path | Evaluates due | Recovers stale | Writes heartbeat |
|------|---------------|----------------|------------------|
| WorkerHost `ScheduleDispatchBackgroundService` | Yes | Yes | Yes |
| Manual run-cycle | Yes | Yes (as part of cycle) | **No** |
| Stale-only recovery | **No** | Yes only | **No** |

## Stale criteria (unchanged)

- Outcome = `InProgress`
- `StartedAtUtc < now − ClaimLeaseMinutes`
- Schedule definition **Enabled**

Fresh in-progress and disabled schedules are skipped.

## UI

- Control Center → Worker runtime → **Scheduler operations** (advanced): run cycle + recover stale, with count results.
- Schedule Detail → narrow “why not firing” hints + link to Control Center.
- Developer/Internal: no duplicate ops surface (link via Control Center).

## Deferred — SWM-9+

See design audit: `Epic-SWM9-Worker-Lifecycle-Fleet-Management-Design-Audit.md`.

- **SWM-9a** — Hosting mode guidance (observe + runbooks; no process control) — delivered
- **SWM-9b** — Optional Epic72B native-script hygiene (or remove)
- **SWM-10** — Schedule readiness & next-fire clarity — delivered (`Epic-SWM10-…`)
- **SWM-11** — Multi-host worker observation — delivered (`Epic-SWM11-…`)
- **Later** — External hosting adapters for lifecycle (post-v1)
