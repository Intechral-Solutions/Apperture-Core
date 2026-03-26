# Epic 72A - Operational Team Migration Hardening and Upgrade Readiness

## Status

Accepted

## Context

Epic 72 shifted attention ownership from contact groups to operational teams with principal-aware membership. That introduced upgrade risk concentrated in migration correctness and post-cutover deliverability semantics.

## Decision

Epic 72A hardens the upgrade path by:

- auditing and constraining operational team membership integrity at the database layer
- adding deterministic migration-audit tests for Epic 72 one-shot transform ordering/content
- strengthening recipient-resolution coverage for principal-without-email and mixed-member teams
- improving operator-facing no-route wording for principal-without-email outcomes
- documenting rehearsal and post-deploy verification steps.

## Key hardening choices

1. **DB integrity constraints**
   - Add FK from `OperationalTeamMembers.OperationalTeamId` to `OperationalTeams.Id`.
   - Add FK from `OperationalTeamMembers.PrincipalUserId` to `Users.Id` (nullable, restrict delete).
   - Add check constraint enforcing member payload shape:
     - `Principal` requires `PrincipalUserId`
     - `ExternalEmail` requires `EmailAddress`

2. **Migration honesty**
   - Keep Epic 72 one-shot transform as the cutover model.
   - Explicitly test that data-copy SQL runs before legacy table drops.

3. **Deliverability honesty**
   - Preserve explicit no-route reason `principal_members_no_email`.
   - Ensure diagnostics text surfaces this reason clearly.

## Consequences

- Upgrade safety improves by preventing malformed team-member rows.
- Principal-backed routing failure modes are visible and test-covered.
- Deployers have a repeatable rehearsal/verification path.
- Rollback remains limited to schema rollback semantics; not all operational meaning is guaranteed to roundtrip perfectly.
