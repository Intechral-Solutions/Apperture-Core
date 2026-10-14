using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Execution;
using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Models;
using Apperture.ModuleSdk.Integration.Configuration;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.RuntimePlane.Engine.Integration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using Resolution = Apperture.IntegrationTests.Audit.ConnectionSecretOwnershipSupport.Resolution;
using static Apperture.IntegrationTests.Audit.SettingSecretOwnershipSqlSupport;
using Foreign = Apperture.IntegrationTests.Audit.SettingSecretOwnershipSqlSupport.Foreign;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// SEC-2.3 (A3.1 to A3.5 and A3.8) against SQL Server on locking READ COMMITTED and READ_COMMITTED_SNAPSHOT catalogs: a setting's
/// secret is stored under the owner the setting defines, replaced, cleared and reset only when the secret's own descriptor proves it
/// belongs to that setting, and read back only through the ownership check. A neighbour's secret (another integration's, an instance
/// setting's, a connection's, an Entra registration's) is never versioned, disabled, adopted or returned: its descriptor, versions,
/// payloads and evidence stay byte for byte as they were. Legacy references (the instance shape on an integration row) are never
/// adopted and are retired only on H1's sole-holder evidence. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class SettingSecretOwnershipSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private const string Sentinel = "sentinel-sec23-new-value";

    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public SettingSecretOwnershipSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    public static IEnumerable<object[]> Isolation() => SettingSecretOwnershipSqlSupport.Isolation();

    public static IEnumerable<object[]> ReplaceMatrix() => IsolationByForeign([.. ForeignWithMaterial, Foreign.Dangling]);

    public static IEnumerable<object[]> PlatformReplaceMatrix() =>
        IsolationByForeign([Foreign.OtherIntegration, Foreign.Connection, Foreign.Entra, Foreign.OtherProviderSameName, Foreign.CaseVariant, Foreign.Dangling]);

    private sealed record Case(AuditSqlScratch Scratch, string A, string B, string Admin, IPrincipalContext Principal, Registry Registry);

    private async Task<Case> NewCaseAsync(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        _output.WriteLine(scratch.ExecutionMarker);
        var a = NewIntegrationId();
        var b = NewIntegrationId();
        await AddIntegrationAsync(scratch, a);
        await AddIntegrationAsync(scratch, b);
        var (admin, principal) = await ConnectionSqlHarness.AdminAsync(scratch);
        // The instance setting is one row per catalog, so each case starts without it.
        await scratch.ExecuteAsync($"DELETE FROM PlatformSettingValues WHERE ScopeKind = N'{PlatformSettingScopeConstants.InstanceKind}';");
        return new Case(scratch, a, b, admin, principal, new Registry());
    }

    // ---- A3.1: new secrets are stored under the owner the setting defines ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Two_integrations_that_set_the_same_key_get_two_distinct_secrets_under_their_own_owner_and_the_instance_name_is_unchanged(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);

        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, "a-value");
        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.B, "b-value");
        await ReplaceInstanceAsync(c.Scratch, c.Principal, "smtp-value");

        var a = (await IntegrationRefAsync(c.Scratch, c.A, SecretKey))!;
        var b = (await IntegrationRefAsync(c.Scratch, c.B, SecretKey))!;
        var smtp = (await InstanceRefAsync(c.Scratch, SmtpKey))!;
        new[] { a, b, smtp }.Distinct().Should().HaveCount(3);
        (await DescriptorAsync(c.Scratch, a)).Should().Be(("integration-setting", $"integration-setting:{c.A}:{SecretKey}"));
        (await DescriptorAsync(c.Scratch, b)).Should().Be(("integration-setting", $"integration-setting:{c.B}:{SecretKey}"));
        (await DescriptorAsync(c.Scratch, smtp)).Should().Be(("platform-setting", $"platform-setting:{SmtpKey}"));

        // Each is readable by its own owner, and by no other.
        await using var res = ConnectionSecretOwnershipSupport.NewResolution(c.Scratch);
        (await res.Composite.ResolveOwnedAsync(new SecretRef(a), SettingSecretOwnership.ForIntegrationSetting(c.A, SecretKey))).Material.OpaqueString.Should().Be("a-value");
        (await res.Composite.ResolveOwnedAsync(new SecretRef(b), SettingSecretOwnership.ForIntegrationSetting(c.B, SecretKey))).Material.OpaqueString.Should().Be("b-value");
        (await res.Composite.ResolveOwnedAsync(new SecretRef(smtp), SettingSecretOwnership.ForPlatformSetting(SmtpKey))).Material.OpaqueString.Should().Be("smtp-value");
        var cross = () => res.Composite.ResolveOwnedAsync(new SecretRef(a), SettingSecretOwnership.ForIntegrationSetting(c.B, SecretKey)).AsTask();
        await cross.Should().ThrowAsync<SecretUnavailableException>("integration B's owner does not match integration A's secret");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_second_replace_by_the_same_setting_versions_its_own_secret_and_keeps_the_reference(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, "one");
        var first = (await IntegrationRefAsync(c.Scratch, c.A, SecretKey))!;

        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, "two");

        (await IntegrationRefAsync(c.Scratch, c.A, SecretKey)).Should().Be(first);
        (await c.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretVersions WHERE SecretId = '{first}';")).Should().Be(2);
        await using var res = ConnectionSecretOwnershipSupport.NewResolution(c.Scratch);
        (await res.Composite.ResolveOwnedAsync(new SecretRef(first), SettingSecretOwnership.ForIntegrationSetting(c.A, SecretKey))).Material.OpaqueString.Should().Be("two");
    }

    // ---- A3.2: a replace never versions a secret that is not the setting's own ----

    [Theory]
    [MemberData(nameof(ReplaceMatrix))]
    public async Task A_replace_over_a_reference_that_is_not_the_integration_settings_own_repoints_it_and_leaves_the_victim_unchanged(bool rcsi, Foreign kind)
    {
        var c = await NewCaseAsync(rcsi);
        var foreign = await SeedForeignAsync(c.Scratch, kind, c.A, SecretKey);
        await InsertIntegrationRowAsync(c.Scratch, c.A, SecretKey, foreign);
        // An instance setting's secret is held by that instance setting's row. (An instance-shaped secret that nothing else holds is
        // indistinguishable from a legacy integration secret and is retired on H1's evidence; the legacy tests cover that.)
        if (kind == Foreign.InstanceSetting)
            await InsertInstanceRowAsync(c.Scratch, SecretKey, foreign);
        var before = await SnapshotAsync(c.Scratch, foreign);

        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, Sentinel);

        var repointed = (await IntegrationRefAsync(c.Scratch, c.A, SecretKey))!;
        repointed.Should().NotBe(foreign);
        (await DescriptorAsync(c.Scratch, repointed)).Should().Be(("integration-setting", $"integration-setting:{c.A}:{SecretKey}"));
        await using var res = ConnectionSecretOwnershipSupport.NewResolution(c.Scratch);
        (await res.Composite.ResolveOwnedAsync(new SecretRef(repointed), SettingSecretOwnership.ForIntegrationSetting(c.A, SecretKey))).Material.OpaqueString.Should().Be(Sentinel);

        (await SnapshotAsync(c.Scratch, foreign)).Should().Be(before, "the victim's descriptor, versions, payload and evidence are untouched");
        var stored = await AuditRowsAsync(c.Scratch, AuditActionNames.IntegrationSettingSecretStored, $"{c.A}:{SecretKey}");
        ConnectionSqlHarness.Detail(stored.Single(), "change").Should().Be("created");
    }

    [Theory]
    [MemberData(nameof(PlatformReplaceMatrix))]
    public async Task A_replace_over_a_reference_that_is_not_the_instance_settings_own_repoints_it_and_leaves_the_victim_unchanged(bool rcsi, Foreign kind)
    {
        var c = await NewCaseAsync(rcsi);
        var foreign = kind switch
        {
            Foreign.OtherIntegration => await SeedOwnedAsync(c.Scratch, c.A, SmtpKey, ForeignMaterial),
            Foreign.OtherProviderSameName => await ConnectionSqlHarness.SeedSecretAsync(c.Scratch, "platform-connection", SettingSecretOwnership.ForPlatformSetting(SmtpKey).Name, ForeignMaterial),
            Foreign.CaseVariant => await ConnectionSqlHarness.SeedSecretAsync(c.Scratch, SettingSecretOwnership.PlatformProvider, SettingSecretOwnership.ForPlatformSetting(SmtpKey).Name.ToUpperInvariant(), ForeignMaterial),
            _ => await SeedForeignAsync(c.Scratch, kind, c.A, SmtpKey),
        };
        await InsertInstanceRowAsync(c.Scratch, SmtpKey, foreign);
        var before = await SnapshotAsync(c.Scratch, foreign);

        await ReplaceInstanceAsync(c.Scratch, c.Principal, Sentinel);

        var repointed = (await InstanceRefAsync(c.Scratch, SmtpKey))!;
        repointed.Should().NotBe(foreign);
        (await DescriptorAsync(c.Scratch, repointed)).Should().Be(("platform-setting", $"platform-setting:{SmtpKey}"));
        (await SnapshotAsync(c.Scratch, foreign)).Should().Be(before);
    }

    // ---- A3.3: a clear or reset never disables a secret the setting does not own ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_clear_by_B_never_disables_integration_A_s_secret_for_the_same_key_even_when_B_s_row_names_it(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, "a-value");
        var aRef = (await IntegrationRefAsync(c.Scratch, c.A, SecretKey))!;
        await InsertIntegrationRowAsync(c.Scratch, c.B, SecretKey, aRef);
        var before = await SnapshotAsync(c.Scratch, aRef);

        await ClearIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.B);

        (await IntegrationRefAsync(c.Scratch, c.B, SecretKey)).Should().BeNull("B's override is gone");
        (await SnapshotAsync(c.Scratch, aRef)).Should().Be(before, "A's credential is untouched");
        (await StateAsync(c.Scratch, aRef)).Should().Be(SecretState.Active);
        (await AuditRowsAsync(c.Scratch, AuditActionNames.IntegrationSettingSecretRetired, $"{c.B}:{SecretKey}")).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_reset_by_B_never_disables_integration_A_s_secret_for_the_same_key_even_when_B_s_row_names_it(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, "a-value");
        var aRef = (await IntegrationRefAsync(c.Scratch, c.A, SecretKey))!;
        await InsertIntegrationRowAsync(c.Scratch, c.B, SecretKey, aRef);
        var before = await SnapshotAsync(c.Scratch, aRef);

        await ResetIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.B);

        (await IntegrationRefAsync(c.Scratch, c.B, SecretKey)).Should().BeNull();
        (await SnapshotAsync(c.Scratch, aRef)).Should().Be(before);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_clear_or_reset_of_the_settings_own_secret_still_retires_it_and_records_why(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, "a-value");
        var aRef = (await IntegrationRefAsync(c.Scratch, c.A, SecretKey))!;
        await ClearIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A);
        (await StateAsync(c.Scratch, aRef)).Should().Be(SecretState.Disabled);

        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.B, "b-value");
        var bRef = (await IntegrationRefAsync(c.Scratch, c.B, SecretKey))!;
        await ResetIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.B);
        (await StateAsync(c.Scratch, bRef)).Should().Be(SecretState.Disabled);

        var reasons = (await AuditRowsAsync(c.Scratch, AuditActionNames.IntegrationSettingSecretRetired))
            .Where(r => ((string)r["TargetId"]!).StartsWith(c.A) || ((string)r["TargetId"]!).StartsWith(c.B))
            .Select(r => ConnectionSqlHarness.Detail(r, "reason"));
        reasons.Should().BeEquivalentTo("cleared", "reset");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_instance_clear_or_reset_never_disables_a_secret_it_does_not_own(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        var integrationSecret = await SeedOwnedAsync(c.Scratch, c.A, SmtpKey, ForeignMaterial);
        await InsertInstanceRowAsync(c.Scratch, SmtpKey, integrationSecret);
        var before = await SnapshotAsync(c.Scratch, integrationSecret);

        await ClearInstanceAsync(c.Scratch, c.Principal);
        (await SnapshotAsync(c.Scratch, integrationSecret)).Should().Be(before);

        await InsertInstanceRowAsync(c.Scratch, SmtpKey, integrationSecret);
        await ResetInstanceAsync(c.Scratch, c.Principal);
        (await SnapshotAsync(c.Scratch, integrationSecret)).Should().Be(before);
        (await InstanceRefAsync(c.Scratch, SmtpKey)).Should().BeNull();
    }

    // ---- A3.4: the runtime accessor and the SMTP resolver refuse a reference that is not owned ----

    private sealed class Reading(Resolution Resolution, IntegrationSettingsAccessor Accessor, IDisposable Scope) : IAsyncDisposable
    {
        public Resolution Resolution { get; } = Resolution;
        public IntegrationSettingsAccessor Accessor { get; } = Accessor;

        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Resolution.DisposeAsync();
        }
    }

    /// <summary>The runtime accessor as the Worker Host composes it: the setting-secret boundary over the real store, with a recording protector and a logged command stream.</summary>
    private static Reading Read(AuditSqlScratch scratch, string integrationId, Registry registry, IAuditEventWriter? writer = null)
    {
        var resolution = ConnectionSecretOwnershipSupport.NewResolution(scratch);
        var scope = new IntegrationExecutionScopeAccessor();
        var settingsDb = scratch.NewDb();
        var boundary = new SettingSecretResolver(
            resolution.Composite,
            writer ?? new SqlAuditEventWriter(scratch.Options(), TimeProvider.System, null, null),
            NullLogger<SettingSecretResolver>.Instance,
            new SqlIntegrationRepository(settingsDb));
        var accessor = new IntegrationSettingsAccessor(
            scope,
            new RuntimeIntegrationSettingsResolver(registry, new SqlPlatformSettingValueRepository(settingsDb)),
            boundary);
        return new Reading(resolution, accessor, scope.BeginScope("Sample.Mod", integrationId));
    }

    [Theory]
    [MemberData(nameof(ReadMatrix))]
    public async Task The_runtime_accessor_refuses_a_reference_that_is_not_owned_before_any_foreign_payload_is_read_and_the_module_sees_not_configured(bool rcsi, Foreign kind)
    {
        var c = await NewCaseAsync(rcsi);
        var foreign = await SeedForeignAsync(c.Scratch, kind, c.A, SecretKey);
        await InsertIntegrationRowAsync(c.Scratch, c.A, SecretKey, foreign);
        var before = await SnapshotAsync(c.Scratch, foreign);
        await using var reading = Read(c.Scratch, c.A, c.Registry);

        var act = () => reading.Accessor.GetSecretAsync(SecretKey).AsTask();

        var thrown = (await act.Should().ThrowAsync<IntegrationConfigurationException>()).Which;
        thrown.Code.Should().Be("missing_secret", "the module sees the approved not-configured behaviour");
        thrown.ToString().Should().NotContain(foreign).And.NotContain(ForeignMaterial);
        reading.Resolution.Protector.UnprotectedWithKeyId.Should().BeEmpty("a foreign payload is never unprotected");
        reading.Resolution.Log.Commands.Should().NotContain(
            sql => sql.Contains("SecretVersions", StringComparison.Ordinal),
            "the descriptor check refuses before the version row is read");
        (await SnapshotAsync(c.Scratch, foreign)).Should().Be(before);

        var rows = await AuditRowsAsync(c.Scratch, AuditActionNames.IntegrationSettingSecretResolutionRefused, $"{c.A}:{SecretKey}");
        var row = rows.Should().ContainSingle("one top-level operation writes one row").Which;
        row["Category"].Should().Be(AuditCategories.Secrets);
        row["Severity"].Should().Be(AuditSeverities.Elevated);
        row["Outcome"].Should().Be(AuditOutcomeCodes.Rejected);
        row["TargetType"].Should().Be(AuditTargetTypes.IntegrationSetting);
        row["Actor"].Should().Be(AuditActor.Worker.RecordedValue);
        ConnectionSqlHarness.Detail(row, "path").Should().Be("run");
        row["ResourceContextId"].Should().Be("shared");
        AssertNoSecretMaterial(row, foreign, ForeignMaterial, "integration-setting:", "platform-setting:", "platform-connection");
    }

    public static IEnumerable<object[]> ReadMatrix() => IsolationByForeign([.. ForeignWithMaterial, Foreign.Dangling]);

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task The_runtime_accessor_returns_a_secret_the_setting_owns_and_writes_no_refusal_row(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, OwnMaterial);
        await using var reading = Read(c.Scratch, c.A, c.Registry);

        (await reading.Accessor.GetSecretAsync(SecretKey)).Should().Be(OwnMaterial);

        reading.Resolution.Protector.UnprotectedWithKeyId.Should().ContainSingle();
        (await AuditRowsAsync(c.Scratch, AuditActionNames.IntegrationSettingSecretResolutionRefused, $"{c.A}:{SecretKey}")).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_refusal_stands_and_nothing_foreign_is_read_when_the_evidence_cannot_be_written(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        var foreign = await SeedForeignAsync(c.Scratch, Foreign.OtherIntegration, c.A, SecretKey);
        await InsertIntegrationRowAsync(c.Scratch, c.A, SecretKey, foreign);
        await using var reading = Read(c.Scratch, c.A, c.Registry, new ThrowingWriter());

        var act = () => reading.Accessor.GetSecretAsync(SecretKey).AsTask();

        (await act.Should().ThrowAsync<IntegrationConfigurationException>()).Which.Code.Should().Be("missing_secret");
        reading.Resolution.Protector.UnprotectedWithKeyId.Should().BeEmpty();
        (await AuditRowsAsync(c.Scratch, AuditActionNames.IntegrationSettingSecretResolutionRefused, $"{c.A}:{SecretKey}")).Should().BeEmpty();
    }

    private sealed class ThrowingWriter : IAuditEventWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("audit store is down"));

        public Task<bool> TryWriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default) =>
            Task.FromException<bool>(new InvalidOperationException("audit store is down"));
    }

    // ---- SMTP: a row-sourced reference is owned; a deployment-configured one is trusted (P-22) ----

    private static EmailSmtpCredentialResolver Smtp(Resolution resolution, AuditSqlScratch scratch, IPrincipalContext? principal) =>
        new(
            NullLogger<EmailSmtpCredentialResolver>.Instance,
            new SettingSecretResolver(
                resolution.Composite,
                new SqlAuditEventWriter(scratch.Options(), TimeProvider.System, null, principal),
                NullLogger<SettingSecretResolver>.Instance,
                null,
                principal));

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_row_sourced_smtp_reference_to_a_foreign_secret_reads_as_no_password_and_the_foreign_payload_is_never_read(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        var foreign = await SeedOwnedAsync(c.Scratch, c.A, SmtpKey, ForeignMaterial);
        var before = await SnapshotAsync(c.Scratch, foreign);
        var refusals = (await AuditRowsAsync(c.Scratch, AuditActionNames.PlatformSettingSecretResolutionRefused)).Count;
        await using var res = ConnectionSecretOwnershipSupport.NewResolution(c.Scratch);

        var password = await Smtp(res, c.Scratch, c.Principal).ResolvePasswordAsync(
            new EmailNotificationOptions { SmtpPasswordRef = foreign, SmtpPasswordRefSource = SmtpPasswordRefSource.SettingRow },
            SettingSecretResolutionPath.Management);

        password.Should().BeNull();
        res.Protector.UnprotectedWithKeyId.Should().BeEmpty();
        (await SnapshotAsync(c.Scratch, foreign)).Should().Be(before);
        var rows = await AuditRowsAsync(c.Scratch, AuditActionNames.PlatformSettingSecretResolutionRefused);
        rows.Should().HaveCount(refusals + 1);
        var row = rows[^1];
        row["TargetType"].Should().Be(AuditTargetTypes.PlatformSetting);
        row["TargetId"].Should().Be(SmtpKey);
        row["Actor"].Should().Be(c.Admin, "a management refusal is attributed to the signed-in operator");
        ConnectionSqlHarness.Detail(row, "path").Should().Be("management");
        AssertNoSecretMaterial(row, foreign, ForeignMaterial, "integration-setting:");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_row_sourced_smtp_reference_to_the_instance_settings_own_secret_resolves(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        await ReplaceInstanceAsync(c.Scratch, c.Principal, "smtp-pass");
        var own = (await InstanceRefAsync(c.Scratch, SmtpKey))!;
        await using var res = ConnectionSecretOwnershipSupport.NewResolution(c.Scratch);

        var password = await Smtp(res, c.Scratch, null).ResolvePasswordAsync(
            new EmailNotificationOptions { SmtpPasswordRef = own, SmtpPasswordRefSource = SmtpPasswordRefSource.SettingRow });

        password.Should().Be("smtp-pass");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_deployment_configured_smtp_reference_is_outside_the_row_rule_and_keeps_working_whatever_secret_it_names(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        // A secret that is not the instance setting's own, supplied by deployment configuration: trusted input (P-22).
        var configured = await ConnectionSqlHarness.SeedSecretAsync(c.Scratch, "deployment-provisioned", "smtp-password-from-the-deployment", "configured-pass");
        await using var res = ConnectionSecretOwnershipSupport.NewResolution(c.Scratch);
        var refusals = (await AuditRowsAsync(c.Scratch, AuditActionNames.PlatformSettingSecretResolutionRefused)).Count;

        var password = await Smtp(res, c.Scratch, null).ResolvePasswordAsync(new EmailNotificationOptions { SmtpPasswordRef = configured });

        password.Should().Be("configured-pass");
        (await AuditRowsAsync(c.Scratch, AuditActionNames.PlatformSettingSecretResolutionRefused)).Should().HaveCount(refusals);
    }

    // ---- A3.5: a GUID stored as ordinary text does not become a secret reference when the key is reclassified ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_foreign_guid_stored_as_ordinary_text_is_refused_at_read_once_the_key_becomes_sensitive(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        c.Registry.SecretKeyIsSensitive = false;
        var foreign = await SeedForeignAsync(c.Scratch, Foreign.OtherIntegration, c.A, SecretKey);
        await InsertIntegrationRowAsync(c.Scratch, c.A, SecretKey, foreign);
        var before = await SnapshotAsync(c.Scratch, foreign);

        // While the key is ordinary the text is just a value.
        await using (var ordinary = Read(c.Scratch, c.A, c.Registry))
            ordinary.Accessor.GetOrDefault(SecretKey, "").Should().Be(foreign);

        // A module declares the key sensitive: the same text is now treated as a reference, and is refused.
        c.Registry.SecretKeyIsSensitive = true;
        await using var sensitive = Read(c.Scratch, c.A, c.Registry);
        var act = () => sensitive.Accessor.GetSecretAsync(SecretKey).AsTask();

        (await act.Should().ThrowAsync<IntegrationConfigurationException>()).Which.Code.Should().Be("missing_secret");
        sensitive.Resolution.Protector.UnprotectedWithKeyId.Should().BeEmpty();
        (await SnapshotAsync(c.Scratch, foreign)).Should().Be(before);
        (await AuditRowsAsync(c.Scratch, AuditActionNames.IntegrationSettingSecretResolutionRefused, $"{c.A}:{SecretKey}")).Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_foreign_guid_stored_as_ordinary_text_is_not_versioned_by_a_replace_after_the_key_becomes_sensitive(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        c.Registry.SecretKeyIsSensitive = false;
        var foreign = await SeedForeignAsync(c.Scratch, Foreign.OtherIntegration, c.A, SecretKey);
        await InsertIntegrationRowAsync(c.Scratch, c.A, SecretKey, foreign);
        var before = await SnapshotAsync(c.Scratch, foreign);
        c.Registry.SecretKeyIsSensitive = true;

        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, Sentinel);

        var repointed = (await IntegrationRefAsync(c.Scratch, c.A, SecretKey))!;
        repointed.Should().NotBe(foreign);
        (await DescriptorAsync(c.Scratch, repointed)).Name.Should().Be($"integration-setting:{c.A}:{SecretKey}");
        (await SnapshotAsync(c.Scratch, foreign)).Should().Be(before);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Text_that_is_not_a_guid_is_refused_at_read_after_the_key_becomes_sensitive_and_replaced_by_a_new_owned_secret(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        await InsertIntegrationRowAsync(c.Scratch, c.A, SecretKey, "an ordinary value a module once stored");
        await using (var reading = Read(c.Scratch, c.A, c.Registry))
        {
            var act = () => reading.Accessor.GetSecretAsync(SecretKey).AsTask();
            (await act.Should().ThrowAsync<IntegrationConfigurationException>()).Which.Code.Should().Be("missing_secret");
        }

        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, OwnMaterial);

        await using var after = Read(c.Scratch, c.A, c.Registry);
        (await after.Accessor.GetSecretAsync(SecretKey)).Should().Be(OwnMaterial);
    }

    // ---- A3.8: legacy references are never adopted; they are retired only on H1's evidence ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_ambiguous_legacy_reference_is_refused_at_read_and_never_adopted_or_renamed(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        var legacy = await SeedLegacyAsync(c.Scratch, SecretKey);
        await InsertIntegrationRowAsync(c.Scratch, c.A, SecretKey, legacy);
        await InsertIntegrationRowAsync(c.Scratch, c.B, SecretKey, legacy);
        var before = await SnapshotAsync(c.Scratch, legacy);
        await using var reading = Read(c.Scratch, c.A, c.Registry);

        var act = () => reading.Accessor.GetSecretAsync(SecretKey).AsTask();

        (await act.Should().ThrowAsync<IntegrationConfigurationException>()).Which.Code.Should().Be("missing_secret");
        reading.Resolution.Protector.UnprotectedWithKeyId.Should().BeEmpty("a legacy secret cannot say which integration made it, so it is not read");
        (await SnapshotAsync(c.Scratch, legacy)).Should().Be(before, "nothing renamed, adopted or modified it");
        (await AuditRowsAsync(c.Scratch, AuditActionNames.IntegrationSettingSecretResolutionRefused, $"{c.A}:{SecretKey}")).Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Re_entering_the_value_of_a_shared_legacy_row_creates_an_owned_secret_and_retains_the_legacy_one_until_the_last_holder_has_moved(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        var legacy = await SeedLegacyAsync(c.Scratch, SecretKey);
        await InsertIntegrationRowAsync(c.Scratch, c.A, SecretKey, legacy);
        await InsertIntegrationRowAsync(c.Scratch, c.B, SecretKey, legacy);
        var before = await SnapshotAsync(c.Scratch, legacy);

        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, "a-value");

        var aRef = (await IntegrationRefAsync(c.Scratch, c.A, SecretKey))!;
        aRef.Should().NotBe(legacy);
        (await DescriptorAsync(c.Scratch, aRef)).Name.Should().Be($"integration-setting:{c.A}:{SecretKey}");
        (await SnapshotAsync(c.Scratch, legacy)).Should().Be(before, "B still holds it: it is retained, untouched");
        (await IntegrationRefAsync(c.Scratch, c.B, SecretKey)).Should().Be(legacy);

        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.B, "b-value");

        (await StateAsync(c.Scratch, legacy)).Should().Be(SecretState.Disabled, "B was the last holder, so H1's sole-holder test now passes");
        var retired = (await AuditRowsAsync(c.Scratch, AuditActionNames.IntegrationSettingSecretRetired, $"{c.B}:{SecretKey}")).Single();
        ConnectionSqlHarness.Detail(retired, "reason").Should().Be("replaced");
        (await AuditRowsAsync(c.Scratch, AuditActionNames.IntegrationSettingSecretRetired, $"{c.A}:{SecretKey}")).Should().BeEmpty("A's re-entry retired nothing");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Re_entering_the_value_of_a_sole_holder_legacy_row_retires_the_legacy_secret(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        var legacy = await SeedLegacyAsync(c.Scratch, SecretKey);
        await InsertIntegrationRowAsync(c.Scratch, c.A, SecretKey, legacy);

        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, "a-value");

        (await StateAsync(c.Scratch, legacy)).Should().Be(SecretState.Disabled);
        (await IntegrationRefAsync(c.Scratch, c.A, SecretKey)).Should().NotBe(legacy);
        await using var reading = Read(c.Scratch, c.A, c.Registry);
        (await reading.Accessor.GetSecretAsync(SecretKey)).Should().Be("a-value");
    }

    public static IEnumerable<object[]> LegacyOtherHolders() =>
        from rcsi in new[] { false, true }
        from holder in new[] { "instance setting", "connection", "entra registration" }
        select new object[] { rcsi, holder };

    [Theory]
    [MemberData(nameof(LegacyOtherHolders))]
    public async Task A_legacy_secret_that_any_other_holder_still_refers_to_is_never_retired_by_a_re_entry(bool rcsi, string holder)
    {
        var c = await NewCaseAsync(rcsi);
        var legacy = await SeedLegacyAsync(c.Scratch, SecretKey);
        await InsertIntegrationRowAsync(c.Scratch, c.A, SecretKey, legacy);
        switch (holder)
        {
            case "instance setting":
                await InsertInstanceRowAsync(c.Scratch, SmtpKey, legacy);
                break;
            case "connection":
                await InsertConnectionAsync(c.Scratch, $"con-{c.A}", $$"""{"Password":"{{legacy}}"}""");
                break;
            default:
                await InsertEntraAsync(c.Scratch, $"reg-{c.A}", legacy);
                break;
        }

        var before = await SnapshotAsync(c.Scratch, legacy);
        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, "a-value");

        (await SnapshotAsync(c.Scratch, legacy)).Should().Be(before, "something else still needs it");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Clearing_or_resetting_a_sole_holder_legacy_row_retires_the_legacy_secret_and_a_shared_one_is_retained(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        var sole = await SeedLegacyAsync(c.Scratch, SecretKey);
        var shared = await SeedLegacyAsync(c.Scratch, OtherSecretKey);
        await InsertIntegrationRowAsync(c.Scratch, c.A, SecretKey, sole);
        await InsertIntegrationRowAsync(c.Scratch, c.A, OtherSecretKey, shared);
        await InsertIntegrationRowAsync(c.Scratch, c.B, OtherSecretKey, shared);
        var sharedBefore = await SnapshotAsync(c.Scratch, shared);

        await ClearIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A);
        await ResetIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, OtherSecretKey);

        (await StateAsync(c.Scratch, sole)).Should().Be(SecretState.Disabled);
        (await SnapshotAsync(c.Scratch, shared)).Should().Be(sharedBefore, "B still holds the shared legacy secret");
        (await IntegrationRefAsync(c.Scratch, c.B, OtherSecretKey)).Should().Be(shared);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_legacy_secret_named_for_another_key_is_never_retired_by_a_clear_or_a_re_entry(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        var otherKeyLegacy = await SeedLegacyAsync(c.Scratch, OtherSecretKey);
        await InsertIntegrationRowAsync(c.Scratch, c.A, SecretKey, otherKeyLegacy);
        var before = await SnapshotAsync(c.Scratch, otherKeyLegacy);

        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, "a-value");
        (await SnapshotAsync(c.Scratch, otherKeyLegacy)).Should().Be(before);

        await InsertIntegrationRowAsync(c.Scratch, c.B, SecretKey, otherKeyLegacy);
        await ClearIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.B);
        (await SnapshotAsync(c.Scratch, otherKeyLegacy)).Should().Be(before);
    }

    // ---- concurrency: the two operations of different holders do not interfere ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Two_integrations_that_replace_the_same_key_at_once_each_end_with_a_secret_of_their_own(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);

        await Task.WhenAll(
            ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, "a-value"),
            ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.B, "b-value"));

        var a = (await IntegrationRefAsync(c.Scratch, c.A, SecretKey))!;
        var b = (await IntegrationRefAsync(c.Scratch, c.B, SecretKey))!;
        a.Should().NotBe(b);
        await using var res = ConnectionSecretOwnershipSupport.NewResolution(c.Scratch);
        (await res.Composite.ResolveOwnedAsync(new SecretRef(a), SettingSecretOwnership.ForIntegrationSetting(c.A, SecretKey))).Material.OpaqueString.Should().Be("a-value");
        (await res.Composite.ResolveOwnedAsync(new SecretRef(b), SettingSecretOwnership.ForIntegrationSetting(c.B, SecretKey))).Material.OpaqueString.Should().Be("b-value");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_clear_by_B_that_races_A_re_entering_its_value_never_disables_A_s_secret(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        await ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, "a-one");
        var aRef = (await IntegrationRefAsync(c.Scratch, c.A, SecretKey))!;
        await InsertIntegrationRowAsync(c.Scratch, c.B, SecretKey, aRef);

        await Task.WhenAll(
            ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, "a-two"),
            ClearIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.B));

        (await IntegrationRefAsync(c.Scratch, c.B, SecretKey)).Should().BeNull();
        (await IntegrationRefAsync(c.Scratch, c.A, SecretKey)).Should().Be(aRef);
        (await StateAsync(c.Scratch, aRef)).Should().Be(SecretState.Active);
        await using var res = ConnectionSecretOwnershipSupport.NewResolution(c.Scratch);
        (await res.Composite.ResolveOwnedAsync(new SecretRef(aRef), SettingSecretOwnership.ForIntegrationSetting(c.A, SecretKey))).Material.OpaqueString.Should().Be("a-two");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Two_integrations_re_entering_a_shared_legacy_secret_at_once_both_get_owned_secrets_and_the_legacy_one_is_never_disabled_while_a_row_still_names_it(bool rcsi)
    {
        var c = await NewCaseAsync(rcsi);
        var legacy = await SeedLegacyAsync(c.Scratch, SecretKey);
        await InsertIntegrationRowAsync(c.Scratch, c.A, SecretKey, legacy);
        await InsertIntegrationRowAsync(c.Scratch, c.B, SecretKey, legacy);

        await Task.WhenAll(
            ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.A, "a-value"),
            ReplaceIntegrationAsync(c.Scratch, c.Principal, c.Registry, c.B, "b-value"));

        var a = (await IntegrationRefAsync(c.Scratch, c.A, SecretKey))!;
        var b = (await IntegrationRefAsync(c.Scratch, c.B, SecretKey))!;
        new[] { a, b, legacy }.Distinct().Should().HaveCount(3);
        (await DescriptorAsync(c.Scratch, a)).Name.Should().Be($"integration-setting:{c.A}:{SecretKey}");
        (await DescriptorAsync(c.Scratch, b)).Name.Should().Be($"integration-setting:{c.B}:{SecretKey}");
        // Nothing names the legacy secret any more; whether it was retired depends on whether the two scans saw each other's
        // commit, and either outcome is safe. What must never happen is a disabled secret that a row still names, and no row does.
        (await c.Scratch.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM PlatformSettingValues WHERE ValueJson LIKE N'%{legacy}%';")).Should().Be(0);
    }

    // ---- the evidence holds no secret material ----

    private static void AssertNoSecretMaterial(Dictionary<string, object?> row, params string[] forbidden)
    {
        foreach (var text in row.Values.OfType<string>())
        {
            foreach (var value in forbidden)
                text.Should().NotContain(value);
        }
    }
}
