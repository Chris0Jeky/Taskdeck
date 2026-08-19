# ADR-0053: Legacy Obsidian Token Substrate — Paper-Scoped Remap as an Interim Floor, Per-View Migration as the Fix

- Status: Accepted
- Date: 2026-08-19
- Deciders: Coordinating agent under maintainer-delegated authority (ADR-0051 autonomous-admission lane), 2026-08-19
- Related: `#1778` (this decision), `#1769` (Paper-shell sweep that found the shared root cause), `#1775` (first Option-B cluster: Saved Views), `#1779`/`#1780`/`#1781` (Option-B clusters in flight), ADR-0011 (Obsidian & Ember token system), ADR-0038 (Paper UI is canonical, legacy frozen)

## Context

Taskdeck's frontend carries **two visual substrates at once**, and only one of them is scoped.

**Substrate 1 — Obsidian legacy, defined at `:root` (global, always on).** Three layers:

1. **`--td-*` custom properties** in `frontend/taskdeck-web/src/design-tokens.css`, declared on
   `:root`. Surfaces are dark (`--td-surface-base: #131313`,
   `--td-surface-container-lowest: #0e0e0e`), text is light (`--td-text-primary: #e5e2e1`), and the
   accent is a hot ember (`--td-color-ember: #ff4d4d`).
2. **The `.td-*` utility layer** in `frontend/taskdeck-web/src/style.css` (`@layer components`):
   `.td-card`, `.td-card-indicator`, `.td-chip`, `.td-btn` and its five variants, `.td-panel`,
   `.td-page-title`, `.td-section-title`, `.td-section-desc`, `.td-alert`, plus the virtual-scroll
   and dynamic-colour helpers. These read substrate 1 both through `var(--td-*)` and through
   `@apply`-ed Tailwind semantic classes. (`#1778` listed `.td-input` here; measured at
   `a2ac87e8` it is *not* in `style.css` — it is re-declared inside 15 components' scoped blocks.
   Those declarations still consume `--td-*`, so they are reached by the same remap.)
3. **Tailwind semantic colors** in `frontend/taskdeck-web/tailwind.config.js`: 51 named colors
   hardcoded to the Obsidian palette — `surface: #131313`, `on-surface: #e5e2e1`,
   `outline-variant: #5b403e`, `ember: #ff4d4d`, and the full Material 3 tonal families.

**Substrate 2 — Paper, scoped under `.paper` / `.paper-night`.** `paper-tokens.css` defines
`--paper`, `--paper-card`, `--ink`, `--ember` and friends *only* under those two class selectors;
`store/paperThemeStore.ts` puts one of them on `<body>`. Its own header states the intent plainly:
Paper variables "DO NOT apply at `:root`, so existing Obsidian (`--td-*`) tokens remain canonical."

ADR-0038 then made Paper the canonical UI and flipped its default to on — but substrate 1 was never
scoped to match. The result is the defect `#1769` swept up: **a view that has not been migrated to
the Paper idiom renders its Obsidian colours inside the cream Paper shell.** Measured at `a2ac87e8`,
28 of the 33 top-level route views reference substrate 1, and **23 of them have no Paper-variant
switch at all** (method: `grep -lE "var\(--td-|\b(bg|text|border|placeholder|ring|divide)-(surface|on-surface|on-background|background|outline|primary|secondary|tertiary|error|ember|obsidian|argent)" *.vue` in `src/views`, minus the files matching `paperTheme\.isOn`). Those 23 are black-panel-and-red-button surfaces sitting on cream paper.

Two views already worked around this by hand: `LoginView.vue` and `RegisterView.vue` write
`background: var(--paper, var(--td-surface-base))` — a per-view, per-property bridge. That pattern
is correct in spirit and unscalable in practice at 23 views and hundreds of properties.

### The dead light theme

`design-tokens.css` also carries a `[data-theme="light"]` block (~70 lines) that warms the whole
`--td-*` substrate — exactly the remap this defect needs. **It is dead code.** Evidence: a repo-wide
grep for `data-theme` at `a2ac87e8` returns matches in only one live source file —
`design-tokens.css` itself (the selector on line 111 and the comment on line 108 telling a reader to
"Apply via `<html data-theme="light">`"). Every other match is documentation or the design handoff:
`.paper-issues/01-tokens.md`, `design_handoff_taskdeck_paper/paper/*.jsx`, `docs/analysis/…`,
`docs/archive/…`, `docs/decisions/ADR-0011…`, `docs/design/TOKEN_CONVENTIONS.md`,
`docs/MANUAL_VERIFICATION_CHECKLIST.md`. No component, store, composable, router guard, `index.html`
or test sets the attribute. The sibling `[data-density]` blocks in the same file *are* live. So the
light theme has never activated in a shipped build, and its values were never validated against a
real surface.

## Decision

**Hybrid. Option A ships now as an interim floor; Option B remains the fix and continues per view.**

**Option A — scoped remap (this change).** Remap substrate 1 onto Paper values, scoped to the Paper
shell classes, in a new `frontend/taskdeck-web/src/paper-legacy-bridge.css`:

- Every semantic color in `tailwind.config.js` becomes `var(--td-tw-<name>, <original-hex>)`.
  Nothing defines `--td-tw-*` at `:root`, so **Legacy mode ("Paper off") resolves the fallback and
  its computed palette is byte-identical to before** — verified mechanically: all 51 fallbacks were
  diffed against `HEAD:tailwind.config.js` and match exactly. The Obsidian hex stays single-sourced
  in the config, not duplicated into the bridge.
- The bridge defines `--td-*` and `--td-tw-*` under `.paper, .paper-night`. Both Paper themes reuse
  the *same* token names, so one rule remaps light and night together and night mode inherits the
  fix for free.
- Every bridge value is a `var()` reference into `paper-tokens.css` — no new hex literals, so a
  Paper token change propagates to legacy surfaces automatically and the Paper Color Audit's
  single-source contract is preserved.
- The four legacy `--td-surface-*` aliases (`--td-surface-primary` etc.) are **restated** in the
  bridge rather than left to follow. A custom property's computed value is its specified value with
  `var()`s *already substituted*, so aliases declared as `var(--td-surface-container)` on `:root`
  froze to the Obsidian value there and would not have followed the remap.
- Two `::-webkit-scrollbar-thumb` rules are scoped alongside, because `style.css` hardcodes those
  colours outside any token.

**Option B — per-view migration to the Paper idiom.** Unchanged as the real fix, and unblocked by
Option A rather than replaced by it. `#1775` (Saved Views) is the reference pattern; `#1779`,
`#1780` and `#1781` are executing further clusters in parallel with this ADR.

**The `[data-theme="light"]` block is marked a deletion candidate, and is NOT deleted here.** The
evidence above is a grep, taken at one commit, while three sibling view-migration PRs are open; a
~70-line delete is trivially separable from a decision record and belongs in its own reviewable
slice. Deleting it in the same change as a shell-wide colour remap would also make bisecting any
visual regression harder. Track and remove it separately.

### Colour-mapping choices worth knowing

- **Surface ladder.** Obsidian's ladder brightens as it rises. On a light substrate that inverts —
  emphasis reads as *more* contrast against the page — with one exception: the card, which Paper
  lifts by going *lighter* than the page (`--paper-card` vs `--paper`), exactly as `.card` does in
  `paper-tokens.css`. The mapped ladder is therefore monotonic away from the page:
  card → page → panel (`--paper-2`) → edge (`--paper-edge`) → `--line` → `--whisper`. Every step is
  a distinct value, so hover pairs Obsidian expressed as "one tier up" still read as state changes,
  and they darken — which is Paper's own direction (`.pbtn:hover`).
  `--td-surface-container` was mapped by measured weight, not by its name: it carries ~122 call
  sites (65 Tailwind `bg-surface-container` + 57 `var()`), making it the dominant *panel* surface
  rather than merely the `.td-card` hover tier, so it gets a real step off the page.
- **Five tokens that were never defined.** `--td-surface-lowest`, `--td-surface-sunken`,
  `--td-surface-elevated`, `--td-surface-low` and `--td-surface-high` are consumed by 9+ components
  and declared nowhere in the repo. Some call sites carry disagreeing hex fallbacks
  (`#0e0e0e` in `InboxDetailPanel` vs `#f9f9f9`/`#f0f0f0` in `CohortDashboard`/`ProvenanceDrawer`);
  `--td-surface-elevated` (`MfaChallengeModal`, `MfaSetup`, `LoginView`) has *no* fallback, so its
  `background` is invalid-at-computed-value-time and resolves to transparent. The bridge defines all
  five under Paper. **The `:root` gap is a separate pre-existing defect and is deliberately not
  fixed here** — defining them at `:root` would change Legacy rendering, which this slice's whole
  safety argument rests on not doing.
- **Accent collapse.** Obsidian already collapsed "primary", "error" and "ember" onto three
  near-identical reds (`#ffb3ae` / `#ffb4ab` / `#ff4d4d`). Paper has one accent hue, so they
  collapse onto `--ember` here too. This is faithful to the palette being replaced, not a new
  flattening.
- **Text on ember.** `on-primary*` and `on-error` map to `--td-on-ember`, the Paper-side token
  already engineered to clear 4.5:1 on an ember fill (at rest and under `brightness(1.1)` hover) in
  both `.paper` and `.paper-night`.
- **`obsidian`.** The Tailwind `obsidian` colour means "the substrate colour". Its only consumer is
  `.td-btn--danger { bg-ember text-obsidian }`, so it maps to `--paper` — keeping light text on the
  ember fill (5.2:1 in `.paper`) instead of inverting to unreadable dark-on-dark.

## Alternatives Considered

**Option B only (migrate every view, ship nothing in the meantime).** Rejected on exposure, not on
correctness. 23 views is many sessions of work; until the last one lands, a free-open-beta user
clicking into Metrics or Activity from a Paper Home sees a black panel with red buttons. The floor
costs one file and makes every one of those views legible today.

**Option A only (declare the remap sufficient).** Rejected. The remap fixes *colour*. It does not
give a view Paper's serif display type, the `tk-*` type utilities, hairline rules, stamps, or the
restrained ember treatment. A remapped legacy view is legible, not canonical, and calling it done
would quietly reverse ADR-0038.

**Revive `[data-theme="light"]` and have the Paper shell set the attribute.** Rejected. It would
warm `--td-*` but does nothing for the Tailwind semantic colours (layer 3), which are compiled hex
and the larger share of legacy view styling. Its values are also unvalidated — never rendered in a
shipped build — and they are a *generic* warm-grey light theme, not Paper's cream-and-ink palette,
so it would introduce a third look rather than converge on the canonical one.

**Move the Paper tokens to `:root` and scope Obsidian instead.** Rejected as the larger and riskier
inversion of the same idea: it changes the default for every surface at once, including Legacy mode,
and `paper-tokens.css` explicitly documents the opposite contract.

**Per-view `var(--paper, var(--td-*))` fallbacks, as `LoginView`/`RegisterView` already do.**
Rejected as the general mechanism: it is Option B's cost with none of Option B's benefit, touching
every property of every view without delivering the Paper idiom.

## Consequences

**Positive**

- Every not-yet-migrated view stops rendering black/red inside the Paper shell, in both `.paper` and
  `.paper-night`, from one file.
- Legacy mode is provably untouched: no `.paper` class on `<body>` means every token in the bridge
  is inert and the Tailwind fallbacks resolve to the original hex.
- The remap is structural, not enumerated. A legacy view added or edited tomorrow inherits the floor
  without anyone remembering to add it — which a hand-written list of utility overrides would not
  have given.
- Palette stays single-sourced: Obsidian hex lives only in `tailwind.config.js`, Paper values only
  in `paper-tokens.css`, and the bridge is references.

**Negative / accepted costs**

- **The floor fixes colour, not idiom.** Serif display type, `tk-*`, hairlines, stamps and ember
  restraint remain per-view work. Do not read a lightened view as a migrated view, and do not close
  an Option-B issue because its view "looks fine now".
- A third token namespace (`--td-tw-*`) exists for the duration. It is deliberately named and
  documented as a bridge, and it dies with the legacy substrate.
- The mappings are one reviewer's judgement about a many-to-many palette translation. They are
  verified for build correctness and for the specific contrast pairs called out above; they have
  **not** been verified by rendering all 23 views. Expect follow-up tuning per view — which
  Option B supersedes anyway.
- Tailwind now emits `var(--td-tw-x, #hex)` for every semantic colour, and opacity modifiers emit
  `color-mix(in oklab, var(--td-tw-x, #hex) N%, transparent)` with a plain-colour fallback
  declaration ahead of it. Verified in the built CSS; browsers without `color-mix` fall back to the
  un-dimmed colour exactly as they did before.

**Neutral**

- `[data-theme="light"]` stays in the tree as documented dead code with a deletion candidate on
  record.
- The Paper Color Audit is unaffected: it scans `components/paper/**`, the AppShell paper region,
  and the `paper-tokens.css` hex count (70/70 baseline, unchanged). The bridge adds no hex outside
  comments and no tokens to `paper-tokens.css`.

## References

- `#1778` — this decision; `#1769` — the Paper-shell sweep that found the shared root cause
- `#1775` — Saved Views, the Option-B reference pattern; `#1779`/`#1780`/`#1781` — Option-B clusters
- ADR-0011 — Design Token System (Obsidian & Ember); ADR-0038 — Paper UI Is the Canonical Frontend
- `frontend/taskdeck-web/src/paper-legacy-bridge.css` — the Option A implementation
- `frontend/taskdeck-web/src/design-tokens.css`, `src/style.css`, `src/paper-tokens.css`,
  `tailwind.config.js`, `src/store/paperThemeStore.ts` — the substrates described above
