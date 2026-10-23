# .NET dependency modernization matrix (October 2026)

Status: recommendations only. Nothing in this record is implemented. It follows [dotnet-dependency-security-remediation-2026-10.md](dotnet-dependency-security-remediation-2026-10.md), which already moved the `10.0.12` ASP.NET Core / EF Core / Extensions family to `10.0.13`, the WebAssembly packages to `10.0.13`, and added transitive pins for `System.Security.Cryptography.Pkcs` (10.0.13) and `MessagePack` (2.5.307).

Method: `dotnet package list --outdated` on the baseline graph (top-level packages; the transitive form fails on this solution with "Sequence contains no matching element", so transitive skew was read from the resolved graph instead), nuget.org metadata for latest stable version, licence and deprecation, and a repository search for how each package is used. Latest versions are as of 2026-10-23. "Risk" is the risk to this repository, not to the package.

## Reading order

Do the security-relevant and cheap items first, keep each group as its own change, and run the validation named in the last column. Items are ranked within a group by benefit over risk.

## 1. Safe patch updates

| Package | Now | Latest | Benefit | Risk | Validate with |
|---|---|---|---|---|---|
| `Azure.Security.KeyVault.Secrets` | 4.11.1 | 4.11.2 | bug fixes in the secret resolution path | very low | Azure Secrets and secret-resolution unit tests |
| `xunit.runner.visualstudio` | 4.0.0 | 4.0.1 | runner fix; test tooling only | very low | any test project |
| `Microsoft.Extensions.Hosting.WindowsServices` | 10.0.0 | 10.0.13 | aligns the one package left on the base release with the rest of the family | low | Windows service host tests, WorkerHost/InstanceAgent startup |

## 2. Compatible minor updates

Release notes should be read before each; none changes a major version.

| Package | Now | Latest | Benefit | Risk | Notes and validation |
|---|---|---|---|---|---|
| `Microsoft.Data.SqlClient` | 7.0.3 | 7.1.1 | driver fixes and newer SNI | medium (the database driver) | **Does not remove the Pkcs pin** (7.1.1 asks for Pkcs 9.0.18 on the `net9.0` group). Run the complete `Apperture.IntegrationTests`, not a focused set. |
| `Aspire.Hosting.SqlServer` and the `Aspire.AppHost.Sdk` version in `Apperture.AppHost.csproj` | 13.5.3 | 13.6.3 | removes the need for the `MessagePack` pin (asks for 2.5.307); newer orchestration fixes | medium (the SDK version is in the project file, and developer machines need to match) | Pkcs is still requested at 10.0.12, so the Pkcs pin stays. Needs an AppHost run against a disposable volume, which the developer must do or approve (the default volume holds the existing development database). |
| `SkiaSharp`, `SkiaSharp.NativeAssets.Linux` | 4.152.0 | 4.153.1 | native image decoding of user-supplied profile photos; fixes in that parser are the likeliest security payoff in this table | low to medium (native code; keep the pair on one version) | profile photo and image normalisation tests, Linux container smoke |
| `Microsoft.Graph` | 6.6.0 | 6.9.0 | Entra provisioning API surface and fixes | medium | Check that it still requests Kiota 2.1.2 (current Kiota pins are latest). Entra link, probe and provisioning tests. |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `.Extensions.Hosting`, `.Instrumentation.AspNetCore`, `.Instrumentation.Http`, `.Instrumentation.Runtime` | 1.18.0 | 1.20.1 | newer instrumentation and exporter fixes | low to medium (move all five together; AppHost already pulls 1.18.0 through the central pin) | ServiceDefaults telemetry tests, a host start with the exporter configured |
| `Microsoft.Extensions.Http.Resilience`, `Microsoft.Extensions.ServiceDiscovery` | 10.10.0 | 10.11.0 | fixes from the separate Extensions telemetry train | low (move together) | ServiceDefaults tests |
| `Microsoft.Playwright` | 1.63.0 | 1.64.1 | newer browser builds for the security suite | low, but CI-visible | `.github/workflows/ci.yml` caches browsers under `playwright-${{ runner.os }}-1.63.0`; change that key in the same commit. First run re-downloads browsers. Run the whole browser-security suite. |
| `Microsoft.NET.Test.Sdk` | 18.10.0 | 18.11.0 | test host fixes | very low | all suites |
| `bunit` | 2.11.3 | 2.12.5 | component test fixes | low | `Apperture.Management.Ui.Client.Tests`, `Ui.Shared.Tests` |
| `Moq` | 4.20.72 | 4.21.0 | | low | all suites (about 130 test files use it) |
| `FluentAssertions` | 8.10.0 | 8.11.0 | | low | see the licence note in section 4 before spending effort here |

## 3. Major or breaking changes needing investigation

| Item | Now | Direction | Benefit | Why it needs investigation |
|---|---|---|---|---|
| `DurableTask.Core` | 2.0.0.6, **deprecated** on nuget.org (reason "Other") | successor `Microsoft.Azure.DurableTask.Core` 3.11.2 (Apache-2.0) | supported package, security fixes will only land there | major version and package id change in `Apperture.RuntimePlane.DTFx`. The DTFx adapter is a design intent and is not the execution path today (see the non-negotiables in `AGENTS.md`), so the first question is whether the adapter should be modernized now or when it is built out. |
| `xunit` 2.9.3 | **deprecated** ("Legacy") | `xunit.v3` | supported test framework | a migration of eight test projects and over 7,000 tests (runner, `IAsyncLifetime`, collection fixtures, `Skip` semantics). Pair it with a decision on `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk`. Large; best as its own work package. |
| .NET 11 (`11.0.0-rc.2` packages exist) | .NET 10 | stay on 10 | none yet | release candidate; the fixes for the advisories above are also in 10.0.13. Revisit at general availability, as a platform decision rather than a dependency one. |
| `MessagePack` 3.x | 2.5.307 (transitive only) | none | none | not referenced by this repository; the 3.x line is chosen by the Aspire/StreamJsonRpc authors. Do not pin to 3.x. |

## 4. Possibly obsolete, redundant or replaceable

| Package | Observation | Suggested action | Benefit |
|---|---|---|---|
| `Microsoft.AspNetCore.Http.Abstractions` 2.3.13 | The ASP.NET Core 2.x-era package, referenced only by `Apperture.ControlPlane.Security`. On `net10.0` these types come from the shared framework (`FrameworkReference Microsoft.AspNetCore.App`). The package adds itself and `Microsoft.AspNetCore.Http.Features` 2.3.12 to that library's graph. | Investigate replacing it with a `FrameworkReference`. Check that nothing relies on the 2.x assembly identity. | drops two legacy packages that sit outside the 10.0 servicing train; small change if confirmed |
| `FluentAssertions` 8.x | The 8.11.0 package ships the Xceed *Community License Agreement (for Non-Commercial Use)* as a licence file, not an SPDX expression. `Apperture.ModuleSdk.Testing` (SDK surface) references it, and about 760 test files use it. | Have the licence reviewed against how this repository and the SDK package are used and distributed. If it is a problem, the options are a commercial licence, a drop-in fork with the same API, or removing it from the SDK package's public dependencies. | settles a licensing question; no runtime change |
| `Moq` | 4.20.x went through the SponsorLink episode; 4.21.0 is current. About 130 files. | No action unless policy requires; `NSubstitute` is the common alternative. | low |
| `Zxcvbn` 0.1.1, `Blazor.Heroicons` 3.2.1.2, `NetArchTest.Rules` 1.3.2 | Small packages with a low release cadence. `NetArchTest.Rules` underpins the architecture tests. | Keep; recheck maintenance status at the next review. | n/a |
| `Microsoft.Azure.Functions.Worker` family (2.52.0 / Sdk 2.1.0 / Extensions.Http 3.3.0) | Used by the Azure runtime host template. All current. | Keep. | n/a |

## 5. Keep unchanged for now

| Item | Reason |
|---|---|
| `global.json` (`10.0.100`, `latestFeature`), CI `dotnet-version: "10.0.x"` | Already resolve to SDK 10.0.402 and runtime 10.0.13; no change is needed for the servicing release. |
| ASP.NET Core, EF Core and Extensions at 10.0.13 | Current stable servicing release; keep them together. |
| `System.Security.Cryptography.Pkcs` and `MessagePack` transitive pins | Needed until upstream catches up. Removal conditions are in the remediation record. |
| `Newtonsoft.Json` 13.0.4, `Microsoft.Kiota.*` 2.1.2, `Azure.Identity` 1.21.0, `Cronos` 0.13.0, `SSH.NET` 2026.0.0 | Already the latest stable release. |
| Transitive version skew in the final graph (`Microsoft.IdentityModel.*` 8.16 / 8.18 / 8.19.2, `Azure.Core` 1.53 / 1.54, `Polly.Core` 8.4 / 8.6, `Humanizer.Core` 2.14 / 3.0, `Grpc.*` and `Google.Protobuf` in AppHost) | Hosts take the highest version, nothing is flagged, and the skew comes from different upstream packages. Do not pin these speculatively; pin one only if a host is seen to load a version its dependants cannot use. `Microsoft.IdentityModel.*` is the one worth a look if sign-in packages are touched, since the OpenID Connect packages ride on it. |

## Suggested order

1. Section 1 (three tiny updates; one commit).
2. `Microsoft.Playwright` with the CI cache key, then `SkiaSharp` (parser exposure), then `OpenTelemetry` and the Extensions telemetry pair.
3. `Microsoft.Data.SqlClient` 7.1.1 together with a complete `Apperture.IntegrationTests` run.
4. The `Microsoft.AspNetCore.Http.Abstractions` investigation (cheap and removes a legacy dependency).
5. Aspire 13.6.3 once the developer can run AppHost against a disposable volume.
6. Decisions, not changes: the FluentAssertions licence review, the `DurableTask.Core` successor, and the `xunit` to `xunit.v3` migration.

## Process suggestions (not part of this change)

- Run `dotnet package list --vulnerable --include-transitive --format json` on a schedule, not only when a restore warning is noticed. Both the Pkcs and WebAssembly.Server advisories were published on 2026-10-13, and one MessagePack advisory (GHSA-7rj2-5w9w-r9jx) is not in the NuGet feed at all, so the feed alone is not a complete signal.
- Decide whether `NU1901`-`NU1904` should fail the CI restore. Today they are warnings, so a vulnerable package can merge. This is a policy choice for the owner; auditing itself must stay on.
- Consider `RestorePackagesWithLockFile` if reproducible restores matter for the on-premises deployments; none exists today.
