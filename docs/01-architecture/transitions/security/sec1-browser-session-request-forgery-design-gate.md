# SEC-1 — Browser Session & Request-Forgery Hardening — design gate

Date: 2026-09-24
Status: **Design gate complete and approved (2026-09-24). Rulings D1–D9 are settled (§33). Implementation is in progress:
SEC-1.0 (browser proof harness and baseline reproduction) is delivered, with its evidence in §0.1 and §38, SEC-1A1
(centralized expired-session recovery) is implemented and proven in browsers, recorded in §39, and SEC-1A2a (the shared
safe return-target policy, normal sign-in and failed/cancelled Entra sign-in) is implemented and proven in browsers, recorded
in §40, and SEC-1A2b (Entra step-up continuation across the cross-site callback, finding F8) is implemented and proven in Chromium and
Firefox, recorded in §41. An independent review of the A-series found one Important trust-boundary issue (I-1, external
Apperture-namespace claims on a validated Entra principal); that follow-up is recorded in §42. SEC-1A3 (background
requests do not renew the sliding session) is implemented and recorded in §43. SEC-1B1 (the platform browser
request-forgery policy) is implemented and recorded in §44. SEC-1B2 (verb and sign-out cleanup, Rule N) is implemented and
recorded in §45. SEC-1B3 (CORS, cookie Secure, host hygiene, Management.Api class library) is implemented and
recorded in §46. SEC-1B4 (the blocking CI browser-security job for I-2) is **complete** and recorded in §47: the
first GitHub Actions execution reached BrowserTests but failed at Management.Host startup (Windows-only DPAPI
default, §47.6), and a sequence of remediation runs (§47.6–§47.15) led to a final pull-request workflow in which
both `build-and-test` and `browser-security` passed on Ubuntu (§47.16). The `master` ruleset lists both as required
checks, but GitHub does not enforce it on this private repository under the current plan, so both checks are
procedurally required before merge (§47.16). SEC-1C (ADR-0045 and the closure audit) is **complete** and recorded in §48:
SEC-1 engineering closure is complete. SEC-1 is **not Done**: the independent final security review, operator smoke and
merge remain ([closure audit](../../../audits/sec1-browser-session-security-closure.md)).**
Audited HEAD: `0424877e985a80d8a956c395ce1f84e892059a8c` (`master`, clean tree).

Required inputs consumed: [pre-release-product-roadmap.md](../../../00-overview/pre-release-product-roadmap.md) (item 7a),
[ADR-0044](../../../02-decisions/ADR-0044-connection-workspace-and-browse-authorization.md),
[services-connection-workspace-closure.md](../../../audits/services-connection-workspace-closure.md),
[next-stage-platform-auth.md](../../next-stage-platform-auth.md), Epic 2 / 42 / 43 / 44 decision records, the IAM-1…IAM-5 (S2D-*) records,
[ADR-0042](../../../02-decisions/ADR-0042-notification-to-toast-channel-model.md) (Toast watcher),
[deferred-public-origin-auth-redirects.md](../../../03-operations/deferred-public-origin-auth-redirects.md),
[management-platform-configuration.md](../../../03-operations/management-platform-configuration.md), and the live code in
`Apperture.Management.Host`, `Apperture.Management.Api`, `Apperture.Management.Client`, `Apperture.Management.Ui.{Client,Server,Shared}`,
`Apperture.ControlPlane.Security` and `Apperture.AppHost`. Where docs and code disagree, **the code wins**; disagreements are recorded.

**Labels.** Every claim carries one of these labels:

- **Code fact** — traced in source at the audited HEAD.
- **Browser finding (pending SEC-1.0)** — a code-level finding whose effect depends on browser behavior. It is a *finding*, not a
  proven defect, until SEC-1.0 produces browser evidence.
- **Approved architecture** — the design as ruled in §33.
- **Requirement** — a numbered, binding implementation requirement (§36).
- **Deferred** — explicitly outside SEC-1 (§37).
- **Hypothesis rejected** — a design option that was considered and not chosen.

Brief discrepancies found while verifying the brief:
- **IAM-8 was absent from committed documentation.** It is now added to the roadmap as item 7b, after SEC-1 and before navigation /
  cross-context grouping (item 8). That keeps the existing item-8 references valid.
- The transitions README did not list this set. It is updated with this gate. The README's missing rows for `components-services-catalog/`
  and `resource-contexts/` are pre-existing and left for SEC-1C or a housekeeping pass. *(Added in SEC-1C, §48.)*
- `UiDev` CORS is not merely "present broadly": it is **applied unconditionally** (`app.UseCors("UiDev")`), with a hard-coded origin list.

---

## 0. Finding register

| ID | Finding | Class | Owning slice |
|---|---|---|---|
| F1 | After startup nothing re-checks the session and no UI code handles 401. In-app expiry shows "401 (Unauthorized)" text or empty states and never starts sign-in. | Code fact. The operator-visible symptom is a **browser finding (pending SEC-1.0)**. | 1A1 — **fixed (§39)** |
| F2 | Hard refresh with a missing, expired or invalid cookie *does* reach `/login?returnUrl=` (§4). | Code fact | — |
| F3 | Valid cookie but unknown/unlinked user: `auth/me` returns 403, the state becomes `ForbiddenUnlinked`, and the protected shell mounts over failing pages. | Code fact. The rendered result is a browser finding. | 1A1 — **fixed (§39)** |
| F4 | API unreachable, or 5xx during startup: state `Error` redirects to login. `GetAccessRequestSessionAsync` maps a 5xx to "signed out". | Code fact | 1A1 — **fixed (§39)** |
| F5 | The return-URL validator accepts `/\evil.example`, and the browser resolves it off-origin (**open redirect**). .NET `Uri` resolves it on-origin (verified in this session). | Reproduced in browsers by SEC-1.0 (§0.1) | 1A2a — **fixed (§40)** |
| F6 | With auto-redirect to Entra enabled, `/login?error=…` redirects to Entra again, which can loop. **Corrected by SEC-1.0:** no loop; the feature effectively never fired on a real entry (N2). | Code fact (logic); loop **not reproduced** | 1A2a — **corrected and fixed (§40)**: the feature works, once, and never on an error |
| F7 | In production, a cancelled or failed Entra sign-in (`OnRemoteFailure`) is unhandled, so the operator gets a raw 500 `problem+json` document on the API host. | Code fact; reproduced in browsers by SEC-1.0 | 1A2a — **fixed (§40)** |
| F8 | Step-up reads the ordinary SameSite=Lax session cookie inside Microsoft's cross-site `form_post` callback. | Code fact (dependency). Reproduced on Chromium and Firefox by SEC-1.0 | 1A2b — **fixed in Chromium and Firefox (§41)**; real Entra and Safari remain manual checks |
| F9 | Background polling renews the 12 h sliding cookie, so a passive tab never idles out. | Code fact (renewal eligibility). The observed lifetime is a browser finding. | 1A3 — **fixed (§43)** |
| F10 | The Toast watcher polls forever after a 401 (300 s backoff). `RunDetail` polling hits 401 every 2 s. | Code fact | 1A1 (401 half — **fixed, §39**) / 1A3 (renewal half — **fixed, §43**) |
| F11 | There is no request-forgery layer. 33 bodyless POSTs, one multipart POST and several nullable-body POSTs can be driven by a plain form from any same-site origin. | Code fact. Cookie attachment is a **browser finding**. | 1B1 — **fixed (§44)** |
| F12 | Two GETs write Apperture state (Entra directory users/groups). `GET /signout` ends the session (§18.1). | Code fact | 1B2 — **fixed (§45)** |
| F13 | `UiDev` credentialed CORS is applied in every environment. | Code fact | 1B3 — **fixed (§46)** |
| F14 | `Management.Api` has a standalone `Program.cs` with allow-any-origin CORS, no auth, and a launch profile on the API's port 9000. There is no supported standalone deployment. | Code fact | 1B3 — **fixed (§46)** |
| F15 | `Ui.Server` startup appends to the hard-coded file `f:\Code\Apperture\Apperture-Core\debug-4bd3d8.log` in every environment. Leftover agent-log script and `appertureShell.debugLog` calls (an undefined function) remain. | Code fact | 1B3 — **fixed (§46)** |
| F16 | Cookie `Secure` is `SameAsRequest` in every environment, and SameSite relies on the framework default. | Code fact | 1B3 — **fixed (§46)** |
| F17 | 33 `ManagementClient` methods collapse 401/403 (sometimes 404) to `null`. | Code fact. Harmless once the handler owns 401 (F1); the 403 UX part is deferred (§37). | — (deferred, §37) |
| F18 | Nullable `[FromBody]` POSTs accept an empty body (e.g. `procedures/{p}/launch` launches with defaults). | Pinned (§44): a trusted empty POST reaches the action; an untrusted one is 403 before it | 1B1 — **pinned** |
| I-1 | External `http://schemas.apperture/` claims on a validated Entra principal were trusted (A-series review). | Code fact | 1A2b follow-up — **fixed (§42)** |
| I-2 | The browser suite was gated off in normal CI (A-series review). | Code fact | 1B4 — **fixed (§47.16)** |

Final state of every finding, including the harness-discovered N1–N6, residuals and deferrals: [closure audit](../../../audits/sec1-browser-session-security-closure.md)
(§48). N1 (View selector `context=all` in return targets) is a closed design clarification, not a security finding: the value is benign on-origin application state and is preserved.

### 0.1 SEC-1.0 evidence register (2026-09-24)

SEC-1.0 built the opt-in browser proof harness ([tests/Apperture.BrowserTests](../../../../tests/Apperture.BrowserTests/README.md); §38)
and reproduced or characterized each browser finding **before any production change**. Nothing in `src/` changed. Every result below
is pinned by a characterization test whose doc comment names the slice that must invert it. Legend: **Reproduced** (observed as
predicted), **Not reproduced** (the prediction did not hold in a browser), **Partial**, **Unverified** (needs something the harness
cannot provide).

Suite size at delivery: 165 test cases (161 browser cases, rows of theories counted, plus 4 always-on isolation guards); the full
gated run takes about 11 minutes and was green (165/165) after an independent review whose Important findings were resolved.

Engines: Chromium 153.0.8010.12 (headless shell), Firefox 155.0, WebKit 26.6 (Playwright 1.63 builds; the WebKit build is
Playwright's Windows one, **not Safari**; see §38.4). "C" = Chromium only, "C/F" = Chromium and Firefox, "C/F/W" = all three.

| ID | SEC-1.0 result | Exact browser evidence | Test (in `SessionExpiryBaselineTests` unless noted) | Inverted by |
|---|---|---|---|---|
| F1 | **Reproduced** (C) | Session expired for real (server clock +13 h, real cookie handler). Clicking Refresh on `/connections`: `GET /api/connections → 401`; an error panel shows the raw text `net_http_message_not_success_statuscode_reason, 401, Unauthorized`; URL unchanged; no document request to `/login`; still nothing 3 s later. | `F1_in_app_reload_after_expiry_…` | 1A1 — **flipped** (now `…_recovers_with_one_probe_and_one_sign_in_navigation`) |
| F2 | **Reproduced** (control, C) | Reload after expiry → `/login?returnUrl=%2Fconnections%3Fstatus%3Dactive%26context%3Dall`. The `#fragment` is dropped. **New (N1):** `context=all` (the client's View selector) is already in the captured target. | `F2_hard_reload_…` | (must stay) / N1 → 1A2 |
| F3 | **Reproduced**, with nuance (C) | Valid cookie, principal unlinked: `GET /api/auth/me → 403`; URL stays `/connections`; **no** login navigation. The shell mounts with an inline banner ("You are signed in, but this identity is not linked to a platform user. … Account not linked") and the page shows "Not authorized". So a banner exists, but not the terminal surface D5 requires. | `BootstrapStateBaselineTests.F3_…` | 1A1 — **flipped** (terminal account-unavailable surface) |
| F4 | **Reproduced** (C), three outage shapes | `auth/me` connection refused / 503 / 500 with a valid session → every shape ends on `/login?returnUrl=%2Fconnections`, text "Sign in … Sign in to the Apperture management console."; no "unavailable"/Retry text. | `BootstrapStateBaselineTests.F4_…` | 1A1 — **flipped** (service-unavailable surface + Retry, all three shapes) |
| F5 | **Reproduced** (C full matrix; F/W the five decisive values) | 10 hostile values leave the origin in a real browser; the validator accepts all 10 (§38.2). `/\evil.example`, the TAB form and the CR LF form navigate to `http://evil.example/` on Chromium, Firefox **and** WebKit (signed-in `/login` bounce); the full 26-value matrix, and post-sign-in navigation, ran on Chromium. .NET `Uri` resolves every one on-origin. The host's `/signin` cannot leave the origin (authority is fixed) but turns them into a `//evil.example` **path** on the UI origin, and answers CR/LF with an unhandled 500. | `OpenRedirectBaselineTests` | 1A2a — **flipped** (§40): no hostile value escapes; all end on `/control/center` |
| F6 | **Not reproduced** as a loop (C) | Auto-redirect is ON, host returns `/login?error=tenant_mismatch`: **0** provider round trips on a direct load of `/login`, on the host's own error redirect and on a protected-route reload. `Login.razor` only auto-redirects from `OnAfterRenderAsync(firstRender)` when the sign-in options are already loaded; on every real entry they are not. It fires exactly once when entered in-app (link from `/login/local`), and the error return is a document load, so it does not fire again. The hazard in the code is real; the observed behavior is "auto-redirect mostly never fires". **New (N2):** the feature itself is effectively dead on real entries. | `OidcFlowBaselineTests.F6_…` | 1A2a — **flipped** (§40): auto-redirect now fires once after the options load; the `error` guard, the recent-attempt marker and the bare-visit rule are pinned |
| F7 | **Reproduced** (C) | Provider `access_denied` and `server_error` (Testing environment = the Production branch): the browser ends on `http://localhost:{api}/signin-oidc`, `POST … → 500`, `application/problem+json`, body `{"type":"https://httpstatuses.com/500","title":"An error occurred while processing your request.","status":500,"correlationId":…}`. One provider round trip, no loop. | `OidcFlowBaselineTests.F7_…` | 1A2a — **flipped** (§40): `error=cancelled` / `error=oidc`, stable panel, one 302 |
| F8 | **Reproduced** (C/F); **not measurable** on WebKit; **real Entra Unverified** | Cross-site `form_post` to `/signin-oidc` from `127.0.0.1` (`Sec-Fetch-Site: cross-site`, `Origin: http://127.0.0.1:{p}`, method POST). The callback carries **only** the OIDC nonce and correlation cookies; `.AspNetCore.Cookies` is absent, though it was sent on the step-up initiation moments earlier (a browser-initiated top-level navigation, `Sec-Fetch-Site: none`). `CompleteStepUpAsync` cannot authenticate the cookie scheme and step-up returns `/account/security?error=user_not_found`. | `StepUpCallbackBaselineTests` | 1A2b — **flipped** (§41): the callback still arrives cross-site without the session cookie, and step-up now succeeds |
| F9 | **Reproduced** (C) | With no operator input after load, the Toast poll at +7 h returned 200 **and re-issued the session cookie** (`Set-Cookie: .AspNetCore.Cookies … expires; samesite=lax`); the poll at +13 h was still 200 (and renewed again). Control: the original ticket replayed at +13 h → 401. | `SessionRenewalBaselineTests` | 1A3 |
| F10 | **Partial** | Toast watcher after expiry: next tick → `401`, no navigation, no toast (C). "Polls forever with backoff" is read from code; one 401 tick was observed. The `RunDetail` 2 s poll was **not exercised** (needs a Run; **Unverified**). | `F10_background_toast_poll_…` | 1A1 — **flipped** (401 half: one recovery, watcher stops) / 1A3 (renewal half, unchanged) |
| F11 | **Reproduced** for same-site; cross-site blocked by SameSite (C/F); **WebKit-Windows differs** | Same-site sibling (`localhost:{other port}`): bodyless form POST and simple credentialed fetch both arrive with the session cookie and **archive a disposable context** (`200`); response unreadable by CORS. Cross-site (`127.0.0.1`): delivered with `Origin` but **no cookie**, `401`, nothing changed (C/F). On Playwright's WebKit-Windows build the cookie **was** attached cross-site (§38.4). | `RequestForgeryBaselineTests` | 1B1 — **flipped (§44)**: same-site and cross-site unsafe requests are 403 and the context stays Active |
| — | **Reproduced** (simultaneous 401) | Three tabs meet the expired session together: 3 × `401`, 3 error panels, **0** `auth/me` probes, **0** navigations to sign-in. (A single tab yields one 401 per navigation: pages issue calls sequentially.) | `Simultaneous_401_baseline_…` | 1A1 — **flipped** (per tab: one probe, one sign-in navigation) |
| — | **Reproduced** (history) | Expired reload replaces the protected entry with `/login` (Back → previous document). After a local sign-in, Back returns to `/login/local`, which immediately bounces the signed-in operator forward again (Back is trapped). | `HistoryBaselineTests` | 1A1 (review) — **extended**: the reload case is unchanged; a third test pins the in-app-recovery history (§39). Local sign-in Back-bounce unchanged (1A2). |
| F13 | **Partial** | The existing `UiDev` policy refuses both cross-site and same-site-sibling preflights: a custom-header JSON POST is never delivered (C), and neither is PUT/PATCH/DELETE (C/F/W). Its being active outside Development is a code fact, not browser-provable here. | `RequestForgeryBaselineTests` | 1B3 |
| D2 | **Reproduced** (C) | A cross-site page's `location.href = '{api}/signout'`: `GET /signout`, `Sec-Fetch-Site: cross-site`, `Sec-Fetch-Mode: navigate`, session cookie sent, `302`; afterwards `GET /api/auth/me → 401` (the session really ended). | `RequestForgeryBaselineTests.D2_…` | 1B2 — **flipped (§45)**: `405`, nothing cleared, session still valid (C/F/W) |
| F15 | **Observed** (developer machine only) | Starting the real Ui.Server touched the hard-coded, git-ignored `debug-4bd3d8.log` at the repository root (file mtime), as predicted. The path is absolute and machine-specific, so there is no automated test. | (side effect of the harness) | 1B3 |
| F16 | **Corrected** | The session `Set-Cookie` is `.AspNetCore.Cookies=…; expires; path=/; samesite=lax; httponly` — SameSite=Lax is **explicit** (emitted by the cookie-authentication defaults), there is no `secure` and no `domain`. Chromium's "Lax+POST" grace period therefore does not apply. | `HarnessSmokeTests` | 1B3 (Secure) |
| F12, F14, F17, F18, D3, D4 | **Not exercised** | Code facts with no browser dimension (F12's Graph GETs need a Graph endpoint; D3/D4 are future behavior: GET-safety classification and no-replay); their slices (1B2, 1B3, 1A1) own the tests. | — | — |

Findings the audit did not predict, discovered by the harness: **N1** the View selector (`context=all`) leaks into captured return
targets and post-sign-in URLs (contradicts §13); **N2** "auto-redirect to Entra" never fires on a direct load; **N3** the raw
resource-key error text in F1; **N4** the client resolves *any* loopback UI origin to API port 9000 (`ManagementUiDevApiRouting`), so a
UI served on another port silently talks to a different developer stack; **N5** `PUT /api/account/preferences` is an unsafe request
issued automatically on every boot (the header handler and D9 classification must account for it); **N6** the host answers a CR/LF
`returnUrl` with an unhandled 500.

---

## 1. Executive recommendation (as approved)

SEC-1 is **two separable mechanisms** that meet at one seam: the single `HttpClient` pipeline every Management UI request already uses.

**Session recovery (client-owned; server stays pure):**
- `/api/**` keeps returning bare **401/403**.
- One pipeline handler reports 401s to one per-tab coordinator. The coordinator:
  - confirms the loss with one `GET auth/me`;
  - captures the local return target;
  - performs **one full-page navigation** to sign-in.
- It never replays requests. **403 never triggers it.**
- Availability failures are not authentication failures.
- An identity failure found by `auth/me` itself leads to a terminal "account unavailable" surface, not a loop.

**Return target:**
- Keep the existing `returnUrl` query and Entra `AuthenticationProperties.RedirectUri`.
- **One shared return-URL policy is authoritative on client and host.** It rejects the browser-normalization escapes .NET does not see.
- The Entra error loop, the unhandled remote failure and the step-up callback dependency are fixed inside SEC-1.

**Request forgery (server-owned; client automatic):** one middleware on Management.Host. For unsafe methods it requires:
- an **exact configured trusted `Origin`**;
- **one platform request header**, added centrally by the client pipeline;
- with `Sec-Fetch-Site: cross-site` rejected as defense in depth.

SameSite=Lax remains a further layer, not the primary mechanism. There are no antiforgery tokens. Exemptions are narrow, declared and
tested.

**GET safety:**
- **GETs must not mutate Apperture state.** The two writing Graph GETs become POST, and logout becomes POST.
- Genuinely read-only remote lookups stay GET.
- Cross-site `/api` GETs are also refused, as defense in depth only.

**Hardening:**
- `UiDev` becomes Development-only with an exact configured origin.
- `Secure` cookies outside Development; SameSite `Lax` set explicitly.
- Background requests are non-renewing, via one central mechanism.
- `Management.Api` becomes a class library.
- The debug filesystem write is removed.

**Proof first:** SEC-1.0 adds an opt-in Playwright harness and reproduces the browser findings before any production fix.

---

## 2. Current authentication topology — code fact

| Concern | Where | Fact |
|---|---|---|
| Who owns authentication | `Apperture.Management.Host` ([Program.cs](../../../../src/Apperture.ControlPlane/Apperture.Management.Host/Program.cs)) | `AddAppertureAuthentication` (L82), `UseAuthentication/UseAuthorization` (L88-89). The UI host has **no** auth middleware. |
| Schemes | [AuthenticationServiceCollectionExtensions.cs:32-68](../../../../src/Apperture.ControlPlane/Apperture.Management.Host/AuthenticationServiceCollectionExtensions.cs) | `Cookies` (application session); `Apperture.EntraAccessRequest` (20 min, non-sliding, limited principal); `OpenIdConnect` (always registered, with placeholder options when Entra is off). |
| Defaults | [EntraAuthenticationOptionsConfigurer.cs](../../../../src/Apperture.ControlPlane/Apperture.Management.Host/EntraAuthenticationOptionsConfigurer.cs) | Authenticate, sign-in, challenge and forbid all default to `Cookies`. API authorization failures never start OIDC. |
| API challenge semantics | [ManagementCookieAuthentication.cs](../../../../src/Apperture.ControlPlane/Apperture.Management.Host/ManagementCookieAuthentication.cs) | `/api/**`: challenge → **401**, forbid → **403**, no body, no redirect. Non-`/api`: 302. |
| Authorization registration | `Program.cs:83` | `AddAuthorization()` with no fallback, default or named policies. `[Authorize]` is set at controller class level. RBAC checks inside actions return `Forbid()` → 403. |
| Hosts | [AppHost.cs:45-57](../../../../src/Apperture.AppHost/AppHost.cs) | Management.Host on **:9000**, Ui.Server on **:9001**: two processes, two origins, **same site** (`localhost`). |
| Production topology | [deferred-public-origin-auth-redirects.md](../../../03-operations/deferred-public-origin-auth-redirects.md) | Documented as same-origin but **not implemented**. Only the dev split-host topology runs today. |
| Shell | [Ui.Server/Program.cs](../../../../src/Apperture.ControlPlane/Apperture.Management.Ui.Server/Program.cs), [_Host.cshtml](../../../../src/Apperture.ControlPlane/Apperture.Management.Ui.Server/Pages/_Host.cshtml) | Anonymous `MapFallbackToPage("/_Host")`, **no prerender**. The WASM shell always loads. |
| Client → API | [ManagementUiDevApiRouting.cs](../../../../src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/Options/ManagementUiDevApiRouting.cs), [IncludeCredentialsHandler.cs](../../../../src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Services/IncludeCredentialsHandler.cs) | Loopback + relative `ApiBaseUrl` resolves to `http://localhost:9000/api`. `credentials: include` on every request. Dev traffic is cross-origin, same-site, credentialed CORS. |
| Local login | `POST /api/auth/local/login` (+ `/local/mfa`, `/break-glass`), [AuthController.cs](../../../../src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/AuthController.cs) | JSON body, anonymous, `SignInAsync(Cookies)` with `IsPersistent = true`, `ExpiresUtc = now + 12h`. |
| Entra login | `GET /signin?returnUrl=`, [ManagementAuthEndpoints.cs](../../../../src/Apperture.ControlPlane/Apperture.Management.Host/ManagementAuthEndpoints.cs) | OIDC challenge (code flow, framework-default `form_post` response mode). `RedirectUri` is the UI URL built from `returnUrl`. |
| Login redirect initiation | [AuthRouteGate.razor](../../../../src/Apperture.ControlPlane/Apperture.Management.Ui.Client/AuthRouteGate.razor) L241-246, L283-291 | Only here, and only from `SessionStateService.State`. |
| Logout | `GET /signout` → [EntraSignOut.ExecuteAsync](../../../../src/Apperture.ControlPlane/Apperture.Management.Host/EntraSignOut.cs) | Clears both cookies, does federated end-session for Entra sessions, then 302 to UI `/login`. The UI reaches it by full navigation from three places. `POST /api/auth/local/logout` exists but is unused. |
| Current user / capabilities | `SessionStateService` (`GET auth/me`), `OperatorCapabilitiesService` (`GET control/capabilities`) | §9 |

## 3. Current session / cookie model — code fact

| Property | Application cookie | Access-request cookie | Pending-MFA cookie |
|---|---|---|---|
| Name | `.AspNetCore.Cookies` (default) | `.Apperture.EntraAccessRequest` | `apperture.mfa.pending` |
| SameSite | Lax (emitted explicitly by the framework's cookie defaults; not set by Apperture code — SEC-1.0 §0.1) | Lax (default) | Lax (explicit) |
| Secure | `SameAsRequest` everywhere | same | `Request.IsHttps` |
| HttpOnly / Domain / Path | true / host-only / `/` | same | same |
| Lifetime | 12 h **sliding**, **persistent** | 20 min absolute, no refresh | ticket lifetime |
| Ticket storage | In-cookie, Data Protection | same | opaque, server-redeemed |
| Revalidation | **None** | — | — |
| Data Protection keys | Framework default (per-user profile on Windows; ephemeral without a profile) | — | — |

The server cannot tell expired, missing, undecryptable and restart-invalidated cookies apart: all produce 401 on `/api/**`. A disabled local
credential does not end a live session (no revalidation; **deferred**, D5). Role revocation is live, because authorization is evaluated per
request.

## 4. Current hard-refresh behavior — code fact

Scenario: the operator is on `/connections/<id>`, the session is gone, and the operator reloads the document.

```
Browser            Ui.Server :9001            WASM (Ui.Client)                    Management.Host :9000
  |  GET /connections/<id>  |                        |                                      |
  |------------------------>| MapFallbackToPage(_Host) (anonymous, no prerender)            |
  |<------ 200 shell -------|                        |                                      |
  |  boot blazor.webassembly.js ------------------->|                                      |
  |                          |   AuthRouteGate: State=Unknown → "Loading session…" (page NOT mounted)
  |                          |   App.BootstrapAsync → SessionStateService.RefreshAsync      |
  |                          |                        |--- GET /api/auth/me (credentials) -->|
  |                          |                        |<------------- 401 -------------------|
  |                          |                        |--- GET /api/access-requests/session->|
  |                          |                        |<------------- 401 -------------------|
  |                          |   State = Unauthenticated → NavigateTo("/login?returnUrl=%2Fconnections%2F<id>", replace)
  |                          |   (fragment dropped) → Login.razor (chooser, or auto-redirect to Entra)
```

- Ui.Server answers first, anonymously. It **cannot** challenge: it has no authentication, a separate key ring and, in dev, a different
  origin from the cookie issuer.
- The page component never mounts before the session is known.
- **A true reload with a missing, expired or invalid cookie reaches login, for both providers.**

The operator's "refreshed and saw Unauthorized" report is therefore attributed to these candidates. **Browser findings, pending SEC-1.0
reproduction:**
1. **In-app refresh or navigation after expiry (F1).** Page refresh buttons, SPA navigation, `RunDetail` polling and a tab restored after
   sleep or back/forward cache all reuse the booted app and receive a raw 401.
2. **Valid cookie, unknown user (F3).** For example after a dev database reset. This is a genuine reload variant.
3. **Outage during boot (F4)** shows the opposite blur: an outage sends the operator to login.

| Variant | Reload today |
|---|---|
| Missing / expired / invalid cookie | → `/login?returnUrl=` ✔ |
| Restart with persisted keys | Session survives ✔ |
| Restart with ephemeral keys | → login ✔ |
| Disabled local credential | Session continues (deferred, D5) |
| User deleted or unlinked | Shell mounts in `ForbiddenUnlinked` ✘ (F3) |
| API down | Login redirect ✘ (F4) |

## 5. Current in-SPA expiry behavior — code fact

- `SessionStateService.RefreshAsync` runs at boot and after account actions only.
- `ManagementClient` has about 203 direct `_http.*` call sites and no central send. It uses four error patterns, and 33 methods map
  401/403 to `null`.
- No UI code handles 401. There is no `ErrorBoundary`.
- After expiry, simultaneous calls produce N inline errors and zero navigations. The Toast watcher keeps polling (F10).

## 6. Current local login flow — code fact

1. `/login` or `/login/local` reads `returnUrl` once-unescaped and validates it with `NormalizeReturnUrl`.
2. `POST auth/local/login` (JSON), then optionally `POST auth/local/mfa`.
3. On success: `NavigateTo(safe ?? "/control/center", forceLoad: true)`, a full document load.
4. On failure: inline message, no loop.
5. The fragment was already dropped at capture.

## 7. Current Entra flow — code fact

1. `/login` → full navigation to `{host}/signin?returnUrl=`.
2. `GET /signin`:
   - Already authenticated → 302 to the UI.
   - Otherwise → challenge. `RedirectUri` is carried inside the protected OIDC `state`, with correlation and nonce cookies at framework
     defaults.
3. `/signin-oidc` (`form_post`, a cross-site POST from Microsoft) → association → `EnsureSafePostAuthenticationRedirect` (authority must be the
   UI base, reserved paths are refused) → cookie sign-in → 302. A full document load follows.
4. Association failure → `/login?error=<code>`. The return target is dropped. An unknown identity with an active registration → access-request
   session → `/access-request`.
5. **`OnRemoteFailure`** handles step-up and Development only. **In production it is unhandled (F7).**
6. **`Login.razor` auto-redirects whenever `AutoRedirectToEntra && EntraSignInEnabled`, ignoring `error` (F6).**
7. **`CompleteStepUpAsync` calls `AuthenticateAsync(Cookies)` inside the `form_post` callback (F8).** A Lax cookie is not sent on a
   cross-site POST.

## 8. Current returnUrl behavior — code fact

| Stage | Code | Behavior |
|---|---|---|
| Capture | `AuthRouteGate.razor:290` | `EscapeDataString(path + query)`. The fragment is dropped. |
| Read | `LoginRouteHelper.GetReturnUrlRaw` | First `returnUrl` value, unescaped once. |
| Validate (client) | `LoginRouteHelper.NormalizeReturnUrl` | Rejects `://` and a leading `//`, and requires a leading `/`. **Allows `\`, control characters and reserved paths.** |
| Validate (host) | `ManagementUiRedirects.NormalizeRelativePath` | A duplicate of the client rules. |
| Reserved paths (host only) | `EntraSignInChallenge.IsReservedAuthPath` | `/signin*`, `/signin-oidc`, `/signout*`. **Not** `/login`, `/login/local` or `/access-request`. |
| Build (host) | `ManagementUiRedirects.Build` | Configured UI authority + path. The host can never redirect off-site. |
| Consume (client) | `AuthRouteGate.razor:256-258`; `LocalLoginFormSection.razor:137-138` (`forceLoad`) | Raw string passed to `NavigationManager` → browser URL parser (F5). |

## 9. Current capability / auth-state caching — code fact

- There is no `AuthenticationStateProvider`. `SessionStateService` is tab-lifetime.
- `OperatorCapabilitiesService` caches in memory. Deny-all is never cached, and there is no TTL.
- `DomainMenuService` caches menus and never retries after a failure.
- localStorage holds only the theme, `apperture.view.{userId}` and the panel width. **No auth data.**
- **Every successful sign-in ends in a full document load today**, so no pre-expiry authorization state survives a sign-in.

## 10. Current background / recurring requests — code fact (complete inventory)

| Request | Trigger | Recurring? | On 401 today |
|---|---|---|---|
| `NotificationToastWatcher` → `GET me/notifications/toast-delta` | Timer, 30 s, backoff to 300 s | **Yes** | Backs off, polls forever |
| `RunDetail.razor` poll → run reload | 2 s `PeriodicTimer` after the operator ticks "Poll" | **Yes** (automatic after opt-in) | Keeps polling every 2 s |
| `PersonalNotificationMenu` → unread count | First render, session change, menu open | No (event-driven) | Swallowed |
| `OperatorCapabilitiesService` retry | On demand, 3 attempts | No | → DenyAll |
| `DomainMenuService` → `GET menus` | Shell load | No | Defaults, never retried |

There is no SignalR, WebSocket, EventSource, JS `fetch` or health polling. Each authenticated request is eligible to renew the sliding
cookie (F9).

## 11. 401 vs 403 — approved architecture

**Code fact.** The server already separates them: challenge → 401, forbid → 403, `UnauthorizedAccessException` → 403, and an unlinked
principal on `auth/me` → 403. The client blurs them in several places:
- 33 null-mapping methods;
- `GetAccessRequestSessionAsync` maps 5xx to `null`;
- capabilities treat everything as `DenyAll`;
- `Error` → login;
- hand-written 403 exceptions let a 401 fall through.

**Approved architecture — three distinct outcomes:**

| Signal | Meaning | Behavior |
|---|---|---|
| 401 from a protected, non-excluded API call, **confirmed** by `auth/me` → 401 | Authentication lost | One recovery transition (§12) |
| 403 from an ordinary resource | Authenticated, not authorized | Existing forbidden conventions (`ForbiddenState`, inline 403). **Never** a login redirect, and never reaches the coordinator. |
| 403 from `auth/me` (at boot or as the recovery probe) | Authenticated principal does not map to a usable Apperture account | **Terminal "account unavailable" surface** (D5): house `PageMessage`, product copy "Your account isn't available in Apperture.", actions **Sign out** and **Sign in with a different account** (sign out, then `/login`). The protected shell does not mount. No automatic redirect, no loop. |
| Network failure, timeout or 5xx on `auth/me` / access-request probe | Availability, not authentication | **Service-unavailable surface** (D8): house `PageMessage`, "Apperture can't reach the management service right now.", a **Retry** action (re-runs `SessionStateService.RefreshAsync`). **Never** a login redirect. |

---

## 12. Session-recovery architecture — approved (D4)

**Components.** Class names are indicative; the implementation gate may refine them.

| Component | Project | Responsibility | Must not |
|---|---|---|---|
| `AuthRecoveryHandler : DelegatingHandler` | Ui.Client | Sees every response. On a 401 from a non-excluded request, calls `coordinator.ReportUnauthenticated()` and returns the **original** response. | Retry, replay, alter or swallow responses, inspect 403, or navigate itself. |
| `SessionRecoveryCoordinator` (scoped: one per tab/document) | Ui.Client | Single-flight: `Normal → Confirming → Recovering`. Probes `auth/me` once, captures the return target, marks the session signed out, performs **one** full-page navigation. Owns the loop breaker. | Replay requests, act on public routes, or hold credentials. |
| `SessionStateService` (existing) | Ui.Client | Gains `MarkSignedOut()` so the existing `Changed` event stops the watcher and updates `AuthRouteGate`. Gains distinct availability vs identity states (§11). | — |
| Server | Management.Host | No change: `/api/**` already returns bare 401/403. | Redirect XHR to HTML. |

**State machine:**

```
Normal     --401 (non-excluded)-->  Confirming   (first reporter only; concurrent reporters await the same task)
Confirming --auth/me 200-->         Normal       (endpoint-specific 401; the page's own error stands)
Confirming --auth/me 403-->         terminal "account unavailable" surface (§11); no navigation loop
Confirming --auth/me network/5xx--> service-unavailable surface (§11) with Retry; no navigation. (Amended in 1A1 by the task
                                    owner: the original text returned to Normal and let the page's own error stand. Either way it
                                    is availability, never authentication.)
Confirming --auth/me 401-->         Recovering: capture target → MarkSignedOut() →
                                    NavigateTo("/login?returnUrl=<target>&reason=expired", forceLoad: true)
Recovering --any 401-->             ignored
```

**Rules:**
- **Positive evidence only.** Recovery requires `auth/me` = 401 (D8). The probe goes through a separate credentials-only `HttpClient` that has
  no recovery handler, so it cannot recurse.
- **Exclusion set (never reports).** Excluded request paths:
  - `auth/me` (the boot path)
  - `auth/local/*`, `auth/break-glass`
  - `auth/sign-in-options`, `auth/forgot-password`, `auth/reset-password`, `auth/setup-password`, `auth/verify-contact-email`
  - `access-requests/*` (separate scheme)
  - `bootstrap/*`

  The coordinator is also inert while the current route is public (`LoginRouteHelper.IsPublicPath`), so sign-in routes cannot recurse.
- **No replay (D4).**
  - A request that received 401 is never re-sent, before or after sign-in. POST/PUT/PATCH/DELETE in particular are never replayed.
  - **Code fact:** a class-level `[Authorize]` rejects in authorization middleware before the action runs, so a session 401 proves the
    mutation did not execute.
  - The operator returns to the original page and re-submits deliberately.
- **Loop breaker.** A per-tab `sessionStorage` counter, best-effort and try/catch-wrapped. More than 3 recoveries in 60 s → stop
  auto-navigating and show "Your session has expired. Sign in to continue." with a manual **Sign in** button.
- **Why a full-page navigation (D4).** It gives a clean auth bootstrap, discards stale WASM state (capabilities, menus, View, toasts),
  restarts watchers from zero, and gives local and Entra sign-in one recovery model.
- **Pipeline order** (the single scoped `HttpClient` in [Ui.Client/Program.cs](../../../../src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Program.cs)):
  `IncludeCredentialsHandler → AuthRecoveryHandler → BrowserRequestHeadersHandler (§22) → HttpClientHandler`.

## 13. Return-target architecture — approved

Reuse the existing carriers:

| Leg | Carrier | Change |
|---|---|---|
| Expired page → `/login` | `returnUrl` query | Capture **path + query + fragment** from `Nav.Uri`, validated by the shared policy (§14), escaped once. |
| `/login` → local sign-in | Same query, held in the component | Validator swap only. |
| `/login` → Entra | `/signin?returnUrl=` → `AuthenticationProperties.RedirectUri`, protected inside OIDC `state` | Host validator swap. The fragment travels encoded inside the query value and is restored in the final `Location`. |
| Post-login landing | Client `forceLoad` (local) / host 302 (Entra) | Unchanged. Both are full document loads. |

- Invalid or missing target → `/control/center` (`LoginRouteHelper.DefaultPostLoginPath`).
- Error legs (`/login?error=…`) drop the target on purpose.
- Rejected carriers:
  - session/local storage (not visible to the host redirect, cross-tab bleed, races);
  - server temp state (no server session exists);
  - a new OIDC property (`RedirectUri` already is that property).
- Return targets are stable routes. Safe captured targets may preserve ordinary application-local query state, including the View selector's `context=all`. Return-target validation governs destination safety; it does not strip benign on-origin UI state merely because it appears in the query string. Query parameters that participate in authentication protocol handling, or that would otherwise make the destination unsafe, remain governed by their specific validation rules.

## 14. Open-redirect protection — approved (mandatory item)

**Browser finding (pending SEC-1.0), F5:**
- `/login?returnUrl=%2F%5Cevil.example` passes `NormalizeReturnUrl`.
- Blazor WASM hands the raw string to the browser, whose WHATWG parser treats `\` as `/` for http(s). An already-signed-in operator is
  bounced immediately (`AuthRouteGate.razor:256-258`). An operator who signs in is bounced after entering credentials
  (`LocalLoginFormSection.razor:137-138`).
- Embedded TAB/CR/LF, which browsers strip, behave the same way.
- .NET `Uri` resolves these on-origin, which is why a .NET-only test would pass.

**Approved architecture — one authoritative `LocalReturnUrl` policy in `Apperture.Management.Ui.Shared`.** It is used by the client *and*
by Management.Host (which references Ui.Shared through Management.Api). Both `NormalizeReturnUrl` and `NormalizeRelativePath` are deleted.
The policy is applied to the value **after exactly one decode**, and never decodes again. Rules:

1. Non-empty; ≤ 2 048 characters.
2. Begins with exactly one `/`. The second character is neither `/` nor `\`.
3. Contains no `\` anywhere, no ASCII control characters (U+0000–U+001F, U+007F) and no whitespace. It is never trimmed internally; it is
   rejected instead.
4. ~~Contains no percent-encoded `/`, `\`, `.` or control character in the **path** segment.~~ **Superseded in SEC-1A2a (§40.1)** by the
   SEC-1.0 evidence (§38.5): browsers keep `%2F`, `%5C` and `%2E` text inside a segment and never turn it into a separator, and
   legitimate routes carry them (`/connections/a%2Fb`, `?q=a%20b%2Fc`), so a blanket refusal was neither needed for origin safety nor
   correct. The implemented rule is positional (the first path segment is a plain route name) plus a browser-stable path form (no dot or
   empty segments).
5. No scheme and no authority: after resolving against a fixed sentinel origin, both must be unchanged. This check supplements the
   string rules and never replaces them.
6. The path (trailing `/` trimmed) is not a reserved auth surface: `/login`, `/login/local`, `/access-request`, `/signin`, `/signin/link`,
   `/signin/step-up`, `/signin-oidc`, `/signout`, `/signout-callback-oidc`, `/signout-oidc`. This also defeats nested `returnUrl` chains.
7. Otherwise the result is `null`, and the caller falls back to `/control/center`.

Host-side `EnsureSafePostAuthenticationRedirect` (UI authority equality) stays as defense in depth. A source guard asserts that no second
return-URL validator exists. **Browser tests are authoritative for this item** (§29.2): each rejection case must be shown to stay on-origin
in a real browser, not only in .NET.

## 15. Reauthentication / capability refresh — approved

- Recovery ends in a full load of `/login`, and sign-in ends in a full load of the target. Identity (`auth/me`) and capabilities are
  therefore fetched fresh by the normal boot.
- No stale auth or capability cache can be reused.
- Watchers restart **exactly once**: one scoped instance per document, and `Start()` is idempotent.
- May survive: theme, panel width, `apperture.view.{userId}`. These are user-keyed and re-validated at boot.
- A guard test asserts that every sign-in success path ends in a full document load (`forceLoad: true` locally, a host 302 for Entra). A
  future in-SPA sign-in therefore cannot silently keep stale capabilities.

## 16. Background polling / session renewal — approved (D9)

**Approved architecture — one central non-renewing marker.**
- **Client.** A `BackgroundRequest` ambient scope (an `AsyncLocal` flag, e.g. `using (BackgroundRequest.Begin()) { … }`) is opened by a
  poller around its timer-driven work. `BrowserRequestHeadersHandler` reads it and adds `X-Apperture-Background: 1`. **No
  `ManagementClient` signature changes, and no feature code touches cookies.**
- **Host.** `CookieAuthenticationEvents.OnCheckSlidingExpiration` sets `ShouldRenew = false` when that header is present.
  - The header can only *decline* renewal. It grants nothing, so forging it is harmless.
  - Background requests still authenticate normally.
- **Classification** (from the §10 inventory; every *timer-driven* request is background):

| Request | Classification |
|---|---|
| Toast watcher ticks | **Background (non-renewing)** |
| `RunDetail` poll ticks | **Background (non-renewing)**: automatic once enabled |
| Unread count on first render / session change | **Background (non-renewing)**: not operator-initiated |
| Unread count on menu open | Operator-initiated (renewing) |
| Boot requests (`auth/me`, capabilities, menus, View) | Operator-initiated: a navigation or reload |
| Everything triggered by operator input | Operator-initiated (renewing) |

- **Guard.** Every `PeriodicTimer`, `System.Threading.Timer`, `ITimer` or `TimeProvider.CreateTimer` use in Ui.Client that issues API calls
  must open a `BackgroundRequest` scope. The guard is a source test with an explicit list.
- **Effect.** A passive tab idles out after 12 h of no operator activity, and recovery (§12) handles the next request.

**After a 401, pollers need no auth code.** Their 401s flow through the handler. `MarkSignedOut()` stops the Toast watcher. The full-page
navigation ends every other poller. There is no redirect storm: at most one navigation per tab per recovery.

---

## 17. Current request-forgery posture — code fact

- There is no `AddAntiforgery`/`UseAntiforgery`, no validation attribute, no Origin or `Sec-Fetch-*` check and no custom-header rule in `src`.
- Protection is incidental:
  - The SameSite=Lax session cookie is withheld on cross-site subresource requests and cross-site POSTs, but sent on cross-site top-level GET
    navigations.
  - `[ApiController]` JSON-only `[FromBody]` binding (415 on form or text bodies).
  - `[Consumes("application/json")]` on the three SVC-7 browse actions only.
- Open today:
  - same-site origins (any `localhost:<port>` in dev, sibling subdomains in production) can drive every form-reachable endpoint;
  - side-effect GETs;
  - GET logout.

## 18. Side-effect endpoint inventory — code fact

Management.Host serves **265 controller actions** from `Apperture.Management.Api` (GET 132, POST 91, PUT 19, PATCH 9, DELETE 14). It also
serves four minimal GETs (`/signin`, `/signin/link`, `/signin/step-up`, `/signout`), three OIDC handler paths, and `/health` + `/alive`
(Development only). Ui.Server serves only the shell and static files. There are no webhook, API-key, bearer or rate-limited endpoints.

### 18.1 GET classification — approved ruling (D3)

**Durable rule: GET endpoints remain safe, i.e. non-mutating with respect to Apperture state.** Appending security-audit records and
materializing read models idempotently are observation, not mutation, and are permitted on GET. A genuinely read-only, idempotent remote
lookup may stay GET. The central cross-site GET rejection (§21 Rule G) is defense in depth and **is not a justification for keeping a
mutation behind GET**.

| # | Endpoint | Behavior (code fact) | Ruling |
|---|---|---|---|
| G1 | `GET /api/config/auth/entra-tenants/{id}/directory/users` | Graph call with stored secret **+ DB write** (`UpdateDirectoryDiscoveryOutcomeAsync`) | **Convert to POST** (JSON body carries the query) |
| G2 | `GET …/entra-tenants/{id}/directory/groups` | Same as G1 | **Convert to POST** |
| G3 | `GET /api/users/entra/directory` | Graph search; no Apperture write found | **Stays GET** (read-only remote lookup). 1B2 re-confirms there is no write. |
| G4 | `GET /api/config/platform-settings/{key}/options` | Option providers: `PlatformConnectorDefinitionsOptionProvider` (local read), `CampusNexusCampusesOptionProvider` (remote CampusNexus read; no writes found) | **Stays GET.** 1B2 re-confirms there is no write in any provider. |
| G5 | `GET /api/integrations/{integrationId}/settings/{key}/options` | Same providers as G4 | **Stays GET** |
| G6 | `GET /signin/step-up` | Security-audit append (`StepUpInitiated`) + OIDC challenge; no state change until the callback | **Stays GET** (auth navigation; audit is observation) |
| G7 | `GET /signout` | **Ends the session** | **Convert to POST** (D2); GET removed |
| G8 | `GET /signin/link` | OIDC challenge only; the binding happens at the callback | **Stays GET** (auth navigation) |
| G9a | `GET /api/auth/me` (unlinked), `GET /api/control/audit[/{id}]` (denied) | Security-audit append | **Stays GET** (observation) |
| G9b | `GET /api/me/notifications`, `…/unread-count` | `EnsureInboxRowsForUserAsync` backfills inbox rows for the reader | **Stays GET** as idempotent read-model materialization, *provided* 1B2 confirms it never changes read/unread/dismissed state or anything else another request can observe as an operator action. If it does, the backfill moves to the write path. |

A reflection/source guard freezes this list. Any new GET action that depends on a writing or remote-calling service fails the build until
it is classified here.

### 18.2 Unsafe methods, grouped by what a forged request could do (code fact)

| Class | Endpoints (method, bind) | Plain-form reachable? |
|---|---|---|
| IAM mutation | `POST /api/users` (J), `POST users/{id}/roles` (J), `DELETE users/{id}/roles/{roleId}`, `POST/DELETE users/{id}/identity-links`, `PATCH users/{id}/local-credential` (J), `PUT roles/{id}/permissions` (J), `POST users/entra` (J), `POST users/entra/access-requests/{id}/approve` (J), **`…/reject` (—)**, `POST bootstrap/first-admin` (anon, pre-init) | reject: **yes** |
| Secrets / credentials | `POST auth/local/change-password`, `reset-password`, `setup-password` (J), `POST users/{id}/local-credential/password` (J), `POST/PATCH/DELETE config/auth/entra-tenants[/{id}]`, `PUT config/platform-settings/{key}` (J), `POST/PATCH connections[/{id}]` (J) | JSON only |
| Auth configuration | **`POST config/auth/entra-tenants/{id}/activate` (—)**, `PATCH config/auth-ui-policy` (J) | activate: **yes** |
| Remote / external operations | **`POST connections/{id}/test-connect` (—)**, `POST connections/{id}/tools/sql-structure/*` (J + Consumes), **`POST config/auth/entra-tenants/{id}/validate-openid` (—)**, **`POST runs/{id}/cancel\|retry\|park\|resume` (—)**, **`POST runtime-instances/{rid}/controls/shutdown/request` (—)**, **`POST control/scheduler/run-cycle\|recover-stale` (Q)**, **`POST control/recovery/blocked-work/{runId}/retry` (—)**, **`POST control/recovery/schedules/run-cycle` (Q)**, **`POST integrations/{i}/procedures/{p}/launch` (J?)**, `POST notification-diagnostics/smtp-test\|platform-pipeline-test` (J) | **yes** (bold) |
| Lifecycle / retire | **`POST integrations/{id}/activate\|deactivate\|retire` (—)**, `DELETE integrations/{id}`, `DELETE connections/{id}`, **`POST modules/{id}/install\|enable\|disable\|uninstall` (—)**, **`POST resource-contexts/{id}/archive\|unarchive` (—)**, **`POST schedules/{id}/enable\|disable` (—)** | **yes** (bold) |
| Ownership move | `POST connections\|integrations\|schedules/{id}/ownership/move` (J) | JSON only |
| Settings | `PUT/DELETE integrations/{id}/settings/{key}`, `PUT/DELETE integrations/{id}/attention-routing`, `PUT attention-routing/defaults`, notification definitions / subscriptions / opt-outs (J) | JSON only |
| Notification prefs / inbox | `PUT account/notification-preferences/{d}`, `PUT me/notification-subscriptions/{d}` (J); **`POST me/notifications/{id}/mark-read\|mark-unread`, `mark-all-read`, `record-menu-viewed` (—/Q)** | **yes** (bold) |
| Account | `PUT account/profile\|preferences\|avatar/selection` (J), **`POST account/avatar` (multipart)**, `DELETE account/avatar`, `POST account/contact-email` (J), **`POST account/contact-email/resend` (—)**, `POST account/secured/verify` (J) | **yes** (bold) |
| Messaging | **`POST users/{id}/setup-invitation` (J?)**, `POST auth/forgot-password` (anon, J) | **yes** (J?) |
| Login / logout | `POST auth/local/login\|mfa\|break-glass` (anon, J), **`POST auth/local/logout` (—, unused)**, **`POST access-requests`, `access-requests/sign-out` (—)** | **yes** (bold) |
| Machine-to-machine | `POST /api/enroll` (anon; runtime caller; enrollment token in JSON body; no cookie) | n/a (exempt) |
| IdP callback | `POST /signin-oidc` (`form_post`; state + correlation + nonce) | n/a (exempt) |

Legend: J = JSON body; J? = nullable `[FromBody]` (empty accepted, F18); Q = query only; — = no body.

## 19. Threat model — approved

| Attacker position | Session cookie sent (Lax) | Mutation today | Reads response | After SEC-1B |
|---|---|---|---|---|
| Cross-site: form POST / fetch | No | No | No | No |
| Cross-site: top-level GET navigation to `/api` | **Yes** | **G1/G2 writes; G3–G5 remote calls** | No | G1/G2 are POST. Cross-site `/api` GETs are refused. |
| Cross-site: navigation to `/signout` | Yes | **Logout** | — | No (POST + trusted Origin) |
| Same-site sibling (dev `localhost:<other port>`; prod sibling subdomain): form POST | **Yes** | **Yes, every form-reachable endpoint** | No | No: untrusted Origin, no header |
| Same-site sibling: `fetch(credentials:'include')`, simple request | **Yes** | **Yes** | No (CORS) | No |
| Same-site sibling: JSON or custom-header fetch | Preflight; `UiDev` refuses | No | No | No |
| Page served at the trusted dev UI origin itself | Yes | Yes | Yes | Inherent to split-host dev. The trusted origin *is* the client. Development only. |
| Same origin (XSS) | Yes | Yes | Yes | Out of scope: no forgery defense stops XSS (CSP is deferred, §37) |
| Cross-site iframe (clickjacking) | No cookie (Lax) | — | — | `frame-ancestors 'none'` / `X-Frame-Options: DENY` on both hosts (1B3) |

These concerns are kept separate:
- **CSRF** = a forged side effect.
- **CORS** = response readability.
- **XSS** is outside all forgery defenses.
- **Open redirect** = navigation integrity (§14).
- **Session fixation**:
  - `SignInAsync` always issues a fresh ticket, and there is no pre-auth application session.
  - **Local login CSRF** (the victim signed into the attacker's account) is possible today from a same-site origin. It is closed by Rule U,
    because anonymous browser login endpoints are covered.
  - Entra login CSRF is prevented by the framework correlation cookie.

## 20. Strategy comparison (as evaluated)

| | 1. ASP.NET antiforgery tokens | 2. Origin validation | 3. Fetch Metadata | 4. Custom token / double-submit | **5. Origin + platform header + Fetch Metadata (approved)** | 6. SameSite + JSON only |
|---|---|---|---|---|---|---|
| vs cross-site | Strong | Strong | Strong | Strong | **Strong, independent layers** | Fails on top-level GETs |
| vs same-site sibling | Strong | **Strong** (exact list) | Only with Origin | Double-submit **weak** (a sibling can write parent-domain cookies) | **Strong** | **None** |
| Browser support | All | All current browsers send `Origin` on cross-origin requests and on unsafe-method requests (**SEC-1.0 verifies per engine**) | Chromium 76+, Firefox 90+, Safari 16.4+ | All | Origin required; Fetch Metadata only adds rejections | All |
| Entra/local compat | Identity-bound tokens rotate at sign-in; `/signin-oidc` exempt | Neutral | Neutral | Rotation | Neutral; declared exemptions | Neutral |
| ManagementClient impact | Token fetch + cache + refresh + cross-origin dev plumbing | None | None | Token plumbing | **One header in one handler** | None |
| Rollout | All-or-nothing per endpoint | Easy | Easy | Medium | Easy | — |
| Failure modes | Stale token after re-login; 400 storms; conflicts with auth recovery | Proxy stripping `Origin` (rare) | Absent in old browsers | Invented crypto | Misconfigured origin → loud 403 on first dev request | Silent same-site exposure |

**Option 6, assessed fairly.** It is sufficient against a cross-site *POST* in modern browsers. It is insufficient as a platform policy:
it fails for top-level GETs, for every same-site origin, and whenever a future endpoint is not JSON-bound (33 already aren't).

**Hypothesis rejected: option 1 (antiforgery tokens).** The reasons:
- There are no server-rendered forms.
- The only running topology is cross-origin.
- Identity-bound token rotation would collide with session recovery.

It is revisited **only** if implementation evidence proves option 5 unworkable (D1).

## 21. Request-forgery architecture — approved (D1)

**`BrowserRequestTrustMiddleware`** on Management.Host. The pipeline is:

`UseManagementHostExceptionHandling → UseCors (Development only) → UseBrowserRequestTrust → UseAuthentication → UseAuthorization → endpoints`

CORS answers preflights first, and `OPTIONS` never reaches Rule U.

**Trusted origins** (`BrowserRequestTrustOptions`, fixed at startup):
- The **exact configured UI origin(s)**: `ShellUi:ManagementUiUrl` (single value today; the option type allows a list for a future
  topology).
- Compared on scheme + host + port. No wildcard, no suffix matching and no blanket localhost trust.
- The request's `Host` header is **not** an origin source (there is no forwarded-headers middleware; deferred-public-origin).
- **Outside Development, a missing, loopback or non-HTTPS `ManagementUiUrl` fails startup** with an explicit message (§34 R2).

**Rule U — unsafe methods (POST, PUT, PATCH, DELETE) to `/api/**`:**
1. `Origin` present **and** exactly ∈ trusted. Absent or `null` → reject.
2. `X-Apperture-Request: 1` present. It is added centrally (§22); a form cannot send it, and a cross-origin script must preflight.
3. `Sec-Fetch-Site: cross-site` → reject (defense in depth).
   - An **absent** `Sec-Fetch-Site` is never, by itself, a reason to reject.
   - `same-site` and `same-origin` add nothing beyond rules 1–2.
4. SameSite=Lax continues to withhold the cookie cross-site, as a further, independent layer.
5. Failure → **403** `application/problem+json` (`title: "Request blocked"`, `code: "request_origin_rejected"`).
   - Never 401, so forgery failures can never trigger auth recovery.
   - Rule U runs **before** authentication, so a forged request learns nothing about session validity.

**Rule G — `/api/**` GET/HEAD (defense in depth only):** reject `Sec-Fetch-Site: cross-site`. An absent header is allowed. This is not a
substitute for the GET-safety rule (§18.1).

**Rule N — host auth endpoints (outside `/api`):**

| Endpoint | Verb | Rule |
|---|---|---|
| `/signout` | **POST** (D2; GET removed → 405) | Trusted `Origin` required; `Sec-Fetch-Site: cross-site` rejected; **no header rule** (it is a top-level HTML form navigation, because the Entra end-session needs a real navigation) |
| `/signin/link`, `/signin/step-up` | GET | Rule G (cross-site rejected; absent allowed; `none` allowed for direct navigation) |
| `/signin` | GET | Open: the anonymous entry point |

**Scope.** Rule U applies to every browser-facing unsafe `/api` endpoint, **anonymous ones included**: login, MFA, break-glass, password
reset/setup, contact verification, bootstrap, access requests and local logout. Covering them closes login CSRF and keeps the exemption
list genuinely narrow.

**Exemptions (the complete list, explicitly declared and tested):**

| Exempt | Why | Mechanism |
|---|---|---|
| `POST /api/enroll` | Machine-to-machine runtime enrollment; token in body; no cookie; no browser | Endpoint metadata `[BrowserRequestTrustExempt("runtime enrollment: machine client, token-authenticated")]` |
| `/signin-oidc` (and a per-registration `CallbackPath` override) | Cross-site `form_post` from the IdP by design; framework state/correlation/nonce | Path list in options, derived from the live `OpenIdConnectOptions` |
| `/signout-callback-oidc` | IdP post-logout redirect (`SignedOutCallbackPath`) | Same |

No controller family is exempted. A guard test pins exactly this set, and each exemption has a positive test (it passes without browser
headers) and a negative test (a neighbouring endpoint does not).

**Rollout.** `BrowserRequestTrust:Mode` = `Enforce` (default everywhere) | `ReportOnly` (honoured **only** in Development; otherwise ignored
with a startup warning). There is no `Off`.

## 22. ManagementClient integration — approved

- **`BrowserRequestHeadersHandler`** is one `DelegatingHandler`, registered once in `Ui.Client/Program.cs`. It adds:
  - `X-Apperture-Request: 1` to every request (harmless on GET);
  - `X-Apperture-Background: 1` inside a `BackgroundRequest` scope (§16).
- **No `ManagementClient` method and no feature adds either header.** A source guard forbids the header literals outside the handler.
- The browser supplies `Origin` and `Sec-Fetch-*`, and page script cannot forge them.
- No tokens, so no acquisition, rotation or post-login refresh.
- No retries: forgery rejection is deterministic.
- A forgery 403 surfaces through the existing `EnsureSuccessOrThrowAsync` `title` text ("Request blocked"). It is logged server-side.
  Legitimate traffic sees it only on misconfiguration.
- **No conflict with recovery:** forgery = 403 (ignored by the recovery handler); session loss = 401.
- **Test host:** `ManagementHostWebApplicationFactory` gets a default handler that sends the trusted test `Origin` + header, so existing
  integration tests are unchanged. Rejection tests opt out.
- **Ui.Server's `HttpClient`/`ManagementClient`/capabilities registrations are dead code** (no prerender). They are removed in 1B2, with a
  guard, so the two host paths cannot diverge. Both host pages load the same WASM, and the handlers live only in `Ui.Client/Program.cs`.
  **No new JavaScript is introduced**, so the two-host-page script registration rule is not triggered.

## 23. Server integration and guards — approved

- There are no controller attributes other than the single `[BrowserRequestTrustExempt]` on `EnrollController.Enroll`.
- Guards (reflection/source):
  1. The exemption set equals §21's table.
  2. Every non-anonymous controller has a class-level `[Authorize]`, which the no-replay guarantee depends on.
  3. The GET classification of §18.1 is frozen.
  4. Middleware is present and in order in `Program.cs`.
  5. There is one return-URL validator.
  6. The platform header literals appear only in the handler.
  7. Every timer-driven API call in Ui.Client is inside a `BackgroundRequest` scope.
- G1/G2 → POST and `/signout` → POST are done in 1B2, with matching client changes.

## 24. CORS ruling — approved

**Code fact:** `UiDev` is registered at `Management.Host/Program.cs:34-42` and applied **unconditionally** at L87, with origins
`http://localhost:9001` and `https://localhost:9001` (which has no listener), `AllowCredentials`, any header and any method.
`Management.Api/Program.cs` is a separate runnable host with `AllowAnyOrigin/AnyMethod/AnyHeader` and no authentication.

**Ruling:**
1. `UiDev` is **registered and applied only when `IHostEnvironment.IsDevelopment()`**. Production cannot activate it.
2. The allowed origin **comes from `ShellUi:ManagementUiUrl`**, exact (default `http://localhost:9001`). The phantom `https` variant is
   dropped.
3. `AllowCredentials` is kept (dev split-host requires it). Methods: `GET, POST, PUT, PATCH, DELETE`. Headers: `Content-Type`,
   `X-Apperture-Request`, `X-Apperture-Background`.
4. **Never** `AllowAnyOrigin` together with credentials, and no `SetIsOriginAllowed(_ => true)`. A guard asserts this.
5. Outside Development no CORS policy exists. Same-origin production needs none; a split-site production needs its own design (§37).
6. `Management.Api`'s allow-any-origin host is removed under D7 (§33).
7. Tests:
   - A Development preflight from the configured origin is allowed.
   - `localhost:9002` and `https://localhost:9001` are refused.
   - Production and `Testing` return no `Access-Control-Allow-*` for `localhost:9001`.
   - Source guard: `UseCors` sits inside the Development branch.

## 25. Cookie / SameSite ruling — approved

| Setting | Ruling |
|---|---|
| SameSite (app, access-request and pending-MFA cookies) | **`Lax`, set explicitly.** It is not loosened to `None` for step-up or any other reason (§26). `Strict` is rejected (it would break the Entra return chain and email deep links). |
| OIDC correlation/nonce cookies | Framework defaults (`SameSite=None; Secure`). The cross-site `form_post` legitimately needs them, and they are short-lived and flow-bound. |
| Secure | **`SecurePolicy = Always` outside Development**, for the app, access-request and pending-MFA cookies. Development keeps `SameAsRequest` (HTTP-only dev listeners). `Always` does not depend on the request scheme, so it works behind a TLS-terminating proxy without forwarded headers. A non-HTTPS public UI origin outside Development is refused at startup (§21). |
| HttpOnly | Keep `true` |
| Domain | Keep **host-only** |
| Path | `/` |
| Lifetime | 12 h sliding, persistent; background requests do not renew (D9) |
| `__Host-` prefix, rename | Deferred (§37) |
| Revalidation | Deferred (D5, §37) |

## 26. Login / logout / OIDC — approved

**Entra error and loop handling (mandatory):**
- `OnRemoteFailure` **always** calls `HandleResponse()` and redirects:
  - step-up → the step-up return path with `error=oidc`;
  - otherwise → `/login?error=<code>`: `cancelled` for `access_denied`, `oidc` for any other failure.
- ~~`detail` is only allowed in Development and never carries token material.~~ **Amended in SEC-1A2a (§40.5):** there is no `detail` in any
  environment. The operator-facing URL and page carry only a fixed code; the diagnostic context stays in the server log.
- The operator never sees a raw API-host error document.
- `Login.razor` **never auto-redirects when the URL carries `error`**. It also never auto-redirects when a per-tab `sessionStorage` marker
  shows an auto-redirect started in the last 30 s, which covers a failure that returns without `error`. **Amended in SEC-1A2a (§40.6):** it
  also never auto-redirects on a bare visit to `/login` (only when the application sent the operator, with a `returnUrl` or `reason`), so
  that signing out is possible.
- On error it shows a stable house error panel ("Sign-in with Microsoft didn't complete." / "Sign-in was cancelled."). The panel offers
  **Try again** (an explicit, manual Entra navigation) and, where the auth UI policy shows it, local sign-in.

**Step-up callback (mandatory investigation and fix, F8; implemented in SEC-1A2b, §41, following this design with the refinements recorded there):**
- SEC-1.0 proves whether `CompleteStepUpAsync` receives the application cookie during the cross-site `form_post`.
- If it does not, the continuation is redesigned so that the state step-up needs survives through **protected
  `AuthenticationProperties`** (OIDC `state`), which is already how the link flow carries `initiatingUserId`. The candidate design:
  - At initiation, the protected properties record the initiating user id (already present), the session's sign-in source and its
    persistence/expiry parameters.
  - The callback verifies that the Entra `oid`/`tid` is linked to that user and that the requested authentication context is satisfied (the
    existing `IEntraStepUpService.CompleteAsync`).
  - It then issues the application cookie from the user record, using the same identity builder as that sign-in source plus the assurance
    claims. That is the same `Set-Cookie`-on-callback path normal Entra sign-in already uses successfully.
- **SameSite is not weakened.** MFA and assurance semantics are not redesigned beyond what correct callback behavior requires.
- If the proof shows step-up works, the item closes with that evidence and a regression test.

**Logout (D2):**
- `/signout` is **POST only**; GET returns 405.
- The three UI entry points (account menu, access-request page, account security) submit a hidden HTML form. The visual controls are
  unchanged.
- Protection is Rule N: trusted `Origin`, cross-site rejected.
- The destination is always `/login`. **No logout return URL.**
- `POST auth/local/logout` (unused) is removed in 1B2. `POST access-requests/sign-out` stays, under Rule U.
- Client state is discarded by the resulting full navigation. localStorage holds no auth data and is kept.

**Login CSRF.**
- Local: closed by Rule U.
- Entra: framework correlation cookie (unchanged). A tampered `state` fails closed to `/login?error=oidc`.

**Loop audit (required tests):**
- `/login` never recovers into itself (public-route inertness).
- An auth error never auto-redirects.
- A reserved-path return target falls back.
- The loop breaker caps recovery reloads.

## 27. API vs navigation semantics — approved

- APIs return bare 401/403 (optionally with a short `problem+json` body). **They never redirect XHR to HTML.** This is already true.
- Only the client navigates. Non-`/api` host endpoints keep 302 semantics.
- **Hypothesis rejected:** a server-side challenge before shell boot. Ui.Server cannot read the cookie (separate process, key ring and, in
  dev, origin). Even under a future same-origin consolidation the client recovery path would still be required for in-app expiry.

## 28. Browser proof strategy — approved (D6)

**Code fact:** there is no committed browser automation.

**Approved: SEC-1.0 adds `tests/Apperture.BrowserTests`** (Microsoft.Playwright):
- **Opt-in:** skipped unless `APPERTURE_BROWSER_TESTS=1`, so the normal `dotnet test` run is unaffected.
- **Self-hosted, deterministic.** The harness starts Management.Host and Ui.Server on real Kestrel ports in a test environment, with test
  configuration and the existing integration SQL prerequisites. Test configuration supplies:
  - a short cookie lifetime (real server-side expiry, with no production knob);
  - auto-redirect-to-Entra on/off;
  - a **local stub OIDC provider** hosted by the harness, as the Entra authority.
- **Stub OIDC provider:** a discovery document, an authorize endpoint that can answer `error=access_denied` or issue a code, a token
  endpoint that signs `id_token`s with an in-memory key, and JWKS.
  - It is served from `127.0.0.1`, a **different site** from `localhost`, so its `form_post` back to the host is genuinely cross-site. This
    reproduces the F8 conditions deterministically.
  - **No live Microsoft Entra credentials are required.** Real Entra is limited to a targeted manual smoke (§29.3 M rows).
- **Attacker pages** served by the harness:
  - cross-site at `http://127.0.0.1:<port>`;
  - same-site at `http://localhost:<other port>`.
- **Session loss:** cookie deletion (missing), value corruption (invalid), short-lifetime wait (expired), user deletion in the test
  database (F3).
- **Engines:** Chromium for the full suite. **The Origin-presence check (D1) also runs on Firefox and WebKit**, so that "Origin is always
  sent on unsafe requests" is measured, not assumed.
- **Pinning.**
  - Each browser finding (F1, F3–F6, F8, F9, F11, the fragment/return-target behavior) gets a characterization test that records
    **today's** behavior.
  - The slice that fixes it flips the assertion.
  - A finding is called a defect in closure documentation only once its SEC-1.0 test has demonstrated it.
- **Fallback:** if self-hosting Ui.Server static web assets proves impractical, the non-OIDC scenarios may run against a running dev stack
  (env-var URLs, like the existing smoke scripts). The OIDC scenarios still use a self-hosted Management.Host with the stub provider.
  SEC-1.0 records which mode it used.
  **Recorded (SEC-1.0): fully self-hosted; the fallback was not needed.** Management.Host runs in the test process on a real Kestrel
  socket (its real `Program.cs`), Ui.Server runs as a harness-owned child process from its own build output, and the client's
  `ApiBaseUrl` is rewritten per browser context (§38.1).

What each layer proves:

| Layer | Proves |
|---|---|
| **Browser** | Cookie attachment, `Origin`/`Sec-Fetch-*` generation, WHATWG URL parsing, history/`forceLoad`, form posts, `form_post` callbacks |
| **Integration (WebApplicationFactory)** | Rules U/G/N and exemptions, CORS per environment, `Set-Cookie` attributes, `OnRemoteFailure`, `OnCheckSlidingExpiration`, verb changes |
| **Unit** | `LocalReturnUrl`, coordinator state machine, exclusion set, handler behavior, `AuthShellAccess` states |
| **Static guards** | §23 list |

## 29. Test matrix — approved

Legend: **U** unit · **I** integration · **S** static guard · **B** browser (Playwright) · **M** manual smoke.

### 29.1 Authentication recovery
| Case | Layer |
|---|---|
| Reload of protected route with no cookie → `/login?returnUrl=<path+query+fragment>` | B |
| Reload with expired cookie (short test lifetime) → login with target | B |
| Reload with invalid cookie value → login | B |
| Reload with valid cookie + deleted user → "account unavailable" surface; no shell; no loop; Sign out works | B, U |
| Reload while API unreachable / returns 5xx → service-unavailable + Retry; **no login redirect** | B, U |
| In-app call after expiry → exactly one navigation to `/login?…&reason=expired` | B, U |
| Five simultaneous 401s → one probe, one navigation | U, B |
| Toast watcher 401 → one transition, no toast storm, no error text storm | B, U |
| 401 on excluded endpoint (wrong password on local login) → no recovery | U, B |
| Endpoint-specific 401 while `auth/me` = 200 → no recovery | U |
| Probe network failure → no recovery | U |
| **Resource 403 never triggers recovery** (no probe issued) | U, B |
| Loop breaker: 4 recoveries in 60 s → manual Sign-in panel | U |
| Unsafe request that got 401 is not replayed (server sees exactly one attempt) | U, B |
| All sign-in success paths end in a full document load | S |
| Logout → `/login`; Back does not show protected data without a fresh boot | B |
| Role granted while signed out → visible after sign-in (fresh capabilities) | B |
| Exactly one watcher cadence after recovery | B |

### 29.2 Return target / open redirect
- **Accept (U):** `/connections/abc`, `/connections/abc?x=1&y=%2F` (encoded slash in the *query* is data), `/connections/abc?x=1#section`,
  `/runs?status=failed`.
- **Reject → `/control/center` (U, and B for each browser-normalizing case):**
  - absolute and scheme-relative: `https://evil.example`, `http://evil.example/x`, `//evil.example`, `javascript:alert(1)`, `evil.example`;
  - backslash forms: `/\evil.example`, `\/evil.example`, `/\/evil.example`;
  - encoded forms: `/%5Cevil.example`, `/%2F%2Fevil.example`, `/%2fevil.example`, `/%2E%2E/`;
  - control characters: `/<TAB>/evil.example`, `/<CR><LF>/evil.example`;
  - reserved paths: `/login?returnUrl=/x`, `/signin`, `/signout`, `/signin-oidc`, `/access-request`;
  - a 2 049-character value, empty, whitespace.
- **Browser (B):** `/login?returnUrl=%2F%5Cevil.example` while signed in, and again after a local sign-in → lands on `/control/center` and
  **`location.origin` is unchanged**. First as a characterization test (F5), then flipped.
- Local sign-in returns to path + query + fragment (B). Stub-OIDC sign-in returns to path + query + fragment (B). Real Entra return (M).

### 29.3 Entra / OIDC
| Case | Layer |
|---|---|
| `/signin?returnUrl=/x?y=1` → challenge `RedirectUri` = UI + `/x?y=1` | I |
| Stub IdP `access_denied` → `/login?error=cancelled`, stable panel, no auto-redirect, Try again works | B |
| Stub IdP failure with auto-redirect on → no loop (F6 characterization → fixed) | B |
| Production environment remote failure → 302 `/login?error=oidc`, no `detail`, never `problem+json` | I |
| Tampered `state` → `/login?error=oidc` | I |
| Step-up via stub IdP cross-site `form_post` (F8 characterization → fixed) | B |
| Step-up against real tenant; cancelled real Entra sign-in | M |

### 29.4 Request forgery
| Case | Expect | Layer |
|---|---|---|
| POST, no `Origin`, no header | 403 `request_origin_rejected`; action not invoked; no audit row | I |
| POST, `Origin: https://evil.example`, header present | 403 | I |
| POST, `Origin: http://localhost:3000` (same-site, untrusted), header present | 403 | I |
| POST, trusted `Origin`, **no** header | 403 | I |
| POST, trusted `Origin` + header, **no** Fetch Metadata | passes to auth (honest 200/401/403) | I |
| POST, trusted `Origin` + header + `Sec-Fetch-Site: cross-site` | 403 | I |
| `Origin: null` | 403 | I |
| Anonymous `auth/local/login` without trust | 403 (login CSRF) | I |
| Empty-body POST to nullable `[FromBody]` (`procedures/…/launch`) without trust | 403; plus a pin test documenting that with trust an empty body binds `null` (F18) | I |
| GET with `Sec-Fetch-Site: cross-site` | 403 | I |
| GET same-origin / no Fetch Metadata | unaffected | I |
| `POST /api/enroll` with no browser headers | reaches action (exempt) | I |
| OIDC callback from stub IdP (cross-site) | not blocked | B |
| `POST /signout` via form from trusted origin → signed out; `GET /signout` → 405 | I, B |
| G1/G2 as POST work from the UI; old GET routes → 405 | I, B |
| ReportOnly honoured in Development, ignored (with warning) elsewhere | I |
| Exemption set, `[Authorize]` coverage, GET classification, middleware order, header-literal location | S |
| Cross-site auto-submitting form from `127.0.0.1:<p>` → no mutation | B |
| Same-site form and `fetch(credentials:'include')` from `localhost:<p>` → no mutation (F11 characterization → fixed) | B |
| Real UI mutations (create + retire a Connection; mark notification read) succeed | B |
| `Origin` sent on same-origin and cross-origin POST/PUT/PATCH/DELETE in Chromium, Firefox, WebKit | B |

### 29.5 CORS (I)
- Development: preflight from the configured UI origin → allowed with credentials.
- `http://localhost:9002` and `https://localhost:9001` → no allow headers.
- Production and `Testing`: `localhost:9001` → no `Access-Control-Allow-*`.
- Guards: `UseCors` is inside the Development branch; no any-origin with credentials.

### 29.6 Cookie / session (I)
- Session `Set-Cookie`: `samesite=lax`, `httponly`, `path=/`, no `domain=`. `secure` in Production, absent in Development. The
  access-request and pending-MFA cookies follow the same Secure rule.
- A request with `X-Apperture-Background: 1` past the renewal threshold → no `Set-Cookie`. The same request without the header → renewed
  `Set-Cookie`.

### 29.7 Host hygiene (S/I)
- `Ui.Server` startup performs no filesystem write.
- No agent-log script or `appertureShell.debugLog` call remains.
- `Management.Api` has no entry point (`OutputType=Library`, no `Program.cs`).
- `frame-ancestors`/`X-Frame-Options` headers are present on both hosts.
- Outside Development, startup fails on a missing, loopback or HTTP `ManagementUiUrl`.

## 30. Performance / reliability — approved

- One `auth/me` probe per recovery, not per 401.
- No retry loops. The coordinator is single-flight, and the loop breaker allows at most 3 reloads per 60 s per tab.
- No replay of unsafe requests, ever.
- One watcher per document; the reload is the reset.
- The return target is captured once, synchronously, at the `Recovering` edge. Later 401s cannot overwrite it.
- Middleware cost: a few header reads and one set lookup per request.
- D9 changes session semantics as intended: 12 h of operator inactivity signs the tab out, and recovery handles the next request.

## 31. Logging / audit — approved

- **Forgery rejections:** a structured `Warning` (`Information` in ReportOnly). Fields:
  - method, route template (not the raw path), rule id;
  - `Origin`, `Sec-Fetch-Site`, `Sec-Fetch-Mode`;
  - authenticated yes/no;
  - correlation id.

  **Never** cookies, whole header sets, bodies, query strings or tokens. There is a meter `apperture.browser_trust.rejected` (by rule).
  **There are no audit records** for forgery attempts: they are not operator actions, and anonymous floods would pollute the audit trail.
- **Auth recovery:** client-side only. There is no server audit of routine expiry. The loop breaker writes one browser console warning.
- **OIDC remote failure:** existing host logging. `detail` never appears in URLs outside Development.
- **Return URLs are never logged** (they can identify the records an operator was viewing).

---

## 32. Implementation decomposition — approved

Each slice is scoped for one Sonnet session, carries its own tests, and leaves `master` green. SEC-1.0 comes first. Boundary adjustment
against the brief's sketch: **the client header handler lands in SEC-1B1, together with the enforcing middleware.** Enforcing Rule U without
the header would break every UI mutation, so the two must land atomically. SEC-1B2 keeps the verb and logout cleanup.

| Slice | Scope | Depends on | Tests |
|---|---|---|---|
| **SEC-1.0 — browser proof harness** — **delivered 2026-09-24 (§0.1, §38)** | `tests/Apperture.BrowserTests` (opt-in Playwright): self-hosted Management.Host + Ui.Server, stub OIDC provider on `127.0.0.1`, attacker pages, session-loss helpers, three-engine Origin probe. **Characterization tests** for F1, F3, F4, F5, F6, F8, F9, F11 and return-target/history behavior. Records which findings are proven and updates §0. No production behavior change. | — | Harness self-tests + characterizations |
| **SEC-1A1 — session-loss detection and recovery** — **delivered (§39)** | `AuthRecoveryHandler`, `SessionRecoveryCoordinator` (probe, single-flight, exclusions, public-route inertness, loop breaker, `reason=expired` panel), `SessionStateService.MarkSignedOut` + distinct availability/identity states, service-unavailable surface (D8), account-unavailable surface (D5), `GetAccessRequestSessionAsync` 5xx fix, `[Authorize]` coverage guard | 1.0 | §29.1 |
| **SEC-1A2 — return URL and login/error/step-up** — **split; SEC-1A2a delivered (§40), SEC-1A2b (step-up continuation) delivered (§41)** | `LocalReturnUrl` (shared; both old validators deleted; single-validator guard), fragment capture, reserved paths; `OnRemoteFailure` always handled; login error panel + no auto-redirect on `error` / recent marker; **step-up continuation per §26 if F8 is proven**. *If step-up needs the redesign, split it out as SEC-1A2b so the return-URL fix is not held back.* | 1.0 | §29.2, §29.3 |
| **SEC-1A3 — background requests, renewal and capability refresh** — **delivered (§43)** | `BrowserRequestHeadersHandler` background part + `BackgroundRequest` scope; Toast watcher, `RunDetail` poll and automatic unread-count refresh marked; host `OnCheckSlidingExpiration` (D9); timer guard; sign-in full-load guard; single-watcher test | 1A1 (header handler: 1B1 or 1A3, whichever lands first creates it) | §29.1 background rows, §29.6 renewal rows |
| **SEC-1B1 — server request-forgery policy** — **delivered (§44)** | `BrowserRequestTrustMiddleware` + options (exact trusted origins, Mode, startup validation), Rules U/G/N, exemption metadata on `Enroll` + OIDC path exemptions, `problem+json` 403, logging/meter, **client `X-Apperture-Request` header in the same change**, test-factory default handler, F18 pin, guards (exemptions, order, header literals) | 1.0 | §29.4 (I, S) |
| **SEC-1B2 — endpoint verb cleanup and logout** — **delivered (§45)** | G1/G2 → POST (+ client); re-confirm G3–G5 and G9b classifications; `/signout` → POST with Rule N + hidden-form UI in three places; remove GET `/signout` and unused `POST auth/local/logout`; remove Ui.Server's dead `HttpClient`/`ManagementClient`/capabilities registrations; GET-classification guard | 1B1 | §29.4 verb/logout rows |
| **SEC-1B3 — CORS, cookie and host cleanup** — **delivered (§46)** | `UiDev` Development-only + exact configured origin + narrowed methods/headers + any-origin guard; explicit `SameSite=Lax`; `SecurePolicy=Always` outside Development (all three cookies); `frame-ancestors`/`X-Frame-Options`; **`Management.Api` → class library** (drop `Program.cs`, launch profile, `.vscode` entry, stub host test; repoint the placeholder `generate-openapi.ps1` at Management.Host) under the D7 STOP condition; **remove the Ui.Server debug filesystem write, the `_Layout` agent-log script and `appertureShell.debugLog` calls** | 1B1 | §29.5, §29.6, §29.7 |
| **SEC-1B4 — browser and integration security proof** — **delivered as the CI gate (§47)** | Flip every remaining characterization; run the full §29 B matrix against rebuilt, restarted hosts; manual real-Entra smoke (§29.3 M). *As delivered, B4 was scoped to I-2 (§47): each earlier slice had already flipped its characterizations, and the suite runs on every pull request as the blocking `browser-security` job. The manual real-Entra and Safari items moved to operator smoke after SEC-1C (closure audit checklist).* | all above | — |
| **SEC-1C — docs and closure** — **delivered (§48)** | ADR (browser request trust + session recovery + GET-safety rule); update `next-stage-platform-auth.md`, ADR-0042 (watcher: background, non-renewing), ADR-0044 (its POST+JSON measure is superseded as a pattern by SEC-1), deferred-public-origin (CORS/cookie/`ManagementUiUrl` parts done), decisions index, transitions README; closure audit with evidence; roadmap status | all | — |

SEC-1A and SEC-1B are independent after SEC-1.0 and may interleave. No slice is a platform-wide change in one task.

---

## 33. Approved rulings (decided 2026-09-24)

### D1 — Request-forgery architecture — **APPROVED**
For cookie-authenticated browser API requests with unsafe methods:
1. An exact configured trusted `Origin` (scheme + host + port; no wildcard, no suffix, no blanket localhost; the actual configured UI
   origin(s)).
2. One platform request header, added centrally by the client pipeline and never by features.
3. Fetch Metadata as defense in depth: reject `Sec-Fetch-Site: cross-site`. Absent Fetch Metadata is never, by itself, a rejection reason.
4. SameSite remains a further layer, not the primary mechanism.

No ASP.NET antiforgery tokens unless implementation evidence proves this model unworkable (then return to this gate). No per-feature
schemes. The OIDC callback and enrollment exemptions stay narrow and explicitly tested (§21).

### D2 — Logout — **APPROVED**
`/signout` becomes POST, and GET logout is removed. It participates in the forgery policy (Rule N). Login/logout loop and CSRF behavior
are audited and tested (§26, §29).

### D3 — State-changing GETs — **APPROVED WITH CORRECTION**
Central rejection of cross-site `/api` GETs is defense in depth only. **Durable rule: GET endpoints remain safe, i.e. non-mutating with
respect to Apperture state.** The two Graph directory GETs that write (G1/G2) become POST. Signout becomes POST under D2. The remaining
GETs are classified individually (§18.1). Read-only, idempotent remote lookups stay GET, and external reads are not mechanically converted.

### D4 — Recovery navigation — **APPROVED**
Full-page navigation. No automatic replay of the request that met 401, and in particular no replay of POST/PUT/PATCH/DELETE after
sign-in. The operator returns to the original page and re-submits explicitly.

### D5 — Disabled / missing users — **APPROVED WITH CLARIFICATION**
Periodic session revalidation to detect a newly disabled user is **outside SEC-1**, with no revocation polling. SEC-1 **must** handle
`auth/me` itself finding that the principal no longer maps to a usable account:
- 401 → recovery;
- ordinary resource 403 → forbidden, never login;
- `auth/me` identity failure → the terminal account-unavailable surface with sign-out / re-authenticate, no loop (§11).

Provisioning and account-disable policy are unchanged.

### D6 — Browser test infrastructure — **APPROVED**
An opt-in Playwright project as SEC-1.0, before behavioral work. It uses a deterministic local harness and a stub OIDC provider. The
normal suite needs no live Entra credentials, and real Entra is reserved for targeted manual smoke (§28).

### D7 — `Management.Api` executable — **APPROVED**
**Precondition verified by this audit:** there is no supported standalone deployment.
- AppHost, the dev scripts (`scripts/dev/lib/DevProjectPaths.ps1`) and publish (`Publish-DotNetProjects.ps1`) all use Management.Host.
- The stub is referenced only by the solution, `.vscode/launch.json`, a placeholder `generate-openapi.ps1` (which prints "not yet
  implemented"), and one stub smoke test (`ManagementApiTests` / `ManagementApiStubWebApplicationFactory`, which only exercises `GET /`).
- All other test references use the assembly as a library.

Convert it to a class library, preserving controllers, DI registration and integration-test hosting. Remove the alternate executable,
launch path and allow-any-origin configuration. **STOP condition:** if implementation discovers a real supported standalone deployment
dependency, stop and return to this decision.

### D8 — API outage semantics — **APPROVED**
A network failure, outage, startup race or 5xx from auth bootstrap never sends the operator to login. Recovery requires positive evidence
(`auth/me` = 401). A service-unavailable + Retry surface uses house UX (§11).

### D9 — Sliding session and polling — **APPROVED**
Automatic/background requests do not renew the sliding session, so a passive tab eventually requires sign-in. Operator-initiated activity
keeps renewing. All recurring and automatic requests are audited (§10, §16), and background requests are marked centrally
(`BackgroundRequest` scope → header → `OnCheckSlidingExpiration`), never by features touching cookies.

---

## 34. Risks

1. **R1 — the observed symptom may have an unlisted cause.** Mitigation: SEC-1.0 reproduces it first. The §4 candidates are browser
   findings until then.
2. **R2 — trusted-origin misconfiguration** would block every mutation. Mitigation: startup validation outside Development (§21). This also
   closes deferred-public-origin item 1 for `ManagementUiUrl`.
3. **R3 — proxies stripping `Origin`** (rare). Mitigation: rejections are loud, logged and metered. The three-engine probe establishes the
   browser baseline.
4. **R4 — the step-up redesign may be larger than the rest of 1A2.** Mitigation: split it out as 1A2b (§32). SameSite is not weakened.
5. **R5 — operator-visible behavior changes:** idle sign-out (D9), outage no longer shows login (D8), logout mechanics (D2), account-unavailable
   surface (D5).
6. **R6 — self-hosting Ui.Server static web assets in the harness may be awkward.** Mitigation: the documented fallback mode (§28).
7. **R7 — `ReportOnly` misuse.** Mitigation: Development only, with a startup warning elsewhere.

## 35. Readiness

- The audit is complete, and rulings D1–D9 are settled.
- The architecture is approved in separable parts:
  - recovery handler + coordinator;
  - the shared return-URL policy;
  - the login/OIDC fixes;
  - the central background marker;
  - the trust middleware + header handler;
  - CORS, cookie and host hardening.

  They meet at one client pipeline and one server middleware slot. There is no omnibus security service.
- **Next step: SEC-1.0.** No production fix lands before its characterization tests exist for the finding it addresses.
- SEC-1 is **not** complete. Only the design gate is.
- *(Readiness as of 2026-09-24. Current state: every slice is delivered and engineering closure is complete, §48.)*

## 36. Implementation requirements (binding)

| # | Requirement |
|---|---|
| REQ-1 | One pipeline interception point for 401 and one per-tab coordinator. Simultaneous 401s coalesce into one probe and one navigation. |
| REQ-2 | Recovery requires `auth/me` = 401. Network failure or 5xx never navigates to login. |
| REQ-3 | 403 never triggers recovery. An `auth/me` 403 shows the terminal account-unavailable surface. |
| REQ-4 | Auth and public routes never trigger recovery. The loop breaker caps reloads. |
| REQ-5 | No automatic replay of any request after 401 or after sign-in. |
| REQ-6 | Recovery and sign-in end in full document loads. Identity and capabilities load fresh, and watchers start exactly once. |
| REQ-7 | One shared `LocalReturnUrl` policy on client and host. It preserves path + query + fragment, falls back to `/control/center`, and is proven in a real browser for every normalizing case. |
| REQ-8 | Auth errors never auto-redirect. A stable error panel with Try again is shown. No raw API-host error document is shown to operators in any environment. |
| REQ-9 | Step-up proven in the harness. If the ordinary cookie is required on the cross-site callback, step-up continues via protected OIDC properties. SameSite is not weakened. |
| REQ-10 | Background requests are marked centrally and never renew the sliding session. Every timer-driven API call is covered by a guard. |
| REQ-11 | Rule U (exact Origin + platform header + cross-site rejection) on every browser-facing unsafe `/api` request, anonymous ones included. It returns 403, never 401, and runs before authentication. |
| REQ-12 | Exemptions are exactly `POST /api/enroll` and the OIDC callback/sign-out paths. Each has a positive and a negative test. |
| REQ-13 | GET safety: G1/G2 become POST; `/signout` becomes POST (GET → 405); the §18.1 classification is frozen by a guard. |
| REQ-14 | `UiDev` is Development-only with an exact configured origin. There is never any-origin with credentials. Production activation is impossible and tested. |
| REQ-15 | SameSite=Lax is explicit. Secure is always set outside Development. HttpOnly and host-only are preserved. |
| REQ-16 | `Management.Api` becomes a class library under the D7 STOP condition. |
| REQ-17 | The Ui.Server startup filesystem write and the debug agent-log instrumentation are removed. |
| REQ-18 | Outside Development, startup fails on a missing, loopback or non-HTTPS `ManagementUiUrl`. |
| REQ-19 | Browser findings are called defects only after SEC-1.0 evidence. The closure audit cites the evidence. |

## 37. Deferred concerns (outside SEC-1)

- Periodic session revalidation / revocation of disabled users (D5); a candidate for a future IAM hardening item.
- The 403→`null` collapse in `ManagementClient` ("not found" vs "forbidden" UX); the deny-all capability retry cost; `DomainMenuService`
  never retrying; the `RunDetail` poll-restart race.
- Content-Security-Policy / XSS hardening.
- Public origin work:
  - forwarded headers;
  - a multi-instance Data Protection key ring;
  - the `ShellUi:ManagementHostUrl` naming collision;
  - the `__Host-` cookie prefix;
  - a split-site production topology (which would need its own CORS/SameSite design).
- Services residuals (`ConnectionDto.SecretRefs`, the Components kind-filter behavior), IAM-8 custom role lifecycle (roadmap 7b), and
  navigation / cross-context grouping (roadmap 8). None of these is a security dependency of SEC-1.

---

## 38. SEC-1.0 — what was built and what it proved (2026-09-24)

Code, run command and lifecycle details live in [tests/Apperture.BrowserTests/README.md](../../../../tests/Apperture.BrowserTests/README.md).
This section records the decisions and evidence that later slices depend on.

### 38.1 Harness architecture

- **Opt-in.** Every test is skipped unless `APPERTURE_BROWSER_TESTS=1`. One command:
  `$env:APPERTURE_BROWSER_TESTS="1"; dotnet test tests/Apperture.BrowserTests/Apperture.BrowserTests.csproj`. Without it the project only runs
  four fast isolation guards, so `dotnet test Apperture.slnx` is unaffected.
- **Origins** (dynamic ports; the developer's Aspire stack owns `:9000/:9001` and is never touched; the *hosts* are fixed because SameSite is
  decided by site, not port):

  | Role | Origin | Relationship to the app (`localhost`) |
  |---|---|---|
  | UI | `http://localhost:{ui}` (Ui.Server child process) | — |
  | API | `http://localhost:{api}` (Management.Host in-process, real Kestrel) | cross-origin, **same-site** |
  | OIDC stub | `http://127.0.0.1:{oidc}` | **cross-site** |
  | Cross-site attacker | `http://127.0.0.1:{atk}` | **cross-site** |
  | Same-site attacker | `http://localhost:{sib}` | cross-origin, **same-site** |

  The site relationships are asserted from the browser (`Sec-Fetch-Site` recorded by the host) in
  `HarnessSmokeTests.Cross_site_and_same_site_attacker_origins_have_the_intended_site_relationship_to_the_api`.
- **Stub OIDC provider.** Discovery, JWKS, an authorize endpoint that answers by browser-driven `form_post` (success, `access_denied`,
  `server_error`), and a token endpoint signing RS256 id_tokens (with `oid`/`tid`/`nonce`/`acrs`) with an in-memory key. The real
  OpenID Connect handler, association service and step-up service run against it. It never sees a credential.
- **Real composition.** `LiveManagementHost` runs the real `Program.cs` (cookie, OIDC, CORS, authorization, controllers, SQL persistence) in the
  test process via `WebApplicationFactory` with Kestrel substituted for TestServer. The deferred-host machinery also runs the entry point for
  the TestServer twin, so the twin is built with **no hosted services** (otherwise two copies of start-up work race on one database).
- **Test-only controls** (all in the test assembly; see the guards below): a server clock on the application cookie handler only
  (`CookieAuthenticationOptions.TimeProvider`), an observe-only request recorder in front of the pipeline (cookie/token *values* never
  captured), an ephemeral Data Protection key ring, the stub-OIDC configuration and `RequireHttpsMetadata=false`, and re-pointing the
  existing `UiDev` policy's origin list at the run's UI origin (headers, methods and credentials are production's). The client's
  `appsettings*.json` `ApiBaseUrl` is rewritten per browser context (N4). No production endpoint manipulates auth state.
- **Test isolation from production.** `HarnessIsolationGuardTests` (always run, no browser) assert: no `src` project references a test project;
  no `src` file mentions the harness or its controls; no production assembly references the harness assembly; the gate defaults to off.
- **Safety nets.** Playwright contexts abort (and count) any browser request to a localhost port that is not one of the run's own hosts, so a
  client mis-pointed at `:9000` cannot reach the developer stack; off-harness *navigations* (evil.example) are answered locally.
- **Lifecycle.** One shared fixture. The only child process (Ui.Server) is held by handle in a Windows kill-on-close **job object** and killed by
  exact handle on disposal; nothing is ever killed by name. The harness database `Apperture_BrowserTests` is dropped and recreated per run.
- **CORS evidence caveat.** While any Playwright route is registered, Chromium answers CORS preflights itself. Contexts that gather CORS
  evidence therefore register no route (`interceptClientConfig:false`); see `BrowserHarness.NewContextAsync`.

### 38.2 F5 detail — return targets in real browsers

Values as the client sees them after its one percent-decode. "Escapes" = the browser navigated to `http://evil.example/` (answered locally).

- **Escape today (validator accepts all 10):** `/\evil.example`, `/\\evil.example`, `/\/evil.example`, `/\@evil.example`, ` /\evil.example`
  (leading space, trimmed by the validator), `/<TAB>/evil.example`, `/<TAB>\evil.example`, `/<CR><LF>/evil.example`, `/<LF>/evil.example`, and
  `/login?returnUrl=%2F%5Cevil.example` (a second hop: `/login` is on-origin, its nested target then escapes). Chromium, Firefox and WebKit agree on
  `/\evil.example`, the TAB form and the CR LF form. Paths: signed-in `/login?returnUrl=…` bounce, and post-sign-in navigation.
- **Rejected by today's validator and stay on-origin:** absolute `https://`/`http://`, `//evil.example`, `//evil.example/deep`, `\/evil.example`,
  `\\evil.example`, `javascript:`, `data:`, `evil.example`.
- **Accepted, and stay on-origin (do not escape):** literal `%5C`, `%2F%2F`, `%2f`, `%255C` text (the browser does not decode them); `/.//evil.example`
  (becomes the *path* `//evil.example` on the UI origin); `%2E%2E/%2E%2E/evil.example` (normalized to `/evil.example`, still same origin);
  `/login?returnUrl=%2F%2Fevil.example` (the nested value is rejected on the second hop).
- **Auth surfaces and loops** (`/login`, `/login/local`, `/login?returnUrl=%2Flogin`, `/signin`, `/signout`, `/signin-oidc`, `/access-request`): none leaves
  the origin. `/login…` return targets end on `/control/center` (the signed-in operator is bounced off login); the others are accepted by the client and
  the browser lands on that path **on the UI origin** (`/access-request` is a UI route; the `/signin*`, `/signout`, `/signin-oidc` paths are not). The **host** replaces `/signin*`, `/signout*`, `/signin-oidc` with `/control/center` but
  accepts `/login`-family targets.
- **Host `GET /signin?returnUrl=`:** always a `302` to the configured UI authority; the browser resolves every case to the UI origin. `/\evil.example`
  becomes the UI *path* `//evil.example`. CR/LF values produce an **unhandled 500** (cannot be written into a `Location` header).
- **Legitimate corpus (all survive a real local sign-in and the signed-in bounce, on Chromium):** `/control/center`; `/connections/<id>`;
  `…?foo=bar`; `…?foo=bar#notes` (fragment preserved when it is *in* the return value); `/components/connector/Apperture.Platform.Sql`;
  `/config/notifications/definitions/platform.schedule.expected_run_missing`; `/modules/Sample.HelloWorld`; `/runs?status=failed&q=a%20b%2Fc`;
  `/connections?next=%2Fcontrol%2Fcenter`; `/runs?q=%E2%9C%93`; `/connections/caf%C3%A9`; `/connections/a%2Fb`; `/connections/a%2Eb`;
  `/runs?status=failed`; `/connections/`. On selector-aware list pages the client appends `context=all` (N1).

### 38.3 Origin / Fetch Metadata across engines

*(D1 depends on the browser sending `Origin` on unsafe requests.)* Measured by `Unsafe_requests_carry_an_Origin_header_on_every_engine`: for
cross-site and same-site form POST, cross-site and same-site fetch POST, and same-origin and trusted-UI-origin fetch with POST/PUT/PATCH/DELETE,
every request that **reached the host** carried an exact `Origin` (never absent, never `null`) and `Sec-Fetch-Site`. PUT/PATCH/DELETE from a
cross-site or same-site-sibling origin never reached the host (preflight refused by `UiDev`).

| Engine | Unsafe requests delivered | With `Origin` | With `Sec-Fetch-Site` | Cross-site ones carrying the session cookie | Same-site ones carrying it |
|---|---|---|---|---|---|
| Chromium 153 | 12 | 12 | 12 | 0 of 2 | all |
| Firefox 155 | 12 | 12 | 12 | 0 of 2 | all |
| WebKit 26.6 (Playwright Windows build) | 12 | 12 | 12 | **2 of 2** (§38.4) | all |

Observed `Sec-Fetch-Site` values: `same-origin` (page on the API origin), `same-site` (trusted UI origin and the sibling), `cross-site`
(`127.0.0.1`); top-level form posts report `Sec-Fetch-Mode: navigate`, fetches `cors`. `Origin` on the UI's own calls is the exact UI
origin, which is what Rule U will compare.

### 38.4 Environment limits and caveats

- **Interception limits CORS fidelity for SPA-driven tests.** While any Playwright route is registered, Chromium answers CORS preflights itself
  (they never reach the host). Every test that drives the Blazor client (F1–F10, the OIDC and F5 flows, history) registers the `appsettings`
  rewrite and therefore runs with permissive preflights; the host's real CORS *response* headers are still what the client sees, but the recorder
  under-reports `OPTIONS` there. CORS evidence (the preflight refusals above) comes only from route-free contexts, which
  `RequestForgeryBaselineTests` uses; those contexts have no localhost-port guard, so they only ever load harness pages.
- **WebKit here is Playwright's Windows build, not Safari.** It (a) attached the Lax session cookie to **cross-site** form and fetch POSTs
  (SameSite not enforced), so the cross-site rows are recorded but not asserted on it; and (b) rejects the `Secure` OIDC correlation cookie on
  `http://localhost`, so no OIDC flow can complete on it. F8 is therefore measured on Chromium and Firefox only. Safari behavior for F8/F11 is a
  manual item (§38.6).
- Real Microsoft Entra was never contacted. What the stub cannot prove: Conditional Access, real `acrs` issuance, and Microsoft's own cookie/consent
  behavior on the return leg. The step-up finding rests on the **browser** not sending the cookie on a cross-site POST, which is provider-independent.
- The harness runs the `Testing` environment (same branch as Production for `OnRemoteFailure`); Development-only behavior (the
  `/login?error=oidc&detail=` redirect) was not exercised in a browser.
- Ports/origins are plain `http`. `Secure` cookies and HTTPS-only behavior (SEC-1B3) need the HTTPS topology, not this harness.

### 38.5 Points where the evidence bears on the approved wording (for implementation review, not changed here)

1. **§14 rule 4 (reject percent-encoded `/`, `\`, `.` in the path).** The browser does **not** turn `%2F`, `%5C` or `%2f` text into a separator or
   authority, and `%2E%2E` only normalizes *within* the origin. None of these left the origin. Rule 4 is therefore not needed for origin safety in the
   current stack; it is defense against a second decoder. It would also reject the legitimate `/connections/a%2Fb` and `/connections/a%2Eb`, which
   route today. Decide explicitly, in the 1A2 implementation review, whether ids that need an encoded `/` or `.` are supported (no shipped id does).
2. **§13 "return targets carry no View-selector state".** Capture happens after the client has appended `context=all` (N1); the capture point, or a
   strip, must be specified.
3. **§26 "Login.razor never auto-redirects when the URL carries `error`".** Still correct as hardening, but the loop it prevents does not occur today
   (F6); the fix must not make auto-redirect start firing on direct loads as a side effect, and the feature's actual behavior (N2) needs a product decision.
4. **§19 threat table row "cross-site form POST / fetch: cookie sent — No".** Holds on Chromium and Firefox; not on Playwright's WebKit-Windows build
   (§38.4). Safari is unverified.
5. **§3 / F16 "SameSite relies on the framework default".** It is emitted **explicitly** as `samesite=lax`; §25's explicit setting is still worthwhile
   for intent but changes no attribute on the wire.
6. **§16 D9 classification.** A boot request, `PUT /api/account/preferences`, is an *unsafe* request issued automatically on every load (N5). It is
   correctly "operator-initiated" (a load is a navigation), but 1B1's header handler must send the platform header on it, and D9 tests must not treat it as background.

### 38.6 Remaining uncertainty and the manual tests it needs

| Item | Why the harness cannot settle it | Manual test |
|---|---|---|
| Real Entra step-up (F8) | The stub proves the browser withholds the cookie; it cannot prove Microsoft's flow, `acrs`, or that a redesigned continuation satisfies Conditional Access | Configure a test tenant with an authentication context; from a signed-in session run `/signin/step-up`; confirm the return lands with `steppedUp=1` (after 1A2/1A2b) and, before the fix, `error=user_not_found` |
| Safari (F8, F11) | Playwright's WebKit build here is not Safari | Repeat the cross-site form POST and the step-up on macOS/iOS Safari |
| `RunDetail` 2 s poll (F10) | Needs a Run; the Toast watcher covers the class | Enable "Poll" on a run, expire the session, watch the 401 cadence |
| HTTPS topology, `Secure` cookies, `UiDev` outside Development | Harness is plain-http `Testing` | SEC-1B3/1B4 integration + smoke |
| Development-only OIDC failure redirect | Not run in the Development environment | Cancel a real sign-in in a dev stack; observe `/login?error=oidc&detail=` |

### 38.7 Which slice inverts which baseline

| Slice | Baselines to flip |
|---|---|
| 1A1 | F1, F3, F4, F10 (401 half), simultaneous-401, history (review) |
| 1A2a | F5 (flipped), F6 (flipped), F7 (flipped), N6 (flipped: a control character is refused, never a 500); N2 (the auto-redirect now works). **N1 is not changed** (§40.8). |
| 1A2b | F8 (`StepUpCallbackBaselineTests`, flipped: §41) |
| 1A3 | F9, F10 (poll half) |
| 1B1 | F11 (same-site sibling form and fetch must be rejected), the Origin/Fetch-Metadata probe becomes the positive-path suite |
| 1B3 | F16 (Secure), F13/F15 assertions |

---

## 39. SEC-1A1 — what was built and what it proved (2026-09-25)

Scope: centralized session-loss detection and recovery only. Return-URL validation and fragment capture (1A2), the Entra/OIDC
fixes (1A2), background non-renewal (1A3) and everything in SEC-1B are untouched.

### 39.1 As built

| Piece | Where (`Apperture.Management.Ui.Client` unless noted) | Responsibility |
|---|---|---|
| `AuthRecoveryHandler` | `Services/AuthRecoveryHandler.cs` | The one pipeline interception point (`IncludeCredentialsHandler → AuthRecoveryHandler → HttpClientHandler`, `Program.cs`). On a **401** from a non-excluded call it reports to the coordinator, **awaits the shared decision**, and returns the original response untouched. Never looks at 403 or any other status. |
| `AuthRecoveryExclusions` | same file | Sign-in and bootstrap endpoints never report: `auth/me`, `auth/local/login\|mfa\|logout`, `auth/break-glass`, `auth/sign-in-options`, `auth/forgot-password`, `auth/reset-password`, `auth/setup-password`, `auth/verify-contact-email`, `access-requests[/*]`, `bootstrap/*`. `auth/local/change-password` is *not* excluded (it is session-authenticated). |
| `SessionRecoveryCoordinator` | `Services/SessionRecoveryCoordinator.cs` | Scoped (per document). `Normal → Confirming → Recovering \| ManualSignInRequired`. Single-flight probe, exception-safe recovery edge, loop breaker, public-route and not-signed-in inertness. |
| `ISessionProbe` / `HttpSessionProbe` | `Services/SessionProbe.cs` | The authoritative `GET auth/me` over a **separate credentials-only `HttpClient`** (no recovery handler, browser cache `no-store`). 200 / 401 / 403 / anything else. 15 s timeout. |
| `IRecoveryHistoryStore` | `Services/RecoveryHistoryStore.cs` | Per-tab `sessionStorage` (via the runtime's dotted interop; **no new JavaScript**), falling back to memory. |
| `SessionStateService` | `Services/SessionStateService.cs` | New `MarkSessionExpired` / `MarkAccountUnavailable` / `MarkServiceUnavailable` (clear the identity, notify once). New event `UnauthorizedDuringRefresh`. |
| `SessionLoadState` | `Ui.Shared/Auth/SessionLoadState.cs` | `ForbiddenUnlinked → AccountUnavailable`, `Error → ServiceUnavailable`, new `SessionExpired`. |
| `AuthShellAccess` | `Ui.Shared/Auth/AuthShellAccess.cs` | The shell never mounts for the three unusable states; `ShouldRedirectToLogin` is now `Unauthenticated` only. |
| `SessionStatusSurface` | `Organisms/SessionStatusSurface.razor` | The three surfaces (house `PageMessage`). Rendered by `AuthRouteGate` in place of the shell. |
| `LoginRouteHelper.BuildLoginUrl` | `Ui.Shared/Auth/LoginRouteHelper.cs` | The one builder for automatic sign-in navigations (`returnUrl` existing form + `reason`); the boot-time gate redirect uses it too. |
| `Login.razor` | `Pages/Login.razor` | Shows "Your session has expired. Sign in to continue." for `reason=expired`. |
| `ManagementClient.GetAccessRequestSessionAsync` | `Apperture.Management.Client` | 5xx / unexpected status now throws instead of reading as "no session". |
| `[Authorize]` coverage guard | `tests/Apperture.UnitTests/ControlPlane/ControllerAuthorizationCoverageTests.cs` | Every controller has a class-level `[Authorize]` except `AuthController` (every action states its own) and `BootstrapController` (`[AllowAnonymous]`); the anonymous mutations are pinned. |

### 39.2 Decision matrix as implemented

| Event | Result |
|---|---|
| 401 from a protected, non-excluded call, tab believes it is signed in, route not public | one `auth/me` probe (shared by every concurrent 401) |
| probe **401** | capture path+query, `MarkSessionExpired`, **one** `NavigateTo(/login?returnUrl=…&reason=expired, forceLoad: true)`; later 401s ignored |
| probe **403** | `AccountUnavailable` surface (Sign out / Sign in with a different account); no navigation |
| probe network failure, timeout, 5xx, unexpected status | `ServiceUnavailable` surface with **Retry**; no navigation (amended from the §12 table, per the slice owner) |
| probe **200** | nothing: the 401 was endpoint-specific; the page's own error stands; no replay; no self-perpetuating probe |
| resource **403** (any call) | never reaches the coordinator: no probe, no navigation; existing forbidden behavior |
| `auth/me` at boot: 401 / 403 / 5xx / network | unchanged for 401 (gate → `/login`, F2); 403 → `AccountUnavailable`; 5xx/network → `ServiceUnavailable` |
| a soft `SessionStateService.RefreshAsync` in a signed-in tab is answered 401 by `auth/me` | forwarded to the coordinator (`UnauthorizedDuringRefresh`); the refresh neither marks the tab signed out nor navigates |

Never done: replaying the request that met 401 (POST/PUT/PATCH/DELETE especially), navigating in-SPA for a runtime recovery,
retrying a 401 (the capabilities service's own retry now skips 401).

### 39.3 Loop breaker, and refinements of §12

- Loop breaker: the coordinator records each automatic recovery (unix-ms stamps in the tab's `sessionStorage`). If **three** stamps
  fall inside the last 60 s, the fourth recovery does not navigate: `ManualSignInRequired`, a console warning, and the surface offers
  **Sign in** (a user gesture, so it navigates regardless). Stamps dated in the future are ignored.
- The handler **awaits** the coordinator's decision rather than firing and forgetting, so a page never paints its own raw
  `401 Unauthorized` before the shell is replaced (measured in the browser tests with a text sampler).
- Retry = a document reload of the same address (re-runs the whole auth bootstrap from a clean slate, preserves the intended
  destination, replays nothing).
- The recovery surface carries a fallback **Sign in** button in both expired phases so a tab restored from the back/forward cache is
  never left with nothing to do.
- The gate's boot-time redirect for `auth/me` = 401 stays an in-SPA `replace` navigation (F2 is unchanged and correct); only
  runtime recovery is a full-page load.
- Fragment capture and the View selector's `context=all` leaking into the target (N1) are unchanged: 1A2.
- `MainLayout`'s "not linked" banner, the top-bar "Account not linked" link and the `AccountProfile` not-linked branch were dead
  once the shell stopped mounting for that state, and were removed.

### 39.4 Browser evidence (Chromium)

| Test | Proves |
|---|---|
| `SessionExpiryBaselineTests.F1_…recovers_with_one_probe_and_one_sign_in_navigation` | one 401, no replay; one probe; one `/login` document; `returnUrl` + `reason=expired`; the raw error is never visible |
| `…F10_background_toast_poll_…converges_on_one_recovery_and_stops_polling` | the watcher's 401 leads to one recovery; no further poll; no toast |
| `…Simultaneous_401_in_several_tabs_…` | three tabs: per tab one probe and one navigation |
| `BootstrapStateBaselineTests.F3_…` | `auth/me` 403 → terminal surface, no shell, no redirect, one check, Sign out works |
| `BootstrapStateBaselineTests.F4_…` (3 shapes) | outage → service-unavailable, address preserved, one probe, Retry opens the intended page |
| `HistoryBaselineTests.In_app_recovery_…` | recovery adds one entry; Back never shows protected data (the reload test is unchanged) |
| `SessionRecoveryBrowserTests` | resource 403 never probes; a failed sign-in 401 never recovers; a mutation that met 401 is sent once (before and after sign-in); probe 403 / 503 mid-session; the loop breaker across a real reload; Account > Security open after expiry |

### 39.5 Findings left exactly as they were

F2 (correct before and after), F5 (open redirect: reproduced), F6 (not reproduced), F7 (raw failed OIDC result), F8 (step-up; real
Entra unverified), F9 (background polling still renews the cookie), F11 / F12 / F13 / F14 / F15 / F16 (request forgery and host hygiene),
F17, F18, N1–N6. Not exercised in a browser: the `RunDetail` 2 s poll (needs a Run). By construction it stops when the gate unmounts
the shell, and the unit tests cover the class through the Toast watcher.

### 39.6 Independent review (read-only) and its resolution

Critical: none. Important, both fixed with tests:

1. **A second, uncoordinated runtime sign-in path.** A soft `SessionStateService.RefreshAsync` in a signed-in tab (opening
   Account > Security, saving a preference) is answered 401 by `auth/me`, which the pipeline excludes; the refresh set
   `Unauthenticated` and the gate did an in-app redirect without `reason=expired`, the loop breaker or a full-page load, and an
   in-flight refresh could overwrite a `SessionExpired` mark. Now: a soft refresh 401 raises `UnauthorizedDuringRefresh`, which the
   coordinator turns into the same single recovery; the refresh never declares the tab signed out, and never overwrites a state marked
   while it was in flight. (`AuthRouteGate` injects the coordinator so it exists from the first render.)
2. **`auth/local/*` was excluded as a prefix, but `auth/local/change-password` is `[Authorize]`d.** The exclusion is now the exact
   anonymous endpoints `auth/local/login|mfa|logout`; a 401 from change-password reports.

Minor, fixed: the recovery edge is exception-safe (a throwing session subscriber cannot stop the navigation); stamps dated in the future
are ignored; the capabilities service no longer retries a 401. Minor, accepted: the stamp is saved just before the final re-check, so a
recovery abandoned at the last moment is still counted (it only advances the breaker); a failed cosmetic soft refresh with a 5xx shows
the service-unavailable surface (uniform D8). Historical decision records that still say `ForbiddenUnlinked`/`Error` are left as
history.

### 39.7 Validation (final code, repo-local artifacts)

`dotnet build Apperture.slnx`: 0 errors. UnitTests 2 827 passed; ArchTests 38; Ui.Shared.Tests 1 374; Ui.Client.Tests 309 (all
green, 0 skipped). Browser suite (`APPERTURE_BROWSER_TESTS=1`, all engines where the suite uses them): **173 / 173 passed** (was 165
at SEC-1.0; +8: seven new recovery cases in `SessionRecoveryBrowserTests` and the in-app-recovery history test; the flipped tests
replaced their baselines one for one). IntegrationTests
were not run: Management.Host and the server-side auth behavior are unchanged.

### 39.8 Known limits

- If `sessionStorage` is blocked, the loop breaker falls back to memory and guards only one document.
- A failed *soft* refresh (a cosmetic refresh after saving a profile or theme) that gets a 5xx now shows the service-unavailable surface,
  because outage handling is uniform (D8).
- `ResourceContextViewState` does not subscribe to session changes; every exit from the three surfaces is a full document load, so
  nothing stale is reused.

---

## 40. SEC-1A2a — what was built and what it proved (2026-09-25)

Scope: the one shared return-target policy, safe preservation of a local path, query and fragment, local and normal Entra sign-in
return, failed and cancelled Entra sign-in, the auto-Entra behavior after the F6 finding, and removal of the raw OIDC/API-host failure
experience. **Not** in this slice, and unchanged: step-up continuation (SEC-1A2b, finding F8), background renewal (SEC-1A3), request
forgery, the signout verb, CORS, cookies and `Management.Api` cleanup.

### 40.1 The `LocalReturnUrl` policy (`Apperture.Management.Ui.Shared/Auth/LocalReturnUrl.cs`)

One class, consumed by the WebAssembly client and by Management.Host (through `Management.Api`'s reference to Ui.Shared). Both old
validators (`LoginRouteHelper.NormalizeReturnUrl`, `ManagementUiRedirects.NormalizeRelativePath`) and the host's private reserved-path list
are deleted; `ReturnTargetSourceGuardTests` pins that no second copy exists.

**Representation contract (decode discipline).** The policy receives the `returnUrl` query value after **exactly one** percent-decode
(the client's `LoginRouteHelper.GetReturnUrlRaw`, or ASP.NET's query binder on the host). It is still a URL *reference*: any `%XX` left in
it is data or an encoded separator inside that reference, and nothing decodes it again. What the policy accepts is emitted **verbatim**:
`Uri.EscapeDataString` once into a query value (`BuildLoginUrl`, `BuildReturnQuery`), or appended to the configured UI origin for a redirect
(`ManagementUiRedirects.Build`). Nothing is trimmed, rewritten or repaired: a candidate is accepted unchanged or refused, and the caller
uses `/control/center` (`LocalReturnUrl.Fallback`, the same route as before).

**Rules** (`LocalReturnUrlVerdict` names each refusal):

1. Non-empty, at most 2 048 characters.
2. Printable ASCII only (`U+0021`–`U+007E`) and **no backslash**. This refuses every control character (TAB/CR/LF that browsers strip), every
   space (a leading one is trimmed by a browser), DEL, every non-ASCII character (a legitimate target is always the browser's serialized href,
   which is ASCII), and the backslash a browser reads as `/`.
3. Begins with `/`.
4. The **first path segment is a plain route name** (`[A-Za-z0-9_-]+`), or the whole path is `/`. This refuses an authority-shaped start (`//x`,
   `/\x`, `/.//x`) and any encoding, dot or punctuation in the position where a second decoder could create an authority.
5. Later path segments are in the form a browser would normalize them to: no `.`/`..` segment (including `%2e` spellings) and no empty segment
   except a single trailing slash (`/connections/`).
6. The first segment is not an authentication surface: `login` (so `/login`, `/login/local` and any nested `returnUrl` chain), `access-request`,
   `signin` (`/signin`, `/signin/link`, `/signin/step-up`), `signin-oidc`, `signout`, `signout-callback-oidc`, `signout-oidc`. Case-insensitive.
7. Supplementary only: resolving against a fixed sentinel origin with `Uri.TryCreate` keeps the scheme and authority. It never decides alone.

**Correction to the gate (rule 4 of §14).** SEC-1.0 proved browsers keep `%2F`, `%5C` and `%2E` text inside a path segment or query and never
turn it into a separator, and that legitimate routes carry them. The policy therefore does **not** reject encoded `/` or `.` merely for being
encoded (`/connections/a%2Fb`, `/connections/a%2Eb`, `?q=a%20b%2Fc`, `?next=%2Fcontrol%2Fcenter` all pass, pinned in unit and browser tests). The
encoded hostile corpus values (`/%5Cevil.example`, `/%2F%2Fevil.example`, `/%2fevil.example`, `/%255Cevil.example`, `/%2E%2E/%2E%2E/evil.example`)
are refused by rule 4 *positionally*, as defense in depth: no legitimate route has any encoding in its first segment, and that is the only place
a lazy second decoder could turn an encoded separator into an authority. `/.//evil.example` is refused by rule 4 as well.

### 40.2 Consumers

| Where | Change |
|---|---|
| `LoginRouteHelper.BuildLoginUrl` | The one builder for every automatic sign-in navigation (A1's boot-time gate and runtime recovery). Validates with the policy and **leaves out** a refused target. Takes the full `path?query#fragment`. |
| `AuthRouteGate`, `SessionRecoveryCoordinator` | Capture `LocalReturnUrl.FromAbsoluteUri(Nav.Uri)`: path, query **and fragment** exactly as the browser serialized them (from the string, not from `Uri`, so .NET canonicalization cannot change it). A1's 401 decision matrix, single-flight, loop breaker, no-replay and outage semantics are untouched. |
| `Login.razor`, `LoginLocal.razor` | `LoginRouteHelper.ReadReturnTarget` (validated). Only a valid target is kept, linked between the two sign-in pages, passed to the local form, or forwarded to `/signin`. |
| `LocalLoginFormSection` | `LocalReturnUrl.ResolveOrFallback(ReturnPath)` then `NavigateTo(..., forceLoad: true)` (checked again: the component does not trust its parent). |
| Host `/signin`, `/signin/link`, `/signin/step-up` | `LocalReturnUrl.Validate` / `ResolveOrFallback`. The redirect authority is always the configured UI origin. A CR/LF `returnUrl` is refused instead of becoming an unhandled 500 (N6). |
| `ManagementUiRedirects.Build` | Host-built paths must still be a browser-stable local path (`IsLocalPath`, which skips the reserved rule because the host's own targets are auth surfaces); the optional query parameter is inserted **before** the fragment (`/x#n` → `/x?steppedUp=1#n`), or a fragment-carrying return target would swallow it. |
| `EntraSignInChallenge.IsSafeUiRedirect` | Kept as defense in depth (UI authority equality) and now uses the shared reserved rule. |

### 40.3 Fragment handling

A fragment never reaches a server, so the journey carries it deliberately: the client captures it from `NavigationManager.Uri`; it travels
inside the **escaped `returnUrl` value** (`%23`) through `/login`; a local sign-in navigates to it directly; a Microsoft sign-in forwards it to the
host's `/signin` (decoded once to `path?query#fragment`), which stores the whole string in `AuthenticationProperties.RedirectUri` (protected inside
the OIDC `state`); the post-callback `302 Location` restores it, and browsers honor a fragment in `Location`. It cannot be an open-redirect
bypass: it is part of the validated string, after the path, and the policy allows nothing in it that a browser reads as a separator (no backslash,
no control character). One bug was in the way: the View selector's own URL rewrite (`ResourceContextViewState.BuildUriWithContext`) dropped the
address bar's `#fragment` on selector-aware list pages before any capture (found by the F2 and in-app-recovery browser tests); it now keeps it.

### 40.4 Local sign-in

Valid target → returned to with path, query and fragment; invalid or hostile → `/control/center`; a failed sign-in stays on the same URL with the
inline message, no navigation, no further document load; `/login` itself can never be a return target (rule 6). Credential semantics are untouched.

### 40.5 Failed and cancelled Microsoft sign-in (F7)

`EntraRemoteFailure` (Management.Host) handles the two events the OpenID Connect handler raises for a sign-in that produced no session:

| Framework event | When | Operator lands on |
|---|---|---|
| `OnAccessDenied` | the provider answered `access_denied` **and** the callback's state and correlation validated | `/login?error=cancelled` |
| `OnRemoteFailure` | every other failure: a provider error, a tampered, replayed or missing state, a failed back-channel exchange, an exception thrown while completing sign-in | `/login?error=oidc` |

Each writes the redirect and calls `HandleResponse()` in the same step, so a failure yields **exactly one** 302 and no rethrow. Step-up keeps its
own return leg (`<return path>?error=oidc`) and a failed self-link now returns to `/account?error=oidc` (it was an unhandled 500 in production too);
neither distinguishes "cancelled". The URL and the page carry **only a fixed code**: no `detail` parameter in any environment (the Development-only
`detail=<exception message>` is removed), no provider text, no return target (an error leg drops it on purpose, so a failure cannot re-enter a flow).
The server log keeps the context (Information for a cancellation; Warning with the exception for a failure). `Login.razor` maps the code to a fixed
message ("Sign-in was cancelled." / "Sign-in with Microsoft didn't complete." / the existing "not connected to an Apperture user" text); an unknown
or empty code is the generic failure, so **every** error code (including `tenant_mismatch`, which previously showed nothing) gets the stable panel.
The panel offers an explicit **Try again**; the chooser and local sign-in remain below it. Integration tests (`ManagementAuthEndpointsTests`, real
OpenID Connect handler on a test server, Development and Production environments) pin: cancelled, provider errors, a tampered or missing state, a
genuine state without the correlation cookie (never "cancelled"), step-up and link legs, no provider text in the URL, no `problem+json`.

### 40.6 Auto-Entra redirect (F6, corrected)

Decision (from the code and Epic 43: a persisted, operator-configurable policy labelled "Automatically redirect to Microsoft sign-in when policy
allows (advanced)"): **the feature is intended, so it is made to work rather than deleted.** SEC-1.0 found it effectively dead because the page
looked at the sign-in options only at first render, before they had loaded (N2). `LoginAutoRedirectPolicy` now decides, once per page load and only
after the options are known. It never fires:

- when the URL carries an `error` (any value, including an empty one): a failed or cancelled attempt shows its panel;
- within 30 s of the previous automatic attempt in the tab (per-tab `sessionStorage` marker, memory fallback): a return without an error, such as a
  session cookie the browser refused, cannot become a loop;
- **on a bare visit to `/login`**. The application always sends an operator with a `returnUrl` and/or `reason` (an expired session, a protected
  route); a bare `/login` is where **sign-out** and "Sign in with a different account" land. Bouncing that straight back into the same Microsoft
  account would make signing out impossible. This rule is an addition to the gate's wording and is the one product decision in this slice; it is
  cheap to relax if the owner prefers.

### 40.7 Browser evidence

| Test | Proves |
|---|---|
| `OpenRedirectBaselineTests` (Chromium, full corpus × three flows) | every hostile value and every auth surface finishes on `/control/center` on the UI origin, in the signed-in bounce, the local sign-in and the host `/signin` leg (F5 flipped: none of the ten escape; N6: no 500); every legitimate deep link (dotted Component / Notification Definition / Module ids, nested routes, query, fragment, encoded data) arrives intact by local sign-in and by bounce |
| `F5_key_hostile_cases_end_on_the_default_route_on_every_engine`, `Key_legitimate_deep_links_arrive_intact_on_every_engine` | the decisive cases on Chromium, Firefox and WebKit |
| `EntraReturnTargetBrowserTests` (Chromium, Firefox) | a legitimate deep link through capture → sign-in page → Microsoft (stub) → landing; a hostile `returnUrl` on the host's `/signin` link and on the UI page; Try again after a cancelled sign-in; local sign-in, failed local sign-in and in-app expiry recovery, fragment included |
| `OidcFlowBaselineTests.F7_…` (Chromium, Firefox) | cancelled → `error=cancelled`, failed → `error=oidc`; one 302 (server-side record), the stable panel and Try again, no raw JSON, no loop |
| `OidcFlowBaselineTests.F6_…` | auto-redirect fires exactly once for a protected route, and never on a bare visit, on an error (with or without a return target) or within the window; a failure returns to its panel with no second attempt |

Engines, honestly: the OIDC legs run on Chromium and Firefox. Playwright's WebKit build for Windows is not Safari and cannot complete an OpenID
Connect handshake here (it rejects the Secure correlation cookie on `http://localhost`, §38.4), so the WebKit evidence is the URL-normalization
cases only. Real Microsoft Entra was not contacted; a cancelled real sign-in remains a manual item (§38.6). Harness note: the login endpoint stamps a
new ticket's expiry from the real clock, so a browser test that advanced the cookie handler's clock must reset it before signing in again.

### 40.8 Findings left exactly as they were

- **F8** (step-up): the `StepUpCallbackBaselineTests` baseline is untouched and still measures the cross-site `form_post` callback arriving without the
  session cookie and ending `error=user_not_found`. **Handed to SEC-1A2b, which fixed it (§41).** The ordinary session cookie was not loosened.
- **F9** / F10 renewal half (SEC-1A3), **F11** (1B1), **F12** side-effect GETs and `GET /signout` (1B2), **F13–F16** CORS, cookie and host hygiene
  (1B3), F17, F18: not touched. The A2a slice only swapped the validator behind the existing step-up and link return paths (and made their
  failure legs redirect); it did not change their continuation.
- **N1** (the View selector's `context=all` leaking into captured targets): **not changed.** It needs an owner ruling, because `context=` is
  genuinely part of what a list page shows; stripping it would silently change the view an operator returns to. The tests strip it before asserting.
- **Known limits, not addressed:** a *configured* (database) OIDC callback path other than `/signin-oidc` is not in the policy's reserved list (the
  policy cannot see it); a return target equal to it is a meaningless UI-origin route, not a loop. An error leg drops the return target, so Try again
  lands on the default route. A raw backslash inside a query or fragment (`?user=CORP\jdoe`) is refused (a browser cannot reinterpret it there, but the
  rule is one line and fails safe to the default route). A failure of the *challenge* leg itself (IdP metadata unreachable) is outside the callback
  handling and is not turned into an error redirect. The account-unavailable surface still reaches sign-out by `GET /signout` (SEC-1B2 converts it).

### 40.9 Independent review (read-only) and its resolution

Critical: none. Important: none. Reviewed: open-redirect bypasses (WHATWG parsing, encoding, Unicode), decode/encode confusion and client/host drift,
`Build`, OIDC failure handling, loops, fragment handling, legitimate-route regressions. Minor findings and what was done:

- *Auto-redirect evaluated before the URL state was read* (a render between the options arriving and `OnInitializedAsync` resuming could silently disable
  it): fixed. The URL-derived state (error, hand-off, target) is now read synchronously before the options request; it also makes the error panel appear
  on first paint.
- *Cancelled claimed without a validated correlation?* Added a test: a genuine protected state with no correlation cookie plus `access_denied` returns
  `error=oidc`, never `cancelled` (the handler validates correlation first).
- *Host and client can differ on `returnUrl`* (`+` is a space to ASP.NET; duplicate parameters are comma-joined): fail-safe only (the host refuses or
  keeps an on-origin value); values built by the client are `EscapeDataString`ed and unaffected. Accepted.
- *2 048-character cap and the OIDC `state` size*, *marker with blocked storage cannot span a full-page navigation* (not a regression: it was unbounded
  before), *guard-test looseness*, *`ResolvesOffOrigin` unreachable*, *duplicate `account_not_linked` constant*, *log volume on junk callbacks*: accepted
  as Minor; the size limit against a real Microsoft authorize URL is part of the manual Entra smoke (§38.6).

### 40.10 Validation (final code, repo-local `artifacts/sec1a2a`)

`dotnet build Apperture.slnx`: 0 errors. UnitTests 3 034 passed (was 2 827); ArchTests 38; Ui.Shared.Tests 1 381 (was 1 374); Ui.Client.Tests 325
(was 309); IntegrationTests 262 passed in about 7 minutes against the test SQL container (the soft-skip pattern returns in milliseconds, so the
duration is the evidence that SQL ran; no separate database write-marker check was made). Browser suite (`APPERTURE_BROWSER_TESTS=1`, all engines where
the suite uses them): **284 / 284 passed** in about 19 minutes (was 173 at SEC-1A1). The additions are the return-target matrix (three flows over the
hostile, auth-surface and legitimate corpus, and the per-engine key cases), `EntraReturnTargetBrowserTests`, the flipped F6/F7 cases and the always-on
corpus-versus-policy tests; the flipped F5/F6/F7/F2 tests replaced their baselines.

---

## 41. SEC-1A2b — Entra step-up continuation across the cross-site callback (2026-09-26)

Scope: finding F8 only. Not touched: background renewal (SEC-1A3), request forgery and every other SEC-1B item, `N1`. The ordinary application
session cookie was **not** loosened (still `SameSite=Lax`, `HttpOnly`; pinned by a unit test and a source guard).

### 41.1 The failure mechanism (recap of §0.1 F8)

Step-up starts as a top-level navigation (the Lax session cookie is sent). Microsoft returns by a cross-site `form_post`, which browsers send
without a Lax cookie. The old `CompleteStepUpAsync` then called `AuthenticateAsync(Cookies)` to *merge the step-up assurance claims onto the
existing session*; with no cookie it failed as `error=user_not_found`. Step-up therefore never worked in a browser, for any session kind. (The
merge was also unsound: on a browser that did send the cookie, it layered the callback's assurance onto whatever session was there.)

### 41.2 Architecture

The callback no longer reads the ambient session at all. Everything comes from two trusted places: the OIDC handler's **protected state**
(`AuthenticationProperties`, already unprotected and bound to this browser's correlation cookie, which is the mechanism account linking already
uses for its initiating user) and the **validated id_token principal**.

| Continuation item (protected `AuthenticationProperties`) | Why the callback needs it |
|---|---|
| purpose = step-up (existing) | so the state cannot be used as a link or sign-in (and vice versa); checked before any association service is reached |
| initiating Apperture user id (existing key, shared with link) | who is stepping up |
| requested authentication context (existing) | what the token must prove |
| return path (existing), validated by the shared `LocalReturnUrl` policy | where to land; fragment kept, `steppedUp=1` inserted before it |
| **session kind** `Local` or `Entra` (new) | to re-issue the same kind of session without the old cookie; a kind, never an authorization decision (permissions are evaluated per request) |
| **`ExpiresUtc` = issue + 15 min** (new use of the built-in field) | the OIDC handler does **not** enforce the state's own expiry (proven by a test: a state expired three hours ago was accepted); the callback enforces it, and a state with no expiry is refused |

Nothing else rides along: no credential, cookie content, token, claim dump or authorization decision (a unit test asserts the exact key set).
A missing or unknown part of the continuation is refused as `continuation_invalid`.

**Callback order.** (1) recover and validate the continuation, (2) `IEntraStepUpService.CompleteAsync` proves the returned identity, (3) load the
initiating user, (4) re-issue the session, (5) redirect. Step 2 is the existing service, unchanged: identity key **`Source + TenantId(tid) +
ExternalId(oid)`** looked up through `IUserRepository.FindIdentityLinkAsync`; the token's `tid` must equal the active registration's tenant; the
link's owner must equal the initiating user (ordinal); the link must be proven; the requested `acrs` context must be present. No email, UPN, display
name or oid-alone path exists (source guard), and the service never links, relinks or provisions (source guard + tests assert no identity-link write).

**Re-issuing the session (step 4).**
- *Entra session kind:* the framework signs in the validated principal (whose tid + oid step 2 just proved are the initiator's) with its saved tokens,
  exactly as a normal Entra sign-in does; only the step-up assurance claims are added and the continuation (and the state's expiry) is removed so it
  cannot become the session's expiry (the ticket keeps the normal 12 h).
- *Local session kind:* rebuilt from the user record by the new `LocalSessionIdentity` (the one definition of the local session identity, now also
  used by local sign-in), plus the assurance claims, persistent 12 h. Because a fresh session is minted, the callback re-applies the gates local
  sign-in enforces: local sign-in enabled and an active local credential, else `local_session_unavailable` (review finding, fixed).
- Emergency (break-glass) sessions and sessions of an unrecognised kind do not start Entra step-up (409, code `session_kind_unsupported`). This never
  worked in a browser before, so nothing regresses.

**Existing step-up model unchanged:** the grant is still the assurance claims (`apperture.auth.assurance.*`) on the session cookie, same mechanism, same
recent-strong-authentication lifetime, evaluated by the same evaluator; native TOTP and recovery-code MFA and local sign-in are not touched.

### 41.3 Identity binding, and what the callback does not do

A callback cannot succeed on "state says user A": the returned identity must be A's own proven link in the active tenant, or the step-up is refused with
`identity_mismatch` / `tenant_mismatch` / `unproven` / `authentication_context_not_satisfied` and **no** session change and no link write. Login-CSRF
analysis: the callback now issues a session (before, it merged onto one). An attacker would have to place their own state, code and correlation/nonce
cookies in a victim's browser, which requires setting the `.AspNetCore.Correlation.*` and `.Nonce.*` cookies on the victim; the residual risk is the same
as the existing `/signin` and `/signin/link` callbacks, which already issue sessions, and even then the session is only the initiator's, after the
initiator's own tid + oid was proven. Replay: the framework deletes the correlation and nonce cookies on first use and the code is single-use at the
provider; the framework adds no replay store and none was added. The pending state is not bound to the initiating *session*: if the operator signs out
while at Microsoft, completing Microsoft still yields a session, for the same user, after that user's own proof (accepted; the old design failed closed
only because it always failed).

### 41.4 Also fixed while here (review)

- A local-session re-issue no longer bypasses the local-credential and local-auth-enabled gates (Important).
- Normal Entra sign-in now strips session-assurance claims from the incoming id_token principal, so a tenant claims-mapping policy emitting
  `apperture.auth.assurance.*` cannot forge assurance (step-up already replaced them).
- The break-glass refusal has its own code (it reused `not_configured`).
- Step-up audit: the existing service still records `StepUpSatisfied` for the identity proof; a later callback refusal (user gone, no local identity,
  local session unavailable, stale or invalid continuation) appends its own `StepUpFailed` with a bounded `reason=` (no token, claim, state or cookie
  content). A `satisfied` record therefore means "identity proven", and a session was issued only if no `failed` record follows it.
- Recent-account-verification claims (a local password unlock) are not carried into the re-issued session: the safe direction (re-verify).
- The local session's 12 h window is now counted from the step-up (before, it would have inherited the old ticket); a local sign-in has the same shape.

### 41.5 Tests and evidence

| Where | Proves |
|---|---|
| `EntraStepUpCallbackTests` (real OIDC handler, real `EntraStepUpService`, fake token endpoint / user store / audit; **39 tests**) | the callback with nonce + correlation cookies and **no session cookie** succeeds for both session kinds; a *different user's* session cookie on the request changes nothing; wrong user, wrong tenant, unknown identity, unproven link, missing context, missing/unknown/tampered/expired/expiry-less continuation, missing correlation cookie, replay, user gone, local credential disabled or absent, Entra-kind refusals: all refuse with no session and no link write; only the minimum keys ride in the state; hostile return targets fall back; account linking and normal sign-in (deep link with query + fragment, refused targets, cancellation) are unchanged |
| `EntraStepUpSecurityGuardTests` | session cookies stay `SameSite=Lax`; no `SameSiteMode.None` in application source; the callback body never reads `AuthenticateAsync` / `HttpContext.User` / request cookies; the continuation lives only in OIDC `AuthenticationProperties`; the step-up identity path is tid + oid only and never links |
| `StepUpCallbackBaselineTests` (**F8 flipped**, Chromium and Firefox) | in each browser: the callback is a cross-site POST carrying only OIDC flow cookies and **no session cookie**; step-up nevertheless succeeds for an Entra-signed-in and for a local-signed-in operator; `auth/me` shows the **same user** and the session kind kept, `hasRecentStrongAuthentication` false → true; the callback's own `Set-Cookie` is the session, `samesite=lax`; return page, query and fragment honored; no `user_not_found`; different-user, other-tenant, unknown-identity and no-context are refused with a bounded code, no session issued, the operator's own session untouched; a cancelled step-up returns with `error=oidc`, no loop; hostile return targets fall back to `/account` |

WebKit is not run: Playwright's Windows WebKit cannot complete an OIDC handshake here (§38.4); it is not simulated. Safari is a manual item.

### 41.6 Manual checks that remain (real Microsoft Entra, Safari)

1. Configure a test tenant with an authentication context (Conditional Access) and set its id on the active registration. Sign in (locally with a linked
   Microsoft account, and separately with Microsoft).
2. From Account › Security (or a step-up-protected action), start **Verify with Microsoft**; complete real Microsoft authentication and the CA challenge.
3. Confirm you land on the original page (path, query, fragment) with a success message, **not** `error=user_not_found`, and that the step-up-protected
   operation now succeeds; `GET /api/auth/me` reports `hasRecentStrongAuthentication: true`.
4. If practical, inspect the callback in the browser devtools: `POST /signin-oidc` carries only the OIDC correlation/nonce cookies, no `.AspNetCore.Cookies`,
   and its response sets `.AspNetCore.Cookies` (`SameSite=Lax`).
5. Cancel at Microsoft: you return to the same page with the bounded error and your session is intact.
6. Wrong account (a Microsoft account linked to a different Apperture user, or unlinked): the step-up is refused (`identity_mismatch`), nothing changes.
7. Repeat 2–3 on Safari (macOS and iOS) and confirm Safari's handling of the cross-site POST (it may or may not send the Lax cookie: the flow must work either way).
8. Check the sign-in cookie sizes with and without saved tokens are unchanged for an Entra step-up (the framework re-issues the session with its tokens).

### 41.7 Validation (final code, repo-local `artifacts/sec1a2b`)

`dotnet build Apperture.slnx`: 0 errors. UnitTests 3 079 passed (was 3 034); ArchTests 38; Ui.Shared.Tests 1 381; Ui.Client.Tests 325;
IntegrationTests 262 passed in about 6 minutes against the test SQL container (the soft-skip pattern returns in milliseconds, so the duration is the
evidence that SQL ran; no separate database write-marker check was made). Browser suite (`APPERTURE_BROWSER_TESTS=1`, all engines where the suite uses
them): **300 / 300 passed** in about 18 minutes (was 284 at SEC-1A2a; the single F8 baseline was replaced by 18 step-up cases). An independent
read-only review found no Critical issue and one Important issue (the local-session gates, §41.2), fixed with tests; its Minor findings are addressed in
§41.3–41.4 or accepted there.

---

## 42. SEC-1A2b follow-up — external Apperture authority claims (I-1)

An independent review of the A-series found no Critical issue and one Important code issue that predates SEC-1 and must be fixed before SEC-1 merges.
A second Important finding (I-2, the browser suite is gated off in normal CI) stays open for SEC-1B4 / closure. SEC-1 is not complete.

### 42.1 The trust boundary

`OnTokenValidated` accepted the validated id_token principal largely intact. A2b already removed `apperture.auth.assurance.*` and
`apperture.auth.account.verification.*`. Other claims in the application namespace `http://schemas.apperture/` (the prefix of
`ClaimsPrincipalToActorMapper.ApplicationClaimNamespace`, shared by `identity_source` and `break_glass`) could survive. A tenant claims-mapping policy on the
trusted Entra application could therefore send `break_glass=1` or `identity_source=Local`, and the host would trust those names. `break_glass` grants
unrestricted authorization.

### 42.2 The rule

Before the host adds any trusted application claim, `RemoveUntrustedApplicationClaims` drops every claim whose type is in
`http://schemas.apperture/`. Ordinary identity-provider claims (`tid`, `oid`, `acrs`, issuer and audience data) are not in that namespace and are kept.
The strip runs on the shared token-validated path, so it covers normal Entra sign-in, account linking, and both step-up session kinds. Host-issued
`identity_source=Entra` is added only after the strip, so an external value cannot win `FindFirst`. Step-up assurance claims (`apperture.auth.assurance.*`)
are still added afterwards by the host and are not in this namespace.

### 42.3 Evidence

`EntraStepUpCallbackTests` (real OpenID Connect handler): a normal sign-in and an Entra-session step-up whose id_token carries `break_glass=1`,
`identity_source=Local`, and an unknown `http://schemas.apperture/custom_authority` claim finish with none of those values, identity source `Entra`, and the
actor not break-glass. `oid`, `tid`, and `acrs` remain. `EntraStepUpSecurityGuardTests` pins that the strip is namespace-wide and occurs before
`identity_source` is added. The full step-up callback and guard classes: 51 passed. Browser and integration suites were not re-run for this follow-up;
the changed path is the same handler those suites already drive, and SEC-1A3 re-runs them.

---

## 43. SEC-1A3 — background requests do not renew the sliding session (2026-09-25)

Scope: findings F9 and the renewal half of F10. Not touched: request forgery (SEC-1B1), side-effect GETs and `GET /signout` (SEC-1B2), CORS / cookie Secure / host hygiene (SEC-1B3), the CI browser job (I-2), `N1`. The 12 h sliding lifetime, SameSite, and Secure policy are unchanged. SEC-1 is not complete.

### 43.1 What counts as activity

A passive tab must not keep the 12 h sliding cookie alive. Operator-initiated authenticated requests still renew it once the framework's renewal threshold is reached. Automatic work is marked by a `BackgroundRequest` scope (`AsyncLocal`, nested and exception-safe). `BrowserRequestHeadersHandler` is the only writer of `X-Apperture-Background: 1` (`BackgroundActivity` in shared kernel). The host's `OnCheckSlidingExpiration` sets `ShouldRenew = false` when that header is present. The request stays authenticated and authorized. Forging the header can only decline renewal of the caller's own cookie.

Call sites, by invocation context rather than by API method:

| Caller | Classification |
|---|---|
| Toast watcher tick | Background |
| `RunDetail` poll loop | Background. The shared `LoadAsync` stays unmarked, so the operator Refresh button renews |
| Unread count on first paint and on session change | Background |
| Unread count when the notification menu is opened | Operator (renewing) |
| Toast dwell timers | Local UI, no HTTP |
| Boot (`auth/me`, capabilities, menus) | Operator: a navigation |

A source guard fails if a new `PeriodicTimer`, `CreateTimer`, or `new Timer` appears in the UI client outside that inventory.

### 43.2 Session kinds

Renewal suppression is on the application cookie scheme, which is the scheme local sign-in, Entra sign-in, and both step-up re-issues use. A unit theory covers Local and Entra tickets with and without step-up assurance claims: background does not re-issue; the next foreground request does.

### 43.3 Findings

| Id | Status |
|---|---|
| I-1 | **Fixed** (§42). External claims in `http://schemas.apperture/` are stripped before the host adds trusted ones |
| F9 | **Fixed** when the browser proof below passes: a passive toast poll does not `Set-Cookie` at the renewal threshold; the session then expires and A1 recovers once |
| F10 | 401 half fixed in A1. Renewal half closed with F9 when that proof passes |
| I-2 | **Open.** The browser suite stays gated off in normal CI. SEC-1 is not complete until a CI, gated, or scheduled run exists. Left for SEC-1B4 / closure |
| F11–F18, N1 | Unchanged. SEC-1B |

### 43.4 Evidence

`dotnet build Apperture.slnx`: 0 errors (repo-local `artifacts/sec1-a3-slnx`). UnitTests 3 090 passed. ArchTests 38. Ui.Shared.Tests 1 381. Ui.Client.Tests 333. `BackgroundSessionRenewalTests`: background requests do not re-issue the cookie for Local and Entra tickets, with and without step-up assurance; the next foreground request does; a background request after expiry is 401 and mints no cookie. Browser suite (`APPERTURE_BROWSER_TESTS=1`): **301 / 301 passed** in about 26 minutes. F9 is flipped (`SessionRenewalBaselineTests`): the passive toast poll at the renewal threshold does not `Set-Cookie`, the session then expires, and recovery reaches `/login`. The same threshold with an operator Refresh does re-issue the cookie. IntegrationTests: 154 passed and 108 failed because the SQL fixture could not open `127.0.0.1,58361` (`SqlAvailable = false`), not because of an auth assertion. I-2 (browser suite gated off in normal CI) stays open. SEC-1 is not complete.

---

## 44. SEC-1B1 — platform browser request-forgery policy (2026-09-25)

Scope: finding F11, Rule U and Rule G, the platform request header, and the F18 empty-body pin. Not in this slice: Rule N and `GET /signout` (SEC-1B2), Graph side-effect GET conversion (SEC-1B2), `UiDev` CORS, cookie `Secure`, SameSite changes, and production HTTPS/loopback startup refusal (SEC-1B3). The application cookie stays `SameSite=Lax`. SEC-1 is not complete.

### 44.1 Pipeline

`UseManagementHostExceptionHandling` → `UseCors("UiDev")` → `UseBrowserRequestTrust` → `UseAuthentication` → `UseAuthorization` → endpoints.

`BrowserRequestTrustMiddleware` runs after CORS, so a preflight is answered before the policy, and before authentication, so a rejection is always 403 and does not consult the session cookie. Endpoint metadata is available there: the enroll exemption is visible, which is the proof that routing has already selected the endpoint. A rejection does not call the rest of the pipeline, so the action, the audit store, and any remote call are not reached.

`OPTIONS` is allowed and is never treated as a mutation. `UiDev` itself is unchanged (SEC-1B3).

### 44.2 What an unsafe `/api` request must present

Unsafe methods are everything except `GET`, `HEAD` and `OPTIONS`. `POST`, `PUT`, `PATCH` and `DELETE` are the product verbs; any other method fails closed under the same rules. The path is `/api` or `/api/…` (case-insensitive). A path such as `/apiv2` is not covered; a source guard shows every controller route is under `/api`.

For a covered unsafe request, in order:

1. **Exact `Origin`.** One value, parsed as an origin (scheme, host, port only). It must equal the origin parsed from the live `ShellUi:ManagementUiUrl` (`ManagementUiRedirects.ResolveBaseUrl`; development fallback `http://localhost:9001`). The comparison reads that configuration when the request is classified, the same value post-login redirects use. It is not taken from the request. A startup snapshot would miss a host that supplies the URL while the application is composed (the browser harness does). Scheme and host compare case-insensitively; the port is exact. No suffix match, no wildcard, no blanket localhost, no `Host` header. `http://localhost:9001` does not trust `http://localhost:9002`, `http://127.0.0.1:9001`, `https://localhost:9001`, or `http://evil.localhost:9001`. A missing `Origin` is rejected. The literal `null` does not parse and is rejected. A configured URL that is not an origin (a path, userinfo, query, or fragment) leaves the trusted set empty and every unsafe `/api` request is rejected. Startup logs that condition. It does not fail the process: refusing a missing, loopback, or non-HTTPS URL outside Development is SEC-1B3.
2. **`X-Apperture-Request: 1`.** One value, ordinal match. Missing, wrong, empty, comma-joined, or duplicated values are rejected. The constant lives once in `Apperture.Shared.Kernel.BrowserRequest`. It is not a credential and it does not replace the Origin check.
3. **`Sec-Fetch-Site: cross-site` is rejected** even when Origin and the header are otherwise valid. `same-origin`, `same-site`, `none`, and a missing header do not reject by themselves. `same-site` is not trusted: a sibling origin still fails the exact Origin check. More than one Fetch Metadata value fails closed.

**Rule G.** `GET` and `HEAD` under `/api` are rejected only when `Sec-Fetch-Site` is `cross-site` (or ambiguous). They do not require Origin or the platform header.

**Failure.** `403` `application/problem+json`, title `Request blocked`, extension code `request_origin_rejected`, plus the correlation id. Never 401, and no `WWW-Authenticate`. The recovery handler ignores it. The log records method, route template, rule id, a bounded Origin, `Sec-Fetch-Site`, `Sec-Fetch-Mode`, and the correlation id. It does not record cookies, the body, or the query. `Authenticated` in that log is the principal before `UseAuthentication`, so it is false on every rejection and does not reveal whether a cookie was present.

`BrowserRequestTrust:Mode` is `Enforce` (default) or `ReportOnly`. Report-only is honoured only in Development. Anywhere else it is ignored and a startup warning says enforcement stays on. There is no `Off`. An unknown mode string is `Enforce`.

### 44.3 Exemptions

| Exempt | Why | Mechanism |
|---|---|---|
| `POST /api/enroll` | Runtime enrollment is a machine client. The token is in the body. There is no browser session. | `[BrowserRequestTrustExempt("runtime enrollment: machine client, token-authenticated")]` on `EnrollController.Enroll` only |
| `/signin-oidc`, `/signout-callback-oidc` | IdP `form_post` and the post-logout redirect. The framework already checks state, correlation, and nonce. | Exact paths from the live `OpenIdConnectOptions` (`CallbackPath`, `SignedOutCallbackPath`). `RemoteSignOutPath` is empty and is not exempted |

Those callback paths are already outside `/api`, so the `/api` rules do not apply to them. The path list is still applied first, so a later change that moved a callback under `/api` would not start demanding the browser header. `POST /api/enroll/invitation` and any other enroll path are not exempt. A `GET` does not inherit the enroll exemption. No auth controller is exempt. Local login, MFA, break-glass, and the other anonymous unsafe `/api` POSTs are covered. `GET /signout` is unchanged and remains SEC-1B2 (D2).

### 44.4 Client

`BrowserRequestHeadersHandler` is the only writer. Every Management UI request gets `X-Apperture-Request: 1`. A `BackgroundRequest` scope also gets `X-Apperture-Background: 1`, as before. Background traffic is not exempt from Rule U. Features, Razor pages, and `ManagementClient` do not set either header. The session-recovery probe stays a credentials-only `GET auth/me`; Rule G does not require the header, and a 403 still does not start recovery.

The integration factory sends `Origin: http://localhost:9001` and `X-Apperture-Request: 1` by default, matching `ShellUi:ManagementUiUrl` in that host. Rejection tests remove both.

### 44.5 Non-browser callers

No supported cookie-authenticated HTTP client is blocked by this contract.

- The CLI host bootstraps in process. `CliCommandService` takes `IManagementClient` and is not registered as an HTTP caller.
- Runtime workers do not call these Management routes.
- `POST /api/enroll` is the machine enrollment client and is the exemption above.
- `Ui.Server`'s `HttpClient` is the dead prerender registration SEC-1B2 removes. The live shell is the WebAssembly client, which uses the handler.
- Test hosts and Playwright's API client send the header on purpose. Attacker pages do not, and the browser context does not add it globally (that would leak onto the attacker).

### 44.6 Independent review

Critical: none. Important: none left open.

Reviewed: middleware order and side-effect suppression; Origin parsing (scheme, host, port, userinfo, path, `null`, missing); sibling-origin and localhost versus `127.0.0.1`; header absence, wrong value, and duplicates; Fetch Metadata (`cross-site` rejects, missing does not, `same-site` is not a bypass); `OPTIONS`; exemption breadth; login POSTs still covered; background header interaction; 403 versus 401; `/api` path coverage; the `Management.Api` stub host (F14, still SEC-1B3, not a supported cookie deployment). Production HTTPS and non-loopback startup refusal stays SEC-1B3, as the slice boundary required. SameSite and CORS were not changed.

### 44.7 Evidence

`dotnet build Apperture.slnx`: 0 errors (repo-local `artifacts/sec1-b1-slnx`). UnitTests 3 125 passed (was 3 090). ArchTests 38. Ui.Shared.Tests 1 381. Ui.Client.Tests 333. IntegrationTests **272 passed, 0 failed, 0 skipped** in 10 m 6 s (`artifacts/sec1-b1-int-full`, collections serialized). SQL execution, read from the test container without putting credentials on a command line: `Apperture_IntegrationTests` last user update `2026-09-25 21:22:00` → `22:46:06`; `Apperture_ManagementGovernanceTests` `21:14:33` → `22:47:59`; server UTC `22:48:19`. A parallel run earlier in the same session resolved the governance fixture to LocalDB (trusted connection, no SQL user) because `SqlServerTestConfigurationTests` mutates process environment; four structure-browse assertions then failed for that reason and the governance catalog on the test container did not move. The serialized run is the one counted above.

Browser suite (`APPERTURE_BROWSER_TESTS=1`, Chromium, Firefox, and WebKit): **312 passed, 0 failed, 0 skipped** in 26 m 34 s (`artifacts/sec1-b1-browser`). Was 301 at SEC-1A3. F11 is flipped on all three engines (`RequestForgeryBaselineTests`): a same-site sibling form and simple fetch, and a cross-site form and simple fetch, are 403 and leave the disposable context Active. Playwright's WebKit-Windows build still attaches the Lax cookie to a cross-site POST; the rejection does not depend on that, and it is not Safari. A legitimate local sign-in POST and the appearance-menu PUT both carry `Origin` equal to the UI origin and `X-Apperture-Request: 1`. `GET /signout` (D2) is still the old characterization. Preflight tests are unchanged (CORS is SEC-1B3).

Host proofs in `BrowserRequestTrustHttpTests`: a trusted login reaches the action (401 for a bad password) and writes the failure audit; the same login without Origin or the header is 403 and writes nothing; sibling Origin, wrong header, and `Sec-Fetch-Site: cross-site` are 403; a cross-site GET is 403 and a GET without Fetch Metadata is not; `OPTIONS`, `POST /signin-oidc`, and `GET /signout` are not `request_origin_rejected`; `POST /api/enroll` is exempt and the invitation and extra paths are not; an untrusted connection create or delete does not write a row or a `connection.created` audit; a trusted empty `POST` launch of a missing integration returns `404` `integration_not_found` (F18), and the untrusted one is 403.

### 44.8 Findings left open

| Id | Status |
|---|---|
| F11 | **Fixed.** Same-site sibling and cross-site form/fetch are 403 and do not mutate (Chromium, Firefox, WebKit) |
| F12, D2 | **Open.** `GET /signout` and the Graph side-effect GETs. SEC-1B2 |
| F13, F14, F15, F16 | **Open.** CORS, the `Management.Api` stub host, the Ui.Server debug write, cookie `Secure`. SEC-1B3 |
| F18 | **Pinned.** Empty trusted POST reaches the action; untrusted POST does not |
| I-2 | **Open.** The browser suite stays gated off in normal CI. SEC-1B4 / closure |
| N1 | Unchanged |

SEC-1 is not complete.

---

## 45. SEC-1B2 — verb cleanup, sign-out and Rule N (2026-09-26)

Scope: F12 and D2, Rule N (§21), G1/G2 (§18.1), the re-confirmation of G3–G5 and G9b, the unused `POST auth/local/logout`, Ui.Server's dead
client registrations, the GET-classification guard, and the framework's front-channel sign-out path (an item the independent SEC-1B1
review carried into this slice; disabled in the B2 closeout). Not in this slice: `UiDev` CORS, cookie `Secure`, SameSite, production
`ManagementUiUrl` validation, the `Management.Api` executable and the Ui.Server debug write (SEC-1B3), and the CI browser job (I-2).
SEC-1 is not complete.

### 45.1 Rule N, as built

Rule N lives in the one server slot, `BrowserRequestTrustMiddleware`, and runs before authentication like Rule U. It is keyed on endpoint
metadata (`NativeNavigationEndpoint`), not on a path string, so a route variant the router accepts (a trailing slash, another case) cannot
slip past it. For an endpoint carrying that metadata:

- an unsafe method needs one `Origin` that equals the trusted UI origin (the same parser and comparison as Rule U), and is refused on
  `Sec-Fetch-Site: cross-site`;
- `GET`/`HEAD` is refused on `Sec-Fetch-Site: cross-site` (or more than one value); an absent header, `none` (a typed or bookmarked
  address), `same-origin` and `same-site` pass;
- the platform header is **not** required: a top-level form or link navigation cannot carry it, and it would add nothing a sibling origin
  could not also fail on the Origin check.

Failure is the Rule U 403 (`request_origin_rejected`). The metadata is on exactly three endpoints: `POST /signout`, `GET /signin/step-up`,
`GET /signin/link`. `GET /signin` stays open (§21). A navigation endpoint under `/api` would trade the header rule for Rule N, so the guard
below forbids it.

### 45.2 Sign-out (D2)

- `/signout` is `MapPost` only; `GET /signout` is 405 from routing and clears nothing. The handler is unchanged (local session → `/login`;
  Entra session → directory end-session, then `/login`).
- The UI signs out through one atom, `SignOutForm`: a native `<form method="post" action="{host}/signout">` with a submit button whose
  classes and label come from the caller, and no script handler. It replaces the four navigations to the sign-out URL: the account menu,
  account security, the access-request page, and the SEC-1A1 account-unavailable surface ("Sign out" and "Sign in with a different
  account"). No new JavaScript. The account menu no longer closes itself first; the document leaves as the form submits.
- `POST auth/local/logout` (unused), `IManagementClient.LocalLogoutAsync` and its recovery exclusion are removed.
  `POST access-requests/sign-out` stays under Rule U.

### 45.3 The front-channel sign-out path (`/signout-oidc`) — gate amendment

§21 listed `/signout-oidc` (the OIDC handler's `RemoteSignOutPath`) only as an exemption from Rule U. It is also a GET any page can navigate
to. The framework's default handler compares `sid`/`iss` only when the session holds them, so a cross-site navigation to a live
`RemoteSignOutPath` ended a local session (every local session holds neither claim). Removing `GET /signout` alone would not have closed D2.

Closeout of this slice established a further fact against the earlier sid/iss-binding amendment: a real Entra sign-in through
`AddAppertureAuthentication` (with `MapInboundClaims = false`) preserves `sid` under its raw name and does **not** preserve `iss`.
ASP.NET Core's OpenID Connect `ClaimActions` delete `iss` (with the other token-envelope claims) after `OnTokenValidated` and before the
cookie ticket is written. Apperture does not change `ClaimActions` or ticket shape solely to support remote front-channel logout.

Entra advertises `frontchannel_logout_supported` but not session-bound front-channel logout. Microsoft's normal front-channel call is a
cross-site iframe and therefore does not receive the SameSite=Lax Apperture authentication cookie. A live `RemoteSignOutPath` would
therefore serve top-level navigations that legitimate Entra logout does not use.

**Authoritative ruling.** `RemoteSignOutPath` is set to empty in the final OpenID Connect `PostConfigure`, after every ordinary
configurer and whether or not Entra is enabled. `/signout-oidc` is not a live OIDC protocol endpoint. It is not exempted from browser
request-trust by a constant fallback: the middleware includes a remote-sign-out path in the callback list only when the effective
`RemoteSignOutPath.HasValue` is true. No unsolicited Entra front-channel request may terminate an Apperture session. Apperture sessions
terminate through protected application-owned `POST /signout`, or through session expiry. `/signout-callback-oidc` (`SignedOutCallbackPath`)
remains active and is independent of `RemoteSignOutPath`; application-owned Entra logout still redirects through Microsoft's end-session
endpoint and returns there.

This slice does not change SameSite, cookie `Secure`, `ClaimActions`, or `MapInboundClaims`. If true Entra front-channel / single-sign-out
is required later, it needs a new design gate together with the relevant cross-site cookie architecture.

### 45.4 Entra directory searches (G1/G2)

`GET …/entra-tenants/{id}/directory/users|groups` called Microsoft Graph with the stored secret and wrote the registration's discovery
outcome. Rule G would have refused only an explicit cross-site GET, so a same-site sibling could trigger both. They are now
`POST …/directory/users|groups` with `[Consumes("application/json")]` and one typed body (`EntraDirectorySearchRequestBody(Search, Top = 25)`),
so they sit under Rule U like every other mutation, with no second mechanism. Authorization is unchanged (`auth.configuration.read`). The
client methods are `SearchEntraDirectoryUsersAsync`/`SearchEntraDirectoryGroupsAsync` (the search text is in the body, never the URL); the
two directory pages and user detail call them.

### 45.5 Re-confirmed GETs (§18.1)

- G3 `GET /api/users/entra/directory`: Graph search plus local reads (users by identity link, pending requests). No write. Stays GET.
- G4/G5 options: `PlatformConnectorDefinitionsOptionProvider` reads the catalog; `CampusNexusCampusesOptionProvider` resolves the slot and
  lists campuses. No write. Stay GET.
- G9b `GET /api/me/notifications` and `…/unread-count`: `EnsureInboxRowsForUserAsync` inserts only the reader's missing inbox rows, with the
  delivery-time defaults. It never changes read, unread or dismissed state of an existing row. Stays GET.

### 45.6 Ui.Server

Ui.Server's `HttpClient`, `IManagementClient`, `DomainMenuService`, `IOperatorCapabilitiesService`, `IncludeCredentialsHandler`, the shell
services and their options were never resolved (no prerender; `_Host` renders no component). They are removed, so the only Management UI
HTTP pipeline is Ui.Client's, where the platform header is added. The debug startup write stays for SEC-1B3.

### 45.7 Guards

- `GetSafetyClassificationGuardTests`: every GET action that can reach a remote-calling or writing service (the directory, provisioning and
  OpenID services, the option-provider coordinator, the inbox service, connection tools, connection management, the notification pipeline
  tester) is listed with its §18.1 class. A new one fails until it is classified; a stale entry fails too. Audit appends are observation and
  are not in the set.
- `BrowserRequestTrustGuardTests.Every_routed_unsafe_endpoint_falls_under_rule_u_or_rule_n`: enumerates the endpoints Management.Host
  really routes (controllers plus host auth endpoints, via `EndpointDataSource`), not attribute templates. Every unsafe endpoint is under
  `/api` or carries Rule N; the non-`/api` set is exactly the four auth endpoints; no `/api` endpoint carries Rule N; the only exemption is
  `POST /api/Enroll`. A companion check pins the `Map*` calls in `Program.cs` to the ones enumerated.
- `SignOutSurfaceGuardTests`, `SignOutFormSourceGuardTests`, `UiServerHostRegistrationGuardTests`, `EntraDirectorySearchRequestForgeryTests`.

### 45.8 Evidence

`dotnet build Apperture.slnx`: 0 errors (repo-local `artifacts/sec1-b2-slnx`; the 47 warnings are pre-existing). UnitTests 3 176 (was
3 177; the sid/iss-matching cases were removed). ArchTests 38. Ui.Shared.Tests 1 393. Ui.Client.Tests 333. IntegrationTests **283 passed, 0 failed** in 6 m 58 s
(collections serialized, `artifacts/sec1-b2-int-full`). SQL execution, read from the test container without a credential on the command
line: `Apperture_IntegrationTests` last user update `2026-09-26 15:41:29` → `20:01:09`; `Apperture_ManagementGovernanceTests`
`15:34:38` → `19:55:47`; server time `20:05:13`.

Browser suite (`APPERTURE_BROWSER_TESTS=1`, Chromium, Firefox, WebKit): first full run **325 passed, 1 failed, 326 total** in 23 m 2 s
(`artifacts/sec1-b2-browser-full`). The one failure was `SessionRenewalBaselineTests.F9_background_polling_does_not_renew_the_sliding_session_and_the_passive_tab_expires`: later-poll 401 and original-ticket replay 401 both succeeded, then `WaitForURLAsync("/login")` timed out at 60 s. Isolated re-run of that test: **1 passed, 0 failed** in 1 m 5 s. Disposition: A3 recovery-navigation flake under suite load, not a B2 product regression. Product proofs on all three engines:

- D2 flipped: a cross-site `location.href = '{api}/signout'` carries the session cookie, gets 405 and no `Set-Cookie`; `auth/me` is 200
  afterwards.
- A cross-site sign-out form (`Origin: 127.0.0.1`, no cookie on Chromium/Firefox) and a same-site sibling form (`Origin: localhost:{other}`,
  cookie attached) are both 403 with no `Set-Cookie`; `auth/me` is 200 afterwards.
- A cross-site navigation to `/signout-oidc?sid=…&iss=…` carries the cookie, clears nothing; `auth/me` is 200 afterwards. The path is not a live `RemoteSignOutPath`.
- A cross-site navigation to `/signin/step-up` or `/signin/link` arrives with `Sec-Fetch-Site: cross-site` (WebKit included) and is 403.
- The account menu's sign-out is `POST /signout` with `Origin` = the UI origin, no `X-Apperture-Request`, `Sec-Fetch-Site: same-site`,
  302 to the UI's `/login`, and `auth/me` is 401 afterwards. The account-unavailable surface's sign-out (F3 test) also lands on `/login`.

Host proofs (`BrowserRequestTrustHttpTests`, real pipeline): trusted-origin sign-out without the header is 302 to `/login`; missing,
`null`, sibling and cross-site origins (and a trusted origin marked cross-site) are 403 with no `Set-Cookie`; `GET /signout` is 405;
cross-site step-up/link are 403 and `same-site`/`same-origin`/`none`/absent are not; `GET /signin` stays open; the directory searches,
rejected (no origin, or a sibling origin) or sent as the old GET (405), make **no** Graph call and write **no** discovery outcome, and the
trusted POST makes exactly one call and records the outcome. Unit proofs (`ManagementAuthEndpointsTests`, `EntraStepUpCallbackTests`): no GET
or form POST to `/signout-oidc` ends a local session or a synthetic `sid`+`iss` session, with Entra on or off; a real Entra sign-in keeps
`sid` and not `iss`, and the same historical path does not clear that ticket. The effective options have `RemoteSignOutPath.HasValue == false`.

### 45.9 Findings

| Id | Status |
|---|---|
| F12 | **Fixed.** G1/G2 are POST under Rule U; `GET /signout` is gone |
| D2 | **Fixed.** Sign-out is a trusted POST; `RemoteSignOutPath` is empty (§45.3) |
| F13, F14, F15, F16 | **Fixed in SEC-1B3 (§46).** |
| I-2 | **Open.** SEC-1B4 / closure |

Residuals, accepted: a same-site sibling can still navigate the operator to `/signin/step-up` (an audit append and a Microsoft prompt) or
`/signin/link` (a Microsoft prompt that can only link the operator's own Microsoft identity); both are auth navigation under §18.1 and Rule N
as approved. Browser engines without Fetch Metadata get no Rule N refusal on those two GETs. Entra/Microsoft front-channel single sign-out
does not terminate an Apperture session: `RemoteSignOutPath` is empty, and the SameSite=Lax cookie is not sent on Microsoft's cross-site
iframe anyway (§45.3). Sessions end through `POST /signout` or expiry.

SEC-1 is not complete.

---

## 46. SEC-1B3 — CORS, cookie and host cleanup (2026-09-26)

Scope: F13, F14, F15, F16, REQ-14–REQ-18, and the B2-deferred rule that a configured OIDC `CallbackPath` under `/api`
must not become a Rule U exemption. Not in this slice: browser-suite CI (I-2 / SEC-1B4), forwarded-headers / public-origin
topology beyond the Production `ManagementUiUrl` refusal, SameSite=None, RemoteSignOutPath, IAM-8, or navigation grouping.
SEC-1 is not complete.

### 46.1 UiDev CORS

Registered and applied only when `IHostEnvironment.IsDevelopment()`. The allowed origin is the exact configured
`ShellUi:ManagementUiUrl` (development fallback `http://localhost:9001`). The phantom `https://localhost:9001` origin is
gone. Methods are `GET, POST, PUT, PATCH, DELETE`. Headers are `Content-Type`, `X-Apperture-Request`, and
`X-Apperture-Background`. Credentials stay on. There is no `AllowAnyOrigin` and no `SetIsOriginAllowed`. Production and
`Testing` register no CORS policy. The browser harness adds a test-only copy of the same policy against its ephemeral UI
origin; that registration cannot be activated by a production composition.

### 46.2 Production `ManagementUiUrl`

`ManagementUiRedirects.EnsurePublicOrigin` runs at host startup. Production and Staging refuse a missing, loopback, HTTP,
or non-origin value and name `ShellUi:ManagementUiUrl` in the exception. Development keeps the localhost fallback.
`Testing` is the integration/browser HTTP topology and is not subjected to the public-origin refusal (plain-http hosts
cannot satisfy it). This closes deferred-public-origin item 1 for `ManagementUiUrl` in Production.

### 46.3 Cookies

`ManagementCookiePolicyPostConfigure` sets `SameSite=Lax` and `HttpOnly` on every cookie-authentication scheme. Secure is
`Always` in Production and Staging, `SameAsRequest` in Development and Testing. Lifetime and sliding behavior are
unchanged. OIDC correlation/nonce cookies stay on framework defaults (`SameSite=None; Secure`). The pending-MFA cookie
follows the same Secure rule. SameSite is not `None`.

### 46.4 Host hygiene

- `Apperture.Management.Api` is a class library (`OutputType=Library`, no `Program.cs`, no launch profile). The
  `.vscode` "Management API" launch entry and the stub host test are gone. `generate-openapi.ps1` points at Management.Host.
  AppHost already hosted Management.Host. D7 STOP was not hit: there is no supported standalone API deployment.
- Ui.Server no longer writes `debug-4bd3d8.log`. The `_Layout` agent-log script and every `appertureShell.debugLog` call
  are gone.
- Both Management.Host and Ui.Server emit `X-Frame-Options: DENY` and `Content-Security-Policy: frame-ancestors 'none'`.

### 46.5 Callback-path fail-closed

`WithCallbacks` adds a live OIDC callback path only when it is not under `/api`. `RemoteSignOutPath` is still empty and
is still not exempted. `/signin-oidc` and `/signout-callback-oidc` remain protocol paths. A mistaken
`CallbackPath=/api/auth/local/login` does not exempt that mutation from Rule U.

### 46.6 Evidence

`dotnet build Apperture.slnx`: 0 errors, 47 pre-existing warnings (`artifacts/sec1-b3-slnx`).

UnitTests **3 199 passed**, 3 failed, 3 202 total (`artifacts/sec1-b3-unit3`). The three failures are `Epic77UatDocumentationTests`, which walk five levels from `AppContext.BaseDirectory` and therefore resolve `tests/` (or the repo parent) when `BaseOutputPath` is used; they are unrelated to B3 and pass under the default `bin/Debug/net10.0` layout. A first solo artifacts run had aborted after 2 098 passed with a test-host memory-mapping crash during parallel validation (`Failed to create RW mapping for RX memory`); the serialized re-run above is the counted result. An earlier default-bin attempt was blocked by `Apperture.RuntimeHost.Windows.WorkerHost` file locks. Cookie isolated-composition plus SameSite guard: 6/6 (`artifacts/sec1-b3-cookie`).

ArchTests **38**. Ui.Shared.Tests **1 395** (was 1 393; `DebugInstrumentationGuardTests` and the Ui.Server debug-file fact). Ui.Client.Tests **333**.

IntegrationTests **282 passed, 0 failed** in 9 m 33 s (collections serialized, `artifacts/sec1-b3-int-full`). One fewer than SEC-1B2 because the Management.Api stub-host test was removed. SQL execution, read from `apperture-test-sql` without a credential on the command line: `Apperture_IntegrationTests` last user update `2026-09-26 20:01:09` → `22:49:24`; `Apperture_ManagementGovernanceTests` `19:55:47` → `22:42:21`; server UTC `22:49:54`.

Browser suite (`APPERTURE_BROWSER_TESTS=1`, Chromium, Firefox, WebKit): first full run **324 passed, 2 failed, 326 total** in 32 m 45 s (`artifacts/sec1-b3-browser`). Both failures were `HarnessIsolationGuardTests` reacting to a temporary `InternalsVisibleTo` `Apperture.BrowserTests` on Management.Host (needed only while `UiDevCors` was internal). `UiDevCors` is public; that friend-assembly entry is gone. Isolation re-run: **4 passed** (`artifacts/sec1-b3-iso`). No F9 flake in this run. B3 does not invert browser session assertions; the suite confirms harness CORS and existing B1/B2 proofs still hold. I-2 (browser suite gated off in normal CI) stays open.

### 46.7 Findings

| Id | Status |
|---|---|
| F13 | **Fixed.** UiDev CORS is Development-only |
| F14 | **Fixed.** Management.Api is a class library |
| F15 | **Fixed.** Ui.Server debug write and agent-log calls are gone |
| F16 | **Fixed.** SameSite=Lax is explicit; Secure is Always outside the local HTTP topology |
| I-2 | **Wired in SEC-1B4 (§47).** First remote run failed at host startup (DPAPI); Linux suite execution remains unproven. *(Status at SEC-1B3; closed in §47.16.)* |

SEC-1 is not complete.

---

## 47. SEC-1B4 — browser security CI gate (2026-09-26)

Scope: I-2. The existing `Apperture.BrowserTests` suite becomes a blocking GitHub Actions job. Not in this slice:
SEC-1C (ADR/closure audit), manual real-Entra or Safari smoke (§29.3 M / §38.6), HTTPS/`Secure` browser topology
(the harness stays plain-http `Testing`; B3 already proved those at host configuration), SameSite=None, RemoteSignOutPath,
IAM-8, or navigation grouping. Characterizations assigned to A1–B3 are already flipped; B4 does not reopen them.
SEC-1 is not complete.

### 47.1 Job

Workflow: `.github/workflows/ci.yml`. Job id and check name: **`browser-security`**. Triggers: push and pull_request
to `main` and `master` (the same events as `build-and-test`; `master` was added so the gate fires on this repository's
historical default branch). Timeout: 60 minutes. No `continue-on-error`, no ignored exit codes, no retries.

`build-and-test` still runs `dotnet test Apperture.slnx` **without** `APPERTURE_BROWSER_TESTS`, so isolation guards and
`BrowserSecurityCiGuardTests` stay in the fast job and the suite stays opt-in there.

### 47.2 How it runs

Ubuntu runner, one SQL Server service container (same image and wait pattern as `build-and-test`). The job builds
`tests/Apperture.BrowserTests/Apperture.BrowserTests.csproj` (Release), installs Playwright 1.63.0 browsers
(`chromium`, `firefox`, `webkit`, `--with-deps`) into `~/.cache/ms-playwright` (cached by OS + package version), then:

```text
APPERTURE_BROWSER_TESTS=1
APPERTURE_TEST_SQL_CONNECTION_STRING=Server=127.0.0.1,1433;User Id=sa;Password=…;TrustServerCertificate=True
dotnet test tests/Apperture.BrowserTests/Apperture.BrowserTests.csproj -c Release --no-build
```

The harness allocates ephemeral `localhost` / `127.0.0.1` origins, owns `Apperture_BrowserTests`, registers test-only
CORS against the UI origin, and keeps `RemoteSignOutPath` disabled. One run at a time (existing exclusive lock). Expected
runtime is about 30–35 minutes after browser install.

On failure the job uploads `artifacts/browser-security` (TRX). The harness does not write Playwright traces or
screenshots to disk; in-memory `PageTrace` diagnostics stay in the test log. The SA password is a CI-only service
password, already used by `build-and-test`; TRX is test names and outcomes, not cookies or connection strings.

### 47.3 Branch protection

Repository YAML cannot mark the check required. After the first green remote run, maintainers should require the
stable check name **`browser-security`**. That was done: the `master` ruleset now requires both `build-and-test`
and `browser-security`. GitHub does not currently enforce it on this private repository (§47.16).

### 47.4 Evidence

Local: `ci.yml` parses as YAML. Always-on BrowserTests (no `APPERTURE_BROWSER_TESTS`): isolation, CI-job, and secret-protector guards passed in this remediation (8: `HarnessIsolationGuardTests` 4, `BrowserSecurityCiGuardTests` 1, `BrowserHarnessSecretProtectorTests` 3). `ReturnTargetCorpusPolicyTests` (63) were not re-run here. `actionlint` is not installed in this workspace.

Local remediation (Windows): `SqlSecretsRegistrationTests` + `DpapiSecretProtectorTests` **8 passed**; `HarnessSmokeTests` with `APPERTURE_BROWSER_TESTS=1` **2 passed** (the live host resolved `BrowserHarnessSecretProtector`, not DPAPI). This proves the revised composition on a Windows development machine. It does **not** prove Ubuntu.

The first GitHub Actions execution of `browser-security` **ran** and is recorded in [§47.6](#476-first-remote-run--dpapi-host-startup-2026-09-27). It is not a remote proof of the 327-test suite. I-2 was not remotely proven at that point; §47.16 records the later proof.

### 47.5 Findings

| Id | Status |
|---|---|
| I-2 | **Browser job proven.** The second Ubuntu `browser-security` run passed. The fifth `build-and-test` reached the SQL structure-browser unreachable proof, which returned Failed instead of RemoteUnavailable (gate §47.10). The pull request is not green. SEC-1C has not started. |
| I-2 (final) | **Closed by SEC-1B4.** The final pull-request workflow was green: `build-and-test` and `browser-security` both passed on Ubuntu (§47.16). Automatic branch-rule enforcement is a separate, plan-dependent matter. |

### 47.6 First remote run — DPAPI host startup (2026-09-27)

The first Ubuntu `browser-security` run reached the BrowserTests execution step. Management.Host failed during `BrowserHarness.InitializeAsync` / `LiveManagementHost.CreateHost`:

```text
System.PlatformNotSupportedException:
Sql-backed secrets with DPAPI protector are supported only on Windows.
```

`Program.cs` calls `AddAppertureSqlSecrets()` with no protector. The default path selected `DpapiSecretProtector` and threw at **registration** on Linux, before `LiveManagementHost.ConfigureServices` could replace `ISecretProtector`. Reported totals (327 tests, 68 passed, 259 failed, ~9 seconds) are cascading host-startup failures, not 259 independent browser security assertions.

This is CI/harness portability, not a failed browser security assertion. Remediation is test-scoped: `LiveManagementHost` replaces `ISecretProtector` with `BrowserHarnessSecretProtector` (AES-GCM, BrowserTests assembly only). Registration of the non-Windows default no longer throws (so WebApplicationFactory can replace the descriptor); a hosted startup validator then probes the **final** `ISecretProtector` so a real host without a supported protector still cannot start. Windows still defaults to DPAPI. `Program.cs` still calls `AddAppertureSqlSecrets()` with no arguments. There is no plaintext, `Testing`, or CI fallback.

The `browser-security` job stays on Ubuntu, still installs Chromium, Firefox and WebKit, and still has no `continue-on-error`. Linux execution of the 327-test suite remains unproven until the next Actions run succeeds. SEC-1C has not started.

SEC-1 is not complete.

### 47.7 Second remote run — browser-security passed, build-and-test failed (2026-09-28)

The second Ubuntu workflow run is the remote proof of `browser-security`. That job passed. The same workflow's existing `build-and-test` job failed. §47.6 stays the record of the first failed `browser-security` run. SEC-1C has not started. The pull request is not merge-green.

`build-and-test` runs `dotnet test Apperture.slnx -c Release --no-build --verbosity normal`. IntegrationTests reported **282 total, 77 passed, 205 failed**. Those totals are not 205 independent authentication failures. Expanded test cases partition exactly as:

| Bucket | Cases | Remote result |
|---|---:|---|
| `tests/Apperture.IntegrationTests/ManagementHost` (both host fixtures) | 205 | failed |
| Every other IntegrationTests class | 77 | passed |

The 77 include the SQL Server integration classes, so SQL readiness is not the primary failure. The 205 are every test whose collection fixture starts `ManagementHostWebApplicationFactory`. One host-startup failure is reported once per test.

`ManagementHostWebApplicationFactory` did not replace `ISecretProtector`. `Program.cs` still calls `AddAppertureSqlSecrets()` with no protector. On Ubuntu the default descriptor throws `PlatformNotSupportedException` when resolved, and `SecretProtectorStartupValidationHostedService` resolves it during host start. `LiveManagementHost` already replaces that descriptor, which is why `browser-security` could pass on the same OS. Before §47.6's remediation, `AddAppertureSqlSecrets()` threw at registration on non-Windows, so this Management Host could not start on Linux then either. The remediation did not introduce the IntegrationTests gap; it closed the gap only in BrowserTests.

Copilot's OIDC correlation, `message.State`, ticket-expiry, and data-protection notes are symptom-level. IntegrationTests do not run an OIDC sign-in round trip. `BrowserRequestTrustHttpTests` posts an empty body to `/signin-oidc` and asserts that path is not a request-trust rejection; the OIDC handler logs a missing state when that request is reached. Those lines are not the earliest failure. The host tests that never touch `/signin-oidc` are in the same 205. No CI data-protection key ring, retry, parallelism change, or SQL sleep change follows from this.

The known IntegrationTests environment-variable race (`SqlServerTestConfigurationTests` mutating process environment while another collection resolves SQL) is a separate, earlier defect. A parallel run earlier in the same session failed four structure-browse assertions, not 205 (§44.7). "Collections serialized" in §44.7 and §46 names that counted local IntegrationTests invocation. The tree has no `xunit.runner.json` and no assembly-level `DisableTestParallelization`. `SqlServerIntegration` and `BootstrapHttp` set `DisableParallelization = true`. `ManagementHostGovernance` does not, so under default xUnit settings it can run beside `SqlServerTestConfigurationTests`. The exact serialized command was not recovered from the tree. CI does not pass an xUnit parallelization switch. `dotnet test` on a solution can run test projects concurrently, each in its own process; that does not share IntegrationTests' process environment and does not explain a failure set that is exactly the Management Host cases.

Remediation is test-scoped and isolated from BrowserTests: `ManagementHostWebApplicationFactory` replaces `ISecretProtector` with `IntegrationTestSecretProtector` (AES-GCM, IntegrationTests assembly only). Production registration is unchanged: Windows still defaults to DPAPI; a non-Windows host with no replacement still fails startup validation. No plaintext, `Testing`, or CI fallback. `ci.yml` is unchanged. Another Ubuntu `build-and-test` run is required before B4 can close. `browser-security` does not need a product change for this defect; the workflow as a whole is still not green until `build-and-test` passes.

### 47.8 Third remote run — Entra linking setup returned 403 (2026-09-28)

The third Ubuntu `build-and-test` is the remote proof that §47.7's test protector lets Management Host start. `browser-security` had already passed on the second run and was not the failing job. IntegrationTests failures fell from 205 to the Entra linking setup. The reported set was 16 failures in `ManagementHostEntraLinkingHttpTests`: `CreateLocalUserAsync` posts `POST /api/users` and received 403 where the scenario expects 201. SEC-1C has not started. The pull request is not green.

That 403 is not, by itself, an authorization failure. Rule U (`BrowserRequestTrustMiddleware`) also returns 403, before authentication, as `application/problem+json` with `code=request_origin_rejected`. `UsersController.Create` returns 403 from `Forbid()` when `UserManagementService.RequireUserManageAsync` throws `UnauthorizedAccessException`. That response has an empty body. The two are distinguishable.

`CreateLocalUserAsync` uses the factory client. `ConfigureClient` sets `Origin: http://localhost:9001` and `X-Apperture-Request: 1`. The factory pins `ShellUi:ManagementUiUrl` to that same origin. The test client does not send `Sec-Fetch-Site`. Authentication is the `X-Test-External-Id` test scheme, not a session cookie. The same default headers are what other Management Host unsafe requests use, including the Entra tests that returned 201 in the same run. A Rule U rejection would have failed those requests too, and the body would have been problem+json. The captured 403 body was empty, so the request passed Rule U and the controller forbade it.

`EnsureAdminClientAsync` does not select the platform administrator. If any user exists it skips first-admin bootstrap, then authenticates as the lexicographically first local `IdentityLinks.ExternalId`. `user.manage` is held by `role-platform-administrator`, which first-admin assigns. Ordinary users created later (`dup-target-*`, and any `aaa-*` name) sort ahead of `entra-admin-*`. The next `POST /api/users` is then an authenticated local user without `user.manage`. Copilot's missing-permission observation matches that principal. The suggested error handling, and any new grant, do not. The endpoint requirement was already correct.

The class source has 11 facts, so it cannot emit 16 distinct xUnit failures. A fresh `Apperture_BootstrapHttpTests` catalog, running only this class on Windows, failed 6 and passed 5. The 6 failures are `CreateLocalUserAsync` calls. The passes were `Duplicate_entra_link_on_same_user_is_idempotent` (it created `dup-target-*` while `entra-admin-*` was still first), `Admin_association_is_pending_until_the_operator_signs_in`, `Caller_without_user_manage_gets_forbidden`, `Add_entra_link_to_missing_user_returns_not_found`, and `Unlinked_entra_principal_returns_forbidden_on_auth_me` (that one expects 403 from `GET /api/auth/me`). End state of that catalog: `dup-target-cd7a7` was not a platform administrator; `entra-admin-9678` was. The remote log was not retrieved here, so the extra count beyond those 11 facts is not reconstructed. The 403 mechanism is.

The latest local Windows IntegrationTests run of 286/286 used the long-lived catalog. Before it was dropped for this reproduction, its first local external id was `acct-admin-78819` and that user held `role-platform-administrator`. `acct-admin-*` sorts ahead of `dup-target-*`, so `EnsureAdminClientAsync` kept selecting an administrator. CI SQL is a new container, so no such row exists at the start. xUnit 2.9.3 orders cases by `UniqueID` (ordinal, stable for the same assembly), not by operating system. This is not an Origin, HTTPS, or header difference, and it is not the `ManagementHostGovernance` / `SqlServerTestConfigurationTests` environment-variable race: that race is a different collection, and this failure follows identity rows inside `BootstrapHttp`.

Remediation stays in the test helper. `EnsureAdminClientAsync` now takes a local identity whose user has `role-platform-administrator`. A regression fact creates an earlier non-admin username and requires the next admin client to create another user. On the catalog that still had `dup-target-*` first, that class plus `BrowserRequestTrustHttpTests` passed 33, and the `BootstrapHttp` collection then passed 77. Production authorization, Rule U, Origin checking, and `X-Apperture-Request` are unchanged. `ci.yml` is unchanged. Another Ubuntu `build-and-test` is required before B4 can close.

### 47.9 Fourth remote run — no platform administrator to select (2026-09-28)

The fourth Ubuntu `build-and-test` still fails in IntegrationTests. `browser-security` is unchanged from the second run. SEC-1C has not started. The pull request is not green.

`EnsureAdminClientAsync` now queries a local identity whose user has `role-platform-administrator`. On Ubuntu that query returned no row and `.FirstAsync()` threw `Sequence contains no elements`. Replacing that with `FirstOrDefaultAsync` would only change the exception text. The catalog had reached a state the helper did not handle.

`FirstAdminBootstrapService.TryCreateFirstAdminAsync` returns `AlreadyExists` when `GetUserCountAsync` is greater than zero. `BootstrapSetupStatus.FromUserCount` sets `AllowsFirstAdmin` to false for the same gate. `POST /api/bootstrap/first-admin` then returns 409 `already_initialized`. Ordinary users with no platform administrator do not reopen bootstrap. An empty user table does. An existing platform administrator does not.

`EnsureActiveRegistrationAsync` writes the Entra registration row only. It does not create users.

`ManagementHostNotificationToastDeltaHttpTests.CreateUserClientAsync` inserts a local user and identity link directly, with no role assignment, and does not call first-admin. It shares `Apperture_BootstrapHttpTests` with the Entra tests. `ManagementHostBootstrapHttpTests.EnsureNoUsersAsync` can also delete every user, including the administrator, which makes the next direct insert the first platform user. Profile and account helpers call first-admin when the user table is empty before they add a second user.

Local sequence on a dropped catalog: one toast test that calls `CreateUserClientAsync`, then one Entra test. The catalog then held one user, local external id `toast-delta-0857f59392e2`, and zero `role-platform-administrator` rows. `Admin_can_add_entra_link_to_existing_user` threw `Sequence contains no elements` from `EnsureAdminClientAsync`. The helper's `Users.AnyAsync()` check skipped first-admin because a user existed. The same DbContext observed that committed user; it did not hide an administrator. There was no administrator row to read.

The helper now looks up the platform administrator in its own scope. When none exists and the user table is empty, it calls `POST /api/bootstrap/first-admin` and reads the new identity from a new scope. When users already exist, it throws `InvalidOperationException` stating that first-admin bootstrap is closed. The toast user factory calls that same helper before inserting an ordinary user, so an empty catalog receives its administrator through the product endpoint. After that change, the same toast test on a dropped catalog left `boot-admin-37ae7` as platform administrator and `toast-delta-4e3b33f38380` as an ordinary user. The closed-gate test removes the product-assigned role rows, expects that exception, and restores those rows so the shared catalog is not left without an administrator. It does not grant the role to the ordinary user. The `BootstrapHttp` collection then passed 78. Production bootstrap and authorization are unchanged. `ci.yml` is unchanged. Another Ubuntu `build-and-test` is required before B4 can close.

### 47.10 Fifth remote run — unreachable SQL classified as Failed (2026-09-28)

The fifth Ubuntu `build-and-test` progressed past bootstrap ordering. Copilot reported one actionable assertion: `ManagementHostConnectionToolsHttpTests.An_unreachable_server_is_a_200_with_a_fixed_unavailable_outcome` expected `outcome=RemoteUnavailable` and received `Failed` for server `127.0.0.1,1`, after about 15 seconds. The remote log was not retrieved here, so that single-assertion count is not independently confirmed. Expected error logging from deliberate negative SQL tests is ignored unless it is a failed assertion. SEC-1C has not started. The pull request is not green. The concurrent `browser-security` job is evidence of its own and is not inferred from this job.

The contract is already that an unreachable or slow SQL endpoint is `RemoteUnavailable` (`ConnectionToolOutcome`, `Classify` comments, and the Services closure's "real closed-port outage"). Login denial is `RemoteDenied`. Unrecognised errors are `Failed`. The HTTP body is a fixed sentence and must not contain the server address or SQL text.

Local Windows `Microsoft.Data.SqlClient` 7.0.3 against that endpoint: outer `SqlException` Number **1225**, one `SqlError` Number **1225**, inner `Win32Exception`, flags refused/network not timeout, connect-timeout bound 15 seconds. 1225 is already in `ClassifySqlNumber` as `RemoteUnavailable`, which is why Windows IntegrationTests pass. Ubuntu's equivalent chain was not captured. SqlClient's own issues document that Linux often surfaces the same transport failure as `SqlException.Number = 0` with an inner `SocketException` (`dotnet/SqlClient` #1773, #1902, #2103). `ClassifySqlNumber(0)` is intentionally `Failed`. The pre-fix classifier returned that result as soon as the `SqlException` numbers were unclassified and never inspected the inner exception. That is a classifier portability gap, not a reason to change the closed-port fixture.

Copilot's suggested error-number list is not applied wholesale. The Windows number was already mapped. The change is to walk inner `TimeoutException`, leftover `OperationCanceledException` (caller cancel is still filtered before `Classify`), `SocketException.SocketErrorCode` for refused/timeout/unreachable/reset, and `Win32Exception.NativeErrorCode` only when that number is already an unavailable SQL/transport code. POSIX errno values 111, 110, 104, 101, and 113 are added to `ClassifySqlNumber` because SqlClient may copy them onto `Number` on Linux. Generic provider 35 is not mapped. Unknown failures remain `Failed`. Login numbers are unchanged. Messages stay the fixed sentences.

The `127.0.0.1,1` HTTP proof is retained. `ci.yml` is unchanged. Another Ubuntu `build-and-test` is required before B4 can close.

### 47.11 Sixth remote run — toast-delta partial composite cursor (2026-09-28)

The sixth Ubuntu `build-and-test` progressed past SQL transport classification. Copilot reported IntegrationTests **282 passed / 6 failed / 288 total**, with the observed exception:

`System.ArgumentException: afterId must be provided when afterCreatedAtUtc is set; the composite cursor requires both or neither. (Parameter 'afterId')`

from `SqlNotificationUserInboxStore.ListToastDeltaAsync` / `MeNotificationsController.ToastDelta`. The remote log was not retrieved here (`gh` is not on PATH), so the six xUnit names are not independently confirmed. Expected error logging from deliberate negative tests is ignored unless it is a failed assertion. SEC-1C has not started. The pull request is not green. The concurrent `browser-security` job is evidence of its own and is not inferred from this job.

The product contract is already both-or-neither. Neither parameter is a valid bootstrap (tip) request. Both parameters are a valid continuation. Timestamp-only and id-only are malformed. `INotificationUserInboxStore` and `SqlNotificationUserInboxStore` keep that invariant because a timestamp without `afterId` would translate to SQL `[Id] > NULL` and drop same-timestamp ties, and an id without a timestamp would silently fall through to bootstrap. `ManagementClient` and `NotificationToastWatcher` already refuse a mismatched pair. ADR-0042 already says a mismatched pair is HTTP 400. The HTTP tests' `Mismatched_cursor_pair_is_rejected_with_400` is the only intentional timestamp-only request; it expected the store `ArgumentException` to surface through `ManagementHostExceptionHandler`, which maps `ArgumentException` to 400 and copies `argument.Message` into ProblemDetails `detail`.

Copilot's recommendation to add controller validation is the right *layer* for a malformed HTTP cursor, but xor-checking the bound `DateTimeOffset?` is not sufficient and can be the wrong check. The class has nine facts. The six that would throw this exact store exception on a no-query GET are the 200-expecting store-hitting cases (`Fresh_user_*`, `Cursor_round_trip_*`, `Non_toast_eligible_*`, `Deleted_definition_*`, `One_users_*`, `Calling_the_endpoint_*`). Those tests send `/api/me/notifications/toast-delta` with no query, or `GetToastDeltaAsync(null, null)` which builds that same path. A naive controller check of `(afterCreatedAtUtc is null) != (afterId is null)` would turn a binder-invented timestamp into HTTP 400 and those tests would still fail. The HTTP boundary now resolves the cursor from **query-string presence**: omitted or blank keys are bootstrap even if the binder supplied a timestamp; a real one-sided pair returns 400 ProblemDetails with a fixed detail (`afterCreatedAtUtc and afterId must both be provided or both omitted.`) and never calls the store. That response must not contain `Parameter 'afterId'` or the store type name.

The test helper `GetToastDeltaAsync` previously added `afterCreatedAtUtc` whenever it was non-null but omitted `afterId` when it was whitespace, unlike `ManagementClient`. Continuation tests in this class pass `NextAfterId` from the previous response, which is a non-empty inbox id when rows were scanned. That helper asymmetry is closed so a lost id cannot silently become a timestamp-only URL. It is not a substitute for the query-presence check on first polls.

Run 5's toast/bootstrap ordering change (`EnsureLocalExternalIdAsync` before inserting `toast-delta-*`) does not add cursor query parameters, change inbox ownership filtering, or change the composite-cursor contract. It can change which rows exist in the shared catalog and when first-admin runs. The six failures are the cursor reaching the store as `(timestamp, null)`, not a missing administrator. Local Windows `BootstrapHttp` was 78/78 after run 5 on a catalog that already had `boot-admin-*`; a dropped-catalog toast-delta class run on Windows completed with exit 0 (the prior verbose capture truncated at compile). Windows does not reproduce this `ArgumentException` on no-query GETs. The Ubuntu-only remaining defect is therefore a public-boundary validation / query-binding issue, not a new store pagination rule.

The store invariant is unchanged. `ci.yml` is unchanged. Another Ubuntu `build-and-test` is required before B4 can close.

### 47.12 Seventh remote run — not a truncated CI password (2026-09-28)

The seventh Ubuntu `build-and-test` (`0e343cb`, [run 36499042512](https://github.com/Intechral-Solutions/Apperture-Core/actions/runs/36499042512)) reports IntegrationTests **283 passed / 6 failed / 289 total**. Copilot attributed that to SQL Error **18456 State 8** (`Login failed for user 'sa'`) and to a truncated `APPERTURE_TEST_SQL` expression (`env.APPERTURE_CI_MSSQL_SA[...]`). Neither is the Test-step cause. `ci.yml` is unchanged since `a79a1ba`; no B4 commit edited it. SEC-1C has not started.

**Workflow (redacted).** Job env sets `APPERTURE_CI_MSSQL_SA_PASSWORD` and `MSSQL_SA_PASSWORD` to the same value. The SQL service uses `MSSQL_SA_PASSWORD: ${{ env.APPERTURE_CI_MSSQL_SA_PASSWORD }}`. The Test step sets `APPERTURE_TEST_SQL: ${{ format('Server=127.0.0.1,1433;User Id=sa;Password={0};TrustServerCertificate=True;Initial Catalog=Apperture_ManagementGovernanceTests;', env.APPERTURE_CI_MSSQL_SA_PASSWORD) }}`. The identifier is the full `APPERTURE_CI_MSSQL_SA_PASSWORD` token, not a truncated name. GitHub UI/Copilot wrapping can look like `SA[...]`.

**Password hypothesis disproven.** A globally wrong SA password cannot explain 283 passing IntegrationTests. Management Host started (Launch/Start actions executed). Catalogs were created and migrated. Toast-delta HTTP (10/10) passed against SQL. The Test process contains **no** `SqlException` and no 18456 under the six Failed blocks. The only Test-process 18456 line is the passing unit map `SF8_sql_error_numbers_map_to_the_remote_systems_own_answer(number: 18456, expected: RemoteDenied)`. Container 18456 State 8 is expected from deliberate invalid-credential proofs (`A_login_the_database_refuses_*` uses `definitely-wrong-*`; missing-database proofs use another catalog). Do not change `ci.yml`.

**Toast-delta (run 6) is fixed on Ubuntu.** All ten `ManagementHostNotificationToastDeltaHttpTests` facts Passed, including no-cursor bootstrap, timestamp-only 400, id-only 400, and cursor round-trip.

**The six IntegrationTests failures are two clusters, not one:**

1. Five `ManagementHostGovernanceHttpTests` POSTs returned **403** where 202 or 400 is required: `Post_integration_procedure_launch_workflow_and_operation_accepted_when_runnable` (line 883), `Get_run_after_procedure_launch_shows_provenance_and_start_parameters_when_runnable` (920), `Workflow_start_with_integration_id_returns_bad_request_when_integration_inactive` (450), `Operation_start_with_integration_id_returns_bad_request_when_integration_inactive` (482), `Workflow_start_without_integration_id_returns_bad_request` (514). Not Rule U (`request_origin_rejected` absent; Launch is `ObjectResult` `application/json` two-string `{code, error}`; Start is `ForbidResult` with the usual `AuthenticationScheme: Cookies was forbidden` log). Adjacent HelloWorld procedure-list, invalid-parameter launch (400 before start-auth), and `Operation_start_without_integration_id` (400 before start-auth) Passed. Authoring create/deactivate in the same facts returned 201/200. `0e343cb` does not touch launch/start authorization. Those five facts were not among run 6's six failures.

2. `ManagementHostConnectionToolsHttpTests.An_unreachable_server_is_a_200_with_a_fixed_unavailable_outcome` returned HTTP 200 `outcome=Failed` after ~14.5s instead of `RemoteUnavailable` (same assertion as run 5). No exception type/`SqlException.Number` in that FAIL window. Run 6's six failures were toast-delta only, so this proof passed then. `Classify` still leaves unclassified `SqlException.Number = 0` as Failed by policy. Do not map 0 to unavailable without an inner transport signal.

**Other slnx failures in the same job (not Copilot's 6):** thirteen `Apperture.Management.Ui.Shared.Tests` link/breadcrumb facts. Separate from IntegrationTests and from 18456.

**browser-security** on this run **passed** (~23 min).

Governance status assertions now include the response body so the next Ubuntu run records the launch `code`. `ci.yml` is not edited. Another Ubuntu `build-and-test` is required before B4 can close.

### 47.13 Local follow-up — Ui.Shared file-URI, governance still opaque, SQL shape log (2026-09-28)

The thirteen `Apperture.Management.Ui.Shared.Tests` failures are one cause. On Unix, `Uri.TryCreate("/runs/r1?from=x", UriKind.Absolute)` succeeds as a `file` URI and percent-encodes `?` and `#` into `AbsolutePath`, leaving `Query` empty. `AffectedRecordNavigation.TryGetPathAndQuery` and `ManagementShellOrientationTracker.GetObservationKey` used that result. Consequences, all matching the run-7 assertions: `WithoutContext` sees no query and returns the original href; `WithContext` appends a new query onto a path that already contains `%3F...`; `TryParse` returns false; breadcrumb observation keys become `/runs/r1%3F...` so Demo Customer context is ignored and parents stay Control Center / Runs. Windows `TryCreate` does not treat that string as absolute, which is why the suite is green locally. The helpers now accept only explicit `http`/`https` absolute URIs. Application paths stay on the string parser. This is not a stale breadcrumb expectation and not a SEC-1 route change.

The five governance 403s are start-authorization, not Rule U and not the BootstrapHttp first-admin helper. `CreateAuthenticatedClient` sets `X-Test-External-Id` to the fixed `test-governance-procedure-launcher`, whose seeded role grants `workflow.execute` and `operation.execute` at `module:Sample.HelloWorld`. Launch returns JSON `{code, error}` from `IntegrationProcedureLaunchErrorCode` (403 is `workflow_start_forbidden` or `operation_start_forbidden`). `WorkflowsController.Start` and `OperationsController.Start` throw `UnauthorizedAccessException` from `EnsureCanStart*` and return `Forbid()`; ASP.NET logs `AuthenticationScheme: Cookies was forbidden` for every `ForbidResult`. Workflow start authorizes **before** the missing-IntegrationId 400. Operation start returns that 400 **before** authorization. That order is the existing controller contract (workflow start auth was placed ahead of validation); it is not changed to make the two tests symmetric. `Operation_start_without_integration_id` therefore passed. A filtered Windows run of the six facts is 6/6, including the five 403s, so a fresh local catalog is not required to explain the Linux file-URI bug. The Ubuntu launch `code` is still not in the run-7 log. The response-body assertions stay so the next run records it. No execute permission is granted.

`127.0.0.1,1` stays the closed-port proof. `Classify` still does not map bare `SqlException.Number = 0` to `RemoteUnavailable`. When classification remains `Failed`, the browser logs `Shape=` with exception type names, `SqlException` Number/Class/State/error numbers, `SocketError`, Win32 native error, and `HResult` only. Exception text, server, and connection string are not included. The fixture is unchanged.

### 47.14 Eighth remote run — Ui.Shared green, governance catalog snapshot, opaque SQL shape (2026-09-28)

The eighth Ubuntu `build-and-test` is `1098364` ([run 36504269534](https://github.com/Intechral-Solutions/Apperture-Core/actions/runs/36504269534)). `browser-security` passed. `Apperture.Management.Ui.Shared.Tests` passed **1395/1395**, so the Unix file-URI remediation is remotely proven. Toast-delta stayed green. Normal SQL connectivity stayed green. SEC-1C has not started. `ci.yml` is unchanged.

IntegrationTests were **283 passed / 6 failed / 289**. The six names are the same five governance Start/Launch facts as run 7, plus `An_unreachable_server_is_a_200_with_a_fixed_unavailable_outcome`. They are not a new set. Launch returned `{"code":"workflow_start_forbidden","error":"Not authorized to start this workflow."}`. Copilot's attribution of that SQL fact to UnitTests was a mixed reading of the log: the fact lives in `ManagementHostConnectionToolsHttpTests`. The one UnitTests failure was `The_provider_constructor_takes_only_the_resolver_the_factory_and_options_and_availability_is_a_constant`, because the temporary `ILogger<PlatformSqlStructureBrowser>` constructor parameter broke the three-argument contract. That logger and its `Shape=` warning existed only to capture the Linux failure. The captured shape was `SqlException Number=0 Class=20 State=0 ErrorNumbers=[0] HResult=0x80131904 | SocketException SocketError=Success ErrorCode=0 HResult=0x80004005`. There is no refused, timed-out, or unreachable transport code in it. `Number=0`, `Class=20`, `HResult`, and `SocketError.Success` are not classified as `RemoteUnavailable`. The logger is removed. The constructor is again resolver, factory, and options.

The five governance 403s reproduce on a fresh Windows catalog. `Apperture_ManagementGovernanceTests` was dropped (previous `create_date` 2026-09-16) and recreated by the fixture at 2026-09-29 01:36:26Z. With permission registration left in its previous order, launch failed with the same `workflow_start_forbidden` body. The launcher identity link, user, role `rolgovernproclaunch00001`, and role JSON (`integration.read`, `workflow.execute` at `module:Sample.HelloWorld`, `operation.execute` at `module:Sample.HelloWorld`) were present. `Sample.HelloWorld` was installed and enabled only after the host had started. `IResourceDescriptorRegistry` did not contain `workflow.execute` / `WorkflowInstance` / `module:Sample.HelloWorld`. `EvaluateAllAsync` therefore fails at `IsKnown`, before role membership or scope containment. `KnownModuleManifestProvider` snapshots enabled modules inside `ModulePermissionRegistrationHostedService`. The fixture's later insert makes the activation filter see the module, so the definition is found and the message is "Not authorized to start this workflow." rather than "Workflow not found". A long-lived local catalog already had the row at host start, which is why earlier Windows runs passed. This is not an Ubuntu authorization difference and not a missing grant on the launcher role.

The governance host now installs and enables `Sample.HelloWorld` in that same hosted-service slot, before `ModulePermissionRegistrar.Register`, and still before platform-admin role repair. The launcher role JSON is unchanged. Controller order is unchanged. A second drop (catalog absent at 2026-09-29 01:37:22Z, recreated at 01:39:25Z) then passed the authority fact, the five Start/Launch facts, the live login-denial fact, and the closed-port HTTP fact (8/8). A third drop of the same catalog, then the governance-host classes together, passed 130/130.

`127.0.0.1,1` is not a cross-platform transport oracle. Windows SqlClient reports 1225 and the classifier returns `RemoteUnavailable`. Ubuntu reports the opaque shape above and stays `Failed`. No local TCP fixture can be assumed to do better, because the captured inner socket already carries `SocketError.Success`. The classifier contract for a real refused or timed-out socket stays in `PlatformSqlStructureBrowserTests`. The HTTP fact now requires 200, empty items, no endpoint text, and the fixed sentence that matches whichever of those two safe outcomes the provider returned. Live login denial stays `RemoteDenied`. POSIX errno values 111, 110, 104, 101, and 113 were removed from `ClassifySqlNumber`; they were added as a possibility that SqlClient might copy them onto `Number`, and run 8 did not show that. Windows and Winsock numbers, including the observed 1225, stay. Another Ubuntu `build-and-test` is required before B4 can close.

### 47.15 Ninth remote run — build-and-test green, one recovery-probe count (2026-09-29)

The ninth Ubuntu workflow is `3af711ec` ([run 36510672867](https://github.com/Intechral-Solutions/Apperture-Core/actions/runs/36510672867)). `build-and-test` passed. `browser-security` failed one fact: `SessionRecoveryBrowserTests.Opening_Account_Security_after_expiry_recovers_with_reason_expired_and_one_navigation`, Chromium (the fact takes the harness default engine; the display name has no engine argument). The job total was 329 passed / 1 failed / 330. `SignInDocuments().ContainSingle()` had already passed, so there was one `/login` document. `RecoveryProbes(before)` contained two `GET /api/auth/me` responses, both 401 and both `fetch`. The trace records no headers and no per-response timestamps. The query assertion did not run. The fact had already seen the text "Your session has expired. Sign in to continue." SEC-1C has not started. B4 is not closed. §47.1–§47.14 stay as written.

That commit does not change browser or session-recovery product code. Its diff is the governance catalog snapshot, the SQL classifier cleanup (the temporary logger removed, POSIX errno values removed from `ClassifySqlNumber`), the closed-port HTTP fact, and the docs. The same Account Security fact passed inside earlier Ubuntu `browser-security` runs, including run 8. Locally, before this trace correction, three isolated Chromium runs were fail, pass, pass. The failure's server log was three `GET /api/auth/me` 401s: sequence 11 with `X-Apperture-Request: 1` and no `X-Apperture-Background`, sequence 13 with neither header, sequence 15 with `X-Apperture-Request: 1` and no background header. The page trace counted the first two, which are before the `/login` document. The passing runs' server log was two calls: the credentials-only probe, then one platform-marked `auth/me` after the document (the sign-in page boot). Final URL on every local run was `/login?returnUrl=/account/security&reason=expired`. One sign-in document. No background marker on any of these calls.

The invariant in §12, REQ-1, and §39.6 is one coordinator probe and one full-page navigation. Account Security calls `Session.RefreshAsync` from `OnInitializedAsync`. That `auth/me` is the application client, so it carries `X-Apperture-Request` and `AuthRecoveryHandler` excludes it. The session raises `UnauthorizedDuringRefresh`, and the coordinator issues one probe on a separate client that has no recovery handler and does not send that header. A parallel 401 can start that probe first and navigate before the page refresh's response is recorded, which is why the old count was sometimes one. When the refresh response lands first, the old count was two. Neither shape is a second recovery decision. The two-second delay in the fact is after the sign-in URL and the expired copy are already visible; `RecoveryProbes` cuts at the first `/login` document, so a longer sleep cannot remove a response already before that document. It stays, because `SignInDocuments` reads the whole trace and the delay is the existing settle for a second navigation. It is not the fix.

`RecoveryProbes` now counts `auth/me` fetches before that document that do not carry `X-Apperture-Request`: the coordinator re-check. The page refresh and the sign-in boot check both carry the header. A second coordinator probe would still be a second item. `ContainSingle()` is unchanged. With the probe response held for two seconds only as a local characterization (that route was removed), the page trace showed both 401s before `/login` — platform header, then no platform header, neither background — and `RecoveryProbes` returned the one credentials-only call. Three unforced runs after the filter, then eight more, passed with that single probe. The cleaned fact, the rest of `SessionRecoveryBrowserTests`, and `SessionExpiryBaselineTests` then passed together, 11/11, on Chromium. Another Ubuntu `browser-security` run was required before B4 could close; it is recorded in §47.16. SEC-1C has not started.

### 47.16 Closeout — final workflow green, ruleset configured but not enforced (2026-09-29)

The latest pull-request workflow on `sec-1-browser-security`, after the remediation sequence recorded in §47.6–§47.15, completed fully green on the GitHub Actions Ubuntu environment:

| Check | Result |
|---|---|
| `build-and-test` | **Passed** |
| `browser-security` | **Passed** |

This is the first branch state on which the complete relevant workflow is green. The Actions run number of that final run is not recorded in this repository and is deliberately not stated here. §47.6–§47.15 are unchanged and remain the historical record: the gate did not pass on its first attempt. It failed at host startup (DPAPI, §47.6), and `build-and-test` then failed through the sequence of causes in §47.7–§47.14. `browser-security` failed once more on the ninth run, on the Account Security recovery fact's probe count (§47.15).

**What is remotely proven.** The `browser-security` job runs on Ubuntu, executes the BrowserTests security suite (Chromium, Firefox and WebKit), fails the workflow when the suite fails (§47.6 and §47.15 are red runs of that job), and passes when the suite is healthy. `build-and-test` passed on the same branch state. I-2 (the browser suite gated off in normal CI) is closed. The stable check names are **`build-and-test`** and **`browser-security`**. `ci.yml` was not edited after `a79a1ba`.

**Branch ruleset.** A GitHub ruleset targeting `master` lists both `build-and-test` and `browser-security` as required status checks. GitHub reports that rulesets are not enforced on this private repository until the organization account is upgraded to GitHub Team. So:

| Layer | State |
|---|---|
| CI enforcement mechanism (`browser-security` fails the workflow when the suite fails) | Implemented and remotely proven |
| GitHub server-side branch-rule enforcement on `master` | Configured, but not enforced under the current plan; GitHub is not currently blocking merges when a check is red |
| Merge policy | Procedural: the project owner does not merge unless both `build-and-test` and `browser-security` are green |

The ruleset is kept in place so that it is enforced automatically if the organization gains the capability or the repository or collaboration model changes. No status-check configuration is missing, and this is not an outstanding SEC-1B4 engineering task.

**SEC-1B4 is complete.** No further B4 code or workflow work remains. SEC-1C (the ADR and closure audit) has **not** started, and SEC-1 as a whole is not complete until SEC-1C. *(SEC-1C is recorded in §48.)*

---

## 48. SEC-1C — documentation and closure (2026-09-29)

Scope, from §32: the ADR, the named documentation updates, the closure audit with evidence, and roadmap status. No product
behavior changed. Not in this slice: the independent final security review, operator smoke, merge, IAM-8, navigation
grouping, and every item in §37.

### 48.1 Delivered

- [ADR-0045](../../../02-decisions/ADR-0045-browser-session-recovery-and-request-trust.md): browser session recovery, request
  trust (Rules U, G, N) and the GET-safety rule, as built.
- [Closure audit](../../../audits/sec1-browser-session-security-closure.md): requirement closure matrix (REQ-1…REQ-19),
  final finding register (F1–F18, D2, I-1, I-2, N1–N6), accepted residuals, deferrals, the cross-slice consistency review,
  the test-only infrastructure audit, CI and ruleset state, evidence reconciliation, and the pending operator smoke
  checklist.
- ADR-0042 addendum (the Toast watcher is background and non-renewing; a 401 goes to session recovery). ADR-0044 status
  note (its POST + JSON measure is superseded as a pattern by ADR-0045). `next-stage-platform-auth.md`,
  `deferred-public-origin-auth-redirects.md` (items 1 and 4 done by SEC-1B3), `management-platform-configuration.md`
  (`ShellUi:ManagementUiUrl` and `BrowserRequestTrust:Mode`), the decisions index, the transitions README (security row;
  the missing `components-services-catalog/` and `resource-contexts/` rows), the BrowserTests README, and roadmap item 7a.
- One §29.7 evidence gap closed: Ui.Server emitted `X-Frame-Options: DENY` and `frame-ancestors 'none'` but no test asserted
  it. `UiServerHostRegistrationGuardTests.The_ui_server_refuses_to_be_framed` now does.
- In this gate: the §0 register carries final statuses (and I-1, I-2), the §32 table marks every slice delivered and records
  the B4 scope as delivered, and §35 points here. §13 now records that safe captured return targets may preserve ordinary
  application-local query state, including `context=all`; N1 is closed as a design clarification. Per-slice finding tables
  (§39.5, §40.8, §43.3, §44.8, §45.9, §46.7, §47.5) are as of their slice and are not rewritten.

### 48.2 Results

Every binding requirement is closed. No unresolved defect was found in the consistency review. Layer substitutions (a §29 B
row evidenced by unit tests or by construction) are listed in the closure audit rather than claimed as browser proof.
**N1** (the View selector's `context=all` in captured return targets) is a closed design clarification, not a security
finding: it is benign on-origin application-local UI state, preserved by the safe-return-target model; §13 records that
and no product-code change was required. N4 (the development client resolves any loopback UI to API port 9000) is a
development-topology observation outside the requirements; Rule U makes a mis-pointed UI fail closed on mutations.

### 48.3 Validation

`git diff --check`. The new guard fact, run with its class: `UiServerHostRegistrationGuardTests` **11 passed, 0 failed**
(repo-local `artifacts/sec1c-uishared`). The full suites were not
re-run: §32 assigns documentation and a closure audit, and REQ-19 requires citing evidence, not regenerating it. The latest
full local run is SEC-1B3 (§46.6); the latest remote proof is §47.16.

### 48.4 Lifecycle

**SEC-1 engineering closure is complete. SEC-1 is not Done.** Remaining, in order: the independent final whole-body
security review, operator smoke (closure audit checklist), and merge with `build-and-test` and `browser-security` green.


