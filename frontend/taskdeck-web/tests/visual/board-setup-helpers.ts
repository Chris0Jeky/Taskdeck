/**
 * Shared helpers for visual regression tests that require a populated board.
 *
 * The board modals (CardModal, ColumnEditModal, StarterPackCatalogModal) and
 * the board toolbar/action rail all need a board with at least one column
 * and one card to be visible. These helpers centralise that setup so
 * individual specs stay focused on the screenshot they capture.
 */
import type { Page } from '@playwright/test'
import { expect } from '@playwright/test'

export async function createBoard(page: Page, boardName: string): Promise<void> {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()
  await page.getByRole('button', { name: '+ New Board' }).click()
  await page.getByPlaceholder('Board name').fill(boardName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page).toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()
}

export async function addColumn(page: Page, columnName: string): Promise<void> {
  await page.getByRole('button', { name: '+ Add Column' }).click()
  await page.getByPlaceholder('Column name').fill(columnName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page.getByRole('heading', { name: columnName, exact: true })).toBeVisible()
}

export async function addCard(page: Page, columnName: string, cardTitle: string): Promise<void> {
  const column = page
    .locator('[data-column-id]')
    .filter({ has: page.getByRole('heading', { name: columnName, exact: true }) })
    .first()
  await column.getByRole('button', { name: 'Add Card' }).click()
  const addCardInput = column.getByPlaceholder('Enter card title...')
  await expect(addCardInput).toBeVisible()
  await addCardInput.fill(cardTitle)
  const createCardResponse = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      /\/api\/boards\/[a-f0-9-]+\/cards$/i.test(response.url()) &&
      response.ok(),
  )
  await column.getByRole('button', { name: 'Add', exact: true }).click()
  await createCardResponse
  await expect(page.locator('[data-card-id]').filter({ hasText: cardTitle }).first()).toBeVisible()
}

/**
 * Convenience: seed a board with a single column and card so modal-opening
 * specs have a predictable starting state. Returns the board name.
 */
export async function seedMinimalBoard(page: Page, boardName: string): Promise<void> {
  await createBoard(page, boardName)
  await addColumn(page, 'Backlog')
  await addCard(page, 'Backlog', 'Sample Card')
}
