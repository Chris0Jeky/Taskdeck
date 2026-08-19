/** @type {import('tailwindcss').Config} */
export default {
  darkMode: 'class',
  content: [
    './index.html',
    './src/**/*.{vue,js,ts,jsx,tsx}',
  ],
  theme: {
    extend: {
      colors: {
        /*
         * Obsidian & Ember — Material 3 tonal palette.
         *
         * ADR-0053 (#1778) — every semantic color is emitted as
         * `var(--td-tw-<name>, <obsidian-hex>)`.  Nothing defines `--td-tw-*`
         * at `:root`, so Legacy ("Paper off") resolves the fallback and the
         * computed color is byte-identical to the pre-bridge palette.  The
         * Paper shell classes (`.paper` / `.paper-night`, set on <body> by
         * paperThemeStore) define `--td-tw-*` in `src/paper-legacy-bridge.css`,
         * which lightens every not-yet-migrated legacy view to Paper-warm
         * values without touching the Obsidian look in Legacy mode.
         *
         * The hex below stays the single source of the Obsidian palette —
         * do NOT move it into the bridge, and do NOT add a `--td-tw-*`
         * definition at `:root`.
         */
        'primary': 'var(--td-tw-primary, #ffb3ae)',
        'primary-container': 'var(--td-tw-primary-container, #ff5352)',
        'on-primary': 'var(--td-tw-on-primary, #68000b)',
        'on-primary-container': 'var(--td-tw-on-primary-container, #5c0008)',
        'on-primary-fixed': 'var(--td-tw-on-primary-fixed, #410004)',
        'on-primary-fixed-variant': 'var(--td-tw-on-primary-fixed-variant, #930014)',
        'primary-fixed': 'var(--td-tw-primary-fixed, #ffdad7)',
        'primary-fixed-dim': 'var(--td-tw-primary-fixed-dim, #ffb3ae)',
        'inverse-primary': 'var(--td-tw-inverse-primary, #ba1724)',

        'secondary': 'var(--td-tw-secondary, #c6c6cf)',
        'secondary-container': 'var(--td-tw-secondary-container, #45464e)',
        'on-secondary': 'var(--td-tw-on-secondary, #2f3037)',
        'on-secondary-container': 'var(--td-tw-on-secondary-container, #b4b4bd)',
        'on-secondary-fixed': 'var(--td-tw-on-secondary-fixed, #1a1b22)',
        'on-secondary-fixed-variant': 'var(--td-tw-on-secondary-fixed-variant, #45464e)',
        'secondary-fixed': 'var(--td-tw-secondary-fixed, #e2e1eb)',
        'secondary-fixed-dim': 'var(--td-tw-secondary-fixed-dim, #c6c6cf)',

        'tertiary': 'var(--td-tw-tertiary, #c6c6c9)',
        'tertiary-container': 'var(--td-tw-tertiary-container, #909193)',
        'on-tertiary': 'var(--td-tw-on-tertiary, #2f3033)',
        'on-tertiary-container': 'var(--td-tw-on-tertiary-container, #282a2c)',
        'on-tertiary-fixed': 'var(--td-tw-on-tertiary-fixed, #1a1c1e)',
        'on-tertiary-fixed-variant': 'var(--td-tw-on-tertiary-fixed-variant, #454749)',
        'tertiary-fixed': 'var(--td-tw-tertiary-fixed, #e2e2e5)',
        'tertiary-fixed-dim': 'var(--td-tw-tertiary-fixed-dim, #c6c6c9)',

        'surface': 'var(--td-tw-surface, #131313)',
        'surface-dim': 'var(--td-tw-surface-dim, #131313)',
        'surface-bright': 'var(--td-tw-surface-bright, #3a3939)',
        'surface-container-lowest': 'var(--td-tw-surface-container-lowest, #0e0e0e)',
        'surface-container-low': 'var(--td-tw-surface-container-low, #1c1b1b)',
        'surface-container': 'var(--td-tw-surface-container, #201f1f)',
        'surface-container-high': 'var(--td-tw-surface-container-high, #2a2a2a)',
        'surface-container-highest': 'var(--td-tw-surface-container-highest, #353534)',
        'surface-variant': 'var(--td-tw-surface-variant, #353534)',
        'surface-tint': 'var(--td-tw-surface-tint, #ffb3ae)',

        'on-surface': 'var(--td-tw-on-surface, #e5e2e1)',
        'on-surface-variant': 'var(--td-tw-on-surface-variant, #e4beba)',
        'on-background': 'var(--td-tw-on-background, #e5e2e1)',
        'background': 'var(--td-tw-background, #131313)',
        'inverse-surface': 'var(--td-tw-inverse-surface, #e5e2e1)',
        'inverse-on-surface': 'var(--td-tw-inverse-on-surface, #313030)',

        'outline': 'var(--td-tw-outline, #ab8986)',
        'outline-variant': 'var(--td-tw-outline-variant, #5b403e)',

        'error': 'var(--td-tw-error, #ffb4ab)',
        'error-container': 'var(--td-tw-error-container, #93000a)',
        'on-error': 'var(--td-tw-on-error, #690005)',
        'on-error-container': 'var(--td-tw-on-error-container, #ffdad6)',

        /* Semantic shortcuts */
        'ember': 'var(--td-tw-ember, #ff4d4d)',
        'ember-glow': 'var(--td-tw-ember-glow, #ff5352)',
        'obsidian': 'var(--td-tw-obsidian, #131313)',
        'argent': 'var(--td-tw-argent, #c7c6c4)',
      },
      fontFamily: {
        headline: ['Manrope', 'system-ui', 'sans-serif'],
        body: ['Manrope', 'system-ui', 'sans-serif'],
        label: ['Space Grotesk', 'system-ui', 'sans-serif'],
      },
      borderRadius: {
        DEFAULT: '0.125rem',
        lg: '0.25rem',
        xl: '0.5rem',
        /* Keep full at 9999px so rounded-full stays circular for avatars/spinners */
      },
      boxShadow: {
        'obsidian': '0 20px 40px rgba(0, 0, 0, 0.4), 0 0 1px rgba(199, 198, 196, 0.1)',
        'obsidian-sm': '0 4px 12px rgba(0, 0, 0, 0.3)',
        'ember-glow': '0 20px 40px rgba(255, 77, 77, 0.3)',
        'ember-pulse': '0 0 0 0 rgba(255, 77, 77, 0.4)',
      },
      animation: {
        'ember-pulse': 'ember-pulse 2s infinite',
      },
      keyframes: {
        'ember-pulse': {
          '0%': { boxShadow: '0 0 0 0 rgba(255, 77, 77, 0.4)' },
          '70%': { boxShadow: '0 0 0 10px rgba(255, 77, 77, 0)' },
          '100%': { boxShadow: '0 0 0 0 rgba(255, 77, 77, 0)' },
        },
      },
      transitionTimingFunction: {
        kinetic: 'cubic-bezier(0.2, 0.8, 0.2, 1)',
      },
    },
  },
  plugins: [],
}
