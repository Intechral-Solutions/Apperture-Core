namespace Apperture.Core.Application.Notifications.Cutover;

public interface IExpectedRunMissingCutoverValidationService
{
    ValueTask<ExpectedRunMissingCutoverValidation> ValidateAsync(
        string integrationId,
        CancellationToken cancellationToken = default);
}
