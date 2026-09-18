/**
 * E2E: Paper review queue permission recovery (#2214)
 *
 * The list read is intercepted at the browser boundary so the test can drive
 * a deterministic revoked -> retry refused -> recovered sequence against the
 * real authenticated Paper surface. No component or API module is mocked.
 */

import { expect, test, type Page } from '@playwright/test'
import { API_BASE_URL, API_ORIGIN, registerAndAttachSession } from './support/authSession'
import { apiRoutePath } from './support/apiRoutePath'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'review-queue-recovery')
})

async function moveReviewScopeWithoutReload(page: Page, boardId: string) {
  await page.evaluate((nextBoardId) => {
    const nextUrl = new URL(window.location.href)
    nextUrl.searchParams.set('boardId', nextBoardId)
    window.history.pushState({}, '', nextUrl)
    window.dispatchEvent(new PopStateEvent('popstate'))
  }, boardId)
}

test('delayed unavailable proposal preserves keyboard focus in the queue', async ({ page }, testInfo) => {
  await page.addInitScript(() => localStorage.setItem('td.paper.mode.v2', 'paper'))
  const missingId = '11111111-1111-4111-8111-111111111111'
  const detailPath = apiRoutePath(API_BASE_URL, `automation/proposals/${missingId}`)
  let releaseLookup!: () => void
  const lookupRelease = new Promise<void>((resolve) => { releaseLookup = resolve })
  let signalLookup!: () => void
  const lookupStarted = new Promise<void>((resolve) => { signalLookup = resolve })
  await page.route((url) => url.origin === API_ORIGIN && url.pathname === detailPath, async (route) => {
    signalLookup()
    await lookupRelease
    await route.fulfill({ status: 404, contentType: 'application/json', body: '{"title":"Not found"}' })
  })
  await page.goto('/workspace/review')
  const queueControl = page.locator('.paper-review-rail__pill').first()
  await expect(queueControl).toBeVisible()
  const announcement = page.getByTestId('paper-review-unavailable-announcement')
  await expect(announcement).toHaveAttribute('role', 'status')
  await expect(announcement).toHaveAttribute('aria-live', 'polite')
  await expect(announcement).toBeEmpty()
  await page.evaluate((id) => {
    window.history.pushState({}, '', `/workspace/review#proposal-${id}`)
    window.dispatchEvent(new PopStateEvent('popstate'))
  }, missingId)
  await lookupStarted
  await queueControl.focus()
  await expect(queueControl).toBeFocused()
  releaseLookup()
  await expect(page.getByTestId('paper-review-unavailable-return')).toBeVisible()
  await expect(queueControl).toBeFocused()
  await expect(announcement).toContainText(missingId)
  await page.screenshot({ path: testInfo.outputPath('unavailable-preserves-queue-focus.png'), fullPage: true })
  await page.getByTestId('paper-review-unavailable-return').click()
  await expect(announcement).toBeEmpty()
})

test('shows repeated refusal feedback only after a second explicit 403 and clears it on success', async ({ page }, testInfo) => {
  test.setTimeout(60_000)

  const collectionPath = apiRoutePath(API_BASE_URL, 'automation/proposals')
  const responseStatuses: number[] = []
  let listReads = 0

  await page.route((url) => (
    url.origin === API_ORIGIN && url.pathname === collectionPath
  ), async (route) => {
    if (route.request().method() !== 'GET') {
      await route.continue()
      return
    }

    listReads += 1
    const status = listReads <= 2 ? 403 : 200
    responseStatuses.push(status)
    await route.fulfill({
      status,
      contentType: 'application/json',
      body: status === 403 ? JSON.stringify({ title: 'Forbidden' }) : '[]',
    })
  })

  await page.goto('/workspace/review?boardId=synthetic-revoked-a')
  const revokedPanel = page.getByTestId('paper-review-access-revoked')
  const retryFeedback = page.getByTestId('paper-review-access-revoked-retry')
  await expect(revokedPanel).toBeVisible()
  await expect(retryFeedback).toHaveCount(0)
  expect(listReads).toBe(1)

  // This is an in-app route change, so the existing composable instance makes
  // the second explicit list read while its revoked state is still active.
  await moveReviewScopeWithoutReload(page, 'synthetic-revoked-b')
  await expect(retryFeedback).toBeVisible()
  expect(listReads).toBe(2)
  expect(responseStatuses).toEqual([403, 403])
  await expect(page.getByText(/Failed to load proposals/i)).toHaveCount(0)
  await page.screenshot({
    path: testInfo.outputPath('review-queue-revoked-retry.png'),
    fullPage: true,
  })

  // A successful explicit read clears both the authority panel and its retry
  // sentence. The empty queue is intentional: this proves recovery without
  // requiring a real proposal or LLM-backed seed.
  await moveReviewScopeWithoutReload(page, 'synthetic-recovered')
  await expect(revokedPanel).toHaveCount(0)
  await expect(retryFeedback).toHaveCount(0)
  expect(listReads).toBe(3)
  expect(responseStatuses).toEqual([403, 403, 200])
  await page.screenshot({
    path: testInfo.outputPath('review-queue-recovered.png'),
    fullPage: true,
  })
})
