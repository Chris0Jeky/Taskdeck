import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

test('a private audio original survives a lost receipt, written confirmation and experience switches', async ({ page, request }) => {
  test.setTimeout(120000)
  const auth = await registerAndAttachSession(page, request, 'thinking-audio')
  const headers = { Authorization: `Bearer ${auth.token}` }
  const boardId = await createBoardWithColumn(request, auth, String(Date.now()), { boardNamePrefix: 'Audio', columnNamePrefix: 'Next' })
  const board = await (await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })).json()
  const response = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, { headers, data: { boardId, columnId: board.columns[0].id, title: 'Keep my original thought' } })
  await assertOk(response, 'seed audio card'); const card = await response.json()
  await page.goto(`/workspace/boards/${boardId}/cards/${card.id}/thinking`)
  await page.getByRole('button', { name: '+ question', exact: true }).click()
  await page.getByLabel('Layer 1 title', { exact: true }).fill('What did the experiment teach me?')
  await page.getByLabel('Layer 1 details', { exact: true }).fill('Keep my original observation separate from the written version.')
  await page.getByRole('button', { name: 'Save thinking', exact: true }).click()
  await expect(page.getByText('Thinking saved', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Your private answer', exact: true }).click()
  await page.getByRole('button', { name: 'Record or open a private audio answer', exact: true }).click()
  const region = page.getByRole('region', { name: 'Private audio answer', exact: true })
  const audio = Buffer.alloc(16044)
  audio.write('RIFF', 0); audio.writeUInt32LE(audio.length - 8, 4); audio.write('WAVEfmt ', 8)
  audio.writeUInt32LE(16, 16); audio.writeUInt16LE(1, 20); audio.writeUInt16LE(1, 22)
  audio.writeUInt32LE(8000, 24); audio.writeUInt32LE(16000, 28); audio.writeUInt16LE(2, 32); audio.writeUInt16LE(16, 34)
  audio.write('data', 36); audio.writeUInt32LE(16000, 40)
  await region.getByLabel('Choose audio file').setInputFiles({ name: 'original.wav', mimeType: 'audio/wav', buffer: audio })
  await page.getByRole('button', { name: 'Hide private answer', exact: true }).click()
  await page.getByRole('button', { name: 'Your private answer', exact: true }).click()
  await expect(region.getByText(/original.wav.*unsaved original/)).toBeVisible()
  let release!: () => void
  const pending = new Promise<void>(resolve => { release = resolve })
  const uploadIds: string[] = []
  await page.route(/\/thinking-audio\/questions\//, async route => {
    if (route.request().method() !== 'POST') return route.continue()
    uploadIds.push(new URL(route.request().url()).searchParams.get('uploadId')!)
    if (uploadIds.length !== 1) return route.continue()
    const accepted = await route.fetch()
    expect(accepted.ok()).toBe(true)
    await pending
    await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'Synthetic lost audio receipt' }) })
  })
  await region.getByRole('button', { name: 'Save original privately', exact: true }).click()
  await page.getByRole('link', { name: 'Choose work for your personal plan', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('Your private answer is still in progress')
  await expect(dialog.getByRole('button', { name: /leave/i })).toBeDisabled()
  await dialog.getByRole('button', { name: 'Keep editing', exact: true }).click()
  release()
  await expect(region.getByRole('alert')).toContainText('request could not be confirmed')
  await region.getByRole('button', { name: 'Save original privately', exact: true }).click()
  await expect(region.getByText(/This question is still unanswered/)).toBeVisible()
  expect(uploadIds).toHaveLength(2); expect(uploadIds[1]).toBe(uploadIds[0])
  let releasePlayback!: () => void
  const stalledPlayback = new Promise<void>(resolve => { releasePlayback = resolve })
  let playbackStarted!: () => void
  const startedPlayback = new Promise<void>(resolve => { playbackStarted = resolve })
  await page.route(/\/thinking-audio\/[^/]+\/original$/, async route => {
    const response = await route.fetch(); playbackStarted()
    await stalledPlayback; await route.fulfill({ response })
  }, { times: 1 })
  await region.getByRole('button', { name: 'Load original for playback or download', exact: true }).click()
  await startedPlayback
  await page.getByLabel('Layer 1 details', { exact: true }).fill('A new question must not receive the old recording.')
  await page.getByRole('button', { name: 'Save thinking', exact: true }).click()
  await expect(region.getByLabel('Choose audio file')).toBeVisible()
  const oldPlaybackResponse = page.waitForResponse(/\/thinking-audio\/[^/]+\/original$/)
  releasePlayback(); await oldPlaybackResponse
  await expect(region.locator('audio')).toHaveCount(0)
  await page.getByLabel('Layer 1 details', { exact: true }).fill('Keep my original observation separate from the written version.')
  await page.getByRole('button', { name: 'Save thinking', exact: true }).click()
  await region.getByRole('button', { name: 'Load original for playback or download', exact: true }).click()
  await expect.poll(() => region.locator('audio').evaluate((element: HTMLAudioElement) => element.readyState)).toBeGreaterThanOrEqual(1)
  await region.getByLabel('Written audio version', { exact: true }).fill('The smaller batch reduced interruptions, but I need another trial.')
  await region.getByRole('button', { name: 'Save written version', exact: true }).click()
  await expect(region.getByText(/question stays unanswered until you confirm/)).toBeVisible()
  await region.getByLabel('Audio answer status', { exact: true }).selectOption('needsReview')
  await region.getByRole('button', { name: 'Confirm written version as my answer', exact: true }).click()
  await expect(region.getByText(/Confirmed by you and kept in private memory/)).toBeVisible()
  const memories = await (await request.get(`${API_BASE_URL}/workspace-memory?boardId=${boardId}`, { headers })).json()
  expect(memories).toHaveLength(1); expect(memories[0].status).toBe('needsReview')
  for (const experience of ['classic', 'studio', 'companion', 'unified']) {
    await page.evaluate(value => {
      localStorage.setItem('td.workspace.layout.v1', JSON.stringify({ experience: value, presentation: 'studio' }))
      localStorage.setItem('td.paper.mode.v2', 'grove')
    }, experience)
    await page.reload()
    await page.getByRole('button', { name: 'Your private answer', exact: true }).click()
    await page.getByRole('button', { name: 'Record or open a private audio answer', exact: true }).click()
    await expect(region.getByText(/Confirmed by you and kept in private memory/)).toBeVisible()
  }
  await page.setViewportSize({ width: 375, height: 812 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375)
  expect((await new AxeBuilder({ page }).include('[aria-label="Private audio answer"]').analyze()).violations.filter(item => ['serious', 'critical'].includes(item.impact ?? ''))).toEqual([])
  await page.screenshot({ path: 'test-results/thinking-audio-mobile.png', fullPage: true })
  expect(await (await request.get(`${API_BASE_URL}/boards/${boardId}/cards`, { headers })).json()).toEqual([card])
})
