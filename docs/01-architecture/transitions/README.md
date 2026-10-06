# Architecture transitions

Working records for **in-flight** architecture changes. These files are **not** the current contract, with one stated exception: where a completed body has no ADR yet, its design gate remains the contract until it is promoted. That currently applies to the **IAM-8** and **LFC-1** design gates below; their promotion to ADRs is scheduled as RC1 validation documentation work (see [rc1-program-gate.md](../rc1-program-gate.md) section 8).

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
| [identity-foundation/](identity-foundation/) | **IAM-9.0 design gate approved with amendments (2026-10-03).** [IAM-9.0 design gate](identity-foundation/iam9-design-gate.md) covers username, session and actor identity, user reference and user search. IAM-9.1 to 9.4 are merged to `master`; IAM-9.5 is awaiting pull request review and merge. IAM-9 is not complete. |
| [list-query-foundation/](list-query-foundation/) | **LST-1.0 design gate approved with amendments (2026-10-03).** [LST-1.0 design gate](list-query-foundation/lst1-design-gate.md) covers paging contracts, query lifecycle, the empty-state invariant, URL state, the Runs keyset design and `AsyncSearchSelect`. No implementation started; LST-1.1 awaits an explicit start instruction. |
| [custom-role-lifecycle/](custom-role-lifecycle/) | **IAM-8 complete and merge-ready (2026-09-30).** [IAM-8.0 design gate](custom-role-lifecycle/iam8-design-gate.md) is the lifecycle contract; closure is §20. **Merged to `master`** (PR #3, 2026-09-30). Not Context-scoped IAM. Grouping is not part of this work. This closeout does not select a next epic. |
| [connection-schedule-lifecycle/](connection-schedule-lifecycle/) | **LFC-1 complete; engineering merged to `master`.** [LFC-1.0 design gate](connection-schedule-lifecycle/lfc1-design-gate.md) is the lifecycle contract; closure is §27. **LFC-1.1** (disabled-schedule hard-delete), **LFC-1.2** (schedule detail delete UI), and **LFC-1.3** (retired connection copy) are implemented. |
| [nav-overhaul/](nav-overhaul/) | **NAV-1 complete.** Management UI **rail** overhaul shipped (Control Center / Integrations / Business Data / Config). This is **not** pre-release roadmap item 8 (cross-context grouping), which the post-RC1 Workspace direction now supersedes. |
| [application-contexts/](application-contexts/) | Evidence snapshot of current surfaces before any Application/Context planning |
| [security/](security/) | SEC-1 browser session & request-forgery hardening — every slice (SEC-1.0 through SEC-1C) delivered; engineering closure complete; independent final review passed with only non-blocking findings; **engineering merged to `master`.** Remaining environment-dependent operator smoke is pre-release/UAT, not claimed passed. Gate: [sec1-browser-session-request-forgery-design-gate.md](security/sec1-browser-session-request-forgery-design-gate.md); decision: [ADR-0045](../../02-decisions/ADR-0045-browser-session-recovery-and-request-trust.md); closure: [sec1-browser-session-security-closure.md](../../audits/sec1-browser-session-security-closure.md) |
| [components-services-catalog/](components-services-catalog/) | Components and Services design gates and planning note. **Implemented and on `master`** — see [ADR-0043](../../02-decisions/ADR-0043-component-catalog-read-model.md) and [ADR-0044](../../02-decisions/ADR-0044-connection-workspace-and-browse-authorization.md) |
| [resource-contexts/](resource-contexts/) | ResourceContext ownership: CTX-1…CTX-3B **complete and merged** (see [ctx3b-closure.md](resource-contexts/ctx3b-closure.md)); HSK-A/HSK-B **complete in current code**; Application Resource Explorer (HSK-D) **complete** (see [hsk-d-application-resources.md](resource-contexts/hsk-d-application-resources.md)); HSK-C complete (ADR-0041) |
| [ntf-c-legacy-removal/](ntf-c-legacy-removal/) | **Completed.** One platform notification pipeline; see [closure-report.md](ntf-c-legacy-removal/closure-report.md) |
