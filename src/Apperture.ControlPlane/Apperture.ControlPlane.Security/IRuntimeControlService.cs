using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

public interface IRuntimeControlService
{
    /// <summary>
    /// Requests a shutdown control intent for a runtime.
    /// Returns null when the runtime is missing or not eligible (does not declare the required control capability).
    /// </summary>
    ValueTask<RuntimeControlRequestResult?> RequestShutdownAsync(string runtimeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all shutdown control requests for the runtime (queued, applied, rejected, failed).
    /// </summary>
    ValueTask<IReadOnlyList<RuntimeControlRequestResult>> GetShutdownRequestsAsync(string runtimeId, CancellationToken cancellationToken = default);
}

public sealed record RuntimeControlRequestResult(
    long Id,
    string RuntimeId,
    string ControlKind,
    string Status,
    DateTimeOffset RequestedAt,
    string? RequestedBy,
    DateTimeOffset? AppliedAt,
    string? AppliedBy,
    string? LastError);

