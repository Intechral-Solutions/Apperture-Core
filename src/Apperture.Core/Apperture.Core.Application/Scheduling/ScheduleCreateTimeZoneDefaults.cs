using Apperture.Core.Application.PlatformSettings;
using Microsoft.Extensions.Logging;

namespace Apperture.Core.Application.Scheduling;

/// <summary>
/// Resolves the effective timezone for schedule create when the request omits/blank TimeZone.
/// Uses <see cref="PlatformSettingKeys.SchedulingDefaultTimeZoneId"/>; never server local time.
/// </summary>
public interface IScheduleCreateTimeZoneDefaults
{
    ValueTask<string> ResolveCreateTimeZoneIdAsync(string? requestedTimeZone, CancellationToken cancellationToken = default);
}

public sealed class ScheduleCreateTimeZoneDefaults : IScheduleCreateTimeZoneDefaults
{
    private readonly IPlatformSettingsResolver _platformSettings;
    private readonly ILogger<ScheduleCreateTimeZoneDefaults> _log;

    public ScheduleCreateTimeZoneDefaults(
        IPlatformSettingsResolver platformSettings,
        ILogger<ScheduleCreateTimeZoneDefaults> log)
    {
        _platformSettings = platformSettings;
        _log = log;
    }

    public async ValueTask<string> ResolveCreateTimeZoneIdAsync(
        string? requestedTimeZone,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(requestedTimeZone))
            return requestedTimeZone.Trim();

        var raw = await _platformSettings
            .ResolveRawValueAsync(PlatformSettingKeys.SchedulingDefaultTimeZoneId, cancellationToken)
            .ConfigureAwait(false);
        var fromSetting = (raw as string)?.Trim();
        if (!string.IsNullOrWhiteSpace(fromSetting) && ScheduleTimeZoneResolver.TryFind(fromSetting, out _))
            return fromSetting;

        if (!string.IsNullOrWhiteSpace(fromSetting))
        {
            _log.LogWarning(
                "Platform setting {SettingKey} value '{Value}' is not a valid time zone on this host; using UTC for schedule create default.",
                PlatformSettingKeys.SchedulingDefaultTimeZoneId,
                fromSetting);
        }

        return ScheduleTimeZoneResolver.UtcId;
    }
}
