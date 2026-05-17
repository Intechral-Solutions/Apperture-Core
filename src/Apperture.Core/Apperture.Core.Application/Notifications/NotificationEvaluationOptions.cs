namespace Apperture.Core.Application.Notifications;

public sealed class NotificationEvaluationOptions
{
    public const string SectionName = "Notifications:Evaluation";

    public bool Enabled { get; set; } = true;
}
