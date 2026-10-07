using System.Reflection;
using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.2 Entra tenant registrations and the sign-in policy against SQL Server (gate §22 new coverage; record D7, D35): each
/// change commits with its row or not at all, a request that changes nothing writes no row and answers as before, and the
/// row names the settings that changed, never a secret. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AuthConfigurationAuditSqlServerTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private AuditSqlScratch _db = null!;

    public AuthConfigurationAuditSqlServerTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync() => _db = await AuditSqlScratch.CreateAsync("Apperture_Aud12AuthConfig_");

    public async Task DisposeAsync()
    {
        _output.WriteLine(_db.ExecutionMarker);
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task A_registration_is_created_changed_activated_and_deleted_each_with_its_row_and_an_unchanged_update_writes_none()
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);

        string id;
        await using (var db = _db.NewDb())
        {
            var created = await Tenants(db, principal).Create(CreateBody(), CancellationToken.None);
            id = ((EntraTenantListItemResponse)((CreatedAtActionResult)created.Result!).Value!).Id;
        }

        var item = await GetAsync(id, principal);
        await using (var db = _db.NewDb())
            (await Tenants(db, principal).Patch(id, UpdateBody(item, allowMembers: true), CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
        item = await GetAsync(id, principal);
        await using (var db = _db.NewDb())
            (await Tenants(db, principal).Patch(id, UpdateBody(item, allowMembers: true), CancellationToken.None)).Result
                .Should().BeOfType<OkObjectResult>("the same settings answer as before");
        await using (var db = _db.NewDb())
            (await Tenants(db, principal).Activate(id, CancellationToken.None)).Should().BeOfType<NoContentResult>();
        await using (var db = _db.NewDb())
            (await Tenants(db, principal).Activate(id, CancellationToken.None)).Should().BeOfType<NoContentResult>("already active answers as before");

        var rows = await RowsAsync(id);
        rows.Select(r => r["Action"]).Should().Equal(
            AuditActionNames.TenantRegistrationCreated,
            AuditActionNames.TenantRegistrationUpdated,
            AuditActionNames.AccessRequestPolicyChanged,
            AuditActionNames.TenantRegistrationActivated);
        rows.Should().OnlyContain(r => (string)r["Actor"]! == admin);
        using (var changed = JsonDocument.Parse((string)rows[1]["Details"]!))
            changed.RootElement.GetProperty("changed").EnumerateArray().Select(e => e.GetString()).Should().Equal("allowMemberAccessRequests");

        // Deactivated (by disabling) so the delete guard allows it, then deleted.
        item = await GetAsync(id, principal);
        await using (var db = _db.NewDb())
            await Tenants(db, principal).Patch(id, UpdateBody(item, allowMembers: true, enabled: false), CancellationToken.None);
        await using (var db = _db.NewDb())
            (await Tenants(db, principal).Delete(id, CancellationToken.None)).Should().BeOfType<NoContentResult>();
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{id}';")).Should().Be(0);
        (await RowsAsync(id)).Select(r => r["Action"]).Should().EndWith(AuditActionNames.TenantRegistrationDeleted);
    }

    [Fact]
    public async Task When_the_audit_insert_fails_a_registration_change_rolls_back_with_it()
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);
        string id;
        await using (var db = _db.NewDb())
        {
            var created = await Tenants(db, principal).Create(CreateBody(), CancellationToken.None);
            id = ((EntraTenantListItemResponse)((CreatedAtActionResult)created.Result!).Value!).Id;
        }

        var item = await GetAsync(id, principal);
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Tenants(db, principal).Patch(id, UpdateBody(item, allowMembers: true, displayName: "Renamed"), CancellationToken.None);
            await act.Should().ThrowAsync<Exception>();
        }

        (await _db.ScalarAsync<string>($"SELECT DisplayName FROM EntraInstanceRegistrations WHERE Id = N'{id}';")).Should().Be("Contoso");
        (await _db.ScalarAsync<bool>($"SELECT AllowMemberAccessRequests FROM EntraInstanceRegistrations WHERE Id = N'{id}';")).Should().BeFalse();
        (await RowsAsync(id)).Select(r => r["Action"]).Should().Equal(AuditActionNames.TenantRegistrationCreated);
    }

    [Fact]
    public async Task The_sign_in_policy_changes_with_its_row_and_the_same_policy_writes_none()
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);

        var current = await PolicyAsync(principal);
        await using (var db = _db.NewDb())
        {
            var changed = await Policy(db, principal).Patch(
                new UpdateAuthUiPolicyRequestBody
                {
                    ShowLocalSignInInPrimaryChooser = current.ShowLocalSignInInPrimaryChooser,
                    PreferEntraInChooser = !current.PreferEntraInChooser,
                    AutoRedirectToEntra = current.AutoRedirectToEntra,
                    RowVersionBase64 = current.RowVersionBase64,
                },
                CancellationToken.None);
            changed.Result.Should().BeOfType<OkObjectResult>();
        }

        var after = await PolicyAsync(principal);
        await using (var db = _db.NewDb())
        {
            var same = await Policy(db, principal).Patch(
                new UpdateAuthUiPolicyRequestBody
                {
                    ShowLocalSignInInPrimaryChooser = after.ShowLocalSignInInPrimaryChooser,
                    PreferEntraInChooser = after.PreferEntraInChooser,
                    AutoRedirectToEntra = after.AutoRedirectToEntra,
                    RowVersionBase64 = after.RowVersionBase64,
                },
                CancellationToken.None);
            same.Result.Should().BeOfType<OkObjectResult>();
        }

        var row = (await _db.RowsAsync($"SELECT * FROM AuditLogs WHERE TargetType = N'auth_policy' AND TargetId = N'instance';"))
            .Should().ContainSingle("the second request changed nothing").Which;
        row["Action"].Should().Be(AuditActionNames.SignInPolicyUpdated);
        row["Actor"].Should().Be(admin);
        using var details = JsonDocument.Parse((string)row["Details"]!);
        details.RootElement.GetProperty("changed").EnumerateArray().Select(e => e.GetString()).Should().Equal("preferEntraInChooser");
    }

    // ---- helpers ----

    private async Task<(string Id, string Username)> AddUserAsync(string stem)
    {
        var id = NewUserId();
        var username = NewUsername(stem);
        await _db.AddUserAsync(id, username);
        return (id, username);
    }

    private static CreateEntraTenantRequestBody CreateBody() =>
        new()
        {
            DisplayName = "Contoso",
            Authority = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0",
            ClientId = "22222222-2222-2222-2222-222222222222",
            UsesDeploymentSecret = true,
            Enabled = true,
            AzureTenantId = "11111111-1111-1111-1111-111111111111",
        };

    private static UpdateEntraTenantRequestBody UpdateBody(EntraTenantListItemResponse item, bool allowMembers, bool enabled = true, string? displayName = null) =>
        new()
        {
            DisplayName = displayName ?? item.DisplayName,
            Authority = item.Authority,
            ClientId = item.ClientId,
            UsesDeploymentSecret = item.UsesDeploymentSecret,
            CallbackPath = item.CallbackPath,
            Enabled = enabled,
            AzureTenantId = item.AzureTenantId,
            AuthenticationContextId = item.AuthenticationContextId,
            AllowMemberAccessRequests = allowMembers,
            AllowGuestAccessRequests = item.AllowGuestAccessRequests,
            RowVersionBase64 = item.RowVersionBase64,
        };

    private async Task<EntraTenantListItemResponse> GetAsync(string id, IPrincipalContext principal)
    {
        await using var db = _db.NewDb();
        var result = await Tenants(db, principal).GetById(id, CancellationToken.None);
        return (EntraTenantListItemResponse)((OkObjectResult)result.Result!).Value!;
    }

    private async Task<AuthUiPolicyResponse> PolicyAsync(IPrincipalContext principal)
    {
        await using var db = _db.NewDb();
        var result = await Policy(db, principal).Get(CancellationToken.None);
        return (AuthUiPolicyResponse)((OkObjectResult)result.Result!).Value!;
    }

    private Task<List<Dictionary<string, object?>>> RowsAsync(string registrationId) =>
        _db.RowsAsync($"SELECT * FROM AuditLogs WHERE TargetType = N'entra_tenant_registration' AND TargetId = N'{registrationId}' ORDER BY Id;");

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Entra:ClientSecret"] = "deployment-secret" }).Build();

    private static ConfigEntraTenantsController Tenants(AppertureDbContext db, IPrincipalContext principal) =>
        WithContext(new ConfigEntraTenantsController(
            new SqlEntraInstanceRegistrationRepository(db),
            principal,
            new AllowAll(),
            Configuration(),
            Options.Create(new LocalAuthOptions { Enabled = true }),
            new SqlInstanceAuthUiPolicyRepository(db),
            Unused<IEntraOpenIdConnectConfigurationValidator>(),
            Unused<IEntraDirectoryDiscoveryService>(),
            new EntraClientSecretMaterialProvisioner(Unused<ISecretStore>()),
            Unused<ISecretResolver>(),
            NewUnit(db, principal)));

    private static ConfigAuthUiPolicyController Policy(AppertureDbContext db, IPrincipalContext principal) =>
        WithContext(new ConfigAuthUiPolicyController(
            new SqlInstanceAuthUiPolicyRepository(db),
            principal,
            new AllowAll(),
            Options.Create(new LocalAuthOptions { Enabled = true }),
            Options.Create(new EntraAuthOptions()),
            NewUnit(db, principal)));

    private static T WithContext<T>(T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private sealed class AllowAll : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(Apperture.Core.Domain.Governance.AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    /// <summary>A collaborator these paths never call: any call fails the test.</summary>
    private static T Unused<T>() where T : class => DispatchProxy.Create<T, UnusedProxy>();

    public class UnusedProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException($"{targetMethod?.DeclaringType?.Name}.{targetMethod?.Name} is not used by this test.");
    }
}
