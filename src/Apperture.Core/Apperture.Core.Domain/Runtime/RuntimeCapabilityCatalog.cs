namespace Apperture.Core.Domain.Runtime;

public enum RuntimeCapabilityKind
{
    /// <summary>Runtime can be inspected by operators (read-only). </summary>
    Inspection = 0,

    /// <summary>Runtime can execute a privileged control intent requested by operators.</summary>
    Control = 1
}

/// <summary>
/// Canonical runtime capability definition (platform seeded).
/// </summary>
public sealed record RuntimeCapabilityDefinition(string CapabilityId, RuntimeCapabilityKind Kind);

/// <summary>
/// Minimal, platform-seeded runtime capability catalog.
/// This cycle does not add module contributions; only explicit platform-defined capabilities exist.
/// </summary>
public static class RuntimeCapabilityCatalog
{
    // Inspection capabilities
    public const string InspectionCapabilities = "runtime.inspection.capabilities";

    // Control capabilities
    public const string ControlShutdown = "runtime.control.shutdown";

    /// <summary>
    /// Attempts to resolve a capability id against the canonical catalog.
    /// </summary>
    public static bool TryResolve(string? capabilityId, out RuntimeCapabilityDefinition definition)
    {
        definition = default!;
        if (string.IsNullOrWhiteSpace(capabilityId))
            return false;

        // Canonical IDs are case-sensitive (authorization/capability identity is case-sensitive in this codebase).
        if (string.Equals(capabilityId, InspectionCapabilities, StringComparison.Ordinal))
        {
            definition = new RuntimeCapabilityDefinition(InspectionCapabilities, RuntimeCapabilityKind.Inspection);
            return true;
        }

        if (string.Equals(capabilityId, ControlShutdown, StringComparison.Ordinal))
        {
            definition = new RuntimeCapabilityDefinition(ControlShutdown, RuntimeCapabilityKind.Control);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Validates a capability id and returns its definition.
    /// </summary>
    public static RuntimeCapabilityDefinition ValidateOrThrow(string capabilityId)
    {
        if (!TryResolve(capabilityId, out var definition))
            throw new InvalidOperationException($"Unknown runtime capability id '{capabilityId}'.");

        return definition;
    }
}

