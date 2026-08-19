# ADR-0053: Legacy Obsidian Token Substrate — Paper-Scoped Remap as an Interim Floor, Per-View Migration as the Fix

- Status: Accepted
- Date: 2026-08-19
- Deciders: Coordinating agent under maintainer-delegated authority (ADR-0051 autonomous-admission lane), 2026-08-19
- Related: `#1778` (this decision), `#1769` (Paper-shell sweep that found the shared root cause), `#1775` (first Option-B cluster: Saved Views), `#1779`/`#1780`/`#1781` (Option-B clusters in flight), `#1817` (residuals ledger for "Known limitations"), `#1815` (Paper-idiom guard scope / Legacy mode's fate), `#1814` (undeclared `--td-surface-*` tokens), ADR-0011 (Obsidian & Ember token system), ADR-0038 (Paper UI is canonical, legacy frozen)

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
28 of the 33 top-level route views reference substrate 1, and **23 of those make no reference to the
Paper theme store at all** (method: `grep -lE "var\(--td-|\b(bg|text|border|placeholder|ring|divide)-(surface|on-surface|on-background|background|outline|primary|secondary|tertiary|error|ember|obsidian|argent)" *.vue` in `src/views` → 28 files, minus the 5 of those 28 that also match `grep -l paperTheme` → **23**. Subtracting `paperTheme\.isOn` instead yields 25, not 23: only `BoardView`, `HomeView` and `TodayView` carry a real `paperTheme.isOn` Paper/Legacy switch, while `AppearanceSettingsView` and `PaperStyleGuideView` reference the store without being one. All three figures — 33 / 28 / 23 — re-reproduced at `53406cfd`.) Those 23 are black-panel-and-red-button surfaces sitting on cream paper.

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
  colours outside any token. Both use a descendant combinator (`.paper ::-webkit-scrollbar-thumb`),
  so they reach in-page scroll containers but **not** the document scrollbar — see
  "Known limitations".

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
  five under Paper. **The `:root` gap was a separate pre-existing defect and was deliberately not
  fixed here** — defining them at `:root` changes Legacy rendering, which this slice's whole
  safety argument rests on not doing. Filed as `#1814` and **closed there**: `design-tokens.css`
  now aliases the five onto the Obsidian ladder at `:root`
  (`lowest`/`sunken` → `container-lowest`, `low` → `container-low`,
  `elevated`/`high` → `container-high`) and the per-site fallbacks were reconciled to agree.
  Paper is unaffected — the bridge's declarations sit on `<body>`, and a declaration on the
  element beats an inherited `:root` value.
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
  `.paper-night`, from one file — **for every colour that resolves through a token**. That is the
  large majority of the surface, but not all of it: six declarations in the `.td-*` utility layer
  hardcode Obsidian colour outside any token and are structurally unreachable by a token remap.
  They are enumerated under "Known limitations" and tracked in `#1817`.
- Legacy mode is untouched wherever the Paper classes are absent, which is every route a user
  actually browses in Legacy. The bridge declares nothing at `:root`, so outside a
  `.paper` / `.paper-night` subtree the `--td-*` tokens are undeclared and every Tailwind
  `var(--td-tw-x, #hex)` resolves to the original hex. **The correct quantifier is "no element
  carrying `.paper`/`.paper-night`", not "no `.paper` on `<body>`"** — two elements carry the class
  independently of `<body>`:
  - `views/paper/PaperReviewView.vue:1087` (`class="paper paper-review-deep"`) renders only behind
    `paperTheme.isOn` (`views/ReviewView.vue:10`), so it is unreachable in Legacy mode.
  - `views/PaperStyleGuideView.vue` frames both preview panes as `.paper` / `.paper-night`
    *regardless of the global mode* (line 76 `:class="['sg-frame', previewClass]"`, line 327 the
    opposite-theme mini frame), on a reachable route. So the style-guide route **does** change in
    Legacy — by exactly one declaration: `.sg-frame`'s `border: 1px solid var(--td-border-default)`
    (line 412) now resolves the bridge's `var(--line)` instead of `:root`'s `#2a2a2a`. Verified that
    every primitive rendered inside those frames (`PaperStamp`, `PaperHLBtn`, `PaperTagstamp`,
    `PaperStatusPill`, `PaperConfidenceDial`, `PaperCard`, `PaperIcon`, `PaperKbd`,
    `PaperLedgerRow`, `InkBleed`) consumes zero `--td-*` and zero Tailwind semantic colours, and the
    page chrome outside the frames (`.sg-root`, `.sg-toolbar`, `.sg-divider` — the file's other
    eight `--td-*` reads) sits outside both classes and is unaffected. The systemic question this
    raises — how far the Paper-idiom guard should extend, and what Legacy mode is ultimately for —
    is `#1815`.
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
  record. *(Superseded 2026-08-19 by `#1817`: the block was deleted after the `data-theme` grep was
  re-run and came back empty. See the status table under "Known limitations".)*
- The Paper Color Audit is unaffected: it scans `components/paper/**`, the AppShell paper region,
  and the `paper-tokens.css` hex count (70/70 baseline, unchanged). The bridge adds no hex outside
  comments and no tokens to `paper-tokens.css`. *(Still 70/70 after `#1817`: `--mute` changed value,
  not count, and the bridge's new rules are `var()` references.)*

## Known limitations

Measured at `53406cfd`. All of these are tracked in **`#1817`** unless noted; none of them is fixed
by this change, and a later session should not read the Positive bullets above as covering them.

> **Status update — 2026-08-19, `#1817`.** Every item below has since been dispositioned. The
> descriptions are kept as the record of what the interim floor shipped with; the current state is:
>
> | # | Disposition |
> |---|---|
> | 1 | **Split.** `.td-alert--error` and `.td-btn--ghost` are reached by Paper surfaces and now carry `.paper`-scoped overrides in the bridge; `.td-card` is Legacy-only (its one consumer renders under `BoardCanvas`, which `BoardView` swaps for `PaperBoardView`) and `.ghost-border` is dead (zero consumers repo-wide) — both recorded as permanent Legacy-only styling, not fixed. |
> | 2 | **Fixed**, but not by the extra selector this section predicted: `style.css` styles `::-webkit-scrollbar-thumb` on `<html>` too, which blocks the usual `<body>`→viewport propagation, so `.paper::-webkit-scrollbar-thumb` would not have reached it either. The standard `scrollbar-color` property does propagate and now carries the skin, with descendants reset to `auto` so in-page containers keep the webkit treatment. |
> | 3 | **Fixed.** `frontend/taskdeck-web/tests/paper-legacy-bridge-invariants.spec.ts` pins both invariants against tables read out of git at `0f6f9a5d^`, plus the scoping and single-source contracts. Mutation-checked. |
> | 4 | **Deleted.** The `[data-theme="light"]` block is gone; the `data-theme` grep was re-run first and now returns nothing in `src/` or `index.html`. The `[data-density]` siblings are untouched. |
> | 5 | **Fixed.** `--td-color-info` maps to `--ink-2`: Paper reserves its one accent hue for attention, so an informational foreground is neutral ink. |
> | 6 | **Fixed.** All four `*-light` tints are opaque Paper palette entries (`--td-color-info-light` → `--paper-edge`), and `--td-shadow-lg`/`-xl` are distinct steps again, composed from existing Paper tokens so `.paper-night` still inverts. |
>
> Also closed under `#1817`: the light-Paper ink ladder (`--mute` darkened to `#635c4e` so it is a
> real rung above `--faint`), the five notification accent stripes (now `--td-notify-*` tokens,
> Tailwind hues at `:root`, Paper hues in the bridge), the `:root`-vs-bridge depth-ladder
> disagreement (`--td-surface-elevated` joins `--td-surface-high` on both substrates), and
> `html { color-scheme: dark }` (the root element now follows the body skin via `:has()`).
>
> Closed since, under `#1842` (see the 2026-08-19 eyebrow-token amendment below): the eyebrow-token
> idiom split, `typeBadgeClass`'s Tailwind palette hues, the `never :root` guard's overstated name,
> and the unasserted contrast figures in permanent comments. `CaptureModal`'s scoped
> `.td-alert--error` was corrected to `--ember-ink`; the global utility's original pairing
> (`--ember` on `--ember-tint`) is a pre-existing sub-AA hairline this ledger had not recorded, and
> re-measuring it under `#1842` put it at **4.46:1**, not the 4.45:1 recorded here and in two code
> comments — corrected in all three places and now asserted exactly.

1. **Six hardcoded Obsidian values in the `.td-*` utility layer are unreachable by the remap.**
   `src/style.css` (227 lines) contains exactly six colour literals outside any token — verified by
   grepping hex, `rgb()`/`rgba()`, `hsl()`, `oklch()` and named colours across the whole file:
   - L41 `::-webkit-scrollbar-thumb` — `background: #5b403e`
   - L45 `::-webkit-scrollbar-thumb:hover` — `background: #ab8986`
   - L76 `.ghost-border` — `border: 0.5px solid rgba(91, 64, 62, 0.15)`
   - L88 `.td-card` — `border: 0.5px solid rgba(91, 64, 62, 0.05)`
   - L134 `.td-btn--ghost` — `border: 0.5px solid rgba(91, 64, 62, 0.2)`
   - L200 `.td-alert--error` — `border-color: rgba(255, 77, 77, 0.2)`

   L41/L45 are partly addressed by the bridge's scoped scrollbar rules (see 2). The other four are
   not addressed at all: warm-brown hairlines (`rgba(91,64,62,.15)` over `#f3eee5` ≈ `#e3dcd4`) and
   a hot-red alert border survive on cream, on primitives that the legacy views share. The blend is
   mild enough to read as a warm hairline rather than an Obsidian artefact, so this is an accuracy
   limit of the floor, not a legibility defect — but it is why the Positive bullet is qualified.
   Fixing it means either `.paper`-scoped overrides in the bridge
   (`.paper .td-card { border-color: var(--line-soft) }` and friends) or a decision to keep them as
   permanent Legacy-only styling. Note the two scoped-style call sites often cited alongside these
   (`components/inbox/InboxDetailPanel.vue`, `components/inbox/InboxListPanel.vue`) are **not** in
   this class: their Obsidian hex are `var(--td-*, #hex)` fallbacks, and the bridge defines every
   one of those tokens under Paper, so the fallbacks never fire there.

2. **The scoped scrollbar rules miss the document scrollbar.** `.paper` sits on `<body>`, and the
   viewport scrollbar is painted from `body` itself, so the descendant combinator in
   `.paper ::-webkit-scrollbar-thumb` cannot match it. The main page scrollbar therefore keeps
   `#5b403e` under Paper — the most visible surviving Obsidian artefact. The fix is one extra
   no-space selector per rule (`.paper::-webkit-scrollbar-thumb`), for both the base and `:hover`
   pairs.

3. **No automated guard for the two invariants the safety argument rests on.** "Legacy is
   byte-identical" and "every `--td-tw-*` the Tailwind config emits is defined in the bridge" are
   both true today, and both were established by hand, once. Nothing stops a later edit to
   `tailwind.config.js` from adding a colour with no bridge definition (an Obsidian hex leaks back
   in under Paper) or dropping a fallback (Legacy changes silently). Both are mechanical,
   file-parsing properties — the same shape as the existing `tests/theme/paperEmberContrast.spec.ts`
   — and a small spec asserting them would hold them permanently.

4. **`error` and `info` collapse to the same foreground under Paper.** `--td-color-error` and
   `--td-color-info` both map to `--ember`; Obsidian distinguished them (`#ff4d4d` vs `#ffb3ae`).
   The "Accent collapse" rationale above covers primary/error/ember and does *not* extend to `info`.
   Backgrounds still differ (`--ember-tint` vs `--ember-bloom`), so the two are not
   indistinguishable, but an info banner's foreground now reads as an error. Live consumers:
   `components/shell/AppShell.vue`, `components/review/ReviewProposalCard.vue`,
   `components/chat/ChatMessageList.vue`, `components/board/starter-pack/starter-pack-tokens.css`.

5. **`*-light` status tokens change from translucent to opaque, inconsistently.** Obsidian's
   `--td-color-{success,warning,error}-light` were `rgba(..., 0.15)`; the Paper mappings
   (`--applied-tint`, `--overdue-tint`, `--ember-tint`) are fully opaque, while
   `--td-color-info-light` → `--ember-bloom` keeps 10% alpha. Call sites that layered these over a
   non-default surface lose the blend, and the four are no longer consistent with one another.

6. **Modal/popover elevation flattens.** `--td-shadow-lg` and `--td-shadow-xl` both fold onto
   `--shadow-lift`. Paired with `--td-surface-container-high` (`#e3dac8`) sitting close to the page
   (`#f3eee5`), legacy dropdowns and popovers may lose separation from the page. This is the mapping
   most likely to produce the first per-view tuning report.

Items 4–6 are the per-token tuning pass; `#1817` also carries the pre-existing
`html { color-scheme: dark }` in `style.css`, which keeps native `<select>` popups, date pickers and
autofill dark inside the Paper shell (not introduced by this change).

## Amendments

- **2026-08-19 — Legacy ("off") mode is KEPT for the open beta as a supported plain skin, with the
  substrate legibility guarantee as its contract.** *(Autonomous coordinator ruling under the
  ADR-0051 admission lane — flagged for maintainer review. It settles the second half of `#1815`,
  the systemic question this ADR's Consequences section raised and deferred.)*

  The question `#1815` posed was what Legacy mode is ultimately *for*, given that the `tk-*` type
  utilities and the rest of the Paper idiom are inert there: a view migrated under Option B gets its
  Paper typography only inside `.paper` / `.paper-night`, so in Legacy the same component renders as
  plain, unstyled-display text on a plain ground. Two answers were live — keep the toggle as a
  supported skin, or retire it post-beta and make Paper the only shell.

  **Ruling: keep it, and define what "supported" means so the guarantee is testable.**

  - Legacy mode is a **supported plain skin**, not a second design system. Its contract is exactly
    one property: **every Paper-idiom view root that sets `--ink` also paints a Paper substrate**, so
    the root's literal fallbacks land together and clear WCAG AA. That is the invariant
    `frontend/taskdeck-web/src/tests/views/paperViewLegacySubstrate.spec.ts` pins mechanically, and
    as of this amendment it covers every Paper-idiom view root in the `#1769` wave — the four
    `#1780` roots, the `#1775` Saved Views root (`#1813`), the six Settings roots from PR #1808, and
    the secondary roots from PR #1810. That closes the first half of `#1815`.
  - **`tk-*` type utilities remain intentionally inert in Legacy, and that is not a defect.** They
    are Paper-scoped by construction. Legacy renders legible system-sans text; it does not render
    Paper's serif display type, hairlines or stamps, and no issue should be filed asking it to.
    Chasing typographic parity would recreate the second design system this project already decided
    against in ADR-0038.
  - **Retirement is deferred until post-beta usage data exists.** The free open beta (ADR-0044) is
    the first time real users will exercise the Appearance toggle; retiring an escape hatch before
    knowing whether anyone reaches for it would be a guess. Revisit once the beta yields usage data
    on the "off" setting — the removal is cheap and reversible either way, and none of the Option-B
    per-view migration work is wasted under either outcome, since it targets the Paper shell.

  Scope note: this amendment rules on Legacy mode's *fate and contract* only. It changes nothing
  about Option A's bridge, and the "Known limitations" ledger (`#1817`) is untouched by it.

- **2026-08-19 — The canonical eyebrow token is `--mute`, because that is what the core loop
  already renders.** *(`#1842`, deferred from `#1817`/PR #1840. Autonomous ruling under the
  ADR-0051 admission lane — flagged for maintainer review.)*

  The Paper view roots had split on the page-header eyebrow: seven tinted it `--mute`, thirteen
  `--ember`. PR #1808 declined to pick a side unilaterally, and picking one on taste would have
  repeated that. The ruling instead followed the surfaces users spend their time in.

  **Measured at `79428f0d`, before any change in this slice:**

  | Where | Evidence |
  | --- | --- |
  | The shared utility | `frontend/taskdeck-web/src/paper-tokens.css:242-244` — `.paper .tk-eyebrow, .paper-night .tk-eyebrow { font-family: var(--mono); font-size: 10px; color: var(--mute); ... }` |
  | Home | `src/views/paper/PaperHomeView.vue:337` (`class="tk-eyebrow paper-home__eyebrow"`), whose rule at `:540` sets only `text-transform: capitalize`; also `:414`, `:454` |
  | Inbox | `src/views/paper/PaperInboxView.vue:194` — bare `class="tk-eyebrow"` |
  | Boards | `src/views/paper/PaperBoardView.vue:256` — `paper-board-view__eyebrow` has **no CSS rule at all** |
  | Review | `src/views/paper/PaperReviewView.vue:1325`, `:1332` — bare `class="tk-eyebrow"` |

  Not one core-loop surface overrides the utility's colour, so all four already render the eyebrow
  at `--mute`. The expected ruling held; no inversion was needed.

  **Consequence.** Thirteen roots moved to `var(--mute, #635c4e)`: `AgentRunDetailView`,
  `AgentRunsView`, `AgentsView`, `ArchiveView`, `AutomationQueueView`, `BoardAccessView`,
  `CalendarView`, `IntegrationsView`, `MetricsView`, `NotFoundView`, `NotificationInboxView`,
  `OpsConsoleView`, `SavedViewsView` — every deviating root, not just the two `#1817` named. All 20
  Paper roots that have an eyebrow now agree (`AutomationChatView` and `DevToolsView` have none).

  `--ember` stays reserved for genuine accent/emphasis elements and is deliberately **not** swept:
  `ReviewChangeSection`'s "after" eyebrow, `ReviewKeysCard`, `PaperCardDetailView`'s banner eyebrow
  — card-level accents rather than page wayfinding — and `LoginView`/`RegisterView`'s
  `.td-auth-eyebrow`, which belongs to the pre-Paper auth shell rather than a Paper view root.

  `frontend/taskdeck-web/tests/paper-eyebrow-token.spec.ts` pins both halves of the ruling — the
  core-loop measurement and the 20 root rules — so the split cannot silently reopen. Reversible by
  a one-line token swap per root plus the two constants at the top of that spec.

  Shipped in the same slice, and needing no ruling: `typeBadgeClass`'s Tailwind palette hues moved
  to `--td-notify-*-{bg,fg}` tokens (the treatment PR #1840 gave the stripes; the dead `dark:`
  variants were dropped rather than translated, since `darkMode: 'class'` and nothing sets `dark`);
  the bridge's `never :root` guard was tightened to exempt its two `:root:has(> body.paper*)`
  color-scheme rules by name instead of by an accidental substring match; and every contrast figure
  stated in a permanent comment is now asserted to two decimal places rather than against the
  `>= 4.5` floor alone.

## References

- `#1778` — this decision; `#1769` — the Paper-shell sweep that found the shared root cause
- `#1842` — the per-token tuning follow-ups deferred from `#1817`/PR #1840 (eyebrow-token ruling,
  `typeBadgeClass`, guard-name precision, pinned contrast figures)
- `#1817` — the residuals ledger for every item under "Known limitations"; `#1815` — how far the
  Paper-idiom guard extends and Legacy mode's long-term fate; `#1814` — the undeclared
  `--td-surface-*` tokens as a `:root`-level defect
- `#1775` — Saved Views, the Option-B reference pattern; `#1779`/`#1780`/`#1781` — Option-B clusters
- ADR-0011 — Design Token System (Obsidian & Ember); ADR-0038 — Paper UI Is the Canonical Frontend
- `frontend/taskdeck-web/src/paper-legacy-bridge.css` — the Option A implementation
- `frontend/taskdeck-web/src/design-tokens.css`, `src/style.css`, `src/paper-tokens.css`,
  `tailwind.config.js`, `src/store/paperThemeStore.ts` — the substrates described above
