import type { APIRequestContext, Page } from '@playwright/test'
import { expect, test } from '@playwright/test'

interface AuthUser {
  id: string
  username: string
  email: string
}

interface AuthResult {
  token: string
  user: AuthUser
}

const API_BASE_URL = 'http://localhost:5000/api'

async function bootstrapAuthenticatedSession(page: Page, request: APIRequestContext) {
  const unique = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const username = `e2e-${unique}`
  const email = `${username}@taskdeck.local`
  const password = 'E2ePassword123!'

  const response = await request.post(`${API_BASE_URL}/auth/register`, {
    data: { username, email, password },
  })
  expect(response.ok()).toBeTruthy()

  const auth = await response.json() as AuthResult
  await page.addInitScript((payload: { token: string; session: { userId: string; username: string; email: string } }) => {
    localStorage.setItem('taskdeck_token', payload.token)
    localStorage.setItem('taskdeck_session', JSON.stringify(payload.session))
  }, {
    token: auth.token,
    session: {
      userId: auth.user.id,
      username: auth.user.username,
      email: auth.user.email,
    },
  })
}

async function gotoBoardsWorkspace(page: Page) {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()
}

test.beforeEach(async ({ page, request }) => {
  await bootstrapAuthenticatedSession(page, request)
})

async function createBoard(page: Page, boardName: string) {
  await gotoBoardsWorkspace(page)
  await page.getByRole('button', { name: '+ New Board' }).click()
  await page.getByPlaceholder('Board name').fill(boardName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page).toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)
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

function columnDragHandleByName(page: Page, columnName: string) {
  return columnDndByName(page, columnName)
    .locator('[data-action="drag-column-handle"]')
    .first()
}

function cardByTitle(page: Page, cardTitle: string) {
  return page.locator('[data-card-id]').filter({ hasText: cardTitle }).first()
}

function cardDragHandleByTitle(page: Page, cardTitle: string) {
  return cardByTitle(page, cardTitle)
    .locator('[data-action="drag-card-handle"]')
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

  await gotoBoardsWorkspace(page)

  await page.getByRole('button', { name: '+ New Board' }).click()
  await page.getByPlaceholder('Board name').fill(boardName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page).toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)

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

  const sourceCardHandle = cardDragHandleByTitle(page, cardTitle)
  const targetLane = columnByName(page, targetColumn)

  await sourceCardHandle.dragTo(targetLane)

  await expect(targetLane.locator('[data-card-id]').filter({ hasText: cardTitle }).first()).toBeVisible()
})

test('card body drag should be ignored unless drag handle is used', async ({ page }) => {
  const boardName = `Card Guard Board ${Date.now()}`
  const sourceColumn = `To Do ${Date.now()}`
  const targetColumn = `Done ${Date.now()}`
  const cardTitle = `Do Not Move ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, sourceColumn)
  await addColumn(page, targetColumn)
  await addCard(page, sourceColumn, cardTitle)

  const sourceCard = cardByTitle(page, cardTitle)
  const sourceLane = columnByName(page, sourceColumn)
  const targetLane = columnByName(page, targetColumn)

  await sourceCard.dragTo(targetLane)

  await expect(sourceLane.locator('[data-card-id]').filter({ hasText: cardTitle }).first()).toBeVisible()
  await expect(targetLane.locator('[data-card-id]').filter({ hasText: cardTitle })).toHaveCount(0)
})

test('board settings lifecycle should support rename archive unarchive and archive action', async ({ page }) => {
  const initialBoardName = `Settings Board ${Date.now()}`
  const renamedBoardName = `${initialBoardName} Renamed`

  await createBoard(page, initialBoardName)
  const boardUrl = page.url()

  await page.locator('button[title="Board Settings"]').click()
  await page.locator('#board-name').fill(renamedBoardName)
  await page.locator('#board-archived').check()
  await page.getByRole('button', { name: 'Save Changes' }).click()

  await expect(page.getByRole('heading', { name: renamedBoardName })).toBeVisible()

  await page.goto('/workspace/boards')
  await expect(page.getByText(renamedBoardName)).toHaveCount(0)

  await page.goto(boardUrl)
  await page.locator('button[title="Board Settings"]').click()
  await page.locator('#board-archived').uncheck()
  await page.getByRole('button', { name: 'Save Changes' }).click()

  await page.goto('/workspace/boards')
  await expect(page.getByText(renamedBoardName).first()).toBeVisible()

  await page.goto(boardUrl)
  await page.locator('button[title="Board Settings"]').click()
  page.once('dialog', (dialog) => dialog.accept())
  await page.getByRole('button', { name: 'Archive Board' }).click()

  await expect(page).toHaveURL(/\/workspace\/boards$/)
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

  const firstColumnLane = columnByName(page, firstColumn)
  await firstColumnLane.getByRole('button', { name: 'Add Card' }).click()
  await firstColumnLane.getByPlaceholder('Enter card title...').fill('Editing in progress')

  const first = columnDndByName(page, firstColumn)
  const third = columnDndByName(page, thirdColumn)
  await first.dragTo(third)

  await expect(page.locator('[data-column-dnd-id] h3').first()).toHaveText(firstColumn)
  await expect(page.locator('[data-column-dnd-id] h3').nth(1)).toHaveText(secondColumn)
  await expect(page.locator('[data-column-dnd-id] h3').nth(2)).toHaveText(thirdColumn)

  const firstHandle = columnDragHandleByName(page, firstColumn)
  await firstHandle.dragTo(third)

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
