namespace Apperture.Shared.Kernel;

public interface IAppertureClock
{
    DateTimeOffset UtcNow { get; }
}
