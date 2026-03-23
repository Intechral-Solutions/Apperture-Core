using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.Sftp;
using System.Data;

namespace Unisa.CampusNexus.PaymentPlans;

/// <summary>
/// UNISA payment plan integration — SFTP intake, eProm/TRANS processing, Campus Nexus API, and shared mapping tables.
/// Full business port from legacy <c>EPromProcessingService</c> is implemented incrementally.
/// </summary>
public interface IUnisaPaymentPlanManagementService
{
    /// <summary>
    /// End-to-end daily run: download (if SFTP bound), process inboxes, update watermarks.
    /// Missing slots are skipped with log entries (dev-friendly).
    /// </summary>
    Task<UnisaPaymentPlanRunSummary> RunDailyProcessingAsync(
        ICampusNexusApiClient? cns,
        ISftpFileClient? sftp,
        IDbConnection? integrationDb,
        CancellationToken cancellationToken = default);
}

public sealed record UnisaPaymentPlanRunSummary(bool SftpUsed, bool CnsUsed, bool DbUsed, IReadOnlyList<string> Messages);
