using System.Text.RegularExpressions;
using Apperture.Management.Ui.Client.Atoms;
using Blazor.Heroicons.Solid;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// The navigation trigger shows a menu icon when closed and a close (X) icon when open, and its accessible
/// name describes the action, not the glyph.
/// </summary>
public sealed class HamburgerToggleRenderTests
{
    private static async Task<string> RenderAsync<TComponent>(IDictionary<string, object?>? parameters = null)
        where TComponent : IComponent
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(
                ParameterView.FromDictionary(parameters ?? new Dictionary<string, object?>()));
            return output.ToHtmlString();
        });
    }

    private static Task<string> RenderToggleAsync(bool locked) =>
        RenderAsync<HamburgerToggle>(new Dictionary<string, object?> { [nameof(HamburgerToggle.IsLocked)] = locked });

    /// <summary>The first svg path of an icon, as a stable signature to find it in the toggle's markup.</summary>
    private static async Task<string> PathOfAsync<TIcon>() where TIcon : IComponent
    {
        var html = await RenderAsync<TIcon>();
        var match = Regex.Match(html, "\\sd=\"([^\"]+)\"");
        match.Success.Should().BeTrue("the icon renders an svg path");
        return match.Groups[1].Value;
    }

    [Fact]
    public async Task Closed_shows_the_menu_icon_and_offers_to_open_navigation()
    {
        var html = await RenderToggleAsync(locked: false);

        html.Should().Contain("aria-label=\"Open navigation\"");
        html.Should().Contain("aria-expanded=\"false\"");
        html.Should().Contain(await PathOfAsync<Bars3Icon>());
        html.Should().NotContain(await PathOfAsync<XMarkIcon>());
    }

    [Fact]
    public async Task Open_shows_a_close_icon_and_offers_to_close_navigation()
    {
        var html = await RenderToggleAsync(locked: true);

        html.Should().Contain("aria-label=\"Close navigation\"");
        html.Should().Contain("aria-expanded=\"true\"");
        html.Should().Contain(await PathOfAsync<XMarkIcon>());
        html.Should().NotContain(await PathOfAsync<Bars3Icon>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_trigger_is_a_labelled_button_that_controls_the_navigation_panel(bool locked)
    {
        var html = await RenderToggleAsync(locked);

        html.Should().Contain("<button");
        html.Should().Contain("type=\"button\"");
        html.Should().Contain("aria-controls=\"control-panel\"");
        // The glyph is decoration; the name comes from aria-label.
        html.Should().Contain("aria-hidden=\"true\"");
        var label = Regex.Match(html, "aria-label=\"([^\"]+)\"").Groups[1].Value.ToLowerInvariant();
        label.Should().NotBeEmpty();
        label.Should().NotContain("hamburger").And.NotContain("arrow").And.NotContain("icon");
    }

    [Fact]
    public async Task The_open_state_no_longer_morphs_into_an_arrow()
    {
        var html = await RenderToggleAsync(locked: true);

        html.Should().NotContain("rotate-180");
        html.Should().NotContain("translate-x");
        html.Should().NotContain("ui-hamburger-line");
    }

    [Fact]
    public void Click_behaviour_and_the_control_panel_id_are_unchanged()
    {
        var toggle = File.ReadAllText(RepoPath(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Atoms/HamburgerToggle.razor"));
        var panel = File.ReadAllText(RepoPath(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/ControlPanel.razor"));

        toggle.Should().Contain("@onclick=\"Toggle\"");
        toggle.Should().Contain("OnToggle.InvokeAsync(!IsOpen)");
        panel.Should().Contain("id=\"control-panel\"");
    }

    private static string RepoPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return Path.Combine(dir!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
