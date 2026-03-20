namespace Apperture.Core.Domain.Governance;

/// <summary>
/// Scope key format helpers for authorization. Per ADR-0013.
/// Scope keys are case-sensitive.
/// </summary>
public static class ScopeKeyConstants
{
    /// <summary>Instance-level scope.</summary>
    public const string Instance = "instance";

    /// <summary>Environment scope: environment:{environmentId}</summary>
    public static string Environment(string environmentId) => $"environment:{environmentId}";

    /// <summary>Module scope: module:{moduleId}</summary>
    public static string Module(string moduleId) => $"module:{moduleId}";

    /// <summary>Connection scope: connection:{connectionId}</summary>
    public static string Connection(string connectionId) => $"connection:{connectionId}";

    /// <summary>Operation scope: operation:{operationId}</summary>
    public static string Operation(string operationId) => $"operation:{operationId}";

    /// <summary>Runtime instance scope: environment:{environmentId}:runtime:{runtimeId}</summary>
    public static string RuntimeInstance(string environmentId, string runtimeId) =>
        $"environment:{environmentId}:runtime:{runtimeId}";

    /// <summary>Slot scope: module:{moduleId}:slot:{slotName}</summary>
    public static string Slot(string moduleId, string slotName) => $"module:{moduleId}:slot:{slotName}";
}
