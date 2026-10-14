using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Persistence.SqlServer;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

/// <summary>
/// SEC-2.3 (A3.6, A3.8): the H1 classifier and retirement accept the integration-specific names next to the legacy shape, and the
/// legacy disposition used when a setting releases a reference (clear, reset, re-entry) retires a legacy secret only on H1's
/// evidence: a usable settings-provisioned secret named for that very setting, that no remaining row, connection or Entra
/// registration refers to. The database-sensitive behaviour is proven against SQL Server by
/// <c>SettingSecretOwnershipSqlServerTests</c>.
/// </summary>
public sealed class IntegrationSettingSecretOwnershipDispositionTests
{
    private const string IntegrationId = "int-sec23";
    private const string OtherIntegrationId = "int-other";
    private const string Key = "Sample.Credential";

    // ---- the delete plan accepts the new names ----

    [Fact]
    public async Task A_secret_the_integration_owns_by_its_new_name_and_nothing_else_holds_is_planned_for_retirement()
    {
        var h = new Harness();
        var secret = h.Secret(SettingSecretOwnership.ForIntegrationSetting(IntegrationId, Key));
        h.Override(IntegrationId, Key, secret);

        var plan = await h.ClassifyAsync();

        plan.IsRefused.Should().BeFalse();
        plan.Dispositions.Single().Kind.Should().Be(IntegrationSecretDispositionKind.RetireAfterOverrideRemoval);
        plan.Retirements.Should().ContainSingle().Which.SettingKey.Should().Be(Key);
    }

    [Fact]
    public async Task A_secret_the_integration_owns_for_another_key_is_ambiguous_and_refuses_the_delete()
    {
        var h = new Harness();
        var secret = h.Secret(SettingSecretOwnership.ForIntegrationSetting(IntegrationId, "Some.Other.Key"));
        h.Override(IntegrationId, Key, secret);

        var plan = await h.ClassifyAsync();

        plan.IsRefused.Should().BeTrue();
        plan.Refusal!.Reasons.Should().Equal(IntegrationSecretAmbiguityReason.SettingKeyMismatch);
        plan.Retirements.Should().BeEmpty();
    }

    [Fact]
    public async Task Another_integrations_secret_that_this_integration_names_is_never_planned_for_retirement()
    {
        var h = new Harness();
        var theirs = h.Secret(SettingSecretOwnership.ForIntegrationSetting(OtherIntegrationId, Key));
        h.Override(IntegrationId, Key, theirs);

        var plan = await h.ClassifyAsync();

        plan.IsRefused.Should().BeTrue("ownership by this integration cannot be established");
        plan.Refusal!.Reasons.Should().Equal(IntegrationSecretAmbiguityReason.SettingKeyMismatch);
        plan.Retirements.Should().BeEmpty();
    }

    [Fact]
    public async Task Another_integrations_secret_that_its_own_override_still_holds_is_retained()
    {
        var h = new Harness();
        var theirs = h.Secret(SettingSecretOwnership.ForIntegrationSetting(OtherIntegrationId, Key));
        h.Override(IntegrationId, Key, theirs);
        h.Override(OtherIntegrationId, Key, theirs);

        var plan = await h.ClassifyAsync();

        plan.IsRefused.Should().BeFalse();
        var disposition = plan.Dispositions.Single();
        disposition.Kind.Should().Be(IntegrationSecretDispositionKind.RetainSharedReference);
        disposition.SurvivingReferences.Should().ContainSingle().Which.Kind.Should().Be(SurvivingSecretReferenceKind.OtherIntegrationSetting);
        plan.Retirements.Should().BeEmpty();
    }

    [Fact]
    public async Task A_legacy_secret_the_integration_alone_holds_is_still_planned_for_retirement_and_a_shared_one_is_retained()
    {
        var h = new Harness();
        var sole = h.Secret(SettingSecretOwnership.LegacyIntegrationSetting(Key));
        var shared = h.Secret(SettingSecretOwnership.LegacyIntegrationSetting("Sample.Shared"));
        h.Override(IntegrationId, Key, sole);
        h.Override(IntegrationId, "Sample.Shared", shared);
        h.Override(OtherIntegrationId, "Sample.Shared", shared);

        var plan = await h.ClassifyAsync();

        plan.IsRefused.Should().BeFalse();
        plan.Dispositions.Single(d => d.Key == Key).Kind.Should().Be(IntegrationSecretDispositionKind.RetireAfterOverrideRemoval);
        plan.Dispositions.Single(d => d.Key == "Sample.Shared").Kind.Should().Be(IntegrationSecretDispositionKind.RetainSharedReference);
        plan.Retirements.Should().ContainSingle().Which.SettingKey.Should().Be(Key);
    }

    [Theory]
    [InlineData("platform-connection", "connection:con-1:Password")]
    [InlineData("entra-registration", "entra-client-secret:reg-1")]
    public async Task A_secret_of_another_provider_is_never_planned_for_retirement(string provider, string name)
    {
        var h = new Harness();
        var foreign = h.Secret(new SecretOwner(provider, name));
        h.Override(IntegrationId, Key, foreign);

        var plan = await h.ClassifyAsync();

        plan.IsRefused.Should().BeFalse();
        var disposition = plan.Dispositions.Single();
        disposition.Kind.Should().Be(IntegrationSecretDispositionKind.NoApplicableSecret);
        disposition.NoSecretReason.Should().Be(IntegrationSecretNoSecretReason.NotSettingsProvisioned);
        plan.Retirements.Should().BeEmpty();
    }

    // ---- retirement executes only what the plan proved ----

    [Fact]
    public async Task A_planned_retirement_of_a_new_name_secret_is_executed_and_one_whose_descriptor_changed_is_skipped()
    {
        var h = new Harness();
        var owned = h.Secret(SettingSecretOwnership.ForIntegrationSetting(IntegrationId, Key));
        var changed = h.Secret(SettingSecretOwnership.ForIntegrationSetting(IntegrationId, "Sample.Second"));
        h.Override(IntegrationId, Key, owned);
        h.Override(IntegrationId, "Sample.Second", changed);
        var plan = await h.ClassifyAsync();
        // Between the plan and the retirement the second secret's descriptor no longer names the setting.
        h.Store.Descriptors[changed] = h.Store.Descriptors[changed] with { Name = "something-else" };
        var unit = new RecordingAuditedUnitOfWork();

        var retired = await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.IntegrationDeleted, AuditActor.CurrentPrincipal),
            async (tx, ct) =>
            {
                await tx.StageAsync(AuditEvents.IntegrationDeleted(IntegrationId, "Sample", "Mod", 1, "ctx"), ct);
                return await h.Retirement.RetireAsync(tx, plan, "ctx", ct);
            },
            (_, _) => Task.FromResult(0));

        retired.Should().Be(1);
        h.Store.Disabled.Should().Equal(owned);
    }

    // ---- the legacy disposition when a row releases a reference ----

    [Fact]
    public async Task A_legacy_secret_nothing_else_refers_to_is_retirable_once_its_row_has_released_it()
    {
        var h = new Harness();
        var legacy = h.Secret(SettingSecretOwnership.LegacyIntegrationSetting(Key));

        (await h.ReleasedAsync(legacy.ToString("D"))).Should().Be(legacy);
    }

    [Fact]
    public async Task A_legacy_secret_is_retirable_whatever_spelling_of_the_reference_the_row_held()
    {
        var h = new Harness();
        var legacy = h.Secret(SettingSecretOwnership.LegacyIntegrationSetting(Key));

        (await h.ReleasedAsync($"  {legacy:N}  ")).Should().Be(legacy);
        (await h.ReleasedAsync(legacy.ToString("B").ToUpperInvariant())).Should().Be(legacy);
    }

    public static TheoryData<string> OtherHolders => new() { "other integration row", "instance setting row", "connection", "entra registration" };

    [Theory]
    [MemberData(nameof(OtherHolders))]
    public async Task A_legacy_secret_that_any_other_holder_still_refers_to_is_retained(string holder)
    {
        var h = new Harness();
        var legacy = h.Secret(SettingSecretOwnership.LegacyIntegrationSetting(Key));
        switch (holder)
        {
            case "other integration row": h.Override(OtherIntegrationId, Key, legacy); break;
            case "instance setting row": h.Instance("Some.Instance.Key", legacy); break;
            case "connection": h.Connection("con-1", $$"""{"Password":"{{legacy:D}}"}"""); break;
            case "entra registration": h.Entra("reg-1", legacy.ToString("D")); break;
        }

        (await h.ReleasedAsync(legacy.ToString("D"))).Should().BeNull();
    }

    [Fact]
    public async Task A_legacy_secret_that_the_releasing_integrations_other_override_still_refers_to_is_retained()
    {
        var h = new Harness();
        var legacy = h.Secret(SettingSecretOwnership.LegacyIntegrationSetting(Key));
        h.Override(IntegrationId, "Sample.Second", legacy);

        (await h.ReleasedAsync(legacy.ToString("D"))).Should().BeNull();
    }

    public static TheoryData<string> NotRetirable => new()
    {
        "a legacy secret named for another key",
        "an integration-owned secret of this setting",
        "another integration's owned secret",
        "a connection secret",
        "an instance-shaped secret under another provider",
        "a disabled legacy secret",
        "a dangling reference",
        "a blank reference",
        "an unreadable reference",
    };

    [Theory]
    [MemberData(nameof(NotRetirable))]
    public async Task Only_a_usable_legacy_secret_named_for_this_very_setting_is_ever_retirable(string shape)
    {
        var h = new Harness();
        var reference = shape switch
        {
            "a legacy secret named for another key" => h.Secret(SettingSecretOwnership.LegacyIntegrationSetting("Other.Key")).ToString("D"),
            "an integration-owned secret of this setting" => h.Secret(SettingSecretOwnership.ForIntegrationSetting(IntegrationId, Key)).ToString("D"),
            "another integration's owned secret" => h.Secret(SettingSecretOwnership.ForIntegrationSetting(OtherIntegrationId, Key)).ToString("D"),
            "a connection secret" => h.Secret(new SecretOwner("platform-connection", "connection:con-1:Password")).ToString("D"),
            "an instance-shaped secret under another provider" => h.Secret(new SecretOwner("entra-registration", SettingSecretOwnership.LegacyIntegrationSetting(Key).Name)).ToString("D"),
            "a disabled legacy secret" => h.Secret(SettingSecretOwnership.LegacyIntegrationSetting(Key), SecretState.Disabled).ToString("D"),
            "a dangling reference" => Guid.NewGuid().ToString("D"),
            "a blank reference" => "  ",
            "an unreadable reference" => "not-a-guid",
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

        (await h.ReleasedAsync(reference)).Should().BeNull();
        h.Store.Disabled.Should().BeEmpty();
    }

    [Fact]
    public async Task No_reference_means_nothing_to_retire()
    {
        var h = new Harness();

        (await h.ReleasedAsync(null)).Should().BeNull();
    }

    // ---- harness ----

    private sealed class Harness
    {
        private readonly AppertureDbContext _db = new(new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

        public Harness()
        {
            var registry = new Mock<IIntegrationSettingDefinitionRegistry>();
            registry.Setup(r => r.TryGet(It.IsAny<string>())).Returns((string key) => new PlatformSettingDefinition
            {
                Key = key,
                DisplayName = "Credential (test)",
                Description = "Sensitive integration-scoped setting.",
                Category = PlatformSettingCategory.PlatformEvents,
                ValueType = PlatformSettingValueType.String,
                InputKind = PlatformSettingInputKind.Secret,
                IsSensitive = true,
                ConfigurationPath = "Test:Credential",
                DefaultValue = string.Empty,
            });
            Store = new FakeStore();
            Retirement = new IntegrationSettingSecretRetirement(Store);
            Classifier = new IntegrationSettingSecretClassifier(new IntegrationSettingSecretInventory(_db), Store, registry.Object);
        }

        public FakeStore Store { get; }
        public IntegrationSettingSecretRetirement Retirement { get; }
        public IntegrationSettingSecretClassifier Classifier { get; }

        public Task<IntegrationSettingDeletionPlan> ClassifyAsync() => Classifier.ClassifyAsync(IntegrationId);

        /// <summary>The secret the releasing row's reference names, if the evidence permits retiring it.</summary>
        public async Task<Guid?> ReleasedAsync(string? reference) =>
            await Classifier.FindLegacySecretToRetireAsync(Key, reference);

        public Guid Secret(SecretOwner owner, SecretState state = SecretState.Active)
        {
            var id = Guid.NewGuid();
            Store.Descriptors[id] = new SecretDescriptor(
                new SecretId(id), owner.Name, owner.Provider, SecretMaterialType.OpaqueString, "v1", state, DateTimeOffset.UtcNow);
            return id;
        }

        public void Override(string integrationId, string key, Guid secret) =>
            Row(PlatformSettingScopeConstants.IntegrationKind, integrationId, key, secret);

        public void Instance(string key, Guid secret) =>
            Row(PlatformSettingScopeConstants.InstanceKind, PlatformSettingScopeConstants.InstanceId, key, secret);

        private void Row(string kind, string scopeId, string key, Guid secret)
        {
            _db.PlatformSettingValues.Add(new PlatformSettingValueEntity
            {
                Id = $"{kind}:{scopeId}:{key}",
                Key = key,
                ScopeKind = kind,
                ScopeId = scopeId,
                ValueJson = JsonSerializer.Serialize(secret.ToString("D")),
            });
            _db.SaveChanges();
        }

        public void Connection(string id, string secretRefsJson)
        {
            _db.Connections.Add(new ConnectionEntity
            {
                Id = id,
                Name = id,
                ConnectorDefinitionId = "test",
                ConfigJson = "{}",
                SecretRefsJson = secretRefsJson,
                ResourceContextId = "shared",
            });
            _db.SaveChanges();
        }

        public void Entra(string id, string clientSecretRef)
        {
            _db.EntraInstanceRegistrations.Add(new EntraInstanceRegistrationEntity { Id = id, ClientSecretRef = clientSecretRef });
            _db.SaveChanges();
        }
    }

    private sealed class FakeStore : ISecretStore
    {
        public Dictionary<Guid, SecretDescriptor> Descriptors { get; } = [];
        public List<Guid> Disabled { get; } = [];

        public ValueTask<SecretDescriptor?> GetAsync(SecretId secretId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Descriptors.GetValueOrDefault(secretId.Value));

        public ValueTask DisableAsync(SecretId secretId, CancellationToken cancellationToken = default)
        {
            Disabled.Add(secretId.Value);
            Descriptors[secretId.Value] = Descriptors[secretId.Value] with { State = SecretState.Disabled };
            return ValueTask.CompletedTask;
        }

        public ValueTask<SecretDescriptor> CreateAsync(SecretCreateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<SecretDescriptor>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<string> AddVersionAsync(SecretId secretId, SecretMaterial material, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask ActivateVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask RevokeVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<SecretAuditPage> GetAuditAsync(SecretAuditQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
