using Apperture.Core.Application.Services;
using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Shared reactivation guard (CTX-3A §6.5, decision D2): while an Application ResourceContext is
/// archived, no transition that would make a resource it owns live again is allowed. Every such
/// transition must call <see cref="EnsureOwningContextIsActiveAsync"/> before persisting.
/// </summary>
/// <remarks>
/// Enumerated guarded transitions -- the design gate's instruction is to "enumerate the actual
/// transitions that exist ... and guard each one":
/// <list type="bullet">
/// <item><description>Integration: any state -&gt; Active. <see cref="IntegrationManagementService.UpdateIntegrationAsync"/> (also reached by <c>ActivateIntegrationAsync</c>).</description></item>
/// <item><description>Integration: Retired -&gt; Inactive. <see cref="IntegrationManagementService.UpdateIntegrationAsync"/> (also reached by <c>DeactivateIntegrationAsync</c>). Added by the CTX-3B archive-concurrency integrity pass: Retired -&gt; Inactive does not make the Integration able to dispatch, but it DOES flip it from non-archive-blocking to archive-blocking (see the remarks on <see cref="ResourceContextOwnershipSummary.BlocksArchive"/>), so it must be guarded the same as a transition into Active.</description></item>
/// <item><description>Schedule: Enabled false -&gt; true via <see cref="SqlScheduleManagementService.EnableAsync"/>.</description></item>
/// <item><description>Schedule: Enabled false -&gt; true via <see cref="SqlScheduleManagementService.UpdateScheduleAsync"/> (direct callers) or <see cref="SqlScheduleManagementService.PatchScheduleAsync"/> (the PATCH route; only a patch carrying <c>Enabled = true</c>). Added for CTX-3A finding C2: this path previously bypassed the EnableAsync-only guard entirely.</description></item>
/// <item><description>Schedule: creating a new enabled, Integration-attached schedule that inherits its Integration's (possibly archived) ResourceContext. <see cref="SqlScheduleManagementService.CreateScheduleAsync"/>.</description></item>
/// </list>
/// Not guarded, and not expected to be: Integration Active&lt;-&gt;Inactive (both states already block
/// archive, so neither direction changes archive-blocking status, and neither is a "starts dispatching
/// again" transition either). Integration Retired-&gt;Inactive, by contrast, IS guarded (see the list
/// above) precisely because it changes archive-blocking status even though it is not a "can dispatch"
/// transition -- the guard's scope is "can this transition make the owning context newly
/// archive-blocking", not literally "can this resource now dispatch". Connections have no reactivation
/// path today (<c>ResourceContextReactivationGuardTests.ConnectionManagementService_HasNoReactivationPath_SoNoGuardIsNeededYet</c>
/// intentionally breaks if that ever changes).
/// </remarks>
internal static class ResourceContextReactivationGuard
{
    /// <summary>
    /// Throws <see cref="ResourceContextArchivedException"/> when <paramref name="owningContextId"/>
    /// resolves to an archived ResourceContext; does nothing otherwise (including when the context
    /// cannot be resolved at all, matching this guard's pre-extraction behavior at every call site).
    /// </summary>
    /// <param name="actionPhrase">
    /// Completes the message "{Name} is archived. Unarchive it first before {actionPhrase}." -- pass a
    /// present-participle phrase, e.g. "activating this integration".
    /// </param>
    public static async ValueTask EnsureOwningContextIsActiveAsync(
        ResourceContextId owningContextId,
        IResourceContextService resourceContexts,
        string actionPhrase,
        CancellationToken cancellationToken)
    {
        var owningContext = await resourceContexts.GetAsync(owningContextId, cancellationToken).ConfigureAwait(false);
        if (owningContext is not null && !owningContext.IsActive)
        {
            throw new ResourceContextArchivedException(
                owningContext.Id,
                owningContext.Name,
                $"{owningContext.Name} is archived. Unarchive it first before {actionPhrase}.");
        }
    }
}
