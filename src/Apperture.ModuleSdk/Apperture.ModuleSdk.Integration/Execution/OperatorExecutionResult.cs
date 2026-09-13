namespace Apperture.Core.Application.Execution;

/// <summary>
/// Unified author-facing execution contract helper (Epic 23).
/// Success declarations return a wrapper value; failure declarations return a typed exception to throw.
/// </summary>
public sealed class OperatorExecutionResult
{
    private OperatorExecutionResult(OperatorOutputDisplayPolicy successDisplayPolicy, object? successPayload)
    {
        SuccessDisplayPolicy = successDisplayPolicy;
        SuccessPayload = successPayload;
    }

    public OperatorOutputDisplayPolicy SuccessDisplayPolicy { get; }
    public object? SuccessPayload { get; }

    public static OperatorExecutionResult SuccessInline(object? payload) =>
        new(OperatorOutputDisplayPolicy.Inline, payload);

    public static OperatorExecutionResult SuccessSummaryOnly(object? payload) =>
        new(OperatorOutputDisplayPolicy.SummaryOnly, payload);

    public static OperatorExecutionResult SuccessWithheld() =>
        new(OperatorOutputDisplayPolicy.Withheld, null);

    public static OperatorExecutionFailureException FailSummaryOnly(string summary) =>
        new(summary, OperatorDiagnosticsDisplayPolicy.SummaryOnly);

    public static OperatorExecutionFailureException FailInline(string summary, object? diagnostics = null) =>
        new(summary, OperatorDiagnosticsDisplayPolicy.Inline, diagnostics);

    public static OperatorExecutionFailureException FailWithheld(string summary) =>
        new(summary, OperatorDiagnosticsDisplayPolicy.Withheld);
}
