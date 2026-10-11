using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Core.Security.Secrets.Providers;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 3, work package 4a (owner ruling H1, refined conservative option B) against SQL Server: the persisted override
/// inventory, the reference inventory over the three known locations, the settings-secret classifier and the transactional
/// retirement. Each case runs on a catalog with the default locking READ COMMITTED and on one with READ_COMMITTED_SNAPSHOT on.
/// Fails, never skips, without SQL Server.
/// </summary>
/// <remarks>
/// The integration delete itself is work package 4b. The harness here (<see cref="RunDeleteAsync"/>) stands in for it only as far
/// as the primitives need: inside a real audited unit it classifies, refuses on an ambiguous plan, removes every override, stages
/// <c>integration.deleted</c> and retires what the plan allows. It is not the delete: it takes no integration lock, counts no
/// blocker and removes no integration.
/// </remarks>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed partial class IntegrationSettingSecretDispositionSqlServerTests : IClassFixture<IntegrationSettingSecretDispositionSqlServerTests.Catalogs>
{
    private const string CredentialKey = "Sample.Credential";
    private const string OtherKey = "Sample.Other.Credential";

    /// <summary>The plaintext every secret holds. It must never leave the store's own payload column.</summary>
    private static readonly string Sentinel = "sentinel-h1-secret-" + Guid.NewGuid().ToString("N");

    private readonly Catalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public IntegrationSettingSecretDispositionSqlServerTests(Catalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    public static IEnumerable<object[]> Isolation() => [[false], [true]];

    public static IEnumerable<object[]> GuidFormatsBySurvivor() =>
        from rcsi in new[] { false, true }
        from kind in Enum.GetValues<SurvivorKind>()
        from format in new[] { "D", "N", "B", "P", "X" }
        // The X format is longer than the Entra column (64 characters), so no registration can hold one.
        where !(kind == SurvivorKind.EntraRegistration && format == "X")
        select new object[] { rcsi, kind, format };

    public enum SurvivorKind
    {
        OtherIntegrationSetting,
        PlatformSetting,
        Connection,
        EntraRegistration,
    }

    // ---- A to C: every override is found and removed, whatever its definition now says ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_historical_override_with_no_credential_is_listed_and_removed_and_involves_no_secret(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        await InsertOverrideAsync(scratch, integration, "Sample.Ordinary", "\"just a value\"");
        var registry = new Registry().Ordinary("Sample.Ordinary");

        var run = await RunDeleteAsync(scratch, integration, registry);

        run.Plan.Overrides.Select(o => o.Key).Should().Equal("Sample.Ordinary");
        run.Plan.Dispositions.Single().Kind.Should().Be(IntegrationSecretDispositionKind.NoApplicableSecret);
        run.Plan.Dispositions.Single().NoSecretReason.Should().Be(IntegrationSecretNoSecretReason.NoReference);
        run.Retired.Should().Be(0);
        (await OverrideCountAsync(scratch, integration)).Should().Be(0);
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationDeleted)).Should().ContainSingle();
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationSettingSecretRetired)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_override_whose_definition_no_longer_exists_is_still_found_and_its_owned_secret_is_still_retired(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));

        var run = await RunDeleteAsync(scratch, integration, new Registry());

        // The row is found by the integration that owns it, not by a definition.
        run.Plan.Overrides.Select(o => o.Key).Should().Equal(CredentialKey);
        run.Plan.Dispositions.Single().Kind.Should().Be(IntegrationSecretDispositionKind.RetireAfterOverrideRemoval,
            "independent provenance in the store identifies the secret without a definition");
        run.Retired.Should().Be(1);
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Disabled);
        (await OverrideCountAsync(scratch, integration)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_override_whose_definition_is_no_longer_integration_scoped_is_still_found_and_classified(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        var registry = new Registry().PlatformOnlyOrdinary(CredentialKey);

        var run = await RunDeleteAsync(scratch, integration, registry);

        run.Plan.Dispositions.Single().Kind.Should().Be(IntegrationSecretDispositionKind.RetireAfterOverrideRemoval,
            "a reclassified setting neither hides the credential nor changes the evidence that it is owned");
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Disabled);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Overrides_with_no_definition_a_reclassified_definition_and_a_current_one_are_all_removed_and_counted(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        await InsertOverrideAsync(scratch, integration, "Sample.Removed", "\"a\"");
        await InsertOverrideAsync(scratch, integration, "Sample.Reclassified", "\"b\"");
        await InsertOverrideAsync(scratch, integration, "Sample.Current", "\"c\"");
        var registry = new Registry().PlatformOnlyOrdinary("Sample.Reclassified").Ordinary("Sample.Current");

        var run = await RunDeleteAsync(scratch, integration, registry);

        run.Plan.Overrides.Select(o => o.Key).Should().Equal("Sample.Current", "Sample.Reclassified", "Sample.Removed");
        run.Removed.Should().Be(3);
        (await OverrideCountAsync(scratch, integration)).Should().Be(0);
        var details = Details((await RowsAsync(scratch, integration, AuditActionNames.IntegrationDeleted)).Single());
        details.GetProperty("settingsRemoved").GetInt32().Should().Be(3);
    }

    // ---- D and O: an owned secret with no surviving reference is retired atomically, with its rows ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_owned_secret_with_a_matching_key_and_no_surviving_reference_is_retired_with_the_deletes_rows_in_one_transaction(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        var registry = new Registry().Credential(CredentialKey);
        var (admin, _) = await AdminAsync(scratch);
        var storeEvidenceBefore = await StoreEvidenceCountAsync(scratch, secret);

        var run = await RunDeleteAsync(scratch, integration, registry, admin: admin);

        run.Plan.Dispositions.Single().Kind.Should().Be(IntegrationSecretDispositionKind.RetireAfterOverrideRemoval);
        run.Retired.Should().Be(1);
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Disabled);
        (await StoreEventsAsync(scratch, secret)).Should().EndWith("Disable");
        (await StoreEvidenceCountAsync(scratch, secret)).Should().Be(storeEvidenceBefore + 1);

        // O: the integration.deleted row and the Secrets-category row, both secondary rows of one operation.
        var deleted = (await RowsAsync(scratch, integration, AuditActionNames.IntegrationDeleted)).Single();
        deleted["Category"].Should().Be(AuditCategories.Configuration);
        deleted["TargetType"].Should().Be(AuditTargetTypes.Integration);
        deleted["TargetId"].Should().Be(integration);
        deleted["Outcome"].Should().Be(AuditOutcomeCodes.Succeeded);
        deleted["ResourceContextId"].Should().Be("shared");
        deleted["ModuleId"].Should().Be("Sample.Mod");
        deleted["OperationEventId"].Should().Be(deleted["EventId"], "the primary row of its operation");
        deleted["Actor"].Should().Be(admin);
        var deletedDetails = Details(deleted);
        deletedDetails.EnumerateObject().Select(p => p.Name).Should().Equal("name", "moduleId", "settingsRemoved");
        deletedDetails.GetProperty("settingsRemoved").GetInt32().Should().Be(1);

        var retired = (await RowsAsync(scratch, integration, AuditActionNames.IntegrationSettingSecretRetired)).Single();
        retired["Category"].Should().Be(AuditCategories.Secrets);
        retired["TargetType"].Should().Be(AuditTargetTypes.IntegrationSetting);
        retired["TargetId"].Should().Be($"{integration}:{CredentialKey}");
        retired["OperationEventId"].Should().Be(deleted["OperationEventId"]);
        retired["EventId"].Should().NotBe(retired["OperationEventId"]);
        Details(retired).GetProperty("reason").GetString().Should().Be("integration_deleted");

        await AssertNoSecretMaterialAsync(scratch, secret);
    }

    // ---- E to H: a surviving reference of any known kind retains the secret ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_secret_still_referenced_by_another_integrations_setting_is_retained(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        var other = NewIntegrationId();
        await InsertOverrideAsync(scratch, other, CredentialKey, JsonSerializer.Serialize(secret.ToString("N")));

        var run = await RunDeleteAsync(scratch, integration, new Registry().Credential(CredentialKey));

        AssertRetained(run, SurvivingSecretReferenceKind.OtherIntegrationSetting);
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Active);
        (await OverrideCountAsync(scratch, other)).Should().Be(1, "the other integration's override is untouched");
        (await OverrideCountAsync(scratch, integration)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_secret_referenced_by_a_platform_setting_value_is_retained(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        await InsertSettingAsync(scratch, PlatformSettingScopeConstants.InstanceKind, "inst-" + NewSuffix(), "Sample.Platform", JsonSerializer.Serialize(secret.ToString("B")));

        var run = await RunDeleteAsync(scratch, integration, new Registry().Credential(CredentialKey));

        AssertRetained(run, SurvivingSecretReferenceKind.PlatformSetting);
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Active);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_secret_referenced_by_a_connections_secret_refs_object_is_retained(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        await InsertConnectionAsync(scratch, "conn-" + NewSuffix(), $$"""{"Password":"{{secret.ToString("N").ToUpperInvariant()}}","ApiKey":"{{Guid.NewGuid()}}"}""");

        var run = await RunDeleteAsync(scratch, integration, new Registry().Credential(CredentialKey));

        AssertRetained(run, SurvivingSecretReferenceKind.Connection);
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Active);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_secret_referenced_by_an_entra_registrations_client_secret_is_retained(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        await InsertEntraAsync(scratch, "entra-" + NewSuffix(), secret.ToString("P"));

        var run = await RunDeleteAsync(scratch, integration, new Registry().Credential(CredentialKey));

        AssertRetained(run, SurvivingSecretReferenceKind.EntraRegistration);
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Active);
    }

    // ---- I: references are compared as GUIDs, not as text ----

    [Theory]
    [MemberData(nameof(GuidFormatsBySurvivor))]
    public async Task A_surviving_reference_matches_by_guid_in_every_format_and_case_whatever_format_the_override_used(
        bool rcsi, SurvivorKind kind, string format)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        // The override says it one way, the survivor another, in upper case: no text comparison can match them.
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize("  " + secret.ToString("N").ToUpperInvariant() + "  "));
        var reference = secret.ToString(format).ToUpperInvariant().Replace("0X", "0x", StringComparison.Ordinal);
        switch (kind)
        {
            case SurvivorKind.OtherIntegrationSetting:
                await InsertOverrideAsync(scratch, NewIntegrationId(), CredentialKey, JsonSerializer.Serialize(reference));
                break;
            case SurvivorKind.PlatformSetting:
                await InsertSettingAsync(scratch, PlatformSettingScopeConstants.InstanceKind, "inst-" + NewSuffix(), "Sample.Platform", JsonSerializer.Serialize(reference));
                break;
            case SurvivorKind.Connection:
                await InsertConnectionAsync(scratch, "conn-" + NewSuffix(), JsonSerializer.Serialize(new Dictionary<string, string> { ["Password"] = reference }));
                break;
            default:
                await InsertEntraAsync(scratch, "entra-" + NewSuffix(), reference);
                break;
        }

        var run = await RunDeleteAsync(scratch, integration, new Registry().Credential(CredentialKey));

        run.Plan.Dispositions.Single().Kind.Should().Be(IntegrationSecretDispositionKind.RetainSharedReference, $"{kind} in format {format}");
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Active);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_reference_in_an_unexpected_shape_still_retains_the_secret_but_an_unrelated_malformed_value_does_not(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        // Not valid JSON, and a connection row whose references are not an object: neither is a normal value, and neither is ignored.
        await InsertConnectionAsync(scratch, "conn-" + NewSuffix(), $"[ not json {secret:N} ]");
        await InsertConnectionAsync(scratch, "conn-" + NewSuffix(), "{ this is malformed and holds nothing }");

        var run = await RunDeleteAsync(scratch, integration, new Registry().Credential(CredentialKey));

        AssertRetained(run, SurvivingSecretReferenceKind.Connection);

        var unrelated = NewIntegrationId();
        var second = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, unrelated, CredentialKey, JsonSerializer.Serialize(second.ToString("D")));
        var again = await RunDeleteAsync(scratch, unrelated, new Registry().Credential(CredentialKey));
        again.Plan.Dispositions.Single().Kind.Should().Be(IntegrationSecretDispositionKind.RetireAfterOverrideRemoval,
            "malformed values that cannot mention the secret do not block its retirement");
    }

    // ---- J: several overrides, one secret, one disposition ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Two_overrides_naming_one_secret_in_different_formats_share_one_ambiguous_disposition_and_nothing_is_retired(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        await InsertOverrideAsync(scratch, integration, OtherKey, JsonSerializer.Serialize(secret.ToString("N").ToUpperInvariant()));
        var registry = new Registry().Credential(CredentialKey).Credential(OtherKey);

        var plan = await ClassifyAsync(scratch, integration, registry);

        plan.IsRefused.Should().BeTrue();
        plan.Dispositions.Should().OnlyContain(d => d.Kind == IntegrationSecretDispositionKind.Ambiguous
            && d.AmbiguityReason == IntegrationSecretAmbiguityReason.SettingKeyMismatch, "one coherent disposition for the one secret");
        plan.Retirements.Should().BeEmpty();
        plan.Refusal!.SettingKeys.Should().Equal(CredentialKey, OtherKey);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task One_override_that_names_its_own_secret_twice_retires_it_exactly_once(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        // A historical value that is no longer a bare text: an array repeating the reference in two formats.
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(new[] { secret.ToString("D"), secret.ToString("N") }));

        var run = await RunDeleteAsync(scratch, integration, new Registry().Ordinary(CredentialKey));

        run.Plan.Retirements.Should().ContainSingle();
        run.Retired.Should().Be(1);
        (await StoreEventsAsync(scratch, secret)).Count(e => e == "Disable").Should().Be(1);
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationSettingSecretRetired)).Should().ContainSingle();
    }

    // ---- K and L: ambiguity refuses the whole delete ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_settings_provisioned_secret_with_no_setting_key_in_its_name_refuses_the_delete(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, "legacy-credential");
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));

        var plan = await ClassifyAsync(scratch, integration, new Registry().Credential(CredentialKey));

        plan.IsRefused.Should().BeTrue();
        plan.Dispositions.Single().AmbiguityReason.Should().Be(IntegrationSecretAmbiguityReason.OwnershipNotEstablished);
        plan.Retirements.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_secret_provisioned_for_another_setting_key_is_not_retired_and_refuses_the_delete(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, SettingSecretName(OtherKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));

        var plan = await ClassifyAsync(scratch, integration, new Registry().Credential(CredentialKey));

        plan.IsRefused.Should().BeTrue();
        plan.Dispositions.Single().AmbiguityReason.Should().Be(IntegrationSecretAmbiguityReason.SettingKeyMismatch);
        plan.Retirements.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_credential_setting_whose_stored_value_is_not_a_readable_reference_refuses_the_delete_but_an_ordinary_one_does_not(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        await InsertOverrideAsync(scratch, integration, CredentialKey, "\"not-a-guid\"");
        await InsertOverrideAsync(scratch, integration, "Sample.Ordinary", "\"not-a-guid\"");

        var plan = await ClassifyAsync(scratch, integration, new Registry().Credential(CredentialKey).Ordinary("Sample.Ordinary"));

        plan.Refusal!.SettingKeys.Should().Equal(CredentialKey);
        plan.Refusal.Reasons.Should().Equal(IntegrationSecretAmbiguityReason.UnreadableReference);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_surviving_reference_decides_the_disposition_even_when_ownership_is_unclear_because_leaving_the_secret_alone_is_safe(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, "legacy-credential");
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        await InsertConnectionAsync(scratch, "conn-" + NewSuffix(), JsonSerializer.Serialize(new Dictionary<string, string> { ["Password"] = secret.ToString("D") }));

        var run = await RunDeleteAsync(scratch, integration, new Registry().Credential(CredentialKey));

        AssertRetained(run, SurvivingSecretReferenceKind.Connection);
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Active);
    }

    // ---- the cases with no applicable usable secret ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_secret_of_another_provider_a_dangling_reference_and_an_already_disabled_secret_are_never_retired(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var foreign = await CreateSecretAsync(scratch, "connection:c1:ApiKey", provider: "platform-connection");
        var disabled = await CreateSecretAsync(scratch, SettingSecretName("Sample.Disabled"));
        await DisableSecretAsync(scratch, disabled);
        await InsertOverrideAsync(scratch, integration, "Sample.Foreign", JsonSerializer.Serialize(foreign.ToString("D")));
        await InsertOverrideAsync(scratch, integration, "Sample.Dangling", JsonSerializer.Serialize(Guid.NewGuid().ToString("D")));
        await InsertOverrideAsync(scratch, integration, "Sample.Disabled", JsonSerializer.Serialize(disabled.ToString("D")));
        var registry = new Registry().Credential("Sample.Foreign").Credential("Sample.Dangling").Credential("Sample.Disabled");

        var run = await RunDeleteAsync(scratch, integration, registry);

        run.Retired.Should().Be(0);
        run.Plan.Dispositions.ToDictionary(d => d.Key, d => d.NoSecretReason).Should().Equal(new Dictionary<string, IntegrationSecretNoSecretReason?>
        {
            ["Sample.Disabled"] = IntegrationSecretNoSecretReason.AlreadyUnusable,
            ["Sample.Dangling"] = IntegrationSecretNoSecretReason.ReferenceDoesNotResolve,
            ["Sample.Foreign"] = IntegrationSecretNoSecretReason.NotSettingsProvisioned,
        });
        (await SecretStateAsync(scratch, foreign)).Should().Be(SecretState.Active, "a secret another resource provisioned is never retired here");
        (await OverrideCountAsync(scratch, integration)).Should().Be(0);
    }

    // ---- M and N: refusal and rollback leave nothing, and say nothing about secrets ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_refused_delete_changes_nothing_not_even_the_secret_that_was_safe_and_names_no_secret(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var safe = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        var ambiguous = await CreateSecretAsync(scratch, SettingSecretName("Sample.Unrelated"));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(safe.ToString("D")));
        await InsertOverrideAsync(scratch, integration, OtherKey, JsonSerializer.Serialize(ambiguous.ToString("D")));
        var registry = new Registry().Credential(CredentialKey).Credential(OtherKey);
        var before = await StateAsync(scratch, integration, safe, ambiguous);

        var act = () => RunDeleteAsync(scratch, integration, registry);

        var thrown = (await act.Should().ThrowAsync<IntegrationSettingDispositionRefusedException>()).Which;
        thrown.Refusal.SettingKeys.Should().Equal(OtherKey);
        AssertNoSecretMaterial(thrown, safe.ToString("D"), ambiguous.ToString("D"), Sentinel);
        (await StateAsync(scratch, integration, safe, ambiguous)).Should().Be(before,
            "no override removed, no secret retired, no store evidence and no audit row, including for the secret that was safe");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_failure_after_the_secrets_were_retired_rolls_back_the_secrets_the_overrides_and_every_row(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        var registry = new Registry().Credential(CredentialKey);
        var before = await StateAsync(scratch, integration, secret);
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        var act = () => RunDeleteAsync(scratch, integration, registry, interceptors: [failAudit]);

        var thrown = await act.Should().ThrowAsync<Exception>();
        failAudit.Failures.Should().BeGreaterThan(0);
        AssertNoSecretMaterial(thrown.Which, secret.ToString("D"), Sentinel);
        (await StateAsync(scratch, integration, secret)).Should().Be(before, "the retirement, the override removal and the store's evidence were one transaction");
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Active);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_rolled_back_commit_leaves_the_secret_active_and_a_rerun_retires_it_once(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        var registry = new Registry().Credential(CredentialKey);
        var attempts = new AttemptCounter();

        var run = await RunDeleteAsync(scratch, integration, registry, interceptors: [new RolledBackCommit(), attempts]);

        attempts.Started.Should().Be(2, "one attempt rolled back, the unit reran once");
        run.Retired.Should().Be(1);
        (await StoreEventsAsync(scratch, secret)).Count(e => e == "Disable").Should().Be(1, "the rolled-back attempt's retirement never existed");
        (await RowsAsync(scratch, integration, AuditActionNames.IntegrationSettingSecretRetired)).Should().ContainSingle();
    }

    // ---- read-only inventory ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Classifying_is_read_only_it_tracks_nothing_saves_nothing_and_opens_no_transaction(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        await InsertConnectionAsync(scratch, "conn-" + NewSuffix(), JsonSerializer.Serialize(new Dictionary<string, string> { ["Password"] = secret.ToString("D") }));
        var before = await StateAsync(scratch, integration, secret);
        var attempts = new AttemptCounter();
        var statements = new RecordedStatements();

        await using (var db = scratch.NewDb(attempts, statements))
        {
            var store = SecretStore(db);
            var plan = await new IntegrationSettingSecretClassifier(new IntegrationSettingSecretInventory(db), store, new Registry().Credential(CredentialKey))
                .ClassifyAsync(integration);

            plan.Dispositions.Single().Kind.Should().Be(IntegrationSecretDispositionKind.RetainSharedReference);
            db.ChangeTracker.Entries().Should().BeEmpty("no entity was tracked, so nothing can be saved by accident");
            (await db.SaveChangesAsync()).Should().Be(0);
        }

        attempts.Started.Should().Be(0, "the primitives open no transaction of their own");
        statements.Texts.Should().NotBeEmpty().And.OnlyContain(t => t.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || t.TrimStart().StartsWith("SET", StringComparison.OrdinalIgnoreCase));
        (await StateAsync(scratch, integration, secret)).Should().Be(before);
    }

    // ---- what a read can and cannot promise ----

    [Fact]
    public async Task Under_locking_read_committed_a_reference_being_written_makes_the_scan_wait_but_under_snapshot_isolation_it_is_not_seen()
    {
        // The documented limit of the inventory: it is a read, not a lock. Locking READ COMMITTED waits for a writer in flight (and
        // then sees what it committed); READ_COMMITTED_SNAPSHOT reads the last committed version and does not see a writer that has
        // not committed. Neither prevents a reference written after the scan, which is why the policy accepts the limit.
        foreach (var rcsi in new[] { false, true })
        {
            var scratch = Scratch(rcsi);
            var secret = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
            var connectionId = "conn-" + NewSuffix();

            // Warm the model and the connection pool, so that a scan that has not finished is waiting on the writer and not starting.
            await using (var warm = scratch.NewDb())
                await new IntegrationSettingSecretInventory(warm).FindSurvivingReferencesAsync(NewIntegrationId(), [secret]);

            await using var writer = new SqlConnection(scratch.ConnectionString);
            await writer.OpenAsync();
            await using var transaction = (SqlTransaction)await writer.BeginTransactionAsync();
            await using (var insert = writer.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO [Connections] ([Id], [ConnectorDefinitionId], [Name], [ConfigJson], [SecretRefsJson], [ResourceContextId], [State], [LastVerificationStatus]) "
                    + "VALUES (@id, N'platform.sql', N'In flight', N'{}', @refs, N'shared', 0, 0);";
                insert.Parameters.AddWithValue("@id", connectionId);
                insert.Parameters.AddWithValue("@refs", JsonSerializer.Serialize(new Dictionary<string, string> { ["Password"] = secret.ToString("D") }));
                await insert.ExecuteNonQueryAsync();
            }

            var scan = Task.Run(async () =>
            {
                await using var db = scratch.NewDb();
                return await new IntegrationSettingSecretInventory(db).FindSurvivingReferencesAsync(NewIntegrationId(), [secret]);
            });
            var finishedWhileWriterOpen = await Task.WhenAny(scan, Task.Delay(TimeSpan.FromSeconds(4))) == scan;

            if (rcsi)
            {
                finishedWhileWriterOpen.Should().BeTrue("snapshot reads do not wait for a writer");
                (await scan).Should().BeEmpty("the uncommitted reference is invisible to a snapshot read");
            }
            else
            {
                finishedWhileWriterOpen.Should().BeFalse("a locking read waits for the writer's lock");
            }

            await transaction.CommitAsync();
            var afterCommit = await scan;
            if (!rcsi)
                afterCommit.Should().ContainKey(secret, "once the writer committed, the waiting scan reads its reference");

            await using var later = scratch.NewDb();
            (await new IntegrationSettingSecretInventory(later).FindSurvivingReferencesAsync(NewIntegrationId(), [secret]))
                .Should().ContainKey(secret, "a scan that starts after the commit sees it under either isolation");
        }
    }

    // ---- harness ----

    private sealed record DeleteRun(IntegrationSettingDeletionPlan Plan, int Removed, int Retired);

    /// <summary>The catalog for the isolation mode; every test takes one first, so each writes its execution marker.</summary>
    private AuditSqlScratch Scratch(bool rcsi)
    {
        var scratch = rcsi ? _catalogs.Rcsi : _catalogs.Locking;
        _output.WriteLine(scratch.ExecutionMarker);
        return scratch;
    }

    private static string NewSuffix() => Guid.NewGuid().ToString("N")[..10];

    private static string NewIntegrationId() => "int-h1-" + NewSuffix();

    private static string SettingSecretName(string key) => PlatformSettingSecretMaterialProvisioner.BuildSecretName(key);

    private static SqlSecretStore SecretStore(AppertureDbContext db) =>
        new(db, new PassThroughProtector(), new DefaultSecretAuditActorProvider());

    /// <summary>The classification alone, on a context with no transaction.</summary>
    private static async Task<IntegrationSettingDeletionPlan> ClassifyAsync(AuditSqlScratch scratch, string integration, IIntegrationSettingDefinitionRegistry registry)
    {
        await using var db = scratch.NewDb();
        return await new IntegrationSettingSecretClassifier(new IntegrationSettingSecretInventory(db), SecretStore(db), registry).ClassifyAsync(integration);
    }

    /// <summary>
    /// What the integration delete will do with these primitives, in a real audited unit and nothing more: classify (read-only) and
    /// refuse on an ambiguous plan, remove every override, stage <c>integration.deleted</c>, retire what the plan allows.
    /// </summary>
    private async Task<DeleteRun> RunDeleteAsync(
        AuditSqlScratch scratch,
        string integration,
        IIntegrationSettingDefinitionRegistry registry,
        string? admin = null,
        IInterceptor[]? interceptors = null)
    {
        admin ??= (await AdminAsync(scratch)).UserId;
        await using var db = scratch.NewDb(interceptors ?? []);
        var store = SecretStore(db);
        var classifier = new IntegrationSettingSecretClassifier(new IntegrationSettingSecretInventory(db), store, registry);
        var retirement = new IntegrationSettingSecretRetirement(store);

        return await NewUnit(db, new Principal(admin)).ExecuteAsync(
            new AuditedOperation(AuditActionNames.IntegrationDeleted, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var plan = await classifier.ClassifyAsync(integration, ct);
                plan.ThrowIfRefused();

                var removed = await db.PlatformSettingValues
                    .Where(v => v.ScopeKind == PlatformSettingScopeConstants.IntegrationKind && v.ScopeId == integration)
                    .ExecuteDeleteAsync(ct);
                await transaction.StageAsync(AuditEvents.IntegrationDeleted(integration, "Sample API Polling", "Sample.Mod", removed, "shared"), ct);
                var retired = await retirement.RetireAsync(transaction, plan, "shared", ct);
                return new DeleteRun(plan, removed, retired);
            },
            (_, _) => throw new InvalidOperationException("The commit of this harness was not expected to need reconstruction."));
    }

    private static void AssertRetained(DeleteRun run, SurvivingSecretReferenceKind kind)
    {
        var disposition = run.Plan.Dispositions.Single();
        disposition.Kind.Should().Be(IntegrationSecretDispositionKind.RetainSharedReference);
        disposition.SurvivingReferences.Select(r => r.Kind).Should().Contain(kind);
        run.Plan.Retirements.Should().BeEmpty();
        run.Retired.Should().Be(0);
    }

    // ---- data ----

    private static async Task<(string UserId, Principal Principal)> AdminAsync(AuditSqlScratch scratch)
    {
        var id = NewUserId();
        await scratch.AddUserAsync(id, NewUsername("admin"));
        return (id, new Principal(id));
    }

    private static async Task<Guid> CreateSecretAsync(AuditSqlScratch scratch, string name, string provider = PlatformSettingSecretMaterialProvisioner.SecretProvider)
    {
        await using var db = scratch.NewDb();
        var store = SecretStore(db);
        var descriptor = await store.CreateAsync(new SecretCreateRequest(name, provider, SecretMaterialType.OpaqueString));
        var version = await store.AddVersionAsync(descriptor.SecretId, new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: Sentinel));
        await store.ActivateVersionAsync(descriptor.SecretId, version);
        return descriptor.SecretId.Value;
    }

    private static async Task DisableSecretAsync(AuditSqlScratch scratch, Guid secret)
    {
        await using var db = scratch.NewDb();
        await SecretStore(db).DisableAsync(new SecretId(secret));
    }

    private static Task InsertOverrideAsync(AuditSqlScratch scratch, string integration, string key, string valueJson) =>
        InsertSettingAsync(scratch, PlatformSettingScopeConstants.IntegrationKind, integration, key, valueJson);

    private static async Task InsertSettingAsync(AuditSqlScratch scratch, string scopeKind, string scopeId, string key, string valueJson)
    {
        await using var connection = new SqlConnection(scratch.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO PlatformSettingValues (Id, [Key], ScopeKind, ScopeId, ValueJson, CreatedUtc, UpdatedUtc)
            VALUES (@id, @key, @kind, @scope, @value, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddWithValue("@id", $"{scopeKind}:{scopeId}:{key}");
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@kind", scopeKind);
        command.Parameters.AddWithValue("@scope", scopeId);
        command.Parameters.AddWithValue("@value", valueJson);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertConnectionAsync(AuditSqlScratch scratch, string id, string secretRefsJson)
    {
        await using var connection = new SqlConnection(scratch.ConnectionString);
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

    private static async Task InsertEntraAsync(AuditSqlScratch scratch, string id, string clientSecretRef)
    {
        await using var connection = new SqlConnection(scratch.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO EntraInstanceRegistrations
                (Id, DisplayName, Authority, ClientId, UsesDeploymentSecret, ClientSecretRef, Enabled, IsActive, CreatedUtc, UpdatedUtc,
                 OpenIdConfigurationStatus, DirectoryDiscoveryStatus, AllowMemberAccessRequests, AllowGuestAccessRequests)
            VALUES (@id, N'Registration', N'https://login.example.test/tenant', N'client', 0, @ref, 1, 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 0, 0, 0, 0);
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@ref", clientSecretRef);
        await command.ExecuteNonQueryAsync();
    }

    // ---- reading ----

    private static async Task<int> OverrideCountAsync(AuditSqlScratch scratch, string integration) =>
        await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM PlatformSettingValues WHERE ScopeKind = N'{PlatformSettingScopeConstants.IntegrationKind}' AND ScopeId = N'{integration}';");

    private static async Task<SecretState> SecretStateAsync(AuditSqlScratch scratch, Guid secret) =>
        (SecretState)await scratch.ScalarAsync<int>($"SELECT State FROM SecretDescriptors WHERE Id = '{secret}';");

    private static async Task<int> StoreEvidenceCountAsync(AuditSqlScratch scratch, Guid secret) =>
        await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretAudits WHERE SecretId = '{secret}';");

    private static async Task<List<string>> StoreEventsAsync(AuditSqlScratch scratch, Guid secret) =>
        (await scratch.RowsAsync($"SELECT EventType FROM SecretAudits WHERE SecretId = '{secret}' ORDER BY Id;"))
            .Select(r => (string)r["EventType"]!).ToList();

    /// <summary>The audit rows of one action that concern this integration (the integration or one of its settings).</summary>
    private static Task<List<Dictionary<string, object?>>> RowsAsync(AuditSqlScratch scratch, string integration, string action) =>
        scratch.RowsAsync(
            $"SELECT * FROM AuditLogs WHERE Action = N'{action}' AND (TargetId = N'{integration}' OR TargetId LIKE N'{integration}:%') ORDER BY Id;");

    private static JsonElement Details(Dictionary<string, object?> row) => JsonDocument.Parse((string)row["Details"]!).RootElement;

    /// <summary>Everything a refused or rolled-back delete must leave as it was.</summary>
    private sealed record Snapshot(int Overrides, int AuditRows, int StoreEvidence, string SecretStates, string OverrideValues);

    private static async Task<Snapshot> StateAsync(AuditSqlScratch scratch, string integration, params Guid[] secrets)
    {
        var states = new List<string>();
        foreach (var secret in secrets)
            states.Add($"{await SecretStateAsync(scratch, secret)}/{await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretVersions WHERE SecretId = '{secret}';")}");
        var values = (await scratch.RowsAsync(
                $"SELECT [Key], ValueJson, RowVersion FROM PlatformSettingValues WHERE ScopeId = N'{integration}' ORDER BY [Key];"))
            .Select(r => $"{r["Key"]}={r["ValueJson"]}@{Convert.ToBase64String((byte[])r["RowVersion"]!)}");
        return new Snapshot(
            await OverrideCountAsync(scratch, integration),
            await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;"),
            await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM SecretAudits;"),
            string.Join(",", states),
            string.Join("|", values));
    }

    // ---- confidentiality ----

    /// <summary>No audit column, no column of the store's evidence except the id it is keyed by, holds a secret or its id in either GUID form.</summary>
    private static async Task AssertNoSecretMaterialAsync(AuditSqlScratch scratch, Guid secret)
    {
        var forms = new[] { secret.ToString("D"), secret.ToString("N"), Sentinel };
        foreach (var row in await scratch.RowsAsync("SELECT * FROM AuditLogs;"))
        {
            foreach (var value in row.Values.OfType<string>())
            {
                foreach (var form in forms)
                    value.Should().NotContainEquivalentOf(form, $"audit columns never hold a secret or its reference ({row["Action"]})");
            }
        }

        foreach (var row in await scratch.RowsAsync($"SELECT EventType, Version, Actor, Outcome FROM SecretAudits WHERE SecretId = '{secret}';"))
        {
            foreach (var value in row.Values.OfType<string>())
            {
                foreach (var form in forms)
                    value.Should().NotContainEquivalentOf(form, "the store's evidence holds ids and event types only");
            }
        }
    }

    private static void AssertNoSecretMaterial(Exception exception, params string[] material)
    {
        var forms = material.SelectMany(m => Guid.TryParse(m, out var guid) ? new[] { m, guid.ToString("D"), guid.ToString("N") } : new[] { m }).Distinct().ToArray();
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            foreach (var form in forms)
            {
                e.Message.Should().NotContainEquivalentOf(form, "exception text never holds a secret or its reference");
                e.ToString().Should().NotContainEquivalentOf(form);
            }
        }
    }

    // ---- fixtures and doubles ----

    /// <summary>Two migrated throwaway catalogs: the default locking READ COMMITTED, and one with READ_COMMITTED_SNAPSHOT on.</summary>
    public sealed class Catalogs : IAsyncLifetime
    {
        public AuditSqlScratch Locking { get; private set; } = null!;
        public AuditSqlScratch Rcsi { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            Locking = await AuditSqlScratch.CreateAsync("Apperture_Aud14H1A_");
            Rcsi = await AuditSqlScratch.CreateAsync("Apperture_Aud14H1B_");

            await using (var connection = new SqlConnection(new SqlConnectionStringBuilder(Rcsi.ConnectionString) { InitialCatalog = "master" }.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"ALTER DATABASE [{Rcsi.Catalog}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;";
                await command.ExecuteNonQueryAsync();
            }

            SqlConnection.ClearPool(new SqlConnection(Rcsi.ConnectionString));

            (await Locking.ScalarAsync<bool>(RcsiQuery(Locking))).Should().BeFalse();
            (await Rcsi.ScalarAsync<bool>(RcsiQuery(Rcsi))).Should().BeTrue("the second catalog must really run under row versioning");
        }

        public async Task DisposeAsync()
        {
            await Locking.DisposeAsync();
            await Rcsi.DisposeAsync();
        }

        private static string RcsiQuery(AuditSqlScratch scratch) =>
            $"SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'{scratch.Catalog}';";
    }

    /// <summary>Records the text of every statement run on the context.</summary>
    private sealed class RecordedStatements : DbCommandInterceptor
    {
        public List<string> Texts { get; } = [];

        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Texts.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Registry : IIntegrationSettingDefinitionRegistry
    {
        private readonly Dictionary<string, PlatformSettingDefinition> _definitions = new(StringComparer.Ordinal);

        public Registry Credential(string key) => Add(key, sensitive: true, PlatformSettingValueScopeKind.Integration);

        public Registry Ordinary(string key) => Add(key, sensitive: false, PlatformSettingValueScopeKind.Integration);

        /// <summary>A setting that once had integration-scoped overrides and is now applicable at the instance scope only.</summary>
        public Registry PlatformOnlyOrdinary(string key) => Add(key, sensitive: false, PlatformSettingValueScopeKind.Instance);

        public IReadOnlyList<PlatformSettingDefinition> GetAll() => _definitions.Values.ToList();

        public PlatformSettingDefinition? TryGet(string key) => _definitions.GetValueOrDefault(key);

        private Registry Add(string key, bool sensitive, PlatformSettingValueScopeKind scope)
        {
            _definitions[key] = new PlatformSettingDefinition
            {
                Key = key,
                DisplayName = key,
                Description = "Test setting.",
                Category = PlatformSettingCategory.PlatformEvents,
                ValueType = PlatformSettingValueType.String,
                InputKind = sensitive ? PlatformSettingInputKind.Secret : PlatformSettingInputKind.Text,
                IsSensitive = sensitive,
                AllowedValueScopes = [scope],
                ConfigurationPath = "Test:" + key.Replace('.', ':'),
                DefaultValue = string.Empty,
                AuditValue = PlatformSettingAuditValuePolicy.Record,
            };
            return this;
        }
    }

    private sealed class PassThroughProtector : ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(plaintext.ToArray());

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(protectedPayload.ToArray());
    }
}
