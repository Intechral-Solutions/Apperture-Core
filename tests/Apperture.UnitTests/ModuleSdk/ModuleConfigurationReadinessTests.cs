using Apperture.ModuleSdk.Integration.Configuration;
using Apperture.ModuleSdk.Integration.Readiness;
using Apperture.ModuleSdk.Integration.Testing;
using FluentAssertions;
using Proof.CampusNexus.StudentReview;
using Sample.ApiPolling;
using Sample.ApiPolling.Contracts;
using Sample.FileImport;
using Sample.HelloWorld;
using Unisa.CampusNexus.PaymentPlans;
using Xunit;

namespace Apperture.UnitTests.ModuleSdk;

public sealed class ModuleConfigurationReadinessTests
{
    [Fact]
    public void HelloWorld_ready_with_no_config()
    {
        var module = new HelloWorldModule();
        var result = new IntegrationConfigurationTestContext().EvaluateIntegration(module);

        result.State.Should().Be(ConfigurationReadinessState.Ready);
        result.Reasons.Should().BeEmpty();
    }

    [Fact]
    public void ApiPolling_missing_connection_reported()
    {
        var module = new ApiPollingModule();
        var result = new IntegrationConfigurationTestContext().EvaluateIntegration(module);

        result.State.Should().Be(ConfigurationReadinessState.NotReady);
        result.Reasons.Should().ContainSingle(r =>
            r.Code == ConfigurationReadinessReasonCodes.MissingConnectionBinding
            && r.ConnectionKey == ApiPollingConnectionKeys.DirectoryApi);
    }

    [Fact]
    public void ApiPolling_ready_when_directory_api_bound()
    {
        var module = new ApiPollingModule();
        var result = new IntegrationConfigurationTestContext()
            .WithConnectionBinding(ApiPollingConnectionKeys.DirectoryApi, new EmbeddedSampleDirectoryApiClient())
            .EvaluateIntegration(module);

        result.State.Should().Be(ConfigurationReadinessState.Ready);
    }

    [Fact]
    public void Proof_missing_cns_and_integration_db_independently()
    {
        var module = new ProofCampusNexusStudentReviewModule();
        var result = new IntegrationConfigurationTestContext().EvaluateIntegration(module);

        result.State.Should().Be(ConfigurationReadinessState.NotReady);
        result.Reasons.Should().Contain(r =>
            r.Code == ConfigurationReadinessReasonCodes.MissingConnectionBinding
            && r.ConnectionKey == ProofCampusNexusStudentReviewConstants.SlotCnsApi);
        result.Reasons.Should().Contain(r =>
            r.Code == ConfigurationReadinessReasonCodes.MissingConnectionBinding
            && r.ConnectionKey == ProofCampusNexusStudentReviewConstants.SlotIntegrationDb);
    }

    [Fact]
    public void Unisa_missing_connections_reported_independently()
    {
        var module = new UnisaCampusNexusPaymentPlansModule();
        var result = new IntegrationConfigurationTestContext().EvaluateIntegration(module);

        result.State.Should().Be(ConfigurationReadinessState.NotReady);
        result.Reasons.Should().Contain(r =>
            r.ConnectionKey == UnisaCampusNexusPaymentPlansConstants.SlotCnsApi);
        result.Reasons.Should().Contain(r =>
            r.ConnectionKey == UnisaCampusNexusPaymentPlansConstants.SlotSftp);
        result.Reasons.Should().Contain(r =>
            r.ConnectionKey == UnisaCampusNexusPaymentPlansConstants.SlotIntegrationDb);
    }

    [Fact]
    public void Unisa_procedure_specific_readiness()
    {
        var module = new UnisaCampusNexusPaymentPlansModule();
        var ctx = new IntegrationConfigurationTestContext()
            .WithConnectionBinding(UnisaCampusNexusPaymentPlansConstants.SlotCnsApi, new object())
            .WithConnectionBinding(UnisaCampusNexusPaymentPlansConstants.SlotIntegrationDb, new object());

        var transOnly = ctx.EvaluateProcedure(
            module,
            UnisaCampusNexusPaymentPlansConstants.ProcedureProcessTransFiles);
        transOnly.IsExecutable.Should().BeTrue("API+DB procedure should not block on missing SFTP");
        transOnly.Reasons.Where(r => r.Severity == ConfigurationReadinessSeverity.Blocking)
            .Select(r => r.ConnectionKey)
            .Should().NotContain(UnisaCampusNexusPaymentPlansConstants.SlotCnsApi)
            .And.NotContain(UnisaCampusNexusPaymentPlansConstants.SlotIntegrationDb);

        var dailyFiles = ctx.EvaluateProcedure(
            module,
            UnisaCampusNexusPaymentPlansConstants.ProcedureProcessDailyFiles);
        dailyFiles.State.Should().Be(ConfigurationReadinessState.NotReady);
        dailyFiles.Reasons.Should().Contain(r =>
            r.Code == ConfigurationReadinessReasonCodes.MissingConnectionBinding
            && r.ConnectionKey == UnisaCampusNexusPaymentPlansConstants.SlotSftp);
    }

    [Fact]
    public void FileImport_ready_with_no_config()
    {
        var module = new FileImportModule();
        var result = new IntegrationConfigurationTestContext().EvaluateIntegration(module);

        result.State.Should().Be(ConfigurationReadinessState.Ready);
    }
}
