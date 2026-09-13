# SDK-6D Current-State Operator UX Audit

**Date:** 2026-07-21  
**Status:** Audit complete — implementation proceeds from this baseline  
**Prerequisite:** SDK-6C closed (`docs/audits/sdk6c-closure.md`)

## Verdict

Operators can already open Integration Detail and see readiness issues, bindings, and procedure launchability, and can edit integration settings on a sibling page. The workflow is **not yet coherent**: declarations are API-only, remediation navigation is thin, schedule surfaces barely mention readiness, ContractType/IDs leak into primary UI, and secret lifecycle / verification storytelling is incomplete for an “understand → fix → verify → launch” loop.

## Surfaces traced

| Surface | What works today | Gap for SDK-6D |
| --- | --- | --- |
| Integration Detail | Readiness issues list, bindings CRUD, procedures with blocked codes, schedules section | No declaration-driven configuration overview; ContractType shown; weak remediation targets; no progressive disclosure for technical codes |
| Integration Settings | PlatformSettingRow editors, secret input kind, integration-scoped values | Not linked to readiness blockers; no “affected procedures” after clear; no confirmation when clearing a required secret |
| Connections pages | Create/list/detail, test-connect | Detached from integration requirement cards; create-compatible navigation from integration is weak |
| Procedure list/launch | Launchable flag + blocked reason codes via helpers | Reasons not mapped to setting/connection remediation routes; degraded vs blocked not operator-clear |
| Schedules list/detail | Disabled vs enabled | Almost no blocked-by-readiness pill or remediation; dispatch evidence not surfaced |
| Runs / audit | Some conflict messages | Need clearer BlockedByReadiness / rejection evidence without secrets |
| Control Center / fleet | Fleet readiness counts | Fine as secondary; not the configuration workspace |
| Management API | Settings, secrets, bindings, verify, readiness, **declarations**, **procedure readiness** | Client methods for declarations/procedure readiness missing; focused contract tests thin |
| Auth / audit | Existing patterns on settings/bindings | Confirm secret clear/replace audit + destructive confirmation |

## Findings (actionable)

1. **Missing navigation** — From a readiness issue to the exact setting row, secret editor, or connection requirement card inside the integration context.
2. **Internal identifiers** — `ContractType` (CLR FQNs), binding/connection GUIDs appear in primary binding UX.
3. **Hidden requirements** — Module declarations (`GET .../configuration/declarations`) exist but UI does not load them into an overview.
4. **Duplicate mental models** — Settings page vs Detail bindings vs Connections catalog without a single “configuration overview.”
5. **Weak empty states** — Modules with no required config (HelloWorld) still risk ceremony; modules with multiple blockers (Unisa) risk collapsing into “not ready.”
6. **Misleading readiness** — Verification advisory vs blocking not consistently explained; schedule blocked-by-readiness invisible while dispatch already enforces it.
7. **Secret workflow** — Presence/replace/clear exist at platform settings level; need clearer Configured / Not configured / Invalid reference states and clear-confirm when required.
8. **ApiPolling risk** — Connection-backed client with embedded fallback must not satisfy production readiness silently (SDK-6C clarification #3).
9. **API contract tests** — Declarations and procedure-readiness routes need focused controller tests.
10. **Accessibility** — Status pills need text + non-color cues; secret dialogs need labels/focus; validation announcements for typed fields.

## Implementation focus (this epic)

1. Unified configuration/readiness overview on Integration Detail (progressive disclosure).
2. Declaration-driven requirement cards with remediation actions (no CLR types as primary labels).
3. Harden Integration Settings for all value kinds + secret lifecycle + readiness refresh.
4. Schedule list/detail blocked-by-readiness pills + remediation links.
5. Management client + focused API contract tests; ApiPolling fallback enforcement.
6. Docs + Aspire smoke + closure.

## Non-goals

No SDK-6C contract redesign, no SMTP/platform-settings redesign, no scheduler semantics change, no module-contributed UI, no broad nav redesign.
