using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Microsoft.Extensions.Configuration;

namespace Apperture.ControlPlane.Security;

public sealed class PlatformSettingsResolver : IPlatformSettingsResolver
{
    private readonly IPlatformSettingDefinitionRegistry _registry;
    private readonly IPlatformSettingValueRepository _repository;
    private readonly IConfiguration _configuration;

    public PlatformSettingsResolver(
        IPlatformSettingDefinitionRegistry registry,
        IPlatformSettingValueRepository repository,
        IConfiguration configuration)
    {
        _registry = registry;
        _repository = repository;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<PlatformSettingResolvedValue>> ListResolvedAsync(CancellationToken cancellationToken = default)
    {
        var dbRows = await _repository.ListInstanceAsync(cancellationToken).ConfigureAwait(false);
        var dbByKey = dbRows.ToDictionary(x => x.Key, StringComparer.Ordinal);
        return _registry.GetAll()
            .Select(def => ResolveDefinition(def, dbByKey.GetValueOrDefault(def.Key)))
            .ToList();
    }

    public async Task<PlatformSettingResolvedValue?> GetResolvedAsync(string key, CancellationToken cancellationToken = default)
    {
        var def = _registry.TryGet(key);
        if (def is null)
            return null;

        var db = await _repository.GetByKeyAsync(key, cancellationToken).ConfigureAwait(false);
        return ResolveDefinition(def, db);
    }

    public async Task<object?> ResolveRawValueAsync(string key, CancellationToken cancellationToken = default)
    {
        var resolved = await GetResolvedAsync(key, cancellationToken).ConfigureAwait(false);
        return resolved?.CurrentValue;
    }

    private PlatformSettingResolvedValue ResolveDefinition(PlatformSettingDefinition def, PlatformSettingValue? db)
    {
        if (db is not null)
        {
            return ToResolved(def, PlatformSettingsValueCodec.ParseFromStoredJson(def, db.ValueJson), PlatformSettingValueSource.Database, db.RowVersion);
        }

        var fromConfig = TryGetFromConfiguration(def);
        if (fromConfig is not null)
        {
            return ToResolved(def, fromConfig, PlatformSettingValueSource.ConfigurationDefault, null);
        }

        return ToResolved(def, def.DefaultValue, PlatformSettingValueSource.DefinitionDefault, null);
    }

    private object? TryGetFromConfiguration(PlatformSettingDefinition def)
    {
        var section = _configuration.GetSection(def.ConfigurationPath);
        if (string.IsNullOrEmpty(section.Value) && !section.GetChildren().Any())
            return null;

        return def.ValueType switch
        {
            PlatformSettingValueType.Boolean => section.Get<bool>(),
            PlatformSettingValueType.Integer => section.Get<int>(),
            PlatformSettingValueType.Duration => section.Get<TimeSpan>(),
            _ => null
        };
    }

    private static PlatformSettingResolvedValue ToResolved(
        PlatformSettingDefinition def,
        object currentValue,
        PlatformSettingValueSource source,
        byte[]? rowVersion) =>
        new()
        {
            Key = def.Key,
            DisplayName = def.DisplayName,
            Description = def.Description,
            Category = def.Category,
            ValueType = def.ValueType,
            CurrentValue = currentValue,
            Source = source,
            IsEditable = def.Editable,
            RequiresRestart = def.RequiresRestart,
            RowVersionBase64 = rowVersion is { Length: > 0 } ? Convert.ToBase64String(rowVersion) : null,
            MinInteger = def.MinInteger,
            MaxInteger = def.MaxInteger,
            MinDurationIso = def.MinDuration?.ToString("c", System.Globalization.CultureInfo.InvariantCulture)
        };
}
