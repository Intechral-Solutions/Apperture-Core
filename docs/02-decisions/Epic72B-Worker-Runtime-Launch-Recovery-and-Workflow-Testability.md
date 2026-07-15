# Epic 72B - Worker Runtime Launch Recovery and Workflow Testability

## Status

**Superseded / Quarantined** (SWM-9a + SWM-12)

Management-driven WorkerHost start/restart is **not** a supported v1 product path. Prefer AppHost/Aspire or `Manage.ps1`. See:

- [Worker-Hosting-Guidance-v1.md](../03-operations/Worker-Hosting-Guidance-v1.md)
- [Epic-SWM9a-Worker-Hosting-Guidance-Lifecycle-Quarantine.md](Epic-SWM9a-Worker-Hosting-Guidance-Lifecycle-Quarantine.md)
- [Epic-SWM12-Worker-Hosting-Script-Hygiene.md](Epic-SWM12-Worker-Hosting-Script-Hygiene.md)

## Context (historical)

Dev workflows were blocked because the testing worker runtime could fail to launch with poor diagnosability and no practical retry path short of tearing down the environment.

## Root cause (historical)

The worker launch path relied on fragile dev environment assumptions:

- worker startup depended on env propagation and bootstrap SQL configuration being present at launch time
- failures were mostly visible only in ephemeral console windows
- no dedicated recovery action existed to start/restart only the worker runtime after failure.

## Original decision (historical — do not implement as written)

Epic 72B proposed:

1. add dedicated worker start/restart scripts with persisted startup state + logs
2. call worker startup via that script from `scripts/dev/up.ps1`
3. add development-only recovery endpoints to start/restart worker and read worker launch status
4. improve WorkerHost startup logging with explicit begin/success/failure events.

**Outcome:** `scripts/dev/start-worker.ps1` / `restart-worker.ps1` were never shipped as supported repo scripts. The Management endpoints remain **quarantined** (`POST .../runtime-worker/start` → **410**). File/PID launch status is not the SWM-6/7/11 heartbeat observation model.

## Consequences (current)

- Use **AppHost/Aspire** (preferred) or **Manage.ps1** to start/stop WorkerHost.
- Use Control Center → Worker runtime / `GET /api/control/scheduler-status` to observe health (incl. multi-host observation, SWM-11).
- Use SWM-8 scheduler run-cycle / recover-stale for schedule recovery — not process lifecycle.
- Hosting adapters (Windows Service / Docker / Azure) remain deferred.
