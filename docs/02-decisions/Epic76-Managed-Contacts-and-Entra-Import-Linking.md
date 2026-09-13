# Epic 76 — Managed Contacts and Entra Import/Linking

## Status

Accepted

## Context

Epic 75 delivered Entra tenant configuration, OpenID validation, provider readiness, and **directory discovery** (browse users/groups). Epic 72 delivered **operational teams** for attention routing with `Principal` and `ExternalEmail` members. The product still needed a **canonical person/contact layer** that is not the same as login-capable `UserEntity`, so ownership and notifications can target real people without forcing every person to be a platform user.

## Decision

1. **Managed contact** (`ManagedContactEntity`) is the canonical contact/person record: display name, optional email, `Source` (`Custom` | `Entra`), `IsEnabled`, optional `LinkedUserId` → `Users`.
2. **Managed contact identity link** (`ManagedContactIdentityLinkEntity`) binds an external directory principal (today: Entra user **object id**) to exactly one managed contact. Links are not the person record; custom contacts may have **no** links.
3. **Platform user** remains the login/access layer. Import from Entra **does not** create users unless an admin explicitly links a contact to an existing user (`managed_contact.manage` + `user.read`).
4. **Entra import** uses Epic 75’s Graph seam extended with **`GetUserByIdAsync` / `GetGroupByIdAsync`** so imports refresh by stable object id instead of trusting stale browse rows alone.
5. **Email conflicts**: if an Entra user’s mail matches another managed contact that is not already linked to that Entra principal, import returns a **per-row error** (no silent merge in Epic 76).
6. **Operational teams** gain member kind **`ManagedContact`** with `ManagedContactId`. Attention expansion joins `ManagedContacts.EmailAddress` for enabled contacts (`SqlAttentionRecipientResolver`).
7. **Entra groups**: **metadata-only** import via `ManagedContactGroupEntity` + `ImportEntraGroupsMetadataAsync` (upsert by `EntraGroupObjectId`). No membership sync or dynamic expansion in Epic 76; avoids a vague half-import.
8. **Permissions**: `managed_contact.read`, `managed_contact.manage`, `managed_contact.import` on resource type `ManagedContact` at instance scope; seeded into platform admin via `PlatformAdminRolePermissionRepair`. Operator capabilities expose `CanViewManagedContacts`, `CanManageManagedContacts`, `CanImportManagedContacts`.

## Relationship to Epic 75

Epic 75 stays the authority for tenant wiring and discovery APIs. Epic 76 **consumes** discovery (`IEntraDirectoryDiscoveryService`) and registration resolution; it does not replace OIDC validation or browse endpoints.

## Consequences

- Admins can create **custom** contacts with an admin-provided email for notifications without Entra or login.
- Imported Entra users become managed contacts with durable identity links; notification usability is explicit (`NotificationUsabilityStatus`: deliverable vs missing email vs disabled).
- Operational teams can reference managed contacts for routing without duplicating email strings as `ExternalEmail` (though that kind remains valid).

## Admin / operations

- **Create custom contact**: Config → Managed contacts → Create; provide display name and optional email.
- **Import Entra users**: Config → Authentication → Entra tenant → Directory users → select rows → Import (requires `managed_contact.import`). Review results message; open **Managed contacts** for detail.
- **Link to user**: Managed contact detail → enter platform **user id** → Link (requires user read + manage permissions).
- **Verify notifications**: On the contact, **NotificationUsabilityStatus** must be `Deliverable` (enabled + email). For teams, add the contact as a **ManagedContact** member (`managed` / `mcontact_…` in the operational team editor) or use other member kinds as before.

## Out of scope

- SCIM or full enterprise provisioning
- Rewriting core authentication or weakening lockout-safe behavior
- Automatic Entra group membership sync or group-based notification expansion
- Runtime/module worker identity management

## Follow-ups

- Optional merge UX when Entra mail collides with an existing custom contact (explicit admin confirmation).
- Group membership sync from Graph if product requires dynamic group expansion.
