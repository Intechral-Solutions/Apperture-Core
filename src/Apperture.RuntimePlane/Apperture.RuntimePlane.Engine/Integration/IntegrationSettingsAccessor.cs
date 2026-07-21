using Apperture.Core.Application.Execution;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.ModuleSdk.Integration.Configuration;

namespace Apperture.RuntimePlane.Engine.Integration;

/// <inheritdoc />
public sealed class IntegrationSettingsAccessor : IIntegrationSettingsAccessor
{
    private readonly IIntegrationExecutionScopeAccessor _scope;
    private readonly IIntegrationSettingsResolver _settingsResolver;
    private readonly ISecretResolver _secretResolver;

    public IntegrationSettingsAccessor(
        IIntegrationExecutionScopeAccessor scope,
        IIntegrationSettingsResolver settingsResolver,
        ISecretResolver secretResolver)
    {
        _scope = scope;
        _settingsResolver = settingsResolver;
        _secretResolver = secretResolver;
    }

    /// <inheritdoc />
    public T GetRequired<T>(string key)
    {
        var resolved = GetResolvedOrThrow(key);
        if (resolved.IsSensitive)
            throw IntegrationConfigurationException.MissingSetting(key);

        if (resolved.CurrentValue is null || IsBlank(resolved.CurrentValue))
            throw IntegrationConfigurationException.MissingSetting(key);

        return ConvertValue<T>(key, resolved.CurrentValue);
    }

    /// <inheritdoc />
    public T GetOrDefault<T>(string key, T defaultValue)
    {
        var resolved = TryGetResolved(key);
        if (resolved is null || resolved.IsSensitive || resolved.CurrentValue is null || IsBlank(resolved.CurrentValue))
            return defaultValue;

        return ConvertValue<T>(key, resolved.CurrentValue);
    }

    /// <inheritdoc />
    public bool IsConfigured(string key)
    {
        var resolved = TryGetResolved(key);
        if (resolved is null)
            return false;

        if (resolved.IsSensitive)
            return resolved.IsSecretConfigured;

        return resolved.CurrentValue is not null && !IsBlank(resolved.CurrentValue);
    }

    /// <inheritdoc />
    public bool IsSecretConfigured(string key)
    {
        var resolved = TryGetResolved(key);
        return resolved is { IsSensitive: true, IsSecretConfigured: true };
    }

    /// <inheritdoc />
    public async ValueTask<string> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        var resolved = GetResolvedOrThrow(key);
        if (!resolved.IsSensitive)
            throw IntegrationConfigurationException.InvalidSetting(key, "Setting is not sensitive.");

        if (!resolved.IsSecretConfigured || resolved.CurrentValue is not string secretRef || string.IsNullOrWhiteSpace(secretRef))
            throw IntegrationConfigurationException.MissingSecret(key);

        if (!Guid.TryParse(secretRef.Trim(), out _))
            throw IntegrationConfigurationException.InvalidSecretReference(key);

        try
        {
            var material = await _secretResolver
                .ResolveAsync(new SecretRef(secretRef.Trim()), cancellationToken)
                .ConfigureAwait(false);
            var plaintext = material.Material.OpaqueString;
            if (string.IsNullOrWhiteSpace(plaintext))
                throw IntegrationConfigurationException.InvalidSecretReference(key);
            return plaintext;
        }
        catch (IntegrationConfigurationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw IntegrationConfigurationException.InvalidSecretReference(key);
        }
    }

    private PlatformSettingResolvedValue GetResolvedOrThrow(string key)
    {
        var resolved = TryGetResolved(key);
        if (resolved is null)
            throw IntegrationConfigurationException.MissingSetting(key);
        return resolved;
    }

    private PlatformSettingResolvedValue? TryGetResolved(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var integrationId = RequireIntegrationId();
        return _settingsResolver
            .GetResolvedAsync(integrationId, key)
            .GetAwaiter()
            .GetResult();
    }

    private string RequireIntegrationId()
    {
        if (string.IsNullOrWhiteSpace(_scope.IntegrationId))
            throw new InvalidOperationException("Integration execution scope is not available for settings access.");
        return _scope.IntegrationId;
    }

    private static bool IsBlank(object value) =>
        value is string s && string.IsNullOrWhiteSpace(s);

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
}
