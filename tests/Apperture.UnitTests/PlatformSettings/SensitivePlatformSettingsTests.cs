using Apperture.ControlPlane.Security;
using Apperture.UnitTests.Audit;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Management.Api.Models;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class SensitivePlatformSettingsTests
{
    private static PlatformSettingDefinition SmtpPasswordDef =>
        new PlatformSettingDefinitionRegistry().TryGet(PlatformSettingKeys.NotificationsEmailSmtpPasswordRef)!;

    [Fact]
    public void Smtp_password_definition_is_sensitive_secret_input()
    {
        var def = SmtpPasswordDef;
        def.IsSensitive.Should().BeTrue();
        def.InputKind.Should().Be(PlatformSettingInputKind.Secret);
        def.ValueType.Should().Be(PlatformSettingValueType.String);
        def.DisplayName.Should().Be("SMTP password");
    }

    [Fact]
    public void Dto_mapper_redacts_sensitive_current_value_and_exposes_status()
    {
        var def = SmtpPasswordDef;
        var secretGuid = Guid.NewGuid().ToString("D");
        var resolved = PlatformSettingResolvedValueFactory.Create(
            def,
            secretGuid,
            PlatformSettingValueSource.Database,
            rowVersion: [1, 2, 3, 4]);

        var dto = PlatformSettingsDtoMapper.ToDto(resolved);

        dto.IsSensitive.Should().BeTrue();
        dto.IsSecretConfigured.Should().BeTrue();
        dto.HasPlatformOverride.Should().BeTrue();
        dto.CurrentValue.Should().BeEmpty();
        dto.CurrentValue.Should().NotContain(secretGuid);
        dto.InputKind.Should().Be("Secret");
    }

    [Fact]
    public async Task Replace_stores_only_guid_in_platform_setting_values()
    {
        var def = SmtpPasswordDef;
        var store = new InMemorySecretStore();
        var repo = new PlatformSettingRepositoryFake();
        var resolver = CreateResolver(def, repo);
        var recorded = new AuditRecording();
        var sut = CreateService(def, repo, resolver, store, recorded);

        var result = await sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload
            {
                SecretOperation = PlatformSettingSecretOperations.Replace,
                SecretValue = "s3cret-password"
            },
            null);

        result.IsSecretConfigured.Should().BeTrue();
        result.HasPlatformOverride.Should().BeTrue();
        result.CurrentValue.Should().BeOfType<string>().Which.Should().MatchRegex(
            @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");
        result.CurrentValue.Should().NotBe("s3cret-password");

        var stored = await repo.GetByKeyAsync(def.Key);
        stored.Should().NotBeNull();
        stored!.ValueJson.Should().NotContain("s3cret-password");
        Guid.TryParse(PlatformSettingsValueCodec.ParseFromStoredJson(def, stored.ValueJson) as string, out _)
            .Should().BeTrue();

        recorded.Actions.Should().Equal("platform.settings.set", "platform.settings.secret.stored");
        var set = recorded.Single("platform.settings.set");
        set.Fields["operation"].Should().Be("replace");
        set.Fields["sensitive"].Should().Be(true);
        set.Target!.Id.Should().Be(def.Key);
        recorded.Single("platform.settings.secret.stored").Fields["change"].Should().Be("created");
        foreach (var text in recorded.Events.Select(e => FieldText(e.Event)))
        {
            text.Should().NotContain("s3cret-password");
            text.Should().NotContain(result.CurrentValue.ToString()!);
        }
    }

    [Fact]
    public async Task Replace_rejects_ordinary_value_payload()
    {
        var def = SmtpPasswordDef;
        var store = new InMemorySecretStore();
        var repo = new PlatformSettingRepositoryFake();
        var sut = CreateService(def, repo, CreateResolver(def, repo), store);

        var act = () => sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { ScalarValue = "plaintext-or-guid" },
            null);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*does not accept Value/Values*");
        (await repo.GetByKeyAsync(def.Key)).Should().BeNull();
    }

    [Fact]
    public async Task Blank_operation_leaves_existing_secret_unchanged()
    {
        var def = SmtpPasswordDef;
        var store = new InMemorySecretStore();
        var provisioner = new PlatformSettingSecretMaterialProvisioner(store);
        var (existingRef, _) = await provisioner.ReplaceAsync(SettingSecretOwnership.ForPlatformSetting(def.Key), "original", null);
        var repo = new PlatformSettingRepositoryFake(new PlatformSettingValue
        {
            Key = def.Key,
            ScopeKind = PlatformSettingScopeConstants.InstanceKind,
            ScopeId = PlatformSettingScopeConstants.InstanceId,
            ValueJson = PlatformSettingsValueCodec.ToStoredJson(def, existingRef),
            RowVersion = [9]
        });
        var sut = CreateService(def, repo, CreateResolver(def, repo), store);

        var result = await sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { SecretValue = "" },
            Convert.ToBase64String([9]));

        result.CurrentValue.Should().Be(existingRef);
        store.ActiveOpaque(existingRef).Should().Be("original");
        store.VersionCount(existingRef).Should().Be(1);
    }

    [Fact]
    public async Task A_stale_row_version_is_a_conflict_before_any_secret_is_stored()
    {
        var def = SmtpPasswordDef;
        var store = new InMemorySecretStore();
        var provisioner = new PlatformSettingSecretMaterialProvisioner(store);
        var (existingRef, _) = await provisioner.ReplaceAsync(SettingSecretOwnership.ForPlatformSetting(def.Key), "keep-me", null);
        var repo = new PlatformSettingRepositoryFake(new PlatformSettingValue
        {
            Key = def.Key,
            ScopeKind = PlatformSettingScopeConstants.InstanceKind,
            ScopeId = PlatformSettingScopeConstants.InstanceId,
            ValueJson = PlatformSettingsValueCodec.ToStoredJson(def, existingRef),
            RowVersion = [1]
        });
        var recorded = new AuditRecording();
        var sut = CreateService(def, repo, CreateResolver(def, repo), store, recorded);

        var act = () => sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload
            {
                SecretOperation = PlatformSettingSecretOperations.Replace,
                SecretValue = "replacement"
            },
            Convert.ToBase64String([2]));

        await act.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException>();
        store.ActiveOpaque(existingRef).Should().Be("keep-me", "the live credential is untouched");
        store.VersionCount(existingRef).Should().Be(1, "no version was added for a stale request");
        store.Descriptors.Should().ContainSingle();
        recorded.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_upsert_records_nothing_and_attempts_no_compensation()
    {
        // The secret store joins the unit's transaction, so a failed save rolls the new secret back with the row and its audit
        // rows; the service no longer disables the new secret itself (the old compensation, whose own save re-sent the failed
        // row). That rollback is proven against SQL Server (SettingsAuditSqlServerTests); this shows the service's side of it.
        var def = SmtpPasswordDef;
        var store = new InMemorySecretStore();
        var repo = new ThrowingUpsertRepository();
        var recorded = new AuditRecording();
        var sut = CreateService(def, repo, CreateResolver(def, repo), store, recorded);

        var act = () => sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload
            {
                SecretOperation = PlatformSettingSecretOperations.Replace,
                SecretValue = "new-password"
            },
            null);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*upsert failed*");
        recorded.Actions.Should().BeEmpty("a change that did not complete records nothing");
        store.DisableCalls.Should().Be(0, "nothing is compensated; the unit's rollback undoes the secret");
    }

    [Fact]
    public async Task Clear_removes_override_and_disables_owned_secret()
    {
        var def = SmtpPasswordDef;
        var store = new InMemorySecretStore();
        var provisioner = new PlatformSettingSecretMaterialProvisioner(store);
        var (existingRef, _) = await provisioner.ReplaceAsync(SettingSecretOwnership.ForPlatformSetting(def.Key), "to-clear", null);
        var repo = new PlatformSettingRepositoryFake(new PlatformSettingValue
        {
            Key = def.Key,
            ScopeKind = PlatformSettingScopeConstants.InstanceKind,
            ScopeId = PlatformSettingScopeConstants.InstanceId,
            ValueJson = PlatformSettingsValueCodec.ToStoredJson(def, existingRef)
        });
        var recorded = new AuditRecording();
        var sut = CreateService(def, repo, CreateResolver(def, repo), store, recorded);

        var result = await sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Clear },
            null);

        result.HasPlatformOverride.Should().BeFalse();
        result.IsSecretConfigured.Should().BeFalse();
        (await repo.GetByKeyAsync(def.Key)).Should().BeNull();
        store.Get(existingRef)!.State.Should().Be(SecretState.Disabled);
        recorded.Actions.Should().Equal("platform.settings.set", "platform.settings.secret.retired");
        recorded.Single("platform.settings.set").Fields["operation"].Should().Be("clear");
        recorded.Single("platform.settings.set").Fields["previousConfigured"].Should().Be(true);
        recorded.Single("platform.settings.secret.retired").Fields["reason"].Should().Be("cleared");
        foreach (var text in recorded.Events.Select(e => FieldText(e.Event)))
        {
            text.Should().NotContain("to-clear");
            text.Should().NotContain(existingRef);
        }
    }

    [Fact]
    public async Task Reset_restores_precedence_and_disables_owned_secret()
    {
        var def = SmtpPasswordDef;
        var store = new InMemorySecretStore();
        var provisioner = new PlatformSettingSecretMaterialProvisioner(store);
        var (existingRef, _) = await provisioner.ReplaceAsync(SettingSecretOwnership.ForPlatformSetting(def.Key), "reset-me", null);
        var repo = new PlatformSettingRepositoryFake(new PlatformSettingValue
        {
            Key = def.Key,
            ScopeKind = PlatformSettingScopeConstants.InstanceKind,
            ScopeId = PlatformSettingScopeConstants.InstanceId,
            ValueJson = PlatformSettingsValueCodec.ToStoredJson(def, existingRef)
        });
        var recorded = new AuditRecording();
        var sut = CreateService(def, repo, CreateResolver(def, repo), store, recorded);

        var result = await sut.ResetAsync(def.Key);

        result!.HasPlatformOverride.Should().BeFalse();
        (await repo.GetByKeyAsync(def.Key)).Should().BeNull();
        store.Get(existingRef)!.State.Should().Be(SecretState.Disabled);
        recorded.Actions.Should().Equal("platform.settings.reset", "platform.settings.secret.retired");
        recorded.Single("platform.settings.secret.retired").Fields["reason"].Should().Be("reset");
        foreach (var text in recorded.Events.Select(e => FieldText(e.Event)))
        {
            text.Should().NotContain("reset-me");
            text.Should().NotContain(existingRef);
        }
    }

    [Fact]
    public async Task Replace_then_resolve_smtp_password_via_secret_store()
    {
        var def = SmtpPasswordDef;
        var store = new InMemorySecretStore();
        var repo = new PlatformSettingRepositoryFake();
        var sut = CreateService(def, repo, CreateResolver(def, repo), store);

        var updated = await sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload
            {
                SecretOperation = PlatformSettingSecretOperations.Replace,
                SecretValue = "smtp-via-settings"
            },
            null);

        var secretRef = (string)updated.CurrentValue;
        var resolved = await store.ResolveAsync(new SecretRef(secretRef));
        resolved.Material.OpaqueString.Should().Be("smtp-via-settings");

        var dto = PlatformSettingsDtoMapper.ToDto(updated);
        dto.CurrentValue.Should().BeEmpty();
        dto.IsSecretConfigured.Should().BeTrue();
    }

    [Fact]
    public void Non_sensitive_settings_remain_unchanged_in_dto()
    {
        var def = new PlatformSettingDefinitionRegistry().TryGet(PlatformSettingKeys.NotificationsEmailSmtpHost)!;
        var resolved = PlatformSettingResolvedValueFactory.Create(
            def,
            "smtp.example.test",
            PlatformSettingValueSource.Database);

        var dto = PlatformSettingsDtoMapper.ToDto(resolved);
        dto.IsSensitive.Should().BeFalse();
        dto.CurrentValue.Should().Be("smtp.example.test");
        dto.IsSecretConfigured.Should().BeFalse();
    }

    private static PlatformSettingsService CreateService(
        PlatformSettingDefinition def,
        IPlatformSettingValueRepository repo,
        IPlatformSettingsResolver resolver,
        InMemorySecretStore store,
        AuditRecording? recording = null)
    {
        var coordinator = PlatformSettingOptionProviderTestHarness.CreateCoordinator(
            new Mock<IPlatformSettingOptionProvider>().Object);

        return new PlatformSettingsService(
            new SingleDefinitionRegistry(def),
            repo,
            resolver,
            coordinator,
            new PlatformSettingSecretMaterialProvisioner(store),
            recording ?? new AuditRecording());
    }

    /// <summary>Every field value of an event as text, to assert that no secret or reference is among them.</summary>
    private static string FieldText(Apperture.Core.Application.Audit.AuditEvent auditEvent) =>
        string.Join("|", auditEvent.Fields.Select(f => f.Value?.ToString() ?? string.Empty));

    private static IPlatformSettingsResolver CreateResolver(
        PlatformSettingDefinition def,
        IPlatformSettingValueRepository repo) =>
        new PlatformSettingsResolver(
            new SingleDefinitionRegistry(def),
            repo,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

    private sealed class SingleDefinitionRegistry(PlatformSettingDefinition definition) : IPlatformSettingDefinitionRegistry
    {
        public IReadOnlyList<PlatformSettingDefinition> GetAll() => [definition];
        public PlatformSettingDefinition? TryGet(string key) =>
            string.Equals(key, definition.Key, StringComparison.Ordinal) ? definition : null;
    }

    private sealed class ThrowingUpsertRepository : PlatformSettingRepositoryFake
    {
        public ThrowingUpsertRepository(params PlatformSettingValue[] seed) : base(seed)
        {
        }

        public override Task UpsertAsync(PlatformSettingValue value, byte[]? expectedRowVersion, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("upsert failed");
    }

    // InMemorySecretStore now lives in its own file (InMemorySecretStore.cs, same namespace) so the
    // CTX-3B Integration hard-delete secret-disposal tests can share it. Body unchanged.
}
