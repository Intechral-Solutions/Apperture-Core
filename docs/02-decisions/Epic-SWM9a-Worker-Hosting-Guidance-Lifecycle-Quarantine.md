# Epic SWM-9a — Worker Hosting Guidance & Broken Lifecycle Quarantine

**Status:** Delivered  
**Track:** Scheduling & Worker Management  
**Depends on:** SWM-9 design audit  
**Delivered:** 2026-07-14

## Contract

1. Management **does not** start/stop/restart WorkerHost in v1.
2. Control Center Worker runtime states observe-only + hosting guidance (Aspire / Manage.ps1 / future deferred).
3. Epic72B `POST .../runtime-worker/start` returns **410** with `runtime_worker_lifecycle_quarantined`.
4. Epic72B `GET .../runtime-worker/status` returns status **Quarantined** (no PID/script truth).
5. SWM-8 scheduler ops remain available and unchanged in semantics.
6. Truthful observability remains SWM-6/7 scheduler heartbeat.

## Docs

- Ops: `docs/03-operations/Worker-Hosting-Guidance-v1.md`
- Quarantine note: `docs/03-operations/Worker-Runtime-Dev-Recovery.md`

## Deferred

SWM-9b (optional script hygiene), SWM-10 multi-host inventory, SWM-11 host adapters.
