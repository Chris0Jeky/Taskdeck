import type { Page } from '@playwright/test'
import { expect, test } from '@playwright/test'

async function createBoard(page: Page, boardName: string) {
  await page.goto('/boards')
  await page.getByRole('button', { name: '+ New Board' }).click()
  await page.getByPlaceholder('Board name').fill(boardName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page).toHaveURL(/\/boards\/[a-f0-9-]+$/)
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()
}

function columnByName(page: Page, columnName: string) {
  return page
    .locator('[data-column-id]')
    .filter({ has: page.getByRole('heading', { name: columnName, exact: true }) })
    .first()
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
  await column.getByPlaceholder('Enter card title...').fill(cardTitle)
  await column.getByRole('button', { name: 'Add', exact: true }).click()
}

test('board to card workflow smoke test', async ({ page }) => {
  const boardName = `Smoke Board ${Date.now()}`
  const columnName = `To Do ${Date.now()}`
  const cardTitle = `Smoke Card ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, columnName)
  await addCard(page, columnName, cardTitle)

  await expect(page.locator('[data-card-id]').filter({ hasText: cardTitle }).first()).toBeVisible()
})

test('filter panel shortcut should toggle panel', async ({ page }) => {
  const boardName = `Filter Board ${Date.now()}`

  await page.goto('/boards')

  await page.getByRole('button', { name: '+ New Board' }).click()
  await page.getByPlaceholder('Board name').fill(boardName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page).toHaveURL(/\/boards\/[a-f0-9-]+$/)

  await page.keyboard.press('f')
  await expect(page.getByRole('heading', { name: 'Filter Cards' })).toBeVisible()

  await page.keyboard.press('f')
  await expect(page.getByRole('heading', { name: 'Filter Cards' })).not.toBeVisible()
})

test('column WIP limit should reject additional cards', async ({ page }) => {
  const boardName = `WIP Board ${Date.now()}`
  const columnName = `In Progress ${Date.now()}`
  const firstCard = `Allowed Card ${Date.now()}`
  const secondCard = `Rejected Card ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, columnName)

  await page.locator('[data-column-id] button[title="Edit Column"]').first().click()
  await expect(page.getByRole('heading', { name: 'Edit Column' })).toBeVisible()
  await page.locator('#column-has-wip-limit').check()
  await page.locator('#wip-limit').fill('1')
  await page.getByRole('button', { name: 'Save Changes' }).click()

  await addCard(page, columnName, firstCard)
  await expect(page.locator('[data-card-id]').filter({ hasText: firstCard }).first()).toBeVisible()

  await addCard(page, columnName, secondCard)
  await expect(page.locator('[data-card-id]').filter({ hasText: secondCard })).toHaveCount(0)
  await expect(page.locator('text=has reached its WIP limit').first()).toBeVisible()
})

test('card drag and drop should move card between columns', async ({ page }) => {
  const boardName = `Move Board ${Date.now()}`
  const sourceColumn = `To Do ${Date.now()}`
  const targetColumn = `Done ${Date.now()}`
  const cardTitle = `Move Me ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, sourceColumn)
  await addColumn(page, targetColumn)
  await addCard(page, sourceColumn, cardTitle)

  const sourceCard = page.locator('[data-card-id]').filter({ hasText: cardTitle }).first()
  const targetLane = columnByName(page, targetColumn)

  await sourceCard.dragTo(targetLane)

  await expect(targetLane.locator('[data-card-id]').filter({ hasText: cardTitle }).first()).toBeVisible()
})

test('board settings lifecycle should support rename archive unarchive and delete', async ({ page }) => {
  const initialBoardName = `Settings Board ${Date.now()}`
  const renamedBoardName = `${initialBoardName} Renamed`

  await createBoard(page, initialBoardName)
  const boardUrl = page.url()

  await page.locator('button[title="Board Settings"]').click()
  await page.locator('#board-name').fill(renamedBoardName)
  await page.locator('#board-archived').check()
  await page.getByRole('button', { name: 'Save Changes' }).click()

  await expect(page.getByRole('heading', { name: renamedBoardName })).toBeVisible()

  await page.goto('/boards')
  await expect(page.locator('text=' + renamedBoardName)).toHaveCount(0)

  await page.goto(boardUrl)
  await page.locator('button[title="Board Settings"]').click()
  await page.locator('#board-archived').uncheck()
  await page.getByRole('button', { name: 'Save Changes' }).click()

  await page.goto('/boards')
  await expect(page.locator('text=' + renamedBoardName).first()).toBeVisible()

  await page.goto(boardUrl)
  await page.locator('button[title="Board Settings"]').click()
  page.once('dialog', (dialog) => dialog.accept())
  await page.getByRole('button', { name: 'Delete Board' }).click()

  await expect(page).toHaveURL(/\/boards$/)
  await expect(page.locator('text=' + renamedBoardName)).toHaveCount(0)
})
