# ADR-0045: Browser session recovery, request trust and GET safety for the Management UI

## Status

Accepted. Engineering closure 2026-09-29 (SEC-1C); **merged to `master`.** Independent final review: SAFE WITH MINOR NON-BLOCKING FOLLOW-UP.
Remaining environment-dependent operator smoke is pre-release/UAT validation and is **not** claimed passed. Design and rulings D1–D9: the
[SEC-1 design gate](../01-architecture/transitions/security/sec1-browser-session-request-forgery-design-gate.md)
(approved 2026-09-24; as-built records §38–§47). Requirement closure, residuals and evidence:
[`sec1-browser-session-security-closure.md`](../audits/sec1-browser-session-security-closure.md). Supersedes, as a pattern,
the endpoint-local request-forgery measure recorded in
[ADR-0044](ADR-0044-connection-workspace-and-browse-authorization.md) (POST + required JSON body under `SameSite=Lax`).

## Context

The Management UI is a Blazor WebAssembly client served by Ui.Server. Management.Host owns authentication (application
cookie, Entra OpenID Connect, local sign-in) and serves `/api`. In development the two are separate origins on the same site
(`localhost:9001` and `localhost:9000`); production is documented as one origin.

An audit (gate §2–§18) and a browser proof harness (gate §0.1, §38) found:

- no client handling of 401: an expired session showed raw "Unauthorized" text and never started sign-in;
- an open redirect through `returnUrl` values that .NET resolves on-origin but browsers do not (`/\evil.example`);
- a raw error document after a cancelled or failed Entra sign-in;
- Entra step-up that could not work in any browser, because it read the `SameSite=Lax` session cookie inside Microsoft's
  cross-site `form_post` callback;
- background polling that kept renewing the 12 h sliding session, so a passive tab never idled out;
- no request-forgery layer: same-site sibling origins could drive every form-reachable mutation, two GETs wrote Apperture
  state, and `GET /signout` ended the session;
- credentialed `UiDev` CORS applied in every environment, a runnable `Management.Api` stub with allow-any-origin CORS,
  `Secure=SameAsRequest` cookies everywhere, and a hard-coded debug file write in Ui.Server.

## Decision

### Session recovery is client-owned; `/api` stays bare 401/403

One pipeline handler reports 401s to one per-document coordinator. The coordinator confirms the loss with one
`GET auth/me` on a separate credentials-only client, captures the local return target, and performs exactly one full-page
navigation to sign-in. It never replays a request. 403 never starts recovery. An `auth/me` 403 shows a terminal
"account unavailable" surface; a network failure or 5xx shows a service-unavailable surface with Retry, never sign-in.
Sign-in and public routes are excluded, and a per-tab loop breaker caps automatic recoveries. Recovery and every sign-in
end in a full document load, so identity and capabilities are always fetched fresh.

Rejected: a server-side challenge before shell boot (Ui.Server cannot read the cookie), and automatic replay after sign-in.

### One return-target policy

`LocalReturnUrl` (Ui.Shared) is the only return-URL validator, used by the client and by Management.Host. It keeps path,
query and fragment, accepts a candidate unchanged or refuses it, and falls back to `/control/center`. It refuses what a
browser would reinterpret (backslash, control characters, whitespace, non-ASCII, an authority-shaped or encoded first
segment, dot segments) and every authentication surface. Browser tests, not .NET URI parsing, are authoritative for it.

### Entra failure and step-up

Every Entra remote failure is handled: one redirect to `/login?error=cancelled` or `error=oidc`, a fixed code only, and a
stable panel with an explicit Try again. Automatic redirect to Microsoft never fires on an error, within 30 s of a previous
attempt, or on a bare `/login` visit.

Step-up does not read the ambient session in the callback. It carries a minimal continuation (purpose, initiating user,
requested context, validated return path, session kind, a 15-minute expiry) in the protected OIDC state, proves the
returned `tid` + `oid` is the initiator's own proven link, and re-issues the same kind of session. `SameSite` is not
loosened. Claims in the `http://schemas.apperture/` namespace from the identity provider are stripped before the host adds
its own.

### Background requests never renew the session

A `BackgroundRequest` scope around timer-driven client work makes the one header handler add `X-Apperture-Background: 1`.
`OnCheckSlidingExpiration` declines renewal for that header. The header can only decline renewal; it grants nothing. A
source guard requires every timer-driven API call to be inside the scope.

### Browser request trust (one middleware, three rules)

`BrowserRequestTrustMiddleware` runs after Development-only CORS and before authentication, so a rejection is always
403 `application/problem+json` (`code: request_origin_rejected`), never 401, and happens before any action, audit, database
or remote call.

- **Rule U (unsafe methods under `/api`, anonymous endpoints included):** exactly one `Origin` equal to the configured UI
  origin (`ShellUi:ManagementUiUrl`; scheme, host and port; no wildcard, suffix or blanket localhost); exactly
  `X-Apperture-Request: 1`, added only by the client's one header handler; `Sec-Fetch-Site: cross-site` refused. A missing
  `Origin` fails closed. A missing `Sec-Fetch-Site` alone does not.
- **Rule G (GET/HEAD under `/api`):** explicit cross-site Fetch Metadata is refused, as defense in depth only.
- **Rule N (native-navigation host endpoints, by endpoint metadata):** `POST /signout` needs the exact `Origin` and no
  cross-site Fetch Metadata, without the platform header (a form navigation cannot send it); `GET /signin/step-up` and
  `GET /signin/link` refuse explicit cross-site Fetch Metadata. `GET /signin` stays open.

Exemptions are exactly `POST /api/enroll` (machine enrollment, token in body) and the live OIDC protocol callbacks outside
`/api` (`/signin-oidc`, `/signout-callback-oidc`). A callback path configured under `/api` is not exempted. `ReportOnly`
is honoured only in Development. There is no `Off`.

Rejected: ASP.NET antiforgery tokens (no server-rendered forms, a cross-origin dev topology, and token rotation that would
collide with session recovery), `SameSite` plus JSON binding alone (fails for top-level GETs and every same-site origin),
and per-feature schemes.

### GET safety

GET endpoints do not mutate Apperture state. Security-audit appends and idempotent read-model materialization are
observation. Read-only remote lookups may stay GET. The Entra directory searches that wrote state are POST. A reflection
guard freezes the classification of every GET that can reach a writing or remote-calling service. Rule G is never a reason
to keep a mutation behind GET.

### Sign-out

Sign-out is `POST /signout` from a native form in the UI; `GET /signout` is 405. The destination is always `/login`.
`RemoteSignOutPath` is emptied after every configurer, so `/signout-oidc` is not a live endpoint and no unsolicited Entra
front-channel request ends an Apperture session. `/signout-callback-oidc` remains the post-logout return.

### Host and deployment boundaries

`UiDev` CORS is registered and applied only in Development, for the exact configured UI origin, with narrow methods and
headers. Every environment other than Development and `Testing` requires a public HTTPS, non-loopback
`ShellUi:ManagementUiUrl` at startup and issues `Secure=Always` cookies; `SameSite=Lax` and `HttpOnly` are explicit.
Both hosts send `X-Frame-Options: DENY` and `frame-ancestors 'none'`. `Management.Api` is a class library. Ui.Server has no
Management API client of its own.

### Proof

An opt-in Playwright suite (`tests/Apperture.BrowserTests`) proves the browser-dependent behavior against the real hosts,
a stub OIDC provider on a different site, and same-site and cross-site attacker pages. It runs as the blocking
`browser-security` job in CI alongside `build-and-test`.

## Consequences

### Positive

- An expired session always leads to one sign-in navigation and a return to the same page, with no replayed mutation.
- Same-site sibling and cross-site pages cannot drive a mutation, sign-out or sign-in; new controllers are covered by
  default because the rule is keyed on method and path, not on attributes.
- Features and `ManagementClient` add no security headers or tokens; one handler and one middleware own the contract.
- Step-up works in real browsers without weakening the session cookie.
- Browser semantics are measured in CI on Chromium, Firefox and WebKit rather than assumed.

### Negative

- A passive tab is signed out after 12 h without operator activity.
- A misconfigured `ShellUi:ManagementUiUrl` blocks every mutation (loudly: startup refuses it outside Development and
  `Testing`, and each rejection is logged and metered).
- Browsers without Fetch Metadata get no Rule G or Rule N refusal on the two auth navigation GETs. A same-site sibling can
  still navigate an operator to `/signin/step-up` or `/signin/link` (a Microsoft prompt; nothing is changed without the
  operator's own proof).
- Microsoft front-channel single sign-out does not end an Apperture session.
- The `Testing` environment name selects the local HTTP topology (no public-origin refusal, `SameAsRequest` cookies). It
  must not be used for a real deployment.

### Neutral

- The browser suite takes about 30 minutes and needs a SQL Server; it is opt-in locally and mandatory in CI.
- Rejected alternatives are recorded in the gate (§20, §27) and are revisited only if implementation evidence proves this
  model unworkable.

## Not decided here

Periodic session revalidation of disabled users; Content-Security-Policy beyond `frame-ancestors`; forwarded headers, a
multi-instance Data Protection key ring, the `__Host-` cookie prefix, and a split-site production topology (which would
need its own CORS and `SameSite` design); true Entra front-channel single sign-out; custom role lifecycle (IAM-8).
