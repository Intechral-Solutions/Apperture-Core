using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Molecules;
using Apperture.Management.Ui.Client.Services;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>The account menu's secondary identity falls back to the username, never the user id.</summary>
public sealed class UserAccountMenuUiTests : GovernancePageTestBase
{
    private static MeResponse Me(string signInSource, string? principal, string displayName) =>
        new(AliceId, "alice", displayName, [], principal, signInSource, [], "none", [], false, null);

    private async Task<IRenderedComponent<UserAccountMenu>> RenderOpenMenuAsync(MeResponse me)
    {
        Client.Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(me);
        await Services.GetRequiredService<SessionStateService>().RefreshAsync();
        var cut = Render<UserAccountMenu>();
        cut.Find("button[aria-haspopup='true']").Click();
        return cut;
    }

    private static string Secondary(IRenderedComponent<UserAccountMenu> cut) =>
        cut.Find(".ui-menu-panel span.text-xs.text-muted").TextContent.Trim();

    [Fact]
    public async Task With_no_sign_in_source_principal_or_display_name_the_menu_falls_back_to_the_username()
    {
        var cut = await RenderOpenMenuAsync(Me("Unknown", null, ""));

        Secondary(cut).Should().Be("alice");
        cut.Markup.Should().NotContain(AliceId, "the user id is never the fallback");
    }

    [Fact]
    public async Task The_sign_in_source_still_wins_when_known()
    {
        var cut = await RenderOpenMenuAsync(Me("Local", "alice", "Alice Example"));

        Secondary(cut).Should().Be("Local sign-in");
    }

    [Fact]
    public async Task The_user_principal_is_used_before_the_display_name()
    {
        var cut = await RenderOpenMenuAsync(Me("Unknown", "alice@contoso.example", "Alice Example"));

        Secondary(cut).Should().Be("alice@contoso.example");
    }
}
