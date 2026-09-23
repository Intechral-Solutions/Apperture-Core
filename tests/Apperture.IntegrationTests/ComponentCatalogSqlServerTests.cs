using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// The Component catalog asks the Notification Definition store for "platformBuiltIn OR moduleBuiltIn" in
/// one query. EF Core InMemory does not prove SQL translation of the new <c>Origins</c> set filter, so this
/// runs it against real SQL Server. Fixtures deliberately contradict Scope so a Scope-based filter fails.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ComponentCatalogSqlServerTests
{
    [Fact]
    public async Task NotificationDefinitionStore_OriginsFilter_TranslatesToSql_AndSelectsByOriginNotScope()
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
        var platformId = $"cmp-platform-{suffix}";
        var moduleId = $"cmp-module-{suffix}";
        var moduleIntegrationScopedId = $"cmp-module-intscope-{suffix}";
        var userPlatformScopedId = $"cmp-user-platformscope-{suffix}";
        var ids = new[] { platformId, moduleId, moduleIntegrationScopedId, userPlatformScopedId };

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            if (!await db.Database.CanConnectAsync())
                throw new InvalidOperationException(SqlServerTestConfiguration.DescribeConnectionFailure());
            await db.Database.MigrateAsync();

            var now = DateTimeOffset.UtcNow;
            NotificationDefinitionEntity Row(string id, string origin, string scope, string? module) => new()
            {
                Id = id,
                DisplayName = id,
                SourceKind = NotificationSourceKinds.PlatformEvent,
                Origin = origin,
                Scope = scope,
                Mutability = NotificationDefinitionMutabilities.StaticRuleMutableSubscription,
                ModuleId = module,
                IsEnabled = true,
                RuleJson = "{}",
                TemplateJson = "{}",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            db.NotificationDefinitions.AddRange(
                Row(platformId, NotificationDefinitionOrigins.PlatformBuiltIn, NotificationDefinitionScopes.Platform, null),
                Row(moduleId, NotificationDefinitionOrigins.ModuleBuiltIn, NotificationDefinitionScopes.Module, $"Cmp.Module.{suffix}"),
                Row(moduleIntegrationScopedId, NotificationDefinitionOrigins.ModuleBuiltIn, NotificationDefinitionScopes.Integration, $"Cmp.Module.{suffix}"),
                Row(userPlatformScopedId, NotificationDefinitionOrigins.UserCreated, NotificationDefinitionScopes.Platform, null));
            await db.SaveChangesAsync();
        }

        try
        {
            await using var scope = provider.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<INotificationDefinitionStore>();

            var rows = await store.ListAsync(new NotificationDefinitionListQuery(
                Take: 200,
                Origins: [NotificationDefinitionOrigins.PlatformBuiltIn, NotificationDefinitionOrigins.ModuleBuiltIn]));

            var mine = rows.Where(r => ids.Contains(r.Id.Value)).Select(r => r.Id.Value).ToList();
            mine.Should().BeEquivalentTo([platformId, moduleId, moduleIntegrationScopedId]);
            rows.Should().OnlyContain(r =>
                r.Origin == NotificationDefinitionOrigins.PlatformBuiltIn
                || r.Origin == NotificationDefinitionOrigins.ModuleBuiltIn);
            rows.Select(r => r.Id.Value).Should().Contain(NotificationDefinitionIds.PlatformScheduleExpectedRunMissing);
        }
        finally
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
            db.NotificationDefinitions.RemoveRange(db.NotificationDefinitions.Where(d => ids.Contains(d.Id)));
            await db.SaveChangesAsync();
        }
    }
}
