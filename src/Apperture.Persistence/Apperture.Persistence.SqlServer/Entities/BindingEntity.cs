namespace Apperture.Persistence.SqlServer;

public sealed class BindingEntity
{
    public string Id { get; set; } = "";
    public string? IntegrationId { get; set; }
    public string ModuleId { get; set; } = "";
    public string SlotName { get; set; } = "";
    public string ContractType { get; set; } = "";
    public string? ConnectionId { get; set; }
    public string? CapabilityId { get; set; }
    public int State { get; set; }
}
