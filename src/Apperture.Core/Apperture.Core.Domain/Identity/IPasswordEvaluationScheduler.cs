namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Coalesces expensive password scoring. Implementations must run only the latest
/// scheduled work; <see cref="Flush"/> evaluates that work immediately.
/// </summary>
public interface IPasswordEvaluationScheduler
{
    void Schedule(Action evaluateLatest);

    void Flush();

    void Cancel();
}
