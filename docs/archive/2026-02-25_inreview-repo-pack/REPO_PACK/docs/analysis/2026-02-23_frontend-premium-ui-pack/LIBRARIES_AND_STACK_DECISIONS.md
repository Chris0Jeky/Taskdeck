# Libraries and Stack Decisions (Vue 3 + Tailwind) — Options + Recommendations
Date: 2026-02-23
Status: Draft

Taskdeck currently uses Vue 3 + TS + Pinia + Vite + Tailwind 3.x. The dependency footprint is intentionally small.

This document outlines **UI component strategy options** and a decision plan.

---

## Decision: “UI kit” vs “headless primitives” vs “roll your own”

### Option A — Full UI kit (Vuetify / PrimeVue / Naive UI, etc.)
**Pros**
- fast to ship complete screens
- consistent components out of the box
- often includes accessibility work

**Cons**
- harder to make uniquely “Taskdeck” (many apps look identical)
- design tokens and theming often fight your own token system
- component APIs can leak opinionated patterns into your app architecture

Recommendation: Only choose this if your top priority is *speed over uniqueness*.

### Option B — Headless primitives + your styling (recommended)
You use accessibility-focused primitives and implement your own styling + tokens.

Candidates:
- **Radix Vue** (Vue port of Radix UI primitives)  
  https://www.radix-vue.com/overview/introduction
- **Headless UI (Vue)** (unstyled, accessible components designed to integrate with Tailwind)  
  https://headlessui.com/  (Vue supported)

**Pros**
- accessibility logic is reusable and tested
- you control visuals completely (premium cohesion)
- works well with Tailwind and design tokens

**Cons**
- you still have to build your own “design system layer” (variants, sizes, spacing, icons)
- you must decide conventions (component props, classes, patterns)

Recommendation: Best long-term for a premium product.

### Option C — shadcn-vue (copy/paste components on top of Radix Vue)
shadcn-vue provides a set of reusable components built with Radix Vue and Tailwind, designed to be copied into your codebase (ownership model).  
https://radix.shadcn-vue.com/docs/introduction

**Pros**
- accelerates premium visuals quickly
- you own the code (no black box)
- aligned with Radix primitives and Tailwind

**Cons**
- community-led; you maintain updates/patches yourself
- you may need to adapt components to your tokens and density needs

Recommendation: Excellent accelerator *if* you accept ownership.

---

## Supporting libraries (strongly recommended)

### Positioning: Floating UI
For tooltips, popovers, dropdowns, context menus — robust collision-aware positioning.
Docs: https://floating-ui.com/docs/getting-started  
GitHub: https://github.com/floating-ui/floating-ui

### Drag-and-drop: Pragmatic Drag and Drop (Atlassian)
Framework-agnostic, headless, incremental; designed for high-performance DnD.  
GitHub: https://github.com/atlassian/pragmatic-drag-and-drop  
Docs: https://atlassian.design/components/pragmatic-drag-and-drop/

(Especially relevant to Taskdeck board drag/move UX.)

### Composables: VueUse
A large collection of well-tested Vue composables (keyboard, media query, debounced refs, etc.).  
https://vueuse.org/

### Server-state caching (optional): TanStack Query for Vue
If your API calling + caching becomes complex, TanStack Query is a standard choice.  
https://tanstack.com/query/v4/docs/vue/overview

---

## Tailwind + design tokens strategy
You already have CSS variable tokens in `src/design-tokens.css`.
Two approaches:

1) Keep Tailwind 3.x, bind utilities to CSS vars via config and component classes.
2) Upgrade later to Tailwind 4’s CSS-first theming (`@theme`) if/when you want it.  
Tailwind theme variables: https://tailwindcss.com/docs/theme

---

## Decision Spike plan (1–2 days, time-boxed)
Create a short spike to avoid “library bikeshedding”.

### Deliverables
1) Implement 3 primitives using each candidate strategy:
- Button (variants + disabled + loading)
- Dialog/Modal (focus trap + ESC + overlay)
- Dropdown menu (positioning + keyboard support)

2) Evaluate against acceptance criteria:
- keyboard behavior correct (tab order, esc, focus restore)
- styling matches your tokens and density needs
- minimal bundle size impact and build complexity
- developer ergonomics in Vue SFCs

### Recommendation
Run the spike for:
- Radix Vue + your own styling
- shadcn-vue (copy/paste) for the same components

Pick one and standardize.

---

## Suggested final choice (most aligned with your goals)
- Radix Vue primitives as baseline.
- Use shadcn-vue selectively for “fast premium components” early.
- Floating UI for edge positioning needs (tooltips, context menus).
- Pragmatic DnD for board interactions.
- Storybook for component-driven development.

Rationale:
- premium feel requires cohesive primitives + a11y
- Taskdeck already invests in discipline and long-term maintainability
