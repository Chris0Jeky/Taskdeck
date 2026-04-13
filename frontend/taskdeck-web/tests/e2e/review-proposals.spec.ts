/**
 * E2E: Review and Proposal Journey Expansion
 *
 * Extends the review/proposal coverage beyond the golden-path tests:
 * - Board-scoped proposal filtering: only shows proposals for the selected board
 * - Multiple proposals on one board: batch visibility
 * - Applied proposal appears in completed toggle: visible when Show Completed is enabled
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
  const proposalCardA = page.locator(`#proposal-${proposalIdA}`)
  await expect(proposalCardA).toBeVisible({ timeout: 15_000 })

  // Proposal for board B should NOT be visible
  const proposalCardB = page.locator(`#proposal-${proposalIdB}`)
  await expect(proposalCardB).toHaveCount(0)
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
  await expect(page.locator(`#proposal-${proposalId1}`)).toBeVisible({ timeout: 15_000 })
  await expect(page.locator(`#proposal-${proposalId2}`)).toBeVisible({ timeout: 15_000 })
})

// --- Applied proposal appears in completed toggle ---

test('applied proposal should appear when Show Completed is toggled on', async ({ page, request }) => {
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
  const proposalCard = page.locator(`#proposal-${proposalId}`)
  await expect(proposalCard).toBeVisible({ timeout: 15_000 })

  await proposalCard.getByRole('button', { name: 'Approve for board' }).click()
  await expect(proposalCard.getByText('Approved, ready to apply')).toBeVisible()

  page.once('dialog', (dialog) => dialog.accept())
  await proposalCard.getByRole('button', { name: 'Apply to board' }).click()
  await expect(proposalCard).not.toBeVisible()

  // Toggle "Show completed" to reveal the applied proposal
  await page.getByLabel('Show completed').check()
  await expect(proposalCard).toBeVisible({ timeout: 10_000 })
})
