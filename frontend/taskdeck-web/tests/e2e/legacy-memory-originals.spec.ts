import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { DatabaseSync } from 'node:sqlite'
import { randomUUID } from 'node:crypto'
import { fileURLToPath } from 'node:url'
import { readFile } from 'node:fs/promises'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'

test('older private memory can preserve original evidence without a content edit', async ({ page, request }) => {
  test.skip(process.env.TASKDECK_E2E_DB !== 'legacy-originals.e2e.db', 'Requires the explicitly named isolated legacy fixture database.')
  const auth = await registerAndAttachSession(page, request, 'legacy-originals')
  const boardId = await createBoardWithColumn(request, auth, String(Date.now()), { boardNamePrefix: 'Legacy originals' })
  const id = randomUUID().toUpperCase()
  const original = '  Original answer\nwith exact spacing.  '
  // Only insert a legacy-shaped record into this dedicated synthetic database. No runtime seed endpoint.
  const db = new DatabaseSync(fileURLToPath(new URL('../../../../backend/src/Taskdeck.Api/legacy-originals.e2e.db', import.meta.url)))
  try {
    db.exec('PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 30000;')
    const now = new Date().toISOString()
    db.prepare('INSERT INTO WorkspaceMemories (Id, UserId, BoardId, Title, Text, OriginalText, OriginalEvidence, Status, Archived, Revision, CreatedAt, UpdatedAt) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 0, 1, ?, ?)')
      .run(id, auth.user.id.toUpperCase(), boardId.toUpperCase(), 'An older answer', original, original, 'Original question evidence', 'unknown', now, now)
  } finally { db.close() }
  await page.goto(`/workspace/memory?boardId=${boardId}`)
  const panel = page.getByRole('region', { name: 'Preserve older memory originals' })
  await expect(panel).toContainText('until account deletion')
  await page.setViewportSize({ width: 375, height: 812 })
  expect((await new AxeBuilder({ page }).include('.memory-preservation').analyze()).violations.filter(x => ['serious', 'critical'].includes(x.impact ?? ''))).toEqual([])
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375)
  await panel.getByRole('button', { name: 'Preserve originals for 1 memory' }).click()
  await expect(panel).toHaveCount(0)
  const memory = page.locator('.paper-memory__card').filter({ has: page.getByRole('heading', { name: 'An older answer', exact: true }) })
  await expect(memory).toContainText('Revision 2')
  await memory.locator('summary').filter({ hasText: 'Preserved originals' }).click()
  await memory.getByRole('button', { name: 'Load originals', exact: true }).click()
  await expect(memory.locator('pre')).toHaveCount(2)
  expect(await memory.locator('pre').last().textContent()).toBe(original)
  const downloading = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Download memory JSON', exact: true }).click()
  const exported = JSON.parse(await readFile((await (await downloading).path())!, 'utf8'))
  expect(exported.version).toBe(2)
  expect(exported.nativeCaptures).toHaveLength(1)
  expect(exported.nativeCaptures[0].capture.sourceAssets[1].text).toBe(original)
  await page.reload()
  await expect(panel).toHaveCount(0)
  await expect(memory).toContainText('Revision 2')
  expect(await (await request.get(`${API_BASE_URL}/boards/${boardId}/cards`, { headers: { Authorization: `Bearer ${auth.token}` } })).json()).toEqual([])
})
