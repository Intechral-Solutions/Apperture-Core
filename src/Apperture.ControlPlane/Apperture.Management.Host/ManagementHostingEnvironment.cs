using Microsoft.Extensions.Hosting;

namespace Apperture.Management.Host;

/// <summary>
/// Production-like environments take the public-origin and Secure-cookie invariants. Development and the
/// <c>Testing</c> host used by integration and browser proofs stay on the local HTTP topology.
/// </summary>
internal static class ManagementHostingEnvironment
{
    public static bool IsLocalHttpTopology(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");

    public static bool RequiresPublicUiOrigin(IHostEnvironment environment) =>
        !IsLocalHttpTopology(environment);

    public static bool RequiresSecureCookies(IHostEnvironment environment) =>
        !IsLocalHttpTopology(environment);
}
