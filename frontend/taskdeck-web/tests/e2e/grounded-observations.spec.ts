import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

test('previews exact evidence across experiences and rejects a stale model submission', async ({ page, request }) => {
  const auth = await registerAndAttachSession(page, request, 'observation-proof')
  const headers = { Authorization: `Bearer ${auth.token}` }
  const boardId = await createBoardWithColumn(request, auth, String(Date.now()), { boardNamePrefix: 'Observation proof', description: 'Synthetic observation evidence.', columnNamePrefix: 'Next' })
  const board = await (await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })).json()
  const created = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, { headers,
    data: { boardId, columnId: board.columns[0].id, title: 'Investigate rollout', description: 'Compare two options before choosing a next action.' } })
  await assertOk(created, 'create source card')
  const card = await created.json()
  await page.goto(`/workspace/insights?boardId=${boardId}`)
  const panel = page.getByRole('region', { name: 'Questions from your evidence' })
  await expect(panel).toBeVisible()
  await panel.getByRole('button', { name: 'Choose a card', exact: true }).click()
  await panel.getByLabel('Card for model analysis').selectOption(card.id)
  await panel.getByRole('button', { name: 'Preview current evidence' }).click()
  await expect(panel.locator('pre')).toContainText('Compare two options')
  for (const experience of ['classic', 'studio', 'companion', 'unified']) {
    await page.getByLabel('Workspace experience', { exact: true }).selectOption(experience)
    await expect(panel.locator('pre')).toContainText('Investigate rollout')
  }
  await assertOk(await request.patch(`${API_BASE_URL}/boards/${boardId}/cards/${card.id}`, { headers, data: { title: 'Investigate updated rollout' } }), 'change source after preview')
  const conflict = page.waitForResponse(response => response.url().endsWith('/workspace-insights/model-analysis'))
  await panel.getByRole('button', { name: 'Analyze this evidence with model' }).click()
  expect((await conflict).status()).toBe(409)
  await expect(panel.getByRole('alert')).toContainText('source changed')
  await expect(panel.locator('pre')).toHaveCount(0)
  await panel.getByRole('button', { name: 'Preview current evidence' }).click()
  await expect(panel.locator('pre')).toContainText('Investigate updated rollout')
  if (process.env.TASKDECK_OBSERVATION_GATEWAY_PROOF === '1') {
    const saved = page.waitForResponse(response => response.url().endsWith('/workspace-insights/model-analysis'))
    await panel.getByRole('button', { name: 'Analyze this evidence with model' }).click()
    await assertOk(await saved, 'save grounded questions through configured loopback provider')
    await expect(page.getByText('What would make the next step clear?', { exact: true })).toBeVisible()
    await expect(page.getByText('Model question · review the evidence', { exact: true })).toBeVisible()
    await page.reload()
    await expect(page.getByText('What would make the next step clear?', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Answer privately', exact: true }).click()
    await page.getByRole('textbox', { name: /answer/i }).fill('Ask the release owner for the checklist.')
    await page.getByRole('button', { name: /Save.*memory/i }).click()
    await expect(page.getByText('Saved to private memory. The board was not changed.')).toBeVisible()
  }
  await page.setViewportSize({ width: 375, height: 812 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBeTruthy()
  const audit = await new AxeBuilder({ page }).include('.observation-panel').withTags(['wcag2a','wcag2aa']).analyze()
  expect(audit.violations).toEqual([])
  await page.screenshot({ path: '../../artifacts/overhaul/grounded-observations.png', fullPage: true })
})
