# Architecture transitions

Working records for **in-flight** architecture changes. These files are **not** the current contract.

Use this folder when a subsystem is about to be restructured and you need a snapshot, a proposed target, and notes on what will move — without putting that material on the architecture index as if it were already true.

| This folder | Architecture docs (`docs/01-architecture/*.md`) |
|-------------|------------------|
| What is about to change | What is true now |
| Disposable after the change ships | Keep and update |
| May be incomplete or contradictory | Canonical |

When the change is done: fold the outcome into an ADR and the real architecture docs, then delete or archive the transition set.

## Active

| Set | Status |
|-----|--------|
| [nav-overhaul/](nav-overhaul/) | Management UI navigation — overhaul about to start |
| [application-contexts/](application-contexts/) | Evidence snapshot of current surfaces before any Application/Context planning |
