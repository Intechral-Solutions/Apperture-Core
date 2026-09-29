using Apperture.Management.Ui.Client.Atoms;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// Sign-out is a native form POST to the host (design gate D2), so the browser sends the UI's Origin and nothing a page script
/// adds. It is never a link or a GET navigation.
/// </summary>
public sealed class SignOutFormRenderTests
{
    private static async Task<string> RenderAsync(string buttonClass, string label, string? hostUrl = "http://api.test:9000")
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<NavigationManager>(new TestNavigationManager("http://ui.test:9001/", "http://ui.test:9001/account"))
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton(Options.Create(new ShellUiOptions { ManagementHostUrl = hostUrl }))
            .AddSingleton<ShellHostNavigation>()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<SignOutForm>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(SignOutForm.ButtonClass)] = buttonClass,
                [nameof(SignOutForm.ChildContent)] = (RenderFragment)(b => b.AddContent(0, label)),
            }));
            return output.ToHtmlString();
        });
    }

    [Fact]
    public async Task It_renders_a_post_form_to_the_host_sign_out_endpoint_with_a_submit_button()
    {
        var html = await RenderAsync("btn btn-secondary", "Sign out");

        html.Should().Contain("<form method=\"post\" action=\"http://api.test:9000/signout\"");
        html.Should().Contain("<button type=\"submit\" class=\"btn btn-secondary\">Sign out</button>");
        html.Should().NotContain("<a ").And.NotContain("method=\"get\"");
    }

    [Fact]
    public async Task The_label_and_the_button_style_come_from_the_caller()
    {
        var html = await RenderAsync("ui-menu-item", "Sign in with a different account");

        html.Should().Contain("<button type=\"submit\" class=\"ui-menu-item\">Sign in with a different account</button>");
    }
}
