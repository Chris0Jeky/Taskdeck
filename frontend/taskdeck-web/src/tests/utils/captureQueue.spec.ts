import 'fake-indexeddb/auto'
import { describe, expect, it, beforeEach, afterEach, vi } from 'vitest'
import {
  enqueueCapture,
  assignCaptureOwner,
  dequeueCapture,
  getAllPending,
  getAllQueuedCaptures,
  incrementRetry,
  markCaptureFailed,
  getPendingCount,
} from '../../utils/captureQueue'
import type { CreateCaptureItemDto } from '../../types/capture'

function makeDto(text = 'Test capture'): CreateCaptureItemDto {
  return { boardId: null, text, source: 'ShareTarget', titleHint: null, externalRef: null }
}

describe('captureQueue', () => {
  beforeEach(async () => {
    const queued = await getAllQueuedCaptures()
    for (const entry of queued) {
      await dequeueCapture(entry.id)
    }
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('enqueues a capture and retrieves it', async () => {
    const dto = makeDto('Hello from share')
    const id = await enqueueCapture(dto)

    expect(id).toBeTruthy()

    const pending = await getAllPending()
    expect(pending).toHaveLength(1)
    expect(pending[0].id).toBe(id)
    expect(pending[0].dto.text).toBe('Hello from share')
    expect(pending[0].dto.source).toBe('ShareTarget')
    expect(pending[0].retryCount).toBe(0)
    expect(pending[0].ownerUserId).toBeNull()
    expect(pending[0].status).toBe('pending')
    expect(pending[0].queuedAt).toBeTruthy()
  })

  it('records the session owner for replay safety', async () => {
    const id = await enqueueCapture(makeDto('Owned capture'), 'user-1')

    const pending = await getAllPending()
    expect(pending[0]).toMatchObject({
      id,
      ownerUserId: 'user-1',
      status: 'pending',
    })
  })

  it('dequeues a capture by id', async () => {
    const id = await enqueueCapture(makeDto())
    await dequeueCapture(id)

    const pending = await getAllPending()
    expect(pending).toHaveLength(0)
  })

  it('returns correct pending count', async () => {
    await enqueueCapture(makeDto('A'))
    await enqueueCapture(makeDto('B'))
    await enqueueCapture(makeDto('C'))

    const count = await getPendingCount()
    expect(count).toBe(3)
  })

  it('increments retry count', async () => {
    const id = await enqueueCapture(makeDto())

    await incrementRetry(id)
    await incrementRetry(id)

    const pending = await getAllPending()
    expect(pending[0].retryCount).toBe(2)
  })

  it('marks a capture failed without deleting its recovery payload', async () => {
    const id = await enqueueCapture(makeDto('Needs recovery'), 'user-1')

    await markCaptureFailed(id, 'HTTP 400')

    expect(await getAllPending()).toHaveLength(0)
    expect(await getPendingCount()).toBe(0)
    const queued = await getAllQueuedCaptures()
    expect(queued).toHaveLength(1)
    expect(queued[0]).toMatchObject({
      id,
      ownerUserId: 'user-1',
      status: 'failed',
      lastError: 'HTTP 400',
    })
    expect(queued[0].failedAt).toBeTruthy()
  })

  it('assigns an owner to a pending ownerless capture', async () => {
    const id = await enqueueCapture(makeDto('Login required capture'))

    await assignCaptureOwner(id, 'user-1')

    const pending = await getAllPending()
    expect(pending).toHaveLength(1)
    expect(pending[0]).toMatchObject({
      id,
      ownerUserId: 'user-1',
      status: 'pending',
    })
  })

  it('handles multiple captures in FIFO order', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-05-16T10:00:00Z'))
    await enqueueCapture(makeDto('First'))
    vi.setSystemTime(new Date('2026-05-16T10:00:01Z'))
    await enqueueCapture(makeDto('Second'))
    vi.setSystemTime(new Date('2026-05-16T10:00:02Z'))
    await enqueueCapture(makeDto('Third'))

    const pending = await getAllPending()
    expect(pending).toHaveLength(3)
    const texts = pending.map((p) => p.dto.text)
    expect(texts).toEqual(['First', 'Second', 'Third'])
  })

  it('dequeue is idempotent for missing ids', async () => {
    await expect(dequeueCapture('nonexistent-id')).resolves.toBeUndefined()
  })
})
