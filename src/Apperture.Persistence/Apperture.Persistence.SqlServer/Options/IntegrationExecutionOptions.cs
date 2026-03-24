namespace Apperture.Persistence.SqlServer.Options;

/// <summary>
/// Fallback integration scope for workflow step execution when the persisted start request has no IntegrationId (Epic 54).
/// </summary>
public sealed class IntegrationExecutionOptions
{
    public const string SectionName = "Apperture:IntegrationExecution";

    /// <summary>
    /// Used when the persisted workflow start request has no IntegrationId.
    /// </summary>
    public string? DefaultIntegrationId { get; set; }
}
