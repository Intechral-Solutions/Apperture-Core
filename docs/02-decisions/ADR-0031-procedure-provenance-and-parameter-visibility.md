# ADR-0031: Procedure provenance and start-parameter visibility

## Status

Accepted

## Context

Epic 56 gives operators parameterized **module procedures** (workflow or operation launches) scoped to an **integration**. Epic 56B aligned launch and registry behavior. Operators and auditors still need a **single explicit story** for:

- What inputs are stored when a procedure is launched  
- Where they live in persistence  
- What appears on run / start detail for authorized operators  
- What is **not** promised (secrets, fine-grained redaction)  

Today the platform already persists `IntegrationId`, `ProcedureId`, and JSON parameters on start handoff rows; `RunQueryService` projects them into `RunDetailDto`. That behavior must be **documented as policy**, not inferred from implementation.

## Decision

### 1. What is persisted for procedure launches

For both **workflow** and **operation** procedure launches (via `IntegrationProcedureService` → dispatch services):

| Field | Meaning |
|--------|---------|
| **`IntegrationId`** | The integration from which the procedure was launched (string id). Stored on the **run** (`OperationRuns`), **instance** (workflow/operation instance), and **start request** (`WorkflowStartRequests` / `OperationStartRequests`) so handoff and forensics stay aligned. |
| **`ProcedureId`** | The module procedure identifier (e.g. `Sample.HelloWorld.procedures.greet`). Stored on the same three places as `IntegrationId`. Nullable for non-procedure starts (direct API / scheduler / retry paths). |
| **`ParametersJson`** | Operator-supplied start parameters **after** procedure schema validation, default merge, and `OrchestrationSafeValidator`. Stored only on the **start request** row (`WorkflowStartRequests.ParametersJson` / `OperationStartRequests.ParametersJson`). |

Workflow and operation paths follow the **same** provenance shape at persistence and in `GetRunAsync` projection.

### 2. Shape of persisted parameters

- Parameters are serialized with **`System.Text.Json`** from the `IReadOnlyDictionary<string, object?>` passed into dispatch (`WorkflowStartDispatchService` / `OperationStartDispatchService`).  
- The result is a **JSON object** (UTF-16 string in SQL). Key order is **not** guaranteed stable across serializers or dictionary ordering; treat persisted JSON as **canonical storage**, not a stable lexical ordering contract.  
- Empty or null parameter maps persist as **`null`** `ParametersJson` (column omitted logically).

### 3. What operators see on run detail (`GET /api/runs/{id}`)

`RunDetailDto` exposes:

- **`integrationId`** / **`procedureId`** — copied from the **run** row (same values as on the start request when both are written in one transaction).  
- **`startParametersJson`** — **full** persisted JSON from the start request, **without** field-level redaction or withholding in the current product.

**Visibility rule:** Any principal authorized to read the run (per workflow/operation run-read rules) receives the **same** `startParametersJson` the control plane stored. There is **no** separate “operator vs admin” split for procedure parameters today.

This aligns with **ADR-0020 / ADR-0021** patterns for **step output and failure diagnostics** (availability/display policy on those surfaces). Start parameters are **not** governed by those ADRs; they are a **flat JSON blob** on the start request.

### 4. Sensitive inputs and limits

- **Procedure parameters are intended for non-secret operational inputs** (names, flags, business identifiers, etc.).  
- **Secret-bearing .NET types** are **rejected at launch** by `OrchestrationSafeValidator` (same as other orchestration starts).  
- **Plain-text strings** are **not** scanned for “looks like a password”; operators must not put secrets into procedure fields until a future **sensitive-field or SecretRef model** exists for procedures.  
- The UI copy on run detail states that JSON may contain **PII or business-sensitive data** and that **future policy may limit visibility**; that is forward-looking honesty, not current behavior.

### 5. Run list (`GET /api/runs`)

`RunSummaryDto` includes **`integrationId`** and **`procedureId`** when present on the run row, for **both** workflow and **operation** runs (parity with workflow list behavior).

## Consequences

### Positive

- Provenance is **explicit**, **symmetric** for workflow vs operation procedures, and **testable** end-to-end.  
- No false claim of field-level secrecy; guardrails match what the code actually does.

### Negative

- Full JSON exposure for anyone who can read the run; tightening later requires a deliberate policy change and possibly new DTO fields.

### Neutral

- Extends [ADR-0029](./ADR-0029-module-contributed-procedures.md) and [ADR-0030](./ADR-0030-procedure-availability-launch-semantics-parameter-policy.md); does not add generic connector operations.
