# Epic 4 Corrective Pass — Visibility and Diagnosability

## Status

Completed (narrow corrective pass). Durable recovery audit for denied and failed recovery; optional break-glass-use visibility for auth-admin mutations.

## Problem addressed

The Epic 4 audit found:

- Executed recovery was written to platform audit; denied and failed recovery attempts were only written to `IBootstrapAuditWriter`, which is stubbed in the Management host, so they were not durably persisted.
- Break-glass login success/failure was audited; use of a break-glass session beyond login (e.g. performing auth-admin operations) was not visible.

## Work Area 1 — Durable recovery audit

**Implementation:** `RealBootstrapRecoveryService` now writes recovery outcomes to **platform audit** (`IAuditService`) in addition to `IBootstrapAuditWriter`, so that all outcomes are durably visible even when the bootstrap audit writer is stubbed.

- **Denied:** After `WriteRecoveryAsync(Outcome: Denied)`, the service calls a new private method `AppendRecoveryToPlatformAuditAsync`, which creates a scope, resolves `IAuditService`, and appends a record with action `recovery.restore_platform_access.denied` and details set to the authorizer reason (e.g. "Recovery not authorized."). No proof or token is ever written.
- **Failed (target user not found):** Inside the existing execution scope, after `WriteRecoveryAsync(Outcome: Failed)`, the service appends a platform audit record with action `recovery.restore_platform_access.failed` and details "Target user not found."
- **Executed:** Unchanged; already appended `recovery.restore_platform_access.executed` with targetUserId.

All three outcomes are now persisted to the same `AuditLogs` table used by `SqlAuditService`. Actor is `"recovery"`; outcome is `"recovery"`; no secrets in details.

## Work Area 2 — Break-glass-use visibility

**Implementation:** When a break-glass session performs an auth-admin **mutation** in `UserManagementService`, the service appends a single audit record so that use of break-glass for sensitive operations is visible.

- **Mechanism:** A new private method `AppendBreakGlassUseAuditIfNeededAsync(operation, actorId, cancellationToken)` gets the current actor from `IPrincipalContext`. If the actor is a `HumanActor` with `IsBreakGlassSession == true`, it appends a platform audit record with action `auth.break_glass.admin_operation`, outcome `auth`, and details `operation={operation}`.
- **Call sites:** The method is invoked at the start of each auth-admin mutation (after the permission check): `CreateUserAsync` (operation `user.create`), `AddIdentityLinkAsync` (`identity_link.add`), `RemoveIdentityLinkAsync` (`identity_link.remove`), `AssignRoleAsync` (`role.assign`), `RemoveRoleAsync` (`role.remove`), `SetLocalPasswordAsync` (`credential.password_set`), `SetLocalCredentialDisabledAsync` (`credential.disabled` or `credential.enabled`).
- **Scope:** Only mutations in `UserManagementService` are instrumented. Reads (list users, get user, list roles) and other controllers (e.g. connections, bindings) do not emit this event. No per-request or global authorization telemetry was added.

## Work Area 3 — Documentation

- **Epic 4 summary:** Updated to add a "Corrective pass" section describing durable recovery audit and break-glass-use visibility; explicitly lists what remains deferred.
- **Bootstrap recovery doc:** Updated to state that recovery outcomes (including denied) are durably written to platform audit.
- **This file:** Implementation summary, files changed, tradeoffs.

## Files changed

- **ControlPlane.Security:** `RealBootstrapRecoveryService.cs` (platform audit for denied and failed; `AppendRecoveryToPlatformAuditAsync`). `UserManagementService.cs` (`AppendBreakGlassUseAuditIfNeededAsync`; calls from seven mutation methods).
- **Docs:** `Epic4-recovery-break-glass-implementation-summary.md` (Corrective pass section). `bootstrap-recovery-and-break-glass.md` (durable audit note). `Epic4-corrective-pass-summary.md` (new).

No migrations. No changes to bootstrap or platform audit abstractions.

## Tradeoffs and limitations

- **Recovery:** Denied and failed recovery are now visible in platform audit. `IBootstrapAuditWriter` remains stubbed in the host; recovery events are not written to a separate bootstrap-audit store. If a dedicated bootstrap audit store is added later, the service can continue to call both.
- **Break-glass:** Only auth-admin mutations in `UserManagementService` are audited when performed by a break-glass session. Other operations (e.g. connections, workflows) that a break-glass user might perform do not emit `auth.break_glass.admin_operation`. Per-request or full authorization-evaluation audit remains deferred.
- **Secrets:** No proof, token, or password is written to any audit record.

## What remains deferred

- Real `IBootstrapAuditWriter` implementation (e.g. dedicated bootstrap audit table or sink).
- Per-request or authorization-evaluation audit for the rest of the platform.
- Break-glass session use visibility for non–auth-admin operations (e.g. connections, workflows).
- Shorter session lifetime or step-up reauth for break-glass sessions.

## Related

- [Epic 4: Recovery, Break-Glass, and Security Hardening](Epic4-recovery-break-glass-implementation-summary.md)
- [Bootstrap Recovery and Break-Glass](../01-architecture/bootstrap-recovery-and-break-glass.md)
