import type { ChatRoleValue, ChatSessionStatusValue, ParseHintPayload } from '../types/chat'

const chatRoleByIndex = ['User', 'Assistant', 'System'] as const
const chatSessionStatusByIndex = ['Active', 'Archived'] as const

export function normalizeChatRole(value: ChatRoleValue): typeof chatRoleByIndex[number] {
  if (typeof value === 'number') {
    return chatRoleByIndex[value] ?? 'User'
  }

  const found = chatRoleByIndex.find(v => v.toLowerCase() === value.toLowerCase())
  return found ?? 'User'
}

const PARSE_HINT_MARKER = '[PARSE_HINT]'

export interface ParsedHintMessage {
  textBeforeHint: string
  hint: ParseHintPayload
}

export function extractParseHint(content: string): ParsedHintMessage | null {
  const markerIndex = content.indexOf(PARSE_HINT_MARKER)
  if (markerIndex === -1) {
    return null
  }

  const jsonStart = markerIndex + PARSE_HINT_MARKER.length
  const jsonStr = content.substring(jsonStart)
  const textBeforeHint = content.substring(0, markerIndex).trimEnd()

  try {
    const hint = JSON.parse(jsonStr) as ParseHintPayload
    if (!hint.supportedPatterns || !Array.isArray(hint.supportedPatterns)) {
      return null
    }
    return { textBeforeHint, hint }
  } catch {
    return null
  }
}

export function normalizeChatSessionStatus(value: ChatSessionStatusValue): typeof chatSessionStatusByIndex[number] {
  if (typeof value === 'number') {
    return chatSessionStatusByIndex[value] ?? 'Active'
  }

  const found = chatSessionStatusByIndex.find(v => v.toLowerCase() === value.toLowerCase())
  return found ?? 'Active'
}
