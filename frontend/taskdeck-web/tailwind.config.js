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
        /* Obsidian & Ember — Material 3 tonal palette */
        'primary': '#ffb3ae',
        'primary-container': '#ff5352',
        'on-primary': '#68000b',
        'on-primary-container': '#5c0008',
        'on-primary-fixed': '#410004',
        'on-primary-fixed-variant': '#930014',
        'primary-fixed': '#ffdad7',
        'primary-fixed-dim': '#ffb3ae',
        'inverse-primary': '#ba1724',

        'secondary': '#c6c6cf',
        'secondary-container': '#45464e',
        'on-secondary': '#2f3037',
        'on-secondary-container': '#b4b4bd',
        'on-secondary-fixed': '#1a1b22',
        'on-secondary-fixed-variant': '#45464e',
        'secondary-fixed': '#e2e1eb',
        'secondary-fixed-dim': '#c6c6cf',

        'tertiary': '#c6c6c9',
        'tertiary-container': '#909193',
        'on-tertiary': '#2f3033',
        'on-tertiary-container': '#282a2c',
        'on-tertiary-fixed': '#1a1c1e',
        'on-tertiary-fixed-variant': '#454749',
        'tertiary-fixed': '#e2e2e5',
        'tertiary-fixed-dim': '#c6c6c9',

        'surface': '#131313',
        'surface-dim': '#131313',
        'surface-bright': '#3a3939',
        'surface-container-lowest': '#0e0e0e',
        'surface-container-low': '#1c1b1b',
        'surface-container': '#201f1f',
        'surface-container-high': '#2a2a2a',
        'surface-container-highest': '#353534',
        'surface-variant': '#353534',
        'surface-tint': '#ffb3ae',

        'on-surface': '#e5e2e1',
        'on-surface-variant': '#e4beba',
        'on-background': '#e5e2e1',
        'background': '#131313',
        'inverse-surface': '#e5e2e1',
        'inverse-on-surface': '#313030',

        'outline': '#ab8986',
        'outline-variant': '#5b403e',

        'error': '#ffb4ab',
        'error-container': '#93000a',
        'on-error': '#690005',
        'on-error-container': '#ffdad6',

        /* Semantic shortcuts */
        'ember': '#ff4d4d',
        'ember-glow': '#ff5352',
        'obsidian': '#131313',
        'argent': '#c7c6c4',
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
