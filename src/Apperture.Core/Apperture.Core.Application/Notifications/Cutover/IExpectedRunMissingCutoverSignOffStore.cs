namespace Apperture.Core.Application.Notifications.Cutover;

public interface IExpectedRunMissingCutoverSignOffStore
{
    ValueTask<ExpectedRunMissingCutoverSignOff?> GetLatestAsync(
        string integrationId,
        string notificationDefinitionId,
        CancellationToken cancellationToken = default);

    ValueTask<ExpectedRunMissingCutoverSignOff> AppendAsync(
        ExpectedRunMissingCutoverSignOff signOff,
        CancellationToken cancellationToken = default);
}
