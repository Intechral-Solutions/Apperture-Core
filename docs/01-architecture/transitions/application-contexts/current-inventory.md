# Current architecture inventory (Application / Context planning)

Evidence snapshot of Apperture Core as implemented. **No roadmap. No proposed design.** Items are **CONFIRMED** unless marked **SPECULATION**.

How to read this file:

- **CONFIRMED** — observed in code, schema, tests, or host wiring.
- **SPECULATION** — inference, docs-only, or “would break if…”.
- Stale docs are called out when they disagree with code. Code wins.

Canonical SQL model: `Apperture.Persistence.SqlServer.AppertureDbContext`. `Apperture.RuntimePlane.Persistence.SqlServer.AppertureDbContext` is a thin subclass with no extra tables.

---

## Confirmed absences (relevant to the planned direction)

These concepts do **not** exist as platform entities, columns, or loaded SPIs today:

| Concept | What exists instead |
|---------|---------------------|
| `ApplicationContext` / `ContextId` / `ApplicationContextId` | Nothing. The only `AuthenticationContextId` is Entra Conditional Access `c1`–`c25` on `EntraInstanceRegistrations`. |
| `ApplicationId` | No column, entity, or property under `src/`. |
| Platform `BusinessRecord` table | `DemoBusinessRecordProjection` is a read-time view over `OperationItems`. |
| `IResourceCatalogProvider` | Named only as future work in `docs/01-architecture/platform-settings-option-providers-and-scopes.md`. No type in `src/`. |
| Visual workflow designer | Workflows are code-declared graphs. `/workflows/start` is a stub to `/execute?kind=workflow`. |
| Operator “Services” domain | `ResourceTypeConstants.ServiceAccount` is an auth resource type. There is no Service aggregate, catalog, or UI. |
| SQL FKs among Integrations / Connections / Schedules / Runs / Notifications | Almost none. Correlation is string columns + indexes. |
| Loaded module UI | `IModuleNavigationProvider` exists; no production module implements it. `Sample.HelloWorld.Ui` is not referenced by any host. |

**CONFIRMED:** the product is a **single-tenant Instance**. There is no Apperture tenant FK on integrations, runs, or settings. `TenantId` on identity tables is the **Entra directory tenant**.

---

## 1. Management UI navigation

**Runtime source of truth:** `ControlPanelMenuDefinitions.GetDefaultMenus()` in `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ControlPanelMenuDefinitions.cs`.

The Blazor client seeds that tree, then replaces it with `GET /api/menus` (`MenusController`). All operator pages below are **Blazor WASM** (`Apperture.Management.Ui.Client`), served by `Apperture.Management.Ui.Server`. Host cookie/OIDC paths (`/signin`, `/signout`, `/signin-oidc`) are ASP.NET endpoints on `Apperture.Management.Host`, not Blazor pages.

**Docs lag (CONFIRMED, not used as truth):**

- `docs/01-architecture/control-panel-ui.md` still puts Attention under Operations and omits Config’s Attention / Platform maintenance / Developer groups.
- `docs/01-architecture/transitions/nav-overhaul/before.md` is close but stale on `/access-request` (that page exists) and on `/demo` as a Config rail domain (`RouteToSectionResolver` does not map `/demo`).

**Nav-item feature flags:** none. **Nav-item permission filters:** none. `GET /api/menus` is `[Authorize]` and may append enabled-module contributions; it does **not** filter by `OperatorCapabilitiesDto`. Pages hide **content** after load (ForbiddenState / HTTP 403). Operators without a permission still see the nav entry.

### Chrome and navigation components

| Role | Class / file |
|------|----------------|
| Router | `App.razor` — `Router` + `RouteView` `DefaultLayout=MainLayout`, wrapped in `AuthRouteGate` |
| Root bounce | `Routes.razor` (`@page "/"`) → `OperatorLanding` → `/dashboard` |
| Authenticated shell | `MainLayout.razor` — rail + Control Panel + top bar |
| Config content wrapper | `ConfigLayout.razor` — `@layout MainLayout`; CSS wrapper only, **no extra sidebar** |
| Auth / public shell | `AuthLayout.razor` — no rail, no top bar |
| Rail | `NavRail.razor` + `NavRailItem.razor` |
| Sliding menu | `ControlPanel.razor` + `ControlPanelNavNode.razor` |
| Menu model | `DomainMenu`, `DomainMenuItem` |
| Menu load | `DomainMenuService` (seeded defaults, then `/api/menus`) |
| Rail domain from URL | `RouteToSectionResolver` + `NavPathMatching` + `DomainMenuTree` |
| Top bar | `TopBar.razor` — hamburger, `TopBarOrientation` (breadcrumbs), `ThemeMenu`, `PersonalNotificationMenu`, `UserAccountMenu` |
| Account tabs | `AccountSectionNav.razor` + `AccountUiRoutes` |
| Breadcrumbs | `ManagementBreadcrumbResolver` (not a menu) |
| Public-path list | `LoginRouteHelper.PublicPaths` |

**Rail construction (`NavRail.BuildMenuItems`):**

1. Logo (top) → Dashboard; click opens Control Panel for `"Dashboard"` and navigates to `/dashboard`.
2. Middle loop: `DomainMenuService.GetMiddleDomains()` = every domain except `"Dashboard"` and `"Config"` (`SectionOwnerConstants`).
3. Gear (bottom, hardcoded) → `"Config"`; click opens Control Panel and navigates to `/config`.

Default middle order is dictionary insertion order: **Operations → Automation → Integrations → Connections**.

### Hierarchy (Control Panel / rail)

```
Nav rail
├── [logo] Dashboard                          SectionOwnerConstants.Dashboard
│   └── Dashboard home                        /dashboard
├── Operations
│   ├── Control Center                        /control/center
│   ├── Blocked Work                          /control/blocked-work
│   ├── Execute                               /execute
│   └── Audit                                 /control/audit
├── Automation
│   ├── Runs                                  /runs
│   └── Schedules                             /schedules
├── Integrations
│   └── Integrations                          /integrations
├── Connections
│   └── Connections                           /connections
└── [gear] Config                             SectionOwnerConstants.Config
    ├── Overview                              /config
    ├── Authentication                        /config/auth
    │   ├── Sign-in experience                /config/auth/sign-in
    │   └── Entra tenants                     /config/auth/tenants
    ├── Governance                            /config/governance
    │   ├── Permission catalog                /config/governance/catalog
    │   ├── Roles                             /config/governance/roles
    │   └── Users                             /config/governance/users
    ├── Notifications                         /config/notifications
    │   ├── Definitions                       /config/notifications/definitions
    │   ├── Instances                         /config/notifications/instances
    │   └── Delivery                          /config/notifications/delivery
    ├── Attention                             /attention/operational-teams   (Id: config-attention)
    │   ├── Operational teams                 /attention/operational-teams
    │   ├── Routing defaults                  /attention/routing-defaults
    │   └── Managed contacts                  /config/contacts
    ├── Platform maintenance                  /config/platform-settings      (Id: config-platform-maintenance)
    │   ├── Platform settings                 /config/platform-settings
    │   └── Modules                           /modules
    └── Developer & internal                  /config/developer              (Id: config-developer)
```

**Not in this tree:** create/detail routes, diagnostics, demo customers, account, inbox, auth pages, module-contributed items (runtime only).

### Rail domain vs URL prefix (`RouteToSectionResolver`)

| Path prefix | Rail domain |
|-------------|-------------|
| `/config…` | Config |
| `/modules…` | Config |
| `/attention…` | Config |
| `/` or `/dashboard…` | Dashboard |
| href match under Operations | Operations |
| `/runs…`, `/schedules…` | Automation |
| `/integrations…` | Integrations |
| `/connections…` | Connections |
| `/account…`, `/me…`, `/demo…`, `/runtimes…`, `/workflows…`, `/operations/start` | **empty** (no rail highlight) |

Confirmed by `ShellSectionOwnerTests.RouteResolver_DemoNestedNav_NotInMenu_ReturnsEmpty`. `/demo/customers` uses the same resolver → empty rail. Breadcrumbs still hang Demo Customers off Config → Developer.

### Surfaces by area

Legend: **menu** = Control Panel item; **hub** = landing card; **child** = reachable from a list/detail; **alias** = redirect; **public** = AuthLayout.

#### Operations / Control Center

Layout: default `MainLayout`.

| Label | Route | Page class | Kind | Page gate |
|-------|-------|------------|------|-----------|
| Control Center | `/control/center` | `ControlCenter` | menu | Page always loads; health/scheduler UI gated by `CanViewRuntimeHealth` |
| Blocked Work | `/control/blocked-work` | `BlockedWork` | menu | `CanManageRecovery` only for “Trigger scheduler cycle” |
| Execute | `/execute` | `ExecutionStart` | menu | No capability prefetch in the page |
| Audit | `/control/audit` | `AuditTimeline` | menu | HTTP 403 → forbidden UI, not a nav hide |
| Start operation (legacy) | `/operations/start` | `OperationStart` | child | Stub linking to `/execute?kind=operation`. Off-rail |
| Shutdown requests | `/runtimes/{RuntimeId}/controls/shutdown/requests` | `RuntimeShutdownRequestsList` | child | Off-rail |

Control Center hub cards also link Integrations, Runs, Schedules, Blocked work, Audit (cross-domain).

#### Config

Landing: `/config` (`ConfigLanding`) uses `ConfigLayout`. Hub cards: Authentication, Governance, Notifications, Attention, Platform settings, Modules, Developer tasks.

#### Authentication (Config)

`ConfigLayout`. Overview `/config/auth` gated by `CanViewAuthConfiguration \|\| CanManageAuthUiPolicy \|\| CanManageEntraTenants`. Child: Sign-in `/config/auth/sign-in`, Entra tenants `/config/auth/tenants`, directory users/groups under `/config/auth/entra/{RegistrationId}/…`. Alias: `/config/security/sign-in` → `/config/auth/sign-in`.

#### Governance (Config)

Permission catalog, Roles, Users at `/config/governance/…`. Access requests `/config/governance/users/requests` and Add from Entra `/config/governance/users/from-entra` are toolbar children, **not** Control Panel items. Aliases: `/governance/…` → `/config/governance/…`.

#### Notifications (three surfaces)

1. **Platform admin** — Config → Notifications (`ConfigLayout`): definitions, instances, delivery. Diagnostics `/config/notifications/diagnostics` is a hub card, **not** a Control Panel child. Gate: `CanViewNotificationDefinitions` (diagnostics also requires `CanViewIntegrations`).
2. **Personal inbox** — top-bar bell → `/me/notifications`. Off-rail.
3. **Per-integration** — `/integrations/{id}/notifications`. Gate: `CanViewIntegrations && CanViewNotificationDefinitions`.

#### Attention

Parent: Config Control Panel group **Attention** (not Operations). `/attention/*` uses default `MainLayout` (no `ConfigLayout`); `/config/contacts` uses `ConfigLayout`. Rail still highlights Config. Create/edit teams gated by `CanManageSchedules`. Contacts gated by `CanViewManagedContacts`. Alias: `/attention/contact-groups` → operational teams.

#### Platform maintenance

- Platform settings `/config/platform-settings` (`PlatformSettings`, `ConfigLayout`) — `CanViewPlatformSettings \|\| CanManagePlatformSettings`.
- Modules `/modules` (`ModulesList`, default `MainLayout`) — **no page-level capability check**; lifecycle actions use `CanManageModules`.

#### Developer & internal

Landing `/config/developer` (`DeveloperTasks`). **No capability gate on the hub.** Cards include deferred dashboard manage/add/delete/layout, Control Center, notification diagnostics, nested-nav demo `/demo/nested-nav`, Demo Customers `/demo/customers`, account preferences.

#### Integrations / Connections / Schedules / Runs

| Surface | List route | Page | Gate notes |
|---------|------------|------|------------|
| Integrations | `/integrations` | `IntegrationsList` | `CanViewIntegrations`; create `CanManageIntegrations`. Detail `{IntegrationId}` plus `/settings`, `/notifications`, `/schedule-diagnostics`. |
| Connections | `/connections` | `ConnectionsPage` | `CanViewConnections \|\| CanViewConnectorDefinitions`. In-page Connectors tab `?tab=connectors`. |
| Schedules | `/schedules` | `SchedulesList` | No view-cap on list; create `CanManageSchedules`. |
| Runs | `/runs` | `RunsList` | **No capability check on the page.** |

#### Audit Log

Operations menu label **Audit**. Route `/control/audit`, page `AuditTimeline`. Not under Config.

#### Business-data / demo-customer

**No** `/business-data` or `/records` Blazor routes. Demo projection only:

| Label | Route | Page | Layout | Gate |
|-------|-------|------|--------|------|
| Demo Customers | `/demo/customers` | `DemoCustomersList` | `ConfigLayout` | `CanViewIntegrations` |
| Detail | `/demo/customers/{BusinessKey}` | `DemoCustomerDetail` | `ConfigLayout` | same |
| Activity | `…/activity` | `DemoCustomerActivity` | `ConfigLayout` | same |

API: `demo/business-records/customers` via `ManagementClient`. Comments in `DemoCustomerUiRoutes` / `DeveloperTaskRoutes`: not final Records nav. Breadcrumbs: Config → Developer → Demo Customers. **Rail domain empty.**

#### Personal account and public auth

Account tabs from `UserAccountMenu`: `/account/profile`, `/account/notifications`, `/account/preferences`, `/account` (security). Inbox `/me/notifications` is the bell, not a tab.

Public `AuthLayout` (`LoginRouteHelper.PublicPaths`): `/login`, `/login/local`, forgot/reset/setup password, `/account/verify-email`, `/access-request`. Host endpoints: `GET /signin`, `/signin/link`, `/signin/step-up`, `/signout`, `/signin-oidc`.

#### Module-contributed nav

`MenusController.GetMenus` starts from `GetDefaultMenus()`, then appends `NavigationContributionDescriptor` from **enabled** modules (`IModuleNavigationProvider` → `Domain`, `Label`, `Route`). Duplicate hrefs skipped. New domain keys become extra middle-rail icons.

**CONFIRMED:** interface exists; **no production module implements it** (only unit-test stubs). Live extra rail items depend on enabled modules at runtime.

---

## 2. Domain / database relationships

**ID convention (most operator objects):** `Guid.NewGuid().ToString("N")[..24]` — 24 hex chars, **not** a `uniqueidentifier` column. Stored as `nvarchar(64)` string PKs.

**SQL FKs among the asked objects:** almost none. `IntegrationId` / `ModuleId` / `ConnectionId` / `ScheduleId` / `RunId` are **string columns + indexes**, not `HasOne`/`HasForeignKey`. Confirmed EF FKs exist for identity, teams/contacts, and operation-item children only.

### Ownership map (logical, not SQL-enforced)

```
IModule / ModuleManifest (code; Id is a dotted string)
    └── ModuleInstallationStates  (PK: EnvironmentId + ModuleId)
            └── Integrations.ModuleId  (string; many integrations per module)
                    ├── PlatformSettingValues  (ScopeKind=Integration, ScopeId=Integration.Id)
                    ├── Bindings.IntegrationId → Connections.Id  (or CapabilityId)
                    ├── ScheduleDefinitions.IntegrationId
                    │       ├── ScheduleAssignments.ScheduleId → RuntimePlaneId
                    │       │       └── ScheduleStates.AssignmentId (1:1 PK)
                    │       └── ScheduleRuns (ScheduleId, FireTimeUtc unique)
                    │               └── ScheduleRuns.RunId → OperationRuns.Id  (string, no FK)
                    ├── WorkflowStartRequests / OperationStartRequests.IntegrationId
                    ├── WorkflowInstances.IntegrationId
                    ├── OperationRuns.IntegrationId
                    │       └── OperationGroups.RunId  (EF FK: Group → Items → retries/acks)
                    ├── AttentionIncidents.IntegrationId
                    ├── NotificationDefinitions.IntegrationId? / ModuleId?
                    ├── NotificationSubscriptions.IntegrationId?
                    ├── NotificationInstances (def + optional Integration/Module/Run/Schedule/Event)
                    └── PlatformEvents.IntegrationId? / ModuleId? / RunId? / ScheduleId?

Connections  — instance-scoped; NOT owned by Integration
    └── Bindings.ConnectionId  (many bindings can share one connection)

AuditLogs  — append-only; optional ModuleId / ScheduleId / RunId; NO IntegrationId

DemoBusinessRecordProjection  — NOT a table; read model over OperationItems
    (SharedRecordType + SharedBusinessKey / SubjectKey)
```

### Cross-cutting ID columns

| Column | Present on | Meaning |
|---|---|---|
| **ModuleId** | Integrations (required), Bindings (required), ModuleInstallationStates (PK part), NotificationDefinitions/Instances, PlatformEvents, AuditLogs, OperationGroups | Dotted string (`Sample.ApiPolling`). **Not a Guid.** |
| **IntegrationId** | Bindings, Schedules, Workflow/Operation start+instances+runs, OperationGroups, Attention*, Notifications*, PlatformEvents, PlatformSettingValues.ScopeId when ScopeKind=Integration | 24-char hex string. **No SQL FK to Integrations.** |
| **EnvironmentId** | `ModuleInstallationStates` (composite PK), `EnrollmentTokens` | Process string from `APERTURE_ENVIRONMENT_ID`, default `"local"`. Domain `Environment` record exists; **no Environments table.** |
| **TenantId** | `IdentityLinks`, `EntraAccessRequests` | **Entra directory tenant**, not an Apperture tenant. Also `EntraInstanceRegistrations.AzureTenantId`. |
| **AuthenticationContextId** | `EntraInstanceRegistrations` | Entra step-up context `c1`–`c25`. **Not** a platform ContextId. |
| **ApplicationId** | — | **CONFIRMED absent.** |
| **ContextId** | — | **CONFIRMED absent** as a platform column. |

### Per-object IDs, names, uniqueness

#### Module definitions

**Not a SQL definitions table.** Modules are compiled `IModule` + `ModuleManifest` (`src/Apperture.ModuleSdk/Apperture.ModuleSdk.Abstractions/ModuleManifest.cs`).

- Stable id: manifest `Id` string, e.g. `Sample.ApiPolling`, `Sample.FileImport`, `Sample.HelloWorld`, `Proof.CampusNexus.StudentReview`, `Unisa.CampusNexus.PaymentPlans`.
- Name: manifest `Name` (display); **not unique in SQL**.
- Persistence: `ModuleInstallationStateEntity` → **`ModuleInstallationStates`**. Composite PK `EnvironmentId + ModuleId` (migration `20260321183051_AddModuleInstallationStates`).
- **No FK** from `Integrations.ModuleId` → `ModuleInstallationStates`.

Connector **table was dropped** in `20260324205713_DropConnectorDefinitionsTable`. Connector kinds live in `IConnectorDefinitionRegistry` (in-memory).

#### Configured Integrations

- Domain: `Apperture.Core.Domain.Integration.Integration`.
- Table: **`Integrations`** / `IntegrationEntity`.
- PK: `Id` `nvarchar(64)`, generated `Guid.NewGuid().ToString("N")[..24]` in `IntegrationManagementService`.
- Name: `DisplayName` (required, 256). **No unique index.** No slug. `ScopeKey` is an **authorization** scope; default `"instance"`.
- Other: `ModuleId` required; `State`; legacy `SettingsJson`; `RowVersion`.
- **Owns by column, not FK:** Bindings, ScheduleDefinitions, start requests, workflow instances, operation runs/groups, attention, notifications, platform events, `PlatformSettingValues` (`ScopeKind=Integration`).

#### Connections

- Domain: `Connection` + `ConnectionId`.
- Table: **`Connections`**. PK only uniqueness. `Name` **not unique**.
- Kind: `ConnectorDefinitionId` string (registry, not SQL), e.g. `Apperture.Platform.Sql`.
- Scope: `ScopeKey`, `OwnerId` — instance-level, **no IntegrationId**.

#### Bindings (only persisted Integration ↔ Connection link)

- Table: **`Bindings`**. Columns: `IntegrationId?`, `ModuleId`, `SlotName`, `ContractType`, `ConnectionId?`, `CapabilityId?`, `State`.
- **No unique index** on `(IntegrationId, ModuleId, SlotName)`.
- **App-level (CONFIRMED):** `BindingService` rejects a second **Active** binding for the same slot+integration (`GetBySlotAndIntegrationAsync`).

#### Schedules / jobs

**No `Jobs` table.** Target is `ScheduledJobType` on `ScheduleDefinitions.TargetJobType`.

| Object | Table | PK | Notes |
|---|---|---|---|
| Definition | `ScheduleDefinitions` | `Id` 24-char hex | Optional `IntegrationId`. `Name` **not unique**. |
| Assignment | `ScheduleAssignments` | `Id` string | `ScheduleId` + `RuntimePlaneId`. Index on RuntimePlaneId **not unique**. |
| State | `ScheduleStates` | `AssignmentId` | 1:1 with assignment. **No FK.** |
| Firing | `ScheduleRuns` | `bigint` identity | Unique **`IX_ScheduleRuns_ScheduleId_FireTimeUtc`**. Optional `RunId` → `OperationRuns.Id`. |

#### Runs / operations

**A. Workflow-centric (primary)**

| Table | PK | Links |
|---|---|---|
| `WorkflowInstances` | string `Id` | `DefinitionId` (in-memory workflow id), optional `IntegrationId`, `ProcedureId` |
| `OperationRuns` | string `Id` | `WorkflowInstanceId`, `DefinitionId`, optional `IntegrationId`, `ProcedureId`, `RetryOfRunId`, `ExecutionOrigin` |
| `WorkflowStartRequests` | **PK = `RunId`** | unique filtered `IdempotencyKey` |
| `OperationStartRequests` | **PK = `RunId`** | same idempotency pattern |

Ids: `WorkflowStartDispatchService.NewId()` = 24-char hex. **Instance id ≠ run id.**

**B. Itemization**

| Table | PK | Uniqueness |
|---|---|---|
| `OperationGroups` | string | Index on `RunId` (**not unique**). **EF FK** to items. |
| `OperationItems` | string (full Guid N, 32 hex) | Indexes on RunId, SubjectKey; **non-unique** `IX_OperationItems_SharedRecordType_SharedBusinessKey_CompletedAtUtc` |
| `OperationItemRetryAttempts` | string | Unique `(OperationItemId, IdempotencyKey)` and `(OperationItemId, AttemptIndex)` |

**No ModuleId on OperationRuns/WorkflowInstances** — ModuleId is on `OperationGroups` and on events/notifications/audit.

#### Notifications

| Object | Table | PK format | Unique constraints |
|---|---|---|---|
| Definition | `NotificationDefinitions` | built-in dotted ids **or** `ndef_{Guid:N}` | **`IX_NotificationDefinitions_Scope_IntegrationId_DisplayName` unique** where `Origin = N'userCreated'` |
| Subscription | `NotificationSubscriptions` | `nsub_{Guid:N}` | Unique recipient combos with/without IntegrationId |
| Instance | `NotificationInstances` | `ninst_{Guid:N}` | **`IX_NotificationInstances_NotificationDefinitionId_InstanceDedupeKey` unique** |
| Outbox | `NotificationDeliveryOutbox` | `nout_{Guid:N}` | Unique `(NotificationInstanceId, RecipientEmail, Channel)` |
| Inbox | `NotificationUserInboxItems` | string | Unique `(PrincipalUserId, NotificationInstanceId)` |

Built-in definition ids (PK): e.g. `platform.schedule.expected_run_missing` (`NotificationDefinitionIds`).

Instance optional refs (**no FKs**): `NotificationDefinitionId`, `PlatformEventId`, `AttentionIncidentId`, `IntegrationId`, `ModuleId`, `RunId`, `ScheduleId`.

#### Audit vs platform events

**Two different stores.**

- **`AuditLogs`**: bigint identity PK. Domain `AuditRecord.Id` is discarded on write (`SqlAuditService.AppendAsync`). Columns: Actor, Action, Details, Outcome, **ModuleId**, **ScheduleId**, **RunId**. **No IntegrationId.** Resource is stuffed into Details text. Migration `20260322240000_AddAuditFirstClassFields`.
- **`PlatformEvents`**: PK `pevt_{Guid:N}`. Optional refs: IntegrationId, ModuleId, ProcedureId, RunId, ScheduleId, ActorId, CorrelationId, CausationId. Unique filtered `IX_PlatformEvents_Source_Type_DedupeKey`. Migration `20260515112835_Epic77_PlatformEvents`.

#### Business-data records

**CONFIRMED: there is no platform `BusinessRecord` table or entity.** See §8.

### Uniqueness cheat sheet

| Constraint | Table | Kind |
|---|---|---|
| PK `(EnvironmentId, ModuleId)` | ModuleInstallationStates | composite |
| PK `Id` only | Integrations, Connections, Bindings, ScheduleDefinitions | **no alt key** |
| `ScheduleId + FireTimeUtc` | ScheduleRuns | unique |
| filtered `IdempotencyKey` | WorkflowStartRequests, OperationStartRequests | unique |
| `(NotificationDefinitionId, InstanceDedupeKey)` | NotificationInstances | unique |
| user-created `(Scope, IntegrationId, DisplayName)` | NotificationDefinitions | unique filtered |
| `(Source, Type, DedupeKey)` | PlatformEvents | unique filtered |
| `(Key, ScopeKind, ScopeId)` | PlatformSettingValues | unique |
| one Active incident per `(IntegrationId, ConditionKey)` | AttentionIncidents | unique filtered |
| `(IntegrationId, ConditionKey)` | IntegrationAttentionRoutings | unique |
| `(SharedRecordType, SharedBusinessKey, CompletedAtUtc)` | OperationItems | **non-unique** index |

---

## 3. Settings Registry and Platform Settings

Settings are **operator-controlled typed knobs**, not a catalog of resources. Architecture docs state that explicitly: settings write `PlatformSettingValues` and audit changes; option providers only supply `{value, label}` choices; resource/catalog browsing is a separate future subsystem.

**Reuse without becoming a metadata store:** keep **definition vs stored value vs option lookup vs domain objects** as four layers. A resource-descriptor/namespace system can copy *key namespacing, ownership, typed values, scope identity, and contribution-at-startup* — it should not store browseable catalogs in `PlatformSettingValues`.

### Registration / discovery — CONFIRMED

There is **no plugin scan**. Definitions are compiled into registries and merged at host DI.

**Instance / platform definitions**

| Piece | Role |
|---|---|
| `IPlatformSettingDefinitionRegistry` | `GetAll()` / `TryGet(key)` |
| `PlatformSettingDefinitionRegistry` | Hardcoded production keys |
| `GatedPlatformSettingDefinitionRegistry` | Production + demo gallery when Development or `PlatformSettings:DemoGallery:Enabled` |
| `PlatformSettingDemoDefinitionRegistry` / `DemoPlatformSettingKeys` | `demo.settings.*` gallery keys |

`AddApperturePlatformSettingsCore` registers `PlatformSettingDefinitionRegistry`. Management host **replaces** it with `GatedPlatformSettingDefinitionRegistry`. WorkerHost uses the production registry only.

**Integration definitions**

| Piece | Role |
|---|---|
| `IIntegrationSettingDefinitionRegistry` | Integration-scoped defs only |
| `IntegrationSettingDefinitionRegistry` | Platform-owned list — currently **empty** `[]` |
| `IModuleIntegrationSettingDefinitionContributor` | Direct module contribution of `PlatformSettingDefinition`s |
| `IIntegrationConfigurationContributor` | SDK-6C: settings + connection requirements + procedure subsets |
| `IntegrationConfigurationSettingContributorAdapter` | Maps SDK-6C settings onto `PlatformSettingDefinition` |
| `CompositeIntegrationSettingDefinitionRegistry` | Concatenates platform list + all contributors; **duplicate keys throw** |
| `IIntegrationConfigurationRegistry` | Module-id → validated `ModuleIntegrationConfiguration` |

Unknown keys are rejected at write. Listing is **registry-driven**: resolvers iterate definitions, then overlay DB rows. Orphan DB rows for unknown keys are not listed.

Option providers are compile-time DI (`IPlatformSettingOptionProvider` → `PlatformSettingOptionProviderRegistry`). Duplicate `ProviderKey` throws.

### Typed values — CONFIRMED

**Platform storage types** (`PlatformSettingValueType`): `Boolean`, `Integer`, `Duration`, `String`, `StringArray`.

**UI/editor kinds** (`PlatformSettingInputKind`): `Toggle`, `Number`, `Text`, `Duration`, `Select`, `MultiSelect`, `Secret`.

**SDK-6C logical kinds** (`IntegrationSettingValueKind`): `String`, `MultilineString`, `Integer`, `Decimal`, `Boolean`, `Choice`, `Uri`, `Duration`, `TimeZone`, `SensitiveText`. Mapping in `IntegrationConfigurationDeclarations.MapValueKind`. Decimal/Uri/TimeZone/Multiline persist as **String + Text** today.

Codec: `PlatformSettingsValueCodec` — JSON scalars in `ValueJson`; durations as invariant strings; arrays as JSON string arrays.

`IIntegrationOptions<TOptions>` exists on `IIntegrationSettingsAccessor`. **No implementation found** (interface-only).

### Namespaces / keys — CONFIRMED

Identity of a stored value is **`(Key, ScopeKind, ScopeId)`**, unique index `IX_PlatformSettingValues_Key_ScopeKind_ScopeId`. **Do not embed `integrationId` in the key.**

| Owner | Pattern | Examples |
|---|---|---|
| Platform instance | dotted product path | `Notifications.Evaluation.Enabled`, `PlatformEvents.Enabled`, `Scheduling.DefaultTimeZoneId` (`PlatformSettingKeys`) |
| Demo gallery | `demo.settings.*` | `demo.settings.boolean` |
| Module integration | `modules.{module}.{name}` | `modules.sample.helloworld.demo.greetingPrefix` |
| Designed, not used as platform keys | `integrations.*`, `platform.*` | Docs only; modules must not invent `platform.*` |

SDK adapter **does not prefix** keys: `ToPlatformSettingDefinition` copies `definition.Key` as-is. Namespace is a **convention of module contract constants**, not an enforced parser.

Boot defaults use colon `IConfiguration` paths. Instance resolver: **Database > Configuration > Definition default**. Integration resolver: **Database > Definition default** only (no implicit instance inheritance).

`PlatformSettingValueScopeKind`: **`Instance`, `Integration` only**. No `Module`, `User`, `Connection`, or Context members. Personal prefs are a separate store (`UserPreferences`).

`Integration` domain still has a legacy `IReadOnlyDictionary<string, string>? Settings` blob. **Typed settings do not use that dictionary**; they use `PlatformSettingValues`.

### Provider ownership — CONFIRMED vs designed

**Definition ownership:** platform instance keys hardcoded; platform integration keys empty; module integration keys via `IIntegrationConfigurationContributor`, category forced to `PlatformSettingCategory.Integrations`, `AllowedValueScopes = [Integration]`.

**Option-provider ownership (choices, not values):**

| ProviderKey | Class |
|---|---|
| `platform.connectorDefinitions` | `PlatformConnectorDefinitionsOptionProvider` |
| `campusnexus.campuses` | `CampusNexusCampusesOptionProvider` |

`IPlatformSettingOptionProvider` is **not** in the ModuleSdk public-API snapshot. Modules cannot register option providers through the SDK today.

**SPECULATION (docs, not code):** future `platform.modules.installed`, `sqlserver.tables`, cross-integration `ProviderBinding`, module-contributed providers. `IResourceCatalogProvider` is named in docs only.

### Sensitive settings / SecretStore — CONFIRMED

Declaration: `IsSensitive = true`, `InputKind = Secret`, `ValueType = String`. Ordinary `Value`/`Values` writes are rejected.

Write path (`PlatformSettingsService.SetSensitiveAsync` / same on integration service):

1. `SecretOperation=replace` + `SecretValue` → `PlatformSettingSecretMaterialProvisioner.ReplaceAsync`
2. `ISecretStore.CreateAsync` or `AddVersionAsync` + `ActivateVersionAsync`
3. Persist **GUID only** in `ValueJson`
4. Producer: `provider=platform-setting`, name `platform-setting:{settingKey}`

Read APIs: `IsSecretConfigured`, `HasPlatformOverride`, `Source`; **never** plaintext or GUID (`PlatformSettingsDtoMapper` blanks `CurrentValue`). Runtime resolvers keep the GUID in `CurrentValue`. Module handlers use `IIntegrationSettingsAccessor.GetSecretAsync` — plaintext only in-process.

`ISecretStore` (lifecycle) vs `ISecretResolver` (runtime material) are the operational secret subsystem. Tests: `SensitivePlatformSettingsTests`, `Sdk6cSecurityReadinessTests` + `SecretLeakageAssertions`.

Storage: table `PlatformSettingValues` (`PlatformSettingValueEntity`): `Id`, `Key` (128), `ScopeKind` (32), `ScopeId` (64), `ValueJson` (**max 512**), timestamps, `UpdatedByPrincipalId`, rowversion. Migration `20260524112919_Epic109_AddPlatformSettingValues`.

### Rendering APIs — CONFIRMED

**HTTP**

- Instance: `ConfigPlatformSettingsController` — `api/config/platform-settings` (`GET /`, `GET /{key}`, `PUT /{key}`, `DELETE /{key}`, `GET /{key}/options?integrationId=`). Auth: `platform.settings.read` / `platform.settings.manage` on `InstanceConfiguration`. The `integrationId` on options is **provider context**, not storage scope.
- Integration: `IntegrationSettingsController` — `api/integrations/{integrationId}/settings`. Auth: `integration.read` / `integration.update`. Route id is **both** value scope and provider context.
- Declarations: `GET /api/integrations/{id}/configuration/declarations` — SDK-6C setting/connection/procedure declarations.

**UI:** `/config/platform-settings` and `/integrations/{id}/settings` share `PlatformSettingsEditor` / `PlatformSettingRow`. Schema-driven forms exist for declared `InputKind`s. **Custom Razor panels / module UI are not loaded.**

**Audit:** `platform.settings.set` / `reset`; `integration.settings.set` / `reset`. Sensitive audits: operation + configured yes/no, never value/GUID.

### Concepts reusable for resource descriptors/namespaces (without a generic KV store)

**Safe to copy as patterns (CONFIRMED existing behavior, not a recommendation to reuse the table):**

1. Compile-time definition registry + contribution interface, not client-supplied keys.
2. Stable namespaced keys with **scope id outside the key**.
3. Typed value + separate UI kind.
4. Provider key as a pointer to an authorized lookup, not stored catalog JSON.
5. Secret refs only in the settings row.
6. Explicit routes per scope rather than `/api/settings?arbitraryTree=`.

**Do not reuse `PlatformSettingValues` as a resource catalog.** Docs already split settings vs option providers vs future `IResourceCatalogProvider`. Governance `ResourceDescriptor(resourceType, scopeKey)` (`IResourceDescriptorRegistry`) is a **third** system — permissions, not config values.

---

## 4. Module discovery / loading and SDK boundary

Loading is **host-controlled explicit DI**, not assembly probing. Intent: one module per process; current hosts register **five** modules for dev/composition.

### How modules are discovered and loaded — CONFIRMED

**Compile-time known set** (Management.Host `Program.cs`; WorkerHost `Program.cs`):

`HelloWorldModule`, `FileImportModule`, `ApiPollingModule`, `UnisaCampusNexusPaymentPlansModule`, `ProofCampusNexusStudentReviewModule`

Registered as `IEnumerable<IModule>`. `AddKnownModules` also binds SDK-6C configuration. Worker additionally `AddIntegrationConfigurationBinding<T>()` and `AddSingleton<IStepHandlerRegistry>` by walking `IModuleStepHandlerRegistrar`.

**Lifecycle persistence** (`SqlModuleRegistryService`, table `ModuleInstallationStates`):

- Discover = enumerate DI `IModule` descriptors (`BuildDescriptorAsync` / `ModuleDescriptorBuilder`)
- Install / enable / disable in SQL
- `IModuleActivationFilter` hides disabled modules from workflow/operation/procedure resolvers
- Handlers for **all** DI modules still register at Worker start; new work for disabled modules is blocked at definition resolution

**Not dynamic plugins.** New modules require a project reference, host `Program.cs` entry, and (for arch tests) an update to `IntegrationSdkBoundaryDependencies.IntegrationModuleTypes`.

### What a module can register / contribute — CONFIRMED

`IModule` = `IModuleManifest` + `IWorkflowDefinitionProvider` (+ default `BuildDescriptorAsync`).

**Manifest (`ModuleManifest`)**

| Field | Effect |
|---|---|
| Id, Name, Version, Description | Required |
| `Permissions` | Host registers into `IResourceDescriptorRegistry` at `module:{id}` scope (`ModulePermissionRegistrationHostedService` → `ModulePermissionRegistrar`) |
| `GovernedResources` / `GovernedActions` | Same registrar |
| `Signals` | Declared platform events (handlers emit via `context.Signals` only) |
| `Procedures` | Operator launch surface (workflow or operation + parameter schema) |
| `ConnectorDefinitions` | **Documentation only** — not the runtime connector registry |

**Optional interfaces on the module type**

| Interface | Contributes |
|---|---|
| `IModuleStepHandlerRegistrar` | `(moduleId, stepName)` handlers into `IStepHandlerRegistry` |
| `IOperationDefinitionProvider` | Direct operations (`OperationDefinition.Name` = handler name) |
| `IIntegrationConfigurationContributor` | Settings, connection requirement slots, procedure requirement subsets |
| `IModuleIntegrationSettingDefinitionContributor` | Legacy/direct platform-setting defs (adapter prefers SDK-6C) |
| `IModuleNavigationProvider` | Nav descriptors on `ModuleDescriptor` |

**`IModuleNavigationProvider`: interface exists; zero production implementations.**

**Descriptor catalog kinds** (`ModuleContributionDescriptors`): `workflow`, `operation`, `permission`, `navigation`. Settings/slots/signals are **not** those descriptor kinds; they flow through other registries.

**Host-side extras (not a generic module service catalog)** — each module’s `Add*` extensions, called from host `Program.cs`: retry handlers, persistence adapters, capability slot bootstrap hosted services. Connectors (CampusNexus, SFTP, platform SQL) are **connector packages** via `IConnectorDefinitionContributor` in Core.Application — not `IModule`.

### How integrations are declared / instantiated — CONFIRMED

An **integration is an operator-created instance of a module**, not the module type.

`IntegrationManagementService.CreateIntegrationAsync`: new `IntegrationId`, `DisplayName`, `ModuleId`, optional legacy `Settings` dict, state `Active`. API: `POST /api/integrations`.

Runtime: schedules/procedures start work against that `integrationId`. Bindings map **slots** (`ConnectionRequirementDefinition`) to **connections**. Handlers resolve `IStepExecutionContext.ResolveSlotAsync` — no connection ids in handlers.

Readiness: `ConfigurationReadinessEvaluator` (SDK) + `IntegrationReadinessService` (host). Declarations API: `GET .../configuration/declarations`.

### Can modules expose services/providers or UI today?

| Capability | Status |
|---|---|
| Workflows + step handlers | Yes |
| Operations + procedures | Yes |
| Integration settings + connection slots | Yes (SDK-6C) |
| Permissions / governed resources | Yes (manifest → host registrar) |
| Signals | Yes (declare + emit through context) |
| `IOperationItemRetryHandler` | Yes, via host `Add*` (not auto from `IModule`) |
| Extra DI (stores, persistence) | Yes, **host composition**, not a module-service SPI |
| Option providers | **No** module SPI; platform registers them |
| Connector runtime definitions | Connector packages / host, not manifest |
| `IIntegrationSettingsAccessor` | Consumed by modules; **implemented in RuntimePlane.Engine** |
| `IIntegrationOptions<T>` | Interface only; **no adapter** |
| Navigation / Blazor UI | Contract unused; `.Ui` not loaded. Arch test forbids module → Management UI |

### Architectural tests — CONFIRMED

`tests/Apperture.ArchTests/IntegrationSdkBoundaryDependencies.cs`

- Explicit module list must include every loaded `IModule` under Sample/Proof/Unisa.
- Those assemblies must **not** reference: `Apperture.Persistence.SqlServer`, `Apperture.Management.Api/Host/Client`, `Apperture.RuntimeHost.Windows.WorkerHost`, EF Core, `Microsoft.Data.SqlClient`.
- `ModuleSdk.Integration` must not reference EF, Persistence, Management, or **`Apperture.Core.Application` assembly** (types moved into the SDK assembly).
- Integration testing toolkit must not reference Persistence/Management/`RuntimePlane.Engine`.
- Engine **may** reference Persistence; modules may not.

`Sdk6dOperatorUiBoundaryDependencies.cs`

- Modules must not reference Management UI Client/Shared/Server.
- ModuleSdk.Integration must not reference Blazor/ASP.NET UI packages.
- UI Shared/Client must not reference Persistence or `Core.Security.Secrets`.

Public contract freeze: `IntegrationSdkPublicApiSnapshotTests`.

**Not tested:** modules referencing Core.Application **assembly** (they don’t today). They **do** use Core.Application **namespaces** that live in the SDK assembly, and Core.Domain types via Abstractions.

**SPECULATION (module side):** plugin folder load; generic module service export; configuration schema on manifest (“Future” in playbook); module-contributed nav actually rendered. Playbook still says integration runtime consumption deferred; **WorkerHost now registers `AddAppertureIntegrationRuntimeSettings/Accessors`.**

---

## 5. Connection model

### What a Connection represents — CONFIRMED

A `Connection` is a governed stored resource: non-secret config plus secret *references*, keyed to a connector kind. It is **not** a live client.

```6:21:src/Apperture.Core/Apperture.Core.Domain/Connector/Connection.cs
/// Governed stored resource: configuration and secret references for resolving a live connector.
/// Not the live client. Resolves to ephemeral client via IConnectionResolver.
public sealed record Connection(
    ConnectionId Id,
    ConnectorDefinitionId ConnectorDefinitionId,
    string Name,
    IReadOnlyDictionary<string, string> Config,
    IReadOnlyDictionary<string, string> SecretRefs,
    ...
```

Persisted as `ConnectionEntity` / table `Connections` (`ConfigJson`, `SecretRefsJson`, `ConnectorDefinitionId`, `State`, verification columns). Connector *kinds* live in `IConnectorDefinitionRegistry` (`ConnectorDefinition` + `ConnectionFieldDescriptor` for config vs secret fields).

Lifecycle: `ConnectionState.Active | Retired`. Retired connections are excluded from binding eligibility.

An **Integration** owns bindings and schedules, **not** connections. ADR-0033: connections stay first-class platform entities; in-flow create still persists normal `Connection` rows.

### Reuse among integrations — CONFIRMED

A connection is instance-scoped (typical `ScopeKey` = instance). It is attached to integrations only through `Binding` rows.

- `BindingService.CreateBindingAsync` uniqueness: **one active binding per (moduleId, slotName, integrationId)**. It does **not** unique-constrain `ConnectionId`.
- `IBindingRepository.GetByConnectionIdAsync` returns **all** bindings for that connection.
- `GET /api/connections` `UsedIn` lists every binding (module, slot, integration).
- Eligibility (`BindingEligibilityService.GetEligibleConnectionsAsync`) returns **all Active** connections whose connector factory can produce the slot’s contract type — not “unused” or “owned by this integration.”
- Canonical bind API: `POST /api/integrations/{integrationId}/bindings`. Eligible list: `GET .../eligible-connections`.

So the same Campus Nexus / SQL / SFTP connection can be bound to many integrations (and many slots), as long as each integration’s slot is still unbound.

**CONFIRMED, limited:** `ConnectionRequirementDefinition.AllowMultipleBindings` is declared and projected in configuration DTOs. `BindingService` still rejects a second **active** binding for the same integration slot.

**CONFIRMED:** runtime resolution is **slot-bound**, not by connection id. With `integrationId`, `SlotBoundResolver` uses `GetBySlotAndIntegrationAsync`; without it, legacy `GetBySlotAsync` (first match).

### Secrets and configuration — CONFIRMED

| Store | What | Shape |
|--------|------|--------|
| `Connection.Config` / `ConfigJson` | Non-secret fields | `Dictionary<string,string>` |
| `Connection.SecretRefs` / `SecretRefsJson` | Secret **ids**, not plaintext | Field key → GUID string |
| `ISecretStore` (`SqlSecretStore`) | Actual material | `SecretDescriptors` + encrypted `SecretVersions` |

Create/update: `ConnectionManagementService` + `ConnectionSecretMaterialProvisioner`.

- Operator may send `SecretValues` (plaintext) **or** existing GUID `SecretRefs`.
- Provisioner writes `ISecretStore` with provider `"platform-connection"` and name `connection:{connectionId}:{fieldKey}`.
- Connection row stores only the GUID.
- `ValidateConnectionMaterial` forbids secret fields in `Config` and non-secret fields in `SecretRefs`.

Runtime: `ResolvedConnectionMaterialBuilder` + `ISecretResolver`. Factories (`IConnectorClientFactory<T>.Create`) see material only — not `ISecretResolver`. Verification: `POST /api/connections/{id}/test-connect` updates `LastVerification*` columns; does not change secret storage.

### How integrations resolve/use connections — CONFIRMED

1. Module declares `ConnectionRequirementDefinition` (key = slot name, `CapabilityContractType`).
2. Operator binds: `IBindingService` → `Bindings`.
3. At run time, `IIntegrationExecutionScopeAccessor` holds `(ModuleId, IntegrationId)` (`DefaultWorkflowExecutor` `BeginScope`).
4. Module code uses `IIntegrationConnectionAccessor` → `IRuntimeSlotResolver.ResolveSlotAsync<T>(moduleId, connectionKey, integrationId)`.
5. Steps equivalently use `IStepExecutionContext.ResolveSlotAsync`.
6. `SlotBoundResolver`: binding (Active + `ConnectionId`) → `IConnectionRepository` (Active) → `IConnectionResolver` (`DefaultConnectionResolver`) → factory → live client.

**CONFIRMED:** no authorization at resolution; **binding is the gate**. `capability.consume` at resolve time is documented as deferred.

APIs: `ConnectionsController` (`/api/connections`), `IntegrationBindingsController`, `BindingsController` (legacy slot bind create = 410), `ConnectorDefinitionsController`.

---

## 6. Schedule and run execution semantics

### How schedules identify an integration or executable — CONFIRMED

A schedule does **not** execute an “integration” as a unit. It owns an optional `IntegrationId` and a **typed target**.

`ScheduleDefinition` (`src/Apperture.Core/Apperture.Core.Domain/Scheduling/ScheduleDefinition.cs`): `ScheduledTarget` = `ScheduledJobType` + `JobConfiguration` JSON.

**Create-time allowed types** (`ScheduleValidationService`): only `WorkflowExecution` (0) and `ModuleOperationExecution` (1). Other enum values (`ConnectorSyncOrPolling`, PowerShell, maintenance, Custom) exist but are **not** creatable and are skipped by `CronScheduleEvaluator`.

Job configuration:

- Workflow: `{ "workflowDefinitionId": "..." }`
- Operation: `{ "operationDefinitionId": "..." }`

Parsed in `ScheduleDispatchRunner.ParseWorkflowDefinitionId` / `ParseOperationDefinitionId`. Target is **immutable** after create (`SchedulesController` 400 if changed).

**Integration correlation (not the executable):**

- Canonical create: `POST /api/integrations/{integrationId}/schedules` sets `ScheduleDefinition.IntegrationId` and assigns `ScheduleAssignments.RuntimePlaneId` (default `"local"`).
- Dispatch requires Active integration when `IntegrationId` is set; missing id is **legacy**, still allowed, audited as `LegacyScheduleWithoutIntegration`.
- Procedure id is **looked up**, not stored on the schedule: `IProcedureRegistryService.FindByTargetAsync(moduleId, Workflow|Operation, definitionId)`.

**Host:** real loop is `ScheduleDispatchBackgroundService` in **WorkerHost**. `SchedulerHostStub` is a no-op.

Due fires: `CronScheduleEvaluator` + `ScheduleAssignments` for a `RuntimePlaneId`. Claim: `ScheduleRuns` unique on `(ScheduleId, FireTimeUtc)`; `ClaimedByHostId` for multi-host.

**CONFIRMED (AGENTS.md #5):** when a schedule fires, the host enqueues a durable start. Nothing in the scheduling path executes workflow steps.

### How runs correlate back — CONFIRMED

**A. Schedule firing** — `ScheduleRuns`: `ScheduleId`, `FireTimeUtc`, `Outcome`, `RunId` = dispatched workflow/operation run id when dispatch succeeds. Unique `(ScheduleId, FireTimeUtc)` is fire-level idempotency.

**B. Durable start / operator run** — `OperationRuns` (used for **both** workflow and module-operation starts).

`WorkflowStartDispatchService` / `OperationStartDispatchService` insert, in one transaction:

- `WorkflowInstances` **or** `OperationInstances`
- `OperationRuns` (`IntegrationId`, `ProcedureId`, `ExecutionOrigin`, `CorrelationKey`, `DefinitionId`, `WorkflowInstanceId`)
- `WorkflowStartRequests` **or** `OperationStartRequests` (`IdempotencyKey` unique filtered index)
- lifecycle event (`Dispatched` → `Requested`)

Schedule-built request (`ScheduleDispatchRunner`):

- `IdempotencyKey`: `"schedule:{ScheduleId}:{FireTimeUtc:O}"`
- `CorrelationId`: `"schedule:{ScheduleId}"`
- `IntegrationId`: from definition
- `ExecutionOrigin`: `"schedule"` (manual path uses `"manual"`)

Manual path: `IntegrationProcedureService.LaunchAsync` and deprecated `POST /api/workflows/{id}/start` (requires `IntegrationId`) use the **same** dispatch services.

Execution: `StartRequestWorker` polls start-request tables, processors claim (`ClaimedBy` = worker id), then `IWorkflowExecutor` (`DefaultWorkflowExecutor`) with `integrationId` from start request → instance → optional default. That scopes slot resolution.

### What existing execution infrastructure could later be reused

**CONFIRMED: workflow runs already use this system.** There is no separate production “workflow executor” besides this SQL handoff + WorkerHost processors + `IWorkflowExecutor`.

**CONFIRMED production stack:**

| Piece | Role today |
|--------|------------|
| `IScheduleDispatchRunner` / `ScheduleDispatchRunner` | Claim fire → start dispatch |
| `IWorkflowStartDispatchService` / `WorkflowStartDispatchService` | Persist instance + `OperationRuns` + `WorkflowStartRequests` |
| `IOperationStartDispatchService` | Same for module operations |
| `StartRequestWorker` + `*StartRequestProcessor` | Claim and execute |
| `IWorkflowExecutor` / `DefaultWorkflowExecutor` | Step graph in-process |
| `IWorkflowPersistence` / `SqlWorkflowPersistence` | Instance/step state |
| Tables | `ScheduleDefinitions`, `ScheduleAssignments`, `ScheduleRuns`, `ScheduleStates`, `SchedulerDispatchHeartbeats`, `WorkflowStartRequests`, `OperationStartRequests`, `OperationRuns`, `WorkflowInstances`, `OperationInstances`, step/lifecycle tables |

**CONFIRMED stubs / not on the enqueue path:**

- `IWorkflowStarter` — explicitly **not** production enqueue.
- `IWorkflowRuntime` — “future-facing adapter”; today’s path is SQL start requests + `IWorkflowExecutor`. Remarks: orchestration-safe parameters; no `ResolvedSecret` in state/history.
- `DurableTaskRuntimeAdapterStub` (`AddAppertureDTFx`) — empty.
- `DurableFunctionsRuntimeStub` — `NotImplementedException`.

`SchedulerHost` is not an execution backend.

**SPECULATION (reuse of this stack, not current behavior):** a future DTFx/Durable Functions backend would implement `IWorkflowRuntime` **behind** the existing dispatch/handoff (`IWorkflowStartDispatchService`, `OperationRuns`, idempotency keys), not a second schedule loop or second run table. `OperationRuns.Id` is already the operator run id for both workflows and operations; `ScheduleRuns.RunId` already points at it.

---

## 7. Notification architecture

> **Superseded for delivery path (2026-09-10):** NTF-C completed after this snapshot. There is now **one** production notification pipeline. See [NTF-C closure](../ntf-c-legacy-removal/closure-report.md). The rest of this section remains the pre-cutover inventory.

### Pipeline — CONFIRMED

**One production delivery path** (NTF-C complete — see [closure-report.md](../ntf-c-legacy-removal/closure-report.md)):

Attention / platform event → rule evaluation → notification instance → recipient resolution → delivery outbox → SMTP / personal inbox.

Kinds: `activation`, `reminder`, `escalation`, `resolved`. Escalation uses Attention routing (`EscalationAfter`, escalation team, ack suppression) and one instance per incident. Feature flags that remain: `NotificationEvaluationOptions`, `NotificationInstanceOptions`, `NotificationDeliveryOptions`. There is no `UsePlatformPipeline` switch.

Registration: `NotificationsServiceCollectionExtensions.AddAppertureNotifications`.

### Definitions / rules / subscriptions — CONFIRMED

**Definitions** are the durable rule+template aggregate, not generated events.

- Domain: `NotificationDefinition`. Entity: `NotificationDefinitionEntity` → `NotificationDefinitions`.
- Rule JSON schema: `apperture.notification.rule/v1` (`NotificationRuleDocument`).
- Template JSON schema: `apperture.notification.template/v1` (`NotificationTemplateDocument`).
- Origins: `platformBuiltIn` / `moduleBuiltIn` / `userCreated`.
- Scopes: `platform` / `module` / `integration` (`NotificationDefinitionScopes`).
- Mutability: `staticRuleMutableSubscription` / `fullyEditable` / `readOnly`.
- Built-in IDs (`NotificationDefinitionIds`): `platform.schedule.expected_run_missing`, `module.sample.helloworld.demo.completed`, `module.sample.fileimport.import.completed_with_errors`, `module.sample.apipolling.sync.completed_with_errors`.

Rules match **platform events** and/or **incident transitions**, with stages/filters/throttle (`NotificationRuleEvaluator`). Kinds: `activation`, `reminder`, `resolved`.

**Subscriptions** bind a definition to recipients:

- `NotificationSubscriptionEntity` — `RecipientKind` = `principal` | `operationalTeam` | `managedContact`; `DeliveryMode` = `immediate` | `muted` | `digestOnly` | `immediateAndDigest`.
- `NotificationSubscriptionUserOverrideEntity` — per-user opt-out.
- Origins: `systemAssigned` | `adminAssigned` | `userSelfSubscribed`.

**CONFIRMED:** `digestOnly` skips immediate email; UI copy says daily digest is **not implemented**.

Recipient resolution (`SqlNotificationRecipientResolver`) expands teams/contacts, applies opt-outs, and for `expected_run_missing` can **implicitly** add the integration’s Attention routing team if no matching team subscription exists.

### Generated notification instances — CONFIRMED

- Entity: `NotificationInstanceEntity` → `NotificationInstances`.
- Statuses: `pending` | `suppressed` (no “delivered” status on the instance).
- Dedupe: `InstanceDedupeKey` (`NotificationInstanceDedupeKeyBuilder`).
- Stores rendered subject/summary/body/action + `RenderContextJson` + `RecipientSnapshotJson`.
- Optional links: `PlatformEventId`, `AttentionIncidentId`, `IntegrationId`, `RunId`, `ScheduleId`.

Creation: `NotificationEvaluateOnlyCoordinator` → `NotificationInstanceCoordinator.TryCreateFromMatchAsync`.

Triggers:

- Sync: `EvaluatingPlatformEventStore.AppendAsync` evaluates after persist.
- Async: `PlatformEventNotificationEvaluationHostedService` for worker-persisted events not yet evaluated.
- Incident reminders: `AttentionIncidentObservationHostedService` calls `EvaluateIncidentAsync` on `ObservedStillActive`.

Personal **inbox** is a projection of instances, not a second generator: `NotificationUserInboxItemEntity` populated by `NotificationUserInboxService`.

### Delivery attempts — CONFIRMED

There is **no** separate `DeliveryAttempt` table. Attempts live on **outbox rows**:

- Entity: `NotificationDeliveryOutboxEntity` → `NotificationDeliveryOutbox`.
- Channel today: `email` only (`NotificationChannels.Email`).
- Statuses: `pending` | `delivering` | `succeeded` | `failed` | `suppressed` | `skipped`.
- Fields: `AttemptCount`, `NextAttemptAtUtc`, `LastError`, `SentAtUtc`, `RowVersion`.
- Processor: `NotificationDispatchProcessor` claimed by `NotificationDispatchHostedService`.
- Retry: `MaxAttempts` (default 5), exponential backoff.

When `Notifications:Delivery` is false: dry-run log, rows stay pending.

Legacy ledger is a **parallel** attempt log (`AttentionIncidentNotificationEntity`: `AttemptedAtUtc`, `SentAtUtc`, `FailedAtUtc`, `FailureSummary`), not the platform outbox.

### Attention integration — CONFIRMED

**Incidents:** `AttentionIncidentEntity` — `ConditionKey` (observed code uses `AttentionConditionKeys.ExpectedRunMissing`), status Active/Resolved, acknowledgement fields. Observer: `AttentionIncidentObservationHostedService` → `IAttentionIncidentTracker.ObserveScheduleExpectationAsync`. Platform events emit via `ScheduleExpectationPlatformEventEmitter` on Opened/Resolved. Still-active observation evaluates reminder and escalation instances.

**Routing:** `InstanceAttentionRoutings` / `IntegrationAttentionRoutings` — enabled, reminder/escalation intervals, primary/escalation operational teams. APIs: `GET/PUT api/attention-routing/defaults`, `GET/PUT api/integrations/{id}/attention-routing`. UI: `/attention/routing-defaults`, `/attention/operational-teams`, `/config/contacts`.

**How Attention feeds notifications:** built-in expected-run-missing instances use `IAttentionRecipientResolver` for suppression (policy disabled, no route, acknowledgement, duplicates) and reminder gating. Recipient source token: `attentionRoutingIntegrationOverride`. Instance column `AttentionIncidentId` is the join key for diagnostics.

**Delivery:** platform pipeline only. The Attention incident/routing subsystem remains; the legacy Attention email orchestrator and ledger are gone.

### Diagnostics — CONFIRMED

Service: `NotificationDeliveryDiagnosticsService`. API: `NotificationDiagnosticsController` → `api/notification-diagnostics` (`/smtp-test`, `/platform-pipeline-test`, `/configuration`, `/health`, `/by-integration/{id}`, `/by-incident/{id}`).

Integration schedule diagnostics surface recent platform notification instances (`IntegrationScheduleDiagnosticsService`). There is no legacy Attention ledger read.

### APIs and UI

| Controller | Base |
|---|---|
| `NotificationDefinitionsController` | `api/notification-definitions` |
| `NotificationSubscriptionsController` | nested under definitions + integrations |
| `NotificationInstancesController` | `api/notification-instances` |
| `NotificationDiagnosticsController` | `api/notification-diagnostics` |
| `MeNotificationsController` | `api/me/notifications` |
| `MeNotificationSubscriptionsController` | `api/me/notification-subscriptions` |
| `AttentionRoutingDefaultsController` | `api/attention-routing` |

UI routes: see §1 Notifications.

**CONFIRMED:** WorkerHost does not host the notification pipeline. Escalation is a platform `NotificationKinds.Escalation` instance (once per incident) with outbox retries. Digest/SMS/push: constants/UI exist for digest; **no digest dispatcher found**. Observed Attention `ConditionKey` in this path: `expected_run_missing`.

---

## 8. Business-data architecture

### There is no platform `BusinessRecord` entity — CONFIRMED

`AppertureDbContext` has **no** `BusinessRecord` table. Comments on projector, DTOs, SDK, and UI all say this is **not** the final platform model.

What exists is:

1. A **metadata convention** on operation items.
2. A **typed identity** (`SharedRecordIdentity`) persisted as columns.
3. A **demo read-time projection** of customers from two sample modules.

### Identity / source identifiers — CONFIRMED

`SharedRecordIdentity(RecordType, BusinessKey)` in `src/Apperture.ModuleSdk/Apperture.ModuleSdk.Integration/OperationItems/SharedRecordIdentity.cs` (namespace still `Apperture.Core.Application.OperationItems`).

Comment on the type: *“Identifies a logical business record across integrations; not a platform entity or payload.”*

- Built-in type: `SharedRecordTypes.Customer` = `"customer"`.
- Validation: record type `[a-z][a-z0-9._-]{0,63}`; business key trimmed, no `/ \ ? # :`.
- Comparison: both case-insensitive.
- Writers: `OperationItemReporting.BuildAppendRequest(..., sharedRecord:)` writes metadata `recordType`/`businessKey` and aligns `SubjectKey` to `BusinessKey`.
- Persistence columns on `OperationItems`: `SharedRecordType`, `SharedBusinessKey` (migration `20260806010000_Sdk7_OperationItemSharedRecordIdentity`).

Metadata keys (`OperationItemBusinessMetadataKeys`): `recordType`, `businessKey`, `displayName`, `changeKind`, `observedAt`. Demo also reads `email` and `status` from metadata JSON.

Compatibility reader: `SharedRecordIdentityCompatibility.TryReadCustomer` (normalized columns **or** metadata **or** subject key).

### `DemoBusinessRecordProjection` — CONFIRMED

Not stored. Built at read time by `DemoBusinessRecordProjector` from `OperationItems` ⋈ `OperationGroups` for modules `Sample.FileImport` and `Sample.ApiPolling` only.

**Correlation:** group by `recordType=customer` + `businessKey`.
**Latest per source:** one observation per `(moduleId, integrationId)` by newest `ObservedAtUtc`.
**Primary fields:** prefer `api_polling`, then newest timestamp (`ChoosePrimary`).
**Conflicts:** email / status / displayName differ across sources (`DetectConflicts`). Requires ≥2 sources.

Source kinds: `file_import` | `api_polling` | `unknown` (from module id).

### Ownership vs multi-source correlation — CONFIRMED

A demo customer is **not owned by one integration**. It is a **correlation** of observations:

- `SourceCount` is the number of distinct `(module, integration)` latest observations.
- Each `DemoBusinessRecordSourceObservation` keeps `IntegrationId`, `ModuleId`, `OperationItemId`, `OperationGroupId`, `RunId`, `ProvenanceLabel`.
- Conflicts exist specifically because the same `businessKey` can appear from FileImport **and** ApiPolling (and potentially two integrations of the same module).

`ISharedRecordReferenceQueryService` (`SharedRecordReferenceQueryService`) is a **cross-integration lookup of operation-item references** by identity. Comment: *“does not merge a canonical entity.”* **No HTTP controller** was found for this service (DI-registered, unit-tested only).

### Provenance / activity — CONFIRMED

**On the projection:** each source observation is provenance (item, group, run, integration).

**Activity feed:** `GetCustomerActivityAsync` lists **all matching operation items** for that business key (not just latest-per-source), joined to runs and integrations, with change kind, item/run/group status, errors, retry counts, `RequiresAttention` via `OperationItemAttentionRules`.

UI reverse-link from operation items: `SharedRecordLinkCatalog` maps `customer` → `/demo/customers/{businessKey}` (“Open demo customer”).

### Demo Customers surface — CONFIRMED

API `DemoBusinessRecordsController` — `api/demo/business-records`:

| Method | Path |
|---|---|
| GET | `/customers` (`q`, `sourceKind`, `hasConflict`, skip/take) |
| GET | `/customers/{businessKey}` |
| GET | `/customers/{businessKey}/activity` |
| GET | `/customers/{businessKey}/activity/summary` |

Auth: authenticated actor + instance-scoped `integration.read`.

Banner copy: *“This is a demo projection, not the final platform BusinessRecord model.”*

Module-owned DBs (separate DbContexts, **not** the demo projection):

- Proof: `ProofStudentReviewSnapshots` unique `(IntegrationId, CnsStudentId)`.
- Unisa: payment-plan tables with their own keys (`SchoolFundId` unique **with no integration column**, etc.).

**SPECULATION:** a future canonical `BusinessRecord` store / ownership model. Whether non-`customer` record types are emitted by other modules: identity validation allows them, but Demo projection **filters to customer only**, and `SharedRecordLinkCatalog` only maps `customer`.

---

## 9. Dashboard / Control Center

These are **two different operator surfaces**, not one widget system.

| | Dashboard | Control Center |
|---|---|---|
| Route | `/dashboard` | `/control/center` |
| Role | Cross-domain **summary cards** | Live **triage console** |
| API | `GET api/dashboard/default` | `GET api/control/center` (+ health/scheduler) |

### Dashboard — CONFIRMED

**Live:** `DashboardHome.razor` (`/dashboard`)

- Header + “Quick links” (`ManagementSummaryCard`s to Control Center, Integrations, Runs, Schedules, My notifications).
- “Operational summary” grid of permission-aware cards from `GET api/dashboard/default`.
- Each card: title, summary, up to **4 facts**, attention tint, optional nav href.
- Sort: attention severity first, then provider `Order`.

**Deferred stubs (not implemented):** `/dashboard/manage`, `/dashboard/layout`, `/dashboard/add`, `/dashboard/delete` — all say “not yet available in v1.”

**No chart library / chart card type.** Card `Type` is `"summary"` (or `"system"` for the partial-load placeholder). Facts are label/value strings rendered as `<dl>`. Control Center has one **HTML table** (observed worker hosts), not a dashboard chart.

Contract (`Apperture.ControlPlane.Dashboard`):

- `IDashboardCardProvider.BuildAsync` → `DashboardCard?`
- `DashboardDefaultViewService` isolates provider failures and may append `dashboard-partial-load`

**Registered card providers** (wired in `ServiceCollectionExtensions.Governance.cs`):

| Order | Id | Provider | Data source | Facts (hardcoded) | Nav |
|---|---|---|---|---|---|
| 10 | `runtime-health` | `RuntimeHealthDashboardCardProvider` | `IRuntimeHealthQueryService` | degradation reasons, blocked work, anomalies | blocked-work / schedules / run / control center |
| 12 | `blocked-work` | `BlockedWorkDashboardCardProvider` | `IRecoveryQueryService` | blocked runs, retry-eligible | `/control/blocked-work` |
| 14 | `integrations-fleet` | `IntegrationsDashboardCardProvider` | `IIntegrationReadinessService.ListFleetAsync` | total, not runnable, blocking, 7d failures, expected-run silence | integrations list or single id |
| 20 | `needs-attention` | `AttentionDashboardCardProvider` | `IControlAttentionQueryService` (24h, take 20) | open items, highest severity | `/control/center` |
| 25 | `executions-24h` | `ExecutionsDashboardCardProvider` | `IRunQueryService` last 24h | visible/failed/in progress/completed/cancelled/other | `/runs?...` |
| 30 | `recent-activity` | `RecentActivityDashboardCardProvider` | `IControlActivityQueryService` | count, latest status | `/control/center` |
| 32 | `schedules-summary` | `SchedulesDashboardCardProvider` | schedule defs + optional health | definitions/enabled/disabled/(degraded) | `/schedules` |
| 34 | `audit-24h` | `AuditDashboardCardProvider` | `IAuditService.CountAsync` 24h | event count | `/control/audit?hours=24` |
| 36 | `governance-summary` | `GovernanceDashboardCardProvider` | `IRoleProvider` | roles, custom roles | `/config/governance/roles` |
| 38 | `auth-warnings` | `AuthWarningsDashboardCardProvider` | `IAuthenticationAdminOverviewService` | sign-in experience, warning count | `/config/auth` |

Unauthorized providers return `null` (card omitted). Attention mapping is centralized in `DashboardCardAttentionConvention`.

### Control Center — CONFIRMED

`ControlCenterQueryService.GetControlCenterAsync` builds `ControlCenterDto`:

- **Targets:** operations (start), workflows (start), runtime instances (shutdown request) — each with `AvailableActions`.
- **RecentActivity:** executions + runtime control requests (`ControlActivityQueryService`).
- **AttentionItems:** failed runs, stuck shutdowns (>15 min queued), rejected workflow external controls (`ControlAttentionQueryService`).

API: `GET api/control/center?createdFromUtc&takeRecentActivity&takeAttention` (default window **24h**). Also: `GET api/control/activity`, `GET api/control/attention`. UI additionally calls `GET api/control/runtime-health` and `GET api/control/scheduler-status` (capability-gated).

UI sections: Summary pills + shortcut cards; Action needed; Recent problems; Platform health; Worker runtime (heartbeat table + scheduler ops); Recent activity; Targets.

### How tightly coupled to specific metrics — CONFIRMED

**Tight coupling at the card level:**

- Each provider hardcodes `Id`, `Title`, fact **labels**, time windows (almost all 24h; integrations fleet uses 7d operational failure from readiness), and navigation URLs.
- Attention rules are metric-specific (`FromVisibleFailedRunsCount`, `FromIntegrationFleetSignals`, `FromRoleCount`, …).
- UI is a generic renderer of `DashboardCardDto` but **does not** accept operator-defined metrics, queries, or chart types.
- Layout/manage/add/delete pages are stubs — no personalization store.

**Loose coupling at composition:**

- New cards can be added by registering another `IDashboardCardProvider`.
- Provider failures don’t take down the rest.
- Cards disappear if the caller lacks permission rather than showing empty metrics.

Control Center is similarly **hardcoded**: three attention kinds, fixed 24h default, explicit target kinds (`operation` / `workflow` / `runtimeInstance`). It is **not** driven by the dashboard card contract.

Dashboard vs Control Center overlap: Needs Attention and Recent Activity cards **re-query the same control-facade services** that Control Center uses, then link to `/control/center`.

**SPECULATION:** Card `Type` string is unused for branching in `DashboardHome` (always summary layout). Could theoretically support other types later; **today it does not**.

---

## 10. Authorization after IAM-5

There is **no** `ApplicationContext`, `ContextId`, or `ApplicationContextId` on configured resources. The only `AuthenticationContextId` in the repo is Microsoft Entra Conditional Access (IAM-2), not a product context.

### What is platform-wide — CONFIRMED

These concepts apply to the whole **Instance** (one customer deployment). They are not per-module, per-integration, or per-application-context.

| Concept | What it is today | Where |
|---|---|---|
| **Principal** | `IActor` (`Human` / `Machine` / `System` / `RemoteInstance`) from cookie/OIDC claims | `HostPrincipalContext`, `ClaimsPrincipalToActorMapper` |
| **User** | Persisted operator identity; roles attach here | `Users`, `IdentityLinks`, `UserRoles` |
| **Role** | Named bag of `PermissionDescriptor`s; name unique per instance | `Roles` (`HasIndex(Name).IsUnique()`), `RolesController` |
| **Permission** | `(Action, ResourceDescriptor?)` — action + optional type/scope | `PermissionDescriptor`, `ResourceDescriptor` |
| **Resource type** | Catalog vocabulary (`Integration`, `Connection`, `User`, …) | `ResourceTypeConstants` |
| **Scope key** | String boundary: `instance`, `environment:{id}`, `module:{id}`, `connection:{id}`, `operation:{id}`, `environment:{id}:runtime:{id}`, `module:{id}:slot:{name}` | `ScopeKeyConstants` |
| **Governed identity** | Instance of a type: `ResourceType` + `ResourceId` + `ScopeKey` + optional `OwnerId` | `IGovernedResourceIdentity`, `GovernedResourceDescriptor.FromIdentity` |
| **Policy evaluation** | Break-glass → (relationship reserved) → RBAC + scope containment. Ownership checks reserved, not implemented | `RbacAuthorizationPolicyEvaluator`, ADR-0012 |
| **Catalog** | In-memory registry seeded at startup | `PlatformPermissionSeeder`, `ModulePermissionRegistrar`, `IResourceDescriptorRegistry` |
| **Enforcement seam** | `IAuthorizationService.EvaluateAsync(actor, permission, resource, resourceIdentity?)` | `DelegatingAuthorizationService` → `IAuthorizationPolicyEvaluator` |
| **UI gates** | Instance-wide booleans from the same checks | `OperatorCapabilitiesQueryService`, `GET /api/control/capabilities` |

`ResourceDescriptor` in Kernel is `(ResourceType, ScopeKey)` — a **permission catalog primitive**, not a resource-namespace/catalog entry (`src/Apperture.Shared/Apperture.Shared.Kernel/ResourceDescriptor.cs`).

**Scope containment:** `instance` contains every other scope. A grant at `instance` therefore covers module, connection, slot, and environment targets (`ScopeContainmentEvaluator`). Default target scope when callers omit identity is also `instance` (`RbacAuthorizationPolicyEvaluator`).

**Catalog is instance-scoped for almost all platform actions.** `PlatformPermissionSeeder.SeedCore` registers `integration.*`, `connection.*`, `binding.*`, `schedule.manage`, `user.*`, `role.*`, `notification.definition.*`, etc. at `ScopeKeyConstants.Instance`. The only non-instance catalog entries are environment/runtime permissions keyed `environment:{environmentId}`.

Module-contributed permissions are registered at `module:{moduleId}` (`ModulePermissionRegistrar`), not at a product context.

**Roles are instance-global.** `Roles.Name` is unique. `UserRoles` is `(UserId, RoleId)` with no scope column. There is no role-per-context or role-per-integration assignment table.

`RbacAuthorizationPolicyEvaluator` matches action (case-insensitive), optional resource type, and **scope containment**. It does **not** compare `ResourceId`. Ownership (`OwnerId`) is unused. Machine/System/RemoteInstance deny. Human + break-glass grants every check.

`OperatorCapabilitiesQueryService` evaluates **every** flag at instance. `GET /api/control/capabilities` returns a flat DTO. There is no per-context or per-resource capability.

### Entra and access requests (IAM-5) — identity, not authz — CONFIRMED

`docs/02-decisions/S2D-iam5-entra-directory-provisioning-and-access-requests.md`: **Microsoft authentication never implies Apperture authorization.** Group.Read.All is unused for authorization. No SCIM, no group-to-role sync.

IAM-5 adds:

- Directory provisioning: `IUserProvisioningService` / `UsersEntraProvisioningController` (`GET/POST /api/users/entra…`).
- Access requests: `EntraAccessRequests` + limited cookie `Apperture.EntraAccessRequest` + `/access-request` + `AccessRequestController`.
- Approval/reject/list pending require **`user.manage` at instance**.
- Pending uniqueness: `(TenantId, ProviderObjectId)` where Status = Pending (`UX_EntraAccessRequests_PendingTenantIdentity`).
- Identity uniqueness: Local `(Source, ExternalId)`; Entra `(Source, TenantId, ExternalId)`.
- At most one **active** Entra registration (`EntraInstanceRegistrations.IsActive` filtered unique).

`AuthenticationContextId` on `EntraInstanceRegistrations` is Microsoft auth context `c1–c25` (step-up), unrelated to product context.

### Does anything assume a resource can only have GLOBAL (not context-scoped) access?

**There is no application-context scope in the authorization model.** Closest existing “global” is `instance`.

**CONFIRMED instance-wide assumptions:**

1. **Catalog** seeds `integration.*` / `connection.*` / `notification.definition.*` at `instance` only — no `context:{id}` scope key exists.
2. **`instance` contains all scopes**, so an instance grant is effective access to every integration/connection/slot unless a future evaluator stops treating instance as universal.
3. **List + capabilities** always evaluate `integration.read` / `connection.read` at instance; they do not filter by a product context.
4. **Demo/shared-record reads** require instance `integration.read` and then return **all** matching keys.
5. **Notification definition auth** always requires instance `notification.definition.read/manage`, then additionally `integration.read/update` on the target integration (`NotificationDefinitionAuthorization`).
6. **ExecutablePermissionEvaluator** catalog match is exact on `ScopeKey`. A definition that declared a context-scoped permission would fail “unknown permission” against today’s instance/module catalog.

**CONFIRMED that finer-than-instance auth scope already exists (but is not a product context):**

- `Integration.ScopeKey` / `Connection.ScopeKey` can be non-instance.
- Get/update/delete evaluate against that identity’s `ScopeKey`.
- Binding assign uses `module:{id}:slot:{name}`.
- Workflow start uses `module:{id}` or instance `workflow.start`.
- Environment/runtime permissions are environment-scoped.

**Not present:** any check that says “this integration may only be granted at GLOBAL/instance and never at a nested product context.” The gap is that **the catalog, capabilities, and list APIs never offer a context grant**, so operators cannot express context-scoped access even if `ScopeKey` on a row were set.

Lists always query with `instance`, which returns every row because instance contains all inner keys (`SqlIntegrationRepository.GetVisibleAsync`, `SqlConnectionRepository.GetVisibleAsync`).

---

## 11. Migration hazards: optional ContextId + Shared backfill

Adding a **nullable** context column and backfilling existing rows to one reserved Shared id **does not by itself violate** unique indexes that do not include that column. Hazards appear when you later need **the same business key in two contexts**, or when lookups/routes assume one row per key instance-wide.

There is currently **no** `GetByName` / `GetByDisplayName` on integrations or connections. Id-based routes would keep working after a nullable column + Shared backfill.

### Unique indexes that would block same key in two contexts

**CONFIRMED current indexes.** Whether they “break” depends on a later “same name, different context” requirement (**SPECULATION** if that is the goal).

| Table | Index | Columns | Filter |
|---|---|---|---|
| `Roles` | unique | `Name` | none |
| `PlatformSettingValues` | unique | `Key`, `ScopeKind`, `ScopeId` | none — `ScopeKind` is only `"Instance"` \| `"Integration"` |
| `NotificationDefinitions` | unique | `Scope`, `IntegrationId`, `DisplayName` | `[Origin] = N'userCreated'` |
| `NotificationSubscriptions` | unique | `NotificationDefinitionId`, `RecipientKind`, `RecipientRef` | `IntegrationId IS NULL` |
| `NotificationSubscriptions` | unique | `NotificationDefinitionId`, `IntegrationId`, `RecipientKind`, `RecipientRef` | `IntegrationId IS NOT NULL` |
| `NotificationInstances` | unique | `NotificationDefinitionId`, `InstanceDedupeKey` | none |
| `AttentionIncidents` | unique | `IntegrationId`, `ConditionKey` | `[Status] = N'Active'` |
| `IntegrationAttentionRoutings` | unique | `IntegrationId`, `ConditionKey` | none |
| `PlatformEvents` | unique | `Source`, `Type`, `DedupeKey` | `DedupeKey IS NOT NULL` |
| `ApiPollingSyncStates` (module DB) | unique | `ScopeKey` | none — `ScopeKey` is integration id or `"sample"` |
| `ApiPollingKnownRecords` | unique | `StateId`, `ExternalId` | none |
| `ProofStudentReviewSnapshots` | unique | `IntegrationId`, `CnsStudentId` | none |
| `UnisaFundSourceCampusMaps` | unique | `SchoolFundId` | **no integration or context column** |

**Identity / instance-config (likely stay instance-global even with Shared context):**

| Table | Index |
|---|---|
| `IdentityLinks` | `UX_IdentityLinks_Local_Source_ExternalId`; Entra `(Source, TenantId, ExternalId)` |
| `EntraAccessRequests` | `UX_EntraAccessRequests_PendingTenantIdentity` |
| `EntraInstanceRegistrations` | unique `IsActive` where `IsActive = 1` |
| `Users` | `UX_Users_VerifiedContactEmail`, `UX_Users_EmergencyAccess` |
| `WorkflowStartRequests` / `OperationStartRequests` | unique `IdempotencyKey` where not null |
| `ScheduleRuns` | unique `(ScheduleId, FireTimeUtc)` |

**Configured resources with NO unique index beyond PK (CONFIRMED — duplicate display names already allowed):** Integrations, Connections, Bindings, ScheduleDefinitions. `OperationalTeams.Name` is indexed, **not** unique. Binding uniqueness is **application-level** per `(moduleId, slotName, integrationId)`.

### Lookup methods — CONFIRMED

| Method | Lookup key | Hazard if context is added |
|---|---|---|
| `IIntegrationRepository.GetByIdAsync` | `IntegrationId` | Id remains unique; **no widening required** for get-by-id |
| `GetVisibleAsync(scopeKey)` | auth `ScopeKey`, not product context; `instance` returns all | Lists stay instance-wide unless rewritten |
| `IConnectionRepository.GetByIdAsync` / `GetVisibleAsync` | same pattern | same |
| `IBindingRepository.GetBySlotAsync(moduleId, slotName)` | **first** `(module, slot)` — documented as legacy | **CONFIRMED ambiguous** if two contexts bind the same module slot without `integrationId` |
| `GetBySlotAndIntegrationAsync` | module + slot + integration | Safe if integration ids stay unique |
| `SlotBoundResolver` | uses integration when provided, else `GetBySlotAsync` | Runtime path without `IntegrationId` is instance-global |
| `SqlPlatformSettingValueRepository.GetByKeyAsync(key)` | **hardcoded** Instance + `"instance"` | Cannot store/read a Shared vs other context override |
| `GetScopedAsync(key, scopeKind, scopeId)` | `ScopeKind` ∈ {Instance, Integration} | Enum/API has no Context kind |
| `SqlNotificationDefinitionStore.ExistsDisplayNameAsync(scope, integrationId, displayName)` | matches unique index | Duplicate names in two contexts blocked for user-created defs |
| `DemoBusinessRecordProjectionService.GetCustomerAsync(businessKey)` | **instance-wide** case-insensitive key | Two Shared-context customers with same key merge/conflict in projection |
| `SharedRecordReferenceQueryService` | `SharedRecordType` + `SharedBusinessKey` | Provenance is cross-integration, not per context |

### API contracts and route params — CONFIRMED

No context parameter anywhere:

- `GET/POST /api/integrations`, `GET /api/integrations/{id}`, nested `{integrationId}/bindings|schedules|settings|procedures`.
- UI: `/integrations/{IntegrationId}`, `/connections/{ConnectionId}`, `/schedules/{ScheduleId}`, `/runs/{RunId}`.
- `GET /api/demo/business-records/customers/{businessKey}` — key is the only identity.
- `IntegrationDto` / `CreateIntegrationRequestBody`: `Id`, `DisplayName`, `ModuleId`, `ScopeKey` (auth scope), `State`, `Settings` — **no ContextId**.
- `PlatformSettingValueScopeKind` = `Instance | Integration` only.
- `OperatorCapabilitiesDto` — instance booleans; cannot express “can view Shared but not Campus X”.
- Notification list query: `IntegrationId` + optional module, not context.

**Would need widening** (**SPECULATION**) only if clients must address a resource as (context, name) rather than by id.

### Tests — CONFIRMED patterns

- Governance tests evaluate `ScopeKeyConstants.Instance`.
- IAM-5 tests cover tenant-scoped identity and access-request pending uniqueness, **not** resource context.
- Platform settings tests use `ScopeKind = Instance` or `Integration`.
- Notification tests assert `NotificationDefinitionScopes` = `platform` \| `module` \| `integration`.
- Binding tests use `duplicate_active_binding_for_slot` **per integration**, not per context.
- Demo projection tests treat customer `BusinessKey` as instance-wide.

### Scheduling / execution / history / notifications / provenance

**CONFIRMED:**

- Schedule dispatch idempotency is `schedule:{ScheduleId}:{FireTimeUtc}` + unique `(ScheduleId, FireTimeUtc)`. Backfill to Shared does **not** break these **if schedule and integration ids stay globally unique**. Hazard is only if dispatch runs without integration id and hits `GetBySlotAsync`.
- Historical `WorkflowStartRequests` / `OperationStartRequests` / `OperationGroups` carry `IntegrationId`, not context. `AuditLogs` indexed by ModuleId / ScheduleId / RunId — no context column. Past rows would all appear in Shared unless rewritten.
- Notification definition scopes: `platform` / `module` / `integration`. User-created defs always `Scope = integration` + `IntegrationId`. Shared backfill does not collide those indexes. Creating the same display name in another context **would**.
- Demo customers keyed by `BusinessKey` instance-wide. `SharedRecordIdentity` is `(RecordType, BusinessKey)` across integrations. UNISA `SchoolFundId` unique **with no integration id**.

### What Shared backfill does vs does not break

**CONFIRMED safe** (nullable column + all existing rows → one Shared id): PK lookups, id-based APIs, schedule fire uniqueness, Entra/IAM uniqueness, existing integration/connection/binding/schedule rows (no unique names).

**CONFIRMED still instance-global after backfill** (behavior, not a unique-index failure): list APIs, capabilities, demo customer key, shared-record lookup, `GetBySlotAsync`, platform settings Instance scope, UNISA `SchoolFundId`, ApiPolling `ScopeKey` uniqueness keyed only by integration id.

**SPECULATION (only if a second context must reuse keys):** widen `PlatformSettingValues`, notification display-name uniqueness, attention routing, ApiPolling `ScopeKey`, Proof `(IntegrationId, CnsStudentId)` if integration ids are reused, UNISA `SchoolFundId`, and add context to `GetBySlotAsync` / demo `/customers/{BusinessKey}` / capabilities.

---

## 12. Dependency map: existing abstractions vs later concepts

This is **not** a plan. It maps **what already exists** that a later design could reuse, vs what would be new. “Reusable” here means the abstraction already does analogous work; it does **not** mean the table or type should be overloaded.

| Later concept | Closest existing abstraction | Reuse shape (CONFIRMED current behavior) | Not this (CONFIRMED) |
|---|---|---|---|
| **Application Contexts** | None. Closest: `ScopeKeyConstants` (`instance` / `module:` / `environment:`) and optional `Integration.ScopeKey` / `Connection.ScopeKey` | Scope-key *string format* and `IGovernedResourceIdentity` already carry a boundary string. `instance` containment is the only hierarchy. | No context entity, no `context:{id}` catalog entries, no context on roles/`UserRoles`, no context on capabilities. Entra `AuthenticationContextId` is unrelated. |
| **Resource Catalog / namespace** | Three separate registries: (1) `IResourceDescriptorRegistry` (auth permissions), (2) `IConnectorDefinitionRegistry` (connector kinds), (3) `IPlatformSettingDefinitionRegistry` + `IIntegrationConfigurationRegistry` (typed knobs + module config) | Compile-time registration, namespaced keys, contribution-at-startup, `GetAll`/`TryGet` discovery. Connector catalog is explicitly **not** stored in settings (`platform-settings-option-providers-and-scopes.md` §L). | `IResourceCatalogProvider` does not exist. `PlatformSettingValues` must not become a catalog. `ResourceDescriptor` is `(type, scopeKey)` for authz, not a namespaced resource record. Connector SQL table was dropped. |
| **Resource capability descriptors** | `ConnectionRequirementDefinition` / capability slots; `ConnectorDefinition` + `ConnectionFieldDescriptor`; module `GovernedResources` / `GovernedActions`; `IDashboardCardProvider` (operator summary cards, not resource capabilities) | Slot = named contract type a module requires. Connector definition describes fields. Module manifest declares governed types/actions into the **permission** catalog. | No discoverable “this resource can power a chart / table / dashboard panel” descriptor. Dashboard cards are hardcoded metric providers. `AllowMultipleBindings` is declared but not enforced as multi-bind. |
| **Cross-context bindings** | `Binding` (slot → connection, optional `IntegrationId`); `BindingEligibilityService`; `IBindingRepository.GetByConnectionIdAsync` | Connections are already **instance-global and reusable** across integrations. One connection, many bindings. Eligibility is by contract type, not ownership. | Bindings are integration/slot-scoped, not context-scoped. No “expose this resource to another Application” object. Legacy `GetBySlotAsync` is module-global first-match. |
| **Services** | `ModuleManifest.Procedures` + `IProcedureRegistryService`; `IOperationDefinitionProvider`; `IntegrationProcedureService.LaunchAsync`; `ResourceTypeConstants.ServiceAccount` (auth type only) | Procedures are the operator launch surface for a workflow or operation on an integration. Same dispatch path as schedules (`IWorkflowStartDispatchService` / `IOperationStartDispatchService`). | No Service aggregate, no service catalog, no long-lived service instances distinct from integrations/procedures. Host `Add*` DI is not a module-service SPI. |
| **Dashboard Panels** | `IDashboardCardProvider` + `DashboardDefaultViewService` + `GET api/dashboard/default`; Control Center is a separate hardcoded facade | Composition: register a provider, isolate failures, omit unauthorized cards. UI already renders a generic `DashboardCard` (title, facts, attention, href). | Cards are tightly bound to specific metrics and 24h windows. No operator-defined panels, no chart types, no capability-driven discovery. Manage/layout/add/delete routes are stubs. Control Center does not use the card contract. |
| **Future Workflow operations** | `IWorkflowStartDispatchService`, `IOperationStartDispatchService`, `OperationRuns`, start-request tables + idempotency keys, `IWorkflowExecutor` / `DefaultWorkflowExecutor`, `ScheduleDispatchRunner` (start-only), `IWorkflowRuntime` (stub adapter), `IWorkflowDefinitionProvider` on modules | **One execution system already serves workflows and module operations.** Schedules enqueue durable starts; they do not run steps. `OperationRuns.Id` is the operator run id for both. Portable workflow model + DTFx/DF stubs sit behind `IWorkflowRuntime`, unused on the hot path. | Do not treat `IWorkflowStarter`, `DurableTaskRuntimeAdapterStub`, `DurableFunctionsRuntimeStub`, or `SchedulerHostStub` as a second production executor. No visual designer. `/workflows/start` is a UI stub. |

### Adjacent catalogs that are easy to confuse

| Catalog | Purpose today | Stored where |
|---|---|---|
| Permission / resource descriptor registry | Authz vocabulary | In-memory, seeded at host start |
| Connector definition registry | Connector *kinds* and field schemas | In-memory; SQL table dropped |
| Platform / integration setting definitions | Typed operator knobs | In-memory defs + `PlatformSettingValues` rows |
| Module installation states | Enable/disable discovered modules | `ModuleInstallationStates` |
| Navigation menu | Control Panel / rail | `ControlPanelMenuDefinitions` + optional module nav (unused) |
| Shared record identity | Cross-integration observation key on items | Columns on `OperationItems`; demo projector only |

---

## Essential files (index)

**Nav:** `ControlPanelMenuDefinitions.cs`, `NavRail.razor`, `ControlPanel.razor`, `MainLayout.razor`, `App.razor`, `RouteToSectionResolver.cs`, `MenusController.cs`, `AccountUiRoutes.cs`, `DeveloperTaskRoutes.cs`, `OperatorCapabilitiesDto.cs`.

**Domain / persistence:** `AppertureDbContext.cs`, `AppertureDbContextModelSnapshot.cs`, `Integration.cs`, `Connection.cs`, `Binding.cs`, `ScheduleDefinition.cs`, `NotificationDefinition.cs`, `AuditRecord.cs`, corresponding `*Entity` types.

**Settings:** `src/Apperture.ModuleSdk/.../PlatformSettings/*`, `src/Apperture.Core/.../PlatformSettings/*`, `PlatformSettingsService.cs`, `IntegrationSettingsService.cs`, `ConfigPlatformSettingsController.cs`, `docs/01-architecture/platform-settings-registry.md`, `platform-settings-option-providers-and-scopes.md`.

**Modules:** `.ai/playbooks/module-sdk.md`, `IModule.cs`, `ModuleManifest.cs`, `IIntegrationConfigurationContributor.cs`, `SqlModuleRegistryService.cs`, `tests/Apperture.ArchTests/IntegrationSdkBoundaryDependencies.cs`.

**Connections / execution:** `ConnectionManagementService.cs`, `SlotBoundResolver.cs`, `ScheduleDispatchRunner.cs`, `WorkflowStartDispatchService.cs`, `IWorkflowRuntime.cs`, `DefaultWorkflowExecutor.cs`.

**Notifications / business data / dashboard:** `NotificationInstanceCoordinator.cs`, `AttentionIncidentObservationHostedService.cs`, `DemoBusinessRecordProjector.cs`, `SharedRecordIdentity.cs`, `DashboardContracts.cs`, `ControlCenterQueryService.cs`.

**Authz:** `IAuthorizationService.cs`, `RbacAuthorizationPolicyEvaluator.cs`, `ScopeContainmentEvaluator.cs`, `PlatformPermissionSeeder.cs`, `ScopeKeyConstants.cs`, `ResourceTypeConstants.cs`, `OperatorCapabilitiesQueryService.cs`, `docs/02-decisions/S2D-iam5-entra-directory-provisioning-and-access-requests.md`.
