# .NET dependency security remediation (October 2026)

Status: implemented on `fix/dotnet-dependency-security-2026-10`, local, not pushed. Base: `origin/master` at `814d663` (2026-10-23). SDK `10.0.402`, runtimes `10.0.13`.

A NuGet audit of the solution found vulnerable packages in 22 of its 64 projects. This record covers what was found, the smallest changes that remove it, what the change touched besides, and what was left alone. The general upgrade backlog is a separate record: [dotnet-dependency-modernization-matrix-2026-10.md](dotnet-dependency-modernization-matrix-2026-10.md).

## Baseline (before any change)

`dotnet restore Apperture.slnx --force` plus `dotnet package list --vulnerable --include-transitive`, nuget.org as the only source, `NuGetAuditMode=all`, `NuGetAuditLevel=low` (SDK defaults; nothing in the repository overrides them, there are no lock files and no `NuGetAuditSuppress`).

| Package | Resolved (baseline) | Severity | Advisory | Projects |
|---|---|---|---|---|
| `Microsoft.AspNetCore.Components.WebAssembly.Server` | 10.0.12 (direct) | High | GHSA-g27p-qf6j-c96c | Ui.Server |
| `Microsoft.AspNetCore.Components.WebAssembly.Server` | 10.0.12 (direct) | High | GHSA-qjpx-jqm5-h2c4 | Ui.Server |
| `System.Security.Cryptography.Pkcs` | 9.0.11, 9.0.13, 10.0.11 (transitive) | High | GHSA-846f-f764-76mx | 21 |
| `System.Security.Cryptography.Pkcs` | 9.0.11, 9.0.13, 10.0.11 (transitive) | Moderate | GHSA-2fmv-rv2c-59x7 | 21 |
| `MessagePack` | 2.5.302 (transitive) | Moderate | GHSA-qhrr-8q5h-9q3h | AppHost |

45 project-level findings (23 High, 22 Moderate) in 22 projects, four of them test projects. The solution-level restore printed 30 `NU1902`/`NU1903` warnings; it did not print AppHost's, which only `package list` and a restore of AppHost on its own show. Both were taken as the baseline.

Advisories and patched versions were confirmed against the GitHub advisory database and the NuGet vulnerability feed, and the fixed versions against nuget.org:

| Advisory | Affected | Patched | Nature |
|---|---|---|---|
| GHSA-846f-f764-76mx (CVE-2026-84008) | Pkcs 9.0.0-9.0.20, 10.0.0-10.0.12 | 9.0.21, 10.0.13 | `SignedCms.CheckSignature` quadratic CPU, unauthenticated |
| GHSA-2fmv-rv2c-59x7 (CVE-2026-70162) | same | same | `EnvelopedCms.Decode` quadratic CPU, Windows |
| GHSA-g27p-qf6j-c96c (CVE-2026-102870) | WebAssembly.Server 10.0.0-10.0.12 | 10.0.13 | code injection in the Blazor WebAssembly debug proxy |
| GHSA-qjpx-jqm5-h2c4 (CVE-2026-96230) | same | same | script injection in the `/_framework/debug` page used by `dotnet watch` |
| GHSA-qhrr-8q5h-9q3h (CVE-2026-92707) | MessagePack < 2.5.303 | 2.5.303 | allocation before the depth check on nested arrays |
| GHSA-7rj2-5w9w-r9jx | MessagePack < 2.5.307 | 2.5.307 | LZ4 length overflow, process crash. **Not yet in the NuGet audit feed** |

## Dependency chains

- **Pkcs.** `Microsoft.EntityFrameworkCore.SqlServer` -> `Microsoft.Data.SqlClient` -> `Pkcs`. SqlClient 6.1.6 (reached through EF Core 10.0.12 by the projects that do not reference SqlClient themselves) asks for 9.0.11; the centrally pinned 7.0.3 asks for 9.0.13 on the `net9.0` asset group that `net10.0` consumers use. AppHost has its own chain: `Aspire.Hosting.SqlServer` 13.5.3 asks for 10.0.11 directly and for SqlClient 7.0.1.
- **MessagePack.** `Aspire.Hosting.AppHost` / `Aspire.Hosting.SqlServer` -> `Aspire.Hosting` -> `StreamJsonRpc` 2.25.29 -> `MessagePack` 2.5.302.
- **WebAssembly.Server.** Direct reference from Ui.Server, and it requires the matching `Microsoft.AspNetCore.Components.WebAssembly`, which Ui.Client references directly.

## Why a transitive pin rather than an upstream upgrade

Upgrading the upstream package would be preferred, and none of the options remove the vulnerable Pkcs:

| Option | Pkcs it brings on `net10.0` | Fixed? |
|---|---|---|
| `Microsoft.Data.SqlClient` 7.0.3 (current) | 9.0.13 | no (needs >= 9.0.21) |
| `Microsoft.Data.SqlClient` 7.1.1 (latest) | 9.0.18 | no |
| `Microsoft.EntityFrameworkCore.SqlServer` 10.0.13 (-> SqlClient 6.1.7) | 9.0.11 | no |
| `Aspire.Hosting.SqlServer` 13.6.3 (latest) | 10.0.12 | no (needs >= 10.0.13) |

So the fix has to come from this repository. Each project that reaches Pkcs would otherwise need its own `PackageReference` (22 projects, mostly libraries that never use it). Central transitive pinning (`CentralPackageTransitivePinningEnabled`) does this once in `Directory.Packages.props`; it is NuGet's mechanism for overriding a transitive version under central package management. The pins are commented with the advisory they answer and the condition for removing them.

For MessagePack the audit feed's minimum (2.5.303) was not used: 2.5.307 also fixes GHSA-7rj2-5w9w-r9jx, which affects every version below it and which the feed does not yet report, so 2.5.303 would have passed the audit while still vulnerable. 2.5.307 is also the version `StreamJsonRpc` 2.26.150 and `Aspire.Hosting` 13.6.3 request, so it is a combination upstream already ships.

## Changes (one file: `Directory.Packages.props`)

| Change | Before | After |
|---|---|---|
| `CentralPackageTransitivePinningEnabled` | off | on |
| `System.Security.Cryptography.Pkcs` (new pin) | 9.0.11 / 9.0.13 / 10.0.11 | 10.0.13 |
| `MessagePack` (new pin) | 2.5.302 | 2.5.307 |
| `Microsoft.AspNetCore.Components.WebAssembly.Server` | 10.0.12 | 10.0.13 |
| `Microsoft.AspNetCore.Components.WebAssembly` | 10.0.12 | 10.0.13 |
| Every other `10.0.12` servicing pin (26 entries: `Microsoft.Extensions.*`, `Microsoft.EntityFrameworkCore.*`, `Microsoft.AspNetCore.*`, `System.Security.Cryptography.ProtectedData`) | 10.0.12 | 10.0.13 |

The family bump is required, not tidying: `Components.WebAssembly.Server` 10.0.13 requires `Components.WebAssembly` 10.0.13, which requires `Microsoft.Extensions.Logging`, `Configuration.Binder` and `Configuration.Json` 10.0.13, and a lower direct reference would be a downgrade (`NU1605`). Moving the whole 10.0.12 set keeps the ASP.NET Core, EF Core and Extensions packages on one servicing release, which is also the installed runtime (10.0.13). The `10.0.0` pin for `Microsoft.Extensions.Hosting.WindowsServices` and the separate `10.10.x` Extensions telemetry packages were not touched.

No SDK, `global.json`, CI, `NuGet.Config` or source change. `global.json` allows `10.0.100` with `latestFeature` roll-forward, and CI installs `10.0.x`; both already resolve to a runtime and SDK that contain the servicing release. Restore behaviour changes in one respect only: with transitive pinning on, any package that has a `PackageVersion` entry is now held at that version wherever it appears in a graph (see side effects).

## Result

| Check | Baseline | After |
|---|---|---|
| `package list --vulnerable --include-transitive`, 64 projects | 45 findings in 22 projects | **0 findings**, 0 problems |
| Solution restore `NU19xx` warnings | 30 | **0** |
| AppHost restore on its own | 3 findings | **0** |
| `poc/BlazorWasmWindowsService` (outside `Apperture.slnx`, shares the central package file) | not audited at baseline; it used the central 10.0.12 WebAssembly versions | restores with no warnings |
| Pkcs versions in the resolved graph | 9.0.11, 9.0.13, 10.0.11 | 10.0.13 only |
| MessagePack | 2.5.302 | 2.5.307 only |
| WebAssembly.Server / WebAssembly | 10.0.12 | 10.0.13 |

This is real remediation, not a feed change: the affected versions are absent from every project's `project.assets.json`, which was checked by diffing the full resolved graph of all 56 projects that have package assets (1,989 -> 1,953 package entries; 332 -> 259 distinct package/version pairs). The audit-feed source was identical for both runs (taken the same day).

## Side effects of transitive pinning, and why they are acceptable

The graph diff shows 1,015 per-project changes; none is a downgrade.

- **Intended**: 10.0.12 family and Pkcs, MessagePack, as above.
- **Six persistence libraries** (`Persistence.SqlServer`, `RuntimePlane.Engine`, `RuntimePlane.DTFx`, `RuntimePlane.Persistence.SqlServer`, `Proof.CampusNexus.StudentReview.Persistence`, `Sample.ApiPolling.Persistence`) used to resolve SqlClient 6.1.6, because nothing in their own graph asked for the pinned 7.0.3. They now resolve 7.0.3, dropping the SqlClient 6.x-only packages (`Microsoft.Identity.Client.Broker`, `.NativeInterop`, `.Extensions.Msal`, Azure.Core/Identity 1.50/1.17, `System.ClientModel` 1.8, `Microsoft.Bcl.AsyncInterfaces` 8.0.0, `System.Memory.Data` 8.0.1) and adding `Microsoft.Data.SqlClient.Extensions.Abstractions`, `.Internal.Logging` 7.0.3 and `Microsoft.Bcl.Cryptography`. Of the 15 other projects that resolve SqlClient, 14 (every host except AppHost, and every test project) already had 7.0.3, because the highest version wins at the application; so the deployed graph does not change and the libraries now agree with it. After the change all 21 projects that resolve SqlClient have 7.0.3.
- **Management.Host**: `Microsoft.OpenApi` 2.12.0 -> 2.12.2, requested by `Microsoft.AspNetCore.OpenApi` 10.0.13.
- **AppHost**: `Microsoft.Data.SqlClient` 7.0.1 -> 7.0.3, OpenTelemetry 1.15.3 -> 1.18.0 (the exporter and hosting packages already pinned centrally), SNI runtime 6.0.2 -> 6.0.3. AppHost is the local development orchestrator and is not part of a deployed Instance.
- Hosts otherwise change only in the 10.0.13 family and Pkcs 9.0.13 -> 10.0.13 (Management.Host, WorkerHost, Cli.Host).

## Exposure

Repository code does not call `SignedCms`/`EnvelopedCms` or use MessagePack or SignalR, and Ui.Server uses `UseBlazorFrameworkFiles` but not `UseWebAssemblyDebugging`. In practice that means:

- The Pkcs advisories are denial of service reachable only through code that parses attacker-supplied CMS (here, inside SqlClient's certificate handling), so the runtime exposure was low, but the package ships in every SQL-connected host and every test project, and it is fixed everywhere.
- **Runtime requirement.** The ASP.NET Core shared framework contains its own `System.Security.Cryptography.Pkcs`, and the SDK drops the package copy when the framework's is the same or newer. In the 10.0.13 build output, Management.Host and WorkerHost do not carry the package at all (`Management.Host.deps.json` has no Pkcs entry), so on a framework-dependent deployment they run whichever Pkcs the installed ASP.NET Core runtime supplies. The pin does not change that. `Cli.Host`, AppHost and the test hosts, which are not on the ASP.NET Core framework, carry Pkcs 10.0.13 themselves. **Every machine that runs the hosts needs the ASP.NET Core runtime 10.0.13 or later** for the Pkcs fix to be in effect; the development machine already has 10.0.13. Nothing in this repository pins a runtime version (no Dockerfile, no self-contained publish; CI installs `10.0.x`), so that requirement belongs in the on-premises installation and cloud image procedures.
- The two WebAssembly.Server advisories concern the debugger proxy and the `dotnet watch` development page. They are a developer-machine risk, not a production request path.
- MessagePack is reached only by AppHost's RPC to the Aspire tooling on a developer machine.

## Evidence

Run from a shim directory so that the source-text tests, which find the repository root by a `.git` directory, work from a worktree (`F:\Code\Apperture\wt-dotnet-security-shim`), with an isolated `--artifacts-path`, Release, single node.

| Check | Result |
|---|---|
| Clean Release build of `Apperture.slnx` | 0 errors, 71 warnings (all nullable, obsolete-API, analyzer; none from NuGet or the SDK; two `ASPIRE010` are existing) |
| `Apperture.UnitTests` | 5,228 passed, 0 failed, 0 skipped |
| `Apperture.ArchTests` | 38 / 0 / 0 |
| `Apperture.Management.Ui.Shared.Tests` | 1,497 / 0 / 0 |
| `Apperture.Management.Ui.Client.Tests` | 529 / 0 / 0 |
| `Apperture.AppHost.Diagnostics.Tests` | 7 / 0 / 0 |
| `Apperture.EndToEndTests` | contains no tests (placeholder): 0 discovered |
| `Apperture.IntegrationTests`, focused (32 classes, 624 cases) | 624 passed, 0 failed, 0 skipped |
| `Apperture.BrowserTests` (`APPERTURE_BROWSER_TESTS=1`; real Management.Host, real Ui.Server child process, Chromium, Firefox, WebKit) | first full run: 329 passed, **1 failed**, 5 not executed; second full run: **330 passed, 0 failed**, 5 not executed |
| Host operational smoke (below) | pass |
| MessagePack/StreamJsonRpc/Pkcs behaviour proof (below) | pass |

The 5 not-executed browser tests are `GovernanceUserIdentityEvidenceTests`, which need a second opt-in (`APPERTURE_UI_EVIDENCE=1`) and are skipped by the blocking CI job too.

**The one browser failure** was `RequestForgeryBaselineTests.A_cross_site_navigation_to_step_up_or_link_is_refused_before_the_endpoint (Chromium)` in the first run. It failed with `net::ERR_NO_BUFFER_SPACE` (Windows socket buffer exhaustion) while navigating to the test's own loopback attacker page, on the second navigation of the test; the first navigation in the same test had already returned the expected `403`, and the machine was at about 2 GB free memory. It is an environment fault, not an assertion about product behaviour. The whole `RequestForgeryBaselineTests` class then passed on its own (35/35, all three engines) and the complete suite passed on a second run (330/0). It has not been seen to fail on a clean machine, and I have not reproduced it on the pre-change graph.

**Host operational smoke.** The real `Apperture.Management.Host` and `Apperture.RuntimeHost.Windows.WorkerHost` executables from the patched build, started against a throwaway catalog on the dedicated test SQL server (created and dropped by the script; the developer's database and Aspire stack were not touched, and AppHost and Ui.Server were not started because the shipped UI resolves loopback to the live API port):

- The host applied all 92 migrations (74 tables) and was ready in about 8 s; `GET /api/bootstrap/status` and `GET /api/auth/sign-in-options` returned 200, and a `POST` without the browser-trust headers returned 403 (`origin_missing`), so the request-forgery middleware is intact.
- The worker reached its run loop, started the start-request worker and the schedule dispatch loop, and stayed up for 60 s with no `fail:` or `crit:` entries in either process (the host's one `warn:` is the deliberate 403 above).
- A first attempt, before the script created the catalog, failed at bootstrap validation with SQL error 4060 surfaced intact through SqlClient 7.0.3; that was the script, not the build.
- Retained logs were scanned for the SQL password; none contained it.

**Assemblies in the patched build output:** Management.Host and WorkerHost carry SqlClient 7.0.3, EF Core 10.0.13, ProtectedData 10.0.13 (and no Pkcs copy, see the runtime requirement above); Ui.Server carries `Components.WebAssembly.Server` 10.0.13; Cli.Host carries Pkcs 10.0.13; AppHost carries MessagePack 2.5.307.

The focused integration set covers the connection and authentication wiring (`PersistenceWiringTests`, `SqlServerTestConfigurationTests`, Management Host bootstrap, session identity, account and governance HTTP), EF migrations (`Ctx2`, `ResourceContext`, `TstB1`, `TstB2`, Entra registration), secrets and cryptography (`IntegrationTestSecretProtectorTests`, `SecretStoreTransaction`, `ConnectionSecretResolutionPaths`, `SettingSecretOwnership`, connection secret fields over HTTP), and workers, scheduling and audit (`ScheduleDispatchAudit`, `ScheduleLifecycleAudit`, `ScheduleDelete`, `AuditCorrelationId`, `AuditRecorder`, `AuditAppendBridge`, `AuditTimeline`, `WorkflowExternalControl`, `RuntimeShutdownRequest`, `RuntimeHealthBlockedDeferred`, `RunsBlockedFacet`, `Epic73SqlSeams`, `ApiPollingDurableSyncState`). The complete integration project (135 files) was not run.

These tests soft-skip as passed when SQL is not configured, so the run was verified against the server itself: the persistent fixture catalogs `Apperture_IntegrationTests` and `Apperture_ManagementGovernanceTests` show writes inside the run window, and the SQL Server error log recorded 154 catalog start-ups (150 distinct throwaway catalogs) in the same window.

**Behaviour proof for the one pin that overrides a version an upstream package was built against** (a throwaway console project, `artifacts/mp-proof`, not part of the solution): `StreamJsonRpc` 2.25.29, built for MessagePack 2.5.302, completes a full-duplex RPC round trip on 2.5.307; the advisory's payload (600 nested array headers, 32 KiB) allocates **114.7 MiB** on 2.5.302 and **0.2 MiB** on 2.5.307 before being rejected; and a `SignedCms` sign/verify round trip works on Pkcs 10.0.13.

## Not done, and why

- **Aspire 13.6.3.** It would remove the need for the MessagePack pin but not the Pkcs pin (it requests 10.0.12), changes the AppHost SDK version and CI, and is a minor upgrade of the orchestration layer. Deferred to the modernization matrix.
- **SqlClient 7.1.1.** Does not fix Pkcs (9.0.18); a database driver minor upgrade with no security gain. Deferred.
- **AppHost orchestration run.** Starting AppHost would start containers and mount the `apperture-aspire-sql` volume that holds the developer's existing database, and the developer's stack may be running. It was not started. AppHost was restored and built, its diagnostics tests pass, and the changed libraries it loads were exercised by the proof above.
- **Residual advisories.** None are reported by the audit for the final graph. GHSA-7rj2-5w9w-r9jx is not in the feed and is covered only because 2.5.307 was chosen deliberately; when the feed catches up it should report nothing.

## Removing the pins later

Remove the `System.Security.Cryptography.Pkcs` pin once SqlClient (and Aspire) request Pkcs >= 10.0.13 on `net10.0`, or >= 9.0.21 on the `net9.0` group, and the `MessagePack` pin once Aspire/StreamJsonRpc request >= 2.5.307. `CentralPackageTransitivePinningEnabled` can then stay on or off; if it is turned off, the six persistence libraries return to SqlClient 6.1.x in their own assets.
