using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowStartRequestClaimFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClaimedAt",
                table: "WorkflowStartRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimedBy",
                table: "WorkflowStartRequests",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "WorkflowStartRequests",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "WorkflowStartRequests",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "WorkflowStartRequests",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimedAt",
                table: "WorkflowStartRequests");

            migrationBuilder.DropColumn(
                name: "ClaimedBy",
                table: "WorkflowStartRequests");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "WorkflowStartRequests");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "WorkflowStartRequests");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "WorkflowStartRequests");
        }
    }
}
