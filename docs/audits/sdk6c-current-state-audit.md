# SDK-6C Current-State Audit — Typed Integration Configuration, Connections, Secrets, and Readiness

**Date:** 2026-07-21  
**Status:** Audit complete — implementation proceeds from this baseline  
**Predecessor:** SDK-6B closed (`docs/audits/sdk6b-closure.md`)  
**Scope:** Evidence-only inventory of configuration, secrets, connections, readiness, and module bootstrap. Does not re-evaluate SDK-6B.

## Verdict

The platform already has substantial **control-plane** pieces for integration settings, connections, bindings, verification, and integration-level readiness. SDK-6C is not a greenfield rebuild. The gaps are:

1. **Authoring model** — modules still speak a mix of `PlatformSettingDefinition`, hosted-service slot registration, and connector types rather than one intentional Integration SDK configuration surface.
2. **Runtime consumption** — WorkerHost does not resolve typed integration settings/secrets for module code (documented as deferred after Epic 118/119).
3. **Readiness completeness** — `IntegrationReadinessService` evaluates bindings/verification/schedule ownership, but **not** required settings, secrets, or **procedure-specific** subsets.
4. **Value-type coverage** — `PlatformSettingValueType` is Boolean/Integer/Duration/String/StringArray only; epic needs decimal, URI, time-zone, multiline, choice, sensitive text as first-class authoring kinds.
5. **Sample/production teaching path** — ApiPolling is settings-only (embedded fake API); Proof/Unisa declare slots but operators lack a coherent “missing CnsApi / IntegrationDb / Sftp” readiness story tied to procedure launchability.

## Lifecycle map (as implemented today)

| Concern | Declaration | Persistence | API / UI | Runtime resolution | Readiness |
| --- | --- | --- | --- | --- | --- |
| Platform-wide settings (e.g. SMTP) | Hardcoded platform registry | `PlatformSettingValues` (instance scope) + optional secrets | Platform settings APIs/UI | Host `IConfiguration` / platform services | Not integration readiness |
| Module integration setting defs | `IModuleIntegrationSettingDefinitionContributor` → `PlatformSettingDefinition` | Definitions in code | Merged into integration settings catalog | **Missing on WorkerHost** | **Not evaluated** |
| Integration setting values | N/A | `PlatformSettingValues` (integration scope) | `/api/integrations/{id}/settings` + Integration Detail settings UI | Control plane resolver only | **Not evaluated** |
| Sensitive setting values | `IsSensitive` / `InputKind.Secret` | Opaque secret ref in settings row; plaintext in `ISecretStore` | Presence via `IsSecretConfigured`; replace/clear verbs | Platform settings path; integration path partial | **Not evaluated** |
| Connection definitions | Connector contributors (CampusNexus, SFTP, Platform SQL) | Code registry | Connections catalog | Factory registry | N/A |
| Connection instances | Operator creates | Connection entities + secret material | ConnectionsController + UI | `IConnectionResolver` | Verification fields |
| Slot / binding | `ICapabilitySlot` via module hosted services | Binding entities (integration-scoped) | Bindings / IntegrationBindings APIs + UI | `SlotBoundResolver` | Missing/inactive/mismatch/verification issues |
| Integration readiness | Computed | Not persisted (projection) | Fleet / operator detail / procedure list | Used to block procedure launch when `IsRunnable=false` | Binding+verification+schedule; **no settings/secrets/procedure subset** |
| Procedure launch | Module procedure catalog | N/A | IntegrationProceduresController | Dispatch after readiness gate | Same integration readiness for all procedures |

## Evidence anchors

### Integration SDK (authoring boundary from SDK-6A/6B)

- `src/Apperture.ModuleSdk/Apperture.ModuleSdk.Integration/` — execution, operation items, `PlatformSettings/`, `Services/IModuleSlotRegistry.cs`
- `src/Apperture.ModuleSdk/Apperture.ModuleSdk.Integration.Testing/IntegrationProcedureTestHost.cs` — `WithSlot`, event/log capture; **no settings/secrets/readiness fakes yet**
- Public API guard: `tests/Apperture.UnitTests/ModuleSdk/IntegrationSdkPublicApiSnapshotTests.cs`

### Settings

- Design: `docs/01-architecture/integration-scoped-settings.md` (Epics 117–119 delivered; **runtime WorkerHost consumption explicitly not implemented**)
- Contributor: `IModuleIntegrationSettingDefinitionContributor`
- Services: `IntegrationSettingsService`, `IntegrationSettingsResolver`, `IntegrationSettingsController`, UI `IntegrationSettings.razor`
- Secrets: `ISecretStore`, `PlatformSettingSecretMaterialProvisioner`, `PlatformSettingSecretOperations` (replace/clear)
- Resolved DTO already has `IsSensitive` / `IsSecretConfigured` (presence only)

### Connections and verification

- Connectors: `Apperture.Connectors.CampusNexus`, `Apperture.Connectors.Sftp`, platform SQL via `Apperture.ConnectorResolution`
- Resolution: `SlotBoundResolver`, `DefaultConnectionResolver`, client factories
- Verification: `ConnectionVerificationService`, connector verifiers (e.g. `CampusNexusConnectionVerifier`), readiness policy `AdvisoryConnectionVerificationReadinessPolicy`
- UI: Connections pages + binding flows (Epics 50/58/59 lineage)

### Readiness and execution enforcement

- Models: `IntegrationReadinessModels.cs` — issue codes for binding/connection/verification/schedule
- Service: `IntegrationReadinessService` (~677 lines) — **no secret issue codes; settings mentioned only peripherally; no procedure filter**
- Launch gate: `IntegrationProcedureService` loads readiness and blocks when integration not runnable — **all procedures share one gate**

### Module bootstrap (concrete SDK-6C pressure)

| Module | Slots / deps today | Settings today | Notes |
| --- | --- | --- | --- |
| HelloWorld | Demo slots via hosted service | Demo emit + greeting via platform setting defs | Epic wants **configuration-free** readiness |
| FileImport | Local fixtures | Light settings possible | Keep fixtures easy |
| ApiPolling | None (in-process fake API) | PageSize, MaxRecords, flags | Must become **connection-backed sample** |
| Proof | `CnsApi`, `IntegrationDb` | Slot hosted service only | Must report missing independently; preserve Persistence adapter |
| Unisa | `CnsApi`, `Sftp`, `IntegrationDb` | Slot hosted service only | Same; independent missing reasons |

Constants:

- Proof: `SlotCnsApi = "CnsApi"`, `SlotIntegrationDb = "IntegrationDb"`
- Unisa: `SlotCnsApi`, `SlotSftp`, `SlotIntegrationDb`

## Ownership confusion to resolve in SDK-6C

1. **`PlatformSettingDefinition` lives in the Integration SDK assembly** under `Apperture.Core.Application.PlatformSettings`, and is used for both instance platform settings and integration-scoped module settings. Authors cannot tell “SMTP” from “my integration page size” by type alone.
2. **Slots are declared at host startup** (`IHostedService` → `IModuleSlotRegistry`) rather than as rich connection-requirement metadata (required/optional, verification expectations, multi-bind, procedure subset).
3. **Modules resolve capabilities via `IRuntimeSlotResolver`** (good) but have **no typed settings accessor** and must not grow env-var-only bootstrap for secrets/config.
4. **Readiness is integration-wide** — cannot say “local fixture procedure is ready while export procedure waits on SFTP.”

## Explicit non-reuse / non-goals (from epic)

Do not expand into SMTP redesign, notification-provider redesign, marketplace, codegen, shared entities, workflow composition, module UI contribution, scheduler redesign, or WorkerHost lifecycle management.

## Implementation implications (carry into design)

1. Add intentional SDK configuration contracts (settings, connection requirements, readiness, procedure requirements) in `Apperture.ModuleSdk.Integration`.
2. Reuse `PlatformSettingValues` + `ISecretStore` + binding/connection tables; extend value kinds / readiness issue codes rather than parallel stores.
3. Implement WorkerHost/runtime accessor adapters as the **only** bridge to persistence/secrets.
4. Extend readiness evaluation for settings/secrets and procedure-scoped requirement sets; keep launch/schedule enforcement on the same logic.
5. Migrate modules; extend `Integration.Testing`; add architecture + security + functional tests; Aspire smoke.

## Next artifact

`docs/audits/sdk6c-design.md` — ownership model and design for all SDK-6C deliverable areas.
