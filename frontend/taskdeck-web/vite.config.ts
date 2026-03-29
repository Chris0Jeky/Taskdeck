import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { VitePWA } from 'vite-plugin-pwa'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    VitePWA({
      registerType: 'autoUpdate',
      injectRegister: 'auto',
      workbox: {
        // Precache app shell assets (JS, CSS, HTML, icons)
        globPatterns: ['**/*.{js,css,html,ico,png,svg,webp,woff,woff2}'],
        // Exclude manifest icons from glob — they are precached via the manifest config
        // to prevent duplicate precache entries and Workbox runtime warnings.
        globIgnores: ['icons/**'],
        // Clean stale caches on SW activation
        cleanupOutdatedCaches: true,
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
        description: 'Local-first execution workspace for developers',
        theme_color: '#1e293b',
        background_color: '#0f172a',
        display: 'standalone',
        scope: '/',
        start_url: '/',
        icons: [
          {
            src: 'icons/icon-192x192.png',
            sizes: '192x192',
            type: 'image/png',
            purpose: 'any maskable',
          },
          {
            src: 'icons/icon-512x512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'any maskable',
          },
        ],
      },
    }),
  ],
})
