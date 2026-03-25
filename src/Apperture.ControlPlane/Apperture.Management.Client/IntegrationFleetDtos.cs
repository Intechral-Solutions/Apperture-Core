namespace Apperture.Management.Client;

/// <summary>Optional query for <see cref="IManagementClient.GetIntegrationFleetAsync"/>. Epic 61.</summary>
public sealed record IntegrationFleetQueryParams(
    string? State = null,
    bool? Runnable = null,
    bool? HasBlocking = null,
    bool? HasAdvisory = null,
    bool? VerificationConcern = null,
    string? Verification = null,
    string? Attention = null,
    string? Sort = null);

/// <summary>Epic 61: evidence-backed attention flags on fleet rows.</summary>
public sealed record IntegrationFleetAttentionFlagsDto(
    bool IsInactiveOperational,
    bool IsRetired,
    bool HasConfigBlockingReadiness,
    bool HasAdvisoryOnly,
    bool HasVerificationFailed,
    bool HasVerificationUnverified,
    bool HasVerificationUnavailable);

/// <summary>Epic 61: one row of GET integrations/fleet.</summary>
public sealed record IntegrationFleetRowDto(
    string Id,
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    string OperationalState,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IntegrationReadinessDto Readiness,
    IntegrationFleetAttentionFlagsDto Attention,
    string[] AttentionTokens,
    string[] TopIssueCodes);
