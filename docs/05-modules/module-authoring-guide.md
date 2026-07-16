# Module Authoring Guide

This guide is for developers building **Apperture modules**: workflow-centric packages that contribute definitions, step handlers, and manifest metadata to a single-tenant instance. It documents **shipped** SDK behavior only. For copyable handler snippets, see [execution-contract-handler-patterns.md](./execution-contract-handler-patterns.md).

## Related documentation

| Document | Purpose |
|----------|---------|
| [.ai/playbooks/module-sdk.md](../../.ai/playbooks/module-sdk.md) | Short project playbook index |
| [module-manifest-spec.md](./module-manifest-spec.md) | Manifest schema (permissions, signals, procedures, governance) |
| [module-types.md](./module-types.md) | Module type taxonomy — placeholder |
| [ADR-0038](../02-decisions/ADR-0038-platform-signals-sdk-conventions.md) | Platform signals taxonomy and conventions |
| [Epic 103A plan](../02-decisions/Epic103A-Platform-Signals-SDK-Stabilization-Implementation-Plan.md) | Signals SDK implementation status |
| [Epic 104](../02-decisions/Epic104-Module-Authoring-Documentation.md) | Guide rewrite (104A) and doc consistency closeout (104B) |
| [platform-signal-catalog.md](../01-architecture/platform-signal-catalog.md) | Event types, implementation status, template allowlists |
| [runtime-connection-resolution.md](../01-architecture/runtime-connection-resolution.md) | Slots, bindings, connection resolution |
| [epic77-notification-system-backlog.md](../03-operations/epic77-notification-system-backlog.md) | Notification system limitations |
| [ADR-0022 / ADR-0023](../02-decisions/) | Declared execution result contracts |

**Reference modules in this repo:**

- `Sample.HelloWorld` — minimal workflow, operation, procedures, manifest signals, harness-friendly emit/log/telemetry demo
- `Sample.FileImport` — portable CSV import demo (SDK-2/3C): embedded fixtures, process-local inspection store + durable platform operation items, partial-failure signals, settings contributor. See [SDK-2 design](../superpowers/specs/SDK-2-Sample-FileImport-Demo.md).
- `Proof.CampusNexus.StudentReview` — integration-scoped slots (Campus Nexus + SQL), procedures, operational logs (no manifest signals)

### Sample.FileImport (SDK-2 + SDK-3C)

**Purpose:** Contract-validation sample for file-ingest modules. Proves fixture selection, multi-step import workflow, module-local per-row outcomes, fail-loud summary signals, and durable platform operation itemization (SDK-3C) without external files.

**How to run:** Create a `Sample.FileImport` integration (or use an existing one). Launch procedure `Sample.FileImport.procedures.import` with `fixtureId`. Optional `strictMode` overrides `strictModeDefault`. Platform schedules can target workflow `Sample.FileImport.Import`.

**Fixture ids:**

| Id | Expected path |
|----|---------------|
| `customers-valid` | All rows succeed → `import.completed` + succeeded operation items |
| `customers-mixed` | Partial success → `import.completed.with.errors` + mixed items |
| `customers-invalid-structure` | Parse hard-fail before row processing; no groups/items; signal may not emit |
| `customers-all-bad` | Valid header, all rows fail → outcomes + failed group + `import.failed` |

**Settings (integration-scoped):** `strictModeDefault`, `maxRowsPerImport`, `failOnFirstError` (no secrets).

**Signals:** `import.completed`, `import.completed.with.errors`, `import.failed` — exactly one per completed batch. Payload includes fixture/batch/`operationGroupId` and counts. (Dotted signal names; underscores are not catalog-valid.)

**Outcome stores:**

* `IImportOutcomeStore` (in-memory) — **process-local** inspection aid; not restart-durable.
* Platform `context.Signals.OperationItems` — durable run-attached `OperationGroup` / `OperationItem` for SDK-3B APIs and SDK-3D UI. `operationKind=file_import`; failed rows set `isRetryable` (execution is SDK-4).

**Notification seed:** Built-in definition for `import.completed.with.errors` (HelloWorld-sized). Subscribe to validate partial-failure → notification path.

## Operation itemization (SDK-3A)

**Purpose:** Platform-durable **operation groups** and **operation items** for business-facing outcome history (rows, records, files)—not merely run-log lines. Workflow **step instances** remain supporting platform diagnostics.

**Hybrid model:** Platform owns SQL tables `OperationGroups` / `OperationItems`. Modules write through `IOperationItemWriter` on `context.Signals.OperationItems`.

**Writer lifecycle (append-oriented):**

1. `BeginGroupAsync(operationKind, sourceLabel?)` — requires platform `RunId` on the step context  
2. `AppendItemAsync(...)` — subject key, display label, status, bounded change summary / metadata  
3. `CompleteGroupAsync(...)` — terminal group status + counts (`Succeeded` / `CompletedWithErrors` / `Failed`)

**Safety rules:** No secrets; no raw files; no silent truncation; JSON fields bounded (see `OperationItemizationBounds`). Missing `RunId` or unregistered store fails loudly.

**Retry fields:** `IsRetryable`, `RetryCount`, `LastAttemptedAtUtc` persist for future use. **Per-item retry execution/UI is SDK-4**, not SDK-3A.

**Relation to Sample.FileImport (SDK-2/3C):** Process-local `IImportOutcomeStore` remains a sample inspection aid. **SDK-3C** FileImport writes durable platform operation items via `context.Signals.OperationItems`. **SDK-3D** Run Detail panel displays those items; **SDK-4** adds per-item retry.

**Persistence:** Host registers `AddOperationItemizationPersistence()` (Worker Host + Management Host). Migration `20260715180000_Sdk3A_OperationItemization` creates `OperationGroups` / `OperationItems`. Unit coverage uses `InMemoryOperationItemStore`; SQL round-trip is `OperationItemizationSqlServerTests` (soft-skips without `APPERTURE_TEST_SQL_*`).

**Read APIs (SDK-3B):** Management exposes:

* `GET /api/runs/{runId}/operation-groups` — group summaries for the run  
* `GET /api/runs/{runId}/operation-groups/{groupId}/items?status=&retryableOnly=&q=&skip=&take=` — paged items  

Same run-read authorization as run detail/timeline. Missing run → 404; no groups/items → empty 200. Client: `GetRunOperationGroupsAsync` / `GetRunOperationGroupItemsAsync`. Run detail includes optional `operationItemization` summary (`groupCount`, `failedItemCount`, `skippedItemCount`, `hasOperationItems`) — **not** full item lists.

**Deferred:** Per-item retry actions/UI (**SDK-4**). Retry fields (`isRetryable`, `retryCount`, `lastAttemptedAtUtc`) are readable and shown as read-only indicators on Run Detail.

**Run Detail panel (SDK-3D):** Management UI hosts `RunOperationItemizationPanel` on `/runs/{runId}` below the run summary band and above forensics. Operators see group kind/source/status/counts, expand to paged items (default page size 50), status filters (All / Failed / Succeeded / Skipped), failed-first ordering when viewing All, and metadata behind disclosure. Empty state when no groups. Section-level errors do not break the rest of Run Detail. **No retry buttons yet.**

**Audit:** [sdk3-itemized-operation-model-design-audit.md](../audits/sdk3-itemized-operation-model-design-audit.md)  
**Plans:** [SDK-3B](../superpowers/plans/2026-07-15-sdk3b-operation-itemization-read-apis.md) · [SDK-3C](../superpowers/plans/2026-07-15-sdk3c-fileimport-operation-itemization.md) · [SDK-3D](../superpowers/plans/2026-07-15-sdk3d-run-detail-operation-itemization-panel.md)

---

## 1. What a module is

An Apperture **module** packages **workflow definitions** and **step implementations** for one product area. The runtime executes steps in order according to the workflow graph; modules do not schedule work directly or use a message bus.

| Concept | Module role |
|---------|-------------|
| Workflows | Declare step names and category; handlers implement each step |
| Operations | Optional single-step executables mapped to one handler |
| Procedures | Optional integration-scoped launch wrappers with parameter forms |
| Manifest | Permissions, signals, governance, connector documentation |

### Deployment assumption

**One module per compute process** is the intended deployment model (on-prem worker or cloud Function App). A dev worker may register **multiple** `IModule` instances for testing; production should still run one primary module per process.

### What modules can contribute

- Workflow definitions (`IWorkflowDefinitionProvider` / `IModule.GetDefinitionsAsync`)
- Step handlers (`IModuleStepHandlerRegistrar`)
- Operations (`IOperationDefinitionProvider`)
- Manifest: permissions, **signals**, procedures, governed resources/actions, connector definition documentation
- Optional menu links (`IModuleNavigationProvider`) merged into Control Panel menus when the module is enabled

### What modules should not own

- Sending email or invoking notification orchestrators
- Writing operational outcomes to `IAuditService` (audit is for governance actions)
- Storing or transporting connection secrets in workflow parameters or event payloads
- Emitting **platform** run lifecycle types (`apperture.execution.run.completed`, `apperture.execution.run.failed`) — the platform owns those
- Custom Management UI panels (`.Ui` projects are not loaded by the shell today)
- Message-bus-style orchestration or implicit handlers

---

## 2. Module anatomy

### Project layout

| Project | Required | Role |
|---------|----------|------|
| `{Name}.Module` | Yes | `IModule`, handlers, manifest |
| `{Name}.Contracts` | Optional | Shared DTOs/constants |
| `{Name}.Ui` | Optional | Placeholder only today — not composed into Management UI |

### Module class

Implement `IModule` at minimum:

```csharp
public sealed class MyModule : IModule, IModuleStepHandlerRegistrar
{
    public ModuleManifest GetManifest() => /* ... */;

    public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        yield return /* WorkflowDefinition */;
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public void RegisterStepHandlers(IStepHandlerRegistry registry) { /* ... */ }
}
```

Optional interfaces:

| Interface | Purpose |
|-----------|---------|
| `IOperationDefinitionProvider` | Direct operations (`GetOperationsAsync`) |
| `IModuleStepHandlerRegistrar` | Register `(moduleId, stepName)` → `IStepHandler` |
| `IModuleNavigationProvider` | Add Control Panel menu items (href + domain label) |

Optional base: `ModuleBase` in `Apperture.ModuleSdk` provides default `BuildDescriptorAsync`.

### Manifest

`GetManifest()` returns `ModuleManifest` (`Apperture.ModuleSdk.Abstractions`). See [section 3](#3-manifest-authoring).

### Operations, procedures, and steps

| Concept | Type | How it runs |
|---------|------|-------------|
| **Step** | `IStepHandler` | Registered per `(moduleId, stepName)`; invoked by workflow executor |
| **Workflow** | `WorkflowDefinition` | Ordered `StepNames`; durable multi-step orchestration |
| **Operation** | `OperationDefinition` | Single step: `OperationDefinition.Name` = handler step name |
| **Procedure** | `ModuleProcedureDeclaration` | Integration-scoped launch of a workflow or operation via Management API/UI |

### Signal declarations

`ModuleManifest.Signals` lists `ModuleSignalDeclaration` entries. At worker/Management startup, `ModuleSignalManifestValidator` builds `IModuleSignalRegistry`. Handlers emit using the **short signal name**; the platform resolves the full event type.

---

## 3. Manifest authoring

### Required fields

| Field | Rule |
|-------|------|
| `Id` | Unique module identifier (e.g. `Sample.HelloWorld`) |
| `Name` | Display name |
| `Version` | Semantic version string |
| `Description` | Human-readable summary |

### Optional contributions (code)

Full field and validation reference: [module-manifest-spec.md](./module-manifest-spec.md).

`ModuleManifest` also supports:

- `Permissions` — `ModulePermissionDeclaration` (action + optional resource type)
- `Signals` — platform event declarations (declare before emit)
- `Procedures` — integration-scoped operator launch surface
- `GovernedResources` / `GovernedActions` — extended governance catalog
- `ConnectorDefinitions` — documentation/tooling only (runtime registration is host-driven)

> **Future:** JSON configuration schema on the manifest for UI-driven module config — see [module-manifest-spec.md](./module-manifest-spec.md). Signal, procedure, and governance fields are specified there.

### Example (HelloWorld)

```csharp
public ModuleManifest GetManifest() => new(ModuleId, "Hello World", "1.0.0", "Validate → DoWork → Complete")
{
    Permissions =
    [
        new ModulePermissionDeclaration("workflow.execute", "WorkflowInstance"),
        new ModulePermissionDeclaration(ActionConstants.OperationExecute, ResourceTypeConstants.OperationDefinition)
    ],
    Signals =
    [
        new ModuleSignalDeclaration(
            "demo.completed",
            "Demo completed",
            "Emitted when Complete runs with emitDemoPlatformEvent=true.",
            "info",
            RequiredDataProperties: ["message"])
    ],
    Procedures =
    [
        new ModuleProcedureDeclaration(
            "Sample.HelloWorld.procedures.greet",
            "Greet",
            "Runs HelloWorld with a name in operator output.",
            Category: "Demo",
            ProcedureLaunchKind.Workflow,
            DefinitionId.Value,
            """{"fields":[{"key":"name","type":"string","required":true,"label":"Name"}]}""")
    ]
};
```

### Signal validation rules

Enforced at startup by `ModuleSignalManifestValidator`:

| Rule | Detail |
|------|--------|
| Signal `Name` | Pattern `[a-z0-9]+(\.[a-z0-9]+)*` (e.g. `demo.completed`, `sync.failed`) |
| `DisplayName` / `Description` | Non-empty |
| `DefaultSeverity` | One of `critical`, `error`, `warning`, `info` |
| `RequiredDataProperties` | camelCase property names (`message`, `failureCode`); no duplicates |
| Full event type | `apperture.module.{normalizedModuleId}.{signalName}` must be unique across all modules in the host |

Full type format: `ModulePlatformEventTypes.Format(manifest.Id, signalName)` (module id segments lowercased in the type string).

### ParameterSchema (operations and procedures)

JSON object with a `fields` array. Supported per-field keys for platform forms and server validation:

| Key | Notes |
|-----|-------|
| `key` | Required |
| `type` | `string`, `int`, `decimal`, `bool`, `datetime`, `enum`, `json` |
| `label`, `helpText`, `required`, `defaultValue`, `order` | Optional |
| `allowedValues` | For enum-like constraints |
| `min`, `max`, `maxLength` | Numeric/string bounds |

`WorkflowDefinition` does **not** carry `ParameterSchema`. Use a **procedure** wrapper or raw JSON on the Execute page for workflow parameters.

---

## 4. Execution model

Orchestration is **workflow-centric**: the runtime invokes registered step handlers in definition order. Modules do not enqueue arbitrary steps or bypass the executor.

### Step handler contract

```csharp
public interface IStepHandler
{
    ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default);
}
```

`IStepExecutionContext` provides:

- `Signals` — events, logs, telemetry, correlation ([section 6](#6-platform-signals))
- `PlatformEvents` — compatibility alias for `Signals.Events`
- `ResolveSlotAsync<T>(slotName)` — slot-bound dependencies ([section 5](#5-connections-bindings-and-slots))
- Correlation fields: `ModuleId`, `IntegrationId`, `RunId`, `ProcedureId`, `ScheduleId`, `CorrelationId`, `CausationId`

Pass `CancellationToken` through to I/O; there is no separate module cancellation API.

### Operator-visible outcomes (`OperatorExecutionResult`)

Use one explicit model for run detail semantics (ADR-0022/0023). Preferred patterns:

| Goal | Preferred authoring |
|------|---------------------|
| Inline success payload | `return OperatorExecutionResult.SuccessInline(payload);` |
| Summary-only success | `return OperatorExecutionResult.SuccessSummaryOnly(payload);` |
| Withhold success payload | `return OperatorExecutionResult.SuccessWithheld();` |
| Fail with summary only | `throw OperatorExecutionResult.FailSummaryOnly(summary);` |
| Fail with inline-safe diagnostics | `throw OperatorExecutionResult.FailInline(summary, diagnostics);` |
| Fail, withhold diagnostics | `throw OperatorExecutionResult.FailWithheld(summary);` |

**Supported fallbacks:** `OperatorRunOutput`, plain `object?` returns, ordinary exceptions (captured by default failure behavior). **Discouraged** for new handlers when declared intent is feasible.

Copyable snippets: [execution-contract-handler-patterns.md](./execution-contract-handler-patterns.md).

### Workflow failure vs platform events

| Mechanism | Use when |
|-----------|----------|
| `OperatorExecutionResult.Fail*` / exceptions | Step/workflow should fail; operator sees run failure |
| `Signals.Events` | A **durable fact** should exist for timelines/notifications (declared in manifest) |

Do not rely on `ILogger.LogError` or operational logs to represent notification-worthy failures.

### Handler registration (current)

The worker host calls `IModuleStepHandlerRegistrar` for each DI-registered module:

```csharp
public void RegisterStepHandlers(IStepHandlerRegistry registry)
{
    registry.Register(ModuleId, "Validate", new HelloWorldValidateStepHandler());
    registry.Register(ModuleId, "DoWork", new HelloWorldDoWorkStepHandler());
    registry.Register(ModuleId, "Complete", new HelloWorldCompleteStepHandler());
}
```

Handlers are typically constructed with `new`. You may pass dependencies via handler constructors if **your module** constructs the instance (e.g. Proof passes `ILogger` into `SyncRecentStudentsStepHandler`). The host does not resolve handlers from DI today.

> **Planned:** DI-backed handler factory (Epic 103A — document only; handlers must still use context-only signals).

Startup checks: `WorkerHostStartupValidation` validates module list, signal manifests, and that every workflow step has a registered handler.

### Operations (MVP contract)

Implement `IOperationDefinitionProvider` and yield `OperationDefinition` records:

1. **`OperationDefinition.Id`** — stable external identity (API routes, persisted references).
2. **`OperationDefinition.Name`** — single handler step name (must match a registered handler).
3. **`ParameterSchema`** — optional JSON for Execute/Integration forms and API validation.

Operation execution is **one step only**; use workflows for multi-step durable work.

### Procedures

`ModuleProcedureDeclaration` ties an integration-scoped operator action to a workflow or operation:

| Field | Role |
|-------|------|
| `Id` | Stable procedure id |
| `LaunchKind` | `Workflow` or `Operation` |
| `TargetDefinitionId` | Workflow or operation definition id |
| `ParameterSchema` | Form metadata for Integration Detail and `/execute` |

Operators launch procedures from Integration Detail or [Execute](../01-architecture/control-panel-ui.md) (`/execute`); the platform validates integration readiness and permissions before dispatch.

---

## 5. Connections, bindings, and slots

Modules never receive connection ids. Access external systems only through **declared slots** resolved at runtime.

### Resolution flow

1. Operator binds an **active connection** to a **slot** for `(moduleId, slotName)` on an integration.
2. At step execution, `context.ResolveSlotAsync<T>(slotName)` looks up the binding and returns a live client (e.g. `IDbConnection`, `ICampusNexusApiClient`).
3. No binding or inactive connection → `null` (handler decides policy).

Resolution is **integration-scoped** when the run carries an integration id. See [runtime-connection-resolution.md](../01-architecture/runtime-connection-resolution.md).

### Safe patterns

**Optional dependency (return early):**

```csharp
var conn = await context.ResolveSlotAsync<IDbConnection>("Db", cancellationToken);
if (conn is null)
    return null;
using (conn) { /* ... */ }
```

**Required dependency (throw if missing):**

```csharp
await context.ExecuteWithSlotAsync<IDbConnection>("Db", async (db, ct) =>
{
    // use db
    return OperatorExecutionResult.SuccessSummaryOnly(new { message = "Done." });
}, cancellationToken);
// Throws SlotNotBoundException when null
```

### Examples in repo

| Module | Slots | Client types |
|--------|-------|--------------|
| HelloWorld | `Db` | `IDbConnection` |
| Proof.CampusNexus.StudentReview | `CnsApi`, `IntegrationDb` | `ICampusNexusApiClient`, `IDbConnection` |

Proof registers slot declarations via `ProofCampusNexusStudentReviewSlotBootstrapHostedService` (`CapabilitySlotDeclaration`). Bindings are configured per integration in Management UI.

**Do not** call APIs that accept raw `ConnectionId` from module code.

---

## 6. Platform signals

Canonical conventions: [ADR-0038](../02-decisions/ADR-0038-platform-signals-sdk-conventions.md). Catalog and emitter status: [platform-signal-catalog.md](../01-architecture/platform-signal-catalog.md).

Step handlers use **`context.Signals` only**. Do not resolve `IModulePlatformEventEmitter`, `IModuleOperationalLogger`, or `IModuleTelemetry` from DI inside handlers.

### Surfaces on `context.Signals`

| Member | Purpose | v1 behavior |
|--------|---------|-------------|
| **`Events`** | Durable facts in `PlatformEvents` | Fail-fast when platform events enabled and validation/append fails |
| **`Logs`** | Correlated operator diagnostics | Best-effort via structured `ILogger`; **not** notification triggers |
| **`Telemetry`** | Optional metrics | No-op in default hosts unless host wires an adapter |
| **`Correlation`** | Emit context (`IntegrationId`, `RunId`, …) | Same fields as context; pass as first argument to `EmitAsync` |

```csharp
await context.Signals.Events.EmitAsync(
    context.Signals.Correlation,
    "demo.completed",
    new Dictionary<string, object?> { ["message"] = "Done." },
    cancellationToken: cancellationToken);

context.Signals.Logs.LogOperational(
    OperationalLogLevel.Information,
    "myModule.step.completed",
    "Step finished.",
    new Dictionary<string, object?> { ["rows"] = count });

context.Signals.Telemetry.RecordCounter("myModule.rows.processed", count);
```

### `context.PlatformEvents` (compatibility)

Aliases **`context.Signals.Events`**. Prefer **`Signals.Events`** in new code. Existing tests may assert either path (`HelloWorldSignalsAdoptionTests`).

### Manifest: declare before emit

Every signal name used in code must appear on **`ModuleManifest.Signals`**. Undeclared emits throw at validation time. Emit with the **short name** only; the platform sets full `type`, `source`, `severity`, `subject`, and correlation extensions.

### When to use which surface

| Need | Use |
|------|-----|
| Fact operators or notifications should react to | `Signals.Events` |
| Troubleshooting detail for a run/integration/step | `Signals.Logs` |
| Optional measurement (loss acceptable) | `Signals.Telemetry` |

### Emit semantics (module-authored)

| Condition | Behavior |
|-----------|----------|
| `PlatformEvents:Enabled=false` | Skip emit; skipped result; step continues |
| Emitter not registered | Skip; step continues |
| Enabled + validation or SQL failure | **Fail-fast** — step fails |

Payload rules: camelCase keys; required properties per manifest; max serialized size ~16 KB; no secrets, tokens, or raw PII blobs. See ADR-0038 §6.

### Platform-owned lifecycle events

The platform emits `apperture.execution.run.completed` and `apperture.execution.run.failed` via `ExecutionLifecyclePlatformEventEmitter` **after** terminal run state is committed. Emission is **best-effort** (does not change run status).

| Setting | Default | Notes |
|---------|---------|-------|
| `PlatformEvents:Enabled` | (host config) | Master switch |
| `PlatformEvents:ExecutionLifecycle:Enabled` | **false** | Lifecycle types off unless enabled |

**Modules must not emit** `apperture.execution.run.*` or other platform lifecycle types.

### Notifications (v1)

**Only durable platform events** drive notification rule evaluation in the current release. Log-source rules, telemetry aggregate rules, and calendar-specific signal APIs are **not available**.

> **Future:** Built-in notification definitions shipped in module manifest (ADR-0037 §7) — not implemented in `Apperture.Modules` today. See [section 8](#8-notifications-integration).

---

## 7. Testing modules

Package: **`Apperture.ModuleSdk.Testing`** (no SQL; fake signal recorders).

### Quickstart

```csharp
var harness = ModuleTestHarness.ForModule(new HelloWorldModule())
    .WithIntegrationId("int-1")
    .WithRunId("run-1")
    .WithSlotResolver(slot =>
        string.Equals(slot, "Db", StringComparison.Ordinal) ? new HarnessStubDbConnection() : null)
    .Build();

var result = await harness.ExecuteStepAsync(
    new HelloWorldCompleteStepHandler(),
    "Complete",
    new Dictionary<string, object?> { ["emitDemoPlatformEvent"] = true });

harness.PlatformEvents.ShouldContain("demo.completed");
var ev = harness.PlatformEvents.GetEvent("demo.completed");
ev.EventType.Should().Be("apperture.module.sample.helloworld.demo.completed");

var context = harness.CreateStepContext("Complete");
await new HelloWorldCompleteStepHandler().ExecuteAsync(context, parameters);
var logs = ModuleTestHarness.GetOperationalLogs(context);
logs.OperationalLogs.Should().Contain(l => l.Code == "helloworld.complete.started");
harness.Telemetry.Counters.Should().Contain(c => c.Name == "helloworld.complete");
```

### Builder options

`WithIntegrationId`, `WithRunId`, `WithProcedureId`, `WithScheduleId`, `WithCorrelationId`, `WithCausationId`, `WithPlatformEventsEnabled(bool)`, `WithSlotResolver(Func<string, object?>)`.

### Assertions

| API | Asserts |
|-----|---------|
| `harness.PlatformEvents.ShouldContain("signalName")` | Declared signal was emitted |
| `harness.PlatformEvents.GetEvent("signalName")` | Type, correlation, `DataJson` |
| `ModuleTestHarness.GetOperationalLogs(context)` | Operational log codes and levels |
| `harness.Telemetry.Counters` / durations | Recorded metrics |
| `harness.AssertManifestValid()` | Manifest signal validation |

### Other tests

- **Undeclared emit** → `PlatformEventValidationException` (`ModuleTestHarnessTests`)
- **Handler return values** — standard unit tests on `OperatorExecutionResult` / thrown failures
- **Reference tests:** `ModuleTestHarnessTests`, `HelloWorldSignalsAdoptionTests`

---

## 8. Notifications integration

### How module events enter the catalog

At Management startup, known modules contribute declared signals to **`CompositePlatformEventCatalog`**. The merged catalog powers:

- Notification definition authoring (valid `event.type` values)
- Template variable allowlists per type ([platform-signal-catalog.md](../01-architecture/platform-signal-catalog.md))

Example full type for HelloWorld `demo.completed`:

`apperture.module.sample.helloworld.demo.completed`

### What operators do

1. Ensure the worker emits events (`PlatformEvents:Enabled=true` and module signal declared).
2. Create or edit a **notification definition** scoped to an integration (user-created) or use platform built-ins where seeded.
3. Set rule `eventTypes` to the **full** type string from the catalog.
4. Use only **allowlisted** template variables for that type in templates.

### Safe payload design for notifications

- Include stable business fields (`failureCode`, `recordsProcessed`, opaque ids).
- Use short excerpts for human-readable text (`failureMessageExcerpt` pattern in catalog types).
- Do not put secrets, connection strings, or full stack traces in `data`.
- Declare `RequiredDataProperties` so validation fails fast at emit time.

### Built-in module notification definitions

Sample.HelloWorld ships a built-in notification definition (`module.sample.helloworld.demo.completed`) seeded by migration Epic 121. It fires when the Complete step emits `demo.completed` (default on; disable per-run with `emitDemoPlatformEvent=false`).

Operators enable email delivery via **My notifications** (S9) for “HelloWorld demo completed”. Management Host polls worker-emitted module platform events for notification evaluation (S13A).

See [S13A HelloWorld notification debugging](../02-decisions/S13A-helloworld-notification-pipeline-debugging.md).

### Out of scope for module authors

- Log-source or telemetry-based notification rules
- Calendar- or schedule-specific module signal SDK
- Digest scheduler, non-email channels — platform notification backlog (operator email preferences: `/me/notifications`, S9)

See [epic77-notification-system-backlog.md](../03-operations/epic77-notification-system-backlog.md).

---

## 9. Operational and security guidance

| Topic | Guidance |
|-------|----------|
| **Secrets** | Use `SecretRef` on connections; never in workflow parameters, event payloads, or log `data` |
| **PII** | Prefer opaque ids; redact excerpts in notification-bound fields |
| **Audit** | Governance actions only (`IAuditService`); not operational step diagnostics |
| **Logs** | Correlated diagnostics; not durable compliance storage in v1 |
| **Events** | Durable facts; fail-fast when enabled and emit is intentional |
| **Lifecycle observer** | Platform run events are best-effort; module emits remain fail-fast when enabled |
| **Idempotency** | Pass idempotency keys on workflow/operation/procedure starts; platform may set `dedupekey` / `subject` on events |
| **Errors in payloads** | Stable `failureCode` + short excerpt; not full exception strings |

---

## 10. Packaging, registration, and deployment

### On-prem worker (shipped)

1. Add module project reference to the worker host.
2. Register module in DI, e.g. `builder.Services.AddSingleton<HelloWorldModule>();`
3. Expose all modules: `builder.Services.AddSingleton<IEnumerable<IModule>>(sp => new IModule[] { ... });`
4. Call `AddRuntimePlatformEvents(configuration)` before hosted services.
5. `IStepHandlerRegistry` is populated from `IModuleStepHandlerRegistrar` implementations.

See `Apperture.RuntimeHost.Windows.WorkerHost/Program.cs` for the explicit known-module list pattern.

### Management host

Modules are discovered for lifecycle, menus, procedure registry, and catalog merge via `IModuleRegistryService` (installed/enabled state). Module admin UI lives under Config → **Modules** (`/modules`).

### Cloud

Same module code patterns; deploy **one module per Function App** per product guidance.

### Future (not shipped)

- NuGet/module package install without recompiling worker
- Dynamic module load
- Module `.Ui` Razor composition in Management shell
- Manifest JSON file format for non-code tooling (spec TBD)

---

## 11. Common recipes

### Emit a declared platform event

```csharp
await context.Signals.Events.EmitAsync(
    context.Signals.Correlation,
    "demo.completed",
    new Dictionary<string, object?> { ["message"] = "Processing finished." },
    cancellationToken: cancellationToken);
```

### Log a diagnostic

```csharp
context.Signals.Logs.LogOperational(
    OperationalLogLevel.Information,
    "proof.studentReview.sync.completed",
    "Sync completed.",
    new Dictionary<string, object?> { ["retrieved"] = rowCount });
```

### Resolve a slot

```csharp
var api = await context.ResolveSlotAsync<ICampusNexusApiClient>("CnsApi", cancellationToken);
if (api is null)
    throw OperatorExecutionResult.FailInline("CNS slot not bound.", new { code = "missingCnsSlot" });
using (api) { /* ... */ }
```

### Return success or failure

```csharp
return OperatorExecutionResult.SuccessSummaryOnly(new { message = "Done.", step = "Complete" });

throw OperatorExecutionResult.FailInline("Validation failed.", new { reason = "missing_field" });
```

### Test a handler with ModuleTestHarness

```csharp
var harness = ModuleTestHarness.ForModule(new MyModule())
    .WithRunId("run-1")
    .WithSlotResolver(name => /* stubs */)
    .Build();

await harness.ExecuteStepAsync(new MyHandler(), "MyStep", parameters);
harness.PlatformEvents.ShouldContain("my.event");
```

---

## 12. Anti-patterns

- Using `ILogger.LogError` or operational logs as a substitute for notification-worthy **platform events**
- Resolving `IModulePlatformEventEmitter` / `IModuleOperationalLogger` / `IModuleTelemetry` from `IServiceProvider` in step handlers
- Emitting signal names **not declared** in `ModuleManifest.Signals`
- Emitting **`apperture.execution.run.*`** or other platform lifecycle types from module code
- Putting secrets, connection strings, or tokens in event or log `data`
- Calling `IAuditService` for operational workflow failures
- Relying on **telemetry** for alerts in v1
- Treating operational logs as durable audit or compliance records
- Passing **connection ids** into handlers or bypassing slot resolution
- Designing around message handlers or a bus instead of workflow steps

---

## 13. Future extension points

The following are **planned or backlog** — not available for module authors today:

| Area | Notes |
|------|-------|
| Calendar / schedule module signal SDK | Not shipped |
| OpenTelemetry `IModuleTelemetry` adapter | Epic 103A Signals-07 |
| Log elevation to `apperture.module.*.log.*` events | Epic 103A Signals-08 |
| DI handler factory / activator | Epic 103A |
| `TryEmitAsync` / per-signal best-effort emit policy | ADR amendment if needed |
| Manifest configuration schema + UI | Future |
| Module Razor UI in Management | Placeholder projects only |
| Built-in notification definitions in manifest | ADR-0037; no module seeds yet |
| Log-source / telemetry notification rules | Epic 77 backlog |
| Notification digest, journal UI, user preferences | Platform features, not module SDK |

For implementation tracking, see [Epic 103A plan](../02-decisions/Epic103A-Platform-Signals-SDK-Stabilization-Implementation-Plan.md).
