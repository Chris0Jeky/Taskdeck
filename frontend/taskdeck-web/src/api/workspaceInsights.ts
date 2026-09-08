import http from './http'
import type {
  AnalyzeInsightsRequest,
  AnswerInsightRequest,
  ArchiveMemoryRequest,
  CreateMemoryRequest,
  Insight,
  InsightAction,
  Memory,
  UpdateMemoryRequest,
} from '../types/workspaceInsights'

function withBoardQuery(path: string, boardId?: string, archived?: boolean): string {
  const params = new URLSearchParams()
  if (boardId) params.set('boardId', boardId)
  if (archived !== undefined) params.set('archived', String(archived))
  const query = params.toString()
  return query ? `${path}?${query}` : path
}

export const workspaceInsightsApi = {
  async getInsights(boardId?: string): Promise<Insight[]> {
    const { data } = await http.get<Insight[]>(withBoardQuery('/workspace-insights', boardId))
    return data
  },

  async analyzeBoard(request: AnalyzeInsightsRequest): Promise<Insight[]> {
    const { data } = await http.post<Insight[]>('/workspace-insights/analyze', request)
    return data
  },

  async updateInsight(id: string, action: InsightAction): Promise<Insight> {
    const { data } = await http.patch<Insight>(
      `/workspace-insights/${encodeURIComponent(id)}`,
      { action },
    )
    return data
  },

  async answerInsight(id: string, answer: AnswerInsightRequest): Promise<Memory> {
    const { data } = await http.post<Memory>(
      `/workspace-insights/${encodeURIComponent(id)}/answer`,
      answer,
    )
    return data
  },

  async getMemories(boardId?: string, archived = false): Promise<Memory[]> {
    const { data } = await http.get<Memory[]>(withBoardQuery('/workspace-memory', boardId, archived))
    return data
  },

  async createMemory(request: CreateMemoryRequest): Promise<Memory> {
    const { data } = await http.post<Memory>('/workspace-memory', request)
    return data
  },

  async updateMemory(id: string, request: UpdateMemoryRequest): Promise<Memory> {
    const { data } = await http.put<Memory>(
      `/workspace-memory/${encodeURIComponent(id)}`,
      request,
    )
    return data
  },

  async setMemoryArchived(id: string, request: ArchiveMemoryRequest): Promise<Memory> {
    const { data } = await http.patch<Memory>(
      `/workspace-memory/${encodeURIComponent(id)}`,
      request,
    )
    return data
  },
}

// Small named aliases keep call sites readable when a surface only needs one
// half of the client, while retaining one request implementation to test.
export const workspaceMemoryApi = {
  getMemories: workspaceInsightsApi.getMemories,
  createMemory: workspaceInsightsApi.createMemory,
  updateMemory: workspaceInsightsApi.updateMemory,
  setMemoryArchived: workspaceInsightsApi.setMemoryArchived,
}
