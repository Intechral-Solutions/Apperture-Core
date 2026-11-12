using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The two halves of the combined foundation meet here, against real SQL Server on both isolation modes: the candidate persistence
/// (a staged candidate, a verified one, the atomic switch, a reset) and the administrator-viability coordinator (the real gate, the
/// real facts reader, the real attested provider and store). The replacement service is a later package, so each case does what that
/// service will do in the transaction of a real coordinator scope and reads back what the database holds. The switch must join the
/// scope's transaction and stand or fall with it, the replacement decision must be made on the posture alone from what the real switch
/// left, and a reset must be refused by that same decision and roll back with its candidate. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class MfaReplacementViabilityIntegrationSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private const string Admin = RoleAuthorityWorld.AdminRoleId;

    private readonly AuditIsolationCatalogs _catalogs;

    public MfaReplacementViabilityIntegrationSqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    // ---- Arrangement ----

    /// <summary>A designated Recovery Access account with a healthy posture, as the viability suites build it, with the candidate helpers over it.</summary>
    private async Task<(MfaCandidateWorld Mfa, string Recovery)> HealthyRecoveryAsync(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        await scratch.ExecuteAsync($"""
            DELETE FROM HostConfigurationAttestations;
            DELETE FROM UserTotpReplacementCandidates;
            DELETE FROM UserMfaRecoveryCodes;
            DELETE FROM UserTotpCredentials;
            DELETE FROM UserRoles WHERE RoleId = N'{Admin}';
            UPDATE Users SET IsBreakGlassEligible = 0 WHERE IsBreakGlassEligible = 1;
            DELETE FROM EntraRegistrationDiagnostics;
            DELETE FROM EntraInstanceRegistrations;
            """);
        var world = new RoleAuthorityWorld(scratch);
        var id = await world.NewUserAsync("recovery", Admin);
        await world.MakeRecoveryAccessHealthyAsync(id);
        return (MfaCandidateWorld.Over(world, new UserId(id)), id);
    }

    /// <summary>A context with no retrying execution strategy, so a case can hold a transaction open across its own steps.</summary>
    private static AppertureDbContext PlainDb(AuditSqlScratch scratch) =>
        new(new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(scratch.ConnectionString)
            .Options);

    /// <summary>The coordinator as the emergency command line composes it, over one context.</summary>
    private static AdministratorViabilityCoordinator Coordinator(AppertureDbContext db) =>
        new(
            new SqlPlatformAdministratorSetGate(db),
            new AdministratorViabilityService(
                new SqlAdministratorFactsReader(db),
                new EffectiveSignInConfigurationProvider(
                    new AttestedDeploymentAuthenticationConfiguration(new SqlHostConfigurationAttestationStore(db)),
                    new SqlEntraInstanceRegistrationRepository(db))));

    private static async Task<RecoveryAccessFacts> FactsAsync(AuditSqlScratch scratch)
    {
        await using var db = scratch.NewDb();
        return await new SqlAdministratorFactsReader(db).ReadRecoveryAccessAsync();
    }

    // ---- The facts the coordinator reads ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_staged_or_verified_candidate_is_invisible_to_the_recovery_posture(bool rcsi)
    {
        var (mfa, _) = await HealthyRecoveryAsync(rcsi);
        var before = await FactsAsync(mfa.Scratch);
        before.IsHealthy.Should().BeTrue("the arrangement is a healthy Recovery Access account");

        var candidate = await mfa.StageAsync();
        (await FactsAsync(mfa.Scratch)).Should().Be(before, "a staged candidate is not an authenticator");

        (await mfa.VerifyAsync(candidate.CandidateId, candidate.Version)).Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
        (await FactsAsync(mfa.Scratch)).Should().Be(before, "nor is a verified one, until the switch");
    }

    // ---- The switch inside a protected attempt ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_real_switch_inside_a_protected_attempt_is_decided_on_the_posture_alone_and_commits_with_it(bool rcsi)
    {
        var (mfa, recovery) = await HealthyRecoveryAsync(rcsi);
        var (candidate, version) = await mfa.StageAndVerifyAsync();
        var before = await mfa.ActiveAsync();
        var request = mfa.Request(candidate.CandidateId, version);

        NativeMfaSwitchResult switched;
        ViabilityDecision decision;
        AdministratorViabilityScope scope;
        await using (var db = PlainDb(mfa.Scratch))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            scope = await Coordinator(db).EnterAsync();
            await scope.CaptureBeforeAsync();
            switched = await new SqlNativeMfaReplacementCandidateRepository(db).SwitchAsync(request);
            decision = await scope.VerifyReplacementAsync(recovery);
            await transaction.CommitAsync();
        }

        switched.Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
        decision.IsAllowed.Should().BeTrue();
        decision.Basis.Should().Be(ViabilityDecisionBasis.PostureOnly, "the replacement reads no configuration and no fact but the posture");
        decision.Evidence.Should().Be(ConfigurationEvidence.NotRequired);
        scope.Captured!.RecoveryHealthy.Should().BeTrue();
        scope.After!.RecoveryHealthy.Should().BeTrue("the switch leaves an enrolled authenticator and unused codes");

        var after = await mfa.ActiveAsync();
        after.Credential.Should().NotBe(before.Credential, "the secret and its identity were replaced");
        (await mfa.ActiveCredentialIdAsync()).Should().Be(switched.NewCredentialId);
        (await mfa.CodeIdsAsync())!.Split(',').Should().BeEquivalentTo(request.RecoveryCodes.Select(c => c.Id), "the old codes were replaced by exactly the new ones");
        Convert.ToInt32((await mfa.CandidateRowsAsync()).Single()["Status"]).Should().Be((int)NativeMfaCandidateStatus.Consumed);
        (await FactsAsync(mfa.Scratch)).IsHealthy.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_real_switch_rolls_back_whole_with_the_attempt_that_refuses_after_it(bool rcsi)
    {
        var (mfa, recovery) = await HealthyRecoveryAsync(rcsi);
        var (candidate, version) = await mfa.StageAndVerifyAsync();
        var before = await mfa.ActiveAsync();
        var codesBefore = await mfa.CodeIdsAsync();
        var request = mfa.Request(candidate.CandidateId, version);

        await using (var db = PlainDb(mfa.Scratch))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var scope = await Coordinator(db).EnterAsync();
            await scope.CaptureBeforeAsync();
            (await new SqlNativeMfaReplacementCandidateRepository(db).SwitchAsync(request)).Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
            (await scope.VerifyReplacementAsync(recovery)).IsAllowed.Should().BeTrue();

            // Whatever fails after the switch (a refusal, an audit write, a lost connection) ends the unit, and the switch must go with it.
            await transaction.RollbackAsync();
        }

        (await mfa.ActiveAsync()).Should().Be(before, "the secret, its identity, its step and its codes are exactly what they were");
        (await mfa.CodeIdsAsync()).Should().Be(codesBefore);
        var stored = (await mfa.GetAsync(candidate.CandidateId))!;
        stored.Status.Should().Be(NativeMfaCandidateStatus.Verified, "the candidate was not consumed");
        stored.Version.Should().Be(version);

        // The same verified candidate still switches afterwards: nothing of the rolled-back attempt survived.
        (await mfa.SwitchAsync(request)).Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
    }

    // ---- A reset is not a replacement ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_reset_judged_as_a_replacement_is_refused_and_rolls_back_with_its_candidate(bool rcsi)
    {
        var (mfa, recovery) = await HealthyRecoveryAsync(rcsi);
        var (candidate, version) = await mfa.StageAndVerifyAsync();
        var before = await mfa.ActiveAsync();
        var codesBefore = await mfa.CodeIdsAsync();

        ViabilityDecision decision;
        await using (var db = PlainDb(mfa.Scratch))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var scope = await Coordinator(db).EnterAsync();
            await scope.CaptureBeforeAsync();

            // What the reset does, with the repositories it uses: the authenticator, its codes and the candidate staged against it.
            await new SqlNativeTotpCredentialRepository(db).DeleteAsync(mfa.UserId);
            await new SqlNativeMfaRecoveryCodeRepository(db).DeleteAllAsync(mfa.UserId);

            decision = await scope.VerifyReplacementAsync(recovery);
            await transaction.RollbackAsync();
        }

        decision.IsAllowed.Should().BeFalse("removing the authenticator is not a replacement");
        decision.Refusal().Code.Should().Be("recovery_access_posture_reduced");
        decision.Basis.Should().Be(ViabilityDecisionBasis.PostureOnly);
        (await mfa.ActiveAsync()).Should().Be(before, "the refused reset left the authenticator and its codes");
        (await mfa.CodeIdsAsync()).Should().Be(codesBefore);
        var stored = (await mfa.GetAsync(candidate.CandidateId))!;
        stored.Status.Should().Be(NativeMfaCandidateStatus.Verified, "and the candidate the reset removed is back with the rollback");
        stored.Version.Should().Be(version);
        (await mfa.SwitchAsync(mfa.Request(candidate.CandidateId, version))).Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
    }
}
