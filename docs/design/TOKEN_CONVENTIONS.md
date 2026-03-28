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

The design system ships two themes controlled by `data-theme` on the document root:

| Attribute value           | Description                       |
| ------------------------- | --------------------------------- |
| _(absent / unset)_        | **Dark (Obsidian)** — the default |
| `data-theme="light"`      | **Light** — warm gray/cream       |

### How themes work

- `:root` defines the full dark token set.
- `[data-theme="light"]` overrides only the tokens that change (surfaces, text, borders, shadows, semantic colors, glass).
- Typography, spacing, radius, shell layout, and motion tokens are theme-independent.

### Applying a theme

```js
document.documentElement.setAttribute('data-theme', 'light');
// or remove to revert to dark:
document.documentElement.removeAttribute('data-theme');
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
2. If it varies by theme, add the light override in `[data-theme="light"]`.
3. If it varies by density, add overrides in `[data-density="compact"]`.
4. Use a descriptive name following the `--td-{category}-{variant}` pattern.
5. Document the token's purpose with a CSS comment.
