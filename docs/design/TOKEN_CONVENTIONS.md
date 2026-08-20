# Design Token Conventions

Source file: `frontend/taskdeck-web/src/design-tokens.css`

## Token Naming Scheme

All tokens use the `--td-` prefix and follow the pattern:

```
--td-{category}-{variant}
```

| Category     | Examples                                        |
| ------------ | ----------------------------------------------- |
| `font`       | `--td-font-sm`, `--td-font-base`                |
| `space`      | `--td-space-1` through `--td-space-16`           |
| `surface`    | `--td-surface-base`, `--td-surface-container-*`  |
| `color`      | `--td-color-primary`, `--td-color-ember`          |
| `text`       | `--td-text-primary`, `--td-text-muted`            |
| `border`     | `--td-border-default`, `--td-border-focus`        |
| `shadow`     | `--td-shadow-sm` through `--td-shadow-xl`         |
| `radius`     | `--td-radius-sm` through `--td-radius-xl`         |
| `transition` | `--td-transition-fast`, `--td-transition-smooth`  |
| `glass`      | `--td-glass-bg`, `--td-glass-blur`                |

## Semantic Tokens vs Raw Values

**Rule: always use semantic tokens; never hard-code colors or sizes.**

```css
/* Good */
background: var(--td-surface-container);
color: var(--td-text-primary);

/* Bad */
background: #201f1f;
color: #e5e2e1;
```

Semantic tokens adapt automatically to theme and density changes. Hard-coded values break when the user switches themes.

### Migration Rule

When you touch a component, replace any hard-coded color, spacing, or shadow values with the appropriate token. Do not introduce new hard-coded values in any file you modify.

## Theme Contract

The design system ships two skins controlled by a class on `<body>` (there is no `data-theme` attribute — the dead `[data-theme="light"]` block was removed in PR #1840, see ADR-0053):

| Body class                 | Description                                    |
| -------------------------- | ---------------------------------------------- |
| _(absent / unset)_         | **Legacy (Obsidian, dark)** — the `:root` default |
| `.paper` / `.paper-night`  | **Paper** (light / night) — the product skin   |

### How skins work

- `:root` in `design-tokens.css` defines the full Legacy (Obsidian) token set and is never remapped in place.
- `.paper` / `.paper-night` re-point the shared `--td-*` aliases at Paper values through `paper-legacy-bridge.css` (ADR-0053); Paper's own palette lives in `paper-tokens.css`.
- Typography, spacing, radius, shell layout, and motion tokens are skin-independent.

### Applying a skin

The Paper skin is applied by `paperThemeStore` (`resolveBodyClass`) as a `<body>` class — never set it by hand in component code:

```js
// Appearance → Paper / Paper Night / Off (Legacy) drives:
document.body.classList.add('paper');        // or 'paper-night'
document.body.classList.remove('paper');     // Legacy (Obsidian) = no class
```

## Density Contract

Density controls spacing tightness via `data-density` on the document root:

| Attribute value              | Description                            |
| ---------------------------- | -------------------------------------- |
| _(absent / unset)_           | **Comfortable** — the default          |
| `data-density="comfortable"` | Same as default (explicit for JS parity)|
| `data-density="compact"`     | Tighter spacing for power users         |

Compact density reduces `--td-space-1` through `--td-space-4`. Components that use these tokens automatically tighten.

### Applying density

```js
document.documentElement.setAttribute('data-density', 'compact');
```

## Reduced Motion Contract

The system respects the operating system's reduced-motion preference:

```css
@media (prefers-reduced-motion: reduce) {
  :root {
    --td-transition-fast: 0ms linear;
    --td-transition-normal: 0ms linear;
    --td-transition-smooth: 0ms linear;
  }
}
```

Components using `var(--td-transition-*)` will automatically stop animating when the user has enabled reduced motion at the OS level. No per-component opt-in is needed.

## Quick Reference: Adding a New Token

1. Add the token to `:root` in `design-tokens.css`.
2. If it must differ under Paper, add a `.paper` / `.paper-night`-scoped override in `paper-legacy-bridge.css` (ADR-0053); the `:root` value is the Legacy default.
3. If it varies by density, add overrides in `[data-density="compact"]`.
4. If it is a transition/motion token, add a `0ms` override in the `@media (prefers-reduced-motion: reduce)` block.
5. Use a descriptive name following the `--td-{category}-{variant}` pattern.
6. Document the token's purpose with a CSS comment.
