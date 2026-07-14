# Worker Hosting Guidance (v1) — SWM-9a

## Product stance

Management **observes** WorkerHost (scheduler heartbeat, SWM-6/7) and can run **scheduler recovery** (SWM-8).  
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
# stop / restart via Manage.ps1 as documented in deploy/README.md
```

Do **not** run Aspire and native Manage.ps1 stacks at the same time (ports 9000/9001).

### Production / future

Windows Service packaging, Docker orchestration, and Azure adapters are **deferred** (see SWM-9 audit / SWM-11). Bootstrap config under ProgramData does not start WorkerHost for you.

## Observe health (supported)

| Surface | Purpose |
|---------|---------|
| Control Center → **Worker runtime** | Host id, plane, last seen, Healthy/Warning/Unknown |
| `GET /api/control/scheduler-status` | Same + `WorkerRuntime` projection |

## Schedule recovery (supported, not process lifecycle)

| Action | Endpoint |
|--------|----------|
| Run one cycle | `POST /api/control/scheduler/run-cycle` |
| Recover stale claims | `POST /api/control/scheduler/recover-stale` |

Permission: `recovery.manage`. Neither writes the WorkerHost heartbeat.

## Epic72B lifecycle quarantine

| Endpoint | v1 behavior |
|----------|-------------|
| `POST /api/control/recovery/runtime-worker/start` | **410 Gone** — quarantined; never shells to scripts |
| `GET /api/control/recovery/runtime-worker/status` | **200** with status `Quarantined` and guidance to use scheduler-status |

Missing `scripts/dev/start-worker.ps1` / `restart-worker.ps1` are **not** restored as supported Management actions.

Historical doc: Epic72B introduced these for native-script recovery; they are **Aspire-unsafe** and incomplete for the current preferred path.

## Related ADRs

- [Epic-SWM9 Design Audit](../02-decisions/Epic-SWM9-Worker-Lifecycle-Fleet-Management-Design-Audit.md)
- [Epic-SWM6](../02-decisions/Epic-SWM6-Scheduler-Worker-Runtime-Visibility.md) / [SWM-7](../02-decisions/Epic-SWM7-Worker-Runtime-Inventory-Health-Detail.md) / [SWM-8](../02-decisions/Epic-SWM8-Scheduler-Operations-Recovery-Controls.md)
- [deploy/README.md](../../deploy/README.md)
