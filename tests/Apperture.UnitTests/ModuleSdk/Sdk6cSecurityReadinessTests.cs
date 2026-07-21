using Apperture.ModuleSdk.Integration.Configuration;
using Apperture.ModuleSdk.Integration.Readiness;
using Apperture.ModuleSdk.Integration.Testing;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ModuleSdk;

public sealed class Sdk6cSecurityReadinessTests
{
    private const string SecretPlaintext = "super-secret-api-key-value";

    private sealed class SensitiveSettingContributor : IIntegrationConfigurationContributor
    {
        public IReadOnlyList<IntegrationSettingDefinition> GetSettingDefinitions() =>
        [
            new IntegrationSettingDefinition
            {
                Key = "ApiKey",
                DisplayName = "API key",
                Description = "Sensitive API key.",
                ValueKind = IntegrationSettingValueKind.SensitiveText,
                IsRequired = true,
            },
        ];

        public IReadOnlyList<ConnectionRequirementDefinition> GetConnectionRequirements() => [];

        public IReadOnlyList<ProcedureRequirementSet> GetProcedureRequirements() => [];
    }

    [Fact]
    public void Readiness_messages_never_include_secret_plaintext()
    {
        var ctx = new IntegrationConfigurationTestContext()
            .WithSecret("ApiKey", SecretPlaintext);

        var readiness = ctx.EvaluateIntegration(new SensitiveSettingContributor());
        SecretLeakageAssertions.AssertSecretAbsentFromReadiness(SecretPlaintext, readiness);
    }

    [Fact]
    public void Missing_secret_readiness_uses_presence_only_language()
    {
        var readiness = new IntegrationConfigurationTestContext().EvaluateIntegration(new SensitiveSettingContributor());

        readiness.IsReady.Should().BeFalse();
        readiness.Reasons.Should().Contain(r => r.Code == ConfigurationReadinessReasonCodes.MissingSecret);
        foreach (var reason in readiness.Reasons)
            reason.Message.Should().NotContain(SecretPlaintext);
    }
}
