# Notification rule JSON schema

**Status:** Proposed (ADR-0037)  
**Schema id:** `apperture.notification.rule/v1`

Rules determine **when** a notification definition fires. Evaluation is **staged**: cheap indexed filters first, then property matching, then optional expressions (platform definitions only in v1).

## Top-level document

```json
{
  "schemaVersion": "apperture.notification.rule/v1",
  "trigger": { },
  "stages": [ ],
  "throttle": { },
  "requiresIncident": false,
  "priority": "normal"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `schemaVersion` | yes | Must be `apperture.notification.rule/v1` |
| `trigger` | yes | What starts evaluation |
| `stages` | yes | Ordered filters (min 1: `index`) |
| `throttle` | no | Per-subject send spacing |
| `requiresIncident` | no | If true, only fire when incident id present |
| `priority` | no | `normal` (default) or `mandatory` (ignores user opt-out) |

## Trigger

At least one of `eventTypes`, `incidentTransitions`:

```json
{
  "trigger": {
    "eventTypes": [
      "apperture.schedule.expected_run.missing"
    ],
    "incidentTransitions": ["opened", "observedStillActive", "resolved"],
    "notificationKinds": ["activation", "reminder", "resolved"]
  }
}
```

| Field | Description |
|-------|-------------|
| `eventTypes` | Platform event `type` values |
| `incidentTransitions` | Maps from `AttentionIncidentObserveTransition` + resolved |
| `notificationKinds` | Sub-select for incident-driven templates (Epic 69 parity) |

## Stages

### Stage 1: `index` (required)

Evaluated against **columns** on `PlatformEvents` and subscription scope. No JSON parsing.

```json
{
  "stage": "index",
  "filters": {
    "severity": ["warning", "error", "critical"],
    "integrationId": { "present": true },
    "moduleId": { "equals": "proof.campusnexus.studentreview" },
    "type": { "in": ["apperture.schedule.expected_run.missing"] },
    "time": { "withinLookback": "7d" }
  }
}
```

**Filter operators:**

| Operator | Applies to | Example |
|----------|------------|---------|
| `in` | string | type list |
| `equals` | string | exact moduleId |
| `present` | id fields | integrationId required |
| `withinLookback` | time | relative window from evaluation time |

**User-created definitions (integration scope):** Index stage MUST include `"integrationId": { "equals": "<owner integration id>" }` — enforced at save time.

### Stage 2: `property`

JSON path conditions on `data` after allowlist validation.

```json
{
  "stage": "property",
  "match": {
    "data.evaluationStatus": { "equals": "MissingExpectedRun" },
    "data.missingMinutes": { "gte": 30 }
  }
}
```

| Operator | Types |
|----------|-------|
| `equals` | string, number, boolean |
| `gte`, `lte` | number |
| `in` | string array |

Paths must appear in the signal catalog for the triggered `event.type`. Unknown paths → validation error at definition save.

### Stage 3: `expression` (optional, v1 platform-only)

```json
{
  "stage": "expression",
  "when": "event.data.missingMinutes >= 30 && event.severity != 'info'"
}
```

**Constraints:**

- No method calls, no object construction
- Identifiers must be allowlisted variables
- Parsed by restricted AST evaluator

User-created definitions: **expression stage disallowed** in v1.

## Throttle

```json
{
  "throttle": {
    "perSubjectMinInterval": "1h",
    "perRecipientMinInterval": "15m"
  }
}
```

| Field | Description |
|-------|-------------|
| `perSubjectMinInterval` | Uses `event.subject` or `dedupekey` |
| `perRecipientMinInterval` | Per email address |

## Built-in vs user rules

| Definition kind | Rule editing | Index enforcement |
|-----------------|--------------|-------------------|
| Module built-in | Static in manifest | Platform injects `moduleId` |
| Platform built-in | Static in seed data | — |
| User integration | Full editor (index + property) | Must pin `integrationId` |

## Evaluation algorithm (pseudocode)

```
function Evaluate(event, incident, definition):
  if not MatchesTrigger(event, incident, definition.trigger):
    return NoMatch
  for stage in definition.stages:
    if stage.kind == "index" and not EvalIndex(event, stage.filters):
      return NoMatch
    if stage.kind == "property" and not EvalProperty(event.data, stage.match):
      return NoMatch
    if stage.kind == "expression" and not EvalExpression(stage.when, context):
      return NoMatch
  if definition.requiresIncident and incident is null:
    return NoMatch
  if Throttled(definition, event, incident):
    return Suppressed
  return Match
```

## Epic 69 parity rule (reference)

Platform definition `platform.schedule.expected_run_missing`:

```json
{
  "schemaVersion": "apperture.notification.rule/v1",
  "trigger": {
    "eventTypes": ["apperture.schedule.expected_run.missing", "apperture.schedule.expected_run.cleared"],
    "incidentTransitions": ["opened", "observedStillActive", "resolved"],
    "notificationKinds": ["activation", "reminder", "resolved"]
  },
  "stages": [
    {
      "stage": "index",
      "filters": {
        "type": {
          "in": [
            "apperture.schedule.expected_run.missing",
            "apperture.schedule.expected_run.cleared"
          ]
        },
        "integrationId": { "present": true }
      }
    }
  ],
  "throttle": {
    "perSubjectMinInterval": "0m"
  },
  "requiresIncident": true,
  "priority": "normal"
}
```

Reminder interval and acknowledgment suppression move to **subscription policy** (not rule JSON) to match Epic 70 routing behavior.
