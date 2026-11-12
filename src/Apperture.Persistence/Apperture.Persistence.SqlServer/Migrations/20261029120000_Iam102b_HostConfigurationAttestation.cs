using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// IAM-10.2b (WP4B-B): the table in which the Management Host records a dated reading of its own authentication configuration, so
/// the emergency command line (another process, which cannot observe the host) can read what the host says about itself, and how
/// old that is by the database's clock.
/// </summary>
/// <remarks>
/// <para><b>Additive only.</b> One new table, no existing table touched, no data copied. The table starts empty: until a host
/// writes its row there is no attestation, which a reader treats as unavailable evidence and never as a configuration.</para>
/// <para><b>Rollback.</b> <c>Down</c> drops the table. Nothing else refers to it.</para>
/// </remarks>
[DbContext(typeof(AppertureDbContext))]
[Migration(MigrationId)]
public partial class Iam102b_HostConfigurationAttestation : Migration
{
    public const string MigrationId = "20261029120000_Iam102b_HostConfigurationAttestation";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "HostConfigurationAttestations",
            columns: table => new
            {
                HostId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                SchemaVersion = table.Column<int>(type: "int", nullable: false),
                ProcessInstanceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ProcessStartedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ClaimedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                RefreshedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                StoppedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                DatabaseFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Payload = table.Column<string>(type: "nvarchar(max)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HostConfigurationAttestations", x => x.HostId);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "HostConfigurationAttestations");
    }
}
