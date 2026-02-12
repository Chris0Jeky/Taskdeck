import type { QueueRequest, QueueStats, QueueStatus, QueueStatusValue } from '../types/queue'

const statusByValue: Record<number, QueueStatus> = {
  0: 'Pending',
  1: 'Processing',
  2: 'Completed',
  3: 'Failed',
  4: 'Cancelled',
}

export function normalizeQueueStatus(status: QueueStatusValue): QueueStatus {
  if (typeof status === 'number') {
    return statusByValue[status] ?? 'Pending'
  }
  return status
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
