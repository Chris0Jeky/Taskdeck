import { expect, test } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

for (const theme of ['paper', 'legacy'] as const) {
  test(`${theme}: type changes persist on the same card and a stale edit keeps its draft`, async ({ page, request }) => {
    const auth = await registerAndAttachSession(page, request, `work-type-${theme}`, { theme })
    const headers = { Authorization: `Bearer ${auth.token}` }
    const boardId = await createBoardWithColumn(request, auth, `${Date.now()}-${theme}`, {
      boardNamePrefix: 'Work item types', description: 'Synthetic type proof', columnNamePrefix: 'Original',
    })
    const board = await (await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })).json()
    const response = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, {
      headers, data: { columnId: board.columns[0].id, title: 'Investigate a work model', workItemType: 'Epic' },
    })
    await assertOk(response, 'Create an Epic')
    const card = await response.json()
    const path = `${API_BASE_URL}/boards/${boardId}/cards/${card.id}`
    const openCard = async () => {
      if (theme === 'paper') await page.getByRole('button', { name: `Card ${card.title}`, exact: true }).click()
      else await page.getByText(card.title, { exact: true }).click()
    }
    await page.goto(`/workspace/boards/${boardId}`)
    await openCard()
    const editor = page.getByRole('dialog', { name: 'Edit Card' })
    await expect(editor.getByLabel('Work item type')).toHaveValue('Epic')
    await editor.getByLabel('Work item type').selectOption('Spike')
    await editor.getByRole('button', { name: 'Save Changes', exact: true }).click()
    await expect(editor).not.toBeVisible()
    const saved = await (await request.get(path, { headers })).json()
    expect(saved.workItemType).toBe('Spike')
    expect(saved.id).toBe(card.id)
    expect(saved.columnId).toBe(card.columnId)
    await page.reload()
    await openCard()
    await expect(editor.getByLabel('Work item type')).toHaveValue('Spike')
    await editor.getByLabel('Work item type').selectOption('Epic')
    const concurrent = await request.patch(path, { headers, data: { workItemType: 'Task', expectedUpdatedAt: saved.updatedAt } })
    await assertOk(concurrent, 'Simulate a concurrent type edit')
    await editor.getByRole('button', { name: 'Save Changes', exact: true }).click()
    await expect(editor.getByRole('alert')).toContainText('Your draft is kept')
    await expect(editor.getByLabel('Work item type')).toHaveValue('Epic')
    expect((await (await request.get(path, { headers })).json()).workItemType).toBe('Task')
  })
}
