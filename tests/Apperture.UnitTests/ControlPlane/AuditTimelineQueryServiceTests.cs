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

    [Fact]
    public async Task QueryAsync_ReferenceFilters_ClampsWindowAndFetchSize()
    {
        var fromUtc = DateTimeOffset.UtcNow.AddDays(-30);
        var audit = new RecordingFakeAuditService([CreateRecord("1", "recovery.blocked_work.retry_requested", "user:alice", "runId=r1;definitionId=wf1")]);
        var svc = new AuditTimelineQueryService(audit);

        var page = await svc.QueryAsync(new AuditFilterCriteria(
            FromUtc: fromUtc,
            ToUtc: null,
            RunId: "r1",
            PageSize: 50));

        page.Items.Should().HaveCount(1);
        audit.LastQuery.Should().NotBeNull();
        audit.LastQuery!.ToUtc.Should().NotBeNull("reference filters clamp effective window");
        var windowHours = (audit.LastQuery.ToUtc!.Value - fromUtc).TotalHours;
        windowHours.Should().BeLessOrEqualTo(AuditTimelineQueryService.MaxWindowHoursForReferenceFilters + 1);
    }

    [Fact]
    public async Task QueryAsync_ReferenceFiltersWithoutFromUtc_UsesDefaultWindow()
    {
        var audit = new RecordingFakeAuditService([]);
        var svc = new AuditTimelineQueryService(audit);

        await svc.QueryAsync(new AuditFilterCriteria(
            FromUtc: null,
            RunId: "r1",
            PageSize: 10));

        audit.LastQuery.Should().NotBeNull();
        audit.LastQuery!.FromUtc.Should().NotBeNull("reference filters require default window");
    }

    [Fact]
    public async Task QueryAsync_ReferenceFilters_FetchSizeCapped()
    {
        var audit = new RecordingFakeAuditService([]);
        var svc = new AuditTimelineQueryService(audit);

        await svc.QueryAsync(new AuditFilterCriteria(
            FromUtc: DateTimeOffset.UtcNow.AddDays(-1),
            RunId: "r1",
            PageSize: 500));

        audit.LastQuery.Should().NotBeNull();
        audit.LastQuery!.PageSize.Should().BeLessOrEqualTo(AuditTimelineQueryService.MaxFetchSizeForReferenceFilters);
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

    private sealed class RecordingFakeAuditService : IAuditService
    {
        private readonly FakeAuditService _inner;

        public RecordingFakeAuditService(List<AuditRecord> records) => _inner = new FakeAuditService(records);

        public AuditQuery? LastQuery { get; private set; }

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return await _inner.QueryAsync(query, cancellationToken);
        }

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            _inner.GetByIdAsync(id, cancellationToken);
    }
}
