using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Security.Trust;
using Apperture.Management.Api.Controllers;
using Apperture.Shared.Kernel;
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
        var controller = new EnvironmentsController(
            new StubAuthorizationEvaluationService(allowed: true),
            new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            new StubEnvironmentContext("local"));

        var result = await controller.GetAll(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var data = (IReadOnlyList<EnvironmentSummaryDto>)ok.Value!;
        data.Should().ContainSingle(e => e.EnvironmentId == "local");
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
    public async Task RuntimeInstancesController_WhenNotAuthorized_Returns403()
    {
        var controller = new RuntimeInstancesController(
            new StubRuntimeIdentityService(new RuntimeIdentity(
                "rt-1",
                "Runtime 1",
                "PublicKey",
                "pk",
                null,
                ["cap.a"],
                DateTimeOffset.UtcNow)),
            new StubAuthorizationEvaluationService(allowed: false),
            new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            new StubEnvironmentContext("local"));

        var result = await controller.GetCapabilities("rt-1", CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
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
        var identity = new RuntimeIdentity(
            "rt-1",
            "Runtime 1",
            "PublicKey",
            "pk",
            null,
            ["cap.a", "cap.b"],
            DateTimeOffset.UtcNow);

        var auth = new StubAuthorizationEvaluationService(allowed: true);
        var controller = new RuntimeInstancesController(
            new StubRuntimeIdentityService(identity),
            auth,
            new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            new StubEnvironmentContext("local"));

        var result = await controller.GetCapabilities("rt-1", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var dto = (RuntimeCapabilitiesDto)ok.Value!;
        dto.RuntimeId.Should().Be("rt-1");
        dto.DeclaredScopes.Should().BeEquivalentTo(new[] { "cap.a", "cap.b" });

        auth.LastEvaluatedPermission!.Action.Should().Be(ActionConstants.RuntimeRead);
        auth.LastEvaluatedResource!.ScopeKey.Should().Be(ScopeKeyConstants.RuntimeInstance("local", "rt-1"));
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
        private readonly RuntimeIdentity? _runtimeIdentity;

        public StubRuntimeIdentityService(RuntimeIdentity? runtimeIdentity) => _runtimeIdentity = runtimeIdentity;

        public ValueTask<RuntimeIdentity?> GetByIdAsync(string runtimeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_runtimeIdentity);

        public ValueTask<RuntimeTrustValidationResult> ValidateAsync(string runtimeId, string presentedCredential, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeTrustValidationResult(false, FailureReason: "not implemented"));

        public ValueTask<EnrollmentResult> RequestCredentialRolloverAsync(RuntimeCredentialRolloverRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new EnrollmentResult(request.RuntimeId, false, "not implemented"));
    }
}

