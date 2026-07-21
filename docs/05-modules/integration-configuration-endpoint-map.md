# Integration configuration endpoint map (SDK-6D)

**Scope:** Operator configuration workflow on top of SDK-6C contracts  
**Audience:** Management UI, CLI, and automation authors

## Workflow

Typical operator loop:

1. **Discover requirements** — declarations for the integration's module  
2. **Configure settings and secrets** — integration-scoped platform settings  
3. **Bind connections** — map module connection requirements to connection instances  
4. **Verify connections** — optional/advisory depending on module expectation  
5. **Check readiness** — integration-wide and procedure-scoped  
6. **Launch or schedule** — blocked when readiness is not satisfied

## Endpoints

All routes require authentication. Most read routes require `integration.read` on the instance scope. Mutations require the permissions noted.

| Step | Method | Route | Purpose |
| --- | --- | --- | --- |
| Declarations | `GET` | `/api/integrations/{id}/configuration/declarations` | Module-authored settings, connection requirements, procedure requirement sets |
| Settings list | `GET` | `/api/integrations/{id}/settings` | Resolved integration-scoped settings (secrets presence-only) |
| Setting get | `GET` | `/api/integrations/{id}/settings/{key}` | Single resolved setting |
| Setting set | `PUT` | `/api/integrations/{id}/settings/{key}` | Scalar/multi value update; secret replace/clear via `secretOperation` |
| Setting reset | `DELETE` | `/api/integrations/{id}/settings/{key}` | Remove integration DB override |
| Setting options | `GET` | `/api/integrations/{id}/settings/{key}/options` | Provider-backed allowed values |
| Bindings list | `GET` | `/api/integrations/{id}/bindings` | Active slot bindings |
| Binding create | `POST` | `/api/integrations/{id}/bindings` | Assign connection to slot (`binding.assign`) |
| Binding delete | `DELETE` | `/api/integrations/{id}/bindings/{bindingId}` | Remove binding |
| Eligible connections | `GET` | `/api/integrations/{id}/eligible-connections` | Connections compatible with a slot (query: slot, contract) |
| Module slots | `GET` | `/api/integrations/{id}/module-slots` | Legacy slot catalog for binding UI |
| Connection verify | `POST` | `/api/connections/{connectionId}/test-connect` | Connector test-connect; updates verification status |
| Integration readiness | `GET` | `/api/integrations/{id}/readiness` | Runnable flag and blocking/advisory issues |
| Procedure readiness | `GET` | `/api/integrations/{id}/procedures/{procedureId}/readiness` | Requirement-subset readiness for one procedure |
| Operator detail | `GET` | `/api/integrations/{id}/detail` | Combined metadata, readiness, bindings, schedules |
| Procedures | `GET` | `/api/integrations/{id}/procedures` | Launchability and blocked reason codes |
| Procedure launch | `POST` | `/api/integrations/{id}/procedures/{procedureId}/launch` | Manual execution (enforces readiness) |

## Management client (SDK-6D)

`IManagementClient` exposes:

- `GetIntegrationConfigurationDeclarationsAsync(integrationId)`
- `GetIntegrationReadinessAsync(integrationId)` — wraps integration readiness route
- `GetIntegrationProcedureReadinessAsync(integrationId, procedureId)`
- Existing settings, bindings, and operator detail methods

Connection requirement DTOs expose `capabilityKind`, operator-facing `capabilityLabel`, and `technicalContractType` (CLR contract) for progressive disclosure.

## Sample module note (ApiPolling)

`ConnectionBackedSampleDirectoryApiClient` resolves the required `DirectoryApi` binding at runtime. Embedded fake directory data is **opt-in only** via `SampleApiPolling:AllowEmbeddedFallback` or `APPERTURE_SAMPLE_APIPOLLING_ALLOW_EMBEDDED_FALLBACK=true`. Production readiness still requires a bound connection; fallback does not satisfy readiness.

## Related docs

- [integration-configuration-operator-guide.md](./integration-configuration-operator-guide.md)
- [integration-typed-settings-persistence.md](./integration-typed-settings-persistence.md)
- SDK-6C closure: `docs/audits/sdk6c-closure.md`
- SDK-6D UX audit: `docs/audits/sdk6d-operator-ux-audit.md`
