namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Legacy table shape for connector definitions. Not used as the catalog source of truth:
/// the control plane and runtime use <see cref="Apperture.Core.Application.Services.IConnectorDefinitionRegistry"/> (host-seeded in-process definitions).
/// </summary>
public sealed class ConnectorDefinitionEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ModuleId { get; set; }
    public string RequiredInputSchemaJson { get; set; } = "{}";
}
