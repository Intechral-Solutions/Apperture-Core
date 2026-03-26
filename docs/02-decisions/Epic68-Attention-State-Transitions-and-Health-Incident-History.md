# Epic 68 — Attention state transitions and health incident history

## Status

Accepted

## Context

Fleet and schedule diagnostics already compute schedule expectation truth (including `MissingExpectedRun`), but that signal was ephemeral unless an operator had the UI open. We needed durable incidents for `expected_run_missing`, driven by the same evaluation inputs as fleet/readiness, with a control-plane observation path and operator acknowledgement.

## Decision

- Persist `AttentionIncidents` with `Active` / `Resolved`, timestamps, optional expected-fire anchors, and acknowledgement fields, with a filtered unique index enforcing at most one active row per integration and condition key.
- Introduce `ScheduleExpectationSnapshot` and `IIntegrationReadinessService.EvaluateScheduleExpectationAsync` / `EvaluateScheduleExpectationForBackgroundAsync` so fleet, diagnostics, and the observer share one construction path (management auth vs repository-only load).
- Implement `IAttentionIncidentTracker` in SQL persistence to open, update, and resolve incidents from snapshots; resolve when evaluation status is no longer `MissingExpectedRun`.
- Run `AttentionIncidentObservationHostedService` on a configurable interval in the management host, enumerating integrations via `IIntegrationRepository.GetVisibleAsync` for the instance scope and evaluating with the background path.
- Extend schedule diagnostics API/DTOs with bounded incident history; add `POST .../attention-incidents/{id}/acknowledge` using the current actor id. Surface history and acknowledge in the schedule diagnostics UI; add a small fleet hint when an active incident exists.

## Consequences

### Positive

- Incident state survives UI sessions and aligns with the same schedule expectation semantics as fleet rows.
- Observers do not depend on UI traffic; acknowledgement is explicit and auditable.

### Negative

- Extra periodic load on the control plane (mitigated by interval configuration and batch-friendly integration enumeration).

### Neutral

- Concurrent hosts may race on first open; the tracker retries as an update when the unique active index rejects a duplicate insert.
