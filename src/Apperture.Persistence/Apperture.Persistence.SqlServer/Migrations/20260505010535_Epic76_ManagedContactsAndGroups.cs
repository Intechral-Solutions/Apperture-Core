using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic76_ManagedContactsAndGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManagedContactGroups",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EntraGroupObjectId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedContactGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManagedContacts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EmailAddress = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LinkedUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManagedContacts_Users_LinkedUserId",
                        column: x => x.LinkedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManagedContactIdentityLinks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ManagedContactId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ExternalObjectId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExternalPrincipalName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ExternalGroupObjectId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LastSyncedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LinkStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedContactIdentityLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManagedContactIdentityLinks_ManagedContacts_ManagedContactId",
                        column: x => x.ManagedContactId,
                        principalTable: "ManagedContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManagedContactGroups_EntraGroupObjectId",
                table: "ManagedContactGroups",
                column: "EntraGroupObjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagedContactIdentityLinks_ManagedContactId",
                table: "ManagedContactIdentityLinks",
                column: "ManagedContactId");

            migrationBuilder.CreateIndex(
                name: "IX_ManagedContactIdentityLinks_Provider_ExternalObjectId",
                table: "ManagedContactIdentityLinks",
                columns: new[] { "Provider", "ExternalObjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagedContacts_LinkedUserId",
                table: "ManagedContacts",
                column: "LinkedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ManagedContacts_Source",
                table: "ManagedContacts",
                column: "Source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManagedContactGroups");

            migrationBuilder.DropTable(
                name: "ManagedContactIdentityLinks");

            migrationBuilder.DropTable(
                name: "ManagedContacts");
        }
    }
}
