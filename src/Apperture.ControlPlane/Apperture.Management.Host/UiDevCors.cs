using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Apperture.Management.Host;

/// <summary>
/// Development split-host CORS. Registered and applied only when <c>IHostEnvironment.IsDevelopment()</c>.
/// Production and Testing do not register this policy.
/// </summary>
public static class UiDevCors
{
    public const string PolicyName = "UiDev";

    public static void Configure(CorsPolicyBuilder policy, string origin)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy
            .WithOrigins(origin)
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
            .WithHeaders("Content-Type", BrowserRequest.HeaderName, BackgroundActivity.HeaderName)
            .AllowCredentials();
    }
}
