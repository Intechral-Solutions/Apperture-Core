# Epic 71 — Operational ownership directory and identity-backed attention routing

## Status

Accepted

## Context

Epic 70 stored attention recipients as rows on `AttentionRoutingRecipients` keyed by instance or integration routing id and role. Operators need **named, reusable contact groups**, first-class **group references** on routing rows, **honest no-route reasons** (no group, disabled group, empty group), and **audit-friendly notification evidence** that records which contact group was used. Future work may attach non-email principals; the group/member model should stay extensible.

## Decision

1. **Contact directory**: `AttentionContactGroups` and `AttentionContactGroupMembers` (email, optional display name, enable flags, audit timestamps). Management is exposed via `IAttentionContactGroupManagementService` and `GET/POST/PUT` APIs under `/api/attention-contact-groups`, authorized with `schedule.manage` (same gate as schedule tooling).
2. **Routing references**: `InstanceAttentionRoutings` and `IntegrationAttentionRoutings` gain nullable `PrimaryContactGroupId` and `EscalationContactGroupId`. Epic 70 recipient rows are **migrated** in migration `Epic71_ContactGroupsAndRouting`: for each distinct `(routing id, role)` with recipients, a group is created, members copied, FKs set, then `AttentionRoutingRecipients` is dropped.
3. **Resolution**: `SqlAttentionRecipientResolver` expands groups in batched queries. Per role: integration FK wins when set; else instance FK. Enabled group + enabled members only; emails trimmed and case-insensitively distinct. `AttentionRecipientResolution` carries group id/name, `HasPrimaryRoute` / `HasEscalationRoute`, `PrimaryNoRouteReason` / `EscalationNoRouteReason` (`no_group_configured`, `group_disabled`, `group_empty`), and **integration-row** FK snapshots (`IntegrationRowPrimaryContactGroupId`, `IntegrationRowEscalationContactGroupId`) so APIs can edit overrides without conflating with effective resolution.
4. **Instance defaults API**: `GET/PUT /api/attention-routing/defaults` (gated by `IScheduleGovernanceService.CanManageAsync`) updates the `instance_default` row for a condition key (default `expected_run_missing`).
5. **Notifications**: `AttentionIncidentNotificationWrite` / entity / projection / client DTOs include `ContactGroupId` and `ContactGroupNameSnapshot`; the orchestrator sets them on sends and on primary no-route suppressions when a group context exists.
6. **UI**: Tools menu adds **Attention contact groups** and **Attention routing defaults**; integration schedule diagnostics uses group pickers for overrides and shows no-route hints and contact group columns on notification evidence.

## Consequences

- Operators can rename migrated groups in the UI; generated migration names are intentionally descriptive, not pretty.
- Downgrading the migration recreates an empty `AttentionRoutingRecipients` table (recipient data is not restored on `Down`).
- Single condition v1 remains; schema stays row-per-condition for future conditions.

## Related

- Epic 70 ADR (superseded recipient-row model for routing)
- [`SqlAttentionRecipientResolver`](../../src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlAttentionRecipientResolver.cs), [`AttentionContactGroupManagementService`](../../src/Apperture.ControlPlane/Apperture.ControlPlane.Security/AttentionContactGroupManagementService.cs), migration `Epic71_ContactGroupsAndRouting`
