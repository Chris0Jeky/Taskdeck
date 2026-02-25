import type { Page } from '@playwright/test'
import { expect, test } from '@playwright/test'
import { API_ORIGIN, registerAndAttachSession } from './support/authSession'

async function gotoBoardsWorkspace(page: Page) {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()
}

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'smoke')
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

async function expectColumnOrder(page: Page, expectedOrder: string[]) {
  const headings = page.locator('[data-column-dnd-id] h3')
  await expect(headings).toHaveCount(expectedOrder.length)

  for (const [index, expectedHeading] of expectedOrder.entries()) {
    await expect(headings.nth(index)).toHaveText(expectedHeading)
  }
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

test('realtime board updates should propagate across active sessions without refresh', async ({ page }) => {
  const boardName = `Realtime Board ${Date.now()}`
  const columnName = `Realtime Column ${Date.now()}`
  const cardTitle = `Realtime Card ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, columnName)

  const boardUrl = page.url()
  const secondaryPage = await page.context().newPage()

  try {
    await secondaryPage.goto(boardUrl)
    await expect(secondaryPage.getByRole('heading', { name: boardName })).toBeVisible()

    await addCard(page, columnName, cardTitle)

    await expect(
      secondaryPage.locator('[data-card-id]').filter({ hasText: cardTitle }).first()
    ).toBeVisible({ timeout: 25000 })
  } finally {
    await secondaryPage.close()
  }
})

test('realtime negotiate should reject unauthenticated subscriptions', async ({ request }) => {
  const response = await request.post(`${API_ORIGIN}/hubs/boards/negotiate?negotiateVersion=1`, {
    data: {},
  })

  expect(response.status()).toBe(401)
  await expect(response.json()).resolves.toMatchObject({
    errorCode: 'Unauthorized',
  })
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

test('command palette keyboard navigation should activate selected command', async ({ page }) => {
  await gotoBoardsWorkspace(page)

  await page.keyboard.press('Control+K')
  const palette = page.getByRole('dialog', { name: 'Command palette' })
  await expect(palette).toBeVisible()

  const paletteInput = palette.getByPlaceholder('Type a command or search...')
  await expect(paletteInput).toBeFocused()

  await paletteInput.press('ArrowDown')
  await paletteInput.press('ArrowDown')
  await paletteInput.press('Enter')

  await expect(page).toHaveURL(/\/workspace\/activity$/)
  await expect(palette).toHaveCount(0)
})

test('capture hotkey should save item and route to inbox', async ({ page }) => {
  await gotoBoardsWorkspace(page)

  const captureText = `Capture item ${Date.now()}`

  await page.keyboard.press('Control+Shift+C')
  const captureModal = page.getByRole('dialog', { name: 'Capture item' })
  await expect(captureModal).toBeVisible()

  await captureModal.getByPlaceholder('Capture a thought, task, or follow-up...').fill(captureText)
  await captureModal.getByPlaceholder('Capture a thought, task, or follow-up...').press('Control+Enter')

  await expect(page).toHaveURL(/\/workspace\/inbox$/)
  await expect(page.locator('.td-inbox-row__excerpt').first()).toContainText(captureText)
})

test('command palette capture action should save item and route to inbox', async ({ page }) => {
  await gotoBoardsWorkspace(page)

  const captureText = `Palette capture item ${Date.now()}`

  await page.keyboard.press('Control+K')
  const palette = page.getByRole('dialog', { name: 'Command palette' })
  await expect(palette).toBeVisible()

  const paletteInput = palette.getByPlaceholder('Type a command or search...')
  await expect(paletteInput).toBeFocused()
  await paletteInput.fill('new capture')
  await paletteInput.press('Enter')

  const captureModal = page.getByRole('dialog', { name: 'Capture item' })
  await expect(captureModal).toBeVisible()
  await expect(palette).toHaveCount(0)

  await captureModal.getByPlaceholder('Capture a thought, task, or follow-up...').fill(captureText)
  await captureModal.getByPlaceholder('Capture a thought, task, or follow-up...').press('Control+Enter')

  await expect(page).toHaveURL(/\/workspace\/inbox$/)
  await expect(page.locator('.td-inbox-row__excerpt').first()).toContainText(captureText)
})

test('activity view selectors should support board and entity discovery without raw IDs', async ({ page }) => {
  const boardName = `Activity Board ${Date.now()}`
  const columnName = `Activity Column ${Date.now()}`
  const cardTitle = `Activity Card ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, columnName)
  await addCard(page, columnName, cardTitle)

  await page.goto('/workspace/activity')

  const boardSelect = page.locator('#activity-board-select')
  await expect(boardSelect).toBeVisible()
  await boardSelect.selectOption({ label: boardName })
  await page.getByRole('button', { name: 'Fetch', exact: true }).click()

  await expect(page).toHaveURL(/\/workspace\/activity\/board\/[a-f0-9-]+$/)

  await page.locator('#activity-view-mode').selectOption('entity')
  await page.locator('#activity-entity-type').selectOption('Card')
  await page.locator('#activity-entity-board-select').selectOption({ label: boardName })

  const entitySelect = page.locator('#activity-entity-select')
  const entityOption = entitySelect.locator('option').filter({ hasText: cardTitle })
  await expect(entityOption).toHaveCount(1)

  const entityOptionValue = await entityOption.first().getAttribute('value')
  expect(entityOptionValue).toBeTruthy()
  await entitySelect.selectOption(entityOptionValue!)
  await page.getByRole('button', { name: 'Fetch', exact: true }).click()

  await expect(page).toHaveURL(/\/workspace\/activity\/entity\/Card\/[a-f0-9-]+$/)
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

test('card drag and drop should move card between columns and persist after refresh', async ({ page }) => {
  const boardName = `Move Board ${Date.now()}`
  const sourceColumn = `To Do ${Date.now()}`
  const targetColumn = `Done ${Date.now()}`
  const cardTitle = `Move Me ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, sourceColumn)
  await addColumn(page, targetColumn)
  await addCard(page, sourceColumn, cardTitle)
  const boardUrl = page.url()

  const sourceCardHandle = cardDragHandleByTitle(page, cardTitle)
  const sourceLane = columnByName(page, sourceColumn)
  let targetLane = columnByName(page, targetColumn)

  await sourceCardHandle.dragTo(targetLane)

  await expect(targetLane.locator('[data-card-id]').filter({ hasText: cardTitle }).first()).toBeVisible()
  await expect(sourceLane.locator('[data-card-id]').filter({ hasText: cardTitle })).toHaveCount(0)

  await page.goto(boardUrl)
  targetLane = columnByName(page, targetColumn)
  await expect(targetLane.locator('[data-card-id]').filter({ hasText: cardTitle }).first()).toBeVisible()
  await expect(columnByName(page, sourceColumn).locator('[data-card-id]').filter({ hasText: cardTitle })).toHaveCount(0)
})

test('card drag should use explicit enlarged handle while add-card controls stay safe', async ({ page }) => {
  const boardName = `Card Handle Board ${Date.now()}`
  const sourceColumn = `To Do ${Date.now()}`
  const targetColumn = `Done ${Date.now()}`
  const cardTitle = `Surface Move ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, sourceColumn)
  await addColumn(page, targetColumn)
  await addCard(page, sourceColumn, cardTitle)

  const sourceCardHandle = cardDragHandleByTitle(page, cardTitle)
  const sourceLane = columnByName(page, sourceColumn)
  const targetLane = columnByName(page, targetColumn)
  const sourceCard = cardByTitle(page, cardTitle)
  const addCardButton = sourceLane.getByRole('button', { name: 'Add Card' })
  const addCardButtonBox = await addCardButton.boundingBox()
  const targetLaneBox = await targetLane.boundingBox()
  const handleBox = await sourceCardHandle.boundingBox()
  const cardBox = await sourceCard.boundingBox()
  expect(addCardButtonBox).not.toBeNull()
  expect(targetLaneBox).not.toBeNull()
  expect(handleBox).not.toBeNull()
  expect(cardBox).not.toBeNull()
  expect(handleBox!.width).toBeGreaterThan(cardBox!.width * 0.75)
  expect(handleBox!.height).toBeGreaterThan(30)

  await page.mouse.move(
    addCardButtonBox!.x + addCardButtonBox!.width / 2,
    addCardButtonBox!.y + addCardButtonBox!.height / 2
  )
  await page.mouse.down()
  await page.mouse.move(
    targetLaneBox!.x + targetLaneBox!.width / 2,
    targetLaneBox!.y + Math.min(80, targetLaneBox!.height / 3),
    { steps: 12 }
  )
  await page.mouse.up()

  await expect(sourceLane.locator('[data-card-id]').filter({ hasText: cardTitle }).first()).toBeVisible()
  await expect(targetLane.locator('[data-card-id]').filter({ hasText: cardTitle })).toHaveCount(0)

  await sourceCardHandle.dragTo(targetLane)

  await expect(targetLane.locator('[data-card-id]').filter({ hasText: cardTitle }).first()).toBeVisible()
  await expect(sourceLane.locator('[data-card-id]').filter({ hasText: cardTitle })).toHaveCount(0)
})

test('board settings lifecycle should support rename archive unarchive and archive action', async ({ page }) => {
  const initialBoardName = `Settings Board ${Date.now()}`
  const renamedBoardName = `${initialBoardName} Renamed`

  await createBoard(page, initialBoardName)
  const boardUrl = page.url()

  await page.locator('button[title="Board Settings"]').click()
  await page.locator('#board-name').fill(renamedBoardName)
  await page.getByRole('button', { name: 'Save Changes' }).click()

  await expect(page.getByRole('heading', { name: renamedBoardName })).toBeVisible()

  await page.locator('button[title="Board Settings"]').click()
  page.once('dialog', (dialog) => dialog.accept())
  await page.getByRole('button', { name: 'Move to Archive' }).click()

  await expect(page).toHaveURL(/\/workspace\/boards$/)
  await expect(page.getByText(renamedBoardName)).toHaveCount(0)

  await page.goto('/workspace/archive')
  await expect(page.getByRole('heading', { name: 'Archive' })).toBeVisible()
  const archivedBoardRow = page.locator('.td-archive-row').filter({ hasText: renamedBoardName }).first()
  await expect(archivedBoardRow).toBeVisible()

  page.once('dialog', (dialog) => dialog.accept())
  await archivedBoardRow.getByRole('button', { name: 'Restore Board' }).click()
  await expect(page.locator('.td-archive-row').filter({ hasText: renamedBoardName })).toHaveCount(0)

  await page.goto('/workspace/boards')
  await expect(page.getByText(renamedBoardName).first()).toBeVisible()
  await page.goto(boardUrl)
  await expect(page.getByRole('heading', { name: renamedBoardName })).toBeVisible()
})

test('column drag and drop should reorder columns and persist after refresh', async ({ page }) => {
  const boardName = `Reorder Board ${Date.now()}`
  const firstColumn = `First ${Date.now()}`
  const secondColumn = `Second ${Date.now()}`
  const thirdColumn = `Third ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, firstColumn)
  await addColumn(page, secondColumn)
  await addColumn(page, thirdColumn)
  const boardUrl = page.url()

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

  await expectColumnOrder(page, [secondColumn, thirdColumn, firstColumn])

  await page.goto(boardUrl)
  await expectColumnOrder(page, [secondColumn, thirdColumn, firstColumn])
})

test('keyboard flow should open card and escape should close modal and inline forms', async ({ page }) => {
  const boardName = `Keyboard Board ${Date.now()}`
  const columnName = `To Do ${Date.now()}`
  const cardTitle = `Keyboard Card ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, columnName)
  await addCard(page, columnName, cardTitle)
  await expect(cardByTitle(page, cardTitle)).toBeVisible()

  const column = columnByName(page, columnName)
  await expect(column.getByPlaceholder('Enter card title...')).toHaveCount(0)

  await page.locator('body').click()
  await page.keyboard.press('n')
  await expect(column.getByPlaceholder('Enter card title...')).toBeVisible()

  await page.locator('body').click()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('heading', { name: 'Edit Card' })).toBeVisible()

  await page.keyboard.press('Escape')
  await expect(page.getByRole('heading', { name: 'Edit Card' })).not.toBeVisible()
  await expect(column.getByPlaceholder('Enter card title...')).toBeVisible()

  await page.keyboard.press('Escape')
  await expect(column.getByPlaceholder('Enter card title...')).toHaveCount(0)

  await page.keyboard.press('Escape')
  await expect(page).toHaveURL(/\/workspace\/boards$/)
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
