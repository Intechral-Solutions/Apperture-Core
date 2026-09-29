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
| [nav-overhaul/](nav-overhaul/) | Management UI navigation — overhaul about to start |
| [application-contexts/](application-contexts/) | Evidence snapshot of current surfaces before any Application/Context planning |
| [security/](security/) | SEC-1 browser session & request-forgery hardening — every slice (SEC-1.0 through SEC-1C) delivered; engineering closure complete; independent final review passed with only non-blocking findings. Pending operator smoke and merge. Gate: [sec1-browser-session-request-forgery-design-gate.md](security/sec1-browser-session-request-forgery-design-gate.md); decision: [ADR-0045](../../02-decisions/ADR-0045-browser-session-recovery-and-request-trust.md); closure: [sec1-browser-session-security-closure.md](../../audits/sec1-browser-session-security-closure.md) |
| [components-services-catalog/](components-services-catalog/) | Components and Services design gates and planning note. **Implemented** — see [ADR-0043](../../02-decisions/ADR-0043-component-catalog-read-model.md) and [ADR-0044](../../02-decisions/ADR-0044-connection-workspace-and-browse-authorization.md) |
| [resource-contexts/](resource-contexts/) | ResourceContext ownership: CTX-1…CTX-3B **complete and merged** (see [ctx3b-closure.md](resource-contexts/ctx3b-closure.md)); Application Resource Explorer (HSK-D) **complete** (see [hsk-d-application-resources.md](resource-contexts/hsk-d-application-resources.md)) |
| [ntf-c-legacy-removal/](ntf-c-legacy-removal/) | **Completed.** One platform notification pipeline; see [closure-report.md](ntf-c-legacy-removal/closure-report.md) |
