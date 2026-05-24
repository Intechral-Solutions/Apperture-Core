using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Microsoft.Extensions.Configuration;

namespace Apperture.RuntimePlane.Engine.PlatformEvents;

public sealed class RuntimePlatformSettingsResolver : IPlatformSettingsResolver
{
    private readonly IPlatformSettingDefinitionRegistry _registry;
    private readonly IPlatformSettingValueRepository _repository;
    private readonly IConfiguration _configuration;

    public RuntimePlatformSettingsResolver(
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
        return _registry.GetAll().Select(def => Resolve(def, dbByKey.GetValueOrDefault(def.Key))).ToList();
    }

    public async Task<PlatformSettingResolvedValue?> GetResolvedAsync(string key, CancellationToken cancellationToken = default)
    {
        var def = _registry.TryGet(key);
        if (def is null)
            return null;

        var db = await _repository.GetByKeyAsync(key, cancellationToken).ConfigureAwait(false);
        return Resolve(def, db);
    }

    public async Task<object?> ResolveRawValueAsync(string key, CancellationToken cancellationToken = default) =>
        (await GetResolvedAsync(key, cancellationToken).ConfigureAwait(false))?.CurrentValue;

    private PlatformSettingResolvedValue Resolve(PlatformSettingDefinition def, PlatformSettingValue? db)
    {
        if (db is not null)
            return ToResolved(def, PlatformSettingsValueCodec.ParseFromStoredJson(def, db.ValueJson), PlatformSettingValueSource.Database);

        var section = _configuration.GetSection(def.ConfigurationPath);
        if (!string.IsNullOrEmpty(section.Value) || section.GetChildren().Any())
        {
            var configValue = def.ValueType switch
            {
                PlatformSettingValueType.Boolean => section.Get<bool>(),
                PlatformSettingValueType.Integer => section.Get<int>(),
                PlatformSettingValueType.Duration => section.Get<TimeSpan>(),
                _ => def.DefaultValue
            };
            return ToResolved(def, configValue, PlatformSettingValueSource.ConfigurationDefault);
        }

        return ToResolved(def, def.DefaultValue, PlatformSettingValueSource.DefinitionDefault);
    }

    private static PlatformSettingResolvedValue ToResolved(PlatformSettingDefinition def, object currentValue, PlatformSettingValueSource source) =>
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
            MinInteger = def.MinInteger,
            MaxInteger = def.MaxInteger,
            MinDurationIso = def.MinDuration?.ToString("c", System.Globalization.CultureInfo.InvariantCulture)
        };
}
