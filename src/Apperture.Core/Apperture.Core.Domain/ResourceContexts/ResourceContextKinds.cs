namespace Apperture.Core.Domain.ResourceContexts;

/// <summary>
/// ResourceContext kinds. Persisted as a string so new kinds are additive without a migration.
/// </summary>
public static class ResourceContextKinds
{
    /// <summary>The single reserved context that owns everything that is not owned by an Application.</summary>
    public const string Shared = "Shared";

    /// <summary>An operator-created Application context.</summary>
    public const string Application = "Application";

    /// <summary>True when the value is a kind this version of the platform understands.</summary>
    public static bool IsKnown(string? kind) =>
        string.Equals(kind, Shared, StringComparison.Ordinal)
        || string.Equals(kind, Application, StringComparison.Ordinal);
}
