# Component Primitives Spec (UI Foundation)
Date: 2026-02-23
Status: Draft

Goal: define a small set of primitives that every screen uses.
If you do this well, screens become assembly, not custom CSS.

---

## Required primitives (MVP)
### Buttons
- `TdButton`
  - variants: `primary|secondary|ghost|danger`
  - sizes: `sm|md|lg`
  - props: `loading`, `disabled`, `iconLeft`, `iconRight`
  - keyboard: default button semantics; focus visible
  - a11y: disabled state communicated

- `TdIconButton`
  - square target size; tooltip support
  - WCAG target size minimum is 24x24 CSS px (guidance).  
    https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html

### Form controls
- `TdInput`, `TdTextarea`
- `TdSelect` (headless primitive recommended)
- `TdSwitch`, `TdCheckbox`
- `TdField` wrapper (label + hint + error)

### Overlays
- `TdDialog` (modal)
- `TdDrawer` (optional; off-canvas)
- `TdPopover`
- `TdDropdownMenu`
- `TdTooltip`

Positioning:
- use Floating UI or library-provided positioning.
Floating UI docs: https://floating-ui.com/docs/getting-started

### Feedback
- `TdToast` + container (you already have)
- `TdInlineAlert` (info/warn/error/success)
- `TdSpinner`
- `TdSkeleton`

### Data display
- `TdBadge` / chips (labels, statuses)
- `TdTag` (interactive tag)
- `TdDivider`
- `TdKbd` (keyboard hint)
- `TdEmptyState` (title, description, primary CTA, optional secondary)

---

## Cross-cutting component requirements
Every primitive must define:
1) states: default / hover / active / focus / disabled
2) density support: comfortable vs compact
3) theming: light vs dark
4) motion: consistent timing and reduced motion support
5) keyboard behavior: escape stack for overlays, focus restore
6) test hooks: stable `data-testid` or role-based selectors

---

## Variant system (recommended)
Adopt a variant strategy so you don't manually manage classes everywhere.

Options:
- simple: `computed(() => ...)` mapping variant→classes
- advanced: class variance helpers (CVA-style)
- or follow shadcn-vue conventions if you adopt it

---

## Component directory layout (recommended)
```
src/components/ui/        # primitives only
src/components/shell/     # app chrome
src/components/board/     # board feature components
src/components/inbox/     # capture/inbox feature
src/composables/          # UI composables (useHotkeys, useEscapeStack, etc.)
src/styles/               # tokens + base styles + component classes
```

---

## “Definition of Done” for a primitive
- used in at least 2 feature screens
- passes keyboard-only navigation sanity
- has unit tests for major states
- has a Storybook story (if you adopt Storybook)
