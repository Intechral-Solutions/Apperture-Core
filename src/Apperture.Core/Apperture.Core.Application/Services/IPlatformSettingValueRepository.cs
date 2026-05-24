namespace Apperture.Core.Application.Services;

/// <summary>Persisted platform setting override at instance scope.</summary>
public sealed class PlatformSettingValue
{
    public required string Key { get; set; }

    public required string ScopeKind { get; set; }

    public required string ScopeId { get; set; }

    public required string ValueJson { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public string? UpdatedByPrincipalId { get; set; }

    public byte[]? RowVersion { get; set; }
}

public interface IPlatformSettingValueRepository
{
    Task<PlatformSettingValue?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlatformSettingValue>> ListInstanceAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(PlatformSettingValue value, byte[]? expectedRowVersion, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}
