namespace Apperture.Bootstrap.Models;

/// <summary>
/// Result of bootstrap configuration validation before commit.
/// </summary>
public sealed record BootstrapValidationResult
{
    /// <summary>True if validation passed.</summary>
    public bool Success { get; init; }

    /// <summary>Validation errors when Success is false.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
}
