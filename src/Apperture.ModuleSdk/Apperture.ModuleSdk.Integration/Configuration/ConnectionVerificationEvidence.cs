namespace Apperture.ModuleSdk.Integration.Configuration;

/// <summary>Normalized connection verification outcomes for readiness and operator surfaces.</summary>
public enum ConnectionVerificationStatusKind
{
    ConfiguredUnverified = 0,
    Verified = 1,
    InvalidCredentials = 2,
    EndpointUnreachable = 3,
    InvalidConfiguration = 4,
    UnsupportedVerification = 5,
    StaleAfterConfigurationChange = 6,
}

/// <summary>Safe verification evidence (no credentials or sensitive remote payloads).</summary>
public sealed record ConnectionVerificationEvidence(
    ConnectionVerificationStatusKind Status,
    string ReasonCode,
    string Summary,
    DateTimeOffset? VerifiedAtUtc,
    DateTimeOffset? EvaluatedAtUtc);
