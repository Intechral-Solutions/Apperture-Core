# Deferred: public origin, Entra redirects, and federated sign-out

**Status:** Deferred until closer to launch. Local split-host (UI `:9001`, management host/API `:9000`) is enough for current development. Do not implement hostname handling until the surrounding hosting architecture is settled.

**Done by SEC-1B3** ([ADR-0045](../02-decisions/ADR-0045-browser-session-recovery-and-request-trust.md)): the `ShellUi:ManagementUiUrl` startup refusal, Development-only CORS, and `Secure` cookies (items 1 and 4 below, and the Production and Cookies bullets). Forwarded headers, templates, Docker/IIS, split-host production and the key ring remain deferred.

Related: [S2D-iam2-strong-authentication.md](../02-decisions/S2D-iam2-strong-authentication.md), [management-platform-configuration.md](management-platform-configuration.md), deploy templates under [deploy/config/examples/](../../deploy/config/examples/).

## What already works

Sign-in and sign-out are path-based on the **management host**, not the UI origin:

| Path | Role |
|------|------|
| `GET /signin` | Entra challenge |
| `/signin-oidc` | Entra redirect URI (sign-in) |
| `POST /signout` | Clear cookie; optional Entra end-session. A form POST from the trusted UI origin only (SEC-1 Rule N); there is no GET |
| `/signout-callback-oidc` | Entra redirect URI (sign-out). Independent of front-channel logout |

The browser then returns to the UI login page via `ManagementUiRedirects` (`ShellUi:ManagementUiUrl`). In development that defaults to `http://localhost:9001`.

Production is already documented as **same-origin**: UI + `/api` + `/signin` + `/signout` share one public origin (`ApiBaseUrl` = `/api`). Split ports are a **dev** concern.

## Why this will break on real hostnames

1. **`ShellUi:ManagementUiUrl` defaults to `http://localhost:9001`.** Unset in Azure or on a Windows service, post-login and post-logout bounce operators to localhost instead of the public UI. *Done (SEC-1B3): the localhost fallback applies only in Development and `Testing`. Every other environment refuses to start on a missing, HTTP, loopback or non-origin value. The same value is the only `Origin` trusted for unsafe browser API requests.*
2. **No forwarded-headers middleware.** Behind App Service TLS or a reverse proxy in front of Kestrel, OIDC `redirect_uri` is built from the *incoming* request. Without `X-Forwarded-Proto` / `Host`, Microsoft sees `http://internal-name/signin-oidc` instead of `https://public-hostname/signin-oidc` and rejects it.
3. **Azure and Windows appsettings examples** do not set public UI/host URLs or list the Entra Redirect URIs.
4. **CORS** on the host allows only `localhost:9001`. That is correct for same-origin production (CORS is unused) and will block a split-host production topology unless updated. *Done (SEC-1B3): the `UiDev` policy is registered and applied only in Development, for the configured UI origin. No other environment has a CORS policy. A split-host production topology still needs its own CORS and `SameSite` design.*

`UseWindowsService()` is not wired. Public-origin handling must not depend on it so a later Kestrel Windows service still works.

## Intended production topology (when we return)

Keep **one public origin** for UI, API, and Entra callbacks (for example `https://management.contoso.com`). Register Entra URLs on that host, not on a separate UI origin.

| Setting | Process | Meaning |
|---------|---------|---------|
| `ShellUi:ManagementUiUrl` | Management.Host | Public **UI** origin for post-login / post-logout redirects. Never default to localhost outside Development. |
| `ShellUi:ManagementHostUrl` | Management.Host | Public **UI** origin for email deep links (already used). Same value as `ManagementUiUrl` in same-origin deploys. The names collide with the UI setting below. |
| `ShellUi:ManagementHostUrl` | UI (WASM) | Public **API/host** origin for `/signin` and `/signout`. Empty when same-origin (page origin is the host). |

Suggested behavior when this is implemented:

- **Development:** keep the localhost:9001 default.
- **Production:** `ShellUi:ManagementUiUrl` is required and must be an HTTPS non-loopback origin (SEC-1B3). Startup fails if it is missing, HTTP, loopback, or not an origin. Do not fall back to localhost.
- **Forwarded headers:** enable behind Azure App Service and when a Windows reverse proxy is configured. Do not blindly trust `X-Forwarded-*` on a Kestrel port exposed to the internet.
- **Cookies:** `SecurePolicy = Always` in Production. `SameSite=Lax` is enough for same-origin. *(Done in SEC-1B3 for every environment except Development and `Testing`.)*
- **Templates:** add the keys above to [appsettings.azure.json.example](../../deploy/config/examples/appsettings.azure.json.example) and [appsettings.windows.json.example](../../deploy/config/examples/appsettings.windows.json.example), plus Entra app registration Redirect URIs:

  - `{public-host}/signin-oidc`
  - `{public-host}/signout-callback-oidc`

  Leave the Entra Front-channel logout URL empty. Apperture sessions end through `POST /signout` or expiry, not through Microsoft single sign-out from another application.

## Cover first (Azure + Windows/Kestrel)

These are the first two deployment types to make work. Docker and IIS come later.

**Azure (App Service or equivalent, real hostname / custom domain)**

- Set public UI origin (or derive from forwarded request).
- Trust App Service forwarded proto/host so Entra `redirect_uri` is `https://{hostname}/...`.
- Same-origin UI: leave WASM `ShellUi:ManagementHostUrl` empty and `ApiBaseUrl` as `/api`.

**Windows service + Kestrel**

- Bind Kestrel internally; public hostname comes from DNS and/or a reverse proxy.
- Same config keys as Azure. Enable forwarded headers only when a proxy is in front and is trusted.
- Service installer / `UseWindowsService()` can wait; URL construction should already be host-agnostic.

## Explicitly later

- Docker and IIS (including `PathBase` / virtual directories and ANCM).
- Split-host production (different UI and API registrable domains): CORS allowlist, `SameSite=None; Secure`.
- Multi-instance Data Protection key ring (Azure scale-out); cookies will not decrypt across instances without it. Separate from redirect URLs, but required before a scaled App Service is a launch target.
- Unifying the `ShellUi:ManagementHostUrl` naming collision (Host = UI origin for email vs UI = API origin for sign-in/out).
