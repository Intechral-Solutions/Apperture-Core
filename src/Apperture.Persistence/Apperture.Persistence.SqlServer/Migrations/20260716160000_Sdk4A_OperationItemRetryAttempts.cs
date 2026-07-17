using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

[DbContext(typeof(AppertureDbContext))]
[Migration("20260716160000_Sdk4A_OperationItemRetryAttempts")]
public partial class Sdk4A_OperationItemRetryAttempts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OperationItemRetryAttempts",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                OperationItemId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                RunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                OperationGroupId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                AttemptIndex = table.Column<int>(type: "int", nullable: false),
                IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RequestedByActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ChildRunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ChildOperationGroupId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ChildOperationItemId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ErrorMessage = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OperationItemRetryAttempts", x => x.Id);
                table.ForeignKey(
                    name: "FK_OperationItemRetryAttempts_OperationItems_OperationItemId",
                    column: x => x.OperationItemId,
                    principalTable: "OperationItems",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OperationItemRetryAttempts_OperationItemId_IdempotencyKey",
            table: "OperationItemRetryAttempts",
            columns: new[] { "OperationItemId", "IdempotencyKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_OperationItemRetryAttempts_OperationItemId_AttemptIndex",
            table: "OperationItemRetryAttempts",
            columns: new[] { "OperationItemId", "AttemptIndex" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_OperationItemRetryAttempts_ChildRunId",
            table: "OperationItemRetryAttempts",
            column: "ChildRunId");

        migrationBuilder.CreateIndex(
            name: "IX_OperationItemRetryAttempts_Status",
            table: "OperationItemRetryAttempts",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_OperationItemRetryAttempts_RunId",
            table: "OperationItemRetryAttempts",
            column: "RunId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "OperationItemRetryAttempts");
    }
}
