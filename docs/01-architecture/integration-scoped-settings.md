# Integration-scoped settings and Integration Detail settings UI

## Status

**Epic 117 — design (delivered).** This document is the canonical model for integration-scoped settings.

**Epic 118 — backend/API foundation (delivered).** Integration value scope is implemented: scoped repository, `IntegrationSettingsResolver` / `IntegrationSettingsService`, explicit routes under `/api/integrations/{integrationId}/settings`, integration-only authorization (`integration.read` / `integration.update`), audit actions `integration.settings.set` / `integration.settings.reset`, and management client methods. Production integration setting keys remain empty until Epic 121.

**Epic 119 — Integration Detail settings UI (delivered).** Management UI at `/integrations/{integrationId}/settings` reuses platform setting row editors, calls Epic 118 client APIs, and shows an empty state when no production integration definitions exist (current default).

**Not implemented:** runtime WorkerHost consumption, module-contributed definitions, cross-integration `ProviderBinding`, production integration setting keys (Epic 121).

**Current baseline:**

- Instance-scoped platform settings registry (Epics 109, 111, 112)
- Provider-backed options and write validation (Epic 114)
- Integration provider context (`integrationId`) for options/validation on platform route (Epic 115)
- First real same-integration provider `campusnexus.campuses` (Epic 116)
- Integration-scoped value persistence and API (Epic 118)

---

## A. Conceptual model

1. **Instance/platform settings**  
   Stored at instance scope and managed at `/config/platform-settings`.
2. **Integration-scoped settings**  
   Values stored per integration (for example selected campuses, integration-specific notification or runtime defaults).
3. **Module-contributed integration settings (future)**  
   Definitions contributed by a module and applied to one integration.
4. **Provider context**  
   Context used to load and validate selectable options (`Instance`/`Integration`), separate from value storage scope.
5. **Runtime consumption**  
   How workers/modules read effective settings during execution, separate from management surfaces.

**Required distinction:** Provider context is not value scope, and value scope is not runtime resolution behavior.

---

## B. Scope model

### Future scope kinds

- `Instance`
- `Integration` (first implementation target)
- `Module` (future)
- `IntegrationModule` (future)
- `Connection` (future)
- `User` (deferred)

### First implementation recommendation

- Implement **Integration** scope only.
- Keep current instance behavior unchanged.

### Persistence guidance

`PlatformSettingValues` already has `ScopeKind` and `ScopeId` plus unique `(Key, ScopeKind, ScopeId)`.  
Recommended key identity remains:

- `SettingKey + ScopeKind + ScopeId`

Recommended values:

- Instance: `ScopeKind = "Instance"`, `ScopeId = "instance"` (match existing constant)
- Integration: `ScopeKind = "Integration"`, `ScopeId = integrationId`

No schema change was required; Epic 118 expanded repository/service/resolver and API behavior for `Integration` scope.

---

## C. Key namespace strategy

| Owner | Pattern | Example |
|-------|---------|---------|
| Platform-owned integration settings | `integrations.*` | `integrations.notifications.enabled` |
| Module-contributed integration settings (future) | `modules.{moduleId}.*` or approved connector prefix | `modules.campusnexusstudent.campuses.included` |

Rules:

- Never embed `integrationId` in setting key.
- `integrationId` belongs in scope (`ScopeId`), not key namespace.
- Modules must not define arbitrary `platform.*` keys.
- No arbitrary settings-tree path reads/writes.

---

## D. Definition ownership

1. **Platform-owned definitions**
   - Platform defines and versions these.
   - First implementation should use this only.
2. **Module-owned definitions (future)**
   - Constrained by module namespace and lifecycle.
   - Deferred until integration-scoped storage/API/UI are stable.
3. **Connector-owned definitions (future)**
   - Defer unless a clear connector-level need appears.

First implementation recommendation: platform-owned definitions only, with optional module applicability filters in metadata.

---

## E. Resolution and precedence

### First implementation (recommended)

1. Integration-scoped DB value
2. Definition default
3. Optional configuration default only when explicitly supported per definition

Do not implicitly inherit all instance settings into integration scope.

### Future possible strategy

Integration override -> explicit fallback scope(s) -> configuration default -> definition default.

Fallback should be opt-in per definition, not global.

---

## F. Provider-backed options with integration scope

Example:

- Setting value scope: Integration `abc`
- Provider context: Integration `abc`
- Provider key: `campusnexus.campuses`

Design rule:

- Same-integration settings UI/API should derive provider integration context from route scope.
- Operator should not manually pass a second integration identifier while editing integration `abc`.
- Cross-integration option providers remain future-only and require explicit `ProviderBinding`.

---

## G. API design

Use explicit integration-scoped routes first:

- `GET /api/integrations/{integrationId}/settings`
- `GET /api/integrations/{integrationId}/settings/{key}`
- `PUT /api/integrations/{integrationId}/settings/{key}`
- `DELETE /api/integrations/{integrationId}/settings/{key}`
- `GET /api/integrations/{integrationId}/settings/{key}/options`

Why:

- Route establishes value scope and default provider context.
- Avoid overloading `/api/config/platform-settings` with mixed scope semantics.
- Keep instance API dedicated to instance scope.

Future generic API (`/api/settings?scopeKind=&scopeId=`) can be considered after integration routes are proven.

---

## H. UI design

### Placement

Add an Integration Detail sibling route:

- `/integrations/{integrationId}/settings`

This aligns with existing sibling surfaces (notifications, schedule diagnostics) and avoids overloading the current Configuration section.

### Surface behavior

- Group by:
  - Platform integration settings
  - Module-specific settings (when applicable)
  - Categories (notifications/runtime/scheduling as needed)
- Show resolved value/source and typed editor behavior similar to platform settings page.
- Provider-backed options auto-use current integration context.
- Read-only mode when operator lacks update permission.
- Inline validation and concurrency feedback.
- Audit note and no appsettings writeback.

### Out of scope

- Custom Razor panels
- Wizard framework

---

## I. Authorization

Initial recommendation:

- View integration settings: `integration.read`
- Manage integration settings: `integration.update`
- Provider-backed options: integration access checks plus existing provider authorization path

Defer introducing a new dedicated action (for example `integration.settings.manage`) unless product requires finer-grained permission boundaries.

Note: current Integration Detail capability gating should be aligned with API expectations when this is implemented.

---

## J. Audit

Recommended action names:

- `integration.settings.set`
- `integration.settings.reset`

Audit payload should include:

- Setting key
- Scope kind
- Integration id
- Old value / new value (safe, readable)
- Actor
- Source
- Provider context summary when relevant

Never include secrets in audit details.

---

## K. Runtime consumption

Future runtime resolver should support integration context directly. Example shape:

```csharp
// Illustrative only.
IIntegrationSettingsResolver.GetResolvedAsync(integrationId, key, cancellationToken);
```

Initial runtime behavior recommendation:

- Resolve effective settings at operation/procedure start.
- Do not apply mid-run mutations to already-running work.
- Defer distributed cache/invalidation strategy to a later phase.
- Optionally expose effective settings source in run diagnostics later.

---

## L. Lifecycle behavior

| Event | Recommendation |
|-------|----------------|
| Integration retired | Preserve scoped rows; UI becomes read-only or hidden by policy |
| Integration deleted | Explicit delete policy (audited cascade or explicit cleanup gate) |
| Module disabled/uninstalled | Hide module-contributed definitions; preserve values until cleanup policy applies |
| Definition deprecated/removed | Preserve values with warning initially |
| Provider unavailable | Preserve stored value, block invalid updates, show warning |
| Provider values stale | Preserve with warning in first implementation, reject on save for invalid new submissions |

---

## M. Relationship to wizards

- Setup wizards may write integration-scoped settings as final persisted values.
- Wizard steps may use the same definitions/options contracts.
- Wizard framework remains separate from this settings design.
- Future services should support validate-without-save and batch validation, but this is deferred.

---

## N. Relationship to resource/catalog providers

- Integration settings may consume same-integration option providers.
- Resource/catalog browsing remains a separate subsystem.
- Settings store selected identifiers only, not remote catalog payloads.
- Option providers may later adapt resource providers without collapsing the boundaries.

---

## O. Recommended implementation sequence

1. **Epic 118 — Integration-scoped persistence and API foundation** (delivered)
   - Scoped repository/service/resolver for `Integration`
   - `/api/integrations/{integrationId}/settings` routes
   - `integration.settings.set` / `integration.settings.reset` audit
   - Empty production integration definition registry (test-only keys in unit tests)
2. **Epic 119 — Integration Detail settings UI** (delivered)
   - `/integrations/{id}/settings`
   - Reuses typed setting editors; integration-scoped save/reset/options client methods
   - Empty state when integration definition registry has no keys
3. **Epic 121 — First module-owned integration setting (next, if product-approved)**
   - Example: included campuses via `campusnexus.campuses`
4. **Later**
   - Module-contributed definitions
   - Resource/catalog browser integration
   - Wizard framework
   - Cross-integration `ProviderBinding`

---

## P. Explicit non-goals (Epic 117)

This epic does not:

- Implement integration-scoped persistence
- Implement integration settings API routes
- Implement integration settings UI
- Add production setting keys
- Add module-contributed definitions
- Implement runtime settings consumption
- Implement resource/catalog browser
- Implement wizard framework
- Implement cross-integration provider bindings
- Change the nine instance platform settings
- Change source precedence in existing instance scope
- Write settings back to appsettings
- Store secrets in settings values

---

## Related

- [platform-settings-registry.md](platform-settings-registry.md)
- [platform-settings-option-providers-and-scopes.md](platform-settings-option-providers-and-scopes.md)
- [runtime-connection-resolution.md](runtime-connection-resolution.md)
- [Epic 80 Integration Detail redesign pilot](../02-decisions/Epic80-Integration-Detail-Redesign-Pilot.md)
- [Epic 115](../02-decisions/Epic115-Integration-Scoped-Option-Provider-Foundation.md)
- [Epic 116](../02-decisions/Epic116-CampusNexus-Campuses-Option-Provider.md)
