# Local development — Management host

## `tenny` and Platform Administrator

The committed **`appsettings.Development.example.json`** already wires the **`tenny`** local user for admin:

1. **`DevTestUser`** — In **Development** only, creates/updates the dev user and ensures **`role-platform-administrator`** is assigned (plus local password if configured).
2. **`BootstrapPlatformAdmin`** — On every host start (any environment if enabled), ensures listed principals have Platform Administrator; for Local auth, **`ExternalId`** is **trim + lowercase** (same as login).

Because **`appsettings.Development.json` is gitignored**, you must have your own file next to this README.

### First-time setup

From this directory:

```powershell
Copy-Item appsettings.Development.example.json appsettings.Development.json
```

Then edit `appsettings.Development.json` if needed (SQL password, etc.) and **restart the Management host** so hosted bootstrap services run.

### If you already have `appsettings.Development.json`

Merge in these sections from the example (or ensure equivalent values):

- **`DevTestUser`**: `Enabled: true`, `Username: "tenny"` (and `Password` if you use local login).
- **`BootstrapPlatformAdmin`**: `Enabled: true`, `Principals` with `IdentitySource: "Local"`, `ExternalId: "tenny"` (any casing is fine; it is normalized).

### Checklist if `tenny` still has no governance access

- Host runs with **`ASPNETCORE_ENVIRONMENT=Development`** (so `DevTestUser` bootstrap runs).
- **`LocalAuth:Enabled`** is `true` in Development settings (see example).
- You sign in with the **normalized** username (`tenny` → stored as `tenny`).
- Restart after config changes.

### Entra (Microsoft ID) instead of Local

`DevTestUser` does not apply. Set **`BootstrapPlatformAdmin`** with **`IdentitySource": "Entra"`** and **`ExternalId`** to the user’s **`oid` or `sub` from the token** (not email). Restart the host.

### Assign the role yourself (another admin)

If you already have a user with **`role.assign`**, use the API: assign role id **`role-platform-administrator`** to that user’s id via the governance/users endpoints (see `UsersController.AssignRole`). If no admin exists, use config bootstrap or platform recovery flows documented elsewhere.
