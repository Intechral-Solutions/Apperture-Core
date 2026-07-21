# SDK-6D Closure — Integration Configuration and Readiness Operator Experience

**Date:** 2026-07-21  
**Status:** Closed (with documented environment / live-system limitations)  
**Predecessor:** SDK-6C closed (`docs/audits/sdk6c-closure.md`, including clarification addendum)  
**UX audit:** `docs/audits/sdk6d-operator-ux-audit.md`

## Closure recommendation

**SDK-6D can be marked closed.** Operators can move from “integration is not ready” to understanding each blocker, resolving it in-platform, verifying connections, and seeing which procedures and schedules are executable — for the HelloWorld, FileImport, ApiPolling, Proof, and Unisa configuration patterns — without reading logs, database rows, or module source.

## Deliverable map

| # | Deliverable | Outcome |
| --- | --- | --- |
| 1 | UX audit | `docs/audits/sdk6d-operator-ux-audit.md` |
| 2 | Configuration overview | `IntegrationConfigurationOverview.razor` on Integration Detail |
| 3 | Typed settings UI | Integration Settings + PlatformSettingRow/Editor hardening |
| 4 | Secret lifecycle | Presence states, replace/clear, required-clear confirm, readiness refresh |
| 5 | Connection binding workflow | Declaration-driven requirement cards (capability labels, not CLR FQNs) |
| 6 | Verification workflow | Status labels + verify actions on requirement cards / existing test-connect |
| 7 | Readiness remediation | Grouped reasons via `IntegrationConfigurationOperatorUiHelpers` |
| 8 | Procedure launchability | Existing launch helpers + degraded/blocked messaging; deep links |
| 9 | Schedule readiness visibility | List/detail execution pills + BlockedByReadiness evidence summary |
| 10 | Run/audit evidence | Dispatch `BlockedByReadiness` surfaced on schedule detail; API rejection unchanged |
| 11 | Endpoint map | `docs/05-modules/integration-configuration-endpoint-map.md` |
| 12 | API contract tests | `ManagementHostIntegrationConfigurationHttpTests` + unit controller tests |
| 13 | Auth/audit | Existing patterns retained; secret clear confirm strengthens operator path |
| 14 | Security/redaction | Ui.Shared plaintext absence tests; ApiPolling fallback opt-in only |
| 15 | Accessibility | Status text on pills; confirm dialogs; setting anchors/labels |
| 16 | Module validation | Covered by helpers + HTTP + module readiness regressions |
| 17 | ApiPolling fallback | Explicit env/options opt-in; unit tests prove default block |
| 18 | Typed persistence | `docs/05-modules/integration-typed-settings-persistence.md` |
| 19 | Testing toolkit | No production SDK type expansion required; UI helpers tested in Ui.Shared |
| 20 | Architecture | `Sdk6dOperatorUiBoundaryDependencies` arch tests |
| 21 | Documentation | Operator guide + endpoint map + typed persistence + SDK-6C clarifications |
| 22 | Files changed | See git status / sections below |
| 23–24 | Tests + Aspire | Results below |
| 25 | External limits | Honest non-claims |
| 26 | Remaining → SDK-6E+ | Below |
| 27 | Close? | **Yes** |

## Operator workflow (what works)

1. Open Integration Detail → **Configuration & readiness** overview (counts, summaries, HelloWorld empty state).
2. Expand grouped blockers with remediation links to settings anchors or requirement cards.
3. Bind/change/unbind/verify from requirement cards using capability labels.
4. Edit settings / secrets on Integration Settings; required-secret clear confirms; readiness banner refreshes.
5. Procedure list shows launchability; schedules show Ready / Disabled / Blocked by configuration.
6. Server continues to enforce readiness on manual launch and scheduled dispatch.

## ApiPolling fallback enforcement

- Default: missing `DirectoryApi` binding throws — no silent embedded client.
- Designated opt-in: `APPERTURE_SAMPLE_APIPOLLING_ALLOW_EMBEDDED_FALLBACK=true` or `SampleApiPollingOptions.AllowEmbeddedFallback`.
- Readiness still requires the connection binding for production execution.

## Test results (2026-07-21)

| Suite | Result |
| --- | --- |
| Solution build | Succeeded (0 errors) |
| Unit tests | **1347 passed** |
| Architecture tests | **34 passed** |
| Management UI Shared | **385 passed** |
| Integration tests (SQL `127.0.0.1,14333`) | **152 passed** |
| End-to-end | **1 passed** |

## Aspire smoke

| Check | Result |
| --- | --- |
| AppHost restart | Started |
| Management API `/health` | **200 Healthy** |
| Management UI | **200** |
| WorkerHost | Running under AppHost |
| SQL | Stable Aspire host port **14333** (no ephemeral hard-coding) |

Interactive deep UI walkthrough of every module card is supported by automated UI helper + HTTP configuration tests; live CampusNexus/SFTP credentials were not used.

## External-system limitations

- No live Proof/Unisa CampusNexus or production SFTP certification in this pass.
- Test SFTP container required for Epic 59A SFTP governance paths when validating that scenario.

## Remaining work (SDK-6E or later)

1. Richer create-connection return deep-links from requirement cards (connector pre-filter UX polish).
2. Fleet Control Center configuration drill-downs beyond current pills.
3. Optional screenshot gallery if the repo adopts visual docs.
4. Live customer-system certification with safe credentials.
5. Further a11y automation (axe/playwright) if the platform standardizes on it.

## Non-goals honored

No SDK-6C architecture replacement, SMTP redesign, marketplace, codegen, module-contributed UI, scheduler redesign, WorkerHost lifecycle redesign, or fleet readiness caching.
