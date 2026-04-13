/**
 * E2E: Capture Edge Cases
 *
 * Extends capture coverage with UI-driven scenarios beyond the API-level
 * edge-journey tests:
 * - UI capture from the global hotkey with empty text is rejected
 * - UI capture from the board action rail linked to a board
 * - Capture modal can be dismissed without saving
 * - Capture with only whitespace is rejected
 */

import type { Page } from '@playwright/test'
import { expect, test } from '@playwright/test'
import { registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'capture-edge')
})

// --- Helper ---

async function openCaptureModalViaHotkey(page: Page) {
  await page.keyboard.press('Control+Shift+C')
  const captureModal = page.getByRole('dialog', { name: 'Capture item' })
  await expect(captureModal).toBeVisible()
  return captureModal
}

// --- Empty text rejection ---

test('capture modal should not submit empty text', async ({ page }) => {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

  const captureModal = await openCaptureModalViaHotkey(page)
  const inputField = captureModal.getByPlaceholder('Capture a thought, task, or follow-up...')

  // Leave text empty and try to submit
  await inputField.fill('')
  await inputField.press('Control+Enter')

  // Modal should remain open (no navigation to inbox)
  await expect(captureModal).toBeVisible()
  await expect(page).not.toHaveURL(/\/workspace\/inbox/)
})

test('capture modal should not submit whitespace-only text', async ({ page }) => {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

  const captureModal = await openCaptureModalViaHotkey(page)
  const inputField = captureModal.getByPlaceholder('Capture a thought, task, or follow-up...')

  // Fill with only whitespace
  await inputField.fill('   \n  \t  ')
  await inputField.press('Control+Enter')

  // Modal should remain open (whitespace-only should be rejected)
  await expect(captureModal).toBeVisible()
  await expect(page).not.toHaveURL(/\/workspace\/inbox/)
})

// --- Capture modal dismissal ---

test('capture modal should close without saving when pressing Escape', async ({ page }) => {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

  const captureModal = await openCaptureModalViaHotkey(page)

  // Type something but do not submit
  await captureModal
    .getByPlaceholder('Capture a thought, task, or follow-up...')
    .fill('This text should not be saved')

  // Press Escape to dismiss
  await page.keyboard.press('Escape')

  // Modal should close
  await expect(captureModal).toHaveCount(0)

  // Navigate to inbox to verify nothing was saved
  await page.goto('/workspace/inbox')
  await expect(page.getByRole('heading', { name: 'Inbox', exact: true })).toBeVisible()
  await expect(page.locator('[data-testid="inbox-item"]').filter({ hasText: 'This text should not be saved' })).toHaveCount(0)
})

// --- Capture from board action rail ---

test('capture from board action rail should link capture to that board', async ({ page, request }) => {
  test.setTimeout(60_000)

  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Rail Capture',
    description: 'board for rail capture test',
    columnNamePrefix: 'Column',
  })
  const boardName = `Rail Capture ${seed}`

  await page.goto(`/workspace/boards/${boardId}`)
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()

  const boardActionRail = page.locator('[data-board-action-rail]')
  await expect(boardActionRail.getByRole('button', { name: 'Capture here' })).toBeVisible()

  await boardActionRail.getByRole('button', { name: 'Capture here' }).click()
  const captureModal = page.getByRole('dialog', { name: 'Capture item' })
  await expect(captureModal).toBeVisible()

  // The capture modal should indicate it is linked to this board
  await expect(captureModal.getByText(boardName)).toBeVisible()

  const captureText = `Rail-linked capture ${seed}`
  const createCaptureResponse = page.waitForResponse((response) =>
    response.request().method() === 'POST'
    && /\/api\/capture\/items$/i.test(response.url())
    && response.ok())

  await captureModal.getByPlaceholder('Capture a thought, task, or follow-up...').fill(captureText)
  await captureModal.getByRole('button', { name: 'Save Capture' }).click()
  await createCaptureResponse
  await expect(captureModal).toHaveCount(0)

  // Navigate to board-filtered inbox and verify the capture is there
  await page.goto(`/workspace/inbox?boardId=${boardId}`)
  await expect(page.getByRole('heading', { name: 'Inbox', exact: true })).toBeVisible()
  const captureRow = page.locator('[data-testid="inbox-item"]').filter({ hasText: captureText }).first()
  await expect(captureRow).toBeVisible({ timeout: 15_000 })
})
