namespace Apperture.ControlPlane.Security;

/// <summary>
/// Whether Entra can actually be used for sign-in with the current (config + database overlay) options.
/// A link to an unusable provider is not a viable sign-in path.
/// </summary>
public static class EntraConfigurationState
{
    public static bool IsUsable(EntraAuthOptions? options)
    {
        if (options is null || !options.Enabled)
            return false;

        return !string.IsNullOrWhiteSpace(options.Authority)
            && !string.IsNullOrWhiteSpace(options.ClientId)
            && !string.IsNullOrWhiteSpace(options.ClientSecret);
    }
}
