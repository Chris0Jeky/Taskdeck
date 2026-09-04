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
