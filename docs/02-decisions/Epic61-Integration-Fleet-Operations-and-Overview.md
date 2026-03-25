# Epic 61: Integration Fleet Operations and Overview

Status: Accepted  
Date: 2026-03-25

## Context

Single-integration flows (bindings, verification, readiness, schedules, procedures, activation) are in place. Operators still need a **fleet-level** view to scan many integrations without opening each one.

## Decision

1. Add a **fleet projection** that composes existing facts: integration operational state, `IntegrationReadinessProjection` (blocking vs advisory counts and issue codes), and derived **attention flags**—no parallel health subsystem.
2. **Core signals**
   - Operational state: `Active`, `Inactive`, `Retired` (domain enum as string).
   - Readiness: `IsRunnable`, `BlockingIssueCount`, `AdvisoryIssueCount`, issue list (same semantics as `GET .../readiness`).
   - Verification concerns: derived only from readiness issue codes (`connection_verification_failed`, `connection_unverified`, `connection_verification_unavailable`).
3. **Attention model** (evidence-backed booleans + stable tokens for filters)
   - `inactive`, `retired`: from operational state.
   - `blocking`: any blocking readiness issue **other than** `integration_not_active` (configuration/safety blockers while distinguishing “paused” from “broken”).
   - `advisory_only`: integration is runnable (`IsRunnable`) and has at least one advisory issue.
   - `verification_failed`, `verification_unverified`, `verification_unavailable`: from matching issue codes.
4. **Quick actions** on the overview: activate, deactivate (existing APIs), navigate to `/integrations/{id}` for detail/remediation.
5. **API**: `GET /api/integrations/fleet` with optional query filters and sort; plain `GET /api/integrations` unchanged.

## Non-goals

- NOC-style monitoring, streaming dashboards, or analytics.
- Synthetic health scores or vague rollup status.
- Run-based “recent failure” on each row (requires run-query extensions; follow-up).

## Follow-ups

- Filter runs by `IntegrationId` (and cap) for optional fleet attention.
- Batch-load bindings for all integrations in one query.
- Pagination on `/fleet`.
