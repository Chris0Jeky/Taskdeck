export type ChatSessionStatus = 'Active' | 'Archived'
export type ChatSessionStatusValue = ChatSessionStatus | number
export type ChatRole = 'User' | 'Assistant' | 'System'
export type ChatRoleValue = ChatRole | number
export type ChatMessageType = 'text' | 'proposal-reference' | 'error' | 'status'

export interface ChatMessage {
  id: string
  sessionId: string
  role: ChatRoleValue
  content: string
  messageType: ChatMessageType
  proposalId: string | null
  tokenUsage: number | null
  createdAt: string
}

export interface ChatSession {
  id: string
  userId: string
  boardId: string | null
  title: string
  status: ChatSessionStatusValue
  createdAt: string
  updatedAt: string
  recentMessages: ChatMessage[]
}

export interface CreateChatSessionRequest {
  title: string
  boardId?: string | null
}

export interface SendChatMessageRequest {
  content: string
  requestProposal?: boolean
}
