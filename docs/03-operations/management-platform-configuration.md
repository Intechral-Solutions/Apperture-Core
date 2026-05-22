# Management platform configuration

Canonical guide for **which `appsettings` files matter**, where they live, and how configuration is loaded for the core management platform (Management API, Management UI, Windows Worker).

For notification cutover flags and staging values, see [epic77-expected-run-missing-notification-cutover-runbook.md](epic77-expected-run-missing-notification-cutover-runbook.md). For bootstrap credentials and secrets boundaries, see [bootstrap-and-secrets-boundary.md](../01-architecture/bootstrap-and-secrets-boundary.md).

## Scope

**In scope**

- [Apperture.Management.Host](../../src/Apperture.ControlPlane/Apperture.Management.Host/) — management API (port 9000 in dev)
- [Apperture.Management.Ui.Server](../../src/Apperture.ControlPlane/Apperture.Management.Ui.Server/) + [Apperture.Management.Ui.Client](../../src/Apperture.ControlPlane/Apperture.Management.Ui.Client/) — Blazor UI (port 9001 in dev)
- [Apperture.RuntimeHost.Windows.WorkerHost](../../src/Apperture.RuntimeHost/Apperture.RuntimeHost.Windows/Apperture.RuntimeHost.Windows.WorkerHost/) — Windows worker runtime

**Out of scope (documented under Future work)**

- `poc/` sample hosts
- `tests/**/bin/**` and `**/bin/**` build copies
- [Apperture.Cli.Host](../../src/Apperture.Cli/Apperture.Cli.Host/) optional `appsettings.json`
- Module EF `DesignTimeDbContextFactory` optional `appsettings.json` (use Management.Host as startup project for `dotnet ef`)

## Why solution search shows ~40+ files

A full-repo file search often finds **~43** paths matching `*appsettings*`. Only a handful are intentional source; the rest are noise.

| Category | Approx. count | What it is |
|----------|---------------|------------|
| `src/` (committed + local) | 7–10 | **Edit these** (see canonical inventory) |
| `tests/**/bin/**` | 13 | Transitive copy from test project references to Management.Host and WorkerHost |
| `**/bin/**`, `**/obj/**` | 18 | Build/publish output (gitignored) |
| `tests/**/obj/**/AppSettingsSchema.json` | 2 | MSBuild JSON schema artifacts, not runtime config |
| `poc/` | 4 | Proof-of-concept hosts |
| `artifacts/` | 1 | Tooling output |

**Rule:** Only edit paths in [Canonical inventory](#canonical-inventory). Do not edit files under `tests/**/bin`, `**/bin`, or `**/obj`.

Integration tests use `WebApplicationFactory` with environment `Testing` and inject SQL via `FixedBootstrapConnectionProfile`; they do not require appsettings under `tests/`.

## Canonical inventory

| File | Host | Committed? | Purpose |
|------|------|------------|---------|
| [Management.Host/appsettings.json](../../src/Apperture.ControlPlane/Apperture.Management.Host/appsettings.json) | API :9000 | Yes | Production-safe baseline (Epic 77 safe defaults) |
| [deploy/config/examples/appsettings.docker-dev.json.example](../../deploy/config/examples/appsettings.docker-dev.json.example) | API (template) | Yes | Docker + local SQL dev overlay template |
| [deploy/config/examples/appsettings.windows.json.example](../../deploy/config/examples/appsettings.windows.json.example) | API (template) | Yes | On-premises Windows deployment skeleton |
| [deploy/config/examples/appsettings.azure.json.example](../../deploy/config/examples/appsettings.azure.json.example) | API (template) | Yes | Azure / Entra deployment skeleton |
| `Management.Host/appsettings.Development.json` | API | Gitignored | Local dev overlay (copy from docker-dev example) |
| [deploy/config/ui/appsettings.json](../../deploy/config/ui/appsettings.json) | UI (shared) | Yes | Canonical source; synced to Ui.Client `wwwroot` on build; linked into Ui.Server |
| `Ui.Client/wwwroot/appsettings.Development.json` | WASM | Gitignored | Optional split-host dev (`ApiBaseUrl` absolute) |
| [WorkerHost/appsettings.json](../../src/Apperture.RuntimeHost/Apperture.RuntimeHost.Windows/Apperture.RuntimeHost.Windows.WorkerHost/appsettings.json) | Worker | Yes | Runtime keys (`PlatformEvents`, logging) |
| `WorkerHost/appsettings.Development.json` | Worker | Gitignored | SQL server/database/user only (no `DevTestUser`) |

Legacy redirect: [Management.Host/appsettings.Development.example.json](../../src/Apperture.ControlPlane/Apperture.Management.Host/appsettings.Development.example.json) points at the docker-dev example for backward-compatible copy commands.

## Configuration loading order

Each ASP.NET Core host uses the default configuration chain (simplified):

1. `appsettings.json`
2. `appsettings.{Environment}.json` (e.g. `Development`, `Production`)
3. User secrets (Development only, Management.Host)
4. Environment variables
5. Command-line arguments

**Secrets**

- **Never** commit SQL passwords, SMTP passwords, or Entra client secrets in JSON.
- **Development:** `SqlServer:Password` in User Secrets on Management.Host; synced to `.env` as `MSSQL_SA_PASSWORD` for Docker ([Sync-EnvFromUserSecrets.ps1](../../scripts/dev/Sync-EnvFromUserSecrets.ps1)).
- **Worker dev:** password via `SqlServer__Password` or `MSSQL_SA_PASSWORD` from [start-worker.ps1](../../scripts/dev/start-worker.ps1).
- **Production:** `SqlServer:CredentialReference` + `IBootstrapCredentialResolver` (see [bootstrap-and-secrets-boundary.md](../01-architecture/bootstrap-and-secrets-boundary.md)).

## Key configuration sections

| JSON section | Bound in code | Notes |
|--------------|---------------|-------|
| `SqlServer` | `BootstrapSqlConfig` | Server, Database, User (dev); `CredentialReference` (prod) |
| `DevTestUser` | `DevTestUserOptions` | Development only |
| `BootstrapPlatformAdmin` | `BootstrapPlatformAdminOptions` | Platform admin bootstrap principals |
| `LocalAuth` | `LocalAuthOptions` | Local username/password auth |
| `Entra` | `EntraAuthOptions` | Microsoft Entra OIDC |
| `PlatformEvents` | `PlatformEventsOptions` | Event-driven notification path |
| `Notifications` | `Notification*Options` | Evaluation, instances, delivery, pipeline cutover |
| `AttentionIncidents` | `AttentionIncidentObservationOptions` | Observation loop |
| `AttentionNotifications` | `AttentionNotificationOptions` | Epic 69 expected-run-missing path |
| `ApiBaseUrl` | UI `HttpClient` / WASM | Relative `/api` when API and UI share origin |
| `ShellUi` | `ShellUiOptions` | Entra sign-in, management host URL for redirects |
| `ControlPanelMenuOptions` | `ControlPanelMenuOptions` | Menu JSON source |

## Dev setup (one story)

1. Set SQL password (once):

   ```powershell
   dotnet user-secrets set "SqlServer:Password" "<password>" --project src/Apperture.ControlPlane/Apperture.Management.Host
   ```

2. Copy the docker-dev example to the Management Host folder:

   ```powershell
   copy deploy\config\examples\appsettings.docker-dev.json.example src\Apperture.ControlPlane\Apperture.Management.Host\appsettings.Development.json
   ```

   (Equivalent: copy `appsettings.Development.example.json` in the Host folder — same content.)

3. Optional: WASM split-host dev overlay:

   ```powershell
   copy deploy\config\ui\appsettings.Development.json.example src\Apperture.ControlPlane\Apperture.Management.Ui.Client\wwwroot\appsettings.Development.json
   ```

   Or rely on [ManagementUiDevApiRouting](../../src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/Options/ManagementUiDevApiRouting.cs) when `ApiBaseUrl` is `/api` and the UI runs on loopback port 9001.

4. Optional: Worker SQL overlay (server/database/user only):

   ```powershell
   copy deploy\config\worker\appsettings.Development.json.example src\Apperture.RuntimeHost\Apperture.RuntimeHost.Windows\Apperture.RuntimeHost.Windows.WorkerHost\appsettings.Development.json
   ```

5. Start the stack: `.\scripts\Start-Dev.ps1` — see [deploy/README.md](../../deploy/README.md).

## Deployed environment

- **Baseline:** committed `Management.Host/appsettings.json` (safe production defaults).
- **Overrides:** environment variables, mounted files, or platform-specific settings (Azure App Service, Windows service install directory). Document instance-specific values outside git.
- **Topology templates:** [deploy/config/examples/](../../deploy/config/examples/) for docker-dev, Windows, and Azure starting points.

## UI configuration rules

- **Production / same-origin:** [deploy/config/ui/appsettings.json](../../deploy/config/ui/appsettings.json) sets `ApiBaseUrl` to `/api`. Edit this file; Ui.Client copies it to `wwwroot` before build. Ui.Server links it via csproj.
- **Development split-host (UI :9001, API :9000):** Ui.Server and Ui.Client use `ManagementUiDevApiRouting` to resolve `http://localhost:9000/api` when the app listens on loopback port 9001 and `ApiBaseUrl` is relative.
- **WASM `wwwroot` is authoritative** for browser-side config; keep Ui.Server aligned via the shared linked file.

## Worker configuration rules

- Worker **does not** need `DevTestUser`, `BootstrapPlatformAdmin`, or notification cutover blocks from the Management Host dev template.
- Worker needs bootstrap SQL connectivity (`SqlServer` server/database/user) and runtime flags (`PlatformEvents`, logging).
- Dev password: environment variables from dev scripts, not committed JSON.

## Topology examples

| Example file | Use when |
|--------------|----------|
| [appsettings.docker-dev.json.example](../../deploy/config/examples/appsettings.docker-dev.json.example) | Local dev with Docker SQL (`localhost,1433`) |
| [appsettings.windows.json.example](../../deploy/config/examples/appsettings.windows.json.example) | On-premises Windows instance |
| [appsettings.azure.json.example](../../deploy/config/examples/appsettings.azure.json.example) | Azure-hosted instance with Entra |

See [deploy/config/README.md](../../deploy/config/README.md) for the index.

## Future work

- **Test `bin/` copies:** Optional MSBuild `PrivateAssets` investigation to stop copying Host/Worker appsettings into test output (cosmetic IDE clutter only).
- **`poc/`:** Exclude from solution or archive; not part of the platform.
- **CLI:** `Apperture.Cli.Host` may use optional `appsettings.json` for bootstrap tooling only.
- **EF design-time:** Run migrations with Management.Host as startup project:

  ```bash
  dotnet ef database update --project src/Apperture.Persistence/Apperture.Persistence.SqlServer --startup-project src/Apperture.ControlPlane/Apperture.Management.Host
  ```
