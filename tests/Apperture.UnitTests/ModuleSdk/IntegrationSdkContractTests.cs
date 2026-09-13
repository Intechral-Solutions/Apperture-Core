using Apperture.Core.Application.Execution;
using Apperture.Core.Application.OperationItems;
using Xunit;

namespace Apperture.UnitTests.ModuleSdk;

public sealed class IntegrationSdkContractTests
{
    [Fact]
    public void OperationGroupAggregator_enforces_total_count_invariant()
    {
        var aggregator = new OperationGroupAggregator();
        aggregator.RecordSucceeded();
        aggregator.RecordSucceeded();
        aggregator.RecordFailed();
        aggregator.RecordSkipped();

        var totals = aggregator.ToTotals();
        totals.EnsureInvariant();
        Assert.Equal(4, totals.TotalCount);
        Assert.Equal(2, totals.SucceededCount);
        Assert.Equal(1, totals.FailedCount);
        Assert.Equal(1, totals.SkippedCount);
    }

    [Theory]
    [InlineData(false, OperationGroupStatuses.CompletedWithErrors)]
    [InlineData(true, OperationGroupStatuses.Failed)]
    public void OperationGroupAggregator_derives_group_status_from_failures(bool strict, string expected)
    {
        var aggregator = new OperationGroupAggregator();
        aggregator.RecordSucceeded();
        aggregator.RecordFailed();
        Assert.Equal(expected, aggregator.DeriveGroupStatus(strict));
    }

    [Fact]
    public void OperationGroupAggregator_empty_group_succeeds()
    {
        var aggregator = new OperationGroupAggregator();
        Assert.Equal(OperationGroupStatuses.Succeeded, aggregator.DeriveGroupStatus());
        Assert.Equal(ProcedureOutcomeKind.Succeeded, aggregator.DeriveProcedureOutcome());
    }

    [Theory]
    [InlineData(OperationItemStatuses.Failed, null, true)]
    [InlineData(OperationItemStatuses.Failed, OperationFailureCategory.TransientExternal, true)]
    [InlineData(OperationItemStatuses.Failed, OperationFailureCategory.PermanentExternal, false)]
    [InlineData(OperationItemStatuses.Succeeded, null, false)]
    [InlineData(OperationItemStatuses.Skipped, null, false)]
    public void OperationItemReporting_classifies_retry_eligibility(
        string status,
        OperationFailureCategory? category,
        bool expected)
    {
        Assert.Equal(expected, OperationItemReporting.IsRetryableFailure(status, category));
    }

    [Fact]
    public void ProcedureExecutionOutcome_maps_to_group_status()
    {
        Assert.Equal(
            OperationGroupStatuses.CompletedWithErrors,
            ProcedureExecutionOutcome.PartialSuccess(new OperationGroupTotals(3, 2, 1, 0)).MapToGroupStatus());
        Assert.Equal(
            OperationGroupStatuses.Succeeded,
            ProcedureExecutionOutcome.Succeeded().MapToGroupStatus());
    }

    [Fact]
    public void Complete_request_from_aggregator_satisfies_validation()
    {
        var aggregator = new OperationGroupAggregator();
        aggregator.RecordSucceeded();
        aggregator.RecordFailed();

        var request = aggregator.ToCompleteRequest("grp-1");
        OperationItemizationValidation.ValidateComplete(request);
        Assert.Equal(2, request.TotalCount);
    }
}
