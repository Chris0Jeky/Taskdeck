import type { Page } from '@playwright/test'
import { expect } from '@playwright/test'

/**
 * UI-level board helpers for E2E tests.
 *
 * These interact with the actual UI (clicking buttons, filling inputs)
 * rather than using the API directly. Shared across cross-browser and
 * mobile-responsive spec files to avoid duplication.
 */

export async function createBoard(page: Page, boardName: string) {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()
  await page.getByRole('button', { name: '+ New Board' }).click()
  await page.getByPlaceholder('Board name').fill(boardName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page).toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()
}

export async function addColumn(page: Page, columnName: string) {
  await page.getByRole('button', { name: '+ Add Column' }).click()
  await page.getByPlaceholder('Column name').fill(columnName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page.getByRole('heading', { name: columnName, exact: true })).toBeVisible()
}

export function columnByName(page: Page, columnName: string) {
  return page
    .locator('[data-column-id]')
    .filter({ has: page.getByRole('heading', { name: columnName, exact: true }) })
    .first()
}

export async function addCard(page: Page, columnName: string, cardTitle: string) {
  const column = columnByName(page, columnName)
  await column.getByRole('button', { name: 'Add Card' }).click()
  const addCardInput = column.getByPlaceholder('Enter card title...')
  await expect(addCardInput).toBeVisible()
  await addCardInput.fill(cardTitle)
  const createCardResponse = page.waitForResponse((response) =>
    response.request().method() === 'POST'
    && /\/api\/boards\/[a-f0-9-]+\/cards$/i.test(response.url())
    && response.ok())
  await column.getByRole('button', { name: 'Add', exact: true }).click()
  await createCardResponse
  // CI can be slow to re-render after card creation; extend the default expect timeout.
  await expect(
    page.locator('[data-card-id]').filter({ hasText: cardTitle }).first(),
  ).toBeVisible({ timeout: 15_000 })
}
