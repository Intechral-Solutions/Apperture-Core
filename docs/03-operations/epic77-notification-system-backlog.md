# Epic 77 — Notification system backlog and known limitations

Concise backlog after Phases 1–8C and 9A UAT hardening. Items are **out of scope** for the current milestone unless marked blocker.

**Module SDK stabilization:** [Epic 103A plan](../02-decisions/Epic103A-Platform-Signals-SDK-Stabilization-Implementation-Plan.md), [ADR-0038](../02-decisions/ADR-0038-platform-signals-sdk-conventions.md).

---

## Implemented in this milestone

- Platform events persistence and catalog
- Notification definitions (built-in + user-created, integration-scoped)
- Rule evaluation (property stage; built-in expected-run-missing)
- Notification instances and delivery outbox (email channel)
- Subscriptions (principal, operational team, managed contact, delivery modes)
- Management UI: definitions, instances, delivery health, diagnostics, integration notifications
- Definition authoring (create/edit, validate, render preview)
- Post-create subscription prompt and recipient preview
- Expected-run-missing cutover readiness, parity diagnostics, operator sign-off (audit only)
- Read-only delivery; no UI flag changes

---

## Known limitations (current behavior)

| Area | Limitation |
|------|------------|
| Channels | Email only; no webhook/Teams/SMS |
| Rules | Property stage for user definitions; **no log-source or telemetry aggregate rules** |
| Templates | Allowlisted variables; no arbitrary logic in user templates |
| Digest / journal | Schema/future only; **no digest scheduler or in-app journal UI** |
| Escalation | **No platform notification escalation** (Epic 69 attention escalation separate) |
| User preferences | **No user opt-out UI** or preference center |
| Operator actions | **No retry-now or send-now** buttons in UI |
| Cutover | **Manual config only** — UI does not set `UsePlatformPipeline` or delivery flags |
| Production | **No automated production cutover** — runbook + operator sign-off |
| Global subscriptions | **No global subscription management page** (per-definition / per-integration only) |
| Integration permissions | Notification manage uses `CanUpdateIntegrations`; other integration UI uses `CanManageIntegrations` |
| Event triggering | User-created definitions require matching platform events from runtime; UAT may skip if no emitter |

---

## Backlog (future phases)

### P0 — Production cutover execution

- Production runbook execution checklist ownership
- Post-cutover monitoring and rollback drills
- Automated parity gate (optional CI/staging job)

### P1 — Operator delivery actions

- Retry failed outbox row (with idempotency)
- Send-now / force dispatch (staging-gated)
- Bulk outbox status in UI

### P2 — User-facing preferences

- User opt-out API + UI
- User preference center (per-definition / per-team)
- Digest-only and muted mode UX beyond admin subscription edit

### P3 — Rule and template expansion

- Log-source rule stage
- Telemetry aggregate rule stage
- Additional platform event types from modules

### P4 — Product features

- Escalation policies on platform notifications
- Digest/journal scheduler and UI
- Non-email channels
- Global subscription management page
- Definition authoring polish (expression stage, module manifest UX)

### P5 — Platform alignment

- Align `CanManageIntegrations` vs `CanUpdateIntegrations` across integration UI where safe
- Federated child→parent notifications (ADR-0010 transport)

---

## UI routes reference

Centralized in `NotificationUiRoutes` (Management.Ui.Shared):

| Helper | Path |
|--------|------|
| `Landing()` | `/config/notifications` |
| `DefinitionsList()` | `/config/notifications/definitions` |
| `DefinitionDetail(id)` | `/config/notifications/definitions/{id}` |
| `DefinitionEdit(id)` | `/config/notifications/definitions/{id}/edit` |
| `DefinitionCreate()` | `/config/notifications/definitions/new` |
| `DefinitionInstances(id)` | `/config/notifications/instances?definitionId={id}` |
| `InstancesList()` | `/config/notifications/instances` |
| `InstanceDetail(id)` | `/config/notifications/instances/{id}` |
| `Delivery()` | `/config/notifications/delivery` |
| `DeliveryDiagnostics()` | `/config/notifications/diagnostics` |
| `IntegrationNotifications(id)` | `/integrations/{id}/notifications` |
| `IntegrationDefinitionCreate(id)` | `/integrations/{id}/notifications/definitions/new` |
| `IntegrationScheduleDiagnostics(id, incidentId?)` | `/integrations/{id}/schedule-diagnostics` |
| `IntegrationCutoverReadiness(id)` | `/integrations/{id}/notifications#cutover-readiness` |

Post-create navigation: `NotificationPostCreateNavigation` (`createdDefinitionId`, `integrationId` query params).
