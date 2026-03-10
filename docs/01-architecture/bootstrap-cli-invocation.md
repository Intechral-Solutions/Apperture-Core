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
| `Set-AppertureBootstrapSqlProfile` | bootstrap sql set-profile | Server, Database |
| `Set-AppertureBootstrapSqlAuthMode` | bootstrap sql set-auth-mode | AuthMode enum |
| `Set-AppertureBootstrapSqlPassword` | bootstrap sql set-password | SecureString; no echo |
| `Set-AppertureBootstrapSqlKeyVaultBinding` | bootstrap sql bind-keyvault-secret | Secret URI |
| `Set-AppertureBootstrapSqlManagedIdentityBinding` | bootstrap sql bind-managed-identity | |
| `Set-AppertureBootstrapSqlIntegratedSecurityBinding` | bootstrap sql bind-integrated-security | |
| `Invoke-AppertureBootstrapSqlCredentialRotation` | bootstrap rotate-sql-credential | |
| `Repair-AppertureBootstrap` | bootstrap repair | Repair path |
| `Unlock-AppertureBootstrapRecovery` | bootstrap recovery-unlock | Recovery only |

## Deferred Implementation (Current Scaffolding)

The following are **stubbed** or **not yet implemented**:

| Area | Status | Notes |
|------|--------|-------|
| `IBootstrapConfigurationStore` | Stub: GetAsync returns null; SaveAsync throws | bootstrap show / validate succeed with "no configuration" until a real store is wired. |
| `IBootstrapCredentialManager` | Stub throws | No credential storage or rotation. |
| `ISqlConnectionBootstrapProfileManager` | Stub throws | No profile persistence. |
| `IBootstrapCredentialResolver` | Stub | Key Vault, Managed Identity, DPAPI not implemented. |
| `IBootstrapRecoveryAuthorizer` | Stub denies | Real MFA/proof logic out of scope. |
| Environment bindings | Stub | `BindKeyVaultAsync` / `BindManagedIdentityAsync` need Azure impl; `BindIntegratedSecurityAsync` needs Windows impl. |
| Secure credential storage | Deferred | `SetPasswordAsync` must store via DPAPI or Key Vault; not in config. Requires `Apperture.Bootstrap.Windows` / `Apperture.Bootstrap.Azure`. |
| pwsh assembly loading | Fallback only | Current PowerShell functions invoke `apperture` helper; direct Cli.Bootstrap assembly loading not yet implemented. |

## What to Build Next

1. **Real `IBootstrapConfigurationStore`** — File or DB-backed; Cli.Bootstrap and Host need config path.
2. **Environment implementations** — `Apperture.Bootstrap.Windows`, `Apperture.Bootstrap.Azure`.
3. **Full `BootstrapCliService` logic** — Replace stub calls with real update/validation flow.
4. **PowerShell assembly loading** — Implement pwsh path to load Cli.Bootstrap and call `BootstrapCliService` without helper exe.

## Related

- [Bootstrap and Secrets Boundary](bootstrap-and-secrets-boundary.md)
- [Tracked issues (Phase 1)](../02-decisions/tracked-issues-bootstrap-secrets-phase1.md)
