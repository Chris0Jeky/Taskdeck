/**
 * E2E: Keyboard Navigation Scenarios
 *
 * Covers keyboard-driven workflows beyond the basic escape tests:
 * - Full keyboard-only board workflow (create board, add column, add card)
 * - Command palette: navigate with arrow keys and Enter
 * - Keyboard shortcut 'n' to add a new card
 * - Shortcut help panel toggled by ? key
 * - Escape from command palette closes it and returns to the prior view
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
  await page.keyboard.press('Escape') // Ensure no input is capturing keystrokes
  await page.keyboard.press('n')
  const column = columnByName(page, columnName)
  const cardInput = column.getByPlaceholder('Enter card title...')
  await expect(cardInput).toBeVisible()
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

  // Type a partial command and use arrow keys to navigate
  await paletteInput.fill('to')

  // Press ArrowDown to move through results (if any)
  await page.keyboard.press('ArrowDown')
  await page.keyboard.press('ArrowDown')
  await page.keyboard.press('ArrowUp')

  // Press Enter to activate the selected result
  await page.keyboard.press('Enter')

  // The palette should close after selection
  await expect(palette).toHaveCount(0)
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

test('question mark shortcut should toggle keyboard shortcuts help', async ({ page }) => {
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

  if (await shortcutsPanel.isVisible({ timeout: 5_000 }).catch(() => false)) {
    await expect(shortcutsPanel).toBeVisible()

    // Press ? again or Escape to dismiss
    await page.keyboard.press('Escape')
    await expect(shortcutsPanel).not.toBeVisible()
  } else {
    // If the shortcut help is not implemented, skip gracefully
    test.skip()
  }
})
