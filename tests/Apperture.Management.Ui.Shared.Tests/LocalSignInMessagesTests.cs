using System.Net;
using Apperture.Management.Ui.Shared.Auth;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// AUD-1.2 review: a recovery code consumed with its own audit row stays consumed when the sign-in that follows cannot be
/// recorded, and an unknown outcome may have consumed it. Only a refusal tells the user to check what they entered.
/// </summary>
public sealed class LocalSignInMessagesTests
{
    [Fact]
    public void A_refused_code_or_password_asks_the_user_to_check_what_they_entered()
    {
        LocalSignInMessages.ForFailure(mfaStep: true, HttpStatusCode.Unauthorized).Should().Be(LocalSignInMessages.CodeRejected);
        LocalSignInMessages.ForFailure(mfaStep: false, HttpStatusCode.Unauthorized).Should().Be(LocalSignInMessages.PasswordRejected);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)] // the sign-in row could not be written, or the outcome is unknown
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(null)] // no response arrived
    public void A_failure_after_the_code_may_have_been_accepted_never_invites_that_code_again(HttpStatusCode? status)
    {
        var message = LocalSignInMessages.ForFailure(mfaStep: true, status);

        message.Should().Be(LocalSignInMessages.MfaNotCompleted);
        message.Should().Contain("may now be used up").And.Contain("different recovery code");
        message.Should().NotContain("Check the authenticator or recovery code", "the code may have been accepted and used up");
        message.Should().NotContainEquivalentOf("nothing", "an unknown outcome is never presented as nothing having happened");
        LocalSignInMessages.ClearsRecoveryCode(mfaStep: true, status).Should().BeTrue("the code is not offered for another attempt");
    }

    [Fact]
    public void A_refused_code_is_left_for_the_user_to_correct()
    {
        LocalSignInMessages.ClearsRecoveryCode(mfaStep: true, HttpStatusCode.Unauthorized).Should().BeFalse();
        LocalSignInMessages.ClearsRecoveryCode(mfaStep: false, HttpStatusCode.InternalServerError).Should().BeFalse();
    }

    [Fact]
    public void A_password_step_that_could_not_complete_does_not_blame_the_credentials()
    {
        LocalSignInMessages.ForFailure(mfaStep: false, HttpStatusCode.InternalServerError).Should().Be(LocalSignInMessages.SignInNotCompleted);
    }
}
