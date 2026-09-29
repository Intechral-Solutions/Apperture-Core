# Apperture.BrowserTests — browser proof harness (SEC-1.0)

Opt-in Playwright suite that drives the **real** Management UI (Blazor WebAssembly) against the **real**
Management Host in a real browser, to prove browser-semantic security behavior (cookies, `Origin`, Fetch
Metadata, URL normalization, `form_post` callbacks). It is *characterization first*: SEC-1.0 tests describe
what the platform does today; the SEC-1 fixing slices flip the marked assertions. **SEC-1A1 has flipped** F1, F3, F4,
F10 (the 401 half) and the simultaneous-401 test (and extended the history test); those now pin the session-recovery
behavior (see the design gate §39), and `SessionRecoveryBrowserTests` adds the recovery cases the baselines did not cover.
**SEC-1A2a has flipped** F5 (`OpenRedirectBaselineTests`), F6 and F7 (`OidcFlowBaselineTests`) and extended F2 (the fragment
is now kept); those now pin the safe return-target policy and the stable Entra error flow (design gate §40), and
`EntraReturnTargetBrowserTests` adds the sign-in journeys (fragment round trip, hostile targets on the host link, Try again).
**SEC-1A2b has flipped** F8 (`StepUpCallbackBaselineTests`, Chromium and Firefox): the cross-site callback still arrives without
the session cookie, and Entra step-up now succeeds from the protected OIDC state (design gate §41), including the wrong-user,
wrong-tenant, unknown-identity and missing-context refusals. **SEC-1A3 has flipped** F9 (`SessionRenewalBaselineTests`): background
polls carry `X-Apperture-Background` and do not re-issue the session cookie; a user Refresh at the same threshold still does (design gate §43).
**SEC-1B1 has flipped** F11 (`RequestForgeryBaselineTests`, Chromium, Firefox and WebKit): a same-site sibling and a cross-site form or simple fetch are rejected with 403 and do not mutate; a legitimate Management UI POST and PUT carry the exact UI `Origin` and `X-Apperture-Request: 1` (design gate §44). **SEC-1B2 has flipped** D2 (`RequestForgeryBaselineTests`, Chromium, Firefox and WebKit): a cross-site `GET /signout` is 405 and the session survives; a cross-site or same-site-sibling sign-out form is 403 on its Origin; a cross-site navigation to `/signout-oidc` clears nothing; a cross-site navigation to `/signin/step-up` or `/signin/link` is 403; the account menu's sign-out is a form POST with the UI `Origin` and no platform header (design gate §45). **SEC-1B3** moved product `UiDev` CORS to Development-only (design gate §46); this harness is `Testing`, so it registers a test-only copy of the same policy against the ephemeral UI origin. Cookie `Secure` stays `SameAsRequest` on this HTTP topology. **SEC-1B4** wires the suite as the blocking GitHub Actions
job `browser-security` (design gate §47). The first remote Actions run reached BrowserTests and failed at
Management.Host startup (Windows-only DPAPI default); that is CI/harness portability, not a failed browser
security assertion (gate §47.6). The final pull-request workflow passed `browser-security` and `build-and-test` on
Ubuntu, so the job is remotely proven and B4 is complete (gate §47.16). The `master` ruleset requires both checks
but is not enforced by GitHub under the current plan, so both are procedurally required before merge.

## Run it (one command)

```powershell
$env:APPERTURE_BROWSER_TESTS = "1"
dotnet test tests/Apperture.BrowserTests/Apperture.BrowserTests.csproj
```

Without `APPERTURE_BROWSER_TESTS=1` every browser test reports **Skipped**, no host/SQL/browser is started,
and a normal `dotnet test Apperture.slnx` is unaffected (only the fast isolation guards in
`HarnessIsolationGuardTests`, `BrowserSecurityCiGuardTests`, and `BrowserHarnessSecretProtectorTests` run).

CI (SEC-1B4) uses the same invocation on `ubuntu-latest` with a SQL service container. The stable check name is
`browser-security`. The first remote run failed at Management.Host startup because the production SQL-secret
default is Windows DPAPI; the harness now replaces `ISecretProtector` with a test-only AES-GCM protector
(`BrowserHarnessSecretProtector`) so Ubuntu can start the host. On failure it uploads the TRX under
`artifacts/browser-security`. It does not set `APPERTURE_BROWSER_TESTS` on the ordinary `build-and-test` job.

If your Aspire dev stack holds file locks on the normal `bin/` folders, build into an isolated, git-ignored
folder first and run with `--no-build`:

```powershell
dotnet build tests/Apperture.BrowserTests/Apperture.BrowserTests.csproj --artifacts-path artifacts/sec10
$env:APPERTURE_BROWSER_TESTS = "1"
dotnet test tests/Apperture.BrowserTests/Apperture.BrowserTests.csproj --artifacts-path artifacts/sec10 --no-build
```

Prerequisites

- The test SQL Server the integration tests use (`apperture-test-sql`). The connection string comes from
  `APPERTURE_TEST_SQL_CONNECTION_STRING` or the repo-root `.env` (see [../README.md](../README.md)). The harness
  owns one database, `Apperture_BrowserTests`, which it **drops and recreates on every run**. It never touches
  any other database.
- Browsers: Chromium, Firefox and WebKit are downloaded to the per-user Playwright cache
  (`%LOCALAPPDATA%\ms-playwright`, never the repo) **the first time an engine is used at run time**, so the first
  run needs internet. To fetch them up front (or for an offline machine):
  `pwsh <output>/playwright.ps1 install chromium firefox webkit`. Building the project also copies the Playwright
  driver (about 100 MB) next to the test binaries.
- Nothing else: **no Entra credentials**, and after the browsers are cached no internet.
- One run at a time per machine: the run takes an exclusive lock (a temp file) because it owns the fixed-name
  database; a second concurrent run fails fast with a clear message.

## What it starts (and stops)

| Piece | How | Origin |
|---|---|---|
| Management.Host | the real `Program.cs`, in the test process, on a real Kestrel socket | `http://localhost:{api}` |
| Ui.Server | the real host, a **child process** started from its build output | `http://localhost:{ui}` |
| Stub OIDC provider | test-only Kestrel app (discovery, JWKS, authorize `form_post`, token, in-memory RSA key) | `http://127.0.0.1:{oidc}` |
| Cross-site attacker pages | test-only Kestrel app | `http://127.0.0.1:{attacker}` |
| Same-site sibling attacker pages | same test-only app, second binding | `http://localhost:{sibling}` |

Ports are allocated dynamically (the developer's Aspire stack owns `:9000/:9001` and is never touched), but
the **hosts** are fixed on purpose: SameSite is decided by *site*, not port, so `localhost` (app) versus
`127.0.0.1` (provider, cross-site attacker) is genuinely cross-site in every engine, while
`localhost:{sibling}` is genuinely same-site. Both are pinned by
`HarnessSmokeTests.Cross_site_and_same_site_attacker_origins_have_the_intended_site_relationship_to_the_api`.

Lifecycle: everything is created once (xunit collection fixture) and disposed once. The only child process
is Ui.Server; it is tracked by PID, assigned to a Windows kill-on-close **job object** (so even an aborted test
run cannot orphan it) and killed by exact handle on disposal. No `taskkill`/`Stop-Process` by name is ever
used. Playwright's own driver and browsers exit with the test process. Host and Ui.Server output is kept in
memory and printed only on startup failure.

Known side effects: the harness SQL database (`Apperture_BrowserTests`) is recreated on every run; browsers are
cached under `%LOCALAPPDATA%\ms-playwright`.
The harness touches nothing else, and a safety net aborts any browser request to a localhost port that is not one
of the run's own hosts (so a client mis-pointed at the developer stack on `:9000` cannot reach it).

## Test-only controls (and why production cannot activate them)

All of these live in this assembly and are wired only by `LiveManagementHost`; no production project
references this one (pinned by `HarnessIsolationGuardTests`):

- `ControllableTimeProvider` on the **application cookie handler only** — real server-side session expiry and
  sliding renewal without sleeping (`CookieAuthenticationOptions.TimeProvider`).
- `RequestRecorder` — an observe-only first middleware (via `IStartupFilter`) that records what the host
  received. Cookie/token **values are never captured**; only cookie names and Set-Cookie attribute names.
- Ephemeral Data Protection key ring (the developer's persisted key ring is never read or written).
- OIDC substitutions: Entra options point at the stub; `RequireHttpsMetadata=false` for the loopback stub.
- A test-only `UiDev` CORS policy against this run's UI origin (product CORS is Development-only and is
  not registered in `Testing`; headers, methods and credentials match the Development policy).
- Playwright routes: the client's `appsettings*.json` `ApiBaseUrl` is rewritten to this run's API origin
  (the shipped client resolves any loopback UI to port 9000); off-harness navigations are answered locally
  (`ExternalNavigationSink`), so nothing ever leaves the machine.

No production endpoint exists to manipulate auth state.

## Engines

The whole suite runs on **Chromium**. Facts that are browser behavior rather than application logic also run
elsewhere:

| Test | Chromium | Firefox | WebKit |
|---|---|---|---|
| `RequestForgeryBaselineTests.Unsafe_requests_carry_an_Origin_header_on_every_engine` (Origin/Fetch Metadata on every unsafe request) | yes | yes | yes |
| `OpenRedirectBaselineTests.F5_key_hostile_cases_end_on_the_default_route_on_every_engine` (URL normalization of the decisive hostile return targets) and `Key_legitimate_deep_links_arrive_intact_on_every_engine` (legitimate ones, so the policy is proven not to be a blanket refusal) | yes | yes | yes |
| `StepUpCallbackBaselineTests` (F8, flipped by SEC-1A2b: step-up over the cross-site `form_post` callback without the session cookie; Entra and Local session kinds; refusals; cancel) | yes | yes | no |
| SEC-1A1 recovery tests (`SessionRecoveryBrowserTests`, and the flipped F1/F3/F4/F10/simultaneous-401/history cases) | yes | no | no |
| SEC-1A2a Entra legs: `OidcFlowBaselineTests.F7_…` (cancelled / failed panel), `EntraReturnTargetBrowserTests` (deep link, hostile link, Try again) | yes | yes | no (cannot complete an OIDC handshake here) |
| SEC-1A2a full hostile / auth-surface / legitimate corpus, local sign-in, F6 auto-redirect | yes | no | no |

**WebKit here is Playwright's Windows build, not Safari.** It does not enforce SameSite (it attached the Lax session
cookie to cross-site requests, so cross-site cookie assertions are skipped for it) and it rejects the `Secure`
OpenID Connect correlation cookie on `http://localhost` (so no OIDC flow can complete on it). Safari is a manual
item. Everything else runs on Chromium only, for runtime cost.

## Files

- `Infrastructure/` — the harness (hosts, stub provider, recorder, clock, seeding, Playwright helpers).
- `*BaselineTests.cs` — characterizations, one file per concern. Each assertion that a later slice must invert
  says **FLIPS IN SEC-…** in its doc comment; the evidence register is the SEC-1 design gate §0.
- `SessionRecoveryBrowserTests.cs` — SEC-1A1: 403 never recovers; a failed sign-in 401 never recovers; a mutation that
  met 401 is never replayed (before or after sign-in); an outage or 403 found by the recovery probe shows the
  service-unavailable / account-unavailable surface; the loop breaker across real document loads.
- `Infrastructure/RecoveryEvidence.cs` — counts a tab's recovery probes and sign-in navigations from a `PageTrace`, and
  samples a page for a raw-error flash.
- `ReturnTargetCorpus.cs` — the hostile and legitimate return-target corpus. It is the contract for the shared
  `LocalReturnUrl` policy: `ReturnTargetCorpusPolicyTests` (always on, no browser) asserts every hostile and auth-surface
  value is refused and every legitimate value accepted, and the browser tests prove the navigation.
- `EntraReturnTargetBrowserTests.cs` — SEC-1A2a: a legitimate deep link (path + query + fragment, dotted and encoded ids) through
  capture → sign-in page → Microsoft (stub) → landing; a hostile `returnUrl` on the host's `/signin` link and on the UI sign-in
  page; Try again after a cancelled sign-in; local sign-in from a deep link, a failed local sign-in, and in-app expiry recovery
  with a fragment.

## Compatibility with the approved design (D1–D9)

The harness already records the platform request header (`X-Apperture-Request`), the background marker
(`X-Apperture-Background`), `Origin`, `Sec-Fetch-*` and Set-Cookie attributes, so SEC-1B1 (Origin + header +
Fetch Metadata) and SEC-1A3 (non-renewing background traffic). SEC-1B3 cookie Secure is proven at host
configuration (Always in Production/Staging; SameAsRequest on this HTTP harness). SEC-1B4 is the blocking
`browser-security` CI job (I-2). Signing in uses `POST /api/auth/local/login` through
`BrowserHarness.SignInAsync`; when Rule U lands, that one helper gains the trusted `Origin` and header.
