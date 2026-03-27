import http from './http'
import type { ChatMessage, ChatProviderHealth, ChatSession, CreateChatSessionRequest, SendChatMessageRequest } from '../types/chat'

export const chatApi = {
  async createSession(request: CreateChatSessionRequest): Promise<ChatSession> {
    const { data } = await http.post<ChatSession>('/llm/chat/sessions', request)
    return data
  },

  async getMySessions(): Promise<ChatSession[]> {
    const { data } = await http.get<ChatSession[]>('/llm/chat/sessions')
    return data
  },

  async getSession(sessionId: string): Promise<ChatSession> {
    const { data } = await http.get<ChatSession>(`/llm/chat/sessions/${encodeURIComponent(sessionId)}`)
    return data
  },

  async getHealth(): Promise<ChatProviderHealth> {
    const { data } = await http.get<ChatProviderHealth>('/llm/chat/health')
    return data
  },

  async sendMessage(sessionId: string, request: SendChatMessageRequest): Promise<ChatMessage> {
    const { data } = await http.post<ChatMessage>(`/llm/chat/sessions/${encodeURIComponent(sessionId)}/messages`, request)
    return data
  },
}
