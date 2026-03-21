namespace Apperture.Core.Application.Execution;

/// <summary>
/// Declares how the last step's return should be exposed to operators. Use with <see cref="OperatorRunOutput"/>.
/// This is module intent, not a security or DLP guarantee. See ADR-0020.
/// </summary>
public enum OperatorOutputDisplayPolicy
{
    /// <summary>
    /// Serialize and persist bounded JSON for operator inline display (same as Epic 20 plain returns).
    /// </summary>
    Inline,

    /// <summary>
    /// Derive and persist a short summary only; full JSON is not stored.
    /// </summary>
    SummaryOnly,

    /// <summary>
    /// Do not persist operator-visible payload or summary.
    /// </summary>
    Withheld
}
