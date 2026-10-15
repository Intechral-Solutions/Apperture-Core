using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Persistence.SqlServer;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// Fixtures for the SEC-2.3 setting secret ownership suites: the two settings services over a real catalog and the real SQL secret
/// store, seeding of every shape a setting's reference can have (its own, another holder's, legacy, dangling), and a comparable
/// snapshot of a secret so a test can prove that a victim's descriptor, versions, payload and evidence did not change.
/// </summary>
public static class SettingSecretOwnershipSqlSupport
{
    /// <summary>Material of a secret that belongs to someone else. It must never be unprotected, returned, sent or logged.</summary>
    internal const string ForeignMaterial = "foreign-setting-material-sentinel";

    internal const string OwnMaterial = "own-setting-material-sentinel";

    /// <summary>A sensitive integration setting (an ordinary text setting while <see cref="Registry.SecretKeyIsSensitive"/> is false).</summary>
    internal const string SecretKey = "Sec23.Api.Token";

    internal const string OtherSecretKey = "Sec23.Other.Token";

    internal static readonly string SmtpKey = PlatformSettingKeys.NotificationsEmailSmtpPasswordRef;

    internal static string NewIntegrationId() => "int-" + Guid.NewGuid().ToString("N")[..16];

    /// <summary>The ways a setting's stored reference can fail to name a secret that setting owns.</summary>
    public enum Foreign
    {
        /// <summary>Another integration's secret for the same key (the new owner name).</summary>
        OtherIntegration,

        /// <summary>The instance setting's secret for the same key.</summary>
        InstanceSetting,

        /// <summary>A connection's secret.</summary>
        Connection,

        /// <summary>An Entra registration's secret.</summary>
        Entra,

        /// <summary>The name of another key of the same integration.</summary>
        OtherKeySameIntegration,

        /// <summary>The owner's name under another provider.</summary>
        OtherProviderSameName,

        /// <summary>The owner's name differing only in case (the catalog's collation folds case).</summary>
        CaseVariant,

        /// <summary>A well-formed reference to no descriptor.</summary>
        Dangling,
    }

    internal static readonly Foreign[] ForeignWithMaterial =
    [
        Foreign.OtherIntegration, Foreign.InstanceSetting, Foreign.Connection, Foreign.Entra,
        Foreign.OtherKeySameIntegration, Foreign.OtherProviderSameName, Foreign.CaseVariant,
    ];

    internal static IEnumerable<object[]> IsolationByForeign(IEnumerable<Foreign> kinds) =>
        from rcsi in new[] { false, true }
        from kind in kinds
        select new object[] { rcsi, kind };

    internal static IEnumerable<object[]> Isolation() => [[false], [true]];

    /// <summary>
    /// Seeds what <paramref name="kind"/> needs in the real store and returns the reference a row would hold. The secret is foreign to
    /// the setting <paramref name="key"/> of <paramref name="integrationId"/>; seeding writes no audit row.
    /// </summary>
    internal static async Task<string> SeedForeignAsync(AuditSqlScratch scratch, Foreign kind, string integrationId, string key)
    {
        var owner = SettingSecretOwnership.ForIntegrationSetting(integrationId, key);
        switch (kind)
        {
            case Foreign.OtherIntegration:
                return await ConnectionSqlHarness.SeedSecretAsync(scratch, owner.Provider, SettingSecretOwnership.ForIntegrationSetting(NewIntegrationId(), key).Name, ForeignMaterial);
            case Foreign.InstanceSetting:
                return await ConnectionSqlHarness.SeedSecretAsync(scratch, SettingSecretOwnership.PlatformProvider, SettingSecretOwnership.ForPlatformSetting(key).Name, ForeignMaterial);
            case Foreign.Connection:
                return await ConnectionSqlHarness.SeedSecretAsync(scratch, "platform-connection", "connection:con-" + Guid.NewGuid().ToString("N")[..8] + ":Password", ForeignMaterial);
            case Foreign.Entra:
                return await ConnectionSqlHarness.SeedSecretAsync(scratch, "entra-registration", "entra-client-secret:reg-" + Guid.NewGuid().ToString("N")[..8], ForeignMaterial);
            case Foreign.OtherKeySameIntegration:
                return await ConnectionSqlHarness.SeedSecretAsync(scratch, owner.Provider, SettingSecretOwnership.ForIntegrationSetting(integrationId, OtherSecretKey).Name, ForeignMaterial);
            case Foreign.OtherProviderSameName:
                return await ConnectionSqlHarness.SeedSecretAsync(scratch, "platform-connection", owner.Name, ForeignMaterial);
            case Foreign.CaseVariant:
                return await ConnectionSqlHarness.SeedSecretAsync(scratch, owner.Provider, owner.Name.ToUpperInvariant(), ForeignMaterial);
            case Foreign.Dangling:
                return Guid.NewGuid().ToString("D");
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    /// <summary>The legacy shape: provider <c>platform-setting</c>, name <c>platform-setting:{key}</c>, as an integration row held before SEC-2.3.</summary>
    internal static Task<string> SeedLegacyAsync(AuditSqlScratch scratch, string key, string material = ForeignMaterial) =>
        ConnectionSqlHarness.SeedSecretAsync(scratch, SettingSecretOwnership.PlatformProvider, SettingSecretOwnership.LegacyIntegrationSetting(key).Name, material);

    internal static Task<string> SeedOwnedAsync(AuditSqlScratch scratch, string integrationId, string key, string material = OwnMaterial) =>
        ConnectionSqlHarness.SeedSecretAsync(scratch, SettingSecretOwnership.IntegrationProvider, SettingSecretOwnership.ForIntegrationSetting(integrationId, key).Name, material);

    internal static async Task AddIntegrationAsync(AuditSqlScratch scratch, string id) =>
        await scratch.ExecuteAsync(
            $"""
            INSERT INTO Integrations (Id, DisplayName, ModuleId, ScopeKey, ResourceContextId, State, CreatedAt, UpdatedAt)
            VALUES (N'{id}', N'Setting Secret Test Integration', N'Sample.Mod', N'instance', N'shared', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);

    /// <summary>Writes a setting row as a poisoned, legacy or historical row would exist (no API path writes these).</summary>
    internal static Task InsertRowAsync(AuditSqlScratch scratch, string scopeKind, string scopeId, string key, string valueJson) =>
        scratch.ExecuteAsync(
            $"""
            INSERT INTO PlatformSettingValues (Id, [Key], ScopeKind, ScopeId, ValueJson, CreatedUtc, UpdatedUtc)
            VALUES (N'{scopeKind}:{scopeId}:{key}', N'{key}', N'{scopeKind}', N'{scopeId}', N'{valueJson.Replace("'", "''")}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);

    internal static Task InsertIntegrationRowAsync(AuditSqlScratch scratch, string integrationId, string key, string reference) =>
        InsertRowAsync(scratch, PlatformSettingScopeConstants.IntegrationKind, integrationId, key, JsonSerializer.Serialize(reference));

    internal static Task InsertInstanceRowAsync(AuditSqlScratch scratch, string key, string reference) =>
        InsertRowAsync(scratch, PlatformSettingScopeConstants.InstanceKind, PlatformSettingScopeConstants.InstanceId, key, JsonSerializer.Serialize(reference));

    /// <summary>A connection whose reference map is <paramref name="secretRefsJson"/>, as another holder of a secret.</summary>
    internal static async Task InsertConnectionAsync(AuditSqlScratch scratch, string id, string secretRefsJson)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(scratch.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO [Connections] ([Id], [ConnectorDefinitionId], [Name], [ConfigJson], [SecretRefsJson], [ResourceContextId], [State], [LastVerificationStatus])
            VALUES (@id, N'platform.sql', N'Connection', N'{}', @refs, N'shared', 0, 0);
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@refs", secretRefsJson);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>An Entra instance registration whose client secret is <paramref name="clientSecretRef"/>, as another holder of a secret.</summary>
    internal static async Task InsertEntraAsync(AuditSqlScratch scratch, string id, string clientSecretRef)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(scratch.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO EntraInstanceRegistrations
                (Id, DisplayName, Authority, ClientId, UsesDeploymentSecret, ClientSecretRef, Enabled, IsActive, CreatedUtc, UpdatedUtc,
                 AllowMemberAccessRequests, AllowGuestAccessRequests)
            VALUES (@id, N'Registration', N'https://login.example.test/tenant', N'client', 0, @ref, 1, 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 0, 0);
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@ref", clientSecretRef);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>The reference an integration's row holds, or null when there is no row.</summary>
    internal static async Task<string?> IntegrationRefAsync(AuditSqlScratch scratch, string integrationId, string key) =>
        await RefAsync(scratch, $"{PlatformSettingScopeConstants.IntegrationKind}:{integrationId}:{key}");

    internal static async Task<string?> InstanceRefAsync(AuditSqlScratch scratch, string key) =>
        await RefAsync(scratch, $"{PlatformSettingScopeConstants.InstanceKind}:{PlatformSettingScopeConstants.InstanceId}:{key}");

    private static async Task<string?> RefAsync(AuditSqlScratch scratch, string rowId)
    {
        var rows = await scratch.RowsAsync($"SELECT ValueJson FROM PlatformSettingValues WHERE Id = N'{rowId.Replace("'", "''")}';");
        return rows.Count == 0 ? null : JsonSerializer.Deserialize<string>((string)rows[0]["ValueJson"]!);
    }

    internal static async Task<string?> RowVersionAsync(AuditSqlScratch scratch, string rowId)
    {
        var rows = await scratch.RowsAsync($"SELECT RowVersion FROM PlatformSettingValues WHERE Id = N'{rowId.Replace("'", "''")}';");
        return rows.Count == 0 ? null : Convert.ToBase64String((byte[])rows[0]["RowVersion"]!);
    }

    internal static Task<string?> IntegrationRowVersionAsync(AuditSqlScratch scratch, string integrationId, string key) =>
        RowVersionAsync(scratch, $"{PlatformSettingScopeConstants.IntegrationKind}:{integrationId}:{key}");

    internal static Task<string?> InstanceRowVersionAsync(AuditSqlScratch scratch, string key) =>
        RowVersionAsync(scratch, $"{PlatformSettingScopeConstants.InstanceKind}:{PlatformSettingScopeConstants.InstanceId}:{key}");

    internal static async Task<SecretState> StateAsync(AuditSqlScratch scratch, string secretRef) =>
        (SecretState)await scratch.ScalarAsync<int>($"SELECT State FROM SecretDescriptors WHERE Id = '{secretRef}';");

    internal static async Task<(string Provider, string Name)> DescriptorAsync(AuditSqlScratch scratch, string secretRef)
    {
        var row = (await scratch.RowsAsync($"SELECT Provider, Name FROM SecretDescriptors WHERE Id = '{secretRef}';")).Single();
        return ((string)row["Provider"]!, (string)row["Name"]!);
    }

    /// <summary>
    /// Everything the store holds about one secret, as one comparable text: its descriptor, every version with its protected
    /// payload, and its evidence rows. A victim's snapshot must be identical before and after a neighbour's operation.
    /// </summary>
    internal static async Task<string> SnapshotAsync(AuditSqlScratch scratch, string secretRef)
    {
        static string Text(IEnumerable<Dictionary<string, object?>> rows) =>
            string.Join("|", rows.Select(r => string.Join(",", r.Values.Select(v => v is byte[] b ? Convert.ToBase64String(b) : v?.ToString()))));

        if (!Guid.TryParse(secretRef, out var id))
            return "no-such-secret";
        return Text(await scratch.RowsAsync($"SELECT * FROM SecretDescriptors WHERE Id = '{id}';"))
            + "#" + Text(await scratch.RowsAsync($"SELECT * FROM SecretVersions WHERE SecretId = '{id}' ORDER BY Version;"))
            + "#" + Text(await scratch.RowsAsync($"SELECT EventType, Version, Actor, Outcome FROM SecretAudits WHERE SecretId = '{id}' ORDER BY Id;"));
    }

    internal static Task<List<Dictionary<string, object?>>> AuditRowsAsync(AuditSqlScratch scratch, string action, string? targetId = null) =>
        scratch.RowsAsync(
            $"SELECT * FROM AuditLogs WHERE Action = N'{action}'"
            + (targetId is null ? "" : $" AND TargetId = N'{targetId.Replace("'", "''")}'")
            + " ORDER BY Id;");

    // ---- the services over a real catalog ----

    /// <summary>Definitions of the test settings. <see cref="SecretKeyIsSensitive"/> flips the key between sensitive and ordinary, as a module's definition change would.</summary>
    internal sealed class Registry : IIntegrationSettingDefinitionRegistry
    {
        private static readonly PlatformSettingValueScopeKind[] IntegrationScope = [PlatformSettingValueScopeKind.Integration];

        public bool SecretKeyIsSensitive { get; set; } = true;

        public IReadOnlyList<PlatformSettingDefinition> GetAll() => [Definition(SecretKey), Definition(OtherSecretKey)];

        public PlatformSettingDefinition? TryGet(string key) =>
            key is SecretKey or OtherSecretKey ? Definition(key) : null;

        private PlatformSettingDefinition Definition(string key) => new()
        {
            Key = key,
            DisplayName = "Token (test)",
            Description = "Integration-scoped credential setting.",
            Category = PlatformSettingCategory.PlatformEvents,
            ValueType = PlatformSettingValueType.String,
            InputKind = SecretKeyIsSensitive ? PlatformSettingInputKind.Secret : PlatformSettingInputKind.Text,
            IsSensitive = SecretKeyIsSensitive,
            AllowedValueScopes = IntegrationScope,
            ConfigurationPath = "Test:Sec23:" + key,
            DefaultValue = string.Empty,
            AuditValue = SecretKeyIsSensitive ? PlatformSettingAuditValuePolicy.Omit : PlatformSettingAuditValuePolicy.Record,
        };
    }

    internal static IntegrationSettingsService IntegrationService(
        AppertureDbContext db, IPrincipalContext principal, Registry registry, IAuditedUnitOfWork? unit = null, bool withLegacyDisposition = true)
    {
        var repository = new SqlPlatformSettingValueRepository(db);
        var store = ConnectionSqlHarness.SecretStore(db);
        return new IntegrationSettingsService(
            registry,
            repository,
            new IntegrationSettingsResolver(registry, repository),
            Coordinator(db, principal),
            new PlatformSettingSecretMaterialProvisioner(store),
            new SqlIntegrationRepository(db),
            unit ?? NewUnit(db, principal),
            withLegacyDisposition ? new IntegrationSettingSecretClassifier(new IntegrationSettingSecretInventory(db), store, registry) : null);
    }

    internal static PlatformSettingsService PlatformService(AppertureDbContext db, IPrincipalContext principal, IAuditedUnitOfWork? unit = null)
    {
        var registry = new PlatformSettingDefinitionRegistry();
        var repository = new SqlPlatformSettingValueRepository(db);
        return new PlatformSettingsService(
            registry,
            repository,
            new PlatformSettingsResolver(registry, repository, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()),
            Coordinator(db, principal),
            new PlatformSettingSecretMaterialProvisioner(ConnectionSqlHarness.SecretStore(db)),
            unit ?? NewUnit(db, principal));
    }

    private static PlatformSettingOptionProviderCoordinator Coordinator(AppertureDbContext db, IPrincipalContext principal)
    {
        var integrations = new SqlIntegrationRepository(db);
        return new PlatformSettingOptionProviderCoordinator(
            new PlatformSettingOptionProviderRegistry([]),
            new PlatformSettingOptionProviderAuthorization(principal, new ConnectionSqlHarness.AllowAll(), integrations),
            new IntegrationSettingOptionProviderAuthorization(principal, new ConnectionSqlHarness.AllowAll(), integrations));
    }

    internal static PlatformSettingUpdatePayload Replace(string value) =>
        new() { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = value };

    internal static PlatformSettingUpdatePayload Clear() =>
        new() { SecretOperation = PlatformSettingSecretOperations.Clear };

    // ---- operations, each on a context of its own as a request would ----

    internal static async Task ReplaceIntegrationAsync(AuditSqlScratch scratch, IPrincipalContext principal, Registry registry, string integrationId, string value, string key = SecretKey)
    {
        await using var db = scratch.NewDb();
        await IntegrationService(db, principal, registry).SetAsync(
            integrationId, key, Replace(value), await IntegrationRowVersionAsync(scratch, integrationId, key));
    }

    internal static async Task ClearIntegrationAsync(AuditSqlScratch scratch, IPrincipalContext principal, Registry registry, string integrationId, string key = SecretKey)
    {
        await using var db = scratch.NewDb();
        await IntegrationService(db, principal, registry).SetAsync(
            integrationId, key, Clear(), await IntegrationRowVersionAsync(scratch, integrationId, key));
    }

    internal static async Task ResetIntegrationAsync(AuditSqlScratch scratch, IPrincipalContext principal, Registry registry, string integrationId, string key = SecretKey)
    {
        await using var db = scratch.NewDb();
        await IntegrationService(db, principal, registry).ResetAsync(integrationId, key);
    }

    internal static async Task ReplaceInstanceAsync(AuditSqlScratch scratch, IPrincipalContext principal, string value)
    {
        await using var db = scratch.NewDb();
        await PlatformService(db, principal).SetAsync(SmtpKey, Replace(value), await InstanceRowVersionAsync(scratch, SmtpKey));
    }

    internal static async Task ClearInstanceAsync(AuditSqlScratch scratch, IPrincipalContext principal)
    {
        await using var db = scratch.NewDb();
        await PlatformService(db, principal).SetAsync(SmtpKey, Clear(), await InstanceRowVersionAsync(scratch, SmtpKey));
    }

    internal static async Task ResetInstanceAsync(AuditSqlScratch scratch, IPrincipalContext principal)
    {
        await using var db = scratch.NewDb();
        await PlatformService(db, principal).ResetAsync(SmtpKey);
    }
}
