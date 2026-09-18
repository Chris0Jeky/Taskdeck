import { expect, test } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

for (const theme of ['paper', 'legacy'] as const) {
  test(`${theme}: parent assignment, stale detach refusal, archive restore and explicit delete`, async ({ page, request }) => {
    test.setTimeout(90_000)
    const auth = await registerAndAttachSession(page, request, `hierarchy-${theme}`, { theme })
    const headers = { Authorization: `Bearer ${auth.token}` }
    const boardId = await createBoardWithColumn(request, auth, `${Date.now()}-${theme}`, {
      boardNamePrefix: 'Parent hierarchy', description: 'Synthetic parent proof', columnNamePrefix: 'Original',
    })
    const board = await (await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })).json()
    const create = async (title: string, parentCardId?: string) => {
      const response = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, {
        headers, data: { columnId: board.columns[0].id, title, workItemType: 'Spike', parentCardId },
      })
      await assertOk(response, 'Create hierarchy card')
      return response.json()
    }
    const parent = await create('Review parent')
    const child = await create('Keep child identity')
    const cardPath = (id: string) => `${API_BASE_URL}/boards/${boardId}/cards/${id}`
    const read = async (id: string) => (await request.get(cardPath(id), { headers })).json()
    const open = async (title: string) => {
      if (theme === 'paper') await page.getByRole('button', { name: `Card ${title}`, exact: true }).click()
      else await page.getByText(title, { exact: true }).click()
    }
    await page.goto(`/workspace/boards/${boardId}`)
    await open(child.title)
    const editor = page.getByRole('dialog', { name: 'Edit Card', exact: true })
    await editor.getByLabel('Parent card', { exact: true }).selectOption(parent.id)
    await editor.getByRole('button', { name: 'Save Changes', exact: true }).click()
    await expect(editor).not.toBeVisible()
    expect((await read(child.id)).parentCardId).toBe(parent.id)
    await open(parent.title)
    await editor.getByRole('button', { name: 'Archive card', exact: true }).click()
    const confirmation = page.getByRole('dialog', { name: 'Archive card?', exact: true })
    await expect(confirmation).toContainText(child.title)
    expect((await read(parent.id)).isArchived).toBe(false)
    await create('Added after preview', parent.id)
    await confirmation.getByRole('button', { name: 'Confirm archive', exact: true }).click()
    await expect(page.getByText('Children changed since preview.', { exact: false })).toBeVisible()
    expect((await read(child.id)).parentCardId).toBe(parent.id)
    await page.reload()
    await open(parent.title)
    await editor.getByRole('button', { name: 'Archive card', exact: true }).click()
    await expect(confirmation).toContainText('Added after preview')
    await confirmation.getByRole('button', { name: 'Confirm archive', exact: true }).click()
    await expect(editor).not.toBeVisible()
    expect((await read(child.id)).parentCardId).toBeNull()
    await page.getByRole('button', { name: 'Archived cards', exact: true }).click()
    const history = page.getByRole('region', { name: 'Card archive' })
    await history.getByRole('button', { name: 'Restore card', exact: true }).click()
    await expect(history.getByText('No archived cards on this board.')).toBeVisible()
    expect((await read(child.id)).parentCardId).toBeNull()
    const currentChild = await read(child.id)
    await assertOk(await request.patch(cardPath(child.id), { headers, data: { parentCardId: parent.id, expectedUpdatedAt: currentChild.updatedAt } }), 'Reassign parent')
    await page.reload()
    await open(parent.title)
    await editor.getByRole('button', { name: 'Delete Card', exact: true }).click()
    const deletion = page.getByRole('dialog', { name: 'Delete Card', exact: true })
    await expect(deletion).toContainText(child.title)
    expect((await read(child.id)).parentCardId).toBe(parent.id)
    await deletion.getByRole('button', { name: 'Delete', exact: true }).click()
    await expect(editor).not.toBeVisible()
    const retained = await read(child.id)
    expect(retained.parentCardId).toBeNull()
    expect(retained.id).toBe(child.id)
    expect(retained.columnId).toBe(child.columnId)
  })
}
