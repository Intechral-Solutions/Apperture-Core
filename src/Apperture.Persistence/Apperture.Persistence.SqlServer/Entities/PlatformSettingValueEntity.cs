namespace Apperture.Persistence.SqlServer;

public sealed class PlatformSettingValueEntity
{
    public string Id { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string ScopeKind { get; set; } = string.Empty;

    public string ScopeId { get; set; } = string.Empty;

    public string ValueJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public string? UpdatedByPrincipalId { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
