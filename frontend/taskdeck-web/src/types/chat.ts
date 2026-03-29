export type ChatSessionStatus = 'Active' | 'Archived'
export type ChatSessionStatusValue = ChatSessionStatus | number
export type ChatRole = 'User' | 'Assistant' | 'System'
export type ChatRoleValue = ChatRole | number
export type ChatMessageType = 'text' | 'proposal-reference' | 'error' | 'status' | 'degraded' | 'parse-hint'

export interface ParseHintPayload {
  supportedPatterns: string[]
  exampleInstruction: string
  closestPattern: string
  detectedIntent: string | null
}

export interface ChatMessage {
  id: string
  sessionId: string
  role: ChatRoleValue
  content: string
  messageType: ChatMessageType
  proposalId: string | null
  tokenUsage: number | null
  createdAt: string
  degradedReason?: string | null
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

export interface ChatProviderHealth {
  isAvailable: boolean
  providerName: string
  errorMessage: string | null
  model: string | null
  isMock: boolean
  isProbed: boolean
}

export interface CreateChatSessionRequest {
  title: string
  boardId?: string | null
}

export interface SendChatMessageRequest {
  content: string
  requestProposal?: boolean
}
