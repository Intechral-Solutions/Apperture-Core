# Epic 77 — Expected run missing notification cutover runbook

Operator guide for moving `expected_run_missing` notifications from Epic 69 (`AttentionIncidentNotificationOrchestrator` + ledger) to the platform notification pipeline (platform events → rule evaluation → notification instances → delivery outbox → SMTP).

**Related:** [Epic 77 implementation plan](../02-decisions/Epic77-Platform-Signals-and-Notification-System-Implementation-Plan.md), [ADR-0037](../02-decisions/ADR-0037-platform-signals-events-and-notification-system.md), [Epic 69 attention notifications](../02-decisions/Epic69-Attention-Notification-Policy-and-Delivery-Evidence.md).

---

## Prerequisites

- SQL migrations applied through `Epic77_NotificationInstances` and `Epic77_NotificationDeliverySentAtUtc` (and `Epic77_NotificationOutboxRowVersion` when present).
- Management Host running with persistence and `AttentionIncidents:Observation:Enabled=true` (incident observation drives both paths today).
- SMTP configured under `AttentionNotifications` if you will enable live delivery.
- Operator access to Management API diagnostics endpoints (see [Diagnostics APIs](#diagnostics-apis)).
- Understanding that **cutover is manual** — no flag flips automatically.

---

## Required configuration flags

| Flag | Section | Purpose |
|------|---------|---------|
| `PlatformEvents:Enabled` | `PlatformEvents` | Dual-emit schedule platform events (`expected_run_missing` / cleared). |
| `Notifications:Evaluation:Enabled` | `Notifications:Evaluation` | Rule matching on platform events / incident transitions. |
| `Notifications:Instances:Enabled` | `Notifications:Instances` | Create `NotificationInstances` + outbox rows. |
| `Notifications:Delivery:Enabled` | `Notifications:Delivery` | Live SMTP from outbox; `false` = dry-run only. |
| `Notifications:UsePlatformPipeline:Enabled` | `Notifications:UsePlatformPipeline` | Skip Epic 69 orchestrator when `true`. |

See [Configuration reference](#configuration-reference) for safe defaults and dangerous combinations.

---

## Recommended enablement order

1. **`PlatformEvents:Enabled=true`** — Ensure platform events are stored when incidents open/clear/remind.
2. **`Notifications:Evaluation:Enabled=true`** — Evaluate rules on append / incident observation.
3. **`Notifications:Instances:Enabled=true`** — Materialize instances and pending outbox rows (no email yet).
4. **`Notifications:Delivery:Enabled=false`** — Dispatch runs in **dry-run** mode (logs “would send”, rows stay `pending`).
5. **Review parity diagnostics** — Per integration/incident; see [Diagnostics APIs](#diagnostics-apis). Target: parity acceptance with no blocking findings.
6. **`Notifications:Delivery:Enabled=true`** (dev/staging only) — Live send from stored instance snapshots; Epic 69 may still send unless step 8.
7. **Review send behavior** — Outbox `succeeded` / `failed`, subjects/bodies, recipient provenance.
8. **`Notifications:UsePlatformPipeline=true`** — Only after acceptance; disables Epic 69 orchestrator for observation cycle. **Do not enable in production until staging sign-off.**

---

## Rollback steps

| Step | Action |
|------|--------|
| 1 | Set `Notifications:UsePlatformPipeline:Enabled=false` — Restores Epic 69 orchestrator immediately on next observation cycle. |
| 2 | Set `Notifications:Delivery:Enabled=false` — Stops live platform SMTP; dry-run or no dispatch effect depending on hosted service. |
| 3 | Set `Notifications:Instances:Enabled=false` — Stops new platform instances (existing rows remain for audit). |
| 4 | Set `Notifications:Evaluation:Enabled=false` — Stops rule evaluation (optional if only rolling back delivery). |
| 5 | Set `PlatformEvents:Enabled=false` — Stops new platform events (optional; incidents still tracked). |

Epic 69 ledger history is unchanged. Platform instances/outbox are audit-only after rollback.

---

## Diagnostics APIs

| Endpoint | Use |
|----------|-----|
| `GET /api/notification-diagnostics/health` | Global instance/outbox counts by status. |
| `GET /api/notification-diagnostics/by-integration/{integrationId}` | Recent instances, outbox, suppressed, failed. |
| `GET /api/notification-diagnostics/by-incident/{attentionIncidentId}` | Per-incident platform rows. |
| `GET /api/notification-diagnostics/expected-run-missing/parity` | Side-by-side Epic 69 ledger vs platform (by kind/recipient). |
| `GET /api/notification-diagnostics/expected-run-missing/parity-acceptance` | Aggregated acceptance summary for cutover decision. |
| `GET /api/integrations/{id}/schedule-diagnostics` | Schedule view + Epic 69 ledger + platform instances/outbox. |

---

## Troubleshooting

### Platform instances missing

- Confirm `Notifications:Evaluation:Enabled` and `Notifications:Instances:Enabled`.
- Confirm `PlatformEvents:Enabled` and that `apperture.schedule.expected_run.missing` / `.cleared` events appear for the integration.
- Check rule match: built-in definition `platform.schedule.expected_run_missing`.
- Reminder instances only on `ObservedStillActive` incident path, not on platform-event-only evaluation.

### Outbox `pending`

- Expected when `Notifications:Delivery:Enabled=false` (dry-run reverts rows to `pending`).
- When delivery enabled: dispatch hosted service must be running; check `NextAttemptAtUtc` not in future.

### Outbox `failed`

- Inspect `LastError` on row; verify SMTP settings under `AttentionNotifications`.
- After `MaxAttempts`, row is terminal `failed` — fix SMTP and optionally reset row only in dev (no automatic retry UI yet).

### Parity `missing_in_platform`

- Epic 69 sent/suppressed but no matching platform recipient/outbox — check subscriptions, implicit attention routing, evaluation/instance flags.
- Often: Epic 69 used `IAttentionRecipientResolver` team emails not present in platform subscription path.

### Parity `extra_in_platform`

- Platform has recipients Epic 69 did not — common during dual-run if subscriptions differ from attention primary team.
- Non-blocking for cutover if intentional; otherwise align subscriptions with attention routing.

### `no_route` / policy suppression

- Platform instance `status=suppressed`, `suppressionReason=no_route_configured` or `policy_disabled` — mirrors Epic 69 suppressed ledger semantics.
- Configure operational team / subscriptions before enabling delivery.

### Duplicate / dual-send risk

- Occurs when **`Notifications:Delivery:Enabled=true`** and **`Notifications:UsePlatformPipeline=false`** — both Epic 69 and platform dispatch send email.
- Mitigation: keep delivery `false` until parity accepted, then enable `UsePlatformPipeline=true` before or when enabling delivery in staging.

---

## Startup warnings

Management Host logs **warnings** at startup for dangerous flag combinations (see `NotificationConfigurationSafetyHostedService`). Warnings do not block startup.

---

## Configuration reference

### Safe defaults (production)

```json
"PlatformEvents": { "Enabled": false },
"Notifications": {
  "Evaluation": { "Enabled": false },
  "Instances": { "Enabled": false },
  "Delivery": { "Enabled": false },
  "UsePlatformPipeline": { "Enabled": false }
}
```

### Recommended dev — shadow + dry-run

```json
"PlatformEvents": { "Enabled": true },
"Notifications": {
  "Evaluation": { "Enabled": true },
  "Instances": { "Enabled": true },
  "Delivery": { "Enabled": false },
  "UsePlatformPipeline": { "Enabled": false }
}
```

### Recommended staging — live platform send, Epic 69 still active

```json
"PlatformEvents": { "Enabled": true },
"Notifications": {
  "Evaluation": { "Enabled": true },
  "Instances": { "Enabled": true },
  "Delivery": { "Enabled": true },
  "UsePlatformPipeline": { "Enabled": false }
}
```

Use parity acceptance API; expect possible duplicate emails until cutover.

### Staging/prod cutover (after acceptance)

```json
"Notifications": {
  "Evaluation": { "Enabled": true },
  "Instances": { "Enabled": true },
  "Delivery": { "Enabled": true },
  "UsePlatformPipeline": { "Enabled": true }
}
```

### Dangerous combinations

| Combination | Risk |
|-------------|------|
| `UsePlatformPipeline=true` + `Delivery=false` | Cutover without delivery — incidents never emailed by either path if instances suppress or have no outbox. |
| `Delivery=true` + `UsePlatformPipeline=false` | **Dual send** — Epic 69 and platform both email. |
| `Delivery=true` + `Instances=false` | Dispatch has nothing valid to send; misconfiguration. |
| `Instances=true` or `Evaluation=true` + `PlatformEvents=false` | Activation/resolved instances may not be created (event-driven path missing). |
| `UsePlatformPipeline=true` without prior parity | Wrong recipients or missing sends vs Epic 69 behavior. |
