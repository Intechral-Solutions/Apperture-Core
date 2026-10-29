namespace Apperture.Core.Application.AdministratorViability;

/// <summary>
/// The class of configuration evidence a decision about administrator viability used, as it is recorded in the audit trail. It
/// names the evidence <i>actually used</i>, including on a refusal for missing evidence, and it never carries a value of the
/// configuration itself.
/// </summary>
public enum ConfigurationEvidence
{
    /// <summary>The decision used a fresh, consistent attestation written by the Management Host.</summary>
    Attested,

    /// <summary>
    /// The decision did not depend on host configuration: its inputs were unchanged, it cannot reduce anything, or a proof that
    /// needs no missing input settled it.
    /// </summary>
    NotRequired,

    /// <summary>The decision needed host configuration and none could be trusted. The refusal is the established unavailable outcome.</summary>
    Unavailable,
}

/// <summary>The canonical audit values of <see cref="ConfigurationEvidence"/> and the one detail they are recorded under.</summary>
public static class ConfigurationEvidenceNames
{
    /// <summary>The audit detail field that carries the evidence class. It is the only configuration detail an event carries.</summary>
    public const string AuditDetailName = "configurationEvidence";

    /// <summary>The audit value of <see cref="ConfigurationEvidence.Attested"/>.</summary>
    public const string Attested = "attested";

    /// <summary>The audit value of <see cref="ConfigurationEvidence.NotRequired"/>.</summary>
    public const string NotRequired = "not_required";

    /// <summary>The audit value of <see cref="ConfigurationEvidence.Unavailable"/>.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>Every canonical value, in the order of the enumeration.</summary>
    public static IReadOnlyList<string> All { get; } = [Attested, NotRequired, Unavailable];

    /// <summary>The canonical audit value of <paramref name="evidence"/>.</summary>
    public static string ToAuditValue(this ConfigurationEvidence evidence) => evidence switch
    {
        ConfigurationEvidence.Attested => Attested,
        ConfigurationEvidence.NotRequired => NotRequired,
        ConfigurationEvidence.Unavailable => Unavailable,
        _ => throw new ArgumentOutOfRangeException(nameof(evidence), evidence, "Unknown configuration evidence class."),
    };
}
