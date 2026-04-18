using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.CampusNexus.Models;
using Apperture.Core.Application.Execution;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Proof.CampusNexus.StudentReview;
using Proof.CampusNexus.StudentReview.Persistence;
using Xunit;

namespace Apperture.UnitTests.Proof.CampusNexus;

public sealed class SyncRecentStudentsStepHandlerTests
{
    [Fact]
    public async Task Upserts_snapshots_and_is_idempotent_on_second_run()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<ProofStudentReviewDbContext>(o =>
            o.UseInMemoryDatabase(dbName));
        await using var provider = services.BuildServiceProvider();

        var apiRows = new List<CnsStudentReviewODataRow>
        {
            new()
            {
                Id = 7,
                StudentNumber = "N7",
                FirstName = "Pat",
                LastName = "Lee",
                CreatedDateTime = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero),
                LastModifiedDateTime = new DateTimeOffset(2024, 2, 2, 0, 0, 0, TimeSpan.Zero)
            }
        };

        var mockCns = new Mock<ICampusNexusApiClient>();
        mockCns
            .Setup(c => c.QueryAsync<CnsStudentReviewODataRow>(It.IsAny<ApiQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiRows);
        mockCns.Setup(c => c.Dispose());

        var handler = new SyncRecentStudentsStepHandler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SyncRecentStudentsStepHandler>.Instance);

        var ctx = new StubProofStepContext("int-a", mockCns.Object);
        var parameters = new Dictionary<string, object?>
        {
            [ProofStudentReviewExecutionKeys.RunId] = "run-1"
        };

        await handler.ExecuteAsync(ctx, parameters);

        await using (var checkScope = provider.CreateAsyncScope())
        {
            var db = checkScope.ServiceProvider.GetRequiredService<ProofStudentReviewDbContext>();
            (await db.StudentReviewSnapshots.CountAsync()).Should().Be(1);
            (await db.StudentReviewRuns.CountAsync()).Should().Be(1);
            var run = await db.StudentReviewRuns.SingleAsync();
            run.Status.Should().Be("succeeded");
            run.InsertedCount.Should().Be(1);
            run.UpdatedCount.Should().Be(0);
        }

        apiRows[0].FirstName = "Pat2";
        await handler.ExecuteAsync(ctx, parameters);

        await using (var checkScope2 = provider.CreateAsyncScope())
        {
            var db = checkScope2.ServiceProvider.GetRequiredService<ProofStudentReviewDbContext>();
            (await db.StudentReviewSnapshots.CountAsync()).Should().Be(1);
            var snap = await db.StudentReviewSnapshots.SingleAsync();
            snap.FirstName.Should().Be("Pat2");
            var runs = await db.StudentReviewRuns.OrderBy(r => r.StartedAtUtc).ToListAsync();
            runs.Should().HaveCount(2);
            runs[^1].InsertedCount.Should().Be(0);
            runs[^1].UpdatedCount.Should().Be(1);
        }
    }

    private sealed class StubProofStepContext : IStepExecutionContext
    {
        private readonly string _integrationId;
        private readonly ICampusNexusApiClient _cns;

        public StubProofStepContext(string integrationId, ICampusNexusApiClient cns)
        {
            _integrationId = integrationId;
            _cns = cns;
        }

        public string ModuleId => ProofCampusNexusStudentReviewConstants.ModuleId;

        public string? IntegrationId => _integrationId;

        public ValueTask<T?> ResolveSlotAsync<T>(string slotName, CancellationToken cancellationToken = default)
            where T : class
        {
            if (typeof(T) == typeof(ICampusNexusApiClient)
                && string.Equals(slotName, ProofCampusNexusStudentReviewConstants.SlotCnsApi, StringComparison.Ordinal))
            {
                return ValueTask.FromResult((T?)(object?)_cns);
            }

            return ValueTask.FromResult<T?>(null);
        }
    }
}
