# Epic 76A — Managed Contact Integrity and Operational Integration Audit

**Type:** audit / corrective pass (not a feature epic)  
**Status:** complete (hardening slice)  
**Related:** Epic 76 (managed contacts, Entra import, identity links, attention expansion)

## Scope reviewed

- Managed operational team member kind `ManagedContact` and attention recipient resolution (`SqlAttentionRecipientResolver`)
- Entra user import into managed contacts (`ManagedContactManagementService.ImportEntraUsersAsync`)
- Stable outcome signaling vs freeform error text for UI/tests
- Identity boundary (managed contact vs platform user vs Entra link)

## Confirmed correct (unchanged model)

- **Managed contact** models a person/contact record for ownership and notifications; it does not imply login.
- **Platform user** remains the login/access principal.
- **Identity link** records Entra (and future provider) linkage to a managed contact; import dedupes by Entra object id via the link table.
- Repeat import / upsert behavior continues to update an existing linked contact; collisions are still surfaced honestly without implementing merge.

## Fixed in this pass

### 1. Honest no-route reasons when email cannot be delivered

**Issue:** When a team had **only** managed-contact members and none yielded a deliverable email, resolution could report `principal_members_no_email`, which mis-describes the situation.

**Change:**

- Added `managed_contact_members_no_deliverable_email` for managed-contact-only failures.
- Added `member_recipients_no_deliverable_email` when multiple member kinds and/or external addresses all fail to produce any deliverable recipient (mixed or combined failure).
- Preserved `principal_members_no_email` for principal-only teams where principals lack deliverable email.

**Locations:** `AttentionNoRouteReasons`, `SqlAttentionRecipientResolver`, operator-facing hint strings on attention routing UI surfaces.

### 2. Stable Entra import outcome codes

**Issue:** Import rows relied primarily on `ErrorMessage` text for interpretation.

**Change:** Added machine-readable `OutcomeCode` on each import row (application record, API/client DTO). Codes are defined in `EntraUserImportOutcomeCodes`. Human-readable messages are retained where they add detail.

## Deferred (explicit non-goals)

- **Explicit merge workflow** when Entra mail collides with an existing custom managed contact (still operator-resolved; messaging remains honest).
- **Broader picker/autocomplete** refinements for managed contacts on teams.
- **Large contact/team refactors** beyond honest diagnostics and outcome codes.
- **`missing_deliverable_email` as an outcome code:** not emitted today because import does not reject solely on absent mail; reserved for a future behavior change if product decides to treat undeliverable directory mail differently.

## Tests

Focused tests cover managed-only vs principal-only vs mixed no-route semantics and Entra import outcomes (success, already linked, conflict, directory failure, not found, repository failure). See `SqlAttentionRecipientResolverTests` and `ManagedContactManagementServiceEntraImportTests`.

## Summary

Epic 76’s core model is sound. This pass removes misleading no-route labeling for managed-contact-only delivery gaps and adds stable import outcome codes so the UI and automated tests can rely on explicit state instead of parsing error strings.
