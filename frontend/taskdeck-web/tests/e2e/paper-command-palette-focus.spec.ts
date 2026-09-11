import { expect, test, type Page } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'

async function enablePaperMode(page: Page) {
  await page.addInitScript(() => {
    window.localStorage.setItem('td.paper.mode.v2', 'paper')
  })
}

test('Paper command palette returns focus inside a card modal after a narrow transition', async ({ page, request }) => {
  await page.setViewportSize({ width: 1280, height: 844 })
  await enablePaperMode(page)
  const auth = await registerAndAttachSession(page, request, 'palette-focus')
  const seed = `${Date.now()}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Paper palette focus',
    description: 'Synthetic command palette focus regression fixture.',
    columnNamePrefix: 'Palette lane',
  })

  const boardResponse = await request.get(`${API_BASE_URL}/boards/${boardId}`, {
    headers: { Authorization: `Bearer ${auth.token}` },
  })
  expect(boardResponse.ok()).toBe(true)
  const board = await boardResponse.json() as { columns: Array<{ id: string }> }
  const cardTitle = `Palette focus card ${seed}`
  const createCardResponse = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, {
    headers: { Authorization: `Bearer ${auth.token}` },
    data: {
      columnId: board.columns[0]!.id,
      title: cardTitle,
      description: 'Synthetic-only command palette focus fixture.',
      dueDate: null,
      labelIds: [],
    },
  })
  expect(createCardResponse.ok()).toBe(true)

  await page.goto(`/workspace/boards/${boardId}`)
  const opener = page.getByRole('button', { name: new RegExp(cardTitle) })
  const editor = page.getByRole('dialog', { name: 'Edit Card' })
  const closeEditor = editor.getByRole('button', { name: 'Close card editor' })
  await opener.focus()
  await page.keyboard.press('Enter')
  await expect(editor).toBeVisible()
  await expect(editor).not.toHaveAttribute('aria-modal', 'true')
  await expect(closeEditor).toBeFocused()

  await page.keyboard.press('Control+K')
  const palette = page.getByRole('dialog', { name: 'Command palette' })
  const paletteInput = palette.getByRole('combobox', { name: 'Command palette search' })
  await expect(paletteInput).toBeFocused()

  await page.setViewportSize({ width: 390, height: 844 })
  await expect(editor).toHaveAttribute('aria-modal', 'true')
  await expect(paletteInput).toBeFocused()

  await page.keyboard.press('Escape')
  await expect(palette).toHaveCount(0)
  await expect(closeEditor).toBeFocused()
  await expect.poll(() => editor.evaluate((element) => element.contains(document.activeElement))).toBe(true)
})

test('Paper command palette restores the programmatically focused Review empty state', async ({ page, request }) => {
  await enablePaperMode(page)
  await registerAndAttachSession(page, request, 'palette-prog')
  await page.goto('/workspace/review')
  const emptyState = page.getByTestId('paper-review-empty')
  await expect(emptyState).toBeVisible()
  await expect(emptyState).toHaveAttribute('tabindex', '-1')
  await emptyState.focus()
  await expect(emptyState).toBeFocused()

  await page.keyboard.press('Control+K')
  const palette = page.getByRole('dialog', { name: 'Command palette' })
  await expect(palette.getByRole('combobox', { name: 'Command palette search' })).toBeFocused()
  await page.keyboard.press('Escape')
  await expect(palette).toHaveCount(0)
  await expect(emptyState).toBeFocused()
})