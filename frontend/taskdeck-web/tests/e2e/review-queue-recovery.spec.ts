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

test('shows repeated refusal feedback only after a second explicit 403 and clears it on success', async ({ page }) => {
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

  // A successful explicit read clears both the authority panel and its retry
  // sentence. The empty queue is intentional: this proves recovery without
  // requiring a real proposal or LLM-backed seed.
  await moveReviewScopeWithoutReload(page, 'synthetic-recovered')
  await expect(revokedPanel).toHaveCount(0)
  await expect(retryFeedback).toHaveCount(0)
  expect(listReads).toBe(3)
  expect(responseStatuses).toEqual([403, 403, 200])
})
