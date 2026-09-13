# S4 — Operator dashboard & Control Center attention surface

**Status:** Accepted (Epic S4)  
**Scope:** Deepen operator-facing Dashboard and Control Center without a layout editor, wizard framework, or full visual overhaul.

## Purpose by surface

| Surface | Operator purpose | Not for |
|---------|------------------|---------|
| **Dashboard** (`/dashboard`) | Cross-domain situational summary — health, attention, runs, schedules | Developer tasks, layout editing, raw diagnostics |
| **Control Center** (`/control/center`) | Live triage — what needs action now | Default landing for platform admin config |
| **Developer tasks** (`/config/developer`) | Deferred utilities and cutover diagnostics (S3) | Day-to-day operator monitoring |

## Conventions

1. **Summary first** — Dashboard and Control Center lead with status, counts, and next-action links.
2. **Attention ordering** — Dashboard cards sort by severity (Critical → Warning → Info → None) before provider order.
3. **Progressive disclosure** — Control targets, anomaly lists, degradation reasons, and technical IDs live in collapsed advanced sections.
4. **Honest empty states** — No integrations / no runs / no attention → explain what will appear and link to setup paths (`/integrations`, `/control/center`).
5. **Link to existing screens** — Drill-ins use known routes (`/integrations/{id}`, `/runs/{id}`, `/schedules/{id}`); no placeholder destinations.

## Data sources (reused APIs)

- `GET /api/dashboard/default` — composed `IDashboardCardProvider` cards (including new integration fleet card)
- `GET /api/control/center` — attention, recent activity, control targets
- `GET /api/control/runtime-health` — platform health snapshot (permission-gated)
- `GET /api/integrations/fleet` — integration health/readiness for fleet card

## Code references

- Dashboard UI: `Pages/Dashboard/DashboardHome.razor`
- Control Center UI: `Pages/Control/ControlCenter.razor`
- Integration fleet card: `IntegrationsDashboardCardProvider.cs`
- Display helpers: `OperatorAttentionUiHelpers.cs`
- S3 shell conventions: `S3-product-shell-conventions.md`

## Test gaps

Blazor route rendering is not covered in CI. S1 smoke tests cover linked API routes; UI behavior is covered by shared helper tests and dashboard provider unit tests.

## Out of scope (S4)

- Dashboard layout editor / widget framework
- Notification subscriptions or daily journals
- Full Control Center or Integration page decomposition
