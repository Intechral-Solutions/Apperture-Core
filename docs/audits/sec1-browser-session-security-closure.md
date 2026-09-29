# SEC-1 — Browser session & request-forgery hardening — engineering closure

Date: 2026-09-29
Branch: `sec-1-browser-security` (not merged; nothing pushed by SEC-1C)
Design authority: [SEC-1 design gate](../01-architecture/transitions/security/sec1-browser-session-request-forgery-design-gate.md)
(rulings D1–D9, requirements REQ-1…REQ-19; as-built records §38–§48).
Decision record: [ADR-0045](../02-decisions/ADR-0045-browser-session-recovery-and-request-trust.md).

**Status: SEC-1 engineering closure is complete (SEC-1C).** Every binding requirement is implemented and evidenced, every
finding is resolved or recorded below as an accepted residual or an explicit deferral, and the browser-security CI gate is
remotely proven. The independent final whole-body security review passed with **no Critical and no Important findings**
(ruling: **SAFE WITH MINOR NON-BLOCKING FOLLOW-UP**). Engineering is not reopened. **SEC-1 is not Done.** Two steps remain
and none is claimed here:

1. operator smoke (the checklist at the end of this document; not executed);
2. merge to `master` (both required checks green at that time).

## What shipped

| Slice | Delivered | Gate |
|---|---|---|
| **SEC-1.0** | Opt-in Playwright harness: real Management.Host and Ui.Server, stub OIDC provider on `127.0.0.1` (cross-site), same-site and cross-site attacker pages, three-engine Origin probe, characterization tests for every browser finding | §0.1, §38 |
| **SEC-1A1** | One 401 interception point and one per-document recovery coordinator (single probe, one full-page sign-in navigation, no replay, loop breaker); account-unavailable and service-unavailable surfaces | §39 |
| **SEC-1A2a** | One shared `LocalReturnUrl` policy (client and host), fragment-preserving return targets, handled Entra remote failure, working and bounded auto-redirect to Microsoft | §40 |
| **SEC-1A2b** | Entra step-up continuation through the protected OIDC state, without reading the session cookie on the cross-site callback; external authority-claim strip (I-1) | §41, §42 |
| **SEC-1A3** | Background requests marked centrally and never renewing the sliding session | §43 |
| **SEC-1B1** | `BrowserRequestTrustMiddleware` (Rules U and G), the client platform header, the enroll exemption | §44 |
| **SEC-1B2** | Rule N; `POST /signout` (GET is 405); Entra directory searches as POST; GET-safety guard; `RemoteSignOutPath` disabled; Ui.Server's dead client registrations removed | §45 |
| **SEC-1B3** | Development-only `UiDev` CORS; public HTTPS `ManagementUiUrl` required outside Development/`Testing`; explicit `SameSite=Lax`; `Secure=Always` outside Development/`Testing`; clickjacking headers; `Management.Api` a class library; Ui.Server debug write removed; callback-under-`/api` fail-closed | §46 |
| **SEC-1B4** | Blocking `browser-security` GitHub Actions job (I-2); cross-platform harness and IntegrationTests portability fixes | §47 |
| **SEC-1C** | ADR-0045; this closure; ADR-0042/ADR-0044 addenda; auth, deployment and configuration docs; decisions index; transitions README; roadmap; one missing §29.7 guard | §48 |

## Requirement closure matrix

"Closed" means implemented and evidenced in the repository. Remote evidence is the Ubuntu CI workflow (§47.16). Where the
§29 matrix planned a browser (B) proof and the evidence is at another layer, the row says so.

| Id | Requirement (§36, abbreviated) | Slice | Code / test evidence | Remote / manual | Status |
|---|---|---|---|---|---|
| REQ-1 | One 401 interception point; one per-tab coordinator; simultaneous 401s → one probe, one navigation | 1A1 | `AuthRecoveryHandler`, `SessionRecoveryCoordinator`; `AuthRecoveryHandlerTests`, `SessionRecoveryCoordinatorTests`; `SessionExpiryBaselineTests` (F1, simultaneous 401 in three tabs); `SessionRecoveryBrowserTests` (Account Security fact counts only the coordinator probe, §47.15) | `browser-security` green | Closed |
| REQ-2 | Recovery requires `auth/me` = 401; outage/5xx never navigates to sign-in | 1A1 | `BootstrapStateBaselineTests.F4_…` (refused / 503 / 500); `SessionRecoveryBrowserTests` (probe 503 mid-session); coordinator unit tests | `browser-security` green | Closed |
| REQ-3 | 403 never recovers; `auth/me` 403 → terminal account-unavailable surface | 1A1 | `SessionRecoveryBrowserTests` (resource 403 never probes; probe 403); `BootstrapStateBaselineTests.F3_…` | `browser-security` green | Closed |
| REQ-4 | Auth/public routes never recover; loop breaker caps reloads | 1A1 | `AuthRecoveryExclusions`; public-route inertness; `SessionRecoveryBrowserTests` (failed sign-in 401 never recovers; loop breaker across real reloads) | `browser-security` green | Closed |
| REQ-5 | No automatic replay after 401 or sign-in | 1A1 | `SessionRecoveryBrowserTests` (a mutation that met 401 is sent once, before and after sign-in); `ControllerAuthorizationCoverageTests` (class-level `[Authorize]`, which the no-replay guarantee relies on) | `browser-security` green | Closed |
| REQ-6 | Recovery and sign-in end in full document loads; fresh identity and capabilities; watchers start once | 1A1, 1A2a, 1A3 | Coordinator `forceLoad` navigation (`TestDoubles` records force-loads); local sign-in `forceLoad` source guards (`ReturnTargetSourceGuardTests`, `Uix2ManagementBreadcrumbStabilizationTests`); Entra sign-in ends in a host 302; `NotificationToastWatcherTests` (`Start()` idempotent, one subscription) | — | Closed. **Layer note:** the §29.1 B rows "role granted while signed out is visible after sign-in" and "exactly one watcher cadence after recovery" have no dedicated browser test; they follow from the full document load (all WASM state discarded) and are covered by the unit tests above |
| REQ-7 | One shared `LocalReturnUrl`; path + query + fragment; `/control/center` fallback; proven in real browsers | 1A2a | `LocalReturnUrlTests`; `ReturnTargetSourceGuardTests` (no second validator); `ReturnTargetCorpusPolicyTests`; `OpenRedirectBaselineTests` (full corpus on Chromium; decisive cases on Chromium, Firefox, WebKit); `EntraReturnTargetBrowserTests` | `browser-security` green | Closed |
| REQ-8 | Auth errors never auto-redirect; stable panel with Try again; no raw host error document | 1A2a | `EntraRemoteFailure`, `LoginAutoRedirectPolicy`; `ManagementAuthEndpointsTests` (Development and Production, tampered/missing state); `OidcFlowBaselineTests` F6/F7 | Real cancelled Entra sign-in: operator smoke | Closed |
| REQ-9 | Step-up proven in the harness; continuation via protected OIDC properties; `SameSite` not weakened | 1A2b | `EntraStepUpCallbackTests`; `EntraStepUpSecurityGuardTests`; `StepUpCallbackBaselineTests` (Chromium, Firefox) | Real Entra and Safari: operator smoke | Closed in the harness |
| REQ-10 | Background requests marked centrally, never renew; timer guard | 1A3 | `BackgroundRequest`, `OnCheckSlidingExpiration`; `BackgroundRequestTests`, `BackgroundSessionRenewalTests`, `BackgroundPollerGuardTests`; `SessionRenewalBaselineTests` (F9 flipped) | `browser-security` green | Closed |
| REQ-11 | Rule U on every browser-facing unsafe `/api` request, anonymous included; 403, before authentication | 1B1 | `BrowserRequestTrustEvaluatorTests`; `BrowserRequestTrustHttpTests` (no action, audit or row on rejection); `RequestForgeryBaselineTests` (F11, three engines) | `build-and-test` and `browser-security` green | Closed |
| REQ-12 | Exemptions exactly `POST /api/enroll`, `/signin-oidc` (sign-in callback) and `/signout-callback-oidc` (signed-out callback); positive and negative tests. `RemoteSignOutPath` is disabled; `/signout-oidc` is not live and is not exempted | 1B1, 1B2, 1B3 | `BrowserRequestTrustGuardTests` (routed unsafe endpoints; the only attribute exemption is enroll); `BrowserRequestTrustHttpTests` (enroll exempt, invitation not; callbacks not rejected); callback-under-`/api` fail-closed (§46.5). `/signout-oidc` is not exempt because it is not live (§45.3 amendment) | `build-and-test` green | Closed |
| REQ-13 | G1/G2 POST; `/signout` POST (GET 405); GET classification frozen | 1B2 | `GetSafetyClassificationGuardTests`; `EntraDirectorySearchRequestForgeryTests`; `SignOutSurfaceGuardTests`; `SignOutFormSourceGuardTests`; `RequestForgeryBaselineTests` (D2 flipped, three engines) | `browser-security` green | Closed |
| REQ-14 | `UiDev` Development-only with exact origin; never any-origin with credentials | 1B3 | `UiDevCorsTests`; `BrowserRequestTrustGuardTests` (CORS position); `BrowserRequestTrustHttpTests` (`Testing` returns no allow headers) | `build-and-test` green | Closed |
| REQ-15 | Explicit `SameSite=Lax`; `Secure=Always` outside Development/`Testing`; HttpOnly and host-only kept | 1B3 | `ManagementCookiePolicyTests`; `EntraStepUpSecurityGuardTests` (no `SameSiteMode.None`) | HTTPS browser topology not exercised (harness is plain-http `Testing`); proven at host configuration | Closed |
| REQ-16 | `Management.Api` a class library (D7) | 1B3 | `OutputType=Library`, no `Program.cs`, no launch profile; D7 STOP not hit | — | Closed |
| REQ-17 | Ui.Server file write and agent-log instrumentation removed | 1B3 | `UiServerHostRegistrationGuardTests`; `DebugInstrumentationGuardTests` | — | Closed |
| REQ-18 | Outside Development, startup refuses a missing, loopback or non-HTTPS `ManagementUiUrl` | 1B3 | `ManagementUiRedirects.EnsurePublicOrigin`; `ManagementUiUrlValidationTests`. As built, the rule applies to every environment except Development and `Testing` | — | Closed |
| REQ-19 | Browser findings called defects only after SEC-1.0 evidence; closure cites the evidence | 1.0, 1C | §0.1 evidence register; this document | — | Closed by this document |

Other §29 rows checked during closure:

- §29.7 "`frame-ancestors`/`X-Frame-Options` on both hosts": Management.Host was asserted by `BrowserRequestTrustHttpTests`;
  Ui.Server had the headers but no test. SEC-1C adds `UiServerHostRegistrationGuardTests.The_ui_server_refuses_to_be_framed`.
- §29.1 "Logout → `/login`; Back does not show protected data": sign-out landing on `/login` with `auth/me` 401 afterwards
  is browser-proven (§45.8). Back after an in-app recovery is browser-proven (`HistoryBaselineTests`). Back after sign-out
  itself has no dedicated browser test; sign-out is a full document navigation, so no WASM state survives it.
- §29.3 M rows (real Entra step-up, cancelled real sign-in): operator smoke.

## Finding register — final state

| Id | Finding | Final state | Evidence |
|---|---|---|---|
| F1 | In-app expiry never started sign-in | **Resolved** (1A1) | §39.4 |
| F2 | Hard reload after expiry reaches `/login?returnUrl=` | Correct before and after (control); fragment now kept (1A2a) | §0.1, §40.3 |
| F3 | Valid cookie, unlinked user mounted the shell | **Resolved** (1A1) | §39.4 |
| F4 | Outage during boot sent the operator to sign-in | **Resolved** (1A1) | §39.4 |
| F5 | Open redirect through `returnUrl` | **Resolved** (1A2a) | §40.7 |
| F6 | Auto-redirect loop hazard (not reproduced; feature was dead) | **Resolved** (1A2a): works once; never on error, within 30 s, or on a bare visit | §40.6 |
| F7 | Raw error document after failed/cancelled Entra sign-in | **Resolved** (1A2a) | §40.5 |
| F8 | Step-up needed the Lax cookie on a cross-site callback | **Resolved in the harness** (1A2b), Chromium and Firefox; real Entra and Safari are operator smoke | §41 |
| F9 | Background polling renewed the sliding session | **Resolved** (1A3) | §43.4 |
| F10 | Toast watcher polled forever after 401; renewal | **Resolved**: 401 half (1A1), renewal half (1A3). The `RunDetail` 2 s poll was not exercised in a browser; it is inside a `BackgroundRequest` scope and stops with the shell | §39.4, §43 |
| F11 | No request-forgery layer | **Resolved** (1B1) | §44.7 |
| F12 | Writing GETs and `GET /signout` | **Resolved** (1B2) | §45 |
| F13 | `UiDev` CORS in every environment | **Resolved** (1B3) | §46.1 |
| F14 | Runnable `Management.Api` stub host | **Resolved** (1B3) | §46.4 |
| F15 | Ui.Server debug file write and agent-log calls | **Resolved** (1B3) | §46.4 |
| F16 | `Secure=SameAsRequest` everywhere; implicit SameSite | **Resolved** (1B3) | §46.3 |
| F17 | 33 `ManagementClient` methods collapse 401/403 to `null` | Harmless since the handler owns 401; the 403-vs-not-found UX is **deferred** (§37) | — |
| F18 | Nullable `[FromBody]` POST accepts an empty body | **Pinned** (1B1): untrusted is 403 before the action; trusted reaches the action | §44.7 |
| D2 | Cross-site navigation to `GET /signout` ended the session | **Resolved** (1B2); `/signout-oidc` is not live | §45.8 |
| I-1 | External `http://schemas.apperture/` claims trusted on an Entra principal | **Resolved** | §42 |
| I-2 | Browser suite gated off in normal CI | **Resolved** (1B4): blocking `browser-security` job, remotely proven | §47.16 |
| N1 | View selector `context=all` appears in captured return targets | **Closed — design clarification.** Not a security finding and not a remediable defect: `context=all` is benign on-origin application-local UI state. Preserving it in a safe, validated, same-origin return target is compatible with the safe-return-target model. Return-target validation governs destination safety; it does not strip benign on-origin UI state merely because it appears in the query string. §13 now records that. No security remediation and no product-code change. | §13 |
| N2 | Auto-redirect never fired on real entries | **Resolved** (1A2a). The bare-visit rule is an implementer product decision, cheap to relax | §40.6 |
| N3 | Raw resource-key error text on expiry | **Resolved** (1A1): the raw error is never painted before recovery | §39.3 |
| N4 | Client resolves any loopback UI origin to API port 9000 | **Not addressed; development-topology observation, not a SEC-1 requirement.** Production uses a relative `/api`. A UI on another loopback port that reaches `:9000` fails Rule U (Origin mismatch) on every mutation | `ManagementUiDevApiRouting` |
| N5 | `PUT /api/account/preferences` issued automatically on boot | **Resolved**: carries the platform header; classified operator-initiated (a navigation) | §38.5, §44.7 |
| N6 | CR/LF `returnUrl` produced an unhandled 500 | **Resolved** (1A2a) | §40.7 |

## Accepted residuals

These are consequences of approved rulings, not open defects.

- **Rule G / Rule N depend on Fetch Metadata.** Browsers without it get no refusal on `/api` GETs or the two auth navigation
  GETs. Rule U does not depend on it.
- **Auth navigations from a same-site sibling.** A sibling can navigate an operator to `/signin/step-up` (an audit append
  and a Microsoft prompt) or `/signin/link` (can only link the operator's own Microsoft identity). §45.9.
- **No Entra front-channel single sign-out.** `RemoteSignOutPath` is empty and the Lax cookie is not sent on Microsoft's
  cross-site iframe; sessions end through `POST /signout` or expiry. §45.3.
- **Step-up pending state is not bound to the initiating session.** Completing Microsoft after signing out yields a session
  for the same user, only after that user's own `tid` + `oid` proof. §41.3.
- **The `Testing` environment name selects the local HTTP topology** (no public-origin refusal, `SameAsRequest` cookies,
  no CORS). It is the integration and browser proof environment and must not be used for a real deployment. §46.2.
- **Idle sign-out after 12 h** of operator inactivity (D9).
- **Loop breaker in memory when `sessionStorage` is blocked** guards one document only. §39.8.
- **Return-target edge cases:** an error leg drops the target (Try again lands on the default route); a raw backslash in a
  query is refused; a database-configured OIDC callback path other than `/signin-oidc` is not in the reserved list (a
  meaningless on-origin route, not a loop); a challenge-leg failure (IdP metadata unreachable) is not turned into an error
  redirect. §40.8.
- **Playwright WebKit on Windows is not Safari**; it does not enforce SameSite and cannot complete an OIDC handshake over
  plain http. Rule U does not depend on SameSite. §38.4.
- **HTTPS browser topology is not exercised**; `Secure` is proven at host configuration. §38.4, §46.3.
- **Production host deployment templates do not set `ShellUi:ManagementUiUrl`.** A Production start without it fails
  closed by design (REQ-18). Adding the key to the templates is deferred public-origin work
  ([deferred-public-origin-auth-redirects.md](../03-operations/deferred-public-origin-auth-redirects.md), item 3).

Superseded residuals removed at closure: I-2 (closed by B4); "`GET /signout` still reachable from the account-unavailable
surface" (§40.8; removed in 1B2); "`/signout-oidc` exempt from Rule U" (§21; amended in §45.3 — the path is not live).

## Deferred (explicitly outside SEC-1)

From §37 and the slice records; none is an open SEC-1 failure.

- Periodic session revalidation / revocation of disabled users (D5).
- The 403→`null` collapse in `ManagementClient`; the deny-all capability retry cost; `DomainMenuService` never retrying; the
  `RunDetail` poll-restart race.
- Content-Security-Policy / XSS hardening beyond `frame-ancestors`.
- Public-origin work: forwarded headers, a multi-instance Data Protection key ring, the `ShellUi:ManagementHostUrl` naming
  collision, the `__Host-` cookie prefix, deploy templates, and a split-site production topology (its own CORS and
  `SameSite=None` design).
- True Entra front-channel single sign-out (needs a new design gate with the cross-site cookie architecture, §45.3).
- IntegrationTests environment-variable parallelization debt: `SqlServerTestConfigurationTests` mutates process environment
  while `ManagementHostGovernance` can run beside it under default xUnit settings (§44.7, §47.7). The counted local runs
  were serialized.
- GitHub server-side enforcement of the `master` ruleset, which needs a GitHub plan capability the organization does not
  currently have.
- IAM-8 custom role lifecycle (roadmap 7b) and navigation / cross-context grouping (roadmap 8).

Independent final review follow-ups (2026-09-29; **non-blocking**; not implemented here): Rule G inventory/guard
strengthening (MINOR-1); CI executed-browser-count enforcement or wait-loop changes (MINOR-5); Testing-environment
hardening (MINOR-6); CI image/action SHA pinning. These stay deferred with the §37 items above.

## Security architecture consistency review (SEC-1C, read-only)

An internal consistency pass across slice boundaries. It is not the independent final review. That review later completed
with only non-blocking findings (SAFE WITH MINOR NON-BLOCKING FOLLOW-UP); see status above.

| Boundary | Result |
|---|---|
| Rule U vs Rule N | Rule N is keyed on endpoint metadata on exactly three non-`/api` endpoints; a guard forbids Rule N under `/api`, so no `/api` mutation can trade the header rule for Rule N |
| Protocol callback exemptions | Exact paths from the live `OpenIdConnectOptions`, case-insensitive; a path under `/api` is never added; evaluated before the `/api` classification |
| Disabled `RemoteSignOutPath` | Emptied in a `PostConfigure` registered after every `IConfigureOptions` (whether or not Entra is enabled); the middleware adds it only when `HasValue`, so no constant fallback exists |
| `/signin-oidc`, `/signout-callback-oidc` | Both remain live protocol paths outside `/api` and exempt; `SignedOutCallbackPath` is independent of `RemoteSignOutPath` |
| A3 background marker vs request trust | The marker only declines cookie renewal; background traffic still carries `X-Apperture-Request` and is subject to Rule U |
| SameSite Lax vs logout | Sign-out is a same-site top-level form POST from the UI (cookie sent under Lax); Rule N supplies the Origin check; a cross-site form carries no cookie and is also 403 |
| Production cookie Secure | `Secure=Always` in every environment other than Development and `Testing`; the pending-MFA cookie uses the same environment rule |
| Test-only secret protectors vs production | Protectors live only in test assemblies; production `AddAppertureSqlSecrets()` is DPAPI on Windows and fail-closed elsewhere, with a startup round-trip validator; no environment-name or CI fallback |
| Test-only CORS vs product CORS | Product `UiDev` is registered and applied only in Development; the harness registers its own copy in `LiveManagementHost` |
| `Testing` vs production behavior | `Testing` shares the Production `OnRemoteFailure` branch and enforcement mode; it differs only in the local HTTP topology (accepted residual above) |
| UI origin vs `ManagementUiUrl` validation | Rule U, CORS and post-login redirects read one value (`ManagementUiRedirects.ResolveBaseUrl`); startup refuses an unusable value outside Development/`Testing` |
| Recovery probe vs recovery behavior | The probe is a credentials-only GET without the platform header or the recovery handler; Rule G does not need the header; the Account Security page's own refresh reports through `UnauthorizedDuringRefresh` into the same single decision (§47.15) |

No unresolved defect was found.

## Test-only infrastructure audit

| Check | Result |
|---|---|
| `BrowserHarnessSecretProtector` only in BrowserTests | Yes (`tests/Apperture.BrowserTests/Infrastructure`); no `src` reference |
| `IntegrationTestSecretProtector` only in IntegrationTests | Yes (`tests/Apperture.IntegrationTests/ManagementHost`); no `src` reference |
| No insecure production fallback | Yes: no `Testing`, Linux or CI branch in `AddAppertureSqlSecrets` |
| Unsupported real hosts still fail startup | Yes: `SecretProtectorStartupValidationHostedService` round-trips the final `ISecretProtector` and throws |
| Governance module enablement test-owned | Yes: `GovernanceHarnessModuleEnablementHostedService` in IntegrationTests |
| Test-only CORS test-owned | Yes: `LiveManagementHost` |
| B4 diagnostic logger removed from product code | Yes: `PlatformSqlStructureBrowser` takes resolver, factory and options only; no `Shape=` logging in `src` |
| Temporary diagnostic assertions | Governance status assertions that print the response body are kept as permanent failure evidence |
| Harness isolation | `HarnessIsolationGuardTests` (always on) |

## CI and branch ruleset

- `.github/workflows/ci.yml`: `build-and-test` (`dotnet test Apperture.slnx`, browser suite opt-in off) and
  `browser-security` (`APPERTURE_BROWSER_TESTS=1`, Chromium/Firefox/WebKit, 60 min, no `continue-on-error`). Unchanged
  since `a79a1ba`.
- The mechanism is proven: `browser-security` has failed the workflow when the suite failed (§47.6, §47.15) and passed when
  it was healthy.
- Both checks passed on Ubuntu on the final pull-request workflow of this branch (§47.16). The last **product-code**
  commit (product behavior) is `43079b3`. `04dbc94` changed documentation only. SEC-1C (`e28b9e6`, current HEAD at
  engineering closure) changed documentation and added one Ui.Server source-guard test; it did not change product
  behavior. That HEAD is not claimed as a remote-CI pass. The run id is not recorded in the repository.
- The `master` ruleset lists `build-and-test` and `browser-security` as required. GitHub does **not** enforce rulesets on
  this private organization repository under the current plan, so it is not blocking merges today.
- Merge policy is procedural: do not merge unless both checks are green. This is not unfinished SEC-1 engineering work.

## Evidence and validation

### Authoritative commands

| Suite | Command |
|---|---|
| Build | `dotnet build Apperture.slnx` |
| UnitTests | `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj` |
| ArchTests | `dotnet test tests/Apperture.ArchTests/Apperture.ArchTests.csproj` |
| Ui.Shared.Tests | `dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj` |
| Ui.Client.Tests | `dotnet test tests/Apperture.Management.Ui.Client.Tests/Apperture.Management.Ui.Client.Tests.csproj` |
| IntegrationTests | `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj` (test SQL container; run serialized locally, see the parallelization debt above) |
| BrowserTests | `$env:APPERTURE_BROWSER_TESTS="1"; dotnet test tests/Apperture.BrowserTests/Apperture.BrowserTests.csproj` |
| CI | `build-and-test` = `dotnet test Apperture.slnx -c Release --no-build`; `browser-security` = the BrowserTests command in Release on `ubuntu-latest` |

Add `--artifacts-path artifacts/<name>` when a running dev stack locks the default `bin/` folders.

### Latest evidence, by kind

| Kind | Latest | Source |
|---|---|---|
| Latest full local run | SEC-1B3, 2026-09-26: build 0 errors; UnitTests 3 199 passed, 3 failed (`Epic77UatDocumentationTests`, an artifacts-path layout effect; they pass under the default layout); ArchTests 38; Ui.Shared.Tests 1 395; Ui.Client.Tests 333; IntegrationTests 282/282 (serialized); BrowserTests 324/326, the two failures an isolation guard reacting to a temporary friend assembly, removed and re-run 4/4 | §46.6 |
| Local during B4 | Focused runs only (per remediation), recorded per run | §47.4–§47.15 |
| Latest remote with counts | Run 8 (`1098364`): Ui.Shared.Tests 1 395/1 395, IntegrationTests 283/289, `browser-security` passed. Run 9 (`3af711e`): `build-and-test` passed; `browser-security` 329/330 | §47.14, §47.15 |
| Final remote | Both checks passed after the last product-code commit `43079b3`; counts and run id not recorded. SEC-1C HEAD is later and is not a claimed remote-CI pass | §47.16 |
| SEC-1C | Documentation plus one source-guard fact; `UiServerHostRegistrationGuardTests` 11 passed, 0 failed (local, Windows) | §48 |

The gate does not require a new full local run for SEC-1C: §32 assigns documentation and a closure audit with evidence, and
REQ-19 requires citing the evidence. The full suites were not re-run for this documentation change.

## Operator smoke checklist (pending; not executed)

Consolidated from the committed manual items (§29.3 M, §38.6, §41.6) and the independent final review's required
manual coverage. Run against a rebuilt, restarted stack on this branch. Do not inspect database rows or source. Record
pass/fail and a one-line observation per step.

The application/session cookie is named `.AspNetCore.Cookies`. That is **not** the OIDC `state` parameter and not the
short-lived nonce/correlation cookies. The gate does not define a numeric cookie-size threshold: for an Entra session
(saved tokens) the size after step-up must stay comparable to the size before (the framework re-issues the session with
its tokens).

### Prerequisites — which steps need what

| Tag | Needed |
|---|---|
| All | Rebuilt, restarted Management UI and Management.Host from this branch |
| **Real Entra** | Test tenant with an authentication context (Conditional Access) on the active registration; a Microsoft account linked to an Apperture user |
| **Local credentials** | An Apperture user who can sign in with username/password **and** has a linked Microsoft account |
| **Safari** | macOS Safari and iOS Safari |
| **DevTools** | Browser developer tools: Application/Storage → Cookies, and Network |
| **HTTPS Staging** | A safe Staging or production-like HTTPS deployment. Do **not** disrupt a live production instance for the configuration-failure check |

**How to read the session cookie (DevTools).** Chrome/Edge: Application → Cookies → the Management host origin →
`.AspNetCore.Cookies` (note Size, or Value length). Firefox: Storage → Cookies. Network: the `Set-Cookie` header on
sign-in or step-up. **How to read `auth/me`.** Network → `GET /api/auth/me` JSON. Use `signInSource` (`Local` or `Entra`)
and `hasRecentStrongAuthentication`. Do not inspect tokens or claims beyond that JSON.

A safe ordinary deep-link for recovery/return tests: `/runs?status=failed#notes` (query **and** fragment; not an
auth/protocol path). If that list is empty, `/connections?context=all#section` is equally valid.

### 1. Expired session, in app

- **Start:** signed in (local or Entra). Open `/runs?status=failed#notes` (or another ordinary list route with query and fragment).
- **Action:** let the session expire, or delete `.AspNetCore.Cookies`, then click Refresh on that page.
- **Expect:** exactly one navigation to `/login` with "Your session has expired". After sign-in, land on the **same**
  path, query, and fragment.
- **Record:** starting URL; landing URL after sign-in (including `#fragment`).

### 2. Application sign-out (local or mixed UI surfaces)

- **Start:** signed in.
- **Action:** sign out from (a) the account menu, (b) Account › Security, (c) the access-request page. Use the normal
  Sign out control (a form POST to `/signout`). After each, use the browser Back button.
- **Expect:** each attempt lands on `/login`. Back does not show protected content.
- **Record:** which three surfaces were used; Back result.

### 3. Real Entra sign-in, deep link, cancel, and OIDC error wording — **Real Entra**

- **Start:** signed out. Open a deep link that is an ordinary application route with **both** query and fragment, for
  example `/runs?status=failed#notes`.
- **Action:** Sign in with Microsoft and complete the real Microsoft flow. Repeat from the same kind of deep link and
  **cancel** at Microsoft. If a non-cancel failure is easy (close the Microsoft tab mid-flow), do that once too.
- **Expect:** successful sign-in lands on that same path + query + fragment. Cancel lands on `/login?error=cancelled`
  with the house panel "Sign-in was cancelled" and **Try again**. Any other handled failure lands on
  `/login?error=oidc` with "Sign-in with Microsoft didn't complete." The URL carries **only** that fixed code: **no
  `detail` query parameter**, no Microsoft/provider error text in the address bar or on the page, no raw host error
  document, no auto-redirect loop.
- **Record:** success landing URL (query + fragment); cancel/failure URL (confirm `error=cancelled` or `error=oidc` and
  the absence of `detail=`).

### 4. Protected-state step-up from a LOCAL session — **Real Entra**, **Local credentials**, **DevTools**

- **Start:** sign in with **local username/password** on an account that has a linked Microsoft identity. Confirm
  `GET /api/auth/me` `signInSource` is `Local`. Open Account (`/account`). Secured account is locked. In DevTools, note
  the `.AspNetCore.Cookies` size.
- **Action:** choose **Verify with Microsoft**. Complete the real Microsoft flow and the Conditional Access challenge.
  After return, **do the protected action** (save or change contact email, or change the local password if that control
  is available). Do not stop at the unlocked overlay. Cancel once at Microsoft as a negative check.
- **Expect:** return to Account with success (`steppedUp=1`, not `error=user_not_found`). You remain on a **local**
  session (`signInSource` still `Local`; you are not now in a Microsoft-only session). `hasRecentStrongAuthentication`
  is true **and** the protected save/change succeeds. Note the cookie size (for comparison with the Entra-with-tokens
  cookie in step 5). Cancel: bounded error, session still local and intact.
- **Record:** before/after `signInSource`; before/after cookie size; which protected action succeeded.

### 5. Protected-state step-up from an ENTRA session — **Real Entra**, **DevTools**

- **Start:** sign out, then sign in **with Microsoft** (Entra session). Confirm `signInSource` is `Entra`. Open Account.
  Note `.AspNetCore.Cookies` size **before** step-up (this cookie includes saved tokens).
- **Action:** **Verify with Microsoft**, complete the real Microsoft flow and CA. After return, **perform the protected
  action** (same as step 4). Optionally inspect `POST /signin-oidc`: it should carry OIDC flow cookies, not the old
  session cookie, and the response should `Set-Cookie` `.AspNetCore.Cookies` with `SameSite=Lax`. Once, use a Microsoft
  account linked to a **different** Apperture user (expect `identity_mismatch`).
- **Expect:** success return to Account; `signInSource` still `Entra`; `hasRecentStrongAuthentication` true; the
  protected action succeeds. After step-up the session-cookie size stays **comparable to the pre-step-up Entra cookie**
  (framework re-issues the session with its saved tokens; no numeric threshold). Wrong account: `identity_mismatch`,
  nothing changes.
- **Record:** before/after `signInSource`; before/after cookie size (this is §41.6 #8); protected action result.

### 6. Safari (macOS and iOS) — **Safari**, **Real Entra**

- **Start:** Safari on macOS, then iOS.
- **Action:** repeat step 3 (sign-in + cancel) and steps 4–5 (step-up from both session kinds, including the protected
  action). From another origin, submit a cross-site form POST toward a Management `/api` mutation.
- **Expect:** the same observable results as Chromium/Edge/Firefox. The cross-site form POST does not mutate. Step-up
  must work whether or not Safari sends the Lax cookie on the callback.
- **Record:** engines/devices; any difference from desktop Chromium.

### 7. `RunDetail` poll

- **Start:** signed in; open a run that can Poll.
- **Action:** enable Poll, then expire the session (or delete the cookie).
- **Expect:** one recovery navigation, not a 2-second 401 storm in Network.
- **Record:** whether a 401 storm appeared.

### 8. Ordinary mutations

- **Start:** signed in on the Management UI origin.
- **Action:** create and retire a Connection; mark a notification read; change appearance.
- **Expect:** all succeed. No "Request blocked".
- **Record:** which three mutations were used.

### 9. Real Entra logout — **Real Entra**

- **Start:** a real **Entra-authenticated** session (signed in with Microsoft).
- **Action:** sign out through the normal UI (account menu Sign out → `POST /signout`). Watch Network. After landing,
  press Back.
- **Expect:** Microsoft end-session runs (navigation to the Microsoft logout URL). Return through
  `/signout-callback-oidc`. Land at `/login`. Back does not show protected content.
- **Record:** that end-session, `/signout-callback-oidc`, `/login`, and Back were observed.

### 10. Disabled front-channel endpoint — **Real Entra** (or any signed-in session on the deployed host)

- **Start:** sign in again (any session kind). Do **not** re-enable front-channel logout.
- **Action:** in the same browser, navigate to `{deployed-host}/signout-oidc?sid=test&iss=https://login.microsoftonline.com/test/v2.0`
  (the historical path; it is not live). Then refresh a protected page.
- **Expect:** the host answers not-found (404). The existing Apperture session remains active.
- **Record:** status shown; still signed in afterwards.

### 11. HTTPS deployment-boundary — **HTTPS Staging**, **DevTools**

Use Staging or a disposable production-like HTTPS host. Do **not** change configuration on a live production instance.

1. **Session `Set-Cookie`.** Sign in and, in Network, inspect `.AspNetCore.Cookies` `Set-Cookie`. **Expect:** `Secure`,
   `HttpOnly`, and `SameSite=Lax` (or `samesite=lax`).
2. **No product CORS.** Inspect a normal `/api` response (not a Development UI). **Expect:** no
   `Access-Control-Allow-Origin` header.
3. **Clickjacking headers.** Inspect a Management.Host response and a Ui.Server (shell page) response. **Expect both:**
   `X-Frame-Options: DENY` and CSP containing `frame-ancestors 'none'`.
4. **Invalid `ManagementUiUrl` (safe test host only).** On a disposable Staging/test instance, set
   `ShellUi:ManagementUiUrl` to a public **HTTP** URL (or omit it / use loopback) and start Management.Host. **Expect:**
   startup refuses. Restore the valid HTTPS origin afterwards.
- **Record:** the four observations; which host was used for (4).

### 12. Optional — account-unavailable sign-out

Already covered in the browser suite. Optional here: from the "Your account isn't available in Apperture" surface,
choose Sign out (or Sign in with a different account) and confirm `/login`. Not required to close smoke.

## What remains after the independent final review

1. Operator smoke (above). Next lifecycle step; do not reopen SEC-1 engineering.
2. Merge, with `build-and-test` and `browser-security` green on the merged state.
