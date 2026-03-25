using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <inheritdoc />
/// <summary>ADR-0029: procedure and integration provenance on start/run records.</summary>
[DbContext(typeof(AppertureDbContext))]
[Migration("20260325040000_AddProcedureProvenance")]
public partial class AddProcedureProvenance : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ProcedureId",
            table: "WorkflowInstances",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ProcedureId",
            table: "WorkflowStartRequests",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ProcedureId",
            table: "OperationRuns",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "IntegrationId",
            table: "OperationStartRequests",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ProcedureId",
            table: "OperationStartRequests",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ProcedureId",
            table: "WorkflowInstances");

        migrationBuilder.DropColumn(
            name: "ProcedureId",
            table: "WorkflowStartRequests");

        migrationBuilder.DropColumn(
            name: "ProcedureId",
            table: "OperationRuns");

        migrationBuilder.DropColumn(
            name: "IntegrationId",
            table: "OperationStartRequests");

        migrationBuilder.DropColumn(
            name: "ProcedureId",
            table: "OperationStartRequests");
    }
}
