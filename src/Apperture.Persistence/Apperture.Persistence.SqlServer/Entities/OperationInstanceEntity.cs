namespace Apperture.Persistence.SqlServer;

public sealed class OperationInstanceEntity
{
    public string Id { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? CorrelationKey { get; set; }

    /// <summary>
    /// When status is <c>running</c>, the current handler step name.
    /// </summary>
    public string? CurrentStepName { get; set; }

    public int Version { get; set; }

    /// <summary>
    /// CTX-2: immutable ResourceContext snapshot copied from the start request/run at creation.
    /// Never re-derived from current Integration ownership. Null for pre-CTX-2 rows and for
    /// legitimately contextless (System) starts.
    /// </summary>
    public string? ResourceContextId { get; set; }
}

