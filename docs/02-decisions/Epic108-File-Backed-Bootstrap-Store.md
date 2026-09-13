# Epic 108 — File-backed bootstrap configuration store

**Status:** Delivered (DPAPI password binding deferred to **108b**)

**Context:** Epic 105B and 107 defined bootstrap as layer 3 but left persistence as design-only. Operators needed non-secret SQL profile + credential **references** outside `appsettings` without conflating bootstrap with the platform settings registry.

## Decision

Persist bootstrap **state** as non-secret JSON on the host:

| Setting | Value |
|---------|--------|
| Default path (Windows) | `%ProgramData%\Apperture\bootstrap\configuration.json` |
| Override | `APPERTURE_BOOTSTRAP_CONFIG_PATH` |

Store `Profile` (server, database, auth mode) and optional `CredentialReference` (Key Vault URI, integrated security, managed identity, etc.). **No plaintext passwords** in the file.

Wire via `FileBootstrapConfigurationStore` and `AddBootstrapScaffolding()` in CLI, Management.Host, and WorkerHost. Expose CLI `bootstrap set-profile` / `show` and thin [`Initialize-AppertureEnvironment.ps1`](../../scripts/Initialize-AppertureEnvironment.ps1).

## Delivered

- [`FileBootstrapConfigurationStore`](../../src/Apperture.Bootstrap/Apperture.Bootstrap/FileBootstrapConfigurationStore.cs)
- [bootstrap-cli-invocation.md](../01-architecture/bootstrap-cli-invocation.md) — invocation paths, PowerShell function map, smoke commands
- CLI persistence for SQL profile and credential reference metadata
- Clear separation from platform settings (Epic 109): bootstrap is install/first-run state, not operator steady-state

## Deferred (108b and later)

| Item | Notes |
|------|-------|
| **108b — DPAPI password binding** | Windows on-prem; `Set-AppertureBootstrapSqlPassword` stubbed |
| Azure Key Vault / Managed Identity binding | Production path |
| First-run setup wizard | After credential binding |
| Merging Aspire and docker-compose SQL volumes | Not planned |

## Non-goals

- Platform-managed settings storage
- Writing bootstrap state from platform settings API
- Replacing Aspire-injected connection strings for local dev

## References

- [ADR-0039](./ADR-0039-environment-startup-bootstrap-and-platform-settings.md)
- [bootstrap-and-secrets-boundary.md](../01-architecture/bootstrap-and-secrets-boundary.md)
- [Epic 109](./Epic109-Platform-Settings-Registry-Phase1.md)
