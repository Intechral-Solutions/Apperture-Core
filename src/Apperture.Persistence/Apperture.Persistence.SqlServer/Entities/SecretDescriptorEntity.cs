namespace Apperture.Persistence.SqlServer;

public sealed class SecretDescriptorEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    public int MaterialType { get; set; }
    public string? ActiveVersion { get; set; }
    public int State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
