# Epic 72 Upgrade Rehearsal and Verification

## Scope

This runbook covers upgrade readiness for the Epic 71 -> Epic 72 ownership cutover.

## Pre-upgrade checks

- Take a verified backup.
- Confirm expected routing rows exist for `expected_run_missing`:
  - `InstanceAttentionRoutings`
  - `IntegrationAttentionRoutings`
- Confirm legacy ownership tables are populated as expected:
  - `AttentionContactGroups`
  - `AttentionContactGroupMembers`

## Rehearsal expectation

- Rehearse migration in staging against production-like data.
- Run targeted tests:
  - recipient resolver tests
  - diagnostics tests
  - notification ledger/orchestrator tests

## Post-upgrade verification

1. **Schema/state**
   - `OperationalTeams` and `OperationalTeamMembers` exist and are populated.
   - Legacy `AttentionContactGroups` and `AttentionContactGroupMembers` are dropped.
   - Routing columns are team-based (`PrimaryOperationalTeamId`, `EscalationOperationalTeamId`).

2. **Routing truth**
   - Instance default routing resolves expected recipients.
   - Integration override routing still beats instance defaults per role.

3. **Diagnostics truth**
   - Diagnostics show effective primary/escalation team and ownership source.
   - `principal_members_no_email` appears when principal members are present but not deliverable.
   - Notification evidence includes operational team snapshot fields.

4. **Deliverability health query**
   - Identify principal-backed members without deliverable email:

```sql
SELECT tm.OperationalTeamId, t.Name AS TeamName, tm.Id AS TeamMemberId, tm.PrincipalUserId
FROM OperationalTeamMembers tm
LEFT JOIN Users u ON u.Id = tm.PrincipalUserId
LEFT JOIN OperationalTeams t ON t.Id = tm.OperationalTeamId
WHERE tm.MemberKind = N'Principal'
  AND tm.IsEnabled = 1
  AND (u.Id IS NULL OR u.EmailAddress IS NULL OR LTRIM(RTRIM(u.EmailAddress)) = N'');
```

## Rollback expectations

- Database rollback is schema/version rollback.
- Operational semantics are not guaranteed to roundtrip perfectly once cutover is exercised.
- Prefer restore-from-backup for severe upgrade failure.
