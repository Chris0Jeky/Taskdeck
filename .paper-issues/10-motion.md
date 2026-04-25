# PAPER-10 · Ink Bleed motion system (signature LLM thinking state)

Part of the Paper overhaul (master tracker: PAPER-00). **Blocked by:** PAPER-01.

## Goal
Implement Ink Bleed. Replaces every `loading…` / spinner / skeleton in LLM-driven flows. 5 phases, total 4.6s.

## Spec
| Phase | Time | What |
|---|---|---|
| Drop | 0 — 0.4s | Single seal-red droplet falls. Page settles 1px on impact. |
| Bloom | 0.4 — 1.4s | First bleed grows to ~40% radius. Edge irregularity hand-drawn (4 droplets at varied positions). |
| Compose | 1.4 — 3.4s | Subsequent droplets land on rhythm; headline reveals through wet/dry mask. |
| Settle | 3.4 — 4.2s | Bleed desaturates ember → ink-deep as it dries. Each droplet has its own dry curve. |
| Stamp | 4.2 — 4.6s | Round seal embosses with 1px shadow (`scale(.96) translateY(1px)` then released). |

### Easings
- drop `cubic-bezier(.45,0,.15,1)` 260ms
- bloom-scale `cubic-bezier(.2,.65,.25,1)` 1000ms
- bloom-opacity linear 1400ms
- reveal-mask `cubic-bezier(.3,.8,.3,1)` 2000ms
- stamp-press `cubic-bezier(.4,0,.15,1)` 320ms

### Constraints
- 4 droplets at irregular positions; not symmetric.
- Bloom radius min 80px / max 240px responsive.
- `mix-blend-mode: multiply` always.
- `filter: blur(6px)` growing to `blur(10px)` during dry phase.
- `prefers-reduced-motion` → 200ms opacity fade only.
- No-JS fallback → render dried + stamped state.

### Where it appears
- Review awaiting proposal headline · full · auto-pause on scroll
- Inbox capture · compose loop only · 1.4–3.4s
- Command palette AI-action row · drop+bloom · 0–1.4s, single droplet
- Card detail open with pending proposal · drop only · 0–0.4s on the badge
- Toast "Proposed" · bloom only · 0.6s

### Don'ts
- Don't loop bloom indefinitely.
- Don't tint the bloom anything but ember.
- Don't run two bleeds simultaneously on the same view.

## Implementation
- Create `components/paper/InkBleed.vue` with props `phase`, `headline?`, `containerSize?`. Default `auto` runs the full 4.6s sequence then holds dried.
- `useInkBleed` composable orchestrates LLM call + bleed: starts on call begin, holds dried beyond 4.6s, finalizes stamp on completion.
- Wire each spec usage as follow-up commits inside this slice.

## Tests
- vitest fake timers: phase advancement at 0/400/1400/3400/4200/4600ms.
- vitest: with `prefers-reduced-motion`, only opacity fade.
- vitest: composable holds dried beyond 4.6s, finalizes on completion.
- Playwright visual smoke (no error mount + screenshot).

## Adversarial review
- [ ] No `setInterval` leaks — all timers cleared on unmount.
- [ ] Reduced motion short-circuits in the composable, not just CSS.
- [ ] Two bleeds on same view coexist via singleton guard (last-write-wins).
- [ ] No ember leak outside bleed boundary (overflow hidden on container).
- [ ] Audio is opt-in only and respects sound-on workspace toggle.
