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

## Implementation status

**Tier 2 for ResourceContext `?context=` URLs — implemented by HSK-C (2026-09-19).** Tiers 1 and 3 needed no
new mechanism (ids were already canonical; HSK-B moved the movement dialog to display names).

### URL contract

| `?context=` value | Meaning | Notes |
|---|---|---|
| a ResourceContext **slug** (`development`) | that Context | What new links emit. Matched case-insensitively against the *current* directory. |
| a canonical **id** | that Context | Permanently accepted (old bookmarks). On a scoped page it is rewritten to the slug with a history *replace*. **An id match always beats a slug match.** |
| `shared` | the persisted Shared Context | Shared's id and slug are both `shared`; nothing special-cased. |
| `all` | the All *View* — not a ResourceContext | Reserved; never resolved as a slug. Unchanged. |
| an archived Context's slug or id | that Context, honoured with the existing archived banner | Routing resolution is independent of picker eligibility: archived Contexts are still excluded from create/movement pickers. |
| anything else | unknown Context | Existing "That context doesn't exist." flow (scoped pages) / "This Context filter is no longer valid." reset flow (Inventory). No alias lookup. |
| `system`, `notRecorded` (Inventory only) | Inventory's own pseudo-filters — not ResourceContexts | Checked before any directory lookup and never resolved through it. |

Resolution and emission live in one place — `ResourceContextUrlTokens` (`Ui.Shared`) — and run entirely
client-side against the directory the shell already loads. There is no slug lookup endpoint and no new
authorization surface; slug resolution is a routing convenience, not access control.

### What stays canonical

The API always receives the canonical id (`contextId=` / `resourceContextId=` / `context=` unchanged).
localStorage and tab-state View persistence store the canonical id (`ResourceContextViewScope.ToQueryValue()`);
the URL token is derived at emission time. Ownership, movement contracts, DTOs, audit and provenance are untouched.
Detail routes (`/integrations/{id}`, `/config/applications/{id}`) still use ids.

### Slug mutability

Slugs stay mutable and there is still no alias/redirect history. After a rename, new links use the new
slug, remembered Views (ids) stay valid, and id URLs keep working; an old-slug bookmark falls to the
ordinary unknown-Context handling. If a slug is later reassigned to another Context, the old link resolves
to the new holder — the same behaviour as any slug URL, and the accepted cost of this ADR.

### Per-surface behaviour

- **Scoped lists** (Integrations, Connections, Schedules, Runs): the URL token is resolved against the
  loaded directory *before* any data request; loads are de-duplicated on the canonical id, so rewriting an
  id URL to its slug never issues a second request.
- **Create flows** (Integration, Connection): `?context=` accepts a slug or id and preselects only Shared or an
  *active* Application; archived/unknown values fall through to the existing default.
- **Config Inventory**: keeps its independent filter. Select values stay ids; URL input accepts slug, id,
  `shared`, `system`, `notRecorded`. Inventory has never written its filter to the URL, so only inbound
  legacy id URLs are canonicalized. An Application whose slug spells `system`/`notRecorded` (case-insensitively)
  is addressed by id on that surface, because the pseudo values win.
- **Audit**: its Context filter is component-local (a select of ids sent as `resourceContextId`) and never
  appeared in the URL, so nothing changed.
- **Control Center / Business Data / Movement**: emit no `?context=` links; unchanged.

### Evidence

`ResourceContextUrlTokensTests`, `ScopedListContextFilterTests`, `InventoryContextUrlFilterTests`
(`Apperture.Management.Ui.Shared.Tests`); `ResourceContextViewStateTests` (`Apperture.Management.Ui.Client.Tests`,
behavioural: canonicalization, back/forward, storage stays id, slug rename); `ContextSlugRoutingUiSourceTests`
(page-wiring guards).
