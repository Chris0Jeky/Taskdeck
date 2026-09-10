import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

test('a checked proposal highlights saved objects across board experiences without applying it', async ({ page, request }) => {
  test.setTimeout(120000)
  const auth = await registerAndAttachSession(page, request, 'board-preview')
  const headers = { Authorization: `Bearer ${auth.token}` }
  const seed = String(Date.now())
  const boardId = await createBoardWithColumn(request, auth, seed, { boardNamePrefix: 'Proposal overlays', columnNamePrefix: 'Next' })
  const board = await (await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })).json()
  const columnId = board.columns[0].id
  const createdCard = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, { headers, data: { boardId, columnId, title: 'Saved before preview' } })
  await assertOk(createdCard, 'seed saved card'); const card = await createdCard.json()
  const savedBoard = await (await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })).json()
  const created = await request.post(`${API_BASE_URL}/automation/proposals`, { headers, data: {
    sourceType: 1, requestedByUserId: auth.user.id, boardId, summary: 'Rename a card, position its column and propose a new card', riskLevel: 1, correlationId: `overlay-${seed}`,
    operations: [
      { sequence: 0, actionType: 'update', targetType: 'card', targetId: card.id, parameters: JSON.stringify({ cardId: card.id, title: 'Proposed new title' }), idempotencyKey: `overlay-${seed}-0` },
      { sequence: 1, actionType: 'reorder', targetType: 'column', targetId: columnId, parameters: JSON.stringify({ columnId, position: 0 }), idempotencyKey: `overlay-${seed}-1` },
      { sequence: 2, actionType: 'create', targetType: 'card', parameters: JSON.stringify({ boardId, columnId, title: 'New proposed card' }), idempotencyKey: `overlay-${seed}-2` },
    ],
  } })
  await assertOk(created, 'seed proposal'); const proposal = await created.json()
  const writes: string[] = []
  page.on('request', req => { if (req.method() !== 'GET' && /\/api\/(boards|automation\/proposals)/.test(req.url())) writes.push(req.url()) })
  const panel = page.getByRole('region', { name: 'Board proposal preview', exact: true })
  for (const [experience, theme] of [['classic', 'off'], ['studio', 'grove'], ['companion', 'grove'], ['unified', 'grove']]) {
    await page.goto(`/workspace/boards/${boardId}`)
    await page.evaluate(value => {
      localStorage.setItem('td.workspace.layout.v1', JSON.stringify({ experience: value[0], presentation: 'studio' }))
      localStorage.setItem('td.paper.mode.v2', value[1]!)
    }, [experience, theme])
    await page.goto(`/workspace/boards/${boardId}?proposalId=${proposal.id}`)
    await expect(page.locator(`[data-card-id="${card.id}"]`)).toBeVisible()
    await panel.getByRole('button', { name: 'Refresh board preview', exact: true }).click()
    await expect(panel.locator('pre')).toContainText('Proposed new title')
    await expect(panel.locator('pre')).toContainText('New proposed card')
    await expect(page.locator(`[data-card-id="${card.id}"]`)).toHaveAttribute('data-proposal-change', 'true')
    await expect(page.locator(`[data-column-id="${columnId}"]`)).toHaveAttribute('data-proposal-change', 'true')
    await expect(page.locator(`[data-card-id="${card.id}"]`)).toContainText('Saved before preview')
  }
  await page.setViewportSize({ width: 375, height: 812 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375)
  expect((await new AxeBuilder({ page }).include('[aria-label="Board proposal preview"]').analyze()).violations.filter(item => ['serious', 'critical'].includes(item.impact ?? ''))).toEqual([])
  await page.screenshot({ path: 'test-results/board-proposal-overlays-mobile.png', fullPage: true })
  await panel.getByRole('button', { name: 'Close preview', exact: true }).click()
  await expect(page.locator('[data-proposal-change]')).toHaveCount(0)
  expect(writes).toEqual([])
  expect(await (await request.get(`${API_BASE_URL}/boards/${boardId}/cards`, { headers })).json()).toEqual([card])
  expect((await (await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })).json()).columns).toEqual(savedBoard.columns)
  await page.goto(`/workspace/review?boardId=${boardId}`)
  await page.getByRole('link', { name: 'Preview on board', exact: true }).click()
  await expect(panel).toBeVisible()
  await page.route(`**/automation/proposals/${proposal.id}/preview`, route => route.fulfill({ status: 403, contentType: 'application/json', body: JSON.stringify({ message: 'Synthetic revoked access' }) }))
  await panel.getByRole('button', { name: 'Refresh board preview', exact: true }).click()
  await expect(panel.getByRole('status')).toBeVisible()
  await expect(page.locator('[data-proposal-change]')).toHaveCount(0)
})
