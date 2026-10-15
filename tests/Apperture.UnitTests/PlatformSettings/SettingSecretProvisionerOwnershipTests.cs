using Apperture.ControlPlane.Security;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

/// <summary>
/// SEC-2.3 (P-19, A3.1 to A3.3 at unit level): a setting versions or disables a secret only after its stored descriptor proves it
/// belongs to that setting. A reference that is not the setting's own is never versioned, adopted or disabled: the setting gets a
/// new owned secret and the referenced one is left exactly as it was.
/// </summary>
public sealed class SettingSecretProvisionerOwnershipTests
{
    private const string Key = "Sample.Api.Token";
    private static readonly SecretOwner IntegrationA = SettingSecretOwnership.ForIntegrationSetting("int-a", Key);
    private static readonly SecretOwner IntegrationB = SettingSecretOwnership.ForIntegrationSetting("int-b", Key);
    private static readonly SecretOwner Instance = SettingSecretOwnership.ForPlatformSetting(Key);

    private static async Task<string> SeedAsync(InMemorySecretStore store, string provider, string name, string material, bool disable = false)
    {
        var descriptor = await store.CreateAsync(new SecretCreateRequest(name, provider, SecretMaterialType.OpaqueString));
        var version = await store.AddVersionAsync(descriptor.SecretId, new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: material));
        await store.ActivateVersionAsync(descriptor.SecretId, version);
        if (disable)
            await store.DisableAsync(descriptor.SecretId);
        return descriptor.SecretId.Value.ToString("D");
    }

    // ---- replace: the first write creates a secret the setting owns ----

    [Fact]
    public async Task A_first_replace_creates_a_secret_under_the_integration_owner()
    {
        var store = new InMemorySecretStore();
        var sut = new PlatformSettingSecretMaterialProvisioner(store);

        var (reference, created) = await sut.ReplaceAsync(IntegrationA, "first", null);

        created.Should().BeTrue();
        var descriptor = store.Get(reference)!;
        descriptor.Provider.Should().Be("integration-setting");
        descriptor.Name.Should().Be($"integration-setting:int-a:{Key}");
        store.ActiveOpaque(reference).Should().Be("first");
    }

    [Fact]
    public async Task A_first_replace_for_an_instance_setting_keeps_the_instance_identity()
    {
        var store = new InMemorySecretStore();
        var sut = new PlatformSettingSecretMaterialProvisioner(store);

        var (reference, _) = await sut.ReplaceAsync(Instance, "first", null);

        var descriptor = store.Get(reference)!;
        descriptor.Provider.Should().Be("platform-setting");
        descriptor.Name.Should().Be($"platform-setting:{Key}");
    }

    [Fact]
    public async Task Two_integrations_that_replace_the_same_key_get_two_distinct_secrets()
    {
        var store = new InMemorySecretStore();
        var sut = new PlatformSettingSecretMaterialProvisioner(store);

        var (a, _) = await sut.ReplaceAsync(IntegrationA, "a-value", null);
        var (b, _) = await sut.ReplaceAsync(IntegrationB, "b-value", null);

        a.Should().NotBe(b);
        store.Get(a)!.Name.Should().Be($"integration-setting:int-a:{Key}");
        store.Get(b)!.Name.Should().Be($"integration-setting:int-b:{Key}");
        store.ActiveOpaque(a).Should().Be("a-value");
        store.ActiveOpaque(b).Should().Be("b-value");
    }

    // ---- replace: the owned secret is versioned ----

    [Fact]
    public async Task A_replace_on_the_settings_own_usable_secret_adds_an_active_version_and_keeps_the_reference()
    {
        var store = new InMemorySecretStore();
        var own = await SeedAsync(store, IntegrationA.Provider, IntegrationA.Name, "old");
        var sut = new PlatformSettingSecretMaterialProvisioner(store);

        var (reference, created) = await sut.ReplaceAsync(IntegrationA, "new", own);

        created.Should().BeFalse();
        reference.Should().Be(own);
        store.VersionCount(own).Should().Be(2);
        store.ActiveOpaque(own).Should().Be("new");
    }

    // ---- replace: a reference that is not the setting's own is never touched ----

    public static TheoryData<string> ForeignShapes => new()
    {
        "another integration's secret for the same key",
        "an instance setting's secret for the same key",
        "the legacy shape (provider platform-setting, name platform-setting:{key})",
        "a connection's secret",
        "an Entra registration's secret",
        "the owner's name under another provider",
        "the owner's provider under another name (the same key, another integration)",
        "a case variant of the owner's name",
        "a case variant of the owner's provider",
        "the name of another key of the same integration",
    };

    private static async Task<string> SeedForeignAsync(InMemorySecretStore store, string shape, string material) => shape switch
    {
        "another integration's secret for the same key" => await SeedAsync(store, IntegrationB.Provider, IntegrationB.Name, material),
        "an instance setting's secret for the same key" => await SeedAsync(store, Instance.Provider, Instance.Name, material),
        "the legacy shape (provider platform-setting, name platform-setting:{key})" => await SeedAsync(store, "platform-setting", $"platform-setting:{Key}", material),
        "a connection's secret" => await SeedAsync(store, "platform-connection", "connection:con-1:Password", material),
        "an Entra registration's secret" => await SeedAsync(store, "entra-registration", "entra-client-secret:reg-1", material),
        "the owner's name under another provider" => await SeedAsync(store, "platform-connection", IntegrationA.Name, material),
        "the owner's provider under another name (the same key, another integration)" => await SeedAsync(store, IntegrationA.Provider, IntegrationB.Name, material),
        "a case variant of the owner's name" => await SeedAsync(store, IntegrationA.Provider, IntegrationA.Name.ToUpperInvariant(), material),
        "a case variant of the owner's provider" => await SeedAsync(store, "Integration-Setting", IntegrationA.Name, material),
        "the name of another key of the same integration" => await SeedAsync(store, IntegrationA.Provider, SettingSecretOwnership.ForIntegrationSetting("int-a", "Other.Key").Name, material),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
    };

    [Theory]
    [MemberData(nameof(ForeignShapes))]
    public async Task A_replace_on_a_reference_that_is_not_the_settings_own_creates_a_new_owned_secret_and_leaves_the_referenced_one_untouched(string shape)
    {
        var store = new InMemorySecretStore();
        var foreign = await SeedForeignAsync(store, shape, "foreign-material");
        var before = store.Get(foreign)!;
        var versionsBefore = store.VersionCount(foreign);
        var sut = new PlatformSettingSecretMaterialProvisioner(store);

        var (reference, created) = await sut.ReplaceAsync(IntegrationA, "new-value", foreign);

        created.Should().BeTrue("the setting gets a secret of its own");
        reference.Should().NotBe(foreign);
        store.Get(reference)!.Provider.Should().Be("integration-setting");
        store.Get(reference)!.Name.Should().Be($"integration-setting:int-a:{Key}");
        store.ActiveOpaque(reference).Should().Be("new-value");

        store.VersionCount(foreign).Should().Be(versionsBefore, "no version was added to the foreign secret");
        store.ActiveOpaque(foreign).Should().Be("foreign-material", "its active version is unchanged");
        var after = store.Get(foreign)!;
        (after.Provider, after.Name, after.State, after.ActiveVersion).Should().Be(
            (before.Provider, before.Name, before.State, before.ActiveVersion), "its descriptor is unchanged");
        store.DisableCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_replace_on_a_dangling_reference_creates_a_new_owned_secret()
    {
        var store = new InMemorySecretStore();
        var sut = new PlatformSettingSecretMaterialProvisioner(store);

        var (reference, created) = await sut.ReplaceAsync(IntegrationA, "new-value", Guid.NewGuid().ToString("D"));

        created.Should().BeTrue();
        store.Get(reference)!.Name.Should().Be(IntegrationA.Name);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_replace_on_an_unreadable_or_blank_reference_creates_a_new_owned_secret(string existing)
    {
        var store = new InMemorySecretStore();
        var sut = new PlatformSettingSecretMaterialProvisioner(store);

        var (reference, created) = await sut.ReplaceAsync(IntegrationA, "new-value", existing);

        created.Should().BeTrue();
        store.Get(reference)!.Name.Should().Be(IntegrationA.Name);
    }

    [Fact]
    public async Task A_replace_on_the_settings_own_disabled_secret_creates_a_new_one()
    {
        var store = new InMemorySecretStore();
        var own = await SeedAsync(store, IntegrationA.Provider, IntegrationA.Name, "old", disable: true);
        var sut = new PlatformSettingSecretMaterialProvisioner(store);

        var (reference, created) = await sut.ReplaceAsync(IntegrationA, "new", own);

        created.Should().BeTrue();
        reference.Should().NotBe(own);
        store.VersionCount(own).Should().Be(1);
    }

    [Fact]
    public async Task A_reference_with_surrounding_whitespace_is_still_checked_against_the_descriptor()
    {
        var store = new InMemorySecretStore();
        var foreign = await SeedAsync(store, IntegrationB.Provider, IntegrationB.Name, "foreign-material");
        var sut = new PlatformSettingSecretMaterialProvisioner(store);

        var (reference, created) = await sut.ReplaceAsync(IntegrationA, "new", $"  {foreign}  ");

        created.Should().BeTrue();
        reference.Should().NotBe(foreign);
        store.VersionCount(foreign).Should().Be(1);
    }

    // ---- disable: only what the setting owns ----

    [Fact]
    public async Task A_disable_retires_the_settings_own_usable_secret()
    {
        var store = new InMemorySecretStore();
        var own = await SeedAsync(store, IntegrationA.Provider, IntegrationA.Name, "mine");
        var sut = new PlatformSettingSecretMaterialProvisioner(store);

        (await sut.TryDisableOwnedSecretAsync(IntegrationA, own)).Should().BeTrue();

        store.Get(own)!.State.Should().Be(SecretState.Disabled);
    }

    [Fact]
    public async Task Integration_B_can_never_disable_integration_A_s_secret_for_the_same_key()
    {
        var store = new InMemorySecretStore();
        var a = await SeedAsync(store, IntegrationA.Provider, IntegrationA.Name, "a-secret");
        var sut = new PlatformSettingSecretMaterialProvisioner(store);

        (await sut.TryDisableOwnedSecretAsync(IntegrationB, a)).Should().BeFalse();

        store.Get(a)!.State.Should().Be(SecretState.Active);
        store.DisableCalls.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(ForeignShapes))]
    public async Task A_disable_never_touches_a_secret_that_is_not_the_settings_own(string shape)
    {
        var store = new InMemorySecretStore();
        var foreign = await SeedForeignAsync(store, shape, "foreign-material");
        var sut = new PlatformSettingSecretMaterialProvisioner(store);

        (await sut.TryDisableOwnedSecretAsync(IntegrationA, foreign)).Should().BeFalse();

        store.Get(foreign)!.State.Should().Be(SecretState.Active);
        store.DisableCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public async Task A_disable_with_no_usable_reference_does_nothing(string? reference)
    {
        var store = new InMemorySecretStore();
        var sut = new PlatformSettingSecretMaterialProvisioner(store);

        (await sut.TryDisableOwnedSecretAsync(IntegrationA, reference)).Should().BeFalse();
        (await sut.TryDisableOwnedSecretAsync(IntegrationA, Guid.NewGuid().ToString("D"))).Should().BeFalse();
        store.DisableCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_disable_does_not_repeat_for_a_secret_that_is_already_disabled()
    {
        var store = new InMemorySecretStore();
        var own = await SeedAsync(store, IntegrationA.Provider, IntegrationA.Name, "mine", disable: true);
        var sut = new PlatformSettingSecretMaterialProvisioner(store);

        (await sut.TryDisableOwnedSecretAsync(IntegrationA, own)).Should().BeFalse();
        store.DisableCalls.Should().Be(1, "only the seed's own disable");
    }

    // ---- the ownership question on its own ----

    [Fact]
    public async Task Ownership_is_a_question_about_the_descriptor_and_never_reads_a_version()
    {
        var store = new InMemorySecretStore();
        var own = await SeedAsync(store, IntegrationA.Provider, IntegrationA.Name, "mine");
        var foreign = await SeedAsync(store, IntegrationB.Provider, IntegrationB.Name, "theirs");
        var sut = new PlatformSettingSecretMaterialProvisioner(store);

        (await sut.IsReferenceOwnedAsync(IntegrationA, own)).Should().BeTrue();
        (await sut.IsReferenceOwnedAsync(IntegrationA, foreign)).Should().BeFalse();
        (await sut.IsReferenceOwnedAsync(IntegrationA, null)).Should().BeFalse();
        (await sut.IsReferenceOwnedAsync(IntegrationA, "not-a-guid")).Should().BeFalse();
        (await sut.IsReferenceOwnedAsync(IntegrationA, Guid.NewGuid().ToString("D"))).Should().BeFalse();
    }
}
