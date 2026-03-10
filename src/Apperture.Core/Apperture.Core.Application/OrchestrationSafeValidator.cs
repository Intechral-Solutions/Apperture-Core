using Apperture.Core.Security.Secrets.Models;

namespace Apperture.Core.Application;

/// <summary>
/// Validates that workflow parameters and signal payloads do not contain secret-bearing types.
/// Call before passing data into workflow runtime or durable orchestration state/history.
/// </summary>
/// <remarks>
/// Orchestration state and history (e.g. Durable Functions) must never persist ResolvedSecret,
/// SecretMaterial, or other types that carry secret material. Only SecretRef-like identifiers
/// may cross orchestration boundaries.
/// </remarks>
public static class OrchestrationSafeValidator
{
    /// <summary>
    /// Validates that no parameter value is a known secret-bearing type.
    /// Call before passing <paramref name="parameters"/> to workflow runtime or persisting to orchestration history.
    /// </summary>
    /// <param name="parameters">Workflow start parameters; may be null or empty.</param>
    /// <exception cref="ArgumentException">Thrown when any value is ResolvedSecret, SecretMaterial, or another forbidden type.</exception>
    public static void ValidateParameters(IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
            return;

        foreach (var kv in parameters)
        {
            if (kv.Value is null)
                continue;
            if (IsSecretBearingType(kv.Value.GetType()))
                throw new ArgumentException(
                    $"Workflow parameters must not contain secret-bearing types. Key '{kv.Key}' has type {kv.Value.GetType().Name}. " +
                    "Use SecretRef-shaped values only; resolve secrets inside the activity/step via ISecretResolver.",
                    nameof(parameters));
        }
    }

    /// <summary>
    /// Validates that the signal payload is not a known secret-bearing type.
    /// Call before passing <paramref name="payload"/> to SignalAsync or persisting to orchestration history.
    /// </summary>
    /// <param name="payload">Signal payload; may be null.</param>
    /// <exception cref="ArgumentException">Thrown when payload is ResolvedSecret, SecretMaterial, or another forbidden type.</exception>
    public static void ValidateSignalPayload(object? payload)
    {
        if (payload is null)
            return;
        if (IsSecretBearingType(payload.GetType()))
            throw new ArgumentException(
                $"Signal payload must not be a secret-bearing type. Got {payload.GetType().Name}. " +
                "Pass only orchestration-safe values; resolve secrets inside the activity via ISecretResolver.",
                nameof(payload));
    }

    private static bool IsSecretBearingType(Type type)
    {
        if (type == typeof(ResolvedSecret) || type == typeof(SecretMaterial))
            return true;
        // Allow SecretRef (identifier only, no material)
        if (type == typeof(SecretRef))
            return false;
        // Check by name for types we cannot reference directly (e.g. from other assemblies)
        var name = type.FullName ?? type.Name;
        if (name.Contains("ResolvedSecret", StringComparison.Ordinal) ||
            name.Contains("SecretMaterial", StringComparison.Ordinal))
            return true;
        return false;
    }
}
