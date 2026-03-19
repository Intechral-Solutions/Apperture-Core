using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// Schedule governance capability evaluation.
/// </summary>
public sealed class ScheduleGovernanceServiceTests
{
    [Fact]
    public async Task CanManageAsync_WhenNoActor_ReturnsFalse()
    {
        var service = new ScheduleGovernanceService(
            new NullPrincipalContext(),
            new AllowAllEvaluation());
        var result = await service.CanManageAsync(CancellationToken.None);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CanManageAsync_WhenNotAuthorized_ReturnsFalse()
    {
        var service = new ScheduleGovernanceService(
            new StubPrincipalContext(),
            new DenyAllEvaluation());
        var result = await service.CanManageAsync(CancellationToken.None);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CanManageAsync_WhenAuthorized_ReturnsTrue()
    {
        var service = new ScheduleGovernanceService(
            new StubPrincipalContext(),
            new AllowAllEvaluation());
        var result = await service.CanManageAsync(CancellationToken.None);
        result.Should().BeTrue();
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
}
