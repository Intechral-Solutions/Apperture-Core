namespace Apperture.Core.Application.Notifications.Cutover;

public sealed record CreateExpectedRunMissingCutoverSignOffRequest(
    string Status,
    string? OperatorNote);

public interface IExpectedRunMissingCutoverSignOffService
{
    ValueTask<ExpectedRunMissingCutoverSignOff?> GetLatestAsync(
        string integrationId,
        CancellationToken cancellationToken = default);

    ValueTask<ExpectedRunMissingCutoverSignOff> CreateOrReplaceSignOffAsync(
        string integrationId,
        CreateExpectedRunMissingCutoverSignOffRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<ExpectedRunMissingCutoverSignOff> RevokeSignOffAsync(
        string integrationId,
        string? operatorNote,
        CancellationToken cancellationToken = default);
}
