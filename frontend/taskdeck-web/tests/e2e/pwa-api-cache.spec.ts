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

test('a worker without the retirement policy is replaced before a session is established @pwa-preview', async ({ browser, request }) => {
  const account = await registerUserSession(request, 'pwa-cache-legacy')
  const context = await browser.newContext({ serviceWorkers: 'allow' })
  const page = await context.newPage()

  // Stand-in for a pre-#2350 installation: it controls the page and answers no
  // policy query, exactly like the shipped worker whose NetworkFirst API route
  // repopulated the authenticated cache after every page-side purge.
  await context.route('**/legacy-sw.js', async (route) => {
    await route.fulfill({
      contentType: 'text/javascript',
      body: [
        "self.addEventListener('install', () => self.skipWaiting())",
        "self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()))",
      ].join('\n'),
    })
  })

  try {
    await page.goto('/login')
    await page.evaluate(async () => {
      await navigator.serviceWorker.register('/legacy-sw.js')
      await navigator.serviceWorker.ready
    })
    await expect.poll(() => page.evaluate(() =>
      navigator.serviceWorker.controller?.scriptURL.endsWith('/legacy-sw.js') === true,
    )).toBe(true)

    await page.getByLabel('Username or Email').fill(account.user.username)
    await page.getByLabel('Password').fill('E2ePassword123!')
    await page.getByRole('button', { name: 'Sign in' }).click()

    // The identity switch is the proof: the legacy worker is no longer in control
    // by the time the workspace renders, either replaced by the current build or
    // removed outright.
    await page.waitForURL('**/workspace/home')
    expect(await page.evaluate(() =>
      navigator.serviceWorker.controller?.scriptURL.endsWith('/legacy-sw.js') === true,
    )).toBe(false)
  } finally {
    await context.close()
  }
})
