import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { readFile } from 'node:fs/promises'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'

test('private originals survive correction, archive, layout switches and a downloaded export', async ({ page, request }) => {
  test.setTimeout(90000)
  const auth = await registerAndAttachSession(page, request, 'private-originals')
  const headers = { Authorization: `Bearer ${auth.token}` }
  await page.addInitScript(() => {
    if (!localStorage.getItem('td.workspace.layout.v1')) localStorage.setItem('td.workspace.layout.v1', JSON.stringify({ experience: 'studio', presentation: 'studio' }))
    localStorage.setItem('td.paper.mode.v2', 'grove')
  })
  const boardId = await createBoardWithColumn(request, auth, String(Date.now()), { boardNamePrefix: 'Private originals', columnNamePrefix: 'Next' })
  await page.goto(`/workspace/memory?boardId=${boardId}`)
  await page.getByRole('button', { name: 'Add memory', exact: true }).click()
  await page.getByLabel('Title', { exact: true }).fill('A useful uncertainty')
  await page.getByLabel('Memory', { exact: true }).fill('  Keep this original exactly.\nStill unknown.  ')
  await page.getByLabel('Status', { exact: true }).selectOption('unknown')
  await page.getByRole('button', { name: 'Save memory', exact: true }).click()
  const memory = page.locator('.paper-memory__card')
  await expect(memory).toHaveCount(1)
  await memory.getByRole('button', { name: 'Correct', exact: true }).click()
  await page.getByLabel('Memory', { exact: true }).fill('We learned enough to take one small step.')
  await page.getByRole('button', { name: 'Save correction', exact: true }).click()
  await expect(memory).toContainText('Revision 2')
  await memory.locator('summary').filter({ hasText: 'Preserved originals' }).click()
  await memory.getByRole('button', { name: 'Load originals', exact: true }).click()
  await expect(memory.locator('pre')).toHaveCount(2)
  expect(await memory.locator('pre').first().textContent()).toBe('  Keep this original exactly.\nStill unknown.  ')
  await expect(memory).toContainText('Superseded; original preserved')
  const downloading = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Download memory JSON', exact: true }).click()
  const download = await downloading
  const exported = JSON.parse(await readFile((await download.path())!, 'utf8'))
  expect(exported.version).toBe(2)
  expect(exported.nativeCaptures[0].capture.sourceAssets).toHaveLength(2)
  expect(exported.nativeCaptures[0].capture.sourceAssets[0].text).toBe('  Keep this original exactly.\nStill unknown.  ')
  await memory.getByRole('button', { name: 'Archive', exact: true }).click()
  await expect(memory).toHaveCount(0)
  await page.getByLabel('Show archived', { exact: true }).check()
  await expect(memory).toHaveCount(1)
  await memory.getByRole('button', { name: 'Restore', exact: true }).click()
  await page.getByLabel('Show archived', { exact: true }).uncheck()
  for (const experience of ['classic', 'studio', 'companion', 'unified']) {
    await page.evaluate(value => localStorage.setItem('td.workspace.layout.v1', JSON.stringify({ experience: value, presentation: 'studio' })), experience)
    await page.reload()
    await memory.locator('summary').filter({ hasText: 'Preserved originals' }).click()
    await memory.getByRole('button', { name: 'Load originals', exact: true }).click()
    await expect(memory.locator('pre')).toHaveCount(2)
  }
  await page.setViewportSize({ width: 375, height: 812 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375)
  await page.screenshot({ path: 'test-results/private-memory-sources-mobile.png', fullPage: true })
  expect((await new AxeBuilder({ page }).include('.memory-sources').analyze()).violations.filter(item => ['serious', 'critical'].includes(item.impact ?? ''))).toEqual([])
  expect(await (await request.get(`${API_BASE_URL}/boards/${boardId}/cards`, { headers })).json()).toEqual([])
})
