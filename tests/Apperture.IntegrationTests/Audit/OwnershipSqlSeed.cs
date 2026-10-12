using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Persistence.SqlServer;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// Seeds the ownership families straight through the repositories and the table, so a case starts with no audit row (the audit
/// table is append-only). Each returns the new id.
/// </summary>
internal static class OwnershipSqlSeed
{
    internal static async Task<string> SeedContextAsync(AuditSqlScratch scratch, string name, string state = ResourceContextStates.Active)
    {
        var id = Guid.NewGuid().ToString("N")[..24];
        await scratch.ExecuteAsync(
            $"""
            INSERT INTO ResourceContexts (Id, Name, Slug, Kind, State, IsReserved, CreatedAtUtc, UpdatedAtUtc)
            VALUES (N'{id}', N'{name}', N'{name.ToLowerInvariant().Replace(' ', '-')}-{id[..6]}', N'{ResourceContextKinds.Application}', N'{state}', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);
        return id;
    }

    internal static async Task<string> SeedIntegrationAsync(AuditSqlScratch scratch, string contextId, IntegrationState state = IntegrationState.Active)
    {
        var id = "int-" + Guid.NewGuid().ToString("N")[..12];
        var now = DateTimeOffset.UtcNow;
        await using var db = scratch.NewDb();
        await new SqlIntegrationRepository(db).AddAsync(new DomainIntegration(
            new IntegrationId(id), "Moving integration", "Sample.Mod", null, state, null, now, now)
        { ResourceContextId = new ResourceContextId(contextId) });
        return id;
    }

    internal static async Task<string> SeedConnectionAsync(AuditSqlScratch scratch, string contextId)
    {
        var id = "con-" + Guid.NewGuid().ToString("N")[..12];
        await using var db = scratch.NewDb();
        await new SqlConnectionRepository(db).AddAsync(new Connection(
            new ConnectionId(id), new ConnectorDefinitionId("test.connector.move"), "Moving connection",
            new Dictionary<string, string>(), new Dictionary<string, string>(), null, null)
        { ResourceContextId = new ResourceContextId(contextId) });
        return id;
    }

    internal static async Task<string> SeedScheduleAsync(AuditSqlScratch scratch, string contextId, string? integration)
    {
        var id = "sch-" + Guid.NewGuid().ToString("N")[..12];
        await using var db = scratch.NewDb();
        await new SqlScheduleRepository(db).AddDefinitionAsync(new ScheduleDefinition(
            new ScheduleId(id), "Moving schedule", "0 0 * * *", "UTC", Enabled: false,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"), ExecutionPolicyRef: null, Version: 1,
            integration is null ? null : new IntegrationId(integration))
        { ResourceContextId = new ResourceContextId(contextId) });
        return id;
    }
}
