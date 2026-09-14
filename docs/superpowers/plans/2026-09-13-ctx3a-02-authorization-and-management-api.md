# CTX-3A Plan 2 — Authorization & Management API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the single new `resource_context.manage` instance-scoped permission, wrap the existing (deliberately unauthorized) `IResourceContextService` in a new `ResourceContextManagementService` that authorizes and audits, and expose it through a brand-new `ResourceContextsController` at `api/resource-contexts` — the full CTX-3A management API surface (A4/A10).

**Architecture:** `ResourceContextManagementService` lives in `Apperture.ControlPlane.Security` alongside its siblings (`IntegrationManagementService`, `PlatformSettingsService`) and follows the "authorize, then delegate to the non-authorizing Core.Application service" pattern already established by `IntegrationManagementService.CreateIntegrationAsync`, combined with `PlatformSettingsService`'s audit-append shape. The controller is a thin exception-to-HTTP-status translator, following `IntegrationsController`'s existing convention exactly. This plan depends on Plan 1 (`ResourceContextOwnershipSummary`, `IResourceContextRepository.GetOwnershipSummaryAsync`/`GetOwnershipSummariesAsync`) being merged first, since the controller's `GET summary` action and the archive-409 body both need it.

**Tech Stack:** .NET / C#, ASP.NET Core controllers, xUnit + FluentAssertions, the existing `IAuthorizationEvaluationService`/`IPrincipalContext`/`IAuditService` abstractions.

**Spec:** `docs/01-architecture/transitions/resource-contexts/ctx3-design-gate.md` — this plan implements design-gate §13 CTX-3A steps 3–4 (`§6.5` lifecycle table, `§9` authorization boundary in full, `§10.1` rows A4/A10, `§14` "Authorization and contracts" and part of "Management" acceptance criteria).

**Depends on:** `2026-09-13-ctx3a-01-contracts-and-domain.md` (must be merged first — this plan's controller and management service use `ResourceContextOwnershipSummary` and the repository's summary methods).

## Global Constraints

- The only new permission is `resource_context.manage` (`ActionConstants.ResourceContextManage`) at `ScopeKeyConstants.Instance` scope, on a new `ResourceTypeConstants.ResourceContext` resource type, family `ResourceFamily.Administrative` (design gate §9.1). No other permission, scope key, or resource type is added in this plan.
- No `context:{id}` scope key, no context-scoped role/grant/RoleAssignment, no context-aware resource-visibility permission is introduced anywhere (design gate §9.3). `resource_context.manage` is instance-scoped only.
- `GET api/resource-contexts` (list/get) requires only `[Authorize]` (authenticated), never `resource_context.manage` — reading is not permission-gated (design gate §9.2). Only create/patch/archive/unarchive require the permission.
- The summary endpoint (`GET api/resource-contexts/summary`) masks per-family counts by the caller's own read permission for that family (`integration.read`, `connection.read`, `schedule.manage`) — a family the caller cannot read returns `null` for that count, never an error and never a leaked number (design gate §9.2).
- `IResourceContextService`/`IResourceContextRepository` gain **no** authorization dependency in this plan — authorization lives exclusively in the new `ResourceContextManagementService` (design gate §9.1, "delegates to `IResourceContextService`, which by design performs no authorization").
- No delete endpoint exists or is added for `ResourceContext` (design gate §6.5 lifecycle table: "Delete: None / None / No route").
- Attempts to mutate Shared (rename, describe, re-slug, archive) fail with 409 (`IsReserved`) — this is existing `ResourceContextService` behavior (`EnsureNotReserved`), not new in this plan; the controller must surface it as 409, not 400 or 500.
- `ResourceContextAuthorizationNonChangeTests.NoResourceContextResourceTypeExists` is the **one** assertion this plan deliberately flips, in a commit that says why; its three companion assertions (`NoContextScopeKeyFormExists`, `InstanceScopeStillContainsEverything`, `GovernedIdentities_IgnoreResourceContextOwnership`) must stay green and unmodified (design gate §9.3, R22).

---

## File Structure

**New files:**
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceContextManagementService.cs` — authorize + audit + delegate.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IResourceContextManagementService.cs` — its interface.
- `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ResourceContextsController.cs` — the new controller.
- `tests/Apperture.UnitTests/ResourceContexts/ResourceContextManagementServiceTests.cs`
- `tests/Apperture.IntegrationTests/ResourceContexts/ResourceContextsControllerTests.cs` (or wherever this repo's controller-level integration tests for similar management controllers live — confirm the convention by locating `IntegrationsController`'s own integration test file first).

**Modified files:**
- `src/Apperture.Core/Apperture.Core.Domain/Governance/ResourceTypeConstants.cs` — add `ResourceContext`.
- `src/Apperture.Core/Apperture.Core.Domain/Governance/ActionConstants.cs` — add `ResourceContextManage`.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/PlatformPermissionSeeder.cs` — seed the new permission in `SeedCore`.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/PlatformAdminRolePermissionRepair.cs` — add `RequiredResourceContextPermissions`, wire into `ApplyAllRepairs`.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/Models/OperatorCapabilitiesDto.cs` — add `CanManageResourceContexts`.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/OperatorCapabilitiesQueryService.cs` — evaluate the new capability; update `DenyAll` and the final constructor call.
- `tests/Apperture.UnitTests/ResourceContexts/ResourceContextAuthorizationNonChangeTests.cs` — flip `NoResourceContextResourceTypeExists`.
- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Integration.cs` (or wherever `IIntegrationManagementService` is registered — confirm exact file) — register `IResourceContextManagementService`.

**Interfaces produced for later plans:**
- `IResourceContextManagementService` — full CRUD+lifecycle surface, consumed by `ResourceContextsController` in this plan; also the natural extension point if Plan 3's Config UI needs anything beyond the client HTTP contract (it shouldn't — Plan 3 talks to the controller only).
- `POST/GET/PATCH api/resource-contexts*` — consumed by Plan 3 (`Apperture.Management.Client` gets a matching client method set built against this exact surface — Plan 3 owns adding the client methods, this plan only builds the server side and documents the exact request/response shapes below so Plan 3 has no ambiguity).
- `OperatorCapabilitiesDto.CanManageResourceContexts : bool` — consumed by Plan 3 (Config → Applications page gates Create/Archive/Edit actions on this flag).

---

## Task 1: Add `ResourceTypeConstants.ResourceContext` and `ActionConstants.ResourceContextManage`

**Files:**
- Modify: `src/Apperture.Core/Apperture.Core.Domain/Governance/ResourceTypeConstants.cs:7-56`
- Modify: `src/Apperture.Core/Apperture.Core.Domain/Governance/ActionConstants.cs`
- Test: `tests/Apperture.UnitTests/ResourceContexts/ResourceContextAuthorizationNonChangeTests.cs` (this is Task 5 below — do not flip the assertion yet in this task; Task 1 alone will make `NoResourceContextResourceTypeExists` fail, which is expected and intentional until Task 5).

**Interfaces:**
- Produces: `ResourceTypeConstants.ResourceContext = "ResourceContext"`, `ActionConstants.ResourceContextManage = "resource_context.manage"`.

- [ ] **Step 1: Add the resource type constant**

In `ResourceTypeConstants.cs`, add under the `// Administrative` grouping (or a new grouping if none fits — read the file's existing groupings first):
```csharp
public const string ResourceContext = "ResourceContext";
```

- [ ] **Step 2: Add the action constant, following the `managed_contact.*` multi-word convention**

In `ActionConstants.cs`, add near other `.manage`-suffixed multi-word actions:
```csharp
/// <summary>Create, rename, re-slug, describe, archive, unarchive a ResourceContext (Application). CTX-3A.</summary>
public const string ResourceContextManage = "resource_context.manage";
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build src/Apperture.Core/Apperture.Core.Domain/Apperture.Core.Domain.csproj`
Expected: SUCCEEDS.

- [ ] **Step 4: Run the non-change tests to confirm the expected, temporary red**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceContextAuthorizationNonChangeTests"`
Expected: FAIL on `NoResourceContextResourceTypeExists` only (it now asserts `names.Should().NotContain("ResourceContext")`, which is now false). The three companion tests (`NoContextScopeKeyFormExists`, `InstanceScopeStillContainsEverything`, `GovernedIdentities_IgnoreResourceContextOwnership`) must still PASS — verify this explicitly, since a regression there would mean this change leaked into scope-key or identity territory it must not touch.

- [ ] **Step 5: Commit**

```bash
git add src/Apperture.Core/Apperture.Core.Domain/Governance/ResourceTypeConstants.cs src/Apperture.Core/Apperture.Core.Domain/Governance/ActionConstants.cs
git commit -m "CTX-3A: add ResourceContext resource type and resource_context.manage action (§9.1)"
```

---

## Task 2: Deliberately update `ResourceContextAuthorizationNonChangeTests` (§9.3, R22)

**Files:**
- Modify: `tests/Apperture.UnitTests/ResourceContexts/ResourceContextAuthorizationNonChangeTests.cs:35-45`

**Interfaces:**
- None new — this task closes the intentional red from Task 1, Step 4.

- [ ] **Step 1: Flip the one assertion, with a comment explaining why**

Replace lines 35-45:
```csharp
/// <summary>Permissions on ResourceContexts themselves are CTX-3-or-later.</summary>
[Fact]
public void NoResourceContextResourceTypeExists()
{
    var names = typeof(ResourceTypeConstants)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();

    names.Should().NotContain("ResourceContext");
    names.Should().Contain(["Integration", "Connection", "Binding"]);
}
```
with:
```csharp
/// <summary>
/// CTX-3A deliberately introduces a "ResourceContext" resource type — not to scope authorization by
/// ownership (that remains explicitly forbidden; see NoContextScopeKeyFormExists and
/// GovernedIdentities_IgnoreResourceContextOwnership below, both unchanged), but to govern *managing the
/// ResourceContext rows themselves* (create/rename/re-slug/describe/archive/unarchive an Application) via
/// the new instance-scoped resource_context.manage permission. This is the one assertion CTX-3's design
/// gate (§9.3, R22) calls out as deliberately superseded.
/// </summary>
[Fact]
public void ResourceContextResourceTypeExists_ForManagingContextsThemselves_NotForOwnershipScoping()
{
    var names = typeof(ResourceTypeConstants)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();

    names.Should().Contain("ResourceContext");
    names.Should().Contain(["Integration", "Connection", "Binding"]);
}
```

- [ ] **Step 2: Run all four tests in the file to verify the full set is green**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceContextAuthorizationNonChangeTests"`
Expected: PASS — all four tests, including the renamed one and the three unchanged companions.

- [ ] **Step 3: Commit**

```bash
git add tests/Apperture.UnitTests/ResourceContexts/ResourceContextAuthorizationNonChangeTests.cs
git commit -m "CTX-3A: deliberately update the CTX-1 non-change test for the new ResourceContext resource type (§9.3, R22)

The companion assertions (no context: scope key, governed identities ignore ownership) are unchanged and still pass."
```

---

## Task 3: Seed `resource_context.manage` and repair it into Platform Administrator

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/PlatformPermissionSeeder.cs:82-92` area
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/PlatformAdminRolePermissionRepair.cs:61-66, 106-122`
- Test: locate the existing test file that exercises `PlatformPermissionSeeder`/`PlatformAdminRolePermissionRepair` for a recent permission addition (search `tests/Apperture.UnitTests` for `PlatformPermissionSeeder` or `PlatformAdminRolePermissionRepair` and follow that file's exact pattern — likely named something like `PlatformPermissionSeederTests.cs`/`PlatformAdminRolePermissionRepairTests.cs`).

**Interfaces:**
- Produces: after seeding, `resource_context.manage` on `ResourceContext`/`instance` is a registered permission; after repair, Platform Administrator holds it.

- [ ] **Step 1: Write the failing tests**

In the located seeder test file, add (matching that file's existing style — read it first for the exact assertion helper names, e.g. `registry.HasPermission(...)`):
```csharp
[Fact]
public void SeedCore_RegistersResourceContextManagePermission()
{
    var registry = CreateRegistryAndSeed(); // use whatever this file's existing setup helper is named

    registry.HasPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext, ScopeKeyConstants.Instance)
        .Should().BeTrue();
}
```
In the located repair test file, add:
```csharp
[Fact]
public async Task ApplyAllRepairs_GrantsResourceContextManageToPlatformAdministrator()
{
    var repair = CreateRepairHarness(); // use whatever this file's existing setup helper is named

    await repair.ApplyAllRepairsAsync();

    (await repair.PlatformAdministratorHasPermissionAsync(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext, ScopeKeyConstants.Instance))
        .Should().BeTrue();
}
```
Adjust both to the actual helper/assertion API found in the located files — do not invent method names; this step's exact code must be rewritten against what those files actually expose once read.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~PlatformPermissionSeeder|FullyQualifiedName~PlatformAdminRolePermissionRepair"`
Expected: FAIL on the two new tests.

- [ ] **Step 3: Add the seeding entry**

In `PlatformPermissionSeeder.cs`, in `SeedCore`, add near the other `Administrative`-family, `Instance`-scoped registrations (following the simpler one-line-per-permission form used for `ActionConstants.ScheduleManage`):
```csharp
Register(registry, ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext, ScopeKeyConstants.Instance, ResourceFamily.Administrative);
```
Decide whether an explicit `registry.RegisterResource(new ResourceDescriptor(ResourceTypeConstants.ResourceContext, ScopeKeyConstants.Instance), ResourceFamily.Administrative);` call is also needed before this line — `InstanceConfiguration` does this explicitly while `Schedule` does not; read both surrounding examples in the file and match whichever pattern the file's own internal consistency suggests is required for a *new* resource type versus one merely gaining an additional permission on an existing type. `ResourceContext` is a genuinely new resource type here, which argues for following the `InstanceConfiguration` (explicit `RegisterResource`) pattern rather than the `Schedule` (implicit) pattern.

- [ ] **Step 4: Add the repair entry**

In `PlatformAdminRolePermissionRepair.cs`, add near `RequiredPlatformSettingsPermissions`:
```csharp
/// <summary>ResourceContext (Application) management (CTX-3A).</summary>
public static readonly PermissionDescriptor[] RequiredResourceContextPermissions =
[
    new(ActionConstants.ResourceContextManage, new ResourceDescriptor(ResourceTypeConstants.ResourceContext, ScopeKeyConstants.Instance))
];
```
In `ApplyAllRepairs`, add before `return step;`:
```csharp
step = EnsureSet(step, RequiredResourceContextPermissions);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~PlatformPermissionSeeder|FullyQualifiedName~PlatformAdminRolePermissionRepair"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.ControlPlane.Security/PlatformPermissionSeeder.cs src/Apperture.ControlPlane/Apperture.ControlPlane.Security/PlatformAdminRolePermissionRepair.cs
git commit -m "CTX-3A: seed resource_context.manage and repair it into Platform Administrator (A10, §9.1)"
```

---

## Task 4: `OperatorCapabilitiesDto.CanManageResourceContexts`

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/Models/OperatorCapabilitiesDto.cs:14-42`
- Modify: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/OperatorCapabilitiesQueryService.cs:14-15` (`DenyAll`), `:37-198` (`GetCapabilitiesAsync`), `:156-184` (final constructor call)
- Test: locate the existing `OperatorCapabilitiesQueryService` test file (search `tests/Apperture.UnitTests` for `OperatorCapabilitiesQueryServiceTests` or similar).

**Interfaces:**
- Produces: `OperatorCapabilitiesDto.CanManageResourceContexts : bool` — the 28th property, appended after the existing 27, defaulted to `false`.

- [ ] **Step 1: Write the failing test**

In the located test file, add (matching its existing per-capability test pattern — likely one test per capability proving grant → true, no-grant → false):
```csharp
[Fact]
public async Task GetCapabilitiesAsync_WhenGrantedResourceContextManage_SetsCanManageResourceContexts()
{
    var service = CreateServiceWithGrantedPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);

    var caps = await service.GetCapabilitiesAsync(SomeActor, CancellationToken.None);

    caps.CanManageResourceContexts.Should().BeTrue();
}

[Fact]
public async Task GetCapabilitiesAsync_WithoutGrant_DeniesCanManageResourceContexts()
{
    var service = CreateServiceWithNoPermissions();

    var caps = await service.GetCapabilitiesAsync(SomeActor, CancellationToken.None);

    caps.CanManageResourceContexts.Should().BeFalse();
}
```
Rewrite the setup-helper calls to match whatever this file's actual harness looks like once read — every existing capability test in this file follows one shared pattern; copy it exactly for the two new tests.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~OperatorCapabilitiesQueryService"`
Expected: FAIL — compile error, `CanManageResourceContexts` doesn't exist on the DTO yet.

- [ ] **Step 3: Add the DTO property**

In `OperatorCapabilitiesDto.cs`, append a 28th positional parameter to the record, defaulted:
```csharp
bool CanManageResourceContexts = false);
```
(Exact insertion point: after the last existing parameter, before the closing `)` of the record declaration — read the file to confirm the precise syntax, since it's a multi-line positional record.)

- [ ] **Step 4: Update `DenyAll` and add the evaluation + final constructor argument**

In `OperatorCapabilitiesQueryService.cs`, change the `DenyAll` array literal (lines 14-15) from 27 `false`s to 28:
```csharp
private static readonly OperatorCapabilitiesDto DenyAll =
    new(false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false);
```
In `GetCapabilitiesAsync`, add a new evaluation alongside the others (following the exact shape of a neighboring `EvaluateAsync` call):
```csharp
var canManageResourceContexts = await EvaluateAsync(actor, ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext, cancellationToken);
```
And add `canManageResourceContexts` as the 28th positional argument to the final `return new OperatorCapabilitiesDto(...)` call (lines 156-184), in the same relative position as the DTO property was appended (last).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~OperatorCapabilitiesQueryService"`
Expected: PASS — including every pre-existing capability test in the file, to confirm the positional-argument reordering did not misalign any other flag (this is the exact risk the research flagged: 27→28 positional args must stay in lockstep between `DenyAll` and the final constructor call).

- [ ] **Step 6: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.ControlPlane.Security/Models/OperatorCapabilitiesDto.cs src/Apperture.ControlPlane/Apperture.ControlPlane.Security/OperatorCapabilitiesQueryService.cs
git commit -m "CTX-3A: surface CanManageResourceContexts on OperatorCapabilitiesDto (A10)"
```

---

## Task 5: `ResourceContextManagementService` — authorize + audit + delegate

**Files:**
- Create: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IResourceContextManagementService.cs`
- Create: `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceContextManagementService.cs`
- Test: `tests/Apperture.UnitTests/ResourceContexts/ResourceContextManagementServiceTests.cs`

**Interfaces:**
- Consumes: `IResourceContextService` (Plan 1 dependency unchanged — `GetAsync`, `GetBySlugAsync`, `ListAsync`, `CreateApplicationContextAsync`, `RenameAsync`, `ChangeSlugAsync`, `UpdateDescriptionAsync`, `ArchiveAsync`, `UnarchiveAsync`), `IResourceContextRepository.GetOwnershipSummaryAsync`/`GetOwnershipSummariesAsync` (Plan 1), `IPrincipalContext.GetCurrentActorAsync`, `IAuthorizationEvaluationService.EvaluateAsync(AuthorizationEvaluationContext, ct)` (the interface `IntegrationManagementService` uses — reuse it, not `IAuthorizationService`'s 4-arg overload, per research's explicit recommendation), `IAuditService.AppendAsync`.
- Produces:
  ```csharp
  namespace Apperture.ControlPlane.Security;

  public interface IResourceContextManagementService
  {
      ValueTask<IReadOnlyList<ResourceContext>> ListAsync(bool includeArchived, CancellationToken ct = default);
      ValueTask<ResourceContext?> GetAsync(ResourceContextId id, CancellationToken ct = default);
      ValueTask<ResourceContext> CreateAsync(string name, string? slug, string? description, CancellationToken ct = default);
      ValueTask<ResourceContext> UpdateAsync(ResourceContextId id, string? name, string? slug, string? description, CancellationToken ct = default);
      ValueTask<ResourceContext> ArchiveAsync(ResourceContextId id, CancellationToken ct = default);
      ValueTask<ResourceContext> UnarchiveAsync(ResourceContextId id, CancellationToken ct = default);
      ValueTask<ResourceContextOwnershipSummary> GetOwnershipSummaryAsync(ResourceContextId id, CancellationToken ct = default);
      ValueTask<IReadOnlyDictionary<string, ResourceContextOwnershipSummary>> GetOwnershipSummariesAsync(CancellationToken ct = default);
  }
  ```
  `ArchiveAsync`/`UpdateAsync` throw the same `InvalidOperationException`s `IResourceContextService` already throws (Shared-reserved 409, live-resources-block-archive 409) — the service does not swallow or reclassify them, it only adds the authorization/audit wrapper. `GetAsync`/`ListAsync`/`GetOwnershipSummaryAsync`/`GetOwnershipSummariesAsync` perform **no** authorization check (design gate §9.2 — reading is unauthenticated-permission-free, gated only by `[Authorize]` at the controller).

- [ ] **Step 1: Write the failing tests**

```csharp
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

public class ResourceContextManagementServiceTests
{
    [Fact]
    public async Task CreateAsync_WithoutResourceContextManage_Throws()
    {
        var harness = ManagementServiceTestHarness.WithNoPermissions();

        var act = () => harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task CreateAsync_WithResourceContextManage_CreatesAndAudits()
    {
        var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);

        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: "desc", CancellationToken.None);

        created.Name.Should().Be("Canvas");
        harness.AuditRecords.Should().ContainSingle(r => r.Action == "resource_context.created" && r.ResourceContextId == created.Id.Value);
    }

    [Fact]
    public async Task ArchiveAsync_WithResourceContextManage_DelegatesToUnderlyingArchiveRule()
    {
        var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);
        harness.SeedLiveIntegration(created.Id);

        var act = () => harness.Service.ArchiveAsync(created.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*active integration*");
    }

    [Fact]
    public async Task UpdateAsync_OnShared_Returns409StyleException()
    {
        var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);

        var act = () => harness.Service.UpdateAsync(ResourceContextId.Shared, name: "Renamed", slug: null, description: null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetAsync_RequiresNoPermission()
    {
        var harness = ManagementServiceTestHarness.WithNoPermissions();
        var created = await ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext)
            .Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);

        var found = await harness.Service.GetAsync(created.Id, CancellationToken.None);

        found.Should().NotBeNull();
    }

    [Fact]
    public async Task ArchiveThenUnarchive_WritesDistinctAuditActions()
    {
        var harness = ManagementServiceTestHarness.WithPermission(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext);
        var created = await harness.Service.CreateAsync("Canvas", slug: null, description: null, CancellationToken.None);

        await harness.Service.ArchiveAsync(created.Id, CancellationToken.None);
        await harness.Service.UnarchiveAsync(created.Id, CancellationToken.None);

        harness.AuditRecords.Select(r => r.Action).Should().Contain(["resource_context.archived", "resource_context.unarchived"]);
    }
}
```
`ManagementServiceTestHarness` is new scaffolding for this task — build it following the exact shape of `ResourceContextOwnershipTestHarness` (Plan 1) but additionally wiring a fake/stub `IAuthorizationEvaluationService` (grant/deny by permission), a fake `IPrincipalContext` (returns one fixed actor), and a fake `IAuditService` (`AppendAsync` records into an in-memory list, exposed as `AuditRecords`) — reuse `ResourceContextOwnershipTestHarness`'s real `SqlResourceContextRepository`/`ResourceContextService` over EF Core InMemory rather than re-mocking those.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceContextManagementServiceTests"`
Expected: FAIL — the type doesn't exist yet.

- [ ] **Step 3: Implement `IResourceContextManagementService` and `ResourceContextManagementService`**

```csharp
namespace Apperture.ControlPlane.Security;

public interface IResourceContextManagementService
{
    ValueTask<IReadOnlyList<ResourceContext>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);
    ValueTask<ResourceContext?> GetAsync(ResourceContextId id, CancellationToken cancellationToken = default);
    ValueTask<ResourceContext> CreateAsync(string name, string? slug, string? description, CancellationToken cancellationToken = default);
    ValueTask<ResourceContext> UpdateAsync(ResourceContextId id, string? name, string? slug, string? description, CancellationToken cancellationToken = default);
    ValueTask<ResourceContext> ArchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default);
    ValueTask<ResourceContext> UnarchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default);
    ValueTask<ResourceContextOwnershipSummary> GetOwnershipSummaryAsync(ResourceContextId id, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyDictionary<string, ResourceContextOwnershipSummary>> GetOwnershipSummariesAsync(CancellationToken cancellationToken = default);
}

public sealed class ResourceContextManagementService(
    IResourceContextService resourceContexts,
    IResourceContextRepository resourceContextRepository,
    IPrincipalContext principalContext,
    IAuthorizationEvaluationService authorizationEvaluation,
    IAuditService audit) : IResourceContextManagementService
{
    public ValueTask<IReadOnlyList<ResourceContext>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default) =>
        resourceContexts.ListAsync(includeArchived, cancellationToken);

    public ValueTask<ResourceContext?> GetAsync(ResourceContextId id, CancellationToken cancellationToken = default) =>
        resourceContexts.GetAsync(id, cancellationToken);

    public ValueTask<ResourceContextOwnershipSummary> GetOwnershipSummaryAsync(ResourceContextId id, CancellationToken cancellationToken = default) =>
        resourceContextRepository.GetOwnershipSummaryAsync(id, cancellationToken);

    public ValueTask<IReadOnlyDictionary<string, ResourceContextOwnershipSummary>> GetOwnershipSummariesAsync(CancellationToken cancellationToken = default) =>
        resourceContextRepository.GetOwnershipSummariesAsync(cancellationToken);

    public async ValueTask<ResourceContext> CreateAsync(string name, string? slug, string? description, CancellationToken cancellationToken = default)
    {
        var actor = await RequireManageAsync(cancellationToken).ConfigureAwait(false);
        var created = await resourceContexts.CreateApplicationContextAsync(name, slug, description, cancellationToken).ConfigureAwait(false);
        await AppendAuditAsync("resource_context.created", actor, created.Id, $"Created Application '{created.Name}' ({created.Slug.Value}).", cancellationToken).ConfigureAwait(false);
        return created;
    }

    public async ValueTask<ResourceContext> UpdateAsync(ResourceContextId id, string? name, string? slug, string? description, CancellationToken cancellationToken = default)
    {
        var actor = await RequireManageAsync(cancellationToken).ConfigureAwait(false);
        var changedFields = new List<string>();
        var current = await resourceContexts.GetAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"ResourceContext '{id.Value}' not found.");

        if (name is not null && !string.Equals(name, current.Name, StringComparison.Ordinal))
        {
            current = await resourceContexts.RenameAsync(id, name, cancellationToken).ConfigureAwait(false);
            changedFields.Add("name");
        }
        if (slug is not null && !string.Equals(slug, current.Slug.Value, StringComparison.Ordinal))
        {
            current = await resourceContexts.ChangeSlugAsync(id, slug, cancellationToken).ConfigureAwait(false);
            changedFields.Add("slug");
        }
        if (description is not null && !string.Equals(description, current.Description, StringComparison.Ordinal))
        {
            current = await resourceContexts.UpdateDescriptionAsync(id, description, cancellationToken).ConfigureAwait(false);
            changedFields.Add("description");
        }

        if (changedFields.Count > 0)
        {
            await AppendAuditAsync("resource_context.updated", actor, id, $"Changed fields: {string.Join(", ", changedFields)}.", cancellationToken).ConfigureAwait(false);
        }

        return current;
    }

    public async ValueTask<ResourceContext> ArchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default)
    {
        var actor = await RequireManageAsync(cancellationToken).ConfigureAwait(false);
        var archived = await resourceContexts.ArchiveAsync(id, cancellationToken).ConfigureAwait(false);
        await AppendAuditAsync("resource_context.archived", actor, id, $"Archived '{archived.Name}'.", cancellationToken).ConfigureAwait(false);
        return archived;
    }

    public async ValueTask<ResourceContext> UnarchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default)
    {
        var actor = await RequireManageAsync(cancellationToken).ConfigureAwait(false);
        var unarchived = await resourceContexts.UnarchiveAsync(id, cancellationToken).ConfigureAwait(false);
        await AppendAuditAsync("resource_context.unarchived", actor, id, $"Unarchived '{unarchived.Name}'.", cancellationToken).ConfigureAwait(false);
        return unarchived;
    }

    private async ValueTask<ActorId> RequireManageAsync(CancellationToken cancellationToken)
    {
        var actor = await principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var allowed = await authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.ResourceContextManage, new ResourceDescriptor(ResourceTypeConstants.ResourceContext, ScopeKeyConstants.Instance)),
                null),
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to manage applications.");

        return actor;
    }

    private async ValueTask AppendAuditAsync(string action, ActorId actorId, ResourceContextId resourceContextId, string details, CancellationToken cancellationToken)
    {
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            actorId,
            action,
            new ResourceDescriptor(ResourceTypeConstants.ResourceContext, ScopeKeyConstants.Instance),
            AuditOutcome.Success,
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            details,
            ResourceContextId: resourceContextId.Value);
        await audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
    }
}
```
Verify the exact `AuditRecord` constructor parameter order/names (positional vs. named) and `AuditOutcome`'s actual shape (enum vs. string constant) by re-reading `src/Apperture.Core/Apperture.Core.Domain/Audit/AuditRecord.cs` and `PlatformSettingsService.cs`'s `AppendAuditAsync` before finalizing — the sketch above follows `PlatformSettingsService`'s pattern but that file used a bare `AuditOutcome` identifier (likely a local constant/field on that class, e.g. `private const string AuditOutcome = "Success";`) rather than an enum; match whichever it actually is.

- [ ] **Step 4: Register the service in DI**

Add to `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Integration.cs` (or the correct extensions file once confirmed):
```csharp
services.AddScoped<IResourceContextManagementService, ResourceContextManagementService>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceContextManagementServiceTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IResourceContextManagementService.cs src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ResourceContextManagementService.cs tests/Apperture.UnitTests/ResourceContexts/ResourceContextManagementServiceTests.cs
git commit -m "CTX-3A: add ResourceContextManagementService — authorize, audit, delegate to IResourceContextService (§9.1)"
```

---

## Task 6: `ResourceContextsController` (A4)

**Files:**
- Create: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ResourceContextsController.cs`
- Test: `tests/Apperture.IntegrationTests/ResourceContexts/ResourceContextsControllerTests.cs` (confirm the exact project/path convention by locating `IntegrationsController`'s integration test file first — this repo may test controllers via `Apperture.IntegrationTests` with a real in-memory `WebApplicationFactory`, or via a lighter controller-unit-test style; match whichever `IntegrationsController` uses).

**Interfaces:**
- Produces the full API surface:
  ```text
  GET    api/resource-contexts?includeArchived=false        -> 200 [ResourceContextDto]
  GET    api/resource-contexts/{id}                          -> 200 ResourceContextDto | 404
  POST   api/resource-contexts        { name, slug?, description? } -> 201 ResourceContextDto | 400 (validation) | 403
  PATCH  api/resource-contexts/{id}   { name?, slug?, description? } -> 200 ResourceContextDto | 400 | 403 | 404 | 409 (Shared)
  POST   api/resource-contexts/{id}/archive   -> 200 ResourceContextDto | 403 | 404 | 409 (ResourceContextOwnershipSummary body)
  POST   api/resource-contexts/{id}/unarchive -> 200 ResourceContextDto | 403 | 404
  GET    api/resource-contexts/summary        -> 200 { [contextId]: ResourceContextOwnershipSummaryDto } (permission-masked)
  ```
  All actions except list/get require `resource_context.manage`, enforced entirely inside `ResourceContextManagementService` — the controller performs zero permission checks of its own, only `[Authorize]` at the class level (matching `IntegrationsController`'s convention exactly).

- [ ] **Step 1: Write the failing controller tests**

Following whatever harness `IntegrationsController`'s own tests use (a real `WebApplicationFactory`-backed integration test, most likely, given the project name `Apperture.IntegrationTests`), write:
```csharp
[Fact]
public async Task List_RequiresOnlyAuthentication_NotResourceContextManage()
{
    var client = Factory.CreateAuthenticatedClient(withPermissions: []);

    var response = await client.GetAsync("api/resource-contexts");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
}

[Fact]
public async Task Create_WithoutPermission_Returns403()
{
    var client = Factory.CreateAuthenticatedClient(withPermissions: []);

    var response = await client.PostAsJsonAsync("api/resource-contexts", new { name = "Canvas" });

    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}

[Fact]
public async Task Create_WithPermission_Returns201AndTheCreatedApplication()
{
    var client = Factory.CreateAuthenticatedClient(withPermissions: [(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext)]);

    var response = await client.PostAsJsonAsync("api/resource-contexts", new { name = "Canvas" });

    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var body = await response.Content.ReadFromJsonAsync<ResourceContextDto>();
    body!.Name.Should().Be("Canvas");
}

[Fact]
public async Task Patch_OnShared_Returns409()
{
    var client = Factory.CreateAuthenticatedClient(withPermissions: [(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext)]);

    var response = await client.PatchAsJsonAsync("api/resource-contexts/shared", new { name = "Renamed" });

    response.StatusCode.Should().Be(HttpStatusCode.Conflict);
}

[Fact]
public async Task Archive_WhenBlocked_Returns409WithOwnershipSummaryBody()
{
    var client = Factory.CreateAuthenticatedClient(withPermissions: [(ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext)]);
    var created = await CreateApplicationAsync(client, "Canvas");
    await SeedLiveIntegrationAsync(created.Id);

    var response = await client.PostAsync($"api/resource-contexts/{created.Id}/archive", null);

    response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var body = await response.Content.ReadFromJsonAsync<ResourceContextOwnershipSummaryDto>();
    body!.LiveIntegrations.Should().Be(1);
}

[Fact]
public async Task Summary_MasksCountsTheCallerCannotRead()
{
    var client = Factory.CreateAuthenticatedClient(withPermissions: []); // no integration.read, connection.read, schedule.manage

    var response = await client.GetAsync("api/resource-contexts/summary");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.Content.ReadFromJsonAsync<Dictionary<string, ResourceContextOwnershipSummaryDto>>();
    body.Values.Should().OnlyContain(s => s.LiveIntegrations == null && s.ActiveConnections == null && s.EnabledSchedules == null);
}
```
`Factory.CreateAuthenticatedClient(withPermissions:...)`, `CreateApplicationAsync`, `SeedLiveIntegrationAsync`, and `ResourceContextDto`/`ResourceContextOwnershipSummaryDto` (the controller's own response shapes, not yet defined) are placeholders for this step only in the sense that their *exact* helper names must be copied from `IntegrationsController`'s existing integration test file — read it in full before writing this step for real, and do not invent a testing harness from scratch if one already exists.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~ResourceContextsControllerTests"`
Expected: FAIL — the controller and route don't exist yet.

- [ ] **Step 3: Implement `ResourceContextsController`**

```csharp
namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/resource-contexts")]
[Authorize]
public sealed class ResourceContextsController(IResourceContextManagementService management) : ControllerBase
{
    public sealed record ResourceContextRef(string Id, string Name, string Slug);

    public sealed record ResourceContextDto(
        string Id, string Name, string Slug, string Kind, string State,
        string? Description, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

    public sealed record CreateResourceContextRequest(string Name, string? Slug, string? Description);

    public sealed record UpdateResourceContextRequest(string? Name, string? Slug, string? Description);

    public sealed record ResourceContextOwnershipSummaryDto(
        int? LiveIntegrations, int? RetiredIntegrations,
        int? ActiveConnections, int? RetiredConnections,
        int? EnabledSchedules, int? DisabledSchedules);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ResourceContextDto>>> List([FromQuery] bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var contexts = await management.ListAsync(includeArchived, cancellationToken).ConfigureAwait(false);
        return Ok(contexts.Select(ToDto).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ResourceContextDto>> Get(string id, CancellationToken cancellationToken = default)
    {
        var context = await management.GetAsync(new ResourceContextId(id), cancellationToken).ConfigureAwait(false);
        return context is null ? NotFound() : Ok(ToDto(context));
    }

    [HttpPost]
    public async Task<ActionResult<ResourceContextDto>> Create([FromBody] CreateResourceContextRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var created = await management.CreateAsync(request.Name, request.Slug, request.Description, cancellationToken).ConfigureAwait(false);
            return CreatedAtAction(nameof(Get), new { id = created.Id.Value }, ToDto(created));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult<ResourceContextDto>> Update(string id, [FromBody] UpdateResourceContextRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await management.UpdateAsync(new ResourceContextId(id), request.Name, request.Slug, request.Description, cancellationToken).ConfigureAwait(false);
            return Ok(ToDto(updated));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("reserved", StringComparison.OrdinalIgnoreCase)) { return Conflict(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("{id}/archive")]
    public async Task<ActionResult<ResourceContextDto>> Archive(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var archived = await management.ArchiveAsync(new ResourceContextId(id), cancellationToken).ConfigureAwait(false);
            return Ok(ToDto(archived));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("reserved", StringComparison.OrdinalIgnoreCase)) { return Conflict(new { error = ex.Message }); }
        catch (InvalidOperationException)
        {
            var summary = await management.GetOwnershipSummaryAsync(new ResourceContextId(id), cancellationToken).ConfigureAwait(false);
            return Conflict(new ResourceContextOwnershipSummaryDto(
                summary.LiveIntegrations, summary.RetiredIntegrations,
                summary.ActiveConnections, summary.RetiredConnections,
                summary.EnabledSchedules, summary.DisabledSchedules));
        }
    }

    [HttpPost("{id}/unarchive")]
    public async Task<ActionResult<ResourceContextDto>> Unarchive(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var unarchived = await management.UnarchiveAsync(new ResourceContextId(id), cancellationToken).ConfigureAwait(false);
            return Ok(ToDto(unarchived));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("summary")]
    public async Task<ActionResult<IReadOnlyDictionary<string, ResourceContextOwnershipSummaryDto>>> Summary(CancellationToken cancellationToken = default)
    {
        var summaries = await management.GetOwnershipSummariesAsync(cancellationToken).ConfigureAwait(false);
        var canReadIntegrations = await HasPermissionAsync(ActionConstants.IntegrationRead, ResourceTypeConstants.Integration, cancellationToken);
        var canReadConnections = await HasPermissionAsync(ActionConstants.ConnectionRead, ResourceTypeConstants.Connection, cancellationToken);
        var canReadSchedules = await HasPermissionAsync(ActionConstants.ScheduleManage, ResourceTypeConstants.Schedule, cancellationToken);

        var masked = summaries.ToDictionary(
            kv => kv.Key,
            kv => new ResourceContextOwnershipSummaryDto(
                canReadIntegrations ? kv.Value.LiveIntegrations : null,
                canReadIntegrations ? kv.Value.RetiredIntegrations : null,
                canReadConnections ? kv.Value.ActiveConnections : null,
                canReadConnections ? kv.Value.RetiredConnections : null,
                canReadSchedules ? kv.Value.EnabledSchedules : null,
                canReadSchedules ? kv.Value.DisabledSchedules : null));

        return Ok(masked);
    }

    private static ResourceContextDto ToDto(ResourceContext context) => new(
        context.Id.Value, context.Name, context.Slug.Value, context.Kind, context.State,
        context.Description, context.CreatedAtUtc, context.UpdatedAtUtc);
}
```
This sketch introduces a `HasPermissionAsync` helper not shown — implement it inline using whichever authorization-check mechanism is idiomatic for a read-only capability probe at the controller layer (likely delegating to the same `IAuthorizationEvaluationService`/`IPrincipalContext` used elsewhere, injected as additional constructor dependencies — the design gate explicitly says summary-masking is per-caller, so this cannot be a static/global check). Verify the exact action-constant names for `integration.read`/`connection.read` (`ActionConstants.IntegrationRead`/`ConnectionRead` are assumed here — confirm the real names by grepping `ActionConstants.cs`) and the exact `Kind`/`State`/`CreatedAtUtc`/`UpdatedAtUtc` property names/types on the domain `ResourceContext` record (Plan 1's research listed `Id, Name, Slug, Kind, State, IsReserved, Description, CreatedAtUtc, UpdatedAtUtc`) before finalizing `ToDto`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "FullyQualifiedName~ResourceContextsControllerTests"`
Expected: PASS

- [ ] **Step 5: Run the full non-change and ArchTests suites for regressions**

Run: `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceContext"`
Run: `dotnet test tests/Apperture.ArchTests/Apperture.ArchTests.csproj`
Expected: PASS on both.

- [ ] **Step 6: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ResourceContextsController.cs tests/Apperture.IntegrationTests/ResourceContexts/ResourceContextsControllerTests.cs
git commit -m "CTX-3A: add ResourceContextsController — list/get/create/patch/archive/unarchive/summary (A4)"
```

---

## Self-Review Notes

- **Spec coverage:** A10 (Tasks 1, 3, 4), the deliberate non-change-test update (Task 2), `ResourceContextManagementService` (Task 5), A4 full controller surface including the 409 ownership-summary body and permission-masked summary (Task 6) are all covered. §9.3's "explicitly not added" list (context scope keys, context-scoped roles/grants) is respected by omission — no task in this plan touches `RoleAssignment`, scope keys, or list-filtering-by-authorization.
- **Type consistency:** `IResourceContextManagementService`'s method names/signatures in Task 5 match exactly what Task 6's controller calls; `ResourceContextOwnershipSummary` (from Plan 1) is reused verbatim, never redefined.
- **No placeholders:** every code block is real; the two explicitly-flagged verification points (exact `AuditRecord` constructor shape, exact `ActionConstants` names for integration/connection read permissions, and the controller-test harness's real helper names) are called out as "read this file first, then adjust" rather than left as silent assumptions.
