using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Pages.Config.Governance;
using Bunit;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>New user: the username field is checked live against the one username rule and is sent as the preferred field.</summary>
public sealed class UserNewUsernameUiTests : GovernancePageTestBase
{
    private IRenderedComponent<UserNew> RenderNew()
    {
        Client.Setup(c => c.GetRolesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<RoleDto>());
        Grant(Caps(manageUsers: true));
        var cut = Render<UserNew>();
        cut.WaitForAssertion(() => cut.FindAll("input#new-user-username").Should().ContainSingle());
        return cut;
    }

    private static void FillDetails(IRenderedComponent<UserNew> cut, string username)
    {
        cut.Find("input#new-user-display-name").Change("Alice Example");
        cut.Find("input#new-user-username").Input(username);
        cut.Find("input#new-user-email").Change("alice@contoso.example");
    }

    private static void ClickCreate(IRenderedComponent<UserNew> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Contains("Create user")).Click();

    [Theory]
    [InlineData("Bad Name!", "3 to 64 characters")]
    [InlineData("jane@contoso.com", "3 to 64 characters")]
    [InlineData("system", "reserved")]
    public void Typing_an_unassignable_name_explains_why_in_visible_text_as_you_type(string typed, string expected)
    {
        var cut = RenderNew();

        cut.Find("input#new-user-username").Input(typed);

        cut.Find("input#new-user-username").GetAttribute("aria-invalid").Should().Be("true");
        cut.Find("#new-user-username-hint").TextContent.Should().Contain(expected);
    }

    [Fact]
    public void A_valid_name_is_accepted_without_complaint()
    {
        var cut = RenderNew();

        cut.Find("input#new-user-username").Input("alice.example");

        cut.Find("input#new-user-username").HasAttribute("aria-invalid").Should().BeFalse();
    }

    [Fact]
    public void Creating_with_an_invalid_name_sends_nothing_and_asks_for_attention()
    {
        var cut = RenderNew();
        FillDetails(cut, "Bad Name!");

        ClickCreate(cut);

        Client.Verify(c => c.CreateUserAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        cut.FindAll(".ui-form-validation-feedback").Should().ContainSingle();
    }

    [Fact]
    public void Creating_sends_the_username_as_the_preferred_field_and_leaves_the_external_id_empty()
    {
        CreateUserRequest? sent = null;
        Client.Setup(c => c.CreateUserAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateUserRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(new CreateUserResponse("created-id"));
        var cut = RenderNew();
        FillDetails(cut, "  Alice.Example ");

        ClickCreate(cut);

        cut.WaitForAssertion(() => sent.Should().NotBeNull());
        sent!.IdentitySource.Should().Be("Local");
        sent.Username.Should().Be("Alice.Example", "the typed text is trimmed; normalization is the server's");
        sent.ExternalId.Should().BeNull();
        sent.DisplayName.Should().Be("Alice Example");
    }
}
