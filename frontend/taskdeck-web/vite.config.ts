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
      // SwUpdatePrompt.vue imports from 'virtual:pwa-register', which makes
      // vite-plugin-pwa skip the auto-injected registerSW.js script.  Keeping
      // 'auto' here means: use the virtual module when it's imported, otherwise
      // fall back to injecting a <script>.  Since we always import the virtual
      // module, no injected script is generated.
      injectRegister: 'auto',
      // Disable SW in dev mode to avoid interfering with HMR / hot reload.
      devOptions: {
        enabled: false,
      },
      workbox: {
        importScripts: ['share-target-handler.js'],
        // Precache app shell assets (JS, CSS, HTML, icons)
        globPatterns: ['**/*.{js,css,html,ico,png,svg,webp,woff,woff2}'],
        // Exclude manifest icons from glob — they are precached via the manifest config
        // to prevent duplicate precache entries and Workbox runtime warnings.
        // The it/es locale catalogs are code-split precisely so an English-only
        // user never downloads them (#1858); precaching would re-add that
        // background transfer, so they are excluded here and served via the
        // runtime rule below instead (cached on first real use).
        globIgnores: ['icons/**', 'assets/it-*.js', 'assets/es-*.js'],
        // Clean stale caches on SW activation
        cleanupOutdatedCaches: true,
        // SPA fallback: serve index.html for navigation requests to unmatched
        // routes (e.g. deep links like /workspace/boards/xyz when offline).
        navigateFallback: 'index.html',
        navigateFallbackDenylist: [/^\/api\//, /^\/health(?:[/?]|$)/, /^\/hubs(?:[/?]|$)/, /^\/mcp/],
        // NetworkFirst for API calls — 1-day TTL ensures extended offline sessions
        // retain cached responses. Fresh data is always preferred when online.
        runtimeCaching: [
          {
            // Lazy locale catalogs (excluded from precache above): cache on
            // first use so a user who picked it/es keeps their language offline.
            urlPattern: /\/assets\/(?:it|es)-[\w-]+\.js$/,
            handler: 'StaleWhileRevalidate',
            options: {
              cacheName: 'taskdeck-locale-chunks',
              expiration: { maxEntries: 8 },
              cacheableResponse: { statuses: [200] },
            },
          },
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
              // Only cache same-origin 200 responses — status 0 (opaque) would
              // cache empty bodies from cross-origin requests.
              cacheableResponse: {
                statuses: [200],
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
        theme_color: '#f3eee5',
        background_color: '#f3eee5',
        display: 'standalone',
        orientation: 'any',
        scope: '/',
        start_url: '/workspace/home',
        lang: 'en',
        categories: ['productivity', 'utilities'],
        share_target: {
          action: '/capture/share',
          method: 'POST',
          enctype: 'multipart/form-data',
          params: {
            title: 'title',
            text: 'text',
            url: 'url',
          },
        },
        icons: [
          {
            src: 'icons/icon-192.svg',
            sizes: 'any',
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
            sizes: 'any',
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
            sizes: 'any',
            type: 'image/svg+xml',
            purpose: 'maskable',
          },
        ],
      },
    }),
  ],
  build: {
    rollupOptions: {
      output: {
        manualChunks(id) {
          const normalizedId = id.replace(/\\/g, '/')
          if (!normalizedId.includes('/node_modules/')) {
            return undefined
          }

          if (
            normalizedId.includes('/node_modules/vue/') ||
            normalizedId.includes('/node_modules/@vue/') ||
            normalizedId.includes('/node_modules/pinia/') ||
            normalizedId.includes('/node_modules/vue-router/')
          ) {
            return 'vendor-vue'
          }

          if (
            normalizedId.includes('/node_modules/axios/') ||
            normalizedId.includes('/node_modules/@microsoft/signalr/')
          ) {
            return 'vendor-network'
          }

          if (
            normalizedId.includes('/node_modules/marked/') ||
            normalizedId.includes('/node_modules/dompurify/')
          ) {
            return 'vendor-markdown'
          }

          if (normalizedId.includes('/node_modules/@tanstack/vue-virtual/')) {
            return 'vendor-virtual'
          }

          return 'vendor'
        },
      },
    },
  },
})
