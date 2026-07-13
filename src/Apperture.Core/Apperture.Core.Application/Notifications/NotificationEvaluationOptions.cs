namespace Apperture.Core.Application.Notifications;

public sealed class NotificationEvaluationOptions
{
    public const string SectionName = "Notifications:Evaluation";

    public bool Enabled { get; set; } = true;

    /// <summary>Poll interval for worker-emitted platform events awaiting notification evaluation.</summary>
    public int PollIntervalSeconds { get; set; } = 15;

    public int BatchSize { get; set; } = 20;
}
