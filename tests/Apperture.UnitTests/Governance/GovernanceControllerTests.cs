using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// Governance descriptor and effective-permissions API auth behavior.
/// </summary>
public sealed class GovernanceControllerTests
{
    [Fact]
    public async Task GetDescriptors_WhenNoActor_Returns401()
    {
        var controller = new GovernanceController(
            new InMemoryResourceDescriptorRegistry(),
            new NullPrincipalContext(),
            new AllowAllEvaluation(),
            new StubPrincipalToUserResolver(),
            new StubRoleProvider());
        var result = await controller.GetDescriptors(CancellationToken.None);
        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task GetDescriptors_WhenNotAuthorized_Returns403()
    {
        var controller = new GovernanceController(
            new InMemoryResourceDescriptorRegistry(),
            new StubPrincipalContext(),
            new DenyAllEvaluation(),
            new StubPrincipalToUserResolver(),
            new StubRoleProvider());
        var result = await controller.GetDescriptors(CancellationToken.None);
        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetDescriptors_WhenAuthorized_Returns200WithData()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        registry.RegisterResource(new ResourceDescriptor("User", "instance"), ResourceFamily.Core);
        registry.RegisterPermission(
            new PermissionDescriptor("user.read", new ResourceDescriptor("User", "instance")),
            ResourceFamily.Core);

        var controller = new GovernanceController(
            registry,
            new StubPrincipalContext(),
            new AllowAllEvaluation(),
            new StubPrincipalToUserResolver(),
            new StubRoleProvider());
        var result = await controller.GetDescriptors(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var response = (GovernanceDescriptorResponse)ok.Value!;
        response.Resources.Should().ContainSingle(r => r.ResourceType == "User");
        response.Permissions.Should().ContainSingle(p => p.Action == "user.read");
    }

    [Fact]
    public async Task GetDescriptors_WhenAuthorized_ReturnsCatalogWithFamilyAndSourceMetadata()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        registry.RegisterPermission(
            new PermissionDescriptor("user.read", new ResourceDescriptor("User", "instance")),
            ResourceFamily.Core);

        var controller = new GovernanceController(
            registry,
            new StubPrincipalContext(),
            new AllowAllEvaluation(),
            new StubPrincipalToUserResolver(),
            new StubRoleProvider());
        var result = await controller.GetDescriptors(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var response = (GovernanceDescriptorResponse)ok.Value!;
        var perm = response.Permissions.Single(p => p.Action == "user.read");
        perm.Family.Should().Be("Core");
        perm.Source.Should().Be("platform");
        perm.ModuleId.Should().BeNull();
    }

    [Fact]
    public async Task GetEffectivePermissions_WhenNoActor_Returns401()
    {
        var controller = new GovernanceController(
            new InMemoryResourceDescriptorRegistry(),
            new NullPrincipalContext(),
            new AllowAllEvaluation(),
            new StubPrincipalToUserResolver(),
            new StubRoleProvider());
        var result = await controller.GetEffectivePermissions(CancellationToken.None);
        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task GetEffectivePermissions_WhenNotAuthorized_Returns403()
    {
        var controller = new GovernanceController(
            new InMemoryResourceDescriptorRegistry(),
            new StubPrincipalContext(),
            new DenyAllEvaluation(),
            new StubPrincipalToUserResolver(),
            new StubRoleProvider());
        var result = await controller.GetEffectivePermissions(CancellationToken.None);
        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetEffectivePermissions_WhenAuthorizedAndUnresolvedUser_Returns200Empty()
    {
        var controller = new GovernanceController(
            new InMemoryResourceDescriptorRegistry(),
            new StubPrincipalContext(),
            new AllowAllEvaluation(),
            new StubPrincipalToUserResolver(resolveToNull: true),
            new StubRoleProvider());
        var result = await controller.GetEffectivePermissions(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var perms = (IReadOnlyList<RolePermissionEntryDto>)ok.Value!;
        perms.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEffectivePermissions_WhenAuthorizedWithRoles_ReturnsMergedPermissions()
    {
        var controller = new GovernanceController(
            new InMemoryResourceDescriptorRegistry(),
            new StubPrincipalContext(),
            new AllowAllEvaluation(),
            new StubPrincipalToUserResolver(),
            new StubRoleProvider(withPermissions: true));
        var result = await controller.GetEffectivePermissions(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var perms = (IReadOnlyList<RolePermissionEntryDto>)ok.Value!;
        perms.Should().Contain(p => p.Action == "user.read");
    }

    private sealed class NullPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(null);
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false));
    }

    private sealed class AllowAllEvaluation : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class DenyAllEvaluation : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }

    private sealed class StubPrincipalToUserResolver : IPrincipalToUserResolver
    {
        private readonly bool _resolveToNull;

        public StubPrincipalToUserResolver(bool resolveToNull = false) => _resolveToNull = resolveToNull;

        public ValueTask<Apperture.Core.Domain.Identity.UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_resolveToNull ? null : new Apperture.Core.Domain.Identity.UserId("user-1"));
    }

    private sealed class StubRoleProvider : IRoleProvider
    {
        private readonly bool _withPermissions;

        public StubRoleProvider(bool withPermissions = false) => _withPermissions = withPermissions;

        public ValueTask<IReadOnlyList<Apperture.Core.Domain.Identity.Role>> GetRolesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Apperture.Core.Domain.Identity.Role>>([]);

        public ValueTask<Apperture.Core.Domain.Identity.Role?> GetRoleAsync(Apperture.Core.Domain.Identity.RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Apperture.Core.Domain.Identity.Role?>(null);

        public ValueTask<IReadOnlyList<Apperture.Core.Domain.Identity.Role>> GetRolesForUserAsync(Apperture.Core.Domain.Identity.UserId userId, CancellationToken cancellationToken = default)
        {
            if (!_withPermissions)
                return ValueTask.FromResult<IReadOnlyList<Apperture.Core.Domain.Identity.Role>>([]);

            var perm = new PermissionDescriptor("user.read", new ResourceDescriptor("User", "instance"));
            var role = new Apperture.Core.Domain.Identity.Role(
                new Apperture.Core.Domain.Identity.RoleId("r1"),
                "Test",
                null,
                [perm],
                false);
            return ValueTask.FromResult<IReadOnlyList<Apperture.Core.Domain.Identity.Role>>([role]);
        }

        public ValueTask AssignRoleToUserAsync(Apperture.Core.Domain.Identity.UserId userId, Apperture.Core.Domain.Identity.RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveRoleFromUserAsync(Apperture.Core.Domain.Identity.UserId userId, Apperture.Core.Domain.Identity.RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
