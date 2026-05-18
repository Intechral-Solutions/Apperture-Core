namespace Apperture.Core.Application.Notifications.Instances;

public sealed class NotificationInstanceOptions
{
    public const string SectionName = "Notifications:Instances";

    public bool Enabled { get; set; }
}

public sealed class NotificationDeliveryOptions
{
    public const string SectionName = "Notifications:Delivery";

    /// <summary>When false, dispatch polls and logs dry-run only; SMTP is never invoked.</summary>
    public bool Enabled { get; set; }

    public int PollIntervalSeconds { get; set; } = 30;

    public int BatchSize { get; set; } = 20;

    public int MaxAttempts { get; set; } = 5;

    public int InitialBackoffSeconds { get; set; } = 60;

    public int MaxBackoffSeconds { get; set; } = 3600;
}

public sealed class NotificationPipelineOptions
{
    public const string SectionName = "Notifications:UsePlatformPipeline";

    public bool Enabled { get; set; }
}
