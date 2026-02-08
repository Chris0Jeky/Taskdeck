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

function columnDndByName(page: Page, columnName: string) {
  return page
    .locator('[data-column-dnd-id]')
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
  await expect(page.getByText(renamedBoardName)).toHaveCount(0)

  await page.goto(boardUrl)
  await page.locator('button[title="Board Settings"]').click()
  await page.locator('#board-archived').uncheck()
  await page.getByRole('button', { name: 'Save Changes' }).click()

  await page.goto('/boards')
  await expect(page.getByText(renamedBoardName).first()).toBeVisible()

  await page.goto(boardUrl)
  await page.locator('button[title="Board Settings"]').click()
  page.once('dialog', (dialog) => dialog.accept())
  await page.getByRole('button', { name: 'Delete Board' }).click()

  await expect(page).toHaveURL(/\/boards$/)
  await expect(page.getByText(renamedBoardName)).toHaveCount(0)
})

test('column drag and drop should reorder columns', async ({ page }) => {
  const boardName = `Reorder Board ${Date.now()}`
  const firstColumn = `First ${Date.now()}`
  const secondColumn = `Second ${Date.now()}`
  const thirdColumn = `Third ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, firstColumn)
  await addColumn(page, secondColumn)
  await addColumn(page, thirdColumn)

  await expect(page.locator('[data-column-dnd-id] h3').first()).toHaveText(firstColumn)

  const first = columnDndByName(page, firstColumn)
  const third = columnDndByName(page, thirdColumn)
  await first.dragTo(third)

  await expect(page.locator('[data-column-dnd-id] h3').first()).toHaveText(secondColumn)
  await expect(page.locator('[data-column-dnd-id] h3').nth(1)).toHaveText(thirdColumn)
  await expect(page.locator('[data-column-dnd-id] h3').nth(2)).toHaveText(firstColumn)
})

test('keyboard flow should open card and escape should close modal and inline forms', async ({ page }) => {
  const boardName = `Keyboard Board ${Date.now()}`
  const columnName = `To Do ${Date.now()}`
  const cardTitle = `Keyboard Card ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, columnName)
  await addCard(page, columnName, cardTitle)

  await page.locator('body').click()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('heading', { name: 'Edit Card' })).toBeVisible()

  await page.keyboard.press('Escape')
  await expect(page.getByRole('heading', { name: 'Edit Card' })).not.toBeVisible()

  await page.keyboard.press('n')
  await expect(page.getByPlaceholder('Enter card title...')).toBeVisible()

  await page.keyboard.press('Escape')
  await expect(page.getByPlaceholder('Enter card title...')).toHaveCount(0)
})

test('filter state should persist while panel is toggled in-session', async ({ page }) => {
  const boardName = `Filter Persist Board ${Date.now()}`
  const columnName = `To Do ${Date.now()}`
  const matchingCard = `Alpha ${Date.now()}`
  const hiddenCard = `Beta ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, columnName)
  await addCard(page, columnName, matchingCard)
  await addCard(page, columnName, hiddenCard)

  await page.keyboard.press('f')
  await expect(page.getByRole('heading', { name: 'Filter Cards' })).toBeVisible()

  const searchInput = page.getByPlaceholder('Search cards...')
  await searchInput.fill(matchingCard)

  await expect(page.locator('[data-card-id]:visible')).toHaveCount(1)
  await expect(page.locator('[data-card-id]').filter({ hasText: matchingCard })).toBeVisible()
  await expect(page.locator('[data-card-id]').filter({ hasText: hiddenCard })).toHaveCount(0)

  await page.locator('body').click()
  await page.keyboard.press('f')
  await expect(page.getByRole('heading', { name: 'Filter Cards' })).not.toBeVisible()

  await page.keyboard.press('f')
  await expect(page.getByRole('heading', { name: 'Filter Cards' })).toBeVisible()
  await expect(searchInput).toHaveValue(matchingCard)

  await page.keyboard.press('Escape')
  await expect(page.getByRole('heading', { name: 'Filter Cards' })).not.toBeVisible()
})
