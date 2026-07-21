using System.Text;
using System.Text.Json;
using Apperture.ModuleSdk.Integration.Configuration;
using Apperture.ModuleSdk.Integration.Readiness;

namespace Apperture.ModuleSdk.Integration.Testing;

/// <summary>In-memory configuration harness for integration procedure tests (persistence-free).</summary>
public sealed class IntegrationConfigurationTestContext
{
    private readonly Dictionary<string, object?> _settings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SecretPresenceState> _secrets = new(StringComparer.Ordinal);
    private readonly HashSet<string> _invalidSettings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ConnectionEvidenceSnapshot> _connections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _connectionCapabilities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _secretValues = new(StringComparer.Ordinal);
    private readonly List<ModuleCheckFailure> _moduleChecks = [];

    public bool ModuleInstalled { get; set; } = true;

    public bool IntegrationEnabled { get; set; } = true;

    public IntegrationConfigurationTestContext WithSetting(string key, object? value)
    {
        _settings[key] = value;
        return this;
    }

    public IntegrationConfigurationTestContext WithInvalidSetting(string key, object? value = null)
    {
        _settings[key] = value;
        _invalidSettings.Add(key);
        return this;
    }

    public IntegrationConfigurationTestContext WithSecret(string key, string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        _secrets[key] = SecretPresenceState.Present;
        _secretValues[key] = plaintext;
        return this;
    }

    public IntegrationConfigurationTestContext WithMissingSecret(string key)
    {
        _secrets[key] = SecretPresenceState.Missing;
        _secretValues.Remove(key);
        return this;
    }

    public IntegrationConfigurationTestContext WithInvalidSecretReference(string key)
    {
        _secrets[key] = SecretPresenceState.InvalidReference;
        _secretValues.Remove(key);
        return this;
    }

    public IntegrationConfigurationTestContext WithConnectionBinding(
        string key,
        object capability,
        ConnectionEvidenceSnapshot? evidence = null)
    {
        ArgumentNullException.ThrowIfNull(capability);
        _connectionCapabilities[key] = capability;
        _connections[key] = evidence ?? new ConnectionEvidenceSnapshot(
            IsBound: true,
            IsAvailable: true,
            VerificationStatus: ConnectionVerificationStatusKind.Verified);
        return this;
    }

    public IntegrationConfigurationTestContext WithConnectionEvidence(string key, ConnectionEvidenceSnapshot evidence)
    {
        _connections[key] = evidence;
        return this;
    }

    public IntegrationConfigurationTestContext WithVerificationState(
        string key,
        ConnectionVerificationStatusKind status)
    {
        if (_connections.TryGetValue(key, out var existing))
        {
            _connections[key] = existing with { VerificationStatus = status };
        }
        else
        {
            _connections[key] = new ConnectionEvidenceSnapshot(
                IsBound: true,
                VerificationStatus: status);
        }

        return this;
    }

    public IntegrationConfigurationTestContext WithModuleCheckFailure(ModuleCheckFailure failure)
    {
        _moduleChecks.Add(failure);
        return this;
    }

    public ConfigurationEvidenceSnapshot ToEvidenceSnapshot() =>
        new(
            ModuleInstalled,
            IntegrationEnabled,
            new Dictionary<string, object?>(_settings, StringComparer.Ordinal),
            new Dictionary<string, SecretPresenceState>(_secrets, StringComparer.Ordinal),
            new HashSet<string>(_invalidSettings, StringComparer.Ordinal),
            new Dictionary<string, ConnectionEvidenceSnapshot>(_connections, StringComparer.Ordinal),
            _moduleChecks.ToList());

    public ConfigurationReadinessResult EvaluateIntegration(IIntegrationConfigurationContributor contributor) =>
        ConfigurationReadinessEvaluator.EvaluateIntegration(
            contributor.GetSettingDefinitions(),
            contributor.GetConnectionRequirements(),
            ToEvidenceSnapshot(),
            DateTimeOffset.UtcNow);

    public ProcedureReadinessResult EvaluateProcedure(
        IIntegrationConfigurationContributor contributor,
        string procedureId) =>
        ConfigurationReadinessEvaluator.EvaluateProcedure(
            procedureId,
            contributor.GetSettingDefinitions(),
            contributor.GetConnectionRequirements(),
            contributor.GetProcedureRequirements(),
            ToEvidenceSnapshot(),
            DateTimeOffset.UtcNow);

    public IIntegrationSettingsAccessor CreateSettingsAccessor() =>
        new TestIntegrationSettingsAccessor(_settings, _secrets, _secretValues);

    public IIntegrationConnectionAccessor CreateConnectionAccessor() =>
        new TestIntegrationConnectionAccessor(_connectionCapabilities, _connections);
}

internal sealed class TestIntegrationSettingsAccessor : IIntegrationSettingsAccessor
{
    private readonly IReadOnlyDictionary<string, object?> _settings;
    private readonly IReadOnlyDictionary<string, SecretPresenceState> _secrets;
    private readonly IReadOnlyDictionary<string, string> _secretValues;

    public TestIntegrationSettingsAccessor(
        IReadOnlyDictionary<string, object?> settings,
        IReadOnlyDictionary<string, SecretPresenceState> secrets,
        IReadOnlyDictionary<string, string> secretValues)
    {
        _settings = settings;
        _secrets = secrets;
        _secretValues = secretValues;
    }

    public T GetRequired<T>(string key)
    {
        if (!_settings.TryGetValue(key, out var value) || value is null)
            throw IntegrationConfigurationException.MissingSetting(key);

        return ConvertValue<T>(key, value);
    }

    public T GetOrDefault<T>(string key, T defaultValue)
    {
        if (!_settings.TryGetValue(key, out var value) || value is null)
            return defaultValue;

        return ConvertValue<T>(key, value);
    }

    public bool IsConfigured(string key) =>
        (_settings.TryGetValue(key, out var value) && value is not null && !IsBlank(value))
        || IsSecretConfigured(key);

    public bool IsSecretConfigured(string key) =>
        _secrets.TryGetValue(key, out var state) && state == SecretPresenceState.Present;

    public ValueTask<string> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!_secrets.TryGetValue(key, out var state))
            throw IntegrationConfigurationException.MissingSecret(key);

        if (state == SecretPresenceState.InvalidReference)
            throw IntegrationConfigurationException.InvalidSecretReference(key);

        if (state != SecretPresenceState.Present || !_secretValues.TryGetValue(key, out var plaintext))
            throw IntegrationConfigurationException.MissingSecret(key);

        return ValueTask.FromResult(plaintext);
    }

    private static T ConvertValue<T>(string key, object value)
    {
        try
        {
            if (value is T typed)
                return typed;

            if (typeof(T) == typeof(string))
                return (T)(object)Convert.ToString(value)!;

            if (typeof(T) == typeof(bool))
                return (T)(object)Convert.ToBoolean(value);

            if (typeof(T) == typeof(int))
                return (T)(object)Convert.ToInt32(value);

            if (typeof(T) == typeof(decimal))
                return (T)(object)Convert.ToDecimal(value);

            if (typeof(T) == typeof(Uri))
                return (T)(object)new Uri(Convert.ToString(value)!, UriKind.Absolute);

            if (typeof(T) == typeof(TimeSpan))
            {
                if (value is TimeSpan ts)
                    return (T)(object)ts;
                return (T)(object)System.Xml.XmlConvert.ToTimeSpan(Convert.ToString(value)!);
            }

            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch (Exception ex) when (ex is not IntegrationConfigurationException)
        {
            throw IntegrationConfigurationException.InvalidSetting(key, ex.Message);
        }
    }

    private static bool IsBlank(object value) =>
        value is string s && string.IsNullOrWhiteSpace(s);
}

internal sealed class TestIntegrationConnectionAccessor : IIntegrationConnectionAccessor
{
    private readonly IReadOnlyDictionary<string, object> _capabilities;
    private readonly IReadOnlyDictionary<string, ConnectionEvidenceSnapshot> _evidence;

    public TestIntegrationConnectionAccessor(
        IReadOnlyDictionary<string, object> capabilities,
        IReadOnlyDictionary<string, ConnectionEvidenceSnapshot> evidence)
    {
        _capabilities = capabilities;
        _evidence = evidence;
    }

    public ValueTask<T> GetRequiredAsync<T>(string connectionKey, CancellationToken cancellationToken = default)
        where T : class
    {
        var value = TryGetAsync<T>(connectionKey, cancellationToken).AsTask().GetAwaiter().GetResult();
        if (value is null)
            throw IntegrationConfigurationException.MissingConnection(connectionKey);
        return ValueTask.FromResult(value);
    }

    public ValueTask<T?> TryGetAsync<T>(string connectionKey, CancellationToken cancellationToken = default)
        where T : class
    {
        if (!_capabilities.TryGetValue(connectionKey, out var capability))
            return ValueTask.FromResult<T?>(null);

        if (capability is not T typed)
        {
            throw IntegrationConfigurationException.ConnectionUnavailable(
                connectionKey,
                $"Bound capability is {capability.GetType().FullName}, not {typeof(T).FullName}.");
        }

        return ValueTask.FromResult<T?>(typed);
    }

    public ValueTask<bool> IsBoundAsync(string connectionKey, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            _evidence.TryGetValue(connectionKey, out var snap) && snap.IsBound
            || _capabilities.ContainsKey(connectionKey));
}

/// <summary>Asserts secret plaintext never appears in captured test surfaces.</summary>
public static class SecretLeakageAssertions
{
    public static void AssertSecretAbsent(
        string secretPlaintext,
        IntegrationProcedureExecution execution,
        params string?[] additionalSurfaces)
    {
        ArgumentException.ThrowIfNullOrEmpty(secretPlaintext);
        ArgumentNullException.ThrowIfNull(execution);

        var surfaces = new List<string>();
        foreach (var evt in execution.PublishedEvents)
        {
            surfaces.Add(evt.Type);
            surfaces.Add(evt.SignalName);
            surfaces.Add(Serialize(evt.Payload));
        }

        foreach (var log in execution.Logs)
        {
            surfaces.Add(log.Code);
            surfaces.Add(log.Message);
            surfaces.Add(Serialize(log.Data));
        }

        foreach (var group in execution.OperationGroups)
            surfaces.Add(Serialize(group));

        foreach (var item in execution.OperationItems)
            surfaces.Add(Serialize(item));

        surfaces.Add(Serialize(execution.Result));
        surfaces.AddRange(additionalSurfaces.Where(s => !string.IsNullOrEmpty(s))!);

        foreach (var surface in surfaces)
        {
            if (surface.Contains(secretPlaintext, StringComparison.Ordinal))
                throw new InvalidOperationException("Secret plaintext leaked into a captured test surface.");
        }
    }

    public static void AssertSecretAbsentFromReadiness(string secretPlaintext, ConfigurationReadinessResult readiness)
    {
        ArgumentException.ThrowIfNullOrEmpty(secretPlaintext);
        foreach (var reason in readiness.Reasons)
        {
            if (reason.Message.Contains(secretPlaintext, StringComparison.Ordinal)
                || (reason.RemediationHint?.Contains(secretPlaintext, StringComparison.Ordinal) ?? false))
            {
                throw new InvalidOperationException("Secret plaintext leaked into readiness text.");
            }
        }
    }

    private static string Serialize(object? value)
    {
        if (value is null)
            return string.Empty;
        if (value is string s)
            return s;
        try
        {
            return JsonSerializer.Serialize(value);
        }
        catch
        {
            return value.ToString() ?? string.Empty;
        }
    }
}
