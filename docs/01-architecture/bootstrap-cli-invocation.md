# Bootstrap CLI Invocation Model

This document describes how the Bootstrap CLI integrates with the Bootstrap Configuration and Credential Management subsystem, the invocation paths, and what is deferred.

## Invocation Paths

Bootstrap commands operate **locally** on the machine where the platform runs. Two invocation paths:

### 1. PowerShell 7+ (pwsh)

- **Preferred:** Load `Apperture.Cli.Bootstrap.dll`, build minimal DI (Bootstrap services), resolve `BootstrapCliService`, call methods directly.
- **Fallback:** If `apperture` is in PATH, invoke `apperture bootstrap <subcommand>`.
- PowerShell functions (e.g. `Get-AppertureBootstrap`, `Set-AppertureBootstrapSqlProfile`) either load the assembly or delegate to the helper.

### 2. Console helper (apperture)

- **Command:** `dotnet run --project src/Apperture.Cli/Apperture.Cli.Host -- bootstrap <subcommand>` (or `apperture bootstrap <subcommand>` when installed).
- The host parses args, resolves `BootstrapCliService`, and delegates.
- Used when pwsh cannot load Cli.Bootstrap directly (e.g. PowerShell 5.1) or when running from scripts.

## Layer Architecture

```
PowerShell functions (Get-AppertureBootstrap, Set-AppertureBootstrapSqlProfile, ...)
    |
    v
BootstrapCliService (orchestration; thin; calls Bootstrap interfaces)
    |
    v
IBootstrapCredentialManager, ISqlConnectionBootstrapProfileManager, IBootstrapConnectionProfile, ...
```

## Security Rules

- **No secrets in output** — `BootstrapCliService` never returns or logs connection strings, passwords, or credential plaintext.
- **Sensitive inputs** — Use `SecureString` for passwords; prompt securely where possible. `Set-AppertureBootstrapSqlPassword` accepts `[SecureString]`.
- **Recovery commands** — `Repair-AppertureBootstrap` and `Unlock-AppertureBootstrapRecovery` are clearly named; not routine admin.

## PowerShell Functions

| Function | Maps To | Notes |
|----------|---------|-------|
| `Get-AppertureBootstrap` | bootstrap show | No secret output |
| `Test-AppertureBootstrap` | bootstrap validate | Validation result |
| `Test-AppertureBootstrapSqlConnection` | bootstrap sql test | Connection test |
| `Set-AppertureBootstrapSqlProfile` | bootstrap set-profile | Server, Database |
| `Set-AppertureBootstrapSqlAuthMode` | bootstrap set-auth-mode | AuthMode enum |
| `Set-AppertureBootstrapSqlPassword` | bootstrap set-password | Deferred (108b); no plaintext storage |
| `Set-AppertureBootstrapSqlKeyVaultBinding` | bootstrap sql bind-keyvault-secret | Secret URI |
| `Set-AppertureBootstrapSqlManagedIdentityBinding` | bootstrap sql bind-managed-identity | |
| `Set-AppertureBootstrapSqlIntegratedSecurityBinding` | bootstrap sql bind-integrated-security | |
| `Invoke-AppertureBootstrapSqlCredentialRotation` | bootstrap rotate-sql-credential | |
| `Repair-AppertureBootstrap` | bootstrap repair | Repair path |
| `Unlock-AppertureBootstrapRecovery` | bootstrap recovery-unlock | Recovery only |

## File-backed bootstrap store (Epic 108)

Production/on-prem bootstrap **state** is persisted as non-secret JSON:

| Setting | Value |
|---------|--------|
| Default path (Windows) | `%ProgramData%\Apperture\bootstrap\configuration.json` |
| Override | `APPERTURE_BOOTSTRAP_CONFIG_PATH` (full file path) |

Contents: `Profile` (server, database, auth mode), optional `CredentialReference` (Key Vault URI, Windows Integrated, Managed Identity, etc.), optional `Version`. **No plaintext passwords.**

Implementation: [`FileBootstrapConfigurationStore`](../../src/Apperture.Bootstrap/Apperture.Bootstrap/FileBootstrapConfigurationStore.cs). Wired in CLI, Management.Host, and WorkerHost via `AddBootstrapScaffolding()`.

**Aspire local dev** does not require this file. SQL comes from Aspire-injected `ConnectionStrings:DefaultConnection` (see [management-platform-configuration.md](../03-operations/management-platform-configuration.md)).

This store is **bootstrap state only** (layer 3 per ADR-0039). It is not the platform-managed settings registry (Epic 109).

### CLI smoke (verify persistence)

Use a temp path so production `%ProgramData%` is not touched:

```powershell
$env:APPERTURE_BOOTSTRAP_CONFIG_PATH = Join-Path $env:TEMP "apperture-bootstrap-smoke.json"
Remove-Item $env:APPERTURE_BOOTSTRAP_CONFIG_PATH -ErrorAction SilentlyContinue

dotnet run --project src/Apperture.Cli/Apperture.Cli.Host -- bootstrap set-profile --server prod-sql.example --database Apperture
dotnet run --project src/Apperture.Cli/Apperture.Cli.Host -- bootstrap show
Get-Content $env:APPERTURE_BOOTSTRAP_CONFIG_PATH

Remove-Item Env:APPERTURE_BOOTSTRAP_CONFIG_PATH
```

Expected JSON shape: `Profile` (Server, Database, AuthMode, TrustServerCertificate), optional `CredentialReference`, `Version`. **No `Password` or `User` keys.** `bootstrap set-password` returns a deferred message and must not write the file.

## CLI commands (implemented vs deferred)

| Subcommand | Status |
|------------|--------|
| `bootstrap show` | Implemented |
| `bootstrap validate` | Implemented |
| `bootstrap sql test` | Implemented |
| `bootstrap set-profile --server X --database Y` | Implemented (file store) |
| `bootstrap set-auth-mode <mode>` | Implemented (file store) |
| `bootstrap bind-keyvault-secret <uri>` | Implemented (credential reference only) |
| `bootstrap bind-managed-identity` | Implemented (credential reference only) |
| `bootstrap bind-integrated-security` | Implemented (credential reference only) |
| `bootstrap set-password` | **Deferred (Epic 108b)** — returns explicit message; no plaintext storage |
| `bootstrap rotate-sql-credential` | Deferred |
| `bootstrap repair` | Deferred |
| `bootstrap recovery-unlock` | Deferred (recovery service on Management.Host only) |

## Deferred implementation

| Area | Status | Notes |
|------|--------|-------|
| `IBootstrapCredentialResolver` | Stub | Key Vault, Managed Identity, DPAPI resolution not implemented |
| Secure password binding | Deferred (108b) | `SetPasswordAsync` / DPAPI / `Apperture.Bootstrap.Windows` |
| Credential rotation | Deferred | `RotateAsync` throws with deferred message |
| `IBootstrapRecoveryAuthorizer` | Stub denies | Real MFA/proof logic out of scope |
| pwsh assembly loading | Fallback only | PowerShell delegates to `apperture` CLI helper |

## What to Build Next

**Local dev orchestration** is handled by [`Apperture.AppHost`](../../src/Apperture.AppHost/) (Epic 106) and [`Initialize-AppertureEnvironment.ps1`](../../scripts/Initialize-AppertureEnvironment.ps1) (Epic 108). Bootstrap CLI targets **production install, post-deploy update, and repair**.

1. **Epic 108b** — DPAPI `IBootstrapCredentialResolver` + `SetPasswordAsync` password binding
2. **Environment implementations** — `Apperture.Bootstrap.Windows`, `Apperture.Bootstrap.Azure`
3. **PowerShell assembly loading** — pwsh path to load Cli.Bootstrap without helper exe

## Related

- [Bootstrap and Secrets Boundary](bootstrap-and-secrets-boundary.md)
- [Tracked issues (Phase 1)](../02-decisions/tracked-issues-bootstrap-secrets-phase1.md)
