import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'

test('comparison files work with the crypto APIs available on HTTP LAN', async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'comparison-lan-apis')
  await page.addInitScript(() => {
    Object.defineProperty(globalThis.crypto, 'randomUUID', { value: undefined, configurable: true })
    Object.defineProperty(globalThis.crypto, 'subtle', { value: undefined, configurable: true })
  })
  await page.goto('/workspace/experiences')
  await page.getByLabel('What scenario did you try?').selectOption('resume-thinking')
  await page.getByLabel('What happened?').selectOption('completed')
  await page.getByRole('button', { name: 'Record observation', exact: true }).click()
  await expect(page.getByText('Observation recorded.', { exact: true })).toBeVisible()
  const legacy = JSON.stringify({ kind: 'taskdeck-workspace-comparison', version: 2, trials: [{
    experience: 'classic', presentation: 'studio', theme: 'grove', build: null, scenario: 'resume-thinking',
    completionOutcome: 'completed', recordedAt: '2026-09-01T00:00:00Z', ease: null, note: 'Retained LAN observation',
  }] })
  const input = page.getByLabel('Import saved observations', { exact: true })
  await input.setInputFiles({ name: 'legacy.json', mimeType: 'application/json', buffer: Buffer.from(legacy) })
  await expect(page.getByText('Imported 1 observation. Exact duplicates were skipped.', { exact: true })).toBeVisible()
  await input.setInputFiles({ name: 'legacy.json', mimeType: 'application/json', buffer: Buffer.from(legacy) })
  await expect(page.getByText('Imported 0 observations. Exact duplicates were skipped.', { exact: true })).toBeVisible()
  const downloading = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Export observations', exact: true }).click()
  const download = await downloading; const stream = await download.createReadStream()
  let retained = ''; for await (const chunk of stream!) retained += chunk.toString()
  expect(JSON.parse(retained).trials).toHaveLength(2)
  await page.reload()
  await input.setInputFiles({ name: 'retained.json', mimeType: 'application/json', buffer: Buffer.from(retained) })
  await expect(page.getByText('Imported 2 observations. Exact duplicates were skipped.', { exact: true })).toBeVisible()
})
