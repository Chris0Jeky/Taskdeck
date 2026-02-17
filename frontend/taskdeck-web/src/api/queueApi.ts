import http from './http'
import type { QueueRequest, CreateQueueRequestDto, QueueStats } from '../types/queue'
import { normalizeQueueRequest } from '../utils/queue'

function encodePathSegment(value: string): string {
  return encodeURIComponent(value)
}

export const queueApi = {
  async createRequest(request: CreateQueueRequestDto): Promise<QueueRequest> {
    const { data } = await http.post<QueueRequest>('/llm-queue', request)
    return normalizeQueueRequest(data)
  },

  async getUserRequests(): Promise<QueueRequest[]> {
    const { data } = await http.get<QueueRequest[]>('/llm-queue/user')
    return data.map(normalizeQueueRequest)
  },

  async getRequestsByStatus(status: string): Promise<QueueRequest[]> {
    const pathStatus = encodePathSegment(status)
    const { data } = await http.get<QueueRequest[]>(`/llm-queue/status/${pathStatus}`)
    return data.map(normalizeQueueRequest)
  },

  async cancelRequest(requestId: string): Promise<void> {
    const pathRequestId = encodePathSegment(requestId)
    await http.post(`/llm-queue/${pathRequestId}/cancel`)
  },

  async processNext(): Promise<QueueRequest | null> {
    try {
      const { data } = await http.post<QueueRequest | null>('/llm-queue/process-next')
      return data ? normalizeQueueRequest(data) : null
    } catch (err: unknown) {
      if (typeof err === 'object' && err !== null) {
        const typed = err as { response?: { status?: number; data?: { errorCode?: string } } }
        const status = typed.response?.status
        const code = typed.response?.data?.errorCode
        if (status === 404 || code === 'NotFound') {
          return null
        }
      }
      throw err
    }
  },

  async getStats(): Promise<QueueStats> {
    const { data } = await http.get<QueueStats>('/llm-queue/stats')
    return data
  },
}
