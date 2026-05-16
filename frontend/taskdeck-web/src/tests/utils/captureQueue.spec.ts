import 'fake-indexeddb/auto'
import { describe, expect, it, beforeEach } from 'vitest'
import {
  enqueueCapture,
  dequeueCapture,
  getAllPending,
  incrementRetry,
  getPendingCount,
} from '../../utils/captureQueue'
import type { CreateCaptureItemDto } from '../../types/capture'

function makeDto(text = 'Test capture'): CreateCaptureItemDto {
  return { boardId: null, text, source: 'ShareTarget', titleHint: null, externalRef: null }
}

describe('captureQueue', () => {
  beforeEach(async () => {
    const pending = await getAllPending()
    for (const entry of pending) {
      await dequeueCapture(entry.id)
    }
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
    expect(pending[0].queuedAt).toBeTruthy()
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

  it('handles multiple captures in FIFO order', async () => {
    await enqueueCapture(makeDto('First'))
    await enqueueCapture(makeDto('Second'))
    await enqueueCapture(makeDto('Third'))

    const pending = await getAllPending()
    expect(pending).toHaveLength(3)
    const texts = pending.map((p) => p.dto.text)
    expect(texts).toContain('First')
    expect(texts).toContain('Second')
    expect(texts).toContain('Third')
  })

  it('dequeue is idempotent for missing ids', async () => {
    await expect(dequeueCapture('nonexistent-id')).resolves.toBeUndefined()
  })
})
