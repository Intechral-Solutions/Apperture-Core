using System.Data;
using Apperture.Connectors.CampusNexus;
using Apperture.Core.Application.Execution;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.RuntimePlane.Engine.PlatformEvents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Proof.CampusNexus.StudentReview;
using Xunit;

namespace Apperture.UnitTests.Proof.CampusNexus;

public sealed class SyncRecentStudentsStepHandlerTests
{
    [Fact]
    public async Task Sync_throws_when_integration_sql_slot_unbound()
    {
        var handler = new SyncRecentStudentsStepHandler(NullLogger<SyncRecentStudentsStepHandler>.Instance);
        var ctx = new StubProofStepContextMissingSql("int-a", new Mock<ICampusNexusApiClient>().Object);
        var parameters = new Dictionary<string, object?>
        {
            [ProofStudentReviewExecutionKeys.RunId] = "run-1"
        };

        var act = async () => _ = await handler.ExecuteAsync(ctx, parameters);

        await act.Should().ThrowAsync<OperatorExecutionFailureException>();
    }

    private sealed class StubProofStepContextMissingSql : IStepExecutionContext
    {
        private readonly string _integrationId;
        private readonly ICampusNexusApiClient _cns;

        public StubProofStepContextMissingSql(string integrationId, ICampusNexusApiClient cns)
        {
            _integrationId = integrationId;
            _cns = cns;
        }

        public string ModuleId => ProofCampusNexusStudentReviewConstants.ModuleId;

        public string? IntegrationId => _integrationId;

        public IModulePlatformEventEmitter PlatformEvents => NullModulePlatformEventEmitter.Instance;

        public string? RunId => null;

        public string? ProcedureId => null;

        public string? ScheduleId => null;

        public string? CorrelationId => null;

        public string? CausationId => null;

        public ValueTask<T?> ResolveSlotAsync<T>(string slotName, CancellationToken cancellationToken = default)
            where T : class
        {
            if (typeof(T) == typeof(IDbConnection)
                && string.Equals(slotName, ProofCampusNexusStudentReviewConstants.SlotIntegrationDb, StringComparison.Ordinal))
            {
                return ValueTask.FromResult<T?>(null);
            }

            if (typeof(T) == typeof(ICampusNexusApiClient)
                && string.Equals(slotName, ProofCampusNexusStudentReviewConstants.SlotCnsApi, StringComparison.Ordinal))
            {
                return ValueTask.FromResult((T?)(object?)_cns);
            }

            return ValueTask.FromResult<T?>(null);
        }
    }
}
