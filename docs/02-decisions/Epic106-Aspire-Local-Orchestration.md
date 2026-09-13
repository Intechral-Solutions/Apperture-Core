# Epic 106 — Aspire local orchestration

**Status:** Delivered

**Context:** Script-based dev (`Start-Dev.ps1`, docker-compose SQL) worked but lacked unified startup ordering, connection injection, and observability defaults. Epic 105B positioned Aspire as an **environment-layer** refinement, not a replacement for the four-layer model.

## Decision

Add [`Apperture.AppHost`](../../src/Apperture.AppHost/) and [`Apperture.ServiceDefaults`](../../src/Apperture.ServiceDefaults/) as the **preferred local full-stack entry point**, while **preserving** the script/docker-compose path as an alternate with selective restart and DB tooling.

## Delivered

| Component | Behavior |
|-----------|----------|
| **AppHost** | SQL Server container (`apperture-aspire-sql` volume); injects `ConnectionStrings:DefaultConnection` to Management.Host and WorkerHost |
| **Processes** | Management API (:9000), Management UI (:9001), WorkerHost |
| **Ordering** | `WaitFor`: SQL → Management API (health/migrations) → UI and Worker |
| **ServiceDefaults** | Health checks, OpenTelemetry, service discovery conventions |
| **Worker identity** | `APERTURE_RUNTIME_ID=worker-local` for **host/claim** identity (not schedule plane) |
| **Schedule plane (SWM-1)** | `ScheduleDispatch__RuntimePlaneId=local` so WorkerHost timer matches assignment defaults |

### Scheduling under Aspire (SWM-1)

| Role | Process | Mechanism |
|------|---------|-----------|
| Timer dispatch | WorkerHost | `ScheduleDispatchBackgroundService` evaluates plane `local` |
| Manual dispatch | Management.Host | Recovery `run-cycle` (plane `local`) |
| Execution | WorkerHost | `StartRequestWorker` |
| Shared SQL | Both | Aspire-injected `ConnectionStrings:DefaultConnection` → catalog `Apperture` |

**Do not** treat `APERTURE_RUNTIME_ID` as the schedule plane. See [Epic SWM-1](./Epic-SWM1-Runtime-Plane-Identity-Alignment.md).

**Preferred start:**

```powershell
dotnet run --project src/Apperture.AppHost/Apperture.AppHost.csproj
```

## Explicit boundaries

| In scope | Out of scope |
|----------|--------------|
| Local dev orchestration and connection injection | Production bootstrap store (108) |
| Dev-time env for SQL | Platform settings registry (109) |
| Dashboard / OTEL defaults | Merging Aspire and docker-compose SQL volumes |
| | Deprecating `Start-Dev.ps1` |
| | Eliminating appsettings Development copy (auth/bootstrap still required) |

**Mutual exclusion:** Do not run Aspire and script paths simultaneously (API/UI 9000/9001; Aspire SQL host port **14333** by default vs compose **1433**; separate SQL volumes and credentials).

## References

- [Epic SWM-1](./Epic-SWM1-Runtime-Plane-Identity-Alignment.md) — schedule plane vs `APERTURE_RUNTIME_ID`
- [ADR-0039](./ADR-0039-environment-startup-bootstrap-and-platform-settings.md) — Epic 106 relationship section
- [management-platform-configuration.md](../03-operations/management-platform-configuration.md) — local orchestration section
- [Epic 107](./Epic107-Post-Aspire-Assessment.md) — what Aspire solved vs deferred
- [Epic 110](./Epic110-Foundation-Stabilization.md) — smoke baseline (Aspire path)
