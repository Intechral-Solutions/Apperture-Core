namespace Apperture.ModuleSdk.Integration.Readiness;

/// <summary>Machine-readable readiness reason codes (safe for API and tests).</summary>
public static class ConfigurationReadinessReasonCodes
{
    public const string IntegrationDisabled = "integration_disabled";
    public const string ModuleNotInstalled = "module_not_installed";
    public const string MissingSetting = "missing_setting";
    public const string InvalidSetting = "invalid_setting";
    public const string MissingSecret = "missing_secret";
    public const string InvalidSecretReference = "invalid_secret_reference";
    public const string MissingConnectionBinding = "missing_connection_binding";
    public const string ConnectionUnavailable = "connection_unavailable";
    public const string ConnectionDisabled = "connection_disabled";
    public const string IncompatibleConnectionType = "incompatible_connection_type";
    public const string VerificationFailed = "verification_failed";
    public const string VerificationUnverified = "verification_unverified";
    public const string VerificationStale = "verification_stale";
    public const string VerificationUnsupported = "verification_unsupported";
    public const string ModuleCheckFailed = "module_check_failed";
    public const string OptionalDependencyUnavailable = "optional_dependency_unavailable";
}

/// <summary>Severity of a readiness reason.</summary>
public enum ConfigurationReadinessSeverity
{
    Blocking = 0,
    Advisory = 1,
}

/// <summary>One readiness reason with safe operator text (never includes secrets).</summary>
public sealed record ConfigurationReadinessReason(
    string Code,
    string Message,
    ConfigurationReadinessSeverity Severity = ConfigurationReadinessSeverity.Blocking,
    string? SettingKey = null,
    string? ConnectionKey = null,
    string? RemediationHint = null);
