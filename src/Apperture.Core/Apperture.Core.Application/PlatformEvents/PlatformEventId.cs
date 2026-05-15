namespace Apperture.Core.Application.PlatformEvents;

/// <summary>Stable platform event identifier (Epic 77).</summary>
public readonly record struct PlatformEventId(string Value)
{
    public static PlatformEventId New() => new($"pevt_{Guid.NewGuid():N}");

    public override string ToString() => Value;
}
