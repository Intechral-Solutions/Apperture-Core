namespace Apperture.Core.Application.Notifications;

public sealed record NotificationValidationIssue(string Code, string Message, string? Path = null);

public sealed record NotificationValidationResult(
    bool IsValid,
    IReadOnlyList<NotificationValidationIssue> Issues)
{
    public static NotificationValidationResult Success() =>
        new(true, Array.Empty<NotificationValidationIssue>());

    public static NotificationValidationResult Failure(params NotificationValidationIssue[] issues) =>
        new(false, issues);

    public static NotificationValidationResult Merge(
        NotificationValidationResult rule,
        NotificationValidationResult template)
    {
        if (rule.IsValid && template.IsValid)
            return Success();

        var merged = new List<NotificationValidationIssue>();
        merged.AddRange(rule.Issues);
        merged.AddRange(template.Issues);
        return new NotificationValidationResult(false, merged);
    }
}
