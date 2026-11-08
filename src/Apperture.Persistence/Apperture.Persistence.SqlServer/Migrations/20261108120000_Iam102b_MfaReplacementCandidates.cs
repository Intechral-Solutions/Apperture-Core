using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// IAM-10.2b (WP4B-C): the persistence for replacing an enrolled authenticator without removing it first, and the identity of a
/// stored authenticator secret that lets an enrolment or a switch prove it acts on the secret that was verified.
/// </summary>
/// <remarks>
/// <para><b>What it does.</b> Adds <c>UserTotpCredentials.CredentialId</c> (the identity of the stored secret) and gives every
/// existing row, pending or enrolled, its own random value; then makes the column required. Adds the table
/// <c>UserTotpReplacementCandidates</c>, which starts empty, with a filtered unique index that allows one live (staged or
/// verified) candidate per user.</para>
/// <para><b>Data.</b> No secret, recovery code, step or timestamp of an existing credential is changed. A candidate never shares a
/// row with the active credential.</para>
/// <para><b>Rollback.</b> <c>Down</c> drops the candidate table and the column. Nothing else refers to either.</para>
/// </remarks>
[DbContext(typeof(AppertureDbContext))]
[Migration(MigrationId)]
public partial class Iam102b_MfaReplacementCandidates : Migration
{
    public const string MigrationId = "20261108120000_Iam102b_MfaReplacementCandidates";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Nullable first, so existing rows can be given an identity before the column becomes required. NEWID() is evaluated per row.
        migrationBuilder.AddColumn<string>(
            name: "CredentialId",
            table: "UserTotpCredentials",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.Sql(
            "UPDATE [UserTotpCredentials] SET [CredentialId] = LOWER(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N'')) WHERE [CredentialId] IS NULL;");

        migrationBuilder.AlterColumn<string>(
            name: "CredentialId",
            table: "UserTotpCredentials",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(64)",
            oldMaxLength: 64,
            oldNullable: true);

        migrationBuilder.CreateTable(
            name: "UserTotpReplacementCandidates",
            columns: table => new
            {
                CandidateId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ProtectedSecret = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                ProtectorKeyId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                StagedAgainstCredentialId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                Version = table.Column<int>(type: "int", nullable: false),
                CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                VerifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                MatchedStep = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserTotpReplacementCandidates", x => x.CandidateId);
                table.ForeignKey(
                    name: "FK_UserTotpReplacementCandidates_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "UX_UserTotpReplacementCandidates_Live",
            table: "UserTotpReplacementCandidates",
            column: "UserId",
            unique: true,
            filter: "[Status] < 2");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "UserTotpReplacementCandidates");

        migrationBuilder.DropColumn(name: "CredentialId", table: "UserTotpCredentials");
    }
}
