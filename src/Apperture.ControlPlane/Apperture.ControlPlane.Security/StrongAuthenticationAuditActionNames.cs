namespace Apperture.ControlPlane.Security;

/// <summary>Audit action names for IAM-2 strong authentication and Entra-primary policy (auth category).</summary>
public static class StrongAuthenticationAuditActionNames
{
    public const string StepUpInitiated = "auth.strong_authentication.step_up.initiated";
    public const string StepUpSatisfied = "auth.strong_authentication.step_up.satisfied";
    public const string StepUpFailed = "auth.strong_authentication.step_up.failed";
    public const string StepUpIdentityMismatch = "auth.strong_authentication.step_up.identity_mismatch";
    public const string StepUpTenantMismatch = "auth.strong_authentication.step_up.tenant_mismatch";
    public const string StepUpContextNotSatisfied = "auth.strong_authentication.step_up.authentication_context_not_satisfied";
    public const string AssuranceInsufficient = "auth.strong_authentication.insufficient";
    public const string LocalDisabledEntraPrimary = "credential.local.disabled_entra_primary";
    public const string LocalDisableDeferredLastAdmin = "credential.local.disable_deferred_last_entra_independent_admin";
    public const string LocalReenabledStrongAdmin = "credential.local.reenabled_strong_authentication";
    public const string AuthenticationContextConfigured = "auth.entra.authentication_context.changed";
}
