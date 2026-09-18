import { describe, expect, it } from 'vitest'
import type { ChatMessage } from '../../types/chat'
import { createLocalUserMessage, mergeLocalMessages, retainLocalMessages } from '../../utils/chatTranscript'

function message(id: string, overrides: Partial<ChatMessage> = {}): ChatMessage {
  return {
    id,
    sessionId: 's1',
    role: 'Assistant',
    content: id,
    messageType: 'text',
    proposalId: null,
    tokenUsage: null,
    createdAt: '2026-05-16T10:00:00.000Z',
    ...overrides,
  }
}

describe('createLocalUserMessage', () => {
  it('places the local user turn immediately before the assistant reply', () => {
    const result = createLocalUserMessage(
      's1',
      'create card for release notes',
      '2026-05-16T10:01:00.000Z',
      7,
    )

    expect(result).toMatchObject({
      id: 'local-user-s1-7',
      sessionId: 's1',
      role: 'User',
      content: 'create card for release notes',
      messageType: 'text',
      proposalId: null,
      tokenUsage: null,
      createdAt: '2026-05-16T10:00:59.999Z',
    })
  })

  it('falls back to a current ISO timestamp when the reply timestamp is invalid', () => {
    const before = Date.now()
    const result = createLocalUserMessage('s1', 'retry instruction', 'not-a-date', 8)
    const createdAt = Date.parse(result.createdAt)

    expect(Number.isFinite(createdAt)).toBe(true)
    expect(createdAt).toBeGreaterThanOrEqual(before)
    expect(createdAt).toBeLessThanOrEqual(Date.now())
  })
})

describe('retainLocalMessages', () => {
  it('updates duplicate ids without disturbing first-seen order', () => {
    const existing = [message('local-1', { content: 'old' }), message('local-2')]
    const incoming = [message('local-1', { content: 'new' }), message('reply-1')]

    expect(retainLocalMessages(existing, incoming)).toEqual([
      message('local-1', { content: 'new' }),
      message('local-2'),
      message('reply-1'),
    ])
  })
})

describe('mergeLocalMessages', () => {
  it('keeps authoritative messages first and appends only unknown local ids', () => {
    const authoritative = [
      message('server-1', { content: 'server message' }),
      message('shared', { content: 'authoritative copy' }),
    ]
    const local = [
      message('shared', { content: 'stale local copy' }),
      message('local-1', { content: 'pending instruction' }),
    ]

    expect(mergeLocalMessages(authoritative, local)).toEqual([
      ...authoritative,
      message('local-1', { content: 'pending instruction' }),
    ])
  })
})
