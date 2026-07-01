namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Prevents stale async page loads from updating UI after route changes or newer refresh/navigation.
/// </summary>
public sealed class NavigationLoadGuard
{
    private int _generation;

    public int Begin() => Interlocked.Increment(ref _generation);

    public bool IsCurrent(int generation) => generation == Volatile.Read(ref _generation);
}
