# Epic 70 — Attention ownership, recipient resolution, and escalation routing

## Status

Accepted

## Context

Epics 68–69 made attention incidents and notification delivery durable and evidence-backed, but recipients were still effectively static (for example, global options). Operators need to know **who** is notified for an integration incident, **why** (integration override vs instance default vs none), and how **escalation** and **acknowledgment** interact—without conflating incident truth with routing.

## Decision

1. **Separate layers** (unchanged intent): incident truth (evaluator/tracker), notification timing policy (enabled, reminder interval, resolved sends), and **recipient resolution** (`IAttentionRecipientResolver` → `AttentionRecipientResolution`).
2. **Persistence**: `InstanceAttentionRoutings` + `IntegrationAttentionRoutings` + `AttentionRoutingRecipients` (normalized addresses, roles `Primary` / `Escalation`). Instance row id `instance_default` seeds `expected_run_missing` with a 5-minute reminder interval; operators add global primary/escalation recipients via data or future tooling.
3. **Resolution chain** (per condition, v1): integration override rows if present for `(IntegrationId, ConditionKey)`; else instance row for `(instance_default, conditionKey)`. Primary and escalation lists resolve **independently** (empty integration list inherits instance for that role). Policy flags (`IsEnabled`, `SendResolvedNotification`, acknowledgment suppression, ticks) follow the integration row when it exists, else instance.
4. **Sources**: `IntegrationOverride`, `GlobalDefault`, `None` on `AttentionRecipientResolution` for explainability.
5. **Orchestration**: `AttentionIncidentNotificationOrchestrator` resolves once per handler path; activation/reminder/resolved use **primary** recipients; **escalation** uses escalation recipients after `EscalationAfter` from incident `StartedAtUtc`, **once** per incident (non-suppressed escalation ledger row), skipped if `SuppressEscalationWhenAcknowledged` and the incident is acknowledged. Escalation is based on incident age, not primary send success.
6. **Evidence**: `AttentionIncidentNotifications` stores `RecipientResolvedFrom`, `WasEscalation`, `EscalationThresholdApplied`, `SuppressedDueToAcknowledgment`. Suppressions such as `no_route_configured` remain explicit.
7. **API/UI**: `GET/PUT .../attention-routing` and schedule diagnostics surface effective routing and notification evidence.

## Consequences

- Routing is queryable and honest when no recipients exist.
- Future scopes (team/module) can extend the resolver chain without changing incident or ledger shapes.
- Instance seed is required for sensible default policy ticks when no custom SQL is applied.

## Related

- Epics 68–69 (incidents, notifications)
- [`SqlAttentionRecipientResolver`](../../src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlAttentionRecipientResolver.cs), [`AttentionIncidentNotificationOrchestrator`](../../src/Apperture.ControlPlane/Apperture.ControlPlane.Security/AttentionIncidentNotificationOrchestrator.cs)
