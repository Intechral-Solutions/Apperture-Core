using System.Reflection;
using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Core.Security.Secrets.Providers;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    // ---- M6 per service (gate §22 AUD-1.2): registration update and activation, and the sign-in policy ----

    [Fact]
    public async Task A_registration_update_and_activation_whose_acknowledgements_are_lost_answer_as_done_with_one_row_each()
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
        var lostPatch = new LostCommitAcknowledgement();
        var patchAttempts = new AttemptCounter();
        await using (var db = _db.NewDb(lostPatch, patchAttempts))
            (await Tenants(db, principal).Patch(id, UpdateBody(item, allowMembers: false, displayName: "Renamed"), CancellationToken.None)).Result
                .Should().BeOfType<OkObjectResult>();
        patchAttempts.Started.Should().Be(1, "verification confirmed the commit, so nothing reran");

        var lostActivate = new LostCommitAcknowledgement();
        var activateAttempts = new AttemptCounter();
        await using (var db = _db.NewDb(lostActivate, activateAttempts))
            (await Tenants(db, principal).Activate(id, CancellationToken.None)).Should().BeOfType<NoContentResult>();
        activateAttempts.Started.Should().Be(1);

        (await _db.ScalarAsync<string>($"SELECT DisplayName FROM EntraInstanceRegistrations WHERE Id = N'{id}';")).Should().Be("Renamed");
        (await _db.ScalarAsync<bool>($"SELECT IsActive FROM EntraInstanceRegistrations WHERE Id = N'{id}';")).Should().BeTrue();
        (await RowsAsync(id)).Select(r => r["Action"]).Should().Equal(
            AuditActionNames.TenantRegistrationCreated,
            AuditActionNames.TenantRegistrationUpdated,
            AuditActionNames.TenantRegistrationActivated);
    }

    [Fact]
    public async Task A_sign_in_policy_whose_audit_fails_is_unchanged_and_one_whose_acknowledgement_is_lost_answers_as_done()
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);
        var current = await PolicyAsync(principal);
        UpdateAuthUiPolicyRequestBody Toggle(AuthUiPolicyResponse policy) => new()
        {
            ShowLocalSignInInPrimaryChooser = policy.ShowLocalSignInInPrimaryChooser,
            PreferEntraInChooser = !policy.PreferEntraInChooser,
            AutoRedirectToEntra = policy.AutoRedirectToEntra,
            RowVersionBase64 = policy.RowVersionBase64,
        };

        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Policy(db, principal).Patch(Toggle(current), CancellationToken.None);
            await act.Should().ThrowAsync<Exception>();
        }
        (await PolicyAsync(principal)).PreferEntraInChooser.Should().Be(current.PreferEntraInChooser);

        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();
        await using (var db = _db.NewDb(lost, attempts))
            (await Policy(db, principal).Patch(Toggle(current), CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();

        attempts.Started.Should().Be(1);
        (await PolicyAsync(principal)).PreferEntraInChooser.Should().Be(!current.PreferEntraInChooser);
        (await _db.RowsAsync($"SELECT * FROM AuditLogs WHERE TargetType = N'auth_policy' AND TargetId = N'instance';"))
            .Should().ContainSingle().Which["Action"].Should().Be(AuditActionNames.SignInPolicyUpdated);
    }

    // ---- AUD-1.2 review: a supplied client secret and the credential the registration resolves (record D35) ----
    // The secret store runs its own transaction. A replacement is stored as a secret of its own before the unit, so the live
    // registration resolves its current secret until the row naming the new one commits; afterwards the unused one is retired,
    // and after an unknown outcome neither is.

    [Fact]
    public async Task A_supplied_secret_becomes_the_credential_only_with_the_committed_update_and_the_replaced_one_is_retired()
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);
        var (id, first) = await ArrangeActiveWithSecretAsync(principal);

        var item = await GetAsync(id, principal);
        await using (var db = _db.NewDb())
            (await Tenants(db, principal, store: SecretStore(db)).Patch(id, UpdateBody(item, allowMembers: false, clientSecret: "second-secret"), CancellationToken.None))
                .Result.Should().BeOfType<OkObjectResult>();

        var second = await SecretRefAsync(id);
        second.Should().NotBe(first, "a replacement is a secret of its own, never a new version of the live one");
        (await ResolveAsync(second)).Should().Be("second-secret");
        (await SecretStateAsync(first)).Should().Be(SecretState.Disabled, "the replaced secret is no longer named by any registration");
        (await SecretStateAsync(second)).Should().Be(SecretState.Active);
        var row = (await RowsAsync(id)).Last();
        row["Action"].Should().Be(AuditActionNames.TenantRegistrationUpdated);
        ((string)row["Details"]!).Should().Contain("clientAuthentication").And.NotContain("second-secret").And.NotContain(second);
    }

    [Fact]
    public async Task A_refused_update_leaves_the_live_credential_records_the_refusal_and_retires_the_secret_it_stored()
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);
        var (id, first) = await ArrangeActiveWithSecretAsync(principal);

        // Local sign-in off and the active registration disabled: no viable sign-in path would remain.
        var item = await GetAsync(id, principal);
        await using (var db = _db.NewDb())
            (await Tenants(db, principal, store: SecretStore(db), localEnabled: false)
                    .Patch(id, UpdateBody(item, allowMembers: false, enabled: false, clientSecret: "second-secret"), CancellationToken.None))
                .Result.Should().BeOfType<BadRequestObjectResult>();

        await AssertLiveCredentialUnchangedAsync(id, first);
        var refused = (await RowsAsync(id)).Should().ContainSingle(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationChangeRefused).Which;
        refused["Actor"].Should().Be(admin);
        refused["Basis"].Should().Be(AuditBases.Attempt);
        using var details = JsonDocument.Parse((string)refused["Details"]!);
        details.RootElement.GetProperty("change").GetString().Should().Be("update");
        details.RootElement.GetProperty("reason").GetString().Should().Be("sign_in_lockout");
        (await RowsAsync(id)).Should().NotContain(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationUpdated);
    }

    [Fact]
    public async Task A_conflicting_update_leaves_the_live_credential_and_retires_the_secret_it_stored()
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);
        var (id, first) = await ArrangeActiveWithSecretAsync(principal);
        var stale = await GetAsync(id, principal);
        await using (var db = _db.NewDb())
            await Tenants(db, principal).Patch(id, UpdateBody(stale, allowMembers: false, displayName: "Changed elsewhere"), CancellationToken.None);

        await using (var db = _db.NewDb())
            (await Tenants(db, principal, store: SecretStore(db)).Patch(id, UpdateBody(stale, allowMembers: false, clientSecret: "second-secret"), CancellationToken.None))
                .Result.Should().BeOfType<ConflictObjectResult>();

        await AssertLiveCredentialUnchangedAsync(id, first);
    }

    [Fact]
    public async Task An_update_whose_audit_fails_rolls_back_and_retires_the_secret_it_stored()
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);
        var (id, first) = await ArrangeActiveWithSecretAsync(principal);
        var item = await GetAsync(id, principal);

        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        await using (var db = _db.NewDb(failAudit))
        await using (var secrets = _db.NewDb())
        {
            var act = () => Tenants(db, principal, store: SecretStore(secrets)).Patch(id, UpdateBody(item, allowMembers: false, clientSecret: "second-secret"), CancellationToken.None);
            await act.Should().ThrowAsync<Exception>();
        }

        await AssertLiveCredentialUnchangedAsync(id, first);
    }

    [Fact]
    public async Task An_update_whose_commit_acknowledgement_is_lost_keeps_the_new_credential_and_retires_the_replaced_one()
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);
        var (id, first) = await ArrangeActiveWithSecretAsync(principal);
        var item = await GetAsync(id, principal);

        // The secret store has its own context here, so the injected commit faults reach only the unit's transaction.
        var lost = new LostCommitAcknowledgement();
        await using (var db = _db.NewDb(lost))
        await using (var secrets = _db.NewDb())
            (await Tenants(db, principal, store: SecretStore(secrets)).Patch(id, UpdateBody(item, allowMembers: false, clientSecret: "second-secret"), CancellationToken.None))
                .Result.Should().BeOfType<OkObjectResult>();

        var second = await SecretRefAsync(id);
        second.Should().NotBe(first);
        (await ResolveAsync(second)).Should().Be("second-secret");
        (await SecretStateAsync(first)).Should().Be(SecretState.Disabled);
        (await RowsAsync(id)).Count(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationUpdated).Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_update_whose_outcome_is_unknown_retires_neither_secret(bool committed)
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);
        var (id, first) = await ArrangeActiveWithSecretAsync(principal);
        var item = await GetAsync(id, principal);

        DbTransactionInterceptor fault = committed ? new LostCommitAcknowledgement() : new RolledBackCommit();
        await using (var db = _db.NewDb(fault))
        await using (var secrets = _db.NewDb())
        {
            var act = () => Tenants(db, principal, store: SecretStore(secrets), unit: NewUnit(db, principal, verification: new UnreachableVerification()))
                .Patch(id, UpdateBody(item, allowMembers: false, clientSecret: "second-secret"), CancellationToken.None);
            await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        }

        var names = await RegistrationSecretsAsync(id);
        names.Should().HaveCount(2).And.OnlyContain(s => s.State == SecretState.Active, "either secret may be the one a committed row names");
        var live = await SecretRefAsync(id);
        (await ResolveAsync(live)).Should().Be(committed ? "second-secret" : "first-secret");
    }

    [Fact]
    public async Task Activation_and_delete_refused_by_their_guards_change_nothing_and_are_recorded_after_rollback()
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);
        var (activeId, _) = await ArrangeActiveWithSecretAsync(principal);

        string disabledId;
        await using (var db = _db.NewDb())
        {
            var body = CreateBody();
            body.Enabled = false;
            var created = await Tenants(db, principal).Create(body, CancellationToken.None);
            disabledId = ((EntraTenantListItemResponse)((CreatedAtActionResult)created.Result!).Value!).Id;
        }

        await using (var db = _db.NewDb())
            (await Tenants(db, principal).Activate(disabledId, CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();
        await using (var db = _db.NewDb())
            (await Tenants(db, principal, localEnabled: false).Delete(activeId, CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();

        (await _db.ScalarAsync<bool>($"SELECT IsActive FROM EntraInstanceRegistrations WHERE Id = N'{disabledId}';")).Should().BeFalse();
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{activeId}';")).Should().Be(1);
        string Reason(Dictionary<string, object?> row)
        {
            using var details = JsonDocument.Parse((string)row["Details"]!);
            return details.RootElement.GetProperty("change").GetString() + "/" + details.RootElement.GetProperty("reason").GetString();
        }

        var activation = (await RowsAsync(disabledId)).Should().ContainSingle(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationChangeRefused).Which;
        Reason(activation).Should().Be("activate/registration_disabled");
        var delete = (await RowsAsync(activeId)).Should().ContainSingle(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationChangeRefused).Which;
        Reason(delete).Should().Be("delete/sign_in_lockout");
        delete["Actor"].Should().Be(admin);
        (await RowsAsync(activeId)).Should().NotContain(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationDeleted);
    }

    /// <summary>An active registration whose client secret (<c>first-secret</c>) is in the SQL secret store.</summary>
    private async Task<(string Id, string SecretRef)> ArrangeActiveWithSecretAsync(IPrincipalContext principal)
    {
        string id;
        await using (var db = _db.NewDb())
        {
            // A stored secret takes precedence over the deployment secret when the registration's credential is resolved.
            var body = CreateBody();
            body.ClientSecret = "first-secret";
            var created = await Tenants(db, principal, store: SecretStore(db)).Create(body, CancellationToken.None);
            id = ((EntraTenantListItemResponse)((CreatedAtActionResult)created.Result!).Value!).Id;
        }

        await using (var db = _db.NewDb())
            (await Tenants(db, principal).Activate(id, CancellationToken.None)).Should().BeOfType<NoContentResult>();
        var secretRef = await SecretRefAsync(id);
        (await ResolveAsync(secretRef)).Should().Be("first-secret");
        return (id, secretRef);
    }

    /// <summary>The registration still names its first secret, which still resolves; the one stored for the change is retired.</summary>
    private async Task AssertLiveCredentialUnchangedAsync(string id, string first)
    {
        (await SecretRefAsync(id)).Should().Be(first);
        (await ResolveAsync(first)).Should().Be("first-secret", "the credential the live registration resolves never changed");
        var secrets = await RegistrationSecretsAsync(id);
        secrets.Should().HaveCount(2);
        secrets.Single(s => s.Id == first).State.Should().Be(SecretState.Active);
        secrets.Single(s => s.Id != first).State.Should().Be(SecretState.Disabled, "the secret stored for a change that did not commit is retired");
    }

    private async Task<string> SecretRefAsync(string registrationId) =>
        (await _db.ScalarAsync<string>($"SELECT ClientSecretRef FROM EntraInstanceRegistrations WHERE Id = N'{registrationId}';"))!;

    private async Task<SecretState> SecretStateAsync(string secretRef) =>
        (SecretState)await _db.ScalarAsync<int>($"SELECT State FROM SecretDescriptors WHERE Id = '{secretRef}';");

    private async Task<List<(string Id, SecretState State)>> RegistrationSecretsAsync(string registrationId) =>
        (await _db.RowsAsync($"SELECT Id, State FROM SecretDescriptors WHERE Name = N'{EntraClientSecretMaterialProvisioner.BuildSecretName(registrationId)}';"))
            .Select(r => (((Guid)r["Id"]!).ToString("D"), (SecretState)(int)r["State"]!))
            .ToList();

    private async Task<string?> ResolveAsync(string secretRef)
    {
        await using var db = _db.NewDb();
        try
        {
            var resolved = await new SqlSecretResolver(SecretStore(db), new PassThroughProtector()).ResolveAsync(new SecretRef(secretRef));
            return resolved.Material.OpaqueString;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static SqlSecretStore SecretStore(AppertureDbContext db) =>
        new(db, new PassThroughProtector(), new DefaultSecretAuditActorProvider());

    private sealed class PassThroughProtector : ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(plaintext.ToArray());

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(protectedPayload.ToArray());
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

    private static UpdateEntraTenantRequestBody UpdateBody(
        EntraTenantListItemResponse item, bool allowMembers, bool enabled = true, string? displayName = null, string? clientSecret = null) =>
        new()
        {
            ClientSecret = clientSecret,
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

    private ConfigEntraTenantsController Tenants(
        AppertureDbContext db,
        IPrincipalContext principal,
        ISecretStore? store = null,
        bool localEnabled = true,
        IAuditedUnitOfWork? unit = null) =>
        WithContext(new ConfigEntraTenantsController(
            new SqlEntraInstanceRegistrationRepository(db),
            principal,
            new AllowAll(),
            Configuration(),
            Options.Create(new LocalAuthOptions { Enabled = localEnabled }),
            new SqlInstanceAuthUiPolicyRepository(db),
            Unused<IEntraOpenIdConnectConfigurationValidator>(),
            Unused<IEntraDirectoryDiscoveryService>(),
            new EntraClientSecretMaterialProvisioner(store ?? Unused<ISecretStore>()),
            Unused<ISecretResolver>(),
            unit ?? NewUnit(db, principal),
            new SqlAuditEventWriter(_db.Options(), principal, HarnessCatalog, null, null, null)));

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
