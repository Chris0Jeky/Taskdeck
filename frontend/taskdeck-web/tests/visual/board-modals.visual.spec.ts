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

  // Click the title, not the card center: the center can land on the drag
  // handle, whose click handler intentionally stops propagation.
  const sampleCard = page.locator('[data-card-id]').filter({ hasText: 'Sample Card' }).first()
  await sampleCard.getByRole('heading', { name: 'Sample Card', exact: true }).click()
  await expect(page.getByRole('dialog', { name: 'Edit Card' })).toBeVisible()

  await prepareForScreenshot(page)

  // Mask the card title input — it reflects the seeded card title which
  // could change if the setup helper is updated. Keep the screenshot
  // focused on modal chrome.
  await expect(page).toHaveScreenshot('card-modal-edit.png', {
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

  await expect(page).toHaveScreenshot('column-edit-modal.png')
})

test('starter pack modal json import tab', async ({ page }) => {
  await createBoard(page, 'Visual Board Modals')

  await page.getByRole('button', { name: 'Starter Packs' }).click()
  // We capture the JSON Import tab rather than the default Catalog tab:
  // the catalog is server-provided and its contents (and count) can
  // change as new packs are added, which would make the baseline
  // unstable. The JSON Import tab is entirely client-rendered static
  // markup so it provides a stable baseline for the modal chrome
  // (header, tab bar, two-column layout, form primitives).
  await page.getByTestId('tab-import').click()
  await expect(page.getByTestId('import-json-textarea')).toBeVisible()

  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('starter-pack-modal-import.png')
})
