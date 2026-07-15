# Epic SWM-11 — Multi-Host Worker Observation Model

**Status:** Delivered  
**Track:** Scheduling & Worker Management  
**Depends on:** SWM-6/7 (heartbeat + worker projection), SWM-9/9a (observe-only lifecycle stance)  
**Delivered:** 2026-07-15

## Contract (v1)

1. Heartbeat identity is **`(RuntimePlaneId, HostId)`** — each observed WorkerHost writes its own row.
2. **Primary** scheduler status for a plane = heartbeat with **max `UpdatedUtc`** (then HostId).
3. `GET /api/control/scheduler-status` remains the SWM-6/7 surface and adds:
   - `observedWorkers[]`
   - `observedWorkerCount` / `staleObservedWorkerCount`
   - `observationGuidance`
   - `WorkerRuntime.InventoryModel` = `SingleHostObserved` | `MultiHostObserved`
4. Observe-only: **no** start/stop/restart/scale/claim-steal/live-attach controls.
5. Stale classification reuses SWM-6 thresholds (`max(60s, 2×poll)`).
6. Lifecycle remains externally managed (SWM-9a).

## Why the key changed

SWM-6 used PK = `RuntimePlaneId` (last writer wins). That hid additional hosts and made Aspire+Manage.ps1 dual-writer situations invisible. SWM-11 preserves history per host for observation.

## Guidance

| Situation | Guidance focus |
|-----------|----------------|
| No workers | Start WorkerHost via Aspire / Manage.ps1; manual run-cycle does not write heartbeat |
| One healthy | Primary snapshot healthy (no extra observation blurb) |
| Multiple hosts | Counts + “primary = newest”; Epic36 claim leases may apply; avoid mixing Aspire and Manage.ps1 |
| Stale | Timer / DB connectivity |
| Plane mismatch | Assignments may not be evaluated |
| HostId reuse | Confirm unique `APERTURE_RUNTIME_ID` |

## Deferred

Fleet orchestration, lifecycle adapters, scaling, claim ownership dashboard, live process attach, calendar/designer.

## Relationship

- SWM-9 audit recommended multi-host **inventory** (earlier labeled SWM-10 there); track numbering delivered that as **SWM-11**.
- SWM-10 delivered schedule next-fire clarity (separate).
- SWM-9a quarantine of `runtime-worker/start` unchanged.

## Migration

`20260715010000_SWM11_MultiHostHeartbeatKey` — composite PK `(RuntimePlaneId, HostId)`.
