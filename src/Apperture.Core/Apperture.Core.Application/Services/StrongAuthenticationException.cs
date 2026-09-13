namespace Apperture.Core.Application.Services;

/// <summary>
/// Expected failure when a sensitive operation requires recent strong authentication.
/// </summary>
public sealed class StrongAuthenticationException : Exception
{
    public StrongAuthenticationException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Stable codes for APIs and the management UI.</summary>
public static class StrongAuthenticationErrorCodes
{
    /// <summary>The actor has the permission but not recent strong authentication.</summary>
    public const string Required = "auth.strong_authentication.required";

    /// <summary>No usable strong-authentication mechanism is configured (for example no Entra authentication context).</summary>
    public const string NotConfigured = "auth.strong_authentication.not_configured";

    /// <summary>Assurance was present but older than the recent-authentication window.</summary>
    public const string Expired = "auth.strong_authentication.expired";
}
