import { expect, test } from '@playwright/test'
import { API_BASE_URL, API_ORIGIN, registerAndAttachSession } from './support/authSession'
import { apiRoutePath } from './support/apiRoutePath'

test('re-exposing a snoozed retained proposal restores its refresh warning', async ({ page, request }, testInfo) => {
  await registerAndAttachSession(page, request, 'review-retained-health')
  const proposalId = '11111111-1111-4111-8111-111111111111'
  const boardId = '22222222-2222-4222-8222-222222222222'
  const now = new Date()
  await page.clock.install({ time: now })
  const collectionPath = apiRoutePath(API_BASE_URL, 'automation/proposals')
  let reads = 0
  await page.route(url => url.origin === API_ORIGIN && url.pathname === collectionPath, async route => {
    reads += 1
    await route.fulfill({
      status: reads === 1 ? 200 : 400,
      contentType: 'application/json',
      body: reads === 1 ? JSON.stringify([{
        id: proposalId, boardId, status: 'PendingReview', sourceType: 'Manual',
        sourceReferenceId: null, requestedByUserId: 'synthetic-owner', riskLevel: 'Low',
        summary: 'Retained snoozed proposal', diffPreview: null, validationIssues: null,
        createdAt: now.toISOString(), updatedAt: now.toISOString(),
        expiresAt: '2099-01-01T00:00:00Z', deferredUntil: new Date(now.getTime() + 600_000).toISOString(),
        decidedAt: null, decidedByUserId: null, appliedAt: null, failureReason: null,
        correlationId: 'retained-health', operations: [], approvedRevisionId: null, latestRevisionId: null,
      }]) : JSON.stringify({ title: 'Synthetic refresh refusal' }),
    })
  })

  await page.goto(`/workspace/review?boardId=${boardId}`)
  await expect.poll(() => reads).toBe(1)
  for (let tick = 0; tick < 3; tick += 1) {
    await page.clock.runFor(15_000)
    await expect.poll(() => reads).toBe(tick + 2)
  }
  const warning = page.getByTestId('paper-review-queue-refused')
  await expect(warning).not.toBeEmpty()

  await page.evaluate(() => {
    window.history.pushState({}, '', '/workspace/review')
    window.dispatchEvent(new PopStateEvent('popstate'))
  })
  await expect.poll(() => reads).toBe(5)
  await expect(warning).toBeEmpty()

  await page.evaluate(id => {
    window.history.pushState({}, '', `/workspace/review#proposal-${id}`)
    window.dispatchEvent(new PopStateEvent('popstate'))
  }, proposalId)
  await expect(warning).not.toBeEmpty()
  await expect(page.getByText('Retained snoozed proposal').first()).toBeVisible()
  expect(reads).toBe(5)
  await page.screenshot({ path: testInfo.outputPath('retained-warning-restored.png'), fullPage: true })
})
