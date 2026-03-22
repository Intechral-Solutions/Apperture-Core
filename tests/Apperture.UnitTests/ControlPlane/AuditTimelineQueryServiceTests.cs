using Apperture.ControlPlane.Security.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class AuditTimelineQueryServiceTests
{
    [Fact]
    public async Task QueryAsync_ReturnsMappedEntries()
    {
        var records = new List<AuditRecord>
        {
            CreateRecord("1", "scheduler.schedule.dispatched", "system:scheduler", "blocked; schedule:s1;RunId=r1"),
            CreateRecord("2", "recovery.blocked_work.retry_requested", "user:alice", "runId=r2;definitionId=wf1"),
        };
        var audit = new FakeAuditService(records);
        var svc = new AuditTimelineQueryService(audit);

        var page = await svc.QueryAsync(new AuditFilterCriteria(
            FromUtc: DateTimeOffset.UtcNow.AddDays(-1),
            PageSize: 10));

        page.Items.Should().HaveCount(2);
        var schedulerEntry = page.Items.First(e => e.Action == "scheduler.schedule.dispatched");
        schedulerEntry.Category.Should().Be("scheduler");
        schedulerEntry.Actor.Should().Be("system:scheduler");
        schedulerEntry.RelatedReferences.Should().Contain(r => r.Kind == "schedule" && r.Id == "s1");
        schedulerEntry.RelatedReferences.Should().Contain(r => r.Kind == "run" && r.Id == "r1");

        var recoveryEntry = page.Items.First(e => e.Action == "recovery.blocked_work.retry_requested");
        recoveryEntry.Category.Should().Be("recovery");
        recoveryEntry.RelatedReferences.Should().Contain(r => r.Kind == "run" && r.Id == "r2");
    }

    [Fact]
    public async Task QueryAsync_FilterByActionPrefix_ReturnsFiltered()
    {
        var records = new List<AuditRecord>
        {
            CreateRecord("1", "scheduler.schedule.dispatched", "system:scheduler", "success"),
            CreateRecord("2", "recovery.blocked_work.retry_requested", "user:alice", "requested"),
            CreateRecord("3", "scheduler.schedule.blocked_by_lifecycle", "system:scheduler", "blocked"),
        };
        var audit = new FakeAuditService(records);
        var svc = new AuditTimelineQueryService(audit);

        var page = await svc.QueryAsync(new AuditFilterCriteria(
            FromUtc: DateTimeOffset.UtcNow.AddDays(-1),
            ActionPrefix: "scheduler.",
            PageSize: 10));

        page.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(e => e.Action.StartsWith("scheduler."));
    }

    [Fact]
    public async Task QueryAsync_FilterByRunId_ReturnsMatching()
    {
        var records = new List<AuditRecord>
        {
            CreateRecord("1", "recovery.blocked_work.retry_requested", "user:alice", "runId=run-123;definitionId=wf1"),
            CreateRecord("2", "recovery.blocked_work.retry_requested", "user:bob", "runId=run-456;definitionId=wf2"),
        };
        var audit = new FakeAuditService(records);
        var svc = new AuditTimelineQueryService(audit);

        var page = await svc.QueryAsync(new AuditFilterCriteria(
            FromUtc: DateTimeOffset.UtcNow.AddDays(-1),
            RunId: "run-123",
            PageSize: 10));

        page.Items.Should().HaveCount(1);
        page.Items[0].RelatedReferences.Should().Contain(r => r.Kind == "run" && r.Id == "run-123");
    }

    [Fact]
    public async Task GetByIdAsync_Found_ReturnsEntry()
    {
        var records = new List<AuditRecord>
        {
            CreateRecord("42", "scheduler.schedule.dispatched", "system:scheduler", "success; schedule:s1"),
        };
        var audit = new FakeAuditService(records);
        var svc = new AuditTimelineQueryService(audit);

        var entry = await svc.GetByIdAsync("42");
        entry.Should().NotBeNull();
        entry!.Id.Should().Be("42");
        entry.Action.Should().Be("scheduler.schedule.dispatched");
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var audit = new FakeAuditService([]);
        var svc = new AuditTimelineQueryService(audit);
        (await svc.GetByIdAsync("999")).Should().BeNull();
    }

    private static AuditRecord CreateRecord(string id, string action, string actor, string details) =>
        new(
            new AuditRecordId(id),
            new ActorId(actor),
            action,
            Resource: null,
            Outcome: "",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            details);

    private sealed class FakeAuditService : IAuditService
    {
        private readonly List<AuditRecord> _records;

        public FakeAuditService(List<AuditRecord> records) => _records = records;

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
        {
            var filtered = _records.AsEnumerable();
            if (query.FilterByActorId is { } actorId)
                filtered = filtered.Where(r => r.ActorId.Value == actorId.Value);
            if (!string.IsNullOrEmpty(query.FilterByActionPrefix))
                filtered = filtered.Where(r => r.Action.StartsWith(query.FilterByActionPrefix, StringComparison.Ordinal));
            var list = filtered.OrderByDescending(r => r.Timestamp).ToList();
            var pageSize = Math.Clamp(query.PageSize, 1, 100);
            var items = list.Take(pageSize + 1).ToList();
            var hasMore = items.Count > pageSize;
            if (hasMore)
                items.RemoveAt(items.Count - 1);
            return ValueTask.FromResult(new AuditPage(items, hasMore ? "token" : null));
        }

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AuditRecord?>(_records.FirstOrDefault(r => r.Id.Value == id));
    }
}
