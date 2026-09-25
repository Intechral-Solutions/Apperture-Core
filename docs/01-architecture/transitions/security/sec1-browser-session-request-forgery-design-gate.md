# SEC-1 — Browser Session & Request-Forgery Hardening — design gate

Date: 2026-09-24
Status: **Design gate complete and approved (2026-09-24). Rulings D1–D9 are settled (§33). Implementation is in progress:
SEC-1.0 (browser proof harness and baseline reproduction) is delivered, with its evidence in §0.1 and §38, and SEC-1A1
(centralized expired-session recovery) is implemented and proven in browsers, recorded in §39; SEC-1A2 onward have not
started.** SEC-1 is not complete.
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
  and `resource-contexts/` are pre-existing and left for SEC-1C or a housekeeping pass.
- `UiDev` CORS is not merely "present broadly": it is **applied unconditionally** (`app.UseCors("UiDev")`), with a hard-coded origin list.

---

## 0. Finding register

| ID | Finding | Class | Owning slice |
|---|---|---|---|
| F1 | After startup nothing re-checks the session and no UI code handles 401. In-app expiry shows "401 (Unauthorized)" text or empty states and never starts sign-in. | Code fact. The operator-visible symptom is a **browser finding (pending SEC-1.0)**. | 1A1 — **fixed (§39)** |
| F2 | Hard refresh with a missing, expired or invalid cookie *does* reach `/login?returnUrl=` (§4). | Code fact | — |
| F3 | Valid cookie but unknown/unlinked user: `auth/me` returns 403, the state becomes `ForbiddenUnlinked`, and the protected shell mounts over failing pages. | Code fact. The rendered result is a browser finding. | 1A1 — **fixed (§39)** |
| F4 | API unreachable, or 5xx during startup: state `Error` redirects to login. `GetAccessRequestSessionAsync` maps a 5xx to "signed out". | Code fact | 1A1 — **fixed (§39)** |
| F5 | The return-URL validator accepts `/\evil.example`, and the browser resolves it off-origin (**open redirect**). .NET `Uri` resolves it on-origin (verified in this session). | **Browser finding (pending SEC-1.0)** | 1A2 |
| F6 | With auto-redirect to Entra enabled, `/login?error=…` redirects to Entra again, which can loop. | Code fact (logic). The loop is a **browser finding**. | 1A2 |
| F7 | In production, a cancelled or failed Entra sign-in (`OnRemoteFailure`) is unhandled, so the operator gets a raw 500 `problem+json` document on the API host. | Code fact | 1A2 |
| F8 | Step-up reads the ordinary SameSite=Lax session cookie inside Microsoft's cross-site `form_post` callback. | Code fact (dependency). The failure is a **browser finding (pending SEC-1.0)**. | 1A2 |
| F9 | Background polling renews the 12 h sliding cookie, so a passive tab never idles out. | Code fact (renewal eligibility). The observed lifetime is a browser finding. | 1A3 |
| F10 | The Toast watcher polls forever after a 401 (300 s backoff). `RunDetail` polling hits 401 every 2 s. | Code fact | 1A1 (401 half — **fixed, §39**) / 1A3 (renewal half) |
| F11 | There is no request-forgery layer. 33 bodyless POSTs, one multipart POST and several nullable-body POSTs can be driven by a plain form from any same-site origin. | Code fact. Cookie attachment is a **browser finding**. | 1B1 |
| F12 | Two GETs write Apperture state (Entra directory users/groups). `GET /signout` ends the session (§18.1). | Code fact | 1B2 |
| F13 | `UiDev` credentialed CORS is applied in every environment. | Code fact | 1B3 |
| F14 | `Management.Api` has a standalone `Program.cs` with allow-any-origin CORS, no auth, and a launch profile on the API's port 9000. There is no supported standalone deployment. | Code fact | 1B3 |
| F15 | `Ui.Server` startup appends to the hard-coded file `f:\Code\Apperture\Apperture-Core\debug-4bd3d8.log` in every environment. Leftover agent-log script and `appertureShell.debugLog` calls (an undefined function) remain. | Code fact | 1B3 |
| F16 | Cookie `Secure` is `SameAsRequest` in every environment, and SameSite relies on the framework default. | Code fact | 1B3 |
| F17 | 33 `ManagementClient` methods collapse 401/403 (sometimes 404) to `null`. | Code fact. Harmless once the handler owns 401 (F1); the 403 UX part is deferred (§37). | — |
| F18 | Nullable `[FromBody]` POSTs accept an empty body (e.g. `procedures/{p}/launch` launches with defaults). | Believed from ASP.NET rules; **pinned by test in 1B1** | 1B1 |

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
| F5 | **Reproduced** (C full matrix; F/W the five decisive values) | 10 hostile values leave the origin in a real browser; the validator accepts all 10 (§38.2). `/\evil.example`, the TAB form and the CR LF form navigate to `http://evil.example/` on Chromium, Firefox **and** WebKit (signed-in `/login` bounce); the full 26-value matrix, and post-sign-in navigation, ran on Chromium. .NET `Uri` resolves every one on-origin. The host's `/signin` cannot leave the origin (authority is fixed) but turns them into a `//evil.example` **path** on the UI origin, and answers CR/LF with an unhandled 500. | `OpenRedirectBaselineTests` | 1A2 |
| F6 | **Not reproduced** as a loop (C) | Auto-redirect is ON, host returns `/login?error=tenant_mismatch`: **0** provider round trips on a direct load of `/login`, on the host's own error redirect and on a protected-route reload. `Login.razor` only auto-redirects from `OnAfterRenderAsync(firstRender)` when the sign-in options are already loaded; on every real entry they are not. It fires exactly once when entered in-app (link from `/login/local`), and the error return is a document load, so it does not fire again. The hazard in the code is real; the observed behavior is "auto-redirect mostly never fires". **New (N2):** the feature itself is effectively dead on real entries. | `OidcFlowBaselineTests.F6_…` | 1A2 (keep the `error` guard) |
| F7 | **Reproduced** (C) | Provider `access_denied` and `server_error` (Testing environment = the Production branch): the browser ends on `http://localhost:{api}/signin-oidc`, `POST … → 500`, `application/problem+json`, body `{"type":"https://httpstatuses.com/500","title":"An error occurred while processing your request.","status":500,"correlationId":…}`. One provider round trip, no loop. | `OidcFlowBaselineTests.F7_…` | 1A2 |
| F8 | **Reproduced** (C/F); **not measurable** on WebKit; **real Entra Unverified** | Cross-site `form_post` to `/signin-oidc` from `127.0.0.1` (`Sec-Fetch-Site: cross-site`, `Origin: http://127.0.0.1:{p}`, method POST). The callback carries **only** the OIDC nonce and correlation cookies; `.AspNetCore.Cookies` is absent, though it was sent on the step-up initiation moments earlier (a browser-initiated top-level navigation, `Sec-Fetch-Site: none`). `CompleteStepUpAsync` cannot authenticate the cookie scheme and step-up returns `/account/security?error=user_not_found`. | `StepUpCallbackBaselineTests` | 1A2 / 1A2b |
| F9 | **Reproduced** (C) | With no operator input after load, the Toast poll at +7 h returned 200 **and re-issued the session cookie** (`Set-Cookie: .AspNetCore.Cookies … expires; samesite=lax`); the poll at +13 h was still 200 (and renewed again). Control: the original ticket replayed at +13 h → 401. | `SessionRenewalBaselineTests` | 1A3 |
| F10 | **Partial** | Toast watcher after expiry: next tick → `401`, no navigation, no toast (C). "Polls forever with backoff" is read from code; one 401 tick was observed. The `RunDetail` 2 s poll was **not exercised** (needs a Run; **Unverified**). | `F10_background_toast_poll_…` | 1A1 — **flipped** (401 half: one recovery, watcher stops) / 1A3 (renewal half, unchanged) |
| F11 | **Reproduced** for same-site; cross-site blocked by SameSite (C/F); **WebKit-Windows differs** | Same-site sibling (`localhost:{other port}`): bodyless form POST and simple credentialed fetch both arrive with the session cookie and **archive a disposable context** (`200`); response unreadable by CORS. Cross-site (`127.0.0.1`): delivered with `Origin` but **no cookie**, `401`, nothing changed (C/F). On Playwright's WebKit-Windows build the cookie **was** attached cross-site (§38.4). | `RequestForgeryBaselineTests` | 1B1 |
| — | **Reproduced** (simultaneous 401) | Three tabs meet the expired session together: 3 × `401`, 3 error panels, **0** `auth/me` probes, **0** navigations to sign-in. (A single tab yields one 401 per navigation: pages issue calls sequentially.) | `Simultaneous_401_baseline_…` | 1A1 — **flipped** (per tab: one probe, one sign-in navigation) |
| — | **Reproduced** (history) | Expired reload replaces the protected entry with `/login` (Back → previous document). After a local sign-in, Back returns to `/login/local`, which immediately bounces the signed-in operator forward again (Back is trapped). | `HistoryBaselineTests` | 1A1 (review) — **extended**: the reload case is unchanged; a third test pins the in-app-recovery history (§39). Local sign-in Back-bounce unchanged (1A2). |
| F13 | **Partial** | The existing `UiDev` policy refuses both cross-site and same-site-sibling preflights: a custom-header JSON POST is never delivered (C), and neither is PUT/PATCH/DELETE (C/F/W). Its being active outside Development is a code fact, not browser-provable here. | `RequestForgeryBaselineTests` | 1B3 |
| D2 | **Reproduced** (C) | A cross-site page's `location.href = '{api}/signout'`: `GET /signout`, `Sec-Fetch-Site: cross-site`, `Sec-Fetch-Mode: navigate`, session cookie sent, `302`; afterwards `GET /api/auth/me → 401` (the session really ended). | `RequestForgeryBaselineTests.D2_…` | 1B2 |
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
- Return targets are stable routes. They carry no View-selector or future-grouping state.

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
4. Contains no percent-encoded `/`, `\`, `.` or control character in the **path** segment (`%2F`, `%5C`, `%2E`, `%0x`, case-insensitive).
   These are encoded forms that a second decoder or the browser could normalize off-origin.
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
| `/signout-callback-oidc`, `/signout-oidc` | IdP-driven sign-out callbacks | Same |

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
- `detail` is only allowed in Development and never carries token material.
- The operator never sees a raw API-host error document.
- `Login.razor` **never auto-redirects when the URL carries `error`**. It also never auto-redirects when a per-tab `sessionStorage` marker
  shows an auto-redirect started in the last 30 s, which covers a failure that returns without `error`.
- On error it shows a stable house error panel ("Sign-in with Microsoft didn't complete." / "Sign-in was cancelled."). The panel offers
  **Try again** (an explicit, manual Entra navigation) and, where the auth UI policy shows it, local sign-in.

**Step-up callback (mandatory investigation and fix, F8):**
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
| **SEC-1A2 — return URL and login/error/step-up** | `LocalReturnUrl` (shared; both old validators deleted; single-validator guard), fragment capture, reserved paths; `OnRemoteFailure` always handled; login error panel + no auto-redirect on `error` / recent marker; **step-up continuation per §26 if F8 is proven**. *If step-up needs the redesign, split it out as SEC-1A2b so the return-URL fix is not held back.* | 1.0 | §29.2, §29.3 |
| **SEC-1A3 — background requests, renewal and capability refresh** | `BrowserRequestHeadersHandler` background part + `BackgroundRequest` scope; Toast watcher, `RunDetail` poll and automatic unread-count refresh marked; host `OnCheckSlidingExpiration` (D9); timer guard; sign-in full-load guard; single-watcher test | 1A1 (header handler: 1B1 or 1A3, whichever lands first creates it) | §29.1 background rows, §29.6 renewal rows |
| **SEC-1B1 — server request-forgery policy** | `BrowserRequestTrustMiddleware` + options (exact trusted origins, Mode, startup validation), Rules U/G/N, exemption metadata on `Enroll` + OIDC path exemptions, `problem+json` 403, logging/meter, **client `X-Apperture-Request` header in the same change**, test-factory default handler, F18 pin, guards (exemptions, order, header literals) | 1.0 | §29.4 (I, S) |
| **SEC-1B2 — endpoint verb cleanup and logout** | G1/G2 → POST (+ client); re-confirm G3–G5 and G9b classifications; `/signout` → POST with Rule N + hidden-form UI in three places; remove GET `/signout` and unused `POST auth/local/logout`; remove Ui.Server's dead `HttpClient`/`ManagementClient`/capabilities registrations; GET-classification guard | 1B1 | §29.4 verb/logout rows |
| **SEC-1B3 — CORS, cookie and host cleanup** | `UiDev` Development-only + exact configured origin + narrowed methods/headers + any-origin guard; explicit `SameSite=Lax`; `SecurePolicy=Always` outside Development (all three cookies); `frame-ancestors`/`X-Frame-Options`; **`Management.Api` → class library** (drop `Program.cs`, launch profile, `.vscode` entry, stub host test; repoint the placeholder `generate-openapi.ps1` at Management.Host) under the D7 STOP condition; **remove the Ui.Server debug filesystem write, the `_Layout` agent-log script and `appertureShell.debugLog` calls** | 1B1 | §29.5, §29.6, §29.7 |
| **SEC-1B4 — browser and integration security proof** | Flip every remaining characterization; run the full §29 B matrix against rebuilt, restarted hosts; manual real-Entra smoke (§29.3 M) | all above | — |
| **SEC-1C — docs and closure** | ADR (browser request trust + session recovery + GET-safety rule); update `next-stage-platform-auth.md`, ADR-0042 (watcher: background, non-renewing), ADR-0044 (its POST+JSON measure is superseded as a pattern by SEC-1), deferred-public-origin (CORS/cookie/`ManagementUiUrl` parts done), decisions index, transitions README; closure audit with evidence; roadmap status | all | — |

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
| 1A2 | F5 (empty `EscapesToday`), F6 guard, F7, F8 (with 1A2b), N1, N6 |
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
