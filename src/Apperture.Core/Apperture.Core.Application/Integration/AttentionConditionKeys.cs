namespace Apperture.Core.Application.Integration;

/// <summary>
/// Epic 68: stable persisted keys for attention incidents (align with <see cref="IntegrationFleetAttention.Tokens"/> where applicable).
/// </summary>
public static class AttentionConditionKeys
{
    /// <summary>Schedule expectation: missing authorized schedule-origin run after expected fire (Epic 65/68).</summary>
    public const string ExpectedRunMissing = "expected_run_missing";
}
