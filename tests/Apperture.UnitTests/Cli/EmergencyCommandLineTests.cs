using Apperture.Cli.Host;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Cli;

/// <summary>The emergency command line is read strictly, so a typed-wrong option never becomes some other operation.</summary>
public sealed class EmergencyCommandLineTests
{
    private static EmergencyCommand Parse(params string[] args)
    {
        EmergencyCommandLine.TryParse(args, out var command, out var error).Should().BeTrue(error);
        return command!;
    }

    private static string Reject(params string[] args)
    {
        EmergencyCommandLine.TryParse(args, out var command, out var error).Should().BeFalse();
        command.Should().BeNull();
        return error!;
    }

    [Fact]
    public void Reset_without_the_bare_option_is_the_replacement()
    {
        var command = Parse("reset-mfa", "--user-id", "u1");

        command.Name.Should().Be("reset-mfa");
        command.BareReset.Should().BeFalse();
        command.UserId.Should().Be("u1");
    }

    [Fact]
    public void The_bare_reset_is_asked_for_only_by_its_own_option()
    {
        Parse("reset-mfa", "--bare-reset").BareReset.Should().BeTrue();
        Parse("RESET-MFA", "--BARE-RESET", "--username", "breakglass").BareReset.Should().BeTrue();
    }

    [Theory]
    [InlineData("--bare-rest")]
    [InlineData("--bare")]
    [InlineData("--reset")]
    [InlineData("bare-reset")]
    public void A_misspelt_bare_reset_is_a_usage_error_and_never_the_replacement_or_the_reset(string option)
    {
        Reject("reset-mfa", option).Should().Contain("does not take");
    }

    [Fact]
    public void An_option_the_command_does_not_take_is_refused()
    {
        Reject("status", "--user-id", "u1");
        Reject("enroll-mfa", "--bare-reset");
        Reject("regenerate-codes", "--bare-reset");
        Reject("revoke", "--user-id", "u1");
        Reject("provision", "--confirm-fallback");
    }

    [Fact]
    public void An_unknown_command_is_refused()
    {
        Reject("reset-everything").Should().Contain("Unknown emergency command");
    }

    [Theory]
    [InlineData("--user-id")]
    [InlineData("--username")]
    public void An_account_option_without_a_value_is_refused_and_does_not_fall_back_to_the_default_account(string option)
    {
        Reject("reset-mfa", option).Should().Contain("needs a value");
        Reject("reset-mfa", option, "--bare-reset").Should().Contain("needs a value");
    }

    [Fact]
    public void An_account_named_twice_or_two_ways_is_refused()
    {
        Reject("reset-mfa", "--user-id", "a", "--user-id", "b").Should().Contain("more than once");
        Reject("reset-mfa", "--user-id", "a", "--username", "b").Should().Contain("not both");
        Reject("reset-mfa", "--bare-reset", "--bare-reset").Should().Contain("more than once");
    }

    [Fact]
    public void Revoke_carries_its_confirmation()
    {
        Parse("revoke", "--confirm-fallback").ConfirmFallback.Should().BeTrue();
        Parse("revoke").ConfirmFallback.Should().BeFalse();
    }

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_is_a_request_for_the_overview(string word)
    {
        Parse(word).Help.Should().BeTrue();
    }

    [Fact]
    public void No_command_is_a_request_for_the_overview()
    {
        Parse().Help.Should().BeTrue();
    }

    [Fact]
    public void A_command_can_ask_for_its_own_help_without_running()
    {
        var command = Parse("reset-mfa", "--help");

        command.Help.Should().BeTrue();
        command.Name.Should().Be("reset-mfa");
    }

    [Fact]
    public void Help_distinguishes_the_safe_replacement_from_the_destructive_reset()
    {
        var overview = new StringWriter();
        EmergencyHelp.Write(overview, null);
        var text = overview.ToString();

        text.Should().Contain("reset-mfa [--user-id <id> | --username <name>]").And.Contain("replace the authenticator, no gap");
        text.Should().Contain("reset-mfa --bare-reset").And.Contain("DESTRUCTIVE");
        text.Should().Contain("Exit status").And.Contain("  5  result not confirmed");

        var detail = new StringWriter();
        EmergencyHelp.Write(detail, "reset-mfa");
        detail.ToString().Should().Contain("keep working").And.Contain("changes nothing").And.Contain("never done instead of a replacement that failed");
    }

    [Fact]
    public void Help_asks_for_no_secret_on_the_command_line()
    {
        var overview = new StringWriter();
        EmergencyHelp.Write(overview, null);

        overview.ToString().Should().Contain("Do not pass secrets on the command line.");
        EmergencyCommandLine.IsCommand("reset-mfa").Should().BeTrue();
    }
}
