using System.Collections.Concurrent;
using System.Diagnostics;
using Apperture.Bootstrap;
using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// IAM-10.2b WP5, deferred items D-C and D-D: what the Entra-primary policy does at Entra sign-in while the administrator-set gate is busy,
/// and while the sign-in configuration cannot be established, measured against real SQL Server on both isolation modes through the real
/// sign-in path (the proof of a proven link, then the policy). It characterizes the existing behaviour for the owner's ruling and changes
/// none: one warning per affected sign-in, no audit row, every sign-in succeeds, a busy sign-in is held for the gate's bound, the policy is
/// applied at the next sign-in, and a user with nothing left to disable never touches the gate. The measured figures are written to the test
/// output. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class EntraPrimaryPolicyBusyVolumeSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private const string Admin = BootstrapConstants.PlatformAdminRoleId;
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const int SignIns = 8;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public EntraPrimaryPolicyBusyVolumeSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception)));
    }

    private async Task<RoleAuthorityWorld> FreshAsync(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        await scratch.ExecuteAsync($"""
            DELETE FROM UserMfaRecoveryCodes;
            DELETE FROM UserTotpCredentials;
            DELETE FROM UserRoles WHERE RoleId = N'{Admin}';
            UPDATE Users SET IsBreakGlassEligible = 0 WHERE IsBreakGlassEligible = 1;
            """);
        var world = new RoleAuthorityWorld(scratch);
        await world.SetActiveRegistrationAsync(Tenant);
        return world;
    }

    /// <summary>Someone about to sign in through a proven Microsoft link (its directory object id a GUID) who still has an enabled Local credential.</summary>
    private sealed record Signer(string Id, string ObjectId);

    private static async Task<Signer> SignerAsync(RoleAuthorityWorld world, string stem, params string[] roles)
    {
        var id = await world.NewUserAsync(stem, roles);
        if (roles.Length == 0)
            await world.AddCredentialAsync(id);
        var objectId = Guid.NewGuid().ToString("D");
        await world.Scratch.ExecuteAsync($"""
            INSERT INTO IdentityLinks (Id, UserId, Source, ExternalId, TenantId, LinkedAt, LastAuthenticatedAt)
            VALUES (N'E{id}', N'{id}', 0, N'{objectId}', N'{Tenant}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);
        return new Signer(id, objectId);
    }

    /// <summary>Directory discovery is not part of a sign-in; a call would be a defect of the case.</summary>
    private sealed class NoDirectoryDiscovery : IEntraDirectoryDiscoveryService
    {
        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>> SearchGroupsAsync(EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DirectoryDiscoveryResult<EntraDirectoryUser?>> GetUserByIdAsync(EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DirectoryDiscoveryResult<EntraDirectoryGroup?>> GetGroupByIdAsync(EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static EntraPrimaryAuthenticationPolicy Policy(AuditSqlScratch scratch, AppertureDbContext db, TimeSpan bound, DeploymentAuthenticationReading reading)
    {
        var resolver = EntraRegistrationSqlHarness.SecretResolver(db);
        return new EntraPrimaryAuthenticationPolicy(
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlLocalCredentialRepository(db),
            NullLogger<EntraPrimaryAuthenticationPolicy>.Instance,
            EntraRegistrationSqlHarness.Viability(db, reading: reading, resolver: resolver, gate: new SqlPlatformAdministratorSetGate(db, bound)),
            AdministratorViabilitySqlServerTests.Service(db, reading, resolver),
            NewUnit(db, null),
            new SqlAuditEventWriter(scratch.Options(), null, HarnessCatalog, null, null, null));
    }

    /// <summary>
    /// One successful Entra sign-in as the host completes it (<see cref="EntraIdentityAssociationService.CompleteSignInAsync"/>): the proven
    /// link's proof is refreshed, then the policy is applied. Returns the signed-in user (null if the sign-in failed) and how long it took.
    /// </summary>
    private static async Task<(UserId? User, TimeSpan Took)> SignInAsync(
        AuditSqlScratch scratch, Signer signer, ILogger<EntraIdentityAssociationService> log, TimeSpan bound, DeploymentAuthenticationReading? reading = null)
    {
        await using var db = scratch.NewDb();
        var watch = Stopwatch.StartNew();
        var outcome = await new EntraIdentityAssociationService(
                new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
                new SqlEntraInstanceRegistrationRepository(db),
                new NoDirectoryDiscovery(),
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                TimeProvider.System,
                log,
                entraPrimaryPolicy: Policy(scratch, db, bound, reading ?? AdministratorViabilitySqlServerTests.Reading(Tenant)),
                auditedUnitOfWork: NewUnit(db, null),
                auditEvents: new SqlAuditEventWriter(scratch.Options(), null, HarnessCatalog, null, null, null))
            .CompleteSignInAsync(new EntraIdentityAssertion(signer.ObjectId, Tenant, "sub"));
        return (outcome.Succeeded ? outcome.UserId : null, watch.Elapsed);
    }

    private static Task<int> PolicyRowsAsync(AuditSqlScratch scratch, IEnumerable<string> userIds) =>
        scratch.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM AuditLogs WHERE Action IN (N'{AuditActionNames.LocalDisabledEntraPrimary}', N'{AuditActionNames.LocalDisableDeferredLastAdmin}') "
            + $"AND TargetId IN ({string.Join(", ", userIds.Select(u => $"N'{u}'"))});");

    private static async Task<RawSession> HoldTheGateAsync(AuditSqlScratch scratch) => await AdministratorGateTestSupport.HoldTheGateAsync(scratch);

    // ================================================================================================ D-C: a busy gate

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task DC_while_the_gate_is_busy_every_sign_in_that_still_has_a_local_credential_to_disable_succeeds_warns_once_and_writes_nothing(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var signers = new List<Signer>();
        for (var i = 0; i < SignIns; i++)
            signers.Add(await SignerAsync(world, $"busy{i}"));
        var ids = signers.Select(s => s.Id).ToList();
        var bound = TimeSpan.FromSeconds(1);
        var log = new CapturingLogger<EntraIdentityAssociationService>();

        (UserId? User, TimeSpan Took)[] busy;
        await using (var holder = await HoldTheGateAsync(world.Scratch))
        {
            busy = await Task.WhenAll(signers.Select(s => SignInAsync(world.Scratch, s, log, bound))).WaitAsync(Patience);
            await holder.RollbackAsync();
        }

        busy.Select(b => b.User).Should().Equal(signers.Select(s => (UserId?)new UserId(s.Id)), "a busy gate never fails a sign-in");
        busy.Should().OnlyContain(b => b.Took >= bound, "each sign-in waits for the gate for its whole bound before it gives up");
        var warnings = log.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        warnings.Should().HaveCount(SignIns, "one warning per affected sign-in, and no more");
        foreach (var id in ids)
            warnings.Should().ContainSingle(w => w.Message.Contains(id) && w.Message.Contains("another change to administrator access is in progress"));
        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Error);
        (await PolicyRowsAsync(world.Scratch, ids)).Should().Be(0, "a busy gate writes no audit row (the accepted ruling)");
        foreach (var id in ids)
            (await world.CredentialDisabledAsync(id)).Should().BeFalse("nothing was applied: Local sign-in stays enabled, the safe direction");

        // The next sign-in of each, with the gate free, applies the policy: the repetition stops once applied.
        var next = new CapturingLogger<EntraIdentityAssociationService>();
        foreach (var signer in signers)
            (await SignInAsync(world.Scratch, signer, next, bound)).User.Should().NotBeNull();
        next.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
        foreach (var id in ids)
            (await world.CredentialDisabledAsync(id)).Should().BeTrue();
        (await PolicyRowsAsync(world.Scratch, ids)).Should().Be(SignIns, "one success row each");

        // Steady state: with nothing left to disable a sign-in takes no gate, so a busy gate neither holds it up nor makes it warn.
        var steady = new CapturingLogger<EntraIdentityAssociationService>();
        (UserId? User, TimeSpan Took)[] quiet;
        await using (var holder = await HoldTheGateAsync(world.Scratch))
        {
            quiet = await Task.WhenAll(signers.Select(s => SignInAsync(world.Scratch, s, steady, bound))).WaitAsync(Patience);
            await holder.RollbackAsync();
        }

        quiet.Should().OnlyContain(q => q.User != null && q.Took < bound, "the lock-free exit answers before the gate is ever asked for");
        steady.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);

        _output.WriteLine(
            $"D-C measurement (rcsi: {rcsi}): {SignIns} concurrent Entra sign-ins with a Local credential to disable, gate held, bound {bound.TotalMilliseconds:0} ms: "
            + $"{warnings.Count} warnings, 0 audit rows, 0 failed sign-ins; each held {busy.Min(b => b.Took).TotalMilliseconds:0} to {busy.Max(b => b.Took).TotalMilliseconds:0} ms. "
            + $"Next sign-in each: applied, 0 warnings. Steady state with the gate held: 0 warnings, {quiet.Max(q => q.Took).TotalMilliseconds:0} ms at most.");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task DC_at_the_production_bound_a_sign_in_waits_the_full_fifteen_seconds_for_a_busy_gate_before_it_succeeds(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var signer = await SignerAsync(world, "production-bound");
        var log = new CapturingLogger<EntraIdentityAssociationService>();

        (UserId? User, TimeSpan Took) result;
        await using (var holder = await HoldTheGateAsync(world.Scratch))
        {
            result = await SignInAsync(world.Scratch, signer, log, PlatformAdministratorSetLock.DefaultTimeout).WaitAsync(Patience);
            await holder.RollbackAsync();
        }

        result.User.Should().Be(new UserId(signer.Id));
        result.Took.Should().BeGreaterThanOrEqualTo(PlatformAdministratorSetLock.DefaultTimeout, "the sign-in is held for the gate's whole bound");
        result.Took.Should().BeLessThan(PlatformAdministratorSetLock.DefaultTimeout + TimeSpan.FromSeconds(15));
        log.Entries.Count(e => e.Level == LogLevel.Warning).Should().Be(1);
        _output.WriteLine($"D-C measurement (rcsi: {rcsi}): one Entra sign-in against a gate held past the production bound of {PlatformAdministratorSetLock.DefaultTimeout.TotalSeconds:0} s took {result.Took.TotalMilliseconds:0} ms and succeeded with one warning.");
    }

    // ================================================================================================ D-D: configuration unavailable

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task DD_while_the_sign_in_configuration_is_unavailable_every_administrators_sign_in_succeeds_warns_once_and_writes_nothing(bool rcsi)
    {
        // Administrators who sign in both ways: whether disabling their Local credential is safe depends on the configuration, and the Local
        // setting cannot be established, so the policy defers for each (DeferredAdministratorViabilityUnavailable).
        var world = await FreshAsync(rcsi);
        var signers = new List<Signer>();
        for (var i = 0; i < SignIns; i++)
            signers.Add(await SignerAsync(world, $"unavailable{i}", Admin));
        var ids = signers.Select(s => s.Id).ToList();
        var unknownLocal = new DeploymentAuthenticationReading(
            ConfigurationTrustLevel.Unknown,
            ConfigurationTrustLevel.Trusted,
            ["LocalAuth:Enabled is not set in the configuration this reader was started with."],
            true,
            new EntraAuthOptions { ClientSecret = "deployment-secret" },
            AuthoritativeAboutSecrets: true,
            Running: null);
        var log = new CapturingLogger<EntraIdentityAssociationService>();

        var results = await Task.WhenAll(signers.Select(s => SignInAsync(world.Scratch, s, log, PlatformAdministratorSetLock.DefaultTimeout, unknownLocal))).WaitAsync(Patience);

        results.Should().OnlyContain(r => r.User != null, "an unavailable configuration never fails a sign-in");
        var warnings = log.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        warnings.Should().HaveCount(SignIns);
        foreach (var id in ids)
            warnings.Should().ContainSingle(w => w.Message.Contains(id) && w.Message.Contains("the sign-in configuration could not be established"));
        (await PolicyRowsAsync(world.Scratch, ids)).Should().Be(0, "an unavailable configuration writes no audit row (the accepted ruling)");
        foreach (var id in ids)
            (await world.CredentialDisabledAsync(id)).Should().BeFalse();

        // It repeats at every sign-in while the configuration stays unavailable.
        var again = new CapturingLogger<EntraIdentityAssociationService>();
        foreach (var signer in signers)
            await SignInAsync(world.Scratch, signer, again, PlatformAdministratorSetLock.DefaultTimeout, unknownLocal);
        again.Entries.Count(e => e.Level == LogLevel.Warning).Should().Be(SignIns, "one warning per sign-in, again");

        _output.WriteLine(
            $"D-D measurement (rcsi: {rcsi}): {SignIns} administrators' Entra sign-ins with the Local setting unknown: {warnings.Count} warnings, 0 audit rows, 0 failed sign-ins; "
            + $"a second round: {again.Entries.Count(e => e.Level == LogLevel.Warning)} warnings again. Slowest sign-in {results.Max(r => r.Took).TotalMilliseconds:0} ms.");
    }
}
