using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Client;
using Apperture.Shared.Kernel;
using Apperture.ControlPlane.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// The response to a preferences update whose account changed while it was processed. The service refuses such a request before
/// it writes anything (the transaction behaviour is shown on SQL Server), and the controller reports the refusal as the established,
/// non-sensitive 409 that the profile and avatar endpoints already use, instead of an unhandled exception.
/// </summary>
public sealed class AccountControllerPreferencesTests
{
    private static readonly UserId Owner = new("usr_prefs_owner");

    private static AccountController NewController(Mock<IAccountProfileService> account)
    {
        var principal = new Mock<IPrincipalContext>();
        principal
            .Setup(p => p.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HumanActor(new ActorId(Owner.Value), "Owner"));
        var resolver = new Mock<IPrincipalToUserResolver>();
        resolver
            .Setup(r => r.ResolveUserIdAsync(It.IsAny<IActor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Owner);
        return new AccountController(principal.Object, resolver.Object, account.Object, new Mock<IRoleProvider>().Object);
    }

    [Fact]
    public async Task A_stale_designation_conflict_is_reported_as_409_with_its_stable_code_and_no_detail()
    {
        var account = new Mock<IAccountProfileService>();
        account
            .Setup(a => a.UpdatePreferencesAsync(Owner, It.IsAny<AccountPreferencesUpdate>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EmergencyAccessException(
                EmergencyAccessErrorCodes.StaleUpdate,
                "The account changed while this was being processed. Try again."));

        var result = await NewController(account).UpdatePreferences(
            new UpdateUserPreferencesRequest("Dark", "Hour24", "Europe/London"),
            CancellationToken.None);

        var conflict = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        var body = System.Text.Json.JsonSerializer.Serialize(conflict.Value);
        body.Should().Contain(EmergencyAccessErrorCodes.StaleUpdate).And.Contain("Try again.");
        body.Should().NotContain(Owner.Value);
    }

    [Fact]
    public async Task An_ordinary_update_still_returns_the_saved_preferences()
    {
        var account = new Mock<IAccountProfileService>();
        account
            .Setup(a => a.UpdatePreferencesAsync(Owner, It.IsAny<AccountPreferencesUpdate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountPreferencesSnapshot(AppearancePreference.Dark, TimeFormatPreference.Hour24, "Europe/London"));

        var result = await NewController(account).UpdatePreferences(
            new UpdateUserPreferencesRequest("Dark", "Hour24", "Europe/London"),
            CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<UserPreferencesDto>().Subject;
        (dto.Appearance, dto.TimeFormat, dto.TimeZoneId).Should().Be(("Dark", "Hour24", "Europe/London"));
    }

    [Fact]
    public async Task An_invalid_value_is_still_a_validation_problem()
    {
        var account = new Mock<IAccountProfileService>();
        account
            .Setup(a => a.UpdatePreferencesAsync(Owner, It.IsAny<AccountPreferencesUpdate>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("'Purple' is not a supported appearance."));

        var result = await NewController(account).UpdatePreferences(
            new UpdateUserPreferencesRequest("Purple", "Hour24", null),
            CancellationToken.None);

        var problem = result.Result.Should().BeAssignableTo<ObjectResult>().Subject;
        problem.Should().NotBeOfType<ConflictObjectResult>("a bad value is a validation problem, not a stale-account conflict");
        problem.Value.Should().BeOfType<ValidationProblemDetails>();
    }
}
