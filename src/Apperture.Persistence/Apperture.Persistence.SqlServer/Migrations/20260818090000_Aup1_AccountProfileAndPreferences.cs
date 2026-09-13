using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// AUP-1: avatar selection columns on Users, plus typed per-user preferences and derived profile
/// image storage. All additions are nullable or absence-tolerant, so existing rows need no backfill:
/// a user with no preference row resolves to platform defaults and a user with no avatar columns
/// resolves to the neutral built-in avatar.
/// </summary>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260818090000_Aup1_AccountProfileAndPreferences")]
public partial class Aup1_AccountProfileAndPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AvatarKind",
            table: "Users",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AvatarOptionId",
            table: "Users",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AvatarImageKey",
            table: "Users",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "UserPreferences",
            columns: table => new
            {
                UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Appearance = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                TimeFormat = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserPreferences", x => x.UserId);
                table.ForeignKey(
                    name: "FK_UserPreferences_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserProfileImages",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ImageKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Size = table.Column<int>(type: "int", nullable: false),
                ContentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                ByteLength = table.Column<int>(type: "int", nullable: false),
                CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserProfileImages", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserProfileImages_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_UserProfileImages_UserId_ImageKey_Size",
            table: "UserProfileImages",
            columns: new[] { "UserId", "ImageKey", "Size" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_UserProfileImages_UserId_ImageKey_Size",
            table: "UserProfileImages");

        migrationBuilder.DropTable(name: "UserProfileImages");
        migrationBuilder.DropTable(name: "UserPreferences");

        migrationBuilder.DropColumn(name: "AvatarImageKey", table: "Users");
        migrationBuilder.DropColumn(name: "AvatarOptionId", table: "Users");
        migrationBuilder.DropColumn(name: "AvatarKind", table: "Users");
    }
}
