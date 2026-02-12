import http from './http'
import type { QueueRequest, CreateQueueRequestDto, QueueStats } from '../types/queue'

export const queueApi = {
  async createRequest(request: CreateQueueRequestDto, userId: string): Promise<QueueRequest> {
    const { data } = await http.post<QueueRequest>('/llm-queue', { ...request, userId })
    return data
  },

  async getUserRequests(userId: string): Promise<QueueRequest[]> {
    const { data } = await http.get<QueueRequest[]>(`/llm-queue/user/${userId}`)
    return data
  },

  async getRequestsByStatus(status: string): Promise<QueueRequest[]> {
    const { data } = await http.get<QueueRequest[]>(`/llm-queue/status/${status}`)
    return data
  },

  async cancelRequest(requestId: string, userId: string): Promise<void> {
    await http.post(`/llm-queue/${requestId}/cancel?userId=${userId}`)
  },

  async processNext(): Promise<QueueRequest | null> {
    const { data } = await http.post<QueueRequest | null>('/llm-queue/process-next')
    return data
  },

  async getStats(): Promise<QueueStats> {
    const { data } = await http.get<QueueStats>('/llm-queue/stats')
    return data
  },
}
