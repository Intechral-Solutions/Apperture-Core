using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class AuthenticationConfigurationGuardTests
{
    [Fact]
    public void ValidateNoLockout_Rejects_NoLocalAndInvalidEntra()
    {
        var local = new LocalAuthOptions { Enabled = false };
        var policy = new InstanceAuthUiPolicy { ShowLocalSignInInPrimaryChooser = false };
        var entra = new EntraAuthOptions { Enabled = true, Authority = "", ClientId = "" };

        var err = AuthenticationConfigurationGuard.ValidateNoLockout(local, policy, entra);

        err.Should().NotBeNull();
    }

    [Fact]
    public void ValidateDeleteActiveRegistration_Rejects_WhenFallbackWouldBeBroken()
    {
        var local = new LocalAuthOptions { Enabled = false };
        var policy = new InstanceAuthUiPolicy { ShowLocalSignInInPrimaryChooser = false };
        var legacy = new EntraAuthOptions { Enabled = false };

        var err = AuthenticationConfigurationGuard.ValidateDeleteActiveRegistration(
            local,
            policy,
            legacy,
            isDeletingActiveRegistration: true);

        err.Should().NotBeNull();
    }
}
