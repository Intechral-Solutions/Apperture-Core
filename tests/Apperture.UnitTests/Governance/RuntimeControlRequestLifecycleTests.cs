using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimeHost.Windows.WorkerHost;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class RuntimeControlRequestLifecycleTests
{
    [Fact]
    public async Task Applier_TransitionsQueuedToApplied()
    {
        var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        db.RuntimeControlRequests.Add(new RuntimeControlRequestEntity
        {
            RuntimeId = "rt-1",
            ControlKind = RuntimeControlKindNames.Shutdown,
            Status = RuntimeControlStatusNames.Queued,
            RequestedAt = now,
            RequestedBy = "u1",
            AppliedAt = null,
            AppliedBy = null,
            LastError = null
        });
        await db.SaveChangesAsync();

        var applier = new RuntimeShutdownRequestApplier();
        var applied = await applier.TryApplyQueuedShutdownAsync(db, runtimeId: "rt-1", workerId: "worker-1", cancellationToken: default);

        applied.Should().BeTrue();

        var row = await db.RuntimeControlRequests.FirstAsync();
        row.Status.Should().Be(RuntimeControlStatusNames.Applied);
        row.AppliedAt.Should().NotBeNull();
        row.AppliedBy.Should().Be("worker-1");
        row.LastError.Should().BeNull();
    }

    [Fact]
    public async Task Applier_DoesNotApply_WhenRuntimeIdDoesNotMatch()
    {
        var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        db.RuntimeControlRequests.Add(new RuntimeControlRequestEntity
        {
            RuntimeId = "rt-1",
            ControlKind = RuntimeControlKindNames.Shutdown,
            Status = RuntimeControlStatusNames.Queued,
            RequestedAt = now,
            RequestedBy = "u1"
        });
        await db.SaveChangesAsync();

        var applier = new RuntimeShutdownRequestApplier();
        var applied = await applier.TryApplyQueuedShutdownAsync(db, runtimeId: "rt-2", workerId: "worker-1", cancellationToken: default);

        applied.Should().BeFalse();

        var row = await db.RuntimeControlRequests.FirstAsync();
        row.Status.Should().Be(RuntimeControlStatusNames.Queued);
        row.AppliedAt.Should().BeNull();
        row.AppliedBy.Should().BeNull();
    }

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }
}

