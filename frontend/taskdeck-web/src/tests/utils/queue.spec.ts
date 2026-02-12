import { describe, expect, it } from 'vitest'
import type { QueueRequest, QueueStatusValue } from '../../types/queue'
import { getQueueTotal, normalizeQueueRequest, normalizeQueueStatus } from '../../utils/queue'

describe('queue utils', () => {
  it('normalizes numeric statuses from backend', () => {
    expect(normalizeQueueStatus(3)).toBe('Failed')
  })

  it('normalizes case-insensitive string statuses', () => {
    expect(normalizeQueueStatus('completed' as QueueStatusValue)).toBe('Completed')
  })

  it('falls back to Pending for unknown status', () => {
    expect(normalizeQueueStatus('unknown' as QueueStatusValue)).toBe('Pending')
  })

  it('normalizes request status field', () => {
    const request: QueueRequest = {
      id: 'r1',
      userId: 'u1',
      boardId: null,
      requestType: 'generate',
      status: 1,
      errorMessage: null,
      createdAt: '2026-01-01T00:00:00Z',
      processedAt: null,
      retryCount: 0,
    }

    expect(normalizeQueueRequest(request).status).toBe('Processing')
  })

  it('computes queue totals from stats payload', () => {
    const total = getQueueTotal({
      pendingCount: 1,
      processingCount: 2,
      completedCount: 3,
      failedCount: 4,
    })

    expect(total).toBe(10)
  })
})
