using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// First half of the username foundation: the permanent username ledger, a nullable
/// <c>Users.Username</c> with an explicit case-insensitive collation, and at most one Local identity link
/// per user.
/// </summary>
/// <remarks>
/// Usernames are filled by the C# data step <see cref="UsernameBackfill"/>, which
/// <see cref="AppertureDatabaseMigrator"/> runs between this migration and
/// <c>Iam9_UsernameRequired</c>. The data step is C# because the Local backfill must use the one
/// username normalization and Entra-only users need the shared username generator.
/// </remarks>
[DbContext(typeof(AppertureDbContext))]
[Migration(UsernameLedgerMigrationId)]
public partial class Iam9_UsernameLedger : Migration
{
    public const string UsernameLedgerMigrationId = "20261004120000_Iam9_UsernameLedger";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // IdentitySource.Local = 1 is a persisted contract. Fail closed rather than choosing or deleting a
        // link: an operator must decide which sign-in name each affected user keeps.
        migrationBuilder.Sql(
            """
            IF EXISTS (
                SELECT 1
                FROM IdentityLinks
                WHERE [Source] = 1
                GROUP BY [UserId]
                HAVING COUNT(*) > 1)
            BEGIN
                DECLARE @users int =
                (
                    SELECT COUNT(*)
                    FROM (
                        SELECT [UserId]
                        FROM IdentityLinks
                        WHERE [Source] = 1
                        GROUP BY [UserId]
                        HAVING COUNT(*) > 1) AS d
                );
                DECLARE @msg nvarchar(4000) = CONCAT(
                    N'Cannot apply the username foundation: ', @users,
                    N' user(s) have more than one Local identity link. Every user may have at most one Local sign-in name. ',
                    N'Find them with: SELECT [UserId], [ExternalId] FROM IdentityLinks WHERE [Source] = 1 AND [UserId] IN (SELECT [UserId] FROM IdentityLinks WHERE [Source] = 1 GROUP BY [UserId] HAVING COUNT(*) > 1). ',
                    N'Remove the extra Local links (keep the sign-in name each person uses), then start the Management Host again.');
                THROW 50011, @msg, 1;
            END
            """);

        migrationBuilder.AddColumn<string>(
            name: "Username",
            table: "Users",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: true,
            collation: "Latin1_General_100_CI_AS");

        migrationBuilder.CreateTable(
            name: "UsernameLedger",
            columns: table => new
            {
                Username = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_CI_AS"),
                UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                AssignedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                RetiredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                RetiredReason = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UsernameLedger", x => x.Username);
                table.CheckConstraint(
                    "CK_UsernameLedger_RetiredReason",
                    "([RetiredAtUtc] IS NULL AND [RetiredReason] IS NULL) OR ([RetiredAtUtc] IS NOT NULL AND [RetiredReason] IN (N'rename', N'canonicalization', N'backfill_previous'))");
                table.ForeignKey(
                    name: "FK_UsernameLedger_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_UsernameLedger_UserId",
            table: "UsernameLedger",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "UX_UsernameLedger_Current_UserId",
            table: "UsernameLedger",
            column: "UserId",
            unique: true,
            filter: "[RetiredAtUtc] IS NULL");

        migrationBuilder.CreateIndex(
            name: "UX_IdentityLinks_Local_UserId",
            table: "IdentityLinks",
            column: "UserId",
            unique: true,
            filter: "[Source] = 1");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_IdentityLinks_Local_UserId",
            table: "IdentityLinks");

        migrationBuilder.DropTable(
            name: "UsernameLedger");

        migrationBuilder.DropColumn(
            name: "Username",
            table: "Users");
    }
}
