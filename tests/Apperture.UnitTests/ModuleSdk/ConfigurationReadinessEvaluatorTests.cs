using Apperture.ModuleSdk.Integration.Configuration;
using Apperture.ModuleSdk.Integration.Readiness;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ModuleSdk;

public sealed class ConfigurationReadinessEvaluatorTests
{
    private static readonly DateTimeOffset EvaluatedAt = DateTimeOffset.Parse("2026-07-21T12:00:00Z");

    private sealed class TestContributor : IIntegrationConfigurationContributor
    {
        private readonly IReadOnlyList<IntegrationSettingDefinition> _settings;
        private readonly IReadOnlyList<ConnectionRequirementDefinition> _connections;
        private readonly IReadOnlyList<ProcedureRequirementSet> _procedures;

        public TestContributor(
            IReadOnlyList<IntegrationSettingDefinition>? settings = null,
            IReadOnlyList<ConnectionRequirementDefinition>? connections = null,
            IReadOnlyList<ProcedureRequirementSet>? procedures = null)
        {
            _settings = settings ?? Array.Empty<IntegrationSettingDefinition>();
            _connections = connections ?? Array.Empty<ConnectionRequirementDefinition>();
            _procedures = procedures ?? Array.Empty<ProcedureRequirementSet>();
        }

        public IReadOnlyList<IntegrationSettingDefinition> GetSettingDefinitions() => _settings;

        public IReadOnlyList<ConnectionRequirementDefinition> GetConnectionRequirements() => _connections;

        public IReadOnlyList<ProcedureRequirementSet> GetProcedureRequirements() => _procedures;
    }

    private static IntegrationSettingDefinition RequiredSetting(string key, bool sensitive = false) =>
        new()
        {
            Key = key,
            DisplayName = key,
            Description = key,
            ValueKind = sensitive ? IntegrationSettingValueKind.SensitiveText : IntegrationSettingValueKind.String,
            IsRequired = true,
        };

    private static ConnectionRequirementDefinition RequiredConnection(string key) =>
        new()
        {
            Key = key,
            DisplayName = key,
            Description = key,
            CapabilityContractType = "System.Data.IDbConnection",
            IsRequired = true,
        };

    [Fact]
    public void EvaluateIntegration_ready_when_no_required_dependencies()
    {
        var contributor = new TestContributor();
        var result = ConfigurationReadinessEvaluator.EvaluateIntegration(
            contributor.GetSettingDefinitions(),
            contributor.GetConnectionRequirements(),
            ConfigurationEvidenceSnapshot.Empty(),
            EvaluatedAt);

        result.IsReady.Should().BeTrue();
        result.Reasons.Should().BeEmpty();
    }

    [Fact]
    public void EvaluateIntegration_reports_missing_setting_and_secret()
    {
        var contributor = new TestContributor(
            settings:
            [
                RequiredSetting("PageSize"),
                RequiredSetting("ApiKey", sensitive: true),
            ]);

        var result = ConfigurationReadinessEvaluator.EvaluateIntegration(
            contributor.GetSettingDefinitions(),
            contributor.GetConnectionRequirements(),
            ConfigurationEvidenceSnapshot.Empty(),
            EvaluatedAt);

        result.IsReady.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.Code == ConfigurationReadinessReasonCodes.MissingSetting && r.SettingKey == "PageSize");
        result.Reasons.Should().Contain(r => r.Code == ConfigurationReadinessReasonCodes.MissingSecret && r.SettingKey == "ApiKey");
    }

    [Fact]
    public void EvaluateIntegration_reports_missing_binding_and_invalid_setting()
    {
        var contributor = new TestContributor(
            settings: [RequiredSetting("Endpoint")],
            connections: [RequiredConnection("IntegrationDb")]);

        var evidence = ConfigurationEvidenceSnapshot.Empty() with
        {
            SettingValues = new Dictionary<string, object?> { ["Endpoint"] = "" },
            InvalidSettingKeys = new HashSet<string>(StringComparer.Ordinal) { "Endpoint" },
        };

        var result = ConfigurationReadinessEvaluator.EvaluateIntegration(
            contributor.GetSettingDefinitions(),
            contributor.GetConnectionRequirements(),
            evidence,
            EvaluatedAt);

        result.Reasons.Should().Contain(r => r.Code == ConfigurationReadinessReasonCodes.MissingConnectionBinding);
        result.Reasons.Should().Contain(r => r.Code == ConfigurationReadinessReasonCodes.InvalidSetting);
    }

    [Fact]
    public void EvaluateProcedure_uses_procedure_subset()
    {
        var contributor = new TestContributor(
            settings: [RequiredSetting("LocalOnly"), RequiredSetting("ExportFlag")],
            connections: [RequiredConnection("IntegrationDb"), RequiredConnection("Sftp")],
            procedures:
            [
                new ProcedureRequirementSet
                {
                    ProcedureId = "local-fixture",
                    RequiredSettingKeys = ["LocalOnly"],
                    RequiredConnectionKeys = ["IntegrationDb"],
                },
            ]);

        var evidence = new ConfigurationEvidenceSnapshot(
            ModuleInstalled: true,
            IntegrationEnabled: true,
            SettingValues: new Dictionary<string, object?> { ["LocalOnly"] = "ok" },
            SecretPresence: new Dictionary<string, SecretPresenceState>(),
            InvalidSettingKeys: new HashSet<string>(StringComparer.Ordinal),
            Connections: new Dictionary<string, ConnectionEvidenceSnapshot>(StringComparer.Ordinal)
            {
                ["IntegrationDb"] = new ConnectionEvidenceSnapshot(
                    IsBound: true,
                    IsAvailable: true,
                    VerificationStatus: ConnectionVerificationStatusKind.Verified),
            },
            ModuleCheckFailures: Array.Empty<ModuleCheckFailure>());

        var local = ConfigurationReadinessEvaluator.EvaluateProcedure(
            "local-fixture",
            contributor.GetSettingDefinitions(),
            contributor.GetConnectionRequirements(),
            contributor.GetProcedureRequirements(),
            evidence,
            EvaluatedAt);
        local.IsExecutable.Should().BeTrue();

        var export = ConfigurationReadinessEvaluator.EvaluateProcedure(
            "export",
            contributor.GetSettingDefinitions(),
            contributor.GetConnectionRequirements(),
            contributor.GetProcedureRequirements(),
            evidence,
            EvaluatedAt);
        export.IsExecutable.Should().BeFalse();
        export.Reasons.Should().Contain(r => r.Code == ConfigurationReadinessReasonCodes.MissingSetting && r.SettingKey == "ExportFlag");
        export.Reasons.Should().Contain(r => r.Code == ConfigurationReadinessReasonCodes.MissingConnectionBinding && r.ConnectionKey == "Sftp");
    }

    [Fact]
    public void EvaluateIntegration_optional_dependency_unavailable_is_degraded_not_blocking()
    {
        var contributor = new TestContributor(
            connections:
            [
                new ConnectionRequirementDefinition
                {
                    Key = "Sftp",
                    DisplayName = "SFTP",
                    Description = "Optional",
                    CapabilityContractType = "Apperture.Connectors.Sftp.ISftpFileClient",
                    IsRequired = false,
                },
            ]);

        var result = ConfigurationReadinessEvaluator.EvaluateIntegration(
            contributor.GetSettingDefinitions(),
            contributor.GetConnectionRequirements(),
            ConfigurationEvidenceSnapshot.Empty(),
            EvaluatedAt);

        result.State.Should().Be(ConfigurationReadinessState.Degraded);
        result.Reasons.Should().ContainSingle(r =>
            r.Code == ConfigurationReadinessReasonCodes.OptionalDependencyUnavailable
            && r.Severity == ConfigurationReadinessSeverity.Advisory);
    }
}
