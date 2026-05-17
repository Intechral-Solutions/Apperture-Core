using Apperture.Core.Application.Notifications;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Notifications;

public sealed class SqlNotificationDefinitionStoreTests
{
    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    [Fact]
    public async Task Create_list_get_update_enabled_and_builtin_mutability()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationDefinitionStore(db);

        var builtin = ExpectedRunMissingNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        db.NotificationDefinitions.Add(new NotificationDefinitionEntity
        {
            Id = builtin.Id.Value,
            DisplayName = builtin.DisplayName,
            Description = builtin.Description,
            SourceKind = builtin.SourceKind,
            Origin = builtin.Origin,
            Scope = builtin.Scope,
            Mutability = builtin.Mutability,
            PrimaryEventType = builtin.PrimaryEventType,
            IsEnabled = builtin.IsEnabled,
            DefaultSeverity = builtin.DefaultSeverity,
            RuleJson = builtin.RuleJson,
            TemplateJson = builtin.TemplateJson,
            CreatedAtUtc = builtin.CreatedAtUtc,
            UpdatedAtUtc = builtin.UpdatedAtUtc,
        });
        await db.SaveChangesAsync();

        var created = await sut.CreateUserDefinitionAsync(
            new NotificationDefinitionCreateCommand(
                "My alert",
                null,
                "int-1",
                NotificationValidationTestHelpers.ValidUserRuleJson,
                NotificationValidationTestHelpers.ValidUserTemplateJson,
                true,
                null,
                null));

        var list = await sut.ListAsync(new NotificationDefinitionListQuery(IntegrationId: "int-1"));
        list.Should().HaveCount(1);

        var fetched = await sut.GetAsync(created.Id);
        fetched.Should().NotBeNull();

        var act = async () => await sut.UpdateAsync(
            builtin.Id,
            new NotificationDefinitionUpdateCommand(
                DisplayName: null,
                Description: null,
                RuleJson: "{}",
                TemplateJson: null,
                IsEnabled: null,
                DefaultSeverity: null,
                UpdatedByActorId: null));
        await act.Should().ThrowAsync<InvalidOperationException>();

        var disabled = await sut.SetEnabledAsync(builtin.Id, false, null);
        disabled!.IsEnabled.Should().BeFalse();
    }
}
