namespace Apperture.ModuleSdk.Integration.Configuration;

/// <summary>How connection verification affects readiness for a declared requirement.</summary>
public enum ConnectionVerificationExpectation
{
    /// <summary>Missing or failed verification blocks readiness.</summary>
    Required = 0,

    /// <summary>Verification issues are advisory only.</summary>
    Advisory = 1,

    /// <summary>Connector does not support verification; absence is acceptable.</summary>
    UnsupportedOk = 2,
}
