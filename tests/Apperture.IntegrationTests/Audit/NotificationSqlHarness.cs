using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Notifications;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The notification definition, subscription and managed contact services over a real catalog (AUD-1.4 slice 6): the production
/// services wired over the real stores, repositories and integration repository, with an authorization that allows everything
/// (authorization has its own suites) and seeding that writes no audit row (the audit table is append-only). Shared by the
/// notification SQL suites.
/// </summary>
internal static class NotificationSqlHarness
{
    /// <summary>Free text of a rule or template that must never reach an audit row.</summary>
    internal const string TemplateSentinel = "SENTINEL-TEMPLATE-SUBJECT-do-not-record";

    internal const string AddressSentinel = "sentinel.person@example.com";
    internal const string PersonSentinel = "Sentinel Person";

    internal const string RuleJson = """{"schemaVersion":"apperture.notification.rule/v1","trigger":{"eventTypes":["platform.schedule.expected_run_missing"]}}""";
    internal const string TemplateJson = """{"schemaVersion":"apperture.notification.template/v1","subject":"SENTINEL-TEMPLATE-SUBJECT-do-not-record"}""";

    /// <summary>Allows everything, counts the integration-update checks, and can be told to deny them after the first.</summary>
    internal sealed class Allow : IAuthorizationService, IAuthorizationEvaluationService
    {
        public int IntegrationUpdateChecks { get; private set; }

        public bool DenyIntegrationUpdateAfterTheFirst { get; set; }

        /// <summary>Called with the integration id at every integration-update check, so a test can observe the state the check runs against.</summary>
        public Action<string>? OnIntegrationUpdateCheck { get; set; }

        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            if (string.Equals(context.Permission.Action, ActionConstants.IntegrationUpdate, StringComparison.Ordinal))
            {
                IntegrationUpdateChecks++;
                if (context.ResourceIdentity is not null)
                    OnIntegrationUpdateCheck?.Invoke(context.ResourceIdentity.ResourceId);
                if (DenyIntegrationUpdateAfterTheFirst && IntegrationUpdateChecks > 1)
                    return ValueTask.FromResult(false);
            }

            return ValueTask.FromResult(true);
        }
    }

    private sealed class NoOpRuleValidator : INotificationRuleValidator
    {
        public NotificationValidationResult Validate(string ruleJson, NotificationRuleValidationContext context) =>
            NotificationValidationResult.Success();
    }

    private sealed class NoOpTemplateValidator : INotificationTemplateValidator
    {
        public NotificationValidationResult Validate(string templateJson, NotificationTemplateValidationContext context) =>
            NotificationValidationResult.Success();
    }

    private static NotificationDefinitionAuthorization Authorization(AppertureDbContext db, IPrincipalContext principal, Allow allow) =>
        new(principal, allow, allow, new SqlIntegrationRepository(db));

    internal static NotificationDefinitionService Definitions(
        AppertureDbContext db, IPrincipalContext principal, IAuditedUnitOfWork unit, Allow? allow = null)
    {
        allow ??= new Allow();
        return new NotificationDefinitionService(
            new SqlNotificationDefinitionStore(db),
            new NoOpRuleValidator(),
            new NoOpTemplateValidator(),
            null!,
            null!,
            Authorization(db, principal, allow),
            principal,
            new SqlIntegrationRepository(db),
            unit);
    }

    internal static NotificationSubscriptionService Subscriptions(
        AppertureDbContext db, IPrincipalContext principal, IAuditedUnitOfWork unit, Allow? allow = null)
    {
        allow ??= new Allow();
        return new NotificationSubscriptionService(
            new SqlNotificationSubscriptionStore(db),
            new SqlNotificationDefinitionStore(db),
            null!,
            new ManagedContactRecipientProvisioner(
                new NotificationRecipientAddressParser(), new SqlManagedContactRepository(db), principal, allow, TimeProvider.System),
            Authorization(db, principal, allow),
            principal,
            new SqlIntegrationRepository(db),
            unit);
    }

    internal static ManagedContactManagementService Contacts(
        AppertureDbContext db, IPrincipalContext principal, IAuditedUnitOfWork unit, IEntraDirectoryDiscoveryService? directory = null, TimeProvider? time = null) =>
        new(
            principal,
            new Allow(),
            new SqlManagedContactRepository(db),
            new SqlManagedContactGroupRepository(db),
            new SqlEntraInstanceRegistrationRepository(db),
            directory ?? new NoDirectory(),
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new ConfigurationBuilder().AddInMemoryCollection().Build(),
            time ?? TimeProvider.System,
            secretResolver: null,
            auditedUnitOfWork: unit);

    /// <summary>A directory that knows the users and groups it was given, by object id.</summary>
    internal sealed class FakeDirectory : IEntraDirectoryDiscoveryService
    {
        public Dictionary<string, EntraDirectoryUser> Users { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, EntraDirectoryGroup> Groups { get; } = new(StringComparer.Ordinal);

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(
            EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>> SearchGroupsAsync(
            EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DirectoryDiscoveryResult<EntraDirectoryUser?>> GetUserByIdAsync(
            EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<EntraDirectoryUser?>(
                true, DiscoveryFailureCategory.None, "ok", null, Users.GetValueOrDefault(objectId)));

        public Task<DirectoryDiscoveryResult<EntraDirectoryGroup?>> GetGroupByIdAsync(
            EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryDiscoveryResult<EntraDirectoryGroup?>(
                true, DiscoveryFailureCategory.None, "ok", null, Groups.GetValueOrDefault(objectId)));
    }

    private sealed class NoDirectory : IEntraDirectoryDiscoveryService
    {
        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(
            EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>> SearchGroupsAsync(
            EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DirectoryDiscoveryResult<EntraDirectoryUser?>> GetUserByIdAsync(
            EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DirectoryDiscoveryResult<EntraDirectoryGroup?>> GetGroupByIdAsync(
            EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    // ---- seeding: straight through the stores and tables, so a case starts with no audit row ----

    internal const string RegistrationId = "entra_reg_notif";

    internal static async Task<string> SeedRegistrationAsync(AuditSqlScratch scratch)
    {
        await using var db = scratch.NewDb();
        var repository = new SqlEntraInstanceRegistrationRepository(db);
        if (await repository.GetByIdAsync(RegistrationId) is null)
        {
            await repository.AddAsync(new EntraInstanceRegistration
            {
                Id = RegistrationId,
                ClientId = "app",
                AzureTenantId = "tenant-1",
                CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow,
            });
        }

        return RegistrationId;
    }

    /// <summary>An active integration in the Shared context. Returns its id.</summary>
    internal static Task<string> SeedIntegrationAsync(AuditSqlScratch scratch) =>
        OwnershipSqlSeed.SeedIntegrationAsync(scratch, ResourceContextIds.SharedValue);

    /// <summary>A user-created, enabled, integration-scoped definition, created through the store. Returns its id.</summary>
    internal static async Task<string> SeedDefinitionAsync(AuditSqlScratch scratch, string integrationId, string? name = null)
    {
        await using var db = scratch.NewDb();
        var created = await new SqlNotificationDefinitionStore(db).CreateUserDefinitionAsync(
            new NotificationDefinitionCreateCommand(
                name ?? "Seeded definition " + Guid.NewGuid().ToString("N")[..8],
                "Seeded",
                integrationId,
                RuleJson,
                TemplateJson,
                IsEnabled: true,
                DefaultSeverity: "warning",
                CreatedByActorId: "seed"));
        return created.Id.Value;
    }

    internal static async Task<string> SeedSubscriptionAsync(AuditSqlScratch scratch, string definitionId, string? integrationId, string userRef = "user-seed")
    {
        await using var db = scratch.NewDb();
        var created = await new SqlNotificationSubscriptionStore(db).AddAsync(
            new NotificationSubscriptionCreateCommand(
                definitionId, integrationId, NotificationRecipientKinds.Principal, userRef,
                NotificationDeliveryModes.Immediate, NotificationSubscriptionOrigins.AdminAssigned, IsEnabled: true, "seed"));
        return created.Id.Value;
    }

    internal static async Task<string> SeedOptOutAsync(AuditSqlScratch scratch, string definitionId, string userId, string? teamId = null)
    {
        await using var db = scratch.NewDb();
        var created = await new SqlNotificationSubscriptionStore(db).SetUserOptOutAsync(
            new NotificationSubscriptionUserOverrideCommand(userId, definitionId, teamId, "seed"));
        return created.Id;
    }

    internal static Task SeedPreferenceAsync(AuditSqlScratch scratch, string definitionId, string userId) =>
        scratch.ExecuteAsync(
            $"""
            INSERT INTO NotificationUserPreferences (Id, UserId, NotificationDefinitionId, Channel, IsEnabled, CreatedAtUtc, UpdatedAtUtc, UpdatedByActorId)
            VALUES (N'nupref_{Guid.NewGuid():N}', N'{userId}', N'{definitionId}', N'toast', 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'seed');
            """);

    internal static async Task<string> SeedContactAsync(AuditSqlScratch scratch, string name, string? email, bool enabled = true)
    {
        await using var db = scratch.NewDb();
        return await new SqlManagedContactRepository(db).CreateCustomAsync(name, email, enabled, DateTimeOffset.UtcNow);
    }

    // ---- observation ----

    /// <summary>Every row a notification writer can change, as one comparable text.</summary>
    internal static async Task<string> SnapshotAsync(AuditSqlScratch scratch)
    {
        static string Text(IEnumerable<Dictionary<string, object?>> rows) =>
            string.Join("|", rows.Select(r => string.Join(",", r.Values.Select(v => v is byte[] b ? Convert.ToBase64String(b) : v?.ToString()))));

        var parts = new List<string>();
        foreach (var table in new[]
                 {
                     "NotificationDefinitions", "NotificationSubscriptions", "NotificationSubscriptionUserOverrides",
                     "NotificationUserPreferences", "ManagedContacts", "ManagedContactIdentityLinks", "ManagedContactGroups",
                 })
        {
            parts.Add(Text(await scratch.RowsAsync($"SELECT * FROM [{table}] ORDER BY [Id];")));
        }

        return string.Join("#", parts);
    }

    internal static Task<List<Dictionary<string, object?>>> AuditRowsAsync(AuditSqlScratch scratch, string? targetId = null) =>
        scratch.RowsAsync(targetId is null
            ? "SELECT * FROM AuditLogs ORDER BY Id;"
            : $"SELECT * FROM AuditLogs WHERE TargetId = N'{targetId}' ORDER BY Id;");

    internal static Task<(string Admin, IPrincipalContext Principal)> AdminAsync(AuditSqlScratch scratch) =>
        ConnectionSqlHarness.AdminAsync(scratch);

    internal static string Detail(Dictionary<string, object?> row, string name) =>
        ConnectionSqlHarness.Detail(row, name) ?? string.Empty;

    internal static string AuditText(IEnumerable<Dictionary<string, object?>> rows) =>
        JsonSerializer.Serialize(rows.Select(r => r.Values.Select(v => v?.ToString())));
}
