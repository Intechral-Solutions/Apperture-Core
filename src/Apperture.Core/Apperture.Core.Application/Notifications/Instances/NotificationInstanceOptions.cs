namespace Apperture.Core.Application.Notifications.Instances;

public sealed class NotificationInstanceOptions
{
    public const string SectionName = "Notifications:Instances";

    public bool Enabled { get; set; }
}

public sealed class NotificationDeliveryOptions
{
    public const string SectionName = "Notifications:Delivery";

    public bool Enabled { get; set; }
}

public sealed class NotificationPipelineOptions
{
    public const string SectionName = "Notifications:UsePlatformPipeline";

    public bool Enabled { get; set; }
}
