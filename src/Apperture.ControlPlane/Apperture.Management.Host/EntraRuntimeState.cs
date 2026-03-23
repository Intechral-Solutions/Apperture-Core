using Apperture.ControlPlane.Security;

namespace Apperture.Management.Host;

internal static class EntraRuntimeState
{
    public static bool IsUsable(EntraAuthOptions options)
    {
        if (!options.Enabled)
            return false;

        return !string.IsNullOrWhiteSpace(options.Authority)
            && !string.IsNullOrWhiteSpace(options.ClientId)
            && !string.IsNullOrWhiteSpace(options.ClientSecret);
    }
}
