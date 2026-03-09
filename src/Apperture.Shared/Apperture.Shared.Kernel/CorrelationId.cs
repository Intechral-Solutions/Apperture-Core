namespace Apperture.Shared.Kernel;

/// <summary>
/// Correlation identifier for tracing a request or operation across boundaries.
/// </summary>
public sealed record CorrelationId(string? Value)
{
    public override string ToString() => Value ?? string.Empty;
}
