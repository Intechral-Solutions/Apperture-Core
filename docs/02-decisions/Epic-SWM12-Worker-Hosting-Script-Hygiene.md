# Epic SWM-12 — Worker Hosting Script Hygiene & Dev Operations Alignment

**Status:** Delivered  
**Track:** Scheduling & Worker Management  
**Depends on:** SWM-9a (quarantine), SWM-11 (multi-host observe)  
**Delivered:** 2026-07-15  
**Also closes:** SWM-9b (Epic72B native-script hygiene) — **do not restore** missing Management lifecycle scripts

## Contract

1. Missing `start-worker.ps1` / `restart-worker.ps1` are **historical only** — not restored as supported Management actions.
2. Supported WorkerHost lifecycle entry points: **AppHost/Aspire** (preferred) and **Manage.ps1** (native/scripted stack).
3. Management **observes** (SWM-6/7/11) and runs **scheduler recovery** (SWM-8); it does **not** start/stop/restart WorkerHost.
4. Quarantine remains: `POST /api/control/recovery/runtime-worker/start` → **410**; status → `Quarantined`.
5. Windows Service / Docker / Azure lifecycle adapters remain **deferred**.

## Investigation answers

| Question | Answer |
|----------|--------|
| Docs implying Management starts WorkerHost? | Epic72B ADR still described that path as Accepted — corrected to Superseded/Quarantined. UI/ops docs already directed away. |
| Missing scripts referenced? | Only as **historical / not supported** (guidance + audits). Not present under `scripts/dev/`. |
| Manage.ps1 correct local entry? | Yes for native/scripted stack (`-Area dev -Command start`). |
| AppHost correct Aspire path? | Yes — preferred. |
| Prod lifecycle deferred? | Yes — documented. |
| Dead refs? | Document as historical; do not restore scripts for Management. |

## Related

- [Worker-Hosting-Guidance-v1.md](../03-operations/Worker-Hosting-Guidance-v1.md)
- [Worker-Runtime-Dev-Recovery.md](../03-operations/Worker-Runtime-Dev-Recovery.md)
- [Epic72B ADR](Epic72B-Worker-Runtime-Launch-Recovery-and-Workflow-Testability.md) (superseded)
