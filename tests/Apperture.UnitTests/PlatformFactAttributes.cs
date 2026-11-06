using System.Runtime.InteropServices;
using Apperture.Core.Application.Scheduling;
using Xunit;

namespace Apperture.UnitTests;

/// <summary>
/// Tests that need a platform capability the host may lack are reported Skipped, with the reason, instead of returning early and
/// reporting Passed. The skip is decided when the test is discovered, so a runner that cannot execute the test says so.
/// </summary>
public static class PlatformTestGate
{
    /// <summary>The IANA zone the scheduling tests assert against (DST rules, fixed UTC offsets in June and November).</summary>
    public const string IanaZoneId = "America/Los_Angeles";

    public const string WindowsOnlySkipReason =
        "Requires Windows: exercises Windows DPAPI, which this platform does not provide.";

    public const string IanaZoneSkipReason =
        "Requires a host that resolves the IANA time zone '" + IanaZoneId + "' (not available here, for example under invariant globalization).";

    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>Uses the production resolver, so the gate and the code under test agree on what "resolvable" means.</summary>
    public static bool IanaZoneResolvable => ScheduleTimeZoneResolver.TryFind(IanaZoneId, out _);
}

/// <summary><see cref="FactAttribute"/> that is skipped unless the test runs on Windows.</summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!PlatformTestGate.IsWindows)
            Skip = PlatformTestGate.WindowsOnlySkipReason;
    }
}

/// <summary><see cref="FactAttribute"/> that is skipped when the host cannot resolve <see cref="PlatformTestGate.IanaZoneId"/>.</summary>
public sealed class IanaTimeZoneFactAttribute : FactAttribute
{
    public IanaTimeZoneFactAttribute()
    {
        if (!PlatformTestGate.IanaZoneResolvable)
            Skip = PlatformTestGate.IanaZoneSkipReason;
    }
}
