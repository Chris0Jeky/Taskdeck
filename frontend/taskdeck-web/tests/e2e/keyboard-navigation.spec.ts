/**
 * E2E: Keyboard Navigation Scenarios
 *
 * Covers keyboard-driven workflows:
 * - Board creation via keyboard, column creation, and card creation using 'n' shortcut
 * - Command palette arrow-key navigation and Enter selection
 * - Escape from command palette closes it and returns to the prior view
 * - Question-mark shortcut toggles keyboard shortcuts help overlay
 */

import type { Page } from '@playwright/test'
import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'keyboard-nav')
})

// --- Helper ---

function columnByName(page: Page, columnName: string) {
  return page
    .locator('[data-column-id]')
    .filter({ has: page.getByRole('heading', { name: columnName, exact: true }) })
    .first()
}

// --- Full keyboard-only board workflow ---

test('user should create board via keyboard then add card using n shortcut', async ({ page }) => {
  const seed = Date.now()
  const boardName = `KB Board ${seed}`
  const columnName = `KB Column ${seed}`
  const cardTitle = `KB Card ${seed}`

  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

  // Click the New Board button to open the form
  await page.getByRole('button', { name: '+ New Board' }).click()

  // Fill board name using keyboard and submit with Enter
  const boardNameInput = page.getByPlaceholder('Board name')
  await expect(boardNameInput).toBeVisible()
  await boardNameInput.fill(boardName)
  await page.keyboard.press('Enter')

  await expect(page).toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()

  // Create column by clicking the button and using keyboard for the form
  await page.getByRole('button', { name: '+ Add Column' }).click()

  const columnNameInput = page.getByPlaceholder('Column name')
  await expect(columnNameInput).toBeVisible()
  await columnNameInput.fill(columnName)
  await page.keyboard.press('Enter')
  await expect(page.getByRole('heading', { name: columnName, exact: true })).toBeVisible()

  // Create card using 'n' shortcut (keyboard-only card creation)
  // After column creation the column-name input is removed from the DOM, so
  // no text field should be capturing keystrokes. We click the board heading
  // to ensure focus is on a non-input element (pressing Escape here would
  // trigger closeOpenUi() which navigates away from the board).
  await page.getByRole('heading', { name: boardName }).click()
  await page.keyboard.press('n')
  const column = columnByName(page, columnName)
  const cardInput = column.getByPlaceholder('Enter card title...')
  await expect(cardInput).toBeVisible({ timeout: 10_000 })
  await cardInput.fill(cardTitle)

  const createCardResponse = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      /\/api\/boards\/[a-f0-9-]+\/cards$/i.test(response.url()) &&
      response.ok(),
  )
  await column.getByRole('button', { name: 'Add', exact: true }).click()
  await createCardResponse

  await expect(page.locator('[data-card-id]').filter({ hasText: cardTitle }).first()).toBeVisible()
})

// --- Command palette keyboard navigation ---

test('command palette should support arrow-key navigation and Enter selection', async ({ page }) => {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

  // Open command palette
  await page.keyboard.press('Control+K')
  const palette = page.getByRole('dialog', { name: 'Command palette' })
  await expect(palette).toBeVisible()

  const paletteInput = palette.getByPlaceholder('Type a command or search boards and cards...')
  await expect(paletteInput).toBeFocused()

  // Type a partial command -- "to" should match "Today" navigation command
  await paletteInput.fill('to')

  // Wait for at least one result item to appear before using arrow keys.
  // Without this, arrow keys and Enter operate on an empty list and the test
  // would vacuously pass when the palette closes for any reason.
  const resultItems = palette.locator('[role="option"]')
  await expect(resultItems.first()).toBeVisible({ timeout: 5_000 })

  // Navigate results with arrow keys
  await page.keyboard.press('ArrowDown')
  await page.keyboard.press('ArrowDown')
  await page.keyboard.press('ArrowUp')

  // Press Enter to activate the selected result
  await page.keyboard.press('Enter')

  // The palette should close after selection
  await expect(palette).toHaveCount(0)

  // Verify that selecting a command actually navigated somewhere.
  // The "to" query matches the "Today" navigation command, so we should
  // end up on /workspace/today (or at least no longer on /workspace/boards).
  await expect(page).not.toHaveURL(/\/workspace\/boards$/, { timeout: 5_000 })
})

// --- Escape from command palette ---

test('Escape from command palette should close it and return to the prior view', async ({ page }) => {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

  // Open command palette
  await page.keyboard.press('Control+K')
  const palette = page.getByRole('dialog', { name: 'Command palette' })
  await expect(palette).toBeVisible()

  // Escape should close the palette
  await page.keyboard.press('Escape')
  await expect(palette).toHaveCount(0)

  // We should still be on the boards page
  await expect(page).toHaveURL(/\/workspace\/boards/)
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()
})

// --- Shortcut help panel ---
// FIXME(#1129): keyboard shortcuts help overlay is not implemented yet.
// This test was silently skipping via a runtime check, producing a false-green
// signal. Converted to test.fixme so it surfaces as "pending" until the
// shortcuts help feature ships.

test.fixme('question mark shortcut should toggle keyboard shortcuts help', async ({ page }) => {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

  // Ensure no input is capturing keystrokes
  await page.keyboard.press('Escape')

  // Press ? to open shortcut help
  await page.keyboard.press('?')

  // Look for a shortcuts panel/dialog/overlay
  const shortcutsPanel = page
    .getByRole('dialog', { name: /shortcut|keyboard|help/i })
    .or(page.locator('[data-shortcuts-help]'))
    .or(page.getByText(/keyboard shortcuts/i))
    .first()

  await expect(shortcutsPanel).toBeVisible({ timeout: 5_000 })

  // Press ? again or Escape to dismiss
  await page.keyboard.press('Escape')
  await expect(shortcutsPanel).not.toBeVisible()
})
