namespace Apperture.Core.Application.OperationItems;

/// <summary>
/// Thrown when operation itemization contracts are violated (SDK-3A / SDK-6A).
/// </summary>
public sealed class OperationItemizationException : InvalidOperationException
{
    public OperationItemizationException(string message) : base(message) { }

    public OperationItemizationException(string message, Exception inner) : base(message, inner) { }
}
