Epic 56 summary — Module-contributed procedures and parameterized operator execution
Objective
Give operators an intentional, governed action surface per integration: modules declare named procedures (workflow or operation launches) with metadata and parameters, instead of exposing generic connector/client operations or introspected RPC-style APIs.

What shipped (conceptually)
ADR-0029 — Procedures as the deliberate operator model vs generic client ops; ties to modules, integrations, runs, and ADR-0027/connector posture.
SDK — ModuleProcedureDeclaration, ProcedureLaunchKind, optional Procedures on ModuleManifest; registry via IProcedureRegistryService (host/catalog wiring).
Shared validation — OrchestrationParameterSchemaValidator (and operation path reuse) over the existing JSON fields[] contract, with UI-oriented hints where added (label, help, default).
Management API — GET/POST under api/integrations/{integrationId}/procedures (list, detail, launch) through IntegrationProcedureService: integration load, module match, readiness, target existence, then existing workflow/operation start authorization + dispatch.
Execution & provenance — ProcedureId and IntegrationId on workflow/operation **run**, **instance**, and **start request** rows; `ParametersJson` on start requests; `RunDetailDto` projects full `startParametersJson` for authorized readers (**ADR-0031**, Epic 56C). Operation **run list** summaries include integration + procedure ids (parity with workflow).
UI — Integration detail: procedure list, ProcedureParameterForm, launch → navigate to run detail; management client DTOs/helpers.
Sample — HelloWorld module declares a greet procedure targeting its workflow; name flows into workflow parameters and visible step output. **Epic 56B** adds **`Sample.HelloWorld.procedures.opPing`** (operation launch to `DoWork`) with a real parameter schema.
Registry — `ProcedureRegistryService` rebuilds when the enabled-module activation signature changes (no stale empty catalog after installs become effective).
Docs — **ADR-0031** procedure provenance and parameter visibility; terminology updated; tests cover schema validation, governance HTTP shape, operation/workflow parity, registry lifecycle, **HTTP run detail after procedure launch**, and related regressions.
Operator-facing truth (after Epic 56)
“This integration exposes these procedures” (server-filtered, not client-invented).
“Each procedure is contributed by the module with stable id and launch kind.”
“Parameters are described and validated on the server; the UI uses a small metadata-driven form.”
“Launch goes through existing workflow/operation execution with procedure (and integration) provenance on the run/start path.”
“Start parameters are stored as JSON on the start request; run detail shows the full blob to run readers unless a future sensitive-field policy says otherwise (ADR-0031).”
Architectural result
Integrations remain the scope where procedures are listed and launched.
Modules own the control surface via explicit declarations.
No generic connector operation API was added; REST stays named procedures only.
Remaining gaps (typical next steps)
Dedicated HTTP coverage for **`target_not_executable`** (404) when a procedure’s target is absent from registries, if product adds a test-only declaration or second module shape.
**Sensitive-field / SecretRef** model for procedure parameters if product needs fine-grained withholding or redaction beyond today’s full JSON for run readers.
UI polish (validation messages, loading states) and optional integration docs cross-links beyond terminology.
