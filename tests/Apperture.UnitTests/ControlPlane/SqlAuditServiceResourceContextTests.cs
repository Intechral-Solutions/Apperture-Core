using Apperture.Core.Domain.Audit;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class SqlAuditServiceResourceContextTests
{
    [Fact]
    public async Task AppendAsync_PersistsResourceContextId_WhenSupplied()
    {
        var db = NewDb();
        var svc = new SqlAuditService(db);

        await svc.AppendAsync(new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId("system"),
            "test.action",
            Resource: null,
            Outcome: "success",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            Details: null,
            ResourceContextId: "app-a"));

        var entity = await db.AuditLogs.SingleAsync();
        entity.ResourceContextId.Should().Be("app-a");
    }

    [Fact]
    public async Task AppendAsync_LeavesResourceContextIdNull_WhenNotSupplied()
    {
        var db = NewDb();
        var svc = new SqlAuditService(db);

        await svc.AppendAsync(new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId("system"),
            "test.action",
            Resource: null,
            Outcome: "success",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            Details: null));

        var entity = await db.AuditLogs.SingleAsync();
        entity.ResourceContextId.Should().BeNull();
    }

    [Fact]
    public async Task QueryAsync_And_GetByIdAsync_RoundTripResourceContextId()
    {
        var db = NewDb();
        var svc = new SqlAuditService(db);
        await svc.AppendAsync(new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId("system"),
            "test.action",
            Resource: null,
            Outcome: "success",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            Details: null,
            ResourceContextId: "app-b"));

        var page = await svc.QueryAsync(new Apperture.Core.Application.Services.AuditQuery());
        page.Items.Should().ContainSingle(r => r.ResourceContextId == "app-b");

        var byId = await svc.GetByIdAsync(page.Items[0].Id.Value);
        byId!.ResourceContextId.Should().Be("app-b");
    }

    [Fact]
    public async Task QueryAsync_FiltersByResourceContextId_WhenSupplied()
    {
        var db = NewDb();
        var svc = new SqlAuditService(db);
        await svc.AppendAsync(NewRecord("app-a"));
        await svc.AppendAsync(NewRecord("app-b"));
        await svc.AppendAsync(NewRecord("app-a"));

        var page = await svc.QueryAsync(new Apperture.Core.Application.Services.AuditQuery(FilterByResourceContextId: "app-a"));

        page.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(r => r.ResourceContextId == "app-a");
    }

    private static AuditRecord NewRecord(string resourceContextId) =>
        new(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId("system"),
            "test.action",
            Resource: null,
            Outcome: "success",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            Details: null,
            ResourceContextId: resourceContextId);

    private static AppertureDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppertureDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
