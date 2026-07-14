# Epic SWM-1 — Runtime-plane identity alignment under Aspire

**Status:** Delivered  
**Track:** Scheduling & Worker Management  
**Evidence:** [swm1-runtime-plane-diagnosis.md](../audits/swm1-runtime-plane-diagnosis.md), [scheduling-runtime-audit.md](../audits/scheduling-runtime-audit.md)

## Context

Timer-driven schedule dispatch under Aspire appeared dead while manual `run-cycle` worked. Root cause: `ScheduleDispatch:RuntimePlaneId` incorrectly inherited `APERTURE_RUNTIME_ID` (`worker-local`), while schedule assignments default to plane `local`.

## Decision

1. **Schedule plane** (`ScheduleDispatch:RuntimePlaneId`, default `local`) selects which `ScheduleAssignments` the WorkerHost timer evaluates. It must match assignment defaults from Management APIs.
2. **Runtime / claim host id** (`APERTURE_RUNTIME_ID`, Aspire value `worker-local`) identifies the worker for leases and `ScheduleRuns.ClaimedByHostId`. It must **not** automatically become the schedule plane.
3. **Ownership (unchanged):**
   - Timer dispatch → WorkerHost (`ScheduleDispatchBackgroundService`)
   - Manual dispatch → Management API recovery endpoint
   - Execution → WorkerHost (`StartRequestWorker`)
4. Under Aspire, Management and Worker share SQL via `ConnectionStrings:DefaultConnection` (catalog `Apperture`, host port **14333** by default).

## Implementation notes

- WorkerHost PostConfigure uses `ScheduleDispatchOptionsDefaults.ApplyPlaneDefaults` (default plane `local`).
- AppHost sets `ScheduleDispatch__RuntimePlaneId=local` and keeps `APERTURE_RUNTIME_ID=worker-local`.
- Dispatch loop logs plane, host id, assignment/due/stale/candidate counts each cycle.

## How to verify scheduling is alive (Aspire)

1. Start AppHost; wait for Management API healthy and Worker running.
2. Create an enabled short-interval schedule on an Active HelloWorld (or other) integration; confirm it appears in Management UI.
3. In WorkerHost logs, confirm: `Schedule dispatch loop starting. Plane=local HostId=worker-local`.
4. Within one poll interval (≥15s), confirm cycle logs show non-zero due/candidates when a fire is due, and `ScheduleRuns` / run history appear — **without** using manual run-cycle.

## Out of scope (next epics)

Scheduler health endpoint / non-green health when idle, assignment-plane operator UI, worker fleet management, non-Aspire LocalDB/`SqlServer` divergence hardening, retiring `SchedulerHost` stub.

## References

- [Epic 106](./Epic106-Aspire-Local-Orchestration.md)
- [ADR-0004 instance-owned scheduling](./ADR-0004-instance-owned-scheduling.md)
- [deploy/README.md](../../deploy/README.md)
