import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'
import { addCard, addColumn, columnByName, createBoard } from './support/boardUiHelpers'

/**
 * Cross-browser E2E tests.
 *
 * These tests run on all desktop browser projects (Chromium, Firefox, WebKit)
 * and validate that critical user journeys work consistently across engines.
 *
 * Tag: @cross-browser — filtered by project grep in playwright.config.ts.
 * On Chromium these also run alongside the regular suite (PR gate includes
 * @cross-browser tests; be mindful of count to avoid slowing PR feedback).
 */

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'xbrowser')
})

// ---------------------------------------------------------------------------
// Tests — critical journeys that must work identically across browsers
// ---------------------------------------------------------------------------

test('@cross-browser board creation and card workflow', async ({ page }) => {
  const boardName = `XB Board ${Date.now()}`
  const columnName = `XB Col ${Date.now()}`
  const cardTitle = `XB Card ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, columnName)
  await addCard(page, columnName, cardTitle)

  // Card should be visible in the correct column
  const column = columnByName(page, columnName)
  await expect(column.locator('[data-card-id]').filter({ hasText: cardTitle }).first()).toBeVisible()

  // Page reload should persist
  await page.reload()
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()
  const reloadedColumn = columnByName(page, columnName)
  await expect(
    reloadedColumn.locator('[data-card-id]').filter({ hasText: cardTitle }).first(),
  ).toBeVisible()
})

test('@cross-browser workspace navigation between views', async ({ page }) => {
  // Home
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  // Boards
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

  // Inbox
  await page.goto('/workspace/inbox')
  await expect(page).toHaveURL(/\/workspace\/inbox$/)

  // Back to Home
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()
})

test('@cross-browser card edit modal open and close', async ({ page }) => {
  const boardName = `XB Edit Board ${Date.now()}`
  const columnName = `XB Edit Col ${Date.now()}`
  const cardTitle = `XB Edit Card ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, columnName)
  await addCard(page, columnName, cardTitle)

  // Click card to open edit modal
  const card = page.locator('[data-card-id]').filter({ hasText: cardTitle }).first()
  await card.click()
  await expect(page.getByRole('heading', { name: 'Edit Card' })).toBeVisible()

  // Close with Escape
  await page.keyboard.press('Escape')
  await expect(page.getByRole('heading', { name: 'Edit Card' })).not.toBeVisible()

  // Card should still be visible after closing modal
  await expect(
    page.locator('[data-card-id]').filter({ hasText: cardTitle }).first(),
  ).toBeVisible()
})

test('@cross-browser capture hotkey submits and routes to inbox', async ({ page }) => {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

  const captureText = `XB Capture ${Date.now()}`

  await page.keyboard.press('Control+Shift+C')
  const captureModal = page.getByRole('dialog', { name: 'Capture item' })
  await expect(captureModal).toBeVisible()

  await captureModal.getByPlaceholder('Capture a thought, task, or follow-up...').fill(captureText)
  await captureModal.getByPlaceholder('Capture a thought, task, or follow-up...').press('Control+Enter')

  await expect(page).toHaveURL(/\/workspace\/inbox$/)
  await expect(page.locator('.td-inbox-row__excerpt').first()).toContainText(captureText)
})

test('@cross-browser filter panel toggle with keyboard shortcut', async ({ page }) => {
  const boardName = `XB Filter Board ${Date.now()}`

  await createBoard(page, boardName)

  // Open filter panel with 'f' key
  await page.keyboard.press('f')
  await expect(page.getByRole('heading', { name: 'Filter Cards' })).toBeVisible()

  // Close with 'f' key
  await page.keyboard.press('f')
  await expect(page.getByRole('heading', { name: 'Filter Cards' })).not.toBeVisible()
})
