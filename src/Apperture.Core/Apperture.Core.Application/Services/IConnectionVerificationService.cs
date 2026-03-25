using Apperture.Core.Application.Connector;
using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Executes connector-specific connection verification on demand.
/// </summary>
public interface IConnectionVerificationService
{
    ValueTask<ConnectionVerificationResult> VerifyAsync(
        Connection connection,
        CancellationToken cancellationToken = default);
}
