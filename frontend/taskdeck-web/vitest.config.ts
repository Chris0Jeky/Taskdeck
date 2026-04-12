import { configDefaults, defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath } from 'node:url'

export default defineConfig({
  plugins: [vue()],
  test: {
    globals: true,
    environment: 'happy-dom',
    setupFiles: './src/tests/setup.ts',
    exclude: [...configDefaults.exclude, 'tests/e2e/**', 'tests/visual/**'],
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,vue}'],
      reporter: ['text', 'json', 'json-summary', 'html'],
      // Ratchet policy: thresholds may stay the same or increase, but never decrease.
      // Note: vitest v4 measures branch coverage more comprehensively than v2
      // (tracks optional chaining, nullish coalescing, ternaries more granularly).
      // Thresholds recalibrated to v4 baseline; ratchet applies going forward.
      thresholds: {
        lines: 45,
        statements: 45,
        functions: 65,
        branches: 71,
        autoUpdate: false,
        'src/api/**': {
          lines: 60,
          statements: 60,
          functions: 70,
          branches: 49,
        },
        'src/store/**': {
          lines: 63,
          statements: 63,
          functions: 75,
          branches: 75,
        },
        'src/composables/**': {
          lines: 68,
          statements: 68,
          functions: 90,
          branches: 80,
        },
        'src/utils/**': {
          lines: 80,
          statements: 80,
          functions: 90,
          branches: 75,
        },
        'src/components/board/**': {
          lines: 78,
          statements: 78,
          functions: 70,
          branches: 78,
        },
      },
      exclude: [
        'node_modules/',
        'src/tests/',
        '**/*.spec.ts',
        '**/*.test.ts',
        '**/types/**',
        'dist/**',
      ],
    },
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      // vite-plugin-pwa provides 'virtual:pwa-register' as a Vite virtual
      // module at build time; vitest does not load Vite plugins, so we alias
      // it to a no-op mock to keep component tests that import SwUpdatePrompt
      // (e.g. AppShell.spec.ts) from failing on the unresolvable import.
      'virtual:pwa-register': fileURLToPath(
        new URL('./src/tests/__mocks__/virtual-pwa-register.ts', import.meta.url),
      ),
    },
  },
})
