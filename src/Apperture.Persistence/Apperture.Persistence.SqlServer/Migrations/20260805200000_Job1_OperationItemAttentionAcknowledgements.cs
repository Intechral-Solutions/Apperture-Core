using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

[DbContext(typeof(AppertureDbContext))]
[Migration("20260805200000_Job1_OperationItemAttentionAcknowledgements")]
public partial class Job1_OperationItemAttentionAcknowledgements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OperationItemAttentionAcknowledgements",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                OperationItemId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                RunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                OperationGroupId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Disposition = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                AcknowledgedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                AcknowledgedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ReopenedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ReopenedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OperationItemAttentionAcknowledgements", x => x.Id);
                table.ForeignKey(
                    name: "FK_OperationItemAttentionAcknowledgements_OperationItems_OperationItemId",
                    column: x => x.OperationItemId,
                    principalTable: "OperationItems",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        // One active acknowledgement per item (ReopenedAtUtc IS NULL).
        migrationBuilder.CreateIndex(
            name: "IX_OperationItemAttentionAcknowledgements_OperationItemId_Active",
            table: "OperationItemAttentionAcknowledgements",
            column: "OperationItemId",
            unique: true,
            filter: "[ReopenedAtUtc] IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_OperationItemAttentionAcknowledgements_RunId",
            table: "OperationItemAttentionAcknowledgements",
            column: "RunId");

        migrationBuilder.CreateIndex(
            name: "IX_OperationItemAttentionAcknowledgements_OperationGroupId",
            table: "OperationItemAttentionAcknowledgements",
            column: "OperationGroupId");

        migrationBuilder.CreateIndex(
            name: "IX_OperationItemAttentionAcknowledgements_AcknowledgedAtUtc",
            table: "OperationItemAttentionAcknowledgements",
            column: "AcknowledgedAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "OperationItemAttentionAcknowledgements");
    }
}
