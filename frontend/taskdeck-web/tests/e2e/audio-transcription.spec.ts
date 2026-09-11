import { createServer, type Server } from 'node:http'
import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

const transportProof = process.env.TASKDECK_SPEECH_TRANSPORT_PROOF === '1'
let server: Server | undefined
let providerCalls = 0
const providerBodies: Buffer[] = []
test.beforeAll(async () => {
  if (!transportProof) return
  server = createServer(async (request, response) => {
    if (request.method !== 'POST' || request.url !== '/v1/audio/transcriptions') { response.writeHead(404).end(); return }
    const chunks: Buffer[] = []; for await (const chunk of request) chunks.push(Buffer.from(chunk))
    providerBodies.push(Buffer.concat(chunks)); providerCalls++
    response.writeHead(providerCalls === 1 ? 503 : 200, { 'Content-Type': 'application/json' })
    response.end(providerCalls === 1 ? '{"error":"Synthetic unavailable provider"}' : '{"text":"A smaller batch helped me focus. I should repeat the experiment."}')
  })
  await new Promise<void>((resolve, reject) => { server!.once('error', reject); server!.listen(Number(process.env.TASKDECK_SPEECH_FIXTURE_PORT ?? 5349), '127.0.0.1', resolve) })
})
test.afterAll(async () => { if (server) await new Promise<void>((resolve, reject) => server!.close(error => error ? reject(error) : resolve())) })

test('transcription stays explicit and provisional across recovery, written confirmation and every experience', async ({ page, request }) => {
  test.setTimeout(120000)
  const auth = await registerAndAttachSession(page, request, 'audio-transcription')
  const headers = { Authorization: `Bearer ${auth.token}` }
  const boardId = await createBoardWithColumn(request, auth, String(Date.now()), { boardNamePrefix: 'Transcription', columnNamePrefix: 'Next' })
  const board = await (await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })).json()
  const cardResponse = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, { headers, data: { boardId, columnId: board.columns[0].id, title: 'Review my spoken thought' } })
  await assertOk(cardResponse, 'seed transcription card'); const card = await cardResponse.json()
  const layerId = crypto.randomUUID()
  await assertOk(await request.put(`${API_BASE_URL}/boards/${boardId}/cards/${card.id}/thinking`, { headers,
    data: { expectedRevision: 0, layers: [{ id: layerId, kind: 'question', title: 'What did the trial teach me?', body: 'Keep uncertainty visible.', items: [] }] } }), 'save source question')
  const original = Buffer.alloc(16044)
  original.write('RIFF', 0); original.writeUInt32LE(original.length - 8, 4); original.write('WAVEfmt ', 8)
  original.writeUInt32LE(16, 16); original.writeUInt16LE(1, 20); original.writeUInt16LE(1, 22)
  original.writeUInt32LE(8000, 24); original.writeUInt32LE(16000, 28); original.writeUInt16LE(2, 32); original.writeUInt16LE(16, 34)
  original.write('data', 36); original.writeUInt32LE(16000, 40)
  const upload = await request.post(`${API_BASE_URL}/thinking-audio/questions/${boardId}/${card.id}/${layerId}`, {
    headers: { ...headers, 'Content-Type': 'audio/wav' }, data: original,
    params: { uploadId: crypto.randomUUID(), expectedDeckRevision: 1, byteSize: original.length, fileName: 'private-trial.wav' },
  })
  await assertOk(upload, 'save transcription original'); const audio = await upload.json()
  await page.goto(`/workspace/boards/${boardId}/cards/${card.id}/thinking`)
  async function open() {
    await page.getByRole('button', { name: 'Your private answer', exact: true }).click()
    await page.getByRole('button', { name: 'Record or open a private audio answer', exact: true }).click()
    await page.getByRole('button', { name: 'Transcription options and receipts', exact: true }).click()
  }
  await open()
  const panel = page.getByRole('region', { name: 'Automatic transcription', exact: true })
  if (transportProof) {
    expect(providerCalls).toBe(0)
    await expect(panel).toContainText(`http://localhost:${process.env.TASKDECK_SPEECH_FIXTURE_PORT ?? 5349}`)
    await expect(panel.getByRole('button', { name: 'Request a transcript', exact: true })).toBeDisabled()
    await panel.getByRole('checkbox').check()
    await panel.getByRole('button', { name: 'Request a transcript', exact: true }).click()
    await expect(panel).toContainText('The provider was unavailable or timed out')
    await panel.getByRole('button', { name: 'Refresh transcription receipts', exact: true }).click()
    const requestIds: string[] = []
    await page.route(`**/thinking-audio/${audio.id}/transcriptions`, async route => {
      if (route.request().method() !== 'POST') return route.continue()
      requestIds.push(route.request().postDataJSON().requestId)
      const response = await route.fetch(); expect(response.ok()).toBe(true)
      await route.fulfill({ status: 503, contentType: 'application/json', body: '{"message":"Synthetic lost transcript receipt"}' })
    }, { times: 1 })
    await panel.getByRole('checkbox').check()
    await panel.getByRole('button', { name: 'Request a transcript', exact: true }).click()
    await expect(panel.getByRole('alert')).toContainText('result could not be confirmed')
    await panel.getByRole('button', { name: 'Refresh transcription receipts', exact: true }).click()
    await expect(panel).toContainText('A smaller batch helped me focus.')
    expect(providerCalls).toBe(2); expect(requestIds).toHaveLength(1)
    expect(providerBodies[1]!.includes(original)).toBe(true)
    expect(providerBodies[1]!.includes(Buffer.from('private-trial.wav'))).toBe(false)
    const status = await (await request.get(`${API_BASE_URL}/thinking-audio/${audio.id}/transcriptions`, { headers })).json()
    expect(status.attempts).toHaveLength(2)
    expect(status.attempts.some((row: { requestId: string }) => row.requestId === requestIds[0])).toBe(true)
    expect(await (await request.get(`${API_BASE_URL}/workspace-memory?boardId=${boardId}`, { headers })).json()).toEqual([])
    await panel.getByRole('button', { name: 'Use this transcript as a written draft', exact: true }).click()
    await expect(page.getByLabel('Written audio version', { exact: true })).toHaveValue('A smaller batch helped me focus. I should repeat the experiment.')
    await page.getByLabel('Written audio version', { exact: true }).fill('A smaller batch may have helped. Repeat the experiment.')
    await page.getByRole('button', { name: 'Save written version', exact: true }).click()
    await page.getByLabel('Audio answer status', { exact: true }).selectOption('needsReview')
    await page.getByRole('button', { name: 'Confirm written version as my answer', exact: true }).click()
    await expect(page.getByText(/Confirmed by you and kept in private memory/)).toBeVisible()
  } else await expect(panel).toContainText('not configured or is paused')
  for (const experience of ['classic', 'studio', 'companion', 'unified']) {
    await page.evaluate(value => {
      localStorage.setItem('td.workspace.layout.v1', JSON.stringify({ experience: value, presentation: 'studio' }))
      localStorage.setItem('td.paper.mode.v2', 'grove')
    }, experience)
    await page.reload(); await open()
    await expect(panel).toContainText(transportProof ? 'A smaller batch helped me focus.' : 'not configured or is paused')
  }
  if (transportProof) expect(providerCalls).toBe(2)
  await page.setViewportSize({ width: 375, height: 812 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375)
  expect((await new AxeBuilder({ page }).include('[aria-label="Automatic transcription"]').analyze()).violations.filter(x => ['serious', 'critical'].includes(x.impact ?? ''))).toEqual([])
  await page.screenshot({ path: 'test-results/audio-transcription-mobile.png', fullPage: true })
  expect(await (await request.get(`${API_BASE_URL}/boards/${boardId}/cards`, { headers })).json()).toEqual([card])
})
