using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Integration;

/// <summary>
/// Policy seam for mapping connection verification state to readiness issue severity.
/// Epic 59 keeps all verification-derived issues advisory.
/// </summary>
public interface IConnectionVerificationReadinessPolicy
{
    IntegrationReadinessIssueSeverity ResolveSeverity(
        string moduleId,
        string slotName,
        ConnectionVerificationStatus verificationStatus);
}
