# Epic 45 - Dashboard Summary Foundations

Date: 2026-03-22

## Status
Accepted

## Context

Dashboard surfaces existed as placeholders, but there was no shared contract for summary cards and no truthful default dashboard backed by real control-plane data. This made the dashboard feel either incomplete or overly aspirational.

Epic 45 establishes a minimal foundation: a shared operator dashboard scope, a small card contract, provider composition, and a first seed set of real cards.

## Decision

1. Dashboard scope is a single shared platform operator view for now.
2. The default dashboard is composed from provider contributions through `IDashboardCardProvider`.
3. The composed model is exposed by `IDashboardDefaultViewService` and `GET /api/dashboard/default`.
4. The initial cards are:
   - Runtime Health
   - Needs Attention
   - Recent Activity
   - Blocked Work
   - Authentication Posture
5. Card visibility is permission-aware by omitting cards when users lack access to the underlying data source.
6. Dashboard home renders only the shared default contract with explicit loading, empty, and error states.
7. Dashboard management routes remain in the menu as deferred items and are labeled as such.

## Consequences

- Operators get a truthful dashboard summary backed by current mature query surfaces.
- Modules and future platform areas can add cards without changing the dashboard page contract.
- The model stays intentionally minimal and does not lock the platform into a widget framework too early.

## Explicit Deferrals

- Per-user dashboard personalization.
- Drag-and-drop layout editing.
- Multi-page dashboard management as a fully implemented feature.
- Dashboard widget marketplace or card catalog management UX.
