using System.Data;
using Apperture.Connectors.CampusNexus;
using Apperture.ModuleSdk.Integration.Configuration;

namespace Proof.CampusNexus.StudentReview;

internal static class ProofIntegrationConfiguration
{
    internal static IReadOnlyList<IntegrationSettingDefinition> Settings { get; } = [];

    internal static IReadOnlyList<ConnectionRequirementDefinition> Connections { get; } =
    [
        CnsApi(),
        IntegrationDb(),
    ];

    internal static IReadOnlyList<ProcedureRequirementSet> Procedures { get; } =
    [
        new ProcedureRequirementSet
        {
            ProcedureId = ProofCampusNexusStudentReviewConstants.ProcedureSyncRecentStudents,
            RequiredConnectionKeys =
            [
                ProofCampusNexusStudentReviewConstants.SlotCnsApi,
                ProofCampusNexusStudentReviewConstants.SlotIntegrationDb,
            ],
        },
    ];

    private static ConnectionRequirementDefinition CnsApi() =>
        new()
        {
            Key = ProofCampusNexusStudentReviewConstants.SlotCnsApi,
            DisplayName = "Campus Nexus API",
            Description = "Campus Nexus OData API client for student review sync.",
            IsRequired = true,
            CapabilityContractType = typeof(ICampusNexusApiClient).FullName!,
            CapabilityKind = ConnectionCapabilityKinds.HttpApi,
            VerificationExpectation = ConnectionVerificationExpectation.Advisory,
            Order = 1,
        };

    private static ConnectionRequirementDefinition IntegrationDb() =>
        new()
        {
            Key = ProofCampusNexusStudentReviewConstants.SlotIntegrationDb,
            DisplayName = "Integration database",
            Description = "SQL database for proof student review persistence.",
            IsRequired = true,
            CapabilityContractType = typeof(IDbConnection).FullName!,
            CapabilityKind = ConnectionCapabilityKinds.RelationalDb,
            VerificationExpectation = ConnectionVerificationExpectation.Advisory,
            Order = 2,
        };
}
