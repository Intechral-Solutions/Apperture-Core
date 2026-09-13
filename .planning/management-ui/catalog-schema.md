# Catalog schema

Each entry in [pattern-catalog.md](pattern-catalog.md) uses these fields.

## Required fields

| Field | Description |
|-------|-------------|
| **ID** | Stable slug, e.g. `control.input.text` |
| **Category** | `token` · `primitive` · `component` · `pattern` · `page-recipe` |
| **Name** | Short human label |
| **Intent** | What operator job this supports (one sentence) |
| **Reference** | Best current implementation (path or route) |
| **Maturity** | `reference` · `pilot` · `legacy` · `candidate` |
| **Action** | `none` · `tokenize` · `reuse` · `extend-primitive` · `new-primitive` · `new-component` · `restructure` · `disclose` |

## Optional fields

| Field | Description |
|-------|-------------|
| **Variation** | How instances differ from reference; justify or plan merge |
| **Disclosure** | `always` · `section` · `advanced` · `drill-in` |
| **Instances** | Routes or files using this pattern |
| **DS section** | Target section in `management-ui-design-system.md` when promoted |
| **Notes** | Blockers, API constraints, permission rules |

## Promotion rules

1. **3+ routes** with the same ad-hoc markup → fix-once shared molecule before more page sweeps.
2. **Interesting variation** does not auto-create a new primitive; document *when* to use A vs B.
3. **New token** only when an existing semantic role cannot express the need in both light and dark.
4. **Progressive disclosure** hides secondary/forensic content, never required actions or permission-gated primary tasks.
5. **No** `dark:` utilities, `light` CSS class, or Razor theme conditionals — light = `:root`, dark = `html.dark`.

## Categories explained

```text
token       → CSS variable role in app.css (:root / .dark)
primitive   → ui-* class in management-ui-primitives.css
component   → Razor atom/molecule/organism with parameters
pattern     → Composed UI for a recurring situation (settings card, filter toolbar)
page-recipe → Full route layout archetype (entity hub, list+detail, authoring)
```
