import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Proposal } from '../../types/automation'
import type { Board } from '../../types/board'
import {
  createProposalDisplayNameResolver,
  PROPOSAL_COLUMN_LOAD_CONCURRENCY,
} from '../../composables/useProposalDisplayNames'

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  const promise = new Promise<T>((innerResolve) => {
    resolve = innerResolve
  })
  return { promise, resolve }
}

const mocks = vi.hoisted(() => ({
  getBoards: vi.fn(),
  getColumns: vi.fn(),
}))

vi.mock('../../api/boardsApi', () => ({
  boardsApi: { getBoards: mocks.getBoards },
}))

vi.mock('../../api/columnsApi', () => ({
  columnsApi: { getColumns: mocks.getColumns },
}))

function makeProposal(boardId: string, columnId: string): Proposal {
  const now = new Date().toISOString()
  return {
    id: `proposal-${boardId}`,
    sourceType: 'Chat',
    sourceReferenceId: null,
    boardId,
    requestedByUserId: 'user-1',
    status: 'PendingReview',
    riskLevel: 'Low',
    summary: `Proposal for ${boardId}`,
    diffPreview: null,
    validationIssues: null,
    createdAt: now,
    updatedAt: now,
    expiresAt: new Date(Date.now() + 60 * 60_000).toISOString(),
    decidedAt: null,
    decidedByUserId: null,
    appliedAt: null,
    failureReason: null,
    correlationId: `correlation-${boardId}`,
    operations: [{
      id: `operation-${boardId}`,
      proposalId: `proposal-${boardId}`,
      sequence: 0,
      actionType: 'MoveCard',
      targetType: 'Column',
      targetId: columnId,
      parameters: JSON.stringify({ boardId, columnId }),
      idempotencyKey: `key-${boardId}`,
      expectedVersion: null,
    }],
    approvedRevisionId: null,
    latestRevisionId: null,
  }
}

function makeBoard(id: string, name: string): Board {
  const now = new Date().toISOString()
  return {
    id,
    name,
    description: null,
    isArchived: false,
    createdAt: now,
    updatedAt: now,
  }
}

describe('useProposalDisplayNames column loading', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('bounds column requests while resolving every board name', async () => {
    const boardIds = Array.from(
      { length: PROPOSAL_COLUMN_LOAD_CONCURRENCY + 3 },
      (_, index) => `board-${index + 1}`,
    )
    const columnRequests = new Map<string, ReturnType<typeof createDeferred<Array<{ id: string; boardId: string; name: string }>>>>()
    let activeRequests = 0
    let maxActiveRequests = 0

    mocks.getColumns.mockImplementation((boardId: string) => {
      const deferred = createDeferred<Array<{ id: string; boardId: string; name: string }>>()
      columnRequests.set(boardId, deferred)
      activeRequests += 1
      maxActiveRequests = Math.max(maxActiveRequests, activeRequests)
      return deferred.promise.finally(() => {
        activeRequests -= 1
      })
    })

    const resolver = createProposalDisplayNameResolver()
    const proposals = boardIds.map((boardId, index) => makeProposal(boardId, `column-${index + 1}`))
    const boards = boardIds.map((id, index) => makeBoard(id, `Board ${index + 1}`))
    const ensurePromise = resolver.ensure(proposals, boards)

    await Promise.resolve()
    expect(mocks.getColumns).toHaveBeenCalledTimes(PROPOSAL_COLUMN_LOAD_CONCURRENCY)
    expect(maxActiveRequests).toBe(PROPOSAL_COLUMN_LOAD_CONCURRENCY)

    for (const [index, boardId] of boardIds.entries()) {
      const request = columnRequests.get(boardId)
      expect(request).toBeDefined()
      request!.resolve([{
        id: `column-${index + 1}`,
        boardId,
        name: `Column ${index + 1}`,
      }])
      await Promise.resolve()
      await Promise.resolve()
      expect(activeRequests).toBeLessThanOrEqual(PROPOSAL_COLUMN_LOAD_CONCURRENCY)
    }

    await ensurePromise
    expect(mocks.getColumns).toHaveBeenCalledTimes(boardIds.length)
    expect(maxActiveRequests).toBe(PROPOSAL_COLUMN_LOAD_CONCURRENCY)
    for (const [index, boardId] of boardIds.entries()) {
      expect(resolver.boardLabel(boardId)).toBe(`Board ${index + 1}`)
      expect(resolver.columnLabel(boardId, `column-${index + 1}`)).toBe(`Column ${index + 1}`)
    }
  })
})

describe('useProposalDisplayNames cache lifecycle', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    mocks.getColumns.mockResolvedValue([])
  })

  it('keeps board metadata failures retryable instead of caching an empty snapshot', async () => {
    mocks.getBoards
      .mockRejectedValueOnce(new Error('temporary board metadata failure'))
      .mockResolvedValueOnce([makeBoard('board-1', 'Roadmap')])

    const resolver = createProposalDisplayNameResolver()
    await resolver.ensure([makeProposal('board-1', 'column-1')])
    expect(resolver.boardLabel('board-1')).toBe('Unavailable board')

    await resolver.ensure([makeProposal('board-1', 'column-1')])
    expect(mocks.getBoards).toHaveBeenCalledTimes(2)
    expect(resolver.boardLabel('board-1')).toBe('Roadmap')
  })

  it('ignores a late account-A board response after reset and account-B hydration', async () => {
    const accountABoards = createDeferred<Board[]>()
    mocks.getBoards
      .mockReturnValueOnce(accountABoards.promise)
      .mockResolvedValueOnce([makeBoard('board-b', 'Account B board')])
    mocks.getColumns.mockResolvedValue([])

    const resolver = createProposalDisplayNameResolver()
    const accountAEnsure = resolver.ensure([makeProposal('board-a', 'column-a')])
    await Promise.resolve()
    expect(mocks.getBoards).toHaveBeenCalledTimes(1)

    resolver.reset()
    await resolver.ensure([makeProposal('board-b', 'column-b')])
    expect(resolver.boardLabel('board-b')).toBe('Account B board')

    accountABoards.resolve([makeBoard('board-a', 'Account A board')])
    await accountAEnsure

    expect(resolver.boardLabel('board-a')).toBe('Unavailable board')
    expect(resolver.boardLabel('board-b')).toBe('Account B board')
  })

  it('uses a proposed name as the target for normalized create column operations', () => {
    const resolver = createProposalDisplayNameResolver()
    const proposal = makeProposal('board-1', 'column-1')
    const operation = proposal.operations[0]
    operation.actionType = 'create'
    operation.targetType = 'column'
    operation.targetId = null
    operation.parameters = JSON.stringify({ boardId: 'board-1', name: 'Ready for review' })

    expect(resolver.operationTargetLabel(proposal, operation)).toBe('Ready for review')
    expect(resolver.operationHeadline(proposal, operation)).toContain('Ready for review')
  })

  it('keeps due date and labels visible when create-card metadata has structural ids', () => {
    const resolver = createProposalDisplayNameResolver()
    const proposal = makeProposal('board-1', 'column-1')
    const operation = proposal.operations[0]
    operation.actionType = 'create'
    operation.targetType = 'card'
    operation.parameters = JSON.stringify({
      title: 'Buy milk',
      description: 'Shopping list',
      columnId: 'column-1',
      boardId: 'board-1',
      dueDate: '2026-08-23T00:00:00+00:00',
      labels: ['shopping'],
    })

    const summary = resolver.summarizeOperation(proposal, operation)

    expect(summary).toContain('dueDate: 2026-08-23T00:00:00+00:00')
    expect(summary).toContain('labels: ["shopping"]')
    expect(summary).not.toContain('columnId:')
  })
})
