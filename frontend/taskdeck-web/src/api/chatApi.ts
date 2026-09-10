import http from './http'
import type { BindChatSessionBoardRequest, ChatMessage, ChatProviderHealth, ChatSession, CreateChatSessionRequest, SendChatMessageRequest } from '../types/chat'
import { buildQueryString } from '../utils/queryBuilder'

export const chatApi = {
  async createSession(request: CreateChatSessionRequest): Promise<ChatSession> {
    const { data } = await http.post<ChatSession>('/llm/chat/sessions', request)
    return data
  },

  async getMySessions(): Promise<ChatSession[]> {
    const { data } = await http.get<ChatSession[]>('/llm/chat/sessions')
    return data
  },

  async getSession(sessionId: string, options?: { skipRetry?: boolean; timeout?: number }): Promise<ChatSession> {
    const { data } = await http.get<ChatSession>(`/llm/chat/sessions/${encodeURIComponent(sessionId)}`, options)
    return data
  },

  async bindBoard(sessionId: string, request: BindChatSessionBoardRequest): Promise<ChatSession> {
    const { data } = await http.post<ChatSession>(
      `/llm/chat/sessions/${encodeURIComponent(sessionId)}/board`,
      request,
    )
    return data
  },

  async getHealth(options?: { probe?: boolean }): Promise<ChatProviderHealth> {
    const query = buildQueryString({ probe: options?.probe ? true : undefined })
    const { data } = await http.get<ChatProviderHealth>(`/llm/chat/health${query}`)
    return data
  },

  async sendMessage(sessionId: string, request: SendChatMessageRequest): Promise<ChatMessage> {
    const { data } = await http.post<ChatMessage>(`/llm/chat/sessions/${encodeURIComponent(sessionId)}/messages`, request)
    return data
  },
}
