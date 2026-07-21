# SDK-6A — Integration SDK Contract Inventory

**Date:** 2026-07-21  
**Status:** Implemented  
**Package:** `Apperture.ModuleSdk.Integration`

## Pre-consolidation model

Before SDK-6A, integration authoring contracts were split across:

| Layer | Responsibility | Problem |
|-------|----------------|---------|
| `Apperture.ModuleSdk.Abstractions` | Manifest, signals, module identity | Too thin for execution |
| `Apperture.Core.Application` | Step handlers, itemization, retry, outcomes, settings | Platform + integration mixed |
| `Apperture.RuntimePlane.*` | Runtime adapters | Correct, but modules imported Core.Application |

`Sample.FileImport` and `Sample.ApiPolling` produced similar platform records through **parallel module-local mappers** (`FileImportOperationItemization`, `ApiPollingOperationItemization`) with no shared aggregation or failure taxonomy.

## Target boundary (SDK-6A)

```text
Integration implementation (.Module + .Contracts)
    ↓
Apperture.ModuleSdk.Abstractions (manifest, signals)
Apperture.ModuleSdk.Integration   (execution, itemization, retry, settings contributor)
    ↓
Apperture.Core.Application          (platform stores, validation adapters, orchestration)
    ↓
Persistence / Runtime / Control plane
```

## Public integration SDK surface

### Execution (`Apperture.Core.Application.Execution` namespace, Integration assembly)

| Type | Responsibility | Consumers |
|------|----------------|-----------|
| `IStepHandler` | Procedure step entry point | All sample modules |
| `IStepExecutionContext` | Run correlation, slots, signals | Step handlers |
| `IStepExecutionSignals` | Events, logs, telemetry, operation items | Step handlers |
| `IModuleStepHandlerRegistrar` | Register handlers at startup | Module classes |
| `OperatorExecutionResult` | Declared success/failure outcomes | All handlers |

### Operation itemization (`Apperture.Core.Application.OperationItems`)

| Type | Responsibility | Consumers |
|------|----------------|-----------|
| `IOperationItemWriter` | Begin/append/complete groups | FileImport, ApiPolling |
| `IOperationItemRetryHandler` | Per-item retry eligibility + child plan | FileImport, ApiPolling |
| `OperationGroupAggregator` | **New** — count invariant + derived status | FileImport, ApiPolling |
| `OperationItemReporting` | **New** — safe metadata/append builder | FileImport, ApiPolling |
| `OperationFailureCategory` | **New** — retry/diagnostic classification | Contract tests, future handlers |
| `ProcedureExecutionOutcome` | **New** — unified terminal outcome | Complete steps (optional) |

### Platform settings contributor (`Apperture.Core.Application.PlatformSettings`)

| Type | Responsibility | Consumers |
|------|----------------|-----------|
| `IModuleIntegrationSettingDefinitionContributor` | Declare integration settings | HelloWorld, FileImport, ApiPolling |
| `PlatformSettingDefinition` | Setting metadata record | Setting contributors |

### Remains platform-only (Core.Application)

- `IOperationItemStore`, `SqlOperationItemStore`, reconciliation services
- `OperationItemizationValidation`, `ContextBoundOperationItemWriter`
- Notification evaluation, schedule dispatch, platform settings resolution

## Duplication resolved

| Before | After |
|--------|-------|
| Per-module JSON serialization in itemization mappers | `OperationItemReporting.SerializeMetadata` |
| Manual count arithmetic in Complete handlers | `OperationGroupAggregator` (available; samples may still use domain stores) |
| Implicit retry eligibility | `OperationItemReporting.IsRetryableFailure` + `OperationFailureCategory` |
| Modules reference full `Core.Application` | Samples reference `ModuleSdk.Integration` only |

## Internal leaks found (pre-6A)

- Sample modules referenced `Apperture.Core.Application` (platform mixed in)
- `Proof.CampusNexus.StudentReview` uses EF in step handler (out of SDK-6A scope; arch test targets samples)
- `Sample.ApiPolling.Persistence` is acceptable optional `.Persistence` project pattern

## Gaps deferred (SDK-6B+)

- Move remaining production modules (Proof, Unisa) to Integration-only references
- Deprecate `IStepExecutionContext.PlatformEvents` alias
- Shared `IOperationItemMapper<TDomainOutcome>` generic helper
- Package publish / external consumer compatibility policy
- Capability manifest enforcement
