using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic102_NotificationUserInboxItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationUserInboxItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NotificationInstanceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PrincipalUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DeliveryOutboxId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SourceRecipientKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceRecipientRef = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReadAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FirstViewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastViewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationUserInboxItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationUserInboxItems_PrincipalUserId_IsRead_CreatedAtUtc",
                table: "NotificationUserInboxItems",
                columns: new[] { "PrincipalUserId", "IsRead", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationUserInboxItems_PrincipalUserId_NotificationInstanceId",
                table: "NotificationUserInboxItems",
                columns: new[] { "PrincipalUserId", "NotificationInstanceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationUserInboxItems");
        }
    }
}
