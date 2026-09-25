namespace Apperture.Shared.Kernel;

/// <summary>
/// Marks an authenticated request as automatic background work so the host can skip sliding
/// session renewal. The marker grants nothing: it does not authenticate, authorize, or change identity.
/// </summary>
public static class BackgroundActivity
{
    public const string HeaderName = "X-Apperture-Background";

    public const string HeaderValue = "1";
}
