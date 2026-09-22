using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apperture.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class TstB2_RetireLegacyEmailOptOutOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // This is the deferred half of TstB1_NotificationTogglePreferences' conversion. TstB1
            // copied historical self-service "Email off" overrides into explicit email=false +
            // toast=false NotificationUserPreferences rows, but deliberately left the source
            // NotificationSubscriptionUserOverrides rows in place -- at that time nothing yet
            // consulted the new preference rows, so deleting them would have silently resumed email
            // delivery to already-opted-out users.
            //
            // It is now safe to delete them: SqlNotificationRecipientResolver's recipient-eligibility
            // check consults the per-user "email" preference channel (Toast-B P2, Task 2), and the
            // self-service Email switch writes only that preference, never this table (Toast-B P2,
            // Task 3). Both landed and are covered by tests before this migration was added.
            //
            // The predicate is EXACTLY the same one TstB1 used to select conversion candidates, so
            // only rows that genuinely originated from the self-service Email switch are removed:
            //   - OperationalTeamId IS NULL excludes all team-scoped overrides (a separate,
            //     still-fully-functional opt-out mechanism, untouched by this whole phase).
            //   - CreatedByActorId = UserId excludes admin-written global opt-outs, where the actor
            //     who created the row is not the user it targets.
            // This is naturally idempotent -- a second run matches and deletes zero rows.
            //
            // See docs/03-operations/notification-toggle-preference-migration-runbook.md for
            // deployment notes.
            migrationBuilder.Sql(
                """
                DELETE FROM [NotificationSubscriptionUserOverrides]
                WHERE [OperationalTeamId] IS NULL AND [CreatedByActorId] = [UserId];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This migration makes no schema changes -- it only deletes data in Up(). That deletion
            // is lossy by construction (the deleted NotificationSubscriptionUserOverrides rows cannot
            // be resurrected; their content isn't retained anywhere else), so Down() intentionally
            // does not attempt to restore it (mirrors the precedent in
            // TstB1_NotificationTogglePreferences.Down() for the same reason). There is nothing to
            // reverse.
        }
    }
}
