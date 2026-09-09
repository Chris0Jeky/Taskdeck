import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
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
  expect(payload.version).toBe(2)
  await assertOk(await request.post(`${API_BASE_URL}/import/boards/json`, { headers, data: payload }), 'import graph')
  await page.setViewportSize({ width: 375, height: 812 })
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  expect((await new AxeBuilder({ page }).include('[aria-label="Card dependencies"]').analyze()).violations).toEqual([])
  await page.screenshot({ path: '../../artifacts/overhaul/card-dependencies-mobile.png', fullPage: true })
  await region.getByRole('link', { name: 'Deliver the result', exact: true }).click()
  await page.getByRole('button', { name: 'Explore dependencies', exact: true }).click()
  await region.getByRole('button', { name: 'Remove prerequisite Prepare the source', exact: true }).click()
  await expect(region.getByText('No prerequisites chosen.', { exact: true })).toBeVisible()
  expect(await (await request.get(`${API_BASE_URL}/boards/${boardId}/cards`, { headers })).json()).toEqual(before)
})
