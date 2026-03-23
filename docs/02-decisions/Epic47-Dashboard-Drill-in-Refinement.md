# Epic 47 — Dashboard drill-in refinement and provider distribution

## Status

Accepted — implemented March 2026.

## Context

After Epic 46, the shared default operator dashboard had real cards with drill-ins, but some links were generic, attention severity on “Needs Attention” could understate risk (newest-first list), audit drill-in did not align the UI time window with the 24h card count, and growing provider code benefited from clearer file layout.

## Decision: provider ownership (hybrid)

- **Keep all `IDashboardCardProvider` implementations in `Apperture.ControlPlane.Security`**, co-located with governance/DI and `DashboardCardProviderAuth`, to avoid scattering permission checks and registration.
- **Organize types under** `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/Dashboard/` **with namespace `Apperture.ControlPlane.Security`** (no cross-assembly move).
- **Data access** remains on existing application/persistence services (`IRunQueryService`, `IRuntimeHealthQueryService`, `IAuditService`, etc.); providers stay thin orchestration layers.

## Drill-in matrix (after Epic 47)

| Card | Drill-in / label |
|------|------------------|
| Runtime Health | Contextual: blocked work → `/control/blocked-work`; schedule degraded/blocked in snapshot → `/schedules`; single distinct anomaly `RunId` → `/runs/{id}`; else `/control/center`. Label matches destination. |
| Needs Attention | `/control/center` (unchanged); **severity** uses max across items. |
| Recent Activity | `/control/center`; label “View control center”. |
| Blocked Work | `/control/blocked-work` (unchanged). |
| Executions (24h) | Failed &gt; 0: `/runs?status=failed&createdFrom=…`; else `/runs?createdFrom=…` only. Two queries: failed-filtered list + full window list for non-failed breakdown. |
| Schedules | `/schedules`; optional fact **Degraded or blocked (runtime)** when caller may read runtime health (same snapshot rules as health card). |
| Audit (24h) | `/control/audit?hours=24`; timeline reads `hours` query and sets range to 24h or 7d (`hours>=168` → 168). |
| Governance | `/config/governance/roles`, label “View roles”. |
| Authentication Posture | `/config/auth`; label “View authentication settings”. |

## Summary / query changes

- **Needs Attention:** `DashboardCardAttentionConvention.MaxAttentionSeverityToken` — worst of high/medium/low across items.
- **Executions:** Scoped `ListRunsAsync` for failures plus full-window list for other status counts.
- **Schedules:** Optional `IRuntimeHealthQueryService` snapshot when `RuntimeHealthRead` is allowed; counts `ScheduleHealthStatus` Degraded or Blocked; `FromScheduleDefinitionCountsAndHealth` for attention.
- **Audit:** Card href and timeline `hours` query aligned with `IAuditService.CountAsync(FromUtc: now-24h)`.

## Card ordering (coherence)

Approximate sort keys: Runtime Health `10`, Blocked Work `12`, Needs Attention `20`, Executions `25`, Recent Activity `30`, Schedules `32`, Audit `34`, Governance `36`, Auth `38`.

## Deferred / corrective pass

- **Schedules list page** still has no server-side filter for “degraded only”; drill-in remains full list. A future pass could add query params once list DTOs expose health fields without duplicating snapshot logic.
- **Runtime health** single-run drill-in only when exactly one distinct `RunId` appears in anomalies; multiple runs still land on control center.
- **Executions** still perform two DB-backed queries per dashboard load for users with run visibility (acceptable for now; revisit if dashboards are cached or traffic grows).

## Testing

- Unit tests in `DashboardExpansionTests` and `DashboardFoundationsTests`: max severity, runtime health navigation branch, audit/governance hrefs, executions dual-query mocks, schedules + health fact, composer ordering.

## Relation to non-goals

No personalization, layout editor, or widget marketplace. Unauthorized cards remain omitted (null providers). Partial-load system card and failure isolation unchanged in `DashboardDefaultViewService`.
