namespace Apperture.ControlPlane.Security;

/// <summary>
/// Expected identity-link mutation failure with a stable client-facing code.
/// </summary>
public sealed class IdentityLinkException : Exception
{
    public IdentityLinkException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Stable identity-link error codes for APIs and clients.</summary>
public static class IdentityLinkErrorCodes
{
    public const string InvalidSource = "identity_link.invalid_source";
    public const string MissingExternalId = "identity_link.missing_external_id";
    public const string InvalidEntraExternalId = "identity_link.invalid_entra_external_id";
    public const string UserNotFound = "identity_link.user_not_found";
    public const string ExternalIdInUse = "identity_link.external_id_in_use";
}
