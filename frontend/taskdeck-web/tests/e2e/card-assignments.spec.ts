import { expect, test } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession, registerUserSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

for (const theme of ['paper', 'legacy'] as const) {
  test(`${theme}: explicit assignment, retained draft, conflict recovery and import mapping`, async ({ page, request }) => {
    test.setTimeout(90_000)
    const auth = await registerAndAttachSession(page, request, `assign-${theme}`, { theme })
    const viewer = await registerUserSession(request, 'assign-viewer')
    const headers = { Authorization: `Bearer ${auth.token}` }
    const boardId = await createBoardWithColumn(request, auth, `${Date.now()}-${theme}`, {
      boardNamePrefix: 'Assignment proof', columnNamePrefix: 'Next', description: 'Synthetic assignments',
    })
    const board = await (await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })).json()
    await assertOk(await request.post(`${API_BASE_URL}/boards/${boardId}/access`, {
      headers, data: { boardId, userId: viewer.user.id, role: 3 },
    }), 'Grant viewer participation')
    const created = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, {
      headers, data: { columnId: board.columns[0].id, title: 'Assignment responsibility' },
    })
    await assertOk(created, 'Create card'); const card = await created.json()
    const cardUrl = `${API_BASE_URL}/boards/${boardId}/cards/${card.id}`
    const read = async () => (await request.get(cardUrl, { headers })).json()
    await page.goto(`/workspace/boards/${boardId}`)
    if (theme === 'paper') await page.getByRole('button', { name: 'Card Assignment responsibility', exact: true }).click()
    else await page.getByText('Assignment responsibility', { exact: true }).click()
    const editor = page.getByRole('dialog', { name: 'Edit Card', exact: true })
    const assignments = editor.getByRole('region', { name: 'Card assignments' })
    await editor.locator('#card-title').fill('Kept title draft')
    await assignments.getByRole('checkbox', { name: auth.user.username, exact: true }).check()
    await assignments.getByRole('checkbox', { name: viewer.user.username, exact: true }).check()
    await assignments.getByRole('button', { name: 'Save assignments', exact: true }).click()
    await expect.poll(async () => (await read()).assignments.length).toBe(2)
    await expect(editor.locator('#card-title')).toHaveValue('Kept title draft')
    await editor.getByRole('button', { name: 'Save Changes', exact: true }).click()
    await expect(editor).not.toBeVisible()
    await expect.poll(async () => (await read()).title).toBe('Kept title draft')
    if (theme === 'paper') await page.getByRole('button', { name: 'Card Kept title draft', exact: true }).click()
    else await page.getByText('Kept title draft', { exact: true }).click()
    const current = await read()
    await assignments.getByRole('button', { name: 'Clear', exact: true }).click()
    await assertOk(await request.patch(cardUrl, { headers, data: { description: 'Concurrent edit', expectedUpdatedAt: current.updatedAt } }), 'Competing edit')
    const refused = page.waitForResponse(r => r.url().endsWith(`/cards/${card.id}/assignments`) && r.request().method() === 'PUT')
    await assignments.getByRole('button', { name: 'Save assignments', exact: true }).click()
    expect((await refused).status()).toBe(409)
    await assignments.getByRole('button', { name: 'Refresh current assignments' }).click()
    await expect(assignments.getByRole('checkbox', { name: auth.user.username, exact: true })).not.toBeChecked()
    await assignments.getByRole('button', { name: 'Save assignments', exact: true }).click()
    await expect.poll(async () => (await read()).assignments.length).toBe(0)

    await page.goto('/workspace/settings/export-import')
    await page.getByRole('button', { name: 'Import', exact: true }).click()
    const source = {
      name: 'Explicit mapped board', columns: [{ name: 'Next', position: 0 }], labels: [],
      cards: [{ title: 'Mapped responsibility', columnName: 'Next', position: 0, sourceAssignees: [
        { sourceKey: 'source-one', displayName: 'Source Alex' }, { sourceKey: 'source-two', displayName: 'Source Blair' },
      ] }],
    }
    await page.getByLabel('Board JSON', { exact: true }).fill(JSON.stringify(source))
    await page.getByRole('button', { name: 'Validate & Preview' }).click()
    await expect(page.getByText('Mapped responsibility — Next', { exact: true })).toBeVisible()
    const apply = page.getByRole('button', { name: 'Import Board', exact: true })
    await expect(apply).toBeDisabled()
    await page.getByLabel('Map Source Alex', { exact: true }).selectOption(auth.user.id)
    await expect(apply).toBeDisabled()
    await page.getByLabel('Map Source Blair', { exact: true }).selectOption(auth.user.id)
    const applied = page.waitForResponse(r => r.url().endsWith('/api/import/boards') && r.request().method() === 'POST')
    await apply.click()
    const result = await (await applied).json()
    await expect(page.getByText('Board imported successfully', { exact: true }).first()).toBeVisible()
    const imported = await (await request.get(`${API_BASE_URL}/boards/${result.boardId}/cards`, { headers })).json()
    expect(imported).toHaveLength(1)
    expect(imported[0].assignments).toHaveLength(1)
    expect(imported[0].assignments[0].userId).toBe(auth.user.id)
  })

  /*
   * #2981. A submitted assignment PUT cannot be recalled, so no close path may
   * offer to discard it. This holds the real request open and drives the real
   * editor: the discard confirmation must never appear, the editor must stay
   * open, and the server must end up holding exactly the change the user saved.
   */
  test(`${theme}: a delayed assignment save is never offered as discardable`, async ({ page, request }) => {
    test.setTimeout(90_000)
    const auth = await registerAndAttachSession(page, request, `assign-pending-${theme}`, { theme })
    const headers = { Authorization: `Bearer ${auth.token}` }
    const boardId = await createBoardWithColumn(request, auth, `${Date.now()}-pending-${theme}`, {
      boardNamePrefix: 'Assignment in flight', columnNamePrefix: 'Next', description: 'Synthetic pending save',
    })
    const board = await (await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })).json()
    const created = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, {
      headers, data: { columnId: board.columns[0].id, title: 'Pending assignment' },
    })
    await assertOk(created, 'Create card'); const card = await created.json()
    const read = async () => (await request.get(`${API_BASE_URL}/boards/${boardId}/cards/${card.id}`, { headers })).json()

    // Hold the real PUT open until the close paths have been exercised.
    let release!: () => void
    const held = new Promise<void>(resolve => { release = resolve })
    await page.route('**/cards/*/assignments', async route => {
      if (route.request().method() !== 'PUT') { await route.fallback(); return }
      await held
      await route.continue()
    })

    await page.goto(`/workspace/boards/${boardId}`)
    if (theme === 'paper') await page.getByRole('button', { name: 'Card Pending assignment', exact: true }).click()
    else await page.getByText('Pending assignment', { exact: true }).click()
    const editor = page.getByRole('dialog', { name: 'Edit Card', exact: true })
    const assignments = editor.getByRole('region', { name: 'Card assignments' })
    await assignments.getByRole('checkbox', { name: auth.user.username, exact: true }).check()
    await assignments.getByRole('button', { name: 'Save assignments', exact: true }).click()
    await expect(assignments.getByText('cannot be discarded', { exact: false })).toBeVisible()

    // Header close, then Escape: both answer truthfully and neither closes.
    await editor.getByRole('button', { name: 'Close card editor', exact: true }).click()
    await expect(page.getByText('already sent to the server', { exact: false })).toBeVisible()
    await expect(page.getByTestId('card-discard-confirm')).toHaveCount(0)
    await page.getByTestId('card-assignment-save-pending-dismiss').click()
    await page.keyboard.press('Escape')
    await expect(page.getByText('already sent to the server', { exact: false })).toBeVisible()
    await expect(editor).toBeVisible()
    await page.getByTestId('card-assignment-save-pending-dismiss').click()

    // Settling commits the change the user was never allowed to "discard",
    // withdraws the notice and restores the close path.
    release()
    await expect.poll(async () => (await read()).assignments.length).toBe(1)
    await expect(page.getByText('already sent to the server', { exact: false })).toHaveCount(0)
    await expect(assignments.getByText(auth.user.username, { exact: false }).first()).toBeVisible()
    await editor.getByRole('button', { name: 'Close card editor', exact: true }).click()
    await expect(editor).not.toBeVisible()
    await page.unroute('**/cards/*/assignments')
  })
}
