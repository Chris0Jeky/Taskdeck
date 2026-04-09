import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { VitePWA } from 'vite-plugin-pwa'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    VitePWA({
      // 'prompt' prevents the new SW from auto-activating; SwUpdatePrompt.vue
      // shows a banner asking the user to reload when a new version is waiting.
      registerType: 'prompt',
      injectRegister: 'auto',
      // Disable SW in dev mode to avoid interfering with HMR / hot reload.
      devOptions: {
        enabled: false,
      },
      workbox: {
        // Precache app shell assets (JS, CSS, HTML, icons)
        globPatterns: ['**/*.{js,css,html,ico,png,svg,webp,woff,woff2}'],
        // Exclude manifest icons from glob — they are precached via the manifest config
        // to prevent duplicate precache entries and Workbox runtime warnings.
        globIgnores: ['icons/**'],
        // Clean stale caches on SW activation
        cleanupOutdatedCaches: true,
        // SPA fallback: serve index.html for navigation requests to unmatched
        // routes (e.g. deep links like /workspace/boards/xyz when offline).
        navigateFallback: 'index.html',
        navigateFallbackDenylist: [/^\/api\//, /^\/mcp/],
        // NetworkFirst for API calls — 1-day TTL ensures extended offline sessions
        // retain cached responses. Fresh data is always preferred when online.
        runtimeCaching: [
          {
            urlPattern: /^https?:\/\/.*\/api\//i,
            handler: 'NetworkFirst',
            options: {
              cacheName: 'taskdeck-api-cache',
              expiration: {
                maxAgeSeconds: 24 * 60 * 60, // 1 day
                maxEntries: 100,
              },
              networkTimeoutSeconds: 10,
              cacheableResponse: {
                statuses: [0, 200],
              },
            },
          },
          {
            // StaleWhileRevalidate for Google Fonts CSS — without this, offline users
            // fall back to system fonts because the stylesheet is not served from cache.
            urlPattern: /^https:\/\/fonts\.googleapis\.com\//i,
            handler: 'StaleWhileRevalidate',
            options: {
              cacheName: 'google-fonts-stylesheets',
              expiration: {
                maxAgeSeconds: 60 * 60 * 24 * 365, // 1 year
                maxEntries: 10,
              },
              cacheableResponse: {
                statuses: [0, 200],
              },
            },
          },
          {
            // CacheFirst for static assets (fonts, images, icons)
            urlPattern: /\.(?:png|jpg|jpeg|svg|gif|webp|ico|woff|woff2)$/i,
            handler: 'CacheFirst',
            options: {
              cacheName: 'taskdeck-static-assets',
              expiration: {
                maxAgeSeconds: 30 * 24 * 60 * 60, // 30 days
                maxEntries: 50,
              },
              cacheableResponse: {
                statuses: [0, 200],
              },
            },
          },
        ],
      },
      manifest: {
        name: 'Taskdeck',
        short_name: 'Taskdeck',
        description: 'Local-first execution workspace for developers. Near-zero-friction capture with review-first automation.',
        theme_color: '#131313',
        background_color: '#131313',
        display: 'standalone',
        orientation: 'any',
        scope: '/',
        start_url: '/workspace/home',
        lang: 'en',
        categories: ['productivity', 'utilities'],
        icons: [
          {
            src: 'icons/icon-192.svg',
            sizes: '192x192',
            type: 'image/svg+xml',
            purpose: 'any',
          },
          {
            src: 'icons/icon-192x192.png',
            sizes: '192x192',
            type: 'image/png',
            purpose: 'any',
          },
          {
            src: 'icons/icon-512.svg',
            sizes: '512x512',
            type: 'image/svg+xml',
            purpose: 'any',
          },
          {
            src: 'icons/icon-512x512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'any',
          },
          {
            src: 'icons/icon-maskable-512.svg',
            sizes: '512x512',
            type: 'image/svg+xml',
            purpose: 'maskable',
          },
        ],
      },
    }),
  ],
})
