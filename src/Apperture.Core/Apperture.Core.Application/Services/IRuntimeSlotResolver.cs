namespace Apperture.Core.Application.Services;

/// <summary>
/// Single runtime entry point for obtaining a live connection-backed client.
/// Resolves by (moduleId, slotName) only; requires an active connection binding for the slot.
/// No connection-by-id or arbitrary connection resolution for module code.
/// </summary>
public interface IRuntimeSlotResolver
{
    /// <summary>
    /// Resolves the slot to a live client of type T. Returns null if no active connection binding exists for the slot.
    /// For capability-only bindings, returns null (future: separate handling).
    /// </summary>
    /// <typeparam name="T">Expected client type (e.g. IDbConnection).</typeparam>
    /// <param name="moduleId">Module that declares the slot.</param>
    /// <param name="slotName">Slot name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved client, or null if the slot has no active connection binding.</returns>
    ValueTask<T?> ResolveSlotAsync<T>(string moduleId, string slotName, CancellationToken cancellationToken = default) where T : class;
}
