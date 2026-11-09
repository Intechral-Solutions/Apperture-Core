using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// Up, down and up again of the candidate migration on a throwaway catalog that holds more than the migration's own cases do: staged
/// candidates beside the credentials when the rollback runs. The rollback may take the candidates (they exist only in the new schema)
/// and the identity column with it; it must not change or lose a stored authenticator or a recovery code, and the second upgrade
/// must give every credential an identity again, a new one. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class MfaReplacementCandidateRoundTripSqlServerTests
{
    private const string PreMigration = "20261029120000_Iam102b_HostConfigurationAttestation";

    private readonly ITestOutputHelper _output;

    public MfaReplacementCandidateRoundTripSqlServerTests(ITestOutputHelper output) => _output = output;

    private static readonly string[] Users = [NewUserId(), NewUserId(), NewUserId()];

    private static Task<List<Dictionary<string, object?>>> StoredAsync(AuditSqlScratch scratch) =>
        scratch.RowsAsync("""
            SELECT t.UserId, CONVERT(nvarchar(64), HASHBYTES('SHA2_256', t.ProtectedSecret), 2) AS SecretHash, t.ProtectorKeyId, t.IsEnrolled, t.LastUsedStep,
                   CONVERT(nvarchar(40), t.EnrolledAtUtc, 127) AS EnrolledAt,
                   (SELECT STRING_AGG(CONCAT_WS(N':', c.Id, c.CodeHash, c.Salt, CONVERT(nvarchar(40), c.ConsumedAtUtc, 127)), N',') WITHIN GROUP (ORDER BY c.Id)
                      FROM UserMfaRecoveryCodes AS c WHERE c.UserId = t.UserId) AS Codes
              FROM UserTotpCredentials AS t ORDER BY t.UserId;
            """);

    [Fact]
    public async Task A_rollback_with_staged_candidates_present_keeps_every_credential_and_code_and_the_second_upgrade_issues_new_identities()
    {
        await using var scratch = await AuditSqlScratch.CreateAtMigrationAsync("Apperture_MfaCandRoundTrip_", PreMigration);
        _output.WriteLine(scratch.ExecutionMarker);

        // Two enrolled authenticators with recovery codes (one of them partly consumed) and one pending enrollment, in the old schema.
        for (var i = 0; i < Users.Length; i++)
        {
            var enrolled = i < 2;
            await scratch.AddUserAsync(Users[i], NewUsername("rt"));
            await scratch.ExecuteAsync($"""
                INSERT INTO UserTotpCredentials (UserId, ProtectedSecret, ProtectorKeyId, IsEnrolled, LastUsedStep, CreatedAtUtc, EnrolledAtUtc, LastVerifiedAtUtc)
                VALUES (N'{Users[i]}', 0x{(0xA0 + i):X2}{(0xA0 + i):X2}, N'apperture.native-mfa.totp', {(enrolled ? 1 : 0)}, {(enrolled ? (5000 + i).ToString() : "NULL")},
                        '2026-08-01T10:00:00+00:00', {(enrolled ? "'2026-08-02T11:30:00+00:00'" : "NULL")}, {(enrolled ? "'2026-09-01T09:15:30+00:00'" : "NULL")});
                """);
            if (enrolled)
            {
                await scratch.ExecuteAsync($"""
                    INSERT INTO UserMfaRecoveryCodes (Id, UserId, CodeHash, Salt, Algorithm, CreatedAtUtc, ConsumedAtUtc)
                    VALUES (N'rc-{Users[i]}-a', N'{Users[i]}', N'hash-a', N'salt-a', N'test', '2026-08-02T11:30:00+00:00', NULL),
                           (N'rc-{Users[i]}-b', N'{Users[i]}', N'hash-b', N'salt-b', N'test', '2026-08-02T11:30:00+00:00', {(i == 1 ? "'2026-09-02T08:00:00+00:00'" : "NULL")});
                    """);
            }
        }

        var original = await StoredAsync(scratch);
        await scratch.MigrateToCurrentAsync();
        var firstIdentities = (await scratch.RowsAsync("SELECT UserId, CredentialId FROM UserTotpCredentials ORDER BY UserId;"))
            .Select(r => (string)r["CredentialId"]!).ToList();
        firstIdentities.Should().OnlyHaveUniqueItems().And.HaveCount(Users.Length);

        // The new schema holds a live candidate and history for each enrolled user.
        foreach (var user in Users.Take(2))
        {
            var credential = await scratch.ScalarAsync<string>($"SELECT CredentialId FROM UserTotpCredentials WHERE UserId = N'{user}';");
            await scratch.ExecuteAsync($"""
                INSERT INTO UserTotpReplacementCandidates (CandidateId, UserId, ProtectedSecret, ProtectorKeyId, StagedAgainstCredentialId, Status, Version, CreatedUtc, ExpiresAtUtc)
                VALUES (N'live-{user}', N'{user}', 0x0102, N'k', N'{credential}', 1, 2, SYSUTCDATETIME(), DATEADD(MINUTE, 10, SYSUTCDATETIME())),
                       (N'hist-{user}', N'{user}', 0x, N'k', N'{credential}', 2, 3, SYSUTCDATETIME(), DATEADD(MINUTE, 10, SYSUTCDATETIME()));
                """);
        }

        (await StoredAsync(scratch)).Should().BeEquivalentTo(original, o => o.WithStrictOrdering(), "the upgrade itself changed no credential value or code");

        await scratch.MigrateToAsync(PreMigration);

        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name = N'UserTotpReplacementCandidates';")).Should().Be(0, "the candidates exist only in the new schema");
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'UserTotpCredentials') AND name = N'CredentialId';")).Should().Be(0);
        (await StoredAsync(scratch)).Should().BeEquivalentTo(original, o => o.WithStrictOrdering(), "the rollback loses no authenticator, enrolment state, step or recovery code");

        await scratch.MigrateToCurrentAsync();

        var secondIdentities = (await scratch.RowsAsync("SELECT UserId, CredentialId FROM UserTotpCredentials ORDER BY UserId;"))
            .Select(r => (string)r["CredentialId"]!).ToList();
        secondIdentities.Should().OnlyHaveUniqueItems().And.HaveCount(Users.Length);
        secondIdentities.Intersect(firstIdentities).Should().BeEmpty("a re-applied migration names the stored secrets afresh, so a candidate staged before the rollback can never match");
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM UserTotpReplacementCandidates;")).Should().Be(0);
        (await StoredAsync(scratch)).Should().BeEquivalentTo(original, o => o.WithStrictOrdering());
    }
}
