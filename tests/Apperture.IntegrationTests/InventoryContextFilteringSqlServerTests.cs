using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// CTX-3A Plan 4, Task 9: EF Core InMemory (used by <c>InventoryContextClassificationTests</c>) does not
/// prove SQL translation. These tests exercise <see cref="SqlAuditService"/>'s new context-classification
/// filter and <see cref="SqlNotificationDefinitionStore"/>'s new owner filter against real SQL Server --
/// in particular the <c>AnyActionPrefix</c> expression-tree predicate, which a plain
/// <c>IEnumerable.Any(p =&gt; x.StartsWith(p))</c> closure cannot translate.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class InventoryContextFilteringSqlServerTests
{
    [Fact]
    public async Task AuditContextClassificationFilter_System_TranslatesToSql_AndMatchesOnlyNullContextSystemPrefixed()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());

        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        services.AddScoped<IAuditService, SqlAuditService>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            if (!await db.Database.CanConnectAsync())
                throw new InvalidOperationException(SqlServerTestConfiguration.DescribeConnectionFailure());
            await db.Database.MigrateAsync();
        }

        // The unique test tag must NOT be prepended before the meaningful action prefix -- doing so
        // would defeat the very System-prefix matching under test (a prefixed random guid no longer
        // starts with "auth."). It is embedded as a marker segment after the real prefix instead, and
        // FilterByActionPrefix is deliberately not used here (that filter is orthogonal to this test);
        // isolation from any concurrent/unrelated audit noise comes from asserting on rows containing
        // the marker rather than on the whole page.
        var marker = Guid.NewGuid().ToString("N");
        var systemNoContext = $"auth.login.ctx3a-t9-{marker}-a";
        var nonSystemNoContext = $"integration.settings.updated.ctx3a-t9-{marker}-b";
        var systemShapedWithContext = $"auth.login.ctx3a-t9-{marker}-c";
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            // System-prefixed, null context: matches.
            await audit.AppendAsync(CreateRecord(baseTime, systemNoContext, resourceContextId: null));
            // Non-system-prefixed, null context: does not match System.
            await audit.AppendAsync(CreateRecord(baseTime.AddSeconds(1), nonSystemNoContext, resourceContextId: null));
            // System-shaped action, but has a context id: id wins, so this must NOT match System either.
            await audit.AppendAsync(CreateRecord(baseTime.AddSeconds(2), systemShapedWithContext, resourceContextId: "some-context"));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            var page = await audit.QueryAsync(new AuditQuery(
                FromUtc: baseTime,
                FilterByContextClassification: AuditContextClassificationFilter.System,
                PageSize: 100));

            var matched = page.Items.Where(x => x.Action.Contains(marker)).ToList();
            matched.Should().ContainSingle();
            matched[0].Action.Should().Be(systemNoContext);
            matched[0].ResourceContextId.Should().BeNull();
        }
    }

    [Fact]
    public async Task AuditContextClassificationFilter_NotRecorded_TranslatesToSql_AndExcludesSystemPrefixed()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());

        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        services.AddScoped<IAuditService, SqlAuditService>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            if (!await db.Database.CanConnectAsync())
                throw new InvalidOperationException(SqlServerTestConfiguration.DescribeConnectionFailure());
            await db.Database.MigrateAsync();
        }

        var marker = Guid.NewGuid().ToString("N");
        var nonSystemNoContext = $"execution.completed.ctx3a-t9-{marker}-a";
        var systemNoContext = $"auth.login.ctx3a-t9-{marker}-b";
        var nonSystemWithContext = $"execution.completed.ctx3a-t9-{marker}-c";
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.AppendAsync(CreateRecord(baseTime, nonSystemNoContext, resourceContextId: null));
            await audit.AppendAsync(CreateRecord(baseTime.AddSeconds(1), systemNoContext, resourceContextId: null));
            await audit.AppendAsync(CreateRecord(baseTime.AddSeconds(2), nonSystemWithContext, resourceContextId: "some-context"));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            var page = await audit.QueryAsync(new AuditQuery(
                FromUtc: baseTime,
                FilterByContextClassification: AuditContextClassificationFilter.NotRecorded,
                PageSize: 100));

            var matched = page.Items.Where(x => x.Action.Contains(marker)).ToList();
            matched.Should().ContainSingle();
            matched[0].Action.Should().Be(nonSystemNoContext);
            matched[0].ResourceContextId.Should().BeNull();
        }
    }

    [Fact]
    public async Task NotificationDefinitionStore_OwnerResourceContextIdFilter_TranslatesToSql()
    {
        var connectionString = SqlServerTestConfiguration.GetConnectionString();
        if (connectionString is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());

        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        var services = new ServiceCollection();
        services.AddApperturePersistence(connectionString);
        services.AddScoped<INotificationDefinitionStore, SqlNotificationDefinitionStore>();
        await using var provider = services.BuildServiceProvider();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var contextA = $"ctx3a-t9-ctxA-{suffix}";
        var contextB = $"ctx3a-t9-ctxB-{suffix}";
        var integrationA = $"ctx3a-t9-intA-{suffix}";
        var integrationB = $"ctx3a-t9-intB-{suffix}";

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            if (!await db.Database.CanConnectAsync())
                throw new InvalidOperationException(SqlServerTestConfiguration.DescribeConnectionFailure());
            await db.Database.MigrateAsync();

            db.ResourceContexts.Add(new ResourceContextEntity
            {
                Id = contextA,
                Name = "Ctx3a T9 Context A " + suffix,
                Slug = "ctx3a-t9-ctxa-" + suffix,
                Kind = "Application",
                State = "Active",
                IsReserved = false,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            db.ResourceContexts.Add(new ResourceContextEntity
            {
                Id = contextB,
                Name = "Ctx3a T9 Context B " + suffix,
                Slug = "ctx3a-t9-ctxb-" + suffix,
                Kind = "Application",
                State = "Active",
                IsReserved = false,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            db.Integrations.Add(new IntegrationEntity
            {
                Id = integrationA,
                DisplayName = "Integration A " + suffix,
                ModuleId = "Sample.Mod",
                ResourceContextId = contextA,
                State = 0,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            db.Integrations.Add(new IntegrationEntity
            {
                Id = integrationB,
                DisplayName = "Integration B " + suffix,
                ModuleId = "Sample.Mod",
                ResourceContextId = contextB,
                State = 0,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            var now = DateTimeOffset.UtcNow;
            db.NotificationDefinitions.Add(new NotificationDefinitionEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayName = "Def owned by A " + suffix,
                SourceKind = NotificationSourceKinds.PlatformEvent,
                Origin = NotificationDefinitionOrigins.UserCreated,
                Scope = NotificationDefinitionScopes.Integration,
                Mutability = NotificationDefinitionMutabilities.FullyEditable,
                IntegrationId = integrationA,
                IsEnabled = true,
                RuleJson = "{}",
                TemplateJson = "{}",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            db.NotificationDefinitions.Add(new NotificationDefinitionEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayName = "Def owned by B " + suffix,
                SourceKind = NotificationSourceKinds.PlatformEvent,
                Origin = NotificationDefinitionOrigins.UserCreated,
                Scope = NotificationDefinitionScopes.Integration,
                Mutability = NotificationDefinitionMutabilities.FullyEditable,
                IntegrationId = integrationB,
                IsEnabled = true,
                RuleJson = "{}",
                TemplateJson = "{}",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            db.NotificationDefinitions.Add(new NotificationDefinitionEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayName = "Platform def " + suffix,
                SourceKind = NotificationSourceKinds.PlatformEvent,
                Origin = NotificationDefinitionOrigins.UserCreated,
                Scope = NotificationDefinitionScopes.Platform,
                Mutability = NotificationDefinitionMutabilities.ReadOnly,
                IsEnabled = true,
                RuleJson = "{}",
                TemplateJson = "{}",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            await db.SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<INotificationDefinitionStore>();

            var byOwner = await store.ListAsync(new NotificationDefinitionListQuery(
                OwnerResourceContextId: contextA,
                Take: 200));
            byOwner.Should().ContainSingle(d => d.DisplayName == "Def owned by A " + suffix);
            byOwner.Should().NotContain(d => d.DisplayName == "Def owned by B " + suffix);
            byOwner.Should().NotContain(d => d.DisplayName == "Platform def " + suffix);

            var systemOnly = await store.ListAsync(new NotificationDefinitionListQuery(
                SystemScopedOnly: true,
                Take: 200));
            systemOnly.Should().Contain(d => d.DisplayName == "Platform def " + suffix);
            systemOnly.Should().NotContain(d => d.DisplayName == "Def owned by A " + suffix);

            var owners = await store.GetIntegrationOwnerContextIdsAsync([integrationA, integrationB, "nonexistent-integration"]);
            owners.Should().ContainKey(integrationA).WhoseValue.Should().Be(contextA);
            owners.Should().ContainKey(integrationB).WhoseValue.Should().Be(contextB);
            owners.Should().NotContainKey("nonexistent-integration");
        }
    }

    private static AuditRecord CreateRecord(DateTimeOffset at, string action, string? resourceContextId) =>
        new(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId("system"),
            action,
            Resource: null,
            Outcome: "",
            at,
            new CorrelationId(null),
            Details: null,
            ResourceContextId: resourceContextId);
}
