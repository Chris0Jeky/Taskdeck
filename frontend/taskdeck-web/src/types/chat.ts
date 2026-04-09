export type ChatSessionStatus = 'Active' | 'Archived'
export type ChatSessionStatusValue = ChatSessionStatus | number
export type ChatRole = 'User' | 'Assistant' | 'System'
export type ChatRoleValue = ChatRole | number
export type ChatMessageType = 'text' | 'proposal-reference' | 'error' | 'status' | 'degraded' | 'parse-hint' | 'clarification'

export interface ParseHintPayload {
  supportedPatterns: string[]
  exampleInstruction: string
  closestPattern: string
  detectedIntent: string | null
}

export interface ToolCallEntry {
  round: number
  tool: string
  args: Record<string, unknown>
  result_summary: string
  is_error: boolean
}

export interface ToolCallMetadata {
  rounds: number
  total_tokens: number
  tool_calls: ToolCallEntry[]
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
  toolCallMetadataJson?: string | null
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

export type VerificationStatus = 'unverified' | 'verified' | 'failed'

export interface ChatProviderHealth {
  isAvailable: boolean
  providerName: string
  errorMessage: string | null
  model: string | null
  isMock: boolean
  isProbed: boolean
  verificationStatus: VerificationStatus
}

export interface CreateChatSessionRequest {
  title: string
  boardId?: string | null
}

export interface SendChatMessageRequest {
  content: string
  requestProposal?: boolean
}
