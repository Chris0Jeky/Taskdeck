import { AxiosError, AxiosHeaders, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import type { Card } from '../types/board'
import type { ChatMessage, ChatSession } from '../types/chat'
import type { Proposal } from '../types/automation'
import { DEMO_USER } from '../utils/demoIdentity'
import {
  DEMO_PROPOSAL_ID,
  buildDemoBoardDetail,
  buildDemoBoardList,
  buildDemoCalendarData,
  buildDemoCaptureItems,
  buildDemoCadence,
  buildDemoChatHealth,
  buildDemoChatSessions,
  buildDemoHomeSummary,
  buildDemoParticipants,
  buildDemoProposalPreview,
  buildDemoProposals,
  buildDemoSearchResult,
  buildDemoStreak,
  buildDemoThinkingDeck,
  buildDemoTodaySummary,
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
    const body = readBody(config)
    const selections = Array.isArray(body.proposals) ? body.proposals : []
    const approvedIds: string[] = []
    const ts = new Date().toISOString()
    for (const selection of selections) {
      if (!selection || typeof selection !== 'object') continue
      const id = 'id' in selection && typeof selection.id === 'string' ? selection.id : ''
      if (!id || !findProposal(id)) continue
      mutateProposal(id, { status: 'Approved', decidedAt: ts, decidedByUserId: DEMO_USER.id })
      approvedIds.push(id)
    }
    return ok({ approvedIds })
  }

  if (path === '/automation/proposals/execute' && method === 'post') {
    return ok({ results: [] })
  }

  const proposalMatch = path.match(/^\/automation\/proposals\/([^/]+)(?:\/(.+))?$/)
  if (proposalMatch) {
    const proposalId = proposalMatch[1] ?? ''
    const action = proposalMatch[2]
    if (!action && method === 'get') {
      const proposal = findProposal(proposalId)
      return proposal ? ok(proposal) : notFound('Proposal not found in demo mode.')
    }
    if (action === 'preview' && method === 'get') {
      const proposal = findProposal(proposalId)
      return proposal ? ok(buildDemoProposalPreview(proposal)) : notFound('Proposal not found in demo mode.')
    }
    if (action === 'diff' && method === 'get') {
      const proposal = findProposal(proposalId)
      return proposal
        ? ok({ diff: proposal.diffPreview ?? 'No diff in this demo proposal.' })
        : notFound('Proposal not found in demo mode.')
    }
    if (action === 'provenance' && method === 'get') {
      return ok([])
    }
    if (action === 'provenance/metadata' && method === 'get') {
      return ok({ provider: null, model: null, promptVersion: null })
    }
    if (action === 'confidence' && method === 'get') {
      return ok({
        overall: null,
        components: [],
        note: null,
        threshold: null,
        meetsThreshold: null,
        source: 'not-reported',
      })
    }
    if (action === 'side-effects' && method === 'get') {
      return ok({
        rows: [],
        reversibility: { summary: 'Demo apply is in-memory only.', description: 'This static demo does not change a live board.', windowMs: 0 },
      })
    }
    if (action === 'conflicts' && method === 'get') {
      return ok([])
    }
    if (action === 'history' && method === 'get') {
      return ok([])
    }
    if (action === 'similar-past' && method === 'get') {
      return ok({ decisions: [], applyRate: 0 })
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
      const user: ChatMessage = {
        id: `demo-chat-msg-${++chatSequence}`,
        sessionId: session.id,
        role: 'User',
        content,
        messageType: 'text',
        proposalId: null,
        tokenUsage: null,
        createdAt: ts,
      }
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
        createdAt: new Date(Date.parse(ts) + 1).toISOString(),
      }
      const updated: ChatSession = {
        ...session,
        updatedAt: reply.createdAt,
        recentMessages: [...session.recentMessages, user, reply],
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

  if (path === '/workspace/home' && method === 'get') {
    return ok(buildDemoHomeSummary())
  }

  if (path === '/workspace/today' && method === 'get') {
    return ok(buildDemoTodaySummary())
  }

  if (path === '/workspace/calendar' && method === 'get') {
    return ok(buildDemoCalendarData(search.get('from') ?? '', search.get('to') ?? ''))
  }

  if (path === '/search' && method === 'get') {
    return ok(buildDemoSearchResult(search.get('q') ?? ''))
  }

  if (path === '/today/cadence' && method === 'get') {
    return ok(buildDemoCadence())
  }

  if (path === '/today/streak' && method === 'get') {
    return ok(buildDemoStreak())
  }

  if (path === '/today/seal' && method === 'get') {
    return ok({
      date: search.get('date') ?? '',
      isSealed: false,
      sealedAt: null,
    })
  }

  if (path === '/today/tomorrow-note' && method === 'get') {
    return { status: 204, data: null }
  }

  if (path === '/workspace-insights' && method === 'get') {
    return ok([])
  }

  if (path === '/workspace-memory' && method === 'get') {
    return ok([])
  }

  if (path === '/capture/items' && method === 'get') {
    return ok(buildDemoCaptureItems())
  }

  if (path === '/workspace/collaboration' && method === 'get') {
    return ok({ memberCount: 2, hasCollaborators: true })
  }

  if (path === '/workspace/preferences' && method === 'get') {
    const summary = buildDemoHomeSummary()
    return ok({
      userId: DEMO_USER.id,
      workspaceMode: summary.workspaceMode,
      onboarding: summary.onboarding,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    })
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

  const thinkingDeck = path.match(/^\/boards\/([^/]+)\/cards\/([^/]+)\/thinking$/)
  if (thinkingDeck && method === 'get') {
    const card = findCard(thinkingDeck[1] ?? '', thinkingDeck[2] ?? '')
    return card ? ok(buildDemoThinkingDeck(card.id)) : notFound('Card not found in demo mode.')
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

  const columns = path.match(/^\/boards\/([^/]+)\/columns$/)
  if (columns && method === 'get') {
    return ok(buildDemoBoardDetail(columns[1] ?? '').board.columns)
  }

  if (method === 'get') {
    if (/\/(comments|labels|revisions|notifications|insights|memory)$/i.test(path)) {
      return ok([])
    }
    return notFound('This resource is not available in demo mode.')
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
