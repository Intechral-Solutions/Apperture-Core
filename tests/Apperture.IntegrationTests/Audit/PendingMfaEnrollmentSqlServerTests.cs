using System.Security.Cryptography;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// WP4B-A, the pending authenticator write against real SQL Server on both isolation modes. Beginning an enrollment checks, then
/// writes; the write must never undo an enrollment that completed in between. Every case forces the interleaving: the begin is parked
/// between its check and its write, the other session does its work, and the begin resumes. The control runs the original, unconditional
/// rewrite through the same interleaving to show that the setup reaches the defect. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class PendingMfaEnrollmentSqlServerTests : IClassFixture<AuditIsolationCatalogs>, IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly AuditIsolationCatalogs _catalogs;

    public PendingMfaEnrollmentSqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    public void Dispose() => PauseAt.ReleaseAll();

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    /// <summary>Waits until the parked operation reaches its pause; an operation that fails or finishes first is reported as that, not as a timeout.</summary>
    private static async Task ReachedAsync(PauseAt pause, Task operation)
    {
        var first = await Task.WhenAny(pause.Reached, operation, Task.Delay(Patience));
        if (first == operation)
        {
            await operation;
            throw new Xunit.Sdk.XunitException("The operation finished without reaching the pause.");
        }

        if (first != pause.Reached)
            throw new TimeoutException("The operation did not reach the pause.");
    }

    private async Task<(RoleAuthorityWorld World, UserId Id)> UserAsync(bool rcsi)
    {
        var world = new RoleAuthorityWorld(_catalogs.For(rcsi));
        return (world, new UserId(await world.NewUserAsync("mfa")));
    }

    private static NativeMfaService Mfa(AppertureDbContext db) =>
        new(
            new SqlNativeTotpCredentialRepository(db),
            new SqlNativeMfaRecoveryCodeRepository(db),
            new LocalPasswordHasher(),
            new ManagementHost.IntegrationTestSecretProtector(),
            TimeProvider.System,
            NullLogger<NativeMfaService>.Instance,
            NewUnit(db, null));

    private static string CodeFor(NativeMfaEnrollmentChallenge challenge) =>
        Rfc6238Totp.Compute(Base32.Decode(challenge.ManualEntryKey), DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private static async Task<IReadOnlyList<string>> EnrollAsync(AuditSqlScratch scratch, UserId id)
    {
        await using var db = scratch.NewDb();
        var mfa = Mfa(db);
        var challenge = await mfa.BeginEnrollmentAsync(id, "emergency");
        return await mfa.CompleteEnrollmentAsync(id, CodeFor(challenge), AuditActor.EmergencyCli);
    }

    private static Task<bool> IsEnrolledAsync(AuditSqlScratch scratch, UserId id) =>
        scratch.ScalarAsync<bool>($"SELECT IsEnrolled FROM UserTotpCredentials WHERE UserId = N'{id.Value}';");

    private static Task<string> SecretFingerprintAsync(AuditSqlScratch scratch, UserId id) =>
        scratch.ScalarAsync<string>($"SELECT CONVERT(nvarchar(64), HASHBYTES('SHA2_256', ProtectedSecret), 2) FROM UserTotpCredentials WHERE UserId = N'{id.Value}';");

    private static Task<int> RowCountAsync(AuditSqlScratch scratch, UserId id) =>
        scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpCredentials WHERE UserId = N'{id.Value}';");

    /// <summary>The first read of the credential row: the begin's own check, before it generates or writes anything.</summary>
    private static PauseAt AfterTheCheck() =>
        new(c => c.CommandText.Contains("[UserTotpCredentials]", StringComparison.Ordinal)
                 && c.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase),
            after: true);

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_first_enrollment_still_works_and_enrolls_with_its_recovery_codes_and_rows(bool rcsi)
    {
        var (world, id) = await UserAsync(rcsi);

        var codes = await EnrollAsync(world.Scratch, id);

        codes.Should().HaveCount(10);
        (await IsEnrolledAsync(world.Scratch, id)).Should().BeTrue();
        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserMfaRecoveryCodes WHERE UserId = N'{id.Value}';")).Should().Be(10);
        (await world.AuditRowsAsync(AuditActionNames.MfaEnrolled, id.Value)).Should().Be(1);
        (await world.AuditRowsAsync(AuditActionNames.RecoveryCodesGenerated, id.Value)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Beginning_again_while_pending_replaces_the_pending_secret_and_stays_unenrolled(bool rcsi)
    {
        var (world, id) = await UserAsync(rcsi);
        await using (var db = world.Scratch.NewDb())
            await Mfa(db).BeginEnrollmentAsync(id, "emergency");
        var first = await SecretFingerprintAsync(world.Scratch, id);

        await using (var db = world.Scratch.NewDb())
            await Mfa(db).BeginEnrollmentAsync(id, "emergency");

        (await SecretFingerprintAsync(world.Scratch, id)).Should().NotBe(first, "a legitimate retry replaces the pending secret");
        (await IsEnrolledAsync(world.Scratch, id)).Should().BeFalse();
        (await RowCountAsync(world.Scratch, id)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_pending_write_reports_whether_it_wrote_and_never_touches_an_enrolled_row(bool rcsi)
    {
        var (world, id) = await UserAsync(rcsi);
        await using (var db = world.Scratch.NewDb())
            (await new SqlNativeTotpCredentialRepository(db).UpsertPendingAsync(id, [1, 2, 3], "k", DateTimeOffset.UtcNow)).Should().BeTrue("no row yet: the first pending credential is created");
        await using (var db = world.Scratch.NewDb())
            (await new SqlNativeTotpCredentialRepository(db).UpsertPendingAsync(id, [4, 5, 6], "k", DateTimeOffset.UtcNow)).Should().BeTrue("a pending row is replaced");
        await EnrollAsync(world.Scratch, id);
        var enrolledSecret = await SecretFingerprintAsync(world.Scratch, id);

        await using (var db = world.Scratch.NewDb())
            (await new SqlNativeTotpCredentialRepository(db).UpsertPendingAsync(id, [7, 8, 9], "k", DateTimeOffset.UtcNow)).Should().BeFalse("an enrolled credential is not replaced");

        (await IsEnrolledAsync(world.Scratch, id)).Should().BeTrue();
        (await SecretFingerprintAsync(world.Scratch, id)).Should().Be(enrolledSecret);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_enrollment_that_completes_between_a_begin_s_check_and_its_write_stays_enrolled_and_the_begin_reports_already_enrolled(bool rcsi)
    {
        var (world, id) = await UserAsync(rcsi);
        var pause = AfterTheCheck();
        var begin = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            return await Mfa(db).BeginEnrollmentAsync(id, "emergency");
        });
        await ReachedAsync(pause, begin);

        // The begin saw no credential. Another operation now completes an enrollment.
        var recoveryCodes = await EnrollAsync(world.Scratch, id);
        var enrolledSecret = await SecretFingerprintAsync(world.Scratch, id);
        var codeIds = await world.Scratch.ScalarAsync<string>($"SELECT STRING_AGG(Id, N',') WITHIN GROUP (ORDER BY Id) FROM UserMfaRecoveryCodes WHERE UserId = N'{id.Value}';");
        var auditRows = await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE TargetId = N'{id.Value}';");
        pause.Release();

        var act = async () => await begin.WaitAsync(Patience);
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("already enrolled");
        (await IsEnrolledAsync(world.Scratch, id)).Should().BeTrue("a stale begin never un-enrolls");
        (await SecretFingerprintAsync(world.Scratch, id)).Should().Be(enrolledSecret, "the enrolled secret was not replaced");
        recoveryCodes.Should().HaveCount(10);
        (await world.Scratch.ScalarAsync<string>($"SELECT STRING_AGG(Id, N',') WITHIN GROUP (ORDER BY Id) FROM UserMfaRecoveryCodes WHERE UserId = N'{id.Value}';"))
            .Should().Be(codeIds, "the recovery codes are untouched");
        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE TargetId = N'{id.Value}';")).Should().Be(auditRows, "the refused begin recorded nothing");
        (await RowCountAsync(world.Scratch, id)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Control_the_original_unconditional_rewrite_un_enrolls_through_the_same_interleaving(bool rcsi)
    {
        var (world, id) = await UserAsync(rcsi);
        var pause = AfterTheCheck();
        var begin = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            // The original sequence: the service's check, then the repository's read-modify-write.
            var existing = await new SqlNativeTotpCredentialRepository(db).GetByUserIdAsync(id);
            existing.Should().BeNull();
            await LegacyUpsertPendingAsync(db, id, [9, 9, 9], "k", DateTimeOffset.UtcNow);
        });
        await ReachedAsync(pause, begin);
        await EnrollAsync(world.Scratch, id);
        (await IsEnrolledAsync(world.Scratch, id)).Should().BeTrue();
        pause.Release();

        await begin.WaitAsync(Patience);

        (await IsEnrolledAsync(world.Scratch, id)).Should().BeFalse("this is the defect: the unconditional rewrite reduced the recovery posture with no guard and no row");
    }

    /// <summary>The pending write as it was before WP4B-A, kept only so the control above can show the defect.</summary>
    private static async Task LegacyUpsertPendingAsync(AppertureDbContext db, UserId userId, byte[] protectedSecret, string protectorKeyId, DateTimeOffset startedAtUtc)
    {
        var entity = await db.UserTotpCredentials.FirstOrDefaultAsync(e => e.UserId == userId.Value);
        if (entity is null)
        {
            db.UserTotpCredentials.Add(new UserTotpCredentialEntity
            {
                UserId = userId.Value,
                ProtectedSecret = protectedSecret,
                ProtectorKeyId = protectorKeyId,
                IsEnrolled = false,
                CreatedAtUtc = startedAtUtc
            });
        }
        else
        {
            entity.ProtectedSecret = protectedSecret;
            entity.ProtectorKeyId = protectorKeyId;
            entity.IsEnrolled = false;
            entity.LastUsedStep = null;
            entity.EnrolledAtUtc = null;
            entity.LastVerifiedAtUtc = null;
            entity.CreatedAtUtc = startedAtUtc;
        }

        await db.SaveChangesAsync();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Two_begins_that_both_find_no_credential_end_with_one_pending_row_and_the_later_secret(bool rcsi)
    {
        var (world, id) = await UserAsync(rcsi);
        var pause = PauseAt.BeforeStatement("INSERT INTO [UserTotpCredentials]");
        var first = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            return await Mfa(db).BeginEnrollmentAsync(id, "emergency");
        });
        await ReachedAsync(pause, first);

        // The second begin creates the row while the first is about to.
        await using (var db = world.Scratch.NewDb())
            await Mfa(db).BeginEnrollmentAsync(id, "emergency");
        var secondFingerprint = await SecretFingerprintAsync(world.Scratch, id);
        pause.Release();

        var challenge = await first.WaitAsync(Patience);

        challenge.ManualEntryKey.Should().NotBeNullOrEmpty();
        (await RowCountAsync(world.Scratch, id)).Should().Be(1, "the key held: the first begin found the row and replaced it instead of inserting");
        (await IsEnrolledAsync(world.Scratch, id)).Should().BeFalse();
        (await SecretFingerprintAsync(world.Scratch, id)).Should().NotBe(secondFingerprint, "the later write is the stored pending secret");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_begin_that_creates_the_row_while_an_enrollment_completes_never_replaces_the_enrolled_credential(bool rcsi)
    {
        var (world, id) = await UserAsync(rcsi);
        var pause = PauseAt.BeforeStatement("INSERT INTO [UserTotpCredentials]");
        var begin = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            return await Mfa(db).BeginEnrollmentAsync(id, "emergency");
        });
        await ReachedAsync(pause, begin);

        // The row appears, and is enrolled, before the parked insert runs.
        await EnrollAsync(world.Scratch, id);
        var enrolledSecret = await SecretFingerprintAsync(world.Scratch, id);
        pause.Release();

        var act = async () => await begin.WaitAsync(Patience);
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("already enrolled");
        (await IsEnrolledAsync(world.Scratch, id)).Should().BeTrue();
        (await SecretFingerprintAsync(world.Scratch, id)).Should().Be(enrolledSecret);
    }
}
