import type { ChatRoleValue, ChatSessionStatusValue } from '../types/chat'

const chatRoleByIndex = ['User', 'Assistant', 'System'] as const
const chatSessionStatusByIndex = ['Active', 'Archived'] as const

export function normalizeChatRole(value: ChatRoleValue): typeof chatRoleByIndex[number] {
  if (typeof value === 'number') {
    return chatRoleByIndex[value] ?? 'User'
  }

  const found = chatRoleByIndex.find(v => v.toLowerCase() === value.toLowerCase())
  return found ?? 'User'
}

export function normalizeChatSessionStatus(value: ChatSessionStatusValue): typeof chatSessionStatusByIndex[number] {
  if (typeof value === 'number') {
    return chatSessionStatusByIndex[value] ?? 'Active'
  }

  const found = chatSessionStatusByIndex.find(v => v.toLowerCase() === value.toLowerCase())
  return found ?? 'Active'
}
