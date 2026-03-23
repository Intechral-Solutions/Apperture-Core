using Apperture.Core.Application.Services;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Single place to build merged sign-in snapshot and effective experience for API responses and admin overview.
/// </summary>
public static class AuthSignInSnapshotBuilder
{
    public static AuthSignInBuiltSnapshot Build(
        LocalAuthOptions local,
        EntraAuthOptions entra,
        InstanceAuthUiPolicy policy)
    {
        var snapshot = AuthSignInOptionsMerger.Merge(local, entra, policy);
        var experience = AuthSignInEffectiveExperience.Resolve(snapshot);
        return new AuthSignInBuiltSnapshot(snapshot, experience);
    }
}

public readonly record struct AuthSignInBuiltSnapshot(
    AuthSignInOptionsSnapshot Snapshot,
    EffectiveAuthExperienceKind EffectiveExperience);
