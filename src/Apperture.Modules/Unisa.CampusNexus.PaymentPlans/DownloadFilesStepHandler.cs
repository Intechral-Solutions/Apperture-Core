using Apperture.Connectors.Sftp;
using Apperture.Core.Application.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Unisa.CampusNexus.PaymentPlans;

public sealed class DownloadFilesStepHandler : IStepHandler
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DownloadFilesStepHandler(IServiceScopeFactory scopeFactory) =>
        _scopeFactory = scopeFactory;

    public async ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var sftp = await context.ResolveSlotAsync<ISftpFileClient>(UnisaCampusNexusPaymentPlansConstants.SlotSftp, cancellationToken).ConfigureAwait(false);
        if (sftp is null)
        {
            return OperatorExecutionResult.SuccessInline(new { step = "DownloadFiles", message = "SFTP slot not bound." });
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<IUnisaPaymentPlanManagementService>();
            // Inbound download/archive rules live here in a follow-up; connector is ready.
            return OperatorExecutionResult.SuccessInline(new { step = "DownloadFiles", message = "SFTP session resolved." });
        }
        finally
        {
            sftp.Dispose();
        }
    }
}
