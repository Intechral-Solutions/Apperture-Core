using System.Reflection;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// SEC-2.4: the Entra registration controller over a real catalog, with the two outbound probes replaced by scripted ones, and the
/// raw SQL a test needs to arrange and read registrations without going through the code under test.
/// </summary>
internal static class EntraRegistrationSqlHarness
{
    public const string Authority = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0";
    public const string ClientId = "22222222-2222-2222-2222-222222222222";
    public const string TenantId = "11111111-1111-1111-1111-111111111111";

    /// <summary>A search value that must never be stored as probe telemetry or reach an audit row.</summary>
    public const string SearchSentinel = "search-sentinel-do-not-record";

    public static readonly string[] ProbeColumns =
    [
        "OpenIdConfigurationStatus", "LastOpenIdValidationUtc", "LastOpenIdValidationSummary", "LastOpenIdValidationDetail",
        "DirectoryDiscoveryStatus", "LastDirectoryDiscoveryUtc", "LastDirectoryDiscoverySummary", "LastDirectoryDiscoveryDetail",
    ];

    /// <summary>Every column of the registration row that is configuration identity: all but the diagnostics, in table order.</summary>
    public const string ConfigurationColumns =
        "Id, DisplayName, Authority, ClientId, UsesDeploymentSecret, ClientSecretRef, CallbackPath, Enabled, IsActive, CreatedUtc, UpdatedUtc, "
        + "RowVersion, AzureTenantId, AuthenticationContextId, AllowMemberAccessRequests, AllowGuestAccessRequests";

    public static ConfigEntraTenantsController Controller(
        AuditSqlScratch scratch,
        AppertureDbContext db,
        IPrincipalContext principal,
        IEntraOpenIdConnectConfigurationValidator? validator = null,
        IEntraDirectoryDiscoveryService? discovery = null,
        IAuthorizationEvaluationService? authorization = null)
    {
        var controller = new ConfigEntraTenantsController(
            new SqlEntraInstanceRegistrationRepository(db),
            principal,
            authorization ?? new AllowAll(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Entra:ClientSecret"] = "deployment-secret" }).Build(),
            Options.Create(new LocalAuthOptions { Enabled = true }),
            new SqlInstanceAuthUiPolicyRepository(db),
            validator ?? new ScriptedValidator(),
            discovery ?? new ScriptedDiscovery(),
            new EntraClientSecretMaterialProvisioner(Unused<ISecretStore>()),
            Unused<ISecretResolver>(),
            NewUnit(db, principal),
            new SqlAuditEventWriter(scratch.Options(), principal, HarnessCatalog, null, null, null));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    public static CreateEntraTenantRequestBody CreateBody(string displayName = "Contoso") =>
        new()
        {
            DisplayName = displayName,
            Authority = Authority,
            ClientId = ClientId,
            UsesDeploymentSecret = true,
            Enabled = true,
            AzureTenantId = TenantId,
        };

    public static UpdateEntraTenantRequestBody UpdateBody(EntraTenantListItemResponse item, string? displayName = null, bool? allowMembers = null) =>
        new()
        {
            DisplayName = displayName ?? item.DisplayName,
            Authority = item.Authority,
            ClientId = item.ClientId,
            UsesDeploymentSecret = item.UsesDeploymentSecret,
            CallbackPath = item.CallbackPath,
            Enabled = item.Enabled,
            AzureTenantId = item.AzureTenantId,
            AuthenticationContextId = item.AuthenticationContextId,
            AllowMemberAccessRequests = allowMembers ?? item.AllowMemberAccessRequests,
            AllowGuestAccessRequests = item.AllowGuestAccessRequests,
            RowVersionBase64 = item.RowVersionBase64,
        };

    /// <summary>Adds an administrator the audited unit can attribute a change to, and returns the principal that is signed in as them.</summary>
    public static async Task<Principal> AddAdministratorAsync(AuditSqlScratch scratch)
    {
        var id = NewUserId();
        await scratch.AddUserAsync(id, NewUsername("admin"));
        return new Principal(id);
    }

    /// <summary>Creates a registration through the controller (so it has the diagnostics row a created registration has).</summary>
    public static async Task<string> CreateRegistrationAsync(AuditSqlScratch scratch, IPrincipalContext principal, string displayName = "Contoso")
    {
        await using var db = scratch.NewDb();
        var created = await Controller(scratch, db, principal).Create(CreateBody(displayName), CancellationToken.None);
        return ((EntraTenantListItemResponse)((CreatedAtActionResult)created.Result!).Value!).Id;
    }

    public static async Task<EntraTenantListItemResponse> GetAsync(AuditSqlScratch scratch, string id, IPrincipalContext principal)
    {
        await using var db = scratch.NewDb();
        var result = await Controller(scratch, db, principal).GetById(id, CancellationToken.None);
        return (EntraTenantListItemResponse)((OkObjectResult)result.Result!).Value!;
    }

    /// <summary>A registration inserted by SQL alone, as an older code path or a test fixture leaves it: configuration, and no diagnostics row.</summary>
    public static async Task<string> InsertRegistrationWithoutDiagnosticsAsync(AuditSqlScratch scratch, string? id = null)
    {
        id ??= "reg" + Guid.NewGuid().ToString("N")[..16];
        await scratch.ExecuteAsync($"""
            INSERT INTO EntraInstanceRegistrations
                (Id, DisplayName, Authority, ClientId, UsesDeploymentSecret, Enabled, IsActive, CreatedUtc, UpdatedUtc, AzureTenantId,
                 AllowMemberAccessRequests, AllowGuestAccessRequests)
            VALUES (N'{id}', N'Inserted by SQL', N'{Authority}', N'{ClientId}', 1, 1, 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{TenantId}', 0, 0);
            """);
        return id;
    }

    /// <summary>The configuration row as stored, including its <c>rowversion</c> and <c>UpdatedUtc</c>.</summary>
    public static async Task<Dictionary<string, object?>> ConfigurationRowAsync(AuditSqlScratch scratch, string id) =>
        (await scratch.RowsAsync($"SELECT {ConfigurationColumns} FROM EntraInstanceRegistrations WHERE Id = N'{id}';")).Single();

    public static async Task<Dictionary<string, object?>?> DiagnosticsRowAsync(AuditSqlScratch scratch, string id) =>
        (await scratch.RowsAsync($"SELECT {string.Join(", ", ProbeColumns)} FROM EntraRegistrationDiagnostics WHERE RegistrationId = N'{id}';")).SingleOrDefault();

    public static Task<int> DiagnosticsRowCountAsync(AuditSqlScratch scratch, string id) =>
        scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraRegistrationDiagnostics WHERE RegistrationId = N'{id}';");

    /// <summary>A collaborator these paths never call: any call fails the test.</summary>
    public static T Unused<T>() where T : class => DispatchProxy.Create<T, UnusedProxy>();

    public class UnusedProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException($"{targetMethod?.DeclaringType?.Name}.{targetMethod?.Name} is not used by this test.");
    }

    public sealed class AllowAll : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    /// <summary>Allows exactly the named permissions, as a role holding only those would.</summary>
    public sealed class Allow(params string[] actions) : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(actions.Contains(context.Permission.Action, StringComparer.Ordinal));
    }

    /// <summary>An OpenID validation that answers as scripted and counts its calls.</summary>
    public sealed class ScriptedValidator : IEntraOpenIdConnectConfigurationValidator
    {
        private int _calls;

        public Func<int, EntraOpenIdConnectValidationResult> Script { get; init; } = _ =>
            new EntraOpenIdConnectValidationResult(true, OpenIdConfigurationStatus.Valid, null, "OpenID configuration is valid.", "issuer matches");

        public int Calls => Volatile.Read(ref _calls);

        public Task<EntraOpenIdConnectValidationResult> ValidateAsync(
            string authority, string clientId, bool usesDeploymentSecret, bool clientSecretConfigured, string? azureTenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Script(Interlocked.Increment(ref _calls)));
    }

    /// <summary>A directory search that answers as scripted, counts its calls and remembers the last search value.</summary>
    public sealed class ScriptedDiscovery : IEntraDirectoryDiscoveryService
    {
        private int _calls;

        public Func<int, (bool Success, DiscoveryFailureCategory Category, string Summary, string? Detail)> Script { get; init; } =
            _ => (true, DiscoveryFailureCategory.None, "Directory search succeeded.", null);

        public int Calls => Volatile.Read(ref _calls);

        public string? LastSearch { get; private set; }

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(
            EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default)
        {
            LastSearch = search;
            var (success, category, summary, detail) = Script(Interlocked.Increment(ref _calls));
            return Task.FromResult(new DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>(
                success, category, summary, detail, success ? [new EntraDirectoryUser("oid-1", "Dana", "dana@contoso.test", null)] : null));
        }

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(
            EntraDirectoryDiscoveryContext context, string? search, int top, string? skipToken, CancellationToken cancellationToken = default) =>
            SearchUsersAsync(context, search, top, cancellationToken);

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>> SearchGroupsAsync(
            EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default)
        {
            LastSearch = search;
            var (success, category, summary, detail) = Script(Interlocked.Increment(ref _calls));
            return Task.FromResult(new DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>(
                success, category, summary, detail, success ? [new EntraDirectoryGroup("gid-1", "Staff", null, null)] : null));
        }

        public Task<DirectoryDiscoveryResult<EntraDirectoryUser?>> GetUserByIdAsync(
            EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DirectoryDiscoveryResult<EntraDirectoryGroup?>> GetGroupByIdAsync(
            EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>A SQL parameter helper for the seeding statements.</summary>
    public static SqlParameter P(string name, object? value) => new(name, value ?? DBNull.Value);
}
