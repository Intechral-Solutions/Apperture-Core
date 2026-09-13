namespace Apperture.Core.Application.Execution;

/// <summary>
/// Declares operator-facing failure intent from handler code.
/// Throw this via <see cref="OperatorExecutionResult"/> helpers.
/// </summary>
public sealed class OperatorExecutionFailureException : Exception
{
    public OperatorExecutionFailureException(
        string summary,
        OperatorDiagnosticsDisplayPolicy displayPolicy,
        object? diagnostics = null)
        : base(summary)
    {
        Summary = summary;
        DisplayPolicy = displayPolicy;
        Diagnostics = diagnostics;
    }

    public string Summary { get; }
    public OperatorDiagnosticsDisplayPolicy DisplayPolicy { get; }
    public object? Diagnostics { get; }
}
