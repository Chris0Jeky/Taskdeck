import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'

test('optional reminders respect consent, quiet time, Zen and the shared account budget', async ({ page, request }) => {
  test.setTimeout(120000)
  const auth = await registerAndAttachSession(page, request, 'attention')
  const headers = { Authorization: `Bearer ${auth.token}` }
  const board = await createBoardWithColumn(request, auth, String(Date.now()), { boardNamePrefix: 'Attention', columnNamePrefix: 'Next' })
  const memory = await request.post(`${API_BASE_URL}/workspace-memory`, { headers,
    data: { boardId: board, title: 'Check the experiment', text: 'The result needs another look.', status: 'needsReview' } })
  expect(memory.ok()).toBe(true)
  const analyzed = await request.post(`${API_BASE_URL}/workspace-insights/analyze`, { headers, data: { boardId: board } })
  expect(analyzed.ok()).toBe(true)
  let claims = 0; let models = 0
  page.on('request', sent => {
    if (sent.url().endsWith('/workspace-attention/claim')) claims++
    if (sent.url().includes('/model-analysis')) models++
  })
  await page.clock.install()
  await page.goto(`/workspace/boards/${board}`)
  await page.getByRole('heading', { name: /Attention/ }).first().waitFor()
  await page.clock.fastForward(60_000)
  const off = (await (await request.get(`${API_BASE_URL}/workspace-attention`, { headers })).json())
  expect(off.enabled).toBe(false); expect(claims).toBe(0)
  await page.goto(`/workspace/insights?boardId=${board}`)
  const settings = page.getByRole('region', { name: 'Optional reminders' })
  await expect(settings.getByRole('checkbox')).not.toBeChecked()
  const saved = page.waitForResponse(response => response.url().endsWith('/workspace-attention') && response.request().method() === 'PUT')
  await settings.getByRole('checkbox').check(); expect((await saved).ok()).toBe(true)
  await page.evaluate(() => localStorage.setItem('td.workspace.layout.v1', JSON.stringify({ experience: 'unified', presentation: 'zen' })))
  await page.goto(`/workspace/boards/${board}`)
  await page.clock.fastForward(120_000)
  expect(claims).toBe(0)
  await page.evaluate(() => localStorage.setItem('td.workspace.layout.v1', JSON.stringify({ experience: 'companion', presentation: 'studio' })))
  await page.reload()
  await page.getByRole('heading', { name: /Attention/ }).first().waitFor()
  const claimed = page.waitForResponse(response => response.url().endsWith('/workspace-attention/claim'))
  await page.clock.fastForward(60_000); expect((await claimed).status()).toBe(200)
  const reminder = page.getByRole('complementary', { name: 'Saved question reminder' })
  await expect(reminder).toBeVisible()
  await page.setViewportSize({ width: 375, height: 812 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375)
  expect((await new AxeBuilder({ page }).include('[aria-label="Saved question reminder"]').analyze()).violations.filter(item => ['serious', 'critical'].includes(item.impact ?? ''))).toEqual([])
  await page.screenshot({ path: 'test-results/attention-mobile.png', fullPage: true })
  await reminder.getByRole('button', { name: 'Dismiss this reminder' }).click()
  await page.clock.fastForward(300_000)
  await expect(reminder).toHaveCount(0)
  expect((await request.post(`${API_BASE_URL}/workspace-attention/claim`, { headers, data: { boardId: board } })).status()).toBe(204)
  const exportData = await (await request.get(`${API_BASE_URL}/account/export`, { headers })).json()
  expect(exportData.data.preferences.attention.count).toBe(1)
  expect(models).toBe(0)
})
