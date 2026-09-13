# NTF-C blocker report — legacy Attention delivery not removed

> **Resolved 2026-09-10.** Escalation is now a first-class platform notification kind. Legacy delivery was then removed. See [closure-report.md](closure-report.md). The rest of this file is the historical stop decision.

**Decision:** stop before destructive removal.  
**Date:** 2026-09-10  
**Scope:** Phase 1 inventory + Phase 2 decision only. No code, tables, or flags were deleted.

This is not a roadmap. It records why the platform pipeline cannot yet be the single production delivery path.

---

## Decision (Phase 2)

The platform pipeline **already replaces** legacy immediate-email for:

- expected-run-missing **activation**
- **reminders** (interval + acknowledgement suppression)
- **resolved** (when routing `SendResolvedNotification` is on)
- integration Attention routing for the **primary** team
- operational-team / managed-contact resolution (subscriptions + implicit Attention routing)
- SMTP via `Notifications:Email` + outbox retries
- personal inbox materialization
- instance dedupe
- diagnostics for the platform path

It does **not** replace **escalation** delivery.

Attention routing still stores and exposes `EscalationAfter`, `EscalationOperationalTeamId`, and `SuppressEscalationWhenAcknowledged` (`AttentionRoutingDefaults.razor`, integration routing APIs). The only production writer of escalation emails is `AttentionIncidentNotificationOrchestrator.TryEscalationAsync`. `NotificationKinds` is `{activation, reminder, resolved}` only. The built-in expected-run-missing definition has no escalation variant.

That is required product behavior available only on the legacy path. Removing the orchestrator would silently stop honoring configured escalation teams. Papering over that (leave the UI, drop the sender) is not acceptable.

Digest delivery is **not** a blocker (legacy does not implement digest either).

---

## Phase 1 — current state

### 1. `Notifications:UsePlatformPipeline` defaults

`NotificationPipelineOptions.SectionName` is `Notifications:UsePlatformPipeline`. The C# property `Enabled` defaults to **false**. `PlatformNotificationCutoverGate.ShouldInvokeEpic69Orchestrator` is `!options.Enabled`.

Effective value is layered: **PlatformSettingValues (DB) > IConfiguration (`Notifications:UsePlatformPipeline:Enabled`) > definition default**. Overlay: `NotificationPipelineOptionsPlatformSettingsPostConfigure` → `PlatformSettingsResolver`.

| Source | Value | Notes |
|--------|--------|--------|
| C# `NotificationPipelineOptions.Enabled` | **false** | Used if nothing binds the section |
| Platform setting definition `Notifications.UsePlatformPipeline.Enabled` | **false** | `PlatformSettingDefinitionRegistry` |
| Management Host `appsettings.json` (committed) | **true** | Also Evaluation/Instances/Delivery **true** |
| `appsettings.Development.example.json` | n/a | Redirects to docker-dev example |
| `deploy/config/examples/appsettings.docker-dev.json.example` | **false** | Instances/Delivery **false**; Evaluation **true** |
| WorkerHost `appsettings.json` | **unset** | WorkerHost does **not** register `AddAppertureNotifications` or observation |
| Aspire `src/Apperture.AppHost` | **unset** | Forces `ASPNETCORE_ENVIRONMENT=Development`; does not set notification flags |
| Test code | mixed | Cutover tests assert both true and false; `PlatformPipelineCutoverGuardTests` asserts default false |
| Deployment templates besides docker-dev example | none found | UI `deploy/config/ui/appsettings.json` has no notification pipeline flags |

No `appsettings.Development.json` is in the repo. Aspire Development therefore uses committed Management Host `appsettings.json` unless a local Development file exists.

### 2. What a normal current development instance uses

**CONFIRMED for a stock repo + Aspire (no local Development.json, no DB override):** **platform pipeline only**. Management Host observation skips the legacy orchestrator. Instances and delivery are on in committed host config. WorkerHost is not on the notification path.

**CONFIRMED if an operator copies the docker-dev example to `appsettings.Development.json` (as that example instructs):** **legacy orchestrator active**, platform evaluation on, instances/delivery off → email only from the Attention ledger path (if SMTP is configured). Dual-send is avoided because Delivery is false.

**CONFIRMED if a Platform Setting row sets `Notifications.UsePlatformPipeline.Enabled=false`:** Epic 69 runs again, even when host JSON says true.

So the inventory’s earlier “default dual-path” claim is only true for **code / definition / docker-dev example**. The **committed Management Host** baseline is platform-only.

### 3. Production call paths that still invoke the legacy orchestrator or write the ledger

**Write path (only one):**

`AttentionIncidentObservationHostedService.ObserveCycleAsync` (Management Host; gated by `AttentionIncidents:Observation:Enabled`)

1. Always: incident observe + `ScheduleExpectationPlatformEventDualEmitter` (platform events on Opened/Resolved).
2. Always on `ObservedStillActive`: `INotificationEvaluateOnlyCoordinator.EvaluateIncidentAsync` (platform reminders).
3. **If** `ShouldInvokeEpic69Orchestrator`: `AttentionIncidentNotificationOrchestrator.HandleObserveResultAsync` → SMTP via `SmtpAttentionNotificationSender` → `IAttentionIncidentNotificationLedger.AppendAsync` → table `AttentionIncidentNotifications`.

No other production caller of `HandleObserveResultAsync` exists.

**Not a caller:** WorkerHost (no notification/observation registration).

### 4. Runtime / read / diagnostic / API / UI dependencies on the legacy ledger

| Consumer | What it does |
|----------|----------------|
| `SqlAttentionIncidentNotificationLedger` | Persist + query ledger |
| `IntegrationScheduleDiagnosticsService` | `RecentAttentionIncidentNotifications` |
| `IntegrationsController` schedule-diagnostics DTO | Maps ledger rows |
| `IntegrationScheduleDiagnostics.razor` | “Notification evidence (Epic 69 legacy ledger)” |
| `SqlNotificationDeliveryDiagnosticsStore` | Incident lookup + `ListEpic69LedgerForIncidentAsync` |
| `NotificationDeliveryDiagnosticsService` | Parity compare platform vs ledger |
| `NotificationDiagnosticsController` | `/expected-run-missing/parity`, `/parity-acceptance`, cutover-status |
| `ExpectedRunMissingParityComparer` / acceptance evaluator | Epic 69 vs platform kinds |
| Cutover sign-off / validation | `ExpectedRunMissingCutoverSignOff*`, `ExpectedRunMissingCutoverValidationService` |
| UI | `NotificationPipelineCutoverStatusCard`, Integration Notifications cutover readiness, Diagnostics/Delivery cutover cards |

`AttentionIncidentNotifications` is **not** referenced by audit append or `PlatformEvents` as a required store. Historical ledger rows are delivery evidence only. Equivalent durable evidence for the platform path is `NotificationInstances` + `NotificationDeliveryOutbox` (and incidents themselves).

### 5. Functional parity (required behavior)

| Behavior | Platform pipeline | Legacy orchestrator |
|----------|-------------------|---------------------|
| Activation | Yes — missing event → rule → instance/outbox | Yes — Opened |
| Reminders | Yes — `EvaluateIncidentAsync` on still-active; interval vs last successful outbox; ack suppress | Yes — vs last ledger send |
| Ack suppression (reminders) | Yes — `SuppressRemindersWhenAcknowledged` | Yes |
| Resolved | Yes — cleared event + `SendResolvedNotification` + prior successful activation outbox | Yes — ledger prior activation |
| Primary Attention routing | Yes — `IAttentionRecipientResolver` + implicit team in `SqlNotificationRecipientResolver` | Yes — same resolver |
| Operational teams / managed contacts | Yes — subscriptions + expander | Yes — routing recipients |
| Email | Yes — outbox + `SmtpEmailNotificationChannel` + retries | Yes — direct `SmtpAttentionNotificationSender` (no outbox retry) |
| Personal inbox | Yes — `NotificationUserInboxService` | No |
| Dedupe | Yes — `NotificationInstanceDedupeKey` unique | Ledger duplicate-activation / escalation-attempt checks |
| Retries / failures | Yes — outbox MaxAttempts / backoff | Single attempt per send; failed ledger row |
| Diagnostics | Yes — SMTP test, pipeline test, health, by-integration | Ledger + **parity vs platform** |
| Subscriptions / overrides | Yes | No (config `AttentionNotifications:ExpectedRunMissing:Recipients` is legacy/merge only) |
| **Escalation** | **No** — no kind, no template, no gate | **Yes** — escalation team, after-interval, ack suppress |

`ScheduleExpectationPlatformEventDualEmitter` is **not** a second email path. It only emits platform events (`expected_run.missing` / `.cleared`). The “dual” name is leftover from the transition. Keep the emitter (rename later); it is required by the platform pipeline.

### 6. Tests that depend on the legacy path

These are not “compat-only” wrappers; they encode Epic 69 as a live alternative:

- `AttentionIncidentNotificationOrchestratorTests` (activation, reminder, resolved, **escalation**)
- `SmtpAttentionNotificationSenderTests`
- `PlatformPipelineCutoverGuardTests`, `NotificationPipelineCutoverPreflightTests`
- `ExpectedRunMissingParityAcceptanceEvaluatorTests`, `ExpectedRunMissingCutover*Tests`
- `Epic77CCutoverStatusUiTests`, `Epic77CutoverReadinessUiTests`
- `Epic77CutoverDocumentationTests` / `Epic77UatDocumentationTests` (assert runbook flag names)
- `IntegrationScheduleDiagnosticsTests` (mocks ledger)
- `NotificationDeliveryDiagnosticsTests` (Epic 69 rows)

Platform-pipeline tests (`NotificationInstanceCoordinator*`, dispatch, recipient resolver, inbox) are independent and should stay.

### 7. Tables / entities that would become dead after a *complete* cutover

After a future removal (not done now):

- `AttentionIncidentNotifications` / `AttentionIncidentNotificationEntity` / ledger interface
- Cutover sign-off table `ExpectedRunMissingCutoverSignOffs` (only meaningful while comparing paths)
- Setting key `Notifications.UsePlatformPipeline.Enabled`
- Config section `AttentionNotifications:ExpectedRunMissing` (SMTP merge + static recipients)

Do **not** drop these until escalation is on the platform path (or escalation is explicitly retired as a product feature). Historical migrations must stay.

### 8. Docs / flags / UI still describing dual-pipeline / cutover

Operator-facing: Notifications landing, Delivery, Diagnostics, Integration Notifications, schedule diagnostics, Platform Settings help for `Use platform pipeline`, cutover status card.

Docs: `docs/03-operations/epic77-expected-run-missing-notification-cutover-runbook.md`, Epic77C / S8D / S12 decisions, `docs/audits/stabilization-closure-check.md`, docker-dev example comments, `current-inventory.md` (partially stale on “default dual-path”).

---

## Smallest work to close the gap

Implement **escalation as a first-class platform notification kind**, then remove the legacy delivery path.

1. Add `NotificationKinds.Escalation` and a template variant on `ExpectedRunMissingNotificationDefinitionSeed`.
2. On `ObservedStillActive`, evaluate an escalation gate in `NotificationInstanceCoordinator` (or a dedicated evaluator) using existing Attention routing: `EscalationAfter`, escalation team recipients, `SuppressEscalationWhenAcknowledged`, one successful escalation per incident (mirror `HasNonSuppressedEscalationAttemptAsync`).
3. Resolve escalation recipients through the same team/contact expander used today (not a second SMTP sender).
4. Write unit tests that replace `AttentionIncidentNotificationOrchestratorTests` escalation cases.
5. Then execute NTF-C Phase 3–4: delete orchestrator, ledger writes/reads, cutover gate/flag, parity APIs/UI, add a **forward** EF migration to drop `AttentionIncidentNotifications` if unused, rename `ScheduleExpectationPlatformEventDualEmitter`.

Do **not** keep the orchestrator “just in case” after escalation ships on the platform path.

### What must remain until then

- `AttentionIncidentNotificationOrchestrator` and `SmtpAttentionNotificationSender`
- `IAttentionIncidentNotificationLedger` + table (writes only when the flag is off)
- `PlatformNotificationCutoverGate` / `Notifications:UsePlatformPipeline`
- Parity/cutover diagnostics (they are how operators detect the gap)
- Attention **incidents, routing, acknowledgement, reminders** (these stay forever; they are not the legacy delivery mechanism)

---

## Intentionally retained debt (unrelated to this stop)

- Digest delivery (`digestOnly`) — not implemented on either path.
- `AttentionNotifications:ExpectedRunMissing:Recipients` — legacy static list; platform uses subscriptions + routing.
- Cutover sign-off / preflight UI — meaningful only while two paths exist.

---

## Confirmation

- Attention **incidents and routing remain**. This stop is about delivery, not the Attention subsystem.
- There are still **two** production delivery architectures, selected by `UsePlatformPipeline`.
- Committed Management Host config already prefers the platform path; escalation on that path is already skipped today. Removal would make that skip permanent and unrecoverable via the flag.
