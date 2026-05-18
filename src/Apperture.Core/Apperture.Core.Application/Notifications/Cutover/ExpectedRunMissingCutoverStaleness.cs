using Apperture.Core.Application.Notifications.Diagnostics;

namespace Apperture.Core.Application.Notifications.Cutover;

public static class ExpectedRunMissingCutoverStaleness
{
    public static bool IsSignOffStale(
        ExpectedRunMissingCutoverSignOff signOff,
        ExpectedRunMissingParityAcceptanceSummary? parity,
        NotificationConfigurationDiagnostics config)
    {
        if (parity is not null && signOff.ParityAcceptableAtReview != parity.LooksAcceptableForCutover)
            return true;

        var currentCodes = config.Warnings.Select(w => w.Code).OrderBy(c => c, StringComparer.Ordinal).ToList();
        if (!signOff.ConfigurationWarningCodes.OrderBy(c => c, StringComparer.Ordinal)
                .SequenceEqual(currentCodes, StringComparer.Ordinal))
            return true;

        if (parity is not null)
        {
            if (signOff.BlockingFindings.Count != parity.BlockingFindings.Count
                || signOff.NonBlockingFindings.Count != parity.NonBlockingFindings.Count)
                return true;
        }

        return false;
    }
}
