using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Connector;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Epic 59 baseline: verification issues are explicit but non-blocking.
/// </summary>
public sealed class AdvisoryConnectionVerificationReadinessPolicy : IConnectionVerificationReadinessPolicy
{
    public IntegrationReadinessIssueSeverity ResolveSeverity(string moduleId, string slotName, ConnectionVerificationStatus verificationStatus)
        => IntegrationReadinessIssueSeverity.Advisory;
}
