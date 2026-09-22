namespace Apperture.Core.Application.Notifications.Subscriptions;

/// <summary>
/// One notification's effective toast (in-app pop-up) preference for a single operator. Unlike the
/// email channel, toast has no subscription/route concept behind it -- it is a pure per-user
/// preference-store read/write over the same self-subscribable definition set.
/// </summary>
public sealed record OperatorToastPreference(
    string DefinitionId, string DisplayName, string? Description, bool ToastEnabled);

public interface IOperatorNotificationToastPreferenceService
{
    ValueTask<IReadOnlyList<OperatorToastPreference>> ListToastPreferencesAsync(
        string userId, CancellationToken cancellationToken = default);

    ValueTask<OperatorToastPreference> SetToastEnabledAsync(
        string userId, string definitionId, bool toastEnabled, CancellationToken cancellationToken = default);
}
