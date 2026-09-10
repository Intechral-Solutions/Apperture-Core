# IAM-5 — Entra directory provisioning and access requests

## Status

Accepted.

## Context

IAM-1 through IAM-4 established the authoritative `User` + `IdentityLink` model, Entra `tid + oid` identity, pending vs proven links, Entra-primary policy, Recovery Access, and verified contact email. Operators still could not provision Apperture users from a configured Microsoft Entra directory, and an unprovisioned person who successfully authenticated with Microsoft received only a login error.

Microsoft authentication must never imply Apperture authorization. Identity v1 does not include SCIM, group-to-role sync, B2B guest creation, or automatic deprovisioning.

## Decision

### Canonical provisioning

`IUserProvisioningService` is the shared user-creation boundary for:

- Microsoft Entra directory provisioning (Governance);
- access-request approval.

Manual local creation remains on `UserManagementService.CreateUserAsync` so existing username reservation, invitation, and password rules are unchanged. Both Entra paths create `User` + Entra `IdentityLink` only. They never create a local identity, `LocalCredential`, or IAM-4 setup invitation.

### Directory identity

Graph `user.id` is the Entra object id (`oid`). Authoritative identity and uniqueness are `TenantId + ObjectId` (`Source + TenantId + ExternalId` on `IdentityLink`). Registration id is provenance only: two registrations for the same tenant must not create two Apperture Entra identities for the same directory object. Email and UPN are snapshots only.

Persistent uniqueness:

- Local: unique `(Source, ExternalId)` where `Source = Local`.
- Entra: unique `(Source, TenantId, ExternalId)` where `Source = Entra` and `TenantId` is present.
- Entra rows without `TenantId` are invalid; upgrade fails rather than inventing tenant ids.

At most one **active** Entra registration exists (`IsActive = 1` filtered unique). Multiple inactive registrations may exist; they do not create a separate identity namespace.

### Admin directory provisioning

Governance → Users → Add from Microsoft Entra searches Microsoft Graph server-side (`User.Read.All` already granted). Continuation state is an opaque registration-scoped token, never a client-supplied Graph URL.

A newly imported Entra link is **pending** (`LastAuthenticatedAt = null`). The first later Microsoft sign-in proves it through existing IAM-1 `CompleteEntraLinkProofAsync`. Administrators may select Member or Guest directory objects; disabled accounts are rejected.

Batch add processes each person in its own transaction (max 20). Duplicate/race on one identity does not roll back others.

Optional welcome mail uses Graph `mail` only (never UPN as a mailbox). Delivery failure does not roll back the user and does not set or verify `Users.EmailAddress`.

### Access-request policy

Each Entra registration has:

- allow members to request access (off by default);
- allow guests to request access (off by default).

Member/Guest is Graph `userType`, never email domain.

### Unknown Microsoft authentication

When OIDC succeeds but `tid + oid` has no Apperture `IdentityLink`:

- do not create a `User`;
- do not issue the application cookie;
- if the tenant/registration is configured and enabled, issue a **limited** cookie (`Apperture.EntraAccessRequest`, 20 minutes) and send the browser to `/access-request`.

That cookie cannot satisfy ordinary `[Authorize]` (default Cookies scheme) or Governance APIs.

Eligibility is re-checked through Graph. Failures fail closed (`eligibility_unavailable`).

### Access-request lifecycle

`EntraAccessRequests` stores pending/approved/rejected rows. At most one Pending row per `(TenantId, ProviderObjectId)`. Registration id is provenance, not part of pending uniqueness. Historical decisions are retained. Repeated submit returns the existing pending row.

Approval re-reads the pending row, invokes `ProvisionFromAccessRequestAsync`, and records `ProvisionedUserId`. The Entra link is **proven** using the request's `AuthenticatedAtUtc` (that identity already completed Apperture's OIDC pipeline). Approval does not create an application session.

Rejection creates neither User nor IdentityLink. The requester sees a non-sensitive "not approved" state.

Direct directory provisioning of the same identity marks a pending request Approved and associates `ProvisionedUserId`.

### Recovery Access

Recovery Access remains outside these flows.

## Consequences

### Positive

- Configured Entra tenants become a first-class provisioning source without a second account model.
- Microsoft sign-in of an unknown identity is an access request, not JIT authorization.

### Negative

- Guest self-request is off unless an operator explicitly enables it.
- Graph outages prevent Member/Guest eligibility and directory search; there is no email fallback.

### Neutral

- Local create is not forced through `IUserProvisioningService`.
- Group.Read.All remains unused for authorization.
