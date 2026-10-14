using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.IntegrationTests.Audit;

// SEC-2.3 (A3.6): the H1 classifier and retirement accept the integration-specific secret names next to the legacy shape. The
// fixtures above are the legacy shape (provider platform-setting, name platform-setting:{key}); these are the names SEC-2.3 gives a
// new secret (provider integration-setting, name integration-setting:{integration}:{key}), on both isolation modes.
public sealed partial class IntegrationSettingSecretDispositionSqlServerTests
{
    private static string OwnerName(string integration, string key) => SettingSecretOwnership.ForIntegrationSetting(integration, key).Name;

    private static Task<Guid> CreateOwnedSecretAsync(AuditSqlScratch scratch, string integration, string key) =>
        CreateSecretAsync(scratch, OwnerName(integration, key), SettingSecretOwnership.IntegrationProvider);

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_secret_the_integration_owns_by_its_integration_specific_name_is_retired_once_with_the_delete(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateOwnedSecretAsync(scratch, integration, CredentialKey);
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));

        var run = await RunDeleteAsync(scratch, integration, new Registry().Credential(CredentialKey));

        run.Plan.Dispositions.Single().Kind.Should().Be(IntegrationSecretDispositionKind.RetireAfterOverrideRemoval);
        run.Retired.Should().Be(1);
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Disabled);
        (await OverrideCountAsync(scratch, integration)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_legacy_secret_and_an_integration_owned_secret_of_one_integration_are_both_retired_by_one_delete(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var legacy = await CreateSecretAsync(scratch, SettingSecretName(CredentialKey));
        var owned = await CreateOwnedSecretAsync(scratch, integration, OtherKey);
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(legacy.ToString("D")));
        await InsertOverrideAsync(scratch, integration, OtherKey, JsonSerializer.Serialize(owned.ToString("D")));

        var run = await RunDeleteAsync(scratch, integration, new Registry().Credential(CredentialKey).Credential(OtherKey));

        run.Retired.Should().Be(2);
        (await SecretStateAsync(scratch, legacy)).Should().Be(SecretState.Disabled);
        (await SecretStateAsync(scratch, owned)).Should().Be(SecretState.Disabled);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_secret_named_for_another_key_of_the_same_integration_refuses_the_delete_and_changes_nothing(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateOwnedSecretAsync(scratch, integration, OtherKey);
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));

        var act = () => RunDeleteAsync(scratch, integration, new Registry().Credential(CredentialKey));

        await act.Should().ThrowAsync<IntegrationSettingDispositionRefusedException>();
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Active);
        (await OverrideCountAsync(scratch, integration)).Should().Be(1, "a refused delete removes nothing");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Another_integrations_secret_that_this_integration_names_is_never_retired_and_refuses_the_delete_when_nothing_else_holds_it(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var theirs = await CreateOwnedSecretAsync(scratch, NewIntegrationId(), CredentialKey);
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(theirs.ToString("D")));

        var act = () => RunDeleteAsync(scratch, integration, new Registry().Credential(CredentialKey));

        await act.Should().ThrowAsync<IntegrationSettingDispositionRefusedException>();
        (await SecretStateAsync(scratch, theirs)).Should().Be(SecretState.Active);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Another_integrations_secret_is_retained_while_that_integrations_own_override_holds_it(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var other = NewIntegrationId();
        var theirs = await CreateOwnedSecretAsync(scratch, other, CredentialKey);
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(theirs.ToString("D")));
        await InsertOverrideAsync(scratch, other, CredentialKey, JsonSerializer.Serialize(theirs.ToString("D")));

        var run = await RunDeleteAsync(scratch, integration, new Registry().Credential(CredentialKey));

        AssertRetained(run, SurvivingSecretReferenceKind.OtherIntegrationSetting);
        (await SecretStateAsync(scratch, theirs)).Should().Be(SecretState.Active);
        (await OverrideCountAsync(scratch, other)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_integration_owned_secret_that_a_platform_setting_value_also_holds_is_retained(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateOwnedSecretAsync(scratch, integration, CredentialKey);
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));
        await InsertSettingAsync(scratch, PlatformSettingScopeConstants.InstanceKind, "inst-" + NewSuffix(), "Sample.Platform", JsonSerializer.Serialize(secret.ToString("D")));

        var run = await RunDeleteAsync(scratch, integration, new Registry().Credential(CredentialKey));

        AssertRetained(run, SurvivingSecretReferenceKind.PlatformSetting);
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Active);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_connection_secret_named_by_an_override_is_never_retired_whatever_the_integrations_name_is(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integration = NewIntegrationId();
        var secret = await CreateSecretAsync(scratch, "connection:con-" + NewSuffix() + ":Password", "platform-connection");
        await InsertOverrideAsync(scratch, integration, CredentialKey, JsonSerializer.Serialize(secret.ToString("D")));

        var run = await RunDeleteAsync(scratch, integration, new Registry().Credential(CredentialKey));

        run.Plan.Dispositions.Single().NoSecretReason.Should().Be(IntegrationSecretNoSecretReason.NotSettingsProvisioned);
        run.Retired.Should().Be(0);
        (await SecretStateAsync(scratch, secret)).Should().Be(SecretState.Active);
    }
}
