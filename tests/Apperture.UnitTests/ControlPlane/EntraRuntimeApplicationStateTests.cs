using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class EntraRuntimeApplicationStateTests
{
    [Fact]
    public void MayRequireRestart_WhenNoActiveRegistration_IsFalse()
    {
        EntraRuntimeApplicationState.MayRequireRestartToApplyChanges(null, new EntraAuthOptions { Enabled = true })
            .Should().BeFalse();
    }

    [Fact]
    public void MayRequireRestart_WhenActiveButProcessOptionsMatch_IsFalse()
    {
        var active = new EntraInstanceRegistration
        {
            Enabled = true,
            Authority = "https://login.microsoftonline.com/f47559c4-15e1-4df2-8bf2-a31cd60330c6",
            ClientId = "client-id",
            CallbackPath = "/signin-oidc"
        };
        var effective = new EntraAuthOptions
        {
            Enabled = true,
            Authority = "https://login.microsoftonline.com/f47559c4-15e1-4df2-8bf2-a31cd60330c6/",
            ClientId = "client-id",
            CallbackPath = "/signin-oidc",
            ClientSecret = "set"
        };

        EntraRuntimeApplicationState.MayRequireRestartToApplyChanges(active, effective).Should().BeFalse();
        EntraRuntimeApplicationState.Describe(false, hasActiveRegistration: true)
            .Should().Contain("applied to this host process");
    }

    [Fact]
    public void MayRequireRestart_WhenActiveButProcessNotEnabled_IsTrue()
    {
        var active = new EntraInstanceRegistration
        {
            Enabled = true,
            Authority = "https://login.microsoftonline.com/tenant",
            ClientId = "client-id"
        };

        EntraRuntimeApplicationState.MayRequireRestartToApplyChanges(active, new EntraAuthOptions { Enabled = false })
            .Should().BeTrue();
    }
}
