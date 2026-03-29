# UI-03: Frontend Primitive Stack Decision Spike

Date: 2026-03-28
Issue: #245
Status: Decision Record (spike complete)

## Context

Taskdeck needs a shared UI primitive layer to replace the current ad-hoc component patterns (hand-rolled modals with manual escape handling, inline button styling, no shared dropdown/menu component). The chosen stack must integrate with:

- **Vue 3** with `<script setup>` composition API and TypeScript
- **Tailwind CSS 3.x** utility classes
- **Existing design tokens** (`design-tokens.css` -- Obsidian & Ember theme with `--td-*` custom properties)
- **Keyboard-first interaction model** (Taskdeck is an execution workspace; keyboard shortcuts are central)

This spike evaluates three candidates against explicit criteria and records a recommendation that unblocks #244 (shared UI primitives baseline).

## Candidates

| # | Candidate | Package | Description |
|---|-----------|---------|-------------|
| A | **Reka UI** (formerly Radix Vue) | `reka-ui` | Vue 3 port of Radix UI headless primitives. Unstyled, accessibility-focused. |
| B | **shadcn-vue** | `shadcn-vue` (CLI) + `reka-ui` (runtime dep) | Copy-paste component library built on Reka UI + Tailwind. You own the source. |
| C | **Headless UI** | `@headlessui/vue` | Tailwind Labs official headless components for Vue. Smaller component surface. |

Note: "Radix Vue" was renamed to "Reka UI" in late 2024. shadcn-vue v1.x uses Reka UI as its primitive layer.

## Evaluation Criteria

### 1. Component Coverage for Target Primitives

| Primitive | Reka UI | shadcn-vue | Headless UI |
|-----------|---------|------------|-------------|
| **Button** | No dedicated component (render-as-child pattern) | Yes -- styled Button with variants, sizes | No dedicated component |
| **Dialog/Modal** | `Dialog`, `DialogContent`, `DialogOverlay`, `DialogTitle`, `DialogDescription`, `DialogClose` | Full Dialog with Header/Footer/Title/Description wrappers | `Dialog`, `DialogPanel`, `DialogTitle`, `DialogDescription` |
| **Dropdown/Menu** | `DropdownMenu` with full sub-menu, checkbox/radio items, typeahead | Full DropdownMenu with Label/Separator/Item/Group wrappers | `Menu`, `MenuButton`, `MenuItems`, `MenuItem` |

**Verdict**: Reka UI and shadcn-vue have the broadest primitive surface. Headless UI covers the basics but has no Button primitive and a smaller menu feature set (no sub-menus, no typeahead, no checkbox/radio items). Reka UI has no styled Button either, but that is expected for a headless library.

### 2. Keyboard / Focus Correctness

| Criterion | Reka UI | shadcn-vue | Headless UI |
|-----------|---------|------------|-------------|
| WAI-ARIA pattern adherence | Full (Dialog, Menu, Combobox, etc.) | Inherits from Reka UI | Full (Dialog, Menu, Listbox, Switch, etc.) |
| Focus trapping (Dialog) | Yes -- automatic | Yes -- inherits | Yes -- automatic |
| Roving tabindex (Menu) | Yes | Yes -- inherits | Yes |
| Typeahead in menus | Yes | Yes -- inherits | No |
| Escape key handling | Yes -- per-component | Yes -- inherits | Yes -- per-component |
| Initial focus control | `onOpenAutoFocus` callback | Inherits | `initialFocus` ref prop |

**Verdict**: Reka UI and shadcn-vue lead on keyboard richness (typeahead, sub-menu arrow-key navigation). Headless UI covers WAI-ARIA basics well but lacks typeahead and some advanced menu patterns. For a keyboard-first product like Taskdeck, the Reka UI foundation is stronger.

### 3. Token / Theming Fit

| Criterion | Reka UI | shadcn-vue | Headless UI |
|-----------|---------|------------|-------------|
| Ships with styles? | No -- fully headless | Yes -- Tailwind utility classes, configurable via CSS variables | No -- fully headless |
| Token integration effort | Low -- you write all styles; tokens map directly | Medium -- shadcn-vue ships its own CSS variable system (`--background`, `--foreground`, etc.) that must be remapped to `--td-*` tokens | Low -- you write all styles |
| Tailwind class compatibility | Full (no style opinions) | Full (designed for Tailwind) | Full (designed for Tailwind) |
| Dark/light mode | You control entirely | Built-in dark mode via CSS variables | You control entirely |

**Verdict**: Reka UI and Headless UI are equally clean for token integration since they are unstyled. shadcn-vue requires remapping its default CSS variable scheme to Taskdeck's `--td-*` tokens, which is manageable but adds an initial integration step. The benefit is that shadcn-vue provides a starting point for visual design rather than starting from zero.

### 4. Implementation Ergonomics (Vue 3 SFCs)

| Criterion | Reka UI | shadcn-vue | Headless UI |
|-----------|---------|------------|-------------|
| Vue 3 `<script setup>` support | Native (built for Vue 3) | Native (built for Vue 3) | Native (built for Vue 3) |
| TypeScript support | Full -- typed props, emits, slots | Full -- typed props, emits, slots | Full -- typed props, emits |
| Render delegation (`as`/`asChild`) | `asChild` prop on all components | Inherits `asChild` from Reka UI | `as` prop on most components |
| Slot-based composition | Scoped slots with state exposure | Scoped slots inherited from Reka UI | Render props / scoped slots |
| Component installation model | `npm install reka-ui` -- import and use | CLI (`npx shadcn-vue add`) copies source files into your project | `npm install @headlessui/vue` -- import and use |

**Verdict**: All three work well with Vue 3 + TypeScript + `<script setup>`. shadcn-vue's copy-paste model means you own the code and can modify freely, but it also means you maintain updates. Reka UI and Headless UI are standard npm dependencies with normal semver updates.

### 5. Bundle and Dependency Impact

| Criterion | Reka UI | shadcn-vue | Headless UI |
|-----------|---------|------------|-------------|
| Package size (npm install) | ~180 KB (tree-shakeable, import only what you use) | No runtime package -- source is copied; runtime dep is Reka UI | ~70 KB (tree-shakeable) |
| Runtime dependencies | `@floating-ui/vue` for positioning | Reka UI + `@floating-ui/vue` + optional: `class-variance-authority`, `clsx`, `tailwind-merge` | No external positioning dep (uses internal implementation) |
| Tree-shaking | Excellent -- per-component imports | Excellent -- you control what files exist | Good -- per-component imports |
| Current Taskdeck deps affected | Adds `reka-ui` + `@floating-ui/vue` | Adds `reka-ui` + `@floating-ui/vue` + utility libs | Adds `@headlessui/vue` only |

**Verdict**: Headless UI has the smallest dependency footprint. Reka UI / shadcn-vue add Floating UI as a transitive dependency, but Floating UI is a well-maintained, focused library that Taskdeck may want anyway for tooltips and popovers. The difference is modest for a Vite-bundled app with tree-shaking.

### 6. Maintenance and Community Health

| Criterion | Reka UI | shadcn-vue | Headless UI |
|-----------|---------|------------|-------------|
| Maintainer | Community (unovue org) | Community (unovue org) | Tailwind Labs (commercial) |
| GitHub stars | ~4K (Reka UI, growing) | ~6K (shadcn-vue) | ~26K (combined React+Vue) |
| Release cadence | Active (monthly releases) | Active (tracks Reka UI + shadcn/ui React) | Slower (last major Vue update less frequent than React counterpart) |
| Vue-first? | Yes -- Vue 3 is the primary target | Yes -- Vue 3 is the primary target | No -- React is primary; Vue port is maintained but secondary |
| Component breadth | 40+ primitives (Dialog, Menu, Combobox, Tabs, Accordion, Tooltip, Popover, Select, etc.) | 50+ composed components (wraps Reka UI primitives with styled defaults) | ~12 components (Dialog, Menu, Listbox, Combobox, Switch, Tabs, Popover, Disclosure, RadioGroup, Transition) |
| Breaking change risk | Moderate (still pre-1.0 but stabilizing) | Low (you own copied source; upstream breaks don't auto-propagate) | Low (stable API, infrequent changes) |

**Verdict**: Headless UI has the strongest single-entity backing (Tailwind Labs) but Vue is its secondary target and the component surface is limited. Reka UI is Vue-first with active development and broader coverage. shadcn-vue adds an ownership model that insulates from upstream breaks but requires manual update adoption.

## Comparison Matrix (Summary)

| Criterion (weight) | Reka UI | shadcn-vue | Headless UI |
|---------------------|---------|------------|-------------|
| Component coverage (high) | Strong | Strongest | Limited |
| Keyboard/focus correctness (high) | Strongest | Strong (inherits) | Good |
| Token/theming fit (medium) | Clean | Requires remapping | Clean |
| Vue 3 SFC ergonomics (medium) | Excellent | Excellent | Excellent |
| Bundle impact (low) | Moderate | Moderate | Smallest |
| Community health (medium) | Good (Vue-first) | Good (Vue-first) | Good (but Vue secondary) |

## Decision

**Chosen stack: shadcn-vue (Option B)**

### Rationale

1. **Best starting velocity for #244**: shadcn-vue provides pre-composed, styled components (Button, Dialog, DropdownMenu, and 50+ more) that work immediately with Tailwind. This accelerates the primitives baseline delivery without sacrificing long-term control.

2. **Ownership model fits Taskdeck**: Components are copied into the project source (`src/components/ui/`). Taskdeck can freely modify variants, token mappings, and accessibility behavior without waiting for upstream releases. This aligns with the project's local-first, control-oriented philosophy.

3. **Reka UI foundation provides keyboard excellence**: shadcn-vue is built on Reka UI, so Taskdeck inherits the full WAI-ARIA keyboard/focus implementation (typeahead, sub-menus, roving tabindex, focus trapping) without implementing it from scratch.

4. **Token remapping is a one-time cost**: shadcn-vue's default CSS variables (`--background`, `--foreground`, etc.) need to be mapped to Taskdeck's `--td-*` tokens. This is a bounded, one-time integration task during #243 (design tokens foundation) and #244 (primitives baseline). After remapping, all components automatically use the Obsidian & Ember theme.

5. **Broader surface unblocks future UI issues**: The 50+ component library means #246 (AppShell reskin), #247 (board polish), #249 (inbox primitives), and future UI work can draw from an existing, consistent primitive set rather than building each from scratch.

### Tradeoffs Accepted

- **Maintenance burden**: Taskdeck owns the component source and must manually adopt upstream improvements. Mitigation: shadcn-vue CLI supports diffing against upstream, and the components are well-structured for selective updates.
- **Additional dependencies**: Adds `reka-ui`, `@floating-ui/vue`, `class-variance-authority`, `clsx`, and `tailwind-merge` as runtime or dev dependencies. These are all small, focused, tree-shakeable libraries. Total additional bundle impact is estimated at <15 KB gzipped for the three target primitives.
- **Initial token remapping**: Requires mapping shadcn-vue's CSS variable conventions to `--td-*` tokens. This is scoped to #243/#244 and does not leak into other work.

### Why Not the Others

- **Reka UI alone**: Viable but means building all visual design (variants, sizes, spacing, composed structure) from scratch. shadcn-vue provides this layer and is built on Reka UI anyway. If Taskdeck later needs to drop down to raw Reka UI primitives for a custom component, that path remains open.
- **Headless UI**: Too limited in component surface (12 vs 50+), Vue is a secondary target, and it lacks keyboard features Taskdeck needs (typeahead, sub-menus). The smaller bundle advantage does not justify the implementation cost of building everything shadcn-vue already provides.

## Implementation Notes for #244

1. Initialize shadcn-vue in the project: `npx shadcn-vue@latest init`
2. Configure the CSS variable mapping in `design-tokens.css` to alias `--td-*` tokens to shadcn-vue's expected variables
3. Add the three baseline primitives: `npx shadcn-vue@latest add button dialog dropdown-menu`
4. Adapt generated components in `src/components/ui/` to use Taskdeck token references
5. Replace existing ad-hoc modal/dialog patterns (e.g., `CardModal.vue`, `CaptureModal.vue`) with the new Dialog primitive in subsequent issues

## References

- [Reka UI docs](https://reka-ui.com) (formerly radix-vue.com)
- [shadcn-vue docs](https://www.shadcn-vue.com/)
- [Headless UI docs](https://headlessui.com/)
- [Existing Taskdeck UI synthesis](docs/analysis/2026-02-23_frontend-premium-ui-synthesis.md)
- [Archived library analysis](docs/archive/2026-02-25_inreview-repo-pack/REPO_PACK/docs/analysis/2026-02-23_frontend-premium-ui-pack/LIBRARIES_AND_STACK_DECISIONS.md)
