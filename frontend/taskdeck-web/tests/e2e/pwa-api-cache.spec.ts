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
