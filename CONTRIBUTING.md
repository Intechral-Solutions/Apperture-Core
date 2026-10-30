# Contributing to Apperture Core

Pull requests are welcome. Please read this page, including the licensing terms below, before you submit one.

## Licensing and the CLA

Apperture Core is licensed under the GNU Affero General Public License v3.0 only (`AGPL-3.0-only`); see [LICENSE](LICENSE).

- **You keep your copyright.** Contributors retain ownership of their contributions. Nothing is assigned to the project.
- **You must agree to the CLA before merge.** Every contributor must agree to the [Apperture Individual Contributor License Agreement](CLA.md) (the "CLA") before a pull request can be merged.
- **What the CLA grants.** It gives the project owner (currently Tennyson Edward Hull) a perpetual, worldwide, non-exclusive, irrevocable, royalty-free license to your contribution, with the right to sublicense it. That includes the right to license your contribution under other terms as part of Apperture Core, including commercial or proprietary licenses, alongside the AGPL. The grant is transferable; anyone who later acquires the project's rights must agree in writing to honor the CLA. Read [CLA.md](CLA.md) for the exact terms; it is the binding text, and this page is only a summary.
- **Your contribution stays open source.** Under the CLA, a contribution that is included in Apperture Core is also licensed under the license Apperture Core uses at the time you submitted it, which is `AGPL-3.0-only` today.
- **You must have the right to submit your work.** Only submit work you wrote yourself and have the right to contribute. If you have an employer, or anyone else who might have rights in your work, make sure you have their permission first. The CLA asks you to confirm this. There is no Entity CLA yet. If you are contributing on behalf of an employer or another legal entity, contact the maintainer before contributing.

### Contributor License Agreement

You must execute the CLA before your contribution can be merged. CLA execution is handled manually and privately by the maintainer; it is not automated, and it is not done in pull-request comments or issues.

Contact the maintainer for CLA execution instructions before, or when, you open your first pull request. Do not post your name, address, or other personal contact details in a pull request or issue. Maintainers check that a CLA is on file during review and will not merge a pull request until it is.

### Third-party material

The CLA covers work that you own the copyright in. If your pull request includes anything you did not write (code copied or adapted from another project, generated output you do not own, bundled assets or data), say so in the pull request description. Name each item, where it came from, and its license, and explain why it is compatible with `AGPL-3.0-only`. Do not include third-party material without telling the maintainers; material that cannot be accepted under the project license will be removed.

Do not remove or alter license notices in third-party or vendored files.

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (version in [global.json](global.json))
- [Docker](https://www.docker.com/) (local SQL Server for Aspire or script-based dev)
- Git

Aspire projects (`Apperture.AppHost`, `Apperture.ServiceDefaults`) restore via NuGet (`Aspire.AppHost.Sdk`); contributors do **not** need a separate Aspire template or workload install to build or run the repo.

## Build and test

From the repo root:

```powershell
.\scripts\ci\build.ps1
.\scripts\ci\test.ps1
```

Or use `dotnet build Apperture.slnx` and `dotnet test Apperture.slnx`. Use `.\scripts\ci\test.ps1 -Filter "UnitTests"` to run a subset of tests by name.

## Local dev with SQL Server

Dev setup uses the **environment/startup configuration layer** only (appsettings templates, User Secrets, dev scripts). For the full four-layer model and what belongs where, see [ADR-0039](docs/02-decisions/ADR-0039-environment-startup-bootstrap-and-platform-settings.md).

See [deploy/README.md](deploy/README.md) and [management platform configuration](docs/03-operations/management-platform-configuration.md). Foundation smoke checklist: [Epic 110](docs/03-operations/epic110-foundation-stabilization.md).

### Preferred: .NET Aspire

Requires Docker for the Aspire-managed SQL Server container.

**One-time setup:** run `.\scripts\Initialize-AppertureEnvironment.ps1` or manually copy the docker-dev template (step 1 below).

1. Copy `deploy/config/examples/appsettings.docker-dev.json.example` to `src/Apperture.ControlPlane/Apperture.Management.Host/appsettings.Development.json` (one-time; needed for `DevTestUser`, bootstrap admin, and dev feature flags).
2. Run the AppHost:

   ```powershell
   dotnet run --project src/Apperture.AppHost/Apperture.AppHost.csproj
   ```

   Starts SQL Server, Management API (:9000), Management UI (:9001), and WorkerHost. Opens the Aspire dashboard. [`Apperture.ServiceDefaults`](src/Apperture.ServiceDefaults/) adds conventional health, OpenTelemetry, and service discovery on orchestrated hosts — hosts still run without Aspire when started directly.

   **WorkerHost lifecycle:** AppHost owns the worker process. Management UI observes health (Control Center → Worker runtime) and can run scheduler recovery — it does **not** start/stop/restart WorkerHost. Quarantined `runtime-worker/start` returns 410. See [Worker-Hosting-Guidance-v1.md](docs/03-operations/Worker-Hosting-Guidance-v1.md).

   **URLs (important):**
   - Management UI: `http://localhost:9001` (HTTP only — there is no HTTPS listener on 9001)
   - Management API: `http://localhost:9000`
   - Aspire dashboard: **DCP assigns its ports at random on every run** (observed to vary run over run — e.g. `61548-61550` one run, `63014-63016` the next). Do not reuse a port number from a previous session or from this doc. Always read the actual **login URL printed in the AppHost console** for *this* run (for example `https://localhost:63015/login?t=...`) and use the full tokenized link, not the bare origin — the bare origin redirects to `/login` and needs the token anyway. If you've lost that console output, the AppHost terminal window (titled "Apperture AppHost (Aspire)" when launched via `Start-DevEnvironment.ps1 -Mode aspire`) still has it in scrollback; restarting AppHost is the only way to get a fresh one otherwise.

   Stop any prior AppHost session before restarting (Ctrl+C, or `.\scripts\Manage.ps1 -Area dev -Command stop` plus stop lingering `dcp.exe` / WorkerHost processes) to avoid port conflicts and file-lock build errors.

   **SQL and login (Aspire vs scripts):** AppHost uses a **separate** persistent SQL volume (`apperture-aspire-sql`), not the docker-compose `apperture-sqlserver` volume. Users and passwords from the script path are **not** shared. Under AppHost, `ConnectionStrings:DefaultConnection` from Aspire wins over `SqlServer:` in `appsettings.Development.json` for Management.Host and WorkerHost. Copy `appsettings.Development.json` for `DevTestUser` / `BootstrapPlatformAdmin` only — local login uses the password from that file (template default `dev`) against the **Aspire** database after Management.Host migrations run. To reset, delete the Aspire SQL volume in Docker and restart AppHost.

   Optional: override the SQL password with User Secrets on the AppHost (`dotnet user-secrets set "Parameters:sql-password" "<password>" --project src/Apperture.AppHost`).

   **Reserved dev SQL port — 14333.** Host port 14333 is reserved *exclusively* for the Aspire-managed
   dev SQL container. Never bind an ad-hoc `docker run` or another container to it, even temporarily —
   see the incident below. Manual/one-off SQL Server containers (integration-test debugging, etc.) must
   use a dynamically allocated port instead: `.\scripts\dev\maintenance\start-adhoc-test-sql.ps1`. The
   committed integration-test paths (CI, `SqlServerTestConfiguration`) already don't hardcode 14333 —
   they use a CI-ephemeral port, a dynamically allocated ad-hoc port, or 1433 via docker-compose — and they now
   **refuse** 14333 as a test target (tests create and drop catalogs). See `tests/README.md`, "Test SQL target safety".

   **Startup safeguard.** AppHost registers a health check (`SqlDevPortGuard` in
   `Apperture.AppHost.Diagnostics`) that verifies — via `docker ps --filter volume=apperture-aspire-sql`
   and `docker port` — that the Aspire SQL container *actually* published 14333 before Management
   Host/Worker are allowed to start. If port 14333 is already held by something else, Docker can leave
   the Aspire container "running" without ever publishing the port; without this check, Aspire still
   hands out `localhost,14333` in `DefaultConnection` regardless, and Management Host/Worker silently
   connect through to whatever unrelated SQL Server already owns that port — no exception, just the
   wrong database. With the check in place, the `sql` resource stays Unhealthy in the dashboard with a
   message naming the conflict, and Management Host/Worker simply never start.

   **If startup reports port 14333 is occupied:** find what's holding it
   (`docker ps --filter publish=14333`), decide whether that container is still needed, then either stop
   it (`docker stop <name>`) or, if it must keep running, permanently move it off 14333. Do **not** just
   pick a different port for Aspire's dev SQL — 14333 is the stable, documented endpoint every dev tool
   targets. Once the port is free, `docker rm` the Aspire SQL container (its data is safe in the
   `apperture-aspire-sql` volume, untouched by removing the container) and restart AppHost so DCP
   recreates it with the port actually bound.

   *(Incident: 2026-09-14 — a leaked, manually-created `apperture-integration-sql-14333` container had
   squatted on host port 14333 for 36+ hours. AppHost's own SQL container came up "running" but Docker
   silently failed to publish the port; Management Host/Worker connected straight through to the
   leftover container instead and ran migrations against a fresh, empty database there. Local logins
   failed and the Microsoft/Entra sign-in option disappeared because the app was quietly talking to the
   wrong SQL Server, not because credentials or Entra config were actually broken. The real dev database
   was untouched throughout. This section and `SqlDevPortGuard` are the fix.)*

**Epic 106 scope:** local dev orchestration. **Epic 108** adds the file-backed bootstrap configuration store for Windows/on-prem production (`%ProgramData%\Apperture\bootstrap\configuration.json`); Aspire local dev does not require that file.

### Alternate: PowerShell scripts

1. `dotnet user-secrets set "SqlServer:Password" "<password>" --project src/Apperture.ControlPlane/Apperture.Management.Host`
2. Copy `deploy/config/examples/appsettings.docker-dev.json.example` to `src/Apperture.ControlPlane/Apperture.Management.Host/appsettings.Development.json`
3. Native dev (default mode):

   ```powershell
   .\scripts\Manage.ps1 -Area dev -Command start
   ```

   Or use the interactive menu: `.\scripts\Interactive.ps1`

Do not run Aspire and native script dev at the same time — both want Management API/UI on 9000/9001. Aspire SQL defaults to host port **14333**; docker-compose/native SQL uses **1433**.

## Architecture and decisions

- Read [docs/01-architecture/](docs/01-architecture/) for control plane, instance plane, workflows, and runtime.
- Record significant decisions in [docs/02-decisions/](docs/02-decisions/) using the [ADR template](docs/02-decisions/adr-template.md).

## AI agents

Follow [AGENTS.md](AGENTS.md) and the playbooks in [.ai/playbooks/](.ai/playbooks/) (principles, anti-patterns, project map).

**Runtime-visible naming:** Do not create integrations, seeds, notifications, settings, or UI labels that embed epic/track IDs (`Epic 121`, `SDK-5A`, `SWM-10`, `BDS-3`, etc.). Keep those in docs/ADRs/tests; use product-like names in operator-facing content. See anti-patterns → “Do not put internal development metadata in runtime-visible content”.

## Pull requests

CI must pass before merge. The pipeline runs on push and pull requests to `main` (see [.github/workflows/ci.yml](.github/workflows/ci.yml)): restore, build in Release, and run all tests.
