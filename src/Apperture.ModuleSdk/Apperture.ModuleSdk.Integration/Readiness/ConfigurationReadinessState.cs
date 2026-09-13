namespace Apperture.ModuleSdk.Integration.Readiness;

/// <summary>Overall configuration readiness state (not a single boolean).</summary>
public enum ConfigurationReadinessState
{
    /// <summary>All required conditions satisfied.</summary>
    Ready = 0,

    /// <summary>One or more blocking conditions prevent execution.</summary>
    NotReady = 1,

    /// <summary>Required conditions satisfied; optional dependencies unhealthy.</summary>
    Degraded = 2,
}
