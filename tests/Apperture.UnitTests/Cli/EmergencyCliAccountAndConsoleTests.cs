using Apperture.Bootstrap;
using Apperture.Cli.Host;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Cli;

/// <summary>Which account a command acts on, the lost enrolment race, the process console's cancellation, and a failure while the services are resolved.</summary>
public sealed class EmergencyCliAccountAndConsoleTests
{
    private static User Recovery(string id = CliWorld.UserId) =>
        new(new UserId(id), "Recovery Access", [new IdentityLink(IdentitySource.Local, "breakglass", null)], [], false) { Username = "breakglass" };

    private static void ResetSucceeds(CliWorld w, UserId id) =>
        w.Mfa.Setup(m => m.ResetAsync(id, AuditActor.EmergencyCli, It.IsAny<CancellationToken>())).Returns(() => ValueTask.CompletedTask);

    // ------------------------------------------------------------------------------------------------ which account

    [Fact]
    public async Task With_no_account_option_the_designated_recovery_account_is_the_one_acted_on()
    {
        var w = new CliWorld();
        var designated = new UserId("usr-designated");
        w.Users.Setup(u => u.FindDesignatedEmergencyUserAsync(It.IsAny<CancellationToken>())).Returns(() => ValueTask.FromResult<User?>(Recovery("usr-designated")));
        ResetSucceeds(w, designated);

        (await w.RunAsync(w.Console(false), ["reset-mfa", "--bare-reset"])).Should().Be(EmergencyExitCodes.Success);

        w.Mfa.Verify(m => m.ResetAsync(designated, AuditActor.EmergencyCli, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task With_no_account_option_and_nobody_designated_the_reserved_recovery_username_is_looked_up()
    {
        var w = new CliWorld();
        w.Users.Setup(u => u.FindDesignatedEmergencyUserAsync(It.IsAny<CancellationToken>())).Returns(() => ValueTask.FromResult<User?>(null));
        w.Users.Setup(u => u.GetByIdentityLinkAsync(IdentitySource.Local, LocalAuthHelpers.NormalizeUsername(EmergencyAccessIdentity.LocalUsername), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<User?>(Recovery()));
        ResetSucceeds(w, CliWorld.Id);

        (await w.RunAsync(w.Console(false), ["reset-mfa", "--bare-reset"])).Should().Be(EmergencyExitCodes.Success);
    }

    [Fact]
    public async Task A_named_username_is_normalised_and_looked_up_as_a_local_identity_and_never_the_designated_account()
    {
        var w = new CliWorld();
        w.Users.Setup(u => u.GetByIdentityLinkAsync(IdentitySource.Local, LocalAuthHelpers.NormalizeUsername("Some.Admin"), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<User?>(Recovery()));
        ResetSucceeds(w, CliWorld.Id);

        (await w.RunAsync(w.Console(false), ["reset-mfa", "--bare-reset", "--username", "Some.Admin"])).Should().Be(EmergencyExitCodes.Success);

        w.Users.Verify(u => u.FindDesignatedEmergencyUserAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_username_nobody_has_is_invalid_input()
    {
        var w = new CliWorld();
        w.Users.Setup(u => u.GetByIdentityLinkAsync(IdentitySource.Local, It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(() => ValueTask.FromResult<User?>(null));
        var console = w.Console(false);

        (await w.RunAsync(console, ["reset-mfa", "--username", "ghost"])).Should().Be(EmergencyExitCodes.InvalidUsage);
        console.ErrorText.ToString().Should().Contain("User not found.");
    }

    // ------------------------------------------------------------------------------------------------ the enrolment race

    [Fact]
    public async Task An_enrolment_another_operator_finished_first_is_a_refusal_found_out_by_reading_the_state()
    {
        var w = new CliWorld();
        var reads = 0;
        w.Mfa.Setup(m => m.IsEnrolledAsync(CliWorld.Id, It.IsAny<CancellationToken>())).Returns(() => ValueTask.FromResult(++reads > 1));
        w.Mfa.Setup(m => m.BeginEnrollmentAsync(CliWorld.Id, "breakglass", It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(new NativeMfaEnrollmentChallenge(CliWorld.Id, "otpauth://x", "KEY", [])));
        w.Mfa.Setup(m => m.CompleteEnrollmentAsync(CliWorld.Id, "123456", AuditActor.EmergencyCli, It.IsAny<CancellationToken>()))
            .Returns(() => CliWorld.Faulted<IReadOnlyList<string>>(new InvalidOperationException("An authenticator is already enrolled.")));
        var console = w.Console(false, "123456");

        (await w.RunAsync(console, CliWorld.OnAccount("enroll-mfa"))).Should().Be(EmergencyExitCodes.Refused);
        console.AllOutput.Should().NotContain("recovery codes now");
    }

    [Fact]
    public async Task A_plain_failure_while_the_account_is_still_not_enrolled_stays_an_internal_failure()
    {
        var w = new CliWorld();
        w.Enrolled(false);
        w.Mfa.Setup(m => m.BeginEnrollmentAsync(CliWorld.Id, "breakglass", It.IsAny<CancellationToken>()))
            .Returns(() => CliWorld.Faulted<NativeMfaEnrollmentChallenge>(new InvalidOperationException("Native MFA administration requires the audit event writer.")));

        (await w.RunAsync(w.Console(false), CliWorld.OnAccount("enroll-mfa"))).Should().Be(EmergencyExitCodes.InternalFailure);
    }

    // ------------------------------------------------------------------------------------------------ the process console

    /// <summary>A reader whose read blocks until the test lets it go, as a terminal does.</summary>
    private sealed class BlockedReader(string line) : TextReader
    {
        public ManualResetEventSlim Gate { get; } = new(false);

        public override string ReadLine()
        {
            Gate.Wait(TimeSpan.FromSeconds(30));
            return line;
        }
    }

    [Fact]
    public async Task Cancelling_ends_the_wait_for_a_blocked_read_at_once()
    {
        var reader = new BlockedReader("123456");
        var console = new SystemEmergencyConsole(reader);
        using var cancellation = new CancellationTokenSource();

        var waiting = console.ReadSecretAsync("code:", cancellation.Token);
        await Task.Delay(50);
        waiting.IsCompleted.Should().BeFalse("the read is blocked");
        cancellation.Cancel();

        var act = async () => await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        await act.Should().ThrowAsync<OperationCanceledException>();
        reader.Gate.Set();
    }

    [Fact]
    public async Task A_line_that_arrives_together_with_the_cancellation_is_never_used()
    {
        var reader = new BlockedReader("123456");
        reader.Gate.Set();
        var console = new SystemEmergencyConsole(reader);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = async () => await console.ReadSecretAsync("code:", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_typed_line_is_returned_trimmed_and_the_end_of_input_is_null()
    {
        var typed = new BlockedReader("  654321 ");
        typed.Gate.Set();
        (await new SystemEmergencyConsole(typed).ReadSecretAsync("code:", CancellationToken.None)).Should().Be("654321");

        var ended = new BlockedReader(null!);
        ended.Gate.Set();
        (await new SystemEmergencyConsole(ended).ReadSecretAsync("code:", CancellationToken.None)).Should().BeNull();
    }

    // ------------------------------------------------------------------------------------------------ a failure while the services are resolved

    private sealed class FailingProfile : IBootstrapConnectionProfile
    {
        public string ConnectionString => throw new InvalidOperationException("Bootstrap SQL credentials must not be used in this environment.");

        public Task ValidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class IdentityProtector : ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default) => ValueTask.FromResult(plaintext.ToArray());

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default) => ValueTask.FromResult(protectedPayload.ToArray());
    }

    [Fact]
    public async Task A_configuration_failure_found_when_the_services_are_resolved_is_an_internal_failure_with_its_next_step()
    {
        var console = new ScriptedConsole(new List<string>(), inputRedirected: true);

        var exit = await EmergencyCli.RunAsync(
            ["status"],
            new ConfigurationBuilder().Build(),
            "Production",
            console,
            services =>
            {
                services.AddSingleton<IBootstrapConnectionProfile>(new FailingProfile());
                services.AddSingleton<ISecretProtector, IdentityProtector>();
            });

        exit.Should().Be(EmergencyExitCodes.InternalFailure, "this is a failure of the command's setup, not a refusal");
        console.ErrorText.ToString().Should().Contain("Error:").And.Contain("Next:");
    }
}
