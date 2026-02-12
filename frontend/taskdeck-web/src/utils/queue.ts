import type { QueueRequest, QueueStats, QueueStatus, QueueStatusValue } from '../types/queue'

const statusByValue: Record<number, QueueStatus> = {
  0: 'Pending',
  1: 'Processing',
  2: 'Completed',
  3: 'Failed',
  4: 'Cancelled',
}

const statusByName: Record<string, QueueStatus> = {
  pending: 'Pending',
  processing: 'Processing',
  completed: 'Completed',
  failed: 'Failed',
  cancelled: 'Cancelled',
}

export function normalizeQueueStatus(status: QueueStatusValue): QueueStatus {
  if (typeof status === 'number') {
    return statusByValue[status] ?? 'Pending'
  }

  const normalized = status.trim().toLowerCase()
  return statusByName[normalized] ?? 'Pending'
}

export function normalizeQueueRequest(request: QueueRequest): QueueRequest {
  return {
    ...request,
    status: normalizeQueueStatus(request.status),
  }
}

export function getQueueTotal(stats: QueueStats): number {
  return stats.pendingCount + stats.processingCount + stats.completedCount + stats.failedCount
}
