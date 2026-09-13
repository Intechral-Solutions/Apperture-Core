namespace Unisa.CampusNexus.PaymentPlans.Persistence;

/// <summary>
/// Logical payment plan arrangement: ULDTN or TRANS REFERENCE with optional in-school / out-of-school CNS plan ids.
/// </summary>
public sealed class UnisaPaymentPlanArrangementEntity
{
    public Guid ArrangementId { get; set; }

    /// <summary>loanULDTN or TRANS REFERENCE (due bill id, reversal id, etc.).</summary>
    public string ExternalKey { get; set; } = "";

    public string SchoolFundId { get; set; } = "";

    public int? CnsStudentId { get; set; }

    public string? StudentNumber { get; set; }

    public int? InSchoolPaymentPlanId { get; set; }

    public int? OutOfSchoolPaymentPlanId { get; set; }

    /// <summary>e.g. EpromSigned, TransDueBill.</summary>
    public string SourceKind { get; set; } = "";

    public bool IsCurrent { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? SupersededAt { get; set; }
}
