namespace Apperture.Core.Application.OperationItems;

/// <summary>
/// Mutable accumulator for operation group item counts. Enforces the total-count invariant (SDK-6A).
/// </summary>
public sealed class OperationGroupAggregator
{
    private int _succeeded;
    private int _failed;
    private int _skipped;

    public int SucceededCount => _succeeded;
    public int FailedCount => _failed;
    public int SkippedCount => _skipped;
    public int TotalCount => _succeeded + _failed + _skipped;

    public void Record(string itemStatus)
    {
        switch (itemStatus)
        {
            case OperationItemStatuses.Succeeded:
                _succeeded++;
                break;
            case OperationItemStatuses.Failed:
                _failed++;
                break;
            case OperationItemStatuses.Skipped:
                _skipped++;
                break;
            case OperationItemStatuses.Pending:
                break;
            default:
                throw new OperationItemizationException($"Unknown item status '{itemStatus}'.");
        }
    }

    public void RecordSucceeded() => _succeeded++;
    public void RecordFailed() => _failed++;
    public void RecordSkipped() => _skipped++;

    public OperationGroupTotals ToTotals() => new(TotalCount, _succeeded, _failed, _skipped);

    public string DeriveGroupStatus(bool strictFailOnAnyFailure = false)
    {
        if (TotalCount == 0)
            return OperationGroupStatuses.Succeeded;

        if (_failed > 0)
            return strictFailOnAnyFailure
                ? OperationGroupStatuses.Failed
                : OperationGroupStatuses.CompletedWithErrors;

        return OperationGroupStatuses.Succeeded;
    }

    public ProcedureOutcomeKind DeriveProcedureOutcome(bool strictFailOnAnyFailure = false)
    {
        if (TotalCount == 0)
            return ProcedureOutcomeKind.Succeeded;

        if (_failed > 0)
            return strictFailOnAnyFailure
                ? ProcedureOutcomeKind.Failed
                : ProcedureOutcomeKind.PartialSuccess;

        return ProcedureOutcomeKind.Succeeded;
    }

    public CompleteOperationGroupRequest ToCompleteRequest(
        string operationGroupId,
        bool strictFailOnAnyFailure = false,
        string? summaryJson = null,
        DateTimeOffset? completedAtUtc = null)
    {
        var totals = ToTotals();
        return new CompleteOperationGroupRequest(
            operationGroupId,
            DeriveGroupStatus(strictFailOnAnyFailure),
            totals.TotalCount,
            totals.SucceededCount,
            totals.FailedCount,
            totals.SkippedCount,
            summaryJson,
            completedAtUtc);
    }
}

public sealed record OperationGroupTotals(
    int TotalCount,
    int SucceededCount,
    int FailedCount,
    int SkippedCount)
{
    public void EnsureInvariant()
    {
        if (SucceededCount + FailedCount + SkippedCount != TotalCount)
        {
            throw new OperationItemizationException(
                "TotalCount must equal SucceededCount + FailedCount + SkippedCount.");
        }
    }
}
