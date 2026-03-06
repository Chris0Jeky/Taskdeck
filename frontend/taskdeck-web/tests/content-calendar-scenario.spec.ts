import { describe, expect, it } from 'vitest'

import { run as runLegacyContentCalendarScenario } from '../scripts/scenarios/content-calendar.mjs'
import { loadJsonScenario, runJsonScenario } from '../scripts/scenario-json-runner.mjs'

const CONTENT_CALENDAR_COLUMNS = [
  { id: 'column-ideas', name: 'Ideas', position: 0 },
  { id: 'column-drafting', name: 'Drafting', position: 1 },
  { id: 'column-review', name: 'Review', position: 2 },
  { id: 'column-scheduled', name: 'Scheduled', position: 3 },
]

const CONTENT_CALENDAR_LABELS = [
  { id: 'label-needs-draft', name: 'needs-draft', colorHex: '#7C3AED' },
  { id: 'label-needs-review', name: 'needs-review', colorHex: '#D97706' },
  { id: 'label-publish-week', name: 'publish-week', colorHex: '#059669' },
]

function createContentCalendarApi() {
  let board: { id: string; name: string; description: string | null } | null = null
  let boardColumns: typeof CONTENT_CALENDAR_COLUMNS = []
  let boardLabels: typeof CONTENT_CALENDAR_LABELS = []
  const cards: Array<{
    id: string
    boardId: string
    columnId: string
    title: string
    description: string | null
    dueDate: string | null
    isBlocked: boolean
    labels: Array<{ id: string; name: string; colorHex: string }>
  }> = []
  const queueRequests = new Map<string, { id: string; status: string; errorMessage: string | null }>()
  const proposals = new Map<string, { id: string; sourceReferenceId: string }>()
  const opsRuns = new Map<string, { id: string; status: string; exitCode: number }>()

  let nextCardId = 1
  let nextQueueRequestId = 1
  let nextProposalId = 1
  let nextOpsRunId = 1

  return {
    getState() {
      return {
        board,
        columns: boardColumns,
        labels: boardLabels,
        cards,
      }
    },

    async get(requestPath: string) {
      if (requestPath === '/boards/board-1') {
        return board ?? { id: 'board-1', name: 'DEMO: Content Calendar Scenario', description: null }
      }

      if (requestPath === '/boards/board-1/starter-packs/catalog') {
        return [
          {
            id: 'board-blueprint-content-calendar',
            manifest: {
              packId: 'board-blueprint-content-calendar',
            },
          },
        ]
      }

      if (requestPath === '/boards/board-1/columns') {
        return boardColumns
      }

      if (requestPath === '/boards/board-1/labels') {
        return boardLabels
      }

      if (requestPath === '/boards/board-1/cards') {
        return cards
      }

      if (requestPath.startsWith('/llm-queue/user')) {
        return Array.from(queueRequests.values())
      }

      if (requestPath.startsWith('/automation/proposals')) {
        return Array.from(proposals.values())
      }

      if (requestPath.startsWith('/ops/cli/runs/') && requestPath.endsWith('/logs')) {
        const runId = requestPath.split('/')[4]
        return [{ id: `log-${runId}`, line: 'ok' }]
      }

      if (requestPath.startsWith('/ops/cli/runs/')) {
        const runId = requestPath.split('/').pop() ?? ''
        return opsRuns.get(runId) ?? null
      }

      throw new Error(`Unexpected GET ${requestPath}`)
    },

    async post(requestPath: string, { body }: { body?: Record<string, unknown> } = {}) {
      if (requestPath === '/boards') {
        board = {
          id: 'board-1',
          name: String(body?.name ?? 'DEMO: Content Calendar Scenario'),
          description: typeof body?.description === 'string' ? body.description : null,
        }
        return board
      }

      if (requestPath === '/boards/board-1/starter-packs/apply') {
        boardColumns = CONTENT_CALENDAR_COLUMNS.map((column) => ({ ...column }))
        boardLabels = CONTENT_CALENDAR_LABELS.map((label) => ({ ...label }))
        return {
          applied: true,
          conflicts: [],
        }
      }

      if (requestPath === '/boards/board-1/cards') {
        const labelIds = Array.isArray(body?.labelIds) ? body.labelIds.map((value) => String(value)) : []
        const card = {
          id: `card-${nextCardId++}`,
          boardId: 'board-1',
          columnId: String(body?.columnId ?? ''),
          title: String(body?.title ?? ''),
          description: typeof body?.description === 'string' ? body.description : null,
          dueDate: typeof body?.dueDate === 'string' ? body.dueDate : null,
          isBlocked: false,
          labels: boardLabels.filter((label) => labelIds.includes(label.id)),
        }
        cards.push(card)
        return card
      }

      if (requestPath === '/llm-queue') {
        const requestId = `queue-${nextQueueRequestId++}`
        queueRequests.set(requestId, {
          id: requestId,
          status: 'Completed',
          errorMessage: null,
        })
        proposals.set(`proposal-${nextProposalId}`, {
          id: `proposal-${nextProposalId++}`,
          sourceReferenceId: requestId,
        })
        return { id: requestId }
      }

      if (
        requestPath.startsWith('/automation/proposals/') &&
        (requestPath.endsWith('/approve') || requestPath.endsWith('/execute'))
      ) {
        return {}
      }

      if (requestPath === '/ops/cli/run') {
        const runId = `ops-${nextOpsRunId++}`
        opsRuns.set(runId, {
          id: runId,
          status: 'Completed',
          exitCode: 0,
        })
        return {
          id: runId,
          status: 'Queued',
        }
      }

      throw new Error(`Unexpected POST ${requestPath}`)
    },
  }
}

describe('content-calendar scenario compatibility', () => {
  it('keeps the JSON content-calendar scenario aligned with the shipped starter-pack contract', async () => {
    const api = createContentCalendarApi()
    const scenario = await loadJsonScenario('content-calendar')

    const result = await runJsonScenario({
      api,
      config: { uiBaseUrl: 'http://localhost:5173' },
      scenario,
      options: { skipLlm: true },
    })

    expect(result.boards).toEqual([{ id: 'board-1', name: 'DEMO: Content Calendar Scenario' }])
    expect(result.results.steps).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ step: '8:queueInstruction', status: 'skipped', reason: '--skip-llm' }),
        expect.objectContaining({ step: 'Ops: health.check', status: 'ok' }),
      ]),
    )

    const state = api.getState()
    expect(state.cards).toHaveLength(5)
    expect(state.cards.map((card) => card.title)).toEqual([
      'Blog: Why proposal-first automations are safer',
      'Release notes draft: Capture Loop MVP',
      'Design: Automations empty-state panel',
      'Tweet thread: Taskdeck demo walkthrough',
      'Schedule: Starter packs for common workflows',
    ])
    expect(state.cards.map((card) => card.labels.map((label) => label.name))).toEqual([
      ['needs-draft'],
      ['needs-draft', 'publish-week'],
      ['needs-review'],
      ['publish-week'],
      ['publish-week'],
    ])
  })

  it('keeps the legacy JS content-calendar scenario aligned with the shipped starter-pack contract', async () => {
    const api = createContentCalendarApi()

    const result = await runLegacyContentCalendarScenario({
      api,
      config: { uiBaseUrl: 'http://localhost:5173' },
    })

    expect(result.board).toEqual({
      id: 'board-1',
      name: 'DEMO: Content Calendar Scenario',
    })

    const state = api.getState()
    expect(state.cards).toHaveLength(5)
    expect(state.cards.every((card) => card.columnId !== '')).toBe(true)
    expect(state.cards.flatMap((card) => card.labels.map((label) => label.name))).not.toContain('writing')
    expect(state.cards.flatMap((card) => card.labels.map((label) => label.name))).toContain('publish-week')
  })
})
