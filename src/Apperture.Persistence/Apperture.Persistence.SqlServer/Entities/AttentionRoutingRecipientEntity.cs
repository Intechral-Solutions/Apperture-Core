namespace Apperture.Persistence.SqlServer;

public sealed class AttentionRoutingRecipientEntity
{
    public string Id { get; set; } = "";
    public string? InstanceAttentionRoutingId { get; set; }
    public string? IntegrationAttentionRoutingId { get; set; }
    public string Role { get; set; } = "";
    public string Address { get; set; } = "";
}
