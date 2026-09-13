namespace Sample.ApiPolling;

/// <summary>
/// Sample ApiPolling module options. Embedded directory-API fallback is opt-in only (SDK-6C/6D clarification).
/// </summary>
public sealed class SampleApiPollingOptions
{
    public const string SectionName = "SampleApiPolling";

    /// <summary>
    /// Environment variable override: set to <c>true</c> to allow embedded fake directory API when no binding exists.
    /// </summary>
    public const string AllowEmbeddedFallbackEnvironmentVariable = "APPERTURE_SAMPLE_APIPOLLING_ALLOW_EMBEDDED_FALLBACK";

    /// <summary>
    /// When true, <see cref="ConnectionBackedSampleDirectoryApiClient"/> may use the in-process fake client
    /// without a DirectoryApi binding. Default false for production WorkerHost paths.
    /// </summary>
    public bool AllowEmbeddedFallback { get; set; }
}
