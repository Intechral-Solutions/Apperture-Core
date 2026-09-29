# Architecture transitions

Working records for **in-flight** architecture changes. These files are **not** the current contract.

Use this folder when a subsystem is about to be restructured and you need a snapshot, a proposed target, and notes on what will move — without putting that material on the architecture index as if it were already true.

| This folder | Architecture docs (`docs/01-architecture/*.md`) |
|-------------|------------------|
| What is about to change | What is true now |
| Disposable after the change ships | Keep and update |
| May be incomplete or contradictory | Canonical |

When the change is done: fold the outcome into an ADR and the real architecture docs, then delete or archive the transition set.

## Active

| Set | Status |
|-----|--------|
| [connection-schedule-lifecycle/](connection-schedule-lifecycle/) | **LFC-1 started.** [LFC-1.0 design gate](connection-schedule-lifecycle/lfc1-design-gate.md) is written. **LFC-1.1** (disabled-schedule hard-delete) is implemented. LFC-1.2 and LFC-1.3 have not started. IAM-8 and navigation / cross-context grouping are not part of this work. |
| [nav-overhaul/](nav-overhaul/) | **NAV-1 complete.** Management UI **rail** overhaul shipped (Control Center / Integrations / Business Data / Config). This is **not** the future **cross-context grouping** body (pre-release roadmap item 8), which is undesigned and not started. |
| [application-contexts/](application-contexts/) | Evidence snapshot of current surfaces before any Application/Context planning |
| [security/](security/) | SEC-1 browser session & request-forgery hardening — every slice (SEC-1.0 through SEC-1C) delivered; engineering closure complete; independent final review passed with only non-blocking findings; **engineering merged to `master`.** Remaining environment-dependent operator smoke is pre-release/UAT, not claimed passed. Gate: [sec1-browser-session-request-forgery-design-gate.md](security/sec1-browser-session-request-forgery-design-gate.md); decision: [ADR-0045](../../02-decisions/ADR-0045-browser-session-recovery-and-request-trust.md); closure: [sec1-browser-session-security-closure.md](../../audits/sec1-browser-session-security-closure.md) |
| [components-services-catalog/](components-services-catalog/) | Components and Services design gates and planning note. **Implemented and on `master`** — see [ADR-0043](../../02-decisions/ADR-0043-component-catalog-read-model.md) and [ADR-0044](../../02-decisions/ADR-0044-connection-workspace-and-browse-authorization.md) |
| [resource-contexts/](resource-contexts/) | ResourceContext ownership: CTX-1…CTX-3B **complete and merged** (see [ctx3b-closure.md](resource-contexts/ctx3b-closure.md)); HSK-A/HSK-B **complete in current code**; Application Resource Explorer (HSK-D) **complete** (see [hsk-d-application-resources.md](resource-contexts/hsk-d-application-resources.md)); HSK-C complete (ADR-0041) |
| [ntf-c-legacy-removal/](ntf-c-legacy-removal/) | **Completed.** One platform notification pipeline; see [closure-report.md](ntf-c-legacy-removal/closure-report.md) |
