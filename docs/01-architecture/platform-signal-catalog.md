# Platform signal catalog

**Status:** Proposed (ADR-0037)  
**Related:** [ADR-0037](../02-decisions/ADR-0037-platform-signals-events-and-notification-system.md), [Epic 77 plan](../02-decisions/Epic77-Platform-Signals-and-Notification-System-Implementation-Plan.md)

This document defines canonical **platform event types**, indexed fields for rule stage 1, and **template variable allowlists** per type.

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
| **Source** | `/platform/readiness` |
| **Severity** | `info` |
| **Incident** | Resolves `expected_run_missing` |

**`data` schema:** `{ "evaluationStatus": "Satisfied", "resolutionReason": "string" }`

**Template allowlist:** subset of missing event + `event.data.resolutionReason`

---

### `apperture.execution.run.failed`

| Attribute | Value |
|-----------|-------|
| **Source** | `/platform/runtime` |
| **Severity** | `error` |
| **Dedupe** | Optional per `runId` + terminal failure |

**`data` schema:**

```json
{
  "executionKind": "workflow | operation",
  "definitionId": "string",
  "failureCode": "string",
  "failureMessage": "string",
  "failureMessageExcerpt": "string (max 500)"
}
```

**Redaction:** `failureMessage` not allowlisted for templates; use `failureMessageExcerpt` only.

**Template allowlist:** `event.*`, `run.id`, `run.status`, `run.definitionId`, `integration.*`, `module.*`, `instance.baseUrl`

---

### `apperture.execution.run.completed`

| Attribute | Value |
|-----------|-------|
| **Source** | `/platform/runtime` |
| **Severity** | `info` |

**`data` schema:** `{ "executionKind": "workflow | operation", "definitionId": "string", "outcome": "success" }`

---

### `apperture.governance.audit.privileged_action`

| Attribute | Value |
|-----------|-------|
| **Source** | `/platform/governance` |
| **Severity** | `info` |
| **Notifications** | Opt-in only; not default |

**Note:** Mirrors select audit actions for operator alerts. Does not replace `AuditLogs`.

---

## Module-owned types

### Pattern: `apperture.module.{moduleId}.{signalName}`

Modules register signals in the module manifest:

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

**Source:** `/modules/{moduleId}`

**Validation:** Emit-time payload validated against manifest schema; unknown properties stripped before persist.

---

## Log-derived types (elevated)

### Pattern: `apperture.module.{moduleId}.log.{code}`

| Rule | Value |
|------|-------|
| Max stored payload | 16 KB |
| Template message | `event.data.messageExcerpt` only (500 chars) |
| HTML body | Disallowed |
| Default subscription | Disabled |

---

## Mapping from legacy attention tokens

| Fleet token (`IntegrationFleetAttention.Tokens`) | Event type | Notes |
|--------------------------------------------------|------------|-------|
| `expected_run_missing` | `apperture.schedule.expected_run.missing` | Token may exist before incident |
| `operational_failure` | `apperture.execution.run.failed` | Aggregate runs → events per failure |
| `blocking` | (none) | Readiness-only; optional future `apperture.integration.readiness.blocking` |

---

## Attention vs incident vs notification

| Mechanism | Driven by |
|-----------|-----------|
| Fleet token | Computed query |
| Platform event | Append to `PlatformEvents` |
| Incident | Condition state machine |
| Notification instance | Rule match on event and/or incident transition |
