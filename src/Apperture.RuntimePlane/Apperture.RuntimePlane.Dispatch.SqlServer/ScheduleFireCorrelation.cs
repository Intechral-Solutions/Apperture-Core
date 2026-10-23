namespace Apperture.RuntimePlane.Dispatch.SqlServer;

/// <summary>
/// The correlation identifier of the audit rows the scheduler writes for one fire of a schedule, so the rows of that fire
/// (a recovered claim, a refusal, the dispatch) can be read together.
/// <para>
/// It names the same instant as the fire's start-request idempotency key (<c>schedule:{id}:{fire:O}</c>) in UTC form, and starts
/// with the correlation key of the run itself (<c>schedule:{id}</c>). The idempotency key is not used as it is: a zero-offset
/// <see cref="DateTimeOffset"/> formats as <c>+00:00</c>, which makes it 67 characters for a generated schedule id, and the audit
/// column holds 64. That key is stored in a 128-character column and parsed by the run-origin classifier, so it is left as it is.
/// </para>
/// </summary>
public static class ScheduleFireCorrelation
{
    /// <summary>Matches <c>AuditLogs.CorrelationId</c> (<c>nvarchar(64)</c>).</summary>
    public const int AuditCorrelationIdMaxLength = 64;

    /// <summary>
    /// <c>schedule:{id}:{fire UTC, ISO 8601 round trip}</c> when the audit column holds it. For a schedule id too long for that,
    /// the run's own correlation key <c>schedule:{id}</c>; for one too long for that as well, null, which the audit row records
    /// as having no correlation (the schedule id and the fire stay in their own columns and in the details). Never truncated.
    /// </summary>
    public static string? ForAudit(string scheduleId, DateTimeOffset fireUtc)
    {
        var perFire = $"schedule:{scheduleId}:{fireUtc.UtcDateTime:O}";
        if (perFire.Length <= AuditCorrelationIdMaxLength)
            return perFire;

        var perSchedule = $"schedule:{scheduleId}";
        return perSchedule.Length <= AuditCorrelationIdMaxLength ? perSchedule : null;
    }
}
