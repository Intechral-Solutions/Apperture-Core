namespace Apperture.Core.Application.PlatformEvents;

public sealed class PlatformEventsOptions
{
    public const string SectionName = "PlatformEvents";

    /// <summary>When false, emitters no-op and dual-emit is skipped. Attention incidents are unaffected.</summary>
    public bool Enabled { get; set; } = true;
}
