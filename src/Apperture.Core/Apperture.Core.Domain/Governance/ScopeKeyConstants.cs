namespace Apperture.Core.Domain.Governance;

/// <summary>
/// Well-known scope key formats for authorization. Per ADR-0013.
/// </summary>
public static class ScopeKeyConstants
{
    /// <summary>Instance-level scope; default for instance-wide operations.</summary>
    public const string Instance = "instance";

    /// <summary>Scope for a specific module. Format: module:{moduleId}</summary>
    public static string ScopeForModule(string moduleId) => $"module:{moduleId}";

    /// <summary>Scope for a specific connection. Format: connection:{connectionId}</summary>
    public static string ScopeForConnection(string connectionId) => $"connection:{connectionId}";

    /// <summary>Scope for a specific operation. Format: operation:{operationId}</summary>
    public static string ScopeForOperation(string operationId) => $"operation:{operationId}";

    /// <summary>Scope for a module slot. Format: module:{moduleId}:slot:{slotName}</summary>
    public static string ScopeForSlot(string moduleId, string slotName) => $"module:{moduleId}:slot:{slotName}";
}
