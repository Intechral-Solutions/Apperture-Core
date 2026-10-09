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
/// AUD-1.4 slice 3, work package 4a (owner ruling H1): the logic of the integration delete's settings-secret classifier and its
/// transactional retirement, on an in-memory context and a fake secret store. The database-sensitive behaviour (reference
/// matching across the three locations, both isolation modes, atomic rollback) is proven against SQL Server by
/// <c>IntegrationSettingSecretDispositionSqlServerTests</c>.
/// </summary>
public sealed class IntegrationSettingSecretDispositionTests
{
    private const string IntegrationId = "int-h1";
    private const string Key = "Sample.Credential";

    [Fact]
    public async Task An_owned_secret_with_a_matching_key_and_no_other_reference_is_planned_for_retirement_once()
    {
        var h = new Harness();
        var secret = h.Secret(Key);
        h.Override(Key, secret);

        var plan = await h.ClassifyAsync();

        plan.IsRefused.Should().BeFalse();
        plan.Overrides.Select(o => o.Key).Should().Equal(Key);
        plan.Dispositions.Single().Kind.Should().Be(IntegrationSecretDispositionKind.RetireAfterOverrideRemoval);
        plan.Retirements.Should().ContainSingle().Which.SettingKey.Should().Be(Key);
    }

    [Fact]
    public async Task A_refused_plan_carries_no_retirement_and_nothing_it_prints_names_a_secret_a_reference_or_a_value()
    {
        var h = new Harness();
        var owned = h.Secret(Key);
        var foreign = h.Secret("Some.Other.Key");
        h.Override(Key, owned);
        h.Override("Sample.Second", foreign);

        var plan = await h.ClassifyAsync();

        plan.IsRefused.Should().BeTrue();
        plan.Refusal!.SettingKeys.Should().Equal("Sample.Second");
        plan.Refusal.Reasons.Should().Equal(IntegrationSecretAmbiguityReason.SettingKeyMismatch);
        plan.Retirements.Should().BeEmpty("a refused plan can retire nothing, not even the secret that was safe");
        var act = plan.ThrowIfRefused;
        var thrown = act.Should().Throw<IntegrationSettingDispositionRefusedException>().Which;
        foreach (var text in new[] { thrown.Message, plan.ToString(), plan.Refusal.ToString(), plan.Overrides[0].ToString(), plan.Dispositions[0].ToString() })
        {
            foreach (var guid in new[] { owned, foreign })
            {
                text.Should().NotContainEquivalentOf(guid.ToString("D"));
                text.Should().NotContainEquivalentOf(guid.ToString("N"));
            }

            text.Should().NotContain("Some.Other.Key", "the message names no secret metadata");
        }

        thrown.Message.Should().NotContain("Sample.Second", "the keys are on the typed refusal, not in the message");
    }

    [Fact]
    public async Task Retirement_given_a_refused_plan_throws_before_it_reads_stages_or_changes_anything()
    {
        var h = new Harness();
        h.Override(Key, h.Secret("Some.Other.Key"));
        var plan = await h.ClassifyAsync();
        var unit = new RecordingAuditedUnitOfWork();

        var act = () => unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.IntegrationDeleted, AuditActor.CurrentPrincipal),
            (tx, ct) => h.Retirement.RetireAsync(tx, plan, "ctx", ct),
            (_, _) => Task.FromResult(0));

        await act.Should().ThrowAsync<IntegrationSettingDispositionRefusedException>();
        h.Store.Disabled.Should().BeEmpty();
        unit.Operations.Single().Staged.Should().BeEmpty();
    }

    [Fact]
    public async Task Each_retirement_is_staged_before_the_secret_is_disabled_and_a_repeated_secret_is_retired_once()
    {
        var h = new Harness();
        var secret = h.Secret(Key);
        h.Override(Key, secret);
        var plan = await h.ClassifyAsync();
        var duplicated = new IntegrationSettingDeletionPlan(
            plan.IntegrationId,
            plan.Overrides,
            plan.Dispositions,
            [plan.Retirements[0], new PlannedSecretRetirement(Key, secret)],
            refusal: null);
        var unit = new RecordingAuditedUnitOfWork();
        h.Store.OnDisable = () => unit.Operations.Single().Staged.Select(e => e.Action)
            .Should().Contain(AuditActionNames.IntegrationSettingSecretRetired, "the row is staged before the change is made");

        var retired = await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.IntegrationDeleted, AuditActor.CurrentPrincipal),
            async (tx, ct) =>
            {
                await tx.StageAsync(AuditEvents.IntegrationDeleted(IntegrationId, "Sample", "Mod", 1, "ctx"), ct);
                return await h.Retirement.RetireAsync(tx, duplicated, "ctx", ct);
            },
            (_, _) => Task.FromResult(0));

        retired.Should().Be(1);
        h.Store.Disabled.Should().Equal(secret);
        var row = unit.Single(AuditActionNames.IntegrationSettingSecretRetired);
        row.Fields["reason"].Should().Be("integration_deleted");
        row.Fields["key"].Should().Be(Key);
        row.Fields.Values.Select(v => v?.ToString()).Should().NotContain(v => v!.Contains(secret.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_secret_that_is_no_longer_usable_when_the_retirement_runs_is_skipped_without_a_row()
    {
        var h = new Harness();
        var secret = h.Secret(Key);
        h.Override(Key, secret);
        var plan = await h.ClassifyAsync();
        h.Store.Descriptors[secret] = h.Store.Descriptors[secret] with { State = SecretState.Disabled };
        var unit = new RecordingAuditedUnitOfWork();

        var retired = await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.IntegrationDeleted, AuditActor.CurrentPrincipal),
            async (tx, ct) =>
            {
                await tx.StageAsync(AuditEvents.IntegrationDeleted(IntegrationId, "Sample", null, 1, "ctx"), ct);
                return await h.Retirement.RetireAsync(tx, plan, "ctx", ct);
            },
            (_, _) => Task.FromResult(0));

        retired.Should().Be(0);
        h.Store.Disabled.Should().BeEmpty();
        unit.Actions.Should().Equal(AuditActionNames.IntegrationDeleted);
    }

    [Fact]
    public async Task A_store_failure_is_reported_without_the_stores_own_message_which_embeds_the_secrets_id()
    {
        var h = new Harness();
        var secret = h.Secret(Key);
        h.Override(Key, secret);
        var plan = await h.ClassifyAsync();
        h.Store.FailDisableWith = new InvalidOperationException($"Secret {secret} not found.");
        var unit = new RecordingAuditedUnitOfWork();

        var act = () => unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.IntegrationDeleted, AuditActor.CurrentPrincipal),
            async (tx, ct) =>
            {
                await tx.StageAsync(AuditEvents.IntegrationDeleted(IntegrationId, "Sample", null, 1, "ctx"), ct);
                return await h.Retirement.RetireAsync(tx, plan, "ctx", ct);
            },
            (_, _) => Task.FromResult(0));

        var thrown = (await act.Should().ThrowAsync<IntegrationSettingSecretRetirementException>()).Which;
        thrown.SettingKey.Should().Be(Key);
        thrown.InnerExceptionType.Should().Be(nameof(InvalidOperationException));
        thrown.InnerException.Should().BeNull();
        thrown.Message.Should().NotContainEquivalentOf(secret.ToString("D"));
        unit.Operations.Single().Completed.Should().BeFalse("the unit's attempt failed, so nothing it staged counts as committed");
    }

    [Fact]
    public async Task A_non_secret_override_and_a_value_that_names_no_guid_are_listed_for_removal_and_involve_no_secret()
    {
        var h = new Harness();
        h.OverrideJson("Sample.Ordinary", "\"just text\"");
        h.OverrideJson("Sample.Empty", "\"\"");
        h.OverrideJson("Sample.Number", "42");

        var plan = await h.ClassifyAsync();

        plan.Overrides.Select(o => o.Key).Should().Equal("Sample.Empty", "Sample.Number", "Sample.Ordinary");
        plan.Dispositions.Should().OnlyContain(d => d.Kind == IntegrationSecretDispositionKind.NoApplicableSecret
            && d.NoSecretReason == IntegrationSecretNoSecretReason.NoReference);
        plan.IsRefused.Should().BeFalse();
        plan.Retirements.Should().BeEmpty();
    }

    [Fact]
    public async Task A_credential_setting_with_an_unreadable_value_is_ambiguous_but_an_unreadable_value_on_an_ordinary_setting_is_not()
    {
        var h = new Harness();
        h.OverrideJson("Sample.Credential.Broken", "{not json");
        h.OverrideJson("Sample.Ordinary.Broken", "{not json");
        h.Sensitive.Add("Sample.Credential.Broken");

        var plan = await h.ClassifyAsync();

        plan.Refusal!.SettingKeys.Should().Equal("Sample.Credential.Broken");
        plan.Refusal.Reasons.Should().Equal(IntegrationSecretAmbiguityReason.UnreadableReference);
        plan.Dispositions.Single(d => d.Key == "Sample.Ordinary.Broken").Kind.Should().Be(IntegrationSecretDispositionKind.NoApplicableSecret);
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
            registry.Setup(r => r.TryGet(It.IsAny<string>())).Returns((string key) =>
                Sensitive.Contains(key)
                    ? new PlatformSettingDefinition
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
                    }
                    : null);
            Store = new FakeStore();
            Retirement = new IntegrationSettingSecretRetirement(Store);
            Classifier = new IntegrationSettingSecretClassifier(new IntegrationSettingSecretInventory(_db), Store, registry.Object);
        }

        public FakeStore Store { get; }
        public IntegrationSettingSecretRetirement Retirement { get; }
        public IntegrationSettingSecretClassifier Classifier { get; }
        public HashSet<string> Sensitive { get; } = [];

        public Task<IntegrationSettingDeletionPlan> ClassifyAsync() => Classifier.ClassifyAsync(IntegrationId);

        /// <summary>A usable settings-provisioned secret named for <paramref name="settingKey"/>.</summary>
        public Guid Secret(string settingKey)
        {
            var id = Guid.NewGuid();
            Store.Descriptors[id] = new SecretDescriptor(
                new SecretId(id),
                PlatformSettingSecretMaterialProvisioner.BuildSecretName(settingKey),
                PlatformSettingSecretMaterialProvisioner.SecretProvider,
                SecretMaterialType.OpaqueString,
                "v1",
                SecretState.Active,
                DateTimeOffset.UtcNow);
            return id;
        }

        public void Override(string key, Guid secret)
        {
            Sensitive.Add(key);
            OverrideJson(key, System.Text.Json.JsonSerializer.Serialize(secret.ToString("D")));
        }

        public void OverrideJson(string key, string valueJson)
        {
            _db.PlatformSettingValues.Add(new PlatformSettingValueEntity
            {
                Id = $"{PlatformSettingScopeConstants.IntegrationKind}:{IntegrationId}:{key}",
                Key = key,
                ScopeKind = PlatformSettingScopeConstants.IntegrationKind,
                ScopeId = IntegrationId,
                ValueJson = valueJson,
            });
            _db.SaveChanges();
        }
    }

    private sealed class FakeStore : ISecretStore
    {
        public Dictionary<Guid, SecretDescriptor> Descriptors { get; } = [];
        public List<Guid> Disabled { get; } = [];
        public Exception? FailDisableWith { get; set; }
        public Action? OnDisable { get; set; }

        public ValueTask<SecretDescriptor?> GetAsync(SecretId secretId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Descriptors.GetValueOrDefault(secretId.Value));

        public ValueTask DisableAsync(SecretId secretId, CancellationToken cancellationToken = default)
        {
            OnDisable?.Invoke();
            if (FailDisableWith is not null)
                throw FailDisableWith;
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
