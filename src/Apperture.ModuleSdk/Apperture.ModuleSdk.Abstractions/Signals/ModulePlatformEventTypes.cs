namespace Apperture.ModuleSdk.Abstractions.Signals;

/// <summary>
/// Naming for durable platform events emitted by modules (Epic 77).
/// Full type format: apperture.module.{normalizedModuleId}.{signalName}
/// </summary>
public static class ModulePlatformEventTypes
{
    /// <summary>Normalizes manifest module id for use in CloudEvents-inspired type suffix (lowercase, segments preserved).</summary>
    public static string NormalizeModuleId(string moduleId) =>
        moduleId.Trim().ToLowerInvariant();

    public static string Format(string moduleId, string signalName) =>
        $"apperture.module.{NormalizeModuleId(moduleId)}.{NormalizeSignalName(signalName)}";

    private static string NormalizeSignalName(string signalName) =>
        signalName.Trim().ToLowerInvariant();
}
