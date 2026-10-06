using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Pages.Governance;
using Bunit;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>Role-derived access: the person is identified by username, and the id is kept to technical details.</summary>
public sealed class UserEffectiveAccessUiTests : GovernancePageTestBase
{
    private IRenderedComponent<UserEffectiveAccess> RenderAccess(OperatorCapabilitiesDto? caps = null)
    {
        Client.Setup(c => c.GetUserAsync(AliceId, It.IsAny<CancellationToken>())).ReturnsAsync(Alice());
        Client.Setup(c => c.GetEffectiveAccessAsync(AliceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrincipalEffectiveAccessDto(AliceId, ["role-ops"], [new RolePermissionEntryDto("user.read", "User", "instance")]));
        Grant(caps ?? Caps());
        return Render<UserEffectiveAccess>(p => p.Add(c => c.UserId, AliceId));
    }

    [Fact]
    public void The_summary_identifies_the_person_by_username_not_by_user_id()
    {
        var cut = RenderAccess();
        cut.WaitForAssertion(() => cut.FindAll(".ui-metadata-item").Should().NotBeEmpty());

        var labels = cut.FindAll(".ui-metadata-item").ToDictionary(
            i => i.QuerySelector(".ui-metadata-item__label")!.TextContent.Trim(),
            i => i.QuerySelector(".ui-metadata-item__value")!.TextContent.Trim());
        labels.Should().ContainKey("Username").WhoseValue.Should().Be("alice");
        labels.Should().NotContainKey("User ID");
        VisibleText(cut.Find(".ui-page-header")).Should().Contain("alice");
    }

    [Fact]
    public void The_user_id_is_not_visible_outside_technical_details_and_is_available_inside_them()
    {
        var cut = RenderAccess();
        cut.WaitForAssertion(() => cut.FindAll(".ui-metadata-item").Should().NotBeEmpty());

        VisibleTextOutsideTechnicalDetails(cut.Find("div.p-4")).Should().NotContain(AliceId);
        var details = cut.Find(".ui-technical-details");
        details.QuerySelector("dt")!.TextContent.Should().Be("User ID");
        details.QuerySelector("dd")!.TextContent.Trim().Should().Be(AliceId);
    }

    [Fact]
    public void Without_governance_access_the_page_is_forbidden_and_shows_no_identifier()
    {
        var cut = RenderAccess(Caps(view: false));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("You do not have permission to view effective access."));
        VisibleText(cut.Find("div.p-4")).Should().NotContain(AliceId);
        Client.Verify(c => c.GetEffectiveAccessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
