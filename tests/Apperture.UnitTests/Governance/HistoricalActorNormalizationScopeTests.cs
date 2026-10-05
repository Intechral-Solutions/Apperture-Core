using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// Pins the historical actor normalization scope to the IAM-9 design gate §17: exactly the approved non-audit
/// columns, all present in the model, never the audit log, and the literal vocabulary taken only from ActorLiterals.
/// </summary>
public sealed class HistoricalActorNormalizationScopeTests
{
    private static readonly (string Table, string Column)[] ApprovedScope =
    [
        ("RuntimeControlRequests", "RequestedBy"),
        ("WorkflowExternalControlRequests", "RequestedBy"),
        ("PlatformSettingValues", "UpdatedByPrincipalId"),
        ("EntraAccessRequests", "ReviewedByUserId"),
        ("AttentionIncidents", "AcknowledgedBy"),
        ("OperationItemAttentionAcknowledgements", "AcknowledgedByUserId"),
        ("OperationItemAttentionAcknowledgements", "ReopenedByUserId"),
        ("OperationItemRetryAttempts", "RequestedByActorId"),
        ("NotificationDefinitions", "CreatedByActorId"),
        ("NotificationDefinitions", "UpdatedByActorId"),
        ("NotificationSubscriptions", "CreatedByActorId"),
        ("NotificationSubscriptions", "UpdatedByActorId"),
        ("NotificationSubscriptionUserOverrides", "CreatedByActorId"),
    ];

    [Fact]
    public void The_scope_is_exactly_the_thirteen_approved_columns() =>
        HistoricalActorNormalization.Columns.Select(c => (c.Table, c.Column))
            .Should().Equal(ApprovedScope, "widening or narrowing the scope is a design-gate change, not an implementation detail");

    [Fact]
    public void The_audit_log_and_columns_that_already_hold_user_ids_are_never_in_scope()
    {
        var tables = HistoricalActorNormalization.Columns.Select(c => c.Table).ToList();
        tables.Should().NotContain("AuditLogs", "AUD-1.1 owns audit actors; AuditLogs.Actor is never rewritten");

        HistoricalActorNormalization.Columns.Select(c => c.ToString()).Should().NotContain(
        [
            "NotificationUserPreferences.UserId",
            "NotificationUserPreferences.UpdatedByActorId",
            "NotificationUserInboxItems.PrincipalUserId",
            "OperationalTeamMembers.PrincipalUserId",
            "ManagedContacts.LinkedUserId",
            "EntraAccessRequests.ProvisionedUserId",
            "RuntimeControlRequests.AppliedBy",
            "WorkflowExternalControlRequests.AppliedBy",
        ]);
    }

    [Fact]
    public void Every_column_in_scope_exists_in_the_model_with_that_table_and_column_name()
    {
        using var db = new AppertureDbContext(new DbContextOptionsBuilder<AppertureDbContext>()
            .UseSqlServer("Server=model-only;Database=model-only")
            .Options);

        foreach (var column in HistoricalActorNormalization.Columns)
        {
            var entity = db.Model.GetEntityTypes().SingleOrDefault(e => e.GetTableName() == column.Table);
            entity.Should().NotBeNull($"{column.Table} must be a mapped table");
            var store = StoreObjectIdentifier.Table(column.Table, entity!.GetSchema());
            entity.GetProperties().Should().Contain(
                p => p.GetColumnName(store) == column.Column && p.ClrType == typeof(string),
                $"{column} must be a mapped string column");
        }
    }

    [Fact]
    public void Literals_come_only_from_ActorLiterals()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Apperture.Core", "Apperture.Core.Application", "Services", "HistoricalActorClassifier.cs"));

        source.Should().Contain("ActorLiterals.IsNonHumanLiteral(value)");
        foreach (var literal in new[] { "\"system\"", "\"anonymous\"", "\"recovery\"", "\"emergency-cli\"", "\"system-worker\"", "\"system:\"" })
            source.Should().NotContain(literal, "the classifier keeps no second copy of the literal vocabulary");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull("repository root not found from test output directory");
        return dir!.FullName;
    }
}
