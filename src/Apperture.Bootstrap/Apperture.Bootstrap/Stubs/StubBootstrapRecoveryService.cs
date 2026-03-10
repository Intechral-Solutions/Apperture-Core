using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap.Stubs;

/// <summary>
/// Stub recovery service: authorizes, writes recovery audit, returns result.
/// Does not perform actual rebind or profile switch; real implementation deferred.
/// </summary>
public sealed class StubBootstrapRecoveryService : IBootstrapRecoveryService
{
    private readonly IBootstrapRecoveryAuthorizer _authorizer;
    private readonly IBootstrapAuditWriter _auditWriter;

    public StubBootstrapRecoveryService(IBootstrapRecoveryAuthorizer authorizer, IBootstrapAuditWriter auditWriter)
    {
        _authorizer = authorizer;
        _auditWriter = auditWriter;
    }

    public async Task<BootstrapRecoveryResult> ExecuteRecoveryAsync(BootstrapRecoveryRequest request, CancellationToken cancellationToken = default)
    {
        var auth = await _authorizer.AuthorizeAsync(request, cancellationToken).ConfigureAwait(false);
        var actionType = request.ActionType;
        var timestamp = DateTimeOffset.UtcNow;

        if (!auth.Authorized)
        {
            await _auditWriter.WriteRecoveryAsync(new BootstrapRecoveryAuditRecord
            {
                Timestamp = timestamp,
                RecoveryAction = actionType,
                Reason = request.Reason,
                Outcome = "Denied",
                Details = auth.Reason
            }, cancellationToken).ConfigureAwait(false);
            return new BootstrapRecoveryResult { Success = false, Message = auth.Reason ?? "Recovery not authorized." };
        }

        await _auditWriter.WriteRecoveryAsync(new BootstrapRecoveryAuditRecord
        {
            Timestamp = timestamp,
            RecoveryAction = actionType,
            Reason = request.Reason,
            Outcome = "Authorized",
            Details = "Stub: no recovery operation performed."
        }, cancellationToken).ConfigureAwait(false);

        return new BootstrapRecoveryResult { Success = true, Message = "Recovery unlock completed. Stub: no recovery operation performed; restart recommended." };
    }
}
