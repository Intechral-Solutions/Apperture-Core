# IAM-10.2b WP1: independent review record

| | |
|---|---|
| Package | IAM-10.2b, work package 1, after the independent security review of commit `e3e70c6` ([WP0 reconciliation](iam10-2b-wp0-reconciliation.md)). |
| Verdict | **WP1 accepted with bounded remediation.** The global SQL application lock and the shared viability architecture were assessed as sound. One finding blocked further work (finding 2, below) and is remediated in the commit that adds this record. The other findings are accepted for the packages named. |
| Branch | `feat/iam-10.2b-administrator-viability`. Not pushed, not merged. |

## 1. Finding 2: the Now view and the OpenID Connect handler

### What was wrong

The first version of the Now view read the handler's configuration from `IOptionsMonitor<EntraAuthOptions>.CurrentValue`, on the reasoning that the handler is configured from that monitor. That is where the handler's options *come from*, once. It is not what the handler *holds*.

### The lifecycle, as traced

| Step | What happens |
|---|---|
| Registration | `AddAppertureAuthentication` registers `EntraOpenIdConnectOptionsConfigurer` as an `IConfigureOptions<OpenIdConnectOptions>` and adds the OpenID Connect scheme. |
| First use | The authentication middleware asks, on every request, for the handlers of the request-handling schemes; the OpenID Connect handler is one (it owns the callback path). The handler's `InitializeAsync` calls `IOptionsMonitor<OpenIdConnectOptions>.Get("OpenIdConnect")`. The first call builds the options: the configurer runs and reads `IOptionsMonitor<EntraAuthOptions>.CurrentValue` **at that moment**, and applies either the real authority, client and secret or the disabled placeholders. The monitor stores the result in its cache. |
| Later requests | `Get` returns the cached instance. |
| An appsettings reload | The configuration source for the `Entra` section raises its change token. `IOptionsMonitor<EntraAuthOptions>` evicts its own cache entry and rebuilds, which runs the options post-configure again and so reads the persisted registration again. **Nothing registers a change token source for `OpenIdConnectOptions`**, so the handler's cached options are not evicted and not rebuilt. |
| Reconstruction | Only an invalidation of the `OpenIdConnectOptions` cache (a change token source for them, or a cache clear) makes the next `Get` build them again, from the Entra options of *that* moment. |

So after a registration switch and a reload, `IOptionsMonitor<EntraAuthOptions>` describes the new tenant while the handler still signs users in through the old one. The first Now view took the new tenant for the handler's, so it saw no conflict with the live tenant check and counted administrators proven for the new tenant as able to sign in now. They cannot: the handler sends the user through the old directory, the token carries the old tenant, and the sign-in compares it with the new one and rejects it. A reduction could therefore have been allowed that left nobody able to sign in.

### The authoritative binding

The Now view now reads the handler's own named options: `IOptionsMonitor<OpenIdConnectOptions>.Get("OpenIdConnect")`, through `IRunningEntraHandlerBindingSource` (Security), implemented by `OpenIdConnectHandlerBindingSource` (Management Host). It is the instance the handler is given, so there is no second copy to keep in step, and a real reconstruction is followed automatically.

- **No capture.** A captured binding (recorded by the configurer when it runs) was not needed and would have been a second authority.
- **Why `Get`, and not a look into the cache.** `IOptionsMonitorCache<T>.TryGetValue` is not public in the framework. `Get` has no effect on the handler in a request, because the middleware has already built the options for that request. Outside a request it builds them as the first request would, from the same Entra options.
- **Placeholders.** A handler holding the disabled placeholders is reported as *disabled*: a known state, not a sign-in path. The placeholder text is now one constant used by both places that set it.
- **Unknown is not usable.** If the options cannot be built (or a host registers no source), the binding is *unavailable*; the Now view counts no Entra method, and the Entra reading is **unknown**, so the guard fails closed for a viability-reducing operation. Only the exception type is carried, never its message, which could quote an option value.
- **Secret.** The binding reports whether a secret exists, never its value.
- **The tenant.** The handler's tenant is the tenant segment of the authority it holds. That authority is already the tenant-specific form, so nothing is derived again.

### Unchanged

The OpenID Connect behaviour, the live accepted-tenant check, the endpoint gating on the fixed `IOptions`, the Restart view, the secret-resolution rules, the legacy deployment-secret fallback and every authentication decision. The only production edits are the new reader, its registration, and the placeholder text moved to a constant.

### Evidence

Tests use the real host registration, the real options post-configure over a changing registration, a configuration source that really reloads, and the framework's own options cache (unit, with the registration faked; and against real SQL Server on both isolation modes, with the registration in the database). They show, in order: the host binds to tenant A; the registration is switched to tenant B and the configuration reloaded; the Entra monitor now says B and the endpoints' options and the handler's options still say A (the same instance); the Now view accepts no tenant; an administrator proven only for B is not viable now and is after the restart; the guard refuses a reduction that leaves nobody able to sign in now; clearing the handler's cache makes the handler, and the Now view, follow B. A mutation check that makes the reader consult the Entra monitor again fails four of those tests.

## 2. Findings accepted for later packages

| Finding | Owner | What is required |
|---|---|---|
| **F-1** A generic user update writes the designation flag from the record the caller holds | **WP4B**, no later | `UpdateAsync` stops writing the flag; one dedicated, gated writer sets it. The writer guard counts the callers meanwhile. |
| Secret-store resolution failure is read as an absent secret | **WP3** | Distinguish a failure to resolve (unknown, fail closed) from a confirmed absence (no secret). Today `EntraClientSecretAccess` swallows a resolver exception and falls through to the deployment secret. |
| **D-6**: provenance of the command line's configuration | A focused owner decision **before WP4B**; **before any command-line reducer is integrated** | The command line trusts what it was given; nothing ties it to its target installation. Options include the host recording its effective Local setting in the database at start. |
| Deterministic overlap and acquisition-order tests | **WP2** | The gate must be shown to be the first lock acquired after the audited operation lock; concurrency tests must force overlap and distinguish protection by the application lock from incidental protection by a row lock. |
| Writer-guard hardening | **WP2**, then **WP5** | The guard stays explicit about pending integrations; it is hardened as each package flips its writers from pending. |
| The deployment-secret fallback: a registration that does not use the deployment secret and has no stored secret still enables Entra from the deployment secret | An **owner decision before WP3**, only if a behaviour change is proposed | Existing behaviour, unchanged here and pinned by a differential test. |

### 2.1 Tracked risks with owners and deadlines (added in the [WP2 review record](iam10-2b-wp2-review-record.md))

| Risk | Definition | Owner | Deadline |
|---|---|---|---|
| **R-1** First-time Entra options construction inside a registration mutation | The first construction of the Entra options (and so of the OpenID Connect handler's cached options) reads the persisted registration and the secret store through a scope and connection of its own. If that happens inside a unit that holds the administrator-set gate **and has written a registration row**, the separate read can block on the unit's own uncommitted row (or read the pre-change row), leaving options that are cached incorrectly initialized, and the viability reading would then be built on them. WP2 units write no registration or secret row, and the request pipeline builds the options before any unit runs, so WP2 is not exposed. | **WP3** (the registration and sign-in policy writers) | Resolved and proven by a test before any WP3 writer is integrated; reviewed in the WP3 independent security review |
| **R-2** A startup writer initializing the handler options too early | A hosted service that runs at start (the bootstrap and repair services of WP4B) can cause the OpenID Connect handler's options to be built before migrations have run or before the registration state is ready, so the framework caches the disabled or incomplete configuration for the life of the process, and the Now view then reports a handler that does not match what an administrator later configures. | **WP4B** (the startup and bootstrap writers) | Resolved and proven by a test before the WP4B review; recorded in the WP5 closure record |
| **UI** Refusal presentation | The management client reports a refused role removal or a busy gate as a bare 409 and explains none of the stable codes. The API contract is complete (`docs/04-api/contracts.md`). | **IAM-10.4** (role editor and UI) | Before the IAM-10.4 UI sign-off |

The acceptance criteria of the design gate are not changed to accommodate the tests.

## 3. Observations made during the remediation

1. The framework offers no public, non-creating way to ask whether a named options instance exists. The handler binding therefore cannot report "not yet built"; it builds as the first request would. In every request path the options already exist.
2. If a change token source for `OpenIdConnectOptions` is ever registered, the handler will be rebuilt on reload and the Now view will follow it with no change here. A test pins today's behaviour (the same instance after a reload), so that change would be noticed.
3. `EntraConfigurationState.IsUsable` and the merged snapshot still differ about `AuthenticationContextId` (finding F-2 of the WP0 record); unchanged, and the stricter one is used.
