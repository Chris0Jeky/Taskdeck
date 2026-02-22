import { configDefaults, defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath } from 'node:url'

export default defineConfig({
  plugins: [vue()],
  test: {
    globals: true,
    environment: 'happy-dom',
    setupFiles: './src/tests/setup.ts',
    exclude: [...configDefaults.exclude, 'tests/e2e/**'],
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,vue}'],
      reporter: ['text', 'json', 'json-summary', 'html'],
      // Ratchet policy: thresholds may stay the same or increase, but never decrease.
      thresholds: {
        lines: 45,
        statements: 45,
        functions: 65,
        branches: 75,
        autoUpdate: false,
        'src/api/**': {
          lines: 60,
          statements: 60,
          functions: 70,
          branches: 85,
        },
        'src/store/**': {
          lines: 63,
          statements: 63,
          functions: 75,
          branches: 80,
        },
        'src/composables/**': {
          lines: 68,
          statements: 68,
          functions: 90,
          branches: 82,
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
    },
  },
})
