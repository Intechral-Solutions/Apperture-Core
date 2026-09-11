# Epic 77 — Notification system UAT checklist

Operator acceptance checklist for the platform notification system (Phases 1–8C). Use in **dev/staging** unless noted. Record pass/fail, operator, date, and notes per row.

**Related:** [NTF-C closure](../01-architecture/transitions/ntf-c-legacy-removal/closure-report.md), [Backlog](./epic77-notification-system-backlog.md), [Implementation plan](../02-decisions/Epic77-Platform-Signals-and-Notification-System-Implementation-Plan.md). Historical cutover runbook: [epic77-expected-run-missing-notification-cutover-runbook.md](./epic77-expected-run-missing-notification-cutover-runbook.md) (completed).

---

## Environment prerequisites

| # | Check | Pass | Notes |
|---|--------|------|-------|
| E1 | SQL migrations through Epic 77 notification/outbox applied | ☐ | |
| E2 | Management Host running with persistence | ☐ | |
| E3 | `AttentionIncidents:Observation:Enabled=true` (expected-run-missing path) | ☐ | |
| E4 | SMTP configured if testing live delivery (staging only) | ☐ | |
| E5 | Operator has `notification.definition.read` (UI: view definitions/instances/delivery) | ☐ | |
| E6 | Operator has `notification.definition.manage` + `integration.update` for subscription/authoring | ☐ | |
| E7 | Operator has `integration.read` for integration notifications / schedule diagnostics | ☐ | |

---

## A. Built-in expected-run-missing flow

| # | Step | Pass | Notes |
|---|------|------|-------|
| A1 | `PlatformEvents:Enabled=true` — events stored on incident open/clear | ☐ | Config diagnostics or integration notifications flags |
| A2 | `Notifications:Evaluation:Enabled=true` | ☐ | |
| A3 | `Notifications:Instances:Enabled=true` | ☐ | |
| A4 | `Notifications:Delivery:Enabled=false` — dry-run (no live SMTP) | ☐ | Dispatch logs “would send”; rows stay pending |
| A5 | Expected-run-missing incident opens (schedule observation) | ☐ | Integration schedule diagnostics |
| A6 | Platform notification instance created for built-in definition `platform.schedule.expected_run_missing` | ☐ | Instances list or integration notifications |
| A7 | Recipient snapshot shows deliverable/skipped; implicit attention-routing labeled separately | ☐ | Instance detail |
| A8 | Outbox rows created (pending when delivery off) | ☐ | Instance detail / delivery page |
| A9 | Reminder instance created after the configured reminder interval | ☐ | Still-active observation |
| A10 | Escalation instance created after `EscalationAfter` from incident start; recipients are the escalation team | ☐ | One instance per incident |
| A11 | Acknowledgement suppresses escalation when routing says so | ☐ | Suppressed instance, no outbox |
| A12 | Duplicate still-active observations do not create a second escalation instance | ☐ | Outbox retries remain on the same instance |
| A13 | **`Notifications:Delivery:Enabled=true` in staging only** — live send verified | ☐ | Not production without SMTP |
| A14 | Notification diagnostics show the instance / outbox lifecycle | ☐ | Config → Diagnostics or integration notifications |

---

## B. User-created notification definition flow

| # | Step | Pass | Notes |
|---|------|------|-------|
| B1 | Create integration-scoped definition from integration notifications | ☐ | `/integrations/{id}/notifications/definitions/new` |
| B2 | Select platform event type from catalog | ☐ | |
| B3 | Add rule filters (property stage) | ☐ | No log/telemetry rules yet |
| B4 | Add template (subject/summary/body) | ☐ | |
| B5 | Validate before save | ☐ | |
| B6 | Render preview (draft) | ☐ | Does not send mail |
| B7 | Save — redirects with post-create query | ☐ | Integration → notifications; config → definition detail |
| B8 | Post-create prompt: Add subscription / Preview / View definition | ☐ | |
| B9 | Add at least one explicit subscription | ☐ | |
| B10 | Preview recipients — deliverable count updates | ☐ | |
| B11 | Trigger matching platform event if possible | ☐ | May require module/runtime event; skip if N/A |
| B12 | Instance and outbox rows created when pipeline enabled | ☐ | |
| B13 | Instance detail links: definition, integration, schedule diagnostics (if incident) | ☐ | |

---

## C. Subscription flow

Perform on **definition detail** and/or **integration notifications** (explicit subscriptions section).

| # | Step | Pass | Notes |
|---|------|------|-------|
| C1 | Add principal subscription | ☐ | |
| C2 | Add operational_team subscription | ☐ | |
| C3 | Add managed_contact subscription | ☐ | |
| C4 | Inline create managed contact if missing (requires `managed_contact.manage`) | ☐ | |
| C5 | Edit delivery mode (`immediate`, `muted`, `digestOnly`, `immediateAndDigest`) | ☐ | |
| C6 | Disable subscription (checkbox) | ☐ | |
| C7 | Remove subscription | ☐ | |
| C8 | Recipient preview refreshes after each change; “Last refreshed” timestamp updates | ☐ | |
| C9 | Empty state: “No explicit subscriptions yet” with correct copy for built-in vs user-created | ☐ | |

---

## D. Diagnostics and drill-down navigation

| # | Route / link | Pass | Notes |
|---|----------------|------|-------|
| D1 | `/config/notifications` — landing cards | ☐ | |
| D2 | `/config/notifications/definitions` — list → detail | ☐ | |
| D3 | Definition detail → edit (fullyEditable only) | ☐ | |
| D4 | Definition detail → filtered instances | ☐ | `?definitionId=` |
| D5 | `/config/notifications/instances` — list → detail | ☐ | |
| D6 | Instance detail → definition, integration notifications, schedule diagnostics `?incidentId=` | ☐ | |
| D7 | `/config/notifications/delivery` — failed/pending/succeeded groups; instance link | ☐ | |
| D8 | `/config/notifications/diagnostics` — parity, config flags | ☐ | |
| D9 | `/integrations/{id}/notifications` — cutover, subscriptions, instances | ☐ | |
| D10 | `/integrations/{id}/schedule-diagnostics` — incident history; `?incidentId=` highlights row | ☐ | |
| D11 | Runbook path documented in UI (`docs/03-operations/…runbook.md`) | ☐ | |

---

## UX states (spot-check)

| State | Where to verify | Pass | Notes |
|-------|-----------------|------|-------|
| Loading | Any notification page initial load | ☐ | |
| Empty | No subscriptions, no instances, no outbox | ☐ | |
| Forbidden | User without `notification.definition.read` | ☐ | |
| Validation failed | Authoring validate errors shown | ☐ | |
| No recipients | User-created definition + empty preview | ☐ | Warning banner |
| Delivery disabled | Delivery page + diagnostics | ☐ | |
| Cutover not ready | Integration notifications readiness badge | ☐ | |
| Parity unavailable | No recent incidents | ☐ | “No recent incidents” |
| Missing integration | Definition without integration scope | ☐ | |
| Stale sign-off | Change parity after sign-off | ☐ | Amber warning |
| Failed outbox | Delivery page failed section | ☐ | |

---

## Permission matrix (UI vs API)

| Capability | API permission (typical) | UI flag | Used for |
|------------|-------------------------|---------|----------|
| View definitions/instances/delivery | `notification.definition.read` | `CanViewNotificationDefinitions` | Read-only pages, preview |
| Manage definitions/subscriptions | `notification.definition.manage` | `CanManageNotificationDefinitions` | Create/edit definitions, subscriptions |
| View integrations | `integration.read` | `CanViewIntegrations` | Integration notifications, diagnostics |
| Update integration / sign-off | `integration.update` | `CanUpdateIntegrations` | Subscriptions on integration-scoped defs, sign-off |
| Create managed contact inline | `managed_contact.manage` | `CanManageManagedContacts` | Checkbox on managed_contact add |

**Known mismatch:** Broader integration pages (detail, list, schedules) use `CanManageIntegrations` for edit actions, while notification subscription management uses `CanUpdateIntegrations` + `CanManageNotificationDefinitions`. Operators with manage-but-not-update may edit integrations but not platform subscriptions — intentional for cutover safety.

---

## Test evidence (automated)

Run before marking milestone UAT complete:

```bash
dotnet build
dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~Notification"
dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj --filter "FullyQualifiedName~Epic77"
```

Optional (environment-dependent):

```bash
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~Notification|Schedule"
```

Document any skipped integration tests (SQL, SMTP, observation timing).

---

## Sign-off

| Role | Name | Date | Milestone ready? |
|------|------|------|------------------|
| Operator / QA | | | ☐ |
| Engineering | | | ☐ |

**Feature-complete for current milestone** when sections A–D pass in staging, automated tests green, and production cutover remains manual per runbook (not automated in UI). **No retry-now or send-now** operator actions in this milestone.
