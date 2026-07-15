# Worker Hosting Guidance (v1) — SWM-9a / SWM-12

## Product stance

Management **observes** WorkerHost (scheduler heartbeat, SWM-6/7/11) and can run **scheduler recovery** (SWM-8).  
Management does **not** start, stop, or restart WorkerHost in v1.

Process lifecycle belongs to the **host orchestrator**.

## How to start / restart WorkerHost

### Aspire (preferred local)

```powershell
dotnet run --project src/Apperture.AppHost/Apperture.AppHost.csproj
```

Or use the Aspire dashboard to restart the `worker` resource.  
AppHost sets `APERTURE_RUNTIME_ID=worker-local` and `ScheduleDispatch__RuntimePlaneId=local` (SWM-1).

### Scripted local (Manage.ps1)

```powershell
./scripts/Manage.ps1 -Area dev -Command start
# stop / restart via Manage.ps1 as documented in deploy/README.md and scripts/README.md
```

`Manage.ps1` is the supported **script-entry** for the native docker-compose + local-process stack. Prefer **one** stack only — do **not** run Aspire and Manage.ps1 native stacks at the same time (ports 9000/9001).

### Production / future

Windows Service packaging, Docker orchestration, and Azure adapters are **deferred** (SWM-9 audit). Bootstrap config under ProgramData does not start WorkerHost for you. Direct Management process control remains out of scope.

## Observe health (supported)

| Surface | Purpose |
|---------|---------|
| Control Center → **Worker runtime** | Host id, plane, last seen, observed worker count (SWM-11), Healthy/Warning/Unknown |
| `GET /api/control/scheduler-status` | Same + `WorkerRuntime` / `observedWorkers` |

## Schedule recovery (supported, not process lifecycle)

| Action | Endpoint |
|--------|----------|
| Run one cycle | `POST /api/control/scheduler/run-cycle` |
| Recover stale claims | `POST /api/control/scheduler/recover-stale` |

Permission: `recovery.manage`. Neither writes the WorkerHost heartbeat.

## Epic72B lifecycle quarantine (SWM-9a / SWM-12)

| Endpoint | v1 behavior |
|----------|-------------|
| `POST /api/control/recovery/runtime-worker/start` | **410 Gone** — quarantined; never shells to scripts |
| `GET /api/control/recovery/runtime-worker/status` | **200** with status `Quarantined` and guidance to use scheduler-status |

**Historical only:** Epic72B planned `scripts/dev/start-worker.ps1` / `restart-worker.ps1`. Those files are **not** in the repo and are **not** restored as supported Management actions (SWM-12 closes script hygiene by documentation alignment, not by recreating broken Management lifecycle scripts).

Canonical historical note: [Epic72B ADR](../02-decisions/Epic72B-Worker-Runtime-Launch-Recovery-and-Workflow-Testability.md) (Superseded / Quarantined).

## Related ADRs

- [Epic-SWM12 Script Hygiene](../02-decisions/Epic-SWM12-Worker-Hosting-Script-Hygiene.md)
- [Epic-SWM9 Design Audit](../02-decisions/Epic-SWM9-Worker-Lifecycle-Fleet-Management-Design-Audit.md)
- [Epic-SWM9a Quarantine](../02-decisions/Epic-SWM9a-Worker-Hosting-Guidance-Lifecycle-Quarantine.md)
- [Epic-SWM11 Multi-host observation](../02-decisions/Epic-SWM11-Multi-Host-Worker-Observation-Model.md)
- [deploy/README.md](../../deploy/README.md) · [scripts/README.md](../../scripts/README.md)
