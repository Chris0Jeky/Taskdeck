import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

test('Companion explicitly selects an original answer and retains its receipt across experiences', async ({ page, request }) => {
  test.setTimeout(90000)
  const auth = await registerAndAttachSession(page, request, 'original-context')
  const headers = { Authorization: `Bearer ${auth.token}` }
  await page.addInitScript(() => {
    if (!localStorage.getItem('td.workspace.layout.v1')) localStorage.setItem('td.workspace.layout.v1', JSON.stringify({ experience: 'companion', presentation: 'studio' }))
    localStorage.setItem('td.paper.mode.v2', 'grove')
  })
  const boardId = await createBoardWithColumn(request, auth, String(Date.now()), { boardNamePrefix: 'Original source context', columnNamePrefix: 'Next' })
  const board = await (await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })).json()
  const cardResponse = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, { headers, data: { boardId, columnId: board.columns[0].id, title: 'Consider original evidence' } })
  await assertOk(cardResponse, 'seed source context card'); const card = await cardResponse.json()
  const memoryResponse = await request.post(`${API_BASE_URL}/workspace-memory`, { headers, data: { boardId, title: 'Original uncertainty', text: '  The original answer was uncertain.  ', status: 'unknown' } })
  await assertOk(memoryResponse, 'seed original answer'); const memory = await memoryResponse.json()
  await assertOk(await request.post(`${API_BASE_URL}/workspace-memory`, { headers, data: { boardId, title: 'Independent evidence', text: 'A different memory, not selected.', status: 'statement' } }), 'seed distinguishable source picker')
  await assertOk(await request.put(`${API_BASE_URL}/workspace-memory/${memory.id}`, { headers, data: { title: memory.title, text: 'The answer has been corrected.', status: 'needsReview', revision: 1 } }), 'correct original answer')
  for (let revision = 2; revision < 12; revision++) {
    await assertOk(await request.put(`${API_BASE_URL}/workspace-memory/${memory.id}`, { headers, data: { title: memory.title, text: `Historical correction ${revision}`, status: 'needsReview', revision } }), 'retain paged original history')
  }
  await page.goto(`/workspace/boards/${boardId}/cards/${card.id}/thinking`)
  await page.getByRole('button', { name: 'Open card companion', exact: true }).click()
  const companion = page.getByRole('region', { name: 'Card companion', exact: true })
  await companion.getByLabel('Session title', { exact: true }).fill('Compare original evidence')
  await companion.getByRole('button', { name: 'Create Session', exact: true }).click()
  await companion.getByRole('button', { name: 'Choose sources', exact: true }).click()
  await expect(companion.getByRole('button', { name: 'Choose original sources for Independent evidence', exact: true })).toBeVisible()
  await companion.getByRole('button', { name: 'Choose original sources for Original uncertainty', exact: true }).click()
  await companion.getByLabel(/answer-revision-1.txt/).check()
  await assertOk(await request.put(`${API_BASE_URL}/workspace-memory/${memory.id}`, { headers, data: { title: memory.title, text: 'A concurrent correction after source selection.', status: 'needsReview', revision: 12 } }), 'revise memory after selection')
  await companion.getByRole('button', { name: 'Load more originals for Original uncertainty', exact: true }).click()
  await expect(companion.getByRole('alert')).toContainText('This memory changed. Refresh sources')
  await expect(companion.getByLabel(/answer-revision-1.txt/)).toHaveCount(0)
  await companion.getByRole('button', { name: 'Refresh sources and clear selection', exact: true }).click()
  await companion.getByRole('button', { name: 'Choose original sources for Original uncertainty', exact: true }).click()
  const nextPage = page.waitForRequest(req => req.method() === 'GET' && req.url().includes(`/context-memory/${memory.id}/sources`) && new URL(req.url()).searchParams.get('afterOrdinal') === '9')
  await companion.getByRole('button', { name: 'Load more originals for Original uncertainty', exact: true }).click()
  await nextPage
  await expect(companion.getByLabel(/answer-revision-13.txt/)).toBeVisible()
  await companion.getByLabel(/answer-revision-1.txt/).check()
  await companion.getByLabel('Automation instruction').fill('What did I originally say?')
  const sent = page.waitForRequest(req => req.method() === 'POST' && /chat\/sessions\/[^/]+\/messages$/.test(new URL(req.url()).pathname))
  await companion.getByRole('button', { name: 'Send Message', exact: true }).click()
  const selection = (await sent).postDataJSON().context
  expect(selection.memories).toEqual([]); expect(selection.assets).toHaveLength(1)
  expect(selection.assets[0]).toMatchObject({ memoryId: memory.id, revision: 13 })
  expect(selection.assets[0].contentHash).toMatch(/^[a-f0-9]{64}$/)
  await expect(companion.getByText('Sources included in this turn (1)', { exact: true })).toBeVisible()
  for (const experience of ['classic', 'studio', 'companion', 'unified']) {
    await page.evaluate(value => localStorage.setItem('td.workspace.layout.v1', JSON.stringify({ experience: value, presentation: 'studio' })), experience)
    await page.reload(); await page.getByRole('button', { name: 'Open card companion', exact: true }).click()
    await companion.locator('.td-context-receipt summary').first().click()
    await expect(companion.locator('.td-context-receipt')).toContainText('superseded historical source')
    await expect(companion.locator('.td-context-receipt')).toContainText('answer-revision-1.txt')
  }
  await page.setViewportSize({ width: 375, height: 812 })
  await companion.getByRole('button', { name: 'Choose sources', exact: true }).click()
  const sourcesUrl = /\/context-memory\/[^/]+\/sources\?/
  await page.route(sourcesUrl, route => route.fulfill({ status: 403, contentType: 'application/json', body: JSON.stringify({ message: 'Synthetic revoked source access' }) }))
  await companion.getByRole('button', { name: 'Choose original sources for Original uncertainty', exact: true }).click()
  await expect(companion.getByRole('alert')).toContainText('no longer have access')
  await expect(companion.getByLabel(/answer-revision-1.txt/)).toHaveCount(0)
  await page.unroute(sourcesUrl)
  await companion.getByRole('button', { name: 'Choose original sources for Original uncertainty', exact: true }).click()
  await expect(companion.getByLabel(/answer-revision-1.txt/)).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375)
  expect((await new AxeBuilder({ page }).include('[aria-label="Card companion"]').analyze()).violations.filter(item => ['serious', 'critical'].includes(item.impact ?? ''))).toEqual([])
  await page.screenshot({ path: 'test-results/original-source-context-mobile.png', fullPage: true })
  expect(await (await request.get(`${API_BASE_URL}/boards/${boardId}/cards`, { headers })).json()).toEqual([card])
})
