import { describe, expect, it } from 'vitest'

import { run as runEngineeringSprintScenario } from '../scripts/scenarios/engineering-sprint.mjs'
import { run as runContentCalendarScenario } from '../scripts/scenarios/content-calendar.mjs'

const ENGINEERING_SPRINT_PACK = {
  id: 'board-blueprint-engineering-sprint',
  manifest: {
    labels: [
      { name: 'priority-high', color: '#B91C1C' },
      { name: 'bug', color: '#1D4ED8' },
      { name: 'tech-debt', color: '#374151' },
    ],
    columns: [
      { name: 'Backlog', position: 0 },
      { name: 'In Progress', position: 1 },
      { name: 'Review', position: 2 },
      { name: 'Done', position: 3 },
    ],
    seedCards: [
      {
        title: 'Review sprint carry-over',
        description: 'Confirm which items stay in the next sprint.',
        columnName: 'Backlog',
        labels: ['tech-debt'],
      },
    ],
  },
}

const CONTENT_CALENDAR_PACK = {
  id: 'board-blueprint-content-calendar',
  manifest: {
    labels: [
      { name: 'needs-draft', color: '#7C3AED' },
      { name: 'needs-review', color: '#D97706' },
      { name: 'publish-week', color: '#059669' },
    ],
    columns: [
      { name: 'Ideas', position: 0 },
      { name: 'Drafting', position: 1 },
      { name: 'Review', position: 2 },
      { name: 'Scheduled', position: 3 },
    ],
    seedCards: [
      {
        title: 'Plan weekly editorial slate',
        description: 'Choose top topics and assign owners.',
        columnName: 'Ideas',
        labels: ['publish-week'],
      },
    ],
  },
}

function createEngineeringSprintApi() {
  const state = {
    nextId: 1,
    board: null as null | { id: string; name: string; description?: string | null },
    labels: [] as Array<{ id: string; name: string; colorHex: string }>,
    columns: [] as Array<{ id: string; name: string; position: number; wipLimit?: number | null }>,
    cards: [] as Array<{
      id: string
      columnId: string
      title: string
      description?: string | null
      dueDate?: string | null
      isBlocked?: boolean
      blockReason?: string | null
      labels: Array<{ id: string; name: string }>
    }>,
    comments: [] as Array<{ id: string; cardId: string; content: string }>,
    queueRequests: [] as Array<{ id: string; status: string; payload: string; boardId: string; errorMessage?: string | null }>,
    proposals: [] as Array<{ id: string; sourceReferenceId: string; boardId: string; instruction: string }>,
  }

  const nextId = (prefix: string) => `${prefix}-${state.nextId++}`

  const findColumnByName = (columnName: string) =>
    state.columns.find((column) => column.name.toLowerCase() === columnName.toLowerCase()) ?? null

  const labelRefsFromIds = (labelIds: string[] | undefined) =>
    (labelIds ?? [])
      .map((labelId) => state.labels.find((label) => label.id === labelId))
      .filter((label): label is { id: string; name: string; colorHex: string } => Boolean(label))
      .map((label) => ({ id: label.id, name: label.name }))

  const seedPack = () => {
    state.labels = ENGINEERING_SPRINT_PACK.manifest.labels.map((label, index) => ({
      id: `label-${index + 1}`,
      name: label.name,
      colorHex: label.color,
    }))
    state.columns = ENGINEERING_SPRINT_PACK.manifest.columns.map((column, index) => ({
      id: `column-${index + 1}`,
      name: column.name,
      position: column.position,
      wipLimit: null,
    }))
    state.cards = ENGINEERING_SPRINT_PACK.manifest.seedCards.map((seedCard) => {
      const column = findColumnByName(seedCard.columnName)
      if (!column) {
        throw new Error(`Unknown seed column ${seedCard.columnName}`)
      }

      const labelIds = state.labels
        .filter((label) => seedCard.labels.includes(label.name))
        .map((label) => label.id)

      return {
        id: nextId('card'),
        columnId: column.id,
        title: seedCard.title,
        description: seedCard.description,
        dueDate: null,
        isBlocked: false,
        blockReason: null,
        labels: labelRefsFromIds(labelIds),
      }
    })
  }

  const applyInstruction = (instruction: string) => {
    const createMatch = instruction.match(/^create card "([^"]+)"(?: in column "([^"]+)")?(?: with description "([^"]+)")?$/i)
    if (!createMatch) {
      throw new Error(`Unsupported queue instruction in test harness: ${instruction}`)
    }

    const [, title, columnName, description] = createMatch
    const column = columnName ? findColumnByName(columnName) : state.columns[0] ?? null
    if (!column) {
      throw new Error(`Unable to resolve target column for instruction: ${instruction}`)
    }

    state.cards.push({
      id: nextId('card'),
      columnId: column.id,
      title,
      description: description ?? null,
      dueDate: null,
      isBlocked: false,
      blockReason: null,
      labels: [],
    })
  }

  return {
    state,
    async get(path: string) {
      if (path === `/boards/${state.board?.id}/starter-packs/catalog`) {
        return [ENGINEERING_SPRINT_PACK]
      }

      if (path === `/boards/${state.board?.id}/columns`) {
        return state.columns
      }

      if (path === `/boards/${state.board?.id}/labels`) {
        return state.labels
      }

      if (path === `/boards/${state.board?.id}/cards`) {
        return state.cards
      }

      if (path === '/llm-queue/user?limit=200') {
        return state.queueRequests
      }

      if (path === '/automation/proposals?limit=200') {
        return state.proposals.map((proposal) => ({
          id: proposal.id,
          sourceReferenceId: proposal.sourceReferenceId,
        }))
      }

      throw new Error(`Unexpected GET ${path}`)
    },

    async post(path: string, { body }: { body?: Record<string, unknown> } = {}) {
      if (path === '/boards') {
        state.board = {
          id: 'board-1',
          name: String(body?.name ?? 'Demo Board'),
          description: typeof body?.description === 'string' ? body.description : null,
        }
        return state.board
      }

      if (path === `/boards/${state.board?.id}/starter-packs/apply`) {
        seedPack()
        return { applied: true }
      }

      if (path === `/boards/${state.board?.id}/cards`) {
        const card = {
          id: nextId('card'),
          columnId: String(body?.columnId ?? ''),
          title: String(body?.title ?? ''),
          description: typeof body?.description === 'string' ? body.description : null,
          dueDate: typeof body?.dueDate === 'string' ? body.dueDate : null,
          isBlocked: false,
          blockReason: null,
          labels: labelRefsFromIds(body?.labelIds as string[] | undefined),
        }
        state.cards.push(card)
        return card
      }

      if (path.startsWith(`/boards/${state.board?.id}/cards/`) && path.endsWith('/comments')) {
        const cardId = path.split('/')[4]
        const comment = {
          id: nextId('comment'),
          cardId,
          content: String(body?.content ?? ''),
        }
        state.comments.push(comment)
        return comment
      }

      if (path === '/llm-queue') {
        const request = {
          id: nextId('queue'),
          status: 'Completed',
          payload: String(body?.payload ?? ''),
          boardId: String(body?.boardId ?? state.board?.id ?? ''),
          errorMessage: null,
        }
        state.queueRequests.push(request)
        state.proposals.push({
          id: nextId('proposal'),
          sourceReferenceId: request.id,
          boardId: request.boardId,
          instruction: request.payload,
        })
        return request
      }

      if (path.startsWith('/automation/proposals/') && path.endsWith('/approve')) {
        return { approved: true }
      }

      if (path.startsWith('/automation/proposals/') && path.endsWith('/execute')) {
        const proposalId = path.split('/')[3]
        const proposal = state.proposals.find((candidate) => candidate.id === proposalId)
        if (!proposal) {
          throw new Error(`Unknown proposal ${proposalId}`)
        }

        applyInstruction(proposal.instruction)
        return { executed: true }
      }

      throw new Error(`Unexpected POST ${path}`)
    },

    async patch(path: string, { body }: { body?: Record<string, unknown> } = {}) {
      if (!path.startsWith(`/boards/${state.board?.id}/cards/`)) {
        throw new Error(`Unexpected PATCH ${path}`)
      }

      const cardId = path.split('/')[4]
      const card = state.cards.find((candidate) => candidate.id === cardId)
      if (!card) {
        throw new Error(`Unknown card ${cardId}`)
      }

      if (Object.prototype.hasOwnProperty.call(body ?? {}, 'isBlocked')) {
        card.isBlocked = Boolean(body?.isBlocked)
      }

      if (Object.prototype.hasOwnProperty.call(body ?? {}, 'blockReason')) {
        card.blockReason = typeof body?.blockReason === 'string' ? body.blockReason : null
      }

      return card
    },
  }
}

function createLegacyScenarioApi() {
  const state = {
    nextId: 1,
    board: null as null | { id: string; name: string; description?: string | null },
    labels: [] as Array<{ id: string; name: string; colorHex: string }>,
    columns: [] as Array<{ id: string; name: string; position: number; wipLimit?: number | null }>,
    cards: [] as Array<{
      id: string
      columnId: string
      title: string
      description?: string | null
      dueDate?: string | null
      isBlocked?: boolean
      labels: Array<{ id: string; name: string }>
    }>,
    queueRequests: [] as Array<{ id: string; status: string; payload: string; boardId: string; errorMessage?: string | null }>,
    proposals: [] as Array<{ id: string; sourceReferenceId: string; boardId: string; instruction: string }>,
  }

  const nextId = (prefix: string) => `${prefix}-${state.nextId++}`

  const findColumnByName = (columnName: string) =>
    state.columns.find((column) => column.name.toLowerCase() === columnName.toLowerCase()) ?? null

  const labelRefsFromIds = (labelIds: string[] | undefined) =>
    (labelIds ?? [])
      .map((labelId) => state.labels.find((label) => label.id === labelId))
      .filter((label): label is { id: string; name: string; colorHex: string } => Boolean(label))
      .map((label) => ({ id: label.id, name: label.name }))

  const seedPack = () => {
    state.labels = CONTENT_CALENDAR_PACK.manifest.labels.map((label, index) => ({
      id: `label-${index + 1}`,
      name: label.name,
      colorHex: label.color,
    }))
    state.columns = CONTENT_CALENDAR_PACK.manifest.columns.map((column, index) => ({
      id: `column-${index + 1}`,
      name: column.name,
      position: column.position,
      wipLimit: null,
    }))
    state.cards = CONTENT_CALENDAR_PACK.manifest.seedCards.map((seedCard) => {
      const column = findColumnByName(seedCard.columnName)
      if (!column) {
        throw new Error(`Unknown seed column ${seedCard.columnName}`)
      }

      const labelIds = state.labels
        .filter((label) => seedCard.labels.includes(label.name))
        .map((label) => label.id)

      return {
        id: nextId('card'),
        columnId: column.id,
        title: seedCard.title,
        description: seedCard.description,
        dueDate: null,
        isBlocked: false,
        labels: labelRefsFromIds(labelIds),
      }
    })
  }

  const applyInstruction = (instruction: string) => {
    const moveMatch = instruction.match(/^move card (\S+) to column "([^"]+)"$/i)
    if (moveMatch) {
      const [, cardId, columnName] = moveMatch
      const card = state.cards.find((candidate) => candidate.id === cardId)
      const column = findColumnByName(columnName)
      if (!card || !column) {
        throw new Error(`Unable to move card ${cardId} to column ${columnName}`)
      }

      card.columnId = column.id
      return
    }

    const createMatch = instruction.match(/^create card "([^"]+)"(?: in column "([^"]+)")?(?: with description "([^"]+)")?$/i)
    if (createMatch) {
      const [, title, columnName, description] = createMatch
      const column = columnName ? findColumnByName(columnName) : state.columns[0] ?? null
      if (!column) {
        throw new Error(`Unable to resolve target column for instruction: ${instruction}`)
      }

      state.cards.push({
        id: nextId('card'),
        columnId: column.id,
        title,
        description: description ?? null,
        dueDate: null,
        isBlocked: false,
        labels: [],
      })
      return
    }

    throw new Error(`Unsupported queue instruction in test harness: ${instruction}`)
  }

  return {
    state,
    async get(path: string) {
      if (path === `/boards/${state.board?.id}/starter-packs/catalog`) {
        return [CONTENT_CALENDAR_PACK]
      }

      if (path === `/boards/${state.board?.id}/columns`) {
        return state.columns
      }

      if (path === `/boards/${state.board?.id}/labels`) {
        return state.labels
      }

      if (path === `/boards/${state.board?.id}/cards`) {
        return state.cards
      }

      if (path === '/llm-queue/user?limit=200') {
        return state.queueRequests
      }

      if (path === '/automation/proposals?limit=200') {
        return state.proposals.map((proposal) => ({
          id: proposal.id,
          sourceReferenceId: proposal.sourceReferenceId,
        }))
      }

      throw new Error(`Unexpected GET ${path}`)
    },

    async post(path: string, { body }: { body?: Record<string, unknown> } = {}) {
      if (path === '/boards') {
        state.board = {
          id: 'board-1',
          name: String(body?.name ?? 'Demo Board'),
          description: typeof body?.description === 'string' ? body.description : null,
        }
        return state.board
      }

      if (path === `/boards/${state.board?.id}/starter-packs/apply`) {
        seedPack()
        return { applied: true }
      }

      if (path === `/boards/${state.board?.id}/cards`) {
        const card = {
          id: nextId('card'),
          columnId: String(body?.columnId ?? ''),
          title: String(body?.title ?? ''),
          description: typeof body?.description === 'string' ? body.description : null,
          dueDate: typeof body?.dueDate === 'string' ? body.dueDate : null,
          isBlocked: false,
          labels: labelRefsFromIds(body?.labelIds as string[] | undefined),
        }
        state.cards.push(card)
        return card
      }

      if (path === '/llm-queue') {
        const request = {
          id: nextId('queue'),
          status: 'Completed',
          payload: String(body?.payload ?? ''),
          boardId: String(body?.boardId ?? state.board?.id ?? ''),
          errorMessage: null,
        }
        state.queueRequests.push(request)
        state.proposals.push({
          id: nextId('proposal'),
          sourceReferenceId: request.id,
          boardId: request.boardId,
          instruction: request.payload,
        })
        return request
      }

      if (path.startsWith('/automation/proposals/') && path.endsWith('/approve')) {
        return { approved: true }
      }

      if (path.startsWith('/automation/proposals/') && path.endsWith('/execute')) {
        const proposalId = path.split('/')[3]
        const proposal = state.proposals.find((candidate) => candidate.id === proposalId)
        if (!proposal) {
          throw new Error(`Unknown proposal ${proposalId}`)
        }

        applyInstruction(proposal.instruction)
        return { executed: true }
      }

      throw new Error(`Unexpected POST ${path}`)
    },
  }
}

function createLegacyPackMismatchApi({
  starterPackId,
  columns,
  labels,
}: {
  starterPackId: string
  columns: string[]
  labels: string[]
}) {
  const boardId = 'board-1'

  return {
    async get(path: string) {
      if (path.endsWith('/starter-packs/catalog')) {
        return [
          {
            id: starterPackId,
            manifest: {
              packId: starterPackId,
            },
          },
        ]
      }

      if (path.endsWith('/columns')) {
        return columns.map((name, index) => ({
          id: `column-${index + 1}`,
          name,
          position: index,
        }))
      }

      if (path.endsWith('/labels')) {
        return labels.map((name, index) => ({
          id: `label-${index + 1}`,
          name,
          colorHex: `#00000${index}`,
        }))
      }

      throw new Error(`Unexpected GET ${path}`)
    },

    async post(path: string, { body }: { body?: Record<string, unknown> } = {}) {
      if (path === '/boards') {
        return {
          id: boardId,
          name: String(body?.name ?? 'Demo Board'),
          description: typeof body?.description === 'string' ? body.description : null,
        }
      }

      if (path.endsWith('/starter-packs/apply')) {
        return { applied: true }
      }

      throw new Error(`Unexpected POST ${path}`)
    },
  }
}

describe('legacy demo scenario compatibility', () => {
  it('keeps the engineering-sprint JS scenario aligned with the shipped starter-pack contract', async () => {
    const api = createEngineeringSprintApi()

    const summary = await runEngineeringSprintScenario({
      api,
      config: { uiBaseUrl: 'http://localhost:5173' },
    })

    expect(summary.board.name).toBe('DEMO: Engineering Sprint')
    expect(api.state.columns.map((column) => column.name)).toEqual([
      'Backlog',
      'In Progress',
      'Review',
      'Done',
    ])
    expect(api.state.labels.map((label) => label.name)).toEqual([
      'priority-high',
      'bug',
      'tech-debt',
    ])
    expect(api.state.cards.some((card) => card.title === 'Spike: simulate LLM-driven user')).toBe(true)
    expect(
      api.state.cards.find((card) => card.title === 'Refactor: consolidate API error mapping')?.isBlocked,
    ).toBe(true)
    expect(api.state.comments).toHaveLength(2)
    expect(summary.snapshot).toContain('Backlog:')
  })

  it('keeps the content-calendar JS scenario aligned with the shipped starter-pack contract', async () => {
    const api = createLegacyScenarioApi()

    const summary = await runContentCalendarScenario({
      api,
      config: { uiBaseUrl: 'http://localhost:5173' },
    })

    expect(summary.board.name).toBe('DEMO: Content Calendar Scenario')
    expect(api.state.columns.map((column) => column.name)).toEqual(['Ideas', 'Drafting', 'Review', 'Scheduled'])
    expect(api.state.labels.map((label) => label.name)).toEqual(['needs-draft', 'needs-review', 'publish-week'])
    expect(api.state.cards.some((card) => card.title === 'Plan weekly editorial slate')).toBe(true)

    const scheduledColumn = api.state.columns.find((column) => column.name === 'Scheduled')
    const designCard = api.state.cards.find((card) => card.title === 'Design: Automations empty-state panel')

    expect(scheduledColumn).toBeTruthy()
    expect(designCard?.columnId).toBe(scheduledColumn?.id)
    expect(summary.snapshot).toContain('Scheduled:')
  })

  it('fails fast when the engineering sprint starter-pack labels drift', async () => {
    const api = createLegacyPackMismatchApi({
      starterPackId: 'board-blueprint-engineering-sprint',
      columns: ['Backlog', 'In Progress', 'Review'],
      labels: ['bug', 'priority-high'],
    })

    await expect(
      runEngineeringSprintScenario({
        api,
        config: { uiBaseUrl: 'http://localhost:5173' },
      }),
    ).rejects.toThrow('Starter pack did not create expected labels')
  })

  it('fails fast when the content-calendar starter-pack labels drift', async () => {
    const api = createLegacyPackMismatchApi({
      starterPackId: 'board-blueprint-content-calendar',
      columns: ['Ideas', 'Drafting', 'Review', 'Scheduled'],
      labels: ['needs-draft', 'needs-review'],
    })

    await expect(
      runContentCalendarScenario({
        api,
        config: { uiBaseUrl: 'http://localhost:5173' },
      }),
    ).rejects.toThrow('Starter pack did not create expected content labels')
  })
})
