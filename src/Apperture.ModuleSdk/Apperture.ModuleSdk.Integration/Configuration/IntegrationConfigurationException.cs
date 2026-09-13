namespace Apperture.ModuleSdk.Integration.Configuration;

/// <summary>Safe, deterministic configuration failure for modules and platform adapters.</summary>
public sealed class IntegrationConfigurationException : InvalidOperationException
{
    public IntegrationConfigurationException(string code, string message, string? settingKey = null, string? connectionKey = null)
        : base(message)
    {
        Code = code;
        SettingKey = settingKey;
        ConnectionKey = connectionKey;
    }

    public string Code { get; }

    public string? SettingKey { get; }

    public string? ConnectionKey { get; }

    public static IntegrationConfigurationException MissingSetting(string key) =>
        new("missing_setting", $"Required setting '{key}' is not configured.", settingKey: key);

    public static IntegrationConfigurationException InvalidSetting(string key, string detail) =>
        new("invalid_setting", $"Setting '{key}' is invalid: {detail}", settingKey: key);

    public static IntegrationConfigurationException MissingSecret(string key) =>
        new("missing_secret", $"Required secret for setting '{key}' is not configured.", settingKey: key);

    public static IntegrationConfigurationException InvalidSecretReference(string key) =>
        new("invalid_secret_reference", $"Secret reference for setting '{key}' is invalid or unavailable.", settingKey: key);

    public static IntegrationConfigurationException MissingConnection(string key) =>
        new("missing_connection", $"Required connection '{key}' is not bound.", connectionKey: key);

    public static IntegrationConfigurationException ConnectionUnavailable(string key, string detail) =>
        new("connection_unavailable", $"Connection '{key}' is unavailable: {detail}", connectionKey: key);

    public static IntegrationConfigurationException ForbiddenSecretDefault(string key) =>
        new("forbidden_secret_default", $"Sensitive setting '{key}' cannot declare a default value.", settingKey: key);

    public static IntegrationConfigurationException DuplicateDeclaration(string key) =>
        new("duplicate_declaration", $"Duplicate configuration declaration for key '{key}'.", settingKey: key);
}
