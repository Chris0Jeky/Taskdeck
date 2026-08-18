import { boardsApi } from '../api/boardsApi'
import { columnsApi } from '../api/columnsApi'
import type { Proposal, ProposalOperation, ProposalAffectedEntity } from '../types/automation'
import type { Board } from '../types/board'

type IdMap = Map<string, string>

// Keep board metadata hydration responsive without turning a large proposal
// page into an unbounded burst of column requests.
export const PROPOSAL_COLUMN_LOAD_CONCURRENCY = 8

function key(value: string | null | undefined): string {
  return (value ?? '').trim().toLowerCase()
}

function mapValue(map: IdMap, value: string | null | undefined): string | null {
  const normalized = key(value)
  if (!normalized) return null
  return map.get(normalized) ?? null
}

function parseParameters(operation: ProposalOperation): Record<string, unknown> {
  if (!operation.parameters) return {}
  try {
    const parsed = JSON.parse(operation.parameters) as unknown
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : {}
  } catch {
    return {}
  }
}

function stringParameter(parameters: Record<string, unknown>, ...names: string[]): string | null {
  for (const name of names) {
    const value = parameters[name]
    if (typeof value === 'string' && value.trim()) return value.trim()
  }
  return null
}

function operationBoardId(proposal: Proposal, operation: ProposalOperation): string | null {
  return stringParameter(parseParameters(operation), 'boardId', 'targetBoardId', 'sourceBoardId') ?? proposal.boardId
}

function operationColumnId(operation: ProposalOperation): string | null {
  const parameters = parseParameters(operation)
  return stringParameter(parameters, 'columnId', 'targetColumnId', 'sourceColumnId')
}

function targetType(operation: ProposalOperation): string {
  return operation.targetType.trim().toLowerCase()
}

export function createProposalDisplayNameResolver() {
  const boardNames: IdMap = new Map()
  const columnNames: IdMap = new Map()
  const accessibleBoards = new Set<string>()
  const loadedColumns = new Map<string, Promise<void>>()
  const pendingColumnLoads: Array<{
    boardId: string
    boardKey: string
    resolve: () => void
  }> = []
  let activeColumnLoads = 0
  let boardRequest: Promise<void> | null = null
  let hasBoardSnapshot = false

  function applyBoards(boards: readonly Board[]) {
    boardNames.clear()
    accessibleBoards.clear()
    for (const board of boards) {
      const boardKey = key(board.id)
      if (!boardKey) continue
      boardNames.set(boardKey, board.name)
      accessibleBoards.add(boardKey)
    }
    hasBoardSnapshot = true
  }

  async function loadBoards(): Promise<void> {
    if (hasBoardSnapshot) return
    if (!boardRequest) {
      boardRequest = boardsApi
        .getBoards(undefined, true)
        .then((boards) => applyBoards(boards))
        .catch(() => applyBoards([]))
    }
    await boardRequest
  }

  function drainColumnLoads(): void {
    while (activeColumnLoads < PROPOSAL_COLUMN_LOAD_CONCURRENCY && pendingColumnLoads.length > 0) {
      const task = pendingColumnLoads.shift()!
      activeColumnLoads += 1
      void (async () => {
        try {
          const columns = await columnsApi.getColumns(task.boardId)
          for (const column of columns) {
            const columnKey = key(column.id)
            if (columnKey) columnNames.set(`${task.boardKey}:${columnKey}`, column.name)
          }
        } catch {
          // Display names are best-effort and must not block proposal review.
        } finally {
          activeColumnLoads -= 1
          task.resolve()
          drainColumnLoads()
        }
      })()
    }
  }

  async function loadColumns(boardId: string): Promise<void> {
    const boardKey = key(boardId)
    if (!boardKey || !accessibleBoards.has(boardKey)) return
    const existing = loadedColumns.get(boardKey)
    if (existing) {
      await existing
      return
    }

    const request = new Promise<void>((resolve) => {
      pendingColumnLoads.push({ boardId, boardKey, resolve })
      drainColumnLoads()
    })
    loadedColumns.set(boardKey, request)
    await request
  }

  function columnReferences(proposal: Proposal): string[] {
    const references = new Set<string>()
    for (const operation of proposal.operations ?? []) {
      const columnId = operationColumnId(operation) ??
        (targetType(operation) === 'column' ? operation.targetId : null)
      if (columnId) {
        const boardId = operationBoardId(proposal, operation)
        if (boardId) references.add(boardId)
      }
    }
    return [...references]
  }

  async function ensure(
    proposals: readonly Proposal[],
    knownBoards?: readonly Board[],
  ): Promise<void> {
    if (knownBoards !== undefined) applyBoards(knownBoards)
    else await loadBoards()

    const boardIds = new Set<string>()
    for (const proposal of proposals) {
      for (const boardId of columnReferences(proposal)) {
        const boardKey = key(boardId)
        if (boardKey && accessibleBoards.has(boardKey)) boardIds.add(boardId)
      }
    }
    await Promise.all([...boardIds].map((boardId) => loadColumns(boardId)))
  }

  function boardLabel(boardId: string | null | undefined): string {
    if (!boardId) return 'Inbox'
    return mapValue(boardNames, boardId) ?? 'Unavailable board'
  }

  function columnLabel(boardId: string | null | undefined, columnId: string | null | undefined): string {
    if (!columnId || !boardId || !accessibleBoards.has(key(boardId))) return 'Unavailable column'
    return columnNames.get(`${key(boardId)}:${key(columnId)}`) ?? 'Unavailable column'
  }

  function operationTargetLabel(proposal: Proposal, operation: ProposalOperation): string | null {
    const type = targetType(operation)
    const parameters = parseParameters(operation)
    if (type === 'board') {
      return boardLabel(operation.targetId ?? stringParameter(parameters, 'boardId', 'targetBoardId') ?? proposal.boardId)
    }
    if (type === 'column') {
      const boardId = operationBoardId(proposal, operation)
      return columnLabel(boardId, operation.targetId ?? operationColumnId(operation))
    }
    return null
  }

  function displayParameterValue(
    proposal: Proposal,
    operation: ProposalOperation,
    parameterName: string,
    value: unknown,
  ): unknown {
    if (typeof value !== 'string') return value
    if (parameterName === 'boardId' || parameterName === 'targetBoardId' || parameterName === 'sourceBoardId') {
      return boardLabel(value)
    }
    if (parameterName === 'columnId' || parameterName === 'targetColumnId' || parameterName === 'sourceColumnId') {
      return columnLabel(operationBoardId(proposal, operation), value)
    }
    return value
  }

  function formatParameterValue(value: unknown): string {
    if (value === null || value === undefined) return 'null'
    if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') return String(value)
    return JSON.stringify(value) ?? String(value)
  }

  function summarizeOperation(proposal: Proposal, operation: ProposalOperation): string {
    const parameters = parseParameters(operation)
    const entries = Object.entries(parameters).slice(0, 4)
    const target = operationTargetLabel(proposal, operation)
    const parts = target ? [`target: ${target}`] : []
    parts.push(...entries.map(([name, value]) => `${name}: ${formatParameterValue(displayParameterValue(proposal, operation, name, value))}`))
    return parts.length > 0 ? parts.join(' · ') : 'No parameter preview supplied for this operation.'
  }

  function operationHeadline(proposal: Proposal, operation: ProposalOperation): string {
    const target = operationTargetLabel(proposal, operation)
      ?? (operationColumnId(operation)
        ? columnLabel(operationBoardId(proposal, operation), operationColumnId(operation))
        : stringParameter(parseParameters(operation), 'boardId', 'targetBoardId', 'sourceBoardId')
          ? boardLabel(stringParameter(parseParameters(operation), 'boardId', 'targetBoardId', 'sourceBoardId'))
          : null)
    return `${operation.actionType} ${operation.targetType}${target ? ` “${target}”` : ''}`
  }

  function affectedEntity(proposal: Proposal, entity: ProposalAffectedEntity): ProposalAffectedEntity {
    const type = entity.entityType.trim().toLowerCase()
    const label = type === 'board'
      ? boardLabel(entity.entityId)
      : type === 'column'
        ? columnLabel(proposal.boardId, entity.entityId)
        : entity.label
    return { ...entity, label }
  }

  function technicalDetails(proposal: Proposal): string {
    return JSON.stringify({
      proposalId: proposal.id,
      boardId: proposal.boardId,
      operations: proposal.operations,
    }, null, 2)
  }

  function displayDiff(proposal: Proposal, text: string): string {
    const replacements = new Map<string, string>()
    if (proposal.boardId) replacements.set(proposal.boardId, boardLabel(proposal.boardId))
    for (const operation of proposal.operations ?? []) {
      if (operation.targetId) {
        replacements.set(operation.targetId, operationTargetLabel(proposal, operation) ?? 'Unavailable item')
      }
      const parameters = parseParameters(operation)
      for (const [name, value] of Object.entries(parameters)) {
        if (typeof value !== 'string') continue
        if (name === 'boardId' || name === 'targetBoardId' || name === 'sourceBoardId') {
          replacements.set(value, boardLabel(value))
        } else if (name === 'columnId' || name === 'targetColumnId' || name === 'sourceColumnId') {
          replacements.set(value, columnLabel(operationBoardId(proposal, operation), value))
        }
      }
    }
    return [...replacements.entries()]
      .filter(([raw]) => raw.length > 0)
      .sort(([left], [right]) => right.length - left.length)
      .reduce((result, [raw, readable]) => result.split(raw).join(readable), text)
  }

  function reset() {
    boardNames.clear()
    columnNames.clear()
    accessibleBoards.clear()
    loadedColumns.clear()
    boardRequest = null
    hasBoardSnapshot = false
  }

  return {
    ensure,
    boardLabel,
    columnLabel,
    operationHeadline,
    operationTargetLabel,
    summarizeOperation,
    affectedEntity,
    technicalDetails,
    displayDiff,
    reset,
  }
}

export const proposalDisplayNames = createProposalDisplayNameResolver()
export const resetProposalDisplayNamesForTests = proposalDisplayNames.reset
