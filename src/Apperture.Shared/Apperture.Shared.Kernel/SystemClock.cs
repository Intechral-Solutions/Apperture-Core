namespace Apperture.Shared.Kernel;

public sealed class SystemClock : IAppertureClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
