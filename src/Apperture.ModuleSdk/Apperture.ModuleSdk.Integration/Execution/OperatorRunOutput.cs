namespace Apperture.Core.Application.Execution;

/// <summary>
/// Wraps the last step return value with operator display intent. Plain returns remain Epic-20-compatible;
/// use this type when the module wants summary-only or withheld output. See ADR-0020.
/// </summary>
public sealed class OperatorRunOutput
{
    private OperatorRunOutput(OperatorOutputDisplayPolicy displayPolicy, object? payload)
    {
        DisplayPolicy = displayPolicy;
        Payload = payload;
    }

    public OperatorOutputDisplayPolicy DisplayPolicy { get; }

    /// <summary>
    /// Value to capture; ignored when <see cref="DisplayPolicy"/> is <see cref="OperatorOutputDisplayPolicy.Withheld"/>.
    /// </summary>
    public object? Payload { get; }

    public static OperatorRunOutput Inline(object? payload) =>
        new(OperatorOutputDisplayPolicy.Inline, payload);

    public static OperatorRunOutput SummaryOnly(object? payload) =>
        new(OperatorOutputDisplayPolicy.SummaryOnly, payload);

    public static OperatorRunOutput Withheld() =>
        new(OperatorOutputDisplayPolicy.Withheld, null);
}
