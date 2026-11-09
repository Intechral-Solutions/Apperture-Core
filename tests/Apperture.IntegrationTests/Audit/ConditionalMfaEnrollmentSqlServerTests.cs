using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The enrolment of a pending authenticator against real SQL Server on both isolation modes. The code is checked against the
/// secret the enrolment read; the write that enrols must be conditional on that same secret still being the pending one, or a
/// begin that replaced the secret in between leaves the account enrolled on a secret nobody verified. The controls run the original
/// unconditional write through the same interleavings to show that the setup reaches the defect. Fails, never skips, without SQL
/// Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ConditionalMfaEnrollmentSqlServerTests : IClassFixture<AuditIsolationCatalogs>, IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly AuditIsolationCatalogs _catalogs;

    public ConditionalMfaEnrollmentSqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    public void Dispose() => PauseAt.ReleaseAll();

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    private Task<MfaCandidateWorld> WorldAsync(bool rcsi) => MfaCandidateWorld.CreateAsync(_catalogs, rcsi, enrolled: false);

    private static async Task<NativeTotpStoredCredential> BeginAsync(MfaCandidateWorld world, byte[] secret)
    {
        await using var db = world.Scratch.NewDb();
        var repository = new SqlNativeTotpCredentialRepository(db);
        (await repository.UpsertPendingAsync(world.UserId, secret, "k", DateTimeOffset.UtcNow)).Should().BeTrue();
        return (await repository.GetByUserIdAsync(world.UserId))!;
    }

    private static async Task<bool> MarkAsync(MfaCandidateWorld world, string credentialId, long step = 7)
    {
        await using var db = world.Scratch.NewDb();
        return await new SqlNativeTotpCredentialRepository(db).MarkEnrolledAsync(world.UserId, credentialId, step, DateTimeOffset.UtcNow);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Enrolling_the_pending_credential_that_was_checked_enrolls_it_and_keeps_its_identity(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var pending = await BeginAsync(world, [1, 2, 3]);

        (await MarkAsync(world, pending.CredentialId, step: 42)).Should().BeTrue();

        await using var db = world.Scratch.NewDb();
        var stored = (await new SqlNativeTotpCredentialRepository(db).GetByUserIdAsync(world.UserId))!;
        stored.IsEnrolled.Should().BeTrue();
        stored.LastUsedStep.Should().Be(42);
        stored.EnrolledAtUtc.Should().NotBeNull();
        stored.LastVerifiedAtUtc.Should().Be(stored.EnrolledAtUtc);
        stored.CredentialId.Should().Be(pending.CredentialId, "enrolling does not change which secret this is");
        MfaCandidateWorld.HashOf(stored.ProtectedSecret).Should().Be(MfaCandidateWorld.HashOf(pending.ProtectedSecret), "compared by digest so a failure never prints secret bytes");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_credential_that_is_not_the_pending_one_checked_is_not_enrolled_and_nothing_changes(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var pending = await BeginAsync(world, [1, 2, 3]);
        var before = await world.ActiveAsync();

        (await MarkAsync(world, "not-the-credential-that-was-checked")).Should().BeFalse();

        (await world.ActiveAsync()).Should().Be(before);
        await using var db = world.Scratch.NewDb();
        (await new SqlNativeTotpCredentialRepository(db).GetByUserIdAsync(world.UserId))!.IsEnrolled.Should().BeFalse();
        pending.IsEnrolled.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_enrolled_or_missing_credential_is_not_enrolled_again(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        (await MarkAsync(world, "no-row-yet")).Should().BeFalse("there is no pending credential to enroll");

        var pending = await BeginAsync(world, [1, 2, 3]);
        (await MarkAsync(world, pending.CredentialId, step: 5)).Should().BeTrue();
        var enrolled = await world.ActiveAsync();

        (await MarkAsync(world, pending.CredentialId, step: 9)).Should().BeFalse("an enrolled credential is not enrolled a second time");

        (await world.ActiveAsync()).Should().Be(enrolled, "the second call did not move the last used step or the enrolment time");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Every_pending_write_gives_the_credential_a_new_identity(bool rcsi)
    {
        var world = await WorldAsync(rcsi);

        var first = await BeginAsync(world, [1, 2, 3]);
        var second = await BeginAsync(world, [4, 5, 6]);

        second.CredentialId.Should().NotBe(first.CredentialId);
        first.CredentialId.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_begin_between_the_check_and_the_enrolment_makes_the_enrolment_fail_and_the_account_stays_pending_on_the_new_secret(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        await using (var db = world.Scratch.NewDb())
        {
            var challenge = await world.Mfa(db).BeginEnrollmentAsync(world.UserId, "emergency");
            var code = Rfc6238Totp.Compute(Base32.Decode(challenge.ManualEntryKey), DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            // The enrolment has checked the code; it is parked at the conditional write.
            var pause = new PauseAt(
                c => c.CommandText.Contains("UPDATE", StringComparison.Ordinal)
                     && c.CommandText.Contains("[UserTotpCredentials]", StringComparison.Ordinal)
                     && c.CommandText.Contains("[IsEnrolled]", StringComparison.Ordinal),
                after: false);
            var enrolment = Task.Run(async () =>
            {
                await using var parked = world.Scratch.NewDb(pause);
                return await world.Mfa(parked).CompleteEnrollmentAsync(world.UserId, code, AuditActor.EmergencyCli);
            });
            var reached = await Task.WhenAny(pause.Reached, enrolment, Task.Delay(Patience));
            reached.Should().Be(pause.Reached, "the enrolment reaches its write");

            // A begin replaces the pending secret the code was checked against.
            await using (var other = world.Scratch.NewDb())
                await world.Mfa(other).BeginEnrollmentAsync(world.UserId, "emergency");
            var replacedSecret = await world.ActiveSecretHashAsync();
            pause.Release();

            var act = async () => await enrolment.WaitAsync(Patience);
            (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("changed while the code was being checked");

            (await world.Scratch.ScalarAsync<bool>($"SELECT IsEnrolled FROM UserTotpCredentials WHERE UserId = N'{world.UserId.Value}';")).Should().BeFalse("nothing was enrolled");
            (await world.ActiveSecretHashAsync()).Should().Be(replacedSecret, "the pending secret is the one the later begin wrote");
            (await world.CodeIdsAsync()).Should().BeNull("no recovery codes were issued");
            (await world.AuditRowsAsync(AuditActionNames.MfaEnrolled)).Should().Be(0);
            (await world.AuditRowsAsync(AuditActionNames.RecoveryCodesGenerated)).Should().Be(0);
        }
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Control_the_original_unconditional_enrolment_enrolls_a_secret_nobody_verified(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var checkedPending = await BeginAsync(world, [1, 2, 3]);   // the code is checked against this secret
        var later = await BeginAsync(world, [9, 9, 9]);            // a begin replaces it afterwards

        await LegacyMarkEnrolledAsync(world, 7);                   // the original write: re-reads, then writes

        (await world.Scratch.ScalarAsync<bool>($"SELECT IsEnrolled FROM UserTotpCredentials WHERE UserId = N'{world.UserId.Value}';"))
            .Should().BeTrue("this is the defect: the account is enrolled");
        (await world.ActiveSecretHashAsync()).Should().Be(MfaCandidateWorld.HashOf(later.ProtectedSecret))
            .And.NotBe(MfaCandidateWorld.HashOf(checkedPending.ProtectedSecret), "on a secret the code was never checked against");

        // The same sequence through the conditional write is refused.
        var other = await MfaCandidateWorld.CreateAsync(_catalogs, rcsi, enrolled: false);
        var first = await BeginAsync(other, [1, 2, 3]);
        await BeginAsync(other, [9, 9, 9]);
        (await MarkAsync(other, first.CredentialId)).Should().BeFalse();
        (await other.Scratch.ScalarAsync<bool>($"SELECT IsEnrolled FROM UserTotpCredentials WHERE UserId = N'{other.UserId.Value}';")).Should().BeFalse();
    }

    /// <summary>The enrolment write as it was before: re-read the row, set the flags, save. Kept only so the control can show the defect.</summary>
    private static async Task LegacyMarkEnrolledAsync(MfaCandidateWorld world, long lastUsedStep)
    {
        await using var db = world.Scratch.NewDb();
        var entity = await db.UserTotpCredentials.FirstAsync(e => e.UserId == world.UserId.Value);
        entity.IsEnrolled = true;
        entity.LastUsedStep = lastUsedStep;
        entity.EnrolledAtUtc = DateTimeOffset.UtcNow;
        entity.LastVerifiedAtUtc = entity.EnrolledAtUtc;
        await db.SaveChangesAsync();
    }
}
