import http from './http'

export interface SpeechConfiguration {
  enabled: boolean; provider: string; model: string; origin: string; configurationHash: string
  timeoutSeconds: number; dailyAttempts: number; dailyInputBytes: number
}
export interface TranscriptionRequest { requestId: string; expectedRevision: number; configurationHash: string }
export interface TranscriptionReceipt {
  id: string; requestId: string; audioAnswerId: string; state: string; provider: string; model: string; configurationHash: string
  startedAt: string; deadline: string; finishedAt: string | null; failureCode: string | null; representationId: string | null; text: string | null
}
export interface TranscriptionStatus { configuration: SpeechConfiguration; attemptsUsedToday: number; inputBytesUsedToday: number; attempts: TranscriptionReceipt[] }
export const audioTranscriptionApi = {
  async status(id: string): Promise<TranscriptionStatus> {
    return (await http.get<TranscriptionStatus>(`/thinking-audio/${id}/transcriptions`, { skipRetry: true, timeout: 15000 })).data
  },
  async start(id: string, request: TranscriptionRequest): Promise<TranscriptionReceipt> {
    return (await http.post<TranscriptionReceipt>(`/thinking-audio/${id}/transcriptions`, request, { skipRetry: true, timeout: 100000 })).data
  },
}
