namespace Apperture.Persistence.SqlServer;

public sealed class SecretVersionEntity
{
    public Guid SecretId { get; set; }
    public string Version { get; set; } = "";
    public int State { get; set; }
    public byte[] ProtectedPayload { get; set; } = [];
    public string KeyId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
