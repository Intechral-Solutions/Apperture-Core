namespace Apperture.Core.Application.Execution;

/// <summary>
/// Declares how operator-facing failure diagnostics should be exposed.
/// This is author intent, not a comprehensive security guarantee.
/// </summary>
public enum OperatorDiagnosticsDisplayPolicy
{
    Inline,
    SummaryOnly,
    Withheld
}
