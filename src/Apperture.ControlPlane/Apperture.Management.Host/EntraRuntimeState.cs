using Apperture.ControlPlane.Security;

namespace Apperture.Management.Host;

internal static class EntraRuntimeState
{
    public static bool IsUsable(EntraAuthOptions options) => EntraConfigurationState.IsUsable(options);
}
