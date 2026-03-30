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

  // Vue accessibility rules (WCAG compliance)
  ...pluginVueA11y.configs['flat/recommended'],
  {
    files: ['**/*.vue'],
    rules: {
      // Warn-level for rules that need gradual remediation across the codebase
      'vuejs-accessibility/click-events-have-key-events': 'warn',
      'vuejs-accessibility/interactive-supports-focus': 'warn',
      // Allow mouseenter without focus equivalent (visual enhancement only)
      'vuejs-accessibility/mouse-events-have-key-events': 'warn',
      // form-control-has-label is covered by our manual label audit
      'vuejs-accessibility/form-control-has-label': 'warn',
      // label-has-for requires explicit for/id binding — warn during rollout
      'vuejs-accessibility/label-has-for': 'warn',
      // Div/span click handlers are common in Vue component patterns — warn for gradual migration
      'vuejs-accessibility/no-static-element-interactions': 'warn',
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
