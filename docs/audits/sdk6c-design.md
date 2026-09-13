# SDK-6C Design — Typed Integration Configuration, Connections, Secrets, and Readiness

**Date:** 2026-07-21  
**Status:** Authoritative design for SDK-6C implementation  
**Audit:** `docs/audits/sdk6c-current-state-audit.md`  
**Predecessor:** SDK-6B closed

## 1. Ownership model

| Concept | Declared by | Owned/persisted by | Consumed by |
| --- | --- | --- | --- |
| Platform-wide settings (SMTP, auth UI, …) | Platform registries | Platform (`PlatformSettingValues` instance scope + secrets) | Platform services only — **not** integration-instance config |
| Module setting declarations | Integration module (SDK) | Code only | Control plane catalog + readiness |
| Integration-instance setting values | Operator / API | Platform (integration-scoped `PlatformSettingValues`) | Runtime accessor (never raw tables) |
| Secret values | Operator via sensitive settings or connection material | Platform `ISecretStore` | Runtime secret resolver for authorized module code |
| Reusable connection instances | Operator | Platform connection entities + secret material | Binding resolution |
| Integration connection bindings | Operator | Platform binding entities | `IRuntimeSlotResolver` / SDK connection accessor |
| Connection verification evidence | Platform verifiers | Connection verification fields | Readiness + operator UI |
| Integration readiness | Platform evaluator | Computed projection (not required to persist) | API, UI, launch, schedule |
| Procedure readiness | Platform evaluator using module procedure requirement sets | Computed | API, UI, launch |

**Hard rule:** Integration authoring projects depend only on `Apperture.ModuleSdk.Integration` (+ Abstractionsctions/Testing). They must not reference settings/secret/connection EF entities, `AppertureDbContext`, Management API DTOs, WorkerHost types, or env-only bootstrap for configuration.

## 2. Typed settings design

### Authoring types (`Apperture.ModuleSdk.Integration.Configuration`)

- `IntegrationSettingValueKind`: `String`, `MultilineString`, `Integer`, `Decimal`, `Boolean`, `Choice`, `Uri`, `Duration`, `TimeZone`, `SensitiveText`
- `IntegrationSettingDefinition`: Key, DisplayName, Description, ValueKind, IsRequired, DefaultValue (forbidden when sensitive), AllowedValues, constraints (min/max, pattern, min/max length), Order, Group, Sensitivity
- `IIntegrationConfigurationContributor`: returns setting definitions, connection requirements, and optional procedure requirement sets

Existing `IModuleIntegrationSettingDefinitionContributor` / `PlatformSettingDefinition` remain supported via an adapter that maps Integration → Platform definitions for persistence/UI reuse.

### Mapping to persistence

- Reuse integration-scoped `PlatformSettingValues`
- Extend `PlatformSettingValueType` / input kinds as needed for Decimal, Uri, TimeZone; Multiline via input kind; Sensitive via `IsSensitive` + Secret input
- **No secret defaults**; create/replace/clear only through secret verbs
- GET responses: non-sensitive values + sensitive **presence** (`IsSecretConfigured`) only

## 3. Runtime settings access

- Preferred: `IIntegrationOptions<TOptions>` / typed bind from resolved settings for the current integration execution context
- Practical: `IIntegrationSettingsAccessor` with `GetRequired<T>`, `GetOrDefault<T>`, `GetSecretAsync` (sensitive only)
- Conversion/validation failures → deterministic `IntegrationConfigurationException` (safe messages, no plaintext secrets)
- Modules never parse secret GUIDs or query configuration tables

## 4. Secret integration design

Flow for sensitive settings:

1. Management API accepts plaintext only on write (replace)
2. Platform stores plaintext solely via `ISecretStore`
3. Settings row stores opaque reference
4. GET exposes presence/state only
5. Runtime resolves secret for authorized module code through accessor
6. Logs, audit, events, operation metadata, readiness text never include plaintext

Reuse: `PlatformSettingSecretOperations`, existing provisioners, audit action names. Clear removes reference + secret material per existing patterns.

## 5. Connection requirements and bindings

### Authoring

`ConnectionRequirementDefinition`:

- Key (stable; e.g. `CnsApi`, `IntegrationDb`, `Sftp`)
- DisplayName, Description
- IsRequired
- CapabilityContract (CLR interface full name or capability kind key)
- VerificationExpectation (`Required`, `Advisory`, `UnsupportedOk`)
- AllowMultipleBindings (default false)

Capability kinds for current needs (not a universal connector framework):

- HTTP API (`ICampusNexusApiClient` / future HTTP abstraction where samples need it)
- Relational DB (`IDbConnection`)
- SFTP (`ISftpFileClient`)

### Runtime

- `IIntegrationConnectionAccessor.GetRequiredAsync<T>(key)` / `TryGetAsync<T>`
- Platform adapter uses existing binding + `IConnectionResolver` / `SlotBoundResolver`
- Do not force one LCD interface across HTTP/DB/SFTP

Slots continue to register into `IModuleSlotRegistry` from contributor metadata (replace ad-hoc hosted services where modules migrate).

## 6. Verification model

Reuse connection verification statuses; normalize operator-safe reason codes:

- ConfiguredUnverified
- Verified
- InvalidCredentials
- EndpointUnreachable
- InvalidConfiguration
- UnsupportedVerification
- StaleAfterConfigurationChange

Results: status, reason code, safe summary, timestamps — never credentials or raw remote bodies.

Distinguish provider connectivity verification from optional module-specific compatibility checks (module checks contribute readiness reasons, not connection entity mutation, unless existing verifier hooks already cover it).

## 7. Integration and procedure readiness

### Result shape (`ConfigurationReadinessResult`)

- Overall state: `Ready` | `NotReady` | `Degraded` (optional deps unhealthy)
- Reasons[]: Code, Severity, Message, AffectedSettingKey?, AffectedConnectionKey?, RemediationHint?
- EvaluatedAtUtc

### Required conditions (blocking when applicable)

- Module installed and integration enabled/active
- Required settings present and valid
- Required secrets present (sensitive settings configured)
- Required connection bindings present and eligible
- Bound connections active/available
- Required verification acceptable (per requirement expectation)
- Module-specific checks (optional contributor hook)

### Procedure readiness

`ProcedureRequirementSet`: ProcedureId → required setting keys + connection keys (subset of module declarations).

- Integration readiness = all module-required deps
- Procedure readiness = procedure subset (+ always integration active/enabled)
- Example: Unisa local fixture may omit Sftp; export requires Sftp + IntegrationDb

API surfaces both; launch uses **procedure** readiness when a procedure id is known, else integration readiness.

## 8. Execution enforcement

Same evaluator for:

- Manual procedure launch (`IntegrationProcedureService`)
- Scheduled dispatch path (reject/withhold with safe reason; no false success; no indefinite queue without evidence)
- Defensive runtime check when configuration changed after dispatch (fail run with audit-safe reason)

Non-ready → predictable rejection, safe reason codes, run/audit evidence without secret leakage.

## 9. Module migration summary (intent)

| Module | Change |
| --- | --- |
| HelloWorld | Configuration-free: no required settings/connections; demo notification settings remain optional and must not block readiness |
| FileImport | Only settings required by existing fixture behavior; keep local execution simple |
| ApiPolling | Declare HTTP (or sample directory API) connection requirement; canonical connection+verification sample; retain settings for poll behavior |
| Proof | Declare `CnsApi` + `IntegrationDb` (+ typed settings if any); consume via accessors; keep Persistence adapter boundary; remove superseded env bootstrap |
| Unisa | Declare `CnsApi`, `Sftp`, `IntegrationDb`; procedure subsets; independent missing reasons; keep Persistence boundary |

## 10. Testing toolkit

Extend `IntegrationProcedureTestHost`:

- `WithSetting` / invalid settings
- `WithSecret` / missing secret
- `WithConnectionBinding` / fake capabilities
- Verification state helpers
- Readiness evaluation helpers
- Assert helpers that secret plaintext is absent from logs, events, outputs, operation metadata, readiness messages

## 11. Management API / UI

Prefer extending existing integration settings, bindings, connections, readiness, and procedure endpoints:

- Declarations (settings + connection requirements)
- Non-sensitive values + sensitive presence
- Set/replace/clear sensitive
- Compatible connections, bind/unbind
- Verify
- Integration readiness + procedure readiness

Minimal UI: adapt Integration settings/bindings/readiness/procedure launch surfaces — no redesign; no SMTP UI duplication.

## 12. Persistence

- Forward-only migrations only if new columns needed (e.g. richer verification staleness, procedure readiness cache — prefer compute-first)
- No plaintext in setting-value tables
- Validate migrate-from-current and empty-database paths

## 13. Architecture / security protections

- Arch tests: modules ↛ persistence/secret entities/Management DTOs; Integration SDK ↛ EF/SQL; Testing toolkit persistence-independent; only adapters bridge
- Security tests: no plaintext persistence/GET/logs/events/audit/metadata/readiness; authz; no cross-integration secret enum; safe verification errors
- Update public API expected-type list for new SDK exports

## 14. Out of scope (explicit)

SMTP/platform settings redesign, notification providers, packaging, capability manifests beyond config/connections, marketplace, codegen, scaffolding, shared entities, workflow composition, module UI, scheduler redesign, WorkerHost lifecycle.

## 15. Closure criteria (preview)

SDK-6C closable when: contracts shipped, modules migrated, tests green (including arch/security), Aspire smoke documented, external Proof/Unisa live systems **not** claimed without real credentials, remaining work filed under SDK-6D+.
