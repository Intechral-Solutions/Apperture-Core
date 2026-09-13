using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Epic73_RemoveUnisaTablesFromPlatformModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Unisa* tables remain in the database; they are modeled by UnisaPaymentPlansDbContext
            // (separate __EFMigrationsHistory_UnisaPaymentPlans). Platform snapshot no longer owns them.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: do not recreate Unisa* tables via platform migrations.
        }
    }
}
