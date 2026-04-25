# PAPER-00 · Master Tracker · Paper & Graphite, Ember Edition Overhaul

## Thesis

Adopt the **Paper & Graphite, Ember Edition** visual + interaction system across the Taskdeck frontend. The system encodes the product's core thesis — capture → review → apply with no silent mutations — directly in materials (cream paper, hairline rules, italic serif, single seal-red ember accent) rather than helper labels. Trust signals are physical: applied stamps emboss, undo windows literally erase as the 6-hour grace closes.

Source of truth: `design_handoff_taskdeck_paper/` checked into the repo (README + JSX prototypes + `paper/tokens.css`).

## Strategic intent

- Replace the current Obsidian-toned shell across all primary surfaces with Paper-Light as canonical, Paper-at-Night as the dark companion (strict mirror of metaphor).
- Make Review the centerpiece: implement the *deep* version with provenance, side-effects, conflicts, history, confidence dial, undo timeline, similar-past decisions.
- Replace every `loading…` / spinner state in LLM flows with **Ink Bleed** — a 5-phase, 4.6s motion that dries to a stamp.
- Keep behavior unchanged: this is presentation + interaction polish, not a backend rewrite. All capture / proposal / approval semantics remain the same.

## Non-goals

- No backend changes beyond what surface needs require (e.g., new fields the dossier needs).
- No new product features beyond what the design implies (e.g., "Seal day", "A line for tomorrow", undo timer).
- No removal of the existing `--td-*` token system in this overhaul. Paper sits parallel under `.paper` / `.paper-night` scope until migration is ready to flip canonical.
- Multiplayer/CRDT explicitly out of scope.

## Phasing

### Phase 1 · Foundation (must land first, blocks everything else)
- **PAPER-01** Tokens + theme classes + fonts + paper substrate
- **PAPER-02** Component primitives (Stamp, HLBtn, Tagstamp, Card, kbd, hairline icons)

### Phase 2 · Shell
- **PAPER-03** Sidebar + TopBar in Paper

### Phase 3 · Surfaces (parallelizable after Phase 1+2)
- **PAPER-04** Home / morning reset
- **PAPER-05** Board / kanban with index card variant
- **PAPER-06** Review *deep* — central surface
- **PAPER-07** Inbox / capture (composer + nib)
- **PAPER-08** Today / end-of-day dossier
- **PAPER-09** Misc surfaces (Card detail, ⌘K palette, Shortcuts overlay, Toasts, Empty states)

### Phase 4 · Motion + responsive + dark
- **PAPER-10** Ink Bleed motion system (signature LLM thinking state)
- **PAPER-11** Narrow companions (375 phone, 768 tablet)
- **PAPER-12** Paper at Night dark theme verification across all surfaces

## Acceptance gates per slice

Every slice must:
- Land behind the Paper feature flag (or `.paper` body class scope) so existing Obsidian surfaces remain green during migration.
- Ship vitest unit/component tests for new primitives + DOM-level assertions for view changes.
- Pass `npm run typecheck` + `npm run lint` (max 20 warnings) + backend `dotnet build` if backend touched.
- Pass an adversarial review pass that explicitly checks: token misuse, ember-accent overuse (must remain ≤5% of any surface's surface area), serif italic in non-decision copy, missing reduced-motion fallback for any animation, focus rings replacing browser default, hairlines surviving zoom 200%.
- Update `docs/STATUS.md` if user-visible behavior changes; cross-link the master tracker.

## Risk register

- **Token collision** — `--td-*` and Paper variables both define ember/paper hues. Mitigation: scope all Paper variables under `.paper`/`.paper-night` and namespace anything new (`--td-paper-*`).
- **Serif loading** — Fraunces is heavy and italic-variable. Self-host with `font-display: swap` to avoid FOUC; pre-cache critical weights.
- **Density** — Paper is a high-density system. 200-card boards must remain frame-stable. Re-verify the perf budget (`docs/PERFORMANCE_BUDGETS.md`).
- **Accent overuse** — single-pigment metaphor breaks if ember leaks into incidental UI. Linter rule: ember class allow-list per surface.
- **Motion accessibility** — Ink Bleed is bold motion. `@media (prefers-reduced-motion)` must reduce to a 200ms opacity fade, no exceptions.

## Reference

- README: `design_handoff_taskdeck_paper/README.md` (canonical spec)
- Tokens: `design_handoff_taskdeck_paper/paper/tokens.css`
- Components: `design_handoff_taskdeck_paper/paper/components.jsx`
- Surfaces: `design_handoff_taskdeck_paper/paper/surface-*.jsx`
- Prototype entry: open `design_handoff_taskdeck_paper/Taskdeck Paper Edition.html` in a browser

## Definition of done for the whole tracker

- All PAPER-01 .. PAPER-12 closed and merged behind the Paper flag.
- A `/styleguide?theme=paper` (and `paper-night`) page renders every component primitive + every surface.
- A workspace toggle promotes Paper to canonical; a follow-up tracker decommissions Obsidian.
- Adversarial review on the integrated experience signs off on accent discipline and motion accessibility.

---

## Child issues

| Phase | Issue | Title |
|---|---|---|
| 1 | #997 | PAPER-01 · Foundation — tokens, theme, fonts, substrate |
| 1 | #998 | PAPER-02 · Component primitives |
| 2 | #999 | PAPER-03 · Sidebar + TopBar shell |
| 3 | #1000 | PAPER-04 · Home / Reset |
| 3 | #1001 | PAPER-05 · Board / Kanban |
| 3 | #1002 | PAPER-06 · Review (Deep) |
| 3 | #1003 | PAPER-07 · Inbox / Capture |
| 3 | #1004 | PAPER-08 · Today / End-of-day dossier |
| 3 | #1005 | PAPER-09 · Misc surfaces |
| 4 | #1006 | PAPER-10 · Ink Bleed motion |
| 4 | #1007 | PAPER-11 · Narrow companions |
| 4 | #1008 | PAPER-12 · Paper at Night |
