# Integration configuration operator guide (SDK-6D)

**Audience:** Instance operators configuring integrations in Management UI  
**Related:** [Endpoint map](./integration-configuration-endpoint-map.md) · [Typed settings persistence](./integration-typed-settings-persistence.md)

## Overview

Each integration declares what it needs (settings, connections, verification policy) through its module. The **Configuration & readiness** section on Integration Detail is the starting point: it summarizes completion, lists blockers with remediation links, and shows impact on procedures and schedules.

Typical loop:

1. Open **Integration Detail** → review the configuration overview and readiness pill.
2. Fix **settings** and **secrets** on Integration Settings.
3. **Bind** compatible connections to required slots.
4. **Verify** connections where the module expects it.
5. Confirm **readiness** is green (or “ready with advisories”).
6. **Launch** procedures or enable **schedules**.

## Configuration overview

The overview loads module declarations (`GET /api/integrations/{id}/configuration/declarations`) and compares them to current settings, bindings, and readiness from the Management API.

| Tile | Meaning |
| --- | --- |
| Settings | Required declared settings that have a value or configured secret |
| Connections | Required connection slots with an active binding |
| Verification | Bound connections and their last test-connect status |
| Procedures | How many declared procedures are launchable |
| Schedules | Enabled schedules and whether dispatch would be blocked by readiness |

**Empty state:** Modules with no required settings or connections (e.g. HelloWorld) show “No configuration required.”

**Remediation:** Each readiness issue includes a link to Integration Settings (for settings/secrets), the connection requirement anchor on Detail (for bindings), or the connection detail page (for connection health).

## Typed setting editing

Integration Settings lists integration-scoped platform settings. Non-secret values are edited inline; logical kinds (boolean, integer, decimal, URI, duration, time zone) are validated on save.

- Required settings must have a value before readiness passes.
- Invalid persisted values produce blocking readiness issues (`invalid_setting`); fix the value on Settings and refresh Detail.

See [integration-typed-settings-persistence.md](./integration-typed-settings-persistence.md) for how values are stored and validated.

## Secret lifecycle

Secrets are **never** shown or returned as plaintext in the UI or Management API.

| Operator state | Meaning |
| --- | --- |
| Not configured | Required secret has no reference |
| Configured | Secret reference exists and resolves |
| Invalid reference | Reference is present but does not resolve |
| Unavailable | Secret store could not be queried |

**Set / replace:** Use the secret editor on Integration Settings. Saving stores a secret reference only.

**Clear:** Clearing a required secret triggers confirmation. After clear, readiness shows `missing_secret` or `invalid_secret_reference` until a new secret is set.

**Replace:** Enter a new value to replace the stored reference; the previous reference is superseded.

Presence labels come from `IsSecretConfigured` on resolved setting rows — not from `CurrentValue` for sensitive fields.

## Connection binding

Each connection requirement card on Integration Detail shows:

- Operator-facing capability label (HTTP API, relational database, SFTP) — not the CLR contract type as primary text.
- Whether a slot is bound, inactive, or missing.
- Procedures that depend on the slot.

**Bind:** Choose an eligible connection from the slot’s bind action (uses eligible-connections API).

**Create compatible connection:** When no connection fits, use create-connection navigation with return URL back to the integration.

**Remediation codes:** `missing_slot_binding`, `inactive_binding`, `binding_contract_mismatch`, `binding_not_eligible`, and related connection codes link to the requirement card or connection detail.

## Verification

Modules declare a verification expectation per connection (`required`, `advisory`, or `none`).

- Run **Test connect** from the connection detail page or binding flow.
- Failed verification surfaces as `connection_verification_failed` (blocking when required).
- Never tested shows as advisory or blocking depending on module policy.
- Unsupported connectors show `connection_verification_unavailable` — fix credentials manually; automated verify may not apply.

The overview **Verification** tile summarizes failed vs unverified bound connections.

## Readiness reasons and remediation

Readiness is computed server-side (`GET /api/integrations/{id}/readiness`). The UI maps issue codes to operator copy and links — it does not re-evaluate readiness locally.

| Category | Example codes | Where to fix |
| --- | --- | --- |
| Settings | `missing_setting`, `invalid_setting` | Integration Settings → setting key |
| Secrets | `missing_secret`, `invalid_secret_reference` | Integration Settings → secret field |
| Bindings | `missing_slot_binding`, `inactive_binding`, … | Integration Detail → requirement card |
| Verification | `connection_unverified`, `connection_verification_failed` | Connection detail → test connect |
| Module | `integration_not_active`, `module_not_installed` | Integration state / module install |
| Schedule | `schedule_ownership_mismatch` | Schedules section on Detail |

**Blocking vs advisory:** Blocking issues prevent runnable status. Advisory issues may still allow launch with a warning when procedure policy permits.

**Procedure-scoped readiness:** `GET .../procedures/{procedureId}/readiness` evaluates only that procedure’s requirement subset — use when one procedure is blocked but others are fine.

## Procedure launchability

The procedures section shows each declared procedure with launchable flag and blocked reason codes.

- **Ready to launch:** Integration runnable and procedure requirements satisfied.
- **Runnable with advisories:** Launch allowed but advisory readiness issues exist.
- **Blocked:** Hard blockers (e.g. `integration_hard_blocked`, missing required connection for that procedure).

Launch uses `POST .../procedures/{procedureId}/launch`, which enforces the same readiness gates as the UI. Error messages are mapped through operator helpers — they do not include secret material.

Use **Execute** navigation from a procedure row when you need the full launch form with parameters.

## Schedule blocked-by-readiness

Schedules can be **enabled** in configuration while dispatch is still blocked if the integration is not runnable.

| Schedule pill | Meaning |
| --- | --- |
| Disabled | Schedule is off — no dispatch attempts |
| Blocked by configuration | Enabled but integration readiness blocks dispatch |
| Ready | Enabled and integration is runnable |
| Ready pending check | Enabled; readiness not loaded in list context |

On **Schedule detail**, last dispatch outcome may show `BlockedByReadiness` with a safe reason string (issue codes, not secrets).

**Fix path:** Resolve blocking readiness on the integration, then confirm on Schedule detail that the next due fire can dispatch. Dispatch enforcement is server-side in the schedule runner — the UI reflects it but does not bypass it.

## API and automation

Operators using CLI or automation should follow the same endpoint sequence documented in [integration-configuration-endpoint-map.md](./integration-configuration-endpoint-map.md). `IManagementClient` mirrors the main read/write routes including declarations and readiness.

## Sample module (ApiPolling)

Production readiness requires a bound `DirectoryApi` connection. Embedded fake directory data is opt-in only (`SampleApiPolling:AllowEmbeddedFallback`) and does **not** satisfy readiness. Use ApiPolling as a reference for connection-backed modules, not as a production shortcut.
