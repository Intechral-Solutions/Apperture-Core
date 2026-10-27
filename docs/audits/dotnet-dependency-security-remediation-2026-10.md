# .NET dependency security remediation (October 2026)

Status: implemented on `fix/dotnet-dependency-security-2026-10`, local, not pushed. Base: `origin/master` at `814d663` (2026-10-23). SDK `10.0.402`, runtimes `10.0.13`. The package change (`151a286`) was accepted after independent review; this record was corrected after that review (2026-10-27) without changing any package version.

A NuGet audit of the solution found vulnerable packages in 22 of its 64 projects. A check of the MessagePack maintainers' own security publications found two further advisories that affect the baseline version and that the NuGet feed does not report. This record covers what was found, the smallest changes that remove it, what the change touched besides, and what was left alone. The general upgrade backlog is a separate record: [dotnet-dependency-modernization-matrix-2026-10.md](dotnet-dependency-modernization-matrix-2026-10.md).

## Baseline (before any change)

`dotnet restore Apperture.slnx --force` plus `dotnet package list --vulnerable --include-transitive`, nuget.org as the only source, `NuGetAuditMode=all`, `NuGetAuditLevel=low` (SDK defaults; nothing in the repository overrides them, there are no lock files and no `NuGetAuditSuppress`).

| Package | Resolved (baseline) | Severity | Advisory | Projects |
|---|---|---|---|---|
| `Microsoft.AspNetCore.Components.WebAssembly.Server` | 10.0.12 (direct) | High | GHSA-g27p-qf6j-c96c | Ui.Server |
| `Microsoft.AspNetCore.Components.WebAssembly.Server` | 10.0.12 (direct) | High | GHSA-qjpx-jqm5-h2c4 | Ui.Server |
| `System.Security.Cryptography.Pkcs` | 9.0.11, 9.0.13, 10.0.11 (transitive) | High | GHSA-846f-f764-76mx | 21 |
| `System.Security.Cryptography.Pkcs` | 9.0.11, 9.0.13, 10.0.11 (transitive) | Moderate | GHSA-2fmv-rv2c-59x7 | 21 |
| `MessagePack` | 2.5.302 (transitive) | Moderate | GHSA-qhrr-8q5h-9q3h | AppHost |

The 45 findings above (23 High, 22 Moderate) are the findings **reported by the NuGet audit**, in 22 projects, four of them test projects. The solution-level restore printed 30 `NU1902`/`NU1903` warnings; it did not print AppHost's, which only `package list` and a restore of AppHost on its own show. Both were taken as the baseline.

### Advisory sources

The advisories fall into two classes, and they are not equally well established.

**Reported by the NuGet audit feed** (also present in GitHub's global advisory database; patched versions confirmed against nuget.org):

| Advisory | Affected | Patched | Nature |
|---|---|---|---|
| GHSA-846f-f764-76mx (CVE-2026-84008) | Pkcs 9.0.0-9.0.20, 10.0.0-10.0.12 | 9.0.21, 10.0.13 | `SignedCms.CheckSignature` quadratic CPU, unauthenticated |
| GHSA-2fmv-rv2c-59x7 (CVE-2026-70162) | same | same | `EnvelopedCms.Decode` quadratic CPU, Windows |
| GHSA-g27p-qf6j-c96c (CVE-2026-102870) | WebAssembly.Server 10.0.0-10.0.12 | 10.0.13 | code injection in the Blazor WebAssembly debug proxy |
| GHSA-qjpx-jqm5-h2c4 (CVE-2026-96230) | same | same | script injection in the `/_framework/debug` page used by `dotnet watch` |
| GHSA-qhrr-8q5h-9q3h (CVE-2026-92707) | MessagePack < 2.5.303 | 2.5.303 | allocation before the depth check on nested arrays (Moderate) |

**Found through the package maintainer's own security publications** (the MessagePack-CSharp repository advisories, listed with `gh api repos/MessagePack-CSharp/MessagePack-CSharp/security-advisories`). Both are published there, neither carries a CVE, neither was in the NuGet audit feed, and both returned 404 from GitHub's global advisory database when checked (GHSA-7rj2-5w9w-r9jx on 2026-10-23 and 2026-10-27, GHSA-fx6j-x9m5-vmrf on 2026-10-27, and by the independent review). An earlier version of this record described the second as confirmed against the GitHub advisory database; that was wrong.

| Advisory | Affected (as stated by the maintainer) | Patched | Nature |
|---|---|---|---|
| GHSA-fx6j-x9m5-vmrf | MessagePack [2.1.115, 2.5.305) and [3.0, 3.1.10) | 2.5.305, 3.1.10 | unescaped dot metacharacters in `AssemblyNameVersionSelectorRegex` cause catastrophic backtracking (CWE-1333), Moderate, published 2026-09-23 |
| GHSA-7rj2-5w9w-r9jx | MessagePack < 2.5.307 and [3.0, 3.1.11) | 2.5.307, 3.1.11 | integer overflow in the LZ4 decompressor length accumulation, process crash (CWE-190), High, published 2026-10-03 |

The baseline MessagePack 2.5.302 is inside the affected range of **all three** MessagePack advisories; the selected 2.5.307 is outside all three. The maintainer's release notes for v2.5.305 and v2.5.307 name the corresponding fixes.

**What is established about each class.** For the five audit-feed advisories the fix is established by the feed and the vendor advisories, and the MessagePack one (GHSA-qhrr) was also exercised by the behaviour proof below. For the two maintainer-published advisories the fix status rests on the maintainer's stated affected and patched ranges and release notes; this work did not independently reproduce them. A clean NuGet audit is therefore a statement about what the feed knows, not proof that no vulnerability remains in the resolved graph.

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

For MessagePack the audit feed's minimum (2.5.303) was not used. The maintainers publish two further advisories that the feed does not report: GHSA-fx6j-x9m5-vmrf, fixed in 2.5.305, and GHSA-7rj2-5w9w-r9jx, fixed in 2.5.307. Version 2.5.303 is inside the affected range of both, so it would have passed the audit while still being vulnerable. 2.5.307 is outside all three ranges. It is also the version `StreamJsonRpc` 2.26.150 and `Aspire.Hosting` 13.6.3 request, so it is a combination upstream already ships.

## Changes (one file: `Directory.Packages.props`)

| Change | Before | After |
|---|---|---|
| `CentralPackageTransitivePinningEnabled` | off | on |
| `System.Security.Cryptography.Pkcs` (new pin) | 9.0.11 / 9.0.13 / 10.0.11 | 10.0.13 |
| `MessagePack` (new pin) | 2.5.302 (three advisories, one of them in the feed) | 2.5.307 |
| `Microsoft.AspNetCore.Components.WebAssembly.Server` | 10.0.12 | 10.0.13 |
| `Microsoft.AspNetCore.Components.WebAssembly` | 10.0.12 | 10.0.13 |
| Every other `10.0.12` servicing pin (26 entries: `Microsoft.Extensions.*`, `Microsoft.EntityFrameworkCore.*`, `Microsoft.AspNetCore.*`, `System.Security.Cryptography.ProtectedData`) | 10.0.12 | 10.0.13 |

The family bump is required, not tidying: `Components.WebAssembly.Server` 10.0.13 requires `Components.WebAssembly` 10.0.13, which requires `Microsoft.Extensions.Logging`, `Configuration.Binder` and `Configuration.Json` 10.0.13, and a lower direct reference would be a downgrade (`NU1605`). Moving the whole 10.0.12 set keeps the ASP.NET Core, EF Core and Extensions packages on one servicing release, which is also the installed runtime (10.0.13). The `10.0.0` pin for `Microsoft.Extensions.Hosting.WindowsServices` and the separate `10.10.x` Extensions telemetry packages were not touched.

No SDK, `global.json`, CI, `NuGet.Config` or source change. `global.json` allows `10.0.100` with `latestFeature` roll-forward, and CI installs `10.0.x`; both already resolve to a runtime and SDK that contain the servicing release. Restore behaviour changes in one respect only: with transitive pinning on, any package that has a `PackageVersion` entry is now held at that version wherever it appears in a graph (see the next two sections for the effect on the resolved graph and on generated package metadata).

## Result

| Check | Baseline | After |
|---|---|---|
| NuGet audit: `package list --vulnerable --include-transitive`, 64 projects | 45 findings in 22 projects | **0 findings**, 0 problems |
| Solution restore `NU19xx` warnings | 30 | **0** |
| AppHost restore on its own | 3 findings | **0** |
| `poc/BlazorWasmWindowsService` (outside `Apperture.slnx`, shares the central package file) | 2 High findings, both in `BlazorWasmPoc.Host`: `Components.WebAssembly.Server` 10.0.12, GHSA-g27p-qf6j-c96c and GHSA-qjpx-jqm5-h2c4 (found by the independent review; this work did not audit it at baseline) | 0 findings, restores with no warnings |
| Pkcs versions in the resolved graph | 9.0.11, 9.0.13, 10.0.11 | 10.0.13 only |
| MessagePack (three advisories affect 2.5.302; one is in the feed) | 2.5.302 | 2.5.307 only, outside all three ranges |
| WebAssembly.Server / WebAssembly | 10.0.12 | 10.0.13 |

This is real remediation, not a feed change: the affected versions are absent from every project's `project.assets.json`, which was checked by diffing the full resolved graph of all 56 projects that have package assets (1,989 -> 1,953 package entries; 332 -> 259 distinct package/version pairs), and the independent review repeated the comparison from the base and the patched commit and reached the same result. The audit-feed source was identical for both runs (taken the same day).

The zero in the first rows means that the feed reports nothing for the final graph. It is not a claim that the graph is free of every vulnerability: the two maintainer-published MessagePack advisories were invisible to the feed, and the same could be true of other packages. The audit result and the package-maintainer check are separate evidence and are reported separately above.

## Side effects of the change on the resolved graph

The comparison of the base and the patched commit lists 1,100 per-project package changes: 1,028 version increases, 54 removals and 18 additions, and no downgrade. They are 1,015 changes in the 44 solution projects that changed, 28 in the `browser-wasm` target of Ui.Client, and 57 in the two `poc/` projects. Most are the 10.0.12 to 10.0.13 family move. The rest follow from central transitive pinning, which holds every package that has a `PackageVersion` entry at that version wherever it appears in a graph. The secondary changes, by cause:

- **Six persistence libraries move from SqlClient 6.1.6 to 7.0.3.** `Persistence.SqlServer`, `RuntimePlane.Engine`, `RuntimePlane.DTFx`, `RuntimePlane.Persistence.SqlServer`, `Proof.CampusNexus.StudentReview.Persistence` and `Sample.ApiPolling.Persistence` used to resolve SqlClient 6.1.6 through EF Core, because nothing in their own graph asked for the pinned 7.0.3. In those six:
  - IdentityModel moves from 7.7.1 to 8.16.0 (`Microsoft.IdentityModel.JsonWebTokens`, `.Logging`, `.Protocols`, `.Protocols.OpenIdConnect`, `.Tokens`, `System.IdentityModel.Tokens.Jwt`), and `Microsoft.IdentityModel.Abstractions` from 8.14.0 to 8.16.0. That is a major-version move in those libraries' own graphs; every other project already resolved 8.16.0 or later.
  - `System.Configuration.ConfigurationManager` and `System.Diagnostics.EventLog` move from 9.0.11 to 9.0.13.
  - `System.Security.Cryptography.ProtectedData` moves from 9.0.11 to 10.0.13 in `Sample.ApiPolling.Persistence`.
  - The packages only SqlClient 6.x asked for are dropped (`Microsoft.Identity.Client.Broker`, `.NativeInterop`, `.Extensions.Msal`, Azure.Core 1.50 and Azure.Identity 1.17, `System.ClientModel` 1.8, `Microsoft.Bcl.AsyncInterfaces` 8.0.0, `System.Memory.Data` 8.0.1), and `Microsoft.Data.SqlClient.Extensions.Abstractions`, `.Internal.Logging` 7.0.3 and `Microsoft.Bcl.Cryptography` are added.
  - Of the 15 other projects that resolve SqlClient, 14 (every host except AppHost, and every test project) already had 7.0.3, because the highest version wins at the application, so the deployed graph does not change and the libraries now agree with it. All 21 projects that resolve SqlClient now have 7.0.3.
- **ProtectedData in four other projects.** `Bootstrap`, `Cli.Bootstrap` and AppHost resolved `System.Security.Cryptography.ProtectedData` 9.0.13 (reached through SqlClient's `ConfigurationManager`); it is now the centrally pinned 10.0.13. The 27 projects that resolved 10.0.12 move to 10.0.13 with the family.
- **AppHost.** Beyond Pkcs and MessagePack:
  - the Extensions packages that Aspire resolves at 10.0.11 (Configuration, DependencyInjection, Diagnostics, FileProviders.Physical, FileSystemGlobbing, Hosting, Http, Logging and its providers, Options.ConfigurationExtensions) and the abstractions at 10.0.12 move to 10.0.13, as does `System.Diagnostics.EventLog` (10.0.11);
  - `Microsoft.Data.SqlClient` 7.0.1 to 7.0.3 (with `.Extensions.Abstractions` and `.Internal.Logging` 1.0.0 to 7.0.3 and the SNI runtime 6.0.2 to 6.0.3);
  - OpenTelemetry 1.15.3 to 1.18.0 for the five packages the repository already pins centrally.

  AppHost is the local development orchestrator and is not part of a deployed Instance.
- **Azure Functions template and Azure secrets.** `RuntimeHost.Azure.Template` has 30 changes to 10.0.13: 16 packages that resolved at 10.0.0 and 4 at 10.0.3, `Microsoft.Extensions.Http` from 6.0.0, and 9 from 10.0.12. `RuntimeHost.Azure.Secrets` has 13: 4 abstractions packages from 10.0.3 and 9 from 10.0.12. `RuntimeHost.Azure.Shared` is unchanged. These follow from the family bump, not from a dedicated pin.
- **Management.Host.** `Microsoft.OpenApi` 2.12.0 to 2.12.2, requested by `Microsoft.AspNetCore.OpenApi` 10.0.13, and Pkcs 9.0.13 to 10.0.13 in the resolved graph.
- Other hosts change only in the 10.0.13 family and, where they resolve it, Pkcs 9.0.13 to 10.0.13 (WorkerHost, Cli.Host).

## Central transitive pinning and package metadata

With `CentralPackageTransitivePinningEnabled` on, NuGet can promote a centrally pinned package that a project reaches only transitively into that project's own dependency list, so that the central version acts as a floor for it. This is the mechanism that raises Pkcs, but it applies to every package with a `PackageVersion` entry, not only the two new pins. It has two consequences worth knowing:

- **Generated package metadata grows.** The nuspec that `dotnet pack` generates now declares the promoted packages as dependencies. Checked by packing the two affected projects from the base and from the patched commit: `Apperture.Persistence.SqlServer` goes from 11 to 23 declared dependencies (12 added: `Microsoft.Data.SqlClient` 7.0.3, `Microsoft.EntityFrameworkCore`, `System.Security.Cryptography.Pkcs` and `.ProtectedData`, `Zxcvbn` and seven `Microsoft.Extensions.*` packages, and six existing ones moved from 10.0.12 to 10.0.13), and `Apperture.ControlPlane.Security` goes from 35 to 49 (14 added, including SqlClient, EF Core, EF Core SqlServer, Pkcs, ProtectedData and Zxcvbn). The independent review verified the same counts.
- **A central pin is a floor that can collide with an upstream requirement.** If an upstream package ever requires a higher version than the central pin for a package that is pinned this way, restore reports a downgrade error (`NU1109`/`NU1605`) which has to be reconciled by raising the central version. The pins are therefore kept deliberately few, and the two new ones are commented in `Directory.Packages.props` with the condition for removing them.

The repository does not publish these packages today, so nothing consumable changes. `scripts/ci/pack.ps1` does run `dotnet pack` over `Apperture.slnx`, so any future packaging or distribution workflow must review the generated dependency metadata (and the packages marked `IsPackable`) before publishing. Once the upstream packages ask for patched versions (see "Removing the pins later"), the pins can be removed and the metadata returns to what each library declares itself.

## Exposure

Repository code does not call `SignedCms`/`EnvelopedCms` or use MessagePack or SignalR, and Ui.Server uses `UseBlazorFrameworkFiles` but not `UseWebAssemblyDebugging`. In practice that means:

- The Pkcs advisories are denial of service reachable only through code that parses attacker-supplied CMS (here, inside SqlClient's certificate handling), so the runtime exposure was low. The vulnerable version was in the resolved graph of every SQL-connected project, including test projects, and that is now fixed in the graph (what a host actually loads is covered under the runtime prerequisite below).
- The two WebAssembly.Server advisories concern the debugger proxy and the `dotnet watch` development page. They are a developer-machine risk, not a production request path.
- MessagePack is reached only by AppHost's RPC to the Aspire tooling on a developer machine. That applies to all three MessagePack advisories.

### Runtime prerequisite (owned by OPS-1)

The ASP.NET Core shared framework contains its own `System.Security.Cryptography.Pkcs`, and the SDK drops the package copy when the framework's is the same or newer. In the 10.0.13 build output, Management.Host and WorkerHost do not carry the package at all (`Management.Host.deps.json` has no Pkcs entry), so a framework-dependent deployment runs whichever Pkcs the installed ASP.NET Core runtime supplies. **The NuGet pin does not by itself ensure that a deployment has a patched shared runtime.** The hosts are framework-dependent with `TargetLatestRuntimePatch` false and no override in the repository, so their runtime configuration asks only for the `Microsoft.AspNetCore.App` 10.0.0 floor and they start on an unpatched 10.0.x runtime without complaint.

The minimum patched runtime validated by this work is **ASP.NET Core 10.0.13**; the development machine and CI's `10.0.x` currently resolve to it. Nothing in this repository pins a runtime version (no Dockerfile, no self-contained publish).

This prerequisite is assigned to **OPS-1 (Operability baseline)**, which delivers the Windows Server topology (SQL Server plus three hosts as Windows Services; see `docs/01-architecture/rc1-program-gate.md` section 6.8 and the pre-release roadmap). Before RC1, OPS-1 must verify that suitable, supported runtime servicing exists across that intended deployment topology (how the runtime is installed and kept patched on each machine). Raising the runtime floor in the hosts' runtime configuration is one option for OPS-1 to assess, so that a host refuses to start on an older runtime; it is **not** an authorized change in this work, and no runtime configuration, host binary, Docker image or deployment script was changed.

Hosts that are not on the ASP.NET Core framework carry the package copy themselves: `Cli.Host` and AppHost output include Pkcs 10.0.13. Test projects follow their own framework choice: `Apperture.IntegrationTests` uses the ASP.NET Core framework and does not carry a package copy. Each test project's output was not otherwise enumerated.

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

**Behaviour proof for the one pin that overrides a version an upstream package was built against** (a throwaway console project, `artifacts/mp-proof`, not part of the solution): `StreamJsonRpc` 2.25.29, built for MessagePack 2.5.302, completes a full-duplex RPC round trip on 2.5.307; the GHSA-qhrr-8q5h-9q3h payload (600 nested array headers, 32 KiB) allocates **114.7 MiB** on 2.5.302 and **0.2 MiB** on 2.5.307 before being rejected; and a `SignedCms` sign/verify round trip works on Pkcs 10.0.13. The proof exercises only that one MessagePack advisory; the regex (GHSA-fx6j-x9m5-vmrf) and LZ4 (GHSA-7rj2-5w9w-r9jx) fixes were not reproduced and rest on the maintainer's statements, as described under "Advisory sources".

## Not done, and why

- **Aspire 13.6.3.** It would remove the need for the MessagePack pin but not the Pkcs pin (it requests 10.0.12), changes the AppHost SDK version and CI, and is a minor upgrade of the orchestration layer. Deferred to the modernization matrix.
- **SqlClient 7.1.1.** Does not fix Pkcs (9.0.18); a database driver minor upgrade with no security gain. Deferred.
- **AppHost orchestration run.** Starting AppHost would start containers and mount the `apperture-aspire-sql` volume that holds the developer's existing database, and the developer's stack may be running. It was not started. AppHost was restored and built, its diagnostics tests pass, and the changed libraries it loads were exercised by the proof above.
- **Residual advisories.** The NuGet audit reports none for the final graph. The two maintainer-published MessagePack advisories (GHSA-fx6j-x9m5-vmrf, GHSA-7rj2-5w9w-r9jx) are not in the feed and are covered because 2.5.307 was chosen deliberately; when the feed catches up it should report nothing for them. No other package was checked against its maintainer's own publications, so the audit result should not be read as proof that nothing else is affected.
- **Runtime servicing.** Assigned to OPS-1 (see the runtime prerequisite above); not changed here.

## CI policy follow-up (not done)

Today `NU1901`-`NU1904` are warnings in CI, so a vulnerable package can merge. Making NuGet vulnerability auditing a meaningful CI gate is still recommended, but it is a separately approved policy and implementation task and CI was not changed here. It needs a way to record an exceptional, time-limited risk acceptance (who accepted it, why, and when it expires) rather than a blanket suppression, so that a gate does not push the team toward silencing warnings. Auditing itself must stay on.

## Removing the pins later

Remove the `System.Security.Cryptography.Pkcs` pin once SqlClient (and Aspire) request Pkcs >= 10.0.13 on `net10.0`, or >= 9.0.21 on the `net9.0` group, and the `MessagePack` pin once Aspire/StreamJsonRpc request >= 2.5.307. That upstream upgrade may allow some or all of the transitive pins to go. `CentralPackageTransitivePinningEnabled` can then stay on or off; if it is turned off, the six persistence libraries return to SqlClient 6.1.x in their own assets, and the generated package metadata returns to what each library declares itself.
