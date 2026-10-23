using System.Net.Http;
using AngleSharp.Dom;
using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Organisms;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared.Options;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// The profile page's avatar picker, rendered for real. It passed an <c>IsBusy</c> argument that <c>ConfirmActionDialog</c> does not
/// declare, so Blazor threw while rendering <c>/account/profile</c>, on every load, and the remove-photo confirmation could not be
/// shown. The dialog keeps its own busy state while its action runs, which is what these cases pin: the picker renders, the
/// confirmation opens, cancel changes nothing, confirm removes the photo once, a second click while it runs does nothing, and a
/// failure keeps the dialog open with the message. The picker used to clear its own open flag inside the action, which the
/// dialog runs, so the picker never rendered again and the confirmation stayed open over a stale "uploaded photo" after the photo
/// had been removed; every other caller closes through <c>OnConfirmed</c>.
/// </summary>
public sealed class AvatarPickerRemovePhotoTests : BunitContext
{
    private static readonly AccountAvatarDto CustomAvatar = new() { Kind = AccountAvatarDto.KindCustom, Version = "v-custom", ImageUrl = "/api/account/avatar/image" };
    private static readonly AccountAvatarDto BuiltInAvatar = new() { Kind = AccountAvatarDto.KindBuiltIn, BuiltInOptionId = "option-a", Version = "option-a" };

    private readonly Mock<IManagementClient> _client = new(MockBehavior.Loose);
    private readonly ToastService _toasts = new();

    public AvatarPickerRemovePhotoTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_client.Object);
        Services.AddSingleton<IToastService>(_toasts);
        Services.AddSingleton(new ShellOrientationService());
        Services.AddOptions<ShellUiOptions>();
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Services.AddSingleton<ShellHostNavigation>();
        Services.AddSingleton<SessionStateService>();
        Services.AddSingleton<AccountAvatarService>();

        _client.Setup(c => c.GetAvatarOptionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<AvatarOptionDto>
        {
            new() { Id = "option-a", DisplayName = "Option A", IsDefault = true },
            new() { Id = "option-b", DisplayName = "Option B" },
            new() { Id = "option-c", DisplayName = "Option C" },
        });
        _client.Setup(c => c.GetAccountAvatarImageAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountAvatarImageDto("image/png", [1, 2, 3]));
    }

    /// <summary>
    /// The picker as the profile page hosts it: <c>OnChanged</c> is a handler of the hosting component.
    /// </summary>
    private sealed class PickerHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<AvatarPicker>(0);
            builder.AddAttribute(1, nameof(AvatarPicker.DisplayName), "Alice Example");
            builder.AddAttribute(2, nameof(AvatarPicker.OnChanged), EventCallback.Factory.Create(this, () => Task.CompletedTask));
            builder.CloseComponent();
        }
    }

    private async Task<IRenderedComponent<PickerHost>> RenderAsync(AccountAvatarDto avatar)
    {
        await Services.GetRequiredService<AccountAvatarService>().ApplyAsync(avatar);
        var cut = Render<PickerHost>();
        cut.WaitForAssertion(() => cut.FindAll("input[name=avatar-option]").Should().HaveCount(3));
        return cut;
    }

    private static IElement RemoveTrigger(IRenderedComponent<PickerHost> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Remove photo" && b.GetAttribute("data-testid") is null);

    private static IElement Confirm(IRenderedComponent<PickerHost> cut) => cut.Find("[data-testid=confirm-accept]");

    private static IElement Cancel(IRenderedComponent<PickerHost> cut) => cut.Find("[data-testid=confirm-cancel]");

    [Fact]
    public async Task The_picker_renders_without_a_component_parameter_exception()
    {
        var cut = await RenderAsync(BuiltInAvatar);

        cut.Find("#avatar-heading").TextContent.Should().Be("Profile photo");
        cut.Markup.Should().Contain("Using Option A");
        cut.FindAll("button").Where(b => b.TextContent.Trim() == "Remove photo").Should().BeEmpty("a built-in avatar has no photo to remove");
        cut.FindAll("[role=dialog]").Should().BeEmpty("the confirmation is closed until asked for");
    }

    [Fact]
    public async Task Remove_photo_opens_the_confirmation_and_states_the_consequence()
    {
        var cut = await RenderAsync(CustomAvatar);

        RemoveTrigger(cut).Click();

        cut.WaitForAssertion(() => cut.Find("[role=dialog]").TextContent.Should().Contain("Remove custom profile photo?"));
        cut.Find("[data-testid=confirm-consequence]").TextContent.Should().Contain("Your stored photo will be deleted");
        Confirm(cut).TextContent.Trim().Should().Be("Remove photo");
        Cancel(cut).TextContent.Trim().Should().Be("Keep photo");
    }

    [Fact]
    public async Task Cancel_closes_the_confirmation_and_leaves_the_avatar_unchanged()
    {
        var cut = await RenderAsync(CustomAvatar);
        RemoveTrigger(cut).Click();
        cut.WaitForAssertion(() => cut.Find("[role=dialog]"));

        Cancel(cut).Click();

        cut.WaitForAssertion(() => cut.FindAll("[role=dialog]").Should().BeEmpty());
        _client.Verify(c => c.ResetAvatarAsync(It.IsAny<CancellationToken>()), Times.Never);
        Services.GetRequiredService<AccountAvatarService>().Avatar!.IsCustom.Should().BeTrue();
        cut.Markup.Should().Contain("Using your uploaded photo");
    }

    [Fact]
    public async Task Confirm_removes_the_photo_once_and_applies_the_default_illustration()
    {
        _client.Setup(c => c.ResetAvatarAsync(It.IsAny<CancellationToken>())).ReturnsAsync(BuiltInAvatar);
        var cut = await RenderAsync(CustomAvatar);
        RemoveTrigger(cut).Click();
        cut.WaitForAssertion(() => cut.Find("[role=dialog]"));

        Confirm(cut).Click();

        cut.WaitForAssertion(() => cut.FindAll("[role=dialog]").Should().BeEmpty("the confirmation closes once the photo is removed"));
        _client.Verify(c => c.ResetAvatarAsync(It.IsAny<CancellationToken>()), Times.Once);
        Services.GetRequiredService<AccountAvatarService>().Avatar!.IsCustom.Should().BeFalse();
        cut.Markup.Should().Contain("Using Option A", "the picker shows the default illustration, not a stale photo");
        _toasts.Toasts.Should().Contain(t => t.Message.Contains("Profile photo removed."));
    }

    [Fact]
    public async Task While_the_removal_runs_both_buttons_are_disabled_and_a_second_confirm_does_nothing()
    {
        var release = new TaskCompletionSource<AccountAvatarDto>();
        _client.Setup(c => c.ResetAvatarAsync(It.IsAny<CancellationToken>())).Returns(release.Task);
        var cut = await RenderAsync(CustomAvatar);
        RemoveTrigger(cut).Click();
        cut.WaitForAssertion(() => cut.Find("[role=dialog]"));

        Confirm(cut).Click();

        cut.WaitForAssertion(() => Confirm(cut).HasAttribute("disabled").Should().BeTrue("the dialog is busy while the action runs"));
        Cancel(cut).HasAttribute("disabled").Should().BeTrue();
        Confirm(cut).Click();
        _client.Verify(c => c.ResetAvatarAsync(It.IsAny<CancellationToken>()), Times.Once);

        release.SetResult(BuiltInAvatar);
        cut.WaitForAssertion(() => cut.FindAll("[role=dialog]").Should().BeEmpty("the removal finished"));
        _client.Verify(c => c.ResetAvatarAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_failed_removal_keeps_the_confirmation_open_shows_the_message_and_allows_trying_again()
    {
        _client.SetupSequence(c => c.ResetAvatarAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("The photo could not be removed."))
            .ReturnsAsync(BuiltInAvatar);
        var cut = await RenderAsync(CustomAvatar);
        RemoveTrigger(cut).Click();
        cut.WaitForAssertion(() => cut.Find("[role=dialog]"));

        Confirm(cut).Click();

        cut.WaitForAssertion(() => cut.Find(".ui-dialog__error").TextContent.Should().Contain("The photo could not be removed."));
        cut.Find("[role=dialog]").Should().NotBeNull("the dialog stays open when the action fails");
        Confirm(cut).HasAttribute("disabled").Should().BeFalse();
        Services.GetRequiredService<AccountAvatarService>().Avatar!.IsCustom.Should().BeTrue();

        Confirm(cut).Click();

        cut.WaitForAssertion(() => cut.FindAll("[role=dialog]").Should().BeEmpty("the second attempt succeeded"));
        _client.Verify(c => c.ResetAvatarAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        Services.GetRequiredService<AccountAvatarService>().Avatar!.IsCustom.Should().BeFalse();
    }
}
