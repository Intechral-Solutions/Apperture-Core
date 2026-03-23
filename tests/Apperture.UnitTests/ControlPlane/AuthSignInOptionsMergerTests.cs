using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class AuthSignInOptionsMergerTests
{
    [Fact]
    public void Merge_WhenEntraOff_ForcesLocalVisibleInPrimaryWhenLocalEnabled()
    {
        var local = new LocalAuthOptions { Enabled = true };
        var entra = new EntraAuthOptions { Enabled = false };
        var policy = new InstanceAuthUiPolicy { ShowLocalSignInInPrimaryChooser = false };

        var m = AuthSignInOptionsMerger.Merge(local, entra, policy);

        m.ShowLocalSignInInPrimaryChooser.Should().BeTrue();
        m.EntraSignInEnabled.Should().BeFalse();
    }

    [Fact]
    public void ValidatePolicyUpdate_RejectsHidingLocalWhenEntraNotViable()
    {
        var local = new LocalAuthOptions { Enabled = true };
        var entra = new EntraAuthOptions { Enabled = false };
        var proposed = new InstanceAuthUiPolicy { ShowLocalSignInInPrimaryChooser = false };

        var err = AuthSignInOptionsMerger.ValidatePolicyUpdate(proposed, local, entra);

        err.Should().NotBeNull();
    }

    [Fact]
    public void ValidatePolicyUpdate_AllowsHidingLocalWhenEntraConfigured()
    {
        var local = new LocalAuthOptions { Enabled = true };
        var entra = new EntraAuthOptions
        {
            Enabled = true,
            Authority = "https://login.microsoftonline.com/tenant",
            ClientId = "cid",
            ClientSecret = "secret"
        };
        var proposed = new InstanceAuthUiPolicy { ShowLocalSignInInPrimaryChooser = false };

        var err = AuthSignInOptionsMerger.ValidatePolicyUpdate(proposed, local, entra);

        err.Should().BeNull();
    }

    [Fact]
    public void Build_IncludesEffectiveExperience_AlignedWithMerge()
    {
        var local = new LocalAuthOptions { Enabled = true };
        var entra = new EntraAuthOptions
        {
            Enabled = true,
            Authority = "https://login.microsoftonline.com/tenant",
            ClientId = "cid",
            ClientSecret = "secret"
        };
        var policy = new InstanceAuthUiPolicy { ShowLocalSignInInPrimaryChooser = true };

        var built = AuthSignInSnapshotBuilder.Build(local, entra, policy);

        built.Snapshot.EntraSignInEnabled.Should().BeTrue();
        built.EffectiveExperience.Should().Be(EffectiveAuthExperienceKind.DualVisible);
    }
}
