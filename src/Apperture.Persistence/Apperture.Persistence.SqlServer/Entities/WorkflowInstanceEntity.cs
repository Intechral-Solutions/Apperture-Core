namespace Apperture.Persistence.SqlServer;

public sealed class WorkflowInstanceEntity
{
    public string Id { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? CorrelationKey { get; set; }

    /// <summary>Integration that launched this workflow, when known (Epic 54).</summary>
    public string? IntegrationId { get; set; }

    /// <summary>
    /// When status is <c>running</c>: the workflow step currently executing (or about to execute after the last boundary).
    /// When status is <c>parked</c>: the next step to run after resume (resume pointer); that step has not executed yet in this parked segment.
    /// </summary>
    public string? CurrentStepName { get; set; }
    public int Version { get; set; }
}
