using Apperture.Shared.Kernel;

namespace Apperture.RuntimePlane.Abstractions;

/// <summary>
/// Operation start request payload (control-plane to runtime-plane handoff contract).
/// Parameters must be orchestration-safe (no secret material).
/// </summary>
public sealed record StartOperationRequest(
    /// <summary>
    /// Operation definition id as a string (control plane maps this to the core domain <c>OperationId</c>).
    /// </summary>
    string OperationDefinitionId,
    IReadOnlyDictionary<string, object?>? Parameters,
    string? IdempotencyKey,
    string? CorrelationId);

