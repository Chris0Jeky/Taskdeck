import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

test('saved thinking, a pending send and receipt recovery stay coherent in the card companion', async ({ page, request }) => {
  test.setTimeout(90000)
  const auth = await registerAndAttachSession(page, request, 'companion-continuity')
  const headers = { Authorization: `Bearer ${auth.token}` }
  await page.addInitScript(() => {
    localStorage.setItem('td.workspace.layout.v1', JSON.stringify({ experience: 'companion', presentation: 'studio' }))
    localStorage.setItem('td.paper.mode.v2', 'grove')
  })
  const boardId = await createBoardWithColumn(request, auth, String(Date.now()), { boardNamePrefix: 'Continuity', columnNamePrefix: 'Next' })
  const board = await (await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })).json()
  const response = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, { headers, data: { boardId, columnId: board.columns[0].id, title: 'Keep this thought' } })
  await assertOk(response, 'seed continuity card'); const card = await response.json()
  await page.goto(`/workspace/boards/${boardId}/cards/${card.id}/thinking`)
  await page.getByRole('button', { name: 'Open card companion', exact: true }).click()
  const companion = page.getByRole('region', { name: 'Card companion', exact: true })
  await companion.getByLabel('Session title', { exact: true }).fill('Stay with the thought')
  await companion.getByRole('button', { name: 'Create Session', exact: true }).click()
  await companion.getByRole('button', { name: 'Choose sources', exact: true }).click()
  await companion.getByLabel('Context card', { exact: true }).selectOption(card.id)
  await companion.getByLabel('Include shared thinking for this card', { exact: true }).check()
  await page.getByRole('button', { name: '+ note', exact: true }).click()
  await page.getByLabel('Layer 1 details', { exact: true }).fill('Keep the saved uncertainty in context.')
  const instruction = companion.getByLabel('Automation instruction')
  await instruction.fill('Help me think through this uncertainty.')
  await expect(companion.getByRole('button', { name: 'Send Message', exact: true })).toBeDisabled()
  let posts = 0
  page.on('request', req => { if (req.method() === 'POST' && /chat\/sessions\/[^/]+\/messages$/.test(new URL(req.url()).pathname)) posts++ })
  await instruction.press('Control+Enter')
  await expect(companion.getByText(/Save your shared thinking before sending/)).toBeVisible()
  expect(posts).toBe(0)
  await page.getByRole('button', { name: 'Save thinking', exact: true }).click()
  await expect(companion.getByRole('button', { name: 'Send Message', exact: true })).toBeEnabled()

  let release!: () => void
  const pending = new Promise<void>(resolve => { release = resolve })
  let failReceipt = false
  await page.route(/\/chat\/sessions\/[^/]+\/messages$/, async route => {
    if (route.request().method() !== 'POST') return route.continue()
    const savedResponse = await route.fetch()
    await pending
    failReceipt = true
    await route.fulfill({ response: savedResponse })
  })
  await page.route(/\/chat\/sessions\/[^/]+$/, async route => {
    if (route.request().method() === 'GET' && failReceipt) {
      failReceipt = false
      return route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'Synthetic receipt interruption' }) })
    }
    await route.continue()
  })
  await companion.getByRole('button', { name: 'Send Message', exact: true }).click()
  await page.getByRole('link', { name: 'Choose work for your personal plan', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('A companion message is still sending')
  await expect(dialog.getByRole('button', { name: 'Discard draft and leave', exact: true })).toBeDisabled()
  await dialog.getByRole('button', { name: 'Keep editing', exact: true }).click()
  release()
  await expect(companion.getByRole('button', { name: 'Retry receipt refresh', exact: true })).toBeVisible()
  await companion.getByRole('button', { name: 'Retry receipt refresh', exact: true }).click()
  await expect(companion.getByText('Sources included in this turn (2)', { exact: true })).toBeVisible()
  expect(posts).toBe(1)
  await page.setViewportSize({ width: 375, height: 812 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375)
  expect((await new AxeBuilder({ page }).include('[aria-label="Card companion"]').analyze()).violations.filter(item => ['serious', 'critical'].includes(item.impact ?? ''))).toEqual([])
  await page.screenshot({ path: 'test-results/companion-continuity-mobile.png', fullPage: true })
  await page.getByRole('link', { name: 'Choose work for your personal plan', exact: true }).click()
  await expect(page).toHaveURL(/\/workspace\/plan$/)
  expect(await (await request.get(`${API_BASE_URL}/boards/${boardId}/cards`, { headers })).json()).toEqual([card])
})
