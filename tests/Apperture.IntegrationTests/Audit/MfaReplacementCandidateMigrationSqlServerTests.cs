using Apperture.Persistence.SqlServer.Migrations;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The migration that adds the replacement-candidate table and the identity of a stored authenticator secret, applied to an existing
/// installation: a catalog migrated to the last migration before it, seeded in the old schema with pending and enrolled credentials,
/// then upgraded through the supported migrator. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class MfaReplacementCandidateMigrationSqlServerTests
{
    private const string PreMigration = "20261029120000_Iam102b_HostConfigurationAttestation";

    private readonly ITestOutputHelper _output;

    public MfaReplacementCandidateMigrationSqlServerTests(ITestOutputHelper output) => _output = output;

    private sealed record Seed(string UserId, bool Enrolled, long? LastUsedStep, byte Secret);

    private static readonly Seed[] Seeds =
    [
        new(NewUserId(), Enrolled: true, LastUsedStep: 58123, Secret: 0xA1),
        new(NewUserId(), Enrolled: true, LastUsedStep: null, Secret: 0xB2),
        new(NewUserId(), Enrolled: false, LastUsedStep: null, Secret: 0xC3),
    ];

    private async Task<AuditSqlScratch> ExistingInstallationAsync()
    {
        var scratch = await AuditSqlScratch.CreateAtMigrationAsync("Apperture_MfaCandMigration_", PreMigration);
        _output.WriteLine(scratch.ExecutionMarker);
        foreach (var seed in Seeds)
        {
            await scratch.AddUserAsync(seed.UserId, NewUsername("seed"));
            await scratch.ExecuteAsync($"""
                INSERT INTO UserTotpCredentials (UserId, ProtectedSecret, ProtectorKeyId, IsEnrolled, LastUsedStep, CreatedAtUtc, EnrolledAtUtc, LastVerifiedAtUtc)
                VALUES (N'{seed.UserId}', 0x{seed.Secret:X2}{seed.Secret:X2}{seed.Secret:X2}, N'apperture.native-mfa.totp', {(seed.Enrolled ? 1 : 0)},
                        {(seed.LastUsedStep is null ? "NULL" : seed.LastUsedStep.ToString())}, '2026-08-01T10:00:00+00:00',
                        {(seed.Enrolled ? "'2026-08-02T11:30:00+00:00'" : "NULL")}, {(seed.Enrolled ? "'2026-09-01T09:15:30+00:00'" : "NULL")});
                """);
        }

        return scratch;
    }

    private static Task<List<Dictionary<string, object?>>> CredentialRowsAsync(AuditSqlScratch scratch, bool withIdentity) =>
        scratch.RowsAsync($"""
            SELECT UserId, CONVERT(nvarchar(64), HASHBYTES('SHA2_256', ProtectedSecret), 2) AS SecretHash, ProtectorKeyId, IsEnrolled, LastUsedStep,
                   CONVERT(nvarchar(40), CreatedAtUtc, 127) AS CreatedAt, CONVERT(nvarchar(40), EnrolledAtUtc, 127) AS EnrolledAt,
                   CONVERT(nvarchar(40), LastVerifiedAtUtc, 127) AS VerifiedAt{(withIdentity ? ", CredentialId" : "")}
              FROM UserTotpCredentials ORDER BY UserId;
            """);

    private static async Task<bool> ColumnExistsAsync(AuditSqlScratch scratch, string table, string column) =>
        await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'{table}') AND name = N'{column}';") == 1;

    [Fact]
    public async Task Existing_credentials_keep_every_value_and_each_receives_its_own_identity()
    {
        await using var scratch = await ExistingInstallationAsync();
        var before = await CredentialRowsAsync(scratch, withIdentity: false);

        await scratch.MigrateToCurrentAsync();

        var after = await CredentialRowsAsync(scratch, withIdentity: true);
        after.Should().HaveCount(Seeds.Length);
        foreach (var (was, now) in before.Zip(after))
        {
            foreach (var (column, value) in was)
                now[column].Should().Be(value, $"{column} of a stored credential is not changed by the migration");
        }

        var identities = after.Select(r => (string)r["CredentialId"]!).ToList();
        identities.Should().OnlyContain(i => i.Length == 32 && i == i.ToLowerInvariant(), "an identity is a lower-case 32 character token");
        identities.Should().OnlyHaveUniqueItems("every credential has an identity of its own, pending ones included");
    }

    [Fact]
    public async Task The_new_column_is_required_and_the_candidate_table_is_empty_with_one_live_candidate_per_user()
    {
        await using var scratch = await ExistingInstallationAsync();
        await scratch.MigrateToCurrentAsync();

        (await scratch.ScalarAsync<bool>("SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(N'UserTotpCredentials') AND name = N'CredentialId';"))
            .Should().BeFalse();
        (await scratch.ScalarAsync<short>("SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID(N'UserTotpCredentials') AND name = N'CredentialId';"))
            .Should().Be(128, "nvarchar(64)");
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM UserTotpReplacementCandidates;")).Should().Be(0);

        var index = (await scratch.RowsAsync("""
            SELECT is_unique, filter_definition FROM sys.indexes
             WHERE object_id = OBJECT_ID(N'UserTotpReplacementCandidates') AND name = N'UX_UserTotpReplacementCandidates_Live';
            """)).Should().ContainSingle().Subject;
        index["is_unique"].Should().Be(true);
        index["filter_definition"].Should().Be("([Status]<(2))");

        // Every candidate of a user is reachable by an unfiltered index on the owner, so a user-scoped delete can seek.
        var owner = (await scratch.RowsAsync("""
            SELECT i.is_unique, i.has_filter, c.name AS key_column, COUNT(*) OVER () AS key_columns
              FROM sys.indexes AS i
              JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
              JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
             WHERE i.object_id = OBJECT_ID(N'UserTotpReplacementCandidates') AND i.name = N'IX_UserTotpReplacementCandidates_UserId';
            """)).Should().ContainSingle().Subject;
        owner["is_unique"].Should().Be(false);
        owner["has_filter"].Should().Be(false);
        owner["key_column"].Should().Be("UserId");
        (await scratch.ScalarAsync<string>("""
            SELECT delete_referential_action_desc FROM sys.foreign_keys
             WHERE parent_object_id = OBJECT_ID(N'UserTotpReplacementCandidates') AND name = N'FK_UserTotpReplacementCandidates_Users_UserId';
            """)).Should().Be("CASCADE");

        // The database itself refuses a second live candidate for a user and allows history beside the live one.
        var user = Seeds[0].UserId;
        string Insert(string id, int status) => $"""
            INSERT INTO UserTotpReplacementCandidates (CandidateId, UserId, ProtectedSecret, ProtectorKeyId, StagedAgainstCredentialId, Status, Version, CreatedUtc, ExpiresAtUtc)
            VALUES (N'{id}', N'{user}', 0x01, N'k', N'c', {status}, 1, SYSUTCDATETIME(), DATEADD(MINUTE, 10, SYSUTCDATETIME()));
            """;
        await scratch.ExecuteAsync(Insert("live-1", 0));
        await scratch.ExecuteAsync(Insert("history-1", 2));
        await scratch.ExecuteAsync(Insert("history-2", 3));
        var second = async () => await scratch.ExecuteAsync(Insert("live-2", 1));
        (await second.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(2601, "the filtered unique index refuses a second live candidate");
    }

    [Fact]
    public async Task Rolling_the_migration_back_restores_the_old_schema_and_keeps_the_credentials_and_it_can_be_applied_again()
    {
        await using var scratch = await ExistingInstallationAsync();
        var before = await CredentialRowsAsync(scratch, withIdentity: false);
        await scratch.MigrateToCurrentAsync();

        await scratch.MigrateToAsync(PreMigration);

        (await ColumnExistsAsync(scratch, "UserTotpCredentials", "CredentialId")).Should().BeFalse();
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name = N'UserTotpReplacementCandidates';")).Should().Be(0);
        (await CredentialRowsAsync(scratch, withIdentity: false)).Should().BeEquivalentTo(before, o => o.WithStrictOrdering(), "the credentials survive a rollback unchanged");

        await scratch.MigrateToCurrentAsync();

        (await ColumnExistsAsync(scratch, "UserTotpCredentials", "CredentialId")).Should().BeTrue();
        (await scratch.ScalarAsync<int>("SELECT COUNT(DISTINCT CredentialId) FROM UserTotpCredentials;")).Should().Be(Seeds.Length);
        Iam102b_MfaReplacementCandidates.MigrationId.Should().Be("20261108120000_Iam102b_MfaReplacementCandidates");
    }

    [Fact]
    public async Task An_earlier_build_cannot_create_an_authenticator_on_the_upgraded_schema_but_can_after_the_schema_is_downgraded()
    {
        // The rollback constraint the deployment guide documents: rolling back only the program is not supported. An earlier build
        // writes a first enrollment without the identity column; the upgraded schema refuses it (no default, by design), loudly and
        // without changing anything. After the schema is downgraded (or the pre-upgrade backup restored) the same write succeeds.
        await using var scratch = await ExistingInstallationAsync();
        await scratch.MigrateToCurrentAsync();
        var user = NewUserId();
        await scratch.AddUserAsync(user, NewUsername("older"));
        var earlierBuildInsert = $"""
            INSERT INTO UserTotpCredentials (UserId, ProtectedSecret, ProtectorKeyId, IsEnrolled, LastUsedStep, CreatedAtUtc, EnrolledAtUtc, LastVerifiedAtUtc)
            VALUES (N'{user}', 0x0102, N'apperture.native-mfa.totp', 0, NULL, SYSDATETIMEOFFSET(), NULL, NULL);
            """;

        var refused = async () => await scratch.ExecuteAsync(earlierBuildInsert);
        (await refused.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(515, "the identity is required and has no default");
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpCredentials WHERE UserId = N'{user}';")).Should().Be(0);

        await scratch.MigrateToAsync(PreMigration);
        await scratch.ExecuteAsync(earlierBuildInsert);
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpCredentials WHERE UserId = N'{user}';")).Should().Be(1);
    }
}
