namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Runs scoring on the calling thread. Used by tests and any caller that needs a
/// completed evaluation before the next statement.
/// </summary>
public sealed class ImmediatePasswordEvaluationScheduler : IPasswordEvaluationScheduler
{
    public static ImmediatePasswordEvaluationScheduler Instance { get; } = new();

    public void Schedule(Action evaluateLatest) => evaluateLatest();

    public void Flush()
    {
    }

    public void Cancel()
    {
    }
}

/// <summary>
/// Keeps only the latest scheduled evaluation. Tests call <see cref="Flush"/> to
/// run it without sleeping.
/// </summary>
public sealed class ManualPasswordEvaluationScheduler : IPasswordEvaluationScheduler
{
    private Action? _pending;

    public int ScheduleCount { get; private set; }

    public bool HasPending => _pending is not null;

    public void Schedule(Action evaluateLatest)
    {
        ArgumentNullException.ThrowIfNull(evaluateLatest);
        ScheduleCount++;
        _pending = evaluateLatest;
    }

    public void Flush()
    {
        var work = _pending;
        _pending = null;
        work?.Invoke();
    }

    public void Cancel() => _pending = null;
}

/// <summary>
/// Defers scoring until input is quiet, then evaluates only the latest password.
/// Default delay is 150 ms — short enough that the meter trails slightly, long
/// enough that a keystroke never waits on zxcvbn.
/// </summary>
public sealed class DebouncedPasswordEvaluationScheduler : IPasswordEvaluationScheduler, IDisposable
{
    public const int DefaultDelayMilliseconds = 150;

    private readonly int _delayMilliseconds;
    private readonly Action? _onRan;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Action? _pending;
    private int _generation;

    public DebouncedPasswordEvaluationScheduler(
        int delayMilliseconds = DefaultDelayMilliseconds,
        Action? onRan = null)
    {
        _delayMilliseconds = delayMilliseconds;
        _onRan = onRan;
    }

    public void Schedule(Action evaluateLatest)
    {
        ArgumentNullException.ThrowIfNull(evaluateLatest);
        CancellationToken token;
        int generation;
        lock (_gate)
        {
            _pending = evaluateLatest;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
            generation = ++_generation;
        }

        _ = RunAsync(generation, token);
    }

    public void Flush()
    {
        Action? work;
        lock (_gate)
        {
            _generation++;
            _cts?.Cancel();
            work = _pending;
            _pending = null;
        }

        work?.Invoke();
        _onRan?.Invoke();
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _generation++;
            _cts?.Cancel();
            _pending = null;
        }
    }

    public void Dispose() => Cancel();

    private async Task RunAsync(int generation, CancellationToken token)
    {
        try
        {
            await Task.Delay(_delayMilliseconds, token).ConfigureAwait(true);
            Action? work;
            lock (_gate)
            {
                if (generation != _generation)
                    return;
                work = _pending;
                _pending = null;
            }

            work?.Invoke();
            _onRan?.Invoke();
        }
        catch (OperationCanceledException)
        {
        }
    }
}
