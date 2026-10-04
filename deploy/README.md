# Deploy

Deployment configuration.

| Folder | Purpose |
|--------|---------|
| [config/](config/) | Canonical `appsettings` templates (docker-dev, Windows, Azure) and shared UI config — see [config/README.md](config/README.md) |
| docker/ | docker-compose.dev.yml, env files, SQL init/migrations |
| k8s/ | Not present. Kubernetes is outside RC1 support |
| azure/ | Not present. Azure is outside RC1 support |

> **RC1 note (2026-10-03).** The RC1-supported production topology is Windows Server, SQL Server and three hosts as Windows Services. It is delivered by the OPS-1 epic and does not exist yet: there is no installer, no service hosting and no supported upgrade or backup procedure today. See [rc1-program-gate.md](../docs/01-architecture/rc1-program-gate.md).

Management platform configuration reference: [docs/03-operations/management-platform-configuration.md](../docs/03-operations/management-platform-configuration.md) (environment/startup layer). Configuration boundaries: [ADR-0039](../docs/02-decisions/ADR-0039-environment-startup-bootstrap-and-platform-settings.md). Foundation smoke baseline: [Epic 110](../docs/03-operations/epic110-foundation-stabilization.md).

**Epic 106 (Aspire)** delivers local orchestration via [`Apperture.AppHost`](../../src/Apperture.AppHost/). **Epic 108** adds the file-backed bootstrap store for Windows/on-prem install/repair. Thin dev setup: [`scripts/Initialize-AppertureEnvironment.ps1`](../scripts/Initialize-AppertureEnvironment.ps1).

## Bootstrap configuration store (Windows / on-prem)

Production bootstrap **state** (non-secret SQL profile + credential references) is persisted to:

| Setting | Value |
|---------|--------|
| Default path | `%ProgramData%\Apperture\bootstrap\configuration.json` |
| Override | `APPERTURE_BOOTSTRAP_CONFIG_PATH` (full file path) |

JSON contains **no plaintext passwords**. Use `apperture bootstrap set-profile`, `bind-integrated-security`, etc. Password binding (DPAPI) is deferred — see [bootstrap-cli-invocation.md](../docs/01-architecture/bootstrap-cli-invocation.md).

Aspire local dev does **not** require this file; SQL comes from Aspire-injected `ConnectionStrings:DefaultConnection`.

## Local development: Aspire (preferred)

From the repo root (requires Docker):

```powershell
dotnet run --project src/Apperture.AppHost/Apperture.AppHost.csproj
```

The AppHost orchestrates:

| Resource | Port / detail |
|----------|----------------|
| SQL Server (Aspire container, `2022-latest`, volume `apperture-aspire-sql`) | Host port from `Aspire:SqlServer:HostPort` in AppHost `appsettings.json` (default **`localhost:14333`**) |
| Management API (`Apperture.Management.Host`) | `http://localhost:9000` |
| Management UI (`Apperture.Management.Ui.Server` + Blazor WASM client) | `http://localhost:9001` (HTTP only) |
| Worker runtime (`Apperture.RuntimeHost.Windows.WorkerHost`) | background worker — timer schedule dispatch + start-request execution |
| Aspire dashboard | `https://localhost:17173` (HTTPS) or `http://localhost:15235` — **open the tokenized login URL printed by AppHost**, not the bare origin |

**Scheduling (SWM-1):** AppHost sets `APERTURE_RUNTIME_ID=worker-local` (claim/host id) and `ScheduleDispatch__RuntimePlaneId=local` (must match UI/API assignment defaults). WorkerHost owns the timer; Management owns CRUD + optional manual `run-cycle`. Both hosts use the same Aspire `DefaultConnection` SQL. Verify: Worker logs `Plane=local HostId=worker-local`, then an enabled schedule produces `ScheduleRuns` / run history within one poll interval (≥15s) without manual run-cycle. Details: [Epic SWM-1](../docs/02-decisions/Epic-SWM1-Runtime-Plane-Identity-Alignment.md).

**Connection string precedence (Development):** `ConnectionStrings:DefaultConnection` (Aspire) wins over file bootstrap store and `SqlServer:*` in `BootstrapConnectionProfile`.

After AppHost starts, the console prints a line like `Login to the dashboard at https://localhost:17173/login?t=...`. Use that URL. The Management UI is **not** on port 17173; do not use `https://localhost:9001` (no TLS listener on 9001).

**First-time (both Aspire and script paths):** run `.\scripts\Initialize-AppertureEnvironment.ps1` or copy the docker-dev appsettings template manually:

```powershell
copy deploy\config\examples\appsettings.docker-dev.json.example src\Apperture.ControlPlane\Apperture.Management.Host\appsettings.Development.json
```

Aspire injects `ConnectionStrings:DefaultConnection` for SQL; that connection **takes precedence** over `SqlServer:Server` / `User` / `Password` in `appsettings.Development.json` when both are present. The appsettings overlay is still required for auth/bootstrap dev flags (`DevTestUser`, `BootstrapPlatformAdmin`, feature toggles) — but **not** for choosing the SQL server under AppHost.

**Separate SQL volumes:** Aspire (`apperture-aspire-sql`) and docker-compose (`apperture-sqlserver`) are independent. Dev users/passwords created on one path do not exist on the other. After switching to Aspire, sign in with the credentials from `DevTestUser` in your `appsettings.Development.json` (template default username/password). To wipe the Aspire database, remove the `apperture-aspire-sql` Docker volume and restart AppHost (migrations and dev bootstrap run again on Management.Host startup).

**Mutual exclusion:** Do not run Aspire and native script dev at the same time — Management API/UI both want **9000/9001**. Aspire SQL uses host port **14333** by default; the script/docker-compose path uses **1433**. Aspire uses its own SQL container/volume (`apperture-aspire-sql`); the script path uses docker-compose (`apperture-sqlserver`).

## Root Docker Compose shim (script path)

From the **repo root** you can control all Compose-defined services with a single interface:

```bash
docker compose up -d
docker compose down
docker compose ps
```

The root [docker-compose.yml](../docker-compose.yml) uses Compose `include` to pull in nested compose files under `deploy/docker/`. Add or remove entries in that `include` list as you add more compose files (e.g. `docker-compose.dev.yml`, `docker-compose.e2e.yml`).

## Dev environment: SQL Server (script path)

The script-based dev stack includes SQL Server for EF Core. **User Secrets are the single source of truth** for the SQL password on this path; `.env` is generated from User Secrets for Docker Compose.

### Quick start (script path)

```powershell
.\scripts\Manage.ps1 -Area dev -Command start
```

This syncs User Secrets to `.env`, starts SQL Server (Docker), and launches the API (Management.Host), UI, and WorkerHost. Use `-Build` to build before start. Use `-Mode aspire` for AppHost orchestration instead.

Management does **not** start/stop/restart WorkerHost via Recovery APIs (`runtime-worker/start` is quarantined — 410). Prefer AppHost or Manage.ps1. See [Worker-Hosting-Guidance-v1.md](../docs/03-operations/Worker-Hosting-Guidance-v1.md).

### First-time setup

1. **Set the SQL password** (one-time; stored in User Secrets, never committed):

   ```powershell
   dotnet user-secrets set "SqlServer:Password" "YourStrong!Passw0rd" --project src/Apperture.ControlPlane/Apperture.Management.Host
   ```

2. **Copy the docker-dev appsettings template** (one-time) for Server, Database, User (password comes from User Secrets):

   ```powershell
   copy deploy\config\examples\appsettings.docker-dev.json.example src\Apperture.ControlPlane\Apperture.Management.Host\appsettings.Development.json
   ```

3. Run `.\scripts\Manage.ps1 -Area dev -Command start` (or `.\scripts\Interactive.ps1`)

### How it works

- **User Secrets** stores `SqlServer:Password`. The .NET app reads it via Configuration.
- **Manage.ps1** native start syncs secrets to `.env`, runs `docker compose up -d`, then starts Management.Host, the UI Server, and WorkerHost.

### Stopping

- `.\scripts\Manage.ps1 -Area dev -Command stop` stops the API, UI, and worker processes on ports 9000 and 9001.
- `docker compose down` stops SQL Server.

### Dev test user (local login)

In Development, the **DevTestUser** bootstrap ensures a configured test user exists with the platform administrator role. Add this to `appsettings.Development.json`:

```json
{
  "DevTestUser": {
    "Enabled": true,
    "Username": "tenny",
    "DisplayName": "Tenny (Dev)",
    "Password": "dev"
  },
  "LocalAuth": {
    "Enabled": true
  }
}
```

Then log in at `POST /api/auth/local/login` with `tenny` / `dev` (or your chosen password). The user will have full access to modules, workflows, runs, and the rest of the platform.

### Run migrations

When EF Core migrations exist:

```bash
dotnet ef database update --project src/Apperture.Persistence/Apperture.Persistence.SqlServer --startup-project src/Apperture.ControlPlane/Apperture.Management.Host
```

The Management Host applies migrations itself at startup, and that is the supported path for a database that already has users. Between two of the migrations it runs a data step that gives every user a username. `dotnet ef database update` cannot run that step: on a database with users it stops with a message asking you to start the Management Host, which then completes the upgrade. An empty database migrates fully either way.

See [docs/00-overview/roadmap.md](../docs/00-overview/roadmap.md).
