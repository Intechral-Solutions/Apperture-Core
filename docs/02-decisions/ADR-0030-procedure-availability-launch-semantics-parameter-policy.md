# ADR-0030: Procedure availability, launch semantics, and start-parameter display policy

## Status

Accepted

## Context

Epic 56 introduced module-contributed procedures and integration-scoped listing, detail, and launch. Operators need a **single honest story** from the server: which procedures exist, whether they can be launched, why not, how launch failures map to HTTP outcomes, and how persisted start parameters relate to later UI disclosure.

## Decision

1. **Availability vs launchability (list/detail)**  
   - **Integration readiness** (`integrationReady`) reflects `IntegrationReadinessProjection.IsRunnable` for the integration.  
   - Each procedure row includes **`launchable`** and **`blockedReasonCodes`** (stable string codes).  
   - Blocked reasons include: `integration_not_ready`, `target_not_executable`, `insufficient_procedure_permission` (procedure-level `RequiredPermissions`), `insufficient_workflow_permission`, `insufficient_operation_permission` (cannot pass the same checks as `EnsureCanStartWorkflowAsync` / `EnsureCanStartOperationAsync` for the target).  
   - When the target definition is missing, only `target_not_executable` is applied for execution auth (no redundant execution code).

2. **Launch HTTP semantics**  
   - Success: **202 Accepted** with launch result body.  
   - Failures return **deterministic status codes** with JSON `{ "code", "error" }` using codes such as `integration_not_found`, `integration_not_ready` (409), `invalid_parameters` (400), `workflow_start_forbidden` / `operation_start_forbidden` (403), `dispatch_failed` (500), etc. (see `IntegrationProcedureLaunchErrorCode`).

3. **Workflow / operation parity**  
   - Both launch kinds use the same readiness, target-existence, procedure-permission, parameter validation, orchestration-safety, **then** workflow vs operation start authorization and dispatch, with matching provenance fields on start requests and runs.

4. **Parameter persistence and display**  
   - Operator-supplied parameters are serialized to **`ParametersJson`** on `WorkflowStartRequestEntity` / `OperationStartRequestEntity` (existing pattern).  
   - Run detail exposes **`StartParametersJson`** when present, subject to the same operator visibility rules as other run metadata.  
   - **No field-level redaction** in this ADR: values are shown as persisted. **Secret-bearing .NET types** remain rejected at launch via `OrchestrationSafeValidator`. Operators should treat JSON as potentially sensitive (PII/business data); future work may add redaction or policy-driven withholding without changing the persistence model.

## Consequences

### Positive

- UI can rely on list/detail for disabled launch and explanations without guessing from separate APIs.  
- Launch failures are debuggable and testable (status + code + message).  
- Workflow and operation procedures stay aligned for auth and provenance.

### Negative

- Listing evaluates start authorization per procedure (extra work; acceptable while procedure counts stay small).

### Neutral

- Extends [ADR-0029](./ADR-0029-module-contributed-procedures.md); does not reintroduce generic connector/client operations.
