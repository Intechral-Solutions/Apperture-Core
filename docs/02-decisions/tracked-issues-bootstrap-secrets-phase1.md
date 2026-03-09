# Tracked Issues: Bootstrap and Secrets (Phase 1)

Issues deferred from the Phase 1–3 scaffolding. Address before or during later phases.

## ICredentialSource

- **Status:** Deferred
- **Description:** `ICredentialSource.TryGetConfig()` returns `BootstrapSqlConfig` (which includes User/Password). Not wired in DI; not used by `BootstrapConnectionProfile`. Either wire for a specific use case or deprecate in favor of `IBootstrapCredentialReference` + `IBootstrapCredentialResolver`.
- **Risk:** Naming conflates "credential source" with raw config; could lead to misuse if wired later.

## DesignTimeDbContextFactory

- **Status:** Deferred
- **Description:** Reads `DefaultConnection` and `Apperture__ConnectionString` directly, bypassing bootstrap. Acceptable for EF migrations tooling when no host is running. Document that this is design-time only and not the runtime pattern.
- **Location:** `src/Apperture.Persistence/Apperture.Persistence.SqlServer/DesignTimeDbContextFactory.cs`

## DbSecretStore vs SqlSecretStore

- **Status:** Deferred
- **Description:** `Core.Security.Secrets` has placeholder `DbSecretStore`; `Persistence.SqlServer` has real `SqlSecretStore`. Naming may cause confusion. Consider deprecating `DbSecretStore` or clarifying roles.
- **Risk:** Low; compile-time clarity only.

## Bootstrap persistence and rotation

- **Status:** Deferred
- **Description:** `IBootstrapConfigurationStore` and `IBootstrapCredentialManager` are stubs. No storage for profile + credential reference. Post-deploy rotation and validation-before-commit require real implementations.
- **Phase:** Implement with Prompt 4 or later.

## CLI ↔ Bootstrap integration

- **Status:** Deferred
- **Description:** `Cli.Core` does not reference `Bootstrap`. CLI bootstrap commands require either (a) adding Bootstrap reference and local bootstrap config handlers, or (b) exposing bootstrap config via Management API. Define the seam before implementing CLI bootstrap commands.
- **Phase:** Before CLI bootstrap feature work.
