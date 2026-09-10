import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

test('retained originals remain discoverable after question removal and board archival across all experiences', async ({ page, request }) => {
  test.setTimeout(120000)
  const auth = await registerAndAttachSession(page, request, 'original-library')
  const headers = { Authorization: `Bearer ${auth.token}` }
  const boardId = await createBoardWithColumn(request, auth, String(Date.now()), { boardNamePrefix: 'Original library', columnNamePrefix: 'Next' })
  const board = await (await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })).json()
  const cardResponse = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, { headers, data: { boardId, columnId: board.columns[0].id, title: 'Retain a changed question' } })
  await assertOk(cardResponse, 'seed library card'); const card = await cardResponse.json()
  const layerId = crypto.randomUUID()
  const thinking = `${API_BASE_URL}/boards/${boardId}/cards/${card.id}/thinking`
  const save = await request.put(thinking, { headers, data: { expectedRevision: 0, layers: [{ id: layerId, kind: 'question', title: 'What should I remember?', body: 'Exact question before removal.', items: [] }] } })
  await assertOk(save, 'save source question')
  const deck = await save.json()
  const audio = Buffer.alloc(16044)
  audio.write('RIFF', 0); audio.writeUInt32LE(audio.length - 8, 4); audio.write('WAVEfmt ', 8)
  audio.writeUInt32LE(16, 16); audio.writeUInt16LE(1, 20); audio.writeUInt16LE(1, 22)
  audio.writeUInt32LE(8000, 24); audio.writeUInt32LE(16000, 28); audio.writeUInt16LE(2, 32); audio.writeUInt16LE(16, 34)
  audio.write('data', 36); audio.writeUInt32LE(16000, 40)
  const upload = await request.post(`${API_BASE_URL}/thinking-audio/questions/${boardId}/${card.id}/${layerId}`, {
    headers: { ...headers, 'Content-Type': 'audio/wav' }, data: audio,
    params: { uploadId: crypto.randomUUID(), expectedDeckRevision: deck.revision, byteSize: audio.length, fileName: 'retained-original.wav' },
  })
  await assertOk(upload, 'save original recording')
  await assertOk(await request.put(thinking, { headers, data: { expectedRevision: deck.revision, layers: [] } }), 'remove source question')
  await page.goto('/workspace/memory')
  const library = page.getByRole('region', { name: 'Original recordings', exact: true })
  await library.getByRole('button', { name: 'Browse original recordings', exact: true }).click()
  await library.getByRole('button', { name: /Inspect retained-original.wav/ }).click()
  const selected = library.getByRole('article', { name: 'Selected original recording', exact: true })
  await expect(selected).toContainText('Exact question before removal.')
  await expect(selected).toContainText('read-only')
  await expect(selected.getByRole('link', { name: 'Open the current question to continue' })).toHaveCount(0)
  await selected.getByRole('button', { name: 'Load selected original for playback or download' }).click()
  await expect.poll(() => selected.locator('audio').evaluate((element: HTMLAudioElement) => element.readyState)).toBeGreaterThanOrEqual(1)
  const downloadPromise = page.waitForEvent('download')
  await selected.getByRole('link', { name: 'Download selected original' }).click()
  const download = await downloadPromise
  expect(download.suggestedFilename()).toBe('retained-original.wav')
  const stream = await download.createReadStream(); const chunks: Buffer[] = []
  for await (const chunk of stream!) chunks.push(Buffer.from(chunk))
  expect(Buffer.concat(chunks)).toEqual(audio)
  expect(await (await request.get(`${API_BASE_URL}/boards/${boardId}/cards`, { headers })).json()).toEqual([card])
  await assertOk(await request.delete(`${API_BASE_URL}/boards/${boardId}`, { headers }), 'delete synthetic board')
  // The public Delete action archives a board. Hard-delete retention is proved by the SQLite API test.
  const archived = await (await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })).json()
  expect(archived.isArchived).toBe(true)
  for (const experience of ['classic', 'studio', 'companion', 'unified']) {
    await page.evaluate(value => {
      localStorage.setItem('td.workspace.layout.v1', JSON.stringify({ experience: value, presentation: 'studio' }))
      localStorage.setItem('td.paper.mode.v2', 'grove')
    }, experience)
    await page.reload()
    await library.getByRole('button', { name: 'Browse original recordings', exact: true }).click()
    await expect(library).toContainText('retained-original.wav')
    await library.getByRole('button', { name: /Inspect retained-original.wav/ }).click()
    await expect(selected).toContainText('Exact question before removal.')
  }
  await selected.getByRole('button', { name: 'Load selected original for playback or download' }).click()
  await expect.poll(() => selected.locator('audio').evaluate((element: HTMLAudioElement) => element.readyState)).toBeGreaterThanOrEqual(1)
  await page.setViewportSize({ width: 375, height: 812 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375)
  expect((await new AxeBuilder({ page }).include('[aria-label="Original recordings"]').analyze()).violations.filter(x => ['serious', 'critical'].includes(x.impact ?? ''))).toEqual([])
  await page.screenshot({ path: 'test-results/original-audio-library-mobile.png', fullPage: true })
})
