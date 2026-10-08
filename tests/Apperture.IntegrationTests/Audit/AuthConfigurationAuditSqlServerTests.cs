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
/// AUD-1.2 Entra tenant registrations and the sign-in policy against SQL Server (gate §22 new coverage; record D7, D35 as
/// amended by AUD-1.4): each change commits with its rows or not at all, a request that changes nothing writes no row and
/// answers as before, and the rows name the settings that changed, never a secret or its reference. A registration's client
/// secret is stored and retired inside the change's unit and recorded under the Secrets category. Fails, never skips,
/// without SQL Server.
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

    // ---- A supplied client secret and the credential the registration resolves (AUD-1.2 record D35, amended by AUD-1.4) ----
    // The secret store joins the unit's transaction. A replacement is a secret of its own, stored inside the unit once every
    // guard has passed; the row naming it and the retirement of the one it replaces commit with it. A change that does not
    // commit stores and retires nothing, and after an unknown outcome it is all committed or none.

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

        var rows = await RowsAsync(id);
        var updated = rows.Single(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationUpdated);
        ((string)updated["Details"]!).Should().Contain("clientAuthentication");
        // The arranged create stored the first secret; this update's rows share its operation event id.
        var stored = rows.Should().ContainSingle(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationClientSecretStored
            && Equals(r["OperationEventId"], updated["EventId"])).Which;
        var retired = rows.Should().ContainSingle(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationClientSecretRetired).Which;
        foreach (var row in new[] { stored, retired })
        {
            row["Category"].Should().Be(AuditCategories.Secrets);
            row["Severity"].Should().Be(AuditSeverities.Elevated);
            row["Actor"].Should().Be(admin);
            row["OperationEventId"].Should().Be(updated["EventId"], "the secret rows commit in the update's unit");
        }

        Field(stored, "change").Should().Be("registration_updated");
        Field(retired, "reason").Should().Be("replaced");
        AssertNoSecretMaterial(rows, "first-secret", "second-secret", first, second);
    }

    [Fact]
    public async Task A_registration_created_with_a_secret_records_its_storage_and_one_whose_audit_fails_stores_nothing()
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);

        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        await using (var db = _db.NewDb(failAudit))
        {
            var body = CreateBody();
            body.ClientSecret = "failed-secret";
            var act = () => Tenants(db, principal, store: SecretStore(db)).Create(body, CancellationToken.None);
            await act.Should().ThrowAsync<Exception>();
        }

        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM EntraInstanceRegistrations;")).Should().Be(0);
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretDescriptors WHERE Provider = N'{EntraClientSecretMaterialProvisioner.SecretProvider}';"))
            .Should().Be(0, "the secret was stored in the unit that rolled back, so it never existed");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SecretAudits;")).Should().Be(0);

        var (id, secretRef) = await ArrangeActiveWithSecretAsync(principal);
        var rows = await RowsAsync(id);
        var created = rows.Single(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationCreated);
        var stored = rows.Should().ContainSingle(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationClientSecretStored).Which;
        stored["OperationEventId"].Should().Be(created["EventId"]);
        Field(stored, "change").Should().Be("registration_created");
        AssertNoSecretMaterial(rows, "first-secret", "failed-secret", secretRef);
    }

    [Fact]
    public async Task A_deleted_registrations_secret_is_retired_in_the_delete_and_a_delete_whose_audit_fails_keeps_both()
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);
        var (id, secretRef) = await ArrangeActiveWithSecretAsync(principal);

        // Disabled first so the delete guard allows it.
        var item = await GetAsync(id, principal);
        await using (var db = _db.NewDb())
            await Tenants(db, principal).Patch(id, UpdateBody(item, allowMembers: false, enabled: false), CancellationToken.None);

        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Tenants(db, principal, store: SecretStore(db)).Delete(id, CancellationToken.None);
            await act.Should().ThrowAsync<Exception>();
        }

        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{id}';")).Should().Be(1);
        (await SecretStateAsync(secretRef)).Should().Be(SecretState.Active, "the retirement rolled back with the delete");

        await using (var db = _db.NewDb())
            (await Tenants(db, principal, store: SecretStore(db)).Delete(id, CancellationToken.None)).Should().BeOfType<NoContentResult>();

        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{id}';")).Should().Be(0);
        (await SecretStateAsync(secretRef)).Should().Be(SecretState.Disabled);
        var rows = await RowsAsync(id);
        var deleted = rows.Single(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationDeleted);
        var retired = rows.Should().ContainSingle(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationClientSecretRetired).Which;
        retired["OperationEventId"].Should().Be(deleted["EventId"]);
        Field(retired, "reason").Should().Be("registration_deleted");
        AssertNoSecretMaterial(rows, "first-secret", secretRef);
    }

    [Fact]
    public async Task A_refused_update_leaves_the_live_credential_records_the_refusal_and_stores_no_secret()
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
    public async Task A_conflicting_update_leaves_the_live_credential_and_stores_no_secret()
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
    public async Task An_update_whose_audit_fails_rolls_back_with_its_secret()
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);
        var (id, first) = await ArrangeActiveWithSecretAsync(principal);
        var item = await GetAsync(id, principal);
        var storeEvidenceBefore = await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SecretAudits;");

        // The secret store shares the unit's context, as it does in the host, so it joins the unit's transaction.
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Tenants(db, principal, store: SecretStore(db)).Patch(id, UpdateBody(item, allowMembers: false, clientSecret: "second-secret"), CancellationToken.None);
            await act.Should().ThrowAsync<Exception>();
        }

        await AssertLiveCredentialUnchangedAsync(id, first);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SecretAudits;")).Should().Be(storeEvidenceBefore, "the store's evidence rolled back with it");
    }

    [Fact]
    public async Task An_update_whose_commit_acknowledgement_is_lost_keeps_the_new_credential_and_retires_the_replaced_one()
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);
        var (id, first) = await ArrangeActiveWithSecretAsync(principal);
        var item = await GetAsync(id, principal);

        // The secret store joins the unit's transaction, so the injected fault reaches the one commit there is.
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();
        await using (var db = _db.NewDb(lost, attempts))
            (await Tenants(db, principal, store: SecretStore(db)).Patch(id, UpdateBody(item, allowMembers: false, clientSecret: "second-secret"), CancellationToken.None))
                .Result.Should().BeOfType<OkObjectResult>();

        attempts.Started.Should().Be(1, "the store began no transaction of its own and the verified commit was not rerun");
        var second = await SecretRefAsync(id);
        second.Should().NotBe(first);
        (await ResolveAsync(second)).Should().Be("second-secret");
        (await SecretStateAsync(first)).Should().Be(SecretState.Disabled);
        (await RegistrationSecretsAsync(id)).Should().HaveCount(2);
        var rows = await RowsAsync(id);
        rows.Count(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationUpdated).Should().Be(1);
        rows.Count(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationClientSecretStored).Should().Be(2, "one from the arranged create, one from this update");
        rows.Count(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationClientSecretRetired).Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_update_whose_outcome_is_unknown_leaves_the_secrets_as_wholly_committed_or_wholly_unchanged(bool committed)
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);
        var (id, first) = await ArrangeActiveWithSecretAsync(principal);
        var item = await GetAsync(id, principal);

        DbTransactionInterceptor fault = committed ? new LostCommitAcknowledgement() : new RolledBackCommit();
        await using (var db = _db.NewDb(fault))
        {
            var act = () => Tenants(db, principal, store: SecretStore(db), unit: NewUnit(db, principal, verification: new UnreachableVerification()))
                .Patch(id, UpdateBody(item, allowMembers: false, clientSecret: "second-secret"), CancellationToken.None);
            await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        }

        var secrets = await RegistrationSecretsAsync(id);
        var live = await SecretRefAsync(id);
        if (committed)
        {
            // The new secret, the row naming it and the retirement of the first all committed.
            live.Should().NotBe(first);
            (await ResolveAsync(live)).Should().Be("second-secret");
            secrets.Should().HaveCount(2);
            secrets.Single(s => s.Id == first).State.Should().Be(SecretState.Disabled);
            (await RowsAsync(id)).Should().Contain(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationClientSecretRetired);
        }
        else
        {
            await AssertLiveCredentialUnchangedAsync(id, first);
            (await RowsAsync(id)).Should().NotContain(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationUpdated);
        }
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

    /// <summary>
    /// The registration still names its first secret, which still resolves, and is the only one it owns: a change that did not
    /// commit stored no secret (it would have been stored in the unit that rolled back) and retired none.
    /// </summary>
    private async Task AssertLiveCredentialUnchangedAsync(string id, string first)
    {
        (await SecretRefAsync(id)).Should().Be(first);
        (await ResolveAsync(first)).Should().Be("first-secret", "the credential the live registration resolves never changed");
        var secrets = await RegistrationSecretsAsync(id);
        secrets.Should().ContainSingle().Which.Should().Be((first, SecretState.Active));
        (await RowsAsync(id)).Count(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationClientSecretStored)
            .Should().Be(1, "only the arranged create stored a secret");
        (await RowsAsync(id)).Should().NotContain(r => (string)r["Action"]! == AuditActionNames.TenantRegistrationClientSecretRetired);
    }

    private static string? Field(Dictionary<string, object?> row, string name)
    {
        using var details = JsonDocument.Parse((string)row["Details"]!);
        return details.RootElement.GetProperty(name).GetString();
    }

    /// <summary>No audit column holds a secret value or a secret's store id, which is the reference a registration holds.</summary>
    private static void AssertNoSecretMaterial(IEnumerable<Dictionary<string, object?>> rows, params string[] material)
    {
        foreach (var row in rows)
        {
            foreach (var value in row.Values.OfType<string>())
            {
                foreach (var item in material)
                    value.Should().NotContainEquivalentOf(item, $"audit column values never hold a secret or its reference ({row["Action"]})");
            }
        }
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
