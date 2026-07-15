# Epic SWM-9 — Worker Lifecycle & Fleet Management Design Audit

**Status:** Audit complete (no implementation)  
**Track:** Scheduling & Worker Management  
**Depends on:** SWM-1 … SWM-8 (closed)  
**Date:** 2026-07-14

## 1. Executive summary

**Recommendation: defer direct WorkerHost lifecycle controls (start/stop/restart) and multi-host fleet dashboards.**

For v1, Apperture should **observe** workers and **operate the scheduler safely** (already delivered in SWM-6…SWM-8), while **process lifecycle stays with the host orchestrator** (Aspire AppHost, Manage.ps1 / compose, future Windows Service / Docker / Azure).

A Development-only recovery path exists (`DevWorkerRuntimeRecoveryService` → `POST /api/control/recovery/runtime-worker/start`), but it is **not suitable as the v1 product model**:

- It shells out to `scripts/dev/start-worker.ps1` / `restart-worker.ps1`, which are **not present in the repo** (broken/fragile).
- Status is a **file-backed** `artifacts/devlogs/worker-runtime-state.json` path, not the SWM-6/7 DB heartbeat.
- It is **Aspire-blind** (Aspire owns the worker resource; script/PID start races the AppHost graph).
- It is **not surfaced in Control Center** Worker runtime (SWM-7/8 use heartbeat + scheduler ops).

**v1 stance:** Option **A (observe-only for process lifecycle)** + limited **B (guided host runbooks)** + keep SWM-8 **scheduler** recovery. Option **C (direct lifecycle)** and multi-host **fleet UI** are premature. Option **D (external orchestrator)** is the long-term production model; for Aspire today that means “open Aspire dashboard / re-run AppHost,” not Management killing PIDs.

---

## 2. Current runtime hosting model

### Processes that exist today

| Process / project | Role in scheduling | In Aspire AppHost? |
|-------------------|--------------------|--------------------|
| **WorkerHost** (`Apperture.RuntimeHost.Windows.WorkerHost`) | Timer schedule dispatch + start-request workers; **authoritative** background scheduler | Yes (`"worker"`) |
| **Management.Host** | CRUD, health APIs, **manual** `IScheduleDispatchRunner` run-cycle / recover-stale (SWM-8); no timer heartbeat | Yes (`"management-api"`) |
| Management UI Server | Blazor host | Yes (`"management-ui"`) |
| **SchedulerHost** | Stub only; **not** live dispatch | No |
| **ModuleHost** | Stub / future one-module-per-process; not local fleet | No |
| InstanceAgent | Separate Windows host stub area | No |

Architecture docs (`Epic73`, epic107, project-map) confirm: **schedule dispatch lives in WorkerHost**, not SchedulerHost.

### How workers start in Aspire / preferred local Dev

`AppHost.cs` adds WorkerHost with:

- `APERTURE_RUNTIME_ID=worker-local` → **host/claim identity**
- `ScheduleDispatch__RuntimePlaneId=local` → **assignment plane** (SWM-1)

SQL is Aspire-injected `DefaultConnection`. Lifecycle of WorkerHost is **Aspire project resource lifecycle** (start/stop with the AppHost graph / Aspire dashboard).

### Alternate local path (scripts)

`Manage.ps1 -Area dev -Command start` (native) starts compose SQL + Management API/UI + WorkerHost as separate processes. Mutual exclusion with Aspire (ports 9000/9001; SQL 1433 vs 14333) is documented in `deploy/README.md`.

`DevRuntimeState.ps1` still knows `worker-runtime-state.json`, but the Epic72B scripts invoked by Management are missing.

### Production / on-prem expectation

- WorkerHost is a **generic .NET Generic Host** (`Host.CreateApplicationBuilder` → `host.Run()`).
- **No** `UseWindowsService()` / Windows Service installer wiring found in WorkerHost Program.
- On-prem bootstrap uses `%ProgramData%\Apperture\bootstrap\...` for Management/bootstrap identity — not a worker service manager.
- Docker/k8s/Azure folders are largely **future** (`deploy/README.md`).

**Conclusion for Q4:** WorkerHost is **intended to be the Windows/on-prem worker process**, but **v1 does not yet ship a first-class Windows Service packaging**; operators run it as Aspire-managed or script-launched console host today.

---

## 3. Current visibility / control surfaces (after SWM-6…SWM-8)

### Safe observe (preferred product surface)

| Surface | What it answers |
|---------|-----------------|
| `GET /api/control/scheduler-status` + `WorkerRuntime` | Host id, plane, last seen, Healthy/Warning/Unknown, alignment, optional process metadata |
| Control Center → **Worker runtime** | Same, with SWM-4 timestamps |
| Scheduler heartbeat table | Timer-loop proof (WorkerHost only) |

### Safe schedule operations (not process lifecycle)

| Surface | What it does |
|---------|--------------|
| `POST /api/control/scheduler/run-cycle` (+ recovery alias) | One evaluation cycle; **no heartbeat write** |
| `POST /api/control/scheduler/recover-stale` | Stale InProgress only |
| Control Center → Scheduler operations | Same, `recovery.manage` |
| Schedule Detail hints | Why-not-firing guidance |

### Dev-only / host-specific (do not expand for v1 product)

| Surface | Concern |
|---------|---------|
| `POST /api/control/recovery/runtime-worker/start` | Development-gated process spawn of **missing** scripts |
| `GET .../runtime-worker/status` | File PID state ≠ SWM heartbeat |
| Not in Control Center Worker runtime | Split-brain operator story |

### What Management can **safely show** today

- Single-host **observed** worker from heartbeat (`SingleHostObserved`)
- Plane vs expected Alignment
- Scheduler health + SWM-8 ops results
- Guidance for unknown/stale heartbeat

### What Management can **safely control** today

- Scheduler evaluation and stale claim recovery
- **Not** Aspire/Windows/Docker process graphs as a product feature

---

## 4. Risk assessment

| Risk | Detail |
|------|--------|
| **Wrong process** | Script/PID based start against Aspire-managed worker can create **two** WorkerHosts writing heartbeats (last writer wins per plane — SWM-6 limitation). |
| **Broken recovery API** | Missing `start-worker.ps1` / `restart-worker.ps1` → Conflict failures; false confidence if docs say “use Recovery API.” |
| **Split visibility** | File state vs DB heartbeat vs Aspire dashboard — three truths. |
| **Security** | Direct process control from Management elevates Management host to host admin; privilege, audit, and remote blast radius increase. |
| **Permissions** | Today reuse `recovery.manage` for scheduler ops; lifecycle would need **stronger** permission (e.g. `runtime.host.manage`) + mandatory audit with host id / correlator. |
| **Multi-host** | Claim lease + unique fire index exist (Epic36); fleet UI without claim topology is misleading. |
| **Production gap** | No service manager integration → Management “Restart worker” cannot map to SCM/systemd/k8s without adapters. |

---

## 5. Options comparison

### Option A — Observe-only (process lifecycle)

Apperture shows health/heartbeats/guidance; does not start/stop/restart WorkerHost.

| Pros | Cons |
|------|------|
| Matches SWM-6/7 truth model | Operators must use Aspire/scripts/OS tools for down workers |
| Avoids PID/Aspire races | Unknown status still needs external fix |
| Low security surface | |

**Docs:** Clarify Aspire vs script path; “if heartbeat unknown → check AppHost / Manage.ps1.”

### Option B — Guided host-level actions

UI shows **copyable** commands / runbook links per hosting mode (`Aspire`, `ScriptDev`, later `WindowsService`).

| Pros | Cons |
|------|------|
| Safer than executing from API | Still no one-click |
| Bridges operability gap | Needs accurate hosting detection |

**Docs:** Hosting-mode runbooks; deprecate or quarantine broken Epic72B API until scripts restored for native path only.

### Option C — Direct lifecycle controls

Management starts/stops/restarts WorkerHost.

| Pros | Cons |
|------|------|
| Attractive for demos | **Premature for v1**; host-model explosion; missing adapters; Aspire conflict; elevation risk |

**Verdict:** Do **not** productize until hosting adapters + identity + audit exist.

### Option D — External orchestrator integration

Aspire/Docker/Windows Service/Azure own lifecycle; Apperture observes and deep-links.

| Pros | Cons |
|------|------|
| Correct long-term boundary | Aspire deep-links / service tags need product work |
| Aligns with AppHost as source of truth | |

**Realistic today:** soft guidance (“use Aspire dashboard / restart AppHost”). Future: hosting-mode plugins.

---

## 6. Recommended v1 model

**Hybrid: A + light B**, keeping SWM-8 scheduler controls.

1. **Observe** via SWM-6/7 heartbeat + Control Center (continue).
2. **Schedule recovery** via SWM-8 only (continue).
3. **Do not** promote `runtime-worker/start` into Control Center or production docs as primary control.
4. **Document** lifecycle as host-managed:
   - Aspire: AppHost / Aspire dashboard
   - Native: Manage.ps1 / start-stop
   - Future: Windows Service / containers / Azure — TBD adapters
5. Optionally later: **hosting mode label** + runbook links (small epic) without executing process control.
6. Quarantine Epic72B API in docs as “legacy native-script recovery; scripts currently incomplete; Aspire-incompatible.”

### Answers to key questions (condensed)

| # | Answer |
|---|--------|
| 1 | WorkerHost is the live worker; Management has no timer; SchedulerHost/ModuleHost stubs |
| 2 | Aspire AppHost project resource (preferred); or Manage.ps1 native |
| 3 | Console host / future OS service or container — **not** Management-orchestrated |
| 4 | Intended Windows/on-prem worker **role**, but **not** packaged as Windows Service in-tree yet |
| 5 | Dev-only API exists but depends on **missing** scripts; not Aspire-safe |
| 6 | **No** as v1 product capability |
| 7 | Aspire=AppHost; WinSvc=SCM; Docker=compose/k8s; Azure=hosting control plane — Apperture should query/link, not replace |
| 8 | Heartbeat inventory + guidance |
| 9 | Scheduler run-cycle / recover-stale |
| 10 | Process start/stop/restart/scale |
| 11 | Host id + plane + orchestrator resource identity; never PID-only |
| 12 | New `runtime.host.manage` + audit; not fold into casual `recovery.manage` |
| 13 | Heartbeat last-writer-wins unsafe for fleet; need multi-row inventory + claim topology (Epic36+) |
| 14 | v1 “fleet” = **one observed worker per expected plane**; future = multi-host inventory |

---

## 7. Required follow-up epics

### SWM-9a — Hosting Mode Guidance (observe + guided docs/UI) — **Delivered**

See `Epic-SWM9a-Worker-Hosting-Guidance-Lifecycle-Quarantine.md`. Epic72B start is **410**; Control Center + Developer guidance added.

### SWM-9b / SWM-12 — Epic72B Native-Script Recovery Hygiene — **Delivered**

Delivered as **SWM-12** (`Epic-SWM12-Worker-Hosting-Script-Hygiene.md`).

- **Decision:** Do **not** restore missing `start-worker.ps1` / `restart-worker.ps1` for Management. Keep quarantine (410). Align docs (Epic72B superseded; Worker-Hosting-Guidance is source of truth).
- **Supported lifecycle:** AppHost/Aspire or Manage.ps1 only.
- **Out:** Recreating Management shell-out scripts; unquarantining endpoints.

### SWM-10 — Schedule readiness & next-fire clarity — **Delivered**

See `Epic-SWM10-Schedule-Management-Readiness-Next-Fire-Clarity.md`.

### SWM-11 — Multi-Host Worker Observation (observe inventory) — **Delivered**

- **Goal:** Replace last-writer heartbeat with multi-host observation when >1 host is real.
- **Implementation:** See `Epic-SWM11-Multi-Host-Worker-Observation-Model.md`.
- **Out:** Lifecycle, scaling, claim-steal UI, adapters.

### Later — Hosting Adapters for Lifecycle (defer past v1)

- **Goal:** Optional adapters that request lifecycle from external control planes (Aspire DCP/API, Windows SCM, Docker, Azure).
- **Scope:** Pluggable `IWorkerLifecycleAdapter`; permission `runtime.host.manage`; heavy audit; explicit resource identity.
- **Acceptance:** Adapter can request restart only for configured resource; fail closed when adapter unknown.
- **Tests:** Adapter contract; deny without permission; audit emission.
- **Docs:** Adapter security model.

### Out of scope for next increments

- Scale-out / claim rebalancing UI
- Live process attach / debugger
- Changing SWM-1 plane identity or schedule semantics

---

## Evidence index (primary)

- `src/Apperture.AppHost/AppHost.cs` — Aspire worker env
- `…/WorkerHost/Program.cs` — Generic Host; hosted schedule loop; no Windows Service
- `…/DevWorkerRuntimeRecoveryService.cs` — Dev script spawn + file status
- `docs/03-operations/Worker-Runtime-Dev-Recovery.md` — Epic72B ops
- `docs/02-decisions/Epic72B-…md`, Epic-SWM6/7/8 ADRs
- `deploy/README.md` — Aspire vs Manage.ps1
- Missing: `scripts/dev/start-worker.ps1`, `restart-worker.ps1` (referenced, not found)

---

## Closure

SWM-9 is an **audit/design** epic. No code was changed. Implementation should start with **SWM-9a** (guidance + hygiene), not direct lifecycle UI.
