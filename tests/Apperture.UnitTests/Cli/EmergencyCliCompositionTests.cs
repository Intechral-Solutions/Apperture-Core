using System.Reflection;
using System.Text.RegularExpressions;
using Apperture.Bootstrap;
using Apperture.Cli.Host;
using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.UnitTests.Cli;

/// <summary>
/// The emergency command line's own container, built by the production composition: the commands resolve the application services with every
/// dependency a protected change needs, and the source holds no way round them. A double of a command handler would pass without any of this.
/// </summary>
public sealed class EmergencyCliCompositionTests
{
    private const string CliFolder = "Apperture.Cli/Apperture.Cli.Host/";

    private sealed class UnusedConnection : IBootstrapConnectionProfile
    {
        // Building the context reads this; nothing here opens it.
        public string ConnectionString => "Server=tcp:unused.invalid,1;Database=unused;User Id=unused;Password=unused;Encrypt=True;TrustServerCertificate=True";

        public Task ValidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class IdentityProtector : ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(plaintext.ToArray());

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(protectedPayload.ToArray());
    }

    /// <summary>The production composition, with only the connection and the platform protector replaced (the real protector needs Windows).</summary>
    private static Microsoft.Extensions.DependencyInjection.ServiceProvider BuildProduction() =>
        EmergencyCli.BuildProvider(
            new ConfigurationBuilder().Build(),
            "Production",
            services =>
            {
                services.AddSingleton<IBootstrapConnectionProfile>(new UnusedConnection());
                services.AddSingleton<ISecretProtector, IdentityProtector>();
            });

    private static object? Field(object instance, string name) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);

    // ------------------------------------------------------------------------------------------------ the container

    [Fact]
    public void The_commands_resolve_the_application_services_from_the_production_container()
    {
        using var provider = BuildProduction();
        using var scope = provider.CreateScope();

        var runner = EmergencyCli.CreateRunner(scope.ServiceProvider, new SystemEmergencyConsole());

        runner.Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<INativeMfaService>().Should().BeOfType<NativeMfaService>();
        scope.ServiceProvider.GetRequiredService<IEmergencyAccessService>().Should().BeOfType<EmergencyAccessService>();
    }

    [Fact]
    public void Every_dependency_a_protected_change_needs_is_wired_not_left_to_the_optional_default()
    {
        // Both services take the unit, the gate and the audit writer as optional constructor parameters: a registration that went missing
        // would build the service with nothing there, and the first protected change would fail at run time. Nothing is optional here.
        using var provider = BuildProduction();
        using var scope = provider.CreateScope();

        var mfa = scope.ServiceProvider.GetRequiredService<INativeMfaService>();
        foreach (var field in new[] { "_auditedUnitOfWork", "_viability", "_auditEvents", "_users", "_candidates" })
            Field(mfa, field).Should().NotBeNull($"NativeMfaService.{field} is the audited unit, the gate or a reader every protected change uses");

        var emergency = scope.ServiceProvider.GetRequiredService<IEmergencyAccessService>();
        foreach (var field in new[] { "_auditedUnitOfWork", "_viability", "_auditEvents", "_mfa" })
            Field(emergency, field).Should().NotBeNull($"EmergencyAccessService.{field}");
    }

    [Fact]
    public void The_command_line_reads_the_deployments_configuration_only_from_the_attestation_and_decides_through_the_coordinator()
    {
        using var provider = BuildProduction();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IDeploymentAuthenticationConfiguration>().Should().BeOfType<AttestedDeploymentAuthenticationConfiguration>();
        scope.ServiceProvider.GetRequiredService<IAdministratorViabilityCoordinator>().Should().BeOfType<AdministratorViabilityCoordinator>();
        scope.ServiceProvider.GetRequiredService<IAuditedUnitOfWork>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IPlatformAdministratorSetGate>().Should().NotBeNull();
    }

    [Fact]
    public void The_one_mfa_instance_is_shared_between_the_commands_and_the_emergency_service()
    {
        // Recovery Access provisioning enrols through the same authenticator service the commands use, in the same scope.
        using var provider = BuildProduction();
        using var scope = provider.CreateScope();

        var mfa = scope.ServiceProvider.GetRequiredService<INativeMfaService>();
        Field(scope.ServiceProvider.GetRequiredService<IEmergencyAccessService>(), "_mfa").Should().BeSameAs(mfa);
    }

    // ------------------------------------------------------------------------------------------------ the entry point

    private static ScriptedConsole Quiet() => new(new List<string>(), inputRedirected: true);

    [Fact]
    public async Task A_command_line_that_is_not_valid_is_refused_before_the_container_is_built()
    {
        var console = Quiet();
        var built = false;

        var exit = await EmergencyCli.RunAsync(["reset-mfa", "--bare-rest"], new ConfigurationBuilder().Build(), "Production", console, _ => built = true);

        exit.Should().Be(EmergencyExitCodes.InvalidUsage);
        built.Should().BeFalse("a usage error never reaches a service");
        console.ErrorText.ToString().Should().Contain("does not take '--bare-rest'");
    }

    [Fact]
    public async Task Help_runs_without_a_container_and_exits_zero()
    {
        var console = Quiet();
        var built = false;

        var exit = await EmergencyCli.RunAsync(["reset-mfa", "--help"], new ConfigurationBuilder().Build(), "Production", console, _ => built = true);

        exit.Should().Be(EmergencyExitCodes.Success);
        built.Should().BeFalse();
        console.OutText.ToString().Should().Contain("--bare-reset").And.Contain("DESTRUCTIVE");
    }

    [Fact]
    public async Task A_container_that_cannot_be_built_is_an_internal_failure_that_names_no_message()
    {
        var console = Quiet();

        var exit = await EmergencyCli.RunAsync(
            ["status"],
            new ConfigurationBuilder().Build(),
            "Production",
            console,
            _ => throw new InvalidDataException("Server=prod;Password=hunter2"));

        exit.Should().Be(EmergencyExitCodes.InternalFailure);
        console.ErrorText.ToString().Should().Contain("InvalidDataException").And.NotContain("hunter2");
    }

    // ------------------------------------------------------------------------------------------------ the source

    private static IReadOnlyList<SourceFile> CliSource() =>
        RepositorySource.Production.Where(f => f.RelativePath.StartsWith(CliFolder, StringComparison.Ordinal)).ToList();

    [Fact]
    public void The_command_line_source_is_found()
    {
        CliSource().Select(f => f.RelativePath.Substring(CliFolder.Length)).Should().Contain(
            ["EmergencyCli.cs", "EmergencyCommandRunner.cs", "EmergencyOutcome.cs", "EmergencyCommandLine.cs", "EmergencyConsole.cs", "EmergencyHelp.cs", "Program.cs"]);
    }

    [Fact]
    public void The_command_line_names_no_repository_but_the_read_only_user_lookup()
    {
        // The candidate, credential and recovery-code repositories, the unit, the gate and the attestation store are reached only through
        // the application services; resolving one here would be a way round the audited unit, the gate or the viability decision.
        var repositories = CliSource()
            .SelectMany(f => Regex.Matches(f.Code, @"\b\w*Repository\w*\b").Select(m => m.Value))
            .Distinct()
            .ToList();
        repositories.Should().BeEquivalentTo(["IUserRepository"]);

        foreach (var forbidden in new[]
                 {
                     "DbContext", "AppertureDbContext", "IAuditedUnitOfWork", "IAdministratorViabilityCoordinator", "IPlatformAdministratorSetGate",
                     "IHostConfigurationAttestationStore", "IAuditEventWriter", "INativeTotpCredentialRepository", "INativeMfaRecoveryCodeRepository",
                     "INativeMfaReplacementCandidateRepository", "ILocalCredentialRepository",
                 })
        {
            CliSource().Where(f => f.Mentions(forbidden)).Select(f => f.RelativePath).Should().BeEmpty($"the command line must not reach {forbidden} directly");
        }
    }

    [Fact]
    public void The_user_repository_is_only_read_from_the_command_line()
    {
        var calls = CliSource()
            .SelectMany(f => Regex.Matches(f.Code, @"\busers\.(\w+)\(").Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();

        calls.Should().BeEquivalentTo(["GetByIdAsync", "GetByIdentityLinkAsync", "FindDesignatedEmergencyUserAsync"]);
    }

    private static string RunnerCode => CliSource().Single(f => f.RelativePath.EndsWith("EmergencyCommandRunner.cs", StringComparison.Ordinal)).Code;

    /// <summary>The text of one method of the runner: from its declaration to the next declaration of a member.</summary>
    private static string MethodBody(string name)
    {
        var match = Regex.Match(RunnerCode, $@"private async Task<int> {name}\(.*?(?=\n    (?:private|public|internal) )", RegexOptions.Singleline);
        match.Success.Should().BeTrue($"the runner declares {name}");
        return match.Value;
    }

    [Fact]
    public void The_switch_is_reached_only_from_the_replacement_and_the_reset_only_from_the_bare_reset()
    {
        Regex.Matches(RunnerCode, @"\bmfa\.CompleteReplacementAsync\(").Count.Should().Be(1);
        MethodBody("ReplaceAuthenticatorAsync").Should().Contain("mfa.CompleteReplacementAsync(");

        Regex.Matches(RunnerCode, @"\bmfa\.ResetAsync\(").Count.Should().Be(1);
        MethodBody("BareResetAsync").Should().Contain("mfa.ResetAsync(");

        // The destructive reset is run only on the explicit option.
        RunnerCode.Should().Contain("command.BareReset");
    }

    [Fact]
    public void Recovery_codes_are_shown_only_from_the_result_of_a_service_call_that_returned()
    {
        // The enrolment challenge carries a recovery-code list of its own; it is never read. Codes come from the committed result only.
        CliSource().Where(f => Regex.IsMatch(f.Code, @"challenge\.RecoveryCodes")).Should().BeEmpty();

        var printers = Regex.Matches(RunnerCode, @"foreach \(var code in codes\)");
        printers.Count.Should().Be(1, "one function prints recovery codes");
        MethodBody("ReplaceAuthenticatorAsync").Should().Contain("ShowCommittedRecoveryCodes(result.RecoveryCodes");
        MethodBody("EnrollMfaAsync").Should().Contain("var codes = await mfa.CompleteEnrollmentAsync(");
        MethodBody("RegenerateCodesAsync").Should().Contain("var codes = await mfa.RegenerateRecoveryCodesAsync(");
    }

    [Fact]
    public void The_command_line_writes_no_secret_to_a_log_and_takes_none_from_the_arguments()
    {
        // No logger is used by the commands, and the options carry only an account name or id and flags.
        CliSource().Where(f => Regex.IsMatch(f.Code, @"\bILogger\b|\.Log(Information|Warning|Error|Debug)\(")).Should().BeEmpty();
        CliSource().Where(f => Regex.IsMatch(f.Code, @"--(password|secret|code|totp|recovery)\b", RegexOptions.IgnoreCase)).Should().BeEmpty();
    }
}
