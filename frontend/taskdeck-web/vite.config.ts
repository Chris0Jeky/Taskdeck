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
        // Machine-facing prefixes never get the app shell. An installed PWA answers a
        // NAVIGATION from its own cache before the request leaves the browser, so without
        // these an offline (or merely slow-to-revalidate) client would see index.html for a
        // path the backend owns — the browser-side twin of the SPA-fallback defect fixed in
        // #1971. Workbox tests these against `pathname + search`, hence the `[/?]` branch.
        //
        // Kept in the same `^/<prefix>(?:/|$)` shape the reverse proxy uses for the identical
        // four prefixes (deploy/nginx/reverse-proxy.conf, checked by
        // scripts/deploy/Test-TaskdeckReverseProxyConfig.ps1), so the two layers agree on where
        // the machine surface starts and ends: the bare prefix and its descendants are machine
        // paths, `/apidocs` and `/mcpx` are not. The `%2[fF]` branch keeps the layers agreeing on
        // a percent-encoded descendant such as `/mcp%2Fmessages`: Workbox tests the still-encoded
        // pathname while nginx location-matches the decoded URI (`/mcp/messages` — machine
        // surface), so without it the service worker would hand the app shell to a path the proxy
        // routes to the API. A double-encoded `%252F` stays SPA-side in both layers (nginx decodes
        // once, leaving literal `%2F` text).
        //
        // The `i` flag is the fail-closed half (#1992 q-10 A, ADR-0064). A machine prefix is the
        // exact lowercase literal, so `/API/boards` is not a machine path — but it is not a
        // client-side route either: nginx and the API both answer it 404. Denying it here is what
        // lets that 404 reach the user; without the flag the service worker would answer a
        // navigation to `/API/boards` from the precache and show the app shell for a URL that does
        // not exist at any layer. Denylisting is not the same as claiming the path is machine
        // surface — it only means "this is not ours to answer from cache".
        //
        // Behaviour is pinned by src/tests/config/PwaMachinePathDenylist.spec.ts (#1992).
        navigateFallbackDenylist: [
          /^\/api(?:[/?]|%2[fF]|$)/i,
          /^\/health(?:[/?]|%2[fF]|$)/i,
          /^\/hubs(?:[/?]|%2[fF]|$)/i,
          /^\/mcp(?:[/?]|%2[fF]|$)/i,
        ],
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
