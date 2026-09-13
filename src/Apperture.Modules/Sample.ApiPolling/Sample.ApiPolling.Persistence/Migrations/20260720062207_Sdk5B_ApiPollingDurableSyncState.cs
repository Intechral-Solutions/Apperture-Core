using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sample.ApiPolling.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sdk5B_ApiPollingDurableSyncState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiPollingSyncStates",
                columns: table => new
                {
                    StateId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ModuleId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LastSnapshotId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LastSyncedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RecordCount = table.Column<int>(type: "int", nullable: false),
                    LastStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SummaryJson = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiPollingSyncStates", x => x.StateId);
                });

            migrationBuilder.CreateTable(
                name: "ApiPollingKnownRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StateId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Tier = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: true),
                    SourceUpdatedAtUtc = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RecordHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiPollingKnownRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiPollingKnownRecords_ApiPollingSyncStates_StateId",
                        column: x => x.StateId,
                        principalTable: "ApiPollingSyncStates",
                        principalColumn: "StateId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiPollingKnownRecords_ExternalId",
                table: "ApiPollingKnownRecords",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiPollingKnownRecords_StateId_ExternalId",
                table: "ApiPollingKnownRecords",
                columns: new[] { "StateId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiPollingSyncStates_ScopeKey",
                table: "ApiPollingSyncStates",
                column: "ScopeKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiPollingKnownRecords");

            migrationBuilder.DropTable(
                name: "ApiPollingSyncStates");
        }
    }
}
