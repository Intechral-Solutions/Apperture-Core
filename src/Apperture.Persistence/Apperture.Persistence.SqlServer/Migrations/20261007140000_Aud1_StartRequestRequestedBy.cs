using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// AUD-1.3 operator action attribution (audit hardening design gate §22): the start request rows record who requested the
/// start, as the cancel and external-control request rows already do.
/// </summary>
/// <remarks>
/// Additive and nullable: no existing row or column changes, and existing start requests keep a null requester (the actor
/// was never recorded, and nothing is invented for them). A new row holds a user id or a non-human actor literal such as
/// <c>system:scheduler</c>. The worker that claims the row stays in <c>ClaimedBy</c>.
/// </remarks>
[DbContext(typeof(AppertureDbContext))]
[Migration(MigrationId)]
public partial class Aud1_StartRequestRequestedBy : Migration
{
    public const string MigrationId = "20261007140000_Aud1_StartRequestRequestedBy";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RequestedBy",
            table: "WorkflowStartRequests",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RequestedBy",
            table: "OperationStartRequests",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RequestedBy",
            table: "WorkflowStartRequests");

        migrationBuilder.DropColumn(
            name: "RequestedBy",
            table: "OperationStartRequests");
    }
}
