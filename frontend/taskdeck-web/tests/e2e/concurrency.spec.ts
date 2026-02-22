import type { Browser, Page } from '@playwright/test'
import { expect, test } from '@playwright/test'
import { attachSessionToPage, registerAndAttachSession } from './support/authSession'

async function gotoBoardsWorkspace(page: Page) {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()
}

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

function cardByTitle(page: Page, cardTitle: string) {
  return page.locator('[data-card-id]').filter({ hasText: cardTitle }).first()
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
  await expect(cardByTitle(page, cardTitle)).toBeVisible()
}

test('concurrent card edits across sessions should converge to last write', async ({ browser, page, request }) => {
  const auth = await registerAndAttachSession(page, request, 'concurrency')
  const boardName = `Concurrency Board ${Date.now()}`
  const columnName = `Concurrency Column ${Date.now()}`
  const initialTitle = `Concurrency Card ${Date.now()}`
  const secondaryTitle = `${initialTitle} Secondary`
  const finalTitle = `${initialTitle} Final`

  await createBoard(page, boardName)
  await addColumn(page, columnName)
  await addCard(page, columnName, initialTitle)
  const boardUrl = page.url()

  const secondaryContext = await browser.newContext()
  const secondaryPage = await secondaryContext.newPage()
  await attachSessionToPage(secondaryPage, auth)
  await secondaryPage.goto(boardUrl)

  try {
    await expect(secondaryPage.getByRole('heading', { name: boardName })).toBeVisible()
    await expect(cardByTitle(secondaryPage, initialTitle)).toBeVisible()

    await cardByTitle(page, initialTitle).click()
    await expect(page.getByRole('heading', { name: 'Edit Card' })).toBeVisible()
    await page.locator('#card-title').fill(finalTitle)

    await cardByTitle(secondaryPage, initialTitle).click()
    await expect(secondaryPage.getByRole('heading', { name: 'Edit Card' })).toBeVisible()
    await secondaryPage.locator('#card-title').fill(secondaryTitle)
    await secondaryPage.getByRole('button', { name: 'Save Changes' }).click()
    await expect(secondaryPage.getByRole('heading', { name: 'Edit Card' })).toHaveCount(0)
    await expect(cardByTitle(secondaryPage, secondaryTitle)).toBeVisible({ timeout: 15000 })

    await page.getByRole('button', { name: 'Save Changes' }).click()
    await expect(page.getByRole('heading', { name: 'Edit Card' })).toHaveCount(0)

    await expect(cardByTitle(page, finalTitle)).toBeVisible({ timeout: 20000 })
    await secondaryPage.reload()
    await expect(cardByTitle(secondaryPage, finalTitle)).toBeVisible({ timeout: 20000 })
    await expect(cardByTitle(secondaryPage, secondaryTitle)).toHaveCount(0)
  } finally {
    await secondaryContext.close()
  }
})

test('secondary session should receive new cards without manual refresh', async ({ browser, page, request }) => {
  const auth = await registerAndAttachSession(page, request, 'concurrency-realtime')
  const boardName = `Concurrency Realtime Board ${Date.now()}`
  const columnName = `Concurrency Realtime Column ${Date.now()}`
  const cardTitle = `Realtime Card ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, columnName)
  const boardUrl = page.url()

  const secondaryContext = await browser.newContext()
  const secondaryPage = await secondaryContext.newPage()
  await attachSessionToPage(secondaryPage, auth)
  await secondaryPage.goto(boardUrl)

  try {
    await expect(secondaryPage.getByRole('heading', { name: boardName })).toBeVisible()
    await addCard(page, columnName, cardTitle)
    await expect(cardByTitle(secondaryPage, cardTitle)).toBeVisible({ timeout: 25000 })
  } finally {
    await secondaryContext.close()
  }
})
