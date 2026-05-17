using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.PlatformEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

public sealed class NotificationEvaluateOnlyCoordinator : INotificationEvaluateOnlyCoordinator
{
    private readonly INotificationDefinitionStore _definitions;
    private readonly INotificationRuleEvaluator _evaluator;
    private readonly IOptions<NotificationEvaluationOptions> _options;
    private readonly ILogger<NotificationEvaluateOnlyCoordinator> _logger;

    public NotificationEvaluateOnlyCoordinator(
        INotificationDefinitionStore definitions,
        INotificationRuleEvaluator evaluator,
        IOptions<NotificationEvaluationOptions> options,
        ILogger<NotificationEvaluateOnlyCoordinator> logger)
    {
        _definitions = definitions;
        _evaluator = evaluator;
        _options = options;
        _logger = logger;
    }

    public async ValueTask<IReadOnlyList<NotificationMatchResult>> EvaluatePlatformEventAsync(
        PlatformEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Value.Enabled)
            return [];

        var defs = await LoadEnabledDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.PlatformEvent,
            envelope,
            Incident: null,
            envelope.RecordedAtUtc);

        var results = await _evaluator.EvaluateAllAsync(defs, context, cancellationToken).ConfigureAwait(false);
        LogResults("platformEvent", envelope.Type, results);
        return results;
    }

    public async ValueTask<IReadOnlyList<NotificationMatchResult>> EvaluateIncidentAsync(
        IncidentEvaluationContext incident,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Value.Enabled)
            return [];

        var defs = await LoadEnabledDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.IncidentTransition,
            PlatformEvent: null,
            incident,
            incident.ObservedAtUtc);

        var results = await _evaluator.EvaluateAllAsync(defs, context, cancellationToken).ConfigureAwait(false);
        LogResults("incidentTransition", incident.Transition.ToString(), results);
        return results;
    }

    private async ValueTask<IReadOnlyList<Core.Domain.Notifications.NotificationDefinition>> LoadEnabledDefinitionsAsync(
        CancellationToken cancellationToken) =>
        await _definitions
            .ListAsync(new NotificationDefinitionListQuery(IsEnabled: true, Take: 500), cancellationToken)
            .ConfigureAwait(false);

    private void LogResults(string source, string key, IReadOnlyList<NotificationMatchResult> results)
    {
        foreach (var result in results.Where(r => r.Status != NotificationMatchStatus.NoMatch))
        {
            _logger.LogInformation(
                "Notification evaluate-only {Source} key={Key} definition={DefinitionId} status={Status} kind={Kind} reason={Reason}",
                source,
                key,
                result.DefinitionId,
                result.Status,
                result.NotificationKind,
                result.SuppressionReason);
        }
    }
}
