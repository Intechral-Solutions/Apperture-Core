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
        {
            return ToResolved(
                def,
                PlatformSettingsValueCodec.ParseFromStoredJson(def, db.ValueJson),
                PlatformSettingValueSource.Database);
        }

        var fromConfig = TryGetFromConfiguration(def);
        if (fromConfig is not null)
            return ToResolved(def, fromConfig, PlatformSettingValueSource.ConfigurationDefault);

        return ToResolved(def, NormalizeDefaultValue(def), PlatformSettingValueSource.DefinitionDefault);
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
            PlatformSettingValueType.StringArray => TryGetStringArrayFromConfiguration(section),
            _ => null
        };
    }

    private static string[]? TryGetStringArrayFromConfiguration(IConfigurationSection section)
    {
        var direct = section.Get<string[]>();
        if (direct is { Length: > 0 })
            return direct;

        var indexed = section.GetChildren()
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!)
            .ToArray();

        return indexed.Length > 0 ? indexed : null;
    }

    private static object NormalizeDefaultValue(PlatformSettingDefinition def) =>
        def.ValueType == PlatformSettingValueType.StringArray
            ? PlatformSettingsValueCodec.CoerceStringArray(def.DefaultValue)
            : def.DefaultValue;

    private static PlatformSettingResolvedValue ToResolved(
        PlatformSettingDefinition def,
        object currentValue,
        PlatformSettingValueSource source) =>
        new()
        {
            Key = def.Key,
            DisplayName = def.DisplayName,
            Description = def.Description,
            Category = def.Category,
            ValueType = def.ValueType,
            InputKind = def.InputKind,
            AllowedValues = def.AllowedValues ?? PlatformSettingResolvedValue.EmptyAllowedValues,
            CurrentValue = def.ValueType == PlatformSettingValueType.StringArray
                ? PlatformSettingsValueCodec.CoerceStringArray(currentValue)
                : currentValue,
            Source = source,
            IsEditable = def.Editable,
            RequiresRestart = def.RequiresRestart,
            MinInteger = def.MinInteger,
            MaxInteger = def.MaxInteger,
            MinDurationIso = def.MinDuration?.ToString("c", System.Globalization.CultureInfo.InvariantCulture),
            MinItems = def.MinItems,
            MaxItems = def.MaxItems
        };
}
