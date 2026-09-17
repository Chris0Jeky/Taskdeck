import { afterEach, describe, expect, it, vi } from 'vitest'
import type { InternalAxiosRequestConfig } from 'axios'
import { demoHttpAdapter, resetDemoHttpFixtures } from '../../api/demoAdapter'
import { DEMO_PROPOSAL_ID, DEMO_CHAT_SESSION_ID } from '../../utils/demoData'
import { DEMO_USER } from '../../utils/demoIdentity'
import { LOCAL_DEV_API_BASE_URL } from '../../utils/apiBaseUrl'

function config(method: string, url: string, data?: unknown): InternalAxiosRequestConfig {
  return {
    method,
    url,
    baseURL: LOCAL_DEV_API_BASE_URL,
    data,
    headers: { 'Content-Type': 'application/json' },
  } as InternalAxiosRequestConfig
}

describe('demoHttpAdapter', () => {
  afterEach(() => {
    resetDemoHttpFixtures()
    vi.restoreAllMocks()
  })

  it('does not open XHR even when the request is aimed at localhost:5000', async () => {
    const open = vi.spyOn(XMLHttpRequest.prototype, 'open')
    const send = vi.spyOn(XMLHttpRequest.prototype, 'send')

    const response = await demoHttpAdapter(config('get', '/automation/proposals'))

    expect(open).not.toHaveBeenCalled()
    expect(send).not.toHaveBeenCalled()
    expect(response.status).toBe(200)
    expect(Array.isArray(response.data)).toBe(true)
    expect(response.data).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: DEMO_PROPOSAL_ID, status: 'PendingReview', boardId: 'demo-board-1' }),
    ]))
  })

  it('loads the same pending proposal Home advertises', async () => {
    const list = await demoHttpAdapter(config('get', '/automation/proposals?limit=200'))
    expect(list.data).toHaveLength(1)
    expect(list.data[0].id).toBe(DEMO_PROPOSAL_ID)

    const one = await demoHttpAdapter(config('get', `/automation/proposals/${DEMO_PROPOSAL_ID}`))
    expect(one.data.id).toBe(DEMO_PROPOSAL_ID)
    expect(one.data.summary).toContain('dark mode')
  })

  it('loads chat sessions and health without a live provider', async () => {
    const health = await demoHttpAdapter(config('get', '/llm/chat/health'))
    expect(health.data).toMatchObject({ isAvailable: true, isMock: true, providerName: 'Mock' })

    const sessions = await demoHttpAdapter(config('get', '/llm/chat/sessions'))
    expect(sessions.data).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: DEMO_CHAT_SESSION_ID, boardId: 'demo-board-1' }),
    ]))
  })

  it('loads card parent candidates and assignees', async () => {
    const cards = await demoHttpAdapter(config('get', '/boards/demo-board-1/cards'))
    expect(Array.isArray(cards.data)).toBe(true)
    expect(cards.data.length).toBeGreaterThan(1)
    expect(cards.data.some((card: { id: string }) => card.id === 'demo-board-1-card-2')).toBe(true)

    const people = await demoHttpAdapter(config('get', '/boards/demo-board-1/participants'))
    expect(people.data).toEqual(expect.arrayContaining([
      expect.objectContaining({ userId: DEMO_USER.id }),
    ]))

    const board = await demoHttpAdapter(config('get', '/boards/demo-board-1'))
    expect(board.data.canWrite).toBe(true)
  })

  it('returns empty deep-review arrays instead of objects', async () => {
    const provenance = await demoHttpAdapter(config('get', `/automation/proposals/${DEMO_PROPOSAL_ID}/provenance`))
    expect(provenance.data).toEqual([])

    const history = await demoHttpAdapter(config('get', `/automation/proposals/${DEMO_PROPOSAL_ID}/history`))
    expect(history.data).toEqual([])

    const similar = await demoHttpAdapter(config('get', `/automation/proposals/${DEMO_PROPOSAL_ID}/similar-past`))
    expect(similar.data).toEqual({ decisions: [], applyRate: 0 })
  })

  it('builds proposal previews from the matched in-memory proposal', async () => {
    const approved = await demoHttpAdapter(config('post', `/automation/proposals/${DEMO_PROPOSAL_ID}/approve`))
    const preview = await demoHttpAdapter(config('get', `/automation/proposals/${DEMO_PROPOSAL_ID}/preview`))

    expect(preview.data).toMatchObject({
      proposalId: DEMO_PROPOSAL_ID,
      boardId: 'demo-board-1',
      status: 'Approved',
      proposalUpdatedAt: approved.data.updatedAt,
      effectiveRevisionId: approved.data.approvedRevisionId,
    })
    expect(Date.parse(preview.data.proposalUpdatedAt)).toBe(Date.parse(approved.data.updatedAt))
  })

  it('returns contract-shaped calendar and thinking payloads', async () => {
    const calendar = await demoHttpAdapter(config('get', '/workspace/calendar?from=2020-01-01T00:00:00.000Z&to=2099-01-01T00:00:00.000Z'))
    expect(calendar.data).toEqual(expect.objectContaining({
      from: expect.any(String),
      to: expect.any(String),
      totalCards: expect.any(Number),
    }))
    expect(Array.isArray(calendar.data.cards)).toBe(true)
    expect(calendar.data.totalCards).toBe(calendar.data.cards.length)
    expect(calendar.data.cards.length).toBeGreaterThan(0)
    expect(calendar.data.cards).toEqual(expect.arrayContaining([
      expect.objectContaining({ cardId: 'demo-board-1-card-2', boardId: 'demo-board-1' }),
    ]))

    const thinking = await demoHttpAdapter(config('get', '/boards/demo-board-1/cards/demo-board-1-card-3/thinking'))
    expect(thinking.data).toEqual({
      cardId: 'demo-board-1-card-3',
      revision: 1,
      schemaVersion: 1,
      canWrite: true,
      layers: [],
    })
  })

  it('returns approvedIds for batch approve', async () => {
    const detail = await demoHttpAdapter(config('get', `/automation/proposals/${DEMO_PROPOSAL_ID}`))
    const response = await demoHttpAdapter(config('post', '/automation/proposals/approve', {
      proposals: [{
        id: DEMO_PROPOSAL_ID,
        expectedProposalUpdatedAt: detail.data.updatedAt,
        expectedLatestRevisionId: detail.data.latestRevisionId,
      }],
    }))

    expect(response.data).toEqual({ approvedIds: [DEMO_PROPOSAL_ID] })

    const after = await demoHttpAdapter(config('get', `/automation/proposals/${DEMO_PROPOSAL_ID}`))
    expect(after.data.status).toBe('Approved')
  })

  it('applies approved proposals through the batch execute receipt', async () => {
    await demoHttpAdapter(config('post', `/automation/proposals/${DEMO_PROPOSAL_ID}/approve`))

    const response = await demoHttpAdapter(config('post', '/automation/proposals/execute', {
      proposals: [{ proposalId: DEMO_PROPOSAL_ID, approvedRevisionId: null, idempotencyKey: 'demo-execute-1' }],
    }))

    expect(response.data).toEqual({
      results: [{
        proposalId: DEMO_PROPOSAL_ID,
        outcome: 'Applied',
        errorCode: null,
        errorMessage: null,
        appliedOperations: 1,
      }],
    })

    const applied = await demoHttpAdapter(config('get', `/automation/proposals/${DEMO_PROPOSAL_ID}`))
    expect(applied.data.status).toBe('Applied')

    const replay = await demoHttpAdapter(config('post', '/automation/proposals/execute', {
      proposals: [{ proposalId: DEMO_PROPOSAL_ID, approvedRevisionId: null, idempotencyKey: 'demo-execute-1' }],
    }))
    expect(replay.data.results[0]).toMatchObject({ proposalId: DEMO_PROPOSAL_ID, outcome: 'Skipped' })
  })

  it('persists thinking saves and rejects stale revisions', async () => {
    const path = '/boards/demo-board-1/cards/demo-board-1-card-3/thinking'
    const initial = await demoHttpAdapter(config('get', path))
    const layers = [{
      id: 'demo-layer-1',
      kind: 'note',
      title: 'Working note',
      body: 'Keep the demo edit after refresh.',
      items: [],
      selectedOptionId: null,
    }]

    const saved = await demoHttpAdapter(config('put', path, { expectedRevision: initial.data.revision, layers }))
    expect(saved.data).toMatchObject({ cardId: 'demo-board-1-card-3', revision: 2, layers })

    const refreshed = await demoHttpAdapter(config('get', path))
    expect(refreshed.data).toMatchObject({ revision: 2, layers })

    await expect(demoHttpAdapter(config('put', path, { expectedRevision: 1, layers }))).rejects.toMatchObject({
      response: { status: 409, data: { message: 'Thinking deck changed in demo mode.' } },
    })
  })

  it('promotes a saved step into an in-memory linked card with live-contract idempotency', async () => {
    const path = '/boards/demo-board-1/cards/demo-board-1-card-3/thinking'
    const initial = await demoHttpAdapter(config('get', path))
    const layers = [{
      id: 'demo-steps-layer',
      kind: 'steps',
      title: 'Next steps',
      body: '',
      items: [{ id: 'demo-step', text: 'Create the follow-up card', completed: false, linkedCardId: null }],
      selectedOptionId: null,
    }]
    const saved = await demoHttpAdapter(config('put', path, { expectedRevision: initial.data.revision, layers }))
    const promotionPath = `${path}/steps/demo-steps-layer/demo-step/card`
    const request = { expectedRevision: saved.data.revision, columnId: 'demo-board-1-col-2', title: 'Follow-up card' }
    const beforeCards = await demoHttpAdapter(config('get', '/boards/demo-board-1/cards'))
    const beforeIds = beforeCards.data.map((card: { id: string }) => card.id)

    const promoted = await demoHttpAdapter(config('post', promotionPath, request))
    expect(promoted.data).toMatchObject({ cardId: 'demo-board-1-card-3', revision: 3, schemaVersion: 2, canWrite: true })
    const linkedCardId = promoted.data.layers[0].items[0].linkedCardId
    expect(linkedCardId).toEqual(expect.any(String))
    expect(beforeIds).not.toContain(linkedCardId)

    const cards = await demoHttpAdapter(config('get', '/boards/demo-board-1/cards'))
    const linked = cards.data.find((card: { id: string }) => card.id === linkedCardId)
    expect(linked).toMatchObject({ title: 'Follow-up card', description: 'Create the follow-up card', columnId: 'demo-board-1-col-2' })
    expect(linked).not.toHaveProperty('parentCardId')

    const replay = await demoHttpAdapter(config('post', promotionPath, { ...request, expectedRevision: 1 }))
    expect(replay.data).toEqual(promoted.data)
  })

  it('enforces the destination column WIP limit before linking a step', async () => {
    const path = '/boards/demo-board-1/cards/demo-board-1-card-3/thinking'
    const initial = await demoHttpAdapter(config('get', path))
    const layers = [{
      id: 'demo-steps-layer',
      kind: 'steps',
      title: 'Next steps',
      body: '',
      items: [1, 2, 3].map((number) => ({
        id: `demo-step-${number}`,
        text: `Create follow-up card ${number}`,
        completed: false,
        linkedCardId: null,
      })),
      selectedOptionId: null,
    }]
    const saved = await demoHttpAdapter(config('put', path, { expectedRevision: initial.data.revision, layers }))
    const promotionPath = (itemId: string) => `${path}/steps/demo-steps-layer/${itemId}/card`
    const columnId = 'demo-board-1-col-2'

    const first = await demoHttpAdapter(config('post', promotionPath('demo-step-1'), {
      expectedRevision: saved.data.revision,
      columnId,
      title: 'Follow-up card 1',
    }))
    const second = await demoHttpAdapter(config('post', promotionPath('demo-step-2'), {
      expectedRevision: first.data.revision,
      columnId,
      title: 'Follow-up card 2',
    }))

    await expect(demoHttpAdapter(config('post', promotionPath('demo-step-3'), {
      expectedRevision: second.data.revision,
      columnId,
      title: 'Follow-up card 3',
    }))).rejects.toMatchObject({
      response: {
        status: 400,
        data: {
          errorCode: 'WipLimitExceeded',
          message: "Cannot add card, column 'In Progress' has reached its WIP limit of 3",
        },
      },
    })

    const cards = await demoHttpAdapter(config('get', '/boards/demo-board-1/cards'))
    expect(cards.data.filter((card: { columnId: string }) => card.columnId === columnId)).toHaveLength(3)
    expect(second.data.layers[0].items[2].linkedCardId).toBeNull()
  })

  it('persists the user chat message before the assistant reply', async () => {
    const reply = await demoHttpAdapter(config('post', `/llm/chat/sessions/${DEMO_CHAT_SESSION_ID}/messages`, {
      content: 'Can you split this further?',
    }))
    expect(reply.data.role).toBe('Assistant')

    const session = await demoHttpAdapter(config('get', `/llm/chat/sessions/${DEMO_CHAT_SESSION_ID}`))
    const messages = session.data.recentMessages as Array<{ role: string; content: string }>
    expect(messages[messages.length - 2]).toMatchObject({
      role: 'User',
      content: 'Can you split this further?',
    })
    expect(messages[messages.length - 1]).toMatchObject({
      role: 'Assistant',
      id: reply.data.id,
    })
  })

  it('returns contract-shaped search and today payloads', async () => {
    const search = await demoHttpAdapter(config('get', '/search?q=dark'))
    expect(Array.isArray(search.data.boards)).toBe(true)
    expect(Array.isArray(search.data.cards)).toBe(true)
    expect(search.data.cards).toEqual(expect.arrayContaining([
      expect.objectContaining({ title: 'Implement dark mode' }),
    ]))

    const cadence = await demoHttpAdapter(config('get', '/today/cadence?date=2026-09-17'))
    expect(Array.isArray(cadence.data.buckets)).toBe(true)
    expect(cadence.data.buckets).toHaveLength(24)

    const streak = await demoHttpAdapter(config('get', '/today/streak?days=90'))
    expect(Array.isArray(streak.data.days)).toBe(true)

    const insights = await demoHttpAdapter(config('get', '/workspace-insights?boardId=demo-board-1'))
    expect(insights.data).toEqual([])

    const memory = await demoHttpAdapter(config('get', '/workspace-memory?boardId=demo-board-1'))
    expect(memory.data).toEqual([])
  })

  it('returns arrays for static insight actions and evidence previews', async () => {
    const source = await demoHttpAdapter(config('get', '/workspace-insights/observation-source?boardId=demo-board-1&cardId=demo-board-1-card-3'))
    expect(source.data).toEqual({
      cardId: 'demo-board-1-card-3',
      title: 'Implement dark mode',
      text: 'Apply Obsidian & Ember tokens across all views.',
      fingerprint: 'demo-demo-board-1-card-3',
      truncated: false,
    })

    const analyzed = await demoHttpAdapter(config('post', '/workspace-insights/analyze', { boardId: 'demo-board-1' }))
    expect(analyzed.data).toEqual([])

    const generated = await demoHttpAdapter(config('post', '/workspace-insights/model-analysis', {
      boardId: 'demo-board-1',
      cardId: 'demo-board-1-card-3',
      fingerprint: 'demo-demo-board-1-card-3',
    }))
    expect(generated.data).toEqual([])
  })

  it('rejects unmatched GET paths instead of returning {}', async () => {
    await expect(demoHttpAdapter(config('get', '/metrics/boards/demo-board-1'))).rejects.toMatchObject({
      response: { status: 404, data: { message: 'This resource is not available in demo mode.' } },
    })
    await expect(demoHttpAdapter(config('get', '/workspace/mystery'))).rejects.toMatchObject({
      response: { status: 404 },
    })
  })
})
