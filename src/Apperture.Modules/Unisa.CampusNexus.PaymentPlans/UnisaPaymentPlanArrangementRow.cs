namespace Unisa.CampusNexus.PaymentPlans;

public sealed record UnisaPaymentPlanArrangementRow(
    Guid ArrangementId,
    string ExternalKey,
    string SchoolFundId,
    int? CnsStudentId,
    string? StudentNumber,
    int? InSchoolPaymentPlanId,
    int? OutOfSchoolPaymentPlanId,
    string SourceKind,
    bool IsCurrent);
