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

    [Fact]
    public async Task List_for_integration_includes_module_scoped_definitions_for_matching_module()
    {
        await using var db = CreateDb();
        var sut = new SqlNotificationDefinitionStore(db);

        var moduleBuiltIn = HelloWorldDemoNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        var otherModuleBuiltIn = moduleBuiltIn with
        {
            Id = NotificationDefinitionId.From("module.other.demo.completed"),
            ModuleId = "Sample.OtherModule",
            DisplayName = "Other module demo completed"
        };

        db.NotificationDefinitions.AddRange(
            new NotificationDefinitionEntity
            {
                Id = moduleBuiltIn.Id.Value,
                DisplayName = moduleBuiltIn.DisplayName,
                Description = moduleBuiltIn.Description,
                SourceKind = moduleBuiltIn.SourceKind,
                Origin = moduleBuiltIn.Origin,
                Scope = moduleBuiltIn.Scope,
                Mutability = moduleBuiltIn.Mutability,
                ModuleId = moduleBuiltIn.ModuleId,
                IntegrationId = moduleBuiltIn.IntegrationId,
                PrimaryEventType = moduleBuiltIn.PrimaryEventType,
                IsEnabled = moduleBuiltIn.IsEnabled,
                DefaultSeverity = moduleBuiltIn.DefaultSeverity,
                RuleJson = moduleBuiltIn.RuleJson,
                TemplateJson = moduleBuiltIn.TemplateJson,
                CreatedAtUtc = moduleBuiltIn.CreatedAtUtc,
                UpdatedAtUtc = moduleBuiltIn.UpdatedAtUtc,
            },
            new NotificationDefinitionEntity
            {
                Id = otherModuleBuiltIn.Id.Value,
                DisplayName = otherModuleBuiltIn.DisplayName,
                Description = otherModuleBuiltIn.Description,
                SourceKind = otherModuleBuiltIn.SourceKind,
                Origin = otherModuleBuiltIn.Origin,
                Scope = otherModuleBuiltIn.Scope,
                Mutability = otherModuleBuiltIn.Mutability,
                ModuleId = otherModuleBuiltIn.ModuleId,
                IntegrationId = otherModuleBuiltIn.IntegrationId,
                PrimaryEventType = otherModuleBuiltIn.PrimaryEventType,
                IsEnabled = otherModuleBuiltIn.IsEnabled,
                DefaultSeverity = otherModuleBuiltIn.DefaultSeverity,
                RuleJson = otherModuleBuiltIn.RuleJson,
                TemplateJson = otherModuleBuiltIn.TemplateJson,
                CreatedAtUtc = otherModuleBuiltIn.CreatedAtUtc,
                UpdatedAtUtc = otherModuleBuiltIn.UpdatedAtUtc,
            });
        await db.SaveChangesAsync();

        var rows = await sut.ListAsync(
            new NotificationDefinitionListQuery(
                IntegrationId: "int-1",
                ApplicableModuleId: "Sample.HelloWorld"));

        rows.Should().ContainSingle(x => x.Id == moduleBuiltIn.Id);
        rows.Should().NotContain(x => x.Id == otherModuleBuiltIn.Id);
    }

    [Fact]
    public async Task Create_persists_toast_enabled_by_default_and_read_back_reflects_it()
    {
        // Toast-B P1 Task 3: CreateUserDefinitionAsync previously ignored the command's
        // ToastEnabledByDefault value entirely -- this pins that it now actually lands in storage and
        // survives a fresh (untracked) read.
        await using var db = CreateDb();
        var sut = new SqlNotificationDefinitionStore(db);

        var created = await sut.CreateUserDefinitionAsync(
            new NotificationDefinitionCreateCommand(
                "Toast default on",
                null,
                "int-1",
                NotificationValidationTestHelpers.ValidUserRuleJson,
                NotificationValidationTestHelpers.ValidUserTemplateJson,
                true,
                null,
                null,
                ToastEnabledByDefault: true));

        created.ToastEnabledByDefault.Should().BeTrue();

        var fetched = await sut.GetAsync(created.Id);
        fetched.Should().NotBeNull();
        fetched!.ToastEnabledByDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Update_with_null_toast_enabled_by_default_leaves_it_unchanged_and_non_null_overwrites_it_alone()
    {
        // Toast-B P1 Task 3: UpdateAsync previously never mutated ToastEnabledByDefault at all, so a
        // caller could never flip it after creation. This pins "null = no change" (matching IsEnabled /
        // DefaultSeverity) and that setting it does not disturb any other field.
        await using var db = CreateDb();
        var sut = new SqlNotificationDefinitionStore(db);

        var created = await sut.CreateUserDefinitionAsync(
            new NotificationDefinitionCreateCommand(
                "Toast default off",
                "Original description",
                "int-1",
                NotificationValidationTestHelpers.ValidUserRuleJson,
                NotificationValidationTestHelpers.ValidUserTemplateJson,
                true,
                "warning",
                null,
                ToastEnabledByDefault: false));

        var untouched = await sut.UpdateAsync(
            created.Id,
            new NotificationDefinitionUpdateCommand(
                DisplayName: null,
                Description: null,
                RuleJson: null,
                TemplateJson: null,
                IsEnabled: null,
                DefaultSeverity: null,
                UpdatedByActorId: null,
                ToastEnabledByDefault: null));
        untouched!.ToastEnabledByDefault.Should().BeFalse();

        var updated = await sut.UpdateAsync(
            created.Id,
            new NotificationDefinitionUpdateCommand(
                DisplayName: null,
                Description: null,
                RuleJson: null,
                TemplateJson: null,
                IsEnabled: null,
                DefaultSeverity: null,
                UpdatedByActorId: null,
                ToastEnabledByDefault: true));

        updated!.ToastEnabledByDefault.Should().BeTrue();
        updated.DisplayName.Should().Be("Toast default off");
        updated.Description.Should().Be("Original description");
        updated.IsEnabled.Should().BeTrue();
        updated.DefaultSeverity.Should().Be("warning");

        var fetched = await sut.GetAsync(created.Id);
        fetched!.ToastEnabledByDefault.Should().BeTrue();
    }
}
