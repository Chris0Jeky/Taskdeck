import { AxiosError, AxiosHeaders, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import type { Card } from '../types/board'
import type { ChatMessage, ChatSession } from '../types/chat'
import type { Proposal } from '../types/automation'
import { DEMO_USER } from '../utils/demoMode'
import {
  DEMO_PROPOSAL_ID,
  buildDemoBoardDetail,
  buildDemoBoardList,
  buildDemoChatHealth,
  buildDemoChatSessions,
  buildDemoParticipants,
  buildDemoProposalPreview,
  buildDemoProposals,
} from '../utils/demoData'

interface DemoHttpResult {
  status: number
  data: unknown
}

function combineAxiosUrl(baseURL: string, url: string): string {
  if (!url) return baseURL
  if (/^https?:\/\//i.test(url)) return url
  if (!baseURL) return url
  return `${baseURL.replace(/\/+$/, '')}/${url.replace(/^\/+/, '')}`
}

function requestPath(config: InternalAxiosRequestConfig): { method: string; path: string; search: URLSearchParams } {
  const method = (config.method ?? 'get').toLowerCase()
  const combined = combineAxiosUrl(config.baseURL ?? '', config.url ?? '')
  const parsed = new URL(combined, 'http://taskdeck.demo')
  let path = parsed.pathname
  if (path.startsWith('/api/')) path = path.slice(4)
  else if (path === '/api') path = '/'
  if (path.length > 1 && path.endsWith('/')) path = path.slice(0, -1)
  const search = parsed.searchParams
  const params = config.params
  if (params && typeof params === 'object') {
    for (const [key, value] of Object.entries(params as Record<string, unknown>)) {
      if (value === undefined || value === null) continue
      search.set(key, String(value))
    }
  }
  return { method, path, search }
}

function decodeSegment(value: string): string {
  try {
    return decodeURIComponent(value)
  } catch {
    return value
  }
}

function ok(data: unknown, status = 200): DemoHttpResult {
  return { status, data }
}

function notFound(message = 'Not found in demo mode.'): DemoHttpResult {
  return { status: 404, data: { message } }
}

let proposals = buildDemoProposals()
let chatSessions = buildDemoChatSessions()
let chatSequence = 2

export function resetDemoHttpFixtures(): void {
  proposals = buildDemoProposals()
  chatSessions = buildDemoChatSessions()
  chatSequence = 2
}

function findProposal(id: string): Proposal | undefined {
  return proposals.find((proposal) => proposal.id === decodeSegment(id))
}

function findSession(id: string): ChatSession | undefined {
  return chatSessions.find((session) => session.id === decodeSegment(id))
}

function boardCards(boardId: string): Card[] {
  return buildDemoBoardDetail(decodeSegment(boardId)).cards
}

function findCard(boardId: string, cardId: string): Card | undefined {
  return boardCards(boardId).find((card) => card.id === decodeSegment(cardId))
}

function mutateProposal(id: string, patch: Partial<Proposal>): DemoHttpResult {
  const current = findProposal(id)
  if (!current) return notFound('Proposal not found in demo mode.')
  const updated: Proposal = { ...current, ...patch, updatedAt: new Date().toISOString() }
  proposals = proposals.map((proposal) => (proposal.id === current.id ? updated : proposal))
  return ok(updated)
}

function readBody(config: InternalAxiosRequestConfig): Record<string, unknown> {
  const body = config.data
  if (!body) return {}
  if (typeof body === 'string') {
    try {
      return JSON.parse(body) as Record<string, unknown>
    } catch {
      return {}
    }
  }
  if (typeof body === 'object') return body as Record<string, unknown>
  return {}
}

function resolveDemoHttpResult(config: InternalAxiosRequestConfig): DemoHttpResult {
  const { method, path, search } = requestPath(config)

  if (path === '/health/live' || path === '/health/ready') {
    return ok({ status: 'Healthy', version: '', timestamp: new Date().toISOString() })
  }

  if (path === '/telemetry/config') {
    return ok({
      sentry: { enabled: false, dsn: '', environment: 'demo', tracesSampleRate: 0 },
      analytics: { enabled: false, provider: '', scriptUrl: '', siteId: '' },
      telemetry: { enabled: false },
    })
  }

  if (path === '/telemetry/events' && method === 'post') {
    return ok({ recorded: 0, message: 'Telemetry is disabled in demo mode.' })
  }

  if (path === '/automation/proposals') {
    if (method === 'get') {
      const boardId = search.get('boardId')
      const items = boardId ? proposals.filter((proposal) => proposal.boardId === boardId) : proposals
      return ok(items)
    }
    return ok({ message: 'Demo mode is view-only for new proposals.' }, 200)
  }

  if (path === '/automation/proposals/dismiss' && method === 'post') {
    const body = readBody(config)
    const ids = Array.isArray(body.ids) ? body.ids.map(String) : []
    let dismissed = 0
    for (const id of ids) {
      if (findProposal(id)) {
        mutateProposal(id, { status: 'Dismissed' })
        dismissed += 1
      }
    }
    return ok({ dismissed })
  }

  if (path === '/automation/proposals/approve' && method === 'post') {
    return ok({ results: [] })
  }

  if (path === '/automation/proposals/execute' && method === 'post') {
    return ok({ results: [] })
  }

  const proposalMatch = path.match(/^\/automation\/proposals\/([^/]+)(?:\/([^/]+))?$/)
  if (proposalMatch) {
    const proposalId = proposalMatch[1] ?? ''
    const action = proposalMatch[2]
    if (!action && method === 'get') {
      const proposal = findProposal(proposalId)
      return proposal ? ok(proposal) : notFound('Proposal not found in demo mode.')
    }
    if (action === 'preview' && method === 'get') {
      const proposal = findProposal(proposalId)
      return proposal ? ok(buildDemoProposalPreview(proposal.id)) : notFound('Proposal not found in demo mode.')
    }
    if (action === 'diff' && method === 'get') {
      const proposal = findProposal(proposalId)
      return proposal
        ? ok({ diff: proposal.diffPreview ?? 'No diff in this demo proposal.' })
        : notFound('Proposal not found in demo mode.')
    }
    if (action === 'approve' && method === 'post') {
      return mutateProposal(proposalId, { status: 'Approved', decidedAt: new Date().toISOString(), decidedByUserId: DEMO_USER.id })
    }
    if (action === 'reject' && method === 'post') {
      return mutateProposal(proposalId, { status: 'Rejected', decidedAt: new Date().toISOString(), decidedByUserId: DEMO_USER.id })
    }
    if (action === 'defer' && method === 'post') {
      const until = new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString()
      return mutateProposal(proposalId, { deferredUntil: until })
    }
    if (action === 'execute' && method === 'post') {
      return mutateProposal(proposalId, { status: 'Applied', appliedAt: new Date().toISOString() })
    }
    if (action === 'dismiss' && method === 'post') {
      return mutateProposal(proposalId, { status: 'Dismissed' })
    }
    if (action === 'feedback' && method === 'post') {
      return ok(null, 204)
    }
  }

  if (path === '/llm/chat/health') {
    return ok(buildDemoChatHealth())
  }

  if (path === '/llm/chat/sessions') {
    if (method === 'get') return ok(chatSessions)
    if (method === 'post') {
      const body = readBody(config)
      const ts = new Date().toISOString()
      const created: ChatSession = {
        id: `demo-chat-session-${++chatSequence}`,
        userId: DEMO_USER.id,
        boardId: typeof body.boardId === 'string' ? body.boardId : null,
        title: typeof body.title === 'string' && body.title.trim() ? body.title.trim() : 'Demo chat',
        status: 'Active',
        createdAt: ts,
        updatedAt: ts,
        recentMessages: [],
      }
      chatSessions = [created, ...chatSessions]
      return ok(created)
    }
  }

  const chatSessionMatch = path.match(/^\/llm\/chat\/sessions\/([^/]+)(?:\/([^/]+))?$/)
  if (chatSessionMatch) {
    const sessionId = chatSessionMatch[1] ?? ''
    const action = chatSessionMatch[2]
    const session = findSession(sessionId)
    if (!session) return notFound('Chat session not found in demo mode.')
    if (!action && method === 'get') return ok(session)
    if (action === 'messages' && method === 'post') {
      const body = readBody(config)
      const content = typeof body.content === 'string' ? body.content : ''
      const ts = new Date().toISOString()
      const reply: ChatMessage = {
        id: `demo-chat-msg-${++chatSequence}`,
        sessionId: session.id,
        role: 'Assistant',
        content: content.trim()
          ? `Demo reply: review stays explicit. Open Review to decide on "${DEMO_PROPOSAL_ID === proposals[0]?.id ? proposals[0].summary : 'the pending proposal'}".`
          : 'Demo reply: this static Pages demo has no live model.',
        messageType: 'text',
        proposalId: proposals[0]?.id ?? null,
        tokenUsage: 12,
        createdAt: ts,
      }
      const updated: ChatSession = {
        ...session,
        updatedAt: ts,
        recentMessages: [...session.recentMessages, reply],
      }
      chatSessions = chatSessions.map((item) => (item.id === session.id ? updated : item))
      return ok(reply)
    }
    if (action === 'board' && method === 'post') {
      const body = readBody(config)
      const boardId = typeof body.boardId === 'string' ? body.boardId : session.boardId
      const updated: ChatSession = { ...session, boardId, updatedAt: new Date().toISOString() }
      chatSessions = chatSessions.map((item) => (item.id === session.id ? updated : item))
      return ok(updated)
    }
  }

  if (path === '/boards' && method === 'get') {
    const items = buildDemoBoardList()
    return ok({
      items,
      totalCount: items.length,
      hasMore: false,
      offset: 0,
      limit: 200,
    })
  }

  const archivedCards = path.match(/^\/boards\/([^/]+)\/cards\/archived$/)
  if (archivedCards && method === 'get') {
    return ok([])
  }

  const participants = path.match(/^\/boards\/([^/]+)\/participants$/)
  if (participants && method === 'get') {
    return ok(buildDemoParticipants())
  }

  const cardAssignments = path.match(/^\/boards\/([^/]+)\/cards\/([^/]+)\/assignments$/)
  if (cardAssignments && method === 'put') {
    const card = findCard(cardAssignments[1] ?? '', cardAssignments[2] ?? '')
    if (!card) return notFound('Card not found in demo mode.')
    const body = readBody(config)
    const userIds = Array.isArray(body.userIds) ? body.userIds.map(String) : []
    const people = buildDemoParticipants()
    const ts = new Date().toISOString()
    const assignments = userIds.map((userId) => {
      const person = people.find((item) => item.userId === userId)
      return {
        userId,
        displayName: person?.displayName ?? userId,
        assignedAt: ts,
        assignedByUserId: DEMO_USER.id,
      }
    })
    return ok({ ...card, assignments, updatedAt: ts })
  }

  const oneCard = path.match(/^\/boards\/([^/]+)\/cards\/([^/]+)$/)
  if (oneCard && method === 'get') {
    const card = findCard(oneCard[1] ?? '', oneCard[2] ?? '')
    return card ? ok(card) : notFound('Card not found in demo mode.')
  }

  const boardCardsMatch = path.match(/^\/boards\/([^/]+)\/cards$/)
  if (boardCardsMatch && method === 'get') {
    return ok(boardCards(boardCardsMatch[1] ?? ''))
  }

  const oneBoard = path.match(/^\/boards\/([^/]+)$/)
  if (oneBoard && method === 'get') {
    return ok(buildDemoBoardDetail(oneBoard[1] ?? '').board)
  }

  if (method === 'get') {
    if (/\/(comments|labels|revisions|notifications|insights|memory)$/i.test(path) || path.endsWith('s')) {
      return ok([])
    }
    return ok({})
  }

  if (method === 'delete') return ok(null, 204)
  return ok({ message: 'This action is view-only in the static demo.' })
}

function toAxiosResponse(config: InternalAxiosRequestConfig, result: DemoHttpResult): AxiosResponse {
  return {
    data: result.data,
    status: result.status,
    statusText: result.status === 204 ? 'No Content' : result.status < 400 ? 'OK' : 'Error',
    headers: AxiosHeaders.from({ 'content-type': 'application/json' }),
    config,
    request: { __taskdeckDemoAdapter: true },
  }
}

/**
 * Axios adapter used only in demo/static mode. It never opens a socket, so a
 * GitHub Pages build cannot call `localhost:5000` even if a caller forgot a
 * store-level demo guard.
 */
export const demoHttpAdapter: AxiosAdapter = (config) => {
  const result = resolveDemoHttpResult(config)
  const response = toAxiosResponse(config, result)
  if (result.status >= 400) {
    return Promise.reject(new AxiosError(
      `Demo adapter status ${result.status}`,
      AxiosError.ERR_BAD_RESPONSE,
      config,
      response.request,
      response,
    ))
  }
  return Promise.resolve(response)
}
