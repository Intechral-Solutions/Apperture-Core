# Management platform configuration

Canonical guide for **environment/startup configuration**: which `appsettings` files matter, where they live, and how they are loaded for the core management platform (Management API, Management UI, Windows Worker).

**This document is not the operator settings control plane.** Phase 1 platform-managed settings use the [platform settings registry](../01-architecture/platform-settings-registry.md) and are edited in Management UI at `/config/platform-settings` (operator UX: Epic 111; Epic 112 adds `StringArray` / MultiSelect foundation). `appsettings` still provides boot defaults. API `PUT` accepts `value` for scalar settings and `values` (string array) for `StringArray` settings; neither writes back to JSON files.

For notification cutover flags and staging values, see [epic77-expected-run-missing-notification-cutover-runbook.md](epic77-expected-run-missing-notification-cutover-runbook.md). For bootstrap credentials and secrets boundaries, see [bootstrap-and-secrets-boundary.md](../01-architecture/bootstrap-and-secrets-boundary.md).

## Configuration layers (summary)

| Layer | This doc? | Where to read |
|-------|-----------|---------------|
| **Environment / startup** | **Yes** — file inventory below | This document |
| **Secrets / credentials** | Partial — loading order only | [ADR-0039](../02-decisions/ADR-0039-environment-startup-bootstrap-and-platform-settings.md), [bootstrap-and-secrets-boundary.md](../01-architecture/bootstrap-and-secrets-boundary.md) |
| **Bootstrap state** | Mentioned in dev templates | [ADR-0039](../02-decisions/ADR-0039-environment-startup-bootstrap-and-platform-settings.md) |
| **Platform-managed settings** | Phase 1 — API/UI (not appsettings) | [platform-settings-registry.md](../01-architecture/platform-settings-registry.md), [epic110-foundation-stabilization.md](epic110-foundation-stabilization.md) |

**Epic 105A** delivered this startup/template inventory. **Epic 106 (Aspire)** added [`Apperture.AppHost`](../../src/Apperture.AppHost/) and [`Apperture.ServiceDefaults`](../../src/Apperture.ServiceDefaults/) for local orchestration and conventional health/OTEL/service-discovery defaults.

**Rule:** Edit `appsettings` and [deploy/config](../../deploy/config/) templates for **install, dev, and boot defaults** — not for steady-state operator changes. The platform must not write managed settings back to JSON files.

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

**Rule:** Only edit paths in [Canonical inventory (environment / startup layer)](#canonical-inventory-environment--startup-layer). Do not edit files under `tests/**/bin`, `**/bin`, or `**/obj`.

Integration tests use `WebApplicationFactory` with environment `Testing` and inject SQL via `FixedBootstrapConnectionProfile`; they do not require appsettings under `tests/`.

## Canonical inventory (environment / startup layer)

Epic 105A file inventory — **startup, template, and boot-default material only**:

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
- **Worker dev:** password via `SqlServer__Password` or `MSSQL_SA_PASSWORD` (User Secrets / `.env`). Prefer Aspire AppHost or Manage.ps1 — not Management `runtime-worker/start` (quarantined SWM-9a). See [Worker-Hosting-Guidance-v1.md](Worker-Hosting-Guidance-v1.md).
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

5. Start the stack:
   - **Preferred (Aspire):** `dotnet run --project src/Apperture.AppHost/Apperture.AppHost.csproj`
   - **Alternate (scripts):** `.\scripts\Start-Dev.ps1` — see [deploy/README.md](../../deploy/README.md).

## Local orchestration (Epic 106)

| Path | Entry | SQL | Hosts started |
|------|-------|-----|---------------|
| **Aspire (preferred)** | `dotnet run --project src/Apperture.AppHost/Apperture.AppHost.csproj` | Aspire-managed container (`apperture-aspire-sql` volume) | Management.Host, Ui.Server, WorkerHost |
| **Scripts (alternate)** | `.\scripts\Start-Dev.ps1` | docker-compose (`apperture-sqlserver`) | Management.Host, Ui.Server, WorkerHost |

**What Aspire injects:** `ConnectionStrings:DefaultConnection` (SQL) via AppHost references. When present, it **overrides** `SqlServer:Server` / `User` / `Password` in `appsettings.Development.json` so Management.Host and WorkerHost share the same Aspire database. Hosts do **not** require Aspire to run — `AddServiceDefaults()` in [`Apperture.ServiceDefaults`](../../src/Apperture.ServiceDefaults/) registers passive health/OTEL/service-discovery that also works under direct `dotnet run`.

**Separate SQL volumes:** Aspire (`apperture-aspire-sql`) and docker-compose (`apperture-sqlserver`) are independent. Dev users/passwords are not shared between paths. Under AppHost, `DevTestUser` / `BootstrapPlatformAdmin` in `appsettings.Development.json` seed the **Aspire** database (Management.Host runs migrations on startup; WorkerHost waits for the API).

**What still comes from appsettings templates:** `DevTestUser`, `BootstrapPlatformAdmin`, notification cutover flags, UI shell config. Copy [`appsettings.docker-dev.json.example`](../../deploy/config/examples/appsettings.docker-dev.json.example) once for Management.Host regardless of orchestration path.

**Ui.Client:** Blazor WASM bundle served by Ui.Server — not a separate AppHost process. Split-host API routing uses [`ManagementUiDevApiRouting`](../../src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/Options/ManagementUiDevApiRouting.cs) (:9001 → :9000).

**WorkerHost:** `AddServiceDefaults()` only (no HTTP health endpoints); optional [`worker/appsettings.Development.json.example`](../../deploy/config/worker/appsettings.Development.json.example) for logging/SQL overlay when not using Aspire connection injection.

**Mutual exclusion:** Do not run both paths simultaneously (ports 1433, 9000, 9001).

## Bootstrap file store (Epic 108)

Windows/on-prem **bootstrap state** (layer 3 per ADR-0039) is persisted separately from appsettings:

| Setting | Value |
|---------|--------|
| Default path | `%ProgramData%\Apperture\bootstrap\configuration.json` |
| Override | `APPERTURE_BOOTSTRAP_CONFIG_PATH` |

JSON holds non-secret SQL profile + credential **references** only. Configure via `apperture bootstrap set-profile`, `bind-integrated-security`, etc. See [bootstrap-cli-invocation.md](../01-architecture/bootstrap-cli-invocation.md).

### SQL connection precedence (Management.Host / WorkerHost)

| Priority | Source | Notes |
|----------|--------|-------|
| 1 | `ConnectionStrings:DefaultConnection` | Development only; Aspire/scripts injection **wins over file store** |
| 2 | File bootstrap store | `configuration.json` profile + credential reference |
| 3 | `SqlServer` appsettings / User Secrets | Dev script path |
| 4 | LocalDB default | Development fallback |

Aspire local dev does **not** require `configuration.json`. Empty Integrations/Connections on a fresh Aspire DB is expected operator data state, not a bootstrap failure.

### Fresh Aspire DB verification (diagnostics)

Use SQL to confirm **identity/bootstrap seed** and **empty operator data**, not connector-definition catalog rows.

| Check | Expected on fresh Aspire DB |
|-------|----------------------------|
| `__EFMigrationsHistory` row count | All migrations applied (Management.Host runs `MigrateAsync` on startup) |
| `Users` / `IdentityLinks` / `UserRoles` | Bootstrap admin from `BootstrapPlatformAdmin` + `DevTestUser` in `appsettings.Development.json` |
| `Connections` | **0** — created by operators via Management UI/API |
| `Integrations` | **0** — created when modules are deployed/registered |

**Do not query `ConnectorDefinitions`.** That table was removed in Epic 53 ([Epic53-Registry-Only-Connector-Definitions](../02-decisions/Epic53-Registry-Only-Connector-Definitions.md)). Connector definitions live in **`IConnectorDefinitionRegistry`** at host startup (well-known platform SQL + connector-package contributors such as Campus Nexus and SFTP). `Connection.ConnectorDefinitionId` is a logical reference to those registry ids, not a foreign key to a SQL catalog.

To verify the catalog at runtime (after Management.Host is up), call `GET /api/connector-definitions` with an authenticated user that has `connector.definition.read` (platform admin on dev seed). An empty `Connections`/`Integrations` result with HTTP 200 and a non-empty connector-definition list is normal. See [connector-resource-family](../01-architecture/connector-resource-family.md).

**Thin dev setup:** [`scripts/Initialize-AppertureEnvironment.ps1`](../../scripts/Initialize-AppertureEnvironment.ps1) copies appsettings template and prints startup commands. Does not create bootstrap `configuration.json`.

**Epic 108 follow-on (unchanged):** DPAPI credential binding (Epic 108b), setup wizard, production installer — not merging Aspire/docker-compose SQL volumes. Platform settings registry Phase 1 was delivered in **Epic 109** (see [epic110-foundation-stabilization.md](epic110-foundation-stabilization.md)).

## Deployed environment

- **Baseline:** committed `Management.Host/appsettings.json` (safe production defaults).
- **Overrides:** environment variables, mounted files, or platform-specific settings (Azure App Service, Windows service install directory). Document instance-specific values outside git.
- **Topology templates:** [deploy/config/examples/](../../deploy/config/examples/) for docker-dev, Windows, and Azure starting points.
- **Public hostnames / Entra redirects:** not wired yet. Launch notes: [deferred-public-origin-auth-redirects.md](deferred-public-origin-auth-redirects.md).

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

## Deferred work and roadmap

Foundation sequence **105A–109** is complete. Current smoke baseline, deferred items, and recommended next epic: **[epic110-foundation-stabilization.md](epic110-foundation-stabilization.md)**.

Historical post-Aspire assessment (pre-108/109): [epic107-post-aspire-assessment.md](epic107-post-aspire-assessment.md). Open items from [ADR-0039 post-Aspire checklist](../02-decisions/ADR-0039-environment-startup-bootstrap-and-platform-settings.md#post-aspire-revisit-checklist):

- DPAPI / Key Vault credential binding (Epic 108b)
- First-run setup wizard
- Full migration of remaining `IOptions` flags to registry-backed settings
- Platform settings UI polish and expanded runtime overlays (**Epic 111** recommended)

**Cosmetic / tooling (still valid):**

- **Test `bin/` copies:** Optional MSBuild `PrivateAssets` investigation to stop copying Host/Worker appsettings into test output (IDE clutter only).
- **`poc/`:** Exclude from solution or archive; not part of the platform.
- **CLI:** `Apperture.Cli.Host` may use optional `appsettings.json` for bootstrap tooling only.
- **EF design-time:** Run migrations with Management.Host as startup project:

  ```bash
  dotnet ef database update --project src/Apperture.Persistence/Apperture.Persistence.SqlServer --startup-project src/Apperture.ControlPlane/Apperture.Management.Host
  ```

**Platform settings (Phase 1 delivered):** [platform-settings-registry.md](../01-architecture/platform-settings-registry.md). Instance UI: `/config/platform-settings` (category tabs; Epic 120 demo gallery). Integration Detail UI: `/integrations/{integrationId}/settings` (Epic 119). Option providers and scoped settings: [platform-settings-option-providers-and-scopes.md](../01-architecture/platform-settings-option-providers-and-scopes.md) and [integration-scoped-settings.md](../01-architecture/integration-scoped-settings.md).

**Development-only — settings input gallery (Epic 120):** On the Management host, demo settings (`demo.settings.*`) appear when `ASPNETCORE_ENVIRONMENT=Development` or when `PlatformSettings:DemoGallery:Enabled` is `true` in configuration. They are not production controls and are not registered on WorkerHost. Remove by deleting `PlatformSettingDemoDefinitionRegistry` and the gated registry merge.
