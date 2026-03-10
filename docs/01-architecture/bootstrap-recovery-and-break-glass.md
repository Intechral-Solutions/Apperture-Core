# Bootstrap Recovery and Break-Glass

This document describes the recovery and break-glass path for bootstrap configuration repair. Break-glass is for **recovery only**; it must not become the routine path for SQL credential rotation or ordinary admin operations.

## Purpose

- **Recovery only** — Authorize and perform repair when bootstrap configuration or access is broken (e.g. credential source unreachable, wrong profile).
- **Not routine admin** — Set profile, rotate credential, bind Key Vault, and repair (validate) do **not** go through recovery authorization. Only the explicit recovery-unlock path uses break-glass.

## Supported Recovery Operations

Well-known recovery actions (see `BootstrapRecoveryAction`):

| Action | Purpose |
|--------|--------|
| `RebindCredentialSource` | Rebind a broken bootstrap credential source (e.g. new Key Vault reference). |
| `SwitchToRecoveryProfile` | Switch to a recovery profile (e.g. alternate server/database or auth mode). |
| `ReplaceLocalCredential` | Replace a locally protected bootstrap credential (e.g. DPAPI-stored). |
| `RestorePlatformAccess` | Restore platform access when primary auth is unavailable (generic recovery). |

## Flow

1. **Authorize** — All recovery flows go through `IBootstrapRecoveryAuthorizer.AuthorizeAsync(BootstrapRecoveryRequest)`.
2. **Audit** — Every recovery attempt (authorized or denied) is recorded via `IBootstrapAuditWriter.WriteRecoveryAsync(BootstrapRecoveryAuditRecord)`. No secret values in audit.
3. **Execute** — If authorized, `IBootstrapRecoveryService.ExecuteRecoveryAsync` performs the requested action (stub implementation may defer actual rebind/profile switch).

Routine bootstrap updates (set profile, rotate credential, bind Key Vault) never call the recovery authorizer. Repair (validate) does not require recovery auth.

## Security Expectations

- **Windows recovery** — Should ideally require local or service admin context in addition to break-glass authorization.
- **Azure recovery** — Should ideally require privileged Azure identity/RBAC and relevant secret/config access rights.
- **No secret values in logs or audit** — `BootstrapRecoveryAuditRecord` and logs must not contain passwords or raw secrets.
- **Auditability** — All recovery use is recorded in `BootstrapRecoveryAuditRecord` (Outcome: Authorized, Denied, Executed, Failed).

## Models

- `BootstrapRecoveryAction` — Enum of well-known recovery actions.
- `BootstrapRecoveryRequest` — ActionType, Reason, Proof; optional NewCredentialReference, RecoveryProfile (no secrets).
- `BootstrapRecoveryAuthorizationResult` — Authorized, Reason.
- `BootstrapRecoveryAuditRecord` — Timestamp, RecoveryAction, Actor, Reason, Outcome, Details (no secrets).
- `BootstrapRecoveryResult` — Success, Message.

## Related

- [Bootstrap and Secrets Boundary](bootstrap-and-secrets-boundary.md) — Bootstrap vs operational secrets; lists recovery authorizer and audit.
- [Bootstrap CLI Invocation](bootstrap-cli-invocation.md) — CLI recovery-unlock command.
