using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications;

/// <summary>
/// The Origins set filter lets the Component catalog ask for "platformBuiltIn OR moduleBuiltIn" in one
/// query. Every fixture deliberately contradicts Scope so a Scope-based implementation cannot pass.
/// </summary>
public sealed class SqlNotificationDefinitionStoreOriginsFilterTests
{
    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private static NotificationDefinitionEntity Row(string id, string origin, string scope, string? moduleId = null, string? integrationId = null) =>
        new()
        {
            Id = id,
            DisplayName = id,
            SourceKind = NotificationSourceKinds.PlatformEvent,
            Origin = origin,
            Scope = scope,
            Mutability = NotificationDefinitionMutabilities.StaticRuleMutableSubscription,
            ModuleId = moduleId,
            IntegrationId = integrationId,
            IsEnabled = true,
            RuleJson = "{}",
            TemplateJson = "{}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    private static async Task<SqlNotificationDefinitionStore> SeedAsync(AppertureDbContext db)
    {
        db.NotificationDefinitions.AddRange(
            Row("platform-1", NotificationDefinitionOrigins.PlatformBuiltIn, NotificationDefinitionScopes.Platform),
            Row("module-1", NotificationDefinitionOrigins.ModuleBuiltIn, NotificationDefinitionScopes.Module, moduleId: "M.One"),
            // Contradictory: a built-in carrying Integration scope is still a built-in.
            Row("module-integration-scope", NotificationDefinitionOrigins.ModuleBuiltIn, NotificationDefinitionScopes.Integration, moduleId: "M.Two"),
            // Contradictory: a user-created row carrying Platform scope is still user-created.
            Row("user-platform-scope", NotificationDefinitionOrigins.UserCreated, NotificationDefinitionScopes.Platform),
            Row("user-1", NotificationDefinitionOrigins.UserCreated, NotificationDefinitionScopes.Integration, integrationId: "int-1"));
        await db.SaveChangesAsync();
        return new SqlNotificationDefinitionStore(db);
    }

    [Fact]
    public async Task Origins_returns_rows_matching_any_listed_origin_regardless_of_scope()
    {
        await using var db = CreateDb();
        var sut = await SeedAsync(db);

        var rows = await sut.ListAsync(new NotificationDefinitionListQuery(
            Origins: [NotificationDefinitionOrigins.PlatformBuiltIn, NotificationDefinitionOrigins.ModuleBuiltIn]));

        rows.Select(r => r.Id.Value).Should().BeEquivalentTo(
            ["platform-1", "module-1", "module-integration-scope"]);
    }

    [Fact]
    public async Task Origins_null_leaves_existing_behaviour_unchanged()
    {
        await using var db = CreateDb();
        var sut = await SeedAsync(db);

        var rows = await sut.ListAsync(new NotificationDefinitionListQuery());

        rows.Should().HaveCount(5);
    }

    [Fact]
    public async Task Origins_composes_with_single_origin_equality()
    {
        await using var db = CreateDb();
        var sut = await SeedAsync(db);

        var rows = await sut.ListAsync(new NotificationDefinitionListQuery(
            Origin: NotificationDefinitionOrigins.ModuleBuiltIn,
            Origins: [NotificationDefinitionOrigins.PlatformBuiltIn, NotificationDefinitionOrigins.ModuleBuiltIn]));

        rows.Select(r => r.Id.Value).Should().BeEquivalentTo(["module-1", "module-integration-scope"]);
    }

    [Fact]
    public async Task Empty_origins_list_returns_no_rows()
    {
        await using var db = CreateDb();
        var sut = await SeedAsync(db);

        var rows = await sut.ListAsync(new NotificationDefinitionListQuery(Origins: []));

        rows.Should().BeEmpty();
    }
}
