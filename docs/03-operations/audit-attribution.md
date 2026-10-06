# Audit attribution: who an entry says acted

Every audit entry records who acted. This page explains how that is shown, and what the notices on some older entries mean.

## How the actor is established

Each entry carries the value it recorded at the time, unchanged, plus a classification of it:

| Shown as | Meaning |
|---|---|
| A person | The entry identifies a user. Since this release, new entries record the person signed in to the session. Older entries were matched to a user by their recorded user id, by a sign-in name in the username history, or by a Microsoft Entra object id linked to exactly one user. |
| "Recorded as *name*" | The sign-in name the entry recorded differs from the person's current username, for example after a rename. |
| System, Scheduler, Module lifecycle, Recovery tool, Emergency access command line, Worker | The platform acted, not a person. |
| Not signed in | An unauthenticated request, such as a failed sign-in. |
| Unrecognized actor | No rule identified a user, or the recorded value could mean more than one user. The recorded value is shown as it was written. |
| Not yet classified | The entry was written while its classification could not run. The Management Host classifies it at its next start. |

Classification never invents a person and never changes what an entry recorded. A user who is later renamed or decommissioned keeps their entries.

An older entry that recorded only the word `unknown` is always shown as an unrecognized actor, even if an account named `unknown` exists.

## Notices on older entries

Some writers in earlier versions recorded the wrong person. Their entries keep what they recorded and carry one of the notices below. Entries written by this version's audit recorder never carry one.

A **definite** notice means every version of the writer recorded the wrong person in that situation. A **possible** notice means it may have.

| Entries | Notice | Certainty |
|---|---|---|
| Account invitation sent or resent | This entry recorded the invited account as the actor. The administrator who sent the invitation was not recorded. | Definite |
| Recovery Access protection blocked a change | This entry recorded the protected account as the actor. The person who attempted the change was not recorded. | Definite |
| Workflow retry blocked, recorded as System | The person who requested this retry was not recorded. | Definite |
| Audit read denied, recorded as System | The person who made this request was not recorded. | Definite |
| Recovery and scheduler operations denied or requested, recorded as System | The person who made this request was not recorded. | Definite |
| Account security mail could not be delivered | This entry may have recorded the affected account instead of the person who acted. | Possible |
| Password reset requested, recorded as a person | This entry may have recorded the affected account instead of the person who acted. | Possible |
| Recovery Access authenticator enrolled, or recovery codes generated | This entry may have recorded the affected account instead of the person who acted. | Possible |
| Recovery Access identity applied, recorded as the emergency access command line | This entry may name the emergency access command line when the change was made automatically by the system. | Possible |
| Microsoft sign-in, recorded as `unknown` | The person who requested this may not have been recorded. | Possible |

An administrator who invited their own account, or a Recovery Access account acting on itself, produces a correct entry that still carries the notice: the platform cannot tell those cases apart afterwards.

## Outcomes on older entries

New entries record a normalized outcome: succeeded, failed, denied, rejected, blocked or requested. Older entries keep the outcome text they recorded. They show a normalized outcome only where that text is known to mean the same thing whenever it was written. Otherwise the outcome shows as "Outcome not recorded". Filtering by an outcome never matches an entry whose outcome was not recorded.

## Related

- Upgrade timing, the integrity guard and rollback: [deploy/README.md](../../deploy/README.md), "Audit log classification after upgrading".
- Design: [AUD-1 design gate](../01-architecture/transitions/audit-hardening/aud1-design-gate.md) §10, §11.5, §15.
