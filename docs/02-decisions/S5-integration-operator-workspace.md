# S5 — Integration operator workspace conventions

**Status:** Accepted (Epic S5)  
**Scope:** Operator-first integration list and detail surfaces — not wizard framework, notification subscriptions, or full page decomposition.

## Principles

1. **Summary first** — Integration detail opens with health, connections, recent outcome, and primary action cards before configuration internals.
2. **Outcomes before config** — Recent activity, schedules, and procedures appear before bindings/settings editing.
3. **Progressive disclosure** — Fleet filters, attention tokens, procedure IDs, readiness codes, and full configuration live in collapsed or advanced sections.
4. **Honest empty states** — No integrations, runs, or schedules → explain what will appear and link to setup actions.
5. **Existing routes only** — Drill-ins use `/integrations/{id}`, `/runs`, `/schedules`, `/connections`, notification pages.

## Surface layout

| Area | Operator content | Advanced / deferred |
|------|------------------|---------------------|
| **Integrations list** | Fleet summary, health column, recent outcomes, human attention labels | Fleet filters panel, technical attention tokens |
| **Detail summary band** | State, readiness, connections, outcome/schedule lines, action cards | — |
| **Detail body** | Needs attention, recent activity, schedules, procedures | Configuration, identity, diagnostics |
| **Readiness** | Shown only when not runnable or issues exist | Issue codes in `<details>` |

## Data sources (no new persistence)

- `GET /api/integrations/fleet` — list health and operational signals
- `GET /api/integrations/{id}/detail` — readiness, bindings, schedules
- Run/audit/notification APIs via `EntityRecentActivityComposer`
- Procedure APIs — unchanged

## Code references

- Helpers: `IntegrationOperatorUiHelpers.cs`
- List: `IntegrationsList.razor`
- Detail: `IntegrationDetail.razor`, `IntegrationSummaryBand.razor`
- S3 shell: `S3-product-shell-conventions.md`
- S4 attention: `S4-operator-dashboard-control-center.md`

## Deferred

- Integration-scoped attention API (reuse fleet + activity today)
- URL query presets on list filters
- Blazor route rendering in CI (documented gap; static/helper tests used)
