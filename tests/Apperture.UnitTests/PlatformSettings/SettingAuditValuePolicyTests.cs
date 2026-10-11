using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Integration.Configuration;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

/// <summary>
/// AUD-1.4 review remediation: which values a setting change may put in its audit rows (design gate §12.3), that a malformed
/// override can still be reset, and how a failed response read after a commit is reported. Every behaviour is run against the
/// platform and the integration settings service, which take the policy from the same definition.
/// </summary>
public sealed class SettingAuditValuePolicyTests
{
    private const string IntegrationId = "int-policy-1";
    private const string Email = "someone@example.test";

    public enum Service
    {
        Platform,
        Integration,
    }

    // ---- the policy, by definition ----

    [Fact]
    public void A_definitions_policy_decides_whether_its_values_are_recorded()
    {
        var choices = new[] { new PlatformSettingAllowedValue { Value = "a", Label = "A" } };
        var cases = new (string Case, PlatformSettingDefinition Definition, bool Records)[]
        {
            ("boolean", Def(PlatformSettingValueType.Boolean), true),
            ("integer", Def(PlatformSettingValueType.Integer), true),
            ("duration", Def(PlatformSettingValueType.Duration), true),
            ("choice with declared options", Def(PlatformSettingValueType.String, allowed: choices), true),
            ("choice from a provider", Def(PlatformSettingValueType.String, provider: "provider.key"), true),
            ("multi-choice with declared options", Def(PlatformSettingValueType.StringArray, allowed: choices), true),
            ("undeclared free text", Def(PlatformSettingValueType.String), false),
            ("undeclared array without options", Def(PlatformSettingValueType.StringArray), false),
            ("declared bounded text", Def(PlatformSettingValueType.String, PlatformSettingAuditValuePolicy.Record), true),
            ("approved email field", Def(PlatformSettingValueType.String, PlatformSettingAuditValuePolicy.RecordOperationalEmail), true),
            ("omitted text", Def(PlatformSettingValueType.String, PlatformSettingAuditValuePolicy.Omit), false),
            ("omitted choice", Def(PlatformSettingValueType.String, PlatformSettingAuditValuePolicy.Omit, allowed: choices), false),
            ("sensitive, declared Record", Def(PlatformSettingValueType.String, PlatformSettingAuditValuePolicy.Record, sensitive: true), false),
            ("sensitive, declared email", Def(PlatformSettingValueType.String, PlatformSettingAuditValuePolicy.RecordOperationalEmail, sensitive: true), false),
            ("sensitive choice", Def(PlatformSettingValueType.String, allowed: choices, sensitive: true), false),
        };

        foreach (var (name, definition, records) in cases)
            PlatformSettingAuditValues.RecordsValue(definition).Should().Be(records, name);
    }

    [Fact]
    public void Every_free_text_platform_setting_declares_its_audit_policy_so_none_depends_on_the_default()
    {
        var registry = new PlatformSettingDefinitionRegistry();

        var undeclared = registry.GetAll()
            .Where(d => !d.IsSensitive && d.ValueType == PlatformSettingValueType.String)
            .Where(d => d.AllowedValues is not { Count: > 0 } && string.IsNullOrWhiteSpace(d.OptionProviderKey))
            .Where(d => d.AuditValue == PlatformSettingAuditValuePolicy.ByShape)
            .Select(d => d.Key);

        undeclared.Should().BeEmpty("a new free-text setting must state whether its values may be audited");
    }

    [Fact]
    public void Only_the_sender_address_is_an_approved_email_field_and_the_credential_identifier_and_display_name_are_omitted()
    {
        var registry = new PlatformSettingDefinitionRegistry();

        registry.GetAll().Where(d => d.AuditValue == PlatformSettingAuditValuePolicy.RecordOperationalEmail).Select(d => d.Key)
            .Should().Equal(PlatformSettingKeys.NotificationsEmailFromAddress);
        PlatformSettingAuditValues.RecordsValue(registry.TryGet(PlatformSettingKeys.NotificationsEmailSmtpUser)!).Should().BeFalse();
        PlatformSettingAuditValues.RecordsValue(registry.TryGet(PlatformSettingKeys.NotificationsEmailFromDisplayName)!).Should().BeFalse();
        PlatformSettingAuditValues.RecordsValue(registry.TryGet(PlatformSettingKeys.NotificationsEmailFromAddress)!).Should().BeTrue();
        PlatformSettingAuditValues.RecordsValue(registry.TryGet(PlatformSettingKeys.NotificationsEmailSmtpHost)!).Should().BeTrue();
    }

    [Fact]
    public void The_value_of_no_sensitive_setting_in_any_registry_is_recorded()
    {
        var sensitive = new PlatformSettingDefinitionRegistry().GetAll()
            .Concat(PlatformSettingDemoDefinitionRegistry.GetAll())
            .Where(d => d.IsSensitive)
            .ToList();

        sensitive.Should().NotBeEmpty();
        sensitive.Should().OnlyContain(d => !PlatformSettingAuditValues.RecordsValue(d));
    }

    [Theory]
    [InlineData(IntegrationSettingValueKind.String, false)]
    [InlineData(IntegrationSettingValueKind.MultilineString, false)]
    [InlineData(IntegrationSettingValueKind.Uri, false)]
    [InlineData(IntegrationSettingValueKind.TimeZone, false)]
    [InlineData(IntegrationSettingValueKind.Decimal, false)]
    [InlineData(IntegrationSettingValueKind.SensitiveText, false)]
    [InlineData(IntegrationSettingValueKind.Boolean, true)]
    [InlineData(IntegrationSettingValueKind.Integer, true)]
    [InlineData(IntegrationSettingValueKind.Duration, true)]
    [InlineData(IntegrationSettingValueKind.Choice, true)]
    public void A_module_declared_setting_records_its_values_only_when_its_kind_bounds_them(IntegrationSettingValueKind kind, bool records)
    {
        var declared = new IntegrationSettingDefinition
        {
            Key = "Module.Setting",
            DisplayName = "Module setting",
            Description = "A module-declared setting.",
            ValueKind = kind,
            AllowedValues = kind == IntegrationSettingValueKind.Choice ? [new IntegrationSettingAllowedValue("a", "A")] : [],
        };

        var mapped = IntegrationConfigurationDeclarations.ToPlatformSettingDefinition(declared, "module-1");

        PlatformSettingAuditValues.RecordsValue(mapped).Should().Be(records);
    }

    // ---- the services apply it, identically ----

    [Theory]
    [InlineData(Service.Platform)]
    [InlineData(Service.Integration)]
    public async Task The_sender_address_is_recorded_on_set_and_reset(Service service)
    {
        var subject = Build(service, RealDefinition(PlatformSettingKeys.NotificationsEmailFromAddress));

        await subject.SetAsync("ops@example.test", null);
        await subject.SetAsync("ops2@example.test", subject.RowVersion());
        await subject.ResetAsync();

        var events = subject.Audit.Events.Select(e => e.Event).ToList();
        events[0].Fields["newValue"].Should().Be("ops@example.test");
        events[0].Fields.Should().NotContainKey("previousValue");
        events[1].Fields["previousValue"].Should().Be("ops@example.test");
        events[1].Fields["newValue"].Should().Be("ops2@example.test");
        events[2].Fields["previousValue"].Should().Be("ops2@example.test");
    }

    [Theory]
    [InlineData(Service.Platform)]
    [InlineData(Service.Integration)]
    public async Task The_smtp_username_is_audited_by_name_and_action_only_even_when_it_is_an_email_address(Service service)
    {
        var subject = Build(service, RealDefinition(PlatformSettingKeys.NotificationsEmailSmtpUser));

        await subject.SetAsync("smtp-user@example.test", null);
        await subject.SetAsync("other-user@example.test", subject.RowVersion());
        await subject.ResetAsync();

        subject.Audit.Actions.Should().HaveCount(3);
        var events = subject.Audit.Events.Select(e => e.Event).ToList();
        events[0].Fields["key"].Should().Be(PlatformSettingKeys.NotificationsEmailSmtpUser);
        events[0].Fields["operation"].Should().Be("set");
        events[0].Fields["sensitive"].Should().Be(false, "the setting keeps its functional classification");
        events.Should().OnlyContain(e => !e.Fields.ContainsKey("previousValue") && !e.Fields.ContainsKey("newValue"));
        AssertNoText(subject.Audit, "smtp-user@example.test", "other-user@example.test", "example.test");
    }

    [Theory]
    [InlineData(Service.Platform)]
    [InlineData(Service.Integration)]
    public async Task An_email_address_in_a_free_text_setting_that_declares_no_policy_does_not_reach_the_audit_row(Service service)
    {
        // IsSensitive is false and nothing says the value may be recorded: it is not.
        var subject = Build(service, Def(PlatformSettingValueType.String));

        await subject.SetAsync(Email, null);
        await subject.SetAsync("else@example.test", subject.RowVersion());
        await subject.ResetAsync();

        subject.Audit.Actions.Should().HaveCount(3);
        subject.Audit.Events.Select(e => e.Event).Should().OnlyContain(e => !e.Fields.ContainsKey("previousValue") && !e.Fields.ContainsKey("newValue"));
        AssertNoText(subject.Audit, "example.test");
    }

    [Theory]
    [InlineData(Service.Platform)]
    [InlineData(Service.Integration)]
    public async Task A_setting_shaped_to_be_bounded_still_records_its_values(Service service)
    {
        var subject = Build(service, Def(PlatformSettingValueType.Integer, minInteger: 1));

        await subject.SetAsync("5", null);
        await subject.SetAsync("7", subject.RowVersion());
        await subject.ResetAsync();

        var events = subject.Audit.Events.Select(e => e.Event).ToList();
        events[0].Fields["newValue"].Should().Be("5");
        events[1].Fields["previousValue"].Should().Be("5");
        events[1].Fields["newValue"].Should().Be("7");
        events[2].Fields["previousValue"].Should().Be("7");
    }

    [Theory]
    [InlineData(Service.Platform)]
    [InlineData(Service.Integration)]
    public async Task A_sensitive_setting_stays_value_free_even_when_its_definition_declares_a_recording_policy(Service service)
    {
        var secret = "hunter2-" + Email;
        var subject = Build(
            service,
            Def(PlatformSettingValueType.String, PlatformSettingAuditValuePolicy.RecordOperationalEmail, sensitive: true));

        await subject.ReplaceSecretAsync(secret, null);
        await subject.ReplaceSecretAsync(secret + "-2", subject.RowVersion());
        await subject.ResetAsync();

        var events = subject.Audit.Events.Select(e => e.Event).ToList();
        events.Where(e => e.Fields.ContainsKey("sensitive")).Should().OnlyContain(e => (bool)e.Fields["sensitive"]!);
        events.Should().OnlyContain(e => !e.Fields.ContainsKey("previousValue") && !e.Fields.ContainsKey("newValue"));
        AssertNoText(subject.Audit, "hunter2", "example.test");
    }

    [Fact]
    public async Task Both_services_record_exactly_the_same_values_for_the_same_definition()
    {
        foreach (var definition in new[]
                 {
                     RealDefinition(PlatformSettingKeys.NotificationsEmailFromAddress),
                     RealDefinition(PlatformSettingKeys.NotificationsEmailSmtpUser),
                     RealDefinition(PlatformSettingKeys.NotificationsEmailSmtpHost),
                     Def(PlatformSettingValueType.String),
                 })
        {
            var recorded = new List<IReadOnlyList<string>>();
            foreach (var service in new[] { Service.Platform, Service.Integration })
            {
                var subject = Build(service, definition);
                await subject.SetAsync("first@example.test", null);
                await subject.SetAsync("second@example.test", subject.RowVersion());
                await subject.ResetAsync();
                recorded.Add(subject.Audit.Events
                    .SelectMany(e => e.Event.Fields.Where(f => f.Key is "previousValue" or "newValue").Select(f => $"{e.Event.Action.Split('.')[^1]}:{f.Key}={f.Value}"))
                    .ToList());
            }

            recorded[1].Should().Equal(recorded[0], $"the {definition.Key} policy is the definition's, not the service's");
        }
    }

    // ---- a malformed override can still be reset ----

    [Theory]
    [InlineData(Service.Platform)]
    [InlineData(Service.Integration)]
    public async Task A_malformed_ordinary_override_is_reset_and_its_unreadable_value_is_left_out_of_the_audit_row(Service service)
    {
        var subject = Build(service, Def(PlatformSettingValueType.Integer), seedJson: "\"not-an-integer\"");

        var act = () => subject.ResetAsync();

        await act.Should().NotThrowAsync("an operator must be able to remove a value that cannot even be read");
        subject.Audit.Actions.Should().ContainSingle().Which.Should().EndWith(".reset");
        subject.Audit.Last.Fields["sensitive"].Should().Be(false);
        subject.Audit.Last.Fields.Should().NotContainKey("previousValue");
        (await subject.StoredAsync()).Should().BeNull("the override is gone");
    }

    [Theory]
    [InlineData(Service.Platform)]
    [InlineData(Service.Integration)]
    public async Task A_valid_override_still_records_its_previous_value_on_reset(Service service)
    {
        var subject = Build(service, Def(PlatformSettingValueType.Integer), seedJson: "42");

        await subject.ResetAsync();

        subject.Audit.Last.Fields["previousValue"].Should().Be("42");
    }

    // Integration only: the platform service's notification safety check lists every resolved setting, and the resolver throws on
    // a malformed stored value, so a platform set over one fails there. That is resolver behaviour this remediation leaves alone.
    [Fact]
    public async Task A_malformed_integration_override_can_be_overwritten_and_the_new_value_is_recorded()
    {
        var subject = Build(Service.Integration, Def(PlatformSettingValueType.Integer), seedJson: "\"not-an-integer\"");

        await subject.SetAsync("9", subject.RowVersion());

        subject.Audit.Last.Fields["newValue"].Should().Be("9");
        subject.Audit.Last.Fields.Should().NotContainKey("previousValue");
    }

    [Theory]
    [InlineData(Service.Platform)]
    [InlineData(Service.Integration)]
    public async Task A_malformed_sensitive_override_is_still_reset_without_recording_anything_about_its_value(Service service)
    {
        var subject = Build(service, Def(PlatformSettingValueType.String, sensitive: true), seedJson: "{ not json");

        await subject.ResetAsync();

        subject.Audit.Last.Fields["sensitive"].Should().Be(true);
        subject.Audit.Last.Fields.Should().NotContainKey("previousValue");
        (await subject.StoredAsync()).Should().BeNull();
    }

    // ---- the response read follows a known commit ----

    [Theory]
    [InlineData(Service.Platform, false)]
    [InlineData(Service.Platform, true)]
    [InlineData(Service.Integration, false)]
    [InlineData(Service.Integration, true)]
    public async Task A_failed_response_read_after_a_commit_is_reported_as_an_unavailable_result_not_as_an_error(
        Service service, bool lostAcknowledgement)
    {
        var subject = Build(service, Def(PlatformSettingValueType.Integer));
        if (lostAcknowledgement)
            subject.Audit.Unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;
        subject.Reads.Fail = true;

        var act = () => subject.SetAsync("5", null);

        var thrown = (await act.Should().ThrowAsync<AuditCommittedResultUnavailableException>()).Which;
        thrown.OperationEventId.Should().NotBeNullOrEmpty();
        thrown.InnerException.Should().BeOfType<InvalidOperationException>();
        subject.Audit.Unit.Staged.Should().ContainSingle("the change committed once and was not rerun");
        subject.Audit.Unit.Operations.Should().ContainSingle();
        (await subject.StoredAsync()).Should().NotBeNull("the change is applied");
    }

    [Theory]
    [InlineData(Service.Platform)]
    [InlineData(Service.Integration)]
    public async Task A_failed_response_read_after_a_reset_is_reported_as_an_unavailable_result(Service service)
    {
        var subject = Build(service, Def(PlatformSettingValueType.Integer), seedJson: "3");
        subject.Reads.Fail = true;

        var act = () => subject.ResetAsync();

        await act.Should().ThrowAsync<AuditCommittedResultUnavailableException>();
        (await subject.StoredAsync()).Should().BeNull();
    }

    [Theory]
    [InlineData(Service.Platform)]
    [InlineData(Service.Integration)]
    public async Task A_failed_read_after_a_change_that_committed_nothing_is_an_ordinary_failure(Service service)
    {
        var subject = Build(service, Def(PlatformSettingValueType.Integer), seedJson: "3");
        subject.Reads.Fail = true;

        var act = () => subject.SetAsync("3", subject.RowVersion());

        // Exactly InvalidOperationException: the read failed, and no change was committed that could be reported as applied.
        await act.Should().ThrowExactlyAsync<InvalidOperationException>();
        subject.Audit.Actions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Service.Platform, false)]
    [InlineData(Service.Platform, true)]
    [InlineData(Service.Integration, false)]
    [InlineData(Service.Integration, true)]
    public async Task The_response_is_the_settings_current_state_including_a_later_writers_value(
        Service service, bool lostAcknowledgement)
    {
        var subject = Build(service, Def(PlatformSettingValueType.Integer));
        if (lostAcknowledgement)
            subject.Audit.Unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        // Another request commits its own change between this operation's commit and the response read.
        subject.Reads.BeforeRead = () => subject.WriteLaterAsync("99");

        var response = await subject.SetAsync("5", null);

        response.CurrentValue.Should().Be(99, "the response is the current state, not a snapshot of what this operation produced");
        subject.Audit.Events.Single().Event.Fields["newValue"].Should().Be("5", "the audit row records what this operation did");
    }

    // ---- harness ----

    private static PlatformSettingDefinition Def(
        PlatformSettingValueType type,
        PlatformSettingAuditValuePolicy policy = PlatformSettingAuditValuePolicy.ByShape,
        bool sensitive = false,
        IReadOnlyList<PlatformSettingAllowedValue>? allowed = null,
        string? provider = null,
        int? minInteger = null) =>
        new()
        {
            Key = "Policy.Test.Setting",
            DisplayName = "Policy test setting",
            Description = "A setting for the audit value policy tests.",
            Category = PlatformSettingCategory.Notifications,
            ValueType = type,
            InputKind = sensitive ? PlatformSettingInputKind.Secret : PlatformSettingInputKind.Text,
            ConfigurationPath = "Policy:Test:Setting",
            DefaultValue = type switch
            {
                PlatformSettingValueType.Boolean => false,
                PlatformSettingValueType.Integer => 0,
                PlatformSettingValueType.Duration => TimeSpan.Zero,
                PlatformSettingValueType.StringArray => Array.Empty<string>(),
                _ => string.Empty,
            },
            AllowedValues = allowed,
            OptionProviderKey = provider,
            MinInteger = minInteger,
            IsSensitive = sensitive,
            AuditValue = policy,
            AllowedValueScopes = [PlatformSettingValueScopeKind.Instance, PlatformSettingValueScopeKind.Integration],
        };

    private static PlatformSettingDefinition RealDefinition(string key) =>
        new PlatformSettingDefinitionRegistry().TryGet(key)! with
        {
            AllowedValueScopes = [PlatformSettingValueScopeKind.Instance, PlatformSettingValueScopeKind.Integration],
        };

    private static void AssertNoText(AuditRecording audit, params string[] text)
    {
        var recorded = string.Join(
            "|",
            audit.Events.SelectMany(e => e.Event.Fields.Values.Select(v => v?.ToString())
                .Append(e.Event.Target?.Id)
                .Append(JsonSerializer.Serialize(e.Event.Fields))));
        foreach (var item in text)
            recorded.Should().NotContain(item);
    }

    private static Subject Build(Service service, PlatformSettingDefinition definition, string? seedJson = null) =>
        new(service, definition, seedJson);

    private sealed class Subject
    {
        private readonly Service _service;
        private readonly PlatformSettingDefinition _definition;
        private readonly PlatformSettingRepositoryFake _repository;
        private readonly PlatformSettingsService? _platform;
        private readonly IntegrationSettingsService? _integration;

        public Subject(Service service, PlatformSettingDefinition definition, string? seedJson)
        {
            _service = service;
            _definition = definition;
            Audit = new AuditRecording();
            var (scopeKind, scopeId) = service == Service.Platform
                ? (PlatformSettingScopeConstants.InstanceKind, PlatformSettingScopeConstants.InstanceId)
                : (PlatformSettingScopeConstants.IntegrationKind, IntegrationId);
            _repository = seedJson is null
                ? new PlatformSettingRepositoryFake()
                : new PlatformSettingRepositoryFake(new PlatformSettingValue
                {
                    Key = definition.Key,
                    ScopeKind = scopeKind,
                    ScopeId = scopeId,
                    ValueJson = seedJson,
                    RowVersion = [1],
                });

            var secrets = new PlatformSettingSecretMaterialProvisioner(new InMemorySecretStore());
            var configuration = new ConfigurationBuilder().Build();
            Reads = new ReadGate();
            if (service == Service.Platform)
            {
                var registry = new PlatformSettingOptionProviderTestHarness.SingleDefinitionRegistry(definition);
                Reads.Platform = new PlatformSettingsResolver(registry, _repository, configuration);
                _platform = new PlatformSettingsService(
                    registry,
                    _repository,
                    Reads,
                    PlatformSettingOptionProviderTestHarness.CreateCoordinator(new Mock<IPlatformSettingOptionProvider>().Object),
                    secrets,
                    Audit);
            }
            else
            {
                var registry = new SingleIntegrationRegistry(definition);
                Reads.Integration = new IntegrationSettingsResolver(registry, _repository);
                _integration = new IntegrationSettingsService(
                    registry,
                    _repository,
                    Reads,
                    PlatformSettingOptionProviderTestHarness.CreateCoordinator(new Mock<IPlatformSettingOptionProvider>().Object),
                    secrets,
                    ExistingIntegration(),
                    Audit);
            }
        }

        public AuditRecording Audit { get; }

        public ReadGate Reads { get; }

        /// <summary>
        /// The integration the service changes settings of. A change takes the integration row lock and requires the row
        /// (AUD-1.4 slice 3), so the repository answers for it on both the plain and the locking read.
        /// </summary>
        private static Apperture.Core.Application.Services.IIntegrationRepository ExistingIntegration()
        {
            ValueTask<Apperture.Core.Domain.Integration.Integration?> Find(Apperture.Core.Domain.Integration.IntegrationId id) =>
                ValueTask.FromResult<Apperture.Core.Domain.Integration.Integration?>(
                    new Apperture.Core.Domain.Integration.Integration(
                        id,
                        "Test Integration",
                        "mod-1",
                        null,
                        Apperture.Core.Domain.Integration.IntegrationState.Active,
                        null,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow));

            var integrations = new Mock<Apperture.Core.Application.Services.IIntegrationRepository>();
            integrations.Setup(x => x.GetByIdAsync(It.IsAny<Apperture.Core.Domain.Integration.IntegrationId>(), It.IsAny<CancellationToken>()))
                .Returns((Apperture.Core.Domain.Integration.IntegrationId id, CancellationToken _) => Find(id));
            integrations.Setup(x => x.GetByIdForUpdateAsync(It.IsAny<Apperture.Core.Domain.Integration.IntegrationId>(), It.IsAny<CancellationToken>()))
                .Returns((Apperture.Core.Domain.Integration.IntegrationId id, CancellationToken _) => Find(id));
            return integrations.Object;
        }

        public async Task<PlatformSettingResolvedValue> SetAsync(string value, string? rowVersion) =>
            _service == Service.Platform
                ? await _platform!.SetAsync(_definition.Key, new PlatformSettingUpdatePayload { ScalarValue = value }, rowVersion)
                : await _integration!.SetAsync(IntegrationId, _definition.Key, new PlatformSettingUpdatePayload { ScalarValue = value }, rowVersion);

        public async Task ReplaceSecretAsync(string secret, string? rowVersion)
        {
            var payload = new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = secret };
            if (_service == Service.Platform)
                await _platform!.SetAsync(_definition.Key, payload, rowVersion);
            else
                await _integration!.SetAsync(IntegrationId, _definition.Key, payload, rowVersion);
        }

        public async Task ResetAsync()
        {
            if (_service == Service.Platform)
                await _platform!.ResetAsync(_definition.Key);
            else
                await _integration!.ResetAsync(IntegrationId, _definition.Key);
        }

        public string? RowVersion() => Convert.ToBase64String(Stored()?.RowVersion ?? [1]);

        public Task<PlatformSettingValue?> StoredAsync() => Task.FromResult(Stored());

        /// <summary>Another request's committed write of the setting, directly in the store.</summary>
        public async Task WriteLaterAsync(string valueJson)
        {
            var (scopeKind, scopeId) = _service == Service.Platform
                ? (PlatformSettingScopeConstants.InstanceKind, PlatformSettingScopeConstants.InstanceId)
                : (PlatformSettingScopeConstants.IntegrationKind, IntegrationId);
            await _repository.UpsertAsync(
                new PlatformSettingValue { Key = _definition.Key, ScopeKind = scopeKind, ScopeId = scopeId, ValueJson = valueJson, RowVersion = [2] },
                null);
        }

        private PlatformSettingValue? Stored() =>
            (_service == Service.Platform
                ? _repository.GetByKeyAsync(_definition.Key)
                : _repository.GetScopedAsync(_definition.Key, PlatformSettingScopeConstants.IntegrationKind, IntegrationId)).GetAwaiter().GetResult();
    }

    /// <summary>Answers what a change reads inside its unit, and can fail or race the response read that follows the commit.</summary>
    private sealed class ReadGate : IPlatformSettingsResolver, IIntegrationSettingsResolver
    {
        public IPlatformSettingsResolver? Platform { get; set; }
        public IIntegrationSettingsResolver? Integration { get; set; }
        public bool Fail { get; set; }
        public Func<Task>? BeforeRead { get; set; }

        public Task<IReadOnlyList<PlatformSettingResolvedValue>> ListResolvedAsync(CancellationToken cancellationToken = default) =>
            Platform!.ListResolvedAsync(cancellationToken);

        public Task<IReadOnlyList<PlatformSettingResolvedValue>> ListResolvedAsync(string integrationId, CancellationToken cancellationToken = default) =>
            Integration!.ListResolvedAsync(integrationId, cancellationToken);

        public Task<object?> ResolveRawValueAsync(string key, CancellationToken cancellationToken = default) =>
            Platform!.ResolveRawValueAsync(key, cancellationToken);

        public async Task<PlatformSettingResolvedValue?> GetResolvedAsync(string key, CancellationToken cancellationToken = default)
        {
            await GateAsync();
            return await Platform!.GetResolvedAsync(key, cancellationToken);
        }

        public async Task<PlatformSettingResolvedValue?> GetResolvedAsync(string integrationId, string key, CancellationToken cancellationToken = default)
        {
            await GateAsync();
            return await Integration!.GetResolvedAsync(integrationId, key, cancellationToken);
        }

        private async Task GateAsync()
        {
            if (Fail)
                throw new InvalidOperationException("Injected: the response read failed.");
            if (BeforeRead is { } before)
            {
                BeforeRead = null;
                await before();
            }
        }
    }

    private sealed class SingleIntegrationRegistry(PlatformSettingDefinition definition) : IIntegrationSettingDefinitionRegistry
    {
        public IReadOnlyList<PlatformSettingDefinition> GetAll() => [definition];

        public PlatformSettingDefinition? TryGet(string key) =>
            string.Equals(key, definition.Key, StringComparison.Ordinal) ? definition : null;
    }
}
