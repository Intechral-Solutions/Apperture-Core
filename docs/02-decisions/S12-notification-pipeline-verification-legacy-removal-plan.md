# S12 — Notification pipeline verification and legacy removal plan

Status: Accepted (legacy removal completed later by NTF-C, 2026-09-10)  
Date: 2026-07-03

## Context

Stabilization epics S1–S11 delivered platform notification email, HTML links, self-service preferences (S9), delivery diagnostics (S10), legacy parity (S8D), and cutover preflight (S11). A manual test enabling notification flags produced **no email**.

Root causes are multi-stage: natural `expected_run_missing` requires schedule silence; platform path requires SMTP, subscriptions/routing, and observation — not just feature flags. Operators lacked deterministic tools to isolate SMTP vs pipeline vs recipient gating.

## Decision

1. **Add admin-only diagnostics** (no new product notification features):
   - `POST /api/notification-diagnostics/smtp-test` — multipart HTML/plain SMTP verification using `Notifications:Email`.
   - `POST /api/notification-diagnostics/platform-pipeline-test` — synthetic `expected_run_missing` platform event through the canonical pipeline with **dry-run default** and optional live dispatch.

2. **Do not remove Epic 69 legacy code** in S12. Removal is blocked until operators verify platform pipeline end-to-end using the new tools and production cutover checklist.

3. **Legacy removal recommendation (pre-release target):** Option **3** — disable by default behind `Notifications:UsePlatformPipeline=true`, then remove in a follow-up epic after verification. Epic 69 orchestrator, ledger, parity UI, and `AttentionNotifications:ExpectedRunMissing` config merge should be deleted only after:
   - SMTP test passes in each environment.
   - Platform pipeline live test creates instance + outbox + successful send for a subscribed operator.
   - Natural or synthetic expected-run-missing verified in staging.
   - Escalation reimplemented on platform subscriptions (platform has no escalation kind today; Epic 69 escalation emails would be lost on removal).

## Manual test failure analysis

Enabling `PlatformEvents`, `Notifications:Evaluation/Instances/Delivery`, and `UsePlatformPipeline` in `Management.Host/appsettings.json` is **necessary but insufficient**:

| Stage | Typical gap |
|-------|-------------|
| Condition | `expected_run_missing` requires active integration + recurring schedule + 48h grace + no authorized runs in window — rarely true on demand |
| Observation | `AttentionIncidents:Observation:Enabled` must be true (enabled in Management.Host committed `appsettings.json`) |
| Platform event | Emitted only on incident **Opened** or **Resolved** transitions |
| Recipients | Platform uses **subscriptions + attention routing**, not `AttentionNotifications:ExpectedRunMissing:Recipients` |
| S9 opt-out | User global/team email opt-out skips delivery |
| SMTP | `Notifications:Email` host/from/credentials required; empty in docker-dev template |
| Links | `ShellUi:ManagementHostUrl` on **Management.Host** (e.g. `appsettings.Development.json` → `http://localhost:9001`) for absolute email action links — not the UI Client appsettings used for Entra sign-in/out |

With `UsePlatformPipeline=true`, Epic 69 orchestrator is **skipped** — legacy `AttentionNotifications` recipients do not apply.

## Required config for platform-pipeline email

**Management.Host** (`appsettings.Development.json` from `deploy/config/examples/appsettings.docker-dev.json.example`):

- `AttentionIncidents:Observation:Enabled=true`
- `PlatformEvents:Enabled=true`
- `Notifications:Evaluation/Instances/Delivery:Enabled=true`
- `Notifications:UsePlatformPipeline:Enabled=true`
- `Notifications:Email` — SmtpHost, FromAddress, credentials (password ref or env)
- Subscriptions on `platform.schedule.expected_run_missing` for target integration (or attention routing → operational team)
- Operator email enabled under `/me/notifications` (S9)

**Management.Host** (email absolute links):

- `ShellUi:ManagementHostUrl` — e.g. `http://localhost:9001` in `appsettings.Development.json` (operator UI origin for deep links in email). UI Client `ShellUi:ManagementHostUrl` is separate (Entra sign-in/out against the API).

**UI** (`deploy/config/ui/appsettings.json`):

- Used for client/API host URLs; do **not** treat as the email deep-link base unless intentionally co-configured.

## Operator test path (S12)

1. Config → Notifications → Diagnostics → review cutover preflight card.
2. **SMTP test** — send to your inbox; fix SMTP before pipeline test.
3. **Platform pipeline test (dry-run)** — confirm rule match + deliverable recipients.
4. Configure subscriptions/routing if dry-run shows blocked recipients.
5. **Platform pipeline test (live + dispatch immediately)** — confirm instance, outbox, sent.
6. Optional: wait for natural silence or use parity/sign-off runbook for production cutover.

## Legacy removal plan (follow-up epic)

**Remove:**

- `AttentionIncidentNotificationOrchestrator`, `SmtpAttentionNotificationSender`, `AttentionIncidentNotificationLedger` / EF entities
- `AttentionNotifications` config section and platform-settings keys for Epic 69
- Parity diagnostics comparing Epic 69 ledger vs platform
- `PlatformNotificationCutoverGate` (always platform)
- Schedule-diagnostics Epic 69 notification history UI sections

**Keep / migrate:**

- `expected_run_missing` attention incidents and observation
- Platform events, definitions, instances, outbox, dispatch
- Escalation → future platform subscription/routing model

**Tests/docs:** delete Epic 69–specific parity tests; update runbook to platform-only.

## Consequences

- Operators can deterministically verify SMTP and pipeline without waiting for schedule silence.
- S12 does not change production defaults or remove dual-path code.
- Next epic: **S13 — Epic 69 legacy removal** after staged verification sign-off.
