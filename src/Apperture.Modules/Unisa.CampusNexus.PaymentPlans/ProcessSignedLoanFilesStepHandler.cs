using Apperture.Connectors.CampusNexus;
using Apperture.Core.Application.Execution;
using Microsoft.Extensions.DependencyInjection;
using System.Data;

namespace Unisa.CampusNexus.PaymentPlans;

public sealed class ProcessSignedLoanFilesStepHandler : IStepHandler
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ProcessSignedLoanFilesStepHandler(IServiceScopeFactory scopeFactory) =>
        _scopeFactory = scopeFactory;

    public async ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var cns = await context.ResolveSlotAsync<ICampusNexusApiClient>(UnisaCampusNexusPaymentPlansConstants.SlotCnsApi, cancellationToken).ConfigureAwait(false);
        var db = await context.ResolveSlotAsync<IDbConnection>(UnisaCampusNexusPaymentPlansConstants.SlotIntegrationDb, cancellationToken).ConfigureAwait(false);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IUnisaPaymentPlanManagementService>();
            var summary = await svc.RunDailyProcessingAsync(cns, sftp: null, db, cancellationToken).ConfigureAwait(false);
            return OperatorExecutionResult.SuccessInline(new { step = "ProcessSignedLoanFiles", summary });
        }
        finally
        {
            cns?.Dispose();
            db?.Dispose();
        }
    }
}
