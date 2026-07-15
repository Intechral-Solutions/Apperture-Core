# Worker Runtime Dev Recovery (Epic72B) — Quarantined

> **SWM-9a / SWM-12:** Script-backed Management lifecycle is **quarantined** and will **not** be restored via missing `start-worker.ps1` / `restart-worker.ps1`.  
> Do not use these APIs as operational controls. See [Worker-Hosting-Guidance-v1.md](Worker-Hosting-Guidance-v1.md) and [Epic-SWM12](../02-decisions/Epic-SWM12-Worker-Hosting-Script-Hygiene.md).

## Preferred actions

1. Observe: Control Center → Worker runtime / `GET /api/control/scheduler-status` (multi-host observation: SWM-11)
2. Scheduler stuck: `POST /api/control/scheduler/run-cycle` or `recover-stale`
3. Worker process down: restart via **AppHost/Aspire** or **Manage.ps1** — not Management

## Quarantined endpoints

| Method | Path | Response |
|--------|------|----------|
| GET | `/api/control/recovery/runtime-worker/status` | 200, `Status=Quarantined` |
| POST | `/api/control/recovery/runtime-worker/start` | **410 Gone** |
| POST | `/api/control/recovery/runtime-worker/start?restart=true` | **410 Gone** |

These no longer spawn PowerShell. References to `start-worker.ps1` / `restart-worker.ps1` are **historical** — those scripts are not in the repo and are not supported commands. WorkerHost process lifecycle stays with AppHost/Aspire or Manage.ps1.

## Historical context (Epic72B)

Epic72B added file-backed launch state under `artifacts/devlogs/` for native script recovery. That path is incomplete relative to Aspire-first local development and is not the SWM-6/7/11 heartbeat observation model. The Epic72B ADR is marked **Superseded / Quarantined**.
