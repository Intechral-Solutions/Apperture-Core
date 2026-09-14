# CTX-3A Plan 1 — Contracts & Domain Foundations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the client/server DTO drift (fact F1), add the pure `ResourceContextViewScope`/`ResourceContextViewRoutes` value types, and refine the ResourceContext ownership-summary/archive rule (fact F4) with reactivation guards — the foundation every later CTX-3A plan (authorization, API, UI) builds on.

**Architecture:** This plan touches only `Apperture.Management.Client` (client DTOs), `Apperture.Management.Ui.Shared` (new pure value types), `Apperture.Core.Application`/`Apperture.Core.Domain` (ownership-summary contract, slug reservation), `Apperture.Persistence.SqlServer` (the SQL-side summary query), and `Apperture.ControlPlane.Security` (the reactivation guards in the existing management services). No controllers, no Razor components, no new authorization permission — those are later plans. Every task in this plan is independently unit-testable without a running API or browser.

**Tech Stack:** .NET / C# records and value types, EF Core (SQL Server provider + EF Core InMemory for tests), xUnit + FluentAssertions.

**Spec:** `docs/01-architecture/transitions/resource-contexts/ctx3-design-gate.md` — this plan implements design-gate §13 CTX-3A steps 1–2 (`§2.2`, `§3.4`, `§6.5`, `§9.3` non-change-test note, `§10.1` rows A1/A2/A11/A5, `§14` acceptance criteria for "Model and persistence" and part of "Management").

## Global Constraints

- `All` is UI view state only — never a `ResourceContext` row, id, or `null`-means-All in any state layer (design gate §2.2).
- The URL token for All is the literal string `all`; it is added to `ResourceContextSlug.ReservedSlugs` (design gate §2.2, A11).
- No `context:{id}` IAM scope, no context-scoped role/grant, is introduced anywhere in this plan (design gate §9.3) — this plan touches no authorization code at all except the two service-level reactivation guards, which reuse the resource family's *existing* permissions, not a new one.
- No delete endpoint/capability is added for `ResourceContext`, `Connection`, or `Schedule` (design gate §6.5, §7, D2). The archive refinement in this plan only changes what counts as "live"; it adds no new lifecycle operation.
- No resource-movement code (CTX-3B) is introduced or referenced as if available now (design gate §8, D1).
- `IResourceContextService` and `IResourceContextRepository` keep performing **no authorization** (design gate §9.1, `ResourceContextService.cs` lines 16-20) — the reactivation guards live in the resource-family services (`IntegrationManagementService`, `SqlScheduleManagementService`), not here.
- Every new client DTO field name must exactly match its already-existing server DTO twin's name and type (design gate A1/A2) so the parity test in Task 5 is meaningful.

---

## File Structure

**New files:**
- `src/Apperture.Core/Apperture.Core.Application/Services/ResourceContextOwnershipSummary.cs` — the `ResourceContextOwnershipSummary` record (§6.5).
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ResourceContextViewScope.cs` — the `readonly record struct ResourceContextViewScope` (§2.2).
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ResourceContextViewRoutes.cs` — the pure route-mode resolver (§3.4).
- `tests/Apperture.Management.Ui.Shared.Tests/ResourceContextViewScopeTests.cs`
- `tests/Apperture.Management.Ui.Shared.Tests/ResourceContextViewRoutesTests.cs`
- `tests/Apperture.UnitTests/ResourceContexts/ClientServerDtoParityTests.cs` — A2's reflection-based parity test.

**Modified files:**
- `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs` — client DTOs (`IntegrationDto`, `CreateIntegrationRequest`, `ConnectionDto`, `CreateConnectionRequest`, `RunSummaryDto`, `RunQueryFilter`) gain `ResourceContextId`/`ResourceContext`; `GetIntegrationsAsync`/`GetConnectionsAsync`/`GetSchedulesAsync` gain a `contextId` parameter.
- `src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs` — implementations of the three `Get*Async` methods build the `?contextId=` query string.
- `src/Apperture.ControlPlane/Apperture.Management.Client/IntegrationFleetDtos.cs` — `IntegrationFleetQueryParams` gains `Context`; `IntegrationFleetRowDto` gains `ResourceContextId`.
- `src/Apperture.Core/Apperture.Core.Domain/ResourceContexts/ResourceContextSlug.cs` — reserve `"all"`.
- `tests/Apperture.UnitTests/ResourceContexts/ResourceContextSlugTests.cs` — updated reserved-set assertions.
- `src/Apperture.Core/Apperture.Core.Application/Services/IResourceContextRepository.cs` — replace `CountOwnedResourcesAsync` with `GetOwnershipSummaryAsync`/`GetOwnershipSummariesAsync`.
- `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlResourceContextRepository.cs` — implement the two new methods with `GROUP BY` queries.
- `src/Apperture.Core/Apperture.Core.Application/ResourceContexts/ResourceContextService.cs` — `ArchiveAsync` uses the summary instead of the raw count.
- `tests/Apperture.UnitTests/ResourceContexts/InMemoryResourceContextRepository.cs` — implement the new repository members.
- `tests/Apperture.UnitTests/ResourceContexts/ResourceContextServiceTests.cs` — update `Archive_IsRejectedWhileTheContextOwnsResources` to the refined rule and message.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationManagementService.cs` — `UpdateIntegrationAsync` gains the reactivation guard.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/SqlScheduleManagementService.cs` — `EnableAsync` gains the reactivation guard; `ResolveOwningResourceContextAsync`'s inherit-from-Integration path is closed against creating a live schedule under an archived context.
- `tests/Apperture.UnitTests/ResourceContexts/ResourceContextOwnershipTestHarness.cs` — extended if needed to expose the new summary methods for the guard tests (read before editing; it already wires real services over EF Core InMemory).

**Interfaces produced for later plans:**
- `ResourceContextOwnershipSummary` (record, with `BlocksArchive` computed property) — consumed by Plan 2 (`ResourceContextManagementService`, controller's 409 body) and Plan 3 (Applications list/detail counts).
- `IResourceContextRepository.GetOwnershipSummaryAsync(ResourceContextId, CancellationToken) : ValueTask<ResourceContextOwnershipSummary>` and `GetOwnershipSummariesAsync(CancellationToken) : ValueTask<IReadOnlyDictionary<string, ResourceContextOwnershipSummary>>` — consumed by Plan 2's controller (`GET summary`) and Plan 3's Applications list page.
- `ResourceContextViewScope` (readonly record struct, `Apperture.Management.Ui.Shared` namespace) — consumed by every later UI plan (3, 4, 5, 6).
- `ResourceContextViewRoutes.Resolve(string path) : ResourceContextViewMode` (new enum `ResourceContextViewMode { Scoped, Partial, Detail, Unscoped }`) — consumed by Plan 3's selector component.
- Client `IntegrationDto.ResourceContextId`/`.ResourceContext`, `ConnectionDto.ResourceContextId`/`.ResourceContext`, `RunSummaryDto.ResourceContextId`, `RunQueryFilter.ResourceContextId`, `CreateIntegrationRequest.ResourceContextId`, `CreateConnectionRequest.ResourceContextId`, `IntegrationFleetRowDto.ResourceContextId` — consumed by Plan 4 (scoped lists) and Plan 5 (creation flows).

---

## Task 1: Reserve the `all` slug (A11)

**Files:**
- Modify: `src/Apperture.Core/Apperture.Core.Domain/ResourceContexts/ResourceContextSlug.cs:26-27`
- Test: `tests/Apperture.UnitTests/ResourceContexts/ResourceContextSlugTests.cs:65-77`

**Interfaces:**
- Produces: `ResourceContextSlug.ReservedSlugs` now contains `"all"`; `ResourceContextSlug.IsReserved("all")` returns `true`.

- [ ] **Step 1: Update the failing tests first**

Edit `ResourceContextSlugTests.cs`. The existing test at lines 65-67:
```csharp
[Fact]
public void ReservedSlugs_AreExactlySharedAndSystem()
{
    ResourceContextSlug.ReservedSlugs.Should().BeEquivalentTo(["shared", "system"]);
}
```
becomes:
```csharp
[Fact]
public void ReservedSlugs_AreExactlySharedSystemAndAll()
{
    ResourceContextSlug.ReservedSlugs.Should().BeEquivalentTo(["shared", "system", "all"]);
}
```
And in the `[Theory]` at lines 69-77 (`IsReserved_MatchesTheReservedSet`), add a new `[InlineData("all", true)]` row alongside the existing `"shared"`/`"system"` rows.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceContextSlugTests"`
Expected: FAIL — `ReservedSlugs_AreExactlySharedAndSystem` no longer exists under that name (compile error until Step 1 is fully applied) or `ReservedSlugs_AreExactlySharedSystemAndAll` fails because `"all"` is not yet in the set.

- [ ] **Step 3: Add `"all"` to the reserved set**

In `ResourceContextSlug.cs`, change lines 26-27 from:
```csharp
public static IReadOnlySet<string> ReservedSlugs { get; } =
    new HashSet<string>(StringComparer.Ordinal) { "shared", "system" };
```
to:
```csharp
public static IReadOnlySet<string> ReservedSlugs { get; } =
    new HashSet<string>(StringComparer.Ordinal) { "shared", "system", "all" };
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceContextSlugTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Apperture.Core/Apperture.Core.Domain/ResourceContexts/ResourceContextSlug.cs tests/Apperture.UnitTests/ResourceContexts/ResourceContextSlugTests.cs
git commit -m "CTX-3A: reserve 'all' as a ResourceContext slug (A11)"
```

---

## Task 2: Client contract parity — Integration and Connection DTOs (A1, part 1)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs:1115-1130` (`IntegrationDto`, `CreateIntegrationRequest`), `:1187-1210` (`ConnectionDto`, `CreateConnectionRequest`)

**Interfaces:**
- Consumes: the already-existing `ResourceContextRef` client type (already used by `ScheduleDtos.cs:104` — a record with at least `Id`, `Name`, `Slug`, mirroring the server's `ResourceContextRef`). Do not redeclare it; add a `using` if it isn't already visible in this file's namespace.
- Produces: `IntegrationDto.ResourceContextId : string?`, `IntegrationDto.ResourceContext : ResourceContextRef?`, `CreateIntegrationRequest.ResourceContextId : ResourceContextId?` — wait, the request type takes a **string** on the wire; server's `CreateIntegrationRequestBody.ResourceContextId` is `string?` (JSON body), so the client twin must also be `string?`, not the domain `ResourceContextId` value type. Same for `ConnectionDto`/`CreateConnectionRequest`.

- [ ] **Step 1: Write the failing parity expectation as a plain assertion test (temporary, will be superseded by Task 5's general test)**

Add a scratch test to confirm today's gap exists, in a new throwaway test method inside `tests/Apperture.UnitTests/ResourceContexts/ClientServerDtoParityTests.cs` is created in Task 5 — for this task, skip a standalone test and instead prove the change via compilation: write the DTO changes first, then verify by building. This task is additive-only to non-authorized public API surface, so there is no meaningful "red" test to write before the field exists (record property addition is not independently behavior-testable in isolation). Proceed directly to Step 2, and rely on Task 5's parity test as the regression guard for this task.

- [ ] **Step 2: Add the fields to `IntegrationDto` and `CreateIntegrationRequest`**

In `IManagementClient.cs`, change (lines 1115-1123):
```csharp
public sealed record IntegrationDto(
    string Id,
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    string State,
    IReadOnlyDictionary<string, string> Settings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
```
to:
```csharp
public sealed record IntegrationDto(
    string Id,
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    string State,
    IReadOnlyDictionary<string, string> Settings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ResourceContextId = null,
    ResourceContextRef? ResourceContext = null);
```
And (lines 1126-1130):
```csharp
public sealed record CreateIntegrationRequest(
    string DisplayName,
    string ModuleId,
    string? ScopeKey = null,
    IReadOnlyDictionary<string, string>? Settings = null);
```
to:
```csharp
public sealed record CreateIntegrationRequest(
    string DisplayName,
    string ModuleId,
    string? ScopeKey = null,
    IReadOnlyDictionary<string, string>? Settings = null,
    string? ResourceContextId = null);
```

- [ ] **Step 3: Add the fields to `ConnectionDto` and `CreateConnectionRequest`**

Change `ConnectionDto` (lines 1187-1200) by appending, after the existing trailing `UsedIn` parameter:
```csharp
    string? ResourceContextId = null,
    ResourceContextRef? ResourceContext = null);
```
Change `CreateConnectionRequest` (lines 1203-1210) by appending, after the existing trailing `SecretValues` parameter:
```csharp
    string? ResourceContextId = null);
```

- [ ] **Step 4: Build to verify no call-site breakage**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Client/Apperture.Management.Client.csproj`
Expected: SUCCEEDS. All new parameters are appended with defaults, and both records use positional-or-named construction at every call site found in Tasks' research (`IntegrationNew.razor` uses the 3-arg positional form which remains valid since the new parameters are optional and trailing).

- [ ] **Step 5: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs
git commit -m "CTX-3A: add ResourceContextId/ResourceContext to client Integration and Connection DTOs (A1)"
```

---

## Task 3: Client contract parity — Run DTOs and fleet DTOs (A1, part 2)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs:1248-1270` (`RunSummaryDto`, `RunQueryFilter`)
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/IntegrationFleetDtos.cs:4-14, 57-71` (`IntegrationFleetQueryParams`, `IntegrationFleetRowDto`)

**Interfaces:**
- Produces: `RunSummaryDto.ResourceContextId : string?`, `RunQueryFilter.ResourceContextId : string?`, `IntegrationFleetQueryParams.Context : string?`, `IntegrationFleetRowDto.ResourceContextId : string?`.
- Note: this task adds the **client-side field only**. The server-side fleet endpoint does not yet accept a `contextId` query parameter (fact F2) — that wiring is Plan 4, Task "Scoped Integrations list" (design gate A3, §13 step 7.1). Adding `IntegrationFleetQueryParams.Context` here is inert until Plan 4 threads it into the query string builder; this is intentional sequencing per the design gate's own step ordering (A1 before A3).

- [ ] **Step 1: Add `ResourceContextId` to `RunSummaryDto` and `RunQueryFilter`**

In `IManagementClient.cs`, `RunSummaryDto` (lines 1248-1259): append a trailing optional parameter:
```csharp
    string? ResourceContextId = null);
```
`RunQueryFilter` (lines 1261-1270): append a trailing optional parameter:
```csharp
    string? ResourceContextId = null);
```

- [ ] **Step 2: Add `Context` to `IntegrationFleetQueryParams` and `ResourceContextId` to `IntegrationFleetRowDto`**

In `IntegrationFleetDtos.cs`, `IntegrationFleetQueryParams` (lines 4-14): append a trailing optional parameter, named `Context` (matching the URL query parameter name the design gate uses, `context`, not `contextId`, since this params object is consumed directly by page-level view-scope wiring in Plan 4 — confirm against Plan 4 before wiring; for this task, just add the field):
```csharp
    string? Context = null);
```
`IntegrationFleetRowDto` (lines 57-71): append a trailing optional parameter:
```csharp
    string? ResourceContextId = null);
```

- [ ] **Step 3: Build to verify no breakage**

Run: `dotnet build src/Apperture.ControlPlane/Apperture.Management.Client/Apperture.Management.Client.csproj`
Expected: SUCCEEDS.

- [ ] **Step 4: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs src/Apperture.ControlPlane/Apperture.Management.Client/IntegrationFleetDtos.cs
git commit -m "CTX-3A: add ResourceContextId to client Run and fleet DTOs (A1)"
```

---

## Task 4: `contextId` overloads on `GetIntegrationsAsync`/`GetConnectionsAsync`/`GetSchedulesAsync` (A1, part 3)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs:175` (`GetConnectionsAsync`), `:215` (`GetIntegrationsAsync`), `:420` (`GetSchedulesAsync`)
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs:558-562` (`GetConnectionsAsync` impl), `:628` (`GetIntegrationsAsync` impl), `:1147-1151` (`GetSchedulesAsync` impl)
- Test: `tests/Apperture.UnitTests/ResourceContexts/ManagementClientContextFilterTests.cs` (new file)

**Interfaces:**
- Consumes: server endpoints already accept `[FromQuery] string? contextId` on all three list actions (`IntegrationsController.List`, `ConnectionsController`'s list action, `SchedulesController.List`) — verified in research; no server change needed here.
- Produces: `Task<IReadOnlyList<IntegrationDto>> GetIntegrationsAsync(string? contextId = null, CancellationToken cancellationToken = default)` (and the Connection/Schedule equivalents) — consumed by Plan 4's scoped-list pages.

- [ ] **Step 1: Write the failing test**

This client uses `HttpClient` under the hood; test the query-string construction using a `DelegatingHandler` capture, following whatever HTTP-mocking pattern the test project already uses for `ManagementClient` (check `tests/Apperture.UnitTests` for an existing `ManagementClient`-targeting test file as the harness template — if none exists, use a minimal `HttpMessageHandler` stub). Write:
```csharp
[Fact]
public async Task GetIntegrationsAsync_WithContextId_AppendsContextIdQueryParam()
{
    string? capturedUri = null;
    var handler = new StubHttpMessageHandler(req =>
    {
        capturedUri = req.RequestUri!.ToString();
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        };
    });
    var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") });

    await client.GetIntegrationsAsync(contextId: "canvas-id");

    capturedUri.Should().Contain("contextId=canvas-id");
}
```
Repeat the same shape for `GetConnectionsAsync` and `GetSchedulesAsync`, plus one test per method proving that omitting `contextId` sends no `contextId` query parameter at all (so the default-omitted behavior is unchanged, matching the server's documented "Omitted (the default) returns the same global set this endpoint has always returned").

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ManagementClientContextFilterTests"`
Expected: FAIL — compile error, since `GetIntegrationsAsync` takes no `contextId` parameter yet.

- [ ] **Step 3: Add the `contextId` parameter to the interface**

In `IManagementClient.cs`:
```csharp
// line 175
Task<IReadOnlyList<ConnectionDto>> GetConnectionsAsync(string? contextId = null, CancellationToken cancellationToken = default);
// line 215
Task<IReadOnlyList<IntegrationDto>> GetIntegrationsAsync(string? contextId = null, CancellationToken cancellationToken = default);
// line 420
Task<IReadOnlyList<ScheduleDto>> GetSchedulesAsync(string? contextId = null, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement query-string construction in `ManagementClient.cs`**

Change (lines 558-562):
```csharp
public async Task<IReadOnlyList<ConnectionDto>> GetConnectionsAsync(CancellationToken cancellationToken = default)
{
    var result = await _http.GetFromJsonAsync<ConnectionDto[]>("connections", cancellationToken).ConfigureAwait(false);
    return result ?? [];
}
```
to:
```csharp
public async Task<IReadOnlyList<ConnectionDto>> GetConnectionsAsync(string? contextId = null, CancellationToken cancellationToken = default)
{
    var path = string.IsNullOrWhiteSpace(contextId) ? "connections" : $"connections?contextId={Uri.EscapeDataString(contextId)}";
    var result = await _http.GetFromJsonAsync<ConnectionDto[]>(path, cancellationToken).ConfigureAwait(false);
    return result ?? [];
}
```
Apply the identical shape to `GetIntegrationsAsync` (line 628, path `"integrations"`) and `GetSchedulesAsync` (lines 1147-1151, path `"schedules"`).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ManagementClientContextFilterTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs tests/Apperture.UnitTests/ResourceContexts/ManagementClientContextFilterTests.cs
git commit -m "CTX-3A: add contextId overloads to GetIntegrationsAsync/GetConnectionsAsync/GetSchedulesAsync (A1)"
```

---

## Task 5: Reflection-based client/server DTO parity test (A2)

**Files:**
- Create: `tests/Apperture.UnitTests/ResourceContexts/ClientServerDtoParityTests.cs`

**Interfaces:**
- Consumes: server DTOs `Apperture.Management.Api.Controllers.IntegrationsController.IntegrationDto` (nested type at `IntegrationsController.cs:726-736`), `.CreateIntegrationRequestBody` (`:742-747`), `Apperture.Management.Api.Controllers.ConnectionsController.ConnectionDto` (`:253-268`), `.CreateConnectionRequestBody` (`:275-283`), `Apperture.ControlPlane.Security.IRunQueryService.RunSummaryDto`/`RunQueryFilter` (`IRunQueryService.cs:58-83`), `Apperture.Core.Application.Integration.IntegrationFleetRowProjection` (`IntegrationFleetModels.cs:97-111`); client twins from Tasks 2-4.
- Produces: a regression guard so this drift (fact F1, R14) cannot silently recur — CI fails the moment a server DTO gains a property the client twin lacks.

- [ ] **Step 1: Write the parity test**

```csharp
using System.Reflection;
using Apperture.ControlPlane.Security;
using FluentAssertions;
using Xunit;
using ClientIntegrationDto = Apperture.Management.Client.IntegrationDto;
using ClientCreateIntegrationRequest = Apperture.Management.Client.CreateIntegrationRequest;
using ClientConnectionDto = Apperture.Management.Client.ConnectionDto;
using ClientCreateConnectionRequest = Apperture.Management.Client.CreateConnectionRequest;
using ClientRunSummaryDto = Apperture.Management.Client.RunSummaryDto;
using ClientRunQueryFilter = Apperture.Management.Client.RunQueryFilter;
using ClientIntegrationFleetRowDto = Apperture.Management.Client.IntegrationFleetRowDto;
using ServerIntegrationDto = Apperture.Management.Api.Controllers.IntegrationsController.IntegrationDto;
using ServerCreateIntegrationRequestBody = Apperture.Management.Api.Controllers.IntegrationsController.CreateIntegrationRequestBody;
using ServerConnectionDto = Apperture.Management.Api.Controllers.ConnectionsController.ConnectionDto;
using ServerCreateConnectionRequestBody = Apperture.Management.Api.Controllers.ConnectionsController.CreateConnectionRequestBody;
using ServerRunSummaryDto = Apperture.ControlPlane.Security.IRunQueryService.RunSummaryDto;
using ServerRunQueryFilter = Apperture.ControlPlane.Security.IRunQueryService.RunQueryFilter;
using ServerIntegrationFleetRowProjection = Apperture.Core.Application.Integration.IntegrationFleetRowProjection;

namespace Apperture.UnitTests.ResourceContexts;

public class ClientServerDtoParityTests
{
    public static IEnumerable<object[]> DtoPairs()
    {
        yield return new object[] { typeof(ServerIntegrationDto), typeof(ClientIntegrationDto) };
        yield return new object[] { typeof(ServerCreateIntegrationRequestBody), typeof(ClientCreateIntegrationRequest) };
        yield return new object[] { typeof(ServerConnectionDto), typeof(ClientConnectionDto) };
        yield return new object[] { typeof(ServerCreateConnectionRequestBody), typeof(ClientCreateConnectionRequest) };
        yield return new object[] { typeof(ServerRunSummaryDto), typeof(ClientRunSummaryDto) };
        yield return new object[] { typeof(ServerRunQueryFilter), typeof(ClientRunQueryFilter) };
        yield return new object[] { typeof(ServerIntegrationFleetRowProjection), typeof(ClientIntegrationFleetRowDto) };
    }

    [Theory]
    [MemberData(nameof(DtoPairs))]
    public void ClientDtoHasEveryServerProperty(Type serverType, Type clientType)
    {
        var serverProps = serverType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        var clientProps = clientType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = serverProps.Except(clientProps).ToList();

        missing.Should().BeEmpty(
            $"the client type {clientType.FullName} is missing properties that {serverType.FullName} already exposes: {string.Join(", ", missing)}");
    }
}
```

Adjust the `using` aliases if the actual nested-type accessibility of `IntegrationsController.IntegrationDto`/`CreateIntegrationRequestBody`/etc. is `private`/`internal` rather than `public` — verify accessibility while implementing this task (`grep -n "record IntegrationDto\|record CreateIntegrationRequestBody" src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationsController.cs`); if any are non-public, either make them `public` (preferred, since a reflection test across assemblies requires public visibility, and these are already serialized over the wire so there is no encapsulation to protect) or use `[InternalsVisibleTo]` from `Apperture.Management.Api` to `Apperture.UnitTests` (check `Apperture.Management.Api.csproj`/`AssemblyInfo` for an existing `InternalsVisibleTo` entry to `Apperture.UnitTests` first, and reuse it if present).

- [ ] **Step 2: Run the test to verify it passes immediately**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ClientServerDtoParityTests"`
Expected: PASS, because Tasks 2-4 already closed every gap this test checks. If any case fails, it means a Task 2-4 field was missed or misnamed — fix the client DTO in the earlier task's file, not this test.

- [ ] **Step 3: Prove the test is not vacuous by temporarily reverting one field**

Temporarily comment out `ResourceContextId` on the client `IntegrationDto` (from Task 2), rerun the filtered test, confirm it fails with a message naming `ResourceContextId`, then restore the field. This is a manual verification step, not a permanent test — do not commit the temporary revert.

- [ ] **Step 4: Commit**

```bash
git add tests/Apperture.UnitTests/ResourceContexts/ClientServerDtoParityTests.cs
git commit -m "CTX-3A: add reflection-based client/server DTO parity test (A2, R14)"
```

---

## Task 6: `ResourceContextViewScope` value type (§2.2)

**Files:**
- Create: `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ResourceContextViewScope.cs`
- Test: `tests/Apperture.Management.Ui.Shared.Tests/ResourceContextViewScopeTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  namespace Apperture.Management.Ui.Shared;

  public readonly record struct ResourceContextViewScope
  {
      public static ResourceContextViewScope All { get; }
      public static ResourceContextViewScope Of(string resourceContextId);

      public bool IsAll { get; }
      public string? ResourceContextId { get; }

      public const string AllToken = "all";

      public static ResourceContextViewScope? Parse(string? queryValue);
      public string ToQueryValue();
      public string? ToApiFilter();
  }
  ```
  `Parse` returns `null` (not a scope value) when `queryValue` is `null`/empty/whitespace — this is the "Unspecified" case the design gate's §4.2 resolution algorithm branches on; callers must treat a `null` return as "consult tab state / localStorage," never as All.

- [ ] **Step 1: Write the failing tests**

```csharp
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public class ResourceContextViewScopeTests
{
    [Fact]
    public void All_IsAll_AndHasNoResourceContextId()
    {
        var scope = ResourceContextViewScope.All;
        scope.IsAll.Should().BeTrue();
        scope.ResourceContextId.Should().BeNull();
    }

    [Fact]
    public void All_ToApiFilter_ReturnsNull()
    {
        ResourceContextViewScope.All.ToApiFilter().Should().BeNull();
    }

    [Fact]
    public void All_ToQueryValue_ReturnsAllToken()
    {
        ResourceContextViewScope.All.ToQueryValue().Should().Be("all");
    }

    [Fact]
    public void Of_WithAnId_IsNotAll_AndReturnsThatIdFromApiFilterAndQueryValue()
    {
        var scope = ResourceContextViewScope.Of("canvas-id");
        scope.IsAll.Should().BeFalse();
        scope.ResourceContextId.Should().Be("canvas-id");
        scope.ToApiFilter().Should().Be("canvas-id");
        scope.ToQueryValue().Should().Be("canvas-id");
    }

    [Fact]
    public void Of_WithShared_BehavesAsAnOrdinaryId()
    {
        var scope = ResourceContextViewScope.Of("shared");
        scope.IsAll.Should().BeFalse();
        scope.ResourceContextId.Should().Be("shared");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyOrWhitespace_ReturnsNullMeaningUnspecified(string? value)
    {
        ResourceContextViewScope.Parse(value).Should().BeNull();
    }

    [Fact]
    public void Parse_AllToken_ReturnsAll()
    {
        var scope = ResourceContextViewScope.Parse("all");
        scope.Should().NotBeNull();
        scope!.Value.IsAll.Should().BeTrue();
    }

    [Fact]
    public void Parse_AnId_ReturnsOfThatId()
    {
        var scope = ResourceContextViewScope.Parse("canvas-id");
        scope.Should().NotBeNull();
        scope!.Value.ResourceContextId.Should().Be("canvas-id");
    }

    [Fact]
    public void Of_NullOrWhitespace_Throws()
    {
        var act = () => ResourceContextViewScope.Of("  ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void All_EqualsAll_AndOfSameId_EqualsEachOther()
    {
        (ResourceContextViewScope.All == ResourceContextViewScope.All).Should().BeTrue();
        (ResourceContextViewScope.Of("x") == ResourceContextViewScope.Of("x")).Should().BeTrue();
        (ResourceContextViewScope.Of("x") == ResourceContextViewScope.Of("y")).Should().BeFalse();
        (ResourceContextViewScope.All == ResourceContextViewScope.Of("all")).Should().BeFalse();
    }
}
```
Note the last assertion: `Of("all")` must NOT equal `All`, because `Of` never special-cases the token — only `Parse` interprets the literal string `"all"` as the All sentinel. `Of` is meant to be called with a real ResourceContext id, and design gate §2.2 guarantees "generated ids are 24 hex characters and Shared's id is `shared`, so no id can equal `all`" — so `Of("all")` is a caller error in practice, but the type itself must not silently coerce it, since that coercion is `Parse`'s job alone. Assert this distinction explicitly so a future refactor cannot merge the two code paths.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj --filter "FullyQualifiedName~ResourceContextViewScopeTests"`
Expected: FAIL — compile error, type doesn't exist yet.

- [ ] **Step 3: Implement `ResourceContextViewScope`**

```csharp
namespace Apperture.Management.Ui.Shared;

public readonly record struct ResourceContextViewScope
{
    public const string AllToken = "all";

    private readonly string? _resourceContextId;

    private ResourceContextViewScope(bool isAll, string? resourceContextId)
    {
        IsAll = isAll;
        _resourceContextId = resourceContextId;
    }

    public bool IsAll { get; }

    public string? ResourceContextId => IsAll ? null : _resourceContextId;

    public static ResourceContextViewScope All { get; } = new(isAll: true, resourceContextId: null);

    public static ResourceContextViewScope Of(string resourceContextId)
    {
        if (string.IsNullOrWhiteSpace(resourceContextId))
            throw new ArgumentException("A specific view scope requires a non-empty ResourceContext id.", nameof(resourceContextId));

        return new ResourceContextViewScope(isAll: false, resourceContextId: resourceContextId);
    }

    public static ResourceContextViewScope? Parse(string? queryValue)
    {
        if (string.IsNullOrWhiteSpace(queryValue))
            return null;

        return string.Equals(queryValue, AllToken, StringComparison.Ordinal)
            ? All
            : Of(queryValue);
    }

    public string ToQueryValue() => IsAll ? AllToken : _resourceContextId!;

    public string? ToApiFilter() => IsAll ? null : _resourceContextId;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj --filter "FullyQualifiedName~ResourceContextViewScopeTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ResourceContextViewScope.cs tests/Apperture.Management.Ui.Shared.Tests/ResourceContextViewScopeTests.cs
git commit -m "CTX-3A: add ResourceContextViewScope value type (design gate §2.2)"
```

---

## Task 7: `ResourceContextViewRoutes` route-mode table (§3.4)

**Files:**
- Create: `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ResourceContextViewRoutes.cs`
- Test: `tests/Apperture.Management.Ui.Shared.Tests/ResourceContextViewRoutesTests.cs`

**Interfaces:**
- Consumes: `NavPathMatching.IsPathUnder(string path, string basePath) : bool` (`src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/NavPathMatching.cs`, existing, segment-boundary-aware prefix match).
- Produces:
  ```csharp
  namespace Apperture.Management.Ui.Shared;

  public enum ResourceContextViewMode
  {
      Scoped,
      Partial,
      Detail,
      Unscoped
  }

  public static class ResourceContextViewRoutes
  {
      public static ResourceContextViewMode Resolve(string path);
  }
  ```
  Consumed by Plan 3's selector component (which must render active/muted/labeled per mode) and by Plan 4/6's page-level wiring decisions.

- [ ] **Step 1: Write the failing tests**

```csharp
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public class ResourceContextViewRoutesTests
{
    [Theory]
    [InlineData("/integrations")]
    [InlineData("/connections")]
    [InlineData("/schedules")]
    [InlineData("/runs")]
    public void ScopedListRoutes_ResolveToScoped(string path)
    {
        ResourceContextViewRoutes.Resolve(path).Should().Be(ResourceContextViewMode.Scoped);
    }

    [Theory]
    [InlineData("/control/center")]
    public void ControlCenter_ResolvesToPartial(string path)
    {
        ResourceContextViewRoutes.Resolve(path).Should().Be(ResourceContextViewMode.Partial);
    }

    [Theory]
    [InlineData("/integrations/abc123")]
    [InlineData("/integrations/abc123/connections")]
    [InlineData("/integrations/abc123/schedules")]
    [InlineData("/integrations/abc123/runs")]
    [InlineData("/integrations/abc123/settings")]
    [InlineData("/integrations/abc123/notifications")]
    [InlineData("/integrations/abc123/schedule-diagnostics")]
    [InlineData("/connections/conn1")]
    [InlineData("/schedules/sched1")]
    [InlineData("/runs/run1")]
    public void DetailAndHubRoutes_ResolveToDetail(string path)
    {
        ResourceContextViewRoutes.Resolve(path).Should().Be(ResourceContextViewMode.Detail);
    }

    [Theory]
    [InlineData("/business-data")]
    [InlineData("/business-data/customers")]
    [InlineData("/config/inventory")]
    [InlineData("/config/applications")]
    [InlineData("/config/applications/new")]
    [InlineData("/config/applications/abc123")]
    [InlineData("/config/governance")]
    [InlineData("/control/audit")]
    [InlineData("/execute")]
    [InlineData("/me/notifications")]
    [InlineData("/account")]
    [InlineData("/schedules/new")]
    [InlineData("/integrations/new")]
    [InlineData("/connections/new")]
    [InlineData("/")]
    public void EverythingElse_ResolvesToUnscoped(string path)
    {
        ResourceContextViewRoutes.Resolve(path).Should().Be(ResourceContextViewMode.Unscoped);
    }

    [Fact]
    public void TrailingSlashAndQueryString_AreIgnored()
    {
        ResourceContextViewRoutes.Resolve("/integrations/").Should().Be(ResourceContextViewMode.Scoped);
        ResourceContextViewRoutes.Resolve("/integrations?context=all").Should().Be(ResourceContextViewMode.Scoped);
    }
}
```
Note `/integrations/new`, `/connections/new`, `/schedules/new` resolve `Unscoped` here (they are not list/detail routes at all — they are create routes, handled by Plan 5's preselection logic directly from `ResourceContextViewScope`/tab state, not through the route-mode table). Confirm this against Plan 5 before finalizing; if Plan 5 instead expects `Create` participation through this same table, add a fifth mode there rather than retrofitting it here silently.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj --filter "FullyQualifiedName~ResourceContextViewRoutesTests"`
Expected: FAIL — type doesn't exist.

- [ ] **Step 3: Implement `ResourceContextViewRoutes`**

```csharp
namespace Apperture.Management.Ui.Shared;

public enum ResourceContextViewMode
{
    Scoped,
    Partial,
    Detail,
    Unscoped
}

public static class ResourceContextViewRoutes
{
    private static readonly string[] ScopedListPaths = ["/integrations", "/connections", "/schedules", "/runs"];
    private static readonly string[] PartialPaths = ["/control/center"];

    public static ResourceContextViewMode Resolve(string path)
    {
        var normalized = Normalize(path);

        foreach (var scoped in ScopedListPaths)
        {
            if (string.Equals(normalized, scoped, StringComparison.OrdinalIgnoreCase))
                return ResourceContextViewMode.Scoped;
        }

        foreach (var partial in PartialPaths)
        {
            if (string.Equals(normalized, partial, StringComparison.OrdinalIgnoreCase))
                return ResourceContextViewMode.Partial;
        }

        if (IsDetailRoute(normalized))
            return ResourceContextViewMode.Detail;

        return ResourceContextViewMode.Unscoped;
    }

    private static bool IsDetailRoute(string normalized)
    {
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return false;

        var root = "/" + segments[0];
        var isKnownFamily = root is "/integrations" or "/connections" or "/schedules" or "/runs";
        if (!isKnownFamily)
            return false;

        // A second segment that is not a known static route ("new") is treated as an id.
        return !string.Equals(segments[1], "new", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        var withoutQuery = path.Split('?', 2)[0].Split('#', 2)[0];
        var trimmed = withoutQuery.TrimEnd('/');
        return string.IsNullOrEmpty(trimmed) ? "/" : trimmed;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj --filter "FullyQualifiedName~ResourceContextViewRoutesTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ResourceContextViewRoutes.cs tests/Apperture.Management.Ui.Shared.Tests/ResourceContextViewRoutesTests.cs
git commit -m "CTX-3A: add ResourceContextViewRoutes route-mode table (design gate §3.4)"
```

---

## Task 8: `ResourceContextOwnershipSummary` and repository summary methods (A5, part 1)

**Files:**
- Create: `src/Apperture.Core/Apperture.Core.Application/Services/ResourceContextOwnershipSummary.cs`
- Modify: `src/Apperture.Core/Apperture.Core.Application/Services/IResourceContextRepository.cs:34` (replace `CountOwnedResourcesAsync`)
- Modify: `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlResourceContextRepository.cs:71-78`
- Modify: `tests/Apperture.UnitTests/ResourceContexts/InMemoryResourceContextRepository.cs`
- Test: `tests/Apperture.UnitTests/ResourceContexts/ResourceContextOwnershipSummaryTests.cs` (new, exercises the SQL-backed repository over EF Core InMemory via `ResourceContextOwnershipTestHarness`)

**Interfaces:**
- Produces:
  ```csharp
  namespace Apperture.Core.Application.Services;

  public sealed record ResourceContextOwnershipSummary(
      int LiveIntegrations, int RetiredIntegrations,
      int ActiveConnections, int RetiredConnections,
      int EnabledSchedules, int DisabledSchedules)
  {
      public bool BlocksArchive => LiveIntegrations + ActiveConnections + EnabledSchedules > 0;
  }
  ```
  `IResourceContextRepository.GetOwnershipSummaryAsync(ResourceContextId id, CancellationToken ct) : ValueTask<ResourceContextOwnershipSummary>` and `GetOwnershipSummariesAsync(CancellationToken ct) : ValueTask<IReadOnlyDictionary<string, ResourceContextOwnershipSummary>>` (keyed by `ResourceContextId.Value`) — consumed by Task 9 (`ResourceContextService.ArchiveAsync`), Plan 2's controller `GET summary`, and Plan 3's Applications list page.

- [ ] **Step 1: Write the failing test against the real SQL repository over EF Core InMemory**

Use `ResourceContextOwnershipTestHarness` (`tests/Apperture.UnitTests/ResourceContexts/ResourceContextOwnershipTestHarness.cs`) — it already wires `SqlResourceContextRepository` over a real `AppertureDbContext` on the EF Core InMemory provider with real `IntegrationManagementService`/`ConnectionManagementService`/`SqlScheduleManagementService`. Read that file in full before writing this test, then add:

```csharp
[Fact]
public async Task GetOwnershipSummaryAsync_CountsLiveAndRetiredPerFamilySeparately()
{
    await using var harness = await ResourceContextOwnershipTestHarness.CreateAsync();
    var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");

    var liveIntegration = await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Active);
    var retiredIntegration = await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Retired);
    var activeConnection = await harness.CreateConnectionAsync(context.Id, state: ConnectionState.Active);
    var retiredConnection = await harness.CreateConnectionAsync(context.Id, state: ConnectionState.Retired);
    var enabledSchedule = await harness.CreateStandaloneScheduleAsync(context.Id, enabled: true);
    var disabledSchedule = await harness.CreateStandaloneScheduleAsync(context.Id, enabled: false);

    var summary = await harness.Repository.GetOwnershipSummaryAsync(context.Id, CancellationToken.None);

    summary.LiveIntegrations.Should().Be(1);
    summary.RetiredIntegrations.Should().Be(1);
    summary.ActiveConnections.Should().Be(1);
    summary.RetiredConnections.Should().Be(1);
    summary.EnabledSchedules.Should().Be(1);
    summary.DisabledSchedules.Should().Be(1);
    summary.BlocksArchive.Should().BeTrue();
}

[Fact]
public async Task GetOwnershipSummaryAsync_WithOnlyRetiredAndDisabled_DoesNotBlockArchive()
{
    await using var harness = await ResourceContextOwnershipTestHarness.CreateAsync();
    var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
    await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Retired);
    await harness.CreateConnectionAsync(context.Id, state: ConnectionState.Retired);
    await harness.CreateStandaloneScheduleAsync(context.Id, enabled: false);

    var summary = await harness.Repository.GetOwnershipSummaryAsync(context.Id, CancellationToken.None);

    summary.BlocksArchive.Should().BeFalse();
}

[Fact]
public async Task GetOwnershipSummariesAsync_RunsAConstantNumberOfQueriesRegardlessOfContextCount()
{
    await using var harness = await ResourceContextOwnershipTestHarness.CreateAsync();
    var contextA = await harness.ResourceContexts.CreateApplicationContextAsync("A");
    var contextB = await harness.ResourceContexts.CreateApplicationContextAsync("B");
    await harness.CreateIntegrationAsync(contextA.Id, state: IntegrationState.Active);
    await harness.CreateIntegrationAsync(contextB.Id, state: IntegrationState.Active);

    var queryCountBefore = harness.CapturedQueryCount;
    var summaries = await harness.Repository.GetOwnershipSummariesAsync(CancellationToken.None);
    var queriesIssued = harness.CapturedQueryCount - queryCountBefore;

    summaries[contextA.Id.Value].LiveIntegrations.Should().Be(1);
    summaries[contextB.Id.Value].LiveIntegrations.Should().Be(1);
    queriesIssued.Should().Be(3, "one GROUP BY per family (Integrations, Connections, ScheduleDefinitions), independent of context count");
}
```
The helper methods `CreateIntegrationAsync`, `CreateConnectionAsync`, `CreateStandaloneScheduleAsync`, and `CapturedQueryCount` may not exist yet on `ResourceContextOwnershipTestHarness` — read the file first; if they're missing, add them as part of this task using the same real-service pattern the harness already uses elsewhere (do not add a raw EF `Add`+`SaveChanges` shortcut that bypasses the domain services, since that would let the test pass without exercising the real state values). For `CapturedQueryCount`, either wrap the harness's `DbContext` with an EF Core interceptor that increments a counter per `ReaderExecuting` event, or drop the exact-count assertion to `queriesIssued.Should().BeLessOrEqualTo(3)` if an interceptor is impractical to add cleanly within this task — note the choice made in the commit message.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceContextOwnershipSummaryTests"`
Expected: FAIL — `GetOwnershipSummaryAsync` doesn't exist on the repository interface yet.

- [ ] **Step 3: Add the `ResourceContextOwnershipSummary` record**

Create `src/Apperture.Core/Apperture.Core.Application/Services/ResourceContextOwnershipSummary.cs`:
```csharp
namespace Apperture.Core.Application.Services;

/// <summary>Per-family live/retired-or-disabled counts for one ResourceContext, used to decide whether it can be archived (CTX-3A §6.5).</summary>
public sealed record ResourceContextOwnershipSummary(
    int LiveIntegrations, int RetiredIntegrations,
    int ActiveConnections, int RetiredConnections,
    int EnabledSchedules, int DisabledSchedules)
{
    public bool BlocksArchive => LiveIntegrations + ActiveConnections + EnabledSchedules > 0;
}
```

- [ ] **Step 4: Replace `CountOwnedResourcesAsync` in the repository interface**

In `IResourceContextRepository.cs`, replace line 34:
```csharp
ValueTask<int> CountOwnedResourcesAsync(ResourceContextId id, CancellationToken cancellationToken = default);
```
with:
```csharp
ValueTask<ResourceContextOwnershipSummary> GetOwnershipSummaryAsync(ResourceContextId id, CancellationToken cancellationToken = default);

ValueTask<IReadOnlyDictionary<string, ResourceContextOwnershipSummary>> GetOwnershipSummariesAsync(CancellationToken cancellationToken = default);
```
Update the doc comment above it (it currently explains the old naive-count semantics) to describe the live/retired split.

- [ ] **Step 5: Implement the two methods in `SqlResourceContextRepository`**

Replace `CountOwnedResourcesAsync` (lines 71-78) with:
```csharp
public async ValueTask<ResourceContextOwnershipSummary> GetOwnershipSummaryAsync(ResourceContextId id, CancellationToken cancellationToken = default)
{
    var all = await GetOwnershipSummariesAsync(cancellationToken).ConfigureAwait(false);
    return all.TryGetValue(id.Value, out var summary)
        ? summary
        : new ResourceContextOwnershipSummary(0, 0, 0, 0, 0, 0);
}

public async ValueTask<IReadOnlyDictionary<string, ResourceContextOwnershipSummary>> GetOwnershipSummariesAsync(CancellationToken cancellationToken = default)
{
    var integrationCounts = await _db.Integrations
        .GroupBy(e => new { e.ResourceContextId, e.State })
        .Select(g => new { g.Key.ResourceContextId, g.Key.State, Count = g.Count() })
        .ToListAsync(cancellationToken).ConfigureAwait(false);

    var connectionCounts = await _db.Connections
        .GroupBy(e => new { e.ResourceContextId, e.State })
        .Select(g => new { g.Key.ResourceContextId, g.Key.State, Count = g.Count() })
        .ToListAsync(cancellationToken).ConfigureAwait(false);

    var scheduleCounts = await _db.ScheduleDefinitions
        .GroupBy(e => new { e.ResourceContextId, e.Enabled })
        .Select(g => new { g.Key.ResourceContextId, g.Key.Enabled, Count = g.Count() })
        .ToListAsync(cancellationToken).ConfigureAwait(false);

    var contextIds = integrationCounts.Select(x => x.ResourceContextId)
        .Concat(connectionCounts.Select(x => x.ResourceContextId))
        .Concat(scheduleCounts.Select(x => x.ResourceContextId))
        .Distinct(StringComparer.Ordinal);

    var result = new Dictionary<string, ResourceContextOwnershipSummary>(StringComparer.Ordinal);
    foreach (var contextId in contextIds)
    {
        int Count(IEnumerable<dynamic> rows, string idKey) => 0; // placeholder removed below
    }

    // Build per-context summaries without dynamic: aggregate into lookup dictionaries first.
    var liveIntegrationsByContext = integrationCounts.Where(x => x.State == (int)IntegrationState.Active)
        .ToDictionary(x => x.ResourceContextId, x => x.Count, StringComparer.Ordinal);
    var retiredIntegrationsByContext = integrationCounts.Where(x => x.State == (int)IntegrationState.Retired)
        .ToDictionary(x => x.ResourceContextId, x => x.Count, StringComparer.Ordinal);
    var activeConnectionsByContext = connectionCounts.Where(x => x.State == (int)ConnectionState.Active)
        .ToDictionary(x => x.ResourceContextId, x => x.Count, StringComparer.Ordinal);
    var retiredConnectionsByContext = connectionCounts.Where(x => x.State == (int)ConnectionState.Retired)
        .ToDictionary(x => x.ResourceContextId, x => x.Count, StringComparer.Ordinal);
    var enabledSchedulesByContext = scheduleCounts.Where(x => x.Enabled)
        .ToDictionary(x => x.ResourceContextId, x => x.Count, StringComparer.Ordinal);
    var disabledSchedulesByContext = scheduleCounts.Where(x => !x.Enabled)
        .ToDictionary(x => x.ResourceContextId, x => x.Count, StringComparer.Ordinal);

    foreach (var contextId in contextIds)
    {
        result[contextId] = new ResourceContextOwnershipSummary(
            LiveIntegrations: liveIntegrationsByContext.GetValueOrDefault(contextId),
            RetiredIntegrations: retiredIntegrationsByContext.GetValueOrDefault(contextId),
            ActiveConnections: activeConnectionsByContext.GetValueOrDefault(contextId),
            RetiredConnections: retiredConnectionsByContext.GetValueOrDefault(contextId),
            EnabledSchedules: enabledSchedulesByContext.GetValueOrDefault(contextId),
            DisabledSchedules: disabledSchedulesByContext.GetValueOrDefault(contextId));
    }

    return result;
}
```
Delete the stray `foreach` block with the unused local function `Count` in the draft above before committing — it was left in to show the wrong approach was considered and rejected; the final method body is the `liveIntegrationsByContext`-style dictionaries plus the single final `foreach`. Verify `IntegrationEntity.State`/`ConnectionEntity.State` are stored as `int` (per research: `State (int, maps IntegrationState: 0=Active,1=Retired,2=Inactive)`) so the `(int)IntegrationState.Active` casts compile and match.

- [ ] **Step 6: Update the in-memory test double**

In `tests/Apperture.UnitTests/ResourceContexts/InMemoryResourceContextRepository.cs`, remove the `OwnedResourceCounts` dictionary and `CountOwnedResourcesAsync` implementation; implement `GetOwnershipSummaryAsync`/`GetOwnershipSummariesAsync` backed by a new `Dictionary<string, ResourceContextOwnershipSummary> OwnershipSummaries` field that test authors populate directly (simpler than re-deriving from raw resource lists, since this double is used only by `ResourceContextServiceTests`, which tests `ResourceContextService` in isolation from the real resource-family services).

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceContextOwnershipSummaryTests"`
Expected: PASS. Also run the full `ResourceContexts` folder to catch any other compile breakage from the interface change:
Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceContexts"`
Expected: some pre-existing tests referencing `CountOwnedResourcesAsync`/`OwnedResourceCounts` will fail to compile — these are exactly the tests Task 10 fixes next. It is acceptable for this task's commit to leave `ResourceContextServiceTests.cs` broken if Task 9-10 immediately follow in the same session before any merge; if this plan is executed with a review gate after every task, do Task 9 and Task 10 as part of this same task's commit instead of leaving the build red. Prefer folding Steps 8-verification of Task 9/10 in before committing here, i.e. treat Tasks 8, 9, and 10 as one atomic commit if the executor is running gated task-by-task review.

- [ ] **Step 8: Commit**

```bash
git add src/Apperture.Core/Apperture.Core.Application/Services/ResourceContextOwnershipSummary.cs src/Apperture.Core/Apperture.Core.Application/Services/IResourceContextRepository.cs src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlResourceContextRepository.cs tests/Apperture.UnitTests/ResourceContexts/InMemoryResourceContextRepository.cs tests/Apperture.UnitTests/ResourceContexts/ResourceContextOwnershipSummaryTests.cs tests/Apperture.UnitTests/ResourceContexts/ResourceContextOwnershipTestHarness.cs
git commit -m "CTX-3A: replace CountOwnedResourcesAsync with per-family ownership summaries (A5, fact F4)"
```

---

## Task 9: Refine `ResourceContextService.ArchiveAsync` to the "nothing live" rule (§6.5, D2)

**Files:**
- Modify: `src/Apperture.Core/Apperture.Core.Application/ResourceContexts/ResourceContextService.cs:111-128`
- Modify: `tests/Apperture.UnitTests/ResourceContexts/ResourceContextServiceTests.cs:163-173`

**Interfaces:**
- Consumes: `IResourceContextRepository.GetOwnershipSummaryAsync` (Task 8).

- [ ] **Step 1: Update the failing test to the refined rule**

Replace `Archive_IsRejectedWhileTheContextOwnsResources` (lines 163-173):
```csharp
[Fact]
public async Task Archive_IsRejectedWhileTheContextOwnsResources()
{
    var (service, repository) = CreateService();
    var created = await service.CreateApplicationContextAsync("Canvas");
    repository.OwnedResourceCounts[created.Id.Value] = 3;

    var act = () => service.ArchiveAsync(created.Id);

    await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*owns 3 resource(s)*");
}
```
with:
```csharp
[Fact]
public async Task Archive_IsRejectedWhileTheContextOwnsLiveResources()
{
    var (service, repository) = CreateService();
    var created = await service.CreateApplicationContextAsync("Canvas");
    repository.OwnershipSummaries[created.Id.Value] = new ResourceContextOwnershipSummary(
        LiveIntegrations: 2, RetiredIntegrations: 0,
        ActiveConnections: 1, RetiredConnections: 0,
        EnabledSchedules: 3, DisabledSchedules: 0);

    var act = () => service.ArchiveAsync(created.Id);

    await act.Should().ThrowAsync<InvalidOperationException>()
        .WithMessage("*2 active integration*1 active connection*3 enabled schedule*");
}

[Fact]
public async Task Archive_SucceedsWhenOnlyRetiredOrDisabledResourcesRemain()
{
    var (service, repository) = CreateService();
    var created = await service.CreateApplicationContextAsync("Canvas");
    repository.OwnershipSummaries[created.Id.Value] = new ResourceContextOwnershipSummary(
        LiveIntegrations: 0, RetiredIntegrations: 4,
        ActiveConnections: 0, RetiredConnections: 2,
        EnabledSchedules: 0, DisabledSchedules: 1);

    var archived = await service.ArchiveAsync(created.Id);

    archived.State.Should().Be(ResourceContextStates.Archived);
}
```
Adjust the exact wording assertion once Step 3's message format is finalized — keep the test's `WithMessage` pattern loose enough (`*2 active integration*`) to survive minor copy changes while still proving the counts are surfaced.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceContextServiceTests.Archive"`
Expected: FAIL — `ArchiveAsync` still calls the old `owned > 0` check against a summary object it doesn't know how to read (compile error via `OwnershipSummaries` not existing until Task 8 Step 6 is done — if Task 8 is already committed, this instead fails on the old message text).

- [ ] **Step 3: Update `ArchiveAsync`**

Replace lines 111-128:
```csharp
public async ValueTask<ResourceContext> ArchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default)
{
    var existing = await GetRequiredAsync(id, cancellationToken).ConfigureAwait(false);
    EnsureNotReserved(existing, "archived");
    if (!existing.IsActive) return existing;

    var summary = await _repository.GetOwnershipSummaryAsync(existing.Id, cancellationToken).ConfigureAwait(false);
    if (summary.BlocksArchive)
    {
        throw new InvalidOperationException(
            $"Cannot archive ResourceContext '{existing.Name}' ({existing.Id.Value}): it still has " +
            $"{summary.LiveIntegrations} active integration(s), {summary.ActiveConnections} active connection(s) " +
            $"and {summary.EnabledSchedules} enabled schedule(s). Retire or disable them first.");
    }

    return await SaveAsync(existing with { State = ResourceContextStates.Archived }, cancellationToken).ConfigureAwait(false);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceContextServiceTests"`
Expected: PASS — full file, not just the Archive tests, to catch any other test relying on the old count-based behavior (e.g. `ArchiveThenUnarchive_RoundTrips`, `ArchivedContext_StillResolvesById` — these should be unaffected since they exercise the zero-owned-resources path, which now means an all-zero `ResourceContextOwnershipSummary`, still non-blocking).

- [ ] **Step 5: Commit**

```bash
git add src/Apperture.Core/Apperture.Core.Application/ResourceContexts/ResourceContextService.cs tests/Apperture.UnitTests/ResourceContexts/ResourceContextServiceTests.cs
git commit -m "CTX-3A: refine archive rule to 'nothing live' using ownership summary (§6.5, D2)"
```

---

## Task 10: Reactivation guard — Integration activate (§6.5, D2)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationManagementService.cs:130-165` (`UpdateIntegrationAsync`, `ActivateIntegrationAsync`)
- Test: `tests/Apperture.UnitTests/ResourceContexts/ResourceContextReactivationGuardTests.cs` (new)

**Interfaces:**
- Consumes: `IResourceContextService` (already injected into `IntegrationManagementService` per the existing `CreateIntegrationAsync` pattern — reuse the same field, do not add a second one).
- Produces: `UpdateIntegrationAsync` throws `InvalidOperationException` naming the Application when a caller attempts to set `State = IntegrationState.Active` on an Integration whose owning ResourceContext is archived. This exception must map to HTTP 409 at the controller layer — verify `IntegrationsController`'s existing `catch (InvalidOperationException ex) { return BadRequest(...) }` vs `Conflict(...)` mapping for `Update`/`Activate` actions (research noted `Create`/`Update` map `InvalidOperationException` to `BadRequest` at lines 396-447) and, if the controller currently maps this exception type to 400 rather than 409, introduce a dedicated exception type (e.g. `ResourceContextArchivedException : InvalidOperationException`) so the controller can special-case it to 409 without reclassifying every other `InvalidOperationException` on this endpoint. Decide and document the choice in this task's commit message; do not silently leave the reactivation guard returning 400 if the design gate's acceptance criteria (§14, "rejected with a message naming the Application") only requires the message content, not a specific status code — re-read §14's exact wording before deciding, and if the status code is unspecified there, 400 is acceptable and no new exception type is needed.

- [ ] **Step 1: Write the failing test**

Using `ResourceContextOwnershipTestHarness` (already wires real `IntegrationManagementService` over EF Core InMemory):
```csharp
[Fact]
public async Task ActivateIntegration_UnderAnArchivedContext_IsRejected()
{
    await using var harness = await ResourceContextOwnershipTestHarness.CreateAsync();
    var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
    var integration = await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Retired);
    await harness.ResourceContexts.ArchiveAsync(context.Id);

    var act = () => harness.Integrations.ActivateIntegrationAsync(integration.Id);

    await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Canvas*archived*");
}

[Fact]
public async Task ActivateIntegration_UnderAnActiveContext_Succeeds()
{
    await using var harness = await ResourceContextOwnershipTestHarness.CreateAsync();
    var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
    var integration = await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Retired);

    var activated = await harness.Integrations.ActivateIntegrationAsync(integration.Id);

    activated.State.Should().Be(IntegrationState.Active);
}

[Fact]
public async Task UpdateIntegration_SettingOtherFieldsUnderAnArchivedContext_IsStillAllowed()
{
    await using var harness = await ResourceContextOwnershipTestHarness.CreateAsync();
    var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
    var integration = await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Retired);
    await harness.ResourceContexts.ArchiveAsync(context.Id);

    var updated = await harness.Integrations.UpdateIntegrationAsync(
        integration.Id, new UpdateIntegrationRequest(DisplayName: "Renamed"));

    updated.DisplayName.Should().Be("Renamed");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceContextReactivationGuardTests"`
Expected: FAIL — `ActivateIntegration_UnderAnArchivedContext_IsRejected` does not throw today (no guard exists).

- [ ] **Step 3: Add the guard in `UpdateIntegrationAsync`**

Read `IntegrationManagementService.cs:130-165` in full first (it is the single choke point both `ActivateIntegrationAsync` and any `State`-changing `PATCH` route through). Add, before the line that applies `State = request.State ?? existing.State`:
```csharp
if (request.State == IntegrationState.Active && existing.State != IntegrationState.Active)
{
    var owningContext = await _resourceContexts.GetAsync(existing.ResourceContextId, cancellationToken).ConfigureAwait(false);
    if (owningContext is not null && !owningContext.IsActive)
    {
        throw new InvalidOperationException(
            $"{owningContext.Name} is archived. Unarchive it first before activating this integration.");
    }
}
```
Place this after the existing authorization check and before the persistence call, matching the method's existing control flow (read the surrounding lines to place it correctly relative to any existing validation).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceContextReactivationGuardTests"`
Expected: PASS

- [ ] **Step 5: Run the full Integration management test suite for regressions**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~IntegrationManagementService"`
Expected: PASS — no existing activate/update test should have exercised an archived-context scenario before, so none should be newly broken by this additive guard.

- [ ] **Step 6: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationManagementService.cs tests/Apperture.UnitTests/ResourceContexts/ResourceContextReactivationGuardTests.cs
git commit -m "CTX-3A: reject activating an Integration owned by an archived Application (§6.5, D2)"
```

---

## Task 11: Reactivation guard — Schedule enable, and close the inherit-on-create gap (§6.5, D2)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/SqlScheduleManagementService.cs:121-128` (`EnableAsync`), `:61-94` (`ResolveOwningResourceContextAsync`)
- Test: `tests/Apperture.UnitTests/ResourceContexts/ResourceContextReactivationGuardTests.cs` (append)

**Interfaces:**
- Consumes: `IResourceContextService.GetAsync`/`ResolveForNewOwnershipAsync` (already injected into `SqlScheduleManagementService`, since `ResolveOwningResourceContextAsync` already calls `ResolveForNewOwnershipAsync` on two of its three branches).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task EnableSchedule_UnderAnArchivedContext_IsRejected()
{
    await using var harness = await ResourceContextOwnershipTestHarness.CreateAsync();
    var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
    var schedule = await harness.CreateStandaloneScheduleAsync(context.Id, enabled: false);
    await harness.ResourceContexts.ArchiveAsync(context.Id);

    var act = () => harness.Schedules.EnableAsync(schedule.Id);

    await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Canvas*archived*");
}

[Fact]
public async Task CreateIntegrationAttachedSchedule_InheritingAnArchivedIntegrationContext_IsRejected()
{
    await using var harness = await ResourceContextOwnershipTestHarness.CreateAsync();
    var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
    var integration = await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Retired);
    await harness.ResourceContexts.ArchiveAsync(context.Id);

    var act = () => harness.Schedules.CreateScheduleAsync(
        new CreateScheduleRequest(integration.Id, "nightly", "0 0 * * *", TimeZone: "UTC", Enabled: true));

    await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Canvas*archived*");
}
```
Confirm the exact constructor shape of `CreateScheduleRequest` and the exact method name on `SqlScheduleManagementService` handling integration-attached creation by reading `SqlScheduleManagementService.cs` in full before writing this test — the research report's citations (`ResolveOwningResourceContextAsync`, lines 61-94) describe the *resolution* helper, not necessarily the public entry-point signature; adjust the test's call to match whatever the real public API is (likely `IScheduleManagementService.CreateScheduleAsync` per the interface file, `IScheduleManagementService.cs`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceContextReactivationGuardTests"`
Expected: FAIL on the two new cases.

- [ ] **Step 3: Add the guard in `EnableAsync`**

Read `SqlScheduleManagementService.cs:121-128` in full. Add, before the `Enabled = true` update:
```csharp
public async ValueTask EnableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default)
{
    var def = await _repo.GetDefinitionAsync(scheduleId, cancellationToken)
        ?? throw new InvalidOperationException($"Schedule {scheduleId.Value} not found.");

    var owningContext = await _resourceContexts.GetAsync(new ResourceContextId(def.ResourceContextId), cancellationToken).ConfigureAwait(false);
    if (owningContext is not null && !owningContext.IsActive)
    {
        throw new InvalidOperationException(
            $"{owningContext.Name} is archived. Unarchive it first before enabling this schedule.");
    }

    var proposed = def with { Enabled = true };
    var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(def, proposed, _timeProvider.GetUtcNow());
    await _repo.UpdateDefinitionAsync(merged, cancellationToken);
}
```

- [ ] **Step 4: Close the inherit-on-create gap in `ResolveOwningResourceContextAsync`**

Read lines 61-94 in full. The research identified that the Integration-attached inherit branch (lines 77-81) returns `integration.ResourceContextId` directly with no Active check, unlike the standalone and explicit-and-matching branches which call `ResolveForNewOwnershipAsync` (which does check Active). Change the inherit branch so it also validates activeness — the simplest fix consistent with "inherit, don't re-resolve ownership" is an explicit guard immediately after resolving `integration.ResourceContextId`, before returning it:
```csharp
// Inherit-from-Integration branch (previously: return integration.ResourceContextId; with no check)
var owningContext = await _resourceContexts.GetAsync(integration.ResourceContextId, cancellationToken).ConfigureAwait(false);
if (owningContext is not null && !owningContext.IsActive && request.Enabled)
{
    throw new InvalidOperationException(
        $"{owningContext.Name} is archived. Unarchive it first before creating an enabled schedule for this integration.");
}
return integration.ResourceContextId;
```
Note the `&& request.Enabled` guard: creating a **disabled** schedule under an archived context's Integration is not a reactivation and must remain allowed (it does not make anything live) — confirm `request`/`Enabled` is in scope at this point in the method; if the method signature doesn't expose the caller's requested `Enabled` value at this point, thread it through as an additional parameter to `ResolveOwningResourceContextAsync` rather than checking it elsewhere, since the check is only meaningful in the inherit branch.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceContextReactivationGuardTests"`
Expected: PASS

- [ ] **Step 6: Run the full Schedule management test suite for regressions**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ScheduleManagementService|FullyQualifiedName~SqlScheduleManagementService"`
Expected: PASS — specifically re-verify no existing test creates a **disabled** attached schedule under an archived-context Integration and expects rejection (Step 4's `&& request.Enabled` guard must not break that path).

- [ ] **Step 7: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.ControlPlane.Security/SqlScheduleManagementService.cs tests/Apperture.UnitTests/ResourceContexts/ResourceContextReactivationGuardTests.cs
git commit -m "CTX-3A: reject enabling a Schedule (or creating an enabled one) under an archived Application (§6.5, D2)"
```

---

## Task 12: Document the verified Connection non-guard (no un-retire path exists)

**Files:**
- Modify: `tests/Apperture.UnitTests/ResourceContexts/ResourceContextReactivationGuardTests.cs` (append one documentation-style test)

**Interfaces:**
- None new — this task records a verified negative so a future reader doesn't re-open the question.

- [ ] **Step 1: Add a documentation test proving the guard is unnecessary today**

```csharp
[Fact]
public void ConnectionManagementService_HasNoReactivationPath_SoNoGuardIsNeededYet()
{
    // Verified during CTX-3A: ConnectionState is one-way (Active -> Retired only).
    // RetireConnectionAsync only ever sets State = Retired; UpdateConnectionAsync never touches State.
    // There is no un-retire/reactivate method on IConnectionManagementService.
    // If this enum ever gains a reverse transition, a reactivation guard analogous to
    // IntegrationManagementService.UpdateIntegrationAsync's must be added at that time.
    var connectionStateNames = Enum.GetNames<ConnectionState>();

    connectionStateNames.Should().BeEquivalentTo(["Active", "Retired"],
        "this test intentionally breaks if ConnectionState grows a reverse transition, " +
        "as a prompt to add the CTX-3A reactivation guard for Connections");
}
```

- [ ] **Step 2: Run to verify it passes today**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ConnectionManagementService_HasNoReactivationPath"`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add tests/Apperture.UnitTests/ResourceContexts/ResourceContextReactivationGuardTests.cs
git commit -m "CTX-3A: record verified absence of a Connection reactivation path (§6.5 scope note)"
```

---

## Self-Review Notes (for the plan author, already applied above)

- **Spec coverage:** A1 (Tasks 2-4), A2 (Task 5), A11 (Task 1), `ResourceContextViewScope` (Task 6), `ResourceContextViewRoutes` (Task 7), A5 + archive refinement (Tasks 8-9), reactivation guards for Integration/Connection/Schedule (Tasks 10-12) are all covered. A3 (fleet `contextId`) is deliberately deferred to Plan 4 per design gate §13's own step ordering — noted explicitly in Task 3.
- **Type consistency:** `ResourceContextOwnershipSummary` is defined once (Task 8) and reused verbatim in Tasks 9-11's test code; `ResourceContextViewScope`/`ResourceContextViewMode` are defined once (Tasks 6-7) for later plans to consume without redefinition.
- **No placeholders:** every step above either shows real code or names the exact existing file/lines to read before writing new code; the one intentionally-flagged design decision left open (Task 10's 400-vs-409 mapping, Task 3's `Context` vs `ContextId` naming) is called out explicitly as a decision to make during implementation, not silently deferred.
