using System.Data;
using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.Sftp;
using Apperture.ModuleSdk.Integration.Configuration;

namespace Unisa.CampusNexus.PaymentPlans;

internal static class UnisaIntegrationConfiguration
{
    internal static IReadOnlyList<IntegrationSettingDefinition> Settings { get; } = [];

    internal static IReadOnlyList<ConnectionRequirementDefinition> Connections { get; } =
    [
        CnsApi(),
        Sftp(),
        IntegrationDb(),
    ];

    internal static IReadOnlyList<ProcedureRequirementSet> Procedures { get; } =
    [
        new ProcedureRequirementSet
        {
            ProcedureId = UnisaCampusNexusPaymentPlansConstants.ProcedureProcessDailyFiles,
            RequiredConnectionKeys =
            [
                UnisaCampusNexusPaymentPlansConstants.SlotCnsApi,
                UnisaCampusNexusPaymentPlansConstants.SlotSftp,
                UnisaCampusNexusPaymentPlansConstants.SlotIntegrationDb,
            ],
        },
        new ProcedureRequirementSet
        {
            ProcedureId = UnisaCampusNexusPaymentPlansConstants.ProcedureProcessTransFiles,
            RequiredConnectionKeys =
            [
                UnisaCampusNexusPaymentPlansConstants.SlotCnsApi,
                UnisaCampusNexusPaymentPlansConstants.SlotIntegrationDb,
            ],
        },
    ];

    private static ConnectionRequirementDefinition CnsApi() =>
        new()
        {
            Key = UnisaCampusNexusPaymentPlansConstants.SlotCnsApi,
            DisplayName = "Campus Nexus API",
            Description = "Campus Nexus API client for payment plan sync.",
            IsRequired = true,
            CapabilityContractType = typeof(ICampusNexusApiClient).FullName!,
            CapabilityKind = ConnectionCapabilityKinds.HttpApi,
            VerificationExpectation = ConnectionVerificationExpectation.Advisory,
            Order = 1,
        };

    private static ConnectionRequirementDefinition Sftp() =>
        new()
        {
            Key = UnisaCampusNexusPaymentPlansConstants.SlotSftp,
            DisplayName = "SFTP",
            Description = "SFTP file client for inbound payment plan file download.",
            IsRequired = true,
            CapabilityContractType = typeof(ISftpFileClient).FullName!,
            CapabilityKind = ConnectionCapabilityKinds.Sftp,
            VerificationExpectation = ConnectionVerificationExpectation.Advisory,
            Order = 2,
        };

    private static ConnectionRequirementDefinition IntegrationDb() =>
        new()
        {
            Key = UnisaCampusNexusPaymentPlansConstants.SlotIntegrationDb,
            DisplayName = "Integration database",
            Description = "SQL database for UNISA payment plan mapping and import state.",
            IsRequired = true,
            CapabilityContractType = typeof(IDbConnection).FullName!,
            CapabilityKind = ConnectionCapabilityKinds.RelationalDb,
            VerificationExpectation = ConnectionVerificationExpectation.Advisory,
            Order = 3,
        };
}
