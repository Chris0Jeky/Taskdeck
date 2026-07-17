# PAPER-02 · Component primitives — Stamp, HLBtn, Tagstamp, Card variants, Kbd, Hairline icons

> **Partially superseded by `#1298` (REVIVAL-02, 2026-07-17):** the `PaperUndoTimeline.vue` primitive
> below was built and then **removed** — it advertised an undo window but no revert endpoint exists.
> Do not rebuild it from this spec.

Part of the Paper overhaul (master tracker: PAPER-00). **Blocked by:** PAPER-01.

## Goal

Provide reusable Vue SFC primitives that every Paper surface composes from. Match the JSX reference behavior pixel-for-pixel within Vue 3 idioms.

## Components (under `frontend/taskdeck-web/src/components/paper/`)

- **`PaperStamp.vue`** — props `kind: 'applied' | 'proposed' | 'captured' | 'overdue' | 'draft'`, `date`, `time`, `num`, `rotate?` (default −7° to −9° randomized once on mount). Embossed inset on `applied`. On undo, crossfade to `proposed` over 240ms.
- **`PaperHLBtn.vue`** — props `label`, `kbd?`, `icon?` (slot), `variant: 'default' | 'primary' | 'ember' | 'ghost'`. Renders the keyboard hint inline with a 1px vertical divider. `:active` `translateY(1px)`.
- **`PaperTagstamp.vue`** — slot for label, `tone?: 'ember' | 'applied' | 'overdue' | 'mute'`. Letter-spacing **.22em**.
- **`PaperCard.vue`** — `variant: 'flat' | 'lift' | 'well'`, `halo?: boolean`. Default slot.
- **`PaperKbd.vue`** — slot for the key glyph. `light?: boolean`.
- **`PaperHairlineIcons.vue`** — exporting named icon components mirroring `paper/icons.jsx` (Plus, Search, Stamp, Sparkle, ArrowRight, X, Check, Pages, Pen, Cursor, Tag, Dot, Eye, Bell, ChevronD, ChevronR, Settings, Sun, Moon). 14–16px, `stroke="currentColor"`, no fill.
- **`PaperStatusPill.vue`** — `kind: 'proposed' | 'applied' | 'overdue' | 'draft' | 'live'`. `live` pulses at 0.6Hz.
- **`PaperLedgerRow.vue`** — `idx`, `title`, `meta`, `status?`, click-to-open.
- **`PaperConfidenceDial.vue`** — `value: number 0..1`, 84px SVG, ember stroke-dasharray, serif italic value, mono "CONF" caption.
- ~~**`PaperUndoTimeline.vue`** — `appliedAt`, `windowMs = 6h`, dashed timeline crossfading dashes left-to-right. `requestAnimationFrame` capped at 1Hz with reduced-motion fallback.~~ *[superseded by `#1298` (2026-07-17): fake undo removed — no revert endpoint exists]*

## Tests

- vitest unit tests per component.
- Storybook stories in `src/stories/paper/` per primitive in both themes.
- Snapshot test for `PaperConfidenceDial value={0.84}`.

## Adversarial review checklist

- [ ] Stamp rotation computed once on mount.
- [ ] HLBtn `kbd` slot does not break layout for wide keys (`space`, `⌘K`).
- [ ] Confidence dial respects `prefers-reduced-motion`.
- [ ] Undo timeline cleans up rAF on unmount.
- [ ] No primitive imports a surface or store.
- [ ] All primitives render correctly inside `.paper-night` without a dark-mode prop.
- [ ] Hairline icons use `currentColor`.
