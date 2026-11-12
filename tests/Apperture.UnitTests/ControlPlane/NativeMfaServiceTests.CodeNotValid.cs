using System.Text.RegularExpressions;
using Apperture.Cli.Host;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.UnitTests.Cli;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// The typed "code is not valid" exception at both throw sites of the real service. The command line tells a mistyped code (status 2)
/// from an internal failure (status 6) by this type and never by a message, so a site that went back to a plain exception would turn an
/// operator's typing mistake into an internal error.
/// </summary>
public sealed partial class NativeMfaServiceTests
{
    private const string CodeNotValidMessage = "The authenticator code is not valid.";

    [Fact]
    public async Task A_wrong_code_at_the_first_enrolment_is_the_typed_exception_with_the_message_callers_have_always_seen()
    {
        var harness = Harness.Create();
        var userId = new UserId("usr-e");
        await harness.Mfa.BeginEnrollmentAsync(userId, "emergency");

        var act = () => harness.Mfa.CompleteEnrollmentAsync(userId, "000000", AuditActor.EmergencyCli).AsTask();

        var thrown = (await act.Should().ThrowExactlyAsync<NativeMfaCodeNotValidException>()).Which;
        thrown.Message.Should().Be(CodeNotValidMessage);
        thrown.Should().BeAssignableTo<InvalidOperationException>("existing callers catch the plain exception type");
        (await harness.Mfa.IsEnrolledAsync(userId)).Should().BeFalse();
        harness.Recorder.Staged.Should().BeEmpty("a refused code commits nothing");
    }

    [Fact]
    public async Task A_wrong_code_at_the_replacement_is_the_typed_exception_with_the_message_callers_have_always_seen()
    {
        var harness = await Harness.EnrolledAsync();
        var challenge = await harness.Mfa.BeginReplacementAsync(harness.UserId, "emergency");

        var act = () => harness.Mfa.VerifyReplacementAsync(harness.UserId, challenge.CandidateId, challenge.Version, "000000").AsTask();

        var thrown = (await act.Should().ThrowExactlyAsync<NativeMfaCodeNotValidException>()).Which;
        thrown.Message.Should().Be(CodeNotValidMessage);
        thrown.Should().BeAssignableTo<InvalidOperationException>("existing callers catch the plain exception type");
    }

    [Fact]
    public async Task The_enrol_command_over_the_real_service_reports_a_wrong_first_code_as_invalid_input_and_enrolls_nothing()
    {
        // The command line's own runner over the real service (only the account lookup and the emergency service are doubles): the exit
        // status comes from the type the service throws, and the typed code and the staged key appear in no output beyond the key's one display.
        var harness = Harness.Create();
        var userId = new UserId("usr-e");
        var users = new Mock<IUserRepository>(MockBehavior.Strict);
        users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<User?>(
                new User(userId, "Recovery Access", [new IdentityLink(IdentitySource.Local, "breakglass", null)], [], false) { Username = "breakglass" }));
        var emergency = new Mock<IEmergencyAccessService>(MockBehavior.Strict);
        var events = new List<string>();
        var console = new ScriptedConsole(events, inputRedirected: true, "000000");
        var runner = new EmergencyCommandRunner(harness.Mfa, emergency.Object, users.Object, console);
        EmergencyCommandLine.TryParse(["enroll-mfa", "--user-id", "usr-e"], out var command, out var error).Should().BeTrue(error);

        var exit = await runner.RunAsync(command!, CancellationToken.None);

        exit.Should().Be(EmergencyExitCodes.InvalidUsage, "a wrong code is the operator's input, not an internal failure");
        console.ErrorText.ToString().Should().Contain(CodeNotValidMessage);
        console.AllOutput.Should().NotContain("000000").And.NotContain("will not be shown again").And.NotContain("Store these recovery codes");

        // The staged key is shown in its one display before the prompt (the otpauth URI that carries it and the manual key line) and never
        // again: the refused code does not echo it, and nothing on standard error carries it.
        var shown = console.OutText.ToString();
        var key = Regex.Match(shown, @"Manual key: (\S+)").Groups[1].Value;
        key.Should().NotBeEmpty("the staged key was displayed before the code was asked for");
        shown.Split('\n').Select(l => l.Trim()).Where(l => l.Contains(key, StringComparison.Ordinal)).Should().SatisfyRespectively(
            uri => uri.Should().StartWith("otpauth://totp/").And.Contain("secret=" + key),
            manual => manual.Should().Be("Manual key: " + key));
        console.ErrorText.ToString().Should().NotContain(key);
        (await harness.Mfa.IsEnrolledAsync(userId)).Should().BeFalse();
        harness.Recorder.Staged.Should().BeEmpty("nothing was committed");
        harness.Recovery.Codes.Should().BeEmpty("no recovery code was created for an enrolment that did not happen");
    }
}
