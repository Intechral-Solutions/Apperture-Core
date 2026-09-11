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
- Expected-run-missing activation, reminder, escalation, and resolved on the **platform pipeline only** (NTF-C complete)
- Read-only delivery diagnostics; no UI flag changes

---

## Known limitations (current behavior)

| Area | Limitation |
|------|------------|
| Channels | Email only; no webhook/Teams/SMS |
| Rules | Property stage for user definitions; **no log-source or telemetry aggregate rules** |
| Templates | Allowlisted variables; no arbitrary logic in user templates |
| Digest / journal | Schema/future only; **no digest scheduler or in-app journal UI** |
| Escalation | Platform `escalation` kind is implemented (once per incident; outbox retries delivery) |
| User preferences | S9 shipped operator email preferences / opt-out at `/me/notifications` (not a full digests center) |
| Operator actions | **No retry-now or send-now** buttons in UI |
| Cutover | **Completed (NTF-C)** — legacy Attention delivery path removed |
| Production | Evaluation / Instances / Delivery remain independent operational flags |
| Global subscriptions | **No global subscription management page** (per-definition / per-integration only) |
| Integration permissions | Notification manage uses `CanUpdateIntegrations`; other integration UI uses `CanManageIntegrations` |
| Event triggering | User-created definitions require matching platform events from runtime; UAT may skip if no emitter |

---

## Backlog (future phases)

### P0 — Production cutover execution

Completed in NTF-C. Historical notes remain in [NTF-C closure](../01-architecture/transitions/ntf-c-legacy-removal/closure-report.md).

### P1 — Operator delivery actions

- Retry failed outbox row (with idempotency)
- Send-now / force dispatch (staging-gated)
- Bulk outbox status in UI

### P2 — User-facing preferences

- Operator preference UX beyond S9 email toggles (digests/muted modes)
- User preference center (per-definition / per-team)
- Digest-only and muted mode UX beyond admin subscription edit

### P3 — Rule and template expansion

- Log-source rule stage
- Telemetry aggregate rule stage
- Additional platform event types from modules

### P4 — Product features

- Richer escalation policies (repeat / multi-tier) beyond one occurrence per incident
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

Post-create navigation: `NotificationPostCreateNavigation` (`createdDefinitionId`, `integrationId` query params).
