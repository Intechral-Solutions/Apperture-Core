# Worker Runtime Dev Recovery (Epic72B) — Quarantined

> **SWM-9a (2026-07-14):** Script-backed Management lifecycle is **quarantined**.  
> Do not use these APIs as operational controls. See [Worker-Hosting-Guidance-v1.md](Worker-Hosting-Guidance-v1.md).

## Preferred actions

1. Observe: Control Center → Worker runtime / `GET /api/control/scheduler-status`
2. Scheduler stuck: `POST /api/control/scheduler/run-cycle` or `recover-stale`
3. Worker process down: restart via **AppHost/Aspire** or **Manage.ps1** — not Management

## Quarantined endpoints

| Method | Path | Response |
|--------|------|----------|
| GET | `/api/control/recovery/runtime-worker/status` | 200, `Status=Quarantined` |
| POST | `/api/control/recovery/runtime-worker/start` | **410 Gone** |
| POST | `/api/control/recovery/runtime-worker/start?restart=true` | **410 Gone** |

These no longer spawn PowerShell or reference missing `start-worker.ps1` / `restart-worker.ps1` as supported.

## Historical context (Epic72B)

Epic72B added file-backed launch state under `artifacts/devlogs/` for native script recovery. That path is incomplete relative to Aspire-first local development and is not the SWM-6/7 heartbeat model.
