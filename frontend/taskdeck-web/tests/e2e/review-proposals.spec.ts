/**
 * E2E: Review and Proposal Journey Expansion
 *
 * Covers review/proposal scenarios:
 * - Board-scoped proposal filtering (boardId query parameter)
 * - Multiple pending proposals displayed for the same board
 * - Applied proposal visibility in Paper's recently-applied ledger
 */

import { expect, test } from '@playwright/test'
import { registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import {
  createCaptureItem,
  triageCaptureItem,
  waitForProposalCreated,
} from './support/captureFlow'

let auth: AuthResult

function proposalSerial(proposalId: string): string {
  return `#${proposalId.slice(0, 4).toUpperCase()}`
}

function proposalQueueItem(page: import('@playwright/test').Page, proposalId: string) {
  return page.locator(`[data-serial="${proposalSerial(proposalId)}"]`)
}

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'review-proposals')
})

// --- Board-scoped proposal filtering ---

test('review view with boardId filter should only show proposals for that board', async ({ page, request }) => {
  test.setTimeout(90_000)

  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`

  // Create two boards
  const boardIdA = await createBoardWithColumn(request, auth, `${seed}-A`, {
    boardNamePrefix: 'Filter Board A',
    description: 'board A for review filtering',
    columnNamePrefix: 'Backlog',
  })
  const boardIdB = await createBoardWithColumn(request, auth, `${seed}-B`, {
    boardNamePrefix: 'Filter Board B',
    description: 'board B for review filtering',
    columnNamePrefix: 'Backlog',
  })

  // Create and triage captures on both boards
  const captureA = await createCaptureItem(request, auth, boardIdA, `- [ ] Card on A ${seed}`)
  await triageCaptureItem(request, auth, captureA.id)
  const triagedA = await waitForProposalCreated(request, auth, captureA.id)
  const proposalIdA = triagedA.provenance?.proposalId
  expect(proposalIdA).toBeTruthy()

  const captureB = await createCaptureItem(request, auth, boardIdB, `- [ ] Card on B ${seed}`)
  await triageCaptureItem(request, auth, captureB.id)
  const triagedB = await waitForProposalCreated(request, auth, captureB.id)
  const proposalIdB = triagedB.provenance?.proposalId
  expect(proposalIdB).toBeTruthy()

  // Navigate to review with boardId filter for board A only
  await page.goto(`/workspace/review?boardId=${boardIdA}`)

  // Proposal for board A should be visible
  await expect(page.getByTestId('paper-review-view')).toBeVisible()
  await expect(proposalQueueItem(page, proposalIdA as string)).toBeVisible({ timeout: 15_000 })

  // Proposal for board B should NOT be visible
  await expect(proposalQueueItem(page, proposalIdB as string)).toHaveCount(0)
})

// --- Multiple proposals on one board ---

test('review view should display multiple pending proposals for the same board', async ({ page, request }) => {
  test.setTimeout(90_000)

  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Multi Proposal',
    description: 'multiple proposals test board',
    columnNamePrefix: 'Backlog',
  })

  // Create two captures and triage them both
  const capture1 = await createCaptureItem(request, auth, boardId, `- [ ] First proposal card ${seed}`)
  await triageCaptureItem(request, auth, capture1.id)
  const triaged1 = await waitForProposalCreated(request, auth, capture1.id)
  const proposalId1 = triaged1.provenance?.proposalId
  expect(proposalId1).toBeTruthy()

  const capture2 = await createCaptureItem(request, auth, boardId, `- [ ] Second proposal card ${seed}`)
  await triageCaptureItem(request, auth, capture2.id)
  const triaged2 = await waitForProposalCreated(request, auth, capture2.id)
  const proposalId2 = triaged2.provenance?.proposalId
  expect(proposalId2).toBeTruthy()

  // Navigate to review filtered by this board
  await page.goto(`/workspace/review?boardId=${boardId}`)

  // Both proposals should be visible
  await expect(page.getByTestId('paper-review-view')).toBeVisible()
  await expect(proposalQueueItem(page, proposalId1 as string)).toBeVisible({ timeout: 15_000 })
  await expect(proposalQueueItem(page, proposalId2 as string)).toBeVisible({ timeout: 15_000 })
})

// --- Applied proposal appears in the Paper filing ledger ---

test('applied proposal should appear in the recently-applied ledger', async ({ page, request }) => {
  test.setTimeout(90_000)

  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Completed Toggle',
    description: 'completed toggle test board',
    columnNamePrefix: 'Todo',
  })

  const cardTitle = `Completed card ${seed}`
  const captureText = `- [ ] ${cardTitle}`
  const captureItem = await createCaptureItem(request, auth, boardId, captureText)
  await triageCaptureItem(request, auth, captureItem.id)

  const triagedItem = await waitForProposalCreated(request, auth, captureItem.id)
  const proposalId = triagedItem.provenance?.proposalId
  expect(proposalId).toBeTruthy()

  // Navigate to review and approve+apply the proposal
  await page.goto(`/workspace/review?boardId=${boardId}#proposal-${proposalId}`)
  const queueItem = proposalQueueItem(page, proposalId as string)
  await expect(queueItem).toBeVisible({ timeout: 15_000 })
  await expect(queueItem).toHaveAttribute('aria-pressed', 'true')

  const approveResponse = page.waitForResponse((response) =>
    response.request().method() === 'POST'
    && response.url().endsWith(`/automation/proposals/${proposalId}/approve`))
  await page.getByTestId('decision-apply').click()
  expect((await approveResponse).ok()).toBeTruthy()

  page.once('dialog', (dialog) => dialog.accept())
  const executeResponse = page.waitForResponse((response) =>
    response.request().method() === 'POST'
    && response.url().endsWith(`/automation/proposals/${proposalId}/execute`))
  await page.getByTestId('decision-apply').click()
  expect((await executeResponse).ok()).toBeTruthy()
  await expect(queueItem).toHaveCount(0)

  // Paper keeps just-applied proposals in the always-visible local filing ledger.
  await expect(
    page.locator('.paper-review-recent__row').filter({ hasText: proposalSerial(proposalId as string) }),
  ).toBeVisible({ timeout: 10_000 })
})
