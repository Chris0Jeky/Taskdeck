import { expect, test } from '@playwright/test'
import { registerUserSession } from './support/authSession'

/*
 * Browser-level regression for the activation-time runtime-cache retirement. Hardens the two assertions that
 * tests/e2e/pwa-api-cache.spec.ts leaves loose, so the browser proof cannot pass
 * vacuously:
 *   - the post-migration controller must be NON-NULL and answer the v2 marker
 *   - taskdeck-static-assets must be emptied by the activation
 *   - taskdeck-share-target must survive it
 */
test.skip(
  process.env.TASKDECK_E2E_PWA_PREVIEW !== '1',
  'This regression requires the generated service worker from a production preview.',
)

test('old #2350 worker -> v2 controller, static cache invalidated, share queue preserved', async ({ browser, request }) => {
  const account = await registerUserSession(request, 'pwa-proof-strict')
  const context = await browser.newContext({ serviceWorkers: 'allow' })
  const page = await context.newPage()

  let serveLegacyWorker = true
  await context.route('**/sw.js', async (route) => {
    if (!serveLegacyWorker) {
      await route.fallback()
      return
    }
    serveLegacyWorker = false
    await route.fulfill({
      contentType: 'text/javascript',
      body: [
        "self.addEventListener('message', (event) => { if (event.data?.type === 'taskdeck:api-cache-policy' && event.ports?.[0]) event.ports[0].postMessage({ policy: 'legacy-api-cache-retired' }) })",
        "self.addEventListener('install', () => self.skipWaiting())",
        "self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()))",
        "self.addEventListener('fetch', () => {})",
      ].join('\n'),
    })
  })

  const askPolicy = () => page.evaluate(() => new Promise<string | null>((resolve) => {
    const controller = navigator.serviceWorker.controller
    if (!controller) { resolve(null); return }
    const channel = new MessageChannel()
    const timer = setTimeout(() => resolve('__timeout__'), 2_000)
    channel.port1.onmessage = (event) => {
      clearTimeout(timer)
      resolve((event.data as { policy?: string }).policy ?? '__nopolicy__')
    }
    controller.postMessage({ type: 'taskdeck:api-cache-policy' }, [channel.port2])
  }))

  try {
    await page.goto('/login')
    await page.evaluate(async () => {
      await navigator.serviceWorker.register('/sw.js')
      await navigator.serviceWorker.ready
    })
    await expect.poll(() => page.evaluate(() => navigator.serviceWorker.controller !== null)).toBe(true)

    // BEFORE: the installed #2350-era worker is in control.
    const markerBefore = await askPolicy()
    console.log('PROOF markerBefore =', markerBefore)
    expect(markerBefore).toBe('legacy-api-cache-retired')

    // Seed both runtime caches under the OLD worker.
    await page.evaluate(async () => {
      const legacyApi = await caches.open('taskdeck-api-cache-v2')
      await legacyApi.put('/api/boards', new Response('legacy-api-bytes'))
      const staticCache = await caches.open('taskdeck-static-assets')
      await staticCache.put('/seeded-static-entry.png', new Response('stale-static-bytes'))
      const shareCache = await caches.open('taskdeck-share-target')
      await shareCache.put('/seeded-share-entry', new Response('queued-share-payload'))
    })
    const cachesBefore = await page.evaluate(async () => ({
      keys: await caches.keys(),
      staticEntries: (await (await caches.open('taskdeck-static-assets')).keys()).map((r) => r.url),
      shareEntries: (await (await caches.open('taskdeck-share-target')).keys()).map((r) => r.url),
    }))
    console.log('PROOF cachesBefore =', JSON.stringify(cachesBefore, null, 2))
    expect(cachesBefore.staticEntries.some((u) => u.includes('seeded-static-entry.png'))).toBe(true)
    expect(cachesBefore.shareEntries.some((u) => u.includes('seeded-share-entry'))).toBe(true)

    // Latch a snapshot at the exact moment control changes, so a delete-then-recreate
    // is distinguishable from a delete that never happened.
    await page.evaluate(() => {
      (window as any).__proofSnaps = []
      const snap = async (label: string) => {
        (window as any).__proofSnaps.push({
          label,
          hasStatic: await caches.has('taskdeck-static-assets'),
          staticEntries: (await (await caches.open('taskdeck-static-assets')).keys()).map((r) => r.url),
        })
      }
      navigator.serviceWorker.addEventListener('controllerchange', () => { void snap('controllerchange') })
      const iv = setInterval(() => { void snap('poll') }, 150)
      setTimeout(() => clearInterval(iv), 25_000)
    })

    // Drive the real migration: sign in, which runs retireLegacyApiCacheWorker().
    await page.getByLabel('Username or Email').fill(account.user.username)
    await page.getByLabel('Password').fill('E2ePassword123!')
    await page.getByRole('button', { name: 'Sign in' }).click()
    await page.waitForURL('**/workspace/home')

    // AFTER: a controller must exist AND it must be the v2 worker.
    await expect.poll(askPolicy, { timeout: 20_000 }).toBe('taskdeck-api-cache-policy-v2')
    const controllerAfter = await page.evaluate(() => ({
      present: navigator.serviceWorker.controller !== null,
      scriptURL: navigator.serviceWorker.controller?.scriptURL ?? null,
    }))
    console.log('PROOF controllerAfter =', JSON.stringify(controllerAfter))
    expect(controllerAfter.present).toBe(true)

    const cachesAfter = await page.evaluate(async () => ({
      keys: await caches.keys(),
      staticEntries: (await (await caches.open('taskdeck-static-assets')).keys()).map((r) => r.url),
      shareEntries: (await (await caches.open('taskdeck-share-target')).keys()).map((r) => r.url),
    }))
    console.log('PROOF cachesAfter =', JSON.stringify(cachesAfter, null, 2))
    console.log('PROOF legacyApiCacheStillPresent =', JSON.stringify(await page.evaluate(async () => (await caches.keys()).filter((n) => n.startsWith('taskdeck-api-cache')))))

    console.log('PROOF snaps =', JSON.stringify(await page.evaluate(() => {
      const snaps = (window as any).__proofSnaps as Array<{label:string;hasStatic:boolean;staticEntries:string[]}>
      // collapse consecutive identical states so the transition is readable
      const out: typeof snaps = []
      for (const s of snaps) {
        const prev = out[out.length - 1]
        if (!prev || prev.hasStatic !== s.hasStatic || prev.staticEntries.length !== s.staticEntries.length || prev.label !== s.label) out.push(s)
      }
      return out
    }), null, 2))

    expect(cachesAfter.staticEntries.some((u) => u.includes('seeded-static-entry.png'))).toBe(false)
    expect(cachesAfter.shareEntries.some((u) => u.includes('seeded-share-entry'))).toBe(true)
    expect(cachesAfter.keys.some((n) => n.startsWith('taskdeck-api-cache'))).toBe(false)

    await page.screenshot({ path: process.env.TASKDECK_PROOF_SHOT ?? 'test-results/pwa-proof.png', fullPage: false })
  } finally {
    await context.close()
  }
})

/*
 * #2475 - the install-to-activation re-poisoning window, at browser level.
 *
 * public/api-cache-cleanup.js sweeps the runtime caches twice: once at script evaluation
 * (which happens while the replacement is INSTALLING, so the old vulnerable worker still
 * controls every open page) and once inside `event.waitUntil` on activate with
 * `force: true`. Only the second pass can remove something the old worker cached after the
 * first one finished, which is exactly the window this case pins.
 *
 * The window is held open by the browser rather than raced. `registerType: 'prompt'` parks a
 * replacement that has finished installing in `waiting` until a page sends
 * `taskdeck:skip-waiting`, and the real sign-in migration is what sends it. So the seed lands
 * strictly after the install-time sweep (proven by the marker cache that sweep writes last)
 * and strictly before activation (proven by `registration.waiting` and by the old worker
 * still answering the pre-#2350 policy handshake).
 *
 * The seed is written by the OLD worker's own CacheFirst handler, from a real network
 * response, not by page script reaching into CacheStorage.
 */
test('a static entry the old worker caches between install and activation does not survive the migration', async ({ browser, request }) => {
  // EXPECTED FAILURE (#2475). This case is red against the current production build and is
  // marked so the pwa-proof lane keeps a meaningful green/red verdict for the strict case
  // above. The final assertion fails because the forced activate-time re-sweep described in
  // the header never runs on the generated worker: public/api-cache-cleanup.js (see the
  // comment at its end, landed with #2416) records that its `activate` listener is attached
  // from inside vite-plugin-pwa's asynchronous AMD factory, after the activate event has
  // already been dispatched, so only the memoised evaluation-time sweep ever fires and it has
  // already resolved by the time this case seeds. The seed therefore survives the migration.
  // When the production repair lands (an activation hook that registers synchronously, or the
  // injectManifest move), this case starts passing and Playwright reports "expected to fail
  // but passed": remove this modifier then. Nothing in CI runs this file, so the marker only
  // governs the manually driven lane.
  test.fail()

  const account = await registerUserSession(request, 'pwa-proof-race')
  const context = await browser.newContext({ serviceWorkers: 'allow' })
  const page = await context.newPage()

  // A real precached asset plus a marker query: the response is a genuine 200 the old
  // CacheFirst rule admits, and the query keeps the seeded entry distinguishable from the
  // icons the app itself fetches.
  const seedPath = '/icons/icon-192x192.png?td-race-seed=1'

  let serveLegacyWorker = true
  await context.route('**/sw.js', async (route) => {
    if (!serveLegacyWorker) {
      await route.fallback()
      return
    }
    serveLegacyWorker = false
    await route.fulfill({
      contentType: 'text/javascript',
      body: [
        "self.addEventListener('message', (event) => { if (event.data?.type === 'taskdeck:api-cache-policy' && event.ports?.[0]) event.ports[0].postMessage({ policy: 'legacy-api-cache-retired' }) })",
        "self.addEventListener('install', () => self.skipWaiting())",
        "self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()))",
        // The pre-#2350 static-asset route: matched on file extension alone, CacheFirst into
        // the shared taskdeck-static-assets runtime cache. This is the handler whose writes
        // the migration has to invalidate.
        "self.addEventListener('fetch', (event) => {",
        "  if (event.request.method !== 'GET') return",
        "  if (!/\\.(png|svg|webp|ico|woff2?)$/.test(new URL(event.request.url).pathname)) return",
        '  event.respondWith((async () => {',
        "    const cache = await caches.open('taskdeck-static-assets')",
        '    const hit = await cache.match(event.request)',
        '    if (hit) return hit',
        '    const response = await fetch(event.request)',
        '    if (response.status === 200) await cache.put(event.request, response.clone())',
        '    return response',
        '  })())',
        '})',
      ].join('\n'),
    })
  })

  const askPolicy = () => page.evaluate(() => new Promise<string | null>((resolve) => {
    const controller = navigator.serviceWorker.controller
    if (!controller) { resolve(null); return }
    const channel = new MessageChannel()
    const timer = setTimeout(() => resolve('__timeout__'), 2_000)
    channel.port1.onmessage = (event) => {
      clearTimeout(timer)
      resolve((event.data as { policy?: string }).policy ?? '__nopolicy__')
    }
    controller.postMessage({ type: 'taskdeck:api-cache-policy' }, [channel.port2])
  }))

  const staticEntries = () => page.evaluate(async () =>
    (await (await caches.open('taskdeck-static-assets')).keys()).map((r) => r.url))

  try {
    await page.goto('/login')
    await page.evaluate(async () => {
      await navigator.serviceWorker.register('/sw.js')
      await navigator.serviceWorker.ready
    })
    await expect.poll(() => page.evaluate(() => navigator.serviceWorker.controller !== null)).toBe(true)
    expect(await askPolicy()).toBe('legacy-api-cache-retired')

    // Bring the replacement all the way to `waiting`. It cannot activate from there without
    // the page's skip-waiting message, so the rest of this case runs inside a window the
    // browser holds open instead of one measured against a clock.
    const replacementState = await page.evaluate(() => new Promise<string>((resolve) => {
      void (async () => {
        const registration = await navigator.serviceWorker.getRegistration()
        if (!registration) { resolve('__noregistration__'); return }
        let settled = false
        const settle = (value: string) => {
          if (settled) return
          settled = true
          clearTimeout(timer)
          resolve(value)
        }
        const timer = setTimeout(() => settle('__timeout__'), 30_000)
        const follow = (worker: ServiceWorker | null) => {
          if (!worker) return
          if (worker.state !== 'installing') { settle(worker.state); return }
          worker.addEventListener('statechange', () => {
            if (worker.state !== 'installing') settle(worker.state)
          })
        }
        registration.addEventListener('updatefound', () => follow(registration.installing))
        if (registration.waiting) { settle('installed'); return }
        follow(registration.installing)
        try {
          await registration.update()
        } catch {
          settle('__updatefailed__')
        }
      })()
    }))
    console.log('PROOF raceReplacementState =', replacementState)
    // 'installed' is the waiting state: installed, not activated, activation withheld.
    expect(replacementState).toBe('installed')

    // The install-time sweep writes the marker cache last, so its presence proves that sweep
    // completed. Anything seeded from here on is strictly inside the window under test.
    await expect.poll(
      () => page.evaluate(() => caches.has('taskdeck-pwa-cache-policy-v2')),
      { timeout: 15_000 },
    ).toBe(true)

    const windowOpen = await page.evaluate(async () => {
      const registration = await navigator.serviceWorker.getRegistration()
      return {
        waiting: Boolean(registration?.waiting),
        activeState: registration?.active?.state ?? null,
        controllerPresent: navigator.serviceWorker.controller !== null,
      }
    })
    console.log('PROOF raceWindowOpen =', JSON.stringify(windowOpen))
    expect(windowOpen.waiting).toBe(true)
    expect(windowOpen.controllerPresent).toBe(true)
    // Still the pre-#2350 worker: the replacement has not taken over yet.
    expect(await askPolicy()).toBe('legacy-api-cache-retired')

    // Seed through the OLD worker's CacheFirst handler. Its `cache.put` is awaited before the
    // response is returned, so this fetch resolving means the entry is already stored.
    const seedStatus = await page.evaluate(async (path) => (await fetch(path)).status, seedPath)
    expect(seedStatus).toBe(200)
    const seededEntries = await staticEntries()
    console.log('PROOF raceSeededEntries =', JSON.stringify(seededEntries))
    expect(seededEntries.some((u) => u.includes('td-race-seed'))).toBe(true)

    // The window is still open after the seed: the replacement is still waiting and the old
    // worker is still the controller, so the entry was written before activation.
    const windowStillOpen = await page.evaluate(async () =>
      Boolean((await navigator.serviceWorker.getRegistration())?.waiting))
    expect(windowStillOpen).toBe(true)
    expect(await askPolicy()).toBe('legacy-api-cache-retired')

    // Real migration: sign-in runs retireLegacyApiCacheWorker(), which sends skip-waiting to
    // the waiting worker and lets it activate.
    await page.getByLabel('Username or Email').fill(account.user.username)
    await page.getByLabel('Password').fill('E2ePassword123!')
    await page.getByRole('button', { name: 'Sign in' }).click()
    await page.waitForURL('**/workspace/home')
    await expect.poll(askPolicy, { timeout: 20_000 }).toBe('taskdeck-api-cache-policy-v2')

    const entriesAfter = await staticEntries()
    console.log('PROOF raceEntriesAfter =', JSON.stringify(entriesAfter))
    console.log('PROOF raceKeysAfter =', JSON.stringify(await page.evaluate(() => caches.keys())))
    // Only the forced re-sweep inside the activate handler can remove this: the memoised
    // evaluation-time sweep already resolved and its marker cache is already present.
    expect(entriesAfter.some((u) => u.includes('td-race-seed'))).toBe(false)
  } finally {
    await context.close()
  }
})
