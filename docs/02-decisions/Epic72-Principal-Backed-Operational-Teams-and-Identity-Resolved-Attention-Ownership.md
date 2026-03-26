# Epic 72 - Principal-Backed Operational Teams and Identity-Resolved Attention Ownership

## Status

Accepted

## Context

Epic 71 introduced contact-group-based attention routing with reusable email recipient sets. Epic 72 upgrades operational ownership to principal-backed teams while preserving workflow-centric attention semantics and incident-history honesty.

The instance uses a one-shot cutover strategy:

- transform live contact groups into operational teams
- transform contact-group members into operational team members with `MemberKind=ExternalEmail`
- remap routing columns from group naming to team naming
- preserve notification evidence using snapshot columns, not live legacy joins
- remove legacy contact-group runtime tables from the active model.

## Decision

1. Introduce `OperationalTeam` and `OperationalTeamMember` as the source of routing ownership.
2. Support two member kinds:
   - `Principal` (references local `UserEntity` by user id; deliverable email resolved from `UserEntity.EmailAddress`)
   - `ExternalEmail` (direct email delivery)
3. Replace routing foreign-key semantics with team ids:
   - `PrimaryOperationalTeamId`
   - `EscalationOperationalTeamId`
4. Snapshot team context into notification evidence:
   - `OperationalTeamId`
   - `OperationalTeamNameSnapshot`
5. Keep no-route explanations explicit (`no_team_configured`, `team_disabled`, `team_empty`, `principal_members_no_email`).

## Consequences

- Routing now distinguishes principal-backed ownership from direct external contacts while still producing normalized recipient lists for sender adapters.
- History remains stable and queryable after cutover because notifications carry team snapshots.
- Local platform users now persist optional `EmailAddress` to support principal recipient delivery.
- Existing behavior for escalation/reminder dedupe remains intact.

## Migration strategy

Migration performs:

1. schema rename from contact-group columns to operational-team columns
2. create `OperationalTeams` and `OperationalTeamMembers`
3. copy data from contact-group tables to operational-team tables (`MemberKind='ExternalEmail'`)
4. drop old contact-group tables.

The down migration recreates prior tables and names, but does not guarantee perfect semantic round-trip for principal members.
