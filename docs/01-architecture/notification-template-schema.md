# Notification template schema

**Status:** Proposed (ADR-0037)  
**Schema id:** `apperture.notification.template/v1`

Templates define **what** recipients see. They are first-class on `NotificationDefinitions` and validated against the [platform signal catalog](./platform-signal-catalog.md) variable allowlist per `event.type`. Phase 3 validation resolves allowed types via the Management Host merged catalog (`IPlatformEventCatalog`: platform-owned + known module manifests).

## Top-level document

```json
{
  "schemaVersion": "apperture.notification.template/v1",
  "subjectTemplate": "[Apperture] {{integration.displayName}}: {{event.data.operatorSummaryLine}}",
  "summaryTemplate": "Expected run missing for {{integration.displayName}}.",
  "bodyTextTemplate": "Integration: {{integration.displayName}}\nCondition: expected_run_missing\n...",
  "bodyHtmlTemplate": null,
  "actionText": "View schedule diagnostics",
  "actionUrlTemplate": "{{instance.baseUrl}}/integrations/{{integration.id}}/schedule-diagnostics",
  "contentFields": [
    {
      "key": "evaluationStatus",
      "label": "Evaluation status",
      "valueTemplate": "{{event.data.evaluationStatus}}",
      "sensitive": false
    }
  ],
  "variants": {
    "activation": { "subjectTemplate": "..." },
    "reminder": { "subjectTemplate": "..." },
    "resolved": { "subjectTemplate": "..." }
  }
}
```

| Field | Required | Max length (v1) |
|-------|----------|-----------------|
| `subjectTemplate` | yes | 200 rendered |
| `summaryTemplate` | yes | 500 rendered |
| `bodyTextTemplate` | yes | 8000 rendered |
| `bodyHtmlTemplate` | no | 16000 rendered |
| `actionText` | no | 80 |
| `actionUrlTemplate` | no | 2000 |
| `contentFields` | no | 20 fields |
| `variants` | no | Override per `notification.kind` |

## Template syntax

- Delimiters: `{{path.to.variable}}`
- Whitespace inside braces trimmed
- Missing variable at render time → empty string + warning in delivery log (not send failure)
- Invalid variable at **save** time → validation error

**Disallowed:**

- Arbitrary code, filters, or includes (no Scriban `if` in v1 unless later ADR)
- Access to paths not in allowlist
- HTML in `bodyTextTemplate`

Optional v1.1: limited `{{#if event.data.missingMinutes}}` blocks with schema approval.

## NotificationRenderContext

Built by `INotificationRenderContextFactory` from envelope + read-only queries:

```json
{
  "event": {
    "id": "pevt_...",
    "type": "apperture.schedule.expected_run.missing",
    "time": "2026-05-15T12:00:00Z",
    "severity": "warning",
    "data": { }
  },
  "integration": {
    "id": "int_...",
    "displayName": "Campus Nexus Sync"
  },
  "module": {
    "id": "proof.campusnexus.studentreview",
    "displayName": "Student Review"
  },
  "run": null,
  "schedule": {
    "id": "sched_...",
    "name": "Nightly sync"
  },
  "incident": {
    "id": "attn_...",
    "conditionKey": "expected_run_missing",
    "startedAt": "2026-05-15T10:00:00Z",
    "acknowledgedAt": null
  },
  "actor": null,
  "notification": {
    "instanceId": "ninst_...",
    "kind": "activation"
  },
  "instance": {
    "id": "instance",
    "baseUrl": "https://management.example"
  }
}
```

**Rules:**

- No EF entities or internal DTOs exposed
- `event.data` contains only allowlisted keys (subset of stored payload)
- Log-derived events: `event.data.messageExcerpt` only

## Validation pipeline

| Step | When | Action |
|------|------|--------|
| Parse | Save definition | Extract `{{variables}}` |
| Allowlist | Save | Each variable ∈ catalog for declared `trigger.eventTypes` |
| Render dry-run | Preview API | Build context from sample event or last real event |
| Length | Render | Enforce max rendered lengths; truncate with ellipsis |
| HTML sanitize | Render | If `bodyHtmlTemplate` set, sanitize tags (allowlist: `p`, `br`, `a`, `table`, `tr`, `td`, `th`, `strong`, `em`) |
| URL | Render | `actionUrlTemplate` must resolve to relative path or `instance.baseUrl` prefix |

## Content fields

Structured rows for email tables and UI detail:

```json
{
  "key": "missingMinutes",
  "label": "Minutes overdue",
  "valueTemplate": "{{event.data.missingMinutes}}",
  "sensitive": false
}
```

| `sensitive` | Behavior |
|-------------|----------|
| `false` | Show in email and UI |
| `true` | Mask in UI (`••••`); omit from email unless admin definition |

## Redaction (log-derived)

| Source | Rule |
|--------|------|
| `apperture.module.*.log.*` | No `bodyHtmlTemplate`; `bodyTextTemplate` uses excerpt only |
| Stack traces | Never in template variables |
| Secrets | Filter keys matching `*password*`, `*secret*`, `*token*` from `data` at emit |

## Rendered snapshot

Persist on `NotificationInstances` create:

| Column | Content |
|--------|---------|
| `RenderedSubject` | Final subject |
| `RenderedSummary` | Final summary |
| `RenderedBodyText` | Final plain body |
| `RenderedBodyHtml` | Final HTML or null |
| `RenderedActionText` | CTA label |
| `RenderedActionUrl` | Resolved URL |
| `RenderedContentFieldsJson` | Array of `{ key, label, value }` |
| `RenderContextJson` | Frozen `NotificationRenderContext` |
| `TemplateDefinitionVersion` | Hash or version of template at render time |

`NotificationDeliveries` reference `NotificationInstanceId`; UI history reads snapshot, not live re-render.

## Module-authored templates

Modules ship default template in manifest alongside static rule:

```json
{
  "notificationDefinitions": [
    {
      "id": "sync.failed",
      "displayName": "Student sync failed",
      "rule": { "schemaVersion": "apperture.notification.rule/v1", "...": "..." },
      "template": {
        "schemaVersion": "apperture.notification.template/v1",
        "subjectTemplate": "[{{module.displayName}}] Sync failed",
        "summaryTemplate": "Sync failed for {{integration.displayName}}.",
        "bodyTextTemplate": "Run {{run.id}} failed.\nCode: {{event.data.code}}\n",
        "actionText": "View run",
        "actionUrlTemplate": "{{instance.baseUrl}}/runs/{{run.id}}"
      }
    }
  ]
}
```

Instance admins may override **template** on user-owned copies only; built-in module templates are read-only (subscription-only mutability).

## Expected-run-missing kind mapping

| Occurrence | `notification.kind` | Variant key |
|--------------|----------------------|-------------|
| Activated | `activation` | `variants.activation` |
| Reminder | `reminder` | `variants.reminder` |
| Escalation | `escalation` | `variants.escalation` |
| Resolved | `resolved` | `variants.resolved` |

Default platform templates carry operator-facing body text for each kind. Escalation is a first-class platform kind (NTF-C); there is no separate Attention email composer.

## Managed contact rendering

Email `To` header built from resolved recipient:

- `DisplayName` null → `user@example.com`
- else → `{{displayName}} <{{email}}>` with RFC 2047 encoding if needed

Parsing input formats when adding external recipients:

1. `email@domain`
2. `Name <email@domain>`

Store normalized; never parse from template variables (injection risk).
