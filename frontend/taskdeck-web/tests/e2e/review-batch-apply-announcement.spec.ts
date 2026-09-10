import { expect, test } from '@playwright/test'
import { API_BASE_URL, API_ORIGIN, registerAndAttachSession } from './support/authSession'
import { apiRoutePath } from './support/apiRoutePath'

test('announces a delayed unavailable lookup inside Batch Apply without moving focus', async ({ page, request }, testInfo) => {
  await registerAndAttachSession(page, request, 'batch-apply-announcement')
  const proposalId = '11111111-1111-4111-8111-111111111111'
  const missingId = '22222222-2222-4222-8222-222222222222'
  const collectionPath = apiRoutePath(API_BASE_URL, 'automation/proposals')
  const missingPath = apiRoutePath(API_BASE_URL, `automation/proposals/${missingId}`)
  const now = new Date().toISOString()
  await page.route(url => url.origin === API_ORIGIN && url.pathname === collectionPath, route => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify([{
      id: proposalId, boardId: '33333333-3333-4333-8333-333333333333',
      status: 'Approved', sourceType: 'Manual', sourceReferenceId: null,
      requestedByUserId: 'synthetic-owner', riskLevel: 'Low', summary: 'Already approved proposal',
      diffPreview: null, validationIssues: null, createdAt: now, updatedAt: now,
      expiresAt: '2099-01-01T00:00:00Z', decidedAt: now, decidedByUserId: 'synthetic-owner',
      appliedAt: null, failureReason: null, correlationId: 'batch-apply-announcement',
      operations: [], approvedRevisionId: null, latestRevisionId: null,
    }]),
  }))
  let releaseLookup!: () => void
  const lookupRelease = new Promise<void>(resolve => { releaseLookup = resolve })
  let signalLookup!: () => void
  const lookupStarted = new Promise<void>(resolve => { signalLookup = resolve })
  await page.route(url => url.origin === API_ORIGIN && url.pathname === missingPath, async route => {
    signalLookup()
    await lookupRelease
    await route.fulfill({ status: 404, contentType: 'application/json', body: '{"title":"Not found"}' })
  })
  await page.goto('/workspace/review')
  await expect(page.getByTestId('queue-batch-execute')).toBeVisible()
  await page.evaluate(id => {
    window.history.pushState({}, '', `/workspace/review#proposal-${id}`)
    window.dispatchEvent(new PopStateEvent('popstate'))
  }, missingId)
  await lookupStarted
  await page.getByTestId('queue-batch-execute').click()
  const confirm = page.getByTestId('batch-execute-confirm')
  await confirm.focus()
  const announcement = page.getByTestId('batch-execute-announcement')
  await expect(announcement).toBeEmpty()
  await expect(announcement).toHaveAttribute('aria-live', 'polite')
  expect(await announcement.evaluate(node => node.closest('[role="dialog"]')?.getAttribute('aria-modal'))).toBe('true')
  releaseLookup()
  await expect(announcement).toContainText(missingId)
  await expect(confirm).toBeFocused()
  await expect(page.getByTestId('paper-review-unavailable-announcement')).toBeEmpty()
  await page.screenshot({ path: testInfo.outputPath('unavailable-inside-batch-apply.png'), fullPage: true, animations: 'disabled' })
  await page.getByTestId('batch-execute-cancel').click()
  await expect(page.getByTestId('paper-review-unavailable-announcement')).toBeEmpty()
  await expect(page.getByTestId('paper-review-unavailable-return')).toBeVisible()
})
