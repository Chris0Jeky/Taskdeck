/**
 * E2E: Review and Proposal Journey Expansion
 *
 * Covers review/proposal scenarios:
 * - Board-scoped proposal filtering (boardId query parameter)
 * - Multiple pending proposals displayed for the same board
 * - Applied proposal visibility in Paper's recently-applied ledger
 */

import { expect, test, type ConsoleMessage, type Page } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { API_BASE_URL, registerAndAttachSession, type AuthResult } from './support/authSession'
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
  const captureBody = page.getByTestId('paper-composer-body')
  await expect(captureBody).toBeVisible()

  const createCaptureResponsePromise = page.waitForResponse((response) =>
    response.request().method() === 'POST'
    && /\/api\/capture\/items$/i.test(response.url()))

  await captureBody.fill(captureText)
  await page.getByRole('button', { name: /^Capture/ }).click()
  const createCaptureResponse = await createCaptureResponsePromise
  await assertOk(createCaptureResponse, 'create Paper review capture')
  const capturePayload = await createCaptureResponse.json() as { id?: string }
  const captureId = capturePayload.id
  expect(captureId).toBeTruthy()

  const captureRow = page.locator('.paper-triage__row').filter({ hasText: cardTitle }).first()
  await expect(captureRow).toBeVisible()
  await captureRow.getByRole('button', { name: 'Ask AI', exact: true }).click()

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

  await page.locator('[data-paper-sidebar] a[href="/workspace/review"]').first().click()
  await expect(page).toHaveURL(/\/workspace\/review$/)
  await expect(
    page.getByRole('heading', { level: 1, name: `Capture triage: ${cardTitle}` }),
  ).toBeVisible({ timeout: 15_000 })
  await expect(page.getByRole('heading', { name: 'Conflicts & warnings' })).toBeVisible()
  await expect(page.getByRole('heading', { name: /History/ })).toBeVisible()

  const confidenceDisclosure = page.getByTestId('paper-review-confidence-disclosure')
  const provenanceDisclosure = page.getByTestId('paper-review-provenance-disclosure')
  const similarPastDisclosure = page.getByTestId('paper-review-similar-past-disclosure')
  await expect(confidenceDisclosure).toHaveAttribute('aria-expanded', 'false')
  await expect(provenanceDisclosure).toHaveAttribute('aria-expanded', 'false')
  await expect(similarPastDisclosure).toHaveAttribute('aria-expanded', 'false')
  await expect(page.getByTestId('paper-review-confidence-details')).toBeHidden()
  await expect(page.getByTestId('paper-review-provenance-details')).toBeHidden()
  await expect(page.getByTestId('paper-review-similar-past-details')).toBeHidden()

  await confidenceDisclosure.focus()
  await confidenceDisclosure.press('Enter')
  await expect(confidenceDisclosure).toBeFocused()
  await expect(confidenceDisclosure).toHaveAttribute('aria-expanded', 'true')
  await expect(page.getByTestId('paper-review-confidence-details')).toBeVisible()
  await expect(provenanceDisclosure).toHaveAttribute('aria-expanded', 'false')

  await provenanceDisclosure.focus()
  await provenanceDisclosure.press('Space')
  await expect(provenanceDisclosure).toBeFocused()
  await expect(provenanceDisclosure).toHaveAttribute('aria-expanded', 'true')
  await expect(page.getByTestId('paper-review-provenance-details')).toBeVisible()
  await expect(page.getByText('View full read-set')).toBeVisible()
  await expect(similarPastDisclosure).toHaveAttribute('aria-expanded', 'false')

  await similarPastDisclosure.click()
  await expect(similarPastDisclosure).toBeFocused()
  await expect(similarPastDisclosure).toHaveAttribute('aria-expanded', 'true')
  await expect(page.getByTestId('paper-review-similar-past-details')).toBeVisible()

  const axeResults = await new AxeBuilder({ page })
    .include('.paper-review-author')
    .include('.paper-review-prov')
    .include('.paper-review-past')
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze()
  expect(axeResults.violations).toHaveLength(0)

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

test('saved revision decision lock consumes the first Apply until authoritative reads settle', async ({
  page,
  request,
}) => {
  test.setTimeout(90_000)

  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Revision Truth Barrier',
    description: 'post-revision decision lock regression',
    columnNamePrefix: 'Todo',
  })
  const cardTitle = `Revision truth barrier ${seed}`
  const capture = await createCaptureItem(request, auth, boardId, `- [ ] ${cardTitle}`)
  await triageCaptureItem(request, auth, capture.id)
  const triaged = await waitForProposalCreated(request, auth, capture.id)
  const proposalId = triaged.provenance?.proposalId
  expect(proposalId).toBeTruthy()

  const proposalPath = `${new URL(API_BASE_URL).pathname}/automation/proposals/${encodeURIComponent(proposalId!)}`
  const collectionPath = `${new URL(API_BASE_URL).pathname}/automation/proposals`
  const selectorSuffixes = [
    'provenance',
    'confidence',
    'side-effects',
    'conflicts',
    'history',
    'similar-past',
  ]
  let holdAuthoritativeReads = false
  let approveRequests = 0
  let executeRequests = 0
  let signalQueueHeld!: () => void
  let releaseQueue!: () => void
  let signalSelectorsHeld!: () => void
  let releaseSelectors!: () => void
  const queueHeld = new Promise<void>((resolve) => { signalQueueHeld = resolve })
  const queueRelease = new Promise<void>((resolve) => { releaseQueue = resolve })
  const selectorsHeld = new Promise<void>((resolve) => { signalSelectorsHeld = resolve })
  const selectorsRelease = new Promise<void>((resolve) => { releaseSelectors = resolve })
  const heldSelectorSuffixes = new Set<string>()

  page.on('request', (outgoing) => {
    if (outgoing.method() !== 'POST') return
    const path = new URL(outgoing.url()).pathname
    if (path === `${proposalPath}/approve`) approveRequests += 1
    if (path === `${proposalPath}/execute`) executeRequests += 1
  })

  await page.route('**/api/automation/proposals**', async (route) => {
    const outgoing = route.request()
    if (outgoing.method() !== 'GET' || !holdAuthoritativeReads) {
      await route.continue()
      return
    }

    const path = new URL(outgoing.url()).pathname
    if (path === collectionPath) {
      const response = await route.fetch()
      signalQueueHeld()
      await queueRelease
      await route.fulfill({ response })
      return
    }

    const selectorSuffix = selectorSuffixes.find(
      (suffix) => path === `${proposalPath}/${suffix}`,
    )
    if (selectorSuffix) {
      const response = await route.fetch()
      heldSelectorSuffixes.add(selectorSuffix)
      if (heldSelectorSuffixes.size === selectorSuffixes.length) signalSelectorsHeld()
      await selectorsRelease
      await route.fulfill({ response })
      return
    }

    await route.continue()
  })

  const initialSelectorReads = Promise.all(
    selectorSuffixes.map((suffix) =>
      page.waitForResponse((response) =>
        response.request().method() === 'GET'
        && new URL(response.url()).pathname === `${proposalPath}/${suffix}`),
    ),
  )
  await page.goto(`/workspace/review?boardId=${boardId}#proposal-${proposalId}`)
  const queueItem = proposalQueueItem(page, proposalId!, cardTitle)
  await expect(queueItem).toBeVisible({ timeout: 15_000 })
  await expect(queueItem).toHaveAttribute('aria-pressed', 'true')
  await initialSelectorReads

  await page.getByTestId('decision-edit').click()
  const operationsField = page.getByTestId('revision-field-operations')
  await expect(operationsField).toBeVisible()
  const revisedOperations = JSON.parse(await operationsField.inputValue()) as Array<{
    idempotencyKey: string
  }>
  expect(revisedOperations.length).toBeGreaterThan(0)
  revisedOperations[0]!.idempotencyKey = `revision-lock-${seed}`
  await operationsField.fill(JSON.stringify(revisedOperations))
  await page.getByTestId('revision-reason').fill('Re-check the saved operation before approval')
  const revisionResponse = page.waitForResponse((response) =>
    response.request().method() === 'POST'
    && new URL(response.url()).pathname === `${proposalPath}/revisions`)
  await page.getByTestId('revision-save').click()
  await assertOk(await revisionResponse, `save revision for proposal ${proposalId}`)
  await expect(page.getByTestId('revision-editor')).toHaveCount(0)
  await expect(page.getByTestId('revision-badge')).toBeVisible()

  holdAuthoritativeReads = true
  const apply = page.getByTestId('decision-apply')
  await apply.focus()
  await apply.click()

  // The collection response has reached the real backend, but the browser has
  // not received it. The whole rail and keymap stay inert under one visible lock.
  await queueHeld
  const lock = page.getByTestId('decision-lock-note')
  await expect(lock).toBeVisible()
  for (const testId of ['decision-reject', 'decision-edit', 'decision-defer', 'decision-apply']) {
    await expect(page.getByTestId(testId)).toBeDisabled()
  }
  await page.keyboard.press('Enter')
  await page.waitForTimeout(100)
  expect(approveRequests).toBe(0)
  expect(executeRequests).toBe(0)

  // Once the refreshed proposal lands, hold every exact-key selector response.
  // Apply must remain locked until the slowest core evidence read settles.
  releaseQueue()
  await selectorsHeld
  expect([...heldSelectorSuffixes].sort()).toEqual([...selectorSuffixes].sort())
  await expect(lock).toBeVisible()
  await expect(apply).toBeDisabled()
  await page.keyboard.press('Enter')
  await page.waitForTimeout(100)
  expect(approveRequests).toBe(0)
  expect(executeRequests).toBe(0)

  releaseSelectors()
  await expect(lock).toHaveCount(0)
  await expect(apply).toBeEnabled()
  await expect(apply).toBeFocused()
  expect(approveRequests).toBe(0)
  expect(executeRequests).toBe(0)
  await expect(page.getByTestId('paper-review-decision-receipt')).toHaveCount(0)

  // The first action was consumed by review refresh. Only this second explicit
  // click records approval, and it still does not execute the board write.
  holdAuthoritativeReads = false
  const approveResponse = page.waitForResponse((response) =>
    response.request().method() === 'POST'
    && new URL(response.url()).pathname === `${proposalPath}/approve`)
  await apply.click()
  await assertOk(await approveResponse, `approve revised proposal ${proposalId}`)
  await expect(page.getByTestId('paper-review-decision-receipt')).toHaveAttribute(
    'data-decision',
    'approved',
  )
  expect(approveRequests).toBe(1)
  expect(executeRequests).toBe(0)
})

test('held Enter on batch confirmation keeps receipts open until keyup', async ({ page, request }) => {
  test.setTimeout(90_000)

  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Held Enter Batch',
    description: 'held Enter batch receipt guard',
    columnNamePrefix: 'Todo',
  })
  const cardTitle = `Held Enter receipt ${seed}`
  const capture = await createCaptureItem(request, auth, boardId, `- [ ] ${cardTitle}`)
  await triageCaptureItem(request, auth, capture.id)
  const triaged = await waitForProposalCreated(request, auth, capture.id)
  const proposalId = triaged.provenance?.proposalId
  expect(proposalId).toBeTruthy()

  await assertOk(
    await request.post(
      `${API_BASE_URL}/automation/proposals/${encodeURIComponent(proposalId!)}/approve`,
      { headers: { Authorization: `Bearer ${auth.token}` } },
    ),
    `approve held-Enter proposal ${proposalId}`,
  )

  await page.goto(`/workspace/review?boardId=${boardId}`)
  const requestBatch = page.getByTestId('queue-batch-execute')
  await expect(requestBatch).toBeVisible({ timeout: 15_000 })
  await requestBatch.click()

  const confirm = page.getByTestId('batch-execute-confirm')
  await expect(confirm).toBeVisible()
  await confirm.focus()
  await expect(confirm).toBeFocused()

  const executeResponse = page.waitForResponse((response) =>
    response.request().method() === 'POST'
    && response.url().endsWith('/automation/proposals/execute'))
  await page.keyboard.down('Enter')
  await assertOk(await executeResponse, 'batch execute held-Enter proposal')

  const receipts = page.getByTestId('batch-execute-receipts')
  const done = page.getByTestId('batch-execute-done')
  await expect(receipts).toBeVisible()
  await expect(page.getByTestId('batch-execute-receipt-summary')).toContainText('Applied 1')
  await expect(done).toBeFocused()

  // A second keydown without keyup is native auto-repeat. It must not activate
  // the newly focused Done button and discard the only durable receipt record.
  await page.keyboard.down('Enter')
  await expect(receipts).toBeVisible()
  await expect(done).toBeFocused()

  await page.keyboard.up('Enter')
  await page.keyboard.press('Enter')
  await expect(receipts).toHaveCount(0)
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
  await expect(page.getByTestId('paper-review-decision-receipt')).toHaveAttribute(
    'data-decision',
    'approved',
  )

  const executeResponse = page.waitForResponse((response) =>
    response.request().method() === 'POST'
    && response.url().endsWith(`/automation/proposals/${proposalId}/execute`))
  // The approved receipt keeps board execution behind a second explicit action.
  await expectApplyConfirmDialog(page, () => page.getByTestId('decision-apply').click())
  await assertOk(await executeResponse, `execute proposal ${proposalId}`)
  await expect(queueItem).toHaveCount(0)

  await expect(
    page
      .locator('.paper-review-recent__row')
      .filter({ hasText: proposalSerial(proposalId as string) })
      .filter({ hasText: cardTitle }),
  ).toBeVisible({ timeout: 10_000 })
})
