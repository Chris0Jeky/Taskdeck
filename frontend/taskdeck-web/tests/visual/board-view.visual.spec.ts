/**
 * Visual regression tests for the Board view.
 *
 * Captures baseline screenshots of the board in various states:
 * - Empty board (freshly created, no columns)
 * - Board with columns and cards (populated state)
 *
 * These tests require a running backend and frontend (configured via
 * playwright.visual.config.ts).
 */
import type { Page } from '@playwright/test'
import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from '../e2e/support/authSession'
import { prepareForScreenshot } from './visual-test-helpers'

function columnByName(page: Page, columnName: string) {
  return page
    .locator('[data-column-id]')
    .filter({ has: page.getByRole('heading', { name: columnName, exact: true }) })
    .first()
}

async function createBoard(page: Page, boardName: string) {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()
  await page.getByRole('button', { name: '+ New Board' }).click()
  await page.getByPlaceholder('Board name').fill(boardName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page).toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()
}

async function addColumn(page: Page, columnName: string) {
  await page.getByRole('button', { name: '+ Add Column' }).click()
  await page.getByPlaceholder('Column name').fill(columnName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page.getByRole('heading', { name: columnName, exact: true })).toBeVisible()
}

async function addCard(page: Page, columnName: string, cardTitle: string) {
  const column = columnByName(page, columnName)
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

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'visual-board')
})

test('empty board view', async ({ page }) => {
  await createBoard(page, 'Visual Test Board')
  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('board-empty.png')
})

test('board with columns and cards', async ({ page }) => {
  await createBoard(page, 'Visual Test Board')

  await addColumn(page, 'Backlog')
  await addColumn(page, 'In Progress')
  await addColumn(page, 'Done')

  await addCard(page, 'Backlog', 'Design wireframes')
  await addCard(page, 'Backlog', 'Write API spec')
  await addCard(page, 'In Progress', 'Implement auth')
  await addCard(page, 'Done', 'Set up CI pipeline')

  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('board-populated.png')
})
