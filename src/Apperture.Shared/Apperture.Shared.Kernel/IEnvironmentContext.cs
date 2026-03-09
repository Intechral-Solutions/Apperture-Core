namespace Apperture.Shared.Kernel;

/// <summary>
/// Provides the current environment identifier (opaque reference). Does not expose rich Environment entity.
/// </summary>
public interface IEnvironmentContext
{
    /// <summary>
    /// Opaque environment identifier for the current context.
    /// </summary>
    string EnvironmentId { get; }
}
