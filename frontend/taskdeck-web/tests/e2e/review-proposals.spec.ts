/**
 * E2E: Review and Proposal Journey Expansion
 *
 * Covers review/proposal scenarios:
 * - Board-scoped proposal filtering (boardId query parameter)
 * - Multiple pending proposals displayed for the same board
 * - Applied proposal visibility in Paper's recently-applied ledger
 */

import { expect, test, type ConsoleMessage, type Page } from '@playwright/test'
import { registerAndAttachSession, type AuthResult } from './support/authSession'
import { expectApplyConfirmDialog } from './support/applyConfirm'
import { createBoardWithColumn } from './support/boardHelpers'
import {
  createCaptureItem,
  triageCaptureItem,
  waitForProposalCreated,
} from './support/captureFlow'
import { assertOk } from './support/httpAsserts'

let auth: AuthResult

const PAPER_ENUM_TEST_TITLE =
  'Paper Review renders numeric deep-review enums without browser or API errors'

function proposalSerial(proposalId: string): string {
  return `#${proposalId.slice(0, 4).toUpperCase()}`
}

function proposalQueueItem(page: Page, proposalId: string, cardTitle: string) {
  return page
    .locator(`[data-serial="${proposalSerial(proposalId)}"]`)
    .filter({ hasText: cardTitle })
}

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'review-proposals')
})

test(PAPER_ENUM_TEST_TITLE, async ({
  page,
  request,
}) => {
  test.setTimeout(90_000)

  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Deep Review Enum',
    description: 'numeric deep-review enum contract regression',
    columnNamePrefix: 'Backlog',
  })
  const cardTitle = `Review numeric enum payload ${seed}`
  const captureText = `- [ ] ${cardTitle}`

  const consoleErrors: string[] = []
  const pageErrors: string[] = []
  const failedDeepReviewResponses: string[] = []
  let proposalId: string | undefined
  page.on('console', (message: ConsoleMessage) => {
    if (message.type() === 'error') consoleErrors.push(message.text())
  })
  page.on('pageerror', (error) => pageErrors.push(error.message))
  page.on('response', (response) => {
    if (
      proposalId &&
      response.url().includes(`/automation/proposals/${proposalId}/`) &&
      response.status() >= 400
    ) {
      failedDeepReviewResponses.push(`${response.status()} ${response.url()}`)
    }
  })

  await page.goto(`/workspace/boards/${boardId}`)
  const captureHereButton = page.getByRole('button', { name: 'Capture here' })
  await expect(captureHereButton).toBeVisible()

  await captureHereButton.click()
  await expect(page).toHaveURL(new RegExp(`/workspace/inbox\\?boardId=${boardId}$`))
  const captureBody = page.getByRole('textbox', { name: 'Capture body' })
  await expect(captureBody).toBeVisible()

  const createCaptureResponsePromise = page.waitForResponse((response) =>
    response.request().method() === 'POST'
    && /\/api\/capture\/items$/i.test(response.url()))

  await captureBody.fill(captureText)
  await page.getByRole('button', { name: 'Capture' }).click()
  const createCaptureResponse = await createCaptureResponsePromise
  await assertOk(createCaptureResponse, 'create Paper review capture')
  const capturePayload = await createCaptureResponse.json() as { id?: string }
  const captureId = capturePayload.id
  expect(captureId).toBeTruthy()

  const captureRow = page.locator('.paper-triage__row').filter({ hasText: cardTitle }).first()
  await expect(captureRow).toBeVisible()
  await captureRow.getByRole('button', { name: 'Accept' }).click()

  const triaged = await waitForProposalCreated(request, auth, captureId!)
  proposalId = triaged.provenance?.proposalId
  expect(proposalId).toBeTruthy()

  const conflictsResponsePromise = page.waitForResponse(
    (response) => response.url().endsWith(`/automation/proposals/${proposalId}/conflicts`),
  )
  const historyResponsePromise = page.waitForResponse(
    (response) => response.url().endsWith(`/automation/proposals/${proposalId}/history`),
  )
  const sideEffectsResponsePromise = page.waitForResponse(
    (response) => response.url().endsWith(`/automation/proposals/${proposalId}/side-effects`),
  )

  await page.getByRole('link', { name: /Review$/ }).click()
  await expect(page).toHaveURL(/\/workspace\/review$/)
  await expect(
    page.getByRole('heading', { level: 1, name: `Capture triage: ${cardTitle}` }),
  ).toBeVisible({ timeout: 15_000 })
  await expect(page.getByRole('heading', { name: 'Conflicts & warnings' })).toBeVisible()
  await expect(page.getByRole('heading', { name: /History/ })).toBeVisible()

  const [conflictsResponse, historyResponse, sideEffectsResponse] = await Promise.all([
    conflictsResponsePromise,
    historyResponsePromise,
    sideEffectsResponsePromise,
  ])
  expect(conflictsResponse.status()).toBe(200)
  expect(historyResponse.status()).toBe(200)
  expect(sideEffectsResponse.status()).toBe(200)

  const conflicts = await conflictsResponse.json() as Array<{ tone: unknown }>
  const history = await historyResponse.json() as Array<{ status: unknown }>
  expect(conflicts.length).toBeGreaterThan(0)
  expect(history.length).toBeGreaterThan(0)
  expect(conflicts.every((row) => typeof row.tone === 'number')).toBe(true)
  expect(history.every((row) => typeof row.status === 'number')).toBe(true)
  expect(conflicts.some((row) => row.tone === 2)).toBe(true)
  expect(history.some((row) => row.status === 0)).toBe(true)

  const sideEffects = await sideEffectsResponse.json() as {
    reversibility: { summary: string; description: string; windowMs: number }
  }
  expect(typeof sideEffects.reversibility.windowMs).toBe('number')
  expect(
    `${sideEffects.reversibility.summary} ${sideEffects.reversibility.description}`.toLowerCase(),
  ).not.toMatch(/undo|reversib|single keystroke/)

  // These are mapped UI values from the real numeric responses. Waiting for
  // them proves all selector requests have settled and the enum mapper ran.
  await expect(
    page.locator('.paper-review-conflicts__row').filter({ hasText: 'CLEAR' }).first(),
  ).toBeVisible()
  await expect(page.locator('.paper-review-history__row[data-status="pending"]').first()).toContainText(
    'PENDING',
  )
  const applyRisk = page.getByTestId('apply-risk-posture')
  await expect(applyRisk).toBeVisible()
  await expect(applyRisk).toContainText('Apply considerations')
  expect((await applyRisk.innerText()).toLowerCase()).not.toMatch(/undo|reversib/)

  await expect(page.getByText('Something went wrong', { exact: true })).toHaveCount(0)
  expect(failedDeepReviewResponses).toEqual([])
  expect(pageErrors).toEqual([])
  expect(consoleErrors).toEqual([])
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
  const cardTitleA = `Card on A ${seed}`
  const captureA = await createCaptureItem(request, auth, boardIdA, `- [ ] ${cardTitleA}`)
  await triageCaptureItem(request, auth, captureA.id)
  const triagedA = await waitForProposalCreated(request, auth, captureA.id)
  const proposalIdA = triagedA.provenance?.proposalId
  expect(proposalIdA).toBeTruthy()

  const cardTitleB = `Card on B ${seed}`
  const captureB = await createCaptureItem(request, auth, boardIdB, `- [ ] ${cardTitleB}`)
  await triageCaptureItem(request, auth, captureB.id)
  const triagedB = await waitForProposalCreated(request, auth, captureB.id)
  const proposalIdB = triagedB.provenance?.proposalId
  expect(proposalIdB).toBeTruthy()

  // Navigate to review with boardId filter for board A only
  await page.goto(`/workspace/review?boardId=${boardIdA}`)

  await expect(page.getByTestId('paper-review-view')).toBeVisible()
  await expect(proposalQueueItem(page, proposalIdA as string, cardTitleA)).toBeVisible({ timeout: 15_000 })

  await expect(proposalQueueItem(page, proposalIdB as string, cardTitleB)).toHaveCount(0)
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
  const cardTitle1 = `First proposal card ${seed}`
  const capture1 = await createCaptureItem(request, auth, boardId, `- [ ] ${cardTitle1}`)
  await triageCaptureItem(request, auth, capture1.id)
  const triaged1 = await waitForProposalCreated(request, auth, capture1.id)
  const proposalId1 = triaged1.provenance?.proposalId
  expect(proposalId1).toBeTruthy()

  const cardTitle2 = `Second proposal card ${seed}`
  const capture2 = await createCaptureItem(request, auth, boardId, `- [ ] ${cardTitle2}`)
  await triageCaptureItem(request, auth, capture2.id)
  const triaged2 = await waitForProposalCreated(request, auth, capture2.id)
  const proposalId2 = triaged2.provenance?.proposalId
  expect(proposalId2).toBeTruthy()

  // Navigate to review filtered by this board
  await page.goto(`/workspace/review?boardId=${boardId}`)

  await expect(page.getByTestId('paper-review-view')).toBeVisible()
  await expect(proposalQueueItem(page, proposalId1 as string, cardTitle1)).toBeVisible({ timeout: 15_000 })
  await expect(proposalQueueItem(page, proposalId2 as string, cardTitle2)).toBeVisible({ timeout: 15_000 })
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
  const queueItem = proposalQueueItem(page, proposalId as string, cardTitle)
  await expect(queueItem).toBeVisible({ timeout: 15_000 })
  await expect(queueItem).toHaveAttribute('aria-pressed', 'true')

  const approveResponse = page.waitForResponse((response) =>
    response.request().method() === 'POST'
    && response.url().endsWith(`/automation/proposals/${proposalId}/approve`))
  await page.getByTestId('decision-apply').click()
  await assertOk(await approveResponse, `approve proposal ${proposalId}`)

  const executeResponse = page.waitForResponse((response) =>
    response.request().method() === 'POST'
    && response.url().endsWith(`/automation/proposals/${proposalId}/execute`))
  // GH-1942: approve hands straight to the confirmation, so it is already open.
  await expectApplyConfirmDialog(page)
  await assertOk(await executeResponse, `execute proposal ${proposalId}`)
  await expect(queueItem).toHaveCount(0)

  await expect(
    page
      .locator('.paper-review-recent__row')
      .filter({ hasText: proposalSerial(proposalId as string) })
      .filter({ hasText: cardTitle }),
  ).toBeVisible({ timeout: 10_000 })
})
