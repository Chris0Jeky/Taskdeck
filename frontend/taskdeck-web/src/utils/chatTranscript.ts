import type { ChatMessage } from '../types/chat'

/**
 * Builds the user-side placeholder that keeps a just-sent turn visible while
 * the next authoritative transcript is loading.
 */
export function createLocalUserMessage(
  sessionId: string,
  content: string,
  assistantCreatedAt: string,
  sequence: number,
): ChatMessage {
  const assistantTimestamp = Date.parse(assistantCreatedAt)
  const createdAt = Number.isFinite(assistantTimestamp)
    ? new Date(assistantTimestamp - 1).toISOString()
    : new Date().toISOString()

  return {
    id: `local-user-${sessionId}-${sequence}`,
    sessionId,
    role: 'User',
    content,
    messageType: 'text',
    proposalId: null,
    tokenUsage: null,
    createdAt,
  }
}

/**
 * Retains the latest value for each message id while preserving first-seen
 * order. Server responses therefore replace local copies without reordering
 * the surrounding transcript.
 */
export function retainLocalMessages(existing: ChatMessage[], messages: ChatMessage[]): ChatMessage[] {
  const byId = new Map(existing.map((message) => [message.id, message]))
  for (const message of messages) {
    byId.set(message.id, message)
  }
  return [...byId.values()]
}

/**
 * Adds local messages that are not present in the authoritative transcript.
 * The server response remains the source of truth for duplicate ids.
 */
export function mergeLocalMessages(messages: ChatMessage[], localMessages: ChatMessage[]): ChatMessage[] {
  const knownIds = new Set(messages.map((message) => message.id))
  return [
    ...messages,
    ...localMessages.filter((message) => !knownIds.has(message.id)),
  ]
}
