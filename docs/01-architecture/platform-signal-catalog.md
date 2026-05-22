# Platform signal catalog

**Status:** Active (ADR-0038)  
**Related:** [ADR-0037](../02-decisions/ADR-0037-platform-signals-events-and-notification-system.md), [ADR-0038](../02-decisions/ADR-0038-platform-signals-sdk-conventions.md), [Epic 77 plan](../02-decisions/Epic77-Platform-Signals-and-Notification-System-Implementation-Plan.md), [Epic 103A plan](../02-decisions/Epic103A-Platform-Signals-SDK-Stabilization-Implementation-Plan.md)

**Module authors:** [module-authoring-guide.md](../05-modules/module-authoring-guide.md), [module-manifest-spec.md](../05-modules/module-manifest-spec.md) (declare `Signals` before emit).

This document defines canonical **platform event types**, indexed fields for rule stage 1, and **template variable allowlists** per type.

> **Module authors:** Only types with implementation status **shipped** have a live platform or module emitter in production. **planned** types are documented for notification authoring and future observers—do not emit them from module code until status changes. **future** types are design placeholders (rules, elevation, or platform features not scheduled in the SDK stabilization track).

## Implementation status legend

| Status | Meaning |
|--------|---------|
| **shipped** | Emitter exists in Apperture-Core today; events may appear in `PlatformEvents` when enabled |
| **planned** | Catalog + rules may reference the type; emitter not yet implemented (see Epic 103A Signals-04) |
| **future** | Not implemented; no emitter; notification rules not supported in v1 |

## Catalog index

| Type | Owner | Status | Emitter today |
|------|-------|--------|---------------|
| `apperture.schedule.expected_run.missing` | Platform | **shipped** | `ScheduleExpectationPlatformEventDualEmitter` |
| `apperture.schedule.expected_run.cleared` | Platform | **shipped** | `ScheduleExpectationPlatformEventDualEmitter` |
| `apperture.execution.run.failed` | Platform | **shipped** | `ExecutionLifecyclePlatformEventEmitter` (worker; flag `PlatformEvents:ExecutionLifecycle:Enabled`) |
| `apperture.execution.run.completed` | Platform | **shipped** | `ExecutionLifecyclePlatformEventEmitter` (worker; flag `PlatformEvents:ExecutionLifecycle:Enabled`) |
| `apperture.governance.audit.privileged_action` | Platform | **future** | None |
| `apperture.module.{moduleId}.{signalName}` | Module | **shipped** (per manifest) | `ModulePlatformEventEmitter` when signal declared |
| `apperture.module.{moduleId}.log.{code}` | Module (elevated) | **future** | None — Signals-08 optional |
| Log-source notification rules | — | **future** | Not in v1 |
| Telemetry aggregate notification rules | — | **future** | Not in v1 |

---

## Envelope recap

All catalog types use the envelope in ADR-0037 (`specversion` `1.0`, `datacontenttype` `application/json`). Emitters must set `source` and applicable extensions (`integrationId`, `moduleId`, `runId`, `scheduleId`, `severity`, `correlationId`, `subject`, `dedupekey`).

## Severity

| Value | Use |
|-------|-----|
| `critical` | Instance-wide or data-loss risk |
| `error` | Failed execution or blocking condition |
| `warning` | Actionable degradation |
| `info` | Normal lifecycle, resolution, digest |

## Platform-owned types

### `apperture.schedule.expected_run.missing`

| Attribute | Value |
|-----------|-------|
| **Implementation status** | **shipped** |
| **Source** | `/platform/readiness` |
| **Typical severity** | `warning` or `error` |
| **Incident** | Opens/updates `expected_run_missing` |
| **Dedupe** | Recommended: `{integrationId}:expected_run_missing` while active |

**`data` schema (illustrative):**

```json
{
  "evaluationStatus": "MissingExpectedRun",
  "operatorSummaryLine": "string",
  "mostRecentExpectedFireUtc": "2026-05-15T12:00:00Z",
  "referenceScheduleId": "sched_abc",
  "missingMinutes": 45
}
```

**Index stage fields:** `integrationId`, `scheduleId`, `severity`, `type`, `time`

**Template allowlist:**

| Variable path | Type | Description |
|---------------|------|-------------|
| `event.type` | string | Event type |
| `event.time` | datetime | Event time UTC |
| `event.severity` | string | Severity |
| `event.data.evaluationStatus` | string | Evaluator status |
| `event.data.operatorSummaryLine` | string | One-line summary |
| `event.data.mostRecentExpectedFireUtc` | datetime? | Last expected fire |
| `event.data.missingMinutes` | number? | Minutes since expected fire |
| `integration.id` | string | Integration id |
| `integration.displayName` | string | Display name |
| `schedule.id` | string? | Reference schedule |
| `schedule.name` | string? | Schedule name |
| `incident.id` | string? | Incident id if linked |
| `incident.startedAt` | datetime? | Incident start |
| `notification.kind` | string | `activation`, `reminder`, `resolved` |
| `instance.baseUrl` | string | Management UI base |

---

### `apperture.schedule.expected_run.cleared`

| Attribute | Value |
|-----------|-------|
| **Implementation status** | **shipped** |
| **Source** | `/platform/readiness` |
| **Severity** | `info` |
| **Incident** | Resolves `expected_run_missing` |

**`data` schema:** `{ "evaluationStatus": "Satisfied", "resolutionReason": "string" }`

**Template allowlist:** subset of missing event + `event.data.resolutionReason`

---

### `apperture.execution.run.failed`

| Attribute | Value |
|-----------|-------|
| **Implementation status** | **shipped** (Epic 103D) |
| **Source** | `/platform/runtime` |
| **Severity** | `error` |
| **Dedupe** | `{runId}:terminal:failed` |
| **Subject** | `run:{runId}` |
| **Requires** | `runId` on envelope; `integrationId` when run is integration-scoped |

> **Platform observer only** — emitted by `ExecutionLifecyclePlatformEventEmitter` after terminal failure is persisted. Best-effort: emit failure does not change run state. Not for module handlers.

**`data` schema (required properties):**

```json
{
  "executionKind": "workflow | operation",
  "definitionId": "string",
  "status": "Failed",
  "failureCode": "string",
  "failureMessageExcerpt": "string (max 500)",
  "durationMs": 0,
  "failedStepName": "string",
  "exceptionType": "string"
}
```

Optional fields: `durationMs`, `failedStepName`, `exceptionType`. No stack traces or secrets.

**Template allowlist:** `event.*`, `event.data.executionKind`, `event.data.definitionId`, `event.data.failureCode`, `event.data.failureMessageExcerpt`, `run.id`, `run.definitionId`, `integration.*`, `module.*`, `instance.baseUrl`

---

### `apperture.execution.run.completed`

| Attribute | Value |
|-----------|-------|
| **Implementation status** | **shipped** (Epic 103D) |
| **Source** | `/platform/runtime` |
| **Severity** | `info` |
| **Dedupe** | `{runId}:terminal:completed` |
| **Subject** | `run:{runId}` |
| **Requires** | `runId` on envelope |

> **Platform observer only** — emitted after successful terminal completion is persisted. Enable with `PlatformEvents:Enabled` and `PlatformEvents:ExecutionLifecycle:Enabled`.

**`data` schema (required properties):**

```json
{
  "executionKind": "workflow | operation",
  "definitionId": "string",
  "status": "Completed",
  "outcome": "success",
  "durationMs": 0,
  "stepCount": 0
}
```

Optional: `durationMs`, `stepCount`.

**Template allowlist:** `event.*`, `event.data.executionKind`, `event.data.definitionId`, `event.data.outcome`, `run.id`, `run.definitionId`, `integration.*`, `module.*`, `instance.baseUrl`

---

### `apperture.governance.audit.privileged_action`

| Attribute | Value |
|-----------|-------|
| **Implementation status** | **future** |
| **Source** | `/platform/governance` |
| **Severity** | `info` |
| **Notifications** | Opt-in only; not default |

**Note:** Mirrors select audit actions for operator alerts. Does not replace `AuditLogs`. No emitter in v1.

---

## Module-owned types

**Implementation status:** **shipped** when the signal is declared in `ModuleManifest.Signals` and the worker registers `ModulePlatformEventEmitter`.

### Pattern: `apperture.module.{normalizedModuleId}.{signalName}`

Full event `type` strings are produced by `ModulePlatformEventTypes.Format(moduleManifestId, signalName)`:

- Module id segments from the manifest are **lower-cased** for the type token (e.g. manifest id `Sample.HelloWorld`, signal `demo.completed` → `apperture.module.sample.helloworld.demo.completed`).
- **Source** on the envelope is `/modules/{normalizedModuleId}` (lowercase).

The sample `Sample.HelloWorld` module declares `demo.completed`; the **Complete** step emits only when workflow parameters include a truthy `emitDemoPlatformEvent` (otherwise no platform event is written).

Modules register signals in the module manifest (C# `ModuleSignalDeclaration` — see [module-manifest-spec.md](../05-modules/module-manifest-spec.md)):

```json
{
  "signals": [
    {
      "name": "sync.failed",
      "type": "apperture.module.proof.campusnexus.studentreview.sync.failed",
      "severity": "error",
      "dataSchema": { "$ref": "#/schemas/syncFailed" },
      "templateVariables": [
        "event.data.code",
        "event.data.recordsProcessed",
        "run.id",
        "integration.displayName"
      ]
    }
  ]
}
```

**Source:** `/modules/{normalizedModuleId}`

**Validation:** Manifest lists required top-level JSON property names (camelCase); the emitter serializes the module payload and `PlatformEventCatalog.ValidateRequest` checks presence, JSON shape, and max serialized size (~16 KB). Undeclared signals or missing required properties fail validation before append. Unknown JSON properties are not stripped automatically in the current implementation.

**Management / Phase 3:** The Management Host builds the same merged in-memory catalog (`CompositePlatformEventCatalog` over known `IModule` instances) for validating `event.type` and template allowlists when saving notification definitions (no separate SQL table for signal metadata in Phase 2B).

---

## Log-derived types (elevated)

**Implementation status:** **future** (Signals-08 optional)

### Pattern: `apperture.module.{moduleId}.log.{code}`

| Rule | Value |
|------|-------|
| Max stored payload | 16 KB |
| Template message | `event.data.messageExcerpt` only (500 chars) |
| HTML body | Disallowed |
| Default subscription | Disabled |

> Operational logs do **not** create these events in v1. `IModuleOperationalLogger` writes to log sinks only (ADR-0038).

---

## Notification rules: non-event sources

| Capability | Status | Notes |
|------------|--------|-------|
| Event `trigger.eventTypes` | **shipped** | Primary v1 path |
| Incident transitions (schedule missing) | **shipped** | Dual-emit with incidents |
| Log-source rule stage | **future** | Epic 77 backlog P3 |
| Telemetry aggregate rules | **future** | No SQL telemetry table in v1 |

---

## Mapping from legacy attention tokens

| Fleet token (`IntegrationFleetAttention.Tokens`) | Event type | Platform status | Notes |
|--------------------------------------------------|------------|-----------------|-------|
| `expected_run_missing` | `apperture.schedule.expected_run.missing` | shipped | Token may exist before incident |
| `operational_failure` | `apperture.execution.run.failed` | shipped | One event per terminal failure when lifecycle flag enabled |
| `blocking` | (none) | future | Optional `apperture.integration.readiness.blocking` |

---

## Attention vs incident vs notification

| Mechanism | Driven by |
|-----------|-----------|
| Fleet token | Computed query |
| Platform event | Append to `PlatformEvents` |
| Incident | Condition state machine |
| Notification instance | Rule match on event and/or incident transition |
