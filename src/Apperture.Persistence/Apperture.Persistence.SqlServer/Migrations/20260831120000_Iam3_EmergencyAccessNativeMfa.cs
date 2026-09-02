using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// IAM-3: native TOTP credentials, hashed MFA recovery codes, and a unique filtered
/// index so at most one user can be designated as the emergency administrator.
/// </summary>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260831120000_Iam3_EmergencyAccessNativeMfa")]
public partial class Iam3_EmergencyAccessNativeMfa : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UserTotpCredentials",
            columns: table => new
            {
                UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ProtectedSecret = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                ProtectorKeyId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                IsEnrolled = table.Column<bool>(type: "bit", nullable: false),
                LastUsedStep = table.Column<long>(type: "bigint", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                EnrolledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                LastVerifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserTotpCredentials", x => x.UserId);
                table.ForeignKey(
                    name: "FK_UserTotpCredentials_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "UserMfaRecoveryCodes",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                CodeHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                Salt = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Algorithm = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserMfaRecoveryCodes", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserMfaRecoveryCodes_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_UserMfaRecoveryCodes_UserId",
            table: "UserMfaRecoveryCodes",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "UX_Users_EmergencyAccess",
            table: "Users",
            column: "IsBreakGlassEligible",
            unique: true,
            filter: "[IsBreakGlassEligible] = 1");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "UserTotpCredentials");
        migrationBuilder.DropTable(name: "UserMfaRecoveryCodes");
        migrationBuilder.DropIndex(name: "UX_Users_EmergencyAccess", table: "Users");
    }
}
