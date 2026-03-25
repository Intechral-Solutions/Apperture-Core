using Apperture.Core.Application.Connector;
using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Services;

public interface IConnectionVerifier
{
    ConnectorDefinitionId ConnectorDefinitionId { get; }

    ValueTask<ConnectionVerificationResult> VerifyAsync(
        Connection connection,
        CancellationToken cancellationToken = default);
}
