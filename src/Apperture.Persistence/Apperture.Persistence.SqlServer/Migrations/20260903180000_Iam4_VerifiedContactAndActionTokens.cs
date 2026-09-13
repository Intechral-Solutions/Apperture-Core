using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// IAM-4: verified contact-email state and hashed one-time account-action tokens
/// (email verification, Apperture setup invitations, forgotten-password reset).
/// </summary>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260903180000_Iam4_VerifiedContactAndActionTokens")]
public partial class Iam4_VerifiedContactAndActionTokens : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "EmailVerifiedAtUtc",
            table: "Users",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PendingEmailAddress",
            table: "Users",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "UX_Users_VerifiedContactEmail",
            table: "Users",
            column: "EmailAddress",
            unique: true,
            filter: "[EmailVerifiedAtUtc] IS NOT NULL AND [EmailAddress] IS NOT NULL");

        migrationBuilder.CreateTable(
            name: "AccountActionTokens",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Purpose = table.Column<int>(type: "int", nullable: false),
                TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                DestinationEmail = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccountActionTokens", x => x.Id);
                table.ForeignKey(
                    name: "FK_AccountActionTokens_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "UX_AccountActionTokens_TokenHash",
            table: "AccountActionTokens",
            column: "TokenHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AccountActionTokens_UserId_Purpose",
            table: "AccountActionTokens",
            columns: new[] { "UserId", "Purpose" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AccountActionTokens");
        migrationBuilder.DropIndex(name: "UX_Users_VerifiedContactEmail", table: "Users");
        migrationBuilder.DropColumn(name: "EmailVerifiedAtUtc", table: "Users");
        migrationBuilder.DropColumn(name: "PendingEmailAddress", table: "Users");
    }
}
