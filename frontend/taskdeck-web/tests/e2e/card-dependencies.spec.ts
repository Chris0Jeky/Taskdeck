import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { API_BASE_URL, registerAndAttachSession, registerUserSession, attachSessionToPage } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

test('keeps explicit prerequisites across experiences, navigation and portable import', async ({ page, request }) => {
  test.setTimeout(90_000)
  const auth = await registerAndAttachSession(page, request, 'card-dependencies')
  const headers = { Authorization: `Bearer ${auth.token}` }
  await page.addInitScript(() => { localStorage.setItem('td.paper.mode.v2', 'grove') })
  const boardId = await createBoardWithColumn(request, auth, String(Date.now()), { boardNamePrefix: 'Connections', columnNamePrefix: 'Next' })
  const board = await (await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })).json() as { columns: { id: string }[] }
  async function create(title: string) {
    const response = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, { headers, data: { boardId, columnId: board.columns[0]!.id, title } })
    await assertOk(response, 'create dependency card'); return await response.json() as { id: string }
  }
  const a = await create('Deliver the result'); const b = await create('Prepare the source')
  const before = await (await request.get(`${API_BASE_URL}/boards/${boardId}/cards`, { headers })).json()
  await page.goto(`/workspace/boards/${boardId}/cards/${a.id}/thinking`)
  await page.getByRole('button', { name: 'Explore dependencies', exact: true }).click()
  const region = page.getByRole('region', { name: 'Card dependencies' })
  await region.getByLabel('Prerequisite card', { exact: true }).selectOption(b.id)
  await region.getByRole('button', { name: 'Add prerequisite', exact: true }).click()
  await expect(region.getByRole('link', { name: 'Prepare the source', exact: true })).toBeVisible()
  for (const experience of ['classic', 'studio', 'companion', 'unified']) {
    await page.getByLabel('Workspace experience', { exact: true }).selectOption(experience)
    await expect(region.getByRole('link', { name: 'Prepare the source', exact: true })).toBeVisible()
  }
  await page.reload()
  await page.getByRole('button', { name: 'Explore dependencies', exact: true }).click()
  await region.getByRole('link', { name: 'Prepare the source', exact: true }).click()
  await page.getByRole('button', { name: 'Explore dependencies', exact: true }).click()
  await expect(region.getByRole('link', { name: 'Deliver the result', exact: true })).toBeVisible()
  await region.getByLabel('Prerequisite card', { exact: true }).selectOption(a.id)
  await region.getByRole('button', { name: 'Add prerequisite', exact: true }).click()
  await expect(region.getByRole('alert')).toContainText(/cycle/i)
  await region.getByRole('button', { name: 'Reload dependencies', exact: true }).click()
  await expect(region.getByRole('link', { name: 'Deliver the result', exact: true })).toBeVisible()
  const exported = await request.get(`${API_BASE_URL}/export/boards/${boardId}/json`, { headers })
  await assertOk(exported, 'export graph')
  const payload = await exported.json()
  expect(payload.version).toBe(5)
  await assertOk(await request.post(`${API_BASE_URL}/import/boards/json`, { headers, data: payload }), 'import graph')
  await page.setViewportSize({ width: 375, height: 812 })
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  expect((await new AxeBuilder({ page }).include('[aria-label="Card dependencies"]').analyze()).violations).toEqual([])
  await page.screenshot({ path: '../../artifacts/overhaul/card-dependencies-mobile.png', fullPage: true })
  await region.getByRole('link', { name: 'Deliver the result', exact: true }).click()
  await page.getByRole('button', { name: 'Explore dependencies', exact: true }).click()
  await region.getByRole('button', { name: 'Remove prerequisite Prepare the source', exact: true }).click()
  await expect(region.getByText('No active prerequisites shown.', { exact: true })).toBeVisible()
  expect(await (await request.get(`${API_BASE_URL}/boards/${boardId}/cards`, { headers })).json()).toEqual(before)
})

for (const theme of ['paper', 'legacy'] as const) {
  test(`${theme}: archived card dependencies remain read-only until restore is confirmed`, async ({ page, request, browser }, testInfo) => {
    const owner = await registerAndAttachSession(page, request, `dep-arc-${theme}`, { theme })
    const headers = { Authorization: `Bearer ${owner.token}` }
    const boardId = await createBoardWithColumn(request, owner, `${Date.now()}-${theme}`, { boardNamePrefix: 'Archive connections', description: 'Synthetic archived dependency proof', columnNamePrefix: 'Next' })
    const board = await (await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })).json()
    expect(board.isArchived).toBe(false)
    const create = async (title: string) => {
      const response = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, {
        headers, data: { boardId, columnId: board.columns[0].id, title },
      })
      await assertOk(response, 'Create dependency archive fixture')
      return await response.json()
    }
    const card = await create('Archive this connected task')
    const prerequisite = await create('Retain this prerequisite')
    await assertOk(await request.put(`${API_BASE_URL}/boards/${boardId}/dependencies`, {
      headers, data: { expectedRevision: 0, edges: [{ cardId: card.id, dependsOnCardId: prerequisite.id }] },
    }), 'Set explicit dependency')
    const archivedResponse = await request.post(`${API_BASE_URL}/boards/${boardId}/cards/${card.id}/archive`, {
      headers, data: { expectedUpdatedAt: card.updatedAt },
    })
    await assertOk(archivedResponse, 'Archive connected task')
    const archived = await archivedResponse.json()
    const writes: string[] = []
    page.on('request', response => {
      if (response.method() === 'PUT' && response.url().endsWith(`/boards/${boardId}/dependencies`)) writes.push(response.url())
    })
    const route = `/workspace/boards/${boardId}/cards/${card.id}/thinking`
    await page.goto(route)
    await page.getByRole('button', { name: 'Explore dependencies', exact: true }).click()
    const region = page.getByRole('region', { name: 'Card dependencies' })
    await expect(region.getByText('No active prerequisites shown.', { exact: true })).toBeVisible()
    await expect(region).toContainText('Connections involving archived cards are hidden until those cards are restored.')
    await expect(region).toContainText('Editing needs an active card and board write access')
    await expect(region.getByLabel('Prerequisite card', { exact: true })).toHaveCount(0)
    await expect(region.getByRole('button', { name: 'Add prerequisite', exact: true })).toHaveCount(0)
    await expect(region.getByRole('button', { name: /Remove prerequisite/ })).toHaveCount(0)
    await page.screenshot({ path: testInfo.outputPath('archived-dependencies.png'), fullPage: true, animations: 'disabled' })
    expect(writes).toEqual([])
    await assertOk(await request.post(`${API_BASE_URL}/boards/${boardId}/cards/${card.id}/restore`, {
      headers, data: { expectedUpdatedAt: archived.updatedAt },
    }), 'Restore connected task')
    // A fresh current-card read, rather than the old board graph, re-enables editing - and
    // Refresh dependencies performs that read in place, with no page or route reload (#2958).
    await region.getByRole('button', { name: 'Refresh dependencies', exact: true }).click()
    await expect(region.getByRole('link', { name: prerequisite.title, exact: true })).toBeVisible()
    await expect(region.getByRole('button', { name: `Remove prerequisite ${prerequisite.title}`, exact: true })).toBeEnabled()
    expect(writes).toEqual([])

    const viewer = await registerUserSession(request, `dep-view-${theme}`)
    await assertOk(await request.post(`${API_BASE_URL}/boards/${boardId}/access`, {
      headers, data: { boardId, userId: viewer.user.id, role: 3 },
    }), 'Grant read-only access')
    const viewerContext = await browser.newContext({ baseURL: new URL(page.url()).origin })
    try {
      const viewerPage = await viewerContext.newPage()
      await attachSessionToPage(viewerPage, viewer, { theme })
      await viewerPage.goto(route)
      await viewerPage.getByRole('button', { name: 'Explore dependencies', exact: true }).click()
      const viewerRegion = viewerPage.getByRole('region', { name: 'Card dependencies' })
      await expect(viewerRegion.getByRole('link', { name: prerequisite.title, exact: true })).toBeVisible()
      await expect(viewerRegion.getByLabel('Prerequisite card', { exact: true })).toHaveCount(0)
      await expect(viewerRegion.getByRole('button', { name: /Remove prerequisite/ })).toHaveCount(0)
    } finally { await viewerContext.close() }
    await region.getByRole('button', { name: `Remove prerequisite ${prerequisite.title}`, exact: true }).click()
    await expect(region.getByText('No active prerequisites shown.', { exact: true })).toBeVisible()
    expect(writes).toHaveLength(1)
  })
}
