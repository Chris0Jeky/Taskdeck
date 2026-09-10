import { expect, test } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

for (const theme of ['paper', 'legacy'] as const) {
  test(`${theme}: archive, read history and restore the same card`, async ({ page, request }) => {
    const auth = await registerAndAttachSession(page, request, `archive-${theme}`, { theme })
    const headers = { Authorization: `Bearer ${auth.token}` }
    const boardId = await createBoardWithColumn(request, auth, `${Date.now()}-${theme}`, {
      boardNamePrefix: 'Archive journey', description: 'Synthetic archive proof', columnNamePrefix: 'Original',
    })
    const board = await (await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })).json()
    const created = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, {
      headers, data: { boardId, columnId: board.columns[0].id, title: 'Keep my archive evidence', description: 'Retained description' },
    })
    await assertOk(created, 'Create archive control')
    const card = await created.json()
    await page.goto(`/workspace/boards/${boardId}`)
    if (theme === 'paper') await page.getByRole('button', { name: `Card ${card.title}`, exact: true }).click()
    else await page.getByText(card.title, { exact: true }).click()
    const editor = page.getByRole('dialog', { name: 'Edit Card' })
    await expect(editor).toBeVisible()
    await editor.getByRole('button', { name: 'Archive card', exact: true }).click()
    await page.getByRole('button', { name: 'Confirm archive', exact: true }).click()
    await expect(editor).not.toBeVisible()
    const active = await request.get(`${API_BASE_URL}/boards/${boardId}/cards`, { headers })
    expect((await active.json()).some((item: { id: string }) => item.id === card.id)).toBe(false)
    await page.getByRole('button', { name: 'Archived cards', exact: true }).click()
    const history = page.getByRole('region', { name: 'Card archive' })
    await expect(history.getByRole('heading', { name: card.title })).toBeVisible()
    await expect(history.getByText('Retained description', { exact: true })).toBeVisible()
    await history.getByRole('button', { name: 'Restore card', exact: true }).click()
    await expect(history.getByText('No archived cards on this board.')).toBeVisible()
    const restored = await (await request.get(`${API_BASE_URL}/boards/${boardId}/cards/${card.id}`, { headers })).json()
    expect(restored.isArchived).toBe(false)
    expect(restored.columnId).toBe(card.columnId)
    expect(restored.id).toBe(card.id)
    await page.reload()
    await expect(page.getByText(card.title, { exact: true })).toBeVisible()
  })
}
