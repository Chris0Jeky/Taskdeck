import pluginVue from 'eslint-plugin-vue'
import pluginVueA11y from 'eslint-plugin-vuejs-accessibility'
import tsParser from '@typescript-eslint/parser'
import tsPlugin from '@typescript-eslint/eslint-plugin'
import eslint from '@eslint/js'
import vueParser from 'vue-eslint-parser'
import globals from 'globals'

export default [
  // Base recommended config
  eslint.configs.recommended,

  // TypeScript and Vue source files
  {
    files: ['**/*.ts', '**/*.vue'],
    plugins: { '@typescript-eslint': tsPlugin },
    languageOptions: {
      parser: vueParser,
      parserOptions: {
        parser: tsParser,
        sourceType: 'module',
        ecmaVersion: 'latest',
        extraFileExtensions: ['.vue'],
      },
      globals: {
        ...globals.browser,
        ...globals.node,
        ...globals.es2022,
      },
    },
    rules: {
      ...tsPlugin.configs.recommended.rules,
      // TypeScript handles undefined checking, no-undef causes false positives with TS namespaces
      'no-undef': 'off',
      'no-console': 'off',
      '@typescript-eslint/no-explicit-any': 'off',
      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_' },
      ],
    },
  },

  // JavaScript / MJS scripts
  {
    files: ['**/*.mjs', '**/*.js'],
    languageOptions: {
      globals: {
        ...globals.browser,
        ...globals.node,
        ...globals.es2022,
      },
    },
    rules: {
      'no-control-regex': 'off',
    },
  },

  // Vue-specific rules
  ...pluginVue.configs['flat/essential'],
  {
    files: ['**/*.vue'],
    rules: {
      'vue/multi-word-component-names': 'off',
      'vue/attributes-order': 'off',
      'vue/html-quotes': 'off',
      'vue/html-self-closing': 'off',
      'vue/max-attributes-per-line': 'off',
      'vue/singleline-html-element-content-newline': 'off',
    },
  },

  // Measured 2026-09-06: count nonblank, noncomment SFC lines and warn above 700.
  // These existing high-cohesion seams stay explicitly allowlisted while they are decomposed.
  {
    files: ['**/*.vue'],
    rules: {
      'max-lines': ['warn', { max: 700, skipBlankLines: true, skipComments: true }],
    },
  },
  {
    files: [
      'src/components/paper/PaperSidebar.vue', // paper navigation and triage shell
      'src/components/shell/ShellSidebar.vue', // shared workspace navigation composition
      'src/views/CalendarView.vue', // calendar workflow and filtering surface
      'src/views/HomeView.vue', // home workspace composition
      'src/views/MetricsView.vue', // metrics dashboard composition
      'src/views/paper/PaperBoardView.vue', // paper board orchestration
      'src/views/paper/PaperHomeView.vue', // Paper home route composition
      'src/views/paper/PaperReviewView.vue', // paper review workflow surface
      'src/views/paper/inbox/PaperTriageTable.vue', // triage table interactions and layout
    ],
    rules: {
      'max-lines': 'off',
    },
  },

  // Vue accessibility rules (WCAG compliance)
  ...pluginVueA11y.configs['flat/recommended'],
  {
    files: ['**/*.vue'],
    rules: {
      // GH-1949 dead-affordance guard, lint half. These three rules catch the
      // exact shapes the 2026-08-22 dogfooding audit found: a labelled control
      // that is not focusable, and a click handler with no keyboard equivalent.
      // Promoted warn -> error on 2026-09-04 after measuring 0 violations across
      // all 177 SFCs, so the codebase is clean at the moment of promotion and
      // the rule is enforcing rather than advisory. A genuinely delegated-handler
      // Vue idiom may opt out with an inline eslint-disable-next-line carrying a
      // one-line reason; none was needed at promotion time.
      'vuejs-accessibility/click-events-have-key-events': 'error',
      'vuejs-accessibility/interactive-supports-focus': 'error',
      // Allow mouseenter without focus equivalent (visual enhancement only)
      'vuejs-accessibility/mouse-events-have-key-events': 'warn',
      // form-control-has-label is covered by our manual label audit
      'vuejs-accessibility/form-control-has-label': 'warn',
      // label-has-for: accept either nesting or for/id association (HTML spec allows both)
      'vuejs-accessibility/label-has-for': ['warn', { required: { some: ['nesting', 'id'] } }],
      // Div/span click handlers: 0 violations measured 2026-09-04, so this is
      // enforced rather than a gradual-migration warning (GH-1949).
      'vuejs-accessibility/no-static-element-interactions': 'error',
      // Autofocus is intentional in modals and command palettes for UX
      'vuejs-accessibility/no-autofocus': 'warn',
      // Redundant roles (e.g. role="list" on <ul>) are harmless — warn only
      'vuejs-accessibility/no-redundant-roles': 'warn',
    },
  },

  // Test files (vitest)
  {
    files: ['**/*.spec.ts', '**/*.test.ts', 'src/tests/**/*.ts', 'tests/**/*.ts'],
    languageOptions: {
      globals: {
        ...globals.browser,
        ...globals.node,
        ...globals.es2022,
        ...globals.vitest,
      },
    },
    rules: {
      '@typescript-eslint/no-unused-expressions': 'off',
    },
  },

  // Ignores
  {
    ignores: [
      'dist/**',
      'coverage/**',
      'test-results/**',
      'playwright-report/**',
      'node_modules/**',
    ],
  },
]
