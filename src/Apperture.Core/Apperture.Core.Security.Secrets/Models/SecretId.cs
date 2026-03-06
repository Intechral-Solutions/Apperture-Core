namespace Apperture.Core.Security.Secrets.Models;

/// <summary>
/// Strongly typed identifier for a logical secret.
/// </summary>
public readonly record struct SecretId(Guid Value)
{
    /// <summary>
    /// Creates a new secret identifier.
    /// </summary>
    public static SecretId New() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}
