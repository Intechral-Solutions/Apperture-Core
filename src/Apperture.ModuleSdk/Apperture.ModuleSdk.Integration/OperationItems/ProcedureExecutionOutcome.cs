namespace Apperture.Core.Application.OperationItems;

/// <summary>
/// Terminal procedure outcome kind derived from operation groups or explicit step declaration (SDK-6A).
/// </summary>
public enum ProcedureOutcomeKind
{
    Succeeded = 0,
    PartialSuccess = 1,
    Failed = 2,
    Cancelled = 3,
    Skipped = 4,
}

/// <summary>
/// Unified author-facing procedure outcome summary for itemized and non-itemized procedures.
/// </summary>
public sealed record ProcedureExecutionOutcome(
    ProcedureOutcomeKind Kind,
    string? Summary = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    OperationGroupTotals? Totals = null,
    string? MetadataJson = null)
{
    public static ProcedureExecutionOutcome Succeeded(string? summary = null) =>
        new(ProcedureOutcomeKind.Succeeded, Summary: summary);

    public static ProcedureExecutionOutcome PartialSuccess(OperationGroupTotals totals, string? summary = null) =>
        new(ProcedureOutcomeKind.PartialSuccess, Summary: summary, Totals: totals);

    public static ProcedureExecutionOutcome Failed(string errorCode, string errorMessage, string? summary = null) =>
        new(ProcedureOutcomeKind.Failed, Summary: summary, ErrorCode: errorCode, ErrorMessage: errorMessage);

    public static ProcedureExecutionOutcome Cancelled(string? summary = null) =>
        new(ProcedureOutcomeKind.Cancelled, Summary: summary);

    public string MapToGroupStatus() =>
        Kind switch
        {
            ProcedureOutcomeKind.Succeeded => OperationGroupStatuses.Succeeded,
            ProcedureOutcomeKind.PartialSuccess => OperationGroupStatuses.CompletedWithErrors,
            ProcedureOutcomeKind.Failed => OperationGroupStatuses.Failed,
            ProcedureOutcomeKind.Cancelled => OperationGroupStatuses.Failed,
            ProcedureOutcomeKind.Skipped => OperationGroupStatuses.Succeeded,
            _ => OperationGroupStatuses.Failed,
        };
}
