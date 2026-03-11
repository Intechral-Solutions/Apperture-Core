namespace Apperture.Persistence.SqlServer;

public sealed class ConnectorDefinitionEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ModuleId { get; set; }
    public string RequiredInputSchemaJson { get; set; } = "{}";
}
