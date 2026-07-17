/**
 * E2E: Integrated Multi-Component Verification (TST-12, #135)
 *
 * Cross-component automated journeys that validate subsystem interactions
 * end-to-end. Each test crosses at least two subsystem boundaries as defined
 * in docs/testing/INTEGRATED_VERIFICATION_STRATEGY.md.
 *
 * Journey 1 (V-02): Full capture-to-board pipeline
 *   Auth (S2) -> Board (S1) -> Capture/Automation (S3) -> Board (S1) -> Audit
 *
 * Journey 2 (V-03): Board bootstrap and management
 *   Auth (S2) -> Board (S1) -> Starter Pack (S5) -> Archive (S5) -> Restore (S5)
 *
 * Journey 3 (V-06): Workspace exploration and navigation coherence
 *   Auth (S2) -> Home -> Today -> Review -> Board (S1) -> Archive (S5) -> Metrics (S5)
 */

import { expect, test } from '@playwright/test'
import type { StarterPackManifest } from '../../src/types/starter-packs'
import { API_BASE_URL, registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { createCaptureItem, waitForProposalCreated, waitForCardWithTitle, listBoardCards } from './support/captureFlow'

/**
 * Minimal starter pack manifest for V-03 integration test.
 * Uses the actual API contract: ApplyStarterPackDto expects { manifest, dryRun }.
 */
const ENGINEERING_SPRINT_MANIFEST: StarterPackManifest = {
  schemaVersion: '1.0',
  packId: 'engineering-sprint',
  displayName: 'Engineering Sprint',
  description: 'Board layout for engineering sprint workflows.',
  compatibility: {
    minTaskdeckVersion: '1.0.0',
    requiredFeatures: ['boards', 'labels'],
  },
  tags: ['engineering', 'sprint'],
  labels: [
    { name: 'bug', color: '#DC2626', description: 'Defect tracking' },
    { name: 'feature', color: '#2563EB', description: 'New feature work' },
  ],
  columns: [
    { name: 'Backlog', position: 0 },
    { name: 'In Progress', position: 1, wipLimit: 3 },
    { name: 'Review', position: 2, wipLimit: 2 },
    { name: 'Done', position: 3 },
  ],
  templates: [],
  seedCards: [],
}

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'integrated-verification', { theme: 'legacy' })
})

// ─── Journey 1 (V-02): Full capture-to-board pipeline ───────────────────────

test('V-02: register to create board to capture to triage to approve to board state', async ({ page, request }) => {
  test.setTimeout(90_000)

  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const cardTitle = `Integration card ${seed}`
  const captureText = `- [ ] ${cardTitle}`

  // Step 1: Verify authenticated workspace access
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  // Step 2: Create board with column via API (crosses S2 auth + S1 board)
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Integration Board',
    description: 'integrated verification test board',
    columnNamePrefix: 'Backlog',
  })

  // Step 3: Create capture item via API (crosses S3 capture + S1 board context)
  const captureItem = await createCaptureItem(request, auth, boardId, captureText)
  const captureId = captureItem.id

  // Step 4: Verify inbox shows the capture (S3 inbox UI)
  await page.goto(`/workspace/inbox?boardId=${boardId}`)
  await expect(page.getByRole('heading', { name: 'Inbox', exact: true })).toBeVisible()
  const captureRow = page.locator('[data-testid="inbox-item"]').filter({ hasText: cardTitle }).first()
  await expect(captureRow).toBeVisible()

  // Step 5: Triage the capture to create a proposal (S3 automation)
  await captureRow.click()
  const triageButton = page.locator('.td-inbox-detail__actions button').filter({ hasText: 'Start Triage' }).first()
  await expect(triageButton).toBeVisible()
  await triageButton.click()

  // Step 6: Wait for proposal creation (S3 automation pipeline)
  const triagedCapture = await waitForProposalCreated(request, auth, captureId)
  const proposalId = triagedCapture.provenance?.proposalId
  expect(proposalId).toBeTruthy()

  // Step 7: Verify board has no cards yet (review-first: no silent mutation)
  const cardsBeforeApproval = await listBoardCards(request, auth, boardId)
  expect(cardsBeforeApproval.length).toBe(0)

  // Step 8: Navigate to review and approve proposal (S3 review + S1 board target)
  await page.goto(`/workspace/review?boardId=${boardId}#proposal-${proposalId}`)
  const proposalCard = page.locator(`#proposal-${proposalId}`)
  await expect(proposalCard).toBeVisible({ timeout: 15_000 })

  await proposalCard.getByRole('button', { name: 'Approve for board' }).click()
  await expect(proposalCard.getByText('Approved, ready to apply')).toBeVisible()

  // Step 9: Apply the approved proposal (S1 board mutation)
  page.once('dialog', (dialog) => dialog.accept())
  await proposalCard.getByRole('button', { name: 'Apply to board' }).click()
  await expect(proposalCard).not.toBeVisible()

  // Step 10: Verify card appeared on the board (S1 board state)
  const createdCard = await waitForCardWithTitle(request, auth, boardId, cardTitle)
  await page.goto(`/workspace/boards/${boardId}`)
  const card = page.locator('[data-card-id]').filter({ hasText: createdCard.title }).first()
  await expect(card).toBeVisible()

  // Step 11: Verify provenance links (audit trail across S1 + S3)
  await card.getByRole('heading', { name: createdCard.title, exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Edit Card' })).toBeVisible()
  await expect(page.getByText('Capture Origin')).toBeVisible()
  await expect(page.getByRole('link', { name: 'Open Capture' })).toHaveAttribute(
    'href',
    `/workspace/inbox?boardId=${boardId}#capture-${captureId}`,
  )
  await expect(page.getByRole('link', { name: 'Open Proposal' })).toHaveAttribute(
    'href',
    `/workspace/review?boardId=${boardId}#proposal-${proposalId}`,
  )
})

// ─── Journey 2 (V-03): Board bootstrap and management ───────────────────────

test('V-03: login to create board to apply starter pack to archive to restore', async ({ page, request }) => {
  test.setTimeout(90_000)

  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardName = `Starter Pack Board ${seed}`

  // Step 1: Navigate to workspace and create a board via UI (S2 auth + S1 board)
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

  await page.getByRole('button', { name: '+ New Board' }).click()
  await page.getByPlaceholder('Board name').fill(boardName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page).toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()
  const boardUrl = page.url()
  const boardIdMatch = /\/workspace\/boards\/([a-f0-9-]+)$/.exec(boardUrl)
  expect(boardIdMatch).toBeTruthy()
  const boardId = boardIdMatch![1]

  // Step 2: Apply a starter pack via API (S5 starter packs + S1 board)
  // Uses a fixture manifest matching the ApplyStarterPackDto contract: { manifest, dryRun }
  const applyResponse = await request.post(
    `${API_BASE_URL}/boards/${encodeURIComponent(boardId)}/starter-packs/apply`,
    {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { manifest: ENGINEERING_SPRINT_MANIFEST, dryRun: false },
    },
  )

  // Assert the apply succeeded -- do not silently swallow failures
  expect(
    applyResponse.ok(),
    `Starter pack apply should succeed (got ${applyResponse.status()}: ${await applyResponse.text()})`,
  ).toBeTruthy()

  const applyResult = await applyResponse.json()
  expect(applyResult.applied).toBeTruthy()

  // Step 3: Verify board has columns from the starter pack
  await page.reload()
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()
  // Starter pack should have added at least one column
  const columnHeadings = page.locator('[data-column-dnd-id] h3')
  const columnCount = await columnHeadings.count()
  expect(columnCount).toBeGreaterThan(0)

  // Step 4: Archive the board via Board Settings (S5 archive)
  await page.locator('button[title="Board Settings"]').click()
  page.once('dialog', (dialog) => dialog.accept())
  await page.getByRole('button', { name: 'Move to Archive' }).click()

  // Step 5: Verify board is no longer in boards list (S1 board list)
  await expect(page).toHaveURL(/\/workspace\/boards$/)
  await expect(page.getByText(boardName)).toHaveCount(0)

  // Step 6: Verify board appears in archive view (S5 archive)
  await page.goto('/workspace/archive')
  await expect(page.getByRole('heading', { name: 'Archive', exact: true })).toBeVisible()
  const archivedBoardRow = page.locator('.td-archive-row').filter({ hasText: boardName }).first()
  await expect(archivedBoardRow).toBeVisible()

  // Step 7: Restore the board from archive (S5 restore)
  page.once('dialog', (dialog) => dialog.accept())
  await archivedBoardRow.getByRole('button', { name: 'Restore Board' }).click()
  await expect(page.locator('.td-archive-row').filter({ hasText: boardName })).toHaveCount(0)

  // Step 8: Verify restored board is back in boards list (S1 board)
  await page.goto('/workspace/boards')
  await expect(page.getByText(boardName).first()).toBeVisible()

  // Step 9: Verify board content survived the archive/restore cycle (S1 board state)
  await page.goto(boardUrl)
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()
  const restoredColumnCount = await page.locator('[data-column-dnd-id] h3').count()
  expect(restoredColumnCount).toBeGreaterThan(0)
})

// ─── Journey 3 (V-06): Workspace exploration and navigation coherence ───────

test('V-06: workspace navigation coherence across home, today, review, board, archive, metrics', async ({ page, request }) => {
  test.setTimeout(60_000)

  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`

  // Step 1: Create a board with content via API for richer navigation
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Nav Coherence Board',
    description: 'workspace navigation coherence test',
    columnNamePrefix: 'Backlog',
  })

  // Step 2: Navigate to Home (S2 auth + S1 workspace shell)
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  // Step 3: Navigate to Today (S5 today view)
  await page.goto('/workspace/today')
  await expect(page.getByRole('heading', { name: 'Today', exact: true })).toBeVisible()

  // Step 4: Navigate to Review (S3 review view)
  await page.goto('/workspace/review')
  await expect(page.getByRole('heading', { name: 'Review', exact: true })).toBeVisible()

  // Step 5: Navigate to the created board (S1 board view)
  await page.goto(`/workspace/boards/${boardId}`)
  const boardHeading = page.getByRole('heading', { level: 1 })
  await expect(boardHeading).toBeVisible()
  const boardName = await boardHeading.textContent()
  expect(boardName).toContain('Nav Coherence Board')

  // Step 6: Navigate to Inbox (S3 inbox view)
  await page.goto('/workspace/inbox')
  await expect(page.getByRole('heading', { name: 'Inbox', exact: true })).toBeVisible()

  // Step 7: Navigate to Archive (S5 archive view)
  await page.goto('/workspace/archive')
  await expect(page.getByRole('heading', { name: 'Archive', exact: true })).toBeVisible()

  // Step 8: Navigate to Metrics (S5 metrics view)
  await page.goto('/workspace/metrics')
  await expect(page.getByRole('heading', { name: /Metrics/i })).toBeVisible()

  // Step 9: Navigate to Activity (S5 activity view)
  await page.goto('/workspace/activity')
  await expect(page.getByRole('heading', { name: 'Activity', exact: true })).toBeVisible()

  // Step 10: Navigate back to Home and verify no stale state
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  // Step 11: Verify board-scoped navigation context is maintained
  await page.goto(`/workspace/inbox?boardId=${boardId}`)
  await expect(page.getByRole('heading', { name: 'Inbox', exact: true })).toBeVisible()
  await expect(page.getByText(`Showing capture items linked to board ${boardId}.`)).toBeVisible()

  // Step 12: Verify board-scoped review navigation
  await page.goto(`/workspace/review?boardId=${boardId}`)
  await expect(page.getByRole('heading', { name: 'Review', exact: true })).toBeVisible()
})

// ─── Journey 4 (V-04): Unauthenticated access denial across endpoints ───────

test('V-04: unauthenticated access should be denied across all protected endpoint families', async ({ request }) => {
  // This test verifies that the auth boundary is enforced across multiple
  // subsystem endpoints without any session token. Each request should
  // return 401 with the GP-03 error contract.

  const unauthenticatedEndpoints = [
    { method: 'GET', path: '/boards', subsystem: 'S1 Board' },
    { method: 'GET', path: '/capture/items', subsystem: 'S3 Capture' },
    { method: 'GET', path: '/automation/proposals', subsystem: 'S3 Proposals' },
    { method: 'GET', path: '/llm/chat/sessions', subsystem: 'S3 Chat' },
    { method: 'GET', path: '/archive/items', subsystem: 'S5 Archive' },
    { method: 'GET', path: '/notifications', subsystem: 'S4 Notifications' },
    { method: 'GET', path: '/ops/cli/templates', subsystem: 'S4 Ops' },
    { method: 'GET', path: '/account/export', subsystem: 'S2 Account' },
  ]

  for (const endpoint of unauthenticatedEndpoints) {
    const response = await request.fetch(`${API_BASE_URL}${endpoint.path}`, {
      method: endpoint.method,
      // Explicitly no Authorization header
      headers: {},
    })

    expect(
      response.status(),
      `${endpoint.subsystem}: ${endpoint.method} ${endpoint.path} should return 401`,
    ).toBe(401)

    const body = await response.json()
    expect(body).toHaveProperty('errorCode')
    expect(body).toHaveProperty('message')
    expect(body.message).toBeTruthy()
  }
})
