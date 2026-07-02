namespace Apperture.ControlPlane.Security;

/// <summary>
/// Configuration for break-glass operator login (<c>POST /api/auth/break-glass</c>).
/// Disabled by default for v1; enable only with operational runbook and eligibility controls.
/// </summary>
public sealed class BreakGlassAuthOptions
{
    public const string SectionName = "BreakGlassAuth";

    /// <summary>
    /// When false (default), the break-glass login endpoint is unavailable (404).
    /// </summary>
    public bool Enabled { get; set; }
}
