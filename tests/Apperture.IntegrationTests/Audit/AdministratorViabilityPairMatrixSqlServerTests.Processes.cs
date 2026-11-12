using Apperture.Cli.Host;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AdministratorGateTestSupport;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2b WP5, A10.2b.13: one mechanism across writers and processes. An administrator writer composed as the Management Host composes it,
// the Entra-primary policy reached through the real proof of a pending Entra link at sign-in, and an emergency command run through the
// command line's own production container (its own composition, its own connection, its configuration from the host's attestation) all
// serialize on apperture:platform-administrator-set, whichever of them holds it; each is then decided on what the others committed.
public sealed partial class AdministratorViabilityPairMatrixSqlServerTests
{
    /// <summary>A console with nobody at it: input is redirected and every prompt reads nothing.</summary>
    private sealed class UnattendedConsole : IEmergencyConsole
    {
        public TextWriter Out { get; } = new StringWriter();

        public TextWriter Error { get; } = new StringWriter();

        public bool IsInputRedirected => true;

        public Task<string?> ReadSecretAsync(string prompt, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class FixedProfile(string connectionString) : Apperture.Bootstrap.IBootstrapConnectionProfile
    {
        public string ConnectionString => connectionString;

        public Task ValidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>The real gate, which parks its first holder after the lock is granted until the case lets it go: the command holds the gate.</summary>
    private sealed class ParkingGate(IPlatformAdministratorSetGate inner, TaskCompletionSource holding, Task release) : IPlatformAdministratorSetGate
    {
        private int _parked;

        public async ValueTask AcquireAsync(CancellationToken cancellationToken = default)
        {
            await inner.AcquireAsync(cancellationToken);
            if (Interlocked.Exchange(ref _parked, 1) == 0)
            {
                holding.TrySetResult();
                await release.WaitAsync(Patience, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Runs an emergency command through <c>EmergencyCli.RunAsync</c>, the production composition of the command line, pointed at the case's
    /// catalog with the protector the enrolment used; <paramref name="replaceGate"/> swaps the gate after the production composition.
    /// </summary>
    private static async Task<(int Exit, UnattendedConsole Console)> CommandLineAsync(World w, string[] line, Func<IServiceProvider, IPlatformAdministratorSetGate>? replaceGate = null)
    {
        var console = new UnattendedConsole();
        var exit = await EmergencyCli.RunAsync(
            line,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            "Production",
            console,
            services =>
            {
                services.AddSingleton<Apperture.Bootstrap.IBootstrapConnectionProfile>(new FixedProfile(w.Scratch.ConnectionString));
                services.AddSingleton<Apperture.Core.Security.Secrets.Abstractions.ISecretProtector>(w.W.Protector);
                if (replaceGate is not null)
                {
                    services.RemoveAll<IPlatformAdministratorSetGate>();
                    services.AddScoped(replaceGate);
                }
            });
        return (exit, console);
    }

    /// <summary>A person with no role, a Local credential and a pending Microsoft link, who is about to sign in through it.</summary>
    private static async Task<(string Id, HumanActor Actor)> PendingSignInAsync(World w)
    {
        var id = await w.R.NewUserAsync("signin");
        await w.R.AddCredentialAsync(id);
        var objectId = Guid.NewGuid().ToString("D");
        await w.Scratch.ExecuteAsync($"""
            INSERT INTO IdentityLinks (Id, UserId, Source, ExternalId, TenantId, LinkedAt, LastAuthenticatedAt)
            VALUES (N'P{id}', N'{id}', 0, N'{objectId}', N'{Tenant}', SYSDATETIMEOFFSET(), NULL);
            """);
        return (id, new HumanActor(new ActorId(id), "Sign In", IdentitySource.Entra, tenantId: Tenant, entraObjectId: objectId));
    }

    /// <summary>The sign-in: the session is resolved through the pending link, which is proven (E-2, no gate), and then the policy runs (gated).</summary>
    private static async Task<UserId?> SignInAsync(World w, HumanActor actor)
    {
        await using var db = w.Scratch.NewDb();
        return await new SqlPrincipalToUserResolver(
                new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
                HostPolicy(w, db, gate: null),
                NullLogger<SqlPrincipalToUserResolver>.Instance,
                NewUnit(db, null))
            .ResolveUserIdAsync(actor);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task F9_an_administrator_change_holding_the_gate_makes_the_command_line_and_the_sign_in_policy_wait_and_each_decides_on_what_it_committed(bool rcsi)
    {
        var (w, recovery, a) = await RecoveryWorldAsync(rcsi);
        var (person, actor) = await PendingSignInAsync(w);

        // The Management Host's administrator writer disables A's credential and holds the gate (decided, not committed).
        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var disable = Task.Run(() => RunAsync(w, new Op(Writer.DisableCredential, a), pause));
        await ReachedAsync(pause, disable);
        (await ProbeGateAsync(w.Scratch)).Should().Be(-1);

        // The emergency command line, in its own container and connection, and the sign-in's policy both queue on the same gate.
        var command = Task.Run(() => CommandLineAsync(w, ["reset-mfa", "--bare-reset", "--user-id", recovery]));
        var signIn = Task.Run(() => SignInAsync(w, actor));
        await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(w.Scratch, 2, command, signIn);
        (await RowLockWaitersAsync(w.Scratch)).Should().Be(0, "both wait for the application lock, not for a row");

        pause.Release();
        (await disable.WaitAsync(Patience)).Should().Be(Applied);
        var (exit, console) = await command.WaitAsync(Patience);
        (await signIn.WaitAsync(Patience)).Should().Be(new UserId(person), "the sign-in resolves whatever the policy reports");

        // Decided on the committed disable: A is no longer Entra-independent, so a reset that would take healthy Recovery Access away is refused.
        exit.Should().Be(EmergencyExitCodes.Refused, console.Error.ToString());
        console.Error.ToString().Should().Contain(GovernanceConflictCodes.EntraIndependentFallbackRequired);
        (await w.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpCredentials WHERE UserId = N'{recovery}';")).Should().Be(1, "the refused reset changed nothing");
        var refusal = (await w.W.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, recovery)).Should().ContainSingle().Which;
        refusal.Fields["operation"].Should().Be("mfa.reset");

        // The policy, for a person who holds no role, had nothing to protect and disabled the credential once the gate was free.
        (await w.R.CredentialDisabledAsync(person)).Should().BeTrue();
        (await w.R.AuditRowsAsync(AuditActionNames.LocalDisabledEntraPrimary, person)).Should().Be(1);
        (await ViabilityAsync(w)).RecoveryHealthy.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task F9_a_command_line_change_holding_the_gate_makes_the_administrator_writer_and_the_sign_in_policy_wait_and_each_decides_on_what_it_committed(bool rcsi)
    {
        var (w, recovery, a) = await RecoveryWorldAsync(rcsi);
        var (person, actor) = await PendingSignInAsync(w);

        // The command line's production container resets Recovery Access's authenticator and holds the gate it took.
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = Task.Run(() => CommandLineAsync(
            w,
            ["reset-mfa", "--bare-reset", "--user-id", recovery],
            sp => new ParkingGate(new SqlPlatformAdministratorSetGate(sp.GetRequiredService<AppertureDbContext>()), holding, release.Task)));
        var first = await Task.WhenAny(holding.Task, command, Task.Delay(Patience));
        first.Should().BeSameAs(holding.Task, "the command takes the gate and is held there");
        (await ProbeGateAsync(w.Scratch)).Should().Be(-1, "the command line holds the administrator-set gate");

        var disable = Task.Run(() => RunAsync(w, new Op(Writer.DisableCredential, a)));
        var signIn = Task.Run(() => SignInAsync(w, actor));
        await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(w.Scratch, 2, disable, signIn);
        (await RowLockWaitersAsync(w.Scratch)).Should().Be(0, "both wait for the application lock, not for a row");

        release.TrySetResult();
        var (exit, console) = await command.WaitAsync(Patience);
        exit.Should().Be(EmergencyExitCodes.Success, console.Error.ToString());
        (await w.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpCredentials WHERE UserId = N'{recovery}';")).Should().Be(0);

        // Decided on the committed reset: Recovery Access is no longer healthy, so the last Entra-independent administrator stays.
        (await disable.WaitAsync(Patience)).Should().Be(FallbackRequired);
        (await w.R.CredentialDisabledAsync(a)).Should().BeFalse();
        (await signIn.WaitAsync(Patience)).Should().Be(new UserId(person));
        (await w.R.CredentialDisabledAsync(person)).Should().BeTrue("a person who holds no role has nothing to protect");
        var after = await ViabilityAsync(w);
        after.RecoveryHealthy.Should().BeFalse();
        after.Now.EntraIndependentAdministrators.Should().BeEquivalentTo([a], "never the violating pair");
    }
}
