using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.PlatformSettings;

/// <summary>
/// Request context passed to option providers when resolving selectable values.
/// Provider context controls where options come from; it is distinct from setting value scope.
/// </summary>
public sealed record PlatformSettingOptionProviderContext
{
    public required PlatformSettingOptionProviderContextKind ContextKind { get; init; }

    public required string SettingKey { get; init; }

    /// <summary>Required when <see cref="ContextKind"/> is <see cref="PlatformSettingOptionProviderContextKind.Integration"/>.</summary>
    public string? IntegrationId { get; init; }

    /// <summary>Connection-scoped providers may use this in a future epic.</summary>
    // public string? ConnectionId { get; init; }

    public required IActor Actor { get; init; }
}
