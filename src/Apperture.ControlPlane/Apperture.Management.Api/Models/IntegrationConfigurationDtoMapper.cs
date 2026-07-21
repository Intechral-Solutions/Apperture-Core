using Apperture.Core.Application.Integration;
using Apperture.ModuleSdk.Integration.Configuration;

namespace Apperture.Management.Api.Models;

internal static class IntegrationConfigurationDtoMapper
{
    public static IntegrationConfigurationDeclarationsDto ToDto(ModuleIntegrationConfiguration configuration) =>
        new(
            configuration.ModuleId,
            configuration.Settings.Select(ToSettingDto).ToList(),
            configuration.Connections.Select(ToConnectionDto).ToList(),
            configuration.Procedures.Select(ToProcedureDto).ToList());

    public static IntegrationProcedureReadinessDto ToProcedureReadinessDto(IntegrationReadinessProjection readiness) =>
        new(
            readiness.IsRunnable,
            readiness.BlockingIssueCount,
            readiness.AdvisoryIssueCount,
            readiness.Issues.Select(ToIssueDto).ToList());

    private static IntegrationSettingDeclarationDto ToSettingDto(IntegrationSettingDefinition s) =>
        new(
            s.Key,
            s.DisplayName,
            s.Description,
            s.ValueKind.ToString(),
            s.IsRequired,
            s.IsSensitive,
            s.Order,
            s.Group);

    private static IntegrationConnectionRequirementDto ToConnectionDto(ConnectionRequirementDefinition c) =>
        new(
            c.Key,
            c.DisplayName,
            c.Description,
            c.IsRequired,
            c.CapabilityContractType,
            c.VerificationExpectation.ToString(),
            c.AllowMultipleBindings);

    private static IntegrationProcedureRequirementDto ToProcedureDto(ProcedureRequirementSet p) =>
        new(p.ProcedureId, p.RequiredSettingKeys, p.RequiredConnectionKeys);

    private static IntegrationReadinessIssueDto ToIssueDto(IntegrationReadinessIssue issue) =>
        new(
            issue.Code,
            issue.Message,
            issue.SlotName,
            issue.BindingId,
            issue.ConnectionId,
            issue.Severity);
}
