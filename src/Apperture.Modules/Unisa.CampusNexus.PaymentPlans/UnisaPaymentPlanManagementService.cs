using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.Sftp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data;

namespace Unisa.CampusNexus.PaymentPlans;

public sealed class UnisaPaymentPlanManagementService : IUnisaPaymentPlanManagementService
{
    private readonly ILogger<UnisaPaymentPlanManagementService> _logger;

    public UnisaPaymentPlanManagementService(ILogger<UnisaPaymentPlanManagementService>? logger = null) =>
        _logger = logger ?? NullLogger<UnisaPaymentPlanManagementService>.Instance;

    public Task<UnisaPaymentPlanRunSummary> RunDailyProcessingAsync(
        ICampusNexusApiClient? cns,
        ISftpFileClient? sftp,
        IDbConnection? integrationDb,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        bool sftpUsed = sftp is { IsConnected: true };
        bool cnsUsed = cns is not null;
        bool dbUsed = integrationDb is not null;

        if (!sftpUsed)
            messages.Add("SFTP slot not bound or not connected; download step skipped.");
        else
            messages.Add("SFTP available; inbound download logic runs in DownloadFiles step.");

        if (!cnsUsed)
            messages.Add("Campus Nexus API slot not bound; plan mutations skipped.");
        else
            messages.Add("Campus Nexus API client resolved.");

        if (!dbUsed)
            messages.Add("Integration DB slot not bound; mapping persistence skipped.");
        else
            messages.Add("Integration database connection resolved (UNISA payment plan tables).");

        _logger.LogInformation(
            "UNISA Campus Nexus payment plan scaffold evaluated. Sftp={Sftp} Cns={Cns} Db={Db}",
            sftpUsed,
            cnsUsed,
            dbUsed);

        return Task.FromResult(new UnisaPaymentPlanRunSummary(sftpUsed, cnsUsed, dbUsed, messages));
    }
}
