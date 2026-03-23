# Epic 43 — Management UI sign-in gate and auth UI policy

## Status

Accepted (implemented).

## Context

The Blazor WASM management UI used passive sign-in (top bar only) and static `ShellUi:UseEntraSignIn` to choose between local and Entra on `/login`. Operators needed:

- Automatic routing of unauthenticated users into a deliberate sign-in experience.
- Runtime-merged **capabilities** (local enabled, Entra configured) with **persisted chooser policy** (show local on primary page, prefer Entra, optional auto-redirect).
- A recovery path (`/login/local`) when local is hidden from the primary chooser but still enabled at the host.

## Decision

1. **Persistence**: Single-row table `InstanceAuthUiPolicies` (`Id = instance`) with `ShowLocalSignInInPrimaryChooser`, `PreferEntraInChooser`, `AutoRedirectToEntra`, and SQL `rowversion` for optimistic concurrency.

2. **API**:
   - `GET /api/auth/sign-in-options` (`[AllowAnonymous]`) returns merged capabilities + effective chooser flags via `AuthSignInOptionsMerger`.
   - `GET` / `PATCH /api/config/auth-ui-policy` (`[Authorize]`) requires `auth.ui_policy.manage` on `InstanceConfiguration` / `instance` scope.

3. **Client**: `AuthRouteGate` wraps the router, blocks protected routes until session is resolved, redirects unauthenticated users to `/login?returnUrl=…`, and keeps `/login` and `/login/local` public. `AuthSignInOptionsService` caches `sign-in-options` for chooser UX.

4. **Host (dev)**: OIDC `OnRemoteFailure` redirects to `http://localhost:9001/login?error=oidc` in Development so the WASM dev server surfaces failures.

## Consequences

- New EF migrations must be applied before the API can read policy (`Get` throws if the row is missing).
- Platform Administrator role gains `auth.ui_policy.manage` via data migration.
- Operator capabilities DTO gains `CanManageAuthUiPolicy` for UI gating.
