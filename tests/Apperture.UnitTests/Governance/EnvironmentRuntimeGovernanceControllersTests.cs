using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Runtime;
using Apperture.Core.Security.Trust;
using Apperture.Management.Api.Controllers;
using Apperture.Shared.Kernel;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class EnvironmentRuntimeGovernanceControllersTests
{
    [Fact]
    public async Task EnvironmentsController_WhenNoActor_Returns401()
    {
        var controller = new EnvironmentsController(
            new StubAuthorizationEvaluationService(allowed: true),
            new StubPrincipalContext(actor: null),
            new StubEnvironmentContext("local"));

        var result = await controller.GetAll(CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task EnvironmentsController_WhenNotAuthorized_Returns403()
    {
        var controller = new EnvironmentsController(
            new StubAuthorizationEvaluationService(allowed: false),
            new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            new StubEnvironmentContext("local"));

        var result = await controller.GetAll(CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task EnvironmentsController_WhenAuthorized_Returns200WithCurrentEnvironment()
    {
        var auth = new StubAuthorizationEvaluationService(allowed: true);
        var controller = new EnvironmentsController(
            auth,
            new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            new StubEnvironmentContext("local"));

        var result = await controller.GetAll(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var data = (IReadOnlyList<EnvironmentSummaryDto>)ok.Value!;
        data.Should().ContainSingle(e => e.EnvironmentId == "local");

        auth.LastEvaluatedPermission!.Action.Should().Be(ActionConstants.EnvironmentRead);
        auth.LastEvaluatedResource!.ResourceType.Should().Be(ResourceTypeConstants.Environment);
        auth.LastEvaluatedResource.ScopeKey.Should().Be(ScopeKeyConstants.Environment("local"));
    }

    [Fact]
    public async Task RuntimeInstancesController_WhenNoActor_Returns401()
    {
        var controller = new RuntimeInstancesController(
            new StubRuntimeIdentityService(runtimeIdentity: null),
            new StubAuthorizationEvaluationService(allowed: true),
            new StubPrincipalContext(actor: null),
            new StubEnvironmentContext("local"));

        var result = await controller.GetCapabilities("rt-1", CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task RuntimeInstancesController_GetAll_WhenNoActor_Returns401()
    {
        var runtime = new StubRuntimeIdentityService(runtimeIdentity: null);
        var controller = new RuntimeInstancesController(
            runtime,
            new StubAuthorizationEvaluationService(allowed: true),
            new StubPrincipalContext(actor: null),
            new StubEnvironmentContext("local"));

        var result = await controller.GetAll(CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedResult>();
        runtime.GetAllCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RuntimeInstancesController_WhenNotAuthorized_Returns403()
    {
        var runtime = new StubRuntimeIdentityService(new RuntimeIdentity(
            "rt-1",
            "Runtime 1",
            "PublicKey",
            "pk",
            null,
            [RuntimeCapabilityCatalog.InspectionCapabilities],
            DateTimeOffset.UtcNow));
        var controller = new RuntimeInstancesController(
            runtime,
            new StubAuthorizationEvaluationService(allowed: false),
            new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            new StubEnvironmentContext("local"));

        var result = await controller.GetCapabilities("rt-1", CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
        runtime.GetByIdCallCount.Should().Be(0); // deny/no-leak: do not resolve runtime existence
    }

    [Fact]
    public async Task RuntimeInstancesController_WhenRuntimeMissing_Returns404()
    {
        var controller = new RuntimeInstancesController(
            new StubRuntimeIdentityService(runtimeIdentity: null),
            new StubAuthorizationEvaluationService(allowed: true),
            new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            new StubEnvironmentContext("local"));

        var result = await controller.GetCapabilities("rt-1", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task RuntimeInstancesController_WhenAuthorized_ReturnsDeclaredScopes()
    {
        var runtime = new StubRuntimeIdentityService(new RuntimeIdentity(
            "rt-1",
            "Runtime 1",
            "PublicKey",
            "pk",
            null,
            [RuntimeCapabilityCatalog.InspectionCapabilities, RuntimeCapabilityCatalog.ControlShutdown],
            DateTimeOffset.UtcNow));

        var identity = new RuntimeIdentity(
            "rt-1",
            "Runtime 1",
            "PublicKey",
            "pk",
            null,
            [RuntimeCapabilityCatalog.InspectionCapabilities, RuntimeCapabilityCatalog.ControlShutdown],
            DateTimeOffset.UtcNow);

        var auth = new StubAuthorizationEvaluationService(allowed: true);
        var controller = new RuntimeInstancesController(
            runtime,
            auth,
            new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            new StubEnvironmentContext("local"));

        var result = await controller.GetCapabilities("rt-1", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var dto = (RuntimeCapabilitiesDto)ok.Value!;
        dto.RuntimeId.Should().Be("rt-1");
        dto.Capabilities.Select(c => c.CapabilityId).Should().BeEquivalentTo(new[]
        {
            RuntimeCapabilityCatalog.InspectionCapabilities,
            RuntimeCapabilityCatalog.ControlShutdown
        });
        dto.Capabilities.Should().ContainSingle(c =>
            c.CapabilityId == RuntimeCapabilityCatalog.InspectionCapabilities &&
            c.Kind == RuntimeCapabilityKind.Inspection);
        dto.Capabilities.Should().ContainSingle(c =>
            c.CapabilityId == RuntimeCapabilityCatalog.ControlShutdown &&
            c.Kind == RuntimeCapabilityKind.Control);
        runtime.GetByIdCallCount.Should().Be(1);

        auth.LastEvaluatedPermission!.Action.Should().Be(ActionConstants.RuntimeRead);
        auth.LastEvaluatedResource!.ScopeKey.Should().Be(ScopeKeyConstants.RuntimeInstance("local", "rt-1"));
    }

    [Fact]
    public async Task RuntimeInstancesController_WhenAuthorized_FiltersUnknownCapabilities()
    {
        var runtime = new StubRuntimeIdentityService(new RuntimeIdentity(
            "rt-1",
            "Runtime 1",
            "PublicKey",
            "pk",
            null,
            [RuntimeCapabilityCatalog.InspectionCapabilities, "unknown.runtime.capability"],
            DateTimeOffset.UtcNow));

        var auth = new StubAuthorizationEvaluationService(allowed: true);
        var controller = new RuntimeInstancesController(
            runtime,
            auth,
            new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            new StubEnvironmentContext("local"));

        var result = await controller.GetCapabilities("rt-1", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var dto = (RuntimeCapabilitiesDto)ok.Value!;

        dto.RuntimeId.Should().Be("rt-1");
        dto.Capabilities.Select(c => c.CapabilityId).Should().BeEquivalentTo(new[]
        {
            RuntimeCapabilityCatalog.InspectionCapabilities
        });
        runtime.GetByIdCallCount.Should().Be(1);

        auth.LastEvaluatedPermission!.Action.Should().Be(ActionConstants.RuntimeRead);
        auth.LastEvaluatedResource!.ScopeKey.Should().Be(ScopeKeyConstants.RuntimeInstance("local", "rt-1"));
    }

    [Fact]
    public async Task RuntimeInstancesController_GetAll_WhenNotAuthorized_Returns403_AndDoesNotLeak()
    {
        var runtime = new StubRuntimeIdentityService(new[]
        {
            new RuntimeIdentity(
                "rt-1",
                "Runtime 1",
                "PublicKey",
                "pk",
                null,
                [RuntimeCapabilityCatalog.InspectionCapabilities],
                DateTimeOffset.UtcNow),
            new RuntimeIdentity(
                "rt-2",
                "Runtime 2",
                "PublicKey",
                "pk",
                null,
                [RuntimeCapabilityCatalog.ControlShutdown],
                DateTimeOffset.UtcNow)
        });

        var controller = new RuntimeInstancesController(
            runtime,
            new StubAuthorizationEvaluationService(allowed: false),
            new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            new StubEnvironmentContext("local"));

        var result = await controller.GetAll(CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
        runtime.GetAllCallCount.Should().Be(0); // deny/no-leak
    }

    [Fact]
    public async Task RuntimeInstancesController_GetAll_WhenAuthorized_ReturnsDurableDeclaredMetadataList()
    {
        var runtime = new StubRuntimeIdentityService(new[]
        {
            new RuntimeIdentity(
                "rt-1",
                "Runtime 1",
                "PublicKey",
                "pk",
                null,
                [RuntimeCapabilityCatalog.InspectionCapabilities],
                DateTimeOffset.UtcNow),
            new RuntimeIdentity(
                "rt-2",
                "Runtime 2",
                "PublicKey",
                "pk",
                null,
                [RuntimeCapabilityCatalog.ControlShutdown],
                DateTimeOffset.UtcNow)
        });

        var auth = new StubAuthorizationEvaluationService(allowed: true);
        var controller = new RuntimeInstancesController(
            runtime,
            auth,
            new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            new StubEnvironmentContext("local"));

        var result = await controller.GetAll(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var dtos = (IReadOnlyList<RuntimeCapabilitiesDto>)ok.Value!;

        dtos.Select(d => d.RuntimeId).Should().BeEquivalentTo(new[] { "rt-1", "rt-2" });
        dtos.First(d => d.RuntimeId == "rt-1").Capabilities.Should().ContainSingle(c =>
            c.CapabilityId == RuntimeCapabilityCatalog.InspectionCapabilities &&
            c.Kind == RuntimeCapabilityKind.Inspection);
        runtime.GetAllCallCount.Should().Be(1);

        auth.LastEvaluatedPermission!.Action.Should().Be(ActionConstants.RuntimeRead);
        auth.LastEvaluatedResource!.ScopeKey.Should().Be(ScopeKeyConstants.Environment("local"));
    }

    private sealed class StubEnvironmentContext : IEnvironmentContext
    {
        public string EnvironmentId { get; }

        public StubEnvironmentContext(string environmentId) => EnvironmentId = environmentId;
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        private readonly IActor? _actor;

        public StubPrincipalContext(IActor? actor) => _actor = actor;

        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_actor);
    }

    private sealed class StubAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        private readonly bool _allowed;

        public PermissionDescriptor? LastEvaluatedPermission { get; private set; }
        public ResourceDescriptor? LastEvaluatedResource { get; private set; }

        public StubAuthorizationEvaluationService(bool allowed) => _allowed = allowed;

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            LastEvaluatedPermission = context.Permission;
            LastEvaluatedResource = context.Resource;
            return ValueTask.FromResult(_allowed);
        }
    }

    private sealed class StubRuntimeIdentityService : IRuntimeIdentityService
    {
        private readonly IReadOnlyDictionary<string, RuntimeIdentity> _byId;
        private readonly IReadOnlyList<RuntimeIdentity> _all;

        public int GetByIdCallCount { get; private set; }
        public int GetAllCallCount { get; private set; }

        public StubRuntimeIdentityService(RuntimeIdentity? runtimeIdentity)
        {
            _all = runtimeIdentity is null ? Array.Empty<RuntimeIdentity>() : new[] { runtimeIdentity };
            _byId = _all.ToDictionary(x => x.Id, x => x);
        }

        public StubRuntimeIdentityService(IReadOnlyList<RuntimeIdentity> runtimeIdentities)
        {
            _all = runtimeIdentities ?? Array.Empty<RuntimeIdentity>();
            _byId = _all.ToDictionary(x => x.Id, x => x);
        }

        public ValueTask<RuntimeIdentity?> GetByIdAsync(string runtimeId, CancellationToken cancellationToken = default)
        {
            GetByIdCallCount++;
            _byId.TryGetValue(runtimeId, out var identity);
            return ValueTask.FromResult(identity);
        }

        public ValueTask<IReadOnlyList<RuntimeIdentity>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            GetAllCallCount++;
            return ValueTask.FromResult(_all);
        }

        public ValueTask<RuntimeTrustValidationResult> ValidateAsync(string runtimeId, string presentedCredential, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeTrustValidationResult(false, FailureReason: "not implemented"));

        public ValueTask<EnrollmentResult> RequestCredentialRolloverAsync(RuntimeCredentialRolloverRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new EnrollmentResult(request.RuntimeId, false, "not implemented"));
    }
}

