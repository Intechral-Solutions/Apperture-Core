using Apperture.Management.Ui.Shared.Auth;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class AuthShellAccessTests
{
    [Theory]
    [InlineData(SessionLoadState.Unknown, false, false, false, false, true)]
    [InlineData(SessionLoadState.Loading, false, false, false, false, true)]
    [InlineData(SessionLoadState.Unauthenticated, false, false, false, false, true)]
    [InlineData(SessionLoadState.Error, false, false, false, false, true)]
    [InlineData(SessionLoadState.Authenticated, false, false, false, false, true)]
    [InlineData(SessionLoadState.Authenticated, true, false, false, false, false)]
    [InlineData(SessionLoadState.ForbiddenUnlinked, false, false, false, false, false)]
    [InlineData(SessionLoadState.Unauthenticated, false, true, false, false, false)]
    [InlineData(SessionLoadState.Unknown, false, true, false, false, false)]
    [InlineData(SessionLoadState.Authenticated, true, true, false, false, true)]
    [InlineData(SessionLoadState.Authenticated, false, true, false, false, true)]
    [InlineData(SessionLoadState.Authenticated, true, false, true, false, true)]
    [InlineData(SessionLoadState.Unauthenticated, false, false, true, false, true)]
    [InlineData(SessionLoadState.Unknown, false, false, true, false, true)]
    // Sticky shell: once committed, do not re-block Authenticated/Loading for cap re-warm.
    [InlineData(SessionLoadState.Authenticated, false, false, false, true, false)]
    [InlineData(SessionLoadState.Loading, false, false, false, true, false)]
    [InlineData(SessionLoadState.ForbiddenUnlinked, false, false, false, true, false)]
    // Sticky does not keep shell on signed-out / error protected paths.
    [InlineData(SessionLoadState.Unauthenticated, false, false, false, true, true)]
    [InlineData(SessionLoadState.Error, false, false, false, true, true)]
    // Root always blocks even if somehow committed.
    [InlineData(SessionLoadState.Authenticated, true, false, true, true, true)]
    public void ShouldBlockProtectedShell_matches_policy(
        SessionLoadState state,
        bool capabilitiesReady,
        bool isPublicPath,
        bool isRootBootstrapPath,
        bool shellAlreadyCommitted,
        bool expectedBlock)
    {
        AuthShellAccess.ShouldBlockProtectedShell(
                state,
                capabilitiesReady,
                isPublicPath,
                isRootBootstrapPath,
                shellAlreadyCommitted)
            .Should().Be(expectedBlock);
    }

    [Fact]
    public void Shell_commit_and_release_helpers()
    {
        AuthShellAccess.ShouldCommitShell(currentlyBlocking: false, isPublicPath: false, isRootBootstrapPath: false)
            .Should().BeTrue();
        AuthShellAccess.ShouldCommitShell(currentlyBlocking: true, isPublicPath: false, isRootBootstrapPath: false)
            .Should().BeFalse();
        AuthShellAccess.ShouldCommitShell(currentlyBlocking: false, isPublicPath: true, isRootBootstrapPath: false)
            .Should().BeFalse();
        AuthShellAccess.ShouldCommitShell(currentlyBlocking: false, isPublicPath: false, isRootBootstrapPath: true)
            .Should().BeFalse();

        AuthShellAccess.ShouldReleaseShellCommit(SessionLoadState.Unauthenticated, isPublicPath: false)
            .Should().BeTrue();
        AuthShellAccess.ShouldReleaseShellCommit(SessionLoadState.Authenticated, isPublicPath: true)
            .Should().BeTrue();
        AuthShellAccess.ShouldReleaseShellCommit(SessionLoadState.Authenticated, isPublicPath: false)
            .Should().BeFalse();
    }

    [Fact]
    public void Authenticated_waits_for_app_bootstrap_even_when_capabilities_ready()
    {
        AuthShellAccess.ShouldBlockProtectedShell(
                SessionLoadState.Authenticated,
                capabilitiesReady: true,
                isPublicPath: false,
                appBootstrapReady: false)
            .Should().BeTrue();

        AuthShellAccess.ShouldBlockProtectedShell(
                SessionLoadState.Authenticated,
                capabilitiesReady: true,
                isPublicPath: false,
                appBootstrapReady: true)
            .Should().BeFalse();
    }

    [Fact]
    public void ForbiddenUnlinked_waits_for_app_bootstrap_before_first_mount()
    {
        AuthShellAccess.ShouldBlockProtectedShell(
                SessionLoadState.ForbiddenUnlinked,
                capabilitiesReady: false,
                isPublicPath: false,
                appBootstrapReady: false)
            .Should().BeTrue();

        AuthShellAccess.ShouldBlockProtectedShell(
                SessionLoadState.ForbiddenUnlinked,
                capabilitiesReady: false,
                isPublicPath: false,
                appBootstrapReady: true)
            .Should().BeFalse();
    }

    [Fact]
    public void Unauthenticated_protected_path_redirects_to_login_once_policy()
    {
        AuthShellAccess.ShouldRedirectToLogin(SessionLoadState.Unauthenticated, isPublicPath: false)
            .Should().BeTrue();
        AuthShellAccess.ShouldRedirectToLogin(SessionLoadState.Unauthenticated, isPublicPath: true)
            .Should().BeFalse();
        AuthShellAccess.ShouldRedirectToLogin(SessionLoadState.Authenticated, isPublicPath: false)
            .Should().BeFalse();
    }

    [Fact]
    public void Authenticated_on_login_redirects_away()
    {
        AuthShellAccess.ShouldRedirectAuthenticatedAwayFromLogin(SessionLoadState.Authenticated, isPublicPath: true)
            .Should().BeTrue();
        AuthShellAccess.ShouldRedirectAuthenticatedAwayFromLogin(SessionLoadState.Unauthenticated, isPublicPath: true)
            .Should().BeFalse();
    }

    [Fact]
    public void Root_bootstrap_redirects_before_shell_mount()
    {
        AuthShellAccess.ShouldRedirectRootBootstrap(SessionLoadState.Authenticated, isRootBootstrapPath: true)
            .Should().BeTrue();
        AuthShellAccess.ShouldRedirectRootBootstrap(SessionLoadState.Unauthenticated, isRootBootstrapPath: true)
            .Should().BeTrue();
        AuthShellAccess.ShouldRedirectRootBootstrap(SessionLoadState.Authenticated, isRootBootstrapPath: false)
            .Should().BeFalse();
        LoginRouteHelper.IsRootBootstrapPath("/").Should().BeTrue();
        LoginRouteHelper.IsRootBootstrapPath("/dashboard").Should().BeFalse();
    }

    [Theory]
    [InlineData(SessionLoadState.Authenticated, true)]
    [InlineData(SessionLoadState.ForbiddenUnlinked, true)]
    [InlineData(SessionLoadState.Unauthenticated, false)]
    [InlineData(SessionLoadState.Unknown, false)]
    [InlineData(SessionLoadState.Loading, false)]
    [InlineData(SessionLoadState.Error, false)]
    public void MayLoadAuthenticatedShellData_gates_menus(SessionLoadState state, bool expected) =>
        AuthShellAccess.MayLoadAuthenticatedShellData(state).Should().Be(expected);

    [Fact]
    public void Blocking_message_for_unauthenticated_is_redirect_copy()
    {
        AuthShellAccess.GetBlockingMessage(SessionLoadState.Unauthenticated, capabilitiesReady: false)
            .Should().Contain("sign-in");
        AuthShellAccess.GetBlockingMessage(SessionLoadState.Loading, capabilitiesReady: false)
            .Should().Contain("session");
    }

    [Fact]
    public void Blocking_message_for_authenticated_post_login_is_opening_console()
    {
        AuthShellAccess.GetBlockingMessage(SessionLoadState.Authenticated, capabilitiesReady: true)
            .Should().Contain("Opening console");
        AuthShellAccess.GetBlockingMessage(SessionLoadState.Authenticated, capabilitiesReady: false)
            .Should().Contain("permissions");
    }
}
