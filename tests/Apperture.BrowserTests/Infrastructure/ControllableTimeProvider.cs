namespace Apperture.BrowserTests.Infrastructure;

/// <summary>
/// Server-side clock the harness installs <em>only</em> on the Management.Host application-session cookie
/// handler (<c>CookieAuthenticationOptions.TimeProvider</c>). Advancing it makes the real cookie handler
/// see a ticket as expired (or as due for sliding renewal) without any production knob and without
/// sleeping for hours. It lives in the test assembly, so it cannot exist in a production composition.
/// </summary>
public sealed class ControllableTimeProvider : TimeProvider
{
    private long _offsetTicks;

    public TimeSpan Offset
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));
        set => Interlocked.Exchange(ref _offsetTicks, value.Ticks);
    }

    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);

    public void Reset() => Offset = TimeSpan.Zero;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;
}
