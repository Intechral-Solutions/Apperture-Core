using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;

namespace Apperture.ControlPlane.Security;

public sealed class IntegrationSettingsResolver : IIntegrationSettingsResolver
{
    private readonly IIntegrationSettingDefinitionRegistry _registry;
    private readonly IPlatformSettingValueRepository _repository;

    public IntegrationSettingsResolver(
        IIntegrationSettingDefinitionRegistry registry,
        IPlatformSettingValueRepository repository)
    {
        _registry = registry;
        _repository = repository;
    }

    public async Task<IReadOnlyList<PlatformSettingResolvedValue>> ListResolvedAsync(
        string integrationId,
        CancellationToken cancellationToken = default)
    {
        var dbRows = await _repository
            .ListScopedAsync(PlatformSettingScopeConstants.IntegrationKind, integrationId, cancellationToken)
            .ConfigureAwait(false);
        var dbByKey = dbRows.ToDictionary(x => x.Key, StringComparer.Ordinal);
        return _registry.GetAll()
            .Select(def => ResolveDefinition(def, integrationId, dbByKey.GetValueOrDefault(def.Key)))
            .ToList();
    }

    public async Task<PlatformSettingResolvedValue?> GetResolvedAsync(
        string integrationId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var def = _registry.TryGet(key);
        if (def is null)
            return null;

        var db = await _repository
            .GetScopedAsync(key, PlatformSettingScopeConstants.IntegrationKind, integrationId, cancellationToken)
            .ConfigureAwait(false);
        return ResolveDefinition(def, integrationId, db);
    }

    private static PlatformSettingResolvedValue ResolveDefinition(
        PlatformSettingDefinition def,
        string integrationId,
        PlatformSettingValue? db)
    {
        if (db is not null)
        {
            return ToResolved(
                def,
                integrationId,
                PlatformSettingsValueCodec.ParseFromStoredJson(def, db.ValueJson),
                PlatformSettingValueSource.Database,
                db.RowVersion);
        }

        return ToResolved(
            def,
            integrationId,
            NormalizeDefaultValue(def),
            PlatformSettingValueSource.DefinitionDefault,
            null);
    }

    private static object NormalizeDefaultValue(PlatformSettingDefinition def) =>
        def.ValueType == PlatformSettingValueType.StringArray
            ? PlatformSettingsValueCodec.CoerceStringArray(def.DefaultValue)
            : def.DefaultValue;

    private static PlatformSettingResolvedValue ToResolved(
        PlatformSettingDefinition def,
        string integrationId,
        object currentValue,
        PlatformSettingValueSource source,
        byte[]? rowVersion) =>
        new()
        {
            Key = def.Key,
            ValueScopeKind = PlatformSettingScopeConstants.IntegrationKind,
            ValueScopeId = integrationId,
            DisplayName = def.DisplayName,
            Description = def.Description,
            Category = def.Category,
            ValueType = def.ValueType,
            InputKind = def.InputKind,
            OptionProviderKey = def.OptionProviderKey,
            OptionProviderContextKind = def.OptionProviderContextKind,
            AllowedValues = def.AllowedValues ?? PlatformSettingResolvedValue.EmptyAllowedValues,
            CurrentValue = def.ValueType == PlatformSettingValueType.StringArray
                ? PlatformSettingsValueCodec.CoerceStringArray(currentValue)
                : currentValue,
            Source = source,
            IsEditable = def.Editable,
            RequiresRestart = def.RequiresRestart,
            RowVersionBase64 = rowVersion is { Length: > 0 } ? Convert.ToBase64String(rowVersion) : null,
            MinInteger = def.MinInteger,
            MaxInteger = def.MaxInteger,
            MinDurationIso = def.MinDuration?.ToString("c", System.Globalization.CultureInfo.InvariantCulture),
            MinItems = def.MinItems,
            MaxItems = def.MaxItems
        };
}
