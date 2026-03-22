using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.Audit;
using Apperture.Core.Application.Services;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class AuditControllerTests
{
    private static AuditController NewController(
        IAuditTimelineQueryService? timeline = null,
        IPrincipalContext? principal = null,
        IAuthorizationEvaluationService? authorization = null)
    {
        return new AuditController(
            timeline ?? new StubAuditTimelineQueryService(new AuditTimelinePage([], null)),
            principal ?? new StubPrincipalContext(),
            authorization ?? new StubAuthorizationEvaluationService(allowed: true));
    }

    [Fact]
    public async Task List_Returns200WithPage()
    {
        var entry = new AuditTimelineEntry(
            "1",
            DateTimeOffset.UtcNow,
            "system:scheduler",
            "scheduler.schedule.dispatched",
            "scheduler",
            "success",
            "scheduler.schedule.dispatched: success",
            "schedule:s1;RunId=r1",
            [new AuditRelatedReference("schedule", "s1"), new AuditRelatedReference("run", "r1")]);
        var page = new AuditTimelinePage([entry], null);
        var svc = new StubAuditTimelineQueryService(page);
        var controller = NewController(timeline: svc);

        var result = await controller.List(cancellationToken: CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var returned = ok.Value.Should().BeOfType<AuditTimelinePage>().Subject;
        returned.Items.Should().HaveCount(1);
        returned.Items[0].Action.Should().Be("scheduler.schedule.dispatched");
        returned.Items[0].Category.Should().Be("scheduler");
    }

    [Fact]
    public async Task GetById_WhenFound_Returns200()
    {
        var entry = new AuditTimelineEntry(
            "42",
            DateTimeOffset.UtcNow,
            "user:alice",
            "recovery.blocked_work.retry_requested",
            "recovery",
            "requested",
            "recovery.blocked_work.retry_requested: requested",
            "runId=r1;definitionId=wf1",
            [new AuditRelatedReference("run", "r1")]);
        var svc = new StubAuditTimelineQueryService(entry: entry);
        var controller = NewController(timeline: svc);

        var result = await controller.GetById("42", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var returned = ok.Value.Should().BeOfType<AuditTimelineEntry>().Subject;
        returned.Id.Should().Be("42");
    }

    [Fact]
    public async Task GetById_WhenNotFound_Returns404()
    {
        var svc = new StubAuditTimelineQueryService(entry: null);
        var controller = NewController(timeline: svc);

        var result = await controller.GetById("999", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task List_WithoutAuditRead_Returns403()
    {
        var page = new AuditTimelinePage([], null);
        var auth = new StubAuthorizationEvaluationService(allowed: false);
        var controller = NewController(timeline: new StubAuditTimelineQueryService(page), authorization: auth);
        var result = await controller.List(cancellationToken: CancellationToken.None);
        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task List_WithAuditRead_Returns200()
    {
        var page = new AuditTimelinePage([], null);
        var auth = new StubAuthorizationEvaluationService(allowed: true);
        var controller = NewController(timeline: new StubAuditTimelineQueryService(page), authorization: auth);
        var result = await controller.List(cancellationToken: CancellationToken.None);
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        public ValueTask<Apperture.Shared.Kernel.IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Apperture.Shared.Kernel.IActor?>(new StubActor());
    }

    private sealed class StubActor : Apperture.Shared.Kernel.IActor
    {
        public Apperture.Shared.Kernel.ActorId Id => new("user:test");
        public Apperture.Shared.Kernel.ActorKind Kind => Apperture.Shared.Kernel.ActorKind.Human;
        public string? DisplayName => "Test User";
    }

    private sealed class StubAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        private readonly bool _allowed;

        public StubAuthorizationEvaluationService(bool allowed = true) => _allowed = allowed;

        public ValueTask<bool> EvaluateAsync(
            Apperture.Core.Domain.Governance.AuthorizationEvaluationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_allowed);
    }

    private sealed class StubAuditTimelineQueryService : IAuditTimelineQueryService
    {
        private readonly AuditTimelinePage _page;
        private readonly AuditTimelineEntry? _entry;

        public StubAuditTimelineQueryService(
            AuditTimelinePage? page = null,
            AuditTimelineEntry? entry = null)
        {
            _page = page ?? new AuditTimelinePage([], null);
            _entry = entry;
        }

        public ValueTask<AuditTimelinePage> QueryAsync(AuditFilterCriteria criteria, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_page);

        public ValueTask<AuditTimelineEntry?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_entry);
    }
}
