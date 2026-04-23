/**
 * Visual regression tests for the board-scoped modals.
 *
 * Covers three modals that can only be opened from an existing board:
 * - CardModal (Edit Card) — opened by clicking a card
 * - ColumnEditModal (Edit Column) — opened via the column settings button
 * - StarterPackCatalogModal — opened from the BoardToolbar "Starter Packs" button
 *
 * Each test seeds a minimal board with a single column/card so the
 * starting state is predictable.
 */
import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from '../e2e/support/authSession'
import { addColumn, createBoard, seedMinimalBoard } from './board-setup-helpers'
import { prepareForScreenshot } from './visual-test-helpers'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'visual-board-modals')
})

test('card modal edit state', async ({ page }) => {
  await seedMinimalBoard(page, 'Visual Board Modals')

  // Open the first card to trigger CardModal
  await page.locator('[data-card-id]').first().click()
  await expect(page.getByRole('dialog', { name: 'Edit Card' })).toBeVisible()

  await prepareForScreenshot(page)

  // Mask the card title input — it reflects the seeded card title which
  // could change if the setup helper is updated. Keep the screenshot
  // focused on modal chrome.
  await expect(page).toHaveScreenshot('card-modal-edit', {
    mask: [page.locator('#card-title')],
  })
})

test('column edit modal', async ({ page }) => {
  await createBoard(page, 'Visual Board Modals')
  await addColumn(page, 'Backlog')

  // The column settings (gear) button is the only "Edit Column"-titled button
  // inside the column header.
  await page.getByTitle('Edit Column').click()
  await expect(page.getByRole('dialog', { name: 'Edit Column' })).toBeVisible()

  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('column-edit-modal')
})

test('starter pack catalog modal', async ({ page }) => {
  await createBoard(page, 'Visual Board Modals')

  await page.getByRole('button', { name: 'Starter Packs' }).click()
  // The modal contains its own search input with a known id.
  await expect(page.locator('#starter-pack-search')).toBeVisible()
  // Wait for catalog fetch to settle so we don't capture the loading state.
  await page.waitForLoadState('networkidle')

  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('starter-pack-catalog-modal')
})
