using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations;

/// <summary>
/// SEC-2.4 (finding F2, design gate P-7 and P-8): the eight probe-outcome columns of <c>EntraInstanceRegistrations</c> move to a
/// 1:1 <c>EntraRegistrationDiagnostics</c> table, so a probe stops rewriting the registration row, its <c>UpdatedUtc</c> and its
/// <c>rowversion</c>.
/// </summary>
/// <remarks>
/// <para><b>Fidelity.</b> The new table is created, every registration's eight values are copied into its row (one row per
/// registration, so the copy is complete and a registration never lacks one), and only then are the old columns dropped. A value
/// is copied as it is, including NULLs, the stored status numbers and the two status defaults (1, <c>NotValidated</c> and
/// <c>NotAttempted</c>), which the new columns carry as database defaults. Nothing is transformed, so a registration that was
/// never probed still reads as it did. The registration row itself is not updated by the copy: its <c>rowversion</c> and
/// <c>UpdatedUtc</c> are the values it had, which is what an administrator holding an edit form across the upgrade needs.</para>
/// <para><b>Rollback.</b> <c>Down</c> restores the eight columns with their original types and defaults, copies each diagnostics
/// row back, and drops the table. The copy-back is an update of every registration row, so it replaces their <c>rowversion</c>
/// (it cannot do otherwise); <c>UpdatedUtc</c> is left alone. A registration without a diagnostics row (inserted by a path that
/// created none) keeps the column defaults. Run in a transaction by the migrator, as every migration is, so a failure part-way
/// restores the starting schema.</para>
/// </remarks>
[DbContext(typeof(AppertureDbContext))]
[Migration(MigrationId)]
public partial class Sec24_EntraRegistrationDiagnostics : Migration
{
    public const string MigrationId = "20261014120000_Sec24_EntraRegistrationDiagnostics";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EntraRegistrationDiagnostics",
            columns: table => new
            {
                RegistrationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                OpenIdConfigurationStatus = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                LastOpenIdValidationUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                LastOpenIdValidationSummary = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                LastOpenIdValidationDetail = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                DirectoryDiscoveryStatus = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                LastDirectoryDiscoveryUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                LastDirectoryDiscoverySummary = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                LastDirectoryDiscoveryDetail = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EntraRegistrationDiagnostics", x => x.RegistrationId);
                table.ForeignKey(
                    name: "FK_EntraRegistrationDiagnostics_EntraInstanceRegistrations_RegistrationId",
                    column: x => x.RegistrationId,
                    principalTable: "EntraInstanceRegistrations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.Sql(
            """
            INSERT INTO [EntraRegistrationDiagnostics]
                ([RegistrationId],
                 [OpenIdConfigurationStatus], [LastOpenIdValidationUtc], [LastOpenIdValidationSummary], [LastOpenIdValidationDetail],
                 [DirectoryDiscoveryStatus], [LastDirectoryDiscoveryUtc], [LastDirectoryDiscoverySummary], [LastDirectoryDiscoveryDetail])
            SELECT
                [Id],
                [OpenIdConfigurationStatus], [LastOpenIdValidationUtc], [LastOpenIdValidationSummary], [LastOpenIdValidationDetail],
                [DirectoryDiscoveryStatus], [LastDirectoryDiscoveryUtc], [LastDirectoryDiscoverySummary], [LastDirectoryDiscoveryDetail]
            FROM [EntraInstanceRegistrations];
            """);

        migrationBuilder.DropColumn(name: "OpenIdConfigurationStatus", table: "EntraInstanceRegistrations");
        migrationBuilder.DropColumn(name: "LastOpenIdValidationUtc", table: "EntraInstanceRegistrations");
        migrationBuilder.DropColumn(name: "LastOpenIdValidationSummary", table: "EntraInstanceRegistrations");
        migrationBuilder.DropColumn(name: "LastOpenIdValidationDetail", table: "EntraInstanceRegistrations");
        migrationBuilder.DropColumn(name: "DirectoryDiscoveryStatus", table: "EntraInstanceRegistrations");
        migrationBuilder.DropColumn(name: "LastDirectoryDiscoveryUtc", table: "EntraInstanceRegistrations");
        migrationBuilder.DropColumn(name: "LastDirectoryDiscoverySummary", table: "EntraInstanceRegistrations");
        migrationBuilder.DropColumn(name: "LastDirectoryDiscoveryDetail", table: "EntraInstanceRegistrations");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "OpenIdConfigurationStatus", table: "EntraInstanceRegistrations", type: "int", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastOpenIdValidationUtc", table: "EntraInstanceRegistrations", type: "datetimeoffset", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "LastOpenIdValidationSummary", table: "EntraInstanceRegistrations", type: "nvarchar(512)", maxLength: 512, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "LastOpenIdValidationDetail", table: "EntraInstanceRegistrations", type: "nvarchar(4000)", maxLength: 4000, nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "DirectoryDiscoveryStatus", table: "EntraInstanceRegistrations", type: "int", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastDirectoryDiscoveryUtc", table: "EntraInstanceRegistrations", type: "datetimeoffset", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "LastDirectoryDiscoverySummary", table: "EntraInstanceRegistrations", type: "nvarchar(512)", maxLength: 512, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "LastDirectoryDiscoveryDetail", table: "EntraInstanceRegistrations", type: "nvarchar(4000)", maxLength: 4000, nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE r SET
                r.[OpenIdConfigurationStatus] = d.[OpenIdConfigurationStatus],
                r.[LastOpenIdValidationUtc] = d.[LastOpenIdValidationUtc],
                r.[LastOpenIdValidationSummary] = d.[LastOpenIdValidationSummary],
                r.[LastOpenIdValidationDetail] = d.[LastOpenIdValidationDetail],
                r.[DirectoryDiscoveryStatus] = d.[DirectoryDiscoveryStatus],
                r.[LastDirectoryDiscoveryUtc] = d.[LastDirectoryDiscoveryUtc],
                r.[LastDirectoryDiscoverySummary] = d.[LastDirectoryDiscoverySummary],
                r.[LastDirectoryDiscoveryDetail] = d.[LastDirectoryDiscoveryDetail]
            FROM [EntraInstanceRegistrations] AS r
            INNER JOIN [EntraRegistrationDiagnostics] AS d ON d.[RegistrationId] = r.[Id];
            """);

        migrationBuilder.DropTable(name: "EntraRegistrationDiagnostics");
    }
}
