# ADR-0041: Human-readable identity policy

## Status

Accepted

## Context

Every human-facing ResourceContext URL, query string, and localStorage value in the Management UI is Id-based
today (`?context=<opaque-id>`), even though each ResourceContext already carries a unique, human-readable
`Slug`. A post-CTX roadmap discovery pass (2026-09-18) investigated moving human-facing surfaces to the slug
and found the load-bearing fact this decision turns on: **Application ResourceContext slugs are mutable**
(only Shared's is immutable — `ResourceContext.IsReserved` blocks re-slugging only for Shared). CTX-1's own
design gate already reasoned through this and deliberately declined to build an alias/redirect table,
because at the time no slug-addressed URL existed to need one ("a rename breaks only human-typed/bookmarked
slug URLs — and slug URLs do not exist yet. Introducing alias history now would be building for a
hypothetical" — `ctx-design-gate.md` §2.3).

That hypothetical is no longer purely hypothetical: the product wants human-facing Context URLs to read as
`?context=dev-app` rather than a raw id. This decision needs a policy that gets that readability without
reopening the alias/redirect-table question CTX-1 correctly declined to build prematurely — and without
inventing a one-off rule that doesn't generalize to other entities that may later want the same treatment.

ResourceContext is not the only entity with an opaque-id-vs-natural-key situation. Integrations and
Connections have a `DisplayName`/`Name` field, but it is explicitly not unique — there is no natural key for
either today.

## Decision

A three-tier identity policy, applied first to ResourceContext and available to other entities later where a
suitably unique, reasonably stable natural key exists:

1. **Canonical identity (database, domain model, API contracts): the opaque id.** Every stored reference,
   every foreign key, every API request/response payload uses the immutable id. This never changes.
2. **Human-facing routing (URLs, query strings): the natural key (slug) where one exists and is practical.**
   For ResourceContext specifically: new human-facing links emit the slug (`?context=dev-app`); old
   id-based links continue to resolve (backward compatible); the resolution from slug (or id) to the
   canonical id happens **entirely client-side**, against the ResourceContext directory the shell already
   loads once per tab — no new server endpoint, no slug sent to the API as the filter itself. The API and
   localStorage/tab-state persistence continue to receive and store only the canonical id, never the slug.
3. **Visible UI copy: the display name.** Operator-facing text (labels, messages, blocker/effect copy)
   resolves to `Name`, never the raw id and never the slug used as a routing shorthand. Technical/advanced
   metadata surfaces may continue to show the canonical id where useful (e.g. a detail page's own identity
   field).

This tier-3 rule generalizes beyond ResourceContext: even where an entity has no suitable natural routing
key (tier 2 does not apply to it), operator-facing lists and messages should still prefer a meaningful
display name or username over an opaque machine id whenever one is unambiguously available, with the
canonical id remaining visible in technical/detail metadata. This is a presentation preference stated here
for reference, not new implementation scope — this ADR's actual routing decision (tier 2) remains scoped to
ResourceContext `?context=` URLs only.

**Explicitly not built now:**

- No slug alias/history subsystem. A slug rename is not tracked; an old slug in a stale bookmark, after an
  explicit rename, falls back to the same "unknown context" UI that already handles an unknown id today —
  it is not a new failure mode, and it is not silently and incorrectly resolved to whatever context now
  holds that slug (client-side resolution fails safely if the slug no longer matches anything in the
  directory).
- Mutable slugs never become the canonical server-side identity for anything. Nothing is looked up, joined,
  or persisted by slug on the server side as a result of this policy.
- No system-wide identifier migration. This first stage covers ResourceContext `?context=` URLs only.
  Extending to every entity's own detail route (e.g. `/config/applications/{id}`) or to other entities
  (Integrations, Connections, Users) is a separate, later decision — not automatic just because this policy
  exists.

## Consequences

### Positive

- Human-facing Context URLs become readable without reopening the alias/redirect-table question CTX-1
  correctly declined to build before a use case existed.
- No new API surface, no new persisted data, no slug-resolution server-side plumbing — the shell's own
  already-cached directory does the work.
- A stale slug link after a rename degrades to the existing, familiar "unknown context" experience rather
  than a new, surprising failure mode or (worse) a silent resolve-to-the-wrong-context bug.
- The three-tier framing (canonical id / routing key / display name) generalizes cleanly if another entity
  later grows a stable natural key, without committing to build that for entities that don't have one today.

### Negative

- A renamed Application's old slug-based bookmarks/shared links stop resolving to that Application (they
  fall back to "unknown"), with no redirect. This is the accepted cost of not building an alias table; it
  is no worse than today's behavior for an archived or deleted-then-recreated id, which already has no
  redirect either.
- Every human-facing surface that currently sends a raw id straight through to the API (list filters,
  Inventory, Audit, creation pickers) needs its own small client-side resolution step added — this is not
  one central switch, since each surface currently builds its own query string.

### Neutral

- Integrations and Connections are explicitly out of scope for this decision; they have no natural key
  today, and adding one is a separate future decision this ADR does not make.
- This does not change how the ResourceContext id itself works, or any part of CTX's ownership/movement
  model — it is purely a routing/display-layer decision.
