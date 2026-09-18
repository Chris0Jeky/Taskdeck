import http from './http'

export interface AttentionWindow { timeZoneId: string; daysMask: number; startMinute: number; endMinute: number }
export interface AttentionSettings { enabled: boolean; revision: number; dailyLimit: number; minimumSpacingMinutes: number; window?: AttentionWindow | null }
export interface AttentionReminder { boardId: string; insightId: string }
const options = { skipRetry: true, timeout: 15_000 }
export const workspaceAttentionApi = {
  async get(): Promise<AttentionSettings> { return (await http.get<AttentionSettings>('/workspace-attention', options)).data },
  async save(expectedRevision: number, enabled: boolean, window?: AttentionWindow | null): Promise<AttentionSettings> {
    return (await http.put<AttentionSettings>('/workspace-attention', {
      expectedRevision, enabled, ...(window === undefined ? {} : { updateWindow: true, window }),
    }, options)).data
  },
  async claim(boardId: string): Promise<AttentionReminder | null> {
    const response = await http.post<AttentionReminder>('/workspace-attention/claim', { boardId }, options)
    return response.status === 204 ? null : response.data
  },
}
