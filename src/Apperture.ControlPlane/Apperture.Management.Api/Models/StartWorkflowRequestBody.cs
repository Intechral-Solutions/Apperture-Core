namespace Apperture.Management.Api.Models;

/// <summary>
/// Request body for POST workflows/{workflowId}/start. WorkflowDefinitionId comes from the route.
/// </summary>
public sealed class StartWorkflowRequestBody
{
    /// <summary>Workflow parameters. Must not contain ResolvedSecret or SecretMaterial; use SecretRef-shaped values only.</summary>
    public IReadOnlyDictionary<string, object?>? Parameters { get; init; }

    /// <summary>Optional idempotency key.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Optional correlation id.</summary>
    public string? CorrelationId { get; init; }
}
