using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// Second half of the username foundation: every user has a username, it is unique case-insensitively,
/// it has a current ledger row, and every Local link mirrors it.
/// </summary>
/// <remarks>
/// The usernames are written by the C# data step that the Management Host runs between
/// <c>Iam9_UsernameLedger</c> and this migration (<see cref="AppertureDatabaseMigrator"/>). A tool that
/// applies migrations directly, such as <c>dotnet ef database update</c>, skips that step. On a database
/// that already has users this migration then fails closed before changing anything, leaving the
/// database at <c>Iam9_UsernameLedger</c>; starting the Management Host completes the sequence.
/// </remarks>
[DbContext(typeof(AppertureDbContext))]
[Migration(UsernameRequiredMigrationId)]
public partial class Iam9_UsernameRequired : Migration
{
    public const string UsernameRequiredMigrationId = "20261004120100_Iam9_UsernameRequired";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF EXISTS (SELECT 1 FROM Users WHERE [Username] IS NULL)
            BEGIN
                DECLARE @missing int = (SELECT COUNT(*) FROM Users WHERE [Username] IS NULL);
                DECLARE @msg nvarchar(4000) = CONCAT(
                    N'Cannot require usernames: ', @missing,
                    N' user(s) have no username yet. Usernames are assigned by the Management Host while it applies database migrations at startup. ',
                    N'Start the Management Host to finish upgrading this database instead of applying migrations directly.');
                THROW 50012, @msg, 1;
            END
            """);

        // Both sides use the declared username collation, so no COLLATE clause is needed here.
        migrationBuilder.Sql(
            """
            IF EXISTS (
                SELECT 1
                FROM Users u
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM UsernameLedger l
                    WHERE l.[Username] = u.[Username]
                      AND l.[UserId] = u.[Id]
                      AND l.[RetiredAtUtc] IS NULL))
            BEGIN
                DECLARE @unrecorded int =
                (
                    SELECT COUNT(*)
                    FROM Users u
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM UsernameLedger l
                        WHERE l.[Username] = u.[Username]
                          AND l.[UserId] = u.[Id]
                          AND l.[RetiredAtUtc] IS NULL)
                );
                DECLARE @msg2 nvarchar(4000) = CONCAT(
                    N'Cannot require usernames: ', @unrecorded,
                    N' user(s) have a username that is not their current username in UsernameLedger. ',
                    N'Start the Management Host so the username backfill can complete, or restore the pre-upgrade backup.');
                THROW 50013, @msg2, 1;
            END
            """);

        // IdentityLinks.ExternalId keeps the database default collation, so compare exactly in binary.
        migrationBuilder.Sql(
            """
            IF EXISTS (
                SELECT 1
                FROM IdentityLinks il
                INNER JOIN Users u ON u.[Id] = il.[UserId]
                WHERE il.[Source] = 1
                  AND il.[ExternalId] COLLATE Latin1_General_100_BIN2 <> u.[Username] COLLATE Latin1_General_100_BIN2)
            BEGIN
                DECLARE @mismatched int =
                (
                    SELECT COUNT(*)
                    FROM IdentityLinks il
                    INNER JOIN Users u ON u.[Id] = il.[UserId]
                    WHERE il.[Source] = 1
                      AND il.[ExternalId] COLLATE Latin1_General_100_BIN2 <> u.[Username] COLLATE Latin1_General_100_BIN2
                );
                DECLARE @msg3 nvarchar(4000) = CONCAT(
                    N'Cannot require usernames: ', @mismatched,
                    N' Local identity link(s) do not match their user''s username. ',
                    N'Start the Management Host so the username backfill can complete, or restore the pre-upgrade backup.');
                THROW 50014, @msg3, 1;
            END
            """);

        migrationBuilder.Sql(
            "ALTER TABLE [Users] ALTER COLUMN [Username] nvarchar(256) COLLATE Latin1_General_100_CI_AS NOT NULL;");

        migrationBuilder.CreateIndex(
            name: "UX_Users_Username",
            table: "Users",
            column: "Username",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_Users_Username",
            table: "Users");

        migrationBuilder.Sql(
            "ALTER TABLE [Users] ALTER COLUMN [Username] nvarchar(256) COLLATE Latin1_General_100_CI_AS NULL;");
    }
}
