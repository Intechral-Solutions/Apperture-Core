using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Security.Secrets.Models;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Moq;
using Xunit;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.PlatformSettings;

/// <summary>
/// SEC-2.3 (A3.1 to A3.3, at service level over fakes): the two settings services give a setting's secret the owner the single
/// ownership rule defines, repoint a setting whose reference is not its own at a new owned secret without touching the old one,
/// and clear or reset only a secret the setting owns. The same behaviour against SQL Server, on both isolation modes, is proven by
/// <c>SettingSecretOwnershipSqlServerTests</c>.
/// </summary>
public sealed class SettingSecretOwnershipServiceTests
{
    private const string IntegrationA = "int-a";
    private const string IntegrationB = "int-b";
    private const string IntegrationKey = PlatformSettingTestDefinitions.IntegrationScopedSecretKey;
    private static readonly string PlatformKey = PlatformSettingKeys.NotificationsEmailSmtpPasswordRef;

    private static readonly SecretOwner OwnerA = SettingSecretOwnership.ForIntegrationSetting(IntegrationA, IntegrationKey);
    private static readonly SecretOwner OwnerB = SettingSecretOwnership.ForIntegrationSetting(IntegrationB, IntegrationKey);

    private static async Task<string> SeedAsync(InMemorySecretStore store, string provider, string name, string material)
    {
        var descriptor = await store.CreateAsync(new SecretCreateRequest(name, provider, SecretMaterialType.OpaqueString));
        var version = await store.AddVersionAsync(descriptor.SecretId, new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: material));
        await store.ActivateVersionAsync(descriptor.SecretId, version);
        return descriptor.SecretId.Value.ToString("D");
    }

    // ---- integration settings ----

    [Fact]
    public async Task Two_integrations_that_replace_the_same_sensitive_key_each_get_a_secret_under_their_own_owner()
    {
        var h = new IntegrationHarness();

        await h.ReplaceAsync(IntegrationA, "a-value");
        await h.ReplaceAsync(IntegrationB, "b-value");

        var a = h.StoredRef(IntegrationA)!;
        var b = h.StoredRef(IntegrationB)!;
        a.Should().NotBe(b);
        h.Store.Get(a)!.Name.Should().Be(OwnerA.Name);
        h.Store.Get(a)!.Provider.Should().Be("integration-setting");
        h.Store.Get(b)!.Name.Should().Be(OwnerB.Name);
        h.Store.ActiveOpaque(a).Should().Be("a-value");
        h.Store.ActiveOpaque(b).Should().Be("b-value");
    }

    [Fact]
    public async Task A_replace_by_B_over_a_reference_to_A_s_secret_repoints_B_to_a_new_secret_and_leaves_A_s_untouched()
    {
        var h = new IntegrationHarness();
        await h.ReplaceAsync(IntegrationA, "a-value");
        var aRef = h.StoredRef(IntegrationA)!;
        h.PointAt(IntegrationB, aRef);
        h.Audit.Clear();

        await h.ReplaceAsync(IntegrationB, "b-value");

        var bRef = h.StoredRef(IntegrationB)!;
        bRef.Should().NotBe(aRef);
        h.Store.Get(bRef)!.Name.Should().Be(OwnerB.Name);
        h.Store.ActiveOpaque(bRef).Should().Be("b-value");
        h.Store.VersionCount(aRef).Should().Be(1, "A's secret received no version");
        h.Store.ActiveOpaque(aRef).Should().Be("a-value");
        h.StoredRef(IntegrationA).Should().Be(aRef, "A's row is untouched");
        h.Audit.Single(AuditActionNames.IntegrationSettingSecretStored).Fields["change"].Should().Be("created");
    }

    [Fact]
    public async Task A_replace_over_the_integrations_own_secret_adds_a_version_and_records_replaced()
    {
        var h = new IntegrationHarness();
        await h.ReplaceAsync(IntegrationA, "one");
        var aRef = h.StoredRef(IntegrationA)!;
        h.Audit.Clear();

        await h.ReplaceAsync(IntegrationA, "two");

        h.StoredRef(IntegrationA).Should().Be(aRef);
        h.Store.VersionCount(aRef).Should().Be(2);
        h.Audit.Single(AuditActionNames.IntegrationSettingSecretStored).Fields["change"].Should().Be("replaced");
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("reset")]
    public async Task A_clear_or_reset_by_B_of_a_row_that_names_A_s_secret_never_disables_it(string how)
    {
        var h = new IntegrationHarness();
        await h.ReplaceAsync(IntegrationA, "a-value");
        var aRef = h.StoredRef(IntegrationA)!;
        h.PointAt(IntegrationB, aRef);
        h.Audit.Clear();

        if (how == "clear")
            await h.ClearAsync(IntegrationB);
        else
            await h.ResetAsync(IntegrationB);

        h.StoredRef(IntegrationB).Should().BeNull("B's own override is gone");
        h.Store.Get(aRef)!.State.Should().Be(SecretState.Active, "A's credential is still usable");
        h.Store.DisableCalls.Should().Be(0);
        h.Audit.Actions.Should().NotContain(AuditActionNames.IntegrationSettingSecretRetired);
    }

    [Theory]
    [InlineData("clear", "cleared")]
    [InlineData("reset", "reset")]
    public async Task A_clear_or_reset_of_the_integrations_own_secret_retires_it_and_records_why(string how, string reason)
    {
        var h = new IntegrationHarness();
        await h.ReplaceAsync(IntegrationA, "a-value");
        var aRef = h.StoredRef(IntegrationA)!;
        h.Audit.Clear();

        if (how == "clear")
            await h.ClearAsync(IntegrationA);
        else
            await h.ResetAsync(IntegrationA);

        h.Store.Get(aRef)!.State.Should().Be(SecretState.Disabled);
        h.Audit.Single(AuditActionNames.IntegrationSettingSecretRetired).Fields["reason"].Should().Be(reason);
    }

    [Fact]
    public async Task A_clear_by_B_leaves_A_working_when_both_hold_their_own_secrets()
    {
        var h = new IntegrationHarness();
        await h.ReplaceAsync(IntegrationA, "a-value");
        await h.ReplaceAsync(IntegrationB, "b-value");
        var aRef = h.StoredRef(IntegrationA)!;
        var bRef = h.StoredRef(IntegrationB)!;

        await h.ClearAsync(IntegrationB);

        h.Store.Get(bRef)!.State.Should().Be(SecretState.Disabled);
        h.Store.Get(aRef)!.State.Should().Be(SecretState.Active);
        h.Store.ActiveOpaque(aRef).Should().Be("a-value");
    }

    // ---- the integration's stored id, not the spelling of the route ----

    [Fact]
    public async Task A_route_that_spells_the_integration_id_in_another_case_stores_the_secret_under_the_integrations_stored_id()
    {
        var h = new IntegrationHarness(storedIdIsCaseInsensitive: true);

        await h.ReplaceAsync("INT-A", "a-value");

        var reference = h.StoredRef(IntegrationA);
        reference.Should().NotBeNull("the row is stored under the integration's own id, which the worker reads with");
        h.StoredRef("INT-A").Should().BeNull("no row is stored under the spelling the caller used");
        h.Store.Get(reference!)!.Name.Should().Be(OwnerA.Name, "the owner is derived from the stored id, so the worker's read is not refused");
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("reset")]
    public async Task A_clear_or_reset_through_another_spelling_retires_the_secret_the_stored_id_owns(string how)
    {
        var h = new IntegrationHarness(storedIdIsCaseInsensitive: true);
        await h.ReplaceAsync(IntegrationA, "a-value");
        var reference = h.StoredRef(IntegrationA)!;

        if (how == "clear")
            await h.ClearAsync("INT-A", IntegrationA);
        else
            await h.ResetAsync("INT-A");

        h.Store.Get(reference)!.State.Should().Be(SecretState.Disabled, "the secret is the integration's own whatever spelling asked");
    }

    // ---- a name too long for the store never blocks clearing ----

    [Theory]
    [InlineData("clear")]
    [InlineData("reset")]
    public async Task A_row_whose_owner_name_cannot_fit_the_store_can_still_be_cleared_or_reset_and_nothing_is_disabled(string how)
    {
        var h = new IntegrationHarness();
        var someSecret = await SeedAsync(h.Store, "platform-connection", "connection:con-1:Password", "other-material");
        h.PointAt(IntegrationA, someSecret, IntegrationHarness.LongKey);

        if (how == "clear")
            await h.ClearAsync(IntegrationA, IntegrationA, IntegrationHarness.LongKey);
        else
            await h.ResetAsync(IntegrationA, IntegrationHarness.LongKey);

        h.StoredRef(IntegrationA, IntegrationHarness.LongKey).Should().BeNull("the row is gone");
        h.Store.Get(someSecret)!.State.Should().Be(SecretState.Active);
        h.Store.DisableCalls.Should().Be(0);
    }

    // ---- instance settings ----

    [Fact]
    public async Task An_instance_replace_keeps_the_identity_the_smtp_password_has_always_had()
    {
        var h = new PlatformHarness();

        await h.ReplaceAsync("smtp-pass");

        var reference = h.StoredRef()!;
        h.Store.Get(reference)!.Provider.Should().Be("platform-setting");
        h.Store.Get(reference)!.Name.Should().Be($"platform-setting:{PlatformKey}");
    }

    [Fact]
    public async Task An_instance_replace_over_a_reference_to_a_connections_secret_creates_a_new_owned_secret_and_leaves_the_connections_untouched()
    {
        var h = new PlatformHarness();
        var connectionSecret = await SeedAsync(h.Store, "platform-connection", "connection:con-1:Password", "connection-material");
        h.PointAt(connectionSecret);

        await h.ReplaceAsync("smtp-pass");

        var reference = h.StoredRef()!;
        reference.Should().NotBe(connectionSecret);
        h.Store.Get(reference)!.Name.Should().Be($"platform-setting:{PlatformKey}");
        h.Store.VersionCount(connectionSecret).Should().Be(1);
        h.Store.ActiveOpaque(connectionSecret).Should().Be("connection-material");
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("reset")]
    public async Task An_instance_clear_or_reset_never_disables_a_secret_it_does_not_own(string how)
    {
        var h = new PlatformHarness();
        var entraSecret = await SeedAsync(h.Store, "entra-registration", "entra-client-secret:reg-1", "entra-material");
        h.PointAt(entraSecret);

        if (how == "clear")
            await h.ClearAsync();
        else
            await h.ResetAsync();

        h.StoredRef().Should().BeNull();
        h.Store.Get(entraSecret)!.State.Should().Be(SecretState.Active);
        h.Store.DisableCalls.Should().Be(0);
        h.Audit.Actions.Should().NotContain(AuditActionNames.PlatformSettingSecretRetired);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("reset")]
    public async Task An_instance_clear_or_reset_of_its_own_secret_retires_it(string how)
    {
        var h = new PlatformHarness();
        await h.ReplaceAsync("smtp-pass");
        var reference = h.StoredRef()!;

        if (how == "clear")
            await h.ClearAsync();
        else
            await h.ResetAsync();

        h.Store.Get(reference)!.State.Should().Be(SecretState.Disabled);
        h.Audit.Actions.Should().Contain(AuditActionNames.PlatformSettingSecretRetired);
    }

    // ---- harnesses ----

    private sealed class IntegrationHarness
    {
        private readonly PlatformSettingRepositoryFake _repo = new();
        private readonly IntegrationSettingsService _service;
        private readonly PlatformSettingDefinition _def = PlatformSettingTestDefinitions.IntegrationScopedSecretSample;

        /// <summary>A key whose escaped owner name cannot fit the secret store (each colon is written as three characters).</summary>
        public static readonly string LongKey = new string(':', 100);

        /// <param name="storedIdIsCaseInsensitive">
        /// The catalog finds an integration whatever the case of the id it is given, as SQL Server's default collation does, and
        /// returns the id as it is stored (<see cref="IntegrationA"/>).
        /// </param>
        public IntegrationHarness(bool storedIdIsCaseInsensitive = false)
        {
            Store = new InMemorySecretStore();
            Audit = new AuditRecording();
            var registry = new Mock<IIntegrationSettingDefinitionRegistry>();
            registry.Setup(r => r.TryGet(IntegrationKey)).Returns(_def);
            registry.Setup(r => r.TryGet(LongKey)).Returns(_def with { Key = LongKey });
            registry.Setup(r => r.GetAll()).Returns([_def]);

            var integrations = new Mock<IIntegrationRepository>();
            ValueTask<DomainIntegration?> Find(IntegrationId id) => ValueTask.FromResult<DomainIntegration?>(
                new DomainIntegration(
                    storedIdIsCaseInsensitive && string.Equals(id.Value, IntegrationA, StringComparison.OrdinalIgnoreCase) ? new IntegrationId(IntegrationA) : id,
                    "Test Integration", "mod-1", null, IntegrationState.Active, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                {
                    ResourceContextId = new ResourceContextId("shared"),
                });
            integrations.Setup(x => x.GetByIdAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>())).Returns((IntegrationId id, CancellationToken _) => Find(id));
            integrations.Setup(x => x.GetByIdForUpdateAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>())).Returns((IntegrationId id, CancellationToken _) => Find(id));

            _service = new IntegrationSettingsService(
                registry.Object,
                _repo,
                new IntegrationSettingsResolver(registry.Object, _repo),
                PlatformSettingOptionProviderTestHarness.CreateCoordinator(new Mock<IPlatformSettingOptionProvider>().Object, integrations: integrations),
                new PlatformSettingSecretMaterialProvisioner(Store),
                integrations.Object,
                Audit);
        }

        public InMemorySecretStore Store { get; }
        public AuditRecording Audit { get; }

        public Task ReplaceAsync(string integrationId, string value) =>
            _service.SetAsync(
                integrationId,
                IntegrationKey,
                new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = value },
                RowVersionOf(integrationId));

        /// <param name="integrationId">The id the request names (the route).</param>
        /// <param name="rowIntegrationId">The id the row is stored under, when it differs from the route's spelling.</param>
        public Task ClearAsync(string integrationId, string? rowIntegrationId = null, string key = IntegrationKey) =>
            _service.SetAsync(
                integrationId,
                key,
                new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Clear },
                RowVersionOf(rowIntegrationId ?? integrationId, key));

        public Task ResetAsync(string integrationId, string key = IntegrationKey) => _service.ResetAsync(integrationId, key);

        public string? StoredRef(string integrationId, string key = IntegrationKey)
        {
            var row = _repo.GetScopedAsync(key, PlatformSettingScopeConstants.IntegrationKind, integrationId).GetAwaiter().GetResult();
            return row is null ? null : JsonSerializer.Deserialize<string>(row.ValueJson);
        }

        /// <summary>Stores a row that names <paramref name="reference"/>, as a poisoned or legacy row would.</summary>
        public void PointAt(string integrationId, string reference, string key = IntegrationKey) =>
            _repo.UpsertAsync(
                new PlatformSettingValue
                {
                    Key = key,
                    ScopeKind = PlatformSettingScopeConstants.IntegrationKind,
                    ScopeId = integrationId,
                    ValueJson = JsonSerializer.Serialize(reference),
                },
                null).GetAwaiter().GetResult();

        private string? RowVersionOf(string integrationId, string key = IntegrationKey)
        {
            var row = _repo.GetScopedAsync(key, PlatformSettingScopeConstants.IntegrationKind, integrationId).GetAwaiter().GetResult();
            return row is null ? null : Convert.ToBase64String(row.RowVersion is { Length: > 0 } v ? v : [1]);
        }
    }

    private sealed class PlatformHarness
    {
        private readonly PlatformSettingRepositoryFake _repo = new();
        private readonly PlatformSettingsService _service;

        public PlatformHarness()
        {
            Store = new InMemorySecretStore();
            Audit = new AuditRecording();
            var registry = new PlatformSettingDefinitionRegistry();
            _service = new PlatformSettingsService(
                registry,
                _repo,
                new PlatformSettingsResolver(registry, _repo, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()),
                PlatformSettingOptionProviderTestHarness.CreateCoordinator(new Mock<IPlatformSettingOptionProvider>().Object),
                new PlatformSettingSecretMaterialProvisioner(Store),
                Audit);
        }

        public InMemorySecretStore Store { get; }
        public AuditRecording Audit { get; }

        public Task ReplaceAsync(string value) =>
            _service.SetAsync(
                PlatformKey,
                new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = value },
                RowVersion());

        public Task ClearAsync() =>
            _service.SetAsync(PlatformKey, new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Clear }, RowVersion());

        public Task ResetAsync() => _service.ResetAsync(PlatformKey);

        public string? StoredRef()
        {
            var row = _repo.GetByKeyAsync(PlatformKey).GetAwaiter().GetResult();
            return row is null ? null : JsonSerializer.Deserialize<string>(row.ValueJson);
        }

        public void PointAt(string reference) =>
            _repo.UpsertAsync(
                new PlatformSettingValue
                {
                    Key = PlatformKey,
                    ScopeKind = PlatformSettingScopeConstants.InstanceKind,
                    ScopeId = PlatformSettingScopeConstants.InstanceId,
                    ValueJson = JsonSerializer.Serialize(reference),
                },
                null).GetAwaiter().GetResult();

        private string? RowVersion()
        {
            var row = _repo.GetByKeyAsync(PlatformKey).GetAwaiter().GetResult();
            return row is null ? null : Convert.ToBase64String(row.RowVersion is { Length: > 0 } v ? v : [1]);
        }
    }
}
