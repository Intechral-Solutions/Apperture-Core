namespace Apperture.Management.Api.Models;

/// <summary>
/// Request body for POST operations/{operationId}/start.
/// </summary>
public sealed class StartOperationRequestBody
{
    /// <summary>Operation parameters. Must be orchestration-safe (no secret material).</summary>
    public IReadOnlyDictionary<string, object?>? Parameters { get; init; }

    /// <summary>Optional idempotency key.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Optional correlation id.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Required integration id; must match the operation's module.</summary>
    public string? IntegrationId { get; init; }

    /// <summary>Optional module procedure id when launched via the procedures API (ADR-0029).</summary>
    public string? ProcedureId { get; init; }
}

