using Apperture.ControlPlane.Security;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.Services;
using Apperture.Management.Api.Controllers;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class PlatformEventsControllerTests
{
    [Fact]
    public async Task List_passes_runId_and_moduleId_into_query()
    {
        PlatformEventQuery? captured = null;
        var store = new CapturingPlatformEventStore(q =>
        {
            captured = q;
            return ValueTask.FromResult(new PlatformEventQueryPage([], false));
        });

        var sut = new PlatformEventsController(
            store,
            new StubPrincipalContext(),
            new StubAuthorizationEvaluationService(true));

        var action = await sut.List(
            integrationId: null,
            eventType: null,
            fromOccurredAtUtc: null,
            toOccurredAtUtc: null,
            runId: "run-99",
            moduleId: "Sample.HelloWorld",
            take: 30,
            CancellationToken.None);

        action.Result.Should().BeOfType<OkObjectResult>();
        captured.Should().NotBeNull();
        captured!.RunId.Should().Be("run-99");
        captured.ModuleId.Should().Be("Sample.HelloWorld");
        captured.Take.Should().Be(30);
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new StubActor());
    }

    private sealed class StubActor : IActor
    {
        public ActorId Id => new("user:test");
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName => "Test";
    }

    private sealed class StubAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        private readonly bool _allowed;
        public StubAuthorizationEvaluationService(bool allowed) => _allowed = allowed;

        public ValueTask<bool> EvaluateAsync(
            Apperture.Core.Domain.Governance.AuthorizationEvaluationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_allowed);
    }

    private sealed class CapturingPlatformEventStore : IPlatformEventStore
    {
        private readonly Func<PlatformEventQuery, ValueTask<PlatformEventQueryPage>> _query;

        public CapturingPlatformEventStore(Func<PlatformEventQuery, ValueTask<PlatformEventQueryPage>> query) =>
            _query = query;

        public ValueTask<PlatformEventEnvelope> AppendAsync(
            PlatformEventEnvelope envelope,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<PlatformEventQueryPage> QueryAsync(
            PlatformEventQuery query,
            CancellationToken cancellationToken = default) =>
            _query(query);
    }
}
