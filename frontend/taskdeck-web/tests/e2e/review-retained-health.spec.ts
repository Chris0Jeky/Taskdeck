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

test('a same-count hash swap restores only the landed proposal refresh warning (#2930)', async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'review-health-owner')
  const retainedId = '31111111-1111-4111-8111-111111111111'
  const freshId = '41111111-1111-4111-8111-111111111111'
  const boardId = '32222222-2222-4222-8222-222222222222'
  const freshBoardId = '42222222-2222-4222-8222-222222222222'
  const now = new Date()
  await page.clock.install({ time: now })
  const retained = {
    id: retainedId, boardId, status: 'PendingReview', sourceType: 'Manual',
    sourceReferenceId: null, requestedByUserId: 'synthetic-owner', riskLevel: 'Low',
    summary: 'Retained deferred board B', diffPreview: null, validationIssues: null,
    createdAt: now.toISOString(), updatedAt: now.toISOString(),
    expiresAt: '2099-01-01T00:00:00Z', deferredUntil: new Date(now.getTime() + 600_000).toISOString(),
    decidedAt: null, decidedByUserId: null, appliedAt: null, failureReason: null,
    correlationId: 'health-owner', operations: [], approvedRevisionId: null, latestRevisionId: null,
  }
  const fresh = { ...retained, id: freshId, boardId: freshBoardId, summary: 'Fresh deferred board C' }
  const collectionPath = apiRoutePath(API_BASE_URL, 'automation/proposals')
  let reads = 0
  await page.route(url => url.origin === API_ORIGIN && url.pathname === collectionPath, async route => {
    reads += 1
    await route.fulfill({
      status: reads === 1 ? 200 : 400,
      contentType: 'application/json',
      body: reads === 1 ? JSON.stringify([retained]) : JSON.stringify({ title: 'Synthetic refresh refusal' }),
    })
  })
  const detailReads: string[] = []
  await page.route(url => url.origin === API_ORIGIN &&
    (url.pathname === `${collectionPath}/${freshId}` || url.pathname === `${collectionPath}/${retainedId}`), async route => {
    const id = new URL(route.request().url()).pathname.split('/').at(-1)!
    detailReads.push(id)
    await route.fulfill({
      status: 200, contentType: 'application/json',
      body: JSON.stringify(id === freshId ? fresh : retained),
    })
  })

  await page.goto(`/workspace/review?boardId=${boardId}`)
  await expect.poll(() => reads).toBe(1)
  for (let tick = 0; tick < 3; tick += 1) {
    await page.clock.runFor(15_000)
    await expect.poll(() => reads).toBe(tick + 2)
  }
  const warning = page.getByTestId('paper-review-queue-refused')
  const rows = page.locator('.paper-review-rail__queue-row')
  await expect(warning).not.toBeEmpty()
  await page.evaluate(() => {
    window.history.pushState({}, '', '/workspace/review')
    window.dispatchEvent(new PopStateEvent('popstate'))
  })
  await expect.poll(() => reads).toBe(5)
  await expect(warning).toBeEmpty()
  await expect(rows).toHaveCount(0)

  await page.evaluate(id => {
    window.history.pushState({}, '', `/workspace/review#proposal-${id}`)
    window.dispatchEvent(new PopStateEvent('popstate'))
  }, freshId)
  await expect(rows).toHaveCount(1)
  await expect(rows).toContainText('Fresh deferred board C')
  await expect(warning).toBeEmpty()

  await page.evaluate(id => {
    window.history.pushState({}, '', `/workspace/review#proposal-${id}`)
    window.dispatchEvent(new PopStateEvent('popstate'))
  }, retainedId)
  await expect(rows).toHaveCount(1)
  await expect(rows).toContainText('Retained deferred board B')
  await expect(warning).not.toBeEmpty()
  expect(reads).toBe(5)
  expect(detailReads).toEqual([freshId])
})
