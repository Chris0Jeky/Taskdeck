import http from './http'
import type { ChatAssetPage } from '../types/chat'

export const chatSourcesApi = {
  async list(memoryId: string, boardId: string, revision: number, afterOrdinal = -1): Promise<ChatAssetPage> {
    const { data } = await http.get<ChatAssetPage>(`/llm/chat/context-memory/${encodeURIComponent(memoryId)}/sources`, {
      params: { boardId, revision, afterOrdinal },
    })
    return data
  },
}
