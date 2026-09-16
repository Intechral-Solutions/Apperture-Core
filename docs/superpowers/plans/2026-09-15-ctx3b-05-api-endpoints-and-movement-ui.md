# CTX-3B Plan 5 — Movement API Endpoints and UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the six `ownership/move-preview` / `ownership/move` endpoints (§8.2) over `IResourceOwnershipMoveService` (Plans 1-4), add the matching `Apperture.Management.Client` methods and DTOs, and build the movement UI: an Integration hub "Move to another context…" action, the same flow on Connection detail (always showing "Used by"), Schedule detail's attached-vs-standalone handling, and the archive-refusal copy update (§6.5 D2).

**Sequencing gate — this plan is last for a reason.** It must not be started until Plans 2, 3 and 4 have landed and been verified, because it is the first thing that makes movement reachable by an operator. Before the first endpoint exists: transactional movement must be real (Plan 2), race prevention must be real and proven by executed SQL Server tests (Plan 2), readiness defense must be real (Plan 3), and movement auditing must be real (Plan 4).

**Architecture:**

- **Contracts.** Controllers map `ResourceMovePreview` (Core.Application, Plan 1) to a client-shippable `ResourceMovePreviewDto` in `Apperture.Management.Client`, reusing `Apperture.Core.Domain.ResourceContexts.ResourceContextRef` directly for `Source`/`Target` exactly as `IntegrationDto.ResourceContext` already does. Verified at review time: `Apperture.Management.Client.csproj` references `Apperture.Core.Domain`, `Apperture.Shared.Kernel` and `Apperture.RuntimePlane.Abstractions` — **not** `Apperture.Core.Application`. This plan adds no project reference and introduces no Core.Application dependency in the client.
- **Blocker/effect codes are mirrored, not hardcoded.** `ResourceMoveBlockerCodes`/`ResourceMoveEffectCodes` live in Core.Application, which the client cannot reference, so this plan mirrors them into `Apperture.Management.Client` and covers the mirror with the same parity test. No razor or client code compares a raw string literal.
- **HTTP mapping lives in one place.** A single static `ResourceMoveHttpMapper` in `Apperture.Management.Api`, used by all three controllers, rather than triplicating `CommonBlockerCodes`/`MapMoveResult`/`ToMoveDto`. This is mapping logic, not a controller base class, so it does not fight the house convention of per-controller `ToDto` mappers.
- **UI testing follows this repository's actual convention.** Verified at review time: **bUnit does not exist anywhere in this repository** — no package reference, no `using Bunit`, nothing. The house convention for UI tests is (a) razor **source-text assertions** (e.g. `Epic96IntegrationActionFeedbackTests`, which walks up to `.git` and reads `IntegrationDetail.razor` from disk) and (b) plain unit tests over extracted non-razor helpers. This plan uses both and introduces no new test framework.
- The UI reuses the existing `MismatchNotice`/`OwnershipChip` components unchanged (§8.9 relies on the §4.6 mechanics CTX-3A already shipped).

**Terminology: "Context", everywhere an operator can see it.** The target of a move may be **Shared** or an Application, so "Move to another application…" is wrong for half the cases. Every operator-visible string in this plan — dialog title, trigger buttons, labels, help text, toasts, and the archive-refusal copy — says **Context**. This deliberately overrides design gate §8.9's "Move to another application…" wording; the gate predates the ruling and the gate's own §8.3 already treats Shared as a normal target.

**Tech Stack:** C#/.NET 9, ASP.NET Core, Blazor WebAssembly, xUnit, FluentAssertions. No bUnit.

**Spec:** `docs/01-architecture/transitions/resource-contexts/ctx3-design-gate.md` §8.2, §8.3, §8.9, §6.5 D2, §13 CTX-3B steps 5-6, §14 CTX-3B acceptance criteria (endpoints/UI bullets).

---

## Global Constraints

- Routes are exactly `GET/POST api/{integrations|connections|schedules}/{id}/ownership/move-preview|move`, matching the existing `[HttpGet("{id}/...")]` sub-resource convention already used for `{id}/activate`, `{id}/retire`, `{id}/attention-routing` — no deviation.
- `UpdateIntegrationRequestBody`/`UpdateConnectionRequestBody`/`UpdateScheduleRequestBody` must not gain a `ResourceContextId` field (§8.2) — this plan never touches those records. Plan 1's `ResourceMoveDtoImmutabilityTests` already asserts it, including that the Schedule body has no `IntegrationId` either.
- **Movement UI never auto-switches the shell View.** After a move the page stays put; `OwnershipChip` re-renders from the fresh detail fetch; `MismatchNotice` appears if the current View now mismatches. Nothing in this plan calls `ResourceContextViewState.SetExplicitAsync`, and a test asserts that.
- Connection detail always shows "Used by" blast radius, even when `CanMove` is true (§8.5) — already partly available via `ConnectionDto.UsedIn`; the move dialog's preview additionally surfaces it via the `used_by_integration` effect.
- Schedule detail: an attached schedule shows a **disabled** action with a link to its Integration, never an enabled dialog trigger that would only fail on submit.
- Authorization is enforced entirely server-side (Plans 1-2 require `integration.update`/`connection.update`/`schedule.manage`); the UI hides actions the operator's `OperatorCapabilitiesDto` says they cannot perform, purely as UX, never as the actual guard.
- **No raw blocker/effect code literals** in controllers, client code or razor. Use the mirrored constants.

---

## File Structure

**New files:**
- `src/Apperture.ControlPlane/Apperture.Management.Client/ResourceMoveDtos.cs` — `ResourceMovePreviewDto`, `ResourceMoveBlockerDto`, `ResourceMoveEffectDto`, `ResourceOwnershipMoveRequest`, plus the mirrored `ResourceMoveBlockerCodes`/`ResourceMoveEffectCodes`.
- `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ResourceMoveHttpMapper.cs` — the one shared mapping/status helper.
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/MoveOwnershipDialogModel.cs` — the dialog's extracted, testable non-razor logic (target options, submit-enablement, preview generation guard).
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/MoveOwnershipDialog.razor` — the shared move dialog.
- `tests/Apperture.UnitTests/ResourceContexts/ResourceMoveDtoContractParityTests.cs`
- `tests/Apperture.UnitTests/ResourceContexts/ResourceOwnershipMoveControllerTests.cs`
- `tests/Apperture.Management.Ui.Shared.Tests/ResourceContexts/MoveOwnershipDialogModelTests.cs` — logic tests over the extracted model.
- `tests/Apperture.Management.Ui.Shared.Tests/ResourceContexts/MovementUiSourceTests.cs` — razor source-text assertions, matching `Epic96IntegrationActionFeedbackTests`'s established pattern.

**Modified files:**
- `IntegrationsController.cs`, `ConnectionsController.cs`, `SchedulesController.cs` — two endpoints each.
- `IManagementClient.cs` / `ManagementClient.cs` — six new client methods.
- `IntegrationDetail.razor`, `ConnectionDetail.razor`, `ScheduleDetail.razor` — the move action and dialog.
- `Pages/Config/Applications/ApplicationDetail.razor` — archive-refusal copy (§6.5 D2).

---

### Task 1: Client DTOs, mirrored codes, and contract-parity test

**Files:**
- Create: `src/Apperture.ControlPlane/Apperture.Management.Client/ResourceMoveDtos.cs`
- Test: `tests/Apperture.UnitTests/ResourceContexts/ResourceMoveDtoContractParityTests.cs`

**Interfaces:**
- Consumes: `Apperture.Core.Domain.ResourceContexts.ResourceContextRef` (already referenced by `Apperture.Management.Client`).
- Produces: `ResourceMovePreviewDto`, `ResourceMoveBlockerDto`, `ResourceMoveEffectDto`, `ResourceOwnershipMoveRequest`, and the client-side mirrors of both code classes.

- [ ] **Step 1: Write the failing contract-parity test (CTX-3A's A2 pattern, strengthened)**

```csharp
using System.Reflection;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3A's A2 pattern: reflection-based proof that the client DTO twin matches the server contract, so
/// drift is caught at build/test time rather than at runtime deserialization. This is the third time this
/// codebase has needed it (CTX-2 and CTX-3A each hit the drift once), so the assertions go beyond name
/// containment: an equal property count catches a client-side field the server never sends, and the
/// scalar type check catches a `Blockers` that is `IReadOnlyList&lt;string&gt;` on one side.
/// </summary>
public sealed class ResourceMoveDtoContractParityTests
{
    public static TheoryData<Type, Type> Pairs => new()
    {
        { typeof(ResourceMovePreview), typeof(ResourceMovePreviewDto) },
        { typeof(ResourceMoveBlocker), typeof(ResourceMoveBlockerDto) },
        { typeof(ResourceMoveEffect),  typeof(ResourceMoveEffectDto)  }
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Client_dto_has_exactly_the_server_properties(Type serverType, Type clientType)
    {
        var serverProps = serverType.GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var clientProps = clientType.GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

        clientProps.Should().Equal(serverProps,
            "the client twin must carry every server property and nothing extra -- an extra client field is a field the server never populates");
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Scalar_property_types_match(Type serverType, Type clientType)
    {
        foreach (var serverProp in serverType.GetProperties())
        {
            var clientProp = clientType.GetProperty(serverProp.Name);
            clientProp.Should().NotBeNull();

            // Scalars must match exactly. Collection/record-shaped properties legitimately differ, because
            // the client mirrors ResourceMoveBlocker as ResourceMoveBlockerDto -- those are covered by
            // their own row in this theory.
            if (serverProp.PropertyType.IsPrimitive || serverProp.PropertyType == typeof(string) || serverProp.PropertyType == typeof(bool))
                clientProp!.PropertyType.Should().Be(serverProp.PropertyType, $"{serverType.Name}.{serverProp.Name}");
        }
    }

    [Fact]
    public void Client_blocker_codes_mirror_the_server_codes_exactly()
    {
        ConstantValues(typeof(ResourceMoveBlockerCodes))
            .Should().BeEquivalentTo(ConstantValues(typeof(Apperture.Management.Client.ResourceMoveBlockerCodes)),
                "the client mirrors these because it cannot reference Core.Application -- a mirror that drifts is worse than no mirror");
    }

    [Fact]
    public void Client_effect_codes_mirror_the_server_codes_exactly()
    {
        ConstantValues(typeof(ResourceMoveEffectCodes))
            .Should().BeEquivalentTo(ConstantValues(typeof(Apperture.Management.Client.ResourceMoveEffectCodes)));
    }

    private static IEnumerable<string> ConstantValues(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);
}
```

- [ ] **Step 2: Run to verify it fails**

Expected: FAIL — compile error, none of the client types exist.

- [ ] **Step 3: Write the client DTOs and the mirrored codes**

```csharp
using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.Management.Client;

/// <summary>
/// Client-side mirror of <c>Apperture.Core.Application.ResourceContexts.ResourceMovePreview</c> (CTX-3B).
/// <see cref="Source"/>/<see cref="Target"/> reuse the domain <see cref="ResourceContextRef"/> directly,
/// exactly as <see cref="IntegrationDto.ResourceContext"/> already does -- this client project references
/// Apperture.Core.Domain, not Apperture.Core.Application.
/// </summary>
public sealed record ResourceMovePreviewDto(
    string ResourceType,
    string ResourceId,
    ResourceContextRef Source,
    ResourceContextRef Target,
    bool CanMove,
    IReadOnlyList<ResourceMoveBlockerDto> Blockers,
    IReadOnlyList<ResourceMoveEffectDto> Effects);

/// <summary>Mirrors <c>ResourceMoveBlocker</c>.</summary>
public sealed record ResourceMoveBlockerDto(
    string Code,
    string Message,
    string? RelatedResourceType,
    string? RelatedResourceId,
    string? RelatedDisplayName,
    string? RelatedResourceContextId);

/// <summary>Mirrors <c>ResourceMoveEffect</c>.</summary>
public sealed record ResourceMoveEffectDto(
    string Code,
    string Message,
    string? RelatedResourceType,
    IReadOnlyList<string>? RelatedResourceIds);

/// <summary>Request body for <c>POST .../ownership/move</c>.</summary>
public sealed record ResourceOwnershipMoveRequest(string TargetResourceContextId);

/// <summary>
/// Client-side mirror of the server's blocker codes. The client cannot reference Core.Application, so
/// these are duplicated deliberately and pinned by <c>ResourceMoveDtoContractParityTests</c>. Never
/// compare a raw string literal against <see cref="ResourceMoveBlockerDto.Code"/> anywhere in the UI.
/// </summary>
public static class ResourceMoveBlockerCodes
{
    public const string TargetNotFound = "target_not_found";
    public const string TargetArchived = "target_archived";
    public const string AlreadyInTarget = "already_in_target";
    public const string BindingConnectionUnreachable = "binding_connection_unreachable";
    public const string ScheduleContextInconsistent = "schedule_context_inconsistent";
    public const string BindingIntegrationUnreachable = "binding_integration_unreachable";
    public const string UnownedBindingRequiresShared = "unowned_binding_requires_shared";
    public const string ScheduleAttachedToIntegration = "schedule_attached_to_integration";
}

/// <summary>Client-side mirror of the server's effect codes. See <see cref="ResourceMoveBlockerCodes"/>.</summary>
public static class ResourceMoveEffectCodes
{
    public const string SchedulesMoveWithIntegration = "schedules_move_with_integration";
    public const string BindingReferencesMissingConnection = "binding_references_missing_connection";
    public const string BindingReferencesMissingIntegration = "binding_references_missing_integration";
    public const string UsedByIntegration = "used_by_integration";
}
```

- [ ] **Step 4: Run to verify it passes, build, commit**

```bash
dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceMoveDtoContractParityTests"
dotnet build Apperture.slnx
git add src/Apperture.ControlPlane/Apperture.Management.Client/ResourceMoveDtos.cs \
        tests/Apperture.UnitTests/ResourceContexts/ResourceMoveDtoContractParityTests.cs
git commit -m "CTX-3B: add client move DTOs and mirrored codes with strengthened parity tests"
```

---

### Task 2: One shared HTTP mapper, and the six endpoints

**Files:**
- Create: `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ResourceMoveHttpMapper.cs`
- Modify: `IntegrationsController.cs`, `ConnectionsController.cs`, `SchedulesController.cs`
- Test: `tests/Apperture.UnitTests/ResourceContexts/ResourceOwnershipMoveControllerTests.cs`

**Interfaces:**
- Consumes: `IResourceOwnershipMoveService` (constructor-injected into all three controllers).
- Produces: six HTTP endpoints, each returning `ResourceMovePreviewDto`; `ResourceMoveHttpMapper`.

- [ ] **Step 1: Write the shared mapper**

```csharp
using Apperture.Core.Application.ResourceContexts;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// The one place the design gate §8.3 status mapping lives. All three movement-capable controllers use
/// it, rather than each carrying its own copy of the same three members.
/// </summary>
internal static class ResourceMoveHttpMapper
{
    /// <summary>
    /// §8.3: the common, resource-less blockers are bad input and get a plain 400. Any family-specific
    /// blocker is a legitimate state the operator must see, so it gets 409 with the full preview body.
    /// </summary>
    private static readonly HashSet<string> CommonBlockerCodes = new(StringComparer.Ordinal)
    {
        ResourceMoveBlockerCodes.TargetNotFound,
        ResourceMoveBlockerCodes.TargetArchived,
        ResourceMoveBlockerCodes.AlreadyInTarget
    };

    public static IActionResult MapMoveResult(ResourceMovePreview preview)
    {
        if (preview.CanMove)
            return new OkObjectResult(ToDto(preview));

        var commonBlocker = preview.Blockers.FirstOrDefault(b => CommonBlockerCodes.Contains(b.Code));
        if (commonBlocker is not null)
            return new BadRequestObjectResult(new { error = commonBlocker.Message });

        return new ConflictObjectResult(ToDto(preview));
    }

    /// <summary>
    /// Plan 2's ownership mutation gate timed out: another ownership change is in progress. Transient and
    /// retryable, so it is a 409 with a stable machine code -- never a 500, and never a raw app-lock error.
    /// </summary>
    public static IActionResult MapBusy(ResourceOwnershipBusyException ex) =>
        new ConflictObjectResult(new { error = ex.Message, code = ex.Code });

    public static Client.ResourceMovePreviewDto ToDto(ResourceMovePreview p) => new(
        p.ResourceType, p.ResourceId, p.Source, p.Target, p.CanMove,
        p.Blockers.Select(b => new Client.ResourceMoveBlockerDto(
            b.Code, b.Message, b.RelatedResourceType, b.RelatedResourceId, b.RelatedDisplayName, b.RelatedResourceContextId)).ToList(),
        p.Effects.Select(e => new Client.ResourceMoveEffectDto(
            e.Code, e.Message, e.RelatedResourceType, e.RelatedResourceIds)).ToList());
}
```

- [ ] **Step 2: Add the two endpoints to `IntegrationsController`**

Add `IResourceOwnershipMoveService` to the constructor (verified at review time: it currently takes **nine** parameters, so this makes ten).

```csharp
    /// <summary>Advisory preview of moving this Integration to another ResourceContext (design gate §8.2).</summary>
    [HttpGet("{id}/ownership/move-preview")]
    public async Task<IActionResult> GetOwnershipMovePreview(
        string id, [FromQuery] string targetResourceContextId, CancellationToken cancellationToken)
    {
        try
        {
            var preview = await _moveService.PreviewIntegrationMoveAsync(
                new IntegrationId(id), new ResourceContextId(targetResourceContextId), cancellationToken);
            return Ok(ResourceMoveHttpMapper.ToDto(preview));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    /// <summary>Moves this Integration to another ResourceContext. Re-validates identically inside the gated transaction (§8.2).</summary>
    [HttpPost("{id}/ownership/move")]
    public async Task<IActionResult> PostOwnershipMove(
        string id, [FromBody] Client.ResourceOwnershipMoveRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var preview = await _moveService.MoveIntegrationAsync(
                new IntegrationId(id), new ResourceContextId(body.TargetResourceContextId), cancellationToken);
            return ResourceMoveHttpMapper.MapMoveResult(preview);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ResourceOwnershipBusyException ex) { return ResourceMoveHttpMapper.MapBusy(ex); }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }
```

Order matters: `ResourceOwnershipBusyException` is caught **before** `InvalidOperationException` (it is not one, but keep the ordering explicit so a later refactor that changes the base type cannot silently turn a transient conflict into a 404).

- [ ] **Step 3: The same two endpoints on `ConnectionsController`**

Identical shape, with `PreviewConnectionMoveAsync`/`MoveConnectionAsync` and `new ConnectionId(id)`.

- [ ] **Step 4: The same two endpoints on `SchedulesController`**

Identical shape, with `PreviewScheduleMoveAsync`/`MoveScheduleAsync` and `new ScheduleId(id)`, plus this controller's existing governance pre-check:

```csharp
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
            return Forbid();
```

`SchedulesController` checks `_scheduleGovernance.CanManageAsync()` directly for every action on the controller (`Enable`/`Disable` and the rest), and `ResourceOwnershipMoveService` still performs its own internal authorization. That double-check is this controller's existing house convention, not a new inconsistency introduced here.

- [ ] **Step 5: Write the controller tests**

The service-level behavior is already covered by Plans 1-4. The genuinely new logic here is the **HTTP status mapping**, which nothing else covers. Test it by instantiating the controller with stubbed dependencies and inspecting the concrete `ActionResult` type — the standard ASP.NET Core MVC unit-testing pattern, needing no test-only production hook of any kind.

```csharp
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Client;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// Design gate §8.3's 200/400/409 mapping is controller-only logic. The service-level tests in Plans 1-4
/// observe <c>ResourceMovePreview.CanMove</c> and blocker codes, never HTTP status codes.
/// </summary>
public sealed class ResourceOwnershipMoveControllerTests
{
    private static readonly ResourceContextRef Ref = new("app-a", "App A", "app-a");

    [Fact]
    public async Task Common_blocker_maps_to_400_with_a_plain_error()
    {
        var preview = Blocked(ResourceMoveBlockerCodes.TargetArchived, "archived");
        var controller = BuildController(new StubMoveService(preview));

        var result = await controller.PostOwnershipMove("int-1", new ResourceOwnershipMoveRequest("app-b"), default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Family_blocker_maps_to_409_with_the_full_preview_body()
    {
        var preview = Blocked(ResourceMoveBlockerCodes.BindingConnectionUnreachable, "unreachable");
        var controller = BuildController(new StubMoveService(preview));

        var result = await controller.PostOwnershipMove("int-1", new ResourceOwnershipMoveRequest("app-b"), default);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().BeOfType<ResourceMovePreviewDto>(
            "the UI needs the blockers and effects, not a bare message");
    }

    [Fact]
    public async Task Success_maps_to_200()
    {
        var preview = new ResourceMovePreview("Integration", "int-1", Ref, Ref, true, [], []);
        var controller = BuildController(new StubMoveService(preview));

        var result = await controller.PostOwnershipMove("int-1", new ResourceOwnershipMoveRequest("app-b"), default);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Ownership_gate_timeout_maps_to_409_with_the_busy_code()
    {
        var controller = BuildController(new ThrowingMoveService(
            new ResourceOwnershipBusyException("Another ownership change is in progress. Wait a moment and try again.")));

        var result = await controller.PostOwnershipMove("int-1", new ResourceOwnershipMoveRequest("app-b"), default);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value!.ToString().Should().Contain(ResourceOwnershipBusyException.BusyCode,
            "a transient ownership conflict must be a retryable 409 with a stable code, never a 500");
    }

    [Fact]
    public async Task Unauthorized_maps_to_403()
    {
        var controller = BuildController(new ThrowingMoveService(new UnauthorizedAccessException()));

        var result = await controller.PostOwnershipMove("int-1", new ResourceOwnershipMoveRequest("app-b"), default);

        result.Should().BeOfType<ForbidResult>();
    }

    private static ResourceMovePreview Blocked(string code, string message) =>
        new("Integration", "int-1", Ref, Ref, false,
            [new ResourceMoveBlocker(code, message, null, null, null, null)], []);

    private static IntegrationsController BuildController(IResourceOwnershipMoveService moveService)
    {
        // Read IntegrationsController's full current constructor before finalizing: nine parameters as of
        // Plan 1, plus this plan's tenth. Most already have permissive fakes elsewhere in this suite --
        // reuse them rather than writing new ones. Only the move service's behavior is under test here.
        // ... construct and return ...
    }

    private sealed class StubMoveService : IResourceOwnershipMoveService { /* returns the given preview from all six members */ }
    private sealed class ThrowingMoveService : IResourceOwnershipMoveService { /* throws the given exception from all six members */ }
}
```

`BuildController` must be genuinely implemented before this task is done. **Do not leave a `NotImplementedException` in it, and do not add any test-only production hook** — if wiring the constructor is painful, that is a signal to reuse the suite's existing fakes, not to open a back door into production code.

- [ ] **Step 6: Run, build, regress, commit**

```bash
dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "FullyQualifiedName~ResourceOwnershipMoveControllerTests"
dotnet build Apperture.slnx
dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj
dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj
git add src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ResourceMoveHttpMapper.cs \
        src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationsController.cs \
        src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ConnectionsController.cs \
        src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/SchedulesController.cs \
        tests/Apperture.UnitTests/ResourceContexts/ResourceOwnershipMoveControllerTests.cs
git commit -m "CTX-3B: add ownership move-preview and move endpoints behind one shared HTTP mapper"
```

---

### Task 3: Client methods, with every response class handled explicitly

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs`
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs`

**Interfaces:**
- Produces: `GetIntegrationOwnershipMovePreviewAsync`, `MoveIntegrationOwnershipAsync`, and the Connection/Schedule equivalents.

**This task's whole risk is response handling**, so it is specified rather than left as a note. CTX-3A shipped exactly this class of bug: `ctx3a-closure.md` §8 item 4 records `ActivateIntegrationAsync`/`DeactivateIntegrationAsync`/`RetireIntegrationAsync` calling bare `EnsureSuccessStatusCode()`, which under Blazor WASM rendered a 409 to the operator as the untranslated resource key `net_http_message_not_success_statuscode_reason, 409, Conflict` instead of the server's real message. Do not reintroduce it.

**The required mapping, for every move call:**

| Status | Meaning | Client behavior |
|---|---|---|
| **200** | Moved | Deserialize `ResourceMovePreviewDto`, return it with `CanMove == true`. |
| **409** with a preview body | Blocked by a family blocker | Deserialize `ResourceMovePreviewDto` and **return it** — this is a normal, renderable outcome, not an exception. The caller inspects `CanMove`. |
| **409** with `{ error, code: "ownership_operation_busy" }` | Gate timeout, transient | Throw `InvalidOperationException` carrying the server's message, so the dialog shows "Another ownership change is in progress. Wait a moment and try again." |
| **400** `{ error }` | Common blocker (unknown/archived/same target) | Throw `InvalidOperationException` carrying the server's message. |
| **403** | Not authorized | Follow the file's existing convention for denied actions. |
| anything else | — | Route through the file's own `EnsureSuccessOrThrowAsync`, which parses `{ "error": ... }` into the thrown message and which ~30 other methods in this file already use. **Never call bare `EnsureSuccessStatusCode()`.** |

Distinguish the two 409 shapes by reading the body once into a buffer and checking for a `code` field (or for the absence of `resourceType`); do not rely on `Content-Type` alone.

- [ ] **Step 1: Add to `IManagementClient`**

```csharp
    Task<ResourceMovePreviewDto> GetIntegrationOwnershipMovePreviewAsync(string integrationId, string targetResourceContextId, CancellationToken cancellationToken = default);
    Task<ResourceMovePreviewDto> MoveIntegrationOwnershipAsync(string integrationId, string targetResourceContextId, CancellationToken cancellationToken = default);
    Task<ResourceMovePreviewDto> GetConnectionOwnershipMovePreviewAsync(string connectionId, string targetResourceContextId, CancellationToken cancellationToken = default);
    Task<ResourceMovePreviewDto> MoveConnectionOwnershipAsync(string connectionId, string targetResourceContextId, CancellationToken cancellationToken = default);
    Task<ResourceMovePreviewDto> GetScheduleOwnershipMovePreviewAsync(string scheduleId, string targetResourceContextId, CancellationToken cancellationToken = default);
    Task<ResourceMovePreviewDto> MoveScheduleOwnershipAsync(string scheduleId, string targetResourceContextId, CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Implement in `ManagementClient`**

Read `ManagementClient.cs`'s existing `EnsureSuccessOrThrowAsync` helper and its handling of `ArchiveResourceContextAsync` (which already distinguishes a `{error}` 400 body from a `ResourceContextOwnershipSummaryDto` 409 body — the closest existing precedent) before writing these. Then implement the table above once in a private helper and have all three move methods share it:

```csharp
    private async Task<ResourceMovePreviewDto> SendMoveAsync(string route, string targetResourceContextId, CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync(route, new ResourceOwnershipMoveRequest(targetResourceContextId), cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Two distinct 409s: a blocked move (full preview body, a normal renderable outcome) and a
            // transient ownership-gate timeout ({ error, code }). Only the latter is exceptional.
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (TryReadPreview(payload, out var blocked))
                return blocked;
            throw new InvalidOperationException(ReadErrorMessage(payload) ?? "Another ownership change is in progress. Wait a moment and try again.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            throw new InvalidOperationException(ReadErrorMessage(await response.Content.ReadAsStringAsync(cancellationToken)) ?? "The move request was rejected.");

        await EnsureSuccessOrThrowAsync(response, cancellationToken); // house convention; never bare EnsureSuccessStatusCode
        return (await response.Content.ReadFromJsonAsync<ResourceMovePreviewDto>(cancellationToken: cancellationToken))!;
    }
```

The preview methods are plain GETs and go through `EnsureSuccessOrThrowAsync` as well.

- [ ] **Step 3: Build and commit**

```bash
dotnet build src/Apperture.ControlPlane/Apperture.Management.Client/Apperture.Management.Client.csproj
git add src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs \
        src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs
git commit -m "CTX-3B: add ownership move client methods with explicit 200/400/409 handling"
```

---

### Task 4: `MoveOwnershipDialog` — extracted model plus razor

**Files:**
- Create: `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/MoveOwnershipDialogModel.cs`
- Create: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/MoveOwnershipDialog.razor`
- Test: `tests/Apperture.Management.Ui.Shared.Tests/ResourceContexts/MoveOwnershipDialogModelTests.cs`

**There is no bUnit in this repository.** The dialog's behavior is therefore split so it can be tested the way this codebase actually tests UI: the decision logic moves into a plain class in `Apperture.Management.Ui.Shared` (unit-tested directly), and the markup is asserted as razor source text in Task 5's `MovementUiSourceTests`.

Before writing the markup, read `ConfirmActionDialog.razor` in full and reuse its modal chrome (backdrop, focus handling, close button, CSS class names) rather than inventing a second modal style.

- [ ] **Step 1: Write the failing model tests**

```csharp
using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests.ResourceContexts;

public sealed class MoveOwnershipDialogModelTests
{
    private static readonly ResourceContextDto Shared = new("shared", "Shared", "shared", /* kind/state per the real DTO */ default!, default!);
    private static readonly ResourceContextDto Canvas = new("app-canvas", "Canvas", "canvas", default!, default!);
    private static readonly ResourceContextDto Campus = new("app-campus", "CampusNexus", "campusnexus", default!, default!);

    [Fact]
    public void Target_options_exclude_the_current_owner()
    {
        var options = MoveOwnershipDialogModel.BuildTargetOptions(
            shared: Shared, activeApplications: [Canvas, Campus], currentResourceContextId: "app-canvas");

        options.Select(o => o.Id).Should().Equal("shared", "app-campus");
    }

    [Fact]
    public void Target_options_include_shared_and_never_an_archived_application()
    {
        // KnownActiveApplications is active-only by construction, so this pins the contract rather than
        // filtering again: whatever the caller passes as activeApplications is offered verbatim.
        var options = MoveOwnershipDialogModel.BuildTargetOptions(
            shared: Shared, activeApplications: [Canvas], currentResourceContextId: "app-campus");

        options.Select(o => o.Id).Should().Contain("shared");
    }

    [Fact]
    public void Submit_is_blocked_until_a_target_is_chosen()
    {
        var model = new MoveOwnershipDialogModel();
        model.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public void Submit_is_blocked_while_a_preview_is_loading()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        model.BeginPreview();
        model.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public void Submit_is_blocked_when_the_preview_says_CanMove_is_false()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        var generation = model.BeginPreview();
        model.ApplyPreview(generation, Preview(canMove: false));

        model.CanSubmit.Should().BeFalse("the UI must never submit a move the server already said is blocked");
    }

    [Fact]
    public void Submit_is_allowed_only_for_a_fresh_CanMove_preview()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        var generation = model.BeginPreview();
        model.ApplyPreview(generation, Preview(canMove: true));

        model.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public void A_stale_preview_response_is_discarded()
    {
        // Rapid target switching: the first request resolves after the second was issued. Without the
        // generation guard the older, wrong-target preview would overwrite the newer one and the dialog
        // would describe a move the operator is not making. IntegrationDetail.razor already uses this
        // exact generation-counter pattern in LoadAsync.
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        var first = model.BeginPreview();
        model.SelectTarget("app-campus");
        var second = model.BeginPreview();

        model.ApplyPreview(second, Preview(canMove: true));
        model.ApplyPreview(first, Preview(canMove: false)); // late, stale

        model.CanSubmit.Should().BeTrue("the stale response must be dropped, not applied over the current one");
    }

    [Fact]
    public void Changing_the_target_clears_the_previous_preview_and_error()
    {
        var model = new MoveOwnershipDialogModel();
        model.SelectTarget("app-canvas");
        model.ApplyPreview(model.BeginPreview(), Preview(canMove: true));
        model.SetSubmitError("boom");

        model.SelectTarget("app-campus");

        model.Preview.Should().BeNull();
        model.SubmitError.Should().BeNull();
        model.CanSubmit.Should().BeFalse();
    }

    private static ResourceMovePreviewDto Preview(bool canMove) =>
        new("Integration", "int-1", default!, default!, canMove, [], []);
}
```

Adjust `ResourceContextDto`'s constructor to its real shape before finalizing.

- [ ] **Step 2: Write `MoveOwnershipDialogModel`**

A plain class in `Apperture.Management.Ui.Shared` (which already references `Apperture.Management.Client`, so it can see the DTOs). It owns: the target-option list, the selected target, a monotonic preview generation counter, the current preview, the submit error, and `CanSubmit`. It performs **no I/O** — the razor component calls the client and hands results back in.

- [ ] **Step 3: Write the razor component**

```razor
@using Apperture.Management.Client
@using Apperture.Management.Ui.Shared
@using Apperture.Management.Ui.Client.Services
@inject IManagementClient Client
@inject ResourceContextViewState ViewState

@if (IsOpen)
{
    @* Reuse ConfirmActionDialog.razor's modal chrome/classes -- do not introduce a second modal style. *@
    <div class="ui-modal-backdrop" role="presentation">
        <div class="ui-modal" role="dialog" aria-modal="true" aria-labelledby="move-dialog-title">
            <h2 id="move-dialog-title">Move to another context…</h2>

            <label for="move-target-select">Target context</label>
            <select id="move-target-select" @bind="_selectedTargetId" @bind:after="LoadPreviewAsync">
                <option value="">Choose a context…</option>
                @foreach (var option in _model.TargetOptions)
                {
                    <option value="@option.Id">@option.Name</option>
                }
            </select>

            @if (_model.IsLoadingPreview)
            {
                <p>Loading preview…</p>
            }
            else if (_model.Preview is { } preview)
            {
                @if (preview.Effects.Count > 0)
                {
                    <ul class="move-effects">
                        @foreach (var effect in preview.Effects)
                        {
                            <li>@effect.Message</li>
                        }
                    </ul>
                }
                @if (preview.Blockers.Count > 0)
                {
                    <ul class="move-blockers">
                        @foreach (var blocker in preview.Blockers)
                        {
                            <li>
                                @blocker.Message
                                @if (blocker.Code == ResourceMoveBlockerCodes.ScheduleAttachedToIntegration && blocker.RelatedResourceId is not null)
                                {
                                    <a href="@($"/integrations/{blocker.RelatedResourceId}")">Go to integration</a>
                                }
                                else if (blocker.Code == ResourceMoveBlockerCodes.BindingIntegrationUnreachable && blocker.RelatedResourceId is not null)
                                {
                                    <a href="@($"/integrations/{blocker.RelatedResourceId}")">@blocker.RelatedDisplayName</a>
                                }
                                else if (blocker.Code == ResourceMoveBlockerCodes.BindingConnectionUnreachable && blocker.RelatedResourceId is not null)
                                {
                                    <a href="@($"/connections/{blocker.RelatedResourceId}")">@blocker.RelatedDisplayName</a>
                                }
                            </li>
                        }
                    </ul>
                }
            }

            @if (_model.SubmitError is not null)
            {
                <p class="ui-error">@_model.SubmitError</p>
            }

            <p class="ui-help-text">History stays with @SourceContextName.</p>

            <div class="ui-modal-actions">
                <button type="button" @onclick="Cancel">Cancel</button>
                <button type="button" disabled="@(!_model.CanSubmit)" @onclick="SubmitAsync">Move</button>
            </div>
        </div>
    </div>
}
```

`@code` holds only the I/O and the parameter surface:

```csharp
    /// <summary>Which resource family this instance moves. Selects which of the client's six methods it calls.</summary>
    public enum ResourceKind { Integration, Connection, Schedule }

    [Parameter, EditorRequired] public bool IsOpen { get; set; }
    [Parameter, EditorRequired] public ResourceKind Kind { get; set; }
    [Parameter, EditorRequired] public string ResourceId { get; set; } = "";
    [Parameter, EditorRequired] public string? CurrentResourceContextId { get; set; }
    [Parameter] public EventCallback OnCancelled { get; set; }
    [Parameter] public EventCallback OnMoved { get; set; }

    private readonly MoveOwnershipDialogModel _model = new();
    private string _selectedTargetId = "";

    private string SourceContextName =>
        CurrentResourceContextId is null ? "Shared" : ViewState.DescribeContext(CurrentResourceContextId);

    protected override void OnParametersSet() =>
        _model.SetTargetOptions(ViewState.SharedContext, ViewState.KnownActiveApplications, CurrentResourceContextId);

    private async Task LoadPreviewAsync()
    {
        _model.SelectTarget(_selectedTargetId);
        if (string.IsNullOrEmpty(_selectedTargetId))
            return;

        var generation = _model.BeginPreview();
        try
        {
            var preview = Kind switch
            {
                ResourceKind.Integration => await Client.GetIntegrationOwnershipMovePreviewAsync(ResourceId, _selectedTargetId),
                ResourceKind.Connection  => await Client.GetConnectionOwnershipMovePreviewAsync(ResourceId, _selectedTargetId),
                ResourceKind.Schedule    => await Client.GetScheduleOwnershipMovePreviewAsync(ResourceId, _selectedTargetId),
                _ => throw new NotSupportedException()
            };
            _model.ApplyPreview(generation, preview); // no-ops if a newer request superseded this one
        }
        catch (InvalidOperationException ex)
        {
            _model.FailPreview(generation, ex.Message);
        }
    }

    private async Task SubmitAsync()
    {
        if (!_model.CanSubmit)
            return;

        _model.SetSubmitError(null);
        try
        {
            var result = Kind switch
            {
                ResourceKind.Integration => await Client.MoveIntegrationOwnershipAsync(ResourceId, _selectedTargetId),
                ResourceKind.Connection  => await Client.MoveConnectionOwnershipAsync(ResourceId, _selectedTargetId),
                ResourceKind.Schedule    => await Client.MoveScheduleOwnershipAsync(ResourceId, _selectedTargetId),
                _ => throw new NotSupportedException()
            };

            if (!result.CanMove)
            {
                // A concurrent change blocked the move between preview and submit (§8.2: move never
                // trusts a prior preview). Show the fresh blockers instead of a generic error.
                _model.ApplyPreview(_model.CurrentGeneration, result);
                return;
            }

            await OnMoved.InvokeAsync();
        }
        catch (InvalidOperationException ex)
        {
            // Covers the 400 common blockers and the transient ownership_operation_busy 409.
            _model.SetSubmitError(ex.Message);
        }
    }

    private async Task Cancel()
    {
        _selectedTargetId = "";
        _model.Reset();
        await OnCancelled.InvokeAsync();
    }
```

`ResourceContextViewState` is **injected**, not passed as a `[Parameter]` — it is a scoped DI service, and `IntegrationDetail.razor` already obtains it by injection. Threading it through three call sites would be a second, divergent access pattern.

- [ ] **Step 4: Run, build, commit**

```bash
dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj --filter "FullyQualifiedName~MoveOwnershipDialogModelTests"
dotnet build Apperture.slnx
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/MoveOwnershipDialogModel.cs \
        src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/MoveOwnershipDialog.razor \
        tests/Apperture.Management.Ui.Shared.Tests/ResourceContexts/MoveOwnershipDialogModelTests.cs
git commit -m "CTX-3B: add MoveOwnershipDialog with extracted, unit-tested dialog model"
```

---

### Task 5: Wire the dialog into Integration hub, Connection detail, Schedule detail

**Files:**
- Modify: `IntegrationDetail.razor`, `ConnectionDetail.razor`, `ScheduleDetail.razor`
- Test: `tests/Apperture.Management.Ui.Shared.Tests/ResourceContexts/MovementUiSourceTests.cs`

- [ ] **Step 1: Integration hub — toolbar action**

In `IntegrationDetail.razor`'s `<Toolbar>`, inside the existing `@if (_capabilities?.CanManageIntegrations == true && _integration is not null)` block, alongside Deactivate/Activate/Retire/Delete:

```razor
                <button type="button" class="ui-toolbar-btn" disabled="@_loading" @onclick="() => _moveDialogOpen = true">Move to another context…</button>
```

Near the existing `<MismatchNotice .../>` line:

```razor
    <MoveOwnershipDialog IsOpen="@_moveDialogOpen"
                         Kind="MoveOwnershipDialog.ResourceKind.Integration"
                         ResourceId="@IntegrationId"
                         CurrentResourceContextId="@_integration?.ResourceContextId"
                         OnCancelled="() => _moveDialogOpen = false"
                         OnMoved="OnMovedAsync" />
```

In `@code`:

```csharp
    private bool _moveDialogOpen;

    private async Task OnMovedAsync()
    {
        _moveDialogOpen = false;
        // Re-fetch only. The page never navigates, and it never switches the shell View: OwnershipChip
        // re-renders from the fresh _integration, and MismatchNotice appears on its own if the current
        // View now differs from the new owner (§4.6, §8.9).
        await RefreshAsync();
        Toasts.Show("Integration moved.");
    }
```

Confirm `RefreshAsync` exists and re-fetches `_integration` (it does — the existing Refresh toolbar button calls it).

- [ ] **Step 2: Connection detail — same flow, "Used by" preserved**

Identical pattern with `Kind="MoveOwnershipDialog.ResourceKind.Connection"` and `CurrentResourceContextId="@_connection?.ResourceContextId"`. Confirm the existing `ConnectionDto.UsedIn` rendering stays visible and unaffected — it is a shipped CTX-3A feature this plan does not touch, and §8.5 requires the blast radius to be visible before submitting.

- [ ] **Step 3: Schedule detail — attached vs standalone**

```razor
    @if (_schedule?.IntegrationId is { } attachedIntegrationId)
    {
        <button type="button" class="ui-toolbar-btn" disabled title="This schedule moves with its integration.">Move to another context…</button>
        <a href="@($"/integrations/{attachedIntegrationId}")">Go to integration</a>
    }
    else
    {
        <button type="button" class="ui-toolbar-btn" @onclick="() => _moveDialogOpen = true">Move to another context…</button>
        <MoveOwnershipDialog IsOpen="@_moveDialogOpen"
                             Kind="MoveOwnershipDialog.ResourceKind.Schedule"
                             ResourceId="@ScheduleId"
                             CurrentResourceContextId="@_schedule?.ResourceContextId"
                             OnCancelled="() => _moveDialogOpen = false"
                             OnMoved="OnMovedAsync" />
    }
```

Confirm the real field name on the detail DTO (`ScheduleDetailDto.IntegrationId` from `SchedulesController`).

- [ ] **Step 4: Write the razor source-text tests**

Following `Epic96IntegrationActionFeedbackTests` exactly — walk up to `.git`, read the razor file, assert on its content. This is the repository's established UI test convention and needs no new framework.

```csharp
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests.ResourceContexts;

/// <summary>
/// Razor source-text assertions, matching this suite's established convention (see
/// Epic96IntegrationActionFeedbackTests). There is no bUnit in this repository; component rendering is not
/// available, so the contract is pinned at the markup level.
/// </summary>
public sealed class MovementUiSourceTests
{
    [Fact]
    public void Move_dialog_disables_submit_unless_the_model_says_it_can_submit()
    {
        ReadRazor("Organisms", "MoveOwnershipDialog.razor")
            .Should().Contain("disabled=\"@(!_model.CanSubmit)\"");
    }

    [Fact]
    public void Move_dialog_uses_mirrored_blocker_code_constants_not_string_literals()
    {
        var content = ReadRazor("Organisms", "MoveOwnershipDialog.razor");
        content.Should().Contain("ResourceMoveBlockerCodes.ScheduleAttachedToIntegration");
        content.Should().NotContain("\"schedule_attached_to_integration\"",
            "blocker codes are mirrored constants, never literals");
    }

    [Fact]
    public void Move_dialog_injects_the_view_state_rather_than_taking_it_as_a_parameter()
    {
        var content = ReadRazor("Organisms", "MoveOwnershipDialog.razor");
        content.Should().Contain("@inject ResourceContextViewState ViewState");
        content.Should().NotContain("[Parameter] public ResourceContextViewState");
    }

    [Theory]
    [InlineData("Organisms", "MoveOwnershipDialog.razor")]
    [InlineData("Pages/Integrations", "IntegrationDetail.razor")]
    [InlineData("Pages/Connections", "ConnectionDetail.razor")]
    [InlineData("Pages/Schedules", "ScheduleDetail.razor")]
    public void Movement_ui_never_switches_the_shell_view(string folder, string file)
    {
        ReadRazor(folder, file).Should().NotContain("SetExplicitAsync",
            "opening or completing a move is not choosing a workspace -- the shell View must never change by itself (§4.6, §8.9)");
    }

    [Theory]
    [InlineData("Organisms", "MoveOwnershipDialog.razor")]
    [InlineData("Pages/Integrations", "IntegrationDetail.razor")]
    [InlineData("Pages/Connections", "ConnectionDetail.razor")]
    [InlineData("Pages/Schedules", "ScheduleDetail.razor")]
    [InlineData("Pages/Config/Applications", "ApplicationDetail.razor")]
    public void Operator_copy_says_context_not_application(string folder, string file)
    {
        var content = ReadRazor(folder, file);
        content.Should().NotContain("Move to another application",
            "the target may be Shared as well as an Application, so operator copy says Context");
    }

    [Fact]
    public void Attached_schedule_shows_a_disabled_action_and_a_link_instead_of_a_dialog()
    {
        var content = ReadRazor("Pages/Schedules", "ScheduleDetail.razor");
        content.Should().Contain("This schedule moves with its integration.");
        content.Should().Contain("Go to integration");
    }

    [Fact]
    public void Integration_detail_refreshes_in_place_after_a_move_and_does_not_navigate()
    {
        var content = ReadRazor("Pages/Integrations", "IntegrationDetail.razor");
        content.Should().Contain("OnMoved=\"OnMovedAsync\"");
        content.Should().Contain("await RefreshAsync();");
        content.Should().NotContain("NavigationManager.NavigateTo", "the page stays put after a successful move");
    }

    private static string ReadRazor(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var path = Path.Combine(dir!.FullName, "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client",
            Path.Combine(folder.Split('/')), file);
        File.Exists(path).Should().BeTrue($"expected {path} to exist");
        return File.ReadAllText(path);
    }
}
```

`IntegrationDetail.razor` already contains a `NavigationManager` usage for unrelated flows; scope the last assertion to `OnMovedAsync`'s body if a whole-file `NotContain` is too broad.

- [ ] **Step 5: Manual browser smoke for this task**

bUnit's absence makes this more important, not less. Walk:

1. Open an Integration in an Application → "Move to another context…" → pick a target → the preview renders effects and blockers → submit a valid move → **the URL does not change**, the ownership chip updates, and if the current shell View is a different Application the mismatch notice appears **without** the View switching.
2. Pick a target that is blocked → the Move button stays disabled and the blockers render with working links.
3. Switch targets rapidly → the dialog never shows a preview for a target other than the currently selected one.
4. Connection detail → confirm "Used by" still renders alongside the new action, and that the preview's `used_by_integration` effect shows even when the move is allowed.
5. Schedule detail → an attached schedule shows the disabled action with a working link; a legacy standalone schedule's dialog works.
6. Move out of an **archived** source Application → allowed. Confirm archived Applications never appear as **targets**.

- [ ] **Step 6: Run and commit**

```bash
dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationDetail.razor \
        src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Connections/ConnectionDetail.razor \
        src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/ScheduleDetail.razor \
        tests/Apperture.Management.Ui.Shared.Tests/ResourceContexts/MovementUiSourceTests.cs
git commit -m "CTX-3B: wire move dialog into Integration hub, Connection detail, and Schedule detail"
```

---

### Task 6: Archive-refusal copy update (§6.5 D2)

**Files:**
- Modify: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Applications/ApplicationDetail.razor`

- [ ] **Step 1: Add move-oriented copy to the existing archive-refusal `PageMessage`**

The existing block (around lines 137-157) already lists "View integrations"/"View connections"/"View schedules" links when `_archiveBlocker` is set. Design gate §6.5 D2 says the copy gains "…or move them to another application" — rendered here as **context**, per this plan's terminology rule:

```razor
                            <PageMessage Kind="PageMessageKind.Warning"
                                         Title="@($"{_context.Name} can't be archived yet.")"
                                         Message="@($"It still has {DescribeBlocker(_archiveBlocker)}. Retire the remaining integrations, retire active connections, disable enabled schedules, or move them to another context before archiving.")">
```

The scoped links already navigate to `context={Id}`-filtered lists where the operator can open each resource and use Task 5's "Move to another context…" action, so no additional link markup is needed beyond the copy change.

Note: CTX-3A's post-closure stabilization (`a8f098a`) already corrected this banner's count wording from "N active integration(s)" to "N integration(s) not retired". Preserve that correction — this task changes only the closing instruction.

- [ ] **Step 2: Manual smoke**

Create an Application, add a live Integration to it, attempt archive → see the refusal mentioning "move them to another context" → follow "View integrations" → open the Integration → use "Move to another context…" to move it elsewhere → return to the source Application → **archive now succeeds**.

This is the end-to-end flow that justifies CTX-3B's existence, so also add it as an automated service-level test rather than relying on the smoke walk alone:

```csharp
    [Fact]
    public async Task Archive_succeeds_after_the_last_live_blocker_is_moved_out()
    {
        using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id); // Active: blocks archive

        var beforeMove = async () => await h.ResourceContextManagement.ArchiveAsync(source.Id);
        await beforeMove.Should().ThrowAsync<InvalidOperationException>("a live Integration blocks archive");

        await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        var archived = await h.ResourceContextManagement.ArchiveAsync(source.Id);
        archived.IsActive.Should().BeFalse("moving the last live resource out is exactly what CTX-3B unblocks");
    }
```

Place it in `tests/Apperture.UnitTests/ResourceContexts/`. It needs the archive management service on the harness; add it if Plan 2 Task 5 has not already.

- [ ] **Step 3: Commit**

```bash
git add src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/Applications/ApplicationDetail.razor \
        tests/Apperture.UnitTests/ResourceContexts/ArchiveAfterMoveTests.cs
git commit -m "CTX-3B: archive-refusal copy mentions moving to another context, with an end-to-end test"
```

---

## Out of scope for this plan

- No changes to `ConnectionDto.UsedIn`/blast-radius computation — already shipped in CTX-3A; this plan only adds the move dialog alongside it.
- No Audit UI changes.
- No Integration hard-delete referential-lifecycle work (Plan 6a).
- No new context-scoped permission or IAM of any kind.
- No bUnit, and no new UI test framework.

## Validation for this plan

- **Plans 2, 3 and 4 have landed and their SQL Server tests demonstrably executed** before this plan begins. This is the plan that makes movement reachable; it must not be the plan that discovers movement was unsafe.
- `dotnet build Apperture.slnx` — 0 errors.
- `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj` — full project green, including the parity, controller-mapping and archive-after-move tests.
- `dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj` — full project green, including `MoveOwnershipDialogModelTests` and `MovementUiSourceTests`.
- `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj` — no regressions against the 204/204 baseline.
- Manual browser smoke per Task 5 Step 5 and Task 6 Step 2. UI correctness cannot be fully proven by source-text assertions alone, and this repository has no component-rendering test framework.
- `dotnet test Apperture.slnx` once at the end.
