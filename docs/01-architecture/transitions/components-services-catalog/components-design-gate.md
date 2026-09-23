# Components — design gate

Date: 2026-09-22
Status: **Design gate complete and approved (2026-09-22). Not implemented. No branch, no code.**
Audited HEAD: `f3ec36957897fb85b5da921f2419c249c42191c7` (`master`, clean tree).

Product rulings D1–D4 were reviewed and **approved** on 2026-09-22 and are recorded as decided in §24. Two
consistency corrections were applied at the same time (§6 wording; §11/§18 menu parent visibility).

> **Services-gate follow-ups (2026-09-23) — no architectural amendment.** The
> [Services design gate](./services-design-gate.md) has now run and **confirmed** every settled decision here:
> the projection-only model, `(Kind, NativeId)` identity, the kind/origin taxonomy, the authorization
> aggregation rule and the non-1:1 Resource rule all stand unchanged, and **CMP-1 is unblocked**. It settles the
> two questions D2 and §23 deliberately delegated to it, and corrects one factual assumption:
>
> 1. **Connectors tab (D2 timing, now final)** — Components behaviour is **unchanged**: keep the tab, link its
>    rows to `/components/connector/{nativeId}` in CMP-5. The tab is **retired during Services implementation
>    (SVC-4)**, never during Components. Components must not be blocked or slowed by that retirement.
> 2. **Capability advertisement (§23 Q1)** — **none in Components v1.** `ComponentSummary` and
>    `ComponentDetailDto` are unchanged; no `Capabilities[]`, no provider-availability field, no list-path
>    lookup. The later mechanism, if ever wanted, is a detail-time-only static lookup (Services gate §14).
> 3. **Factual correction to §12** — the deep link *"Create a connection with this connector →
>    `/connections/new?connectorDefinitionId=…`"* names a query parameter that **does not exist**:
>    `ConnectionNew.razor` supports only `?context=`, and the connector is chosen from a `<select>` bound to
>    `_connectorDefinitionId` with no `[SupplyParameterFromQuery]`. **CMP-3 may add the small supported
>    preselection behaviour if still desired** (roughly five lines), or link to `/connections/new` without it.
>    This is a CMP-3 implementation detail, not a design change.

Starts from the approved rulings in [`README.md`](./README.md) (the Components/Resource-Catalog/Services
boundary note) and the near-term execution order in
[`pre-release-product-roadmap.md`](../../../00-overview/pre-release-product-roadmap.md) §4. Everything below is
grounded in the code as it stands at that commit; where a prior planning note and the code disagree, the code
wins and the disagreement is called out rather than quietly smoothed over.

The **Services design gate has now run and is approved (2026-09-23)** — see
[`services-design-gate.md`](./services-design-gate.md). §23 was this gate's handoff and is answered there in full.
**Components implementation (CMP-1…CMP-6) is unblocked and is the next implementation body.**

---

## 1. Executive recommendation

**Component is a read-time projection, not a persisted entity.** Option A. No `Components` table, no
`ComponentDefinition` aggregate, no `IComponentDefinitionRegistry`, no `IComponentDefinitionContributor`. The
authoritative registries already exist per family (`IConnectorDefinitionRegistry` in memory,
`INotificationDefinitionStore` in SQL); a Component record would be a third copy of data neither of them would
cede ownership of.

The nine load-bearing decisions:

| # | Decision | Why |
|---|---|---|
| 1 | **Projection only** (Option A) | Both v1 families already have authoritative storage and authorized read services. Nothing in the audit requires cross-family identity, lifecycle, versioning or configuration that those can't carry. |
| 2 | **Identity = `(Kind, NativeId)`**, routed as `/components/{kind}/{nativeId}` | Every v1 Component's native id is already a stable, human-readable dotted key (`Apperture.Platform.Sql`, `platform.schedule.expected_run_missing`). ADR-0041 tier 2 is satisfied with no slug system and no new identifier. |
| 3 | **Kind = string token** in a `ComponentKinds` static class | Matches `InventoryTypeTokens` / `NotificationDefinitionOrigins`. Adding a family later is additive, not a breaking enum change in a contract the Blazor client also compiles against. |
| 4 | **Origin = derived, two values** (`System`, `Module`) | `SupplyingModuleId is null` → System. Both families already carry a nullable `ModuleId`. No `User` origin: user-created NotificationDefinitions are not Components at all, so the taxonomy never sees them. |
| 5 | **`IComponentCatalog` + one small provider per family** | Two providers today. The seam is one interface and a DI collection — the same shape `IConnectorDefinitionContributor` and `IDashboardCardProvider` already use. Not a plugin framework. |
| 6 | **No new permissions.** Per-family gates reused; unauthorized families are omitted, not denied | `connector.definition.read` and `notification.definition.read` already exist and are already the gate on both surfaces. A `component.read` permission would let aggregation grant access the family gate refuses. |
| 7 | **Detail = catalog wrapper that delegates every mutation** (Option B) | NotificationDefinition already has a rich authoritative 798-line detail page. Connector has none. The wrapper unifies the entry point without duplicating authoring. |
| 8 | **Nav: `Config → System → Components → { Catalog, Modules }`**, not a new rail domain | NAV-1 closed the rail and `SectionOwnerConstants.KnownRailDomains`. Modules is *already* a Config → System leaf, not a primary-level menu. See §18 — this refines the stated product direction on a factual point. **Approved (D1).** |
| 9 | **No Component configuration system** | Connectors have zero platform-scope configuration today. NotificationDefinitions' platform-scope settings live on the definition row and belong to the specialized surface. Option A of Task 12. |

**Scope: exactly two families, seven entries today.** Three Connector Components (all System) and four built-in
NotificationDefinition Components (one System, three Module). This is deliberately small enough that server
paging, caching and usage-count denormalization are all unjustified — and §20 shows the real performance risk
is the opposite one (accidentally calling the module registry on list).

**One departure from the prior planning note, stated openly.** [`README.md`](./README.md) recommended a
`ComponentDefinition` record paired with an `I*Registry`/`I*Contributor` triad mirroring
`ConnectorDefinition`/`IConnectorDefinitionRegistry`/`IConnectorDefinitionContributor`. That recommendation
presumed Components would *hold* definitions. The audit shows they should not: building that triad would create
exactly the "universal registry for conceptual neatness" the same note forbids two sections later. The note's
real constraint — **never introduce a bare `Component` type, because it collides with Blazor's
`ComponentBase`/"Razor component" vocabulary in the UI project** — is honoured in full: every type named below
is `ComponentSummary`, `ComponentDetail`, `ComponentKey`, `ComponentKinds`, `ComponentOrigins`,
`IComponentCatalog`, `IComponentCatalogProvider`. No bare `Component`.

---

## 2. Current Module architecture

### What a Module is today

A Module is a **compile-time-composed capability package** with a **persisted per-Environment
installation-state row**. It is not a dynamically loadable package and there is no runtime install pipeline.

- Contract: `IModule : IModuleManifest, IWorkflowDefinitionProvider`
  (`src/Apperture.ModuleSdk/Apperture.ModuleSdk.Abstractions/IModule.cs`).
- Metadata: `ModuleManifest(Id, Name, Version, Description)` plus optional `Permissions`,
  `GovernedResources`, `GovernedActions`, `ConnectorDefinitions`, `Procedures`, `Signals`.
- Runtime composition: **five modules are `new`-ed literally in
  `src/Apperture.ControlPlane/Apperture.Management.Host/Program.cs:57`** and handed to `AddKnownModules`:
  `Sample.HelloWorld`, `Sample.FileImport`, `Sample.ApiPolling`, `Unisa.CampusNexus.PaymentPlans`,
  `Proof.CampusNexus.StudentReview`. The registration is `AddSingleton<IEnumerable<IModule>>(list)`.
- Domain records `Module` / `ModuleInstallation` / `ModuleInstance` exist in
  `Apperture.Core.Domain/Environment/` but are **not** what the management surface uses — the live path is
  `ModuleDescriptor` + `ModuleCatalogEntry`.

### Registry / discovery

`SqlModuleRegistryService` (`src/Apperture.Persistence/.../SqlModuleRegistryService.cs`) is the real registry.
`ListModulesAsync` merges two sources:

1. **Discovery** — `DiscoverDescriptorsAsync` calls `module.BuildDescriptorAsync()` on every DI-registered
   `IModule`. `ModuleDescriptorBuilder.BuildAsync` enumerates the module's workflow definitions and (if it
   implements `IOperationDefinitionProvider`) its operations, then appends permission and navigation
   contributions.
2. **Persistence** — `ModuleInstallationStates` rows for the current `EnvironmentId` (`EnvironmentId`,
   `ModuleId`, `Version`, `Enabled`, `InstalledAt`, `UpdatedAt`).

An installed row with no discoverable descriptor yields a synthetic descriptor and a `descriptor_missing`
blocker. Cross-module conflict detection (`BuildCrossModuleIssues`) enforces uniqueness of workflow ids,
operation ids, navigation ids, navigation routes and `{moduleId}:{action}:{resourceType}` permission keys across
*enabled* modules.

### Contributions a Module can declare

| Contribution | Declared via | Lands in | In `ModuleDescriptor.Contributions`? |
|---|---|---|---|
| Workflow definitions | `IWorkflowDefinitionProvider.GetDefinitionsAsync` | `IWorkflowDefinitionRegistry` | Yes (`workflow`) |
| Operation definitions | `IOperationDefinitionProvider` | `IOperationRegistryService` | Yes (`operation`) |
| Permissions | `ModuleManifest.Permissions` | `IResourceDescriptorRegistry` (scope `module:{Id}`) | Yes (`permission`) |
| Navigation | `IModuleNavigationProvider` | `/api/menus` (known rail domains only) | Yes (`navigation`) |
| Procedures | `ModuleManifest.Procedures` | `IProcedureRegistryService` | **No** |
| Signals | `ModuleManifest.Signals` | `IModuleSignalRegistry` (worker startup) | **No** |
| Governed resources / actions | `ModuleManifest.GovernedResources/GovernedActions` | `IResourceDescriptorRegistry` | **No** |
| Integration settings, connection requirements, procedure requirement sets | `IIntegrationConfigurationContributor` | `IIntegrationConfigurationRegistry` | **No** |
| Connector definitions | `ManifestConnectorDefinitions` (`ConnectorDefinitionDeclaration`) | **nothing** — the type's own XML doc says "documentation/tooling", "not a runtime registry" | **No** |

### Management surface

`ModulesController` (`/api/modules`) → `IModuleWorkflowListingService` → `IModuleRegistryService`. DTO:
`ModuleSummaryDto(Id, Name, Version, Description, State, Enabled, Installed, EffectivelyActive,
ValidationIssues, ContributionCounts)`. `ContributionCounts` is `Kind → count` from
`ModuleDescriptor.Contributions` — **already shipped on the wire and rendered nowhere.** UI: `/modules` list
(`ModulesList.razor`, flat table) and `/modules/{ModuleId}` detail (`ModuleDetail.razor`, 388 lines: lifecycle
actions, state, recent activity, HSK-A's "Integrations using this module", links).

### Authorization

**Read and manage are the same gate.** `ModuleWorkflowListingService.GetModulesAsync` requires
`module.configure` on `ResourceTypeConstants.Module` at instance scope — the same permission
install/enable/disable/uninstall require. `OperatorCapabilitiesDto` exposes only `CanManageModules`; there is no
module-read capability. The nav leaf is gated `DomainMenuCapabilityTokens.ManageModules`, and NAV-1's own
mapping note lists Modules under "no view capability today, not invented here".

This asymmetry is load-bearing for §11: a caller with `connector.definition.read` but not `module.configure`
may **not** be shown module display names, versions or lifecycle state.

### Task 0 — direct answers

1. **What is a Module today?** A compile-time-linked capability package identified by a manifest string id,
   with a per-Environment installation-state row and a shallow install/enable lifecycle.
2. **Persisted or discovered?** Both, and neither alone. Identity/metadata/contributions are *discovered* from
   code at every call. Install/enable state is *persisted* (`ModuleInstallationStates`).
3. **Can a Module enumerate its contributions?** Partly. `BuildDescriptorAsync` enumerates workflows,
   operations, permissions and navigation. Procedures, signals, integration settings, connection requirements
   and connector declarations are **not** in the descriptor — they are reachable only via their own registries.
4. **Does that information exist at runtime already?** Yes, for everything relevant to v1. Both v1 families are
   readable at runtime today through existing authorized services.
5. **Could a Component catalog be derived without a new table?** **Yes.** `IConnectorDefinitionRegistry.GetAll()`
   and `INotificationDefinitionService.ListAsync(...)` already return everything `ComponentSummary` needs. This
   is the evidence for Option A.
6. **What lifecycle does a Module have?** `Discovered → InstalledDisabled ⇄ InstalledEnabled`, back to
   `Discovered` on uninstall. Enable/disable report `PartialUntilRestart`; uninstall reports `RequiresRestart`.
   Disable/uninstall are blocked by persisted workflow or operation instances referencing the module's
   definitions.
7. **What if a Module disappears or is disabled?** Disappears (descriptor gone, install row remains):
   `descriptor_missing` blocker, `EffectivelyActive = false`, still listed. Disabled: still listed and still
   discoverable; `KnownModuleManifestProvider` filters manifests to enabled modules. **Critically for §17 —
   neither case removes a connector definition from the in-memory registry, and neither case deletes or hides a
   `ModuleBuiltIn` NotificationDefinition row.**

---

## 3. Current Connector architecture

### End-to-end trace

- **Domain**: `ConnectorDefinition(Id, Name, ModuleId, RequiredInputSchema)` with `Description`,
  `ConnectionFields : IReadOnlyList<ConnectionFieldDescriptor>`, `Capabilities : ConnectorCapabilities`
  (`[Flags]`: `None`, `BrokeredAccessCandidate`, `SupportsConnectionVerification`).
  `GetIdentity()` returns scope `module:{ModuleId}` when `ModuleId` is set, else null.
- **Identity**: `ConnectorDefinitionId` — a string. Well-known ids in `WellKnownConnectorDefinitionIds`:
  `Apperture.Platform.Sql`, `Apperture.Platform.CampusNexus`, `Apperture.Platform.Sftp`.
- **Registry**: `IConnectorDefinitionRegistry` — `Register` / `Get(id)` / `GetAll(moduleId?)`. Two identical
  in-memory implementations: `StubConnectorDefinitionRegistry` (control plane, registered singleton) and
  `InMemoryConnectorDefinitionRegistry` (worker). **No SQL table.**
- **Population**: two hosted services at startup. `SeedWellKnownConnectorDefinitionsHostedService` registers
  Platform SQL. `ConnectorDefinitionContributorsHostedService` then applies every
  `IConnectorDefinitionContributor`; later registration for the same id overwrites.
- **Contributors today**: exactly two, both in `src/Apperture.Connectors/` —
  `SftpConnectorDefinitionContributor` and `CampusNexusConnectorDefinitionContributor`. These are **connector
  packages, not Modules**.
- **Authorized read**: `ConnectorDefinitionCatalogReadService` requires `connector.definition.read` on
  `ResourceTypeConstants.ConnectorDefinition` at instance scope, then returns `_registry.GetAll()` verbatim.
- **API**: `/api/connector-definitions` (list) and `/api/connector-definitions/{id}` (detail — **exists and is
  currently unused by any UI**). DTO `ConnectorDefinitionCatalogDto(Id, Name, ModuleId, Description, Fields,
  Capabilities)` where each `ConnectionFieldCatalogDto(Key, DisplayName, IsSecret, Description, IsRequired)`
  derives `IsRequired` from `RequiredInputSchema.ContainsKey(f.Key)`.
- **UI**: `ConnectorsListContent.razor`, a **tab inside `/connections`**, flat table of Name / Id / Source /
  Fields / Capabilities. `Source` renders `d.ModuleId ?? "Platform"`. **No connector detail page exists.**
- **Connection creation**: `Connection` carries `ConnectorDefinitionId`; creation validates against the
  registry; `ConnectionsController` uses `_connectorDefinitions.Get(id)` to compute `SupportsVerification`.
- **Platform-scope configuration**: **none.** The only consumer of
  `PlatformConnectorDefinitionsOptionProvider` (`platform.connectorDefinitions`) is the Epic-120 settings
  *gallery* demo (`PlatformSettingDemoDefinitionRegistry` lines 148 and 163). No production setting is
  connector-scoped.

### Task 1 — determinations

- **Stable identity of a Connector Component**: `ConnectorDefinitionId.Value`. It is the registry dictionary
  key, the `Connection` foreign key, the platform-settings option value and the API route parameter. Already
  human-readable. Nothing new is needed.
- **Enough metadata to project directly into `ComponentSummary`?** **Yes, entirely.** Name → display name;
  `Description` → description; `ModuleId` → supplying module / origin; `Capabilities` and `ConnectionFields`
  → detail facts. `ConnectorDefinitionCatalogDto` is already almost exactly a `ComponentDetail` body.
- **Supplying Module**: `ConnectorDefinition.ModuleId`.
- **System connector**: `ModuleId is null`. **All three connectors today are System.** The Module-supplied path
  is fully built (`ModuleId` field, `GetAll(moduleId)` filter, `module:{ModuleId}` authorization scope) and
  **completely unexercised** — no module declares a connector, and `ManifestConnectorDefinitions` documents
  rather than registers. v1 must handle Module connectors correctly and must be tested with a fake, but must
  not claim any exist.
- **Configuration at platform scope**: none. Do not invent one (§16).
- **"Where used"**: **configured Connections**, via `Connection.ConnectorDefinitionId`. This is the single
  strongest and only directly-attributable Component→Resource link in v1 (§19).

**Connector definition ≠ Connection Resource.** The definition is host-seeded in-memory metadata describing the
*shape* of connection material. The Connection is a persisted, ResourceContext-owned, secret-referencing
Resource. The Connectors tab's own copy already says this, correctly.

---

## 4. Current Notification Definition architecture

### Model

`NotificationDefinition(Id, DisplayName, Description, SourceKind, Origin, Scope, Mutability, ModuleId,
IntegrationId, PrimaryEventType, IsEnabled, DefaultSeverity, RuleJson, TemplateJson, CreatedAtUtc, UpdatedAtUtc,
CreatedByActorId, UpdatedByActorId, ToastEnabledByDefault)`. Persisted in SQL
(`NotificationDefinitionEntity` / `NotificationDefinitions`).

Taxonomy constants (`NotificationDefinitionConstants.cs`):

- `Origin`: `platformBuiltIn` · `moduleBuiltIn` · `userCreated`
- `Scope`: `platform` · `module` · `integration`
- `Mutability`: `staticRuleMutableSubscription` · `fullyEditable` · `readOnly`

### The four built-ins, and how they got there

Each is inserted by a **dedicated EF migration** running raw SQL built from a C# seed class, guarded by
`IF NOT EXISTS`:

| Id | Origin | Scope | ModuleId | Migration |
|---|---|---|---|---|
| `platform.schedule.expected_run_missing` | `platformBuiltIn` | `platform` | `null` | `20260517201155_Epic77_NotificationDefinitions` |
| `module.sample.helloworld.demo.completed` | `moduleBuiltIn` | `module` | `Sample.HelloWorld` | `20260526120000_Epic121_…` |
| `module.sample.fileimport.import.completed_with_errors` | `moduleBuiltIn` | `module` | `Sample.FileImport` | `20260715120000_Sdk2_…` |
| `module.sample.apipolling.sync.completed_with_errors` | `moduleBuiltIn` | `module` | `Sample.ApiPolling` | `20260718120000_Sdk5A_…` |

All four have `Mutability = staticRuleMutableSubscription`. All four are seeded `ToastEnabledByDefault = true`
(ADR-0042). Each `ModuleId` string matches a real registered module id — **by convention, enforced by nothing.**
There is no foreign key, no startup reconciliation and no test asserting the match. This is a real finding
(§17, §21).

### User-created definitions

`SqlNotificationDefinitionStore.CreateUserDefinitionAsync` hard-codes `Origin = userCreated`,
`Scope = integration`, `Mutability = fullyEditable`, and requires an `IntegrationId`. Id is
`ndef_{guid:N}`. So **every user-created definition is Integration-scoped with an opaque id**, and every
built-in is Platform- or Module-scoped with a readable dotted id. Convenient, but §8 keys on **Origin**, not on
id shape or Scope, because Origin is the approved eligibility rule.

### Mutation restrictions (server-enforced)

`UpdateAsync` throws `"Built-in rule JSON is immutable."` / `"Built-in template JSON is immutable."` when
`Origin` is `platformBuiltIn` or `moduleBuiltIn`. `DeleteAsync` throws `"Built-in notification definitions
cannot be deleted."` `DisplayName`, `Description`, `IsEnabled`, `DefaultSeverity` and `ToastEnabledByDefault`
are **not** refused for built-ins at the store level.

**But the UI does not expose them.** `NotificationDefinitionDetail.razor` gates the "Edit definition" button on
`_canEditDefinition => _canManage && Mutability == "fullyEditable"` — false for every built-in. So today an
administrator **cannot change a built-in's Toast default or default severity through any UI**, even though the
API permits it. `IsEnabled` alone is reachable, via the checkbox on the list page. Recorded in §16 and §24.

### Authorization

`NotificationDefinitionAuthorization`: platform-scoped definitions need `notification.definition.read` /
`notification.definition.manage` at instance scope; Integration-scoped ones additionally need
`integration.read` / `integration.update` **on the owning Integration**. `RequireDefinitionAccessAsync` picks
the branch on `IntegrationId is null`. Since every Component-eligible definition is Platform- or Module-scoped
with a null `IntegrationId`, **Component reads take the platform branch only** — a single instance-scope
permission, no per-row Integration check, no N+1.

### Query surface

`NotificationDefinitionListQuery(IntegrationId, ApplicableModuleId, EventType, IsEnabled, Origin, Take=100,
OwnerResourceContextId, SystemScopedOnly)`. `Take` clamps to `[1, 200]`. Ordered by `DisplayName`. `Origin` is
**single-value equality** — there is no way to ask for "platformBuiltIn OR moduleBuiltIn" in one query (§10).

### "Where used" candidates

- `INotificationSubscriptionStore.ListByDefinitionAsync(definitionId)` — **exists today**, direct.
- `ListUserOverridesAsync(definitionId)` — per-user opt-outs.
- `NotificationUserPreferences` — per-user email/toast preference rows.
- `NotificationInstances` — **historical**, deliberately never cascaded on delete; a dangling
  `NotificationDefinitionId` is expected and documented.
- Integration ownership — **not applicable** to any Component-eligible definition (all have
  `IntegrationId = null`).

### Task 2 — determinations

- **Stable identity**: `NotificationDefinitionId.Value`. Readable for every eligible entry.
- **Which are Components**: `Origin ∈ { platformBuiltIn, moduleBuiltIn }`. `userCreated` is excluded — this is
  the approved rule and it must be expressed as an **Origin** predicate in code, not inferred from Scope or id
  shape, even though those correlate perfectly today.
- **System vs Module origin**: `platformBuiltIn` → System; `moduleBuiltIn` → Module. The projection should use
  `SupplyingModuleId is null` as the single origin rule across both families (§9) and assert in tests that it
  agrees with `Origin` for this family. One definition of origin, two independent confirmations.
- **Does `moduleBuiltIn` always have a reliable Module identity?** Today yes — all three set a real module id.
  But the guarantee is a seeding convention, not a constraint. v1 must render an unresolvable module id
  verbatim without claiming a module, and a test must cover it.
- **Reuse detail or wrap?** **Wrap, and delegate.** The existing page is authoritative and rich (summary pills,
  mutability, subscriptions, recipient resolution, preview, instances, diagnostics, technical payloads).
  Duplicating any of it into a generic page would be pure cost. §14.
- **Which usage links are operationally useful?** **Current subscriptions only** in v1. Not instances — those
  are historical activity and the authoritative page already presents them as such. Mixing a historical instance
  count into a "where used" block would misrepresent an inert definition as in-use.

---

## 5. Other candidate families

Targeted inventory of every registry, contributor and declaration surface found in `src/`.

| Concept | Registry / source | Operator-visible today | Classification |
|---|---|---|---|
| **Connector definition** | `IConnectorDefinitionRegistry` | Yes — `/connections` Connectors tab | **v1 Component** |
| **Notification definition (built-in)** | `INotificationDefinitionStore` | Yes — `/config/notifications/definitions` | **v1 Component** |
| Notification definition (user-created) | same | Yes | **Operator content, not a Component** (approved rule) |
| Workflow definition | `IWorkflowDefinitionRegistry`, `/api/modules/{id}/workflows` | Yes — Execute picker, Module detail | **Plausible future Component.** Real identity, real module attribution, already in `ModuleDescriptor.Contributions`. Excluded from v1 by the approved scope; the model must not block it. |
| Operation definition | `IOperationRegistryService` | Yes — Execute | **Plausible future Component.** Same reasoning. |
| Procedure | `IProcedureRegistryService`, `ModuleProcedureDeclaration` | Yes — Integration procedure launch | **Plausible future Component.** Has `Id`, `DisplayName`, `Description`, `Category`, module attribution, `RequiredPermissions`. The best-shaped non-v1 candidate found. Deferred by scope, not by unsuitability. |
| Module signal | `IModuleSignalRegistry`, `ModuleSignalDeclaration` | Indirectly — event types on notification rules | **Plausible future Component.** Has `DisplayName`, `Description`, `DefaultSeverity`. Registered at **worker** startup, so not reachable from the management host today — a real blocker, not a taxonomy question. |
| Integration setting definition | `IIntegrationConfigurationRegistry` / `IIntegrationSettingDefinitionRegistry` | Yes — Integration settings form | **Module configuration schema, not a Component.** It describes how to configure an Integration; it is not itself a capability an operator selects. |
| Connection requirement (`ConnectionRequirementDefinition`) | `IIntegrationConfigurationRegistry` | Yes — binding eligibility | **Consumption, not contribution.** A module declaring "I need an `http-api`" is the inverse of supplying a Component. Feeds §19's "enabled/enhanced by". |
| Capability slot (`ICapabilitySlot`) | `IModuleSlotRegistry` | Indirectly — Bindings | **Internal implementation detail.** |
| Permission / resource descriptor | `IResourceDescriptorRegistry` | Yes — permission catalog | **Authorization concept, not a Component.** Explicitly excluded by [`README.md`](./README.md). |
| Platform setting definition | `IPlatformSettingDefinitionRegistry` | Yes — Platform settings | **Configuration, not a Component.** |
| Platform setting option provider | `IPlatformSettingOptionProviderRegistry` | No | **Internal implementation detail.** |
| Connector client factory | `IConnectorClientFactoryRegistry` | No | **Internal implementation detail.** Runtime resolution only. Note for Services: this is where an executable capability actually lives. |
| Connection verifier | `IConnectionVerifierContributor` | Indirectly — `SupportsVerification` | **Capability of a Connector Component**, surfaced as a fact. Not a family. |
| Mapping definition | `IMappingDefinitionRegistry` | No | **Insufficient evidence.** Gateway-result persistence; no operator surface, no live registrations found. |
| Secret resolver | `ISecretResolverRegistry` | No | **Internal implementation detail.** |
| Step handler | `IStepHandlerRegistry` | No | **Internal implementation detail.** |
| Navigation contribution | `IModuleNavigationProvider` | Yes — rail menus | **Internal implementation detail** of the shell. Constrained to known rail domains by NAV-1. |
| Dashboard card provider | `IDashboardCardProvider` | **No — orphaned since NAV-1** | **Internal implementation detail.** Registered but unreached from the operator UI. |
| Role provider | `IRoleProvider` | Yes — governance | **Authorization concept.** |
| Schedule provider | `IScheduleProvider` | Yes — Schedules | **Resource concern**, not a Component. |
| Business Data collections | Demo projection services | Yes | **Resource concern.** Track 5, gated. |
| Custom metrics | — | — | **No model exists.** Deferred, as approved. |

**Nothing here forces v1 to grow.** The three plausible future families (Workflow, Operation, Procedure) share
the exact shape v1 assumes — stable string id, display name, description, optional module attribution — which is
the positive test that the model does not make them impossible. Module signals are the one candidate with a real
architectural obstacle (worker-side registration), and that is a Services/runtime question, not a catalog one.

---

## 6. Component definition

> A **Component** is a curated, operator-visible platform capability, supplied either by the platform itself or
> by a Module, that an operator can discover in one place, understand, and follow to wherever it is configured
> or used.

**"by a Module", deliberately — not "by an installed Module".** Catalog membership is a property of the
supplying definition, not of the supplying Module's install/enable state (§17). A Component supplied by a
disabled or uninstalled Module is still a Component; its Module's state is shown as a fact, not used as a
filter. Wording that implied otherwise was corrected on approval (2026-09-22); the §17 lifecycle ruling is
unchanged.

Precise boundaries:

- A Component is a **capability definition**, never a configured instance. `Apperture.Platform.Sql` is a
  Component; the `ProductionDb` Connection configured from it is a Resource.
- A Component is **supplied**, never authored by the operator. Origin is the eligibility test. A user-created
  NotificationDefinition shares the `NotificationDefinition` model and is not a Component — **sharing a model
  does not imply sharing catalog membership.**
- A Component is **curated**. Not every discoverable descriptor qualifies. Permission descriptors, setting
  definitions, capability slots and client factories are deliberately excluded (§5).
- A Component has **no independent lifecycle and no independent persistence**. It exists because its family's
  authoritative registry says so, and it stops existing when that registry stops saying so.
- The catalog is **aggregation, not ownership** — the same durable rule ADR-0040 §6 states and every CTX/HUB/NAV
  closure reaffirms. Config Inventory obeys it; Components obeys it identically.

---

## 7. Component domain / read-model decision

### Selected: Option A — catalog read-model projection

No persisted Component entity. No migration. `ComponentSummary` / `ComponentDetail` are composed at read time
from the authoritative per-family services.

### Why Option B was rejected

Option B needs at least one of: stable cross-family identity, lifecycle/versioning, common configuration,
cross-cutting references, or persistence that can't be modelled otherwise. The audit found **none**:

| Option B justification | Audit finding |
|---|---|
| Stable cross-family identity | `(Kind, NativeId)` is already stable and unique by construction (§8). A synthetic GUID would be a *less* stable second identity requiring reconciliation. |
| Lifecycle / versioning | Connector definitions have no version and are rebuilt in memory at every host start. NotificationDefinition built-ins are migration-seeded and immutable in rule/template. There is nothing to version. |
| Common configuration | Connectors have zero platform-scope configuration. NotificationDefinition settings live on the definition row. Nothing is shared (§16). |
| Cross-cutting references | The only real reference is `Connection.ConnectorDefinitionId`, which already points at the native id. A Component row would be a second hop to the same place. |
| Persistence impossible otherwise | Everything needed is readable at runtime (§2 Q5). |

And the decisive negative: **a Component table would be authoritative for nothing while being writable by
something.** `IConnectorDefinitionRegistry` is populated by hosted services at every start; a mirror table would
need reconciliation on boot, drift detection, and an answer for "what if the registry no longer contains a row
the table does." That is real, permanent cost bought for zero capability.

### Why Option C was rejected

Hybrid persistence ("just the common catalog metadata") is strictly worse here: it introduces the
reconciliation problem *and* leaves detail composition split across two sources, so every read still needs both.
It only pays off when catalog-level metadata is operator-authored and must survive the underlying definition —
which no requirement in this gate asks for.

### Reversibility

Option A is the reversible choice. Adding persistence later is additive: the same `ComponentKey` becomes a
primary key, the same providers become the reconciliation source. Removing a premature table is not.

---

## 8. Identity model

### Scheme

```
ComponentKey = (Kind, NativeId)
```

- `Kind` — a stable lowercase token from `ComponentKinds` (§9).
- `NativeId` — the family's own authoritative identifier, used verbatim, never re-encoded:
  `ConnectorDefinitionId.Value` or `NotificationDefinitionId.Value`.

### Route

```
/components                              → catalog list
/components/{kind}/{nativeId}            → detail
```

Concretely: `/components/connector/Apperture.Platform.Sql`,
`/components/notification-definition/platform.schedule.expected_run_missing`.

### Why this scheme

- **Collision-free by construction.** Different kinds are separated by segment; within a kind the native id is
  already a dictionary key or a SQL primary key.
- **Routable, bookmarkable, testable.** No lookup table, no alias history, no resolution step.
- **ADR-0041 compliant for free.** Tier 1 (canonical identity is the opaque/native id): unchanged — the API and
  every stored reference keep using the native id, and the catalog *is* the native id. Tier 2 (human-facing
  routing prefers a readable natural key): satisfied without building anything, because every v1 Component's
  native id is already `Apperture.Platform.Sql`-shaped. Tier 3 (visible copy uses display names): the list and
  detail render `DisplayName`, with the native id in `TechnicalDetails` per UIX-6.
- **No new identifiers.** Explicitly: **no Component GUIDs**, no slug column, no alias/redirect subsystem —
  ADR-0041 declined that and nothing here reopens it.
- **Future families work unchanged.** Workflow ids, operation ids and procedure ids are all stable dotted
  strings today.

### Rejected alternatives

- **Single canonical string `connector:Apperture.Platform.Sql`** — needs escaping in a path segment for no gain
  over two segments. Keep it as the *logical* key type (`ComponentKey.ToString()`) for logging and test
  assertions, but route on two segments.
- **`ModuleId + local key`** — fails immediately: System Components have no module.
- **Synthetic GUID** — solves no stated requirement, breaks bookmarkability, and would need a persisted mapping,
  i.e. Option B through the back door.

### One thing to verify in smoke

Dotted final path segments can be intercepted by static-file/fallback handling. The precedent is good:
`/modules/{ModuleId}` already routes `Sample.HelloWorld` and shipped through HSK-A's smoke. But
`Apperture.Platform.Sql` ends in `.Sql`, which reads more like an extension. **Verify
`/components/connector/Apperture.Platform.Sql` resolves on the Ui.Server host in the operator smoke.** If it
does not, the fallback is a query-string detail route on the same page — not a change to the identity model.

---

## 9. Kind and origin model

### Kind — string tokens

```csharp
public static class ComponentKinds
{
    public const string Connector = "connector";
    public const string NotificationDefinition = "notification-definition";
}
```

**Why a string token, not an enum.** The house convention for catalog taxonomy is exactly this:
`InventoryTypeTokens`, `NotificationDefinitionOrigins`, `NotificationDefinitionScopes`,
`ModuleContributionDescriptor.Kind`, `ConnectionCapabilityKinds`. Enums in this codebase are reserved for
closed state machines (`ModuleLifecycleState`, `ConnectionState`). A kind taxonomy is explicitly open-ended, and
the token is shared with the Blazor client, the route and the filter query parameter — adding a member to an
enum in that position is a wire-contract change for every consumer; adding a constant is not.

**Guard against stringly-typed drift**: the token set is closed at the server (`ComponentKinds.All`), an unknown
`kind` query value returns `400`, and an unknown kind in a route returns `404` — the same discipline
`InventoryTypeTokens` uses via `Wants(...)`.

### Detail DTO shape — explicit nullable family blocks

```csharp
public sealed record ComponentDetailDto(
    string Kind, string NativeId, string DisplayName, string? Description,
    string Origin, string? SupplyingModuleId, ModuleAttributionDto? SupplyingModule,
    ConnectorComponentDetailDto? Connector,
    NotificationDefinitionComponentDetailDto? NotificationDefinition,
    ComponentUsageDto? Usage);
```

Exactly one family block is non-null, matched to `Kind`. Not polymorphic JSON: it needs no converter, it is
trivially assertable in tests, and with two families the cost of a third nullable property later is one line.
`SupplyingModule` is null unless the caller is authorized to see module metadata (§11, §17).

### Origin — derived, two values

```csharp
public static class ComponentOrigins
{
    public const string System = "system";
    public const string Module = "module";
}
```

**One rule, applied identically to both families:**

```
Origin = SupplyingModuleId is null ? System : Module
```

- Connector: `SupplyingModuleId = ConnectorDefinition.ModuleId`.
- NotificationDefinition: `SupplyingModuleId = NotificationDefinition.ModuleId`.

**No `User` origin.** User-created definitions are filtered out before projection, so the taxonomy never
encounters them. Adding a `User` origin because the underlying model has one would misrepresent the catalog's
contents — the origin taxonomy describes *Component providers*, and an operator is not one.

**Cross-check, not a second rule.** For the NotificationDefinition family, a provider-level assertion must hold:
`Origin == platformBuiltIn ⟺ ModuleId is null` and `Origin == moduleBuiltIn ⟺ ModuleId is not null`. Test it
(§21). If it ever breaks, the data is wrong and should be visible, not silently coerced.

---

## 10. Catalog architecture and API

### Shape

```csharp
// Apperture.Core.Application (contracts)
public sealed record ComponentKey(string Kind, string NativeId);

public sealed record ComponentSummary(
    ComponentKey Key, string DisplayName, string? Description,
    string Origin, string? SupplyingModuleId);

public sealed record ComponentCatalogQuery(
    string? Kind = null, string? Origin = null, string? ModuleId = null,
    string? Q = null, int Take = 200);

public sealed record ComponentCatalogResult(
    IReadOnlyList<ComponentSummary> Items,
    IReadOnlyList<string> AvailableKinds,
    IReadOnlyList<ComponentModuleRef> AvailableModules,
    int TotalMatching);

public interface IComponentCatalog
{
    ValueTask<ComponentCatalogResult> QueryAsync(ComponentCatalogQuery query, CancellationToken ct = default);
    ValueTask<ComponentDetail?> GetAsync(ComponentKey key, CancellationToken ct = default);
}

public interface IComponentCatalogProvider
{
    string Kind { get; }
    ValueTask<IReadOnlyList<ComponentSummary>> ListAsync(CancellationToken ct = default);
    ValueTask<ComponentDetail?> GetAsync(string nativeId, CancellationToken ct = default);
}
```

Implementations live in `Apperture.ControlPlane.Security` beside `InventoryQueryService`, which is the closest
precedent and the closest neighbour: `ConnectorComponentCatalogProvider`,
`NotificationDefinitionComponentCatalogProvider`, `ComponentCatalogService`.

### Why a provider seam at all, for only two families

Because the alternative is worse in a specific, concrete way: `IConnectorDefinitionCatalogReadService` and
`INotificationDefinitionService` have **different authorization failure modes** (`UnauthorizedAccessException`
from different gates), **different filtering capabilities** (in-memory `GetAll` vs SQL `ListAsync` with a
clamped `Take`) and **different mapping rules**. A single service with both inlined mixes those concerns in one
method and makes "omit the family the caller can't read" a tangle of try/catch around unrelated calls. Two
~60-line providers keep each family's quirks local and each independently testable.

Equally: **this is not a plugin framework.** No manifest, no discovery, no ordering, no metadata, no lifecycle.
One property and two methods, registered with `AddScoped<IComponentCatalogProvider, …>()` — exactly the shape
`IConnectorDefinitionContributor` and `IDashboardCardProvider` already use in this codebase.

### Aggregation algorithm — modelled directly on `InventoryQueryService.QueryAsync`

1. For each provider whose `Kind` the query wants (skip the rest entirely — `Wants(...)`), call `ListAsync`
   inside a `try`. On `UnauthorizedAccessException`, **omit that family silently** and record nothing.
2. Concatenate.
3. `AvailableKinds` = distinct kinds **of the items actually collected** — never the static `ComponentKinds.All`
   (§11: otherwise the filter dropdown leaks the existence of a family the caller can't read).
4. `AvailableModules` = distinct `SupplyingModuleId` of collected items, id-only.
5. Apply `Origin`, `ModuleId` and `Q` filters in memory. `Q` matches `DisplayName`, `NativeId` and
   `Description`, case-insensitive substring — the same shape Inventory's `Matches` uses.
6. Sort by `DisplayName`, then `Kind`, then `NativeId` (stable and deterministic; Inventory sorts by name too).
7. `TotalMatching` = post-filter count. Take the page.

Inventory's `EnsureTypeRepresentation` truncation guard is **not needed** — with two families and seven entries
no kind can be crowded out — but the honest `TotalMatching` and cap are kept, matching HSK-D's
"Showing the first N of M" discipline.

### API

```
GET  /api/components?kind=&origin=&moduleId=&q=&take=
GET  /api/components/{kind}/{nativeId}
```

Route `/api/components` on a new `ComponentsController` in `Apperture.Management.Api`, `[Authorize]`, returning
`403` only when the caller can read **no** family (mirroring how `ConnectionsPage` renders a single
`ForbiddenState` when neither tab is permitted), and `200` with a partial result otherwise.

### One additive change to an existing contract

`NotificationDefinitionListQuery.Origin` is single-value equality, so "platformBuiltIn OR moduleBuiltIn" cannot
be asked in one query. Add:

```csharp
IReadOnlyList<string>? Origins = null   // null ⇒ unchanged behaviour
```

to `NotificationDefinitionListQuery`, honoured in `SqlNotificationDefinitionStore.ListAsync` as
`q.Where(x => origins.Contains(x.Origin))`. Additive, default-null, no existing caller affected — the same shape
HSK-D used when it added `InventoryQuery.Types`.

**Do not** reuse `SystemScopedOnly: true` for this. It filters on `Scope != integration`, which selects exactly
the same four rows today — but the approved eligibility rule is **Origin**, and encoding it as a Scope filter
would silently misclassify the first built-in that is ever given Integration scope, or the first user-created
definition given Platform scope. Express the rule that was approved.

---

## 11. Authorization model

### Rule

**No new permission. No new role. Aggregation grants nothing.**

| Family | Gate | Enforced by |
|---|---|---|
| Connector | `connector.definition.read` on `ConnectorDefinition` @ instance | `ConnectorDefinitionCatalogReadService.EnsureCanViewAsync` — already the only path to the registry |
| NotificationDefinition | `notification.definition.read` on `NotificationDefinition` @ instance | `NotificationDefinitionAuthorization.RequirePlatformReadAsync` via `INotificationDefinitionService.ListAsync` |
| Supplying-module metadata | `module.configure` on `Module` @ instance | `ModuleWorkflowListingService.GetModulesAsync` |
| Connector usage (Connections) | `connection.read` | `IConnectionManagementService` |
| NotificationDefinition usage (subscriptions) | `notification.definition.read` (already held to see the Component) | subscription store, gated by the same definition read |

Providers call the **existing authorized services**, never the raw registries or stores. A provider must never
reach `IConnectorDefinitionRegistry.GetAll()` or `INotificationDefinitionStore.ListAsync()` directly — that is
precisely how a catalog would bypass a family gate.

### Why no `component.read` / `component.manage`

A cross-family permission would be *additive* to the family gates or *alternative* to them. Additive means it
changes nothing except adding an administration burden. Alternative means holding it grants sight of connector
definitions to someone `connector.definition.read` denies — the exact leak the brief forbids. There is no
cross-family policy need in evidence. If one ever appears, it is a forcing function then, not a default now.

### Specific leak vectors and their closures

| Vector | Closure |
|---|---|
| Hidden family's **names** appear in the list | Provider is never called; family contributes zero items. |
| Hidden family's **existence** leaks through the Kind filter options | `AvailableKinds` is derived from collected items, never from `ComponentKinds.All`. |
| Hidden family's **existence** leaks through `TotalMatching` | Count is computed after collection, so it counts only authorized items. |
| **Module display name / version / state** leaks to a connector-read-only caller | `ComponentDetailDto.SupplyingModule` is null unless `module.configure`. `SupplyingModuleId` (the bare string) is *already* disclosed at family-read level today by `ConnectorDefinitionCatalogDto.ModuleId` and `NotificationDefinitionDetailDto.ModuleId`, so echoing it is not a new disclosure — but resolving it into module metadata is. |
| **Usage counts** leak Connections the caller can't see | Usage is computed through `IConnectionManagementService`, which applies the caller's own visibility. If `connection.read` is denied, the usage block is **omitted**, not rendered as zero — zero would assert "this connector is unused", which the caller has not earned the right to know. |
| **Module → Components** section on Module detail leaks | Gated per family exactly as the list is; if the caller can read no family, the section is **omitted entirely** (following `ModuleDetail.razor`'s `@if (_capabilities?.CanViewIntegrations == true)` precedent), because an empty state would assert "this module provides none". |
| Detail of a Component in a hidden family | `IComponentCatalog.GetAsync` routes to the provider, which calls the authorized read service, which throws → `404`, not `403`. **404, deliberately**: a 403 confirms the Component exists. |

### UI capability token

The **Catalog** menu leaf needs a visibility signal. Add a **derived token only**:

```
ViewComponents  ⇒  caps.CanViewConnectorDefinitions || caps.CanViewNotificationDefinitions
```

as `DomainMenuCapabilityTokens.ViewComponents`, resolved in `MenusController.HasCapability`'s switch
expression from the two flags **already on `OperatorCapabilitiesDto`**. **No new permission, no new
`EvaluateAsync` call, and no `OperatorCapabilitiesDto` change** — both operands already exist
(`CanViewConnectorDefinitions`, `CanViewNotificationDefinitions`).

**Implementation trap:** `MenusController.HasCapability` ends in `_ => true`, so a token that is added to
`ControlPanelMenuDefinitions` but *not* to the switch is silently permissive and renders for everyone. Adding
the `ViewComponents` arm is mandatory, not optional, and is asserted by test N6 (§21).

**This token gates the `Catalog` leaf only.** The `Modules` leaf keeps `ManageModules`, and the structural
`Components` parent carries **no** `RequiredCapability` — see §18 for why, and for the existing mechanism that
makes that correct.

---

## 12. Component detail UX

### Selected: Option B — uniform catalog wrapper that delegates (D3 — approved)

One route, one shell, a small read-only family fact block, and a prominent link to the authoritative surface.
**Uniform across both families** — every Component gets a wrapper; no family links straight past it.

The wrapper provides exactly: Component identity · kind · origin · supplying-Module attribution where
authorized · read-only family facts · current usage · a link to the authoritative specialized surface. The
existing Notification Definition detail page **remains authoritative for management and diagnostics**.
**No authoring or mutation controls are duplicated onto Component detail** (enforced by tests SD1/SD3, §21).

**Why not A (navigate straight to the specialized page):** there is no connector detail page to navigate to.
Picking A means either building one under `/connections` (which entrenches connectors inside a Resource surface
the catalog is meant to lift them out of) or leaving connector rows dead-ended.

**Why not C (shared shell with a family-section framework):** a section registry for two families is
speculative machinery. The wrapper's family block is one small Razor fragment chosen by `Kind`, not a
registered extension point.

### Shell (both families)

- `PageHeader` — Eyebrow `Components`, Title = `DisplayName`, Subtitle = kind label + origin.
- Pills: `Kind`, `Origin: System | Module`, plus family-specific status pills.
- Description, when the definition has one.
- **Supplied by**: `System (platform)`, or the module — display name + version + lifecycle state when
  `module.configure`, otherwise the bare id with no link.
- **Where used** (§13) — omitted when unauthorized, never rendered as zero.
- `TechnicalDetails` (UIX-6) — native id, `ComponentKey`, kind token, origin token.

### Connector family block

Read-only, sourced entirely from `ConnectorDefinitionCatalogDto`:

- Connection fields table: display name, key, required, secret, description.
- Capabilities, rendered as operator-legible facts — `SupportsConnectionVerification` → "Supports test
  connection". `BrokeredAccessCandidate` is marked reserved/future in code and must **not** be surfaced as a
  capability claim.
- Actions: **Create a connection with this connector** → `/connections/new?connectorDefinitionId=…` when
  `connection.create`; **View connections using it** → §13.

**This is the first connector detail page in the product.** It is read-only because there is nothing to write.

### NotificationDefinition family block

Deliberately thin:

- Facts: primary event type, source kind, scope, mutability, default severity, enabled, Toast default,
  last updated.
- **"Rule and template are fixed for supplied definitions"** — the same truthful statement the authoritative
  page already makes, because the store enforces it.
- One prominent **Open notification definition** link to
  `/config/notifications/definitions/{id}` — where subscriptions, recipient resolution, preview, instances,
  diagnostics and enable/disable already live, correctly authorized.
- **No authoring, no subscription editing, no enable toggle on Component detail.** Every mutation delegates.

### Mutability honesty

Component detail must never imply an operator can change something the domain refuses. Practically: it renders
**no** editable control in v1. The only mutable platform-scope settings that exist for a supplied Component
(`IsEnabled`, `DefaultSeverity`, `ToastEnabledByDefault` on a built-in definition) are the specialized
surface's job — including the gap that two of the three have no UI at all today (§16, §24).

---

## 13. Usage relationships

### Principle

Only **directly attributable, current-configuration** usage. No graph traversal. No historical activity
presented as usage. Nothing on the list — **usage is a detail-only concern** (§20).

### Connector Component → Connections

Direct and real: `Connection.ConnectorDefinitionId` equals the Component's native id.

**v1 ships**: a count plus up to N named links to `/connections/{id}`, and a "view all" link into the
Connections list.

**Implementation**: push the filter into SQL. Add to `IConnectionRepository` a
`GetByConnectorDefinitionIdAsync(ConnectorDefinitionId, …)` reached through
`IConnectionManagementService` so the caller's visibility filter is preserved. **Do not** reuse
`ConnectionsController.List` — it issues `_bindingRepository.GetByConnectionIdAsync(...)` **per connection**
(an N+1 already present on that endpoint) to build the `UsedIn` projection, none of which a usage count needs.

**Not in v1**: Integrations reached indirectly through those Connections. It is a second hop, it is already
answered by the Connection's own "Used in" block, and it would make a connector look busy because one shared
Connection is bound widely.

### NotificationDefinition Component → subscriptions

`INotificationSubscriptionStore.ListByDefinitionAsync(id)` exists today and is direct.

**v1 ships**: a current-subscription count (recipients configured to receive this notification), with a link to
the authoritative detail page's subscriptions section.

**Explicitly excluded from "where used":**

- **`NotificationInstances`** — historical. A dangling `NotificationDefinitionId` after deletion is documented
  and expected. Counting instances would present a disabled, inert definition as in use. The authoritative
  detail page already links instances, correctly labelled as activity.
- **Integration ownership** — no Component-eligible definition has an `IntegrationId` (§4).
- **Per-user preference rows** — a personal setting, not configuration usage.

### HSK-D relationship

Application/Shared Resource Explorer explicitly lists neither Notification definitions, nor Modules, nor
Components. **Components must not depend on it and must not extend it.** Where a real relationship exists, link
to the resource's **existing** surface (`/connections`, `/connections/{id}`). No generic Component→Resource
browser, and no Context filtering on catalog relationships — a Component is a system/catalog concept, exactly as
HSK-A ruled for "Integrations using this module".

---

## 14. Module detail evolution

Add one section to `ModuleDetail.razor`, immediately **above** the shipped "Integrations using this module":

**"Components provided by this module"**

- **Always visible** when the caller can read at least one v1 family; **omitted entirely** otherwise (the
  `CanViewIntegrations` precedent on the same page). Never an empty state for an unauthorized caller.
- **Empty state** when authorized and genuinely none: *"This module provides no catalog components."* True and
  common — only three of five modules supply one today, and none supplies a connector.
- Rows: display name + kind label, linking to `/components/{kind}/{nativeId}`.
- **Same projection as the main catalog**, filtered by `moduleId` — `IComponentCatalog.QueryAsync(new
  ComponentCatalogQuery(ModuleId: id))`. One code path, one authorization story, one set of tests.
- **Global with respect to shell View/Context**, exactly like "Integrations using this module". No `contextId`
  is passed and none should be — Module and Component are both system/catalog concepts. Preserve HSK-A's
  comment block and reasoning verbatim in spirit.

**Do not regress** the existing "Integrations using this module" section: it stays global, stays gated on
`CanViewIntegrations`, and its `contextId: null` call and explanatory comment stay untouched.

Optional, cheap, and recommended: render `ModuleSummaryDto.ContributionCounts` — already on the wire since
before this gate, rendered nowhere — as a small facts row (workflows / operations / permissions / navigation).
It is Module metadata, not Component metadata, and it answers "what does this module contribute" for the four
contribution kinds Components deliberately does not cover.

---

## 15. System Component model

### Decision: origin is derived metadata. There is no System Module, real or virtual.

`Origin = SupplyingModuleId is null ? System : Module`. Nothing is invented; both families already carry the
nullable module id that decides it.

Rejected alternatives: a seeded "System" module row (pollutes `/modules`, breaks lifecycle semantics — what does
disabling it mean? — and lies in `ModuleInstallationStates`); a virtual provider (same lie, with extra
indirection); a separate persisted catalog grouping (Option B via the back door).

### Does the SQL Server Connector qualify?

**Yes.** `Apperture.Platform.Sql` is registered by `SeedWellKnownConnectorDefinitionsHostedService` directly
into the control-plane registry with `ModuleId = null`, carries a real display name ("Platform SQL"), a real
description, six connection field descriptors, and `SupportsConnectionVerification`. It is the archetypal
System Component.

**So do the other two**, and this needs stating precisely: `Apperture.Platform.Sftp` and
`Apperture.Platform.CampusNexus` are contributed by `IConnectorDefinitionContributor` implementations living in
`src/Apperture.Connectors/` — **connector packages, which are not Modules**. They ship with the platform, they
have `ModuleId = null`, and they are correctly System Components. The Connectors tab already renders their
`Source` as `"Platform"`.

**Therefore: three System Connector Components, zero Module Connector Components, today.** The Module path is
fully wired and entirely unexercised. v1 implements it and tests it with a fake contributor; the product copy
must not imply module-supplied connectors exist.

### Built-in Notification Definitions

Verified: `platform.schedule.expected_run_missing` (`platformBuiltIn`, `ModuleId = null`) is a **System**
Component. The three `moduleBuiltIn` seeds carry `Sample.HelloWorld`, `Sample.FileImport`,
`Sample.ApiPolling` — all real, registered module ids — and are **Module** Components.

### Catalog totals at this commit

| Kind | System | Module | Total |
|---|---|---|---|
| Connector | 3 | 0 | 3 |
| Notification definition | 1 | 3 | 4 |
| **Total** | **4** | **3** | **7** |

---

## 16. Configuration model

### Selected: Option A — configuration stays with the specialized domain; Component detail describes and links.

### Connector

**No platform-scope configuration exists.** The registry is metadata-only; the only setting keyed off connectors
is a settings-gallery demo. **Do not invent one.** Component detail renders the definition's own read-only facts
(fields, secrets, required flags, capabilities) and links to the place configuration actually happens —
Connection creation. If a real connector-scoped default ever appears, `IPlatformSettingDefinitionRegistry` plus
the existing `platform.connectorDefinitions` option provider is the natural home, and it would arrive as a
platform setting, not as a Component-settings subsystem.

### Notification Definition

Real platform-scope settings exist, and they live on the definition row:

| Setting | Writable on built-ins (API) | Reachable in UI on built-ins | Owner |
|---|---|---|---|
| `IsEnabled` | Yes | **Yes** — checkbox on the definitions list | `SetEnabledAsync` |
| `DefaultSeverity` | Yes | **No** — authoring form gated to `fullyEditable` | `UpdateAsync` |
| `ToastEnabledByDefault` | Yes | **No** — same gate | `UpdateAsync` |
| `RuleJson` / `TemplateJson` | **No** — store throws | No | immutable by design |
| Subscriptions | Yes | Yes — authoritative detail page | subscription service |

**The gap is real and pre-existing:** ADR-0042 added an administrator-set Toast default so a new notification
doesn't default to silent, and seeded it `true` for exactly the four built-ins — but an administrator cannot
change it afterwards for any of them, because the only editor is behind a `fullyEditable` gate no built-in
passes. Same for `DefaultSeverity`.

**This is not a Components problem and must not be solved by Components (D4 — approved).** The fix belongs on
`NotificationDefinitionDetail.razor` — a small "Platform defaults" block for built-ins exposing exactly the two
fields the store already accepts (`DefaultSeverity`, `ToastEnabledByDefault`), under the existing
`notification.definition.manage` authorization, **preserving built-in rule/template immutability**. It ships as
**CMP-X**, a separate plan (§22). Component detail then links to it, as it links to everything else.

**Editable versions of these controls must not appear on Component detail** — that is what makes CMP-X
necessary rather than optional. Because CMP-3 delegates operators to that specialized page, **CMP-X must be
complete before CMP-3 is considered fully polished**; otherwise CMP-3 sends an administrator to a page that
still cannot change the two settings the Component detail names.

### Not built

No generic key/value Component settings. No uniform "Configuration" tab. A Component with nothing to configure
shows nothing to configure — which is the honest rendering for all three Connector Components today.

---

## 17. Lifecycle, version and provenance

### Lifecycle realities (not invented)

| Event | What actually happens | Catalog consequence |
|---|---|---|
| Module **disabled** | `ModuleInstallationStates.Enabled = false`; `EffectivelyActive = false`; `KnownModuleManifestProvider` stops returning its manifest. Effect is `PartialUntilRestart`. | **Connector definitions are unaffected** — they are seeded by hosted services independent of module state. **`ModuleBuiltIn` NotificationDefinition rows are unaffected** — still present, still `IsEnabled`, merely inert because the module no longer emits the event. |
| Module **uninstalled** | Install row deleted. Effect `RequiresRestart`. Blocked if persisted workflow/operation instances reference its definitions. | Same as above. **The module is still discovered and still listed** (`Discovered` state), because it is compiled in. |
| Module **descriptor missing** (install row survives, code gone) | `descriptor_missing` blocker, `EffectivelyActive = false`. | A `ModuleBuiltIn` definition's `ModuleId` becomes unresolvable. |
| Definition **deleted** | Refused for built-ins. User-created deletion cascades subscriptions/overrides/preferences; instances keep dangling ids. | User-created were never Components. No catalog effect. |
| Connector registry **changes** | Only at host start, by hosted services. Re-registration of the same id overwrites. | Catalog reflects it on the next read. There is nothing to reconcile — which is the whole point of Option A. |
| Module **upgraded** | `Version` on the install row is refreshed on enable/install. | Module attribution shows the new version. No Component versioning. |
| Component **metadata changes** | Connector: a code change, live at next start. NotificationDefinition: a migration or an `UpdateAsync`. | Catalog reflects it on the next read. |

### Ruling: catalog membership does not depend on module enablement

A Component supplied by a disabled or uninstalled module **still appears**, with the module's state shown as a
fact when the caller is authorized to see it.

Why: the underlying row still exists and the underlying definition is still registered. Hiding it would make the
catalog assert something false, and it would make a `ModuleBuiltIn` definition — still `IsEnabled = true`, still
in `/config/notifications/definitions`, still evaluable — invisible in the catalog while visible one menu away.
Two surfaces disagreeing about the same row is worse than one surface showing an accurate, slightly inert row.

### No runtime package lifecycle is invented

Modules are compiled into `Program.cs`. There is no upload, no download, no versioned package feed, no
side-by-side. Install/enable/disable/uninstall toggle a row over a fixed set. **This gate adds no Component
lifecycle actions at all**; every lifecycle control stays on Module detail.

### Version and provenance — only what is trustworthy

| Field | Exists? | Surface it? |
|---|---|---|
| Module display name | Yes (`ModuleDescriptor.DisplayName`) | Yes, **under `module.configure` only** |
| Module version | Yes (`ModuleDescriptor.Version`, `ModuleInstallationStateEntity.Version`) | Yes, **under `module.configure` only** |
| Module lifecycle state | Yes (`ModuleCatalogEntry.State`, `EffectivelyActive`) | Yes, **under `module.configure` only** |
| `ModuleDescriptor.Publisher` | **Hardcoded `null`** by `ModuleDescriptorBuilder` | **No** |
| `ModuleDescriptor.MinimumPlatformVersion` | **Hardcoded `null`** | **No** |
| Connector version | Does not exist | **No** — and do not add one |
| Assembly version | Not exposed anywhere in the management surface | **No** |
| NotificationDefinition `UpdatedAtUtc` | Yes, real, already on the summary DTO | Yes |
| Component version | Does not exist | **No** |

Unresolvable module id: render the id verbatim with no name, no version and no link. Do not fabricate, do not
fall back to "Unknown Module", do not hide the Component.

**One gap to close in implementation:** `NotificationDefinitionSummaryDto` does **not** carry `ModuleId` (only
the detail DTO does), so the provider must project from the domain `NotificationDefinition` returned by
`INotificationDefinitionService.ListAsync` — which does carry it — rather than from the wire summary. No DTO
change is required.

---

## 18. Navigation recommendation

### A factual correction to the stated direction

The brief says *"the current Modules menu will eventually be replaced at the primary navigation level by
Components."* At this commit, **Modules is not at the primary navigation level.** The rail is:

```
[Logo] Control Center
Integrations
Business Data
────────────
[Gear] Config
```

Modules is a **third-level leaf**: `Config → System → Modules` (`/modules`), gated on `ManageModules`. NAV-1
closed the rail deliberately: `SectionOwnerConstants.KnownRailDomains` is `{ Control Center, Integrations,
Business Data, Config }`, `/api/menus` "no longer creates new rail domains", and module contributions naming any
other domain are ignored. The product direction is still achievable — it just describes a *later* promotion, and
that promotion is the navigation / cross-context grouping gate's call (roadmap item 8), not this gate's.

### Approved shape (D1)

Replace the `Modules` leaf with a `Components` node that contains it:

```
Config
  Overview · Inventory · Applications · Governance · Attention
  System
    Platform settings
    Components                       ← new node, ViewComponents
      Catalog        → /components   ← new
      Modules        → /modules      ← unchanged route, unchanged page
    Authentication
    Developer & internal
```

- **One edit to `ControlPanelMenuDefinitions`**: the `Modules` leaf becomes a `Components` parent with two
  children. `Config → System` already nests `Authentication` two deep, so the pattern exists.
- **`/modules` and `/modules/{id}` are untouched.** No redirect, no alias, no broken bookmark, no test churn.
- **No new rail domain**, so NAV-1's ruling and `KnownRailDomains` stay closed.
- **Breadcrumbs** derive from the route (UIX-2/UIX-4), so `/components` and `/components/{kind}/{nativeId}` need
  a `RouteToSectionResolver` entry mapping them to `Config`, alongside the existing `/modules` mapping.

### Menu visibility — the parent must not gate its children

**Approved rule (D1):**

| Node | Visibility |
|---|---|
| `Catalog` | `ViewComponents` (§11) |
| `Modules` | `ManageModules` (unchanged) |
| `Components` (structural parent) | visible whenever **at least one child** is visible |

**The existing menu composition already implements that rule. Nothing new is needed — and, critically, putting
a `RequiredCapability` on the parent would break it.**

`DomainMenuCapabilityFilter.FilterItems`
(`src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/DomainMenuCapabilityFilter.cs`), applied server-side
by `MenusController` before `/api/menus` returns, behaves as follows:

1. **A parent's own `RequiredCapability`, when unheld, drops the parent *and its entire subtree*** — the filter
   `continue`s before recursing into `Children` (lines 47–49). So gating the `Components` parent on
   `ViewComponents` would make an authorized `Modules` child unreachable for an operator holding
   `module.configure` but neither family read. **That is exactly the failure D1 names, and it is real.**
2. **A parent with no `RequiredCapability` is kept whenever any child survives, and dropped when none does**
   (lines 53–54). This *is* "visible whenever at least one child is visible", already implemented.
3. **A parent whose own `Href` duplicates a child's `Href` has that `Href` nulled** when the child is filtered
   out, degrading the parent to an expand-only node rather than a link to a page the backend will refuse
   (lines 56–70).

Rule 3 is the shipped `Config → System` pattern: `System` has `Href = "/config/platform-settings"`, which is
also its `Platform settings` child's href, gated on `ViewPlatformSettings`. `Components` reproduces it exactly:

```csharp
new DomainMenuItem
{
    Id = "config-components",
    Text = "Components",
    Href = "/components",              // mirrors the Catalog child; no RequiredCapability
    Children =
    [
        new DomainMenuItem { Text = "Catalog", Href = "/components",
                             RequiredCapability = DomainMenuCapabilityTokens.ViewComponents },
        new DomainMenuItem { Text = "Modules",  Href = "/modules",
                             RequiredCapability = DomainMenuCapabilityTokens.ManageModules }
    ]
}
```

Resulting matrix, entirely from existing filter behaviour:

| `ViewComponents` | `ManageModules` | Rendered |
|---|---|---|
| ✔ | ✔ | `Components` (links `/components`) → Catalog, Modules |
| ✔ | ✘ | `Components` (links `/components`) → Catalog |
| ✘ | ✔ | `Components` (**href nulled**, expand-only) → Modules — **reachable**, per rule 3 |
| ✘ | ✘ | `Components` dropped entirely, per rule 2 |

`Nav1OperatorInformationArchitectureTests` already covers all three behaviours against the `System` node
(`…_keeps_Attention_href_…`, `…_drops_parent_entirely_when_its_href_child_and_all_other_children_are_gated`,
and the `system.Href.Should().BeNull(...)` assertion). §21 N1–N4 re-assert them for `Components` specifically.

**Do not** put `RequiredCapability` on the `Components` parent, and **do not** add a permission for parent
visibility. The parent is structural; its visibility is derived.

### The existing Connectors tab (D2 — approved)

**Keep it, and link its rows.** `/connections` currently carries a Connectors tab with the only connector list in
the product. Removing it in the same change that introduces the catalog would be a second, unrelated regression
risk on a shipped surface. Instead: make each row's Name link to `/components/connector/{nativeId}`. One-line
change, immediate coherence, nothing to migrate.

**Do not retire or redirect the tab during Components implementation.** Its eventual fate is explicitly
delegated to the **Services design gate**, because Services may reshape the `/connections` experience (§23 Q5,
and the closing constraint of §23).

### Compatibility with the later navigation gate

Nothing here presumes the final IA. `/components` is a top-level route whose menu *placement* is one dictionary
entry. Promoting Components to a rail domain later means adding a `KnownRailDomains` member and moving the
node — the routes, the API, the identity scheme and every page survive untouched.

Roadmap item 8 is **navigation / cross-context grouping design** (renamed 2026-09-22; see
[`pre-release-product-roadmap.md`](../../../00-overview/pre-release-product-roadmap.md)). That gate — not this
one — owns any rail promotion. Note for whoever runs it: the current product direction is to **retain** the
ResourceContext/View selector broadly as-is and design a *separate* organizational grouping layer alongside it,
not to replace the selector with an Application-centric one. Components introduces nothing that depends on
either outcome: the catalog is a global, system/catalog surface and is never Context-filtered (§13, §14).

---

## 19. Resource relationship

**The non-1:1 rule stands, unweakened.** Restated as the design constraint it is:

> Many operational Resources are concrete manifestations of, or are enabled/enhanced by, Components. **This is
> not a one-to-one invariant.** Some Components produce no Resource; some Resources have no originating
> Component.

### Confirmed against the code at this commit

| Resource | Relationship to Components | Evidence |
|---|---|---|
| **Connection** | **Configured manifestation of a Connector Component.** The one real, directly attributable link. | `Connection.ConnectorDefinitionId`; creation validated against `IConnectorDefinitionRegistry`; `SupportsVerification` computed from the definition's capabilities. |
| **Integration** | **Instance of a Module, not of a Component.** *Enabled/enhanced by* connector capability, indirectly, through Bindings. | `Integration.ModuleId`; `Binding(ModuleId, SlotName, ContractType, ConnectionId?, CapabilityId?)`; `ConnectionRequirementDefinition.CapabilityContractType` checked by `BindingEligibilityService`. Nothing catalog-like is instantiated. |
| **Schedule** | **No originating Component.** Subordinate operational configuration. | `ScheduledTarget` / `ScheduledJobType`, optional `IntegrationId`, own `ResourceContextId`. It configures *when*, not *what capability*. |

### Prohibited, and not present in this design

- No `Resource.ComponentId`, on any resource, ever in v1. There is nothing to point at: connector definitions are
  in-memory, and a FK to a non-table is not a thing.
- No `IComponentResource` interface.
- No one-Component-to-one-Resource invariant.
- No generic resource action / command framework.
- No generic lifecycle abstraction over families.
- No generic `Capabilities[]` normalization. `ConnectorCapabilities` stays a connector-family `[Flags]` enum,
  rendered as connector-family facts.

### What is permitted

"Where used" links **where the relationship is real** — which in v1 means exactly one: Connector Component →
Connections. The state vocabularies stay un-normalized (Active/Inactive/Retired vs Active/Retired vs
Enabled/Disabled), exactly as HSK-D deliberately left them.

---

## 20. Performance

### Real scale

Seven Components, five modules, three in-memory connector definitions, four built-in definition rows in a table
capped at 200 per query. Every surface in this design fits one screen. **Server paging is not justified and
must not be built.** Bounded in-memory aggregation with an honest cap and `TotalMatching`, following
`InventoryQueryService` and HSK-D's "Showing the first N of M".

But — per the brief — this was verified rather than assumed, and the verification surfaced the one real risk.

### The one real performance hazard: do not touch the module registry on list

`ModuleWorkflowListingService.GetModulesAsync` calls `DiscoverModulesAsync()` **and then** `ListModulesAsync()`.
Each of those independently runs `DiscoverDescriptorsAsync`, which calls `BuildDescriptorAsync` on **every**
registered module, which **enumerates every workflow definition and every operation** the module provides.
`ListModulesAsync` additionally queries `ModuleInstallationStates` and runs `BuildCrossModuleIssues` over the
full cross-product. So a single `/api/modules` call builds every module descriptor **twice**.

**Therefore:**

- **The Components list must never call the module registry.** It needs only `SupplyingModuleId`, which both
  families already carry on their own rows. `AvailableModules` is a distinct-id projection of collected items —
  ids only, no names.
- **Module display name / version / state are resolved on detail only**, once, and only when the caller holds
  `module.configure`.
- **Module detail's "Components provided" section** calls `IComponentCatalog.QueryAsync(ModuleId: id)` — the
  page has already loaded the module, so it must not re-enter the registry.

### Cost of the list, as designed

| Step | Cost |
|---|---|
| Connector provider | One authorization evaluation + an in-memory `ConcurrentDictionary.Values` walk. No I/O. |
| NotificationDefinition provider | One authorization evaluation + **one** indexed SQL query (`Origins IN (...)`, `ORDER BY DisplayName`, `TOP 200`). |
| Aggregation, filter, sort, page | In memory over ≤ ~200 rows. |
| **Total** | **Two authorization evaluations, one SQL query, zero per-Component queries.** |

**No N+1 by construction**, because no per-item lookup exists in the list path. Assert it (§21).

### Cost of detail

One provider `GetAsync` (dictionary hit, or one `WHERE Id = @id`), plus — only when authorized and only on
detail — one usage query and at most one module lookup.

### Usage counts: detail only

Putting counts on the list would add, per row, either a Connections query or a subscriptions query — the exact
N+1 the brief warns about, for a seven-row table. Omit them from the list. If operators later ask for at-a-glance
usage, the upgrade is a single bounded `GROUP BY ConnectorDefinitionId` and a single `GROUP BY
NotificationDefinitionId`, added behind the same authorization gates — not a schema change.

### Explicitly forbidden

- Calling `IModuleRegistryService` from any list path.
- Reusing `ConnectionsController.List` for usage (it is N+1 on bindings today).
- Loading every Connection to count one connector's usage — push the filter into SQL.
- Any full resource-graph traversal.
- Caching. At this scale a cache is pure invalidation risk: connector definitions change only at host start, but
  notification definitions change at runtime and a stale catalog would contradict the authoritative page.

---

## 21. Test plan

New: `tests/Apperture.UnitTests/ControlPlane/ComponentCatalogTests.cs` (+ provider tests),
`tests/Apperture.UnitTests/ControlPlane/ComponentsControllerTests.cs`,
`tests/Apperture.Management.Ui.Client.Tests/` for page wiring. Existing neighbours to mirror:
`ConnectorDefinitionCatalogReadServiceTests`, `ConnectorDefinitionsControllerTests`, and the Inventory tests.

### Catalog composition

| # | Assertion |
|---|---|
| C1 | System Connector (`Apperture.Platform.Sql`, `ModuleId = null`) appears with `Origin = system`. |
| C2 | Module Connector (fake contributor with `ModuleId = "Fake.Module"`) appears with `Origin = module`, `SupplyingModuleId = "Fake.Module"`. **The only coverage of a path with no production instance.** |
| C3 | `platformBuiltIn` NotificationDefinition appears with `Origin = system`. |
| C4 | `moduleBuiltIn` NotificationDefinition appears with `Origin = module` and its seeded `ModuleId`. |
| C5 | **`userCreated` NotificationDefinition does NOT appear** — in the list, in `AvailableKinds`, in `TotalMatching`, or via direct `GetAsync` by its native id (which must 404). |
| C6 | Eligibility is driven by **`Origin`**, not Scope: a `userCreated` row given `Scope = platform` is still excluded; a `moduleBuiltIn` row given `Scope = integration` is still included. |
| C7 | Origin cross-check for the NotificationDefinition family: `platformBuiltIn ⟺ ModuleId is null`. |
| C8 | Catalog totals at seed state: 3 connector + 4 notification-definition = 7. |

### Identity

| # | Assertion |
|---|---|
| I1 | No `ComponentKey` collision across kinds — two Components sharing a native id in different kinds coexist and resolve independently. |
| I2 | Native ids are preserved verbatim — no casing change, no normalization, no re-encoding. |
| I3 | Detail route is stable and round-trips: `ComponentKey` → route → `GetAsync` → same Component. |
| I4 | Unknown kind → 400 on list, 404 on detail. Unknown native id within a known kind → 404. |
| I5 | A dotted native id (`Apperture.Platform.Sql`) resolves through the route. |

### Filtering

| # | Assertion |
|---|---|
| F1 | `kind=connector` returns only connectors; the notification provider is **not called** (verify on the test double). |
| F2 | `origin=system` / `origin=module` partition the set exactly. |
| F3 | `moduleId=Sample.HelloWorld` returns that module's Components only. |
| F4 | `q=` matches display name, native id and description, case-insensitively. |
| F5 | Combinations compose (AND): `kind` + `origin`, `origin` + `moduleId`, `moduleId` + `q`. |
| F6 | `origin=system` + `moduleId=X` returns empty (contradictory), not an error. |
| F7 | `TotalMatching` reflects post-filter count; the cap truncates `Items` without altering `TotalMatching`. |

### Authorization

| # | Assertion |
|---|---|
| A1 | Connector-read denied → no connector rows, **and `"connector"` absent from `AvailableKinds`**. |
| A2 | Notification-read denied → no notification rows, **and `"notification-definition"` absent from `AvailableKinds`**. |
| A3 | Both denied → 403 from the controller. |
| A4 | `TotalMatching` never counts a hidden family's items. |
| A5 | `GetAsync` for a Component in a denied family returns **404, not 403** (a 403 confirms existence). |
| A6 | Without `module.configure`, `SupplyingModule` is null while `SupplyingModuleId` is present; module display name, version and state are absent from the payload. |
| A7 | Without `connection.read`, the connector usage block is **absent**, not zero. |
| A8 | Usage lists only Connections the caller can see. |
| A9 | The catalog never reaches `IConnectorDefinitionRegistry` or `INotificationDefinitionStore` directly — a source/arch test asserting providers depend only on the authorized read services. |

### Module detail

| # | Assertion |
|---|---|
| M1 | A module supplying Components lists them, linking to `/components/{kind}/{nativeId}`. |
| M2 | A module supplying none shows the empty state (authorized caller). |
| M3 | A caller authorized for no family sees the section **omitted**, not empty. |
| M4 | **"Integrations using this module" is unchanged**: still `contextId: null`, still global across ResourceContexts, still gated on `CanViewIntegrations`. Guard against regression. |
| M5 | The Components section is likewise not Context-filtered. |

### System

| # | Assertion |
|---|---|
| S1 | System Components require no Module row — the catalog resolves them with an **empty** `IModuleRegistryService`. |
| S2 | No synthetic "System" module appears in `/api/modules` or in `AvailableModules`. |
| S3 | `AvailableModules` contains only real supplying module ids, never a System placeholder. |

### Usage

| # | Assertion |
|---|---|
| U1 | Connector → Connections returns exactly the Connections whose `ConnectorDefinitionId` matches. |
| U2 | A Connector with zero Connections shows zero (authorized), not an error. |
| U3 | Retired Connections are included/excluded per the decision taken in implementation, and asserted either way. |
| U4 | NotificationDefinition → current subscription count matches `ListByDefinitionAsync`. |
| U5 | **`NotificationInstances` never contribute to any usage count.** A definition with many historical instances and zero subscriptions reports zero usage. |
| U6 | Usage is **absent from the list payload** for every row. |

### Specialized delegation

*(Test ids are `SD*` so they do not collide with the product rulings `D1`–`D4` in §24.)*

| # | Assertion |
|---|---|
| SD1 | Component detail exposes **no mutation endpoint** — the controller has no POST/PUT/PATCH/DELETE. |
| SD2 | NotificationDefinition Component detail links to `/config/notifications/definitions/{id}`; that page remains authoritative. |
| SD3 | Built-in Component detail renders **no** rule/template editor, no enable toggle, and no editable `DefaultSeverity` or `ToastEnabledByDefault` control (D4). |
| SD4 | Enable/disable, edit and delete of a built-in still route through `INotificationDefinitionService` and still enforce their existing rules (built-in rule/template immutable, built-in delete refused). |

### Navigation visibility (D1)

Extend `Nav1OperatorInformationArchitectureTests` (`Apperture.Management.Ui.Shared.Tests`) and the
`MenusController` tests. These exist specifically because the parent-gate mistake is easy to make and silent.

| # | Assertion |
|---|---|
| N1 | `ViewComponents` ✔ + `ManageModules` ✔ → `Components` present, `Href = "/components"`, both children present. |
| N2 | `ViewComponents` ✔ + `ManageModules` ✘ → `Components` present with `Catalog` only; `Modules` absent. |
| N3 | **`ViewComponents` ✘ + `ManageModules` ✔ → `Components` present with `Modules` reachable, and `Components.Href` is null** (rule 3). The regression guard for D1's stated failure mode. |
| N4 | `ViewComponents` ✘ + `ManageModules` ✘ → `Components` absent entirely from the Config menu. |
| N5 | The `Components` menu item carries **no** `RequiredCapability` (source assertion on `ControlPanelMenuDefinitions`). |
| N6 | `MenusController.HasCapability` resolves `ViewComponents` (i.e. it is **not** falling through to `_ => true`), and resolves it as `CanViewConnectorDefinitions \|\| CanViewNotificationDefinitions` across all four operand combinations. |

### Performance guards

| # | Assertion |
|---|---|
| P1 | A list request issues **at most one** notification-definition query and **zero** per-Component queries (counting test double invocations). |
| P2 | A list request **never invokes `IModuleRegistryService`** — a strict-mock/no-call assertion. This is the §20 hazard, and the test is the only thing that will keep it true. |
| P3 | Module detail's Components section issues one catalog query, not one per Component. |

### Documentation / hygiene guards

| # | Assertion |
|---|---|
| G1 | `RuntimeVisibleDevMetadataGuardTests` extended to the new menu labels and page copy — no `CMP-`/epic labels in operator-facing text. |
| G2 | `Uix6InteractionSystemTests` continues to pass for the new pages (no native confirm, no arbitrary z-index, no new panel/drawer). |
| G3 | No type named exactly `Component` is introduced (source test), per the `ComponentBase` collision ruling. |

---

## 22. Implementation decomposition

Sonnet-sized plans, each independently testable and commit-able. **The Services design gate has now completed
(2026-09-23) and this sequencing condition is satisfied** — it confirmed every settled decision here and required
no architectural amendment, so **CMP-1…CMP-6 are unblocked**. CMP-3's detail surface is unchanged; the Services
gate added only the two follow-ups recorded at the top of this document (Connectors-tab retirement moves to
Services; the `?connectorDefinitionId=` deep link does not yet exist and CMP-3 may add it).

| Plan | Scope | Est. |
|---|---|---|
| **CMP-1 — Catalog read model and API** | `ComponentKey`/`ComponentSummary`/`ComponentDetail`/`ComponentKinds`/`ComponentOrigins` contracts; `IComponentCatalog` + `IComponentCatalogProvider`; both providers; `ComponentCatalogService` aggregation; additive `Origins` on `NotificationDefinitionListQuery` + `SqlNotificationDefinitionStore`; `ComponentsController` (`GET /api/components`, `GET /api/components/{kind}/{nativeId}`); `IManagementClient` methods + DTOs. Tests: composition, identity, filtering, authorization, P1/P2 performance guards. | Medium |
| **CMP-2 — Catalog list page and navigation** | `/components` page (flat table: Name / Kind / Origin / Supplied by / Id, house table conventions); Kind + Origin + Module filters and a debounced search; `ViewComponents` derived capability in `OperatorCapabilitiesQueryService` + `DomainMenuCapabilityTokens`; `ControlPanelMenuDefinitions` Components node; `RouteToSectionResolver` entries. Tests: page wiring, capability gating, menu shape. | Medium |
| **CMP-3 — Component detail** | `/components/{kind}/{nativeId}` shell; connector fact block (fields, capabilities, create-connection link); notification-definition fact block + delegation link; `TechnicalDetails`; module attribution under `module.configure`. Tests: SD1–SD4, A5–A6, I5. | Medium |
| **CMP-4 — Usage relationships** | `IConnectionRepository.GetByConnectorDefinitionIdAsync` + service pass-through with visibility preserved; subscription count for notification definitions; usage block on detail only. Tests: U1–U6, A7–A8. | Small |
| **CMP-5 — Module detail and connector-tab coherence** | "Components provided by this module" section (global, per-family gated, omitted when unauthorized); optional `ContributionCounts` facts row; Connectors-tab rows link to Component detail. Tests: M1–M5, and the M4 regression guard on "Integrations using this module". | Small |
| **CMP-6 — Documentation closure** | ADR for the Components catalog architecture; update this transition note; roadmap item 6 status; `control-panel-ui.md`; glossary; closure audit. | Small |
| **CMP-X — (separate) built-in definition platform defaults** | On `NotificationDefinitionDetail.razor`, a "Platform defaults" block exposing `DefaultSeverity` and `ToastEnabledByDefault` for built-ins under the existing `notification.definition.manage`, preserving built-in rule/template immutability. **Not a Components deliverable** — it closes a pre-existing ADR-0042 gap (§16). | Small |

**Order**: CMP-1 → CMP-2 → CMP-3 → CMP-4 → CMP-5 → CMP-6.

**CMP-X sequencing (D4 — approved)**: CMP-X may run **before CMP-1 or alongside early Components
implementation**, but it **must be complete before CMP-3 is considered fully polished**, because CMP-3's
Component detail delegates operators to that specialized page for exactly the two settings CMP-X exposes.
CMP-X carries no dependency on CMP-1–CMP-2 and can be planned independently.

**Gate sequencing — satisfied (2026-09-23)**: Components design gate → Services design gate → *then*
implementation bodies. Both gates are complete and approved, so CMP-1–CMP-6 may now be planned and started.
CMP-X remains separate and is **not** complete.

---

## 23. Services-gate handoff

Constraints and evidence only. **These are not answered here.**

### 1. Can a Component advertise browse/query/operational capability without a generic `Capabilities[]` framework?

**Evidence**: `ConnectorCapabilities` is a `[Flags]` enum with two real members, one of which
(`BrokeredAccessCandidate`) is documented as reserved and unused; only `SupportsConnectionVerification` drives
behaviour, via `IConnectionVerifierContributor` → `ConnectionsController.IsVerificationSupported`. There is
already a working precedent for "a connector-family capability flag backed by a contributor registry."
**Constraint**: a generic cross-family `Capabilities[]` is prohibited by the approved rulings and by this gate.
Extending the existing connector-family enum is not.

### 2. Where should Service-specific adapters/providers live?

**Evidence**: `IConnectorClientFactoryRegistry` / `IConnectorClientFactoryContributor`
(`src/Apperture.ConnectorResolution/`, `src/Apperture.Connectors/*`) is where **executable** connector
capability already lives, distinct from the metadata-only `IConnectorDefinitionRegistry`. That split — metadata
registry vs client-factory registry — is the existing seam.
**Constraint**: Components owns metadata projection only. It must not acquire an executable surface.

### 3. Does Connector metadata need an optional specialized Service provider?

**Evidence**: `ConnectorDefinition` carries zero executable surface by design (its own XML doc says so), and
`ConnectionRequirementDefinition.CapabilityKind` (`http-api` / `relational-db` / `sftp`) is documented as
"not a universal connector taxonomy". `ConnectorDefinitionId` is stable and already the key everything else
uses.
**Constraint**: if Services needs a per-connector provider, key it on `ConnectorDefinitionId` — the same
`NativeId` this gate routes on. Do **not** add an executable field to `ConnectorDefinition`.

### 4. Is any Client/Browser concept needed at all?

**Evidence**: ADR-0026's `ClientDefinition` was removed by ADR-0027 (Epic 52/53); **zero references remain**
anywhere in the repository — confirmed again at this commit.
**Constraint**: Components v1 depends on no Client/Browser abstraction and introduces none. Whether one is
needed is entirely the Services gate's call, and it would be designed from scratch.

### 5. How does a Service derive its UI/control surface from the backing Connection plus available providers?

**Evidence available to Services**: `Connection(Id, ConnectorDefinitionId, Name, Config, SecretRefs, ScopeKey,
OwnerId, State, LastVerificationStatus/At/Summary, ResourceContextId)`; the connector's
`ConnectionFields`/`Capabilities` reachable by `ConnectorDefinitionId`; `ConnectionsController`'s existing
`UsedIn` projection; `IConnectionResolver` for ephemeral clients.
**Constraint**: whatever Services builds, the **Component catalog must remain readable without it** — a
Component with no Service is normal (three of three connectors today).

### 6. How does Service authorization reuse Connection authorization?

**Evidence**: `connection.read` / `connection.create` / `connection.update` / `connection.delete` on
`ResourceTypeConstants.Connection`; `Connection.ScopeKey` is the authorization scope and is **explicitly
unrelated** to `ResourceContextId` (the domain record says so in a doc comment).
**Constraint from this gate**: no new cross-cutting permission for aggregation. Components declined
`component.read` for exactly this reason; Services should need an equivalent justification before adding
`service.read`.

### 7. How does Service Context derive from the backing Connection?

**Evidence**: `Connection.ResourceContextId` defaults to `ResourceContextIds.Shared`; ownership is orthogonal to
where the originating definition came from (HSK-D finding, reconfirmed here).
**Constraint (already approved)**: derive at point of use. No separate `Service.ResourceContextId` column,
because there is no Service row.

### 8. How should Service experiences link back to Components without persisting a Service→Component relationship?

**Evidence**: `Connection.ConnectorDefinitionId` already *is* the link. A Service over a Connection can reach
its Component with `ComponentKey(ComponentKinds.Connector, connection.ConnectorDefinitionId)` — a pure
construction, no lookup, no join, no new column.
**Constraint**: that derivation is the whole mechanism. Do not persist a Service→Component edge.

### Additional constraints Services must honour

- **No `Resource.ComponentId`**, no `IComponentResource`, no generic action/lifecycle framework (§19).
- **No `Integration → Service → Connection`** persisted chain. Bindings stay Integration → Connection.
- Components' identity scheme `(Kind, NativeId)` is stable and referenceable; Services may key on it.
- Components' list path **must not** be given a per-Component Service lookup (§20 P2 applies equally).
- If Services wants `/connections` restructured, say so in that gate — this gate deliberately leaves the
  Connectors tab in place rather than pre-empting it (§24).

---

## 24. Product rulings (decided)

**All four decisions were reviewed and approved on 2026-09-22. None is open.** They are recorded here as
binding; implementation bodies must not re-litigate them.

### D1 — Navigation placement — **APPROVED: `Config → System → Components`**

Components is placed at `Config → System → Components`, with `Catalog` (`/components`) and `Modules`
(`/modules`) as its children. **No rail domain is created now**; NAV-1's closed `KnownRailDomains` stays
closed. Any later rail promotion belongs to the **navigation / cross-context grouping design** gate (roadmap
item 8), not to Components.

**Visibility rule (corrected on approval):** a gate on the parent must not make an authorized child
unreachable.

- `Catalog` visibility = `ViewComponents`
- `Modules` visibility = `ManageModules`
- the structural `Components` parent is visible **whenever at least one child is visible**

**This is existing behaviour, not new work.** `DomainMenuCapabilityFilter` already drops an ungated parent only
when every child is filtered out, and already nulls a parent's `Href` when the child sharing it is filtered out
— the shipped `Config → System` node relies on both. The `Components` parent therefore carries **no**
`RequiredCapability`. Mechanism, code references, the four-case matrix and the existing test coverage are in
§18; the token definition is in §11. **No new permission was created**; `ViewComponents` is a derived menu
token resolved from two flags already on `OperatorCapabilitiesDto`.

### D2 — Connectors tab on `/connections` — **APPROVED: keep it, link its rows**

The tab stays. Connector rows link to `/components/connector/{nativeId}`. **The tab is not retired or
redirected during Components implementation.** Its eventual fate is explicitly delegated to the **Services
design gate**, because Services may reshape the `/connections` experience (§18, §23).

### D3 — Component detail for Notification Definitions — **APPROVED: uniform wrapper**

Every Component uses the same Component detail wrapper, which provides identity, kind, origin,
supplying-Module attribution where authorized, read-only family facts, current usage, and a link to the
authoritative specialized surface. The existing Notification Definition detail page **remains authoritative for
management and diagnostics**. **Authoring and mutation controls are not duplicated onto Component detail**
(§12; tests SD1/SD3 in §21).

### D4 — Built-in Notification Definition defaults gap — **APPROVED: separate, as CMP-X**

The gap is a pre-existing Notification Definition UI limitation, not a Component responsibility. **CMP-X**
exposes the already-supported built-in metadata controls — `DefaultSeverity` and `ToastEnabledByDefault` —
under the existing `notification.definition.manage` authorization, while **preserving built-in rule/template
immutability**.

Sequencing: **Services design gate → then implementation bodies.** CMP-X may run before CMP-1 or alongside
early Components implementation, but **must be complete before CMP-3 is considered fully polished**, because
CMP-3 delegates operators to that page. **Editable versions of these controls must not be placed on Component
detail** (§16, §22).

---

## 25. Risks

| # | Risk | Likelihood | Mitigation |
|---|---|---|---|
| R1 | **Scope creep into a third family.** Workflow/Operation/Procedure all fit the model, which makes them tempting. | Medium | v1 kinds are a closed constant set; adding one is a deliberate product decision with its own plan. §5 records the classification so the argument isn't re-run. |
| R2 | **The catalog calls the module registry on list**, making a seven-row page rebuild every module descriptor twice. | **High** — it is the obvious-looking implementation. | Test P2 (strict no-call assertion). Stated as a forbidden pattern in §20. Module ids come from the definition rows, never from the registry. |
| R3 | **`AvailableKinds` built from `ComponentKinds.All`**, leaking the existence of a family the caller can't read. | Medium | Tests A1/A2 assert absence from `AvailableKinds`, not just from `Items`. Inventory's `availableTypes` is the correct precedent to copy. |
| R4 | **Eligibility implemented as a Scope filter** (`SystemScopedOnly`) because it selects the same rows today. | **High** — it is a one-word change that passes every current test. | §10 forbids it explicitly; test C6 uses deliberately contradictory Scope/Origin fixtures that only an Origin predicate passes. |
| R5 | **A dotted native id fails to route** on the Ui.Server host (`/components/connector/Apperture.Platform.Sql`). | Low — `/modules/Sample.HelloWorld` is the shipped precedent | Named as a smoke item (§8). Fallback is a query-string detail route; the identity model is unaffected either way. |
| R6 | **Module-supplied connectors are untested in production** — the path exists and nothing exercises it. | Certain (it is true today) | Test C2 with a fake contributor is the only coverage. Product copy must not claim module connectors exist. |
| R7 | **`ModuleBuiltIn.ModuleId` has no referential integrity** — a seeded string with no FK, no reconciliation, no test. | Medium over time | Test C7 (origin/module cross-check) plus a test asserting each seeded `ModuleId` matches a registered module id. Unresolvable ids render verbatim (§17). |
| R8 | **Duplicated authoring creeps onto Component detail** — "just add the enable toggle, it's one line." | Medium | Tests SD1 (no mutation endpoint on the controller) and SD3 make the shortcut fail loudly rather than quietly. D3 and D4 forbid it as rulings. |
| R9 | **Usage counts migrate onto the list** for polish. | Medium | Test U6 asserts usage is absent from list payloads. §20 documents the upgrade path if it is ever genuinely wanted. |
| R10 | **Services changes the detail surface after CMP-3 ships.** | Medium | Mitigated by sequencing: both gates run before either implementation. If CMP-3 must move, the identity scheme, API and catalog survive — only the detail body changes. |
| R11 | **`Component` type-name collision with Blazor `ComponentBase`.** | Low | No type is named bare `Component`; test G3 enforces it. |
| R13 | **`RequiredCapability` put on the `Components` menu parent**, silently hiding an authorized `Modules` child from an operator holding `module.configure` but neither family read. | **High** — it reads as the natural way to gate a node, and it fails only for a narrow operator profile that manual smoke is unlikely to use. | D1 forbids it; §18 documents the filter's short-circuit (`continue` before recursing); test N3 is the direct regression guard and N5 asserts the parent carries no token. |
| R14 | **`ViewComponents` added to `ControlPanelMenuDefinitions` but not to `MenusController.HasCapability`**, whose `_ => true` fallback renders the Catalog leaf for every operator. | Medium | Test N6 asserts the token resolves rather than falling through. |
| R12 | **Catalog and Notification Definitions page disagree** about a Module-supplied definition when its module is disabled. | Low | §17 rules that membership does not depend on module enablement, so both show it; the catalog adds module state as a fact rather than hiding the row. |

---

## 26. Readiness

**The Components design is approved. The Services design gate has now completed and confirmed it. No product
decisions remain open** — D1–D4 are recorded as decided in §24. **CMP-1…CMP-6 are unblocked and are the next
implementation body.**

What is settled and will not need to be reopened by Services: the projection-vs-persistence decision, the
identity scheme, the kind/origin taxonomy, the authorization model, the navigation placement and its visibility
rule, the non-1:1 Resource rule, and the performance constraints. Each is grounded in code at `f3ec369` rather
than in prior planning prose.

What Services could legitimately have changed — and what it actually decided (2026-09-23): the **content of
Component detail** (outcome: **no change**; no capability advertisement in Components v1, so `ComponentSummary`
and `ComponentDetailDto` are untouched), and the **fate of the `/connections` Connectors tab**, which D2
delegated to it (outcome: **keep it during Components exactly as CMP-5 plans; retire it during Services, in
SVC-4**). Neither touched the catalog's shape, its API, its identity or its authorization — which is the property
that made running the Services gate next safe, and which held.

**§23 was the authoritative handoff** to the Services design gate, which consumed it in full along with §19's
prohibitions. All eight questions are answered in [`services-design-gate.md`](./services-design-gate.md), and
§19's prohibitions were re-confirmed rather than relaxed.

**Nothing here is implemented.** No branch, no code, no nav change, no schema, no new permission. Roadmap item 4
(Components design gate) is **complete**; item 5 (Services design gate) is **complete**; item 6 (Components
implementation) is **next**; item 7 (Services implementation) is **not started**. CMP-1 through CMP-6 and CMP-X
are planned, not begun.
