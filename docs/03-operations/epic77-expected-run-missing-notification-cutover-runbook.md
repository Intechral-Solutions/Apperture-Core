# Epic 77 — Expected run missing notification cutover runbook

Operator guide for moving `expected_run_missing` notifications from Epic 69 (`AttentionIncidentNotificationOrchestrator` + ledger) to the platform notification pipeline (platform events → rule evaluation → notification instances → delivery outbox → SMTP).

**Related:** [Epic 77 implementation plan](../02-decisions/Epic77-Platform-Signals-and-Notification-System-Implementation-Plan.md), [ADR-0037](../02-decisions/ADR-0037-platform-signals-events-and-notification-system.md), [Epic 69 attention notifications](../02-decisions/Epic69-Attention-Notification-Policy-and-Delivery-Evidence.md), [UAT checklist](./epic77-notification-system-uat-checklist.md), [Backlog](./epic77-notification-system-backlog.md).

---

## Management UI routes (operator)

| Page | Route |
|------|--------|
| Notifications home | `/config/notifications` |
| Definitions | `/config/notifications/definitions` |
| Definition detail / subscriptions | `/config/notifications/definitions/{definitionId}` |
| Instances | `/config/notifications/instances` |
| Instance detail (snapshot, outbox) | `/config/notifications/instances/{instanceId}` |
| Delivery health | `/config/notifications/delivery` |
| Diagnostics (parity, flags) | `/config/notifications/diagnostics` |
| Platform settings (flag edits) | `/config/platform-settings` |
| Integration cutover + subscriptions | `/integrations/{integrationId}/notifications` |
| Schedule + incident history | `/integrations/{integrationId}/schedule-diagnostics` |
| Incident deep-link | `/integrations/{integrationId}/schedule-diagnostics?incidentId={id}` |

The diagnostics UI remains **read-only** for flags. Use `/config/platform-settings` for edits and resets. Sign-off is audit evidence only.

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
5. **Review parity diagnostics** — Per integration/incident; see [Diagnostics APIs](#diagnostics-apis). Target: parity acceptance with no blocking findings. Use **Config → Notifications → Diagnostics** or **Integrations → {id} → Notifications** cutover readiness panel.
6. **Dry-run review** — With `Notifications:Delivery:Enabled=false`, confirm instances and outbox rows are created; dispatch logs show would-send only. Review dangerous flag combinations in the UI or `GET /api/notification-diagnostics/configuration`.
7. **Operator sign-off** — On the integration notifications page, record sign-off when parity looks acceptable (`readyForCutover`) or document `notReady` if not. Sign-off is **evidence for audit**; it does **not** change flags or enable cutover. Revoke sign-off if conditions change. Audit events: `notification.cutover.expected_run_missing.signed_off`, `.sign_off_replaced`, `.sign_off_revoked`.
8. **`Notifications:Delivery:Enabled=true`** (dev/staging only) — Live send from stored instance snapshots; Epic 69 may still send unless step 10.
9. **Review send behavior** — Outbox `succeeded` / `failed`, subjects/bodies, recipient provenance.
10. **`Notifications:UsePlatformPipeline=true`** — Only after sign-off and staging validation; disables Epic 69 orchestrator for observation cycle. **Manual config change only** — the UI does not flip this flag.

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
| `GET /api/notification-diagnostics/configuration` | Instance notification flag state and dangerous-combination warnings. |
| `GET /api/integrations/{id}/expected-run-missing/cutover-sign-off` | Latest operator sign-off for the integration. |
| `POST /api/integrations/{id}/expected-run-missing/cutover-sign-off` | Create/replace sign-off (`readyForCutover` or `notReady`). Requires integration update + notification manage. |
| `POST /api/integrations/{id}/expected-run-missing/cutover-sign-off/revoke` | Revoke the latest active sign-off. |
| `GET /api/integrations/{id}/schedule-diagnostics` | Schedule view + Epic 69 ledger + platform instances/outbox. |
| `GET /api/integrations/{id}/expected-run-missing/cutover-validation` | Read-only staging validation (modes, sign-off, parity, blockers). |

---

## Staging validation script (manual)

Execute in **staging** only. No step changes production flags automatically.

| Step | Action | Pass criteria |
|------|--------|---------------|
| 1 | Set **shadow mode** flags (see [Configuration reference](#configuration-reference)) | `GET /api/notification-diagnostics/configuration` shows shadow/dry-run modes, no dual-send warning |
| 2 | Trigger or wait for `expected_run_missing` on a test integration | Attention incident opens; platform missing event stored when `PlatformEvents:Enabled=true` |
| 3 | Open **Integrations → {id} → Notifications** cutover panel | Platform instances and pending outbox rows appear; Epic 69 ledger may still send if delivery enabled |
| 4 | Run parity acceptance API or UI refresh | No blocking findings; note any `missing_in_platform` recipient gaps |
| 5 | Resolve recipient gaps | External operational-team emails added as managed contacts + explicit subscriptions |
| 6 | Record **readyForCutover** sign-off | `GET cutover-sign-off` returns `readyForCutover`; audit event emitted |
| 7 | Enable `Notifications:Delivery:Enabled=true` in staging | Platform outbox moves to `succeeded` for test recipients; inspect subjects/bodies |
| 8 | Verify Epic 69 still sends while `UsePlatformPipeline=false` | Expect possible duplicate email — confirms dual-send risk |
| 9 | Enable `Notifications:UsePlatformPipeline=true` in staging | Epic 69 orchestrator skipped on next observation; only platform sends |
| 10 | Verify platform activation, reminder (if applicable), and **resolved** | Resolved instance requires `apperture.schedule.expected_run.cleared` platform event on incident resolve (emitted when `PlatformEvents:Enabled=true`) |
| 11 | Run `GET cutover-validation` | No readiness blockers; sign-off not stale |
| 12 | If failed, follow [Rollback steps](#rollback-steps) | Epic 69 restored when `UsePlatformPipeline=false` |

Sign-off is **evidence only** — completing the script does not enable production cutover.

---

## Epic 69 ledger history (replacement plan)

| Decision | Detail |
|----------|--------|
| **Keep** | All historical `AttentionIncidentNotifications` rows remain as legacy evidence |
| **Display** | Schedule diagnostics and integration notifications show Epic 69 ledger alongside platform instances/outbox |
| **Do not migrate** | No backfill of old ledger rows into `NotificationInstances` unless a future phase requires it |
| **Dual-write** | **Not implemented** — platform deliveries do not write to Epic 69 ledger; operators use platform outbox + audit for post-cutover evidence |
| **UI label** | Epic 69 table labeled “legacy attention notification ledger” in schedule diagnostics |

---

## Recipient parity gap

| Topic | Guidance |
|-------|----------|
| **Cause** | Epic 69 resolves recipients via attention routing and operational teams (including external email members). Platform path uses explicit subscriptions (principals, operational teams, managed contacts). |
| **Symptom** | Parity `missing_in_platform` — Epic 69 sent or would send to recipients with no platform subscription |
| **Before cutover** | Convert external-email-only team members to **managed contacts** and add explicit subscriptions on the integration notifications page |
| **Diagnostics** | Cutover validation returns `recipients:missing_in_platform` blocker; UI shows recipient parity gap warning |

---

## Resolved-event behavior

| Topic | Guidance |
|-------|----------|
| **Platform path** | Resolved notification instances are driven by `apperture.schedule.expected_run.cleared` platform events |
| **Emission** | `ScheduleExpectationPlatformEventDualEmitter` emits cleared events on `AttentionIncidentObserveTransition.Resolved` when `PlatformEvents:Enabled=true` |
| **Gap** | If platform events are disabled, resolved platform instances will not be created from events; Epic 69 may still send resolved email via orchestrator |
| **Incident-only evaluation** | Reminder instances use incident observation path; activation/resolved from events require platform events enabled |
| **Backlog** | If incidents can resolve without observation cycle, ensure cleared events are emitted on all resolve transitions — do not enable partial cutover without verifying resolve path in your deployment |

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
