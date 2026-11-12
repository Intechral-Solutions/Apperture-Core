using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Xunit;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The contract of the candidate persistence at the repository interface, one writer at a time, against real SQL Server on both
/// isolation modes. The reset path removes a staged candidate in two places (the credential delete and the recovery-code removal) that
/// mask each other when only the service is observed, so each is driven alone here; the switch and the verification each name the
/// owner and the active credential in their own statement, and a case below makes each of those conditions the only one standing.
/// Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class MfaReplacementCandidateContractSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private readonly AuditIsolationCatalogs _catalogs;

    public MfaReplacementCandidateContractSqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    private Task<MfaCandidateWorld> WorldAsync(bool rcsi) => MfaCandidateWorld.CreateAsync(_catalogs, rcsi);

    // ---- The three writers that remove a staged candidate, each on its own ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Deleting_the_authenticator_alone_removes_the_staged_candidate_and_only_that_users(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var other = await world.OtherEnrolledUserAsync();
        await world.StageAndVerifyAsync();
        await world.StageAsync(user: other);
        var codesBefore = await world.CodeIdsAsync();

        await using (var db = world.Scratch.NewDb())
            await new SqlNativeTotpCredentialRepository(db).DeleteAsync(world.UserId);

        (await world.CandidateRowCountAsync()).Should().Be(0, "a candidate staged against a removed authenticator never outlives it");
        (await world.CandidateRowCountAsync(other)).Should().Be(1, "another user's candidate stays");
        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpCredentials WHERE UserId = N'{world.UserId.Value}';")).Should().Be(0);
        (await world.CodeIdsAsync()).Should().Be(codesBefore, "this writer removes the authenticator and its candidate, not the recovery codes");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Removing_all_recovery_codes_alone_removes_the_staged_candidate_and_leaves_the_authenticator(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var other = await world.OtherEnrolledUserAsync();
        await world.StageAndVerifyAsync();
        await world.StageAsync(user: other);
        var credentialBefore = await world.ActiveCredentialIdAsync();
        var secretBefore = await world.ActiveSecretHashAsync();

        await using (var db = world.Scratch.NewDb())
            await new SqlNativeMfaRecoveryCodeRepository(db).DeleteAllAsync(world.UserId);

        (await world.CandidateRowCountAsync()).Should().Be(0, "the codes a candidate would replace are gone, so the candidate is");
        (await world.CandidateRowCountAsync(other)).Should().Be(1, "another user's candidate stays");
        (await world.CodeIdsAsync()).Should().BeNullOrEmpty();
        (await world.ActiveCredentialIdAsync()).Should().Be(credentialBefore);
        (await world.ActiveSecretHashAsync()).Should().Be(secretBefore, "the authenticator itself is untouched by this writer");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Replacing_all_recovery_codes_alone_removes_the_staged_candidate_and_leaves_the_authenticator(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var other = await world.OtherEnrolledUserAsync();
        await world.StageAndVerifyAsync();
        await world.StageAsync(user: other);
        var credentialBefore = await world.ActiveCredentialIdAsync();
        var secretBefore = await world.ActiveSecretHashAsync();
        var replacement = world.Request("unused", 1, codePrefix: "rp").RecoveryCodes;

        await using (var db = world.Scratch.NewDb())
            await new SqlNativeMfaRecoveryCodeRepository(db).ReplaceAllAsync(world.UserId, replacement);

        (await world.CandidateRowCountAsync()).Should().Be(0);
        (await world.CandidateRowCountAsync(other)).Should().Be(1, "another user's candidate stays");
        (await world.CodeIdsAsync())!.Split(',').Should().BeEquivalentTo(replacement.Select(c => c.Id));
        (await world.ActiveCredentialIdAsync()).Should().Be(credentialBefore);
        (await world.ActiveSecretHashAsync()).Should().Be(secretBefore);
    }

    // ---- The conditions each statement names for itself ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_switch_checks_the_owner_itself_not_only_the_credential_the_candidate_was_staged_against(bool rcsi)
    {
        // Credential identities are unique, so the staged-against condition alone already refuses another user's candidate. Two users are given
        // the same identity so that only the ownership conditions of the switch stand between the second user and the first user's candidate.
        var world = await WorldAsync(rcsi);
        var other = await world.OtherEnrolledUserAsync();
        var (candidate, version) = await world.StageAndVerifyAsync();
        var shared = await world.ActiveCredentialIdAsync();
        await world.Scratch.ExecuteAsync($"UPDATE UserTotpCredentials SET CredentialId = N'{shared}' WHERE UserId = N'{other.Value}';");
        var otherBefore = await world.ActiveAsync(other);
        var ownerBefore = await world.ActiveAsync();

        var result = await world.SwitchAsync(world.Request(candidate.CandidateId, version, user: other));

        result.Outcome.Should().Be(NativeMfaCandidateOutcome.NotFound);
        result.NewCredentialId.Should().BeNull();
        (await world.ActiveAsync(other)).Should().Be(otherBefore, "the second user's authenticator and codes are untouched");
        (await world.ActiveAsync()).Should().Be(ownerBefore, "so are the owner's");
        (await world.GetAsync(candidate.CandidateId))!.Status.Should().Be(NativeMfaCandidateStatus.Verified, "the candidate is not consumed by a user it does not belong to");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Verification_is_refused_when_the_authenticator_changed_after_staging_and_changes_nothing(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var candidate = await world.StageAsync();
        await world.Scratch.ExecuteAsync($"UPDATE UserTotpCredentials SET CredentialId = N'changed-behind' WHERE UserId = N'{world.UserId.Value}';");
        var active = await world.ActiveAsync();

        var result = await world.VerifyAsync(candidate.CandidateId, candidate.Version);

        result.Outcome.Should().Be(NativeMfaCandidateOutcome.ActiveEnrollmentChanged);
        result.Version.Should().BeNull();
        var stored = (await world.GetAsync(candidate.CandidateId))!;
        stored.Status.Should().Be(NativeMfaCandidateStatus.Staged, "a candidate staged against another authenticator is never marked verified");
        stored.Version.Should().Be(candidate.Version);
        stored.MatchedStep.Should().BeNull();
        (await world.ActiveAsync()).Should().Be(active);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Verification_with_no_enrolled_authenticator_reports_that_and_changes_nothing(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var candidate = await world.StageAsync();
        await world.Scratch.ExecuteAsync($"DELETE FROM UserTotpCredentials WHERE UserId = N'{world.UserId.Value}';");

        var result = await world.VerifyAsync(candidate.CandidateId, candidate.Version);

        result.Outcome.Should().Be(NativeMfaCandidateOutcome.NoActiveEnrollment);
        result.Version.Should().BeNull();
        var stored = (await world.GetAsync(candidate.CandidateId))!;
        stored.Status.Should().Be(NativeMfaCandidateStatus.Staged);
        stored.Version.Should().Be(candidate.Version);
    }
}
