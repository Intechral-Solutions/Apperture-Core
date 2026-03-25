using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Connector;

public sealed record ConnectionVerificationResult(
    ConnectionVerificationStatus Status,
    DateTimeOffset VerifiedAtUtc,
    string Summary);
