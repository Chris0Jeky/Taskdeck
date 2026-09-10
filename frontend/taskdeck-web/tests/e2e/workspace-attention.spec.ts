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
  await expect(settings.getByRole('checkbox', { name: 'Enable occasional reminders', exact: true })).not.toBeChecked()
  const saved = page.waitForResponse(response => response.url().endsWith('/workspace-attention') && response.request().method() === 'PUT')
  await settings.getByRole('checkbox', { name: 'Enable occasional reminders', exact: true }).check(); expect((await saved).ok()).toBe(true)
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

test('reminder hours persist across experiences and recover a concurrent settings change', async ({ page, request }) => {
  const auth = await registerAndAttachSession(page, request, 'reminder-hours')
  const headers = { Authorization: `Bearer ${auth.token}` }
  const board = await createBoardWithColumn(request, auth, String(Date.now()), { boardNamePrefix: 'Reminder hours', columnNamePrefix: 'Next' })
  await page.goto(`/workspace/insights?boardId=${board}`)
  const settings = page.getByRole('region', { name: 'Optional reminders' })
  await settings.getByRole('checkbox', { name: 'Only offer new reminders during selected hours', exact: true }).check()
  await settings.getByLabel('Time zone', { exact: true }).fill('Europe/London')
  for (const day of ['Tuesday', 'Wednesday', 'Thursday', 'Friday']) await settings.getByRole('checkbox', { name: day, exact: true }).uncheck()
  await settings.getByLabel('Start time', { exact: true }).fill('22:00')
  await settings.getByLabel('End time', { exact: true }).fill('02:00')
  for (const enabled of [true, false]) {
    const toggled = page.waitForResponse(response => response.url().endsWith('/workspace-attention') && response.request().method() === 'PUT')
    await settings.getByRole('checkbox', { name: 'Enable occasional reminders', exact: true }).setChecked(enabled)
    const toggleReceipt = await toggled; expect(toggleReceipt.ok()).toBe(true)
    expect((await toggleReceipt.json()).window).toBeNull()
    await expect(settings.getByLabel('Time zone', { exact: true })).toHaveValue('Europe/London')
    await expect(settings.getByLabel('Start time', { exact: true })).toHaveValue('22:00')
    await expect(settings.getByLabel('End time', { exact: true })).toHaveValue('02:00')
  }
  const saved = page.waitForResponse(response => response.url().endsWith('/workspace-attention') && response.request().method() === 'PUT')
  await settings.getByRole('button', { name: 'Save reminder hours', exact: true }).click()
  const receipt = await saved; expect(receipt.ok()).toBe(true)
  const value = await receipt.json()
  expect(value.enabled).toBe(false)
  expect(value.window).toEqual({ timeZoneId: 'Europe/London', daysMask: 2, startMinute: 1320, endMinute: 120 })
  for (const experience of ['classic', 'studio', 'companion', 'unified']) {
    await page.getByLabel('Workspace experience', { exact: true }).selectOption(experience)
    await expect(settings.getByLabel('Time zone', { exact: true })).toHaveValue('Europe/London')
  }
  await page.reload()
  await expect(settings.getByLabel('Start time', { exact: true })).toHaveValue('22:00')
  const concurrent = await request.put(`${API_BASE_URL}/workspace-attention`, { headers, data: {
    expectedRevision: value.revision, enabled: false, updateWindow: true,
    window: { timeZoneId: 'UTC', daysMask: 62, startMinute: 540, endMinute: 1020 },
  } })
  expect(concurrent.ok()).toBe(true)
  await settings.getByLabel('Time zone', { exact: true }).fill('America/New_York')
  const conflict = page.waitForResponse(response => response.url().endsWith('/workspace-attention') && response.request().method() === 'PUT')
  await settings.getByRole('button', { name: 'Save reminder hours', exact: true }).click()
  expect((await conflict).status()).toBe(409)
  await expect(settings).toContainText('could not be confirmed')
  await settings.getByRole('button', { name: 'Reload reminder preference', exact: true }).click()
  await expect(settings.getByLabel('Time zone', { exact: true })).toHaveValue('UTC')
  await expect(settings.getByLabel('Start time', { exact: true })).toHaveValue('09:00')
  await page.setViewportSize({ width: 375, height: 812 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375)
  expect((await new AxeBuilder({ page }).include('[aria-label="Optional reminders"]').analyze()).violations.filter(item => ['serious', 'critical'].includes(item.impact ?? ''))).toEqual([])
  await page.screenshot({ path: 'test-results/reminder-hours-mobile.png', fullPage: true })
  for (const theme of ['grove', 'grove-night']) {
    await page.evaluate(value => localStorage.setItem('td.paper.mode.v2', value), theme)
    await page.reload()
    await expect(settings.getByLabel('Time zone', { exact: true })).toHaveValue('UTC')
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375)
    expect((await new AxeBuilder({ page }).include('[aria-label="Optional reminders"]').analyze()).violations.filter(item => ['serious', 'critical'].includes(item.impact ?? ''))).toEqual([])
    await page.screenshot({ path: `test-results/reminder-hours-${theme}-mobile.png`, fullPage: true })
  }
})
