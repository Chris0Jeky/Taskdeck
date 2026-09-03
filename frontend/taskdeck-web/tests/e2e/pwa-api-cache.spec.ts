import { expect, test } from '@playwright/test'
import { API_BASE_URL, registerUserSession } from './support/authSession'

test.skip(
  process.env.TASKDECK_E2E_PWA_PREVIEW !== '1',
  'This regression requires the generated service worker from a production preview.',
)

test('a legacy response from user A is never replayed after switching to user B on a slow network @pwa-preview', async ({ browser, request }) => {
  const accountA = await registerUserSession(request, 'pwa-cache-a')
  const accountB = await registerUserSession(request, 'pwa-cache-b')
  const context = await browser.newContext({ serviceWorkers: 'allow' })
  const page = await context.newPage()
  const legacyApiUrl = `${API_BASE_URL}/boards`

  try {
    await page.goto('/login')
    await page.getByLabel('Username or Email').fill(accountA.user.username)
    await page.getByLabel('Password').fill('E2ePassword123!')
    await page.getByRole('button', { name: 'Sign in' }).click()
    await page.waitForURL('**/workspace/home')
    await page.evaluate(() => navigator.serviceWorker.ready)
    await page.reload()
    await page.waitForURL('**/workspace/home')
    await expect.poll(() => page.evaluate(() => navigator.serviceWorker.controller !== null)).toBe(true)

    // This simulates the namespace an older service worker populated under A.
    await page.evaluate(async (url) => {
      const cache = await caches.open('taskdeck-api-cache-v2')
      await cache.put(url, new Response(JSON.stringify({ owner: 'account-a' })))
    }, legacyApiUrl)

    await page.locator('[data-topbar-action="account"]').click()
    await page.getByRole('menuitem', { name: 'Sign out' }).click()
    await page.waitForURL('**/login')

    await page.getByLabel('Username or Email').fill(accountB.user.username)
    await page.getByLabel('Password').fill('E2ePassword123!')
    await page.getByRole('button', { name: 'Sign in' }).click()
    await page.waitForURL('**/workspace/home')
    await expect.poll(() => page.evaluate(async () =>
      (await caches.keys()).some((name) => name.startsWith('taskdeck-api-cache')),
    )).toBe(false)

    await page.route('**/api/boards', async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 11_000))
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ owner: 'account-b' }),
      })
    })
    const outcome = await page.evaluate(async (url) => {
      const startedAt = performance.now()
      const response = await fetch(url)
      return {
        body: await response.json(),
        elapsedMs: performance.now() - startedAt,
      }
    }, legacyApiUrl)

    expect(outcome.body).toEqual({ owner: 'account-b' })
    expect(outcome.elapsedMs).toBeGreaterThan(10_000)
  } finally {
    await context.close()
  }
})

test('a worker with the old retirement marker is replaced before a session is established @pwa-preview', async ({ browser, request }) => {
  const account = await registerUserSession(request, 'pwa-cache-legacy')
  const context = await browser.newContext({ serviceWorkers: 'allow' })
  const page = await context.newPage()

  // The stand-in has to be served AT /sw.js, not at a URL of its own. retire() drives
  // the migration with registration.update(), which refetches the registration's own
  // script — so a stub registered at /legacy-sw.js would simply be refetched
  // unchanged, no updatefound would fire, and the test would prove nothing. Serving
  // legacy bytes for the first fetch and then releasing the route reproduces the real
  // deploy shape: the same script URL, different bytes on update.
  let serveLegacyWorker = true
  await context.route('**/sw.js', async (route) => {
    if (!serveLegacyWorker) {
      await route.fallback()
      return
    }
    serveLegacyWorker = false
    await route.fulfill({
      contentType: 'text/javascript',
      // This is the installed #2350 worker: it claims clients and answers with
      // the old marker that the v2 page must reject.
      body: [
        "self.addEventListener('message', (event) => { if (event.data?.type === 'taskdeck:api-cache-policy' && event.ports?.[0]) event.ports[0].postMessage({ policy: 'legacy-api-cache-retired' }) })",
        "self.addEventListener('install', () => self.skipWaiting())",
        "self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()))",
        "self.addEventListener('fetch', () => {})",
      ].join('\n'),
    })
  })

  try {
    await page.goto('/login')
    await page.evaluate(async () => {
      await navigator.serviceWorker.register('/sw.js')
      await navigator.serviceWorker.ready
    })
    await expect.poll(() => page.evaluate(() => navigator.serviceWorker.controller !== null)).toBe(true)
    // The legacy stand-in answers no policy query; prove it is the one in control.
    expect(await page.evaluate(() => new Promise((resolve) => {
      const channel = new MessageChannel()
      const timer = setTimeout(() => resolve(false), 2_000)
      channel.port1.onmessage = (event) => {
        clearTimeout(timer)
        resolve((event.data as { policy?: string }).policy === 'legacy-api-cache-retired')
      }
      navigator.serviceWorker.controller?.postMessage(
        { type: 'taskdeck:api-cache-policy' },
        [channel.port2],
      )
    }))).toBe(false)

    await page.getByLabel('Username or Email').fill(account.user.username)
    await page.getByLabel('Password').fill('E2ePassword123!')
    await page.getByRole('button', { name: 'Sign in' }).click()
    await page.waitForURL('**/workspace/home')

    // The identity switch is the proof: whatever now controls the page answers the
    // retirement policy, so it cannot be the worker that replays API responses.
    expect(await page.evaluate(() => new Promise((resolve) => {
      if (!navigator.serviceWorker.controller) { resolve(true); return }
      const channel = new MessageChannel()
      const timer = setTimeout(() => resolve(false), 2_000)
      channel.port1.onmessage = (event) => {
        clearTimeout(timer)
        resolve((event.data as { policy?: string }).policy === 'taskdeck-api-cache-policy-v2')
      }
      navigator.serviceWorker.controller.postMessage(
        { type: 'taskdeck:api-cache-policy' },
        [channel.port2],
      )
    }))).toBe(true)
  } finally {
    await context.close()
  }
})
