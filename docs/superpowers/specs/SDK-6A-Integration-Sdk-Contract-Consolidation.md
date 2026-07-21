# SDK-6A — Integration SDK Contract Consolidation

**Date:** 2026-07-21  
**Status:** Implemented  
**Inventory:** [sdk6a-integration-sdk-contract-inventory.md](../audits/sdk6a-integration-sdk-contract-inventory.md)

## Goal

Establish `Apperture.ModuleSdk.Integration` as the intentional boundary between integration code and platform internals. FileImport and ApiPolling now share the same execution, itemization, aggregation, and retry contracts.

## Decisions

1. **Same namespaces, new assembly** — Types remain in `Apperture.Core.Application.*` namespaces but live in `Apperture.ModuleSdk.Integration` to avoid a breaking rename sweep while enforcing project-reference boundaries.
2. **Platform owns persistence** — Modules write via `IOperationItemWriter`; stores and reconciliation stay in Core.Application + Persistence.
3. **Progressive complexity preserved** — HelloWorld references Integration but uses only `IStepHandler` + signals; no itemization required.
4. **No schema changes** — Contract consolidation only; existing run/item/retry evidence preserved.

## Sample migration

| Module | Change |
|--------|--------|
| HelloWorld | `Core.Application` → `ModuleSdk.Integration` |
| FileImport | Same + `OperationItemReporting` / `OperationGroupAggregator` in itemization |
| ApiPolling | Same + shared reporting helpers |

## Architecture tests

`IntegrationSdkBoundaryDependencies` (ArchTests) enforces sample modules do not reference Persistence, Management API/Host, WorkerHost, or EF Core.

## Authoring path

```text
Simple procedure (HelloWorld)
  → IStepHandler + OperatorExecutionResult + optional signals
Itemized procedure (FileImport / ApiPolling)
  → context.Signals.OperationItems + OperationGroupAggregator + OperationItemReporting
Per-item retry
  → IOperationItemRetryHandler registered on Management Host
```

Integration code must **not** reference `AppertureDbContext`, EF entities, or SQL persistence types.
