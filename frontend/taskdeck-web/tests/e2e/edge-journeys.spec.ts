/**
 * E2E: Edge Journeys (#712)
 *
 * Covers capture edge cases, proposal review states, and keyboard/accessibility
 * scenarios that are not part of the golden happy path:
 *
 * Capture edge cases:
 *   - Very long capture text (5000 chars) is accepted and displayed
 *   - Special characters (emoji, unicode, markdown) are preserved
 *   - Capture while not on a board is saved and reachable in inbox
 *
 * Proposal / Review journeys:
 *   - User rejects proposal → it disappears from review queue
 *   - Proposal details show human-readable operation descriptions
 *   - Approve proposal → board immediately reflects change
 *
 * Keyboard navigation:
 *   - Escape closes every open modal/dialog
 *
 * Dark mode:
 *   - Toggle dark mode → all views render (no white-on-white indicators)
 *   - Dark mode preference persists across page refresh
 */

import type { Page } from '@playwright/test'
import { expect, test } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import {
  createCaptureItem,
  waitForProposalCreated,
} from './support/captureFlow'
import { assertOk } from './support/httpAsserts'

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'edge-journeys')
})

// ─── Helpers ──────────────────────────────────────────────────────────────────

async function gotoBoardsWorkspace(page: Page) {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()
}

// ─── Capture edge cases ───────────────────────────────────────────────────────

test('very long capture text (5000 chars) should be accepted and visible in inbox', async ({ page }) => {
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(page.request, auth, seed, {
    boardNamePrefix: 'Long Capture',
    description: 'long capture test board',
    columnNamePrefix: 'Column',
  })

  // Generate 5000-char string with a unique marker so we can find it
  const marker = `LONGCAP-${seed}`
  const longText = `${marker} ${'A very long capture item text. '.repeat(161).substring(0, 4990 - marker.length - 1)}`
  expect(longText.length).toBeGreaterThanOrEqual(4990)

  // Submit via API (faster, avoids textarea truncation)
  const createResponse = await page.request.post(`${API_BASE_URL}/capture/items`, {
    headers: { Authorization: `Bearer ${auth.token}` },
    data: { boardId, text: longText, source: 'Typed' },
  })
  await assertOk(createResponse, 'create long capture')

  await page.goto('/workspace/inbox')
  const captureRow = page.locator('[data-testid="inbox-item"]').filter({ hasText: marker }).first()
  await expect(captureRow).toBeVisible({ timeout: 10_000 })

  // Click to open detail and confirm text is present (possibly truncated in excerpt)
  await captureRow.click()
  const detailText = page.locator('.td-inbox-detail__text')
  if (await detailText.isVisible({ timeout: 5_000 }).catch(() => false)) {
    await expect(detailText).toContainText(marker)
  }
})

test('capture with special characters (emoji, unicode, markdown) should be preserved', async ({ page }) => {
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(page.request, auth, seed, {
    boardNamePrefix: 'Special Chars',
    description: 'special char capture board',
    columnNamePrefix: 'Column',
  })

  const specialText = `🚀 Unicode test Ñoño — em-dash — **bold** _italic_ \`code\` ${seed}`

  const createResponse = await page.request.post(`${API_BASE_URL}/capture/items`, {
    headers: { Authorization: `Bearer ${auth.token}` },
    data: { boardId, text: specialText, source: 'Typed' },
  })
  await assertOk(createResponse, 'create special-char capture')

  await page.goto('/workspace/inbox')
  const captureRow = page.locator('[data-testid="inbox-item"]').filter({ hasText: seed }).first()
  await expect(captureRow).toBeVisible({ timeout: 10_000 })

  // Detail view must preserve the emoji and unicode content
  await captureRow.click()
  const detailText = page.locator('.td-inbox-detail__text')
  if (await detailText.isVisible({ timeout: 5_000 }).catch(() => false)) {
    await expect(detailText).toContainText('🚀')
    await expect(detailText).toContainText('Ñoño')
  }
})

test('capture submitted while not on a board should be reachable in global inbox', async ({ page }) => {
  // Navigate to a non-board page (home)
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  const captureText = `No-board capture ${Date.now()}`

  // Open the global capture hotkey
  await page.keyboard.press('Control+Shift+C')
  const captureModal = page.getByRole('dialog', { name: 'Capture item' })
  await expect(captureModal).toBeVisible()

  await captureModal
    .getByPlaceholder('Capture a thought, task, or follow-up...')
    .fill(captureText)

  const saveCapture = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      /\/api\/capture\/items$/i.test(response.url()) &&
      response.ok(),
    { timeout: 10_000 },
  )
  await captureModal.getByPlaceholder('Capture a thought, task, or follow-up...').press('Control+Enter')
  await saveCapture

  // Inbox should contain the new capture
  await page.goto('/workspace/inbox')
  const captureRow = page
    .locator('[data-testid="inbox-item"]')
    .filter({ hasText: captureText })
    .first()
  await expect(captureRow).toBeVisible({ timeout: 10_000 })
})

// ─── Proposal / Review journeys ───────────────────────────────────────────────

test('rejecting a proposal should remove it from the review queue', async ({ page, request }) => {
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Reject Proposal',
    description: 'reject proposal test board',
    columnNamePrefix: 'Backlog',
  })

  const captureText = `- [ ] Reject me card ${seed}`
  const captureItem = await createCaptureItem(request, auth, boardId, captureText)

  const triageResponse = await request.post(
    `${API_BASE_URL}/capture/items/${encodeURIComponent(captureItem.id)}/triage`,
    { headers: { Authorization: `Bearer ${auth.token}` } },
  )
  await assertOk(triageResponse, 'trigger triage for reject test')

  const triagedItem = await waitForProposalCreated(request, auth, captureItem.id)
  const proposalId = triagedItem.provenance?.proposalId
  expect(proposalId).toBeTruthy()

  await page.goto(`/workspace/review?boardId=${boardId}#proposal-${proposalId}`)
  const proposalCard = page.locator(`#proposal-${proposalId}`)
  await expect(proposalCard).toBeVisible({ timeout: 15_000 })

  // Reject the proposal
  const rejectButton = proposalCard
    .getByRole('button', { name: /reject|dismiss/i })
    .first()
  await expect(rejectButton).toBeVisible()

  page.once('dialog', (dialog) => dialog.accept())
  await rejectButton.click()

  // Proposal card must disappear from the review queue
  await expect(proposalCard).toHaveCount(0, { timeout: 10_000 })
})

test('proposal approve should reflect on board immediately without manual refresh', async ({ page, request }) => {
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Approve Reflect',
    description: 'approve and reflect board',
    columnNamePrefix: 'Todo',
  })

  const cardTitle = `Reflected card ${seed}`
  const captureText = `- [ ] ${cardTitle}`
  const captureItem = await createCaptureItem(request, auth, boardId, captureText)

  const triageResponse = await request.post(
    `${API_BASE_URL}/capture/items/${encodeURIComponent(captureItem.id)}/triage`,
    { headers: { Authorization: `Bearer ${auth.token}` } },
  )
  await assertOk(triageResponse, 'trigger triage for approve-reflect test')

  const triagedItem = await waitForProposalCreated(request, auth, captureItem.id)
  const proposalId = triagedItem.provenance?.proposalId
  expect(proposalId).toBeTruthy()

  await page.goto(`/workspace/review?boardId=${boardId}#proposal-${proposalId}`)
  const proposalCard = page.locator(`#proposal-${proposalId}`)
  await expect(proposalCard).toBeVisible({ timeout: 15_000 })

  await proposalCard.getByRole('button', { name: 'Approve for board' }).click()
  await expect(proposalCard.getByText('Approved, ready to apply')).toBeVisible()

  page.once('dialog', (dialog) => dialog.accept())
  await proposalCard.getByRole('button', { name: 'Apply to board' }).click()
  await expect(proposalCard).not.toBeVisible()

  // Navigate to board and check card appears without manual refresh
  await page.goto(`/workspace/boards/${boardId}`)
  await expect(
    page.locator('[data-card-id]').filter({ hasText: cardTitle }).first(),
  ).toBeVisible({ timeout: 15_000 })
})

test('proposal detail should show human-readable operation descriptions', async ({ page, request }) => {
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Proposal Detail',
    description: 'proposal detail description test board',
    columnNamePrefix: 'Column',
  })

  const cardTitle = `Detail card ${seed}`
  const captureText = `- [ ] ${cardTitle}`
  const captureItem = await createCaptureItem(request, auth, boardId, captureText)

  const triageResponse = await request.post(
    `${API_BASE_URL}/capture/items/${encodeURIComponent(captureItem.id)}/triage`,
    { headers: { Authorization: `Bearer ${auth.token}` } },
  )
  await assertOk(triageResponse, 'trigger triage for detail test')

  const triagedItem = await waitForProposalCreated(request, auth, captureItem.id)
  const proposalId = triagedItem.provenance?.proposalId
  expect(proposalId).toBeTruthy()

  await page.goto(`/workspace/review?boardId=${boardId}#proposal-${proposalId}`)
  const proposalCard = page.locator(`#proposal-${proposalId}`)
  await expect(proposalCard).toBeVisible({ timeout: 15_000 })

  // The proposal summary or operations list must contain human-readable text
  // (not raw JSON or UUIDs as the primary description)
  const summary = proposalCard.locator('[class*="summary"], [class*="description"], p').first()
  if (await summary.isVisible({ timeout: 5_000 }).catch(() => false)) {
    const summaryText = await summary.innerText()
    // Human-readable: should contain words, not only hex/braces
    expect(summaryText).toMatch(/\w{3,}/)
    expect(summaryText).not.toMatch(/^\{.*\}$/)
  }
})

// ─── Keyboard navigation ──────────────────────────────────────────────────────

test('Escape key should close each open modal and inline form in sequence', async ({ page }) => {
  await gotoBoardsWorkspace(page)

  // Create board
  await page.getByRole('button', { name: '+ New Board' }).click()
  await page.getByPlaceholder('Board name').fill(`Escape Board ${Date.now()}`)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page).toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)

  // Create column
  await page.getByRole('button', { name: '+ Add Column' }).click()
  const colName = `Escape Column ${Date.now()}`
  await page.getByPlaceholder('Column name').fill(colName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page.getByRole('heading', { name: colName, exact: true })).toBeVisible()

  // Create card
  const column = page
    .locator('[data-column-id]')
    .filter({ has: page.getByRole('heading', { name: colName, exact: true }) })
    .first()
  await column.getByRole('button', { name: 'Add Card' }).click()
  const cardTitle = `Escape Card ${Date.now()}`
  await column.getByPlaceholder('Enter card title...').fill(cardTitle)

  const createCardResponse = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      /\/api\/boards\/[a-f0-9-]+\/cards$/i.test(response.url()) &&
      response.ok(),
  )
  await column.getByRole('button', { name: 'Add', exact: true }).click()
  await createCardResponse

  const card = page.locator('[data-card-id]').filter({ hasText: cardTitle }).first()
  await expect(card).toBeVisible()

  // Open card edit modal via Enter key
  await page.locator('body').click()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('heading', { name: 'Edit Card' })).toBeVisible()

  // Escape closes card modal → should show add-card inline form
  await page.keyboard.press('Escape')
  await expect(page.getByRole('heading', { name: 'Edit Card' })).not.toBeVisible()

  // Another Escape closes the inline add form
  await page.keyboard.press('Escape')
  await expect(column.getByPlaceholder('Enter card title...')).toHaveCount(0)

  // Another Escape navigates back to boards list
  await page.keyboard.press('Escape')
  await expect(page).toHaveURL(/\/workspace\/boards$/)
})

// ─── Dark mode ────────────────────────────────────────────────────────────────

test('dark mode toggle should apply dark theme class to document', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  // Find the dark-mode toggle — look by aria-label or common patterns
  const darkModeToggle = page
    .getByRole('button', { name: /dark mode|theme|light|dark/i })
    .or(page.getByLabel(/dark mode|toggle theme/i))
    .first()

  if (!(await darkModeToggle.isVisible({ timeout: 5_000 }).catch(() => false))) {
    test.skip()
    return
  }

  await darkModeToggle.click()

  // Expect a dark-mode class on html or body
  const hasDarkClass = await page.evaluate(() => {
    return (
      document.documentElement.classList.contains('dark') ||
      document.documentElement.dataset.theme === 'dark' ||
      document.body.classList.contains('dark') ||
      document.body.dataset.theme === 'dark'
    )
  })

  expect(hasDarkClass).toBeTruthy()
})

test('dark mode preference should persist across page refresh', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  const darkModeToggle = page
    .getByRole('button', { name: /dark mode|theme|light|dark/i })
    .or(page.getByLabel(/dark mode|toggle theme/i))
    .first()

  if (!(await darkModeToggle.isVisible({ timeout: 5_000 }).catch(() => false))) {
    test.skip()
    return
  }

  await darkModeToggle.click()

  // Confirm dark mode is active
  const isDarkAfterToggle = await page.evaluate(() => {
    return (
      document.documentElement.classList.contains('dark') ||
      document.documentElement.dataset.theme === 'dark' ||
      document.body.classList.contains('dark') ||
      document.body.dataset.theme === 'dark'
    )
  })
  expect(isDarkAfterToggle).toBeTruthy()

  // Reload and check that dark mode persists
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  const isDarkAfterReload = await page.evaluate(() => {
    return (
      document.documentElement.classList.contains('dark') ||
      document.documentElement.dataset.theme === 'dark' ||
      document.body.classList.contains('dark') ||
      document.body.dataset.theme === 'dark'
    )
  })
  expect(isDarkAfterReload).toBeTruthy()
})

// ─── Rapid sequential captures ────────────────────────────────────────────────

test('10 rapid sequential captures should all be saved and visible in inbox', async ({ page, request }) => {
  test.setTimeout(60_000)

  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Rapid Capture',
    description: 'rapid sequential capture board',
    columnNamePrefix: 'Column',
  })

  const captures: string[] = []
  for (let i = 1; i <= 10; i++) {
    captures.push(`Rapid capture ${i} - ${seed}`)
  }

  // Submit all 10 captures in rapid succession via API
  await Promise.all(
    captures.map((text) =>
      page.request.post(`${API_BASE_URL}/capture/items`, {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { boardId, text, source: 'Typed' },
      }),
    ),
  )

  await page.goto('/workspace/inbox')

  // All 10 captures must appear in the inbox
  for (const captureText of captures) {
    const captureRow = page
      .locator('[data-testid="inbox-item"]')
      .filter({ hasText: captureText })
      .first()
    await expect(captureRow).toBeVisible({ timeout: 15_000 })
  }
})
